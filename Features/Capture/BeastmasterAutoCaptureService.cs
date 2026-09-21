using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Beastmaster;

public sealed class BeastmasterAutoCaptureService : IDisposable
{
    private const uint BeastmasterClassJobId = 43;
    private const uint BeastmasterUltimateActionId = 47093;
    private const uint BeastmasterReleaseBaseActionId = 44890;
    private const uint DrumActionId = 44905;
    private const uint CheerActionId = 44904;
    private const uint SafeShieldActionId = 44893;
    private const float SafeShieldRange = 3f;
    private static readonly TimeSpan SafeShieldRequestCooldown = TimeSpan.FromSeconds(2);
    private const uint WhitePhysicalThirdFormActionId = 44931;
    private const uint PurplePhysicalThirdFormActionId = 44930;
    private const uint WhiteMagicalThirdFormActionId = 44933;
    private const uint PurpleMagicalThirdFormActionId = 44932;
    private const uint WhistleOneActionId = 44881;
    private const uint WhistleTwoActionId = 44892;
    private const uint WhistleThreeActionId = 44894;
    private const uint FinalStrikeActionId = 44891;
    private const float CooperationMinimumGcdRemaining = 0.7f;
    private const uint BorrowActionId = 44895;
    private const uint BeastSkillActionId = 44886;
    private const uint SmashActionId = 44879;
    private const uint BiteActionId = 44883;
    private const uint ShieldActionId = 44885;
    private const uint CaptureActionId = 44880;
    private const uint CaptureStatusId = 4626;
    private const int MaxAbilitiesPerGcdWindow = 2;
    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterSequenceService sequenceService;
    private readonly BeastmasterRuleService ruleService;
    private readonly BeastmasterCrucibleItemService crucibleItemService;
    private readonly uint smashActionId;
    private readonly uint biteActionId;
    private readonly uint shieldActionId;
    private readonly uint captureActionId;
    private readonly uint captureStatusId;
    private DateTime nextCheckUtc = DateTime.MinValue;
    private DateTime nextActionUtc = DateTime.MinValue;
    private DateTime capturePendingUntilUtc = DateTime.MinValue;
    private ulong captureTargetId;
    private ulong activeTargetId;
    private int abilitiesUsedInGcdWindow;
    private uint pendingCooperationActionId;
    private uint pendingCooperationStatusId;
    private DateTime pendingCooperationUntilUtc = DateTime.MinValue;
    private DateTime nextReleaseAttemptUtc = DateTime.MinValue;
    private uint pendingWhistleActionId;
    private DateTime pendingWhistleUntilUtc = DateTime.MinValue;
    private DateTime nextWhistleAttemptUtc = DateTime.MinValue;
    private DateTime nextFinalStrikeAttemptUtc = DateTime.MinValue;
    private DateTime nextSafeShieldAttemptUtc = DateTime.MinValue;
    private DateTime resurrectionProtectionUntilUtc = DateTime.MinValue;
    private DateTime recoveryDiagnosticDeadlineUtc = DateTime.MinValue;
    private DateTime lastRecoveryFailureChatUtc = DateTime.MinValue;
    private uint recoveryDiagnosticStartHp;
    private ushort recoveryDiagnosticItemId;
    private string recoveryDiagnosticDetail = string.Empty;
    private string lastRecoveryFailureReason = string.Empty;
    private bool playerWasDead;
    private bool reportedMissingData;
    private int whistleRotationStage = -1;
    private bool whistleRotationWaitingForCooldown;
    private DateTime whistleRotationNextActionUtc = DateTime.MinValue;
    private DateTime lastSuccessfulActionUtc = DateTime.MinValue;
    private string lastAutoOutputSummary = string.Empty;
    private readonly List<string> currentBattleLog = [];
    private readonly List<(string Label, string Text)> battleLogs = [];
    private bool wasInCombat;
    private readonly Dictionary<string, string> battleLogModuleStates = new(StringComparer.Ordinal);

    public string StatusText { get; private set; } = "等待当前目标";

    public string NextActionName { get; private set; } = "-";

    public string NextActionReason { get; private set; } = "";

    public string ResourceStatus { get; private set; } = "量谱未读取";

    public string AdvancedActionStatus { get; private set; } = "未评估";

    public string TargetStatus { get; private set; } = "无有效目标";

    public float TargetHpPercent { get; private set; }

    public string CaptureState { get; private set; } = "未开始";

    public string ManualActionStatus { get; private set; } = "未执行";

    private void ReportAutoOutputDiagnostic(string actionName, string reason, string reasonKey)
    {
        _ = actionName;
        _ = reason;
        _ = reasonKey;
    }

    private void ReportAutoOutputSuccess(string actionName, uint actionId)
    {
        _ = actionName;
        _ = actionId;
        lastSuccessfulActionUtc = DateTime.UtcNow;
        RecordBattleLog($"已请求{actionName}（ActionId {actionId}）");
    }

    private void ReportCooperationDiagnostic(string message, string stateKey)
    {
        if (!configuration.AutoOutputDiagnosticsEnabled)
        {
            return;
        }

        var key = $"协作二段|{stateKey}";
        if (battleLogModuleStates.TryGetValue(key, out var previousState)
            && previousState == stateKey)
        {
            return;
        }

        battleLogModuleStates[key] = stateKey;
        RecordBattleLog($"协作二段：{message}");
    }

    private void RecordBattleLog(string message)
    {
        if (!configuration.AutoOutputDiagnosticsEnabled || !wasInCombat)
        {
            return;
        }

        if (currentBattleLog.Count >= 500)
        {
            currentBattleLog.RemoveAt(0);
        }

        currentBattleLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void UpdateBattleLogState(bool inCombat)
    {
        if (!wasInCombat && inCombat)
        {
            currentBattleLog.Clear();
            battleLogModuleStates.Clear();
            currentBattleLog.Add($"[{DateTime.Now:HH:mm:ss}] 战斗开始");
        }
        else if (wasInCombat && !inCombat)
        {
            currentBattleLog.Add($"[{DateTime.Now:HH:mm:ss}] 战斗结束");
            battleLogs.Insert(0, ($"战斗 {currentBattleLog[0][1..9]}", string.Join(Environment.NewLine, currentBattleLog)));
            if (battleLogs.Count > 10) battleLogs.RemoveAt(battleLogs.Count - 1);
            currentBattleLog.Clear();
        }

        wasInCombat = inCombat;
    }

    public string WhistleRotationStatus { get; private set; } = "未开启";

    public BeastmasterAutoCaptureService(
        BeastmasterConfiguration configuration,
        BeastmasterSequenceService sequenceService,
        BeastmasterRuleService ruleService,
        BeastmasterCrucibleItemService crucibleItemService)
    {
        this.configuration = configuration;
        this.sequenceService = sequenceService;
        this.ruleService = ruleService;
        this.crucibleItemService = crucibleItemService;
        // Action and status RowId are language-independent; names differ by client locale.
        smashActionId = SmashActionId;
        biteActionId = BiteActionId;
        shieldActionId = ShieldActionId;
        captureActionId = CaptureActionId;
        captureStatusId = CaptureStatusId;
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public bool IsEnabled => configuration.AutoCaptureEnabled;

    public bool IsPaused => configuration.AutoOutputPaused;

    public bool TryCapture => configuration.AutoCaptureTryCapture;

    public bool ForceCapture => configuration.ForceCaptureEnabled;

    public bool ActiveAttack => configuration.ActiveAttackEnabled;

    public bool FinalStrikeEnabled => configuration.AutoFinalStrikeEnabled;

    public bool BasicComboEnabled => configuration.BasicComboEnabled;

    public IReadOnlyList<string> BattleLogLabels => battleLogs.Select(log => log.Label).ToArray();

    public string GetBattleLog(int index)
        => index >= 0 && index < battleLogs.Count ? battleLogs[index].Text : "暂无战斗日志。";

    public void ClearBattleLogs() => battleLogs.Clear();

    public void SetEnabled(bool enabled)
    {
        configuration.AutoCaptureEnabled = enabled;
        configuration.Save();
        if (enabled)
        {
            DalamudApi.ChatGui.Print("[驯兽师助手] 自动捕获已开启，仅对当前手动选择的目标生效。");
        }
        else
        {
            sequenceService.Abort("自动输出已关闭");
            nextActionUtc = DateTime.MinValue;
            nextReleaseAttemptUtc = DateTime.MinValue;
            nextFinalStrikeAttemptUtc = DateTime.MinValue;
            nextSafeShieldAttemptUtc = DateTime.MinValue;
            ResetCaptureState();
            ResetCooperationState("自动输出关闭");
            ResetAutoWhistle();
            crucibleItemService.Reset();
            DalamudApi.ChatGui.Print("[驯兽师助手] 自动捕获已关闭。");
        }
    }

    public void SetTryCapture(bool enabled)
    {
        configuration.AutoCaptureTryCapture = enabled;
        if (!enabled)
        {
            configuration.ForceCaptureEnabled = false;
        }
        configuration.Save();
    }

    public void SetForceCapture(bool enabled)
    {
        configuration.ForceCaptureEnabled = enabled;
        if (enabled)
        {
            configuration.AutoCaptureTryCapture = true;
        }
        configuration.Save();
    }

    public void SetBasicComboEnabled(bool enabled)
    {
        configuration.BasicComboEnabled = enabled;
        configuration.Save();
    }

    public void SetActiveAttack(bool enabled)
    {
        configuration.ActiveAttackEnabled = enabled;
        configuration.Save();
    }

    public void SetFinalStrikeEnabled(bool enabled)
    {
        configuration.AutoFinalStrikeEnabled = enabled;
        configuration.Save();
    }

    public void SetPaused(bool paused)
    {
        configuration.AutoOutputPaused = paused;
        configuration.Save();
        if (paused)
        {
            sequenceService.Abort("自动输出已暂停");
            nextActionUtc = DateTime.MinValue;
            nextReleaseAttemptUtc = DateTime.MinValue;
            nextFinalStrikeAttemptUtc = DateTime.MinValue;
            nextSafeShieldAttemptUtc = DateTime.MinValue;
            ResetCaptureState();
            ResetCooperationState("自动输出暂停");
            ResetAutoWhistle();
            crucibleItemService.Reset();
        }
    }

    public unsafe bool TryUseUltimate()
    {
        var gauge = BeastmasterGaugeSnapshot.Read();
        var entry = gauge.SummonEntry;
        if (!gauge.Available || entry == null)
        {
            ManualActionStatus = "量谱或当前魔兽不可用";
            return false;
        }

        if (gauge.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement
            || gauge.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            ManualActionStatus = $"资源不足：技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250";
            return false;
        }

        if (DalamudApi.TargetManager.Target is not IBattleChara target
            || target.ObjectKind != ObjectKind.BattleNpc
            || !target.IsTargetable
            || target.IsDead
            || target.CurrentHp == 0)
        {
            ManualActionStatus = "当前目标无效";
            return false;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            ManualActionStatus = "ActionManager 不可用";
            return false;
        }

        var actionId = BeastmasterUltimateActionId;
        if (BeastmasterFinalStrikeLock.IsBlocked(actionId, DateTime.UtcNow))
        {
            ManualActionStatus = BeastmasterFinalStrikeLock.GetBlockReason(actionId, DateTime.UtcNow);
            return false;
        }
        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                DalamudApi.ObjectTable.LocalPlayer!,
                target,
                actionId,
                out var playerDistance,
                out var actionRange))
        {
            ManualActionStatus = $"大招等待进入技能射程（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
            return false;
        }
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
        if (actionStatus != 0)
        {
            ManualActionStatus = $"大招当前不可用（状态码 {actionStatus}）";
            return false;
        }

        if (!actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId))
        {
            ManualActionStatus = "大招请求失败";
            return false;
        }

