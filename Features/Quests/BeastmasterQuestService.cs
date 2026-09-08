using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Text;

namespace Beastmaster;

public sealed record BeastmasterQuestLocation(
    uint TerritoryType,
    uint MapRowId,
    Vector3 Position,
    string Zone,
    string NpcName);

public sealed record BeastmasterQuestPrerequisite(uint RowId, string Name);

public enum BeastmasterQuestStatus
{
    NotAccepted,
    Accepted,
    Completed,
}

public sealed class BeastmasterQuestService
{
    private readonly Dictionary<uint, BeastmasterQuestLocation?> issuerLocations = [];
    private readonly Dictionary<uint, IReadOnlyList<BeastmasterQuestPrerequisite>> prerequisites = [];
    private readonly Dictionary<uint, byte> sequences = [];
    private readonly Dictionary<uint, BeastmasterQuestStatus> statuses = [];

    public BeastmasterQuestStatus GetStatus(uint questRowId)
        => statuses.GetValueOrDefault(questRowId);

    public byte GetSequence(uint questRowId)
        => sequences.GetValueOrDefault(questRowId);

    public bool TryGetCurrentTarget(uint questRowId, out BeastmasterQuestLocation location)
    {
        var sequence = GetSequence(questRowId);
        var target = BeastmasterQuestGuide.Targets.FirstOrDefault(candidate =>
            candidate.QuestRowId == questRowId && candidate.Sequence == sequence);
        if (target == null)
        {
            location = default!;
            return false;
        }

        location = new BeastmasterQuestLocation(
            target.TerritoryType,
            target.MapRowId,
            target.Position,
            target.Zone,
            target.Name);
        return true;
    }

    public IReadOnlyList<BeastmasterQuestPrerequisite> GetPrerequisites(uint questRowId)
    {
        if (prerequisites.TryGetValue(questRowId, out var cached))
        {
            return cached;
        }

        if (!DalamudApi.DataManager.GetExcelSheet<Quest>().TryGetRow(questRowId, out var quest))
        {
            prerequisites[questRowId] = [];
            return prerequisites[questRowId];
        }

        var questSheet = DalamudApi.DataManager.GetExcelSheet<Quest>();
        prerequisites[questRowId] = quest.PreviousQuest
            .Select(previous => previous.RowId)
            .Where(rowId => rowId != 0)
            .Distinct()
            .Select(rowId => new BeastmasterQuestPrerequisite(
                rowId,
                questSheet.TryGetRow(rowId, out var previousQuest)
                    ? previousQuest.Name.ExtractText()
                    : $"任务 {rowId}"))
            .ToArray();
        return prerequisites[questRowId];
    }

    public unsafe void RefreshStatuses(IEnumerable<uint> questRowIds)
    {
        var manager = QuestManager.Instance();
        foreach (var questRowId in questRowIds)
        {
            sequences[questRowId] = QuestManager.GetQuestSequence(questRowId);
            statuses[questRowId] = QuestManager.IsQuestComplete(questRowId)
                ? BeastmasterQuestStatus.Completed
                : manager != null && manager->IsQuestAccepted(questRowId)
                    ? BeastmasterQuestStatus.Accepted
                    : BeastmasterQuestStatus.NotAccepted;
        }
    }

    public unsafe string GetActiveQuestsDebug()
    {
        var manager = QuestManager.Instance();
        var builder = new StringBuilder()
            .AppendLine("类型: 当前所有普通任务状态");
        if (manager == null)
        {
            builder.AppendLine("QuestManager: 不可用。");
            return builder.ToString().TrimEnd();
        }

        var questRowsByRuntimeId = DalamudApi.DataManager.GetExcelSheet<Quest>()
            .Where(quest => quest.RowId != 0)
            .GroupBy(quest => (ushort)(quest.RowId & 0xFFFF))
            .ToDictionary(group => group.Key, group => group.First());
        var count = 0;
        foreach (var work in manager->NormalQuests)
        {
            if (work.QuestId == 0)
            {
                continue;
            }

            count++;
            var hasQuestRow = questRowsByRuntimeId.TryGetValue(work.QuestId, out var quest);
            var rowId = hasQuestRow ? quest.RowId : work.QuestId;
            var name = hasQuestRow ? quest.Name.ExtractText() : "未知任务";
            builder.AppendLine($"RowId={rowId} | RuntimeQuestId={work.QuestId} | {name}");
            builder.AppendLine($"  Sequence={work.Sequence} | Flags={work.Flags} | AcceptClassJob={work.AcceptClassJob}");
            var variables = new byte[6];
            for (var index = 0; index < variables.Length; index++)
            {
                variables[index] = work.Variables[index];
            }

            builder.AppendLine($"  Variables={string.Join(',', variables)}");
        }

        builder.AppendLine($"任务数量={count}");
        return builder.ToString().TrimEnd();
    }

    public bool TryGetIssuerLocation(uint questRowId, out BeastmasterQuestLocation location)
    {
        if (issuerLocations.TryGetValue(questRowId, out var cached))
        {
            location = cached!;
            return cached != null;
        }

        location = default!;
        if (!DalamudApi.DataManager.GetExcelSheet<Quest>().TryGetRow(questRowId, out var quest)
            || quest.IssuerLocation.RowId == 0)
        {
            issuerLocations[questRowId] = null;
            return false;
        }

        var level = quest.IssuerLocation.Value;
        var territoryType = level.Territory.RowId;
        var mapRowId = level.Map.RowId;
        if (territoryType == 0 || mapRowId == 0)
        {
            issuerLocations[questRowId] = null;
            return false;
        }

        var zone = level.Territory.Value.PlaceName.Value.Name.ExtractText();
        var npcName = DalamudApi.DataManager.GetExcelSheet<ENpcResident>()
            .TryGetRow(quest.IssuerStart.RowId, out var npc)
                ? npc.Singular.ExtractText()
                : string.Empty;
        location = new BeastmasterQuestLocation(
            territoryType,
            mapRowId,
            new Vector3(level.X, level.Y, level.Z),
            zone,
            npcName);
        issuerLocations[questRowId] = location;
        return true;
    }
}
