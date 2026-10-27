using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Utility.Raii;
using StoryLens.Core;

namespace StoryLens;

internal sealed class StoryLensWindow(Plugin plugin, ITextureProvider textures, IGameGui gameGui)
{
    public bool IsOpen;
    private readonly StorySelection selection = new();
    private string name = string.Empty, search = string.Empty, playerSearch = string.Empty, companySearch = string.Empty;
    private uint world;
    private string? addError, requestedTab;
    private WatchedPlayer? member;
    private WatchedPlayer? contextPlayer;
    private bool focusRequested;
    private string? failedMapLocation;
    private Clue[] evidence = [];
    private QuestCandidate[] candidates = [];
    private ProgressEstimate? estimate;
    private uint? anchor, focused, lastRoadAnchor, lastDetailsQuest;
    private string anchorLabel = string.Empty;
    private DateTimeOffset nextCompanyEstimate;
    private Dictionary<string, ProgressEstimate> companyEstimates = [];
    private List<CompanyMember>? companyRoster;
    private string? lastCompanySearch;
    private CompanyMember[] companyRows = [];
    private (Zone, Vector3, uint?)? markerKey;
    private (QuestObjective Objective, string Label)[] markers = [];
    private (string, bool, uint)? browseKey;
    private QuestInfo[] browseQuests = [];
    private static readonly Vector4 Accent = new(.40f, .87f, .79f, 1f);
    private static readonly Vector4 Gold = new(1f, .81f, .42f, 1f);
    private static float Scale => ImGui.GetFontSize() / 17f;

    private void RefreshSelection()
    {
        if (selection.PlayerKey is null)
        {
            member = null; evidence = []; estimate = null; candidates = [];
            anchor = null; focused = selection.ResolveQuest(null);
            return;
        }
        member = plugin.Settings.PersistentPlayers().FirstOrDefault(p => p.Key == selection.PlayerKey) ??
            (selection.PlayerKey is { } key ? plugin.SessionPlayers.GetValueOrDefault(key)?.Player : null) ??
            (contextPlayer?.Key == selection.PlayerKey ? contextPlayer : null);
        if (selection.PlayerKey is not null && member is null) selection.ClearPlayer();
        evidence = member is null ? [] : plugin.GetEvidence(member.Key);
        estimate = member is null ? null : plugin.Estimate(member.Key, evidence: evidence);
        candidates = QuestCandidate.Resolve(evidence, member is null ? null : plugin.Seen.GetValueOrDefault(member.Key), DateTimeOffset.UtcNow);
        if (selection.CandidateId is { } selected && !candidates.Any(c => c.QuestId == selected)) selection.ClearQuest();
        var clue = candidates.FirstOrDefault();
        anchor = selection.ResolveAnchor(clue?.QuestId ?? estimate?.QuestId);
        anchorLabel = selection.CandidateId is not null ? "Selected possibility; unconfirmed" :
            clue is not null ? clue.Recent ? "Leading possibility; unconfirmed" : "Earlier observation; current quest unknown" : estimate?.Label.EndsWith('+') == true
            ? "Known minimum progress" : "Area reference; current quest unknown";
        focused = selection.ResolveQuest(anchor);
    }

    internal void OpenForPlayer(WatchedPlayer player)
    {
        contextPlayer = player;
        selection.SelectPlayer(player.Key);
        lastRoadAnchor = null;
        addError = null;
        requestedTab = "Follow player";
        IsOpen = true;
        focusRequested = true;
    }

