using System.Numerics;

namespace StoryLens.Core;

public sealed record WatchedPlayer(string Name, uint HomeWorldId)
{
    public string Key => MakeKey(Name, HomeWorldId);

    public static string MakeKey(string name, uint world) => $"{name.Trim().ToUpperInvariant()}@{world}";

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= 64 && name.Trim().Contains(' ') &&
        name.Trim().All(c => char.IsLetter(c) || c is ' ' or '\'' or '-');
}

public sealed record QuestObjective(uint QuestId, string QuestName, bool IsMainScenario,
    uint LevelId, int TodoIndex, byte CompleteSequence, uint TerritoryId, uint MapId,
    Vector3 Position, string NpcName)
{
    public string Key => $"{QuestId}/{TodoIndex}/{LevelId}";
    public string Label => TodoIndex < 0 ? "Quest giver" : $"Objective {TodoIndex + 1}";
    public bool IsValid => QuestId != 0 && LevelId != 0 && TerritoryId != 0 && MapId != 0 &&
        !string.IsNullOrWhiteSpace(QuestName) && IsFinite(Position);

    public static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}

public readonly record struct Zone(uint TerritoryId, uint MapId, uint Instance);
public sealed record PlayerSample(uint EntityId, Zone Zone, Vector3 Position, bool InCutscene);
public enum ClueKind { Nearby, Lingered, CutscenePresent, EnteredCutscene }
public sealed record Clue(QuestObjective Objective, ClueKind Kind, DateTimeOffset ObservedAt,
    float Distance, int MatchingQuestCount)
{
    public string Description => Kind switch
    {
        ClueKind.EnteredCutscene => "Observed entering a cutscene nearby",
        ClueKind.CutscenePresent => "Seen in a cutscene nearby (start unknown)",
        ClueKind.Lingered => "Spent at least 8 seconds nearby",
        _ => "Seen near this objective",
    };
}
