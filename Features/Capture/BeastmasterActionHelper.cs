using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;

namespace Beastmaster;

public static class BeastmasterActionHelper
{
    private const float BasicComboRange = 6f;

    public static bool IsPlayerInActionRange(
        IBattleChara player,
        IBattleChara target,
        uint actionId,
        out float distance,
        out float actionRange)
    {
        distance = Vector3.Distance(player.Position, target.Position);
        actionRange = 0f;
        if (!DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action))
        {
            return true;
        }

        actionRange = actionId is 44879 or 44883 or 44885
            ? BasicComboRange
            : ActionManager.GetActionRange(actionId);
        if (actionRange <= 0f)
        {
            actionRange = action.Range;
        }

        return actionRange > 0f && distance <= actionRange;
    }

    public static bool IsSummonInActionRange(
        IBattleChara target,
        uint actionId,
        out float distance,
        out float actionRange)
    {
        distance = 0f;
        actionRange = 0f;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        if (DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action))
        {
            actionRange = action.Range > 0 ? action.Range : action.EffectRange;
        }
        var summon = DalamudApi.ObjectTable.FirstOrDefault(obj =>
            obj is IBattleChara battleChara
            && battleChara.ObjectKind == ObjectKind.BattleNpc
            && battleChara.OwnerId == player.EntityId
            && battleChara.EntityId != player.EntityId
            && battleChara.BaseId is >= 18916 and <= 18965
            && battleChara.CurrentHp > 0);
        if (summon is not IBattleChara summonChara)
        {
            return false;
        }

        distance = Vector3.Distance(summonChara.Position, target.Position);
        return actionRange > 0f && distance <= actionRange;
    }

    public static bool TryGetActionLevel(uint actionId, out uint level)
    {
        level = 0;
        if (!DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action))
        {
            return false;
        }

        level = action.ClassJobLevel;
        return true;
    }

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
