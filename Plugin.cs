using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using StoryLens.Core;

namespace StoryLens;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/storylens";
    private readonly object gate = new();
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly ICondition condition;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private readonly StoryLensWindow window;
    private readonly CompanyTracker companyTracker;
    private WorkspaceTests? tests;
    private DateTimeOffset nextScan;
    private DateTimeOffset nextHistorySave;
    private bool historyDirty;
    private string? lastError;
    private bool commandRegistered;
    private bool disposed;

    internal QuestCatalog Catalog { get; }
    internal ProgressCatalog Progress { get; }
    internal CompanyTracker CompanyTracker => companyTracker;
    internal Configuration Settings { get; }
    internal InferenceEngine Engine { get; } = new();
    internal Dictionary<string, PlayerSample> Seen { get; } = [];
    internal Dictionary<string, SessionPlayer> SessionPlayers { get; } = [];
    internal WatchedPlayer? Target { get; private set; }
    internal Zone CurrentZone { get; private set; }
    internal Vector3 LocalPosition { get; private set; }
    internal bool WorldAvailable { get; private set; }
    internal string Status { get; private set; } = "Waiting for the first game update.";
    internal string? SaveError { get; private set; }
    internal uint? FocusedQuestId { get; set; }

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework,
        IClientState client, ICondition condition, IObjectTable objects, ITargetManager targets,
        IDataManager data, ICommandManager commands, IGameGui gameGui, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.client = client;
        this.condition = condition;
        this.objects = objects;
        this.targets = targets;
        this.commands = commands;
        this.gameGui = gameGui;
        this.log = log;
        Catalog = new QuestCatalog(data.Excel);
        Progress = new ProgressCatalog(data.Excel, Catalog);
        Settings = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var loadedVersion = Settings.Version;
        Settings.Validate(Catalog);
        historyDirty = loadedVersion != Settings.Version;
        foreach (var player in Settings.PersistentPlayers())
            Engine.Track(player.Key);
        companyTracker = new CompanyTracker(this);
        window = new StoryLensWindow(this);
        try
        {
            commandRegistered = commands.AddHandler(Command, new CommandInfo(OnCommand)
                { HelpMessage = "Open Story Lens: quest markers and local player story clues." });
            if (!commandRegistered)
                throw new InvalidOperationException("Story Lens command is already registered.");
            tests = new WorkspaceTests(pluginInterface, framework, CaptureSmoke);
            framework.Update += Update;
            client.Logout += OnLogout;
            client.TerritoryChanged += OnZoneChanged;
            client.MapIdChanged += OnZoneChanged;
            client.InstanceChanged += OnZoneChanged;
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenMainUi += Open;
            pluginInterface.UiBuilder.OpenConfigUi += Open;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal string? AddPlayer(string name, uint world)
    {
        if (!WatchedPlayer.IsValidName(name) || !Catalog.Worlds.Any(w => w.Id == world))
            return "Enter a full character name and choose their home world.";
        var player = new WatchedPlayer(name.Trim(), world);
        if (Settings.Players.Any(p => p.Key == player.Key))
            return "This player is already on the list.";
        if (Settings.Players.Count >= InferenceEngine.MaximumTrackedPlayers)
            return "The list is limited to 32 players.";
        Settings.Players.Add(player);
        Engine.Track(player.Key);
        RememberTrackedPlayer(player.Key);
        Save();
        return null;
    }

    internal void RemovePlayer(WatchedPlayer player)
    {
        Settings.Players.Remove(player);
        if (!Settings.Company.Members.Any(m => m.Player.Key == player.Key))
        {
            Settings.LastSeen.Remove(player.Key);
            Engine.Untrack(player.Key);
        }
        Save();
    }

    internal Clue[] GetEvidence(string key) => Settings.LastSeen.TryGetValue(key, out var saved)
        ? saved.ResolveClues(Catalog.ObjectivesByKey, DateTimeOffset.UtcNow) : Engine.GetClues(key);

    internal LastSeenPoint? GetLastSeen(string key) => Settings.LastSeen.GetValueOrDefault(key) ??
        (SessionPlayers.TryGetValue(key, out var session)
            ? LastSeenPoint.Capture(session, Engine.GetClues(key)) : null);

    internal void ClearRecordedData(string key)
    {
        Engine.Remove(key);
        SessionPlayers.Remove(key);
        Seen.Remove(key);
        Settings.LastSeen.Remove(key);
        if (Settings.Company.Members.FirstOrDefault(m => m.Player.Key == key) is { } member)
        {
            member.Profile = new(); member.LocationId = 0; member.LocationSeenAt = default;
        }
        Save();
    }

    internal void MarkHistoryDirty() => historyDirty = true;

    internal void ReplaceCompanyMembers(List<CompanyMember> members)
    {
        var previous = Settings.PersistentPlayers().Select(p => p.Key).ToHashSet();
        Settings.Company.Members = members;
        var current = Settings.PersistentPlayers().Select(p => p.Key).ToHashSet();
        foreach (var key in previous.Except(current))
        {
            Engine.Untrack(key); Settings.LastSeen.Remove(key);
        }
        foreach (var key in current.Except(previous))
        {
            Engine.Track(key); RememberTrackedPlayer(key);
        }
        historyDirty = true;
    }

    internal ProgressEstimate Estimate(string key)
    {
        var member = Settings.Company.Members.FirstOrDefault(m => m.Player.Key == key);
        var point = GetLastSeen(key);
        var area = point is not null && (member is null || point.LastSeenAt > member.LocationSeenAt)
            ? point.Zone.TerritoryId : member?.LocationId ?? 0;
        return Progress.Story.Estimate(member?.Profile, GetEvidence(key),
            Progress.AreaQuests.TryGetValue(area, out var quest) ? quest : null, Catalog.TerritoryName(area), DateTimeOffset.UtcNow);
    }

    private void RememberTrackedPlayer(string key)
    {
        if (!SessionPlayers.TryGetValue(key, out var session))
            return;
        Settings.LastSeen[key] = LastSeenPoint.Capture(session, Engine.GetClues(key, session.LastSeenAt),
            Settings.LastSeen.GetValueOrDefault(key));
        historyDirty = true;
    }

    private void SaveHistoryIfDue(DateTimeOffset now)
    {
        if (historyDirty && now >= nextHistorySave)
            Save();
    }

    internal void Save()
    {
        nextHistorySave = DateTimeOffset.UtcNow.AddSeconds(30);
        try
        {
            Settings.Validate(Catalog);
            pluginInterface.SavePluginConfig(Settings);
            historyDirty = false;
            SaveError = null;
        }
        catch (Exception e)
        {
            historyDirty = true;
            SaveError = "Settings and last-seen points could not be saved. Retrying in 30 seconds, or choose Retry save.";
            log.Warning("Story Lens could not save settings ({ExceptionType}).", e.GetType().Name);
        }
    }

    private void Update(IFramework _)
    {
        lock (gate)
        {
            if (disposed)
                return;
            var now = DateTimeOffset.UtcNow;
            if (now < nextScan)
                return;
            nextScan = now.AddMilliseconds(250);
            SaveHistoryIfDue(now);
            try
            {
                var identityPlayer = client.IsLoggedIn ? objects.LocalPlayer : null;
                var localIdentity = identityPlayer is null ? null : new WatchedPlayer(identityPlayer.Name.TextValue, identityPlayer.HomeWorld.RowId);
                if (localIdentity is not null && (!WatchedPlayer.IsValidName(localIdentity.Name) || !Catalog.Worlds.Any(w => w.Id == localIdentity.HomeWorldId)))
                    localIdentity = null;
                var inDuty = condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95];
                companyTracker.Update(now, localIdentity, condition[ConditionFlag.InCombat], inDuty);
                if (!client.IsLoggedIn)
                {
                    Pause("Log in to view objectives and observe visible players. Last-seen data is retained.");
                    return;
                }
                if (client.IsPvP || client.IsGPosing || inDuty ||
                    condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
                    condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78] ||
                    condition[ConditionFlag.OccupiedInCutSceneEvent])
                {
                    Pause("Observation and markers pause in duties, PvP, zoning, GPose and your cutscenes.");
                    return;
                }
                var local = objects.LocalPlayer;
                if (local is null || !QuestObjective.IsFinite(local.Position))
                {
                    Pause("Waiting for the local player.");
                    return;
                }

                var zone = new Zone(client.TerritoryType, client.MapId, client.Instance);
                if (zone != CurrentZone)
                    Engine.Pause();
                CurrentZone = zone;
                LocalPosition = local.Position;
                WorldAvailable = true;
                Target = targets.Target is IPlayerCharacter target && target.EntityId != local.EntityId
                    ? new WatchedPlayer(target.Name.TextValue, target.HomeWorld.RowId) : null;
                var previouslySeen = Seen.Keys.ToArray();
                Seen.Clear();
                if (!Settings.ObservePlayers)
                {
                    Engine.Pause();
                    Status = "Automatic gauging paused. Quest markers remain available.";
                    return;
                }

                var objectives = Catalog.InTerritory(zone.TerritoryId);
                foreach (var player in objects.PlayerObjects.OfType<IPlayerCharacter>())
                {
                    if (player.EntityId == local.EntityId || player.EntityId is 0 or 0xE0000000)
                        continue;
                    var identity = new WatchedPlayer(player.Name.TextValue.Trim(), player.HomeWorld.RowId);
                    if (!WatchedPlayer.IsValidName(identity.Name) || identity.HomeWorldId == 0)
                        continue;
                    var key = identity.Key;
                    var sample = new PlayerSample(player.EntityId, zone, player.Position,
                        player.OnlineStatus.RowId == Catalog.CutsceneStatusId);
                    if (!QuestObjective.IsFinite(sample.Position))
                        continue;
                    Seen[key] = sample;
                    var evicted = Engine.Observe(key, sample, objectives, now);
                    if (evicted is not null)
                        SessionPlayers.Remove(evicted);
                    SessionPlayers[key] = new SessionPlayer(identity, now, sample);
                }
                foreach (var key in previouslySeen.Where(k => !Seen.ContainsKey(k)))
                    Engine.BreakContinuity(key);
                foreach (var player in Settings.PersistentPlayers(Settings.TrackFreeCompany).Where(p => Seen.ContainsKey(p.Key)))
                    RememberTrackedPlayer(player.Key);
                SaveHistoryIfDue(now);
                var trackedVisible = Settings.Players.Count(p => Seen.ContainsKey(p.Key));
                Status = $"Observing {Seen.Count} visible players ({trackedVisible} tracked). {SessionPlayers.Count} players remembered this session.";
                lastError = null;
            }
            catch (Exception e)
            {
                Pause("Observation failed; waiting for a valid game update.");
                var type = e.GetType().Name;
                if (lastError != type)
                    log.Warning("Story Lens observation paused ({ExceptionType}).", type);
                lastError = type;
            }
        }
    }

    private void Pause(string status, bool clearEvidence = false)
    {
        WorldAvailable = false;
        Target = null;
        Seen.Clear();
        if (clearEvidence)
        {
            Engine.Clear();
            SessionPlayers.Clear();
        }
        else
            Engine.Pause();
        Status = status;
    }

    private void OnLogout(int type, int code)
    {
        lock (gate)
        {
            if (historyDirty)
                Save();
            Pause("Logged out. Session observations and saved last-seen points are retained.");
        }
    }

    private void OnZoneChanged(uint value)
    {
        lock (gate)
            Pause("Waiting for the new area.");
    }

    private void OnCommand(string command, string arguments) => Open();
    private void Open()
    {
        lock (gate)
            window.IsOpen = true;
    }

    private void Draw()
    {
        lock (gate)
        {
            if (disposed)
                return;
            window.DrawMarkers(gameGui);
            window.Draw();
        }
    }

    private object CaptureSmoke()
    {
        lock (gate)
        {
            var catalogValid = Catalog.Objectives.Length > 0 && Catalog.Objectives.All(o => o.IsValid);
            var chainValid = Catalog.Chain.Quests.Count > 0 && Catalog.Objectives.All(o =>
                Catalog.Chain.Quests.ContainsKey(o.QuestId)) && Catalog.Chain.Quests.Values.All(q =>
                q.Id != 0 && !string.IsNullOrWhiteSpace(q.Name) && q.Duties.All(d =>
                    !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Type)));
            var observationsValid = SessionPlayers.Count <= InferenceEngine.MaximumSessionPlayers &&
                Engine.PlayerCount == SessionPlayers.Count && Seen.All(p =>
                    SessionPlayers.ContainsKey(p.Key) && QuestObjective.IsFinite(p.Value.Position) && p.Value.Zone == CurrentZone);
            var persistenceValid = Settings.LastSeen.All(p => Settings.PersistentPlayers().Any(w => w.Key == p.Key) && p.Value.IsValid());
            var progressValid = Progress.Story.Quests.Count > 900 && Progress.AchievementQuests.Count > 30 && Progress.Collections.Length >= 7;
            var serializationValid = ConfigurationRoundTripIsValid();
            var saveHealthy = SaveError is null;
            return new
            {
                ok = !disposed && catalogValid && chainValid && observationsValid && persistenceValid && serializationValid && saveHealthy && progressValid,
                catalogValid, chainValid, observationsValid, persistenceValid, serializationValid, saveHealthy,
                configurationVersion = Settings.Version,
                chainQuestCount = Catalog.Chain.Quests.Count,
                questDutyLinkCount = Catalog.Chain.Quests.Values.Sum(q => q.Duties.Length),
                objectiveCount = Catalog.Objectives.Length,
                mainScenarioQuestCount = Catalog.Objectives.Where(o => o.IsMainScenario).Select(o => o.QuestId).Distinct().Count(),
                currentAreaObjectiveCount = Catalog.InTerritory(CurrentZone.TerritoryId).Length,
                listedPlayerCount = Settings.Players.Count,
                visibleListedPlayerCount = Settings.Players.Count(p => Seen.ContainsKey(p.Key)),
                visiblePlayerCount = Seen.Count, sessionPlayerCount = SessionPlayers.Count,
                savedLastSeenCount = Settings.LastSeen.Count,
                progressValid, companyMemberCount = Settings.Company.Members.Count,
                cachedCompanyMemberCount = Settings.Company.CachedMemberCount,
                profilesChecked = Settings.Company.Members.Count(m => m.Profile.CheckedAt != default),
                profilesWithEvidence = Settings.Company.Members.Count(m => m.Profile.CompletedQuestIds.Count > 0),
                profilesWithLevels = Settings.Company.Members.Count(m => m.Profile.MaxCombatLevel.HasValue),
                cachedCompanyLocations = Settings.Company.Members.Count(m => m.LocationId > 0),
                companyRequestCount = companyTracker.Requests, companyFailureCount = companyTracker.Failures,
                companySweepPaused = companyTracker.PauseReason is not null, companySweepPauseReason = companyTracker.PauseReason,
                companyRequestIntervalSeconds = CompanyTracker.RequestIntervalSeconds,
                companyRetryDelayMinutes = CompanyState.RetryDelayMinutes,
                worldAvailable = WorldAvailable, observingEnabled = Settings.ObservePlayers,
            };
        }
    }

    private bool ConfigurationRoundTripIsValid()
    {
        try
        {
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
                Newtonsoft.Json.JsonConvert.SerializeObject(Settings));
            if (restored is null)
                return false;
            restored.Validate(Catalog);
            return Newtonsoft.Json.JsonConvert.SerializeObject(restored) == Newtonsoft.Json.JsonConvert.SerializeObject(Settings);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            if (historyDirty)
                Save();
            disposed = true;
            companyTracker?.Dispose();
            framework.Update -= Update;
            client.Logout -= OnLogout;
            client.TerritoryChanged -= OnZoneChanged;
            client.MapIdChanged -= OnZoneChanged;
            client.InstanceChanged -= OnZoneChanged;
            pluginInterface.UiBuilder.Draw -= Draw;
            pluginInterface.UiBuilder.OpenMainUi -= Open;
            pluginInterface.UiBuilder.OpenConfigUi -= Open;
            if (commandRegistered)
                commands.RemoveHandler(Command);
            tests?.Dispose();
            Pause("Plugin unloaded.", clearEvidence: true);
        }
    }
}
