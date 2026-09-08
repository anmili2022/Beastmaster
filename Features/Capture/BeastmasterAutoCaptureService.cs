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
    private readonly BeastmasterConfiguration configuration;
    private readonly uint smashActionId;
    private readonly uint biteActionId;
    private readonly uint shieldActionId;
    private readonly uint captureActionId;
    private readonly uint captureStatusId;
    private DateTime nextCheckUtc = DateTime.MinValue;
    private DateTime nextActionUtc = DateTime.MinValue;
    private bool reportedMissingData;

    public string StatusText { get; private set; } = "等待当前目标";

    public string NextActionName { get; private set; } = "-";

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

    public void Dispose()
        => DalamudApi.Framework.Update -= OnFrameworkUpdate;

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        _ = framework;
        if (!configuration.AutoCaptureEnabled)
        {
            StatusText = "自动捕获已关闭";
            NextActionName = "-";
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
            return;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            StatusText = "等待角色加载";
            NextActionName = "-";
            return;
        }

        if (player.ClassJob.RowId != BeastmasterClassJobId)
        {
            StatusText = "请切换为驯兽师";
            NextActionName = "-";
            return;
        }

        if (player.CurrentHp == 0 || player.IsCasting)
        {
            StatusText = "等待可执行状态";
            return;
        }

        if (smashActionId == 0 || biteActionId == 0 || shieldActionId == 0 || captureActionId == 0 || captureStatusId == 0)
        {
            if (!reportedMissingData)
            {
                reportedMissingData = true;
                StatusText = "技能或状态数据解析失败";
                NextActionName = "-";
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
            return;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            StatusText = "等待动作系统";
            return;
        }

        var hasCapture = target.StatusList.Any(status => status.StatusId == captureStatusId);
        var actionId = configuration.AutoCaptureTryCapture && !hasCapture
            ? captureActionId
            : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == biteActionId && player.Level >= 12
                ? shieldActionId
                : actionManager->Combo.Timer > 0f && actionManager->Combo.Action == smashActionId && player.Level >= 2
                    ? biteActionId
                    : smashActionId;

        StatusText = configuration.AutoCaptureTryCapture ? "自动捕获中..." : "自动攻击中...";
        NextActionName = GetActionName(actionId);

        if (actionManager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId) != 0)
        {
            return;
        }

        if (actionManager->UseAction(ActionType.Action, actionId, target.GameObjectId))
        {
            nextActionUtc = now.AddMilliseconds(actionId == captureActionId ? 700 : 250);
        }
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
