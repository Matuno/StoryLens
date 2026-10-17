using System.Net;
using System.Text.RegularExpressions;

namespace StoryLens.Core;

public sealed record RosterEntry(string Id, string Name, string World);
public sealed record RosterPage(RosterEntry[] Members, int CurrentPage, int LastPage);

public static class LodestoneParser
{
    private const RegexOptions Flags = RegexOptions.Singleline | RegexOptions.CultureInvariant;
    private static MatchCollection Matches(string html, string pattern) => Regex.Matches(html, pattern, Flags, TimeSpan.FromSeconds(1));
    private static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "", Flags, TimeSpan.FromSeconds(1))).Trim();
    public static bool ValidId(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit) && id.Any(c => c != '0');

    public static string MainContent(string html)
    {
        if (html.Length > 4_000_000) throw new InvalidDataException("Lodestone page too large");
        // Sidebars contain unrelated characters and unlocks. Never parse them as profile evidence.
        var end = html.IndexOf("<div class=\"ldst__side\"", StringComparison.Ordinal);
        return end < 0 ? html : html[..end];
    }

    public static RosterPage Roster(string html)
    {
        html = MainContent(html);
        var members = new List<RosterEntry>();
        foreach (Match m in Matches(html, "<a href=\"/lodestone/character/(\\d+)/\" class=\"entry__(?:bg|link)\">(.*?)</a>"))
        {
            var body = m.Groups[2].Value;
            var name = Matches(body, "<p class=\"entry__name\">(.*?)</p>").Cast<Match>().FirstOrDefault();
            var world = Matches(body, "<p class=\"entry__world\">(.*?)</p>").Cast<Match>().FirstOrDefault();
            if (name is null || world is null) continue;
            members.Add(new(m.Groups[1].Value, Text(name.Groups[1].Value), Text(world.Groups[1].Value).Split('[')[0].Trim()));
        }
        if (members.Count == 0 || members.Count > 50) throw new InvalidDataException("Roster markup unavailable");
        var pagination = Matches(html, "btn__pager__current\">Page (\\d+) of (\\d+)</li>").Cast<Match>().FirstOrDefault();
        if (pagination is null) throw new InvalidDataException("Roster pagination unavailable");
        var current = int.Parse(pagination.Groups[1].Value);
        var pages = int.Parse(pagination.Groups[2].Value);
        if (pages is < 1 or > 20 || current < 1 || current > pages) throw new InvalidDataException("Invalid roster pagination");
        return new(members.DistinctBy(m => m.Id).ToArray(), current, pages);
    }

    public static string FindCharacter(string html, string name, string world)
    {
        var entries = Roster(html).Members.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && m.World.Equals(world, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Character search was not an exact match");
        return entries[0].Id;
    }

    public static string CompanyId(string html)
    {
        var section = Matches(MainContent(html), "<div class=\"character__freecompany__name\">(.*?)</div>").Cast<Match>().FirstOrDefault()?.Groups[1].Value ?? "";
        return Matches(section, "/lodestone/freecompany/(\\d+)/").Cast<Match>().FirstOrDefault()?.Groups[1].Value ?? "";
    }

    public static void VerifyCharacter(string html, string id)
    {
        if (!ValidId(id) || !MainContent(html).Contains($"/lodestone/character/{id}/", StringComparison.Ordinal) ||
            !html.Contains("frame__chara__name", StringComparison.Ordinal))
            throw new InvalidDataException("Character page unavailable");
    }

    public static bool IsPrivateCharacterPage(int status, string html, string id)
    {
        if (status != 403) return false;
        try { VerifyCharacter(html, id); return true; }
        catch (InvalidDataException) { return false; }
    }

    public static int MaxCombatLevel(string html)
    {
        html = MainContent(html);
        var start = html.IndexOf(">DoW/DoM</h3>", StringComparison.Ordinal);
        var end = html.IndexOf(">DoH/DoL</h3>", StringComparison.Ordinal);
        if (start < 0 || end <= start) throw new InvalidDataException("Complete combat job section unavailable");
        var jobs = Matches(html[start..end], "<li[^>]*>(.*?)</li>").Cast<Match>().Where(m => m.Value.Contains("character__job__level")).ToArray();
        if (jobs.Length < 20) throw new InvalidDataException("Incomplete combat job list");
        var levels = new List<int>();
        foreach (var job in jobs)
        {
            if (job.Value.Contains("Blue Mage", StringComparison.Ordinal)) continue;
            var level = Matches(job.Value, "character__job__level\">(.*?)</div>").Cast<Match>().Single();
            var value = Text(level.Groups[1].Value);
            if (value == "-") continue;
            if (!int.TryParse(value, out var number) || number is < 1 or > 150) throw new InvalidDataException("Invalid combat level");
            levels.Add(number);
        }
        return levels.Count > 0 ? levels.Max() : throw new InvalidDataException("No combat job level available");
    }

    public static HashSet<string> CollectionIcons(string html, string kind)
    {
        html = MainContent(html);
        if (!html.Contains($"js__{kind}_list", StringComparison.Ordinal)) throw new InvalidDataException("Collection private or unavailable");
        return Matches(html, $"<li class=\"{kind}__list_icon\".*?itemicon/[a-f0-9]{{2}}/([a-f0-9]{{40}})\\.png").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    public static uint[] Achievements(string html)
    {
        html = MainContent(html);
        if (!html.Contains("achievement__category", StringComparison.Ordinal) || !html.Contains("data-achieved=", StringComparison.Ordinal))
            throw new InvalidDataException("Achievements private or unavailable");
        return Matches(html, "<li class=\"entry\"\\s+data-achieved=\"1\"[^>]*>\\s*<a href=\"/lodestone/character/\\d+/achievement/detail/(\\d+)/\"").Cast<Match>()
            .Select(m => uint.Parse(m.Groups[1].Value)).Distinct().ToArray();
    }
}
