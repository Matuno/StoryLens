using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using StoryLens.Core;

namespace StoryLens;

internal sealed class ProgressCatalog
{
    public StoryProgress Story { get; }
    public IReadOnlyDictionary<uint, uint> AchievementQuests { get; }
    public IReadOnlyDictionary<uint, uint> AreaQuests { get; }
    public CollectionMilestone[] Collections { get; } =
    [
        new("mount", "Magitek Armor", "edcd84ac3929f91a6f05d35f2aa7fdc44ce8fca1", 70058, 6008, "df9c3673696"),
        new("mount", "Black Chocobo", "2e0e69537289e0fece83b6fc1db06c5ad0da2f9c", 67133, 12075, "25495c7bf13"),
        new("mount", "Manacutter", "87f3fa899909fff94c5981a1c9869e262bcb18f8", 67170, 12073, "bcde91bd8b9"),
        new("minion", "Wind-up G'raha Tia", "04376f271cf6c501c22007d774b0a7345e376a25", 69190, 23992, "50dda3800b0"),
        new("mount", "Argos", "cf1371705111e6b72be95b480bf364dacdfec024", 70000, 36002, "e5c76ee0e98"),
        new("minion", "Wind-up Herois", "32da5210bbe8c47c8751122e363d6c3f3fdb33d6", 70000, 35989, "0d329f1d16c"),
        new("mount", "Alpaca", "96363213d4302b248b08df720b7e59a9c690ee4b", 70495, 43592, "f0dc2bcabea"),
    ];

    public ProgressCatalog(ExcelModule excel, QuestCatalog catalog)
    {
        var quests = excel.GetSheet<Quest>(Language.English)!;
        var stages = new Dictionary<uint, (int Stage, string Name)>
        {
            [1] = (0, "ARR"), [2] = (1, "ARR patches"), [3] = (2, "HW"),
            [4] = (3, "HW patches"), [5] = (3, "HW patches"), [6] = (4, "SB"),
            [7] = (5, "SB patches"), [8] = (6, "ShB"), [9] = (7, "ShB patches"),
            [10] = (7, "ShB patches"), [11] = (8, "EW"), [12] = (9, "EW patches"),
            [13] = (10, "DT"), [14] = (11, "DT patches"), [15] = (11, "DT patches"),
        };
        Story = new StoryProgress(quests.Where(q => q.JournalGenre.ValueNullable?.Icon == 61412 &&
                !q.Id.IsEmpty && stages.ContainsKey(q.JournalGenre.RowId))
            .Select(q => new StoryQuest(q.RowId, q.Name.ExtractText(), stages[q.JournalGenre.RowId].Stage,
                stages[q.JournalGenre.RowId].Name, (int)q.JournalGenre.RowId * 10000 + q.SortKey, q.ClassJobLevel[0])));
        AchievementQuests = excel.GetSheet<Achievement>(Language.English)!
            .Where(a => a.Description.ExtractText().StartsWith("Complete the main scenario quest “", StringComparison.Ordinal) && Story.Quests.ContainsKey(a.Key.RowId))
            .ToDictionary(a => a.RowId, a => a.Key.RowId);
        AreaQuests = catalog.Objectives.Where(o => Story.Quests.ContainsKey(o.QuestId)).GroupBy(o => o.TerritoryId)
            .ToDictionary(g => g.Key, g => g.Select(o => Story.Quests[o.QuestId]).OrderBy(q => q.Stage).ThenBy(q => q.Order).First().Id);
        foreach (var rule in Collections)
            if (!Story.Quests.ContainsKey(rule.QuestId) || !quests.GetRow(rule.QuestId).Reward.Any(r => r.RowId == rule.ItemId) ||
                !excel.GetSheet<Item>()!.GetRow(rule.ItemId).IsUntradable)
                throw new InvalidDataException("Collection milestone no longer matches quest reward data.");
    }
}
