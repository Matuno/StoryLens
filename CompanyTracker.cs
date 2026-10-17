using System.Net;
using System.Net.Http;
using StoryLens.Core;

namespace StoryLens;

internal sealed class CompanyTracker : IDisposable
{
    public const int RequestIntervalSeconds = 5;
    private readonly Plugin plugin;
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private Task<string>? pending;
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
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StoryLens/0.2 (+https://na.finalfantasyxiv.com/lodestone/)");
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
                var html = pending.GetAwaiter().GetResult();
                if (request!.Owner == local.Key && request.Owner == state.OwnerKey && request.Company == state.Id)
                    Apply(request, html, now);
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
            pending = null; request = null;
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
        request = new(state.OwnerKey, state.Id, path, kind, memberKey);
        state.NextRequestAt = now.AddSeconds(RequestIntervalSeconds);
        plugin.MarkHistoryDirty();
        Requests++;
        Status = kind == "roster" ? $"Reading roster page {rosterPage}/{rosterLastPage}…" : "Checking one stale public profile page…";
        pending = Fetch(path);
    }

    private async Task<string> Fetch(string path)
    {
        using var response = await http.GetAsync(path, stop.Token).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync(stop.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Lodestone serves private sections as 403 with the requested character's ordinary profile frame.
            var parts = path.Split('/');
            if (parts.Length > 4 && parts[2] == "character" && response.Headers.RetryAfter is null &&
                LodestoneParser.IsPrivateCharacterPage((int)response.StatusCode, html, parts[3])) return html;
            var error = new HttpRequestException("Lodestone response unavailable", null, response.StatusCode);
            if (response.Headers.RetryAfter?.Date is { } date) error.Data["RetryAt"] = date;
            else if (response.Headers.RetryAfter?.Delta is { } delay) error.Data["RetryAt"] = DateTimeOffset.UtcNow.Add(delay);
            throw error;
        }
        return html;
    }

    private void Apply(Request job, string html, DateTimeOffset now)
    {
        var state = plugin.Settings.Company;
        if (job.Kind == "search")
        {
            var split = state.OwnerKey.LastIndexOf('@');
            state.CharacterId = LodestoneParser.FindCharacter(html, state.OwnerKey[..split], plugin.Catalog.WorldName(uint.Parse(state.OwnerKey[(split + 1)..])));
        }
        else if (job.Kind == "company")
        {
            LodestoneParser.VerifyCharacter(html, state.CharacterId);
            state.Id = LodestoneParser.CompanyId(html);
            if (state.Id.Length > 0 && !LodestoneParser.ValidId(state.Id)) throw new InvalidDataException("Invalid company link");
            if (state.Id.Length == 0)
            {
                Status = "No public Free Company link; waiting for the in-game member list or a later profile refresh.";
                state.NextRequestAt = now.AddHours(6);
            }
        }
        else if (job.Kind == "roster")
        {
            if (!LodestoneParser.MainContent(html).Contains($"/lodestone/freecompany/{state.Id}/", StringComparison.Ordinal))
                throw new InvalidDataException("Wrong Free Company page");
            var page = LodestoneParser.Roster(html);
            if (page.CurrentPage != rosterPage || (rosterPage > 1 && page.LastPage != rosterLastPage) ||
                (page.CurrentPage < page.LastPage && page.Members.Length != 50))
                throw new InvalidDataException("Roster pagination changed during scan");
            rosterLastPage = page.LastPage;
            roster.AddRange(page.Members);
            if (rosterPage < rosterLastPage) rosterPage++;
            else
            {
                var entries = roster.DistinctBy(m => m.Id).ToArray();
                if (entries.Length is 0 or > 512 || entries.Length != roster.Count) throw new InvalidDataException("Invalid or changing company roster");
                var members = new List<CompanyMember>();
                foreach (var entry in entries)
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
            LodestoneParser.VerifyCharacter(html, member.LodestoneId);
            var profile = member.Profile;
            try
            {
                if (job.Kind == "class_job")
                {
                    profile.MaxCombatLevel = LodestoneParser.MaxCombatLevel(html);
                    profile.JobsCheckedAt = now;
                }
                else if (job.Kind is "mount" or "minion")
                {
                    var icons = LodestoneParser.CollectionIcons(html, job.Kind);
                    foreach (var rule in plugin.Progress.Collections.Where(r => r.Kind == job.Kind && icons.Contains(r.IconHash)))
                        AddEvidence(rule.QuestId, $"{rule.Kind}: {rule.Name}");
                }
                else
                    foreach (var id in LodestoneParser.Achievements(html))
                        if (plugin.Progress.AchievementQuests.TryGetValue(id, out var quest)) AddEvidence(quest, $"Achievement: {plugin.Progress.Story.Quests[quest].Name}");
                profile.Status = "Public evidence checked; missing unlocks do not imply incomplete quests";
                profile.SectionStatus[job.Kind] = "Public page checked";
            }
            catch (InvalidDataException)
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

    public void Dispose()
    {
        stop.Cancel();
        try { pending?.GetAwaiter().GetResult(); } catch (Exception) { }
        http.Dispose(); stop.Dispose();
    }
}
