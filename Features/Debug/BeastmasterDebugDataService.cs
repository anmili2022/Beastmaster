using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;
using Dalamud.Game.ClientState.Objects.Types;
using System.Globalization;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.Inventory;

namespace Beastmaster;

public sealed class BeastmasterDebugDataService
{
    private const int ResultLimit = 200;
    private readonly BeastmasterCountdownService countdownService;

    public BeastmasterDebugDataService(BeastmasterCountdownService countdownService)
    {
        this.countdownService = countdownService;
    }

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

    public string FindTerritories(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return "区域 TerritoryType\n请输入 TerritoryType ID 或区域名称关键词。";
        }

        var territories = DalamudApi.DataManager.GetExcelSheet<TerritoryType>();
        var matches = uint.TryParse(query, out var territoryTypeId)
            ? territories.Where(row => row.RowId == territoryTypeId).ToArray()
            : territories
                .Where(row => row.RowId != 0
                    && row.PlaceName.Value.Name.ExtractText().Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.RowId)
                .Take(ResultLimit + 1)
                .ToArray();
        var duties = DalamudApi.DataManager.GetExcelSheet<ContentFinderCondition>();
        var builder = new StringBuilder()
            .AppendLine("类型: 区域 TerritoryType")
            .AppendLine($"查询: {query}");

