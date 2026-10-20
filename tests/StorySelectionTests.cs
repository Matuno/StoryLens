using System.Numerics;
using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class StorySelectionTests
{
    [Fact]
    public void OpeningAPlayerAlwaysFollowsThemEvenWhenAlreadySelected()
    {
        var state = new StorySelection();
        state.SelectPlayer("fixture");
        Assert.Null(state.QuestId);
        state.ToggleQuest(20);
        Assert.Equal(20u, state.QuestId);
        state.SelectPlayer("fixture");
        Assert.Equal("fixture", state.PlayerKey);
        Assert.True(state.FollowingPlayer);
        Assert.Equal(21u, state.ResolveQuest(21));
        Assert.Null(state.QuestId);
        state.SelectPlayer("another-fixture");
        Assert.Equal("another-fixture", state.PlayerKey);
        state.ClearPlayer();
        Assert.Null(state.PlayerKey);
        Assert.Null(state.ResolveQuest(21));
    }

    [Fact]
    public void ClearingAQuestStopsAutomaticReselectionUntilExplicitResume()
    {
        var state = new StorySelection();
        state.TogglePlayer("fixture");
        Assert.Equal(12u, state.ResolveQuest(12));
        state.ClearQuest();
        Assert.Null(state.ResolveQuest(13));
        Assert.Null(state.QuestId);
        Assert.Equal("fixture", state.PlayerKey);
        state.FollowPlayer();
        Assert.Equal(13u, state.ResolveQuest(13));
        Assert.Null(state.QuestId);
    }

    [Fact]
    public void PreviewStaysPinnedAndClickingAgainOrClearingPlayerRemovesIt()
    {
        var state = new StorySelection();
        state.TogglePlayer("fixture");
        state.ToggleQuest(20);
        Assert.Equal(20u, state.ResolveQuest(21));
        state.ToggleQuest(20);
        Assert.Null(state.ResolveQuest(21));
        Assert.Null(state.QuestId);
        state.ToggleQuest(22);
        state.TogglePlayer("fixture");
        Assert.Null(state.PlayerKey);
        Assert.Null(state.ResolveQuest(23));
        state.TogglePlayer("another-fixture");
        Assert.Equal(30u, state.ResolveQuest(30));
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");

    [Fact]
    public void ChoosingACandidateMovesThePathAndAdjacentPreviewsKeepItPinned()
    {
        var state = new StorySelection();
        state.SelectPlayer("fixture");
        state.ToggleCandidate(20);
        Assert.Equal(20u, state.ResolveAnchor(10));
        Assert.Equal(20u, state.ResolveQuest(10));
        state.ToggleQuest(21);
        Assert.Equal(20u, state.ResolveAnchor(10));
        Assert.Equal(21u, state.ResolveQuest(10));
        state.ToggleCandidate(20);
        Assert.Null(state.QuestId);
        Assert.Null(state.CandidateId);
        Assert.Null(state.ResolveQuest(10));
        Assert.Equal(10u, state.ResolveAnchor(10));
        state.ToggleCandidate(30);
        state.FollowPlayer();
        Assert.Null(state.CandidateId);
        Assert.Equal(10u, state.ResolveQuest(10));
        state.ToggleCandidate(30);
        state.SelectPlayer("another");
        Assert.Null(state.CandidateId);
    }

    [Fact]
    public void CandidatesKeepSideQuestsOldCluesAndSharedStepsWithoutCountingRepeatedLocationsAsVotes()
    {
        var sideQuest = Evidence(0, Now) with { Objective = Objective(0) with { QuestId = 2, IsMainScenario = false } };
        var earlier = sideQuest with { Objective = sideQuest.Objective with { QuestId = 3 },
            Kind = ClueKind.EnteredCutscene, ObservedAt = Now.AddHours(-1) };
        var future = earlier with { Objective = earlier.Objective with { QuestId = 4 }, ObservedAt = Now.AddHours(1) };
        var candidates = QuestCandidate.Resolve([Evidence(1, Now, 2), Evidence(2, Now, 2),
            sideQuest, earlier, future], Live, Now);
        Assert.Equal(new uint[] { 1, 2, 3 }, candidates.Select(c => c.QuestId));
        Assert.True(candidates[0].Recent);
        Assert.True(candidates[0].Match.Ambiguous);
        Assert.Equal(new[] { 1, 2 }, candidates[0].Match.Steps);
        Assert.False(candidates[2].Recent);
        Assert.False(candidates[2].Match.NearbyNow);
    }

    private static QuestObjective Objective(int step, float x = 0) => new(1, "Quest", true,
        (uint)(step + 2), step, 0, 1, 1, new Vector3(x, 0, 0), "NPC");
    private static Clue Evidence(int step, DateTimeOffset time, int quests = 1) =>
        new(Objective(step), ClueKind.Lingered, time, 1, quests);
    private static readonly PlayerSample Live = new(1, new Zone(1, 1, 1), Vector3.Zero, false);

    [Fact]
    public void LatestStepReplacesOlderMatchWithoutInventingCompletion()
    {
        var match = ObjectiveMatch.Resolve(1, [Evidence(0, Now.AddMinutes(-1)), Evidence(2, Now)], Live, Now);
        Assert.Equal(new[] { 2 }, match.Steps);
        Assert.True(match.NearbyNow);
        Assert.False(match.Ambiguous);
        var absent = ObjectiveMatch.Resolve(1, [Evidence(2, Now)], null, Now);
        Assert.False(absent.NearbyNow);
        Assert.Equal(Now, absent.ObservedAt);
    }

    [Fact]
    public void SharedObjectivesRemainAmbiguousAndOldOrFutureCluesAreNotCurrent()
    {
        var match = ObjectiveMatch.Resolve(1, [Evidence(1, Now), Evidence(2, Now, 2)], Live, Now);
        Assert.Equal(new[] { 1, 2 }, match.Steps);
        Assert.True(match.Ambiguous);
        Assert.False(ObjectiveMatch.Resolve(1, [Evidence(1, Now.AddHours(-1))], Live, Now).NearbyNow);
        Assert.Empty(ObjectiveMatch.Resolve(1, [Evidence(1, Now.AddHours(1))], Live, Now).Steps);
        Assert.Empty(ObjectiveMatch.Resolve(2, [Evidence(1, Now)], Live, Now).Steps);
    }
}