        nextActionUtc = DateTime.UtcNow.AddMilliseconds(700);
        if ((configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
            && TryGetCooperationAction(gauge, configuration.BeastHeartCooperationEnabled, out var firstCooperationActionId, out var followUpActionId, out var requiredStatusId)
            && firstCooperationActionId == BeastmasterUltimateActionId)
        {
            pendingCooperationActionId = followUpActionId;
            pendingCooperationStatusId = requiredStatusId;
            pendingCooperationUntilUtc = DateTime.UtcNow.AddSeconds(7);
        }

        ManualActionStatus = $"已请求释放：{GetActionName(actionId)}";
        return true;
    }

    private static unsafe bool TryUseAdvancedAction(
        ActionManager* actionManager,
        uint actionId,
        ulong targetId,
        uint actionStatus)
    {
        if (BeastmasterFinalStrikeLock.IsBlocked(actionId, DateTime.UtcNow))
        {
            return false;
        }

        return actionStatus == 0
            && actionManager->UseAction(ActionType.Action, actionId, targetId);
    }

    public void Dispose()
        => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        var now = DateTime.UtcNow;
        UpdateRecoveryItemDiagnostic(now);
        UpdateBattleLogState(DalamudApi.Condition[ConditionFlag.InCombat]);
        if (!configuration.AutoCaptureEnabled)
        {
            StatusText = "自动捕获已关闭";
            NextActionName = "-";
            NextActionReason = "未启用自动输出";
            ResourceStatus = "量谱未读取";
            AdvancedActionStatus = "自动输出未启用";
            TargetStatus = "无有效目标";
            TargetHpPercent = 0f;
            ResetCaptureState();
            ResetWhistleRotation("自动输出未启用");
            ResetAutoWhistle();
            crucibleItemService.Reset();
            return;
        }

        if (configuration.AutoOutputPaused)
        {
            StatusText = "自动输出已暂停";
            NextActionName = "-";
            NextActionReason = "暂停开关已开启";
            AdvancedActionStatus = "暂停中";
            ResetCaptureState();
            ResetCooperationState("暂停开关开启");
            ResetAutoWhistle();
            crucibleItemService.Reset();
            return;
        }

        crucibleItemService.ProcessPendingRequest(now);
        crucibleItemService.UpdateExecutionProbe(now);
        FlushExecutionProbes();
        HandleRecoveryItemDispatch(now);
        ruleService.ProcessCrucibleDispatchResults(now);
        if (crucibleItemService.HasPendingRequest)
        {
            StatusText = "等待奇弈道具执行";
            NextActionName = "奇弈道具";
            NextActionReason = "等待动作锁解除或热键栏槽位同步";
            return;
        }

        if (!configuration.WhistleRotationEnabled
            && (whistleRotationStage >= 0 || whistleRotationWaitingForCooldown))
        {
            ResetWhistleRotation("未开启");
        }

        if (now < nextCheckUtc)
        {
            return;
        }

        nextCheckUtc = now.AddMilliseconds(100);
        if (now < nextActionUtc)
        {
            StatusText = "等待可执行状态";
            NextActionReason = "技能节流等待";
            return;
        }

        if (DalamudApi.Condition[ConditionFlag.BetweenAreas])
        {
            sequenceService.Abort("序列中止：正在切图或传送");
            StatusText = "等待可执行状态";
            NextActionReason = "正在切图或传送";
            return;
        }

        if (DalamudApi.Condition[ConditionFlag.Mounted])
        {
            StatusText = "等待可执行状态";
            NextActionReason = "当前处于骑乘状态";
            return;
        }

        if (DalamudApi.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            StatusText = "等待可执行状态";
            NextActionReason = "剧情或特殊事件占用";
            return;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            StatusText = "等待角色加载";
            NextActionName = "-";
            NextActionReason = "角色尚未加载";
            return;
        }

        if (player.ClassJob.RowId != BeastmasterClassJobId)
        {
            sequenceService.Abort("序列中止：当前职业不是驯兽师");
            StatusText = "请切换为驯兽师";
            NextActionName = "-";
            NextActionReason = "当前职业不是驯兽师";
            ResourceStatus = "仅驯兽师可用";
            return;
        }

        if (player.CurrentHp == 0)
        {
            playerWasDead = true;
            resurrectionProtectionUntilUtc = DateTime.MinValue;
            sequenceService.Abort("序列中止：角色已死亡");
            StatusText = "等待可执行状态";
            NextActionReason = "角色已死亡";
            return;
        }

        if (playerWasDead)
        {
            playerWasDead = false;
            resurrectionProtectionUntilUtc = now.AddSeconds(3);
            nextActionUtc = DateTime.MinValue;
            nextReleaseAttemptUtc = DateTime.MinValue;
            nextFinalStrikeAttemptUtc = DateTime.MinValue;
            nextSafeShieldAttemptUtc = DateTime.MinValue;
            ResetCaptureState();
            ResetCooperationState("复活后重置");
            ResetAutoWhistle();
        }

        if (now < resurrectionProtectionUntilUtc)
        {
            StatusText = "复活保护中";
            NextActionName = "-";
            NextActionReason = $"复活后等待 {(resurrectionProtectionUntilUtc - now).TotalSeconds:0.0} 秒";
            return;
        }

        if (player.IsCasting)
        {
            StatusText = "等待可执行状态";
            NextActionReason = "角色正在读条";
            return;
        }

        var gauge = BeastmasterGaugeSnapshot.Read();
        ResourceStatus = gauge.Available
            ? $"技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250"
            : gauge.Status;
        AdvancedActionStatus = GetAdvancedActionStatus(gauge);

        if (pendingCooperationActionId != 0 && pendingCooperationUntilUtc <= now)
        {
            ResetCooperationState($"等待二段超时；二段 {GetActionName(pendingCooperationActionId)}（{pendingCooperationActionId}），Buff {pendingCooperationStatusId}");
        }

        if (smashActionId == 0 || biteActionId == 0 || shieldActionId == 0 || captureActionId == 0 || captureStatusId == 0)
        {
            if (!reportedMissingData)
            {
                reportedMissingData = true;
                StatusText = "技能或状态数据解析失败";
                NextActionName = "-";
                NextActionReason = "自动输出所需技能或状态缺失";
                SetEnabled(false);
                DalamudApi.ChatGui.Print("[驯兽师助手] 无法从客户端解析自动捕获所需技能或状态，请通过 DEBUG 查询后反馈。");
            }

            return;
        }

        var target = DalamudApi.TargetManager.Target as IBattleChara;
        if (target is not null
            && (target.EntityId == player.EntityId
                || target.ObjectKind != ObjectKind.BattleNpc
                || !target.IsTargetable
                || target.IsDead
                || target.CurrentHp == 0))
        {
            target = null;
        }

        if (target == null)
        {
            if (activeTargetId != 0)
            {
                ResetTargetScopedState();
            }
        }
        else if (activeTargetId != target.EntityId)
        {
            if (activeTargetId != 0)
            {
                ResetTargetScopedState();
            }

            activeTargetId = target.EntityId;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            StatusText = "等待动作系统";
            NextActionReason = "ActionManager 不可用";
            return;
        }

        if (sequenceService.TryHandle(actionManager, gauge, target, now))
        {
            StatusText = sequenceService.Status;
            NextActionName = "技能序列";
            NextActionReason = "技能序列正在接管规则模式和普通 ACR";
            return;
        }

