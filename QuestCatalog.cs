using System.Numerics;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using StoryLens.Core;

namespace StoryLens;

internal sealed class QuestCatalog
{
    public QuestObjective[] Objectives { get; }
    public IReadOnlyDictionary<string, QuestObjective> ObjectivesByKey { get; }
    public IReadOnlyDictionary<uint, QuestObjective[]> ByTerritory { get; }
    public QuestObjective[] Quests { get; }
    public QuestChain Chain { get; }
    public (uint Id, string Name)[] Worlds { get; }
    public uint CutsceneStatusId { get; }
    private readonly Dictionary<uint, string> territoryNames;
    private readonly ExcelModule excel;
    private readonly Dictionary<uint, QuestDetails> details = [];
    private readonly Dictionary<uint, QuestObjective[]> byQuest;

    public QuestCatalog(ExcelModule excel)
    {
        this.excel = excel;
        var npcs = excel.GetSheet<ENpcResident>()!;
        territoryNames = excel.GetSheet<TerritoryType>()!.ToDictionary(t => t.RowId,
            t => t.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
        var instances = excel.GetSheet<InstanceContent>()!;
        var soloDuties = excel.GetSheet<QuestBattleResident>()!;
        var questBattles = excel.GetSheet<QuestBattle>()!;
        var unlocks = excel.GetSheet<ContentFinderCondition>()!
            .SelectMany(d => new[] { (Type: d.UnlockType, Id: d.UnlockCriteria.RowId, Duty: d),
                (Type: d.UnlockType2, Id: d.UnlockCriteria2.RowId, Duty: d) })
            .Where(d => d.Type == 1 && d.Id != 0).ToLookup(d => d.Id, d => d.Duty);
        var objectives = new List<QuestObjective>();
        var questInfo = new List<QuestInfo>();
        foreach (var quest in excel.GetSheet<Quest>()!)
        {
            var name = quest.Name.ExtractText().Trim();
            if (quest.RowId == 0 || name.Length == 0 || quest.Id.IsEmpty)
                continue;
            var msq = quest.JournalGenre.ValueNullable?.Icon == 61412;
            var duties = new Dictionary<string, QuestDuty>();
            AddInstance(quest.InstanceContentUnlock.RowId, DutyRelation.Unlock);
            foreach (var instance in quest.InstanceContent)
                AddInstance(instance.RowId, DutyRelation.Prerequisite);
            foreach (var duty in unlocks[quest.RowId])
                AddDuty(duty, DutyRelation.Unlock);
            foreach (var parameter in quest.QuestParams)
            {
                if (parameter.ScriptArg == 0)
                    continue;
                var symbol = parameter.ScriptInstruction.ExtractText();
                if (QuestChain.IsNumberedSymbol(symbol, "INSTANCEDUNGEON"))
                    AddInstance(parameter.ScriptArg, DutyRelation.Referenced);
                else if (QuestChain.IsNumberedSymbol(symbol, "QUESTBATTLE"))
                {
                    // New solo duties use InstanceContent; older ones resolve through QuestBattleResident.
                    if (parameter.ScriptArg >= 5000)
                        AddInstance(parameter.ScriptArg, DutyRelation.Referenced);
                    else if (soloDuties.TryGetRow(parameter.ScriptArg, out var solo) && solo.SoloDuty.RowId != 0)
                        AddDuty(solo.SoloDuty.ValueNullable, DutyRelation.Referenced);
                    else if (questBattles.TryGetRow(parameter.ScriptArg, out _))
                        duties.TryAdd($"solo/{parameter.ScriptArg}", new QuestDuty($"solo/{parameter.ScriptArg}",
                            "Solo encounter", "Solo duty", DutyRelation.Referenced)
                        {
                            IconId = excel.GetSheet<ContentType>()!.GetRow(7).Icon,
                        });
                }
            }
            questInfo.Add(new QuestInfo(quest.RowId, name, msq,
                quest.PreviousQuest.Select(p => p.RowId).Where(id => id != 0).Distinct().ToArray(),
                duties.Values.OrderBy(d => d.Name).ToArray())
            {
                IconId = quest.EventIconType.ValueNullable is { NpcIconAvailable: > 0 } icon
                    ? icon.NpcIconAvailable + (icon.IconRange > 1 ? 1u : 0u)
                    : excel.GetSheet<EventIconType>()!.GetRow(msq ? 3u : 1u).NpcIconAvailable + 1,
                IsUnlock = quest.EventIconType.RowId is 8 or 10,
                Level = quest.ClassJobLevel[0],
                TextSheet = $"quest/{(quest.RowId & 0xFFFF) / 100:000}/{quest.Id.ExtractText()}",
                HasCutsceneReference = quest.QuestParams.Any(p => p.ScriptArg != 0 &&
                    QuestChain.IsNumberedSymbol(p.ScriptInstruction.ExtractText(), "CUTSCENE")),
            });
            Add(quest.IssuerLocation, -1, 0);
            for (var i = 0; i < quest.TodoParams.Count; i++)
            {
                var todo = quest.TodoParams[i];
                foreach (var location in todo.ToDoLocation)
                    Add(location, i, todo.ToDoCompleteSeq);
            }

            void Add(RowRef<Level> reference, int todoIndex, byte completeSequence)
            {
                if (reference.RowId == 0 || reference.ValueNullable is not { } location)
                    return;
                var npcName = location.Type == 8 && npcs.TryGetRow(location.Object.RowId, out var npc)
                    ? npc.Singular.ExtractText() : string.Empty;
                var objective = new QuestObjective(quest.RowId, name, msq, location.RowId,
                    todoIndex, completeSequence, location.Territory.RowId, location.Map.RowId,
                    new Vector3(location.X, location.Y, location.Z), npcName);
                if (objective.IsValid)
                    objectives.Add(objective);
            }

            void AddInstance(uint id, DutyRelation relation)
            {
                if (id != 0 && instances.TryGetRow(id, out var instance))
                    AddDuty(instance.ContentFinderCondition.ValueNullable, relation);
            }

            void AddDuty(ContentFinderCondition? duty, DutyRelation relation)
            {
                if (duty is not { RowId: > 0 } row)
                    return;
                var dutyName = row.Name.ExtractText().Trim();
                if (dutyName.Length == 0)
                    return;
                var dutyType = row.ContentType.ValueNullable?.Name.ExtractText().Trim();
                var key = $"duty/{row.RowId}";
                duties[key] = duties.TryGetValue(key, out var old)
                    ? old with { Relations = old.Relations | relation }
                    : new QuestDuty(key, dutyName, string.IsNullOrWhiteSpace(dutyType) ? "Duty" : dutyType, relation)
                    {
                        IconId = row.ContentType.ValueNullable?.Icon ?? 0,
                    };
            }
        }

        Objectives = objectives.DistinctBy(o => o.Key).ToArray();
        ObjectivesByKey = Objectives.ToDictionary(o => o.Key);
        byQuest = Objectives.GroupBy(o => o.QuestId).ToDictionary(g => g.Key, g => g.ToArray());
        Chain = new QuestChain(questInfo);
        Quests = Objectives.DistinctBy(o => o.QuestId).OrderBy(o => o.QuestName).ToArray();
        ByTerritory = Objectives.GroupBy(o => o.TerritoryId).ToDictionary(g => g.Key, g => g.ToArray());
        Worlds = excel.GetSheet<World>()!.Where(w => w.IsPublic)
            .Select(w => (w.RowId, w.Name.ExtractText())).OrderBy(w => w.Item2).ToArray();
        // Resolve the public status from the installed data; fail closed if its meaning changes.
        CutsceneStatusId = excel.GetSheet<OnlineStatus>(Lumina.Data.Language.English)!
            .Single(s => s.Name.ExtractText() == "Viewing Cutscene").RowId;
        if (Objectives.Length == 0 || !Objectives.Any(o => o.IsMainScenario) || CutsceneStatusId == 0)
            throw new InvalidDataException("Quest catalog or cutscene status is unavailable.");
    }

    public QuestObjective[] InTerritory(uint territory) => ByTerritory.GetValueOrDefault(territory) ?? [];
    public QuestObjective[] InQuest(uint id) => byQuest.GetValueOrDefault(id) ?? [];

    public QuestDetails Details(uint id)
    {
        if (details.TryGetValue(id, out var cached)) return cached;
        var quest = Chain.Quests[id];
        var text = new Dictionary<string, string>();
        if (excel.SheetNames.Contains(quest.TextSheet))
        {
            var sheet = excel.GetRawSheet(quest.TextSheet);
            if (sheet is not null)
                foreach (var row in new ExcelSheet<RawRow>(sheet))
                    text[row.ReadStringColumn(0).ExtractText()] = row.ReadStringColumn(1).ExtractText().Trim();
        }
        var prefix = "TEXT_" + quest.TextSheet.Split('/')[^1].ToUpperInvariant();
        var locations = InQuest(id);
        var steps = new List<QuestStep> { new(-1, "Accept the quest", locations.Where(o => o.TodoIndex < 0).ToArray()) };
        for (var index = 0; index < 24; index++)
        {
            var label = text.GetValueOrDefault($"{prefix}_TODO_{index:00}");
            var points = locations.Where(o => o.TodoIndex == index).ToArray();
            if (!string.IsNullOrWhiteSpace(label) || points.Length > 0)
                steps.Add(new(index, string.IsNullOrWhiteSpace(label) ? $"Objective {index + 1}" : label, points));
        }
        return details[id] = new(text.GetValueOrDefault($"{prefix}_SEQ_00") ?? string.Empty, steps.ToArray(), text.Count > 0);
    }
    public string WorldName(uint id) => Worlds.FirstOrDefault(w => w.Id == id).Name ?? id.ToString();
    public string TerritoryName(uint id) => territoryNames.TryGetValue(id, out var name) && name.Length > 0
        ? name : $"Area {id}";
    public bool HasTerritory(uint id) => territoryNames.TryGetValue(id, out var name) && name.Length > 0;
}
