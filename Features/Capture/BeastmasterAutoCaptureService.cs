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
    private const uint WhitePhysicalThirdFormActionId = 44931;
    private const uint PurplePhysicalThirdFormActionId = 44930;
    private const uint WhiteMagicalThirdFormActionId = 44933;
    private const uint PurpleMagicalThirdFormActionId = 44932;
    private const uint WhistleOneActionId = 44881;
    private const uint WhistleTwoActionId = 44892;
    private const uint WhistleThreeActionId = 44894;
    private const uint FinalStrikeActionId = 44891;
    private const uint SmashActionId = 44879;
    private const uint BiteActionId = 44883;
    private const uint ShieldActionId = 44885;
    private const uint CaptureActionId = 44880;
    private const uint CaptureStatusId = 4626;
    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterSequenceService sequenceService;
    private readonly BeastmasterRuleService ruleService;
    private readonly uint smashActionId;
    private readonly uint biteActionId;
    private readonly uint shieldActionId;
    private readonly uint captureActionId;
    private readonly uint captureStatusId;
    private DateTime nextCheckUtc = DateTime.MinValue;
    private DateTime nextActionUtc = DateTime.MinValue;
    private DateTime capturePendingUntilUtc = DateTime.MinValue;
    private ulong captureTargetId;
    private uint pendingCooperationActionId;
    private uint pendingCooperationStatusId;
    private DateTime pendingCooperationUntilUtc = DateTime.MinValue;
    private DateTime nextReleaseAttemptUtc = DateTime.MinValue;
    private uint pendingWhistleActionId;
    private DateTime pendingWhistleUntilUtc = DateTime.MinValue;
    private DateTime nextWhistleAttemptUtc = DateTime.MinValue;
    private DateTime nextFinalStrikeAttemptUtc = DateTime.MinValue;
    private bool reportedMissingData;
    private int whistleRotationStage = -1;
    private bool whistleRotationWaitingForCooldown;
    private DateTime whistleRotationNextActionUtc = DateTime.MinValue;
    private DateTime lastAutoOutputDiagnosticUtc = DateTime.MinValue;
    private string lastAutoOutputDiagnosticKey = string.Empty;

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
        if (!configuration.AutoOutputDiagnosticsEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = $"{actionName}|{reasonKey}";
        if (key == lastAutoOutputDiagnosticKey
            && now - lastAutoOutputDiagnosticUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        lastAutoOutputDiagnosticKey = key;
        lastAutoOutputDiagnosticUtc = now;
        DalamudApi.ChatGui.Print($"[驯兽师助手 {DateTime.Now:HH:mm:ss}] {actionName}无法释放：{reason}");
    }

    private void ReportAutoOutputSuccess(string actionName, uint actionId)
    {
        if (!configuration.AutoOutputDiagnosticsEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = $"{actionName}|used";
        if (key == lastAutoOutputDiagnosticKey
            && now - lastAutoOutputDiagnosticUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        lastAutoOutputDiagnosticKey = key;
        lastAutoOutputDiagnosticUtc = now;
        DalamudApi.ChatGui.Print($"[驯兽师助手 {DateTime.Now:HH:mm:ss}] 已请求{actionName}（ActionId {actionId}）");
    }

    public string WhistleRotationStatus { get; private set; } = "未开启";

    public BeastmasterAutoCaptureService(
        BeastmasterConfiguration configuration,
        BeastmasterSequenceService sequenceService,
        BeastmasterRuleService ruleService)
    {
        this.configuration = configuration;
        this.sequenceService = sequenceService;
        this.ruleService = ruleService;
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
            ResetCaptureState();
            ResetCooperationState();
            ResetAutoWhistle();
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
            ResetCaptureState();
            ResetCooperationState();
            ResetAutoWhistle();
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
        return actionStatus == 0
            && actionManager->UseAction(ActionType.Action, actionId, targetId);
    }

    public void Dispose()
        => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
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
            return;
        }

        if (configuration.AutoOutputPaused)
        {
            StatusText = "自动输出已暂停";
            NextActionName = "-";
            NextActionReason = "暂停开关已开启";
            AdvancedActionStatus = "暂停中";
            ResetCaptureState();
            ResetCooperationState();
            ResetAutoWhistle();
            return;
        }

        if (!configuration.WhistleRotationEnabled
            && (whistleRotationStage >= 0 || whistleRotationWaitingForCooldown))
        {
            ResetWhistleRotation("未开启");
        }

        var now = DateTime.UtcNow;
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

        if (player.CurrentHp == 0 || player.IsCasting)
        {
            if (player.CurrentHp == 0)
            {
                sequenceService.Abort("序列中止：角色已死亡");
            }
            StatusText = "等待可执行状态";
            NextActionReason = player.CurrentHp == 0 ? "角色已死亡" : "角色正在读条";
            return;
        }

        var gauge = BeastmasterGaugeSnapshot.Read();
        ResourceStatus = gauge.Available
            ? $"技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250"
            : gauge.Status;
        AdvancedActionStatus = GetAdvancedActionStatus(gauge);

        if (pendingCooperationActionId != 0 && pendingCooperationUntilUtc <= now)
        {
            pendingCooperationActionId = 0;
            pendingCooperationStatusId = 0;
            pendingCooperationUntilUtc = DateTime.MinValue;
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
            ResetCooperationState();
            return;
        }

        if (captureTargetId != target.EntityId)
        {
            captureTargetId = target.EntityId;
            capturePendingUntilUtc = DateTime.MinValue;
            CaptureState = "未开始";
            ResetCooperationState();
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
            ResetCooperationState();
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
        var capturePending = capturePendingUntilUtc > now;
        if (capturePending)
        {
            StatusText = "等待捕获结果";
            NextActionName = "-";
            NextActionReason = "等待自身捕获状态刷新";
            return;
        }

        uint actionId;

        if ((configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
            && pendingCooperationActionId != 0)
        {
            actionId = pendingCooperationActionId;
            StatusText = "自动协作技中...";
            NextActionName = GetActionName(actionId);
            if (pendingCooperationStatusId != 0 && !HasSelfStatus(pendingCooperationStatusId))
            {
                NextActionReason = $"等待自身获得{GetAttributeStatusName(pendingCooperationStatusId)}（{pendingCooperationStatusId}）";
                ReportAutoOutputDiagnostic(NextActionName, NextActionReason, $"status-{pendingCooperationStatusId}");
                return;
            }

            NextActionReason = $"自身已有{GetAttributeStatusName(pendingCooperationStatusId)}，释放协作技第二段";

            if (!BeastmasterActionHelper.IsPlayerInActionRange(
                    player,
                    target,
                    actionId,
                    out var followUpDistance,
                    out var followUpRange))
            {
                NextActionReason = $"等待进入协作技射程（当前 {followUpDistance:0.##}/{followUpRange:0.##} yalms）";
                ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {followUpDistance:0.##}/{followUpRange:0.##} yalms）", "range");
                return;
            }

            var cooperationStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
            var cooperationUsed = TryUseAdvancedAction(actionManager, actionId, target.GameObjectId, cooperationStatus);
            if (!cooperationUsed)
            {
                ReportAutoOutputDiagnostic(NextActionName,
                    $"技能系统状态码 {cooperationStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                    $"status-{cooperationStatus}");
            }
            if (cooperationUsed)
            {
                nextActionUtc = now.AddMilliseconds(700);
                ResetCooperationState();
            }

            return;
        }

        if (TryUseFinalStrike(actionManager, gauge, target.GameObjectId, now))
        {
            return;
        }

        if ((configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled)
            && TryUseThirdFormAction(actionManager, gauge, target.GameObjectId, now))
        {
            return;
        }

        if (configuration.AutoReleaseEnabled
            && gauge.SummonEntry != null
            && TryUseReleaseAction(actionManager, gauge, target, now))
        {
            return;
        }

        if ((configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
            && TryGetCooperationAction(gauge, configuration.BeastHeartCooperationEnabled, out var cooperationActionId, out var cooperationFollowUpId, out var cooperationStatusId))
        {
            actionId = cooperationActionId;
            StatusText = "自动协作技中...";
            NextActionName = GetActionName(actionId);
            NextActionReason = $"协作技第一段，下一段：{GetActionName(cooperationFollowUpId)}";

            if (!BeastmasterActionHelper.IsPlayerInActionRange(
                    player,
                    target,
                    actionId,
                    out var cooperationDistance,
                    out var cooperationRange))
            {
                NextActionReason = $"等待进入协作技射程（当前 {cooperationDistance:0.##}/{cooperationRange:0.##} yalms）";
                ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {cooperationDistance:0.##}/{cooperationRange:0.##} yalms）", "range");
                return;
            }

            var cooperationStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
            var cooperationUsed = TryUseAdvancedAction(actionManager, actionId, target.GameObjectId, cooperationStatus);
            if (!cooperationUsed)
            {
                NextActionReason = $"协作技请求失败（状态码 {cooperationStatus}）";
                ReportAutoOutputDiagnostic(NextActionName,
                    $"技能系统状态码 {cooperationStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                    $"status-{cooperationStatus}");
            }
            if (cooperationUsed)
            {
                nextActionUtc = now.AddMilliseconds(700);
                pendingCooperationActionId = cooperationFollowUpId;
                pendingCooperationStatusId = cooperationStatusId;
                pendingCooperationUntilUtc = now.AddSeconds(7);
            }

            return;
        }

        if (configuration.AutoCaptureTryCapture
            && canCapture
            && (configuration.ForceCaptureEnabled || !hasOwnCapture))
        {
            actionId = captureActionId;
        }
        else if (!configuration.BasicComboEnabled)
        {
            StatusText = "等待可用技能";
            NextActionName = "-";
            NextActionReason = "基础技能（1→2→3）已关闭";
            return;
        }
        else
        {
            actionId = actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;
        }

        StatusText = configuration.AutoCaptureTryCapture
            ? configuration.ForceCaptureEnabled ? "强制捕获中..." : "自动捕获中..."
            : "自动攻击中...";
        NextActionName = GetActionName(actionId);
        NextActionReason = configuration.AutoCaptureTryCapture && !configuration.ForceCaptureEnabled && !hasOwnCapture && !canCapture
            ? $"目标血量 {targetHpPercent:0.#}% 高于捕获阈值 {configuration.CaptureHpThreshold:0.#}%"
            : configuration.AutoCaptureTryCapture && configuration.ForceCaptureEnabled && !canCapture
                ? $"强制捕获仍受血量阈值限制：目标血量 {targetHpPercent:0.#}% 高于阈值 {configuration.CaptureHpThreshold:0.#}%"
                : configuration.AutoCaptureTryCapture && configuration.ForceCaptureEnabled
                    ? "强制捕获模式，无视捕获状态但仍受血量阈值限制"
                : actionId == BeastmasterUltimateActionId
                ? "高级技能已开启，技力和兽力满足大招门槛"
            : hasOwnCapture
                ? "目标已有自身施加的捕获状态"
                : hasOtherCapture
                    ? "目标有他人施加的捕获状态，不影响自身捕获判断"
                    : "技能系统允许使用";

        if (!BeastmasterActionHelper.IsPlayerInActionRange(
                player,
                target,
                actionId,
                out var playerDistance,
                out var actionRange))
        {
            if (actionId != captureActionId)
            {
                NextActionReason = $"等待进入技能射程（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
                ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）", "range");
                return;
            }

            actionId = actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;
            NextActionName = GetActionName(actionId);
            if (!BeastmasterActionHelper.IsPlayerInActionRange(
                    player,
                    target,
                    actionId,
                    out playerDistance,
                    out actionRange))
            {
                NextActionReason = $"捕获超出射程，基础技能也在射程外（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
                ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）", "range");
                return;
            }
        }

        var availability = BeastmasterActionHelper.GetAvailability(actionId, target.GameObjectId);
        NextActionReason = availability.Reason;
        if (!availability.CanUse && actionId == captureActionId)
        {
            actionId = actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;
            NextActionName = GetActionName(actionId);
            if (!BeastmasterActionHelper.IsPlayerInActionRange(
                    player,
                    target,
                    actionId,
                    out playerDistance,
                    out actionRange))
            {
                NextActionReason = $"捕获不可用，基础技能在射程外（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）";
                ReportAutoOutputDiagnostic(NextActionName, $"距离不足（当前 {playerDistance:0.##}/{actionRange:0.##} yalms）", "range");
                return;
            }
            availability = BeastmasterActionHelper.GetAvailability(actionId, target.GameObjectId);
            NextActionReason = "捕获未进入技能范围，回退基础技能；" + availability.Reason;
        }
        if (!availability.CanUse)
        {
            ReportAutoOutputDiagnostic(NextActionName,
                $"{availability.Reason}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                availability.Reason);
            return;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
        var used = actionStatus == 0
            && actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId);
        if (used)
        {
            ReportAutoOutputSuccess(NextActionName, actionId);
            nextActionUtc = now.AddMilliseconds(actionId == captureActionId ? 700 : actionId == BeastmasterUltimateActionId ? 700 : 250);
            if (actionId == captureActionId)
            {
                capturePendingUntilUtc = now.AddMilliseconds(1200);
                CaptureState = "已发送请求，等待结果";
                StatusText = "等待捕获结果";
                NextActionReason = "等待自身捕获状态刷新";
            }
        }
        else if (actionId == captureActionId)
        {
            CaptureState = "捕获请求失败";
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
        }
        else if (!used)
        {
            ReportAutoOutputDiagnostic(NextActionName,
                $"技能系统状态码 {actionStatus}；技力 {gauge.Tp}/250，兽力 {gauge.BeastPower}/250",
                $"status-{actionStatus}");
        }
    }

    private void ResetCaptureState()
    {
        captureTargetId = 0;
        capturePendingUntilUtc = DateTime.MinValue;
        CaptureState = "未开始";
    }

    private unsafe bool TryUseReleaseAction(
        ActionManager* actionManager,
        BeastmasterGaugeSnapshot gauge,
        IBattleChara target,
        DateTime now)
    {
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
        NextActionReason = "释放技能冷却完成";
        if (!actionManager->UseAction(ActionType.Action, availability.ActionId, target.GameObjectId))
        {
            NextActionReason = "释放技能请求失败";
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            nextReleaseAttemptUtc = now.AddSeconds(1);
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, availability.ActionId);
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
        if (!configuration.AutoFinalStrikeEnabled
            || !TryGetFinalStrikeSettings(gauge.WhistleIndex, out var enabled, out var hpThreshold)
            || !enabled
            || gauge.SummonDataId == 0
            || gauge.SummonMaxHp == 0
            || gauge.SummonHpPercent >= hpThreshold
            || now < nextFinalStrikeAttemptUtc)
        {
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
            var releaseActionId = actionManager->GetAdjustedActionId(BeastmasterReleaseBaseActionId);
            var releaseUnavailable = releaseActionId != 0
                && actionManager->GetActionStatus(ActionType.Action, releaseActionId, targetId) != 0;
            if (!releaseUnavailable)
            {
                NextActionReason = "等待当前魔兽先使用释放";
                ReportAutoOutputDiagnostic(NextActionName, NextActionReason, "wait-release");
                return false;
            }
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
        NextActionReason = $"{gauge.WhistleIndex} 笛宝宝血量 {gauge.SummonHpPercent:0.#}% 低于阈值 {hpThreshold:0.#}%";
        if (!actionManager->UseAction(ActionType.Action, FinalStrikeActionId, targetId))
        {
            ReportAutoOutputDiagnostic(NextActionName, "UseAction 返回 false", "use-action-false");
            nextFinalStrikeAttemptUtc = now.AddMilliseconds(500);
            return false;
        }

        ReportAutoOutputSuccess(NextActionName, FinalStrikeActionId);
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

    private void ResetCooperationState()
    {
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

        if (ultimateFirst)
        {
            firstActionId = BeastmasterUltimateActionId;
            secondActionId = nextAxeActionId;
            requiredStatusId = attributeStatusId;
            return firstActionId != 0 && secondActionId != 0;
        }

        firstActionId = nextAxeActionId;
        secondActionId = BeastmasterUltimateActionId;
        requiredStatusId = nextAttributeStatusId;
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
