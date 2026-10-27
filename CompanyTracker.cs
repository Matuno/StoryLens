using System.Net;
using System.Net.Http;
using StoryLens.Core;

namespace StoryLens;

internal sealed class CompanyTracker : IAsyncDisposable
{
    public const int RequestIntervalSeconds = 5;
    private readonly Plugin plugin;
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private Task<LodestonePage>? pending;
    private Request? request;
    private readonly List<RosterEntry> roster = [];
    private int rosterPage = 1;
    private int rosterLastPage = 1;
    private DateTimeOffset nextNativeRead;
    public string Status { get; private set; } = "Waiting for your Free Company.";
    public int Requests { get; private set; }
    public int Failures { get; private set; }
    public string LastFailure { get; private set; } = "None";
    public string? PauseReason { get; private set; }
    private sealed record Request(string Owner, string Company, string Path, string Kind, string? MemberKey = null);

    public CompanyTracker(Plugin plugin)
    {
        this.plugin = plugin;
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All })
        { BaseAddress = new Uri("https://na.finalfantasyxiv.com"), Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4_000_000 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StoryLens/1.0 (+https://github.com/Matuno/StoryLens)");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
    }

    public void Update(DateTimeOffset now, WatchedPlayer? local, bool inCombat, bool inDuty)
    {
        PauseReason = CompanyState.SweepPauseReason(plugin.Settings.TrackFreeCompany, local is not null, inCombat, inDuty);
        if (PauseReason is not null)
        {
            // Keep an already dispatched response buffered until safe to process; preserve the cursor and backoff.
            Status = PauseReason;
            return;
        }
        if (local is null) return;
        var state = plugin.Settings.Company;
        if (pending is { IsCompleted: true })
        {
            try
            {
                var page = pending.GetAwaiter().GetResult();
                if (request!.Owner == local.Key && request.Owner == state.OwnerKey && request.Company == state.Id)
                    Apply(request, page, now);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException or
                FormatException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                Failures++;
                LastFailure = $"{request?.Kind}: {e.GetType().Name}" + (e is HttpRequestException h ? $" ({(int?)h.StatusCode})" : "");
                state.NextRequestAt = CompanyState.RetryDeadline(now, e.Data["RetryAt"] as DateTimeOffset?);
                Status = $"Lodestone unavailable or page format changed; saved evidence retained. Retry no earlier than {state.NextRequestAt:u}.";
                if (request?.MemberKey is { } key && state.Members.FirstOrDefault(m => m.Player.Key == key) is { } member)
                {
                    member.Profile.Status = "Profile unavailable; earlier evidence retained";
                    member.Profile.NextCheckAt = now.AddHours(6);
                }
                roster.Clear(); rosterPage = rosterLastPage = 1;
                plugin.MarkHistoryDirty();
            }
            finally { pending = null; request = null; }
        }
        if (state.OwnerKey != local.Key)
        {
            // Keep the old roster until the new character's complete roster has been verified.
            state.OwnerKey = local.Key; state.CharacterId = ""; state.Id = "";
            state.RosterCheckedAt = default;
            roster.Clear(); rosterPage = rosterLastPage = 1;
            plugin.MarkHistoryDirty();
        }
        if (now >= nextNativeRead)
        {
            nextNativeRead = now.AddSeconds(10);
            var cached = CompanyReader.Read(local);
            if (cached.Id.Length > 0)
            {
                if (state.CachedMemberCount != cached.Total)
                {
                    state.CachedMemberCount = cached.Total;
                    plugin.MarkHistoryDirty();
                }
                if (state.Id != cached.Id)
                {
                    state.Id = cached.Id; state.RosterCheckedAt = default;
                    roster.Clear(); rosterPage = rosterLastPage = 1;
                    plugin.MarkHistoryDirty();
                }
                foreach (var entry in cached.Members)
                    if (state.Members.FirstOrDefault(m => m.Player.Key == entry.Player.Key) is { } member &&
                        member.LocationId != entry.Location && plugin.Catalog.HasTerritory(entry.Location))
                    {
                        member.LocationId = entry.Location;
                        member.LocationSeenAt = now;
                        plugin.MarkHistoryDirty();
                    }
            }
        }
        if (pending is not null)
        {
            Status = "Waiting for the current Lodestone request…";
            return;
        }
        if (now < state.NextRequestAt)
        {
            Status = $"Next scheduled Lodestone read: {state.NextRequestAt:u}. {state.Members.Count} FC members retained.";
            return;
        }
        string path; string kind; string? memberKey = null;
        if (state.Id.Length == 0)
        {
            kind = state.CharacterId.Length == 0 ? "search" : "company";
            path = kind == "search" ? $"/lodestone/character/?q={Uri.EscapeDataString(local.Name)}&worldname={Uri.EscapeDataString(plugin.Catalog.WorldName(local.HomeWorldId))}"
                : $"/lodestone/character/{state.CharacterId}/";
        }
        else if (now - state.RosterCheckedAt >= TimeSpan.FromHours(6))
        {
            path = $"/lodestone/freecompany/{state.Id}/member/?page={rosterPage}"; kind = "roster";
        }
        else
        {
            var member = state.NextStaleMember(now);
            if (member is null) { Status = "All public profiles are fresh. Checking only stale members."; return; }
            kind = new[] { "class_job", "mount", "minion", "achievement/category/76" }[member.Profile.NextSection];
            path = $"/lodestone/character/{member.LodestoneId}/{kind}/"; memberKey = member.Player.Key;
        }
        var job = new Request(state.OwnerKey, state.Id, path, kind, memberKey);
        request = job;
        state.NextRequestAt = now.AddSeconds(RequestIntervalSeconds);
        plugin.MarkHistoryDirty();
        Requests++;
        Status = kind == "roster" ? $"Reading roster page {rosterPage}/{rosterLastPage}…" : "Checking one stale public profile page…";
        var worldName = plugin.Catalog.WorldName(local.HomeWorldId);
        pending = Task.Run(() => Fetch(job, local.Name, worldName), stop.Token);
    }

    private async Task<LodestonePage> Fetch(Request job, string name, string world)
    {
        using var response = await http.GetAsync(job.Path, stop.Token).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync(stop.Token).ConfigureAwait(false);
        var parts = job.Path.Split('/');
        var privateSection = parts.Length > 4 && parts[2] == "character" && response.Headers.RetryAfter is null &&
            LodestoneParser.IsPrivateCharacterPage((int)response.StatusCode, html, parts[3]);
        if (!response.IsSuccessStatusCode && !privateSection)
        {
            var error = new HttpRequestException("Lodestone response unavailable", null, response.StatusCode);
            if (response.Headers.RetryAfter?.Date is { } date) error.Data["RetryAt"] = date;
            else if (response.Headers.RetryAfter?.Delta is { } delay) error.Data["RetryAt"] = DateTimeOffset.UtcNow.Add(delay);
            throw error;
        }
        stop.Token.ThrowIfCancellationRequested();
        var parsed = LodestoneParser.ParsePage(html, job.Kind, job.Kind == "roster" ? job.Company : parts[3], name, world);
        stop.Token.ThrowIfCancellationRequested();
        return parsed;
    }

    private void Apply(Request job, LodestonePage page, DateTimeOffset now)
    {
        var state = plugin.Settings.Company;
        if (job.Kind == "search")
        {
            state.CharacterId = page.Id;
        }
        else if (job.Kind == "company")
        {
            state.Id = page.Id;
            if (state.Id.Length == 0)
            {
                Status = "No public Free Company link; waiting for the in-game member list or a later profile refresh.";
                state.NextRequestAt = now.AddHours(6);
            }
        }
        else if (job.Kind == "roster")
        {
            var rosterResult = page.Roster!;
            if (rosterResult.CurrentPage != rosterPage || (rosterPage > 1 && rosterResult.LastPage != rosterLastPage) ||
                (rosterResult.CurrentPage < rosterResult.LastPage && rosterResult.Members.Length != 50))
                throw new InvalidDataException("Roster pagination changed during scan");
            rosterLastPage = rosterResult.LastPage;
            roster.AddRange(rosterResult.Members);
            if (rosterPage < rosterLastPage) rosterPage++;
            else
            {
                LodestoneParser.ValidateRoster(roster);
                var members = new List<CompanyMember>();
                foreach (var entry in roster)
                {
                    var world = plugin.Catalog.Worlds.SingleOrDefault(w => w.Name.Equals(entry.World, StringComparison.OrdinalIgnoreCase)).Id;
                    if (!WatchedPlayer.IsValidName(entry.Name) || world == 0) throw new InvalidDataException("Invalid company identity");
                    var old = state.Members.FirstOrDefault(m => m.LodestoneId == entry.Id);
                    var member = new CompanyMember { LodestoneId = entry.Id, Player = new(entry.Name, world),
                        Profile = old?.Profile ?? new(), LocationId = old?.LocationId ?? 0, LocationSeenAt = old?.LocationSeenAt ?? default };
                    members.Add(member);
                }
                if (!members.Any(m => m.Player.Key == state.OwnerKey)) throw new InvalidDataException("Own character missing from company roster");
                plugin.ReplaceCompanyMembers(members);
                state.RosterCheckedAt = now;
                roster.Clear(); rosterPage = rosterLastPage = 1;
                Status = $"Tracking all {members.Count} Free Company members; profile checks are paced.";
            }
        }
        else if (state.Members.FirstOrDefault(m => m.Player.Key == job.MemberKey) is { } member)
        {
            var profile = member.Profile;
            if (!page.Unavailable)
            {
                if (job.Kind == "class_job")
                {
                    profile.MaxCombatLevel = page.MaxCombatLevel;
                    profile.JobsCheckedAt = now;
                }
                else if (job.Kind is "mount" or "minion")
                {
                    var icons = page.Icons!;
                    foreach (var rule in plugin.Progress.Collections.Where(r => r.Kind == job.Kind && icons.Contains(r.IconHash)))
                        AddEvidence(rule.QuestId, $"{rule.Kind}: {rule.Name}");
                }
                else
                    foreach (var id in page.Achievements!)
                        if (plugin.Progress.AchievementQuests.TryGetValue(id, out var quest)) AddEvidence(quest, $"Achievement: {plugin.Progress.Story.Quests[quest].Name}");
                profile.Status = "Public evidence checked; missing unlocks do not imply incomplete quests";
                profile.SectionStatus[job.Kind] = "Public page checked";
            }
            else
            {
                profile.Status = $"{job.Kind}: private, incomplete or changed page; earlier evidence retained";
                profile.SectionStatus[job.Kind] = "Private, incomplete or changed page";
            }
            profile.Advance(now);
            void AddEvidence(uint quest, string source)
            {
                if (!profile.CompletedQuestIds.Contains(quest)) profile.CompletedQuestIds.Add(quest);
                if (!profile.Sources.Contains(source) && profile.Sources.Count < 128) profile.Sources.Add(source);
            }
        }
        plugin.MarkHistoryDirty();
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { if (pending is not null) await pending.ConfigureAwait(false); }
        catch (Exception) { /* Worker failures are reported while running; shutdown only drains it. */ }
        finally { http.Dispose(); stop.Dispose(); }
    }
}
