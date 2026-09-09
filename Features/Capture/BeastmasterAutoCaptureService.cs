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
    private bool reportedMissingData;

    public string StatusText { get; private set; } = "等待当前目标";

    public string NextActionName { get; private set; } = "-";

    public string NextActionReason { get; private set; } = "";

    public string ResourceStatus { get; private set; } = "量谱未读取";

    public string AdvancedActionStatus { get; private set; } = "未评估";

    public string TargetStatus { get; private set; } = "无有效目标";

    public float TargetHpPercent { get; private set; }

    public string CaptureState { get; private set; } = "未开始";

    public string ManualActionStatus { get; private set; } = "未执行";

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
            return;
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

        if (DalamudApi.TargetManager.Target is not IBattleChara target
            || target.ObjectKind != ObjectKind.BattleNpc
            || !target.IsTargetable
            || target.IsDead
            || target.CurrentHp == 0)
        {
            StatusText = "等待当前敌对目标";
            NextActionName = "-";
            NextActionReason = "没有有效的 BattleNpc 目标";
            TargetStatus = "无有效目标";
            TargetHpPercent = 0f;
            ResetCaptureState();
            return;
        }

        if (captureTargetId != target.EntityId)
        {
            captureTargetId = target.EntityId;
            capturePendingUntilUtc = DateTime.MinValue;
            CaptureState = "未开始";
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            StatusText = "等待动作系统";
            NextActionReason = "ActionManager 不可用";
            return;
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

        var advancedUltimateReady = configuration.AdvancedActionsEnabled
            && configuration.AutoUltimateEnabled
            && gauge.Available
            && gauge.SummonEntry != null
            && gauge.Tp >= BeastmasterGaugeSnapshot.ComboGaugeRequirement
            && gauge.BeastPower >= BeastmasterGaugeSnapshot.ComboGaugeRequirement;
        var actionId = configuration.AutoCaptureTryCapture && !hasOwnCapture && canCapture
            ? captureActionId
            : advancedUltimateReady
                ? BeastmasterUltimateActionId
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

        if (configuration.AutoUltimateEnabled)
        {
            if (gauge.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement
                || gauge.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
            {
                return $"大招 {entry.Name}：资源不足（技力 {gauge.Tp}/{BeastmasterGaugeSnapshot.MaximumGauge}，兽力 {gauge.BeastPower}/{BeastmasterGaugeSnapshot.MaximumGauge}）";
            }

            return $"大招 {entry.Name}：资源满足，等待动作系统确认";
        }

        if (configuration.AutoCooperationEnabled)
        {
            return "协作技已开启，等待运行时协作窗口数据";
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
