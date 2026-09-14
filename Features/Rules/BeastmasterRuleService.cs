using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Beastmaster;

public sealed class BeastmasterRuleService
{
    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterCrucibleItemService crucibleItemService;
    private DateTime lastChatUtc = DateTime.MinValue;
    private string lastChatKey = string.Empty;
    private readonly Dictionary<string, string> lastFailureMessages = new(StringComparer.Ordinal);

    public BeastmasterRuleService(BeastmasterConfiguration configuration, BeastmasterCrucibleItemService crucibleItemService)
    {
        this.configuration = configuration;
        this.crucibleItemService = crucibleItemService;
    }

    public bool Enabled => configuration.RuleModeEnabled;
    public string LastDiagnostic { get; private set; } = "规则模式未启用";
    public DateTime LastDiagnosticUtc { get; private set; } = DateTime.MinValue;

    public void SetEnabled(bool enabled)
    {
        configuration.RuleModeEnabled = enabled;
        configuration.Save();
        RecordDiagnostic(enabled ? "规则模式已启用，等待条件命中" : "规则模式未启用");
    }

    public unsafe bool TryHandle(ActionManager* actionManager, IBattleChara player, IBattleChara? target, DateTime now)
    {
        if (!Enabled || !DalamudApi.Condition[ConditionFlag.InCombat])
        {
            return false;
        }

        var territoryId = (ushort)Math.Clamp(DalamudApi.ClientState.TerritoryType, 0u, ushort.MaxValue);
        var validRuleCount = 0;
        foreach (var ruleSet in configuration.RuleSets)
        {
            if (!ruleSet.Enabled || !ruleSet.AppliesTo(territoryId))
            {
                continue;
            }

            for (var ruleIndex = 0; ruleIndex < ruleSet.Rules.Count; ruleIndex++)
            {
                var rule = ruleSet.Rules[ruleIndex];
                if (!rule.Enabled || !rule.TryValidate(out _))
                {
                    continue;
                }

                validRuleCount++;

                if (!Matches(rule, player, target, out var matchReason))
                {
                    continue;
                }

                return TryExecute(actionManager, ruleSet, rule, ruleIndex, player, target, matchReason, now);
            }
        }

        if (configuration.RuleDiagnosticsEnabled)
        {
            var message = validRuleCount == 0
                ? $"规则诊断：当前区域 {territoryId} 没有可执行的有效规则"
                : $"规则诊断：已检查 {validRuleCount} 条规则，当前没有条件命中";
            RecordDiagnostic(message);
            if (validRuleCount > 0)
            {
                var diagnosticRuleSet = configuration.RuleSets.FirstOrDefault(set => set.Enabled
                    && set.AppliesTo(territoryId)
                    && set.DiagnosticMode == BeastmasterRuleDiagnosticMode.Full);
                if (diagnosticRuleSet != null)
                {
                    PrintChat($"{diagnosticRuleSet.Name}|未命中", message, now, TimeSpan.FromSeconds(2));
                }
            }
        }

        return false;
    }

