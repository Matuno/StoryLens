using System.Globalization;
using System.Numerics;
using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("tr-TR")]
    [InlineData("fa-IR")]
    public void PersistedIdentifiersAndEnglishEndpointParsingIgnoreTheHostCulture(string locale)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var objective = new QuestObjective(1, "Localized quest", true, 100, -1, 0, 148, 4, Vector3.Zero, "");
            var key = "1/-1/100";
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            var clue = SavedClue.From(new(objective, ClueKind.Nearby, DateTimeOffset.UtcNow, 0, 1));
            Assert.Equal(key, objective.Key);
            Assert.Equal(key, clue.ObjectiveKey());
            Assert.Equal("EXAMPLE MEMBER@39", WatchedPlayer.MakeKey("Example Member", 39));
            const string roster = """
                <li class="btn__pager__current">Page 1 of 1</li>
                <a href="/lodestone/character/123/" class="entry__bg"><p class="entry__name">Example Member</p><p class="entry__world">Louisoix</p></a>
                """;
            Assert.Equal("123", LodestoneParser.FindCharacter(roster, "example member", "louisoix"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
