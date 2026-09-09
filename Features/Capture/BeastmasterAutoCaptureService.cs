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
    private const uint WhistleOneActionId = 44881;
    private const uint WhistleTwoActionId = 44892;
    private const uint WhistleThreeActionId = 44894;
    private const uint FinalStrikeActionId = 44891;
    private readonly BeastmasterConfiguration configuration;
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
    private bool reportedMissingData;
    private int whistleRotationStage = -1;
    private bool whistleRotationWaitingForCooldown;
    private DateTime whistleRotationNextActionUtc = DateTime.MinValue;

    public string StatusText { get; private set; } = "等待当前目标";

    public string NextActionName { get; private set; } = "-";

    public string NextActionReason { get; private set; } = "";

    public string ResourceStatus { get; private set; } = "量谱未读取";

    public string AdvancedActionStatus { get; private set; } = "未评估";

    public string TargetStatus { get; private set; } = "无有效目标";

    public float TargetHpPercent { get; private set; }

    public string CaptureState { get; private set; } = "未开始";

    public string ManualActionStatus { get; private set; } = "未执行";

    public string WhistleRotationStatus { get; private set; } = "未开启";

    public BeastmasterAutoCaptureService(BeastmasterConfiguration configuration)
    {
        this.configuration = configuration;
        smashActionId = ResolveAction("碎击斩");
        biteActionId = ResolveAction("碎咬斧");
        shieldActionId = ResolveAction("裂盾劈");
        captureActionId = ResolveAction("捕获");
        captureStatusId = ResolveStatus("待捕获") is var pendingCaptureStatusId
            && pendingCaptureStatusId != 0
                ? pendingCaptureStatusId
                : ResolveStatus("捕获");
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public bool IsEnabled => configuration.AutoCaptureEnabled;

    public bool TryCapture => configuration.AutoCaptureTryCapture;

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
            DalamudApi.ChatGui.Print("[驯兽师助手] 自动捕获已关闭。");
        }
    }

    public void SetTryCapture(bool enabled)
    {
        configuration.AutoCaptureTryCapture = enabled;
        configuration.Save();
    }

    public unsafe bool TryUseUltimate()
    {
        if (!configuration.AdvancedActionsEnabled)
        {
            ManualActionStatus = "高级技能总开关已关闭";
            return false;
        }

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
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
        if (actionStatus != 0 && !TryUseActionWithStatusFallback(actionManager, actionId, target.GameObjectId, actionStatus))
        {
            ManualActionStatus = $"大招当前不可用（状态码 {actionStatus}）";
            return false;
        }

        if (actionStatus == 0 && !actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId))
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
            pendingCooperationUntilUtc = DateTime.UtcNow.AddSeconds(4);
        }

        ManualActionStatus = $"已请求释放：{GetActionName(actionId)}";
        return true;
    }

    private static unsafe bool TryUseActionWithStatusFallback(
        ActionManager* actionManager,
        uint actionId,
        ulong targetId,
        uint actionStatus)
    {
        _ = actionStatus;
        return actionManager->UseAction(ActionType.Action, actionId, targetId);
    }

    private static unsafe bool TryUseAdvancedAction(
        ActionManager* actionManager,
        uint actionId,
        ulong targetId,
        uint actionStatus)
    {
        _ = actionStatus;
        return actionManager->UseAction(ActionType.Action, actionId, targetId);
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
        if (now < nextActionUtc
            || DalamudApi.Condition[ConditionFlag.BetweenAreas]
            || DalamudApi.Condition[ConditionFlag.Mounted]
            || DalamudApi.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            StatusText = "等待可执行状态";
            NextActionReason = "角色当前不可执行动作";
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
            StatusText = "请切换为驯兽师";
            NextActionName = "-";
            NextActionReason = "当前职业不是驯兽师";
            ResourceStatus = "仅驯兽师可用";
            return;
        }

        if (player.CurrentHp == 0 || player.IsCasting)
        {
            StatusText = "等待可执行状态";
            NextActionReason = "角色死亡或正在读条";
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
            && (target.ObjectKind != ObjectKind.BattleNpc
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

        var canCapture = targetHpPercent < configuration.CaptureHpThreshold;
        var capturePending = capturePendingUntilUtc > now;
        if (capturePending)
        {
            StatusText = "等待捕获结果";
            NextActionName = "-";
            NextActionReason = "等待自身捕获状态刷新";
            return;
        }

        uint actionId;

        if (configuration.AdvancedActionsEnabled
            && (configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
            && pendingCooperationActionId != 0)
        {
            actionId = pendingCooperationActionId;
            StatusText = "自动协作技中...";
            NextActionName = GetActionName(actionId);
            NextActionReason = "协作技第二段，已按属性选择技能";

            var cooperationStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
            var cooperationUsed = TryUseAdvancedAction(actionManager, actionId, target.GameObjectId, cooperationStatus);
            if (cooperationUsed)
            {
                nextActionUtc = now.AddMilliseconds(700);
                ResetCooperationState();
            }

            return;
        }

        if (configuration.AdvancedActionsEnabled
            && configuration.AutoReleaseEnabled
            && gauge.SummonEntry != null
            && TryUseReleaseAction(actionManager, target.GameObjectId, now))
        {
            return;
        }

        if (configuration.AdvancedActionsEnabled
            && (configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
            && TryGetCooperationAction(gauge, configuration.BeastHeartCooperationEnabled, out var cooperationActionId, out var cooperationFollowUpId, out var cooperationStatusId))
        {
            actionId = cooperationActionId;
            StatusText = "自动协作技中...";
            NextActionName = GetActionName(actionId);
            NextActionReason = $"协作技第一段，下一段：{GetActionName(cooperationFollowUpId)}";

            var cooperationStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
            var cooperationUsed = TryUseAdvancedAction(actionManager, actionId, target.GameObjectId, cooperationStatus);
            if (!cooperationUsed)
            {
                NextActionReason = $"协作技请求失败（状态码 {cooperationStatus}）";
            }
            if (cooperationUsed)
            {
                nextActionUtc = now.AddMilliseconds(700);
                pendingCooperationActionId = cooperationFollowUpId;
                pendingCooperationStatusId = cooperationStatusId;
                pendingCooperationUntilUtc = now.AddSeconds(4);
            }

            return;
        }

        actionId = configuration.AutoCaptureTryCapture && !hasOwnCapture && canCapture
            ? captureActionId
            : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;

        StatusText = configuration.AutoCaptureTryCapture ? "自动捕获中..." : "自动攻击中...";
        NextActionName = GetActionName(actionId);
        NextActionReason = configuration.AutoCaptureTryCapture && !hasOwnCapture && !canCapture
            ? $"目标血量 {targetHpPercent:0.#}% 高于捕获阈值 {configuration.CaptureHpThreshold:0.#}%"
            : actionId == BeastmasterUltimateActionId
                ? "高级技能已开启，技力和兽力满足大招门槛"
            : hasOwnCapture
                ? "目标已有自身施加的捕获状态"
                : hasOtherCapture
                    ? "目标有他人施加的捕获状态，不影响自身捕获判断"
                    : "技能系统允许使用";

        var availability = CheckActionAvailability(actionManager, actionId, target.GameObjectId);
        NextActionReason = availability.Reason;
        if (!availability.CanUse && actionId != BeastmasterUltimateActionId)
        {
            return;
        }

        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId);
        var used = actionStatus == 0
            ? actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId)
            : actionId == BeastmasterUltimateActionId
                && actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId);
        if (used)
        {
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
        }
    }

    private void ResetCaptureState()
    {
        captureTargetId = 0;
        capturePendingUntilUtc = DateTime.MinValue;
        CaptureState = "未开始";
    }

    private unsafe bool TryUseReleaseAction(ActionManager* actionManager, ulong targetId, DateTime now)
    {
        if (now < nextReleaseAttemptUtc)
        {
            return false;
        }

        var releaseActionId = actionManager->GetAdjustedActionId(BeastmasterReleaseBaseActionId);
        if (releaseActionId == 0)
        {
            return false;
        }

        var releaseName = GetActionName(releaseActionId);
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, releaseActionId, targetId);
        if (actionStatus != 0)
        {
            nextReleaseAttemptUtc = now.AddMilliseconds(500);
            return false;
        }

        StatusText = "自动释放魔兽技能...";
        NextActionName = releaseName;
        NextActionReason = "释放技能冷却完成";
        if (!actionManager->UseAction(ActionType.Action, releaseActionId, targetId))
        {
            NextActionReason = "释放技能请求失败";
            nextReleaseAttemptUtc = now.AddSeconds(1);
            return false;
        }

        nextReleaseAttemptUtc = now.AddMilliseconds(500);
        nextActionUtc = now.AddMilliseconds(700);
        return true;
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

    private bool HasOwnStatus(uint statusId)
    {
        var player = DalamudApi.ObjectTable.LocalPlayer;
        return player != null && player.StatusList.Any(status => status.StatusId == statusId && status.SourceId == player.EntityId);
    }

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

    private static unsafe BeastmasterActionAvailability CheckActionAvailability(
        ActionManager* actionManager,
        uint actionId,
        ulong targetId)
    {
        if (actionId == 0)
        {
            return new(0, "-", false, "ActionId 无效");
        }

        var actionName = GetActionName(actionId);
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, actionId, targetId);
        return actionStatus == 0
            ? new(actionId, actionName, true, "技能系统允许使用")
            : new(actionId, actionName, false, $"技能系统暂不可用（状态码 {actionStatus}）");
    }

    private string GetAdvancedActionStatus(BeastmasterGaugeSnapshot gauge)
    {
        if (!configuration.AdvancedActionsEnabled)
        {
            return "高级技能已关闭，保留资源";
        }

        var entry = gauge.SummonEntry;
        if (entry == null)
        {
            return "等待召唤兽，暂不评估高级技能";
        }

        if (configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled)
        {
            return $"{(configuration.BeastHeartCooperationEnabled ? "御兽协作（黄豆）" : "兽灵协作（蓝豆）")}已开启";
        }

        return "高级技能已开启，但大招和协作技均关闭";
    }

    private static uint ResolveAction(string name)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .Where(action => action.RowId != 0
                && action.Name.ExtractText().Equals(name, StringComparison.Ordinal))
            .OrderByDescending(action => action.ClassJob.RowId == BeastmasterClassJobId)
            .ThenBy(action => action.RowId)
            .Select(action => action.RowId)
            .FirstOrDefault();

    private static uint ResolveStatus(string name)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>()
            .Where(status => status.RowId != 0
                && status.Name.ExtractText().Equals(name, StringComparison.Ordinal))
            .OrderBy(status => status.RowId)
            .Select(status => status.RowId)
            .FirstOrDefault();

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .TryGetRow(actionId, out var action)
                ? action.Name.ExtractText()
                : $"技能 {actionId}";
}
