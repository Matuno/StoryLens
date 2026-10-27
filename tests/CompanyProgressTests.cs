using System.Numerics;
using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class CompanyProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly StoryProgress Story = new([
        new(1, "Start", 0, "ShB", 1, 70), new(2, "Middle", 0, "ShB", 2, 75), new(3, "Finish", 0, "ShB", 3, 80),
        new(4, "Patch start", 1, "ShB patches", 1, 80), new(5, "Patch end", 1, "ShB patches", 2, 80)]);

    [Fact]
    public void PercentCountsCompletedQuestsAndSeparatesPatches()
    {
        Assert.Equal("ShB - 0%", Story.Label(1, false));
        Assert.Equal("ShB - 66%", Story.Label(3, false));
        Assert.Equal("ShB - 100%", Story.Label(3, true));
        Assert.Equal("ShB patches - 0%", Story.Label(4, false));
    }

    [Fact]
    public void ProfileFloorSurvivesOldQuestReplayAndMissingUnlocks()
    {
        var profile = new ProfileEvidence { CompletedQuestIds = [3] };
        var clue = new Clue(new(1, "Start", true, 1, 0, 1, 1, 1, Vector3.Zero, ""), ClueKind.EnteredCutscene, Now, 0, 1);
        Assert.Equal("ShB - 100% +", Story.Estimate(profile, [clue], null, null, Now).Label);
        Assert.Equal("Unknown", Story.Estimate(new(), [], null, null, Now).Label);
    }

    [Fact]
    public void MaxLevelOnlyProvidesCeilingAndStaleLevelsDoNotRestrictNewEvidence()
    {
        var p = new ProfileEvidence { MaxCombatLevel = 72, JobsCheckedAt = Now };
        var estimate = Story.Estimate(p, [], null, null, Now);
        Assert.Equal("Unknown", estimate.Label);
        Assert.Contains("Middle", estimate.Ceiling);
        p.CompletedQuestIds.Add(3);
        Assert.True(Story.Estimate(p, [], null, null, Now).Conflict);
        Assert.Null(Story.Estimate(p, [], null, null, Now.AddDays(4)).Ceiling);
    }

    [Theory]
    [InlineData(ClueKind.CombatPresent)]
    [InlineData(ClueKind.EnteredCombat)]
    public void UnattributedCombatDoesNotEstablishAnMsqProgressPercentage(ClueKind kind)
    {
        var clue = new Clue(new(1, "Start", true, 1, 0, 1, 1, 1, Vector3.Zero, ""), kind, Now, 0, 1);
        Assert.Equal("Unknown", Story.Estimate(null, [clue], null, null, Now).Label);
    }

    [Fact]
    public void LocationCanAdvanceAnEstimateButIsNeverACompletionFloor()
    {
        var p = new ProfileEvidence { CompletedQuestIds = [1] };
        var estimate = Story.Estimate(p, [], 4, "The Ocular", Now);
        Assert.EndsWith("?", estimate.Label);
        Assert.Contains("Area clue", estimate.Basis);
        Assert.Contains("Unlock floor", estimate.Basis);
    }

    [Fact]
    public void AreaCluesDoNotHideContradictoryLevelEvidence()
    {
        var profile = new ProfileEvidence { MaxCombatLevel = 72, JobsCheckedAt = Now, CompletedQuestIds = [3] };
        var estimate = Story.Estimate(profile, [], 4, "Later area", Now);
        Assert.True(estimate.Conflict);
        Assert.EndsWith("?", estimate.Label);
    }

    [Fact]
    public void FutureObservationsAndLevelsDoNotEstablishCurrentProgress()
    {
        var profile = new ProfileEvidence { MaxCombatLevel = 72, JobsCheckedAt = Now.AddMinutes(1) };
        var clue = new Clue(new(2, "Middle", true, 1, 0, 1, 1, 1, Vector3.Zero, ""), ClueKind.EnteredCutscene, Now.AddMinutes(1), 0, 1);
        var estimate = Story.Estimate(profile, [clue], null, null, Now);
        Assert.Equal("Unknown", estimate.Label);
        Assert.Null(estimate.Ceiling);
    }

    [Fact]
    public void QueueFinishesPartialMemberAndOnlyRevisitsStaleProfiles()
    {
        var a = new CompanyMember { Profile = new() { NextSection = 1 } };
        var b = new CompanyMember();
        var company = new CompanyState { Members = [b, a] };
        Assert.Same(a, company.NextStaleMember(Now));
        for (var i = 0; i < 3; i++) a.Profile.Advance(Now);
        Assert.Equal(Now.AddDays(3), a.Profile.NextCheckAt);
        Assert.Same(b, company.NextStaleMember(Now));
        for (var i = 0; i < 4; i++) b.Profile.Advance(Now);
        Assert.Null(company.NextStaleMember(Now.AddDays(2)));
        Assert.NotNull(company.NextStaleMember(Now.AddDays(3)));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void SweepOnlyRunsOutsideCombatAndDuties(bool inCombat, bool inDuty, bool canRun)
    {
        Assert.Equal(canRun, CompanyState.SweepPauseReason(true, true, inCombat, inDuty) is null);
        Assert.NotNull(CompanyState.SweepPauseReason(false, true, inCombat, inDuty));
        Assert.NotNull(CompanyState.SweepPauseReason(true, false, inCombat, inDuty));
    }

    [Fact]
    public void RetryWaitsTenMinutesUnlessTheServerRequestsLonger()
    {
        Assert.Equal(Now.AddMinutes(10), CompanyState.RetryDeadline(Now, null));
        Assert.Equal(Now.AddMinutes(10), CompanyState.RetryDeadline(Now, Now.AddMinutes(-5)));
        Assert.Equal(Now.AddMinutes(10), CompanyState.RetryDeadline(Now, Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(45), CompanyState.RetryDeadline(Now, Now.AddMinutes(45)));
    }

    [Fact]
    public void VersionTwoMigratesAndCompanyMembershipWhitelistsPersistence()
    {
        var p = new WatchedPlayer("Example Member", 39);
        var data = new ConfigurationData { Version = 2, Company = new() { Members = [new() { Player = p, LodestoneId = "123" }] } };
        data.Validate(new HashSet<uint> { 39 });
        Assert.Equal(3, data.Version);
        Assert.Equal(p, Assert.Single(data.PersistentPlayers()));
        data.Company.Members.Add(new() { Player = p, LodestoneId = "456" });
        Assert.Throws<InvalidDataException>(() => data.Validate(new HashSet<uint> { 39 }));
    }

    [Fact]
    public void CompanyPinsDoNotConsumeTheManualTrackingLimit()
    {
        var engine = new InferenceEngine();
        for (var i = 0; i < 544; i++) engine.Track($"player{i}");
        Assert.Throws<InvalidOperationException>(() => engine.Track("extra"));
    }

    [Fact]
    public void CompanyPauseStopsNewSavedObservationsWithoutDeletingRetainedMembers()
    {
        var manual = new WatchedPlayer("Manual Member", 39);
        var company = new WatchedPlayer("Company Member", 39);
        var data = new ConfigurationData { Players = [manual], Company = new() { Members = [new() { Player = company, LodestoneId = "123" }] } };
        Assert.Equal(2, data.PersistentPlayers().Count());
        Assert.Equal(manual, Assert.Single(data.PersistentPlayers(false)));
        Assert.Single(data.Company.Members);
    }
}
