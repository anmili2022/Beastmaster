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
        var matchedRuleCount = 0;
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

                if (!MatchesRule(rule, player, target, out var matchReason))
                {
                    continue;
                }

                matchedRuleCount++;
                if (TryExecute(actionManager, ruleSet, rule, ruleIndex, player, target, matchReason, now))
                {
                    return true;
                }
            }
        }

        if (configuration.RuleDiagnosticsEnabled)
        {
            var targetSnapshot = target == null
                ? "当前无有效目标"
                : $"当前目标 BaseId={target.BaseId}，HP={target.CurrentHp}/{target.MaxHp}";
            var message = validRuleCount == 0
                ? $"规则诊断：当前区域 {territoryId} 没有可执行的有效规则"
                : matchedRuleCount == 0
                    ? $"规则诊断：已检查 {validRuleCount} 条规则，当前没有条件命中；{targetSnapshot}"
                    : $"规则诊断：{matchedRuleCount}/{validRuleCount} 条规则命中但均执行失败，已回退 ACR；{targetSnapshot}";
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

        var requestedActionId = rule.ActionId;
        var resolvedBeastSkillId = BeastmasterActionHelper.IsBeastSkillAction(requestedActionId)
            ? BeastmasterActionHelper.ResolveBeastSkillAction(actionManager)
            : 0u;
        if (BeastmasterActionHelper.IsBeastSkillAction(requestedActionId)
            && !BeastmasterActionHelper.IsBeastSkillAction(resolvedBeastSkillId))
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "魔兽技当前没有有效的借用技能（44886 未调整为 44896~44903）", now);
            return false;
        }

        var actionId = resolvedBeastSkillId != 0 ? resolvedBeastSkillId : requestedActionId;
        var targetId = BeastmasterRuleActions.RequiresTarget(requestedActionId)
            || (target != null
                && BeastmasterActionHelper.IsBeastSkillAction(requestedActionId)
                && ActionManager.CanUseActionOnTarget(actionId, (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address))
            ? target?.GameObjectId ?? 0UL
            : 0UL;
        if (BeastmasterRuleActions.RequiresTarget(requestedActionId) && targetId == 0)
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "所选技能需要有效的当前目标", now);
            return false;
        }

        var availability = BeastmasterActionHelper.GetAvailability(
            actionId,
            targetId,
            BeastmasterRuleActions.UsesAdjustedActionId(requestedActionId));
        if (BeastmasterFinalStrikeLock.IsBlocked(availability.ActionId, now))
        {
            Fail(ruleSet, rule, ruleIndex, matchReason,
                BeastmasterFinalStrikeLock.GetBlockReason(availability.ActionId, now),
                now);
            return false;
        }
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

        if (rule.ActionId == 44890)
        {
            BeastmasterFinalStrikeLock.RecordRelease(now);
        }
        else if (availability.ActionId == 44891)
        {
            BeastmasterFinalStrikeLock.RecordFinalStrike(now);
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
        var requiresTarget = rule.CrucibleItemType is BeastmasterCrucibleItemType.Fang
            or BeastmasterCrucibleItemType.VampireFang
            or BeastmasterCrucibleItemType.StarSand;
        if (requiresTarget && target == null)
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, "奇弈道具需要有效的当前目标", now);
            return false;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer as IBattleChara;
        var requestSource = new BeastmasterCrucibleItemService.RuleRequestSource(
            ruleSet.Name,
            rule.Name,
            ruleIndex);
        if (player == null || !crucibleItemService.TryUseCrucibleItemOnTarget(
                rule.CrucibleItemType, player, target, now, out var itemId, requestSource))
        {
            Fail(ruleSet, rule, ruleIndex, matchReason, crucibleItemService.LastFailureReason, now);
            return false;
        }

        var itemName = BeastmasterRuleActions.GetCrucibleItemName(itemId);
        var message = $"规则集“{ruleSet.Name}”第 {ruleIndex + 1} 条“{rule.Name}”命中：{matchReason}；已请求 {itemName}（{itemId}）";
        RecordDiagnostic(message);
        lastFailureMessages.Remove($"{ruleSet.Name}|{rule.Name}");
        if (configuration.RuleDiagnosticsEnabled
            && ruleSet.DiagnosticMode == BeastmasterRuleDiagnosticMode.Full)
        {
            PrintChat($"{ruleSet.Name}|{rule.Name}|成功", message, now, TimeSpan.FromSeconds(2));
        }
        return true;
    }

    public void ProcessCrucibleDispatchResults(DateTime now)
    {
        while (crucibleItemService.TryTakeRuleDispatchResult(out var result))
        {
            var source = result.Source;
            var ruleSet = configuration.RuleSets.FirstOrDefault(candidate => candidate.Name == source.RuleSetName);
            var diagnosticMode = ruleSet?.DiagnosticMode ?? BeastmasterRuleDiagnosticMode.Failures;
            var ruleKey = $"{source.RuleSetName}|{source.RuleName}";
            var itemName = BeastmasterRuleActions.GetCrucibleItemName(result.ItemId);
            var message = result.Success
                ? $"规则集“{source.RuleSetName}”第 {source.RuleIndex + 1} 条“{source.RuleName}”异步分派成功：{itemName}（{result.ItemId}）；{result.Detail}"
                : $"规则集“{source.RuleSetName}”第 {source.RuleIndex + 1} 条“{source.RuleName}”异步分派失败：{itemName}（{result.ItemId}）；{result.Detail}";
            RecordDiagnostic(message);

            if (result.Success)
            {
                lastFailureMessages.Remove(ruleKey);
                if (configuration.RuleDiagnosticsEnabled && diagnosticMode == BeastmasterRuleDiagnosticMode.Full)
                {
                    PrintChat($"{ruleKey}|异步成功", message, now, TimeSpan.FromSeconds(2));
                }
            }
            else if (configuration.RuleDiagnosticsEnabled && diagnosticMode != BeastmasterRuleDiagnosticMode.Off)
            {
                PrintFailureChat($"{ruleKey}|异步失败", result.Detail, message);
            }
        }
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
            case BeastmasterRuleConditionType.SelfHp:
            {
                if (player.MaxHp == 0)
                {
                    return false;
                }

                var hpPercent = player.CurrentHp * 100f / player.MaxHp;
                var matched = rule.HpCondition == BeastmasterRuleHpCondition.Above
                    ? hpPercent > rule.HpThreshold
                    : hpPercent < rule.HpThreshold;
                if (matched)
                {
                    reason = $"自身血量 {hpPercent:0.#}% {(rule.HpCondition == BeastmasterRuleHpCondition.Above ? ">" : "<")} {rule.HpThreshold:0.#}%";
                }
                return matched;
            }
            case BeastmasterRuleConditionType.TargetHp:
            {
                if (target == null || target.MaxHp == 0)
                {
                    return false;
                }

                var hpPercent = target.CurrentHp * 100f / target.MaxHp;
                var matched = rule.HpCondition == BeastmasterRuleHpCondition.Above
                    ? hpPercent > rule.HpThreshold
                    : hpPercent < rule.HpThreshold;
                if (matched)
                {
                    reason = $"目标血量 {hpPercent:0.#}% {(rule.HpCondition == BeastmasterRuleHpCondition.Above ? ">" : "<")} {rule.HpThreshold:0.#}%";
                }
                return matched;
            }
            case BeastmasterRuleConditionType.TargetIsBoss:
            {
                if (target == null || player.MaxHp == 0 || target.MaxHp == 0)
                {
                    return false;
                }

                var threshold = (double)player.MaxHp * 5d;
                var matched = target.MaxHp > threshold;
                if (matched)
                {
                    reason = $"目标最大血量 {target.MaxHp} > 自身最大血量 {player.MaxHp} × 5";
                }
                return matched;
            }
            case BeastmasterRuleConditionType.CurrentWhistle:
            {
                var currentWhistle = BeastmasterGaugeSnapshot.Read().WhistleIndex;
                var matched = currentWhistle == rule.WhistleIndex;
                if (matched)
                {
                    reason = $"当前为 {currentWhistle} 笛";
                }
                return matched;
            }
            default:
                return false;
        }
    }

    private static bool MatchesRule(
        BeastmasterRuleDefinition rule,
        IBattleChara player,
        IBattleChara? target,
        out string reason)
    {
        rule.EnsureConditions();
        var results = new List<string>(rule.Conditions.Count);
        foreach (var condition in rule.Conditions)
        {
            var conditionRule = new BeastmasterRuleDefinition
            {
                ConditionType = condition.Type,
                StatusCondition = condition.StatusCondition,
                DataId = condition.DataId,
                ConditionId = condition.ConditionId,
                HpCondition = condition.HpCondition,
                HpThreshold = condition.HpThreshold,
                WhistleIndex = condition.WhistleIndex,
            };
            if (Matches(conditionRule, player, target, out var conditionReason))
            {
                results.Add(conditionReason);
            }
            else if (rule.ConditionJoinMode == BeastmasterRuleConditionJoinMode.All)
            {
                reason = string.Empty;
                return false;
            }
        }

        var matched = rule.ConditionJoinMode == BeastmasterRuleConditionJoinMode.All
            ? results.Count == rule.Conditions.Count
            : results.Count > 0;
        reason = matched ? string.Join(rule.ConditionJoinMode == BeastmasterRuleConditionJoinMode.All ? " 且 " : " 或 ", results) : string.Empty;
        return matched;
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