    private unsafe bool TryExecute(
        ActionManager* actionManager,
        BeastmasterRuleSetDefinition ruleSet,
        BeastmasterRuleDefinition rule,
        int ruleIndex,
        IBattleChara player,
        IBattleChara? target,
        string matchReason,
        DateTime now)
    {
        if (rule.ActionType == BeastmasterRuleActionType.CrucibleItem)
        {
            return TryExecuteCrucibleItem(ruleSet, rule, ruleIndex, target, matchReason, now);
        }

        var targetId = BeastmasterRuleActions.RequiresTarget(rule.ActionId)
            ? target?.GameObjectId ?? 0UL
            : 0UL;
        if (BeastmasterRuleActions.RequiresTarget(rule.ActionId) && targetId == 0)
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "所选技能需要有效的当前目标", now);
            return false;
        }

        var availability = BeastmasterActionHelper.GetAvailability(
            rule.ActionId,
            targetId,
            BeastmasterRuleActions.UsesAdjustedActionId(rule.ActionId));
        if (!availability.CanUse)
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, availability.Reason, now);
            return false;
        }

        if (target != null
            && !BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                availability.ActionId,
                out var distance,
                out var actionRange))
        {
            var rangeReason = $"等待进入技能射程（当前 {distance:0.##}/{actionRange:0.##} yalms）";
            Fail(ruleSet, rule, ruleIndex, matchReason,
                rangeReason,
                now);
            return false;
        }

        if (!actionManager->UseAction(ActionType.Action, availability.ActionId, targetId))
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "技能请求失败", now);
            return false;
        }

        var message = $"规则集“{ruleSet.Name}”第 {ruleIndex + 1} 条“{rule.Name}”命中：{matchReason}；已请求 {availability.ActionName}（{availability.ActionId}）";
        RecordDiagnostic(message);
        lastFailureMessages.Remove($"{ruleSet.Name}|{rule.Name}");
        if (configuration.RuleDiagnosticsEnabled
            && ruleSet.DiagnosticMode == BeastmasterRuleDiagnosticMode.Full)
        {
            PrintChat($"{ruleSet.Name}|{rule.Name}|成功", message, now, TimeSpan.FromSeconds(2));
        }
        return true;
    }

    private bool TryExecuteCrucibleItem(
        BeastmasterRuleSetDefinition ruleSet,
        BeastmasterRuleDefinition rule,
        int ruleIndex,
        IBattleChara? target,
        string matchReason,
        DateTime now)
    {
        if (rule.CrucibleItemType == BeastmasterCrucibleItemType.Fang && target == null)
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "奇弈道具需要有效的当前目标", now);
            return false;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer as IBattleChara;
        if (player == null || !crucibleItemService.TryUseCrucibleItemOnTarget(
                rule.CrucibleItemType, player, target!, now, out var itemId))
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, crucibleItemService.LastFailureReason, now);
            return false;
        }

        var itemName = BeastmasterRuleActions.GetCrucibleItemName(itemId);
        var message = $"规则集“{ruleSet.Name}”第 {ruleIndex + 1} 条“{rule.Name}”命中：{matchReason}；已使用 {itemName}（{itemId}）";
        RecordDiagnostic(message);
        lastFailureMessages.Remove($"{ruleSet.Name}|{rule.Name}");
        if (configuration.RuleDiagnosticsEnabled
            && ruleSet.DiagnosticMode == BeastmasterRuleDiagnosticMode.Full)
        {
            PrintChat($"{ruleSet.Name}|{rule.Name}|成功", message, now, TimeSpan.FromSeconds(2));
        }
        return true;
    }

    private void Fail(
        BeastmasterRuleSetDefinition ruleSet,
        BeastmasterRuleDefinition rule,
        int ruleIndex,
        string matchReason,
        string failureReason,
        DateTime now)
    {
        var message = $"规则集“{ruleSet.Name}”第 {ruleIndex + 1} 条“{rule.Name}”命中：{matchReason}；{failureReason}，已回退 ACR";
        RecordDiagnostic(message);
        if (configuration.RuleDiagnosticsEnabled
            && ruleSet.DiagnosticMode != BeastmasterRuleDiagnosticMode.Off)
        {
            PrintFailureChat($"{ruleSet.Name}|{rule.Name}", failureReason, message);
        }
    }

    private static bool Matches(
        BeastmasterRuleDefinition rule,
        IBattleChara player,
        IBattleChara? target,
        out string reason)
    {
        reason = string.Empty;
        switch (rule.ConditionType)
        {
            case BeastmasterRuleConditionType.SelfStatus:
                return MatchesStatus(rule, player.StatusList.Any(status => status.StatusId == rule.ConditionId), "自身", out reason);
            case BeastmasterRuleConditionType.TargetStatus:
                return target != null
                    && MatchesStatus(rule, target.StatusList.Any(status => status.StatusId == rule.ConditionId), "当前目标", out reason);
            case BeastmasterRuleConditionType.TargetCast:
                if (target is { IsCasting: true } && target.CastActionId == rule.ConditionId)
                {
                    reason = $"当前目标正在读条 {rule.ConditionId}";
                    return true;
                }
                return false;
            case BeastmasterRuleConditionType.DataIdStatus:
            {
                var actors = FindDataIdActors(rule.DataId);
                var matched = rule.StatusCondition == BeastmasterRuleStatusCondition.Present
                    ? actors.Any(actor => actor.StatusList.Any(status => status.StatusId == rule.ConditionId))
                    : actors.Any(actor => actor.StatusList.All(status => status.StatusId != rule.ConditionId));
                if (matched)
                {
                    reason = $"{actors.Count} 个 DataID {rule.DataId} 对象中有对象{GetStatusConditionText(rule.StatusCondition)} BUFF {rule.ConditionId}";
                }
                return matched;
            }
            case BeastmasterRuleConditionType.DataIdCast:
            {
                var actors = FindDataIdActors(rule.DataId);
                if (actors.Any(actor => actor.IsCasting && actor.CastActionId == rule.ConditionId))
                {
                    reason = $"{actors.Count} 个 DataID {rule.DataId} 对象中有对象正在读条 {rule.ConditionId}";
                    return true;
                }
                return false;
            }
            case BeastmasterRuleConditionType.TargetDataId:
            {
                if (target == null)
                {
                    return false;
                }

                if (target.BaseId == rule.DataId)
                {
                    reason = $"当前目标 DataID 匹配 {rule.DataId}";
                    return true;
                }
                return false;
            }
            default:
                return false;
        }
    }

    private static bool MatchesStatus(BeastmasterRuleDefinition rule, bool hasStatus, string actor, out string reason)
    {
        var matched = rule.StatusCondition == BeastmasterRuleStatusCondition.Present ? hasStatus : !hasStatus;
        reason = matched ? $"{actor}{GetStatusConditionText(rule.StatusCondition)} BUFF {rule.ConditionId}" : string.Empty;
        return matched;
    }

    private static List<IBattleChara> FindDataIdActors(uint dataId)
        => DalamudApi.ObjectTable
            .OfType<IBattleChara>()
            .Where(actor => actor.ObjectKind == ObjectKind.BattleNpc
                && actor.BaseId == dataId
                && !actor.IsDead
                && actor.CurrentHp > 0)
            .ToList();

    private static string GetStatusConditionText(BeastmasterRuleStatusCondition condition)
        => condition == BeastmasterRuleStatusCondition.Present ? "存在" : "缺少";

    private void RecordDiagnostic(string message)
    {
        LastDiagnostic = message;
        LastDiagnosticUtc = DateTime.UtcNow;
    }

    private void PrintChat(
        string key,
        string message,
        DateTime now,
        TimeSpan interval)
    {
        if (key == lastChatKey && now - lastChatUtc < interval)
        {
            return;
        }

        lastChatKey = key;
        lastChatUtc = now;
        DalamudApi.ChatGui.Print($"[驯兽师助手 {DateTime.Now:HH:mm:ss}] {message}");
    }

    private void PrintFailureChat(string ruleKey, string failureReason, string message)
    {
        if (lastFailureMessages.TryGetValue(ruleKey, out var previousReason)
            && previousReason == failureReason)
        {
            return;
        }

        lastFailureMessages[ruleKey] = failureReason;
        DalamudApi.ChatGui.Print($"[驯兽师助手 {DateTime.Now:HH:mm:ss}] {message}");
    }
}
