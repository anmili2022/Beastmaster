using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;
using Dalamud.Game.ClientState.Objects.Types;
using System.Globalization;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.NativeWrapper;

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

    private static string JoinLines(params string[] lines)
        => string.Join(Environment.NewLine, lines);

    private static string NormalizeDutyName(string name)
        => new(name.Where(character => !char.IsWhiteSpace(character)
            && character is not '·' and not '：' and not ':').ToArray());
}
