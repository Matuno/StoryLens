namespace StoryLens.Core;

public sealed class StorySelection
{
    public string? PlayerKey { get; private set; }
    public uint? QuestId { get; private set; }
    public uint? CandidateId { get; private set; }
    public bool FollowingPlayer { get; private set; }

    public void TogglePlayer(string key)
    {
        if (PlayerKey == key) { ClearPlayer(); return; }
        SelectPlayer(key);
    }

    public void SelectPlayer(string key)
    {
        PlayerKey = key;
        FollowPlayer();
    }

    public void ToggleQuest(uint id)
    {
        var deselect = !FollowingPlayer && QuestId == id;
        if (deselect) { ClearQuest(); return; }
        FollowingPlayer = false;
        QuestId = id;
    }

    public void ToggleCandidate(uint id)
    {
        if (CandidateId == id) { ClearQuest(); return; }
        CandidateId = QuestId = id;
        FollowingPlayer = false;
    }

    public uint? ResolveAnchor(uint? estimate) => CandidateId ?? estimate;
    public uint? ResolveQuest(uint? estimate) => FollowingPlayer ? estimate : QuestId;
    public void ClearQuest() { CandidateId = QuestId = null; FollowingPlayer = false; }
    public void ClearPlayer() { PlayerKey = null; ClearQuest(); }
    public void FollowPlayer() { CandidateId = QuestId = null; FollowingPlayer = PlayerKey is not null; }
}

public sealed record QuestCandidate(Clue Evidence, ObjectiveMatch Match, bool Recent)
{
    public uint QuestId => Evidence.Objective.QuestId;

    public static QuestCandidate[] Resolve(IEnumerable<Clue> evidence, PlayerSample? live, DateTimeOffset now) =>
        InferenceEngine.Rank(evidence.Where(c => c.ObservedAt <= now), now)
            .GroupBy(c => c.Objective.QuestId)
            .Select(g => new QuestCandidate(g.First(), ObjectiveMatch.Resolve(g.Key, g, live, now),
                now - g.First().ObservedAt < InferenceEngine.RecentWindow)).ToArray();
}

public sealed record ObjectiveMatch(int[] Steps, DateTimeOffset? ObservedAt, bool NearbyNow, bool Ambiguous)
{
    public static ObjectiveMatch Resolve(uint questId, IEnumerable<Clue> evidence, PlayerSample? live, DateTimeOffset now)
    {
        var clues = evidence.Where(c => c.Objective.QuestId == questId && c.ObservedAt <= now).ToArray();
        if (clues.Length == 0) return new([], null, false, false);
        var latest = clues.Max(c => c.ObservedAt);
        var matching = clues.Where(c => c.ObservedAt == latest).ToArray();
        var nearby = live is not null && now - latest < InferenceEngine.RecentWindow
            ? matching.Where(c => InferenceEngine.Matches(c.Objective, live)).ToArray() : [];
        var chosen = nearby.Length > 0 ? nearby : matching;
        var steps = chosen.Select(c => c.Objective.TodoIndex).Distinct().Order().ToArray();
        return new(steps, latest, nearby.Length > 0, steps.Length > 1 || chosen.Any(c => c.MatchingQuestCount > 1));
    }
}