        var playerHpPercent = player.MaxHp == 0
            ? 100f
            : player.CurrentHp * 100f / player.MaxHp;
        if (configuration.AutoRecoveryItemEnabled
            && DalamudApi.Condition[ConditionFlag.InCombat]
            && IsArenaTerritory(DalamudApi.ClientState.TerritoryType)
            && playerHpPercent < configuration.AutoRecoveryItemHpThreshold
            && recoveryDiagnosticItemId == 0)
        {
            if (!crucibleItemService.TryUseBestRecoveryItem(player, target, now, out var recoveryItemId))
            {
                ReportRecoveryItemFailure(
                    player,
                    playerHpPercent,
                    "恢复药",
                    $"{crucibleItemService.LastFailureReason}{crucibleItemService.LastDiagnostic}",
                    now);
            }
            else
            {
                StatusText = "自动使用恢复药...";
                NextActionName = GetRecoveryItemName(recoveryItemId);
                NextActionReason = $"自身血量 {playerHpPercent:0.#}% 低于阈值 {configuration.AutoRecoveryItemHpThreshold:0.#}%";
                nextActionUtc = now.AddMilliseconds(700);
                RecordBattleLog($"已请求{NextActionName}（XBMItem {recoveryItemId}）");
                return;
            }
        }

        if (ruleService.TryHandle(actionManager, player, target, now))
        {
            StatusText = "规则模式执行中...";
            NextActionName = "规则技能";
            NextActionReason = ruleService.LastDiagnostic;
            nextActionUtc = now.AddMilliseconds(700);
            return;
        }

        if (!configuration.AutoWhistleEnabled && pendingWhistleActionId != 0)
        {
            ResetAutoWhistle();
        }

        if (configuration.AutoWhistleEnabled
            && TryUseAutoWhistle(actionManager, gauge, now))
        {
            return;
        }

        // 暂时禁用兽笛循环连招入口，保留实现以便后续恢复。
        // if ((configuration.WhistleRotationEnabled || whistleRotationWaitingForCooldown || whistleRotationStage >= 0)
        //     && TryRunWhistleRotation(actionManager, target, now))
        // {
        //     return;
        // }

        if (target is null)
        {
            StatusText = "等待当前敌对目标";
            NextActionName = "-";
            NextActionReason = "没有有效的 BattleNpc 目标";
            TargetStatus = "无有效目标";
            TargetHpPercent = 0f;
            ResetCaptureState();
            ResetCooperationState("没有有效敌对目标");
            return;
        }

        if (captureTargetId != target.EntityId)
        {
            captureTargetId = target.EntityId;
            capturePendingUntilUtc = DateTime.MinValue;
            CaptureState = "未开始";
        }

        var hasOwnCapture = target.StatusList.Any(status =>
            status.StatusId == captureStatusId && status.SourceId == player.EntityId);
        var hasOtherCapture = target.StatusList.Any(status =>
            status.StatusId == captureStatusId && status.SourceId != player.EntityId);
        var targetHpPercent = target.MaxHp == 0
            ? 100f
            : target.CurrentHp * 100f / target.MaxHp;
        TargetHpPercent = Math.Clamp(targetHpPercent, 0f, 100f);
        TargetStatus = hasOwnCapture
            ? "自身已施加捕获状态"
            : hasOtherCapture
                ? "他人已施加捕获状态"
                : "未施加捕获状态";

        if (!configuration.ActiveAttackEnabled && !DalamudApi.Condition[ConditionFlag.InCombat])
        {
            StatusText = "等待进入战斗";
            NextActionName = "-";
            NextActionReason = "主动攻击已关闭，未进战时不攻击或捕获";
            ResetCaptureState();
            ResetCooperationState("主动攻击关闭且尚未进入战斗");
            return;
        }

        if (hasOwnCapture)
        {
            CaptureState = "已确认自身捕获";
            capturePendingUntilUtc = DateTime.MinValue;
        }
        else if (capturePendingUntilUtc > now)
        {
            CaptureState = "等待捕获结果";
        }
        else if (capturePendingUntilUtc != DateTime.MinValue)
        {
            CaptureState = "捕获状态未确认，可重试";
            capturePendingUntilUtc = DateTime.MinValue;
        }

        var canCapture = targetHpPercent <= configuration.CaptureHpThreshold;
        EmitAutoOutputDiagnosticSummary(actionManager, player, target, gauge, targetHpPercent, canCapture, now);
        var capturePending = capturePendingUntilUtc > now;
        if (capturePending)
        {
            StatusText = "等待捕获结果";
            NextActionName = "-";
            NextActionReason = "等待自身捕获状态刷新";
            return;
        }

        if (actionManager->AnimationLock > 0f)
        {
            StatusText = "等待可执行状态";
            NextActionName = "-";
            NextActionReason = "技能动画锁中";
            return;
        }

