using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Beastmaster;

public enum BeastmasterSequenceState
{
    Idle,
    Countdown,
    WaitingForCombat,
    Combat,
}

public sealed class BeastmasterSequenceService
{
    private const uint SmashActionId = 44879;
    private const uint BiteActionId = 44883;
    private const uint ShieldActionId = 44885;
    private const uint WhistleOneActionId = 44881;
    private const uint BeastSkillActionId = 44886;
    private const uint ReleaseActionId = 44890;
    private const uint FinalStrikeActionId = 44891;
    private const uint WhistleTwoActionId = 44892;
    private const uint ShieldChargeActionId = 44893;
    private const uint WhistleThreeActionId = 44894;
    private const uint BorrowActionId = 44895;
    private const uint DrumActionId = 44905;

    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterCountdownService countdownService;
    private int countdownStep;
    private int combatStep;
    private byte pendingWhistle;
    private uint pendingBorrowedAction;
    private int pendingBorrowedActionStep = -1;
    private DateTime nextAttemptUtc = DateTime.MinValue;
    private DateTime combatDeadlineUtc = DateTime.MinValue;
    private DateTime stepDeadlineUtc = DateTime.MinValue;
    private DateTime finalCountdownAttemptDeadlineUtc = DateTime.MinValue;
    private DateTime lastHandledStartUtc = DateTime.MinValue;
    private DateTime lastChatUtc = DateTime.MinValue;
    private string lastChatMessage = string.Empty;
    private string combatStepFailure = "尚未尝试";
    private int combatStepAttempts;

    public BeastmasterSequenceService(
        BeastmasterConfiguration configuration,
        BeastmasterCountdownService countdownService)
    {
        this.configuration = configuration;
        this.countdownService = countdownService;
        lastHandledStartUtc = countdownService.LastTransitionUtc;
    }

    public bool Enabled => configuration.WaterOpenerSequenceEnabled;

    public bool ChatMessagesEnabled => configuration.SequenceChatMessagesEnabled;
    public string CurrentSequenceName
        => configuration.Sequences.Count == 0
            ? "无可用序列"
            : configuration.Sequences[Math.Clamp(configuration.SelectedSequenceIndex, 0, configuration.Sequences.Count - 1)].Name;

    private BeastmasterSequenceDefinition? CurrentSequence
        => configuration.Sequences.Count == 0
            ? null
            : configuration.Sequences[Math.Clamp(configuration.SelectedSequenceIndex, 0, configuration.Sequences.Count - 1)];
    public BeastmasterSequenceState State { get; private set; }
    public bool IsControlling => State != BeastmasterSequenceState.Idle;
    public string Status { get; private set; } = "未启用";

    public void SetEnabled(bool enabled)
    {
        configuration.WaterOpenerSequenceEnabled = enabled;
        configuration.Save();
        if (!enabled)
        {
            Abort("已关闭");
        }
        else if (CurrentSequence is not { } sequence)
        {
            configuration.WaterOpenerSequenceEnabled = false;
            configuration.Save();
            Status = "无法启用：没有可用序列";
        }
        else if (!sequence.TryValidate(out var error))
        {
            configuration.WaterOpenerSequenceEnabled = false;
            configuration.Save();
            Status = $"无法启用：{error}";
        }
        else
        {
            lastHandledStartUtc = countdownService.LastTransitionUtc;
            Status = "等待下一次团队倒计时";
        }
    }

    public void SetChatMessagesEnabled(bool enabled)
    {
        configuration.SequenceChatMessagesEnabled = enabled;
        configuration.Save();
    }