    public void Draw()
    {
        if (!IsOpen) return;
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, new Vector4(.055f, .067f, .09f, .98f))
            .Push(ImGuiCol.TitleBg, new Vector4(.055f, .067f, .09f, 1f))
            .Push(ImGuiCol.TitleBgActive, new Vector4(.09f, .14f, .18f, 1f))
            .Push(ImGuiCol.ChildBg, new Vector4(.075f, .09f, .12f, 1f))
            .Push(ImGuiCol.PopupBg, new Vector4(.09f, .11f, .15f, 1f))
            .Push(ImGuiCol.Text, new Vector4(.91f, .94f, .97f, 1f))
            .Push(ImGuiCol.TextDisabled, new Vector4(.57f, .64f, .72f, 1f))
            .Push(ImGuiCol.Border, new Vector4(.18f, .23f, .29f, .65f))
            .Push(ImGuiCol.Separator, new Vector4(.18f, .23f, .29f, .65f))
            .Push(ImGuiCol.FrameBg, new Vector4(.11f, .14f, .18f, 1f))
            .Push(ImGuiCol.Button, new Vector4(.12f, .19f, .23f, 1f))
            .Push(ImGuiCol.ButtonHovered, new Vector4(.19f, .32f, .35f, 1f))
            .Push(ImGuiCol.ButtonActive, new Vector4(.21f, .40f, .40f, 1f))
            .Push(ImGuiCol.Header, new Vector4(.11f, .18f, .22f, 1f))
            .Push(ImGuiCol.HeaderHovered, new Vector4(.13f, .24f, .29f, 1f))
            .Push(ImGuiCol.HeaderActive, new Vector4(.18f, .36f, .37f, 1f))
            .Push(ImGuiCol.Tab, new Vector4(.08f, .11f, .15f, 1f))
            .Push(ImGuiCol.TabActive, new Vector4(.14f, .29f, .31f, 1f))
            .Push(ImGuiCol.TabHovered, new Vector4(.19f, .32f, .35f, 1f))
            .Push(ImGuiCol.CheckMark, Accent);
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(18, 15) * Scale)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(9, 6) * Scale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8) * Scale)
            .Push(ImGuiStyleVar.CellPadding, new Vector2(10, 9) * Scale)
            .Push(ImGuiStyleVar.WindowRounding, 10 * Scale)
            .Push(ImGuiStyleVar.ChildRounding, 9 * Scale)
            .Push(ImGuiStyleVar.FrameRounding, 6 * Scale)
            .Push(ImGuiStyleVar.TabRounding, 6 * Scale);
        ImGui.SetNextWindowSize(new Vector2(980, 720), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(720, 640), new Vector2(float.MaxValue));
        if (focusRequested)
        {
            ImGui.SetNextWindowCollapsed(false);
            ImGui.SetNextWindowFocus();
            focusRequested = false;
        }
        var visible = ImGui.Begin("Story Lens###StoryLens", ref IsOpen);
        try
        {
            if (!visible) return;
            RefreshSelection();
            ImGui.TextColored(Accent, "STORY LENS");
            ImGui.SameLine(); Badge("STORY COMPANION", new Vector4(.64f, .70f, .83f, 1));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(plugin.Status);
            DrawSelectionBar();
            if (plugin.SaveError is not null)
            {
                ImGui.TextWrapped(plugin.SaveError);
                if (ImGui.SmallButton("Retry save")) plugin.Save();
            }
            using var tabs = ImRaii.TabBar("StoryLensTabs");
            if (tabs)
            {
                Tab("Free Company", DrawCompany);
                Tab("Follow player", DrawFollow);
                Tab("Quest browser", DrawBrowser);
                Tab("Settings", DrawSettings);
            }
        }
        finally { ImGui.End(); }
    }

    private void Tab(string title, Action draw)
    {
        var flags = requestedTab == title ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var tab = ImRaii.TabItem(title, flags);
        if (!tab) return;
        if (requestedTab == title) requestedTab = null;
        draw();
    }

    private void DrawSelectionBar()
    {
        if (member is not null)
        {
            Badge("MONITORING", Accent); ImGui.SameLine();
            ImGui.TextUnformatted($"{member.Name} @ {plugin.Catalog.WorldName(member.HomeWorldId)}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear player")) { selection.ClearPlayer(); RefreshSelection(); }
        }
        if (focused is { } id && plugin.Catalog.Chain.Quests.TryGetValue(id, out var quest))
        {
            ImGui.TextWrapped($"{(selection.FollowingPlayer ? "Following" : "Previewing")}: {quest.Name}");
            if (ImGui.SmallButton("Clear quest")) { selection.ClearQuest(); focused = null; }
            if (member is not null && !selection.FollowingPlayer)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Resume following player")) selection.FollowPlayer();
            }
        }
        else if (member is not null && !selection.FollowingPlayer && ImGui.SmallButton("Follow player's estimate"))
            selection.FollowPlayer();
        ImGui.Separator();
    }

    private static void Search(string label, ref string value)
    {
        ImGui.SetNextItemWidth(Math.Max(150, ImGui.GetContentRegionAvail().X - 85));
        ImGui.InputTextWithHint($"##{label}", label, ref value, 120);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Clear##{label}")) value = string.Empty;
    }

    private void SelectPlayer(string key, bool navigate = false)
    {
        selection.TogglePlayer(key);
        lastRoadAnchor = null;
        RefreshSelection();
        if (navigate && member is not null) requestedTab = "Follow player";
    }

    private void DrawCompany()
    {
        if (DateTimeOffset.UtcNow >= nextCompanyEstimate || companyRoster != plugin.Settings.Company.Members || lastCompanySearch != companySearch)
        {
            nextCompanyEstimate = DateTimeOffset.UtcNow.AddSeconds(1);
            companyRoster = plugin.Settings.Company.Members;
            lastCompanySearch = companySearch;
            companyEstimates = companyRoster.ToDictionary(m => m.Player.Key, m => plugin.Estimate(m.Player.Key, m));
            companyRows = companyRoster.Where(m => m.Player.Name.Contains(companySearch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Player.Name).ToArray();
        }
        var known = companyEstimates.Values.Count(e => e.QuestId.HasValue);
        using (var cards = ImRaii.Table("##OverviewCards", 3, ImGuiTableFlags.SizingStretchSame))
        if (cards)
        {
            StatCard("FC MEMBERS", plugin.Settings.Company.Members.Count, new Vector4(.64f, .70f, .93f, 1));
            StatCard("WITH STORY CLUES", known, Accent);
            StatCard("AWAITING EVIDENCE", plugin.Settings.Company.Members.Count - known, Gold);
        }
        ImGui.TextWrapped(plugin.CompanyTracker.Status);
        ImGui.TextDisabled("~ observed estimate    + known minimum    ? area clue    |    Click a member to follow their path.");
        Search("Find FC member", ref companySearch);
        using var table = ImRaii.Table("##Company", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new Vector2(0, ImGui.GetContentRegionAvail().Y));
        if (table)
        {
            ImGui.TableSetupColumn("Member", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Rough story progress", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Last observed", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(companyRows.Length);
                while (clipper.Step())
                    for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) DrawCompanyRow(companyRows[row]);
            }
            finally { clipper.Destroy(); }
        }
    }

    private void DrawCompanyRow(CompanyMember entry)
    {
        var progress = companyEstimates.GetValueOrDefault(entry.Player.Key);
        ImGui.TableNextRow(); ImGui.TableNextColumn();
        if (ImGui.Selectable($"{entry.Player.Name}##fc{entry.Player.Key}", selection.PlayerKey == entry.Player.Key))
            SelectPlayer(entry.Player.Key, true);
        ImGui.TableNextColumn();
        if (progress?.QuestId is { } quest)
        {
            using var tone = ImRaii.PushColor(ImGuiCol.Text, progress.Label.EndsWith('+') ? Accent : progress.Label.EndsWith('?') ? Gold : new Vector4(.63f, .75f, 1f, 1));
            if (ImGui.Selectable($"{progress.Label}##progress{entry.Player.Key}"))
            {
                if (selection.PlayerKey != entry.Player.Key) SelectPlayer(entry.Player.Key);
                selection.ToggleQuest(quest); requestedTab = "Follow player";
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(progress.Basis);
        }
        else ImGui.TextDisabled("Unknown");
        ImGui.TableNextColumn();
        var point = plugin.GetLastSeen(entry.Player.Key);
        ImGui.TextUnformatted(plugin.Seen.ContainsKey(entry.Player.Key) ? "Visible now" : point is not null
            ? $"{point.LastSeenAt:MM-dd HH:mm} UTC" : entry.LocationSeenAt != default
                ? $"{entry.LocationSeenAt:MM-dd HH:mm} UTC (FC cache)" : "Not observed");
    }

    private void DrawFollow()
    {
        using (ImRaii.Disabled(plugin.Target is null))
        if (ImGui.Button("Monitor target") && plugin.Target is { } target)
        {
            addError = plugin.Settings.PersistentPlayers().Any(p => p.Key == target.Key) ? null : plugin.AddPlayer(target.Name, target.HomeWorldId);
            if (addError is null && selection.PlayerKey != target.Key) SelectPlayer(target.Key);
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(200, ImGui.GetContentRegionAvail().X));
        using (var picker = ImRaii.Combo("##PlayerPicker", member?.Name ?? "Choose a player"))
        if (picker)
        {
            Search("Find player", ref playerSearch);
            if (ImGui.Selectable("None — clear player", selection.PlayerKey is null)) { selection.ClearPlayer(); RefreshSelection(); }
            foreach (var player in plugin.Settings.PersistentPlayers().Concat(plugin.SessionPlayers.Values.Select(s => s.Player))
                         .DistinctBy(p => p.Key).Where(p => p.Name.Contains(playerSearch, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p.Name).Take(200))
                if (ImGui.Selectable($"{player.Name} @ {plugin.Catalog.WorldName(player.HomeWorldId)}##pick{player.Key}", selection.PlayerKey == player.Key))
                    SelectPlayer(player.Key);
        }
        if (ImGui.CollapsingHeader("Add a player by name")) DrawAddPlayer();
        if (addError is not null) ImGui.TextWrapped(addError);
        if (member is null)
        {
            ImGui.TextWrapped("Choose an FC member, someone seen nearby, or your current target. Their estimated quest path and objectives will appear here.");
            return;
        }
        Badge(estimate?.Label ?? "Unknown", Accent);
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.Seen.ContainsKey(member.Key) ? "Visible now" : "Not currently observed");
        if (plugin.GetLastSeen(member.Key) is { } point)
            ImGui.TextWrapped($"Last seen {point.LastSeenAt:u} — {plugin.Catalog.TerritoryName(point.Zone.TerritoryId)}");
        DrawCandidates();
        if (ImGui.CollapsingHeader("Evidence & tracking")) DrawEvidence();
        ImGui.Separator();
        DrawPanes(() => DrawRoad(anchor, anchorLabel), DrawDetails);
    }

    private void DrawCandidates()
    {
        var recent = candidates.Count(c => c.Recent);
        ImGui.TextColored(Accent, $"Possible quests — {recent} recent / {candidates.Length - recent} earlier");
        if (candidates.Length == 0)
        {
            ImGui.TextWrapped("No objective observations yet. Any path below is a progression reference, not an observed quest.");
            return;
        }
        ImGui.TextWrapped("Each is a possibility. Select one to center its path and inspect its steps; select it again to clear.");
        var height = Math.Clamp(ImGui.GetContentRegionAvail().Y * .30f, 80 * Scale, 155 * Scale);
        using var child = ImRaii.Child("##Candidates", new Vector2(0, height), true);
        if (child)
        {
            using var choices = ImRaii.Table("##CandidateChoices", Math.Min(2, candidates.Length), ImGuiTableFlags.SizingStretchSame);
            if (choices)
            {
                foreach (var candidate in candidates)
                {
                    if (!plugin.Catalog.Chain.Quests.TryGetValue(candidate.QuestId, out var quest)) continue;
                    ImGui.TableNextColumn();
                    QuestRow(quest, $"candidate{quest.Id}", candidate: true);
                    var progress = plugin.Catalog.Details(quest.Id).ProgressLabel(candidate.Match.Steps);
                    using var tone = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                    ImGui.TextWrapped($"{(candidate.Match.NearbyNow ? "Possible now" : candidate.Recent ? "Recent match" : "Earlier match")} · step {progress}");
                    var clueStep = plugin.Catalog.Details(quest.Id).ProgressLabel([candidate.Evidence.Objective.TodoIndex]);
                    ImGui.TextWrapped($"{candidate.Evidence.Description} · step {clueStep}");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{candidate.Evidence.ObservedAt:u}\n{MatchScope(candidate.Evidence)}");
                    ImGui.TextWrapped($"{candidate.Evidence.ObservedAt:MM-dd HH:mm} UTC · " +
                        (candidate.Evidence.MatchingQuestCount > 1 ? $"shared by {candidate.Evidence.MatchingQuestCount} quests" : "one known quest fits"));
                }
            }
        }
    }

    private void DrawAddPlayer()
    {
        ImGui.InputText("Full character name", ref name, 65);
        using (var worlds = ImRaii.Combo("Home world", world == 0 ? "Choose a world" : plugin.Catalog.WorldName(world)))
        if (worlds)
        {
            if (ImGui.Selectable("None", world == 0)) world = 0;
            foreach (var w in plugin.Catalog.Worlds)
                if (ImGui.Selectable(w.Name, world == w.Id)) world = world == w.Id ? 0 : w.Id;
        }
        if (ImGui.Button("Track player"))
        {
            addError = plugin.AddPlayer(name, world);
            if (addError is null) SelectPlayer(WatchedPlayer.MakeKey(name, world));
        }
    }

    private void DrawEvidence()
    {
        if (member is null) return;
        ImGui.TextWrapped(estimate?.Basis ?? "No evidence yet.");
        if (estimate?.Ceiling is { } ceiling) ImGui.TextWrapped(ceiling);
        if (estimate?.Conflict == true) ImGui.TextColored(Accent, "Conflicting evidence; the estimate is uncertain.");
        ImGui.TextWrapped("A cutscene start supports a quest match, even when only one known quest fits, but never confirms it. Visible markers can overlap or omit objectives.");
        ImGui.TextWrapped("Combat near an objective does not identify its cause or who spawned a monster. NPC dialogue has no reliably observed remote interaction event; no status change does not rule a quest out.");
        var fc = plugin.Settings.Company.Members.FirstOrDefault(m => m.Player.Key == member.Key);
        var manual = plugin.Settings.Players.Any(p => p.Key == member.Key);
        if (manual && ImGui.Button(fc is null ? "Stop tracking and delete saved point" : "Remove from manual list")) plugin.RemovePlayer(member);
        if (!manual && fc is null && ImGui.Button("Save this player's last-seen progress")) addError = plugin.AddPlayer(member.Name, member.HomeWorldId);
        ImGui.TextDisabled(fc is not null ? "Saved as an FC member." : manual ? "Saved across sessions." : "Session only; discarded on unload.");
        if (ImGui.Button("Clear recorded evidence")) { plugin.ClearRecordedData(member.Key); RefreshSelection(); }
    }

    private static void DrawPanes(Action left, Action right)
    {
        var width = Math.Max(245, ImGui.GetContentRegionAvail().X * .40f);
        using (var pane = ImRaii.Child("##PathPane", new Vector2(width, 0), true))
            if (pane) left();
        ImGui.SameLine();
        using (var pane = ImRaii.Child("##DetailPane", new Vector2(0, 0), true))
            if (pane) right();
    }

    private void DrawRoad(uint? root, string label)
    {
        ImGui.TextUnformatted("Quest path");
        ImGui.TextDisabled("3 earlier / 5 upcoming");
        ImGui.TextWrapped("Linked quests, not a completion checklist. Click any quest for details; click it again to clear.");
        if (root is null || !plugin.Catalog.Chain.Quests.TryGetValue(root.Value, out var current))
        {
            ImGui.TextWrapped("No quest clue yet. Observations and public unlocks will fill in the path.");
            return;
        }
        var previous = plugin.Catalog.Chain.Previous(current.Id);
        var upcoming = plugin.Catalog.Chain.Upcoming(current.Id);
        DrawBranch(previous, "Earlier");
        for (var i = previous.Quests.Length - 1; i >= 0; i--) RoadNode(previous.Quests[i], $"Earlier {i + 1}", false);
        RoadNode(current, label, true);
        if (lastRoadAnchor != root) { ImGui.SetScrollHereY(.4f); lastRoadAnchor = root; }
        for (var i = 0; i < upcoming.Quests.Length; i++) RoadNode(upcoming.Quests[i], $"Upcoming {i + 1}", false);
        DrawBranch(upcoming, "Next");
    }

    private void RoadNode(QuestInfo quest, string label, bool current)
    {
        var origin = ImGui.GetCursorScreenPos();
        var inset = 18 * Scale;
        using var indent = ImRaii.PushIndent(inset, scaled: false);
        var style = ImGui.GetStyle();
        var innerWidth = Math.Max(40, ImGui.GetContentRegionAvail().X - style.WindowPadding.X * 2);
        var titleHeight = Math.Max(26 * Scale, ImGui.CalcTextSize(quest.Name, false, Math.Max(40, innerWidth - 36 * Scale)).Y);
        var dutyHeight = quest.Duties.Sum(d => Math.Max(22 * Scale,
            ImGui.CalcTextSize(d.Name, false, Math.Max(40, innerWidth - 22 * Scale - style.ItemSpacing.X)).Y));
        var height = style.WindowPadding.Y * 2 + ImGui.CalcTextSize(label, false, innerWidth).Y +
            titleHeight + dutyHeight + style.ItemSpacing.Y * (1 + quest.Duties.Length) + 2 * Scale;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, current ? new Vector4(.10f, .20f, .23f, 1) : new Vector4(.09f, .115f, .15f, 1)))
        {
            using var card = ImRaii.Child($"##RoadCard{quest.Id}", new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar);
            if (card)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, current ? Accent : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
                    ImGui.TextWrapped(label);
                QuestRow(quest, $"road{quest.Id}");
                foreach (var duty in quest.Duties) DrawDuty(duty);
            }
        }
        indent.Dispose();
        ImGui.Spacing();
        var end = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddLine(origin + new Vector2(5, 15) * Scale, new Vector2(origin.X + 5 * Scale, end.Y + 15 * Scale), 0xFF554638, 2 * Scale);
        draw.AddCircleFilled(origin + new Vector2(5, 15) * Scale, (current ? 5 : 3) * Scale, current ? ImGui.ColorConvertFloat4ToU32(Accent) : 0xFF998877);
    }

    private void DrawBranch(QuestChainPath path, string label)
    {
        if (path.Stop == ChainStop.Branch)
        {
            ImGui.TextWrapped($"{label}: alternate links; no single route assumed.");
            foreach (var quest in path.Branches) QuestRow(quest, $"branch{label}{quest.Id}");
        }
        else if (path.Stop is ChainStop.MissingLink or ChainStop.Cycle)
            ImGui.TextDisabled($"{label}: incomplete or repeated quest links.");
        else if (path.Stop == ChainStop.End) ImGui.TextDisabled($"{label}: end of linked chain.");
    }

    private void QuestRow(QuestInfo quest, string key, bool candidate = false)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var iconSize = 26 * Scale;
        var textWidth = Math.Max(40, width - iconSize - 10 * Scale);
        var height = Math.Max(iconSize, ImGui.CalcTextSize(quest.Name, false, textWidth).Y);
        if (ImGui.Selectable($"##{key}", candidate ? selection.CandidateId == quest.Id : !selection.FollowingPlayer && selection.QuestId == quest.Id,
                ImGuiSelectableFlags.None, new Vector2(width, height)))
        {
            if (candidate) { selection.ToggleCandidate(quest.Id); RefreshSelection(); }
            else selection.ToggleQuest(quest.Id);
            focused = selection.ResolveQuest(anchor);
        }
        var hovered = ImGui.IsItemHovered();
        var next = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(origin); Icon(quest.IconId, iconSize);
        ImGui.SetCursorScreenPos(origin + new Vector2(iconSize + 10 * Scale, 0));
        using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + textWidth))
            ImGui.TextUnformatted(quest.Name);
        ImGui.SetCursorScreenPos(next);
        if (hovered) ImGui.SetTooltip($"{quest.Kind} / Level {quest.Level}\n{(candidate ? "Click to center the path on this possibility" : "Click to preview")}; click again to clear.");
    }

    private void DrawDuty(QuestDuty duty)
    {
        using (ImRaii.Group())
        {
            if (duty.IconId != 0) Icon(duty.IconId, 22 * Scale);
            else
            {
                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.Dungeon.ToIconString());
            }
            ImGui.SameLine();
            ImGui.TextWrapped(duty.Name);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{duty.Type}\n{duty.Relationship}");
    }

    private static string MatchScope(Clue clue) => clue.MatchingQuestCount == 1
        ? "Only one catalog quest matched this observation; it is still unconfirmed."
        : $"{clue.MatchingQuestCount} catalog quests shared this observation.";

    private void DrawDetails()
    {
        focused = selection.ResolveQuest(anchor);
        if (lastDetailsQuest != focused) { ImGui.SetScrollY(0); lastDetailsQuest = focused; }
        if (focused is not { } id || !plugin.Catalog.Chain.Quests.TryGetValue(id, out var quest))
        {
            ImGui.TextUnformatted("Quest details");
            ImGui.TextWrapped("Select a quest to inspect its objectives and duties.");
            return;
        }
        Icon(quest.IconId, 36 * Scale); ImGui.SameLine(); ImGui.TextWrapped(quest.Name);
        Badge(quest.Kind, quest.IsMainScenario ? Gold : quest.IsUnlock ? new Vector4(.50f, .72f, 1, 1) : Accent);
        ImGui.SameLine(); ImGui.TextDisabled($"Level {quest.Level}");
        if (ImGui.SmallButton("Close details")) { selection.ClearQuest(); focused = null; return; }
        if (member is not null && !selection.FollowingPlayer)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Follow player again")) { selection.FollowPlayer(); return; }
        }
        var detail = plugin.Catalog.Details(id);
        if (detail.Summary.Length > 0) ImGui.TextWrapped(detail.Summary);
        var candidate = candidates.FirstOrDefault(c => c.QuestId == id);
        if (candidate is not null)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, "Observed possibility — unconfirmed");
            ImGui.TextWrapped($"{candidate.Evidence.Description} at step " +
                $"{detail.ProgressLabel([candidate.Evidence.Objective.TodoIndex])} ({candidate.Evidence.ObservedAt:u}).");
            ImGui.TextWrapped(MatchScope(candidate.Evidence));
            if (!candidate.Recent) ImGui.TextWrapped("This clue is over 20 minutes old; current progress is unknown.");
        }
        var live = member is not null ? plugin.Seen.GetValueOrDefault(member.Key) : null;
        var match = ObjectiveMatch.Resolve(id, evidence, live, DateTimeOffset.UtcNow);
        ImGui.Separator();
        ImGui.TextUnformatted("Objectives");
        if (match.ObservedAt is { } observed)
        {
            ImGui.TextWrapped(match.NearbyNow ? "Possible current step: player is near the latest matched objective." : $"Last objective match: {observed:u}. Current step unknown.");
            if (match.Ambiguous) ImGui.TextColored(Accent, "Ambiguous: several steps or quests share this clue.");
        }
        else ImGui.TextDisabled("No objective observation for this quest.");
        ImGui.TextWrapped("Highlighted steps are estimates. Earlier steps are not marked complete.");
        ImGui.TextWrapped(quest.HasCutsceneReference
            ? "Quest data references cutscenes, but does not identify which step plays them."
            : "Dialogue versus cutscene is unknown for these steps; missing cutscene references do not mean dialogue only.");
        foreach (var step in detail.Steps)
        {
            ImGui.Spacing();
            var matching = match.Steps.Contains(step.Index);
            using (ImRaii.PushColor(ImGuiCol.Text, matching ? Gold : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]))
                ImGui.TextWrapped($"{detail.StepNumber(step.Index)}. {step.Text}");
            foreach (var location in step.Locations.DistinctBy(p => (p.TerritoryId, p.LevelId)))
                DrawMapLocation(location);
            if (step.Locations.Length == 0) ImGui.TextDisabled("  No map coordinate in quest data.");
            if (matching) Badge(match.NearbyNow ? "Possible now" : "Last match", Gold);
        }
        if (!detail.TextAvailable) ImGui.TextDisabled("Objective text unavailable; showing known locations.");
        ImGui.Separator(); ImGui.TextUnformatted("Associated duties");
        foreach (var duty in quest.Duties)
        {
            DrawDuty(duty);
            using var tone = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(duty.Relationship);
        }
        if (quest.Duties.Length == 0) ImGui.TextDisabled("No duty linked in quest data.");
        if (quest.PreviousQuestIds.Length > 0 && ImGui.CollapsingHeader("Prerequisite quests"))
            foreach (var previous in quest.PreviousQuestIds)
                if (plugin.Catalog.Chain.Quests.TryGetValue(previous, out var prerequisite)) QuestRow(prerequisite, $"prereq{previous}");
    }

    private void DrawMapLocation(QuestObjective location)
    {
        var link = new MapLinkPayload(location.TerritoryId, location.MapId,
            (int)(location.Position.X * 1000), (int)(location.Position.Z * 1000));
        if (link.Map.ValueNullable is not { SizeFactor: > 0 } map)
        {
            ImGui.TextDisabled("Map unavailable in quest data.");
            return;
        }
        var mapName = map.PlaceName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(mapName)) mapName = plugin.Catalog.TerritoryName(location.TerritoryId);
        var floor = map.PlaceNameSub.ValueNullable?.Name.ExtractText();
        if (!string.IsNullOrWhiteSpace(floor) && floor != mapName) mapName += $" — {floor}";
        if (ImGuiComponents.IconButton($"map{location.Key}", FontAwesomeIcon.Map))
            failedMapLocation = gameGui.OpenMapWithMapLink(link) ? null : location.Key;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Open map and flag this objective\n{mapName} {link.CoordinateString}\n{location.NpcName}");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            ImGui.TextWrapped($"{link.CoordinateString}  {mapName}");
        if (failedMapLocation == location.Key) ImGui.TextWrapped("The game could not open this map right now.");
    }

    private void DrawBrowser()
    {
        Search("Search quests (2+ characters)", ref search);
        var msq = plugin.Settings.MainScenarioOnly;
        if (ImGui.Checkbox("MSQ only", ref msq)) { plugin.Settings.MainScenarioOnly = msq; plugin.Save(); }
        var key = (search.Trim(), msq, plugin.CurrentZone.TerritoryId);
        if (browseKey != key)
        {
            browseKey = key;
            var nearby = plugin.Catalog.InTerritory(key.Item3).Select(o => o.QuestId).ToHashSet();
            browseQuests = plugin.Catalog.Chain.Quests.Values.Where(q => (!msq || q.IsMainScenario) &&
                (key.Item1.Length >= 2 ? q.Name.Contains(key.Item1, StringComparison.OrdinalIgnoreCase) : nearby.Contains(q.Id)))
                .OrderBy(q => q.Name).ToArray();
        }
        DrawPanes(() =>
        {
            ImGui.TextDisabled($"{browseQuests.Length} matches / showing up to 100");
            foreach (var quest in browseQuests.Take(100)) QuestRow(quest, $"browse{quest.Id}");
        }, () =>
        {
            DrawDetails();
            if (focused is { } id && ImGui.CollapsingHeader("Quest chain")) DrawRoad(id, "Browsing");
        });
    }

    private void DrawSettings()
    {
        var observing = plugin.Settings.ObservePlayers;
        if (ImGui.Checkbox("Observe visible players", ref observing))
        {
            plugin.Settings.ObservePlayers = observing; plugin.Engine.Pause(); plugin.Seen.Clear(); plugin.Save();
        }
        var fc = plugin.Settings.TrackFreeCompany;
        if (ImGui.Checkbox("Track my FC and check public Lodestone profiles", ref fc)) { plugin.Settings.TrackFreeCompany = fc; plugin.Save(); }
        var markers = plugin.Settings.ShowMarkers;
        if (ImGui.Checkbox("Show in-world quest icons", ref markers)) { plugin.Settings.ShowMarkers = markers; plugin.Save(); }
        ImGui.TextWrapped("All nearby MSQ, blue unlock and normal quest markers are shown. Click a specific quest to focus its markers; clear it to show all again. Following a player and the browser's MSQ filter do not narrow world markers.");
        ImGui.TextWrapped("Visible-player clues are estimates. Manually tracked players and FC members are saved locally. Other players stay in memory until unload. Public unlocks establish minimum progress; missing/private data proves nothing.");
        ImGui.TextWrapped("World markers pause in duties, cutscenes and zoning. Public-profile checks also pause in combat. Markers have no wall occlusion and are limited to 32 within 150 yalms.");
        ImGui.TextDisabled($"Lodestone: one page every {CompanyTracker.RequestIntervalSeconds}s; profiles every 3 days; roster every 6 hours.");
        if (plugin.Settings.Company.CachedMemberCount != plugin.Settings.Company.Members.Count)
            ImGui.TextWrapped($"Membership sources: {plugin.Settings.Company.Members.Count} public roster / {plugin.Settings.Company.CachedMemberCount} reported by the game cache.");
        var debug = plugin.Settings.DebugMode;
        if (ImGui.Checkbox("Debug diagnostics", ref debug)) { plugin.Settings.DebugMode = debug; plugin.Save(); }
        if (debug)
        {
            ImGui.TextWrapped($"Requests {plugin.CompanyTracker.Requests}; failures {plugin.CompanyTracker.Failures}; next request {plugin.Settings.Company.NextRequestAt:u}. Last failure: {plugin.CompanyTracker.LastFailure}");
            ImGui.TextWrapped($"Quest records {plugin.Catalog.Chain.Quests.Count}; objectives {plugin.Catalog.Objectives.Length}; following {selection.FollowingPlayer}; quest selected {focused.HasValue}.");
            ImGui.TextDisabled($"Player context menus: {plugin.ContextMenuOffers} offered / {plugin.ContextMenuOpens} opened.");
        }
    }

    private void Icon(uint id, float size)
    {
        if (textures.TryGetFromGameIcon(id, out var shared) && shared.TryGetWrap(out var texture, out _))
            ImGui.Image(texture.Handle, new Vector2(size));
        else ImGui.Dummy(new Vector2(size));
    }

    private static void Badge(string text, Vector4 color)
    {
        var origin = ImGui.GetCursorScreenPos();
        var pad = new Vector2(8, 4) * Scale;
        var size = ImGui.CalcTextSize(text) + pad * 2;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(color with { W = .14f }), 5 * Scale);
        draw.AddText(origin + pad, ImGui.ColorConvertFloat4ToU32(color), text);
        ImGui.Dummy(size);
    }

    private static void StatCard(string label, int count, Vector4 color)
    {
        ImGui.TableNextColumn();
        using var card = ImRaii.Child($"##{label}", new Vector2(0, 78 * Scale), true, ImGuiWindowFlags.NoScrollbar);
        if (card)
        {
            ImGui.TextColored(color, count.ToString());
            ImGui.TextDisabled(label);
        }
    }

    public void DrawMarkers()
    {
        if (!plugin.Settings.ShowMarkers || !plugin.WorldAvailable || gameGui.GameUiHidden) return;
        var key = (plugin.CurrentZone, plugin.LocalPosition, selection.QuestId);
        if (markerKey != key)
        {
            markerKey = key;
            markers = plugin.Catalog.InTerritory(plugin.CurrentZone.TerritoryId)
                .Where(o => o.MapId == plugin.CurrentZone.MapId &&
                    (selection.QuestId is not { } id || o.QuestId == id) &&
                    Vector3.DistanceSquared(o.Position, plugin.LocalPosition) <= 150f * 150f)
                .GroupBy(o => (o.QuestId, o.LevelId))
                .OrderBy(g => Vector3.DistanceSquared(g.First().Position, plugin.LocalPosition)).Take(32)
                .Select(g =>
                {
                    var objective = g.First();
                    var progress = plugin.Catalog.Details(objective.QuestId).ProgressLabel(g.Select(o => o.TodoIndex));
                    return (objective, $"{objective.QuestName} ({progress})");
                }).ToArray();
        }
        var draw = ImGui.GetBackgroundDrawList();
        var occupied = new List<Vector2>();
        var size = 32 * ImGui.GetFontSize() / 17f;
        // shortcut: overlays have no wall occlusion; add depth checks if hidden markers become distracting.
        foreach (var marker in markers)
        {
            var objective = marker.Objective;
            if (!gameGui.WorldToScreen(objective.Position + Vector3.UnitY * 1.5f, out var screen) ||
                occupied.Any(p => Vector2.DistanceSquared(p, screen) < size * size)) continue;
            occupied.Add(screen);
            var icon = plugin.Catalog.Chain.Quests[objective.QuestId].IconId;
            if (textures.TryGetFromGameIcon(icon, out var shared) && shared.TryGetWrap(out var texture, out _))
                draw.AddImage(texture.Handle, screen - new Vector2(size / 2), screen + new Vector2(size / 2));
            var textPos = screen + new Vector2(size / 2 + 4, -ImGui.GetTextLineHeight() / 2);
            draw.AddText(textPos + Vector2.One, 0xFF000000, marker.Label);
            draw.AddText(textPos, 0xFFEEEEEE, marker.Label);
        }
    }
}
