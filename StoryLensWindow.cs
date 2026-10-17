using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using StoryLens.Core;

namespace StoryLens;

internal sealed class StoryLensWindow(Plugin plugin)
{
    public bool IsOpen;
    private string name = string.Empty;
    private uint world;
    private string search = string.Empty;
    private string playerSearch = string.Empty;
    private string? selectedPlayer;
    private string? addError;
    private (string Search, bool MsqOnly, uint Territory)? browseKey;
    private QuestObjective[] browseQuests = [];
    private DateTimeOffset nextCompanyEstimate;
    private Dictionary<string, ProgressEstimate> companyEstimates = [];

    public void Draw()
    {
        if (!IsOpen)
            return;
        ImGui.SetNextWindowSize(new Vector2(760, 620), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Story Lens###StoryLens", ref IsOpen))
        {
            ImGui.TextWrapped(plugin.Status);
            ImGui.TextWrapped("Quest candidates are clues, never confirmed progress. Tracked last-seen points are saved; other player data stays in memory for this session.");
            var observing = plugin.Settings.ObservePlayers;
            if (ImGui.Checkbox("Automatically observe visible players", ref observing))
            {
                plugin.Settings.ObservePlayers = observing;
                plugin.Engine.Pause();
                plugin.Seen.Clear();
                plugin.Save();
            }
            if (plugin.SaveError is not null)
            {
                ImGui.TextWrapped(plugin.SaveError);
                if (ImGui.Button("Retry save"))
                    plugin.Save();
            }
            if (ImGui.BeginTabBar("StoryLensTabs"))
            {
                if (ImGui.BeginTabItem("Free Company"))
                {
                    DrawCompany();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Players"))
                {
                    DrawPlayers();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Quest objectives"))
                {
                    DrawObjectives();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Quest chain & duties"))
                {
                    DrawQuestChain();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void DrawCompany()
    {
        if (DateTimeOffset.UtcNow >= nextCompanyEstimate || companyEstimates.Count != plugin.Settings.Company.Members.Count)
        {
            nextCompanyEstimate = DateTimeOffset.UtcNow.AddSeconds(1);
            companyEstimates = plugin.Settings.Company.Members.ToDictionary(m => m.Player.Key, m => plugin.Estimate(m.Player.Key));
        }
        var enabled = plugin.Settings.TrackFreeCompany;
        if (ImGui.Checkbox("Automatically track my Free Company and check public Lodestone profiles", ref enabled))
        {
            plugin.Settings.TrackFreeCompany = enabled;
            plugin.Save();
        }
        ImGui.TextWrapped(plugin.CompanyTracker.Status);
        ImGui.TextWrapped("~ = observed quest estimate; + = minimum from an unlock; ? = area clue. Percentages count catalog quests within the launch story or patch story shown. They are estimates, including alternate routes; level caps and public profiles may be stale.");
        ImGui.InputText("Find FC member", ref playerSearch, 120);
        ImGui.TextDisabled($"{plugin.Settings.Company.Members.Count} members; one page every {CompanyTracker.RequestIntervalSeconds} seconds; profiles refreshed after 3 days; roster every 6 hours.");
        ImGui.TextDisabled("The sweep pauses automatically during combat and duties.");
        if (plugin.Settings.Company.CachedMemberCount > 0 && plugin.Settings.Company.CachedMemberCount != plugin.Settings.Company.Members.Count)
            ImGui.TextWrapped($"Lodestone currently lists {plugin.Settings.Company.Members.Count} members; the in-game cache reports {plugin.Settings.Company.CachedMemberCount}. Membership sources can update at different times; the roster refreshes automatically.");
        if (ImGui.BeginChild("##CompanyRoster", new Vector2(0, 245), true))
        {
            foreach (var member in plugin.Settings.Company.Members.OrderBy(m => m.Player.Name))
            {
                if (playerSearch.Length > 0 && !member.Player.Name.Contains(playerSearch, StringComparison.OrdinalIgnoreCase)) continue;
                var estimate = companyEstimates.GetValueOrDefault(member.Player.Key) ?? plugin.Estimate(member.Player.Key);
                if (ImGui.Selectable($"{estimate.Label,-22} {member.Player.Name}##fc{member.LodestoneId}", selectedPlayer == member.Player.Key))
                    selectedPlayer = member.Player.Key;
            }
        }
        ImGui.EndChild();
        if (plugin.Settings.Company.Members.FirstOrDefault(m => m.Player.Key == selectedPlayer) is { } selected)
        {
            var estimate = plugin.Estimate(selected.Player.Key);
            ImGui.Separator();
            ImGui.TextUnformatted($"{selected.Player.Name} — {estimate.Label}");
            ImGui.TextWrapped(estimate.Basis);
            if (estimate.Ceiling is not null) ImGui.TextWrapped(estimate.Ceiling);
            if (estimate.Conflict) ImGui.TextWrapped("Evidence conflicts with the level snapshot. Treat the estimate as uncertain until a newer profile check.");
            ImGui.TextWrapped(selected.Profile.Status);
            foreach (var section in selected.Profile.SectionStatus) ImGui.TextDisabled($"{section.Key}: {section.Value}");
            if (selected.Profile.CheckedAt != default) ImGui.TextDisabled($"Profile last checked {selected.Profile.CheckedAt:u}");
            if (selected.LocationId > 0) ImGui.TextWrapped($"FC list location: {plugin.Catalog.TerritoryName(selected.LocationId)}, cached observation {selected.LocationSeenAt:u}");
            foreach (var source in selected.Profile.Sources.TakeLast(8)) ImGui.BulletText(source);
            if (estimate.QuestId is { } quest && ImGui.Button("Show estimated quest chain and markers")) plugin.FocusedQuestId = quest;
            if (ImGui.Button("Clear this member's recorded evidence")) plugin.ClearRecordedData(selected.Player.Key);
        }
        var debug = plugin.Settings.DebugMode;
        if (ImGui.Checkbox("Debug diagnostics", ref debug)) { plugin.Settings.DebugMode = debug; plugin.Save(); }
        if (debug) ImGui.TextWrapped($"Requests: {plugin.CompanyTracker.Requests}; failures: {plugin.CompanyTracker.Failures}; MSQ records: {plugin.Progress.Story.Quests.Count}; achievement rules: {plugin.Progress.AchievementQuests.Count}; collection rules: {plugin.Progress.Collections.Length}. Next request no earlier than {plugin.Settings.Company.NextRequestAt:u}.");
        if (debug) ImGui.TextWrapped($"Last failure: {plugin.CompanyTracker.LastFailure}");
    }

    private void DrawPlayers()
    {
        var persistentKeys = plugin.Settings.PersistentPlayers().Select(p => p.Key).ToHashSet();
        selectedPlayer ??= plugin.Settings.Players.FirstOrDefault()?.Key;
        ImGui.TextWrapped("Visible players are observed automatically. Track a player to save their last-seen point and quest clues across game sessions.");
        ImGui.BeginDisabled(plugin.Target is null);
        if (ImGui.Button("Add targeted player") && plugin.Target is { } target)
        {
            addError = plugin.AddPlayer(target.Name, target.HomeWorldId);
            selectedPlayer = target.Key;
        }
        ImGui.EndDisabled();
        if (plugin.Target is { } t)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"{t.Name} @ {plugin.Catalog.WorldName(t.HomeWorldId)}");
        }
        if (ImGui.CollapsingHeader("Add by name"))
        {
            ImGui.InputText("Full character name", ref name, 65);
            if (ImGui.BeginCombo("Home world", plugin.Catalog.WorldName(world)))
            {
                foreach (var w in plugin.Catalog.Worlds)
                    if (ImGui.Selectable(w.Name, world == w.Id))
                        world = w.Id;
                ImGui.EndCombo();
            }
            if (ImGui.Button("Add to list"))
            {
                addError = plugin.AddPlayer(name, world);
                if (addError is null)
                    selectedPlayer = WatchedPlayer.MakeKey(name, world);
            }
        }
        if (addError is not null)
            ImGui.TextWrapped(addError);
        ImGui.Separator();
        ImGui.TextUnformatted("Tracked players (saved)");
        foreach (var player in plugin.Settings.Players)
        {
            var visible = plugin.Seen.ContainsKey(player.Key);
            var lastSeen = plugin.Settings.LastSeen.GetValueOrDefault(player.Key);
            var status = visible ? "visible" : lastSeen is null ? "not seen yet" : $"last seen {lastSeen.LastSeenAt:yyyy-MM-dd HH:mm} UTC";
            if (ImGui.Selectable($"{player.Name} @ {plugin.Catalog.WorldName(player.HomeWorldId)} — {status}##{player.Key}",
                    selectedPlayer == player.Key))
                selectedPlayer = player.Key;
        }

        if (ImGui.CollapsingHeader($"Session-only players ({plugin.SessionPlayers.Count(p => !persistentKeys.Contains(p.Key))})###StoryLensSessionPlayers"))
        {
            ImGui.InputText("Find seen player", ref playerSearch, 120);
            ImGui.TextWrapped("This list is never saved. Select a player to inspect earlier clues or start persistent tracking.");
            if (ImGui.BeginListBox("##SessionPlayers", new Vector2(-1, 150)))
            {
                foreach (var session in plugin.SessionPlayers.Values
                             .Where(s => !persistentKeys.Contains(s.Player.Key) &&
                                 (playerSearch.Length == 0 || s.Player.Name.Contains(playerSearch, StringComparison.OrdinalIgnoreCase)))
                             .OrderByDescending(s => s.LastSeenAt).Take(200))
                {
                    var player = session.Player;
                    if (ImGui.Selectable($"{player.Name} @ {plugin.Catalog.WorldName(player.HomeWorldId)} — {(plugin.Seen.ContainsKey(player.Key) ? "visible" : "seen earlier")}##{player.Key}",
                            selectedPlayer == player.Key))
                        selectedPlayer = player.Key;
                }
                ImGui.EndListBox();
            }
            ImGui.TextDisabled("Showing up to 200 matches; search to find older observations.");
        }
        var selected = plugin.Settings.PersistentPlayers().FirstOrDefault(p => p.Key == selectedPlayer) ??
            (selectedPlayer is not null ? plugin.SessionPlayers.GetValueOrDefault(selectedPlayer)?.Player : null);
        if (selected is null)
            return;
        ImGui.Separator();
        var companyTracked = plugin.Settings.Company.Members.Any(m => m.Player.Key == selected.Key);
        var manuallyTracked = plugin.Settings.Players.Any(p => p.Key == selected.Key);
        var tracked = companyTracked || manuallyTracked;
        if (manuallyTracked && ImGui.Button(companyTracked ? "Remove from manual list (FC tracking continues)" : "Stop tracking (delete saved point)"))
        {
            plugin.RemovePlayer(selected);
        }
        if (!tracked && ImGui.Button("Track player and save last-seen point"))
            addError = plugin.AddPlayer(selected.Name, selected.HomeWorldId);
        if (companyTracked) ImGui.TextDisabled("Saved automatically as a Free Company member.");
        ImGui.SameLine();
        if (ImGui.Button("Clear recorded data"))
            plugin.ClearRecordedData(selected.Key);
        if (plugin.GetLastSeen(selected.Key) is { } point)
        {
            ImGui.TextWrapped($"Last seen: {point.LastSeenAt:yyyy-MM-dd HH:mm:ss} UTC — {plugin.Catalog.TerritoryName(point.Zone.TerritoryId)}, instance {point.Zone.Instance}");
            ImGui.TextUnformatted($"World position: {point.Position.X:0.0}, {point.Position.Y:0.0}, {point.Position.Z:0.0}");
            if (!plugin.Seen.ContainsKey(selected.Key))
                ImGui.TextDisabled(tracked ? "Saved last-seen information; this player is not currently observed." :
                    "Earlier session observation; this player is not currently observed.");
        }
        var candidates = plugin.GetEvidence(selected.Key)
            .GroupBy(c => c.Objective.QuestId).ToArray();
        ImGui.TextUnformatted($"{candidates.Length} quest candidate(s). Showing the 12 strongest; click one to focus its markers.");
        if (candidates.Length == 0)
            ImGui.TextWrapped("No matching objective observed yet. Stay nearby; another player's quest log is not available to this client.");
        foreach (var group in candidates.Take(12))
        {
            var clue = group.First();
            if (ImGui.Selectable($"{(clue.Objective.IsMainScenario ? "MSQ: " : "")}{clue.Objective.QuestName}##candidate{group.Key}",
                    plugin.FocusedQuestId == group.Key))
                plugin.FocusedQuestId = group.Key;
            var age = Math.Max(0, (DateTimeOffset.UtcNow - clue.ObservedAt).TotalMinutes);
            ImGui.TextWrapped($"{clue.Description}; {clue.Distance:0.0}y from marker; {age:0.0} min ago.");
            ImGui.TextWrapped(clue.MatchingQuestCount > 1
                ? $"Ambiguous location: {clue.MatchingQuestCount} quests matched that observation (including side quests)."
                : "One quest matched in this catalog. Other activities or missing locations can still explain it.");
            ImGui.TextWrapped("Possible steps: " + string.Join(", ", group.Select(c => c.Objective.Label).Distinct()));
        }
    }

    private void DrawQuestChain()
    {
        var member = plugin.Settings.PersistentPlayers().FirstOrDefault(p => p.Key == selectedPlayer) ??
            (selectedPlayer is not null ? plugin.SessionPlayers.GetValueOrDefault(selectedPlayer)?.Player : null) ??
            (plugin.Settings.Players.Count == 1 ? plugin.Settings.Players[0] : null);
        var candidate = member is null ? null : plugin.GetEvidence(member.Key).FirstOrDefault();
        var candidateId = candidate?.Objective.QuestId;
        var id = plugin.FocusedQuestId ?? candidateId;
        if (id is null || !plugin.Catalog.Chain.Quests.TryGetValue(id.Value, out var quest))
        {
            ImGui.TextWrapped("Select a player's quest candidate or find a quest in Quest objectives to see its chain and duties.");
            return;
        }

        ImGui.TextWrapped(plugin.FocusedQuestId.HasValue ? "Previewing the focused quest." :
            $"Following the strongest quest candidate for {member!.Name}.");
        if (!plugin.FocusedQuestId.HasValue && candidate is not null)
            ImGui.TextDisabled($"Quest evidence observed at {candidate.ObservedAt:yyyy-MM-dd HH:mm:ss} UTC.");
        if (!plugin.FocusedQuestId.HasValue && member is not null && !plugin.Seen.ContainsKey(member.Key))
            ImGui.TextDisabled("Based on last-seen evidence; the player is not currently observed.");
        ImGui.TextWrapped("Three previous quests and five upcoming quests. Links describe the quest chain, not this player's completion history. Select a quest to preview its markers and chain.");
        if (plugin.FocusedQuestId.HasValue && candidateId.HasValue && ImGui.Button("Follow selected player's strongest candidate"))
            plugin.FocusedQuestId = null;

        var previous = plugin.Catalog.Chain.Previous(quest.Id);
        var upcoming = plugin.Catalog.Chain.Upcoming(quest.Id);
        ImGui.Separator();
        ImGui.TextUnformatted("Previous quests");
        DrawChainStop(previous, "Earlier");
        for (var i = previous.Quests.Length - 1; i >= 0; i--)
            DrawQuestRow(previous.Quests[i], $"-{i + 1}");
        ImGui.Separator();
        DrawQuestRow(quest, "Selected");
        var otherPrerequisites = quest.PreviousQuestIds
            .Select(p => plugin.Catalog.Chain.Quests.GetValueOrDefault(p))
            .OfType<QuestInfo>().Where(q => q.IsMainScenario != quest.IsMainScenario).ToArray();
        if (otherPrerequisites.Length > 0)
        {
            ImGui.TextWrapped("Other prerequisite quest links (requirements may be alternatives):");
            foreach (var prerequisite in otherPrerequisites)
                DrawQuestRow(prerequisite, "Prerequisite");
        }
        ImGui.Separator();
        ImGui.TextUnformatted("Upcoming quests");
        for (var i = 0; i < upcoming.Quests.Length; i++)
            DrawQuestRow(upcoming.Quests[i], $"+{i + 1}");
        DrawChainStop(upcoming, "Next");
        ImGui.TextWrapped("Duty links include quest references, unlocks and prerequisites; they do not prove a duty was entered or completed. Some scripted duties have no usable link.");
    }

    private void DrawQuestRow(QuestInfo quest, string position)
    {
        if (ImGui.Selectable($"{position}: {quest.Name}##chain{position}{quest.Id}",
                plugin.FocusedQuestId == quest.Id))
            plugin.FocusedQuestId = quest.Id;
        ImGui.Indent();
        if (quest.Duties.Length == 0)
            ImGui.TextDisabled("No duty linked in quest data.");
        foreach (var duty in quest.Duties)
            ImGui.TextWrapped($"{duty.Name} — {duty.Type} ({duty.Relationship})");
        ImGui.Unindent();
    }

    private void DrawChainStop(QuestChainPath path, string direction)
    {
        if (path.Stop == ChainStop.Branch)
        {
            ImGui.TextWrapped($"{direction} links branch or contain alternate versions. Select a branch to continue:");
            foreach (var branch in path.Branches)
                DrawQuestRow(branch, $"{direction} option [{branch.Id & 0xFFFF}]");
        }
        else if (path.Stop == ChainStop.End)
            ImGui.TextDisabled($"{direction}: no further linked quests of this kind.");
        else if (path.Stop == ChainStop.MissingLink)
            ImGui.TextDisabled($"{direction}: quest links are incomplete in this catalog.");
        else if (path.Stop == ChainStop.Cycle)
            ImGui.TextDisabled($"{direction}: stopped at a repeated quest link.");
    }

    private void DrawObjectives()
    {
        var markers = plugin.Settings.ShowMarkers;
        if (ImGui.Checkbox("Show world markers", ref markers))
        {
            plugin.Settings.ShowMarkers = markers;
            plugin.Save();
        }
        var msq = plugin.Settings.MainScenarioOnly;
        if (ImGui.Checkbox("MSQ only when browsing nearby objectives", ref msq))
        {
            plugin.Settings.MainScenarioOnly = msq;
            plugin.Save();
        }
        ImGui.InputText("Search quests", ref search, 120);
        if (plugin.FocusedQuestId is { } focused)
        {
            var quest = plugin.Catalog.Chain.Quests.GetValueOrDefault(focused);
            ImGui.TextWrapped($"Focused quest: {quest?.Name}");
            if (ImGui.Button("Show all nearby quests again"))
                plugin.FocusedQuestId = null;
        }
        ImGui.TextWrapped("Markers are overlays at quest-data coordinates, including NPCs your character cannot see. They show possible steps, not that player's active step. Up to 32 nearby markers within 150 yalms; no wall occlusion.");
        var key = (search.Trim(), plugin.Settings.MainScenarioOnly, plugin.CurrentZone.TerritoryId);
        if (browseKey != key)
        {
            browseKey = key;
            browseQuests = (key.Item1.Length >= 2 ? plugin.Catalog.Quests :
                    plugin.Catalog.InTerritory(key.Item3).DistinctBy(o => o.QuestId))
                .Where(o => (!key.Item2 || o.IsMainScenario) &&
                    (key.Item1.Length < 2 || o.QuestName.Contains(key.Item1, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(o => o.QuestName).ToArray();
        }
        ImGui.TextUnformatted($"{browseQuests.Length} quests with known coordinates. Showing up to 80; search needs 2 characters.");
        foreach (var quest in browseQuests.Take(80))
            if (ImGui.Selectable($"{quest.QuestName}##quest{quest.QuestId}", plugin.FocusedQuestId == quest.QuestId))
                plugin.FocusedQuestId = quest.QuestId;
        if (plugin.FocusedQuestId is { } id)
        {
            ImGui.Separator();
            foreach (var objective in plugin.Catalog.InTerritory(plugin.CurrentZone.TerritoryId)
                         .Where(o => o.QuestId == id).Take(24))
                ImGui.TextWrapped($"{objective.Label}: {objective.NpcName} ({objective.Position.X:0.0}, {objective.Position.Y:0.0}, {objective.Position.Z:0.0})");
        }
    }

    public void DrawMarkers(IGameGui gameGui)
    {
        if (!plugin.Settings.ShowMarkers || !plugin.WorldAvailable || gameGui.GameUiHidden)
            return;
        var objectives = plugin.Catalog.InTerritory(plugin.CurrentZone.TerritoryId)
            .Where(o => o.MapId == plugin.CurrentZone.MapId &&
                (plugin.FocusedQuestId is { } id ? o.QuestId == id : !plugin.Settings.MainScenarioOnly || o.IsMainScenario) &&
                Vector3.Distance(o.Position, plugin.LocalPosition) <= 150f)
            .GroupBy(o => (o.QuestId, o.LevelId)).Select(g => g.First())
            .OrderBy(o => Vector3.DistanceSquared(o.Position, plugin.LocalPosition)).Take(32);
        var draw = ImGui.GetBackgroundDrawList();
        var occupied = new List<Vector2>();
        const uint gold = 0xFF55DFFF;
        // shortcut: overlays do not test wall occlusion; add depth checks if hidden markers become distracting.
        foreach (var objective in objectives)
        {
            if (!gameGui.WorldToScreen(objective.Position + Vector3.UnitY * 1.5f, out var screen) ||
                occupied.Any(p => Vector2.DistanceSquared(p, screen) < 32f * 32f))
                continue;
            occupied.Add(screen);
            draw.AddCircleFilled(screen, 6f, 0xD0000000);
            draw.AddCircle(screen, 6f, gold, 12, 2f);
            var label = $"{objective.QuestName} ({Vector3.Distance(objective.Position, plugin.LocalPosition):0}y)";
            var textPos = screen + new Vector2(10, -8);
            draw.AddText(textPos + Vector2.One, 0xFF000000, label);
            draw.AddText(textPos, gold, label);
        }
    }
}
