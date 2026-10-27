namespace StoryLens.Core;

public class ConfigurationData
{
    public int Version { get; set; } = 3;
    public List<WatchedPlayer> Players { get; set; } = [];
    public Dictionary<string, LastSeenPoint> LastSeen { get; set; } = [];
    public bool ObservePlayers { get; set; } = true;
    public bool ShowMarkers { get; set; } = true;
    public bool MainScenarioOnly { get; set; } = true;
    public bool TrackFreeCompany { get; set; } = true;
    public bool DebugMode { get; set; }
    public CompanyState Company { get; set; } = new();

    public IEnumerable<WatchedPlayer> PersistentPlayers(bool includeCompany = true) =>
        Players.Concat(includeCompany ? Company.Members.Select(m => m.Player) : []).DistinctBy(p => p.Key);

    public void Validate(IReadOnlySet<uint> worldIds)
    {
        if (Version is not (1 or 2 or 3) || Players is null || Players.Count > InferenceEngine.MaximumTrackedPlayers ||
            Players.Any(p => p is null || !WatchedPlayer.IsValidName(p.Name) || !worldIds.Contains(p.HomeWorldId)) ||
            Players.Select(p => p.Key).Distinct().Count() != Players.Count || LastSeen is null || Company is null ||
            Company.Id is null || Company.CharacterId is null || Company.OwnerKey is null || Company.OwnerKey.Length > 128 ||
            Company.CachedMemberCount is < 0 or > 512 ||
            Company.Members is null || Company.Members.Count > 512 ||
            Company.Members.Any(m => m is null || m.Player is null || !WatchedPlayer.IsValidName(m.Player.Name) || !worldIds.Contains(m.Player.HomeWorldId) ||
                !LodestoneParser.ValidId(m.LodestoneId) || m.Profile is null || m.Profile.CompletedQuestIds is null || m.Profile.CompletedQuestIds.Count > 2048 ||
                m.Profile.Sources is null || m.Profile.Sources.Count > 128 || m.Profile.Sources.Any(s => s is null || s.Length > 256) ||
                m.Profile.SectionStatus is null || m.Profile.SectionStatus.Count > 4 ||
                m.Profile.NextSection is < 0 or > 3 || m.Profile.MaxCombatLevel is < 1 or > 150) ||
            Company.Members.Select(m => m.Player.Key).Distinct().Count() != Company.Members.Count ||
            Company.Members.Select(m => m.LodestoneId).Distinct().Count() != Company.Members.Count ||
            (Company.Id.Length > 0 && !LodestoneParser.ValidId(Company.Id)) ||
            (Company.CharacterId.Length > 0 && !LodestoneParser.ValidId(Company.CharacterId)))
            throw new InvalidDataException("Story Lens configuration is invalid; preserve it for manual recovery.");
        var persistentKeys = PersistentPlayers().Select(p => p.Key).ToHashSet();
        if (LastSeen.Any(p => !persistentKeys.Contains(p.Key) || p.Value is null || !p.Value.IsValid()))
            throw new InvalidDataException("Story Lens configuration is invalid; preserve it for manual recovery.");
        Version = 3;
    }
}
