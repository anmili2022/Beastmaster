using Lumina.Excel.Sheets;
using Dalamud.Game.ClientState.Objects.Types;
using System.Globalization;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Beastmaster;

public sealed class BeastmasterDebugDataService
{
    private const int ResultLimit = 200;

    public string GetCharacter()
    {
        var contentId = DalamudApi.PlayerState.ContentId;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (!DalamudApi.ClientState.IsLoggedIn || player == null)
        {
            return "未登录，无法读取角色资料。";
        }

        var world = player.HomeWorld.Value.Name.ExtractText();
        return JoinLines(
            "类型: 当前角色",
            $"ContentId: {contentId.ToString(CultureInfo.InvariantCulture)}",
            $"名称: {player.Name.TextValue}",
            $"服务器: {world}");
    }

    public string GetLocation()
    {
        var territoryType = DalamudApi.ClientState.TerritoryType;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var position = player?.Position ?? Vector3.Zero;
        var territoryName = string.Empty;
        uint mapRowId = 0;

        if (DalamudApi.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryType, out var territory))
        {
            territoryName = territory.PlaceName.Value.Name.ExtractText();
            mapRowId = territory.Map.RowId;
        }

        return JoinLines(
            "类型: 当前位置",
            $"TerritoryType: {territoryType}",
            $"区域: {territoryName}",
            $"Map.RowId: {mapRowId}",
            player == null
                ? "坐标: 无本地角色"
                : $"世界坐标: X={position.X:0.###}, Y={position.Y:0.###}, Z={position.Z:0.###}");
    }

    public string FindClassJobs(string query)
        => FormatMatches(
            "职业 ClassJob",
            query,
            DalamudApi.DataManager.GetExcelSheet<ClassJob>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    public string FindQuests(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return "任务 Quest\n请输入名称关键词后再读取。";
        }

        var matches = DalamudApi.DataManager.GetExcelSheet<Quest>()
            .Where(row => row.RowId != 0
                && row.Name.ExtractText().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.RowId)
            .Take(ResultLimit + 1)
            .ToArray();
        var builder = new StringBuilder()
            .AppendLine("类型: 任务 Quest")
            .AppendLine($"关键词: {query}");

        foreach (var quest in matches.Take(ResultLimit))
        {
            var name = quest.Name.ExtractText();
            builder.AppendLine($"RowId={quest.RowId} | {name}");
            builder.AppendLine($"  IssuerStart={quest.IssuerStart.RowId} | IssuerLocation={quest.IssuerLocation.RowId}");
            builder.AppendLine($"  JournalGenre={quest.JournalGenre.RowId} | ClassJobCategory={quest.ClassJobCategory0.RowId}");
            builder.AppendLine($"  PreviousQuest={string.Join(',', quest.PreviousQuest.Select(previous => previous.RowId).Where(rowId => rowId != 0))}");

            if (quest.IssuerLocation.RowId == 0)
            {
                continue;
            }

            var level = quest.IssuerLocation.Value;
            var npcName = DalamudApi.DataManager.GetExcelSheet<ENpcResident>()
                .TryGetRow(quest.IssuerStart.RowId, out var npc)
                    ? npc.Singular.ExtractText()
                    : string.Empty;
            var zone = level.Territory.Value.PlaceName.Value.Name.ExtractText();
            builder.AppendLine($"  开始NPC={npcName} | TerritoryType={level.Territory.RowId} | Map.RowId={level.Map.RowId}");
            builder.AppendLine($"  世界坐标: X={level.X:0.###}, Y={level.Y:0.###}, Z={level.Z:0.###} | 区域={zone}");
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }
        else if (matches.Length > ResultLimit)
        {
            builder.AppendLine($"结果超过 {ResultLimit} 条，请使用更具体的关键词。");
        }

        return builder.ToString().TrimEnd();
    }

    public string FindBeastmasterQuestChain()
    {
        const uint beastmasterJournalGenre = 198;
        const uint beastmasterClassJobCategory = 203;
        var matches = DalamudApi.DataManager.GetExcelSheet<Quest>()
            .Where(quest => quest.RowId != 0
                && (quest.JournalGenre.RowId == beastmasterJournalGenre
                    || quest.ClassJobCategory0.RowId == beastmasterClassJobCategory))
            .OrderBy(quest => quest.RowId)
            .ToArray();
        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师任务链候选")
            .AppendLine($"条件: JournalGenre={beastmasterJournalGenre} 或 ClassJobCategory={beastmasterClassJobCategory}");

        foreach (var quest in matches)
        {
            builder.AppendLine($"RowId={quest.RowId} | {quest.Name.ExtractText()}");
            builder.AppendLine($"  JournalGenre={quest.JournalGenre.RowId} | ClassJobCategory={quest.ClassJobCategory0.RowId}");
            builder.AppendLine($"  PreviousQuest={string.Join(',', quest.PreviousQuest.Select(previous => previous.RowId).Where(rowId => rowId != 0))}");
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }

        return builder.ToString().TrimEnd();
    }

    public string FindItems(string query)
        => FormatMatches(
            "物品 Item",
            query,
            DalamudApi.DataManager.GetExcelSheet<Item>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    public string FindNpcs(string query)
        => FormatMatches(
            "NPC ENpcResident",
            query,
            DalamudApi.DataManager.GetExcelSheet<ENpcResident>()
                .Select(row => (row.RowId, Name: row.Singular.ExtractText())));

    public string FindMonsters(string query)
        => FormatMatches(
            "怪物 BNpcName",
            query,
            DalamudApi.DataManager.GetExcelSheet<BNpcName>()
                .Select(row => (row.RowId, Name: row.Singular.ExtractText())));

    public string FindDuties(string query)
        => FormatMatches(
            "副本 ContentFinderCondition",
            query,
            DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>()
                .Select(row => (row.RowId, Name: row.Name.ExtractText())));

    public string FindCatalogDuties()
    {
        var duties = DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>();
        var builder = new StringBuilder()
            .AppendLine("类型: 魔兽图鉴副本 ID")
            .AppendLine("来源: BeastmasterCatalog.Duty");

        foreach (var entry in BeastmasterCatalog.Entries.Where(entry => entry.LocationType == BeastmasterCatalogLocationType.Duty))
        {
            var normalizedName = NormalizeDutyName(entry.Location);
            var matches = duties
                .Where(duty => duty.RowId != 0
                    && duty.TerritoryType.RowId != 0
                    && NormalizeDutyName(duty.Name.ExtractText()).Equals(normalizedName, StringComparison.Ordinal))
                .OrderBy(duty => duty.RowId)
                .ToArray();

            builder.AppendLine($"图鉴 {entry.Number}. {entry.Name} | 副本={entry.Location}");
            if (matches.Length == 0)
            {
                builder.AppendLine("  未找到匹配的 ContentFinderCondition。");
                continue;
            }

            foreach (var duty in matches)
            {
                var territoryName = DalamudApi.DataManager.GetExcelSheet<TerritoryType>()
                    .TryGetRow(duty.TerritoryType.RowId, out var territory)
                    ? territory.PlaceName.Value.Name.ExtractText()
                    : string.Empty;
                builder.AppendLine($"  ContentFinderCondition.RowId={duty.RowId} | TerritoryType={duty.TerritoryType.RowId} | Map.RowId={duty.TerritoryType.Value.Map.RowId} | 区域={territoryName}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string FindAutoCaptureData()
    {
        string[] actionNames = ["碎击斩", "碎咬斧", "裂盾劈", "捕获"];
        var actions = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var statuses = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        var builder = new StringBuilder()
            .AppendLine("类型: 自动捕获技能与状态 ID")
            .AppendLine("技能 Action:");

        foreach (var name in actionNames)
        {
            var matches = actions
                .Where(action => action.RowId != 0
                    && action.Name.ExtractText().Equals(name, StringComparison.Ordinal))
                .OrderBy(action => action.RowId)
                .ToArray();
            if (matches.Length == 0)
            {
                builder.AppendLine($"  {name}: 未找到");
                continue;
            }

            foreach (var action in matches)
            {
                builder.AppendLine($"  {name}: Action.RowId={action.RowId} | ClassJob={action.ClassJob.RowId} | 等级={action.ClassJobLevel} | 射程={action.Range}");
            }
        }

        builder.AppendLine("状态 Status:");
        var captureStatuses = statuses
            .Where(status => status.RowId != 0
                && status.Name.ExtractText().Equals("捕获", StringComparison.Ordinal))
            .OrderBy(status => status.RowId)
            .ToArray();
        if (captureStatuses.Length == 0)
        {
            builder.AppendLine("  捕获: 未找到");
        }
        else
        {
            foreach (var status in captureStatuses)
            {
                builder.AppendLine($"  捕获: Status.RowId={status.RowId}");
            }
        }

        builder.AppendLine("当前目标状态:");
        if (DalamudApi.TargetManager.Target is not IBattleChara target)
        {
            builder.AppendLine("  当前未选择战斗目标。");
        }
        else if (!target.StatusList.Any())
        {
            builder.AppendLine($"  {target.Name.TextValue}: 无状态。");
        }
        else
        {
            foreach (var status in target.StatusList.OrderBy(status => status.StatusId))
            {
                var statusName = statuses.TryGetRow(status.StatusId, out var statusRow)
                    ? statusRow.Name.ExtractText()
                    : string.Empty;
                builder.AppendLine($"  StatusId={status.StatusId} | {statusName} | 剩余={status.RemainingTime:0.0}s | SourceId={status.SourceId}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string FindBeastmasterAttributes()
    {
        var actions = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师魔兽属性映射")
            .AppendLine("来源: 魔兽图鉴编号 + 召唤物 DataId + 大招 Action IconId")
            .AppendLine("属性 IconId: 3906=猛，3907=坚，3908=魔，3909=翔")
            .AppendLine();

        for (var number = 1; number <= BeastmasterCatalog.Entries.Count; number++)
        {
            var entry = BeastmasterCatalog.Entries[number - 1];
            var dataId = (uint)(18915 + number);
            var hasSkills = TryGetSummonSkills(dataId, out var ultimateId, out var releaseId);
            var ultimate = hasSkills && actions.TryGetRow(ultimateId, out var ultimateAction)
                ? ultimateAction
                : default;
            var release = hasSkills && actions.TryGetRow(releaseId, out var releaseAction)
                ? releaseAction
                : default;
            var iconId = hasSkills ? ultimate.Icon : 0;
            var attribute = iconId switch
            {
                3906 => "猛",
                3907 => "坚",
                3908 => "魔",
                3909 => "翔",
                _ => "未知",
            };

            builder.AppendLine($"图鉴 {entry.Number:00} | {entry.Name} | DataId={dataId}");
            builder.AppendLine($"  大招 ActionId={ultimateId} | {GetActionName(ultimate)} | IconId={iconId} | 属性={attribute}");
            builder.AppendLine($"  释放 ActionId={releaseId} | {GetActionName(release)}");
        }

        return builder.ToString().TrimEnd();
    }

    public string GetCurrentTargetDebug()
    {
        var target = DalamudApi.TargetManager.Target;
        if (target is not IBattleChara battleTarget)
        {
            return "类型: 当前目标\n无有效 BattleNpc 目标。";
        }

        var player = DalamudApi.ObjectTable.LocalPlayer;
        var statuses = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        var builder = new StringBuilder()
            .AppendLine("类型: 当前目标")
            .AppendLine($"名称: {battleTarget.Name.TextValue}")
            .AppendLine($"EntityId: {battleTarget.EntityId}")
            .AppendLine($"BaseId: {battleTarget.BaseId}")
            .AppendLine($"HP: {battleTarget.CurrentHp} / {battleTarget.MaxHp}")
            .AppendLine($"血量: {(battleTarget.MaxHp == 0 ? 0 : battleTarget.CurrentHp * 100f / battleTarget.MaxHp):0.##}%")
            .AppendLine($"可选中: {battleTarget.IsTargetable}")
            .AppendLine($"死亡: {battleTarget.IsDead}")
            .AppendLine("状态:");

        foreach (var status in battleTarget.StatusList.OrderBy(status => status.StatusId))
        {
            var name = statuses.TryGetRow(status.StatusId, out var row) ? row.Name.ExtractText() : "";
            var sourceType = player != null && status.SourceId == player.EntityId ? "自身" : "他人/未知";
            builder.AppendLine($"  StatusId={status.StatusId} | {name} | SourceId={status.SourceId} | 来源={sourceType} | 剩余={status.RemainingTime:0.0}s");
        }

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetComboDebug()
    {
        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return "类型: 当前连击\nActionManager 不可用。";
        }

        return new StringBuilder()
            .AppendLine("类型: 当前连击")
            .AppendLine("模式: 只读")
            .AppendLine($"Combo.Action: {manager->Combo.Action}")
            .AppendLine($"Combo.Timer: {manager->Combo.Timer:0.000}s")
            .AppendLine("说明: Timer 大于 0 表示当前连击窗口仍有效。")
            .ToString()
            .TrimEnd();
    }

    private static bool TryGetSummonSkills(uint dataId, out uint ultimateId, out uint releaseId)
    {
        var index = (int)dataId - 18915;
        if (index is < 1 or > 50)
        {
            ultimateId = 0;
            releaseId = 0;
            return false;
        }

        ultimateId = (uint)(44933 + index * 2);
        releaseId = ultimateId + 1;
        return true;
    }

    private static string GetActionName(Lumina.Excel.Sheets.Action action)
        => action.RowId == 0 ? "未找到" : action.Name.ExtractText();

    public unsafe string GetBeastmasterGaugeRaw()
    {
        const uint beastmasterClassJobId = 43;
        if (DalamudApi.PlayerState.ClassJob.RowId != beastmasterClassJobId)
        {
            return "类型: 驯兽师量谱原始数据\n请先切换为驯兽师。";
        }

        var snapshot = BeastmasterGaugeSnapshot.ReadRaw();
        if (!snapshot.Available)
        {
            return $"类型: 驯兽师量谱原始数据\n{snapshot.Status}。";
        }

        var address = snapshot.Address;
        var bytes = snapshot.Bytes.AsSpan();
        var length = bytes.Length;
        var uint16Values = new ushort[length / 2];
        var uint32Values = new uint[length / 4];
        for (var index = 0; index < uint16Values.Length; index++)
        {
            uint16Values[index] = BitConverter.ToUInt16(bytes.Slice(index * 2, 2));
        }

        for (var index = 0; index < uint32Values.Length; index++)
        {
            uint32Values[index] = BitConverter.ToUInt32(bytes.Slice(index * 4, 4));
        }

        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师量谱原始数据")
            .AppendLine("模式: 只读，不写入内存")
            .AppendLine($"ClassJob: {beastmasterClassJobId}")
            .AppendLine($"Address: 0x{address.ToInt64():X}")
            .AppendLine($"Length: {length} bytes")
            .AppendLine($"Hex: {string.Join(' ', bytes.ToArray().Select(value => value.ToString("X2")))}")
            .AppendLine($"UInt16: {string.Join(' ', uint16Values)}")
            .AppendLine($"UInt32: {string.Join(' ', uint32Values)}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatMatches(
        string category,
        string query,
        IEnumerable<(uint RowId, string Name)> rows)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return $"{category}\n请输入名称关键词后再读取。";
        }

        var matches = rows
            .Where(row => row.RowId != 0
                && !string.IsNullOrWhiteSpace(row.Name)
                && row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.RowId)
            .Take(ResultLimit + 1)
            .ToArray();
        var truncated = matches.Length > ResultLimit;

        var builder = new StringBuilder()
            .AppendLine($"类型: {category}")
            .AppendLine($"关键词: {query}");
        foreach (var match in matches.Take(ResultLimit))
        {
            builder.Append("RowId=")
                .Append(match.RowId)
                .Append(" | ")
                .AppendLine(match.Name);
        }

        if (matches.Length == 0)
        {
            builder.AppendLine("未找到匹配内容。");
        }
        else if (truncated)
        {
            builder.AppendLine($"结果超过 {ResultLimit} 条，请使用更具体的关键词。");
        }

        return builder.ToString().TrimEnd();
    }

    private static string JoinLines(params string[] lines)
        => string.Join(Environment.NewLine, lines);

    private static string NormalizeDutyName(string name)
        => new(name.Where(character => !char.IsWhiteSpace(character)
            && character is not '·' and not '：' and not ':').ToArray());
}
