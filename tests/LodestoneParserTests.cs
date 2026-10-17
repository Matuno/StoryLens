using StoryLens.Core;
using Xunit;

namespace StoryLens.Tests;

public sealed class LodestoneParserTests
{
    [Fact]
    public void RosterReadsEveryPageAndExcludesSidebarCharacters()
    {
        const string entry = """<a href="/lodestone/character/123/" class="entry__bg"><p class="entry__name">Example O&#39;member</p><p class="entry__world"><i> </i>Louisoix [Chaos]</p></a>""";
        var page = LodestoneParser.Roster("<li class=\"btn__pager__current\">Page 1 of 11</li>" + entry + "<div class=\"ldst__side\">" + entry.Replace("123", "456"));
        Assert.Equal(11, page.LastPage);
        var member = Assert.Single(page.Members);
        Assert.Equal("Example O'member", member.Name);
        Assert.Equal("Louisoix", member.World);
        Assert.Throws<InvalidDataException>(() => LodestoneParser.Roster(entry));
    }

    [Fact]
    public void CollectionsRequirePublicMarkupAndIgnoreUnrelatedImages()
    {
        const string hash = "edcd84ac3929f91a6f05d35f2aa7fdc44ce8fca1";
        var html = $"<ul class=\"js__mount_list\"><li class=\"mount__list_icon\" data-tooltip_href=\"tooltip\"><img src=\"https://lds-img.finalfantasyxiv.com/itemicon/ed/{hash}.png?n7.57\"></li></ul>";
        Assert.Equal(hash, Assert.Single(LodestoneParser.CollectionIcons(html, "mount")));
        Assert.Throws<InvalidDataException>(() => LodestoneParser.CollectionIcons("This profile is private", "mount"));
        Assert.Empty(LodestoneParser.CollectionIcons("<ul class=\"js__mount_list\"></ul><div class=\"ldst__side\">" + html, "mount"));
    }

    [Fact]
    public void AchievementCatalogueDoesNotTreatUnachievedEntriesAsOwned()
    {
        const string html = """
            <nav class="achievement__category"></nav>
            <li class="entry" data-achieved="1" data-reward_type=""><a href="/lodestone/character/123/achievement/detail/3496/" class="entry__achievement--complete"></a></li>
            <li class="entry" data-achieved="0"><a href="/lodestone/character/123/achievement/detail/3943/"></a></li>
            """;
        Assert.Equal(3496u, Assert.Single(LodestoneParser.Achievements(html)));
        Assert.Throws<InvalidDataException>(() => LodestoneParser.Achievements("Private achievements"));
    }

    [Fact]
    public void JobsExcludeCraftingAndLimitedJobsAndRejectPartialSections()
    {
        string Job(string name, string level) => $"<li><div class=\"character__job__level\">{level}</div><div>{name}</div></li>";
        var html = ">DoW/DoM</h3>" + string.Concat(Enumerable.Range(0, 20).Select(i => Job("Combat", i == 1 ? "73" : "-"))) +
            Job("Blue Mage", "100") + ">DoH/DoL</h3>" + Job("Carpenter", "100");
        Assert.Equal(73, LodestoneParser.MaxCombatLevel(html));
        Assert.Throws<InvalidDataException>(() => LodestoneParser.MaxCombatLevel(">DoW/DoM</h3>" + Job("Combat", "73") + ">DoH/DoL</h3>"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("123?foo=bar")]
    public void IdsCannotEscapeTheFixedLodestoneEndpoint(string id) => Assert.False(LodestoneParser.ValidId(id));

    [Fact]
    public void PrivateAchievement403IsDistinguishedFromRateLimitsAndGenericBlocks()
    {
        const string profile = "<p class=\"frame__chara__name\">Example Member</p><a href=\"/lodestone/character/123/\">Character</a>";
        Assert.True(LodestoneParser.IsPrivateCharacterPage(403, profile, "123"));
        Assert.False(LodestoneParser.IsPrivateCharacterPage(429, profile, "123"));
        Assert.False(LodestoneParser.IsPrivateCharacterPage(403, "Request blocked", "123"));
        Assert.False(LodestoneParser.IsPrivateCharacterPage(403, profile, "456"));
        Assert.Throws<InvalidDataException>(() => LodestoneParser.Achievements(profile));
    }
}
