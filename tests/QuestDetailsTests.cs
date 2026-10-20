using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class QuestDetailsTests
{
    [Fact]
    public void MarkerNumbersMatchTheTimelineIncludingAcceptanceAndTextOnlySteps()
    {
        var detail = new QuestDetails("", [new(-1, "Accept", []), new(0, "Speak", []), new(1, "Wait", []),
            new(2, "Return", []), new(3, "Enter", []), new(4, "Finish", [])], true);
        Assert.Equal(1, detail.StepNumber(-1));
        Assert.Equal("1/6", detail.ProgressLabel([-1]));
        Assert.Equal(3, detail.StepNumber(1));
        Assert.Equal("3/6", detail.ProgressLabel([1]));
        Assert.Equal("1/6, 3/6, 6/6", detail.ProgressLabel([4, -1, 1, 1]));
        Assert.Equal("", detail.ProgressLabel([99]));
    }

    [Fact]
    public void SparseOrMissingMetadataDoesNotInventSteps()
    {
        var detail = new QuestDetails("", [new(-1, "Accept", []), new(2, "Objective", [])], false);
        Assert.Equal(2, detail.StepNumber(2));
        Assert.Equal("2/2", detail.ProgressLabel([2]));
        Assert.Equal("", new QuestDetails("", [], false).ProgressLabel([-1, 0]));
    }
}