        var basicComboActionId = GetBasicComboActionId(actionManager, player);
        var gcdActive = actionManager->IsRecastTimerActive(ActionType.Action, basicComboActionId);
        var gcdTotal = actionManager->GetRecastTime(ActionType.Action, basicComboActionId);
        var gcdElapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, basicComboActionId);
        var gcdRemaining = gcdActive ? Math.Max(0f, gcdTotal - gcdElapsed) : 0f;
        var gcdReady = !gcdActive
            && actionManager->GetActionStatus(ActionType.Action, basicComboActionId, target.GameObjectId) == 0;

        if (gcdReady)
        {
            if ((configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled)
                && TryUseThirdFormAction(actionManager, gauge, target.GameObjectId, now))
            {
                abilitiesUsedInGcdWindow = 0;
                return;
            }

            if (configuration.BasicComboEnabled && TryUseBasicCombo(actionManager, player, target, now))
            {
                abilitiesUsedInGcdWindow = 0;
                return;
            }

            if (!configuration.BasicComboEnabled)
            {
                StatusText = "等待可用技能";
                NextActionName = "-";
                NextActionReason = "基础技能（1→2→3）已关闭";
                return;
            }
        }
        else if (gcdActive && gcdRemaining > 0f)
        {
            if (pendingCooperationActionId != 0)
            {
                ReportCooperationDiagnostic(
                    $"待续 {GetActionName(pendingCooperationActionId)}（{pendingCooperationActionId}）；Buff={pendingCooperationStatusId}，"
                    + $"是否存在={HasSelfStatus(pendingCooperationStatusId)}，GCD剩余={gcdRemaining:0.###}s，能力技计数={abilitiesUsedInGcdWindow}/{MaxAbilitiesPerGcdWindow}",
                    $"pending-{pendingCooperationActionId}-{pendingCooperationStatusId}-{HasSelfStatus(pendingCooperationStatusId)}");
            }

            if (gcdRemaining > CooperationMinimumGcdRemaining
                && TryUseCooperationSecondStage(actionManager, gauge, player, target, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (abilitiesUsedInGcdWindow >= MaxAbilitiesPerGcdWindow)
            {
                StatusText = "等待 GCD 转好";
                NextActionName = "-";
                NextActionReason = pendingCooperationActionId != 0
                    ? "本 GCD 能力技次数已满；协作二段保留到下一安全窗口"
                    : "本 GCD 窗口已放满 2 个能力技，等待 GCD";
                return;
            }

            if (gcdRemaining > CooperationMinimumGcdRemaining
                && TryUseCooperationFirstStage(actionManager, gauge, player, target, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (TryUseCapture(actionManager, player, target, canCapture, hasOwnCapture, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoBorrowEnabled
                && TryUseBorrow(actionManager, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoBeastSkillEnabled
                && TryUseBeastSkill(actionManager, target.GameObjectId, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoDrumEnabled
                && (gauge.BeastHeartStacks == 0 || (gauge.BeastHeartStacks == 3 && gauge.Tp == 0))
                && TryUseEnabledSelfAction(actionManager, DrumActionId, "鼓劲", now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoCheerEnabled
                && (gauge.BeastSoulStacks == 0 || (gauge.BeastSoulStacks == 3 && gauge.BeastPower == 0))
                && TryUseEnabledSelfAction(actionManager, CheerActionId, "声援", now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoReleaseEnabled
                && gauge.SummonEntry != null
                && TryUseReleaseAction(actionManager, gauge, target, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (pendingCooperationActionId == 0
                && !HasRecoverableCooperationBuff()
                && TryUseFinalStrike(actionManager, gauge, target.GameObjectId, now))
            {
                abilitiesUsedInGcdWindow++;
                return;
            }

            if (configuration.AutoSafeShieldEnabled
                && now >= nextSafeShieldAttemptUtc
                && BeastmasterActionHelper.IsPlayerInActionRange(
                    player,
                    target,
                    SafeShieldActionId,
                    out var shieldDistance,
                    out _)
                && shieldDistance <= SafeShieldRange
                && TryUseEnabledSelfAction(actionManager, SafeShieldActionId, "安全盾牌", now, target.GameObjectId))
            {
                nextSafeShieldAttemptUtc = now.Add(SafeShieldRequestCooldown);
                abilitiesUsedInGcdWindow++;
                return;
            }

        }

        StatusText = abilitiesUsedInGcdWindow >= MaxAbilitiesPerGcdWindow
            ? "等待 GCD 转好"
            : "等待可执行状态";
        NextActionName = "-";
        NextActionReason = abilitiesUsedInGcdWindow >= MaxAbilitiesPerGcdWindow
            ? "本 GCD 窗口已放满 2 个能力技，等待 GCD"
            : !gcdActive && !gcdReady
                ? "GCD 已转好，但当前基础连击因目标、距离或动作状态暂不可用"
                : $"等待 GCD 或能力技就绪（GCD 剩余 {gcdRemaining:0.###} 秒）";
    }

    private static bool IsArenaTerritory(uint territoryId)
        => territoryId is >= 1339 and <= 1343;

    private void FlushExecutionProbes()
    {
        if (!configuration.AutoRecoveryItemDiagnosticsEnabled)
        {
            return;
        }

        var printed = 0;
        while (printed < 4 && crucibleItemService.TryTakeExecutionProbe(out var probe))
        {
            DalamudApi.ChatGui.Print($"[驯兽师恢复药诊断 {DateTime.Now:HH:mm:ss}] {probe}");
            printed++;
        }
    }

    private void UpdateRecoveryItemDiagnostic(DateTime now)
    {
        if (recoveryDiagnosticItemId == 0)
        {
            return;
        }

        if (!configuration.AutoRecoveryItemDiagnosticsEnabled)
        {
            ClearRecoveryItemDiagnostic();
            return;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null || player.MaxHp == 0)
        {
            PrintRecoveryItemDiagnostic(0, 0, recoveryDiagnosticItemId, false, "角色状态不可用");
            ClearRecoveryItemDiagnostic();
            return;
        }

        if (player.CurrentHp > recoveryDiagnosticStartHp)
        {
            PrintRecoveryItemDiagnostic(player.CurrentHp, player.MaxHp, recoveryDiagnosticItemId, true, string.Empty);
            ClearRecoveryItemDiagnostic();
            return;
        }

        if (now >= recoveryDiagnosticDeadlineUtc)
        {
            PrintRecoveryItemDiagnostic(player.CurrentHp, player.MaxHp, recoveryDiagnosticItemId, false, "请求后 3 秒内血量未上升");
            ClearRecoveryItemDiagnostic();
        }
    }

    private void HandleRecoveryItemDispatch(DateTime now)
    {
        if (crucibleItemService.TryTakeRecoveryDispatchFailure(out var failedItemId, out var failureReason))
        {
            var player = DalamudApi.ObjectTable.LocalPlayer;
            if (player != null && player.MaxHp > 0)
            {
                var hpPercent = player.CurrentHp * 100f / player.MaxHp;
                ReportRecoveryItemFailure(player, hpPercent, GetRecoveryItemName(failedItemId), failureReason, now);
            }
            return;
        }

        if (!crucibleItemService.TryTakeDispatchedRecoveryItem(out var itemId)
            || !configuration.AutoRecoveryItemDiagnosticsEnabled)
        {
            return;
        }

        var localPlayer = DalamudApi.ObjectTable.LocalPlayer;
        if (localPlayer == null || localPlayer.MaxHp == 0)
        {
            PrintRecoveryItemDiagnostic(0, 0, itemId, false, "实际分派后角色状态不可用");
            return;
        }

        recoveryDiagnosticItemId = itemId;
        recoveryDiagnosticStartHp = localPlayer.CurrentHp;
        recoveryDiagnosticDeadlineUtc = now.AddSeconds(3);
        recoveryDiagnosticDetail = crucibleItemService.LastDiagnostic;
    }

    private void ReportRecoveryItemFailure(
        IBattleChara player,
        float hpPercent,
        string itemName,
        string reason,
        DateTime now)
    {
        if (!configuration.AutoRecoveryItemDiagnosticsEnabled
            || (reason == lastRecoveryFailureReason && now - lastRecoveryFailureChatUtc < TimeSpan.FromSeconds(2)))
        {
            return;
        }

        lastRecoveryFailureReason = reason;
        lastRecoveryFailureChatUtc = now;
        DalamudApi.ChatGui.Print(
            $"[驯兽师恢复药诊断 {DateTime.Now:HH:mm:ss}] 当前血量 {player.CurrentHp}/{player.MaxHp}（{hpPercent:0.#}%），吃{itemName}失败（{reason}）");
    }

    private void PrintRecoveryItemDiagnostic(uint currentHp, uint maxHp, ushort itemId, bool success, string reason)
    {
        var hpPercent = maxHp == 0 ? 0f : currentHp * 100f / maxHp;
        var result = success ? "成功" : $"失败（{reason}）";
        DalamudApi.ChatGui.Print(
            $"[驯兽师恢复药诊断 {DateTime.Now:HH:mm:ss}] 当前血量 {currentHp}/{maxHp}（{hpPercent:0.#}%），吃{GetRecoveryItemName(itemId)}{result}"
            + $"\n  分派详情：{recoveryDiagnosticDetail}"
            + $"\n  道具状态：{crucibleItemService.DescribeRecoveryItemSlot(itemId)}");
    }

    private void ClearRecoveryItemDiagnostic()
    {
        recoveryDiagnosticItemId = 0;
        recoveryDiagnosticStartHp = 0;
        recoveryDiagnosticDeadlineUtc = DateTime.MinValue;
        recoveryDiagnosticDetail = string.Empty;
    }

    private static string GetRecoveryItemName(ushort itemId)
        => itemId switch
        {
            140 => "魔兽恢复药套装",
            139 => "星之沙",
            134 => "吸血鬼之牙",
            135 => "魔兽吸血药",
            80 or 81 or 82 => $"{itemId - 79}级魔兽药粉",
            76 or 77 or 78 or 79 => $"{itemId - 75}级魔兽恢复药",
            _ => $"奇弈恢复道具 {itemId}",
        };

    private void ResetCaptureState()
    {
        captureTargetId = 0;
        capturePendingUntilUtc = DateTime.MinValue;
        CaptureState = "未开始";
    }

    private unsafe bool TryUseEnabledSelfAction(
        ActionManager* actionManager,
        uint actionId,
        string actionName,
        DateTime now,
        ulong targetId = 0)
    {
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, targetId);
        if (actionStatus != 0)
        {
            ReportAutoOutputDiagnostic(actionName, $"技能系统状态码 {actionStatus}", $"status-{actionStatus}");
            return false;
        }

        StatusText = $"自动使用{actionName}...";
        NextActionName = actionName;
        NextActionReason = "高级技能已就绪";
        if (!actionManager->UseAction(ActionType.Action, actionId, targetId))
        {
            ReportAutoOutputDiagnostic(actionName, "UseAction 返回 false", "use-action-false");
            return false;
        }

        ReportAutoOutputSuccess(actionName, actionId);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private bool IsThirdFormEnabled
        => configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled;

    private unsafe void EmitAutoOutputDiagnosticSummary(
        ActionManager* actionManager,
        IBattleChara player,
        IBattleChara target,
        BeastmasterGaugeSnapshot gauge,
        float targetHpPercent,
        bool canCapture,
        DateTime now)
    {
        if (!configuration.AutoOutputDiagnosticsEnabled)
        {
            return;
        }

        var items = new List<(string Name, string StateKey, string Detail)>();
        void Add(string name, bool available, string reason = "")
        {
            var detail = available ? "可用" : $"不可用:{reason}";
            var stateKey = available ? "可用" : $"不可用:{GetDiagnosticReasonKey(reason)}";
            items.Add((name, stateKey, detail));
        }

        if (configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
        {
            if (pendingCooperationActionId != 0)
            {
                var status = actionManager->GetActionStatus(ActionType.Action, pendingCooperationActionId, target.GameObjectId);
                Add("御兽", status == 0, status == 0 ? "" : $"状态码 {status}");
            }
            else if (TryGetCooperationAction(gauge, configuration.BeastHeartCooperationEnabled, out var cooperationId, out _, out _))
            {
                var status = actionManager->GetActionStatus(ActionType.Action, cooperationId, target.GameObjectId);
                Add("御兽", status == 0, status == 0 ? "" : gauge.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement || gauge.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement
                    ? $"资源 技 {gauge.Tp}/250 兽 {gauge.BeastPower}/250"
                    : $"状态码 {status}");
            }
            else
            {
                Add("御兽", false, $"资源 技 {gauge.Tp}/250 兽 {gauge.BeastPower}/250");
            }
        }

        if (configuration.AutoFinalStrikeEnabled
            && TryGetFinalStrikeSettings(gauge.WhistleIndex, out var finalEnabled, out var finalThreshold))
        {
            var finalStatus = actionManager->GetActionStatus(ActionType.Action, FinalStrikeActionId, target.GameObjectId);
            var releaseBlocked = BeastmasterFinalStrikeLock.IsBlocked(FinalStrikeActionId, now);
            Add("最后一击", finalEnabled && gauge.SummonMaxHp > 0 && gauge.SummonHpPercent <= finalThreshold && finalStatus == 0 && !releaseBlocked,
                !finalEnabled ? "当前笛位关闭"
                    : gauge.SummonMaxHp == 0 ? "无宝宝"
                    : gauge.SummonHpPercent > finalThreshold ? $"宝宝血量 {gauge.SummonHpPercent:0.#}%/{finalThreshold:0.#}%"
                    : releaseBlocked ? BeastmasterFinalStrikeLock.GetBlockReason(FinalStrikeActionId, now)
                    : finalStatus == 0 ? "" : $"状态码 {finalStatus}");
        }

        if (configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled)
        {
            var thirdReady = gauge.BeastHeartStacks >= 3 && (gauge.HasWhiteStatus || gauge.HasPurpleStatus);
            Add("万象流转", thirdReady, thirdReady ? "" : $"资源/状态不足（兽心 {gauge.BeastHeartStacks} 层）");
        }

        if (configuration.AutoReleaseEnabled
            && gauge.SummonEntry != null)
        {
            var releaseId = actionManager->GetAdjustedActionId(BeastmasterReleaseBaseActionId);
            if (releaseId == 0)
            {
                Add("释放", false, "无运行时技能");
            }
            else
            {
                var status = actionManager->GetActionStatus(ActionType.Action, releaseId, target.GameObjectId);
                var inRange = BeastmasterActionHelper.IsSummonInActionRange(
                    target,
                    gauge.SummonEntry.ReleaseActionId,
                    out var distance,
                    out var range);
                Add("释放", status == 0 && inRange,
                    status != 0 ? $"状态码 {status}" : inRange ? "" : $"宝宝距离 {distance:0.#}/{range:0.#}");
            }
        }

        if (configuration.AutoSafeShieldEnabled)
        {
            var shieldStatus = actionManager->GetActionStatus(ActionType.Action, SafeShieldActionId, target.GameObjectId);
            var shieldInRange = BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                SafeShieldActionId,
                out var shieldDistance,
                out _);
            Add("安全盾牌", shieldStatus == 0 && shieldInRange && shieldDistance <= SafeShieldRange,
                shieldDistance > SafeShieldRange
                    ? $"距离 {shieldDistance:0.#}/{SafeShieldRange:0.#}"
                    : shieldStatus == 0 ? "" : $"状态码 {shieldStatus}");
        }

        if (configuration.AutoCaptureTryCapture)
        {
            Add("捕获", canCapture, canCapture ? "" : $"目标血量 {targetHpPercent:0.#}%/{configuration.CaptureHpThreshold:0.#}%");
        }

        if (configuration.BasicComboEnabled)
        {
            var comboId = actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;
            var comboStatus = actionManager->GetActionStatus(ActionType.Action, comboId, target.GameObjectId);
            var comboInRange = BeastmasterActionHelper.IsPlayerInActionRange(player, target, comboId, out var comboDistance, out var comboRange);
            Add("基础技能", comboStatus == 0 && comboInRange,
                comboStatus != 0 ? $"状态码 {comboStatus}" : comboInRange ? "" : $"距离 {comboDistance:0.#}/{comboRange:0.#}");
        }

        var summary = string.Join(" ", items.Select(item => $"{item.Name}[{item.Detail}]"));
        var summaryKey = string.Join(" ", items.Select(item => $"{item.Name}[{item.StateKey}]"));
        if (items.Count > 0 && summaryKey != lastAutoOutputSummary)
        {
            lastAutoOutputSummary = summaryKey;
            RecordBattleLog($"自动输出诊断：{summary}");
        }

    }

    private static string GetDiagnosticReasonKey(string reason)
    {
        if (reason.StartsWith("状态码", StringComparison.Ordinal)) return reason;
        if (reason.StartsWith("距离", StringComparison.Ordinal)
            || reason.StartsWith("宝宝距离", StringComparison.Ordinal)) return "距离";
        if (reason.StartsWith("资源", StringComparison.Ordinal)) return "资源不足";
        if (reason.StartsWith("宝宝血量", StringComparison.Ordinal)) return "宝宝血量";
        if (reason.StartsWith("资源/状态不足", StringComparison.Ordinal)) return "资源/状态不足";
        return reason;
    }

    private void ResetTargetScopedState()
    {
        activeTargetId = 0;
        abilitiesUsedInGcdWindow = 0;
        lastAutoOutputSummary = string.Empty;
        lastSuccessfulActionUtc = DateTime.MinValue;
        nextActionUtc = DateTime.MinValue;
        nextReleaseAttemptUtc = DateTime.MinValue;
        nextFinalStrikeAttemptUtc = DateTime.MinValue;
        ResetCaptureState();
        ResetCooperationState("目标切换");
    }

    private static unsafe uint GetBasicComboActionId(ActionManager* actionManager, IBattleChara player)
    {
        if (actionManager->Combo.Timer > 0f && actionManager->Combo.Action == BiteActionId && player.Level >= 12)
        {
            return ShieldActionId;
        }

        if (actionManager->Combo.Timer > 0f && actionManager->Combo.Action == SmashActionId && player.Level >= 2)
        {
            return BiteActionId;
        }

        return SmashActionId;
    }

    private unsafe bool TryUseBasicCombo(
        ActionManager* actionManager,
        IBattleChara player,
        IBattleChara target,
        DateTime now)
    {
        var actionId = GetBasicComboActionId(actionManager, player);
        StatusText = "自动攻击中...";
        NextActionName = GetActionName(actionId);
        NextActionReason = "基础连击（1→2→3）";

        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                actionId,
                out var distance,
                out var range))
        {
            NextActionReason = $"等待进入技能射程（当前 {distance:0.##}/{range:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {distance:0.##}/{range:0.##} yalms）", "range");
            return false;
        }

        var availability = BeastmasterActionHelper.GetAvailability(actionId, target.GameObjectId);
        NextActionReason = availability.Reason;
        if (!availability.CanUse)
        {
            ReportAutoOutputDiagnostic(NextActionName, availability.Reason, availability.Reason);
            return false;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, availability.ActionId, target.GameObjectId);
        if (actionStatus != 0
            || !actionManager->UseAction(ActionType.Action, availability.ActionId, target.GameObjectId))
        {
            ReportAutoOutputDiagnostic(NextActionName, $"技能系统状态码 {actionStatus}", $"status-{actionStatus}");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, availability.ActionId);
        nextActionUtc = now.AddMilliseconds(250);
        return true;
    }

    private unsafe bool TryUseCooperationFirstStage(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara player,
        IBattleChara target,
        DateTime now)
    {
        if ((!configuration.BeastHeartCooperationEnabled && !configuration.BeastSoulCooperationEnabled)
            || pendingCooperationActionId != 0
            || HasRecoverableCooperationBuff()
            || !TryGetCooperationAction(gauge, configuration.BeastHeartCooperationEnabled, out var cooperationActionId, out var cooperationFollowUpId, out var cooperationStatusId))
        {
            return false;
        }

        StatusText = "自动协作技中...";
        NextActionName = GetActionName(cooperationActionId);
        NextActionReason = $"协作技第一段，下一段：{GetActionName(cooperationFollowUpId)}";

        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                cooperationActionId,
                out var distance,
                out var range))
        {
            NextActionReason = $"等待进入协作技射程（当前 {distance:0.##}/{range:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {distance:0.##}/{range:0.##} yalms）", "range");
            return false;
        }

        var status = actionManager->GetActionStatus(ActionType.Action, cooperationActionId, target.GameObjectId);
        if (!TryUseAdvancedAction(actionManager, cooperationActionId, target.GameObjectId, status))
        {
            NextActionReason = $"协作技请求失败（状态码 {status}）";
            ReportAutoOutputDiagnostic(NextActionName,
                $"技能系统状态码 {status}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                $"status-{status}");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, cooperationActionId);
        nextActionUtc = now.AddMilliseconds(700);
        pendingCooperationActionId = cooperationFollowUpId;
        pendingCooperationStatusId = cooperationStatusId;
        pendingCooperationUntilUtc = now.AddSeconds(7);
        ReportCooperationDiagnostic(
            $"已建立{(configuration.BeastHeartCooperationEnabled ? "御兽（黄豆）" : "兽灵（蓝豆）")}待续："
            + $"一段 {GetActionName(cooperationActionId)}（{cooperationActionId}），"
            + $"二段 {GetActionName(cooperationFollowUpId)}（{cooperationFollowUpId}），"
            + $"等待 {GetAttributeStatusName(cooperationStatusId)}（{cooperationStatusId}），"
            + $"技力={gauge.Tp}/250，兽力={gauge.BeastPower}/250，窗口=7s",
            $"created-{cooperationActionId}-{cooperationFollowUpId}-{cooperationStatusId}");
        return true;
    }

    private unsafe bool TryUseCooperationSecondStage(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara player,
        IBattleChara target,
        DateTime now)
    {
        if (!configuration.BeastHeartCooperationEnabled && !configuration.BeastSoulCooperationEnabled)
        {
            return false;
        }

        if (pendingCooperationActionId == 0)
        {
            if (!TryGetCooperationFollowUpFromStatuses(out var recoveredActionId, out var recoveredStatusId))
            {
                return false;
            }

            pendingCooperationActionId = recoveredActionId;
            pendingCooperationStatusId = recoveredStatusId;
            pendingCooperationUntilUtc = now.AddSeconds(7);
            ReportCooperationDiagnostic(
                $"从人物现有{GetAttributeStatusName(recoveredStatusId)}（{recoveredStatusId}）恢复二段候选："
                + $"{GetActionName(recoveredActionId)}（{recoveredActionId}）",
                $"recovered-{recoveredActionId}-{recoveredStatusId}");
        }

        var pendingActionId = pendingCooperationActionId;
        StatusText = "自动协作技中...";
        NextActionName = GetActionName(pendingActionId);
        if (pendingCooperationStatusId != 0 && !HasSelfStatus(pendingCooperationStatusId))
        {
            NextActionReason = $"等待自身获得{GetAttributeStatusName(pendingCooperationStatusId)}（{pendingCooperationStatusId}）";
            ReportCooperationDiagnostic($"等待{GetAttributeStatusName(pendingCooperationStatusId)}，准备{NextActionName}", $"wait-{pendingCooperationStatusId}");
            return false;
        }

        NextActionReason = $"自身已有{GetAttributeStatusName(pendingCooperationStatusId)}，释放协作技第二段";
        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                pendingActionId,
                out var followUpDistance,
                out var followUpRange))
        {
            NextActionReason = $"等待进入协作技射程（当前 {followUpDistance:0.##}/{followUpRange:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {followUpDistance:0.##}/{followUpRange:0.##} yalms）", "range");
            return false;
        }

        var cooperationStatus = actionManager->GetActionStatus(ActionType.Action, pendingActionId, target.GameObjectId);
        if (BeastmasterFinalStrikeLock.IsBlocked(pendingActionId, now))
        {
            NextActionReason = BeastmasterFinalStrikeLock.GetBlockReason(pendingActionId, now);
            ReportCooperationDiagnostic(
                $"{NextActionName}被保护窗口阻止：{NextActionReason}",
                $"protected-{pendingActionId}");
            return false;
        }
        if (!TryUseAdvancedAction(actionManager, pendingActionId, target.GameObjectId, cooperationStatus))
        {
            ReportCooperationDiagnostic(
                $"{NextActionName}不可用：状态码 {cooperationStatus}（等待窗口剩余 {(pendingCooperationUntilUtc - now).TotalSeconds:0.#} 秒）",
                $"status-{cooperationStatus}");
            ReportAutoOutputDiagnostic(NextActionName,
                $"技能系统状态码 {cooperationStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                $"status-{cooperationStatus}");
            return false;
        }

        ReportCooperationDiagnostic($"已完成协作第二段：{NextActionName}", "completed");
        ReportAutoOutputSuccess(NextActionName, pendingActionId);
        nextActionUtc = now.AddMilliseconds(700);
        ResetCooperationState("协作二段成功完成");
        return true;
    }

    private bool HasRecoverableCooperationBuff()
        => TryGetCooperationFollowUpFromStatuses(out _, out _);

    private bool TryGetCooperationFollowUpFromStatuses(out uint actionId, out uint statusId)
    {
        actionId = 0;
        statusId = 0;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null
            || (!configuration.BeastHeartCooperationEnabled && !configuration.BeastSoulCooperationEnabled))
        {
            return false;
        }

        var statusIds = player.StatusList.Select(status => status.StatusId).ToHashSet();
        statusId = new uint[] { 4598, 4595, 4596, 4597 }
            .FirstOrDefault(statusIds.Contains);
        if (statusId == 0)
        {
            return false;
        }

        if (configuration.BeastSoulCooperationEnabled)
        {
            actionId = BeastmasterUltimateActionId;
            return true;
        }

        actionId = statusId switch
        {
            4598 => 44889u,
            4595 => 44884u,
            4596 => 44887u,
            4597 => 44888u,
            _ => 0u,
        };
        return actionId != 0;
    }

    private unsafe bool TryUseBorrow(ActionManager* actionManager, DateTime now)
    {
        if (actionManager->GetActionStatus(ActionType.Action, BorrowActionId, 0) != 0)
        {
            return false;
        }

        var adjustedBeastSkill = actionManager->GetAdjustedActionId(BeastSkillActionId);
        if (adjustedBeastSkill is >= 44896 and <= 44903)
        {
            return false;
        }

        StatusText = "自动借用...";
        NextActionName = GetActionName(BorrowActionId);
        NextActionReason = "借用当前魔兽的本能技能";
        if (!actionManager->UseAction(ActionType.Action, BorrowActionId, 0))
        {
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, BorrowActionId);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private unsafe bool TryUseBeastSkill(ActionManager* actionManager, ulong targetId, DateTime now)
    {
        var adjustedActionId = actionManager->GetAdjustedActionId(BeastSkillActionId);
        if (adjustedActionId is < 44896 or > 44903)
        {
            return false;
        }

        if (actionManager->GetActionStatus(ActionType.Action, adjustedActionId, targetId) != 0)
        {
            return false;
        }

        StatusText = "自动魔兽技...";
        NextActionName = GetActionName(adjustedActionId);
        NextActionReason = "释放借用技能";
        if (!actionManager->UseAction(ActionType.Action, adjustedActionId, targetId))
        {
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, adjustedActionId);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private unsafe bool TryUseCapture(
        ActionManager* actionManager,
        IBattleChara player,
        IBattleChara target,
        bool canCapture,
        bool hasOwnCapture,
        DateTime now)
    {
        if (!configuration.AutoCaptureTryCapture
            || !canCapture
            || (!configuration.ForceCaptureEnabled && hasOwnCapture))
        {
            return false;
        }

        var actionId = captureActionId;
        StatusText = configuration.ForceCaptureEnabled ? "强制捕获中..." : "自动捕获中...";
        NextActionName = GetActionName(actionId);
        NextActionReason = configuration.ForceCaptureEnabled
            ? "强制捕获模式，无视捕获状态但仍受血量阈值限制"
            : "目标血量达到捕获阈值";

        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                actionId,
                out var distance,
                out var range))
        {
            NextActionReason = $"捕获超出射程（当前 {distance:0.##}/{range:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {distance:0.##}/{range:0.##} yalms）", "range");
            return false;
        }

        var availability = BeastmasterActionHelper.GetAvailability(actionId, target.GameObjectId);
        NextActionReason = availability.Reason;
        if (!availability.CanUse)
        {
            ReportAutoOutputDiagnostic(NextActionName, availability.Reason, availability.Reason);
            return false;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
        if (actionStatus != 0
            || !actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId))
        {
            CaptureState = "捕获请求失败";
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false 或状态码非零", "use-action-false");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, actionId);
        nextActionUtc = now.AddMilliseconds(700);
        capturePendingUntilUtc = now.AddMilliseconds(1200);
        CaptureState = "已发送请求，等待结果";
        return true;
    }

    private unsafe bool TryUseReleaseAction(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara target,
        DateTime now)
    {
        if (!TryGetReleaseSettings(gauge.WhistleIndex, out var enabled, out var targetHpThreshold))
        {
            ReportAutoOutputDiagnostic("释放", $"当前兽笛 {gauge.WhistleIndex} 无对应的 1/2/3 笛设置", "whistle");
            return false;
        }

        if (!enabled)
        {
            ReportAutoOutputDiagnostic("释放", $"当前 {gauge.WhistleIndex} 笛独立开关未开启", "disabled");
            return false;
        }

        var targetHpPercent = target.MaxHp == 0 ? 100f : target.CurrentHp * 100f / target.MaxHp;
        if (configuration.AutoReleaseBossOnly
            && DalamudApi.ObjectTable.LocalPlayer is IBattleChara player
            && (player.MaxHp == 0 || target.MaxHp <= (double)player.MaxHp * 5d))
        {
            ReportAutoOutputDiagnostic("释放", $"目标未被判定为 BOSS（目标最大 HP {target.MaxHp}，自身最大 HP {player.MaxHp} × 5）", "boss");
            return false;
        }
        if (targetHpPercent > targetHpThreshold)
        {
            ReportAutoOutputDiagnostic("释放", $"目标血量 {targetHpPercent:0.#}% 高于当前笛阈值 {targetHpThreshold:0.#}%", "hp");
            return false;
        }

        if (now < nextReleaseAttemptUtc)
        {
            return false;
        }

        var availability = BeastmasterActionHelper.GetAvailability(
            BeastmasterReleaseBaseActionId,
            target.GameObjectId,
            useAdjustedActionId: true);
        if (!availability.CanUse)
        {
            NextActionName = availability.ActionName;
            NextActionReason = availability.Reason;
            ReportAutoOutputDiagnostic(NextActionName,
                $"{availability.Reason}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                availability.Reason);
            nextReleaseAttemptUtc = now.AddMilliseconds(500);
            return false;
        }

        if (!BeastmasterActionHelper.IsSummonInActionRange(
                target,
                gauge.SummonEntry?.ReleaseActionId ?? availability.ActionId,
                out var summonDistance,
                out var actionRange))
        {
            NextActionName = availability.ActionName;
            NextActionReason = actionRange <= 0f
                ? "等待识别召唤兽和释放射程"
                : $"等待召唤兽进入释放距离（当前 {summonDistance:0.##}/{actionRange:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName,
                actionRange <= 0f ? NextActionReason : $"召唤兽距离不足（当前 {summonDistance:0.##}/{actionRange:0.##} yalms）",
                "summon-range");
            nextReleaseAttemptUtc = now.AddMilliseconds(250);
            return false;
        }

        StatusText = "自动释放魔兽技能...";
        NextActionName = availability.ActionName;
        NextActionReason = $"{gauge.WhistleIndex} 笛目标血量 {targetHpPercent:0.#}% 达到阈值 {targetHpThreshold:0.#}%";
        if (!actionManager->UseAction(ActionType.Action, availability.ActionId, target.GameObjectId))
        {
            NextActionReason = "释放技能请求失败";
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            nextReleaseAttemptUtc = now.AddSeconds(1);
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, availability.ActionId);
        BeastmasterFinalStrikeLock.RecordRelease(now);
        nextReleaseAttemptUtc = now.AddMilliseconds(500);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private unsafe bool TryUseFinalStrike(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        ulong targetId,
        DateTime now)
    {
        if (!configuration.AutoFinalStrikeEnabled)
        {
            return false;
        }

        if (!TryGetFinalStrikeSettings(gauge.WhistleIndex, out var enabled, out var hpThreshold))
        {
            ReportAutoOutputDiagnostic("最后一击", $"当前兽笛 {gauge.WhistleIndex} 无对应的 1/2/3 笛设置", "whistle");
            return false;
        }

        if (!enabled)
        {
            ReportAutoOutputDiagnostic("最后一击", $"当前 {gauge.WhistleIndex} 笛独立开关未开启", "disabled");
            return false;
        }

        if (gauge.SummonDataId == 0 || gauge.SummonMaxHp == 0)
        {
            ReportAutoOutputDiagnostic("最后一击", "未识别到有效召唤兽或宝宝血量", "summon");
            return false;
        }

        if (gauge.SummonHpPercent > hpThreshold)
        {
            ReportAutoOutputDiagnostic(
                "最后一击",
                $"宝宝血量 {gauge.SummonHpPercent:0.#}% 高于阈值 {hpThreshold:0.#}%",
                "hp");
            return false;
        }

        if (now < nextFinalStrikeAttemptUtc)
        {
            return false;
        }

        if (BeastmasterFinalStrikeLock.IsBlocked(FinalStrikeActionId, now))
        {
            NextActionName = GetActionName(FinalStrikeActionId);
            NextActionReason = BeastmasterFinalStrikeLock.GetBlockReason(FinalStrikeActionId, now);
            ReportAutoOutputDiagnostic(NextActionName, NextActionReason, "release-lock");
            nextFinalStrikeAttemptUtc = now.AddMilliseconds(250);
            return false;
        }

        NextActionName = GetActionName(FinalStrikeActionId);
        if (DalamudApi.ObjectTable.LocalPlayer is { } player
            && DalamudApi.TargetManager.Target is IBattleChara target
            && !BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                FinalStrikeActionId,
                out var playerDistance,
                out var actionRange))
        {
            NextActionReason = $"等待进入技能射程（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName,
                $"距离不足（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）",
                "range");
            nextFinalStrikeAttemptUtc = now.AddMilliseconds(250);
            return false;
        }

        if (configuration.AutoFinalStrikeWaitForRelease)
        {
            var shouldWaitForRelease = TryGetReleaseSettings(gauge.WhistleIndex, out var releaseEnabled, out var releaseThreshold)
                && releaseEnabled
                && DalamudApi.TargetManager.Target is IBattleChara hpTarget
                && hpTarget.MaxHp > 0
                && hpTarget.CurrentHp * 100f / hpTarget.MaxHp <= releaseThreshold;
            var releaseActionId = shouldWaitForRelease
                ? actionManager->GetAdjustedActionId(BeastmasterReleaseBaseActionId)
                : 0u;
            var releaseReady = shouldWaitForRelease
                && releaseActionId != 0
                && actionManager->GetActionStatus(ActionType.Action, releaseActionId, targetId) == 0;
            if (releaseReady)
            {
                var releaseInRange = DalamudApi.TargetManager.Target is IBattleChara releaseTarget
                    && gauge.SummonEntry is { } summonEntry
                    && BeastmasterActionHelper.IsSummonInActionRange(
                        releaseTarget,
                        summonEntry.ReleaseActionId,
                        out _,
                        out _);
                NextActionReason = releaseInRange
                    ? "等待当前魔兽先使用释放"
                    : "释放可用但召唤兽尚未进入释放距离，等待释放完成后再使用最后一击";
                ReportAutoOutputDiagnostic(NextActionName, NextActionReason, "wait-release");
                return false;
            }

            ReportAutoOutputDiagnostic(
                NextActionName,
                $"释放已不可用，继续检查最后一击（释放 ActionId {releaseActionId}，状态码 {actionManager->GetActionStatus(ActionType.Action, releaseActionId, targetId)}）",
                "release-complete");
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, FinalStrikeActionId, targetId);
        if (actionStatus != 0)
        {
            NextActionReason = $"技能暂不可用（状态码 {actionStatus}）";
            ReportAutoOutputDiagnostic(NextActionName,
                $"技能系统状态码 {actionStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                $"status-{actionStatus}");
            nextFinalStrikeAttemptUtc = now.AddMilliseconds(250);
            return false;
        }

        StatusText = "自动最后一击...";
        NextActionReason = $"{gauge.WhistleIndex} 笛宝宝血量 {gauge.SummonHpPercent:0.#}% 达到阈值 {hpThreshold:0.#}%";
        if (!actionManager->UseAction(ActionType.Action, FinalStrikeActionId, targetId))
        {
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            nextFinalStrikeAttemptUtc = now.AddMilliseconds(500);
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, FinalStrikeActionId);
        BeastmasterFinalStrikeLock.RecordFinalStrike(now);
        nextFinalStrikeAttemptUtc = now.AddMilliseconds(700);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private bool TryGetFinalStrikeSettings(byte whistleIndex, out bool enabled, out float hpThreshold)
    {
        (enabled, hpThreshold) = whistleIndex switch
        {
            1 => (configuration.AutoFinalStrikeWhistleOneEnabled, configuration.AutoFinalStrikeWhistleOneHpThreshold),
            2 => (configuration.AutoFinalStrikeWhistleTwoEnabled, configuration.AutoFinalStrikeWhistleTwoHpThreshold),
            3 => (configuration.AutoFinalStrikeWhistleThreeEnabled, configuration.AutoFinalStrikeWhistleThreeHpThreshold),
            _ => (false, 0f),
        };
        return whistleIndex is >= 1 and <= 3;
    }

    private bool TryGetReleaseSettings(byte whistleIndex, out bool enabled, out float targetHpThreshold)
    {
        (enabled, targetHpThreshold) = whistleIndex switch
        {
            1 => (configuration.AutoReleaseWhistleOneEnabled, configuration.AutoReleaseWhistleOneTargetHpThreshold),
            2 => (configuration.AutoReleaseWhistleTwoEnabled, configuration.AutoReleaseWhistleTwoTargetHpThreshold),
            3 => (configuration.AutoReleaseWhistleThreeEnabled, configuration.AutoReleaseWhistleThreeTargetHpThreshold),
            _ => (false, 0f),
        };
        return whistleIndex is >= 1 and <= 3;
    }


    private unsafe bool TryUseThirdFormAction(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        ulong targetId,
        DateTime now)
    {
        uint actionId;
        ulong actionTargetId;
        string reason;
        if (gauge.BeastHeartStacks >= 3 && (gauge.HasWhiteStatus || gauge.HasPurpleStatus))
        {
            actionId = DrumActionId;
            actionTargetId = 0;
            reason = $"御兽之心 {gauge.BeastHeartStacks} 层，进入三式流程";
        }
        else if (gauge.HasWhiteStatus || gauge.HasPurpleStatus)
        {
            actionId = (gauge.HasWhiteStatus, configuration.PhysicalThirdFormEnabled) switch
            {
                (true, true) => WhitePhysicalThirdFormActionId,
                (true, false) => WhiteMagicalThirdFormActionId,
                (false, true) => PurplePhysicalThirdFormActionId,
                (false, false) => PurpleMagicalThirdFormActionId,
            };
            actionTargetId = targetId;
            reason = $"{(gauge.HasWhiteStatus ? "白（生息）" : "黑/紫（死灭）")} + {(configuration.PhysicalThirdFormEnabled ? "万象流转（物理）" : "万象流转（魔法）")}";
        }
        else
        {
            return false;
        }

        StatusText = "自动三式中...";
        NextActionName = GetActionName(actionId);
        if (actionTargetId != 0
            && DalamudApi.ObjectTable.LocalPlayer is { } player
            && DalamudApi.TargetManager.Target is IBattleChara target
            && !BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                actionId,
                out var playerDistance,
                out var actionRange))
        {
            NextActionReason = $"等待进入技能射程（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
            ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）", "range");
            return false;
        }
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, actionTargetId);
        NextActionReason = actionStatus == 0 ? reason : $"{reason}，技能暂不可用（状态码 {actionStatus}）";
        if (actionStatus != 0 || !actionManager->UseAction(ActionType.Action, actionId, actionTargetId))
        {
            ReportAutoOutputDiagnostic(NextActionName,
                $"技能系统状态码 {actionStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                $"status-{actionStatus}");
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, actionId);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
    }

    private unsafe bool TryUseAutoWhistle(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        DateTime now)
    {
        var hasSummon = gauge.SummonEntry != null || gauge.WhistleIndex is >= 1 and <= 3;
        if (hasSummon)
        {
            ResetAutoWhistle();
            return false;
        }

        if (pendingWhistleActionId != 0 && now < pendingWhistleUntilUtc)
        {
            StatusText = "等待魔兽召唤...";
            NextActionName = GetActionName(pendingWhistleActionId);
            NextActionReason = "兽笛请求已发送，等待召唤确认";
            return true;
        }

        if (pendingWhistleActionId != 0)
        {
            pendingWhistleActionId = 0;
            pendingWhistleUntilUtc = DateTime.MinValue;
        }

        if (now < nextWhistleAttemptUtc)
        {
            return false;
        }

        foreach (var actionId in new[] { WhistleOneActionId, WhistleTwoActionId, WhistleThreeActionId })
        {
            var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, 0);
            if (actionStatus != 0)
            {
                continue;
            }

            StatusText = "自动召唤魔兽...";
            NextActionName = GetActionName(actionId);
            NextActionReason = "当前没有魔兽，按 1→2→3 选择首个可用兽笛";
            if (!actionManager->UseAction(ActionType.Action, actionId, 0))
            {
                nextWhistleAttemptUtc = now.AddMilliseconds(250);
                return false;
            }

            pendingWhistleActionId = actionId;
            pendingWhistleUntilUtc = now.AddSeconds(1);
            return true;
        }

        nextWhistleAttemptUtc = now.AddMilliseconds(500);
        return false;
    }

    private void ResetAutoWhistle()
    {
        pendingWhistleActionId = 0;
        pendingWhistleUntilUtc = DateTime.MinValue;
        nextWhistleAttemptUtc = DateTime.MinValue;
    }

    private unsafe bool TryRunWhistleRotation(ActionManager* actionManager, IBattleChara? target, DateTime now)
    {
        if (whistleRotationWaitingForCooldown)
        {
            var cooldownStatus = actionManager->GetActionStatus(ActionType.Action, WhistleOneActionId, 0);
            if (cooldownStatus != 0)
            {
                WhistleRotationStatus = $"已完成，等待兽笛1冷却（状态码 {cooldownStatus}）";
                NextActionName = GetActionName(WhistleOneActionId);
                NextActionReason = "兽笛1冷却完成后才能再次开启";
                if (configuration.WhistleRotationEnabled)
                {
                    configuration.WhistleRotationEnabled = false;
                    configuration.Save();
                }

                return true;
            }

            whistleRotationWaitingForCooldown = false;
            WhistleRotationStatus = "兽笛1已就绪，可开启连招";
        }

        if (whistleRotationStage < 0)
        {
            if (!configuration.WhistleRotationEnabled)
            {
                WhistleRotationStatus = "未开启";
                return false;
            }

            if (DalamudApi.Condition[ConditionFlag.InCombat])
            {
                WhistleRotationStatus = "等待脱离战斗后启动";
                NextActionName = GetActionName(WhistleOneActionId);
                NextActionReason = "兽笛1只能在未进入战斗时启动";
                return true;
            }

            var whistleStatus = actionManager->GetActionStatus(ActionType.Action, WhistleOneActionId, 0);
            if (whistleStatus != 0)
            {
                WhistleRotationStatus = $"等待兽笛1可用（状态码 {whistleStatus}）";
                NextActionName = GetActionName(WhistleOneActionId);
                NextActionReason = "连招启动需要兽笛1可用";
                return true;
            }

            whistleRotationStage = 0;
            WhistleRotationStatus = "连招进行中";
        }

        if (now < whistleRotationNextActionUtc)
        {
            return true;
        }

        var actionId = whistleRotationStage switch
        {
            0 => WhistleOneActionId,
            1 => BeastmasterReleaseBaseActionId,
            2 => FinalStrikeActionId,
            3 => WhistleTwoActionId,
            4 => BeastmasterReleaseBaseActionId,
            5 => FinalStrikeActionId,
            6 => WhistleThreeActionId,
            7 => BeastmasterReleaseBaseActionId,
            _ => 0u,
        };
        var requiresTarget = actionId is not (WhistleOneActionId or WhistleTwoActionId or WhistleThreeActionId);
        if (requiresTarget && target is null)
        {
            WhistleRotationStatus = "等待有效目标后继续";
            NextActionName = GetActionName(actionId);
            NextActionReason = "释放和最后一击需要当前目标";
            return true;
        }

        var targetId = actionId is WhistleOneActionId or WhistleTwoActionId or WhistleThreeActionId
            ? 0UL
            : target!.GameObjectId;
        var adjustedActionId = actionId == BeastmasterReleaseBaseActionId
            ? actionManager->GetAdjustedActionId(BeastmasterReleaseBaseActionId)
            : actionId;
        if (adjustedActionId == 0)
        {
            WhistleRotationStatus = "等待释放技能运行时 ID";
            NextActionName = GetActionName(BeastmasterReleaseBaseActionId);
            NextActionReason = "无法取得当前魔兽的释放技能 ID";
            return true;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, adjustedActionId, targetId);
        NextActionName = GetActionName(adjustedActionId);
        NextActionReason = actionStatus == 0 ? "兽笛循环连招" : $"技能暂不可用（状态码 {actionStatus}）";
        if (actionStatus != 0 || !actionManager->UseAction(ActionType.Action, adjustedActionId, targetId))
        {
            WhistleRotationStatus = $"连招等待：{GetActionName(adjustedActionId)}";
            whistleRotationNextActionUtc = now.AddMilliseconds(250);
            return true;
        }

        StatusText = "兽笛循环连招中...";
        if (actionId == BeastmasterReleaseBaseActionId)
        {
            BeastmasterFinalStrikeLock.RecordRelease(now);
        }
        else if (actionId == FinalStrikeActionId)
        {
            BeastmasterFinalStrikeLock.RecordFinalStrike(now);
        }
        whistleRotationNextActionUtc = now.AddMilliseconds(actionId == BeastmasterReleaseBaseActionId ? 700 : 350);
        if (whistleRotationStage == 7)
        {
            configuration.WhistleRotationEnabled = false;
            configuration.Save();
            whistleRotationStage = -1;
            whistleRotationWaitingForCooldown = true;
            WhistleRotationStatus = "连招完成，等待兽笛1冷却";
            return true;
        }

        whistleRotationStage++;
        return true;
    }

    private void ResetWhistleRotation(string reason)
    {
        whistleRotationStage = -1;
        whistleRotationWaitingForCooldown = false;
        whistleRotationNextActionUtc = DateTime.MinValue;
        WhistleRotationStatus = reason;
    }

    private void ResetCooperationState(string reason = "状态重置")
    {
        if (pendingCooperationActionId != 0)
        {
            RecordBattleLog(
                $"协作二段：清除待续；原因={reason}；二段={GetActionName(pendingCooperationActionId)}（{pendingCooperationActionId}），"
                + $"Buff={pendingCooperationStatusId}，剩余窗口={Math.Max(0d, (pendingCooperationUntilUtc - DateTime.UtcNow).TotalSeconds):0.###}s");
        }
        pendingCooperationActionId = 0;
        pendingCooperationStatusId = 0;
        pendingCooperationUntilUtc = DateTime.MinValue;
    }


    private static bool HasSelfStatus(uint statusId)
    {
        var player = DalamudApi.ObjectTable.LocalPlayer;
        return player != null && player.StatusList.Any(status => status.StatusId == statusId);
    }

    private static string GetAttributeStatusName(uint statusId)
        => statusId switch
        {
            4595 => "兽心一式·翔",
            4596 => "兽心一式·猛",
            4597 => "兽心一式·坚",
            4598 => "兽心一式·魔",
            _ => "对应兽心一式状态",
        };

    private static bool TryGetCooperationAction(
        BeastmasterGaugeSnapshot gauge,
        bool ultimateFirst,
        out uint firstActionId,
        out uint secondActionId,
        out uint requiredStatusId)
    {
        firstActionId = 0;
        secondActionId = 0;
        requiredStatusId = 0;
        var entry = gauge.SummonEntry;
        if (entry == null
            || gauge.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement
            || gauge.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            return false;
        }

        var nextAttribute = entry.Attribute switch
        {
            BeastmasterAttribute.魔 => BeastmasterAttribute.翔,
            BeastmasterAttribute.翔 => BeastmasterAttribute.猛,
            BeastmasterAttribute.猛 => BeastmasterAttribute.坚,
            BeastmasterAttribute.坚 => BeastmasterAttribute.魔,
            _ => BeastmasterAttribute.Unknown,
        };
        var previousAttribute = entry.Attribute switch
        {
            BeastmasterAttribute.魔 => BeastmasterAttribute.坚,
            BeastmasterAttribute.翔 => BeastmasterAttribute.魔,
            BeastmasterAttribute.猛 => BeastmasterAttribute.翔,
            BeastmasterAttribute.坚 => BeastmasterAttribute.猛,
            _ => BeastmasterAttribute.Unknown,
        };
        var nextAxeActionId = nextAttribute switch
        {
            BeastmasterAttribute.猛 => 44884u,
            BeastmasterAttribute.坚 => 44887u,
            BeastmasterAttribute.魔 => 44888u,
            BeastmasterAttribute.翔 => 44889u,
            _ => 0u,
        };
        var attributeStatusId = entry.Attribute switch
        {
            BeastmasterAttribute.猛 => 4596u,
            BeastmasterAttribute.坚 => 4597u,
            BeastmasterAttribute.魔 => 4598u,
            BeastmasterAttribute.翔 => 4595u,
            _ => 0u,
        };
        var nextAttributeStatusId = nextAttribute switch
        {
            BeastmasterAttribute.猛 => 4596u,
            BeastmasterAttribute.坚 => 4597u,
            BeastmasterAttribute.魔 => 4598u,
            BeastmasterAttribute.翔 => 4595u,
            _ => 0u,
        };
        var previousAxeActionId = previousAttribute switch
        {
            BeastmasterAttribute.猛 => 44884u,
            BeastmasterAttribute.坚 => 44887u,
            BeastmasterAttribute.魔 => 44888u,
            BeastmasterAttribute.翔 => 44889u,
            _ => 0u,
        };
        var previousAttributeStatusId = previousAttribute switch
        {
            BeastmasterAttribute.猛 => 4596u,
            BeastmasterAttribute.坚 => 4597u,
            BeastmasterAttribute.魔 => 4598u,
            BeastmasterAttribute.翔 => 4595u,
            _ => 0u,
        };

        if (ultimateFirst)
        {
            firstActionId = BeastmasterUltimateActionId;
            secondActionId = nextAxeActionId;
            requiredStatusId = attributeStatusId;
            return firstActionId != 0 && secondActionId != 0;
        }

        firstActionId = previousAxeActionId;
        secondActionId = BeastmasterUltimateActionId;
        requiredStatusId = previousAttributeStatusId;
        return firstActionId != 0 && secondActionId != 0;
    }

    private string GetAdvancedActionStatus(BeastmasterGaugeSnapshot gauge)
    {
        var entry = gauge.SummonEntry;
        if (entry == null)
        {
            return "等待召唤兽，暂不评估高级技能";
        }

        if (configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
        {
            return $"{(configuration.BeastHeartCooperationEnabled ? "御兽协作（黄豆）" : "兽灵协作（蓝豆）")}已开启";
        }

        if (configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled)
        {
            return $"{(configuration.PhysicalThirdFormEnabled ? "万象流转（物理）" : "万象流转（魔法）")}已开启";
        }

        var anyFinalStrikeEnabled = configuration.AutoFinalStrikeWhistleOneEnabled
            || configuration.AutoFinalStrikeWhistleTwoEnabled
            || configuration.AutoFinalStrikeWhistleThreeEnabled;
        if (configuration.AutoWhistleEnabled || anyFinalStrikeEnabled || configuration.AutoReleaseEnabled)
        {
            return $"自动兽笛{(configuration.AutoWhistleEnabled ? "开启" : "关闭")}，最后一击{(anyFinalStrikeEnabled ? "开启" : "关闭")}，释放{(configuration.AutoReleaseEnabled ? "开启" : "关闭")}";
        }

        return "高级技能均已关闭";
    }

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .TryGetRow(actionId, out var action)
                ? action.Name.ExtractText()
                : $"技能 {actionId}";
}
