using System.Numerics;

namespace StoryLens.Core;

public sealed class InferenceEngine
{
    public const int MaximumTrackedPlayers = 32;
    public const int MaximumSessionPlayers = 4096;
    public const int MaximumCluesPerPlayer = 128;
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(20);
    public const float MatchRadius = 6f;
    public const float HeightTolerance = 4f;

    private sealed class State
    {
        public PlayerSample? Previous;
        public DateTimeOffset PreviousAt;
        public Dictionary<string, DateTimeOffset> NearSince = [];
        public Dictionary<string, Clue> Clues = [];
    }

    private readonly Dictionary<string, State> players = [];
    private readonly HashSet<string> tracked = [];
    public int PlayerCount => players.Count;

    public static bool Matches(QuestObjective objective, PlayerSample sample) => objective.IsValid &&
        QuestObjective.IsFinite(sample.Position) && objective.TerritoryId == sample.Zone.TerritoryId &&
        objective.MapId == sample.Zone.MapId &&
        Math.Abs(objective.Position.Y - sample.Position.Y) <= HeightTolerance &&
        Vector2.Distance(new(objective.Position.X, objective.Position.Z),
            new(sample.Position.X, sample.Position.Z)) <= MatchRadius;

    public string? Observe(string playerKey, PlayerSample sample, IEnumerable<QuestObjective> objectives, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(playerKey) || sample.EntityId is 0 or 0xE0000000 ||
            !QuestObjective.IsFinite(sample.Position))
        {
            BreakContinuity(playerKey);
            return null;
        }

        string? evicted = null;
        if (!players.TryGetValue(playerKey, out var state))
        {
            if (players.Count >= MaximumSessionPlayers)
            {
                // shortcut: retain 4096 session players; evict the oldest untracked entry when the cache fills.
                evicted = players.Where(p => !tracked.Contains(p.Key)).MinBy(p => p.Value.PreviousAt).Key;
                players.Remove(evicted);
            }
            players[playerKey] = state = new State();
        }

        var previous = state.Previous;
        var continuous = previous is not null && previous.Zone == sample.Zone &&
            previous.EntityId == sample.EntityId && now >= state.PreviousAt &&
            now - state.PreviousAt <= TimeSpan.FromSeconds(1);
        if (!continuous)
            state.NearSince.Clear();

        // An already-set status after streaming/zoning is not an observed cutscene start.
        var entered = continuous && !previous!.InCutscene && sample.InCutscene &&
            Vector3.Distance(previous.Position, sample.Position) <= 3f;
        var matches = objectives.Where(o => Matches(o, sample)).DistinctBy(o => o.Key).ToArray();
        var questCount = matches.Select(o => o.QuestId).Distinct().Count();
        var matchingKeys = matches.Select(o => o.Key).ToHashSet();
        foreach (var key in state.NearSince.Keys.Where(k => !matchingKeys.Contains(k)).ToArray())
            state.NearSince.Remove(key);

        foreach (var objective in matches)
        {
            if (!state.NearSince.TryGetValue(objective.Key, out var since))
                state.NearSince[objective.Key] = since = now;
            var kind = entered ? ClueKind.EnteredCutscene : sample.InCutscene ? ClueKind.CutscenePresent :
                now - since >= TimeSpan.FromSeconds(8) ? ClueKind.Lingered : ClueKind.Nearby;
            if (!state.Clues.TryGetValue(objective.Key, out var old) || kind >= old.Kind || now - old.ObservedAt >= RecentWindow)
                state.Clues[objective.Key] = new Clue(objective, kind, now,
                    Vector3.Distance(objective.Position, sample.Position), questCount);
        }

        if (state.Clues.Count > MaximumCluesPerPlayer)
            state.Clues = Rank(state.Clues.Values, now).Take(MaximumCluesPerPlayer).ToDictionary(c => c.Objective.Key);
        state.Previous = sample;
        state.PreviousAt = now;
        return evicted;
    }

    public Clue[] GetClues(string playerKey, DateTimeOffset? now = null) => players.TryGetValue(playerKey, out var state)
        ? Rank(state.Clues.Values, now ?? DateTimeOffset.UtcNow) : [];

    public static Clue[] Rank(IEnumerable<Clue> clues, DateTimeOffset now) => clues
        .OrderByDescending(c => c.ObservedAt <= now && now - c.ObservedAt < RecentWindow)
        .ThenByDescending(c => c.Kind).ThenByDescending(c => c.ObservedAt).ToArray();

    public void Track(string playerKey)
    {
        if (!tracked.Contains(playerKey) && tracked.Count >= MaximumTrackedPlayers + 512)
            throw new InvalidOperationException("The tracked player limit has been reached.");
        tracked.Add(playerKey);
    }

    public void Untrack(string playerKey) => tracked.Remove(playerKey);

    public void BreakContinuity(string playerKey)
    {
        if (!players.TryGetValue(playerKey, out var state))
            return;
        state.Previous = null;
        state.NearSince.Clear();
    }

    public void Pause()
    {
        foreach (var key in players.Keys)
            BreakContinuity(key);
    }

    public void Remove(string playerKey) => players.Remove(playerKey);
    public void Clear()
    {
        players.Clear();
        tracked.Clear();
    }
}