        foreach (var territory in matches.Take(ResultLimit))
        {
            var name = territory.PlaceName.Value.Name.ExtractText();
            builder.AppendLine($"TerritoryType={territory.RowId} | 区域={name} | Map.RowId={territory.Map.RowId}");

            var dutyMatches = duties
                .Where(duty => duty.RowId != 0 && duty.TerritoryType.RowId == territory.RowId)
                .Select(duty => $"{duty.RowId} {duty.Name.ExtractText()}")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (dutyMatches.Length > 0)
            {
                builder.AppendLine($"  副本: {string.Join(" | ", dutyMatches)}");
            }
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
        uint[] actionIds = [44879, 44883, 44885, 44880];
        var actions = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        var statuses = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        var builder = new StringBuilder()
            .AppendLine("类型: 自动捕获技能与状态 ID")
            .AppendLine("技能 Action:");

        foreach (var actionId in actionIds)
        {
            if (!actions.TryGetRow(actionId, out var action))
            {
                builder.AppendLine($"  Action.RowId={actionId}: 未找到");
                continue;
            }

            builder.AppendLine($"  Action.RowId={action.RowId} | {action.Name.ExtractText()} | ClassJob={action.ClassJob.RowId} | 等级={action.ClassJobLevel} | 射程={action.Range}");
        }

        builder.AppendLine("状态 Status:");
        if (!statuses.TryGetRow(4626, out var captureStatus))
        {
            builder.AppendLine("  Status.RowId=4626: 未找到");
        }
        else
        {
            builder.AppendLine($"  Status.RowId={captureStatus.RowId} | {captureStatus.Name.ExtractText()}");
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

    public string GetBeastmasterCatalogProbe()
    {
        var builder = new StringBuilder()
            .AppendLine("类型: 魔兽图鉴客户端探针")
            .AppendLine("模式: 只读，不打开窗口、不触发回调、不写入游戏数据")
            .AppendLine("说明: 仅检查当前已存在的候选原生 Addon；需要先在游戏中打开相关图鉴页面。")
            .AppendLine();

        var addonNames = new[] { "MonsterNote", "MobHunt", "MinionNotebook" };
        foreach (var addonName in addonNames)
        {
            try
            {
                var addon = DalamudApi.GameGui.GetAddonByName(addonName);
                builder.AppendLine($"Addon={addonName}");
                if (addon.IsNull)
                {
                    builder.AppendLine("  状态: 不存在");
                    continue;
                }

                builder.AppendLine($"  Address=0x{addon.Address.ToInt64():X}");
                builder.AppendLine($"  Name={addon.Name}");
                builder.AppendLine($"  Id={addon.Id} | ParentId={addon.ParentId} | HostId={addon.HostId}");
                builder.AppendLine($"  Ready={addon.IsReady} | Visible={addon.IsVisible}");
                builder.AppendLine($"  AtkValuesCount={addon.AtkValuesCount}");

                if (!addon.IsReady)
                {
                    continue;
                }

                var index = 0;
                foreach (var value in addon.AtkValues)
                {
                    string renderedValue;
                    try
                    {
                        renderedValue = value.GetValue()?.ToString() ?? "<null>";
                    }
                    catch (Exception ex)
                    {
                        renderedValue = $"<读取失败: {ex.GetType().Name}>";
                    }

                    builder.AppendLine($"  Value[{index++}] Type={value.ValueType} Value={renderedValue}");
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine($"  读取失败: {ex.GetType().Name}: {ex.Message}");
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetBeastLevelExperienceProbe()
    {
        const int pageValueIndex = 10;
        const int firstEntryValueIndex = 24;
        const int entryStride = 8;
        const int entriesPerPage = 25;
        const int agentDumpSize = 0x400;

        var builder = new StringBuilder()
            .AppendLine("类型: 魔兽等级经验结构")
            .AppendLine("模式: 只读，不触发回调，不翻页，不写入内存")
            .AppendLine("目标: 定位每只魔兽的当前等级、当前经验和升级所需经验字段")
            .AppendLine("采集方法: 打开魔兽图鉴后读取；建议在同一只魔兽获得经验前后各采集一次并对比")
            .AppendLine();

        var addonAddress = DalamudApi.GameGui.GetAddonByName("XBMMonsterNotebook", 1).Address;
        var addon = (AtkUnitBase*)addonAddress;
        if (addon == null || !addon->IsVisible || addon->AtkValues == null)
        {
            builder.AppendLine("XBMMonsterNotebook 不存在或不可见。")
                .AppendLine("请先在游戏中打开魔兽图鉴并保持窗口可见。");
            return builder.ToString().TrimEnd();
        }

        var page = addon->AtkValuesCount > pageValueIndex
            ? addon->AtkValues[pageValueIndex].UInt
            : uint.MaxValue;
        builder.AppendLine($"Addon Address=0x{addonAddress.ToInt64():X}")
            .AppendLine($"AtkValues Address=0x{(nint)addon->AtkValues:X} | AtkValueSize=0x{sizeof(AtkValue):X}")
            .AppendLine($"AtkValuesCount={addon->AtkValuesCount} | Page={page}")
            .AppendLine($"详情内部编号 AtkValue: 0x{(nint)(addon->AtkValues + 227):X} | 该字段可能滞后，不用于同步")
            .AppendLine($"选中显示编号 AtkValue: 0x{(nint)(addon->AtkValues + 229):X} | 与 Value[231] 图标共同校验")
            .AppendLine($"选中兽级 AtkValue: 0x{(nint)(addon->AtkValues + 258):X} | 类型=ManagedString，数值请读取 Value[258]")
            .AppendLine($"选中当前经验 AtkValue: 0x{(nint)(addon->AtkValues + 261):X} | UInt数值: 0x{(nint)(&addon->AtkValues[261].UInt):X}")
            .AppendLine($"选中经验上限 AtkValue: 0x{(nint)(addon->AtkValues + 262):X} | UInt数值: 0x{(nint)(&addon->AtkValues[262].UInt):X}")
            .AppendLine()
            .AppendLine("当前页条目字段（每项 8 个 AtkValue）:");

        for (var entryIndex = 0; entryIndex < entriesPerPage; entryIndex++)
        {
            var valueIndex = firstEntryValueIndex + entryIndex * entryStride;
            if (valueIndex + entryStride > addon->AtkValuesCount)
            {
                break;
            }

            var number = page <= 1 ? 1 + (int)page * entriesPerPage + entryIndex : entryIndex + 1;
            builder.AppendLine($"图鉴 {number:00} | Value[{valueIndex}..{valueIndex + entryStride - 1}]");
            for (var field = 0; field < entryStride; field++)
            {
                var value = addon->AtkValues[valueIndex + field];
                builder.AppendLine($"  +{field}: {FormatAtkValue(value)}");
            }
        }

        builder.AppendLine().AppendLine("图鉴非条目字段:");
        for (var index = 0; index < addon->AtkValuesCount; index++)
        {
            if (index >= firstEntryValueIndex
                && index < firstEntryValueIndex + entriesPerPage * entryStride)
            {
                continue;
            }

            builder.AppendLine($"  Value[{index}]: {FormatAtkValue(addon->AtkValues[index])}");
        }

        var agentModule = AgentModule.Instance();
        var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)500);
        builder.AppendLine().AppendLine("Agent 500 候选整数（前 0x400 字节）:");
        if (agent == null)
        {
            builder.AppendLine("Agent 500 不可用。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"Agent Address=0x{(nint)agent:X}");
        for (var offset = 0; offset < agentDumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16; column += 4)
            {
                var value = *(uint*)(agent + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }

        AppendDetailProgressionProbe(builder, agentModule);
        AppendPetPartyProgressionProbe(builder, agentModule);
        AppendXbmAddonVisibility(builder);

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetBeastResultProgressionProbe()
    {
        const int agentDumpSize = 0x1000;
        var builder = new StringBuilder()
            .AppendLine("类型: 斗兽结算等级经验")
            .AppendLine("模式: 只读，不触发回调，不退出结算页，不写入内存")
            .AppendLine("目标: 从每轮 XBMResult 结算页定位参战魔兽的结算后等级与经验")
            .AppendLine("当前验证值: 图鉴 02 松鼠种，等级 6，经验 9/100")
            .AppendLine();

        var resultAddress = DalamudApi.GameGui.GetAddonByName("XBMResult", 1).Address;
        var result = (AtkUnitBase*)resultAddress;
        if (result == null || !result->IsVisible || result->AtkValues == null)
        {
            builder.AppendLine("XBMResult 不存在或不可见。请在一轮斗兽结束后的经验结算页保持窗口可见后读取。");
            AppendXbmAddonVisibility(builder);
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"Result Addon Address=0x{resultAddress.ToInt64():X}")
            .AppendLine($"Result AtkValuesCount={result->AtkValuesCount}")
            .AppendLine("Result AtkValues:");
        for (var index = 0; index < result->AtkValuesCount; index++)
        {
            builder.AppendLine($"  ResultValue[{index}]: {FormatAtkValue(result->AtkValues[index])}");
        }

        var agentModule = AgentModule.Instance();
        var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)505);
        builder.AppendLine().AppendLine("Agent 505 候选整数（前 0x1000 字节）:");
        if (agent == null)
        {
            builder.AppendLine("Agent 505 不可用。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"Agent Address=0x{(nint)agent:X}");
        for (var offset = 0; offset < agentDumpSize; offset += 4)
        {
            var value = *(uint*)(agent + offset);
            if (value is 6 or 9 or 100
                || (value >> 16) is 6 or 9 or 100
                || (value & 0xFFFF) is 6 or 9 or 100)
            {
                builder.AppendLine($"  候选 +0x{offset:X3}: UInt={value} | High16={value >> 16} | Low16={value & 0xFFFF}");
            }
        }

        builder.AppendLine("Agent 505 完整整数:");
        for (var offset = 0; offset < agentDumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16; column += 4)
            {
                var value = *(uint*)(agent + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetPetPartyStructureProbe()
    {
        const int memberCountIndex = 5;
        const int firstMemberIndex = 6;
        const int memberStride = 77;
        const int maximumSlots = 15;
        const uint iconBase = 242000;

        var builder = new StringBuilder()
            .AppendLine("类型: 魔兽编队结构")
            .AppendLine("模式: 只读，不触发回调，不增删成员，不写入内存")
            .AppendLine("目标: 识别当前奇盘、编队人数、容量和成员顺序")
            .AppendLine($"TerritoryType: {DalamudApi.ClientState.TerritoryType}")
            .AppendLine();

        var addonAddress = DalamudApi.GameGui.GetAddonByName("XBMPetParty", 1).Address;
        var addon = (AtkUnitBase*)addonAddress;
        if (addon == null || !addon->IsVisible || addon->AtkValues == null)
        {
            builder.AppendLine("XBMPetParty 不存在或不可见。请保持挑战前的魔兽编队窗口打开。");
            return builder.ToString().TrimEnd();
        }

        var count = addon->AtkValuesCount > memberCountIndex
            ? ReadDebugNumber(addon->AtkValues[memberCountIndex])
            : uint.MaxValue;
        builder.AppendLine($"Address=0x{addonAddress.ToInt64():X}")
            .AppendLine($"AtkValuesCount={addon->AtkValuesCount}")
            .AppendLine($"当前成员数候选 Value[5]={count}")
            .AppendLine()
            .AppendLine("头部字段 Value[0..5]:");
        for (var index = 0; index <= memberCountIndex && index < addon->AtkValuesCount; index++)
        {
            builder.AppendLine($"  Value[{index}]: {FormatAtkValue(addon->AtkValues[index])}");
        }

        builder.AppendLine().AppendLine("成员列表:");
        var parsedMembers = 0;
        for (var slot = 0; slot < maximumSlots; slot++)
        {
            var start = firstMemberIndex + slot * memberStride;
            if (start + memberStride > addon->AtkValuesCount)
            {
                break;
            }

            var level = ReadDebugText(addon->AtkValues[start]);
            var icon = ReadDebugNumber(addon->AtkValues[start + 1]);
            var name = ReadDebugText(addon->AtkValues[start + 3]);
            if (icon is > iconBase and <= iconBase + 50)
            {
                parsedMembers++;
                builder.AppendLine($"  位置 {slot + 1:00}: 图鉴 {icon - iconBase:00} | {name} | 兽级 {level} | Icon={icon}");
            }
            else
            {
                builder.AppendLine($"  位置 {slot + 1:00}: 空或无效 | Icon={icon} | Name=\"{name}\" | Level=\"{level}\"");
            }

            builder.AppendLine($"    块头: +0={FormatAtkValue(addon->AtkValues[start])} | +1={FormatAtkValue(addon->AtkValues[start + 1])} | +2={FormatAtkValue(addon->AtkValues[start + 2])} | +3={FormatAtkValue(addon->AtkValues[start + 3])}");
            builder.AppendLine($"    块尾: +72={FormatAtkValue(addon->AtkValues[start + 72])} | +73={FormatAtkValue(addon->AtkValues[start + 73])} | +74={FormatAtkValue(addon->AtkValues[start + 74])} | +75={FormatAtkValue(addon->AtkValues[start + 75])} | +76={FormatAtkValue(addon->AtkValues[start + 76])}");
        }

        builder.AppendLine()
            .AppendLine($"解析成员数={parsedMembers} | Value[5]={count}")
            .AppendLine("尾部字段:");
        var memberAreaEnd = firstMemberIndex + maximumSlots * memberStride;
        for (var index = memberAreaEnd; index < addon->AtkValuesCount; index++)
        {
            builder.AppendLine($"  Value[{index}]: {FormatAtkValue(addon->AtkValues[index])}");
        }

        builder.AppendLine()
            .AppendLine("采集说明: 请分别在第一盘、第二盘、第三盘、高段第一盘、高段第二盘的编队界面读取，并注明界面显示容量。");
        return builder.ToString().TrimEnd();
    }

    private static uint ReadDebugNumber(AtkValue value)
        => value.TypeCode() switch
        {
            3 when value.Int >= 0 => (uint)value.Int,
            4 or 5 => value.UInt,
            8 or 10 when uint.TryParse(value.String.ToString(), out var parsed) => parsed,
            _ => uint.MaxValue,
        };

    private static string ReadDebugText(AtkValue value)
        => value.TypeCode() is 8 or 10 ? value.String.ToString() ?? string.Empty : string.Empty;

    private static unsafe void AppendDetailProgressionProbe(StringBuilder builder, AgentModule* agentModule)
    {
        const int agentDumpSize = 0x400;
        builder.AppendLine().AppendLine("宝宝详情 XBM 数据:");
        var detailAddress = DalamudApi.GameGui.GetAddonByName("XBMBattleMonsterDetail", 1).Address;
        var detail = (AtkUnitBase*)detailAddress;
        if (detail == null || !detail->IsVisible || detail->AtkValues == null)
        {
            builder.AppendLine("XBMBattleMonsterDetail 不存在或不可见。请在原生图鉴中点开目标宝宝详情后重试。");
        }
        else
        {
            builder.AppendLine($"Detail Addon Address=0x{detailAddress.ToInt64():X}")
                .AppendLine($"Detail AtkValuesCount={detail->AtkValuesCount}");
            for (var index = 0; index < detail->AtkValuesCount; index++)
            {
                builder.AppendLine($"  DetailValue[{index}]: {FormatAtkValue(detail->AtkValues[index])}");
            }
        }

        builder.AppendLine().AppendLine("Agent 499 候选整数（前 0x400 字节）:");
        var detailAgent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)499);
        if (detailAgent == null)
        {
            builder.AppendLine("Agent 499 不可用。");
            return;
        }

        builder.AppendLine($"Agent Address=0x{(nint)detailAgent:X}");
        for (var offset = 0; offset < agentDumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16; column += 4)
            {
                var value = *(uint*)(detailAgent + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }
    }

    private static unsafe void AppendPetPartyProgressionProbe(StringBuilder builder, AgentModule* agentModule)
    {
        const int agentDumpSize = 0x800;
        builder.AppendLine().AppendLine("魔兽编队 XBM 数据:");
        var partyAddress = DalamudApi.GameGui.GetAddonByName("XBMPetParty", 1).Address;
        var party = (AtkUnitBase*)partyAddress;
        if (party == null || !party->IsVisible || party->AtkValues == null)
        {
            builder.AppendLine("XBMPetParty 不存在或不可见。请打开原生魔兽编队界面后重试。");
        }
        else
        {
            builder.AppendLine($"Party Addon Address=0x{partyAddress.ToInt64():X}")
                .AppendLine($"Party AtkValuesCount={party->AtkValuesCount}");
            for (var index = 0; index < party->AtkValuesCount; index++)
            {
                builder.AppendLine($"  PartyValue[{index}]: {FormatAtkValue(party->AtkValues[index])}");
            }
        }

        builder.AppendLine().AppendLine("Agent 501 候选整数（前 0x800 字节）:");
        var partyAgent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)501);
        if (partyAgent == null)
        {
            builder.AppendLine("Agent 501 不可用。");
            return;
        }

        builder.AppendLine($"Agent Address=0x{(nint)partyAgent:X}");
        for (var offset = 0; offset < agentDumpSize; offset += 4)
        {
            var value = *(uint*)(partyAgent + offset);
            if (value is 4 or 61 or 100
                || (value >> 16) is 4 or 61 or 100
                || (value & 0xFFFF) is 4 or 61 or 100)
            {
                builder.AppendLine($"  候选 +0x{offset:X3}: UInt={value} | High16={value >> 16} | Low16={value & 0xFFFF}");
            }
        }

        builder.AppendLine("Agent 501 完整整数:");
        for (var offset = 0; offset < agentDumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16; column += 4)
            {
                var value = *(uint*)(partyAgent + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }
    }

    private static void AppendXbmAddonVisibility(StringBuilder builder)
    {
        builder.AppendLine().AppendLine("XBM Addon 状态:");
        foreach (var addonName in XbmAddonNames())
        {
            try
            {
                var addon = DalamudApi.GameGui.GetAddonByName(addonName, 1);
                builder.AppendLine(addon.IsNull
                    ? $"  {addonName}: 不存在"
                    : $"  {addonName}: Visible={addon.IsVisible} | Ready={addon.IsReady} | AtkValuesCount={addon.AtkValuesCount} | Address=0x{addon.Address.ToInt64():X}");
            }
            catch (Exception ex)
            {
                builder.AppendLine($"  {addonName}: 读取失败 {ex.GetType().Name}");
            }
        }
    }

    private static unsafe string FormatAtkValue(AtkValue value)
    {
        var typeCode = (int)value.Type & 0xF;
        var text = typeCode is 8 or 10
            ? (value.String.ToString() ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n")
            : string.Empty;
        return typeCode switch
        {
            2 => $"Type={value.Type} Bool={value.Bool}",
            3 => $"Type={value.Type} Int={value.Int}",
            4 or 5 => $"Type={value.Type} UInt={value.UInt}",
            8 or 10 => $"Type={value.Type} String=\"{text}\"",
            _ => $"Type={value.Type} UInt={value.UInt}",
        };
    }

    public string FindRecommendedEquipmentIds()
    {
        var items = DalamudApi.DataManager.GetExcelSheet<Item>();
        var builder = new StringBuilder()
            .AppendLine("类型: 推荐装备物品 ID")
            .AppendLine("说明: 装备页面使用固定 ItemId 检测持有状态")
            .AppendLine();

        foreach (var plan in new[]
        {
            (Name: "开荒装", Entries: BeastmasterEquipmentGuide.Level50Starter),
            (Name: "BIS", Entries: BeastmasterEquipmentGuide.Level50BestInSlot),
        })
        {
            builder.AppendLine($"[{plan.Name}]");
            foreach (var equipment in plan.Entries.Where(entry => entry.Name != "无装备"))
            {
                var exactMatches = items
                    .Where(item => item.RowId != 0 && item.Name.ExtractText().Equals(equipment.Name, StringComparison.Ordinal))
                    .OrderBy(item => item.RowId)
                    .ToArray();
                builder.AppendLine($"{equipment.Slot} | {equipment.Name}");
                if (exactMatches.Length > 0)
                {
                    foreach (var item in exactMatches)
                    {
                        builder.AppendLine($"  精确匹配: ItemId={item.RowId} | {item.Name.ExtractText()}");
                    }
                }
                else
                {
                    var candidates = items
                        .Where(item => item.RowId != 0 && item.Name.ExtractText().Contains(equipment.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.RowId)
                        .Take(10)
                        .ToArray();
                    if (candidates.Length == 0)
                    {
                        builder.AppendLine("  未找到精确匹配或候选项");
                    }
                    else
                    {
                        foreach (var item in candidates)
                        {
                            builder.AppendLine($"  候选: ItemId={item.RowId} | {item.Name.ExtractText()}");
                        }
                    }
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string FindBeastmasterRecoveryItems()
    {
        var items = DalamudApi.DataManager.GetExcelSheet<Item>();
        var candidates = items
            .Where(item => item.RowId != 0
                && (item.Name.ExtractText().Contains("恢复药", StringComparison.Ordinal)
                    || item.Name.ExtractText().Contains("魔兽", StringComparison.Ordinal)))
            .OrderBy(item => item.RowId)
            .ToArray();
        var inventoryTypes = new[]
        {
            GameInventoryType.Inventory1,
            GameInventoryType.Inventory2,
            GameInventoryType.Inventory3,
            GameInventoryType.Inventory4,
        };
        var counts = new Dictionary<uint, uint>();
        foreach (var type in inventoryTypes)
        {
            foreach (var inventoryItem in DalamudApi.GameInventory.GetInventoryItems(type))
            {
                if (!inventoryItem.IsEmpty)
                {
                    counts[inventoryItem.BaseItemId] = counts.GetValueOrDefault(inventoryItem.BaseItemId) + (uint)inventoryItem.Quantity;
                }
            }
        }

        var builder = new StringBuilder()
            .AppendLine("类型: 魔兽恢复药扫描")
            .AppendLine("奇弈恢复类内部 ID: 恢复药1~4级=76~79，药粉1~3级=80~82，吸血药=135，套装=140")
            .AppendLine("扫描容器: Inventory1~Inventory4")
            .AppendLine();
        if (candidates.Length == 0)
        {
            builder.AppendLine("物品表中没有找到名称包含“恢复药”或“魔兽”的候选物品。");
        }
        else
        {
            foreach (var item in candidates)
            {
                builder.AppendLine($"ItemId={item.RowId} | {item.Name.ExtractText()} | 持有数量={counts.GetValueOrDefault(item.RowId)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public unsafe string FindContentInventoryContainers()
    {
        var manager = ContentInventoryManager.Instance();
        if (manager == null)
        {
            return "类型: 内容道具容器扫描\nContentInventoryManager 不可用。";
        }

        var itemSheet = DalamudApi.DataManager.GetExcelSheet<Item>();
        var candidates = Enumerable.Range(0, 10000)
            .Select(value => (InventoryType)(uint)value)
            .Concat(Enum.GetValues<InventoryType>())
            .Distinct()
            .OrderBy(value => (uint)value)
            .ToArray();

        var builder = new StringBuilder()
            .AppendLine("类型: 内容道具容器扫描")
            .AppendLine("模式: 只读，不使用道具，不写入内存")
            .AppendLine("目标: 查找奇弈道具等 ContentInventoryManager 容器")
            .AppendLine($"TerritoryType: {DalamudApi.ClientState.TerritoryType}")
            .AppendLine($"InCombat: {DalamudApi.Condition[ConditionFlag.InCombat]}")
            .AppendLine();

        var containerCount = 0;
        var nonEmptySlotCount = 0;
        foreach (var inventoryType in candidates)
        {
            if (!manager->HasInventoryContainer(inventoryType))
            {
                continue;
            }

            containerCount++;
            var container = manager->GetInventoryContainer(inventoryType);
            var typeValue = (uint)inventoryType;
            var typeName = Enum.IsDefined(inventoryType) ? inventoryType.ToString() : $"Unknown{typeValue}";
            if (container == null)
            {
                builder.AppendLine($"InventoryType={typeValue} ({typeName}) | 容器指针为空");
                continue;
            }

            var size = Math.Clamp(container->Size, 0, 200);
            builder.AppendLine($"InventoryType={typeValue} ({typeName}) | Loaded={container->IsLoaded} | Size={container->Size}");
            for (short slot = 0; slot < size; slot++)
            {
                var item = manager->GetInventorySlot(inventoryType, slot);
                if (item == null || item->ItemId == 0 || item->Quantity <= 0)
                {
                    continue;
                }

                nonEmptySlotCount++;
                var itemName = itemSheet.TryGetRow(item->ItemId, out var row)
                    ? row.Name.ExtractText()
                    : string.Empty;
                builder.AppendLine($"  Slot={slot} | ItemId={item->ItemId} | 数量={item->Quantity} | {itemName}");
            }
        }

        if (containerCount == 0)
        {
            builder.AppendLine("未发现 ContentInventoryManager 当前可见容器。请进入斗兽奇弈并打开奇弈道具后再次读取。");
        }

        builder.AppendLine()
            .AppendLine($"发现容器数: {containerCount}")
            .AppendLine($"非空槽位数: {nonEmptySlotCount}")
            .AppendLine("提示: 请把包含恢复药 ItemId/数量的扫描结果反馈，用于确认奇弈道具容器。当前扫描不会验证物品能否使用。");

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetXbmAddonProbe()
    {
        var builder = new StringBuilder()
            .AppendLine("类型: XBM Agent/AddOn 扫描")
            .AppendLine("模式: 只读，不触发回调，不使用道具，不写入内存")
            .AppendLine("目标: 查找斗兽奇弈/驯兽师界面中的奇弈道具数据")
            .AppendLine($"TerritoryType: {DalamudApi.ClientState.TerritoryType}")
            .AppendLine($"InCombat: {DalamudApi.Condition[ConditionFlag.InCombat]}")
            .AppendLine();

        var agentModule = AgentModule.Instance();
        builder.AppendLine("XBM Agents:");
        foreach (var agent in XbmAgents())
        {
            try
            {
                var pointer = agentModule == null
                    ? null
                    : agentModule->GetAgentByInternalId((AgentId)agent.Id);
                builder.AppendLine($"  AgentId={agent.Id} {agent.Name} | Address={(pointer == null ? "null" : $"0x{((nint)pointer).ToInt64():X}")}");
            }
            catch (Exception ex)
            {
                builder.AppendLine($"  AgentId={agent.Id} {agent.Name} | 读取失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        builder.AppendLine()
            .AppendLine("XBM AddOns:");
        foreach (var addonName in XbmAddonNames())
        {
            DumpAddon(builder, addonName);
            builder.AppendLine();
        }

        builder.AppendLine("Item Action 状态探针（只读）:");
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            builder.AppendLine("  ActionManager 不可用。");
        }
        else
        {
            foreach (var item in new[]
            {
                (Id: 76u, Name: "1级魔兽恢复药"),
                (Id: 77u, Name: "2级魔兽恢复药"),
                (Id: 78u, Name: "3级魔兽恢复药"),
                (Id: 243136u, Name: "1级恢复药图标"),
                (Id: 243137u, Name: "2级恢复药图标"),
                (Id: 243138u, Name: "3级恢复药图标"),
            })
            {
                var itemStatus = actionManager->GetActionStatus(ActionType.Item, item.Id, 0);
                var actionStatus = actionManager->GetActionStatus(ActionType.Action, item.Id, 0);
                builder.AppendLine($"  {item.Name} Id={item.Id} | ItemType状态={itemStatus} | ActionType状态={actionStatus}");
            }
        }

        builder.AppendLine("提示: 请在打开奇弈道具界面后读取；重点查看 AtkValue 中疑似 ItemId、数量或恢复药名称的字段。");
        return builder.ToString().TrimEnd();
    }

    private static (uint Id, string Name)[] XbmAgents()
        =>
        [
            (497, "XBMContentsMainHUD"),
            (498, "XBMItemDetail"),
            (499, "XBMBattleMonsterDetail"),
            (500, "XBMMonsterNotebook"),
            (501, "XBMPetParty"),
            (502, "XBMStageDetailList"),
            (503, "XBMStageList"),
            (504, "XBMStageMap"),
            (505, "XBMResult"),
            (506, "XBMRanking"),
        ];

    private static string[] XbmAddonNames()
        =>
        [
            "XBMContentsMainHUD",
            "XBMItemDetail",
            "XBMBattleMonsterDetail",
            "XBMMonsterNotebook",
            "XBMPetParty",
            "XBMStageDetailList",
            "XBMStageList",
            "XBMStageMap",
            "XBMResult",
            "XBMRanking",
        ];

    private static void DumpAddon(StringBuilder builder, string addonName)
    {
        try
        {
            var addon = DalamudApi.GameGui.GetAddonByName(addonName);
            builder.AppendLine($"Addon={addonName}");
            if (addon.IsNull)
            {
                builder.AppendLine("  状态: 不存在");
                return;
            }

            builder.AppendLine($"  Address=0x{addon.Address.ToInt64():X}");
            builder.AppendLine($"  Name={addon.Name}");
            builder.AppendLine($"  Id={addon.Id} | ParentId={addon.ParentId} | HostId={addon.HostId}");
            builder.AppendLine($"  Ready={addon.IsReady} | Visible={addon.IsVisible}");
            builder.AppendLine($"  AtkValuesCount={addon.AtkValuesCount}");

            if (!addon.IsReady)
            {
                return;
            }

            var index = 0;
            foreach (var value in addon.AtkValues.Take(300))
            {
                string renderedValue;
                try
                {
                    renderedValue = value.GetValue()?.ToString() ?? "<null>";
                }
                catch (Exception ex)
                {
                    renderedValue = $"<读取失败: {ex.GetType().Name}>";
                }

                builder.AppendLine($"  Value[{index++}] Type={value.ValueType} Value={renderedValue}");
            }

            if (addon.AtkValuesCount > 300)
            {
                builder.AppendLine($"  AtkValues 超过 300，仅输出前 300 项。");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"Addon={addonName}");
            builder.AppendLine($"  读取失败: {ex.GetType().Name}: {ex.Message}");
        }
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

    public unsafe string GetActionStatusDebug(uint actionId, bool useAdjustedActionId)
    {
        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return "类型: 技能状态\nActionManager 不可用。";
        }

        var target = DalamudApi.TargetManager.Target;
        var targetId = target?.GameObjectId ?? 0xE0000000UL;
        var availability = BeastmasterActionHelper.GetAvailability(actionId, targetId, useAdjustedActionId);
        if (availability.ActionId == 0)
        {
            return new StringBuilder()
                .AppendLine("类型: 技能状态")
                .AppendLine($"输入 ActionId: {actionId}")
                .AppendLine($"使用 GetAdjustedActionId: {(useAdjustedActionId ? "是" : "否")}")
                .AppendLine($"技能: {availability.ActionName}")
                .AppendLine("能否使用: 否")
                .AppendLine($"判定: {availability.Reason}")
                .ToString()
                .TrimEnd();
        }

        var resolvedActionId = availability.ActionId;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var targetDistance = player != null && target != null
            ? Vector3.Distance(player.Position, target.Position)
            : (float?)null;
        var gauge = BeastmasterGaugeSnapshot.Read();
        var summon = gauge.SummonEntry;

        var recastTotal = 0f;
        var recastElapsed = 0f;
        var recastActive = false;
        var recastRemaining = 0f;
        var actionRange = 0f;
        try
        {
            recastTotal = manager->GetRecastTime(ActionType.Action, resolvedActionId);
            recastElapsed = manager->GetRecastTimeElapsed(ActionType.Action, resolvedActionId);
            recastActive = manager->IsRecastTimerActive(ActionType.Action, resolvedActionId);
            recastRemaining = recastActive ? Math.Max(0f, recastTotal - recastElapsed) : 0f;
            actionRange = ActionManager.GetActionRange(resolvedActionId);
        }
        catch
        {
            return new StringBuilder()
                .AppendLine("类型: 技能状态")
                .AppendLine($"输入 ActionId: {actionId}")
                .AppendLine($"使用 GetAdjustedActionId: {(useAdjustedActionId ? "是" : "否")}")
                .AppendLine($"实际 ActionId: {resolvedActionId}")
                .AppendLine($"技能: {availability.ActionName}")
                .AppendLine("能否使用: 否")
                .AppendLine("判定: 调用原生 API 时发生异常，ActionId 可能无法用于当前状态")
                .ToString()
                .TrimEnd();
        }

        return new StringBuilder()
            .AppendLine("类型: 技能状态")
            .AppendLine($"输入 ActionId: {actionId}")
            .AppendLine($"使用 GetAdjustedActionId: {(useAdjustedActionId ? "是" : "否")}")
            .AppendLine($"实际 ActionId: {resolvedActionId}")
            .AppendLine($"技能: {availability.ActionName}")
            .AppendLine($"能否使用: {(availability.CanUse ? "是" : "否")}")
            .AppendLine($"判定: {availability.Reason}")
            .AppendLine($"当前 CD: {recastRemaining:0.###}s / {recastTotal:0.###}s（已过 {recastElapsed:0.###}s）")
            .AppendLine($"技能射程: {actionRange:0.###} yalms")
            .AppendLine(targetDistance.HasValue
                ? $"当前目标距离: {targetDistance.Value:0.###} yalms | {target!.Name.TextValue}"
                : "当前目标距离: 无当前目标或本地角色")
            .AppendLine(summon != null
                ? $"当前魔兽: {summon.Name} | 图鉴 {summon.Number:00} | DataId={gauge.SummonDataId}"
                : $"当前魔兽: {(string.IsNullOrWhiteSpace(gauge.SummonName) ? "未识别/未召唤" : gauge.SummonName)}")
            .ToString()
            .TrimEnd();
    }

    public unsafe string GetCooperationValidationDebug()
    {
        var gauge = BeastmasterGaugeSnapshot.ReadRaw();
        var target = DalamudApi.TargetManager.Target as IBattleChara;
        var manager = ActionManager.Instance();
        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师协力验证数据")
            .AppendLine("模式: 只读，不释放技能")
            .AppendLine($"量谱: {(gauge.Available ? "可用" : gauge.Status)}")
            .AppendLine($"技力: {gauge.Tp}/250")
            .AppendLine($"兽力: {gauge.BeastPower}/250")
            .AppendLine($"御兽之心: {gauge.BeastHeartStacks} 层")
            .AppendLine($"兽灵之心: {gauge.BeastSoulStacks} 层")
            .AppendLine($"当前兽笛: {(gauge.WhistleIndex is >= 1 and <= 3 ? $"{gauge.WhistleIndex} 号" : "未召唤")}");

        var entry = gauge.SummonEntry;
        if (entry == null)
        {
            builder.AppendLine("当前魔兽: 未识别");
        }
        else
        {
            builder.AppendLine($"当前魔兽: {entry.Name}");
            builder.AppendLine($"属性: {entry.Attribute}");
            builder.AppendLine($"宠物大招资料: {entry.UltimateActionId} | {GetActionNameById(entry.UltimateActionId)}");
            builder.AppendLine($"释放资料: {entry.ReleaseActionId} | {GetActionNameById(entry.ReleaseActionId)}");
        }

        if (target == null)
        {
            builder.AppendLine("目标: 无有效 BattleNpc");
        }
        else
        {
            builder.AppendLine($"目标: {target.Name.TextValue} | EntityId={target.EntityId} | BaseId={target.BaseId}");
            builder.AppendLine($"目标 HP: {target.CurrentHp}/{target.MaxHp} | 可选中={target.IsTargetable} | 死亡={target.IsDead}");
        }

        if (manager == null || target == null)
        {
            builder.AppendLine("Action 状态: ActionManager 或目标不可用");
        }
        else
        {
            builder.AppendLine("Action 状态:");
            foreach (var actionId in new uint[] { 47093, 44884, 44887, 44888, 44889 })
            {
                var status = manager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
                builder.AppendLine($"  ActionId={actionId} | {GetActionNameById(actionId)} | 状态码={status}");
            }
        }

        builder.AppendLine("自身属性状态:");
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            builder.AppendLine("  本地角色不可用");
        }
        else
        {
            foreach (var status in player.StatusList.Where(status => status.StatusId is >= 4595 and <= 4600))
            {
                builder.AppendLine($"  StatusId={status.StatusId} | SourceId={status.SourceId} | 剩余={status.RemainingTime:0.0}s");
            }
        }

        return builder.ToString().TrimEnd();
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

    private static string GetActionNameById(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action)
            ? GetActionName(action)
            : "未找到";

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

    public unsafe string GetCaptureCheckDebug()
    {
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return "类型: 目标捕获判定\n角色未加载。";
        }

        if (player.ClassJob.RowId != 43)
        {
            return "类型: 目标捕获判定\n当前职业不是驯兽师。";
        }

        if (DalamudApi.TargetManager.Target is not IBattleChara target)
        {
            return "类型: 目标捕获判定\n当前未选择有效目标。";
        }

        var manager = ActionManager.Instance();
        if (manager == null)
        {
            return "类型: 目标捕获判定\nActionManager 不可用。";
        }

        var captureActionId = 44880u;

        var actionStatus = captureActionId != 0
            ? manager->GetActionStatus(ActionType.Action, captureActionId, target.GameObjectId)
            : 0xFFFFFFFF;

        var statuses = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        var hpPercent = target.MaxHp == 0
            ? 100f
            : target.CurrentHp * 100f / target.MaxHp;

        var builder = new StringBuilder()
            .AppendLine("类型: 目标捕获判定")
            .AppendLine($"目标: {target.Name.TextValue} | BaseId={target.BaseId}")
            .AppendLine($"HP: {target.CurrentHp}/{target.MaxHp} ({hpPercent:0.#}%)")
            .AppendLine($"可选中: {target.IsTargetable} | 已死亡: {(target.IsDead || target.CurrentHp == 0)}")
            .AppendLine($"捕获 ActionId: {(captureActionId != 0 ? captureActionId.ToString() : "未找到")}")
            .AppendLine($"GetActionStatus 状态码: {(actionStatus == 0 ? "0（可用）" : actionStatus.ToString())}")
            .AppendLine($"游戏判定能否捕获: {(actionStatus == 0 ? "可以" : "不可以")}")
            .AppendLine()
            .AppendLine("目标当前状态列表:");

        if (!target.StatusList.Any())
        {
            builder.AppendLine("  （无状态）");
        }
        else
        {
            foreach (var s in target.StatusList.OrderBy(s => s.StatusId))
            {
                var statusName = statuses?.TryGetRow(s.StatusId, out var row) == true
                    ? row.Name.ExtractText()
                    : "";
                var isSource = s.SourceId == player.EntityId;
                builder.AppendLine($"  StatusId={s.StatusId} | {statusName} | 剩余={s.RemainingTime:0.0}s | 来源={(isSource ? "自身" : $"他人({s.SourceId})")}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string GetAutoOutputConditionDebug()
    {
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var builder = new StringBuilder()
            .AppendLine("类型: 自动输出人物状态诊断")
            .AppendLine($"已登录: {DalamudApi.ClientState.IsLoggedIn}")
            .AppendLine($"角色已加载: {player != null}")
            .AppendLine($"职业 ID: {DalamudApi.PlayerState.ClassJob.RowId}")
            .AppendLine($"BetweenAreas: {DalamudApi.Condition[ConditionFlag.BetweenAreas]}")
            .AppendLine($"Mounted: {DalamudApi.Condition[ConditionFlag.Mounted]}")
            .AppendLine($"OccupiedInCutSceneEvent: {DalamudApi.Condition[ConditionFlag.OccupiedInCutSceneEvent]}")
            .AppendLine($"InCombat: {DalamudApi.Condition[ConditionFlag.InCombat]}");

        if (player == null)
        {
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"角色 HP: {player.CurrentHp}/{player.MaxHp}")
            .AppendLine($"角色读条: {player.IsCasting}")
            .AppendLine($"角色对象类型: {player.ObjectKind}")
            .AppendLine($"当前目标: {DalamudApi.TargetManager.Target?.Name.TextValue ?? "无"}");
        return builder.ToString().TrimEnd();
    }

    public unsafe string GetSkillSequenceValidationDebug()
    {
        const uint borrowActionId = 44895;
        const uint beastSkillActionId = 44886;
        const uint releaseActionId = 44890;
        const uint beastHideActionId = 44896;
        const uint shieldChargeActionId = 44893;

        var gauge = BeastmasterGaugeSnapshot.Read();
        var countdown = countdownService.Snapshot;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var target = DalamudApi.TargetManager.Target;
        var manager = ActionManager.Instance();
        var builder = new StringBuilder()
            .AppendLine("类型: 技能序列验证数据")
            .AppendLine("模式: 只读，不释放技能")
            .AppendLine($"已登录: {DalamudApi.ClientState.IsLoggedIn}")
            .AppendLine($"职业 ID: {DalamudApi.PlayerState.ClassJob.RowId}")
            .AppendLine($"InCombat: {DalamudApi.Condition[ConditionFlag.InCombat]}")
            .AppendLine($"BetweenAreas: {DalamudApi.Condition[ConditionFlag.BetweenAreas]}")
            .AppendLine($"角色 HP: {(player == null ? "未加载" : $"{player.CurrentHp}/{player.MaxHp}")}")
            .AppendLine($"角色读条: {player?.IsCasting}")
            .AppendLine($"当前目标: {target?.Name.TextValue ?? "无"} | GameObjectId={(target?.GameObjectId.ToString() ?? "0")}")
            .AppendLine($"当前兽笛: {(gauge.WhistleIndex is >= 1 and <= 3 ? gauge.WhistleIndex.ToString() : "未召唤")}")
            .AppendLine($"当前魔兽: {(gauge.SummonEntry?.Name ?? gauge.SummonName)} | DataId={gauge.SummonDataId}")
            .AppendLine($"原生倒计时可用: {countdown.Available} | 状态={countdown.Status}")
            .AppendLine($"原生倒计时激活: {countdown.Active} | 剩余={countdown.TimeRemaining:0.000}s | 发起者={countdown.Initiator}")
            .AppendLine($"缓存采样时间 UTC: {(countdownService.LastPolledUtc == DateTime.MinValue ? "未采样" : countdownService.LastPolledUtc.ToString("O"))}")
            .AppendLine($"最近倒计时边沿: {countdownService.LastTransition} | 时间 UTC={(countdownService.LastTransitionUtc == DateTime.MinValue ? "无" : countdownService.LastTransitionUtc.ToString("O"))}")
            .AppendLine();

        if (manager == null)
        {
            builder.AppendLine("ActionManager: 不可用");
        }
        else
        {
            AppendSequenceAction(builder, manager, "借用", borrowActionId, 0);
            AppendSequenceAction(builder, manager, "魔兽技", beastSkillActionId, 0);
            AppendSequenceAction(builder, manager, "释放", releaseActionId, target?.GameObjectId ?? 0);
            AppendSequenceAction(builder, manager, "百兽肤期望结果", beastHideActionId, 0, adjust: false);
            AppendSequenceAction(builder, manager, "盾牌冲击", shieldChargeActionId, target?.GameObjectId ?? 0, adjust: false);
        }

        return builder.ToString().TrimEnd();
    }

    private static unsafe void AppendSequenceAction(
        StringBuilder builder,
        ActionManager* manager,
        string label,
        uint actionId,
        ulong targetId,
        bool adjust = true)
    {
        var adjustedActionId = adjust ? manager->GetAdjustedActionId(actionId) : actionId;
        var status = adjustedActionId == 0
            ? uint.MaxValue
            : manager->GetActionStatus(ActionType.Action, adjustedActionId, targetId);
        builder.AppendLine(
            $"{label}: Base={actionId} | Adjusted={adjustedActionId} | {GetActionNameById(adjustedActionId)} | Target={targetId} | Status={status}");
    }

    public unsafe string GetXbmItemStructureProbe()
    {
        var builder = new StringBuilder()
            .AppendLine("类型: XBM 道具结构")
            .AppendLine("模式: 只读，不触发回调，不使用道具，不写入内存")
            .AppendLine("目标: 读取 XBMContentsMainHUD 道具字段，定位恢复药显示槽位")
            .AppendLine($"TerritoryType: {DalamudApi.ClientState.TerritoryType}")
            .AppendLine($"InCombat: {DalamudApi.Condition[ConditionFlag.InCombat]}")
            .AppendLine();

        var addon = DalamudApi.GameGui.GetAddonByName("XBMContentsMainHUD", 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            builder.AppendLine("XBMContentsMainHUD 不存在或不可见。");
            builder.AppendLine("提示: 请先进入斗兽奇弈并打开奇弈道具界面。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"XBMContentsMainHUD Address=0x{addon.Address.ToInt64():X}");
        builder.AppendLine($"  IsReady={addon.IsReady} | IsVisible={addon.IsVisible}");
        builder.AppendLine($"  AtkValuesCount={addon.AtkValuesCount}");
        builder.AppendLine();

        builder.AppendLine("AtkValues 完整列表:");
        var index = 0;
        foreach (var value in addon.AtkValues)
        {
            string renderedValue;
            try
            {
                renderedValue = value.GetValue()?.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                renderedValue = $"<读取失败: {ex.GetType().Name}>";
            }
            builder.AppendLine($"  Value[{index++}] Type={value.ValueType} Value={renderedValue}");
            if (index >= 300) break;
        }

        if (addon.AtkValuesCount > 300)
        {
            builder.AppendLine($"  AtkValues 超过 300，仅输出前 300 项。");
        }

        builder.AppendLine();
        builder.AppendLine("提示: 已知恢复药分组结构: Value[N+0]=Bool(存在) Value[N+1]=Bool(可用) Value[N+2]=UInt(iconId) Value[N+3]=UInt(itemId) Value[N+4]=String(name)");
        builder.AppendLine("提示: 实际使用通过 RaptureHotbarModule.ExecuteSlot 执行显示槽位，不使用 FireCallback。");
        return builder.ToString().TrimEnd();
    }

    public unsafe string GetCrucibleItemList()
    {
        var builder = new StringBuilder()
            .AppendLine("类型: 奇弈道具列表")
            .AppendLine("模式: 只读，不触发回调，不使用道具，不写入内存")
            .AppendLine("目标: 列出当前界面中所有奇弈道具的 ID 和名称")
            .AppendLine($"TerritoryType: {DalamudApi.ClientState.TerritoryType}")
            .AppendLine();

        var addon = DalamudApi.GameGui.GetAddonByName("XBMContentsMainHUD", 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            builder.AppendLine("XBMContentsMainHUD 不存在或不可见。");
            builder.AppendLine("提示: 请先进入斗兽奇弈并打开奇弈道具界面。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"XBMContentsMainHUD Address=0x{addon.Address.ToInt64():X}");
        builder.AppendLine($"  AtkValuesCount={addon.AtkValuesCount}");
        builder.AppendLine();

        var atkValues = addon.AtkValues.ToArray();
        var itemCount = 0;
        var itemIds = new HashSet<uint>();
        var index = 9;
        builder.AppendLine("奇弈道具列表:");
        while (index + 4 < atkValues.Length)
        {
            try
            {
                var exists = atkValues[index].GetValue()?.ToString() == "True";
                var usable = atkValues[index + 1].GetValue()?.ToString() == "True";
                var iconId = atkValues[index + 2].GetValue();
                var itemIdRaw = atkValues[index + 3].GetValue();
                var name = atkValues[index + 4].GetValue()?.ToString() ?? string.Empty;

                if (itemIdRaw != null
                    && uint.TryParse(itemIdRaw.ToString(), out var itemId)
                    && itemId is >= 76 and <= 143
                    && itemIds.Add(itemId))
                {
                    var status = exists ? (usable ? "可用" : "不可用") : "不存在";
                    builder.AppendLine($"  [{itemId}] {name} | 状态={status} | 图标={iconId}");
                    itemCount++;
                }
            }
            catch
            {
                // Skip invalid entries
            }

            index += 5;
        }

        if (itemCount == 0)
        {
            builder.AppendLine("  未找到奇弈道具。");
            builder.AppendLine("提示: 请先进入斗兽奇弈并打开奇弈道具界面。");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine($"共找到 {itemCount} 个奇弈道具。");
        }

        return builder.ToString().TrimEnd();
    }

    public unsafe string GetXbmModuleProbe()
    {
        const uint maximumDumpSize = 0x400;
        const int structSize = 0xA8;
        var builder = new StringBuilder()
            .AppendLine("类型: 驯兽师养成数据模块探针")
            .AppendLine("模式: 只读，不写入内存，不触发回调")
            .AppendLine("目标: 定位 50 只宝宝等级/经验的常驻数据结构（XBMModule）")
            .AppendLine("采集方法: 建议打开魔兽图鉴或魔兽编队后再读取；在宝宝获得经验前后各采集一次并对比差异")
            .AppendLine();

        var module = XBMModule.Instance();
        if (module == null)
        {
            builder.AppendLine("XBMModule 不可用（未登录或游戏界面未加载）。");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"XBMModule Address=0x{(nint)module:X}")
            .AppendLine($"CharacterContentId={module->CharacterContentId}")
            .AppendLine($"FileName=\"{module->FileNameString}\"")
            .AppendLine($"TempDataPtr=0x{module->TempDataPtr:X}")
            .AppendLine($"TempDataBytesWritten=0x{module->TempDataBytesWritten:X} ({module->TempDataBytesWritten})")
            .AppendLine($"GetDataSize()={module->GetDataSize()} | GetFileSize()={module->GetFileSize()} | GetFileVersion()={module->GetFileVersion()} | GetFileType()=0x{module->GetFileType():X}")
            .AppendLine($"HasChanges={module->HasChanges} | IsSavePending={module->IsSavePending} | IsVirtual={module->IsVirtual}");

        var structBytes = (byte*)module;
        builder.AppendLine().AppendLine($"XBMModule 结构内存（{structSize} 字节，4 字节整数视图）:");
        for (var offset = 0; offset < structSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X2}:");
            for (var column = 0; column < 16 && offset + column + 3 < structSize; column += 4)
            {
                var value = *(uint*)(structBytes + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }

        builder.AppendLine().AppendLine($"XBMModule 结构内存（{structSize} 字节，字节视图）:");
        for (var offset = 0; offset < structSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X2}:");
            for (var column = 0; column < 16 && offset + column < structSize; column++)
            {
                builder.Append($" {structBytes[offset + column]:X2}");
            }
            builder.AppendLine();
        }

        AppendPointerDump(builder, structBytes, 0x48, "结构 +0x48 指针");
        AppendPointerDump(builder, structBytes, 0x58, "结构 +0x58 指针");

        var dataPtr = module->TempDataPtr;
        var dataSize = module->TempDataBytesWritten;
        if (dataPtr == 0 || dataSize == 0)
        {
            builder.AppendLine().AppendLine("TempDataPtr 为空：养成数据尚未加载。");
            builder.AppendLine("请先在游戏内打开魔兽图鉴（/魔兽图鉴）或魔兽编队窗口，让数据加载后再重新读取。");
            return builder.ToString().TrimEnd();
        }

        var dumpSize = (int)Math.Min(dataSize, maximumDumpSize);
        var bytes = (byte*)dataPtr;
        builder.AppendLine().AppendLine($"TempDataPtr 缓冲区 4 字节整数视图（前 {dumpSize}/{dataSize} 字节）:");
        for (var offset = 0; offset + 3 < dumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16 && offset + column + 3 < dumpSize; column += 4)
            {
                var value = *(uint*)(bytes + offset + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }

        builder.AppendLine().AppendLine($"TempDataPtr 缓冲区字节视图（前 {dumpSize}/{dataSize} 字节）:");
        for (var offset = 0; offset < dumpSize; offset += 16)
        {
            builder.Append($"  +0x{offset:X3}:");
            for (var column = 0; column < 16 && offset + column < dumpSize; column++)
            {
                builder.Append($" {bytes[offset + column]:X2}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static unsafe void AppendPointerDump(StringBuilder builder, byte* basePointer, int offset, string label)
    {
        const int pointerDumpSize = 0x100;
        var pointer = *(nint*)(basePointer + offset);
        builder.AppendLine().AppendLine($"{label}=0x{pointer:X}");
        if (pointer == 0)
        {
            builder.AppendLine("  指针为空。");
            return;
        }

        var bytes = (byte*)pointer;
        builder.AppendLine($"  4 字节整数视图（{pointerDumpSize} 字节）:");
        for (var i = 0; i < pointerDumpSize; i += 16)
        {
            builder.Append($"    +0x{i:X3}:");
            for (var column = 0; column < 16; column += 4)
            {
                var value = *(uint*)(bytes + i + column);
                builder.Append($" {value,10}");
            }
            builder.AppendLine();
        }

        builder.AppendLine($"  字节视图（{pointerDumpSize} 字节）:");
        for (var i = 0; i < pointerDumpSize; i += 16)
        {
            builder.Append($"    +0x{i:X3}:");
            for (var column = 0; column < 16; column++)
            {
                builder.Append($" {bytes[i + column]:X2}");
            }
            builder.AppendLine();
        }
    }

    private static string JoinLines(params string[] lines)
        => string.Join(Environment.NewLine, lines);

    private static string NormalizeDutyName(string name)
        => new(name.Where(character => !char.IsWhiteSpace(character)
            && character is not '·' and not '：' and not ':').ToArray());
}
