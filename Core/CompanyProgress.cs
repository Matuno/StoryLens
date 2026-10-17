namespace StoryLens.Core;

public sealed class CompanyState
{
    public const int RetryDelayMinutes = 10;
    public static DateTimeOffset RetryDeadline(DateTimeOffset now, DateTimeOffset? serverRetryAt)
    {
        var fallback = now.AddMinutes(RetryDelayMinutes);
        return serverRetryAt is { } requested && requested > fallback ? requested : fallback;
    }

    public static string? SweepPauseReason(bool enabled, bool localAvailable, bool inCombat, bool inDuty) =>
        !enabled || !localAvailable ? "Free Company collection paused." :
        inDuty ? "Free Company sweep paused while in a duty; resumes automatically afterward." :
        inCombat ? "Free Company sweep paused during combat; resumes automatically afterward." : null;

    public string OwnerKey { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string Id { get; set; } = "";
    public DateTimeOffset RosterCheckedAt { get; set; }
    public DateTimeOffset NextRequestAt { get; set; }
    public int CachedMemberCount { get; set; }
    public List<CompanyMember> Members { get; set; } = [];
    public CompanyMember? NextStaleMember(DateTimeOffset now) => Members.Where(m => now >= m.Profile.NextCheckAt)
        .OrderByDescending(m => m.Profile.NextSection > 0).ThenBy(m => m.Profile.CheckedAt).FirstOrDefault();
}

public sealed class CompanyMember
{
    public WatchedPlayer Player { get; set; } = new("", 0);
    public string LodestoneId { get; set; } = "";
    public ProfileEvidence Profile { get; set; } = new();
    public uint LocationId { get; set; }
    public DateTimeOffset LocationSeenAt { get; set; }
}

public sealed class ProfileEvidence
{
    public int? MaxCombatLevel { get; set; }
    public DateTimeOffset JobsCheckedAt { get; set; }
    public List<uint> CompletedQuestIds { get; set; } = [];
    public List<string> Sources { get; set; } = [];
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public int NextSection { get; set; }
    public string Status { get; set; } = "Waiting for public profile check";
    public Dictionary<string, string> SectionStatus { get; set; } = [];

    public void Advance(DateTimeOffset now)
    {
        NextSection = (NextSection + 1) % 4;
        if (NextSection == 0) { CheckedAt = now; NextCheckAt = now.AddDays(3); }
    }
}

public sealed record StoryQuest(uint Id, string Name, int Stage, string StageName, int Order, int Level);
public sealed record ProgressEstimate(string Label, string Basis, string? Ceiling, bool Conflict, uint? QuestId);
public sealed record CollectionMilestone(string Kind, string Name, string IconHash, uint QuestId, uint ItemId, string Source);

public sealed class StoryProgress
{
    public IReadOnlyDictionary<uint, StoryQuest> Quests { get; }
    private readonly Dictionary<uint, int> indices;
    private readonly StoryQuest[] ordered;
    private readonly Dictionary<int, StoryQuest[]> stages;

    public StoryProgress(IEnumerable<StoryQuest> quests)
    {
        ordered = quests.OrderBy(q => q.Stage).ThenBy(q => q.Order).ThenBy(q => q.Id).ToArray();
        Quests = ordered.ToDictionary(q => q.Id);
        indices = ordered.Select((q, i) => (q.Id, i)).ToDictionary(p => p.Id, p => p.i);
        stages = ordered.GroupBy(q => q.Stage).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public string Label(uint id, bool completed)
    {
        var q = Quests[id];
        var stage = stages[q.Stage];
        var count = Array.FindIndex(stage, x => x.Id == id) + (completed ? 1 : 0);
        return $"{q.StageName} - {(int)Math.Floor(100d * count / stage.Length)}%";
    }

    public ProgressEstimate Estimate(ProfileEvidence? profile, IEnumerable<Clue> clues,
        uint? areaQuest, string? areaName, DateTimeOffset now)
    {
        var lower = profile?.CompletedQuestIds.Where(Quests.ContainsKey).OrderByDescending(id => indices[id]).Cast<uint?>().FirstOrDefault();
        var observation = clues.Where(c => c.Objective.IsMainScenario && Quests.ContainsKey(c.Objective.QuestId) &&
            c.Kind >= ClueKind.Lingered && c.MatchingQuestCount == 1).FirstOrDefault();
        var ceilingQuest = profile is { MaxCombatLevel: > 0 } && now - profile.JobsCheckedAt < TimeSpan.FromDays(3)
            ? ordered.FirstOrDefault(q => q.Level > profile.MaxCombatLevel) : null;
        var ceiling = ceilingQuest is null ? null : $"Level ceiling: before {ceilingQuest.Name} ({Label(ceilingQuest.Id, false)}), highest combat job {profile!.MaxCombatLevel} as of {profile.JobsCheckedAt:u}";
        var conflict = lower.HasValue && ceilingQuest is not null && indices[lower.Value] >= indices[ceilingQuest.Id];
        if (observation is not null && (!lower.HasValue || indices[observation.Objective.QuestId] > indices[lower.Value]))
            return new(Label(observation.Objective.QuestId, false) + " ~", $"Observed quest candidate: {observation.Objective.QuestName}; {observation.ObservedAt:u}", ceiling,
                conflict || ceilingQuest is not null && indices[observation.Objective.QuestId] >= indices[ceilingQuest.Id], observation.Objective.QuestId);
        if (areaQuest.HasValue && Quests.ContainsKey(areaQuest.Value) && (!lower.HasValue || indices[areaQuest.Value] > indices[lower.Value]))
            return new(Label(areaQuest.Value, false) + " ?", $"Area clue: {areaName}. Access/revisits do not identify the current quest." +
                (lower.HasValue ? $" Unlock floor: {Label(lower.Value, true)}." : ""), ceiling, false, areaQuest);
        if (lower.HasValue)
            return new(Label(lower.Value, true) + " +", $"At least through {Quests[lower.Value].Name}; public unlock/achievement evidence", ceiling, conflict, lower);
        if (areaQuest.HasValue && Quests.ContainsKey(areaQuest.Value))
            return new(Label(areaQuest.Value, false) + " ?", $"Area clue: {areaName}. Access/revisits do not identify the current quest.", ceiling, false, areaQuest);
        return new("Unknown", "No quest or progression unlock evidence yet.", ceiling, false, null);
    }
}
