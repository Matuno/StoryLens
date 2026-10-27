using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StoryLens.Core;

public sealed record RosterEntry(string Id, string Name, string World);
public sealed record RosterPage(RosterEntry[] Members, int CurrentPage, int LastPage);
public sealed record LodestonePage(string Id = "", RosterPage? Roster = null, int? MaxCombatLevel = null,
    string[]? Icons = null, uint[]? Achievements = null, bool Unavailable = false);

public static class LodestoneParser
{
    private const RegexOptions Flags = RegexOptions.Singleline | RegexOptions.CultureInvariant;
    private static MatchCollection Matches(string html, string pattern) => Regex.Matches(html, pattern, Flags, TimeSpan.FromSeconds(1));
    private static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "", Flags, TimeSpan.FromSeconds(1))).Trim();
    public static bool ValidId(string? id) => id is { Length: > 0 and <= 20 } && id.All(char.IsAsciiDigit) && id.Any(c => c != '0');

    // Parse on the HTTP worker; only these managed results are applied on the framework thread.
    public static LodestonePage ParsePage(string html, string kind, string id, string name, string world)
    {
        if (kind == "search") return new(Id: FindCharacter(html, name, world));
        if (kind == "roster")
        {
            if (!ValidId(id) || !MainContent(html).Contains($"/lodestone/freecompany/{id}/", StringComparison.Ordinal))
                throw new InvalidDataException("Wrong Free Company page");
            return new(Roster: Roster(html));
        }
        VerifyCharacter(html, id);
        if (kind == "company")
        {
            var company = CompanyId(html);
            if (company.Length > 0 && !ValidId(company)) throw new InvalidDataException("Invalid company link");
            return new(Id: company);
        }
        try
        {
            return kind switch
            {
                "class_job" => new(MaxCombatLevel: MaxCombatLevel(html)),
                "mount" or "minion" => new(Icons: CollectionIcons(html, kind).ToArray()),
                "achievement/category/76" => new(Achievements: Achievements(html)),
                _ => throw new InvalidOperationException("Unknown profile section"),
            };
        }
        catch (InvalidDataException) { return new(Unavailable: true); }
    }

    public static void ValidateRoster(IReadOnlyCollection<RosterEntry> members)
    {
        if (members.Count is 0 or > 512 || members.Any(m => !ValidId(m.Id) ||
                !WatchedPlayer.IsValidName(m.Name) || string.IsNullOrWhiteSpace(m.World)) ||
            members.Select(m => m.Id).Distinct().Count() != members.Count ||
            members.Select(m => $"{m.Name}@{m.World}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
            throw new InvalidDataException("Invalid or changing company roster");
    }

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
            if (name is null || world is null) throw new InvalidDataException("Incomplete roster entry");
            members.Add(new(m.Groups[1].Value, Text(name.Groups[1].Value), Text(world.Groups[1].Value).Split('[')[0].Trim()));
        }
        if (members.Count == 0 || members.Count > 50) throw new InvalidDataException("Roster markup unavailable");
        var pagination = Matches(html, "btn__pager__current\">Page (\\d+) of (\\d+)</li>").Cast<Match>().FirstOrDefault();
        if (pagination is null) throw new InvalidDataException("Roster pagination unavailable");
        var current = int.Parse(pagination.Groups[1].Value, CultureInfo.InvariantCulture);
        var pages = int.Parse(pagination.Groups[2].Value, CultureInfo.InvariantCulture);
        if (pages is < 1 or > 20 || current < 1 || current > pages) throw new InvalidDataException("Invalid roster pagination");
        ValidateRoster(members);
        return new(members.ToArray(), current, pages);
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
        var frames = Matches(MainContent(html), "<a\\b[^>]*class=\"frame__chara__link(?: [^\"]*)?\"[^>]*>(.*?)</a>");
        if (!ValidId(id) || frames.Count != 1 || !frames[0].Value.Contains($"href=\"/lodestone/character/{id}/\"", StringComparison.Ordinal) ||
            !frames[0].Value.Contains("frame__chara__name", StringComparison.Ordinal))
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
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 150) throw new InvalidDataException("Invalid combat level");
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
            .Select(m => uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToArray();
    }
}
