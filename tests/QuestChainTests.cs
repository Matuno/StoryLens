using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class QuestChainTests
{
    private static QuestInfo Quest(uint id, bool msq = true, params uint[] previous) =>
        new(id, $"Quest {id}", msq, previous, []);

    [Fact]
    public void LimitsContextToThreePreviousAndFiveUpcomingQuests()
    {
        var chain = new QuestChain(Enumerable.Range(1, 12).Select(i =>
            Quest((uint)i, true, i == 1 ? [] : [(uint)i - 1])));
        Assert.Equal(new uint[] { 5, 4, 3 }, chain.Previous(6).Quests.Select(q => q.Id));
        Assert.Equal(new uint[] { 7, 8, 9, 10, 11 }, chain.Upcoming(6).Quests.Select(q => q.Id));
        Assert.Equal(ChainStop.Limit, chain.Upcoming(6).Stop);
    }

    [Fact]
    public void MsqWalkIgnoresSideBranchesAndSidePrerequisites()
    {
        var chain = new QuestChain([Quest(1), Quest(2, false), Quest(3, true, 1, 2),
            Quest(4, false, 3), Quest(5, true, 3), Quest(6, false, 5)]);
        Assert.Equal(1u, Assert.Single(chain.Previous(3).Quests).Id);
        Assert.Equal(5u, Assert.Single(chain.Upcoming(3).Quests).Id);
        Assert.Empty(chain.Upcoming(5).Quests);
    }

    [Fact]
    public void StopsAtForwardForkWithoutChoosingAnArbitraryBranch()
    {
        var chain = new QuestChain([Quest(1), Quest(2, true, 1), Quest(3, true, 2), Quest(4, true, 2)]);
        var result = chain.Upcoming(1);
        Assert.Equal(2u, Assert.Single(result.Quests).Id);
        Assert.Equal(new uint[] { 3, 4 }, result.Branches.Select(q => q.Id));
        Assert.Equal(ChainStop.Branch, result.Stop);
    }

    [Fact]
    public void PreviousForkKeepsAllPrerequisiteAlternatives()
    {
        var chain = new QuestChain([Quest(1), Quest(2), Quest(3, true, 1, 2)]);
        var result = chain.Previous(3);
        Assert.Empty(result.Quests);
        Assert.Equal(2, result.Branches.Length);
        Assert.Equal(ChainStop.Branch, result.Stop);
    }

    [Fact]
    public void MissingLinksDoNotInventContinuity()
    {
        var chain = new QuestChain([Quest(1), Quest(2, true, 1, 999)]);
        Assert.Equal(ChainStop.MissingLink, chain.Previous(2).Stop);
        Assert.Empty(chain.Previous(2).Quests);
        Assert.Equal(ChainStop.MissingLink, chain.Upcoming(999).Stop);
        Assert.Equal(ChainStop.End, chain.Previous(1).Stop);
    }

    [Fact]
    public void CyclesAndDuplicateEdgesCannotRepeatQuests()
    {
        var chain = new QuestChain([Quest(1, true, 2), Quest(2, true, 1, 1)]);
        Assert.Equal(2u, Assert.Single(chain.Upcoming(1).Quests).Id);
        Assert.Equal(ChainStop.Cycle, chain.Upcoming(1).Stop);
        Assert.Equal(ChainStop.Cycle, chain.Previous(1).Stop);
    }

    [Theory]
    [InlineData("INSTANCEDUNGEON0", "INSTANCEDUNGEON", true)]
    [InlineData("INSTANCEDUNGEON11", "INSTANCEDUNGEON", true)]
    [InlineData("QUESTBATTLE0", "QUESTBATTLE", true)]
    [InlineData("UNLOCK_IMAGE_DUNGEON", "INSTANCEDUNGEON", false)]
    [InlineData("UNLOCK_ADD_NEW_CONTENT_TO_CF", "INSTANCEDUNGEON", false)]
    [InlineData("INSTANCEDUNGEON", "INSTANCEDUNGEON", false)]
    [InlineData("QUESTBATTLE_EXTRA", "QUESTBATTLE", false)]
    public void ReadsOnlyKnownTypedDutySymbols(string symbol, string prefix, bool expected) =>
        Assert.Equal(expected, QuestChain.IsNumberedSymbol(symbol, prefix));

    [Fact]
    public void CombinedDutyRelationsRemainExplicit()
    {
        var duty = new QuestDuty("duty/1", "Example dungeon", "Dungeon", DutyRelation.Referenced | DutyRelation.Unlock);
        Assert.Equal("Referenced by quest, Unlock link", duty.Relationship);
        Assert.Equal("Prerequisite duty link", (duty with { Relations = DutyRelation.Prerequisite }).Relationship);
    }
}
