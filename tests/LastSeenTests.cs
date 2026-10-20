using System.Numerics;
using System.Text.Json;
using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class LastSeenTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WatchedPlayer Member = new("Example Member", 39);
    private static readonly QuestObjective Objective = new(1, "Example quest", true, 100, 0, 1, 148, 4,
        Vector3.Zero, "Example NPC");
    private static readonly PlayerSample Sample = new(123, new Zone(148, 4, 1), new Vector3(1, 2, 3), false);
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    private static LastSeenPoint Point() => LastSeenPoint.Capture(new SessionPlayer(Member, Start, Sample),
        [new Clue(Objective, ClueKind.EnteredCutscene, Start, 2f, 1)]);

    [Fact]
    public void ExistingConfigurationMigratesWithoutLosingPlayersOrPreferences()
    {
        var settings = JsonSerializer.Deserialize<ConfigurationData>("""
            {"Version":1,"Players":[{"Name":"Example Member","HomeWorldId":39}],"ShowMarkers":false}
            """)!;
        settings.Validate(new HashSet<uint> { 39 });
        Assert.Equal(3, settings.Version);
        Assert.Equal(Member, Assert.Single(settings.Players));
        Assert.False(settings.ShowMarkers);
        Assert.Empty(settings.LastSeen);
    }

    [Fact]
    public void LastSeenRoundTripsPositionTimeAndCluesWithoutRuntimeActorState()
    {
        var settings = new ConfigurationData { Players = [Member], LastSeen = new() { [Member.Key] = Point() } };
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.DoesNotContain("EntityId", json);
        Assert.DoesNotContain("InCutscene", json);
        var restored = JsonSerializer.Deserialize<ConfigurationData>(json, JsonOptions)!;
        restored.Validate(new HashSet<uint> { 39 });
        var point = restored.LastSeen[Member.Key];
        Assert.Equal(Start, point.LastSeenAt);
        Assert.Equal(Sample.Position, point.Position);
        Assert.Equal(Sample.Zone, point.Zone);
        var clue = Assert.Single(point.ResolveClues(new Dictionary<string, QuestObjective> { [Objective.Key] = Objective }, Start));
        Assert.Equal(ClueKind.EnteredCutscene, clue.Kind);
        Assert.Equal(Start, clue.ObservedAt);
        Assert.Empty(new InferenceEngine().GetClues(Member.Key));
    }

    [Fact]
    public void PersistenceRejectsAnyUntrackedPlayerRecord()
    {
        var settings = new ConfigurationData { LastSeen = new() { [Member.Key] = Point() } };
        Assert.Throws<InvalidDataException>(() => settings.Validate(new HashSet<uint> { 39 }));
        settings.Players.Add(Member);
        settings.Validate(new HashSet<uint> { 39 });
        settings.Players.Clear();
        Assert.Throws<InvalidDataException>(() => settings.Validate(new HashSet<uint> { 39 }));
    }

    [Fact]
    public void SeeingNoNewObjectiveKeepsTheLastKnownEstimateAtItsOriginalTime()
    {
        var session = new SessionPlayer(Member, Start.AddDays(1), Sample with { Position = new Vector3(9, 8, 7) });
        var point = LastSeenPoint.Capture(session, [], Point());
        Assert.Equal(Start.AddDays(1), point.LastSeenAt);
        Assert.Equal(Start, Assert.Single(point.Clues).ObservedAt);
        Assert.Equal(session.Sample.Position, point.Position);
        Assert.True(point.IsValid());
    }

    [Fact]
    public void MissingCatalogEntriesAreRetainedOnDiskButNotPresentedAsCurrentQuestData()
    {
        var point = Point();
        Assert.Empty(point.ResolveClues(new Dictionary<string, QuestObjective>(), Start));
        Assert.Single(point.Clues);
    }

    [Theory]
    [InlineData(1, ClueKind.EnteredCutscene)]
    [InlineData(21, ClueKind.Nearby)]
    public void FreshProximityAfterReloadPreservesRecentStrongerEvidenceUntilItAgesOut(int minutes, ClueKind expected)
    {
        var now = Start.AddMinutes(minutes);
        var session = new SessionPlayer(Member, now, Sample);
        var clue = new Clue(Objective, ClueKind.Nearby, now, 1, 1);
        var point = LastSeenPoint.Capture(session, [clue], Point());
        var saved = Assert.Single(point.Clues);
        Assert.Equal(expected, saved.Kind);
        Assert.Equal(minutes == 1 ? Start : now, saved.ObservedAt);
    }

    [Fact]
    public void InvalidPersistedPointsAreRejected()
    {
        Assert.False((Point() with { Position = new Vector3(float.NaN, 0, 0) }).IsValid());
        Assert.False((Point() with { LastSeenAt = default }).IsValid());
        Assert.False((Point() with { Clues = [Point().Clues[0] with { Kind = (ClueKind)99 }] }).IsValid());
        Assert.False((Point() with { Clues = [Point().Clues[0] with { ObservedAt = Start.AddMinutes(1) }] }).IsValid());
        var settings = new ConfigurationData { Version = 999, Players = [Member] };
        Assert.Throws<InvalidDataException>(() => settings.Validate(new HashSet<uint> { 39 }));
        Assert.Equal(999, settings.Version);
    }
}