    private void PrintChat(string message)
    {
        if (!configuration.SequenceChatMessagesEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (message == lastChatMessage && now - lastChatUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        lastChatMessage = message;
        lastChatUtc = now;
        DalamudApi.ChatGui.Print($"[驯兽师助手 {DateTime.Now:HH:mm:ss}] {message}");
    }

    public void Abort(string reason)
    {
        State = BeastmasterSequenceState.Idle;
        countdownStep = 0;
        combatStep = 0;
        pendingWhistle = 0;
        pendingBorrowedAction = 0;
        pendingBorrowedActionStep = -1;
        nextAttemptUtc = DateTime.MinValue;
        combatDeadlineUtc = DateTime.MinValue;
        stepDeadlineUtc = DateTime.MinValue;
        finalCountdownAttemptDeadlineUtc = DateTime.MinValue;
        combatStepFailure = "尚未尝试";
        combatStepAttempts = 0;
        Status = reason;
        PrintChat(reason);
    }

    public unsafe bool TryHandle(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara? target,
        DateTime now)
    {
        if (!Enabled)
        {
            return false;
        }

        var countdown = countdownService.Snapshot;
        var sequence = CurrentSequence;
        if (sequence == null)
        {
            Status = "序列无效：没有可用序列";
            return false;
        }
        if (!sequence.TryValidate(out var validationError))
        {
            Status = $"序列无效：{validationError}";
            return false;
        }
        if (State == BeastmasterSequenceState.Idle)
        {
            if (countdownService.LastTransition != BeastmasterCountdownTransition.Started
                || countdownService.LastTransitionUtc <= lastHandledStartUtc
                || !countdown.Active)
            {
                Status = "等待下一次团队倒计时";
                return false;
            }

            lastHandledStartUtc = countdownService.LastTransitionUtc;
            State = BeastmasterSequenceState.Countdown;
            countdownStep = 0;
            Status = $"倒计时：等待 T-{Math.Abs(sequence.CountdownSteps[0].TimeSeconds ?? 0f):0.###} {sequence.CountdownSteps[0].Label}";
            PrintChat($"序列“{CurrentSequenceName}”开始，等待 {sequence.CountdownSteps[0].Label}");
        }

        if (State == BeastmasterSequenceState.Countdown)
        {
            if (!DalamudApi.Condition[ConditionFlag.InCombat]
                && countdownService.LastTransition == BeastmasterCountdownTransition.Cancelled
                && countdownService.LastTransitionUtc >= lastHandledStartUtc)
            {
                Abort("序列中止：倒计时已取消");
                return true;
            }

            return RunCountdown(actionManager, gauge, target, countdown, sequence, now);
        }

        if (State == BeastmasterSequenceState.WaitingForCombat)
        {
            if (DalamudApi.Condition[ConditionFlag.InCombat])
            {
                State = BeastmasterSequenceState.Combat;
                combatStep = 0;
                nextAttemptUtc = DateTime.MinValue;
                stepDeadlineUtc = now.AddSeconds(8);
                ResetCombatStepFailure();
                Status = "战斗序列：碎击斩";
                PrintChat("已进入战斗，开始执行战斗序列");
                return true;
            }

            if (now >= combatDeadlineUtc)
            {
                Abort("序列中止：盾牌冲击后未进入战斗");
            }
            return true;
        }

        return RunCombat(actionManager, gauge, target, sequence, now);
    }

    private unsafe bool RunCountdown(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara? target,
        BeastmasterCountdownSnapshot countdown,
        BeastmasterSequenceDefinition sequence,
        DateTime now)
    {
        if (DalamudApi.Condition[ConditionFlag.InCombat])
        {
            pendingWhistle = 0;
            pendingBorrowedAction = 0;
            pendingBorrowedActionStep = -1;
            State = BeastmasterSequenceState.Combat;
            combatStep = 0;
            nextAttemptUtc = now;
            stepDeadlineUtc = now.AddSeconds(8);
            ResetCombatStepFailure();
            Status = "已进入战斗，开始战斗序列";
            PrintChat("已进入战斗，开始执行战斗序列");
            return true;
        }

        var currentStep = countdownStep < sequence.CountdownSteps.Count
            ? sequence.CountdownSteps[countdownStep]
            : null;
        var isFinalZeroStep = currentStep is not null
            && Math.Abs(currentStep.TimeSeconds ?? 0f) <= 0.001f;
        var isFinalStep = countdownStep >= sequence.CountdownSteps.Count - 1;
        if (!countdown.Active
            && !isFinalStep)
        {
            Abort(countdownService.LastTransition == BeastmasterCountdownTransition.Cancelled
                ? "序列中止：倒计时已取消"
                : "序列中止：倒计时提前结束");
            return true;
        }

        if (!countdown.Active && isFinalStep)
        {
            Status = "未进入战斗，倒计时没有提前结束";
        }

        if (pendingWhistle != 0)
        {
            if (gauge.WhistleIndex == pendingWhistle)
            {
                pendingWhistle = 0;
                countdownStep++;
            }
            else if (MissedCountdownDeadline(countdown.TimeRemaining, sequence, countdownStep))
            {
                Abort($"序列中止：{pendingWhistle} 号兽笛未确认");
            }
            return true;
        }

        if (pendingBorrowedAction != 0)
        {
            if (actionManager->GetAdjustedActionId(BeastSkillActionId) == pendingBorrowedAction)
            {
                var actionName = GetActionName(pendingBorrowedAction);
                pendingBorrowedAction = 0;
                pendingBorrowedActionStep = -1;
                countdownStep++;
                Status = $"已确认借用技能：{actionName}";
                PrintChat($"已确认借用技能：{actionName}");
            }
            else if (pendingBorrowedActionStep >= 0
                     && countdown.TimeRemaining <= Math.Abs(sequence.CountdownSteps[pendingBorrowedActionStep].TimeSeconds ?? 0f))
            {
                Abort($"序列中止：借用后{GetActionName(pendingBorrowedAction)}未就绪");
            }
            return true;
        }

        if (countdownStep >= sequence.CountdownSteps.Count)
        {
            Abort("序列中止：倒计时步骤已耗尽");
            return true;
        }

        var step = sequence.CountdownSteps[countdownStep];
        if (TryGetWhistleIndex(step.ActionId, out var requiredWhistle)
            && gauge.WhistleIndex == requiredWhistle)
        {
            countdownStep++;
            Status = $"已确认当前为{requiredWhistle}号兽笛，准备下一步骤";
            PrintChat($"已确认当前为{requiredWhistle}号兽笛，跳过重复请求");
            return true;
        }

        var trigger = Math.Abs(step.TimeSeconds ?? 0f);
        if (isFinalZeroStep)
        {
            trigger = 0.1f;
        }
        if (countdown.TimeRemaining > trigger)
        {
            Status = $"倒计时：等待 T-{trigger:0.###} {step.Label}";
            return true;
        }

        if (now < nextAttemptUtc)
        {
            return true;
        }

        if (isFinalStep && finalCountdownAttemptDeadlineUtc == DateTime.MinValue)
        {
            finalCountdownAttemptDeadlineUtc = now.AddSeconds(2);
        }

        var baseActionId = step.ActionId;
        var adjustedActionId = baseActionId is BorrowActionId or BeastSkillActionId
            ? actionManager->GetAdjustedActionId(baseActionId)
            : baseActionId;
        if (adjustedActionId != 0
            && BeastmasterActionHelper.TryGetActionLevel(adjustedActionId, out var requiredLevel)
            && DalamudApi.ObjectTable.LocalPlayer is { } player
            && player.Level < requiredLevel)
        {
            PrintChat($"跳过倒计时步骤：{step.Label}，等级 {player.Level}，技能要求等级 {requiredLevel}");
            countdownStep++;
            if (countdownStep >= sequence.CountdownSteps.Count)
            {
                State = BeastmasterSequenceState.WaitingForCombat;
                combatDeadlineUtc = now.AddSeconds(2);
                Status = "等待进入战斗";
            }
            else
            {
                Status = "倒计时：等待下一步骤";
            }
            return true;
        }

        var requiresTarget = IsTargetAction(baseActionId);
        if (requiresTarget && !IsValidTarget(target))
        {
            Abort("序列中止：T-0 没有有效敌对目标");
            return true;
        }

        var targetId = requiresTarget ? target!.GameObjectId : 0UL;
        if (adjustedActionId == 0
            || actionManager->GetActionStatus(ActionType.Action, adjustedActionId, targetId) != 0
            || !actionManager->UseAction(ActionType.Action, adjustedActionId, targetId))
        {
            Status = $"等待技能可用：{step.Label}";
            nextAttemptUtc = now.AddMilliseconds(100);
            if ((isFinalZeroStep && now >= finalCountdownAttemptDeadlineUtc)
                || (!isFinalZeroStep && MissedCountdownDeadline(countdown.TimeRemaining, sequence, countdownStep)))
            {
                Abort($"序列中止：{GetActionName(adjustedActionId)}未在窗口内使用");
            }
            return true;
        }

        nextAttemptUtc = now.AddMilliseconds(250);
        if (baseActionId is WhistleOneActionId or WhistleTwoActionId or WhistleThreeActionId)
        {
            pendingWhistle = baseActionId == WhistleOneActionId ? (byte)1 : baseActionId == WhistleTwoActionId ? (byte)2 : (byte)3;
                Status = $"倒计时：等待{pendingWhistle}号兽笛确认";
            PrintChat($"已请求倒计时技能：{step.Label}，等待{pendingWhistle}号兽笛确认");
        }
        else if (baseActionId == BorrowActionId)
        {
            pendingBorrowedActionStep = FindBorrowedActionStep(sequence, countdownStep + 1);
            if (pendingBorrowedActionStep >= 0)
            {
                pendingBorrowedAction = sequence.CountdownSteps[pendingBorrowedActionStep].ActionId;
                var actionName = GetActionName(pendingBorrowedAction);
                Status = $"倒计时：等待{actionName}就绪";
                PrintChat($"已请求借用，等待{actionName}就绪");
            }
            else
            {
                PrintChat("已请求倒计时技能：借用");
                countdownStep++;
                Status = "倒计时：等待下一步骤";
            }
        }
        else
        {
            PrintChat($"已请求倒计时技能：{step.Label}");
            countdownStep++;
            if (countdownStep >= sequence.CountdownSteps.Count)
            {
                State = BeastmasterSequenceState.WaitingForCombat;
                combatDeadlineUtc = now.AddSeconds(2);
                Status = "等待进入战斗";
            }
            else
            {
                Status = "倒计时：等待下一步骤";
            }
        }
        return true;
    }

    private unsafe bool RunCombat(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara? target,
        BeastmasterSequenceDefinition sequence,
        DateTime now)
    {
        if (!DalamudApi.Condition[ConditionFlag.InCombat])
        {
            Abort("序列中止：已脱离战斗");
            return true;
        }

        if (combatStep >= sequence.CombatSteps.Count)
        {
            Status = "序列完成，等待下一次团队倒计时";
            PrintChat("技能序列已完成");
            State = BeastmasterSequenceState.Idle;
            return true;
        }

        var step = sequence.CombatSteps[combatStep];
        if (now >= stepDeadlineUtc)
        {
            Abort($"序列中止：步骤 {combatStep + 1}/{sequence.CombatSteps.Count}「{step.Label}」超时：{combatStepFailure}（重试 {combatStepAttempts} 次）");
            return true;
        }

        if (pendingWhistle != 0)
        {
            if (gauge.WhistleIndex == pendingWhistle)
            {
                pendingWhistle = 0;
                combatStep++;
                stepDeadlineUtc = now.AddSeconds(8);
                ResetCombatStepFailure();
            }
            else
            {
                combatStepFailure = $"等待 {pendingWhistle} 号兽笛确认，当前兽笛 {gauge.WhistleIndex}";
            }
            return true;
        }

        if (now < nextAttemptUtc)
        {
            return true;
        }

        var baseActionId = step.ActionId;
        if (TryGetWhistleIndex(baseActionId, out var requiredWhistle)
            && gauge.WhistleIndex == requiredWhistle)
        {
            combatStep++;
            stepDeadlineUtc = now.AddSeconds(8);
            ResetCombatStepFailure();
            Status = $"已确认当前为{requiredWhistle}号兽笛，准备下一步骤";
            PrintChat($"已确认当前为{requiredWhistle}号兽笛，跳过重复请求");
            return true;
        }

        var adjustedActionId = baseActionId is ReleaseActionId or BeastSkillActionId or BorrowActionId
            ? actionManager->GetAdjustedActionId(baseActionId)
            : baseActionId;
        var requiresTarget = baseActionId is SmashActionId or ReleaseActionId or FinalStrikeActionId;
        if (requiresTarget && !IsValidTarget(target))
        {
            Abort($"序列中止：{GetActionName(adjustedActionId)}没有有效目标");
            return true;
        }

        var targetId = requiresTarget ? target!.GameObjectId : 0UL;
        Status = $"战斗序列 {combatStep + 1}/{sequence.CombatSteps.Count}：{step.Label}";
        combatStepAttempts++;
        if (adjustedActionId == 0)
        {
            combatStepFailure = $"调整后 ActionId 为 0（基础 ActionId {baseActionId}）";
            nextAttemptUtc = now.AddMilliseconds(100);
            return true;
        }

        if (BeastmasterActionHelper.TryGetActionLevel(adjustedActionId, out var requiredLevel)
            && DalamudApi.ObjectTable.LocalPlayer is { } player
            && player.Level < requiredLevel)
        {
            PrintChat($"跳过战斗步骤：{step.Label}，等级 {player.Level}，技能要求等级 {requiredLevel}");
            combatStep++;
            stepDeadlineUtc = now.AddSeconds(8);
            ResetCombatStepFailure();
            return true;
        }

        if (baseActionId == ReleaseActionId
            && target is not null
            && !BeastmasterActionHelper.IsSummonInActionRange(
                target,
                gauge.SummonEntry?.ReleaseActionId ?? adjustedActionId,
                out var summonDistance,
                out var actionRange))
        {
            combatStepFailure = $"等待召唤兽进入释放距离（当前 {summonDistance:0.##}/{actionRange:0.##} yalms）";
            nextAttemptUtc = now.AddMilliseconds(250);
            return true;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, adjustedActionId, targetId);
        if (actionStatus != 0)
        {
            combatStepFailure = $"GetActionStatus={actionStatus}（Action {baseActionId}→{adjustedActionId}，目标 {targetId}）";
            nextAttemptUtc = now.AddMilliseconds(100);
            return true;
        }

        if (!actionManager->UseAction(ActionType.Action, adjustedActionId, targetId))
        {
            combatStepFailure = $"UseAction 返回 false（Action {baseActionId}→{adjustedActionId}，Status=0，目标 {targetId}）";
            nextAttemptUtc = now.AddMilliseconds(100);
            return true;
        }

        PrintChat($"已请求战斗技能：{combatStep + 1}/{sequence.CombatSteps.Count} {step.Label}");

        nextAttemptUtc = now.AddMilliseconds(baseActionId is WhistleOneActionId or WhistleTwoActionId or WhistleThreeActionId ? 250 : 350);
        if (baseActionId == WhistleTwoActionId)
        {
            pendingWhistle = 2;
            combatStepFailure = "等待 2 号兽笛确认";
        }
        else if (baseActionId == WhistleThreeActionId)
        {
            pendingWhistle = 3;
            combatStepFailure = "等待 3 号兽笛确认";
        }
        else
        {
            combatStep++;
            stepDeadlineUtc = now.AddSeconds(8);
            ResetCombatStepFailure();
        }
        return true;
    }

    private void ResetCombatStepFailure()
    {
        combatStepFailure = "尚未尝试";
        combatStepAttempts = 0;
    }

    private static bool MissedCountdownDeadline(float remaining, BeastmasterSequenceDefinition sequence, int stepIndex = 0)
    {
        var deadline = stepIndex + 1 < sequence.CountdownSteps.Count
            ? Math.Abs(sequence.CountdownSteps[stepIndex + 1].TimeSeconds ?? 0f)
            : 0f;
        return remaining <= deadline;
    }

    private static int FindBorrowedActionStep(BeastmasterSequenceDefinition sequence, int startIndex)
    {
        for (var index = startIndex; index < sequence.CountdownSteps.Count; index++)
        {
            if (sequence.CountdownSteps[index].ActionId is >= 44896 and <= 44903)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTargetAction(uint actionId)
        => actionId is SmashActionId or BiteActionId or ShieldActionId or ShieldChargeActionId or ReleaseActionId or FinalStrikeActionId;

    private static bool TryGetWhistleIndex(uint actionId, out byte whistleIndex)
    {
        whistleIndex = actionId switch
        {
            WhistleOneActionId => 1,
            WhistleTwoActionId => 2,
            WhistleThreeActionId => 3,
            _ => 0,
        };
        return whistleIndex != 0;
    }

    private static bool IsValidTarget(IBattleChara? target)
        => target is not null
            && target.ObjectKind == ObjectKind.BattleNpc
            && target.IsTargetable
            && !target.IsDead
            && target.CurrentHp > 0;

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .TryGetRow(actionId, out var action)
                ? action.Name.ExtractText()
                : $"技能 {actionId}";
}
