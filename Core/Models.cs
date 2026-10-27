using System.Numerics;
using System.Globalization;

namespace StoryLens.Core;

public sealed record WatchedPlayer(string Name, uint HomeWorldId)
{
    public string Key => MakeKey(Name, HomeWorldId);

    public static string MakeKey(string name, uint world) => string.Create(CultureInfo.InvariantCulture, $"{name.Trim().ToUpperInvariant()}@{world}");

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= 64 && name.Trim().Contains(' ') &&
        name.Trim().All(c => char.IsLetter(c) || c is ' ' or '\'' or '-');
}

public sealed record QuestObjective(uint QuestId, string QuestName, bool IsMainScenario,
    uint LevelId, int TodoIndex, byte CompleteSequence, uint TerritoryId, uint MapId,
    Vector3 Position, string NpcName)
{
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{QuestId}/{TodoIndex}/{LevelId}");
    public string Label => TodoIndex < 0 ? "Quest giver" : $"Objective {TodoIndex + 1}";
    public bool IsValid => QuestId != 0 && LevelId != 0 && TerritoryId != 0 && MapId != 0 &&
        !string.IsNullOrWhiteSpace(QuestName) && IsFinite(Position);

    public static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}

public readonly record struct Zone(uint TerritoryId, uint MapId, uint Instance);
public sealed record PlayerSample(uint EntityId, Zone Zone, Vector3 Position, bool InCutscene)
{
    public bool InCombat { get; init; }
}
// Existing numeric values are persisted in saved clues.
public enum ClueKind { Nearby = 0, Lingered = 1, CutscenePresent = 2, EnteredCutscene = 3, CombatPresent = 4, EnteredCombat = 5 }
public sealed record Clue(QuestObjective Objective, ClueKind Kind, DateTimeOffset ObservedAt,
    float Distance, int MatchingQuestCount)
{
    public static int Strength(ClueKind kind) => kind switch
    {
        ClueKind.EnteredCutscene => 4,
        ClueKind.CutscenePresent => 3,
        ClueKind.EnteredCombat => 2,
        ClueKind.Lingered or ClueKind.CombatPresent => 1,
        _ => 0,
    };

    public string Description => Kind switch
    {
        ClueKind.EnteredCutscene => "Viewing Cutscene status began nearby",
        ClueKind.CutscenePresent => "Viewing Cutscene status seen nearby (start unknown)",
        ClueKind.EnteredCombat => "Combat began nearby; cause unknown",
        ClueKind.CombatPresent => "Seen in combat nearby; cause unknown",
        ClueKind.Lingered => "Spent at least 8 seconds nearby",
        _ => "Seen near this objective",
    };
}
