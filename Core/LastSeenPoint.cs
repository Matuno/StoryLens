using System.Numerics;

namespace StoryLens.Core;

public sealed record SessionPlayer(WatchedPlayer Player, DateTimeOffset LastSeenAt, PlayerSample Sample);

public sealed record SavedClue(uint QuestId, uint LevelId, int TodoIndex, ClueKind Kind,
    DateTimeOffset ObservedAt, float Distance, int MatchingQuestCount)
{
    public string ObjectiveKey() => $"{QuestId}/{TodoIndex}/{LevelId}";
    public static SavedClue From(Clue clue) => new(clue.Objective.QuestId, clue.Objective.LevelId,
        clue.Objective.TodoIndex, clue.Kind, clue.ObservedAt, clue.Distance, clue.MatchingQuestCount);
}

public sealed record LastSeenPoint(DateTimeOffset LastSeenAt, Zone Zone, Vector3 Position, SavedClue[] Clues)
{
    public static LastSeenPoint Capture(SessionPlayer player, IEnumerable<Clue> clues, LastSeenPoint? previous = null)
    {
        var merged = clues.Select(SavedClue.From).Concat(previous?.Clues ?? [])
            .Where(c => c.ObservedAt <= player.LastSeenAt)
            .OrderByDescending(c => player.LastSeenAt - c.ObservedAt < InferenceEngine.RecentWindow)
            .ThenByDescending(c => Clue.Strength(c.Kind)).ThenByDescending(c => c.ObservedAt)
            .DistinctBy(c => c.ObjectiveKey())
            .Take(InferenceEngine.MaximumCluesPerPlayer).ToArray();
        return new(player.LastSeenAt, player.Sample.Zone, player.Sample.Position, merged);
    }

    public Clue[] ResolveClues(IReadOnlyDictionary<string, QuestObjective> objectives, DateTimeOffset now) =>
        InferenceEngine.Rank(Clues.Where(c => objectives.ContainsKey(c.ObjectiveKey()))
            .Select(c => new Clue(objectives[c.ObjectiveKey()], c.Kind, c.ObservedAt, c.Distance, c.MatchingQuestCount)), now);

    public bool IsValid() => LastSeenAt != default && Zone.TerritoryId != 0 && Zone.MapId != 0 &&
        QuestObjective.IsFinite(Position) && Clues is not null && Clues.Length <= InferenceEngine.MaximumCluesPerPlayer &&
        Clues.All(c => c is not null && c.QuestId != 0 && c.LevelId != 0 && c.TodoIndex is >= -1 and < 24 &&
            Enum.IsDefined(c.Kind) && c.ObservedAt != default && c.ObservedAt <= LastSeenAt &&
            float.IsFinite(c.Distance) && c.Distance >= 0 && c.MatchingQuestCount > 0) &&
        Clues.Select(c => c.ObjectiveKey()).Distinct().Count() == Clues.Length;
}
