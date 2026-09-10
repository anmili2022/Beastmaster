using FFXIVClientStructs.FFXIV.Client.Game;

namespace Beastmaster;

public static class BeastmasterActionHelper
{
    public static unsafe BeastmasterActionAvailability GetAvailability(
        uint actionId,
        ulong targetId,
        bool useAdjustedActionId = false)
    {
        if (actionId == 0)
        {
            return new(0, "-", false, "ActionId 无效");
        }

        var actionSheet = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (!actionSheet.TryGetRow(actionId, out var action))
        {
            return new(0, $"技能 {actionId}", false, $"ActionId 不存在（{actionId}）");
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return new(actionId, action.Name.ExtractText(), false, "ActionManager 不可用");
        }

        var resolvedActionId = useAdjustedActionId
            ? actionManager->GetAdjustedActionId(actionId)
            : actionId;
        if (resolvedActionId == 0)
        {
            return new(0, action.Name.ExtractText(), false, "无法取得调整后的技能 ID");
        }

        if (!actionSheet.TryGetRow(resolvedActionId, out var resolvedAction))
        {
            return new(0, action.Name.ExtractText(), false, $"调整后的 ActionId 不存在（{resolvedActionId}）");
        }

        var actionName = resolvedAction.Name.ExtractText();
        var actionStatus = actionManager->GetActionStatus(ActionType.Action, resolvedActionId, targetId);
        return actionStatus == 0
            ? new(resolvedActionId, actionName, true, "技能系统允许使用")
            : new(resolvedActionId, actionName, false, $"技能系统暂不可用（状态码 {actionStatus}）");
    }

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .TryGetRow(actionId, out var action)
                ? action.Name.ExtractText()
                : $"技能 {actionId}";
}
