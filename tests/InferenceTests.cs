using System.Numerics;
using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class InferenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Zone Area = new(148, 4, 1);
    private static QuestObjective Objective(uint quest = 1, int step = 0) =>
        new(quest, $"Quest {quest}", quest == 1, 100, step, 1, 148, 4, Vector3.Zero, "Example NPC");
    private static PlayerSample Sample(bool cutscene = false, Vector3? position = null) =>
        new(123, Area, position ?? Vector3.Zero, cutscene);

    [Theory]
    [InlineData(6, 4, 0, true)]
    [InlineData(6.01f, 0, 0, false)]
    [InlineData(0, 4.01f, 0, false)]
    [InlineData(5, 0, 5, false)]
    [InlineData(0, -4, 0, true)]
    public void UsesHorizontalRadiusAndHeight(float x, float y, float z, bool expected) =>
        Assert.Equal(expected, InferenceEngine.Matches(Objective(), Sample(position: new(x, y, z))));

    [Fact]
    public void RejectsWrongMapTerritoryAndNonFinitePositions()
    {
        Assert.False(InferenceEngine.Matches(Objective(), Sample() with { Zone = Area with { MapId = 5 } }));
        Assert.False(InferenceEngine.Matches(Objective(), Sample() with { Zone = Area with { TerritoryId = 150 } }));
        Assert.False(InferenceEngine.Matches(Objective(), Sample(position: new(float.NaN, 0, 0))));
        Assert.False(InferenceEngine.Matches(Objective() with { Position = new(0, float.PositiveInfinity, 0) }, Sample()));
    }

    [Fact]
    public void ObservedCutsceneTransitionRanksAboveNearbyWithoutRepeatedTickInflation()
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(), [Objective()], Start);
        engine.Observe("member", Sample(true), [Objective()], Start.AddMilliseconds(250));
        for (var i = 2; i < 100; i++)
            engine.Observe("member", Sample(true), [Objective()], Start.AddMilliseconds(i * 250));
        var clue = Assert.Single(engine.GetClues("member"));
        Assert.Equal(ClueKind.EnteredCutscene, clue.Kind);
        Assert.Equal(Start.AddMilliseconds(250), clue.ObservedAt);
        Assert.Equal(1, clue.MatchingQuestCount);
    }

    [Fact]
    public void FirstSeenInCutsceneIsNotATransition()
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(true), [Objective()], Start);
        Assert.Equal(ClueKind.CutscenePresent, Assert.Single(engine.GetClues("member")).Kind);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("missing")]
    [InlineData("instance")]
    [InlineData("respawn")]
    [InlineData("pause")]
    [InlineData("clock")]
    [InlineData("jump")]
    public void BrokenContinuityCannotInventCutsceneEntry(string reason)
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(), [Objective()], Start);
        var sample = Sample(true);
        var time = Start.AddMilliseconds(250);
        switch (reason)
        {
            case "gap": time = Start.AddSeconds(2); break;
            case "missing": engine.BreakContinuity("member"); break;
            case "instance": sample = sample with { Zone = Area with { Instance = 2 } }; break;
            case "respawn": sample = sample with { EntityId = 124 }; break;
            case "pause": engine.Pause(); break;
            case "clock": time = Start.AddMilliseconds(-1); break;
            case "jump": sample = sample with { Position = new(5, 0, 0) }; break;
        }
        engine.Observe("member", sample, [Objective()], time);
        Assert.Equal(ClueKind.CutscenePresent, Assert.Single(engine.GetClues("member")).Kind);
    }

    [Fact]
    public void CountsSideQuestsForAmbiguityAndKeepsRepeatedStepsAmbiguous()
    {
        var engine = new InferenceEngine();
        QuestObjective[] objectives = [Objective(), Objective(step: 1), Objective(2)];
        engine.Observe("member", Sample(), objectives, Start);
        engine.Observe("member", Sample(true), objectives, Start.AddMilliseconds(250));
        var clues = engine.GetClues("member");
        Assert.Equal(3, clues.Length);
        Assert.All(clues, c => Assert.Equal(2, c.MatchingQuestCount));
        Assert.Equal(2, clues.Count(c => c.Objective.QuestId == 1));
    }

    [Fact]
    public void LingerRequiresContinuousPresenceAndResetsAfterLeaving()
    {
        var engine = new InferenceEngine();
        for (var second = 0; second <= 8; second++)
            engine.Observe("member", Sample(), [Objective()], Start.AddSeconds(second));
        Assert.Equal(ClueKind.Lingered, Assert.Single(engine.GetClues("member")).Kind);
        engine.Remove("member");
        engine.Observe("member", Sample(), [Objective()], Start);
        engine.Observe("member", Sample(position: new(20, 0, 0)), [Objective()], Start.AddSeconds(1));
        engine.Observe("member", Sample(), [Objective()], Start.AddSeconds(2));
        Assert.Equal(ClueKind.Nearby, Assert.Single(engine.GetClues("member")).Kind);
    }

    [Fact]
    public void RetainsSessionEvidenceUntilExplicitlyCleared()
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(true), [Objective()], Start);
        engine.BreakContinuity("member");
        engine.Pause();
        Assert.Single(engine.GetClues("member", Start.AddDays(1)));
        engine.Observe("member", Sample(), [Objective()], Start.AddMinutes(21));
        engine.Remove("member");
        Assert.Empty(engine.GetClues("member"));
        engine.Observe("member", Sample(), [Objective()], Start.AddMinutes(22));
        engine.Clear();
        Assert.Empty(engine.GetClues("member"));
    }

    [Fact]
    public void CapsPlayersAndCluesAndIgnoresInvalidActors()
    {
        var engine = new InferenceEngine();
        var objectives = Enumerable.Range(1, 200).Select(i => Objective((uint)i)).ToArray();
        engine.Track("tracked");
        engine.Observe("tracked", Sample(), objectives, Start);
        string? evicted = null;
        for (var i = 0; i < InferenceEngine.MaximumSessionPlayers; i++)
            evicted = engine.Observe($"member{i}", Sample(), [], Start.AddSeconds(i));
        Assert.Equal(128, engine.GetClues("tracked").Length);
        Assert.Equal(InferenceEngine.MaximumSessionPlayers, engine.PlayerCount);
        Assert.Equal("member0", evicted);
        engine.Clear();
        engine.Observe("member", Sample() with { EntityId = 0xE0000000 }, objectives, Start);
        Assert.Empty(engine.GetClues("member"));
    }

    [Fact]
    public void RecentEvidenceOutranksAnOlderStrongerClueWithoutDeletingIt()
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(), [Objective()], Start);
        engine.Observe("member", Sample(true), [Objective()], Start.AddMilliseconds(250));
        engine.Observe("member", Sample(), [Objective(2)], Start.AddHours(1));
        var clues = engine.GetClues("member", Start.AddHours(1));
        Assert.Equal(2, clues.Length);
        Assert.Equal(2u, clues[0].Objective.QuestId);
        Assert.Equal(ClueKind.EnteredCutscene, clues[1].Kind);
    }

    [Fact]
    public void TrackingAnAlreadyObservedPlayerPreservesTheirEvidence()
    {
        var engine = new InferenceEngine();
        engine.Observe("member", Sample(true), [Objective()], Start);
        engine.Track("member");
        Assert.Equal(ClueKind.CutscenePresent, Assert.Single(engine.GetClues("member")).Kind);
        engine.Untrack("member");
        Assert.Single(engine.GetClues("member"));
    }

    [Fact]
    public void IdentitiesIgnoreCaseButRespectHomeWorld()
    {
        Assert.Equal(WatchedPlayer.MakeKey(" Example Member ", 1), WatchedPlayer.MakeKey("example member", 1));
        Assert.NotEqual(WatchedPlayer.MakeKey("Example Member", 1), WatchedPlayer.MakeKey("Example Member", 2));
        Assert.True(WatchedPlayer.IsValidName("Example O'Name"));
        Assert.False(WatchedPlayer.IsValidName("Example\nMember"));
        Assert.False(WatchedPlayer.IsValidName(""));
    }
}
