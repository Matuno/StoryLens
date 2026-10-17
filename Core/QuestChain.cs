namespace StoryLens.Core;

[Flags]
public enum DutyRelation { Referenced = 1, Unlock = 2, Prerequisite = 4 }

public sealed record QuestDuty(string Key, string Name, string Type, DutyRelation Relations)
{
    public string Relationship => string.Join(", ", new[]
    {
        Relations.HasFlag(DutyRelation.Referenced) ? "Referenced by quest" : null,
        Relations.HasFlag(DutyRelation.Unlock) ? "Unlock link" : null,
        Relations.HasFlag(DutyRelation.Prerequisite) ? "Prerequisite duty link" : null,
    }.OfType<string>());
}

public sealed record QuestInfo(uint Id, string Name, bool IsMainScenario,
    uint[] PreviousQuestIds, QuestDuty[] Duties);

public enum ChainStop { Limit, End, Branch, MissingLink, Cycle }
public sealed record QuestChainPath(QuestInfo[] Quests, QuestInfo[] Branches, ChainStop Stop);

public sealed class QuestChain
{
    public IReadOnlyDictionary<uint, QuestInfo> Quests { get; }
    private readonly Dictionary<uint, uint[]> next;

    public QuestChain(IEnumerable<QuestInfo> quests)
    {
        Quests = quests.ToDictionary(q => q.Id);
        next = Quests.Values.SelectMany(q => q.PreviousQuestIds.Distinct().Select(p => (Previous: p, Next: q.Id)))
            .GroupBy(p => p.Previous).ToDictionary(g => g.Key, g => g.Select(p => p.Next).ToArray());
    }

    public QuestChainPath Previous(uint questId) => Walk(questId, forward: false, 3);
    public QuestChainPath Upcoming(uint questId) => Walk(questId, forward: true, 5);

    private QuestChainPath Walk(uint questId, bool forward, int count)
    {
        var path = new List<QuestInfo>();
        if (!Quests.TryGetValue(questId, out var current))
            return new([], [], ChainStop.MissingLink);
        var visited = new HashSet<uint> { questId };
        for (var i = 0; i < count; i++)
        {
            var ids = (forward ? next.GetValueOrDefault(current.Id) ?? [] : current.PreviousQuestIds)
                .Where(id => id != 0).Distinct().ToArray();
            if (ids.Any(id => !Quests.ContainsKey(id)))
                return new(path.ToArray(), [], ChainStop.MissingLink);
            // MSQ prerequisites also contain unlock side quests; those are not the next story chapter.
            var choices = ids.Select(id => Quests[id]).Where(q => q.IsMainScenario == current.IsMainScenario)
                .OrderBy(q => q.Name).ThenBy(q => q.Id).ToArray();
            if (choices.Length == 0)
                return new(path.ToArray(), [], ChainStop.End);
            if (choices.Length > 1)
                return new(path.ToArray(), choices, ChainStop.Branch);
            current = choices[0];
            if (!visited.Add(current.Id))
                return new(path.ToArray(), [], ChainStop.Cycle);
            path.Add(current);
        }
        return new(path.ToArray(), [], ChainStop.Limit);
    }

    public static bool IsNumberedSymbol(string symbol, string prefix) =>
        symbol.StartsWith(prefix, StringComparison.Ordinal) && symbol.Length > prefix.Length &&
        symbol[prefix.Length..].All(char.IsAsciiDigit);
}
