using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HotbarSlot = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlot;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace Beastmaster;

public sealed unsafe class BeastmasterCrucibleItemService
{
    private const string UseStatusSignature = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 41 8B F8 48 8B D9 83 FA 0A 0F 83 ?? ?? ?? ?? 8B C2 48 8D 14 40 48 8D 34 91 0F B7 86 84 23 00 00";
    private const string RefreshMappingSignature = "48 89 5C 24 18 57 48 83 EC 30 48 8B D9 E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 48 8B F8 48 85 C0 0F 84 ?? ?? ?? ?? 48 89 6C 24 40 33 ED";
    private const int SlotCount = 10;
    private const int MappingOffset = 80;
    private const int MappingStride = 8;
    private const int InventoryOffset = 9092;
    private const int InventoryStride = 12;
    private const uint FirstRecoveryActionId = 46959;
    private const ushort FirstRecoveryItemId = 76;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RecoveryUseInterval = TimeSpan.FromSeconds(2);
    private static readonly ushort[] RecoveryItemPriority = [140, 79, 78, 77, 76, 82, 81, 80, 135];
    private static readonly ushort[] FangItemPriority = [134, 133, 132, 131, 130, 129, 128, 139];

    private DateTime nextRequestUtc = DateTime.MinValue;
    private DateTime nextRecoveryUseUtc = DateTime.MinValue;
    private DateTime itemDetailCleanupNotBeforeUtc = DateTime.MinValue;
    private DateTime itemDetailCleanupDeadlineUtc = DateTime.MinValue;
    private PendingRequest? pendingRequest;
    private string pendingRequestLastFailure = string.Empty;
    private ushort dispatchedRecoveryItemId;
    private ushort failedRecoveryItemId;
    private string recoveryDispatchFailure = string.Empty;
    private delegate* unmanaged<byte*, uint, byte, uint> getUseStatus;
    private delegate* unmanaged<byte*, void> refreshMapping;
    private bool nativeInitializationAttempted;
    private nint mappingDirector;
    private uint mappingTerritory;
    private readonly ushort[] mappingInventoryItemIds = new ushort[SlotCount];
    private readonly List<string> executionProbes = [];
    private readonly Queue<RuleDispatchResult> ruleDispatchResults = [];

    public string LastFailureReason { get; private set; } = "未知原因";

    public string LastDiagnostic { get; private set; } = string.Empty;

    public bool HasPendingRequest => pendingRequest != null;

    public bool TryTakeExecutionProbe(out string probe)
    {
        if (executionProbes.Count == 0)
        {
            probe = string.Empty;
            return false;
        }

        probe = executionProbes[0];
        executionProbes.RemoveAt(0);
        return true;
    }

    public bool TryUseBestRecoveryItem(
        IBattleChara player,
        IBattleChara? target,
        DateTime now,
        out ushort itemId,
        RuleRequestSource? ruleSource = null)
    {
        itemId = 0;
        if (pendingRequest != null || now < nextRecoveryUseUtc || player.IsDead || player.CurrentHp == 0)
        {
            LastFailureReason = pendingRequest != null ? "已有奇弈道具请求等待执行"
                : now < nextRecoveryUseUtc ? "恢复道具防重复等待中"
                : "自身已死亡或 HP 为 0";
            return false;
        }

        if (!InitializeNative())
        {
            LastFailureReason = "奇弈道具原生签名不可用";
            return false;
        }

        var actionManager = ActionManager.Instance();
        var hotbar = RaptureHotbarModule.Instance();
        var targets = TargetSystem.Instance();
        var agentModule = AgentModule.Instance();
        var agent = agentModule == null
            ? null
            : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
        var eventFramework = EventFramework.Instance();
        var director = eventFramework == null
            ? null
            : eventFramework->GetInstanceContentDirector();
        if (actionManager == null || hotbar == null || targets == null || agent == null || director == null
            || (int)director->InstanceContentType != 22)
        {
            LastFailureReason = actionManager == null ? "ActionManager 不可用"
                : hotbar == null ? "RaptureHotbarModule 不可用"
                : targets == null ? "TargetSystem 不可用"
                : agent == null ? "奇弈道具 Agent 不可用"
                : director == null ? "副本内容控制器不可用"
                : $"当前副本类型不是斗兽塔（InstanceContentType={(int)director->InstanceContentType}）";
            return false;
        }

        var self = (GameObject*)player.Address;
        EnsureMapping(agent, director);
        var diagnostic = new System.Text.StringBuilder();
        foreach (var recoveryItemId in RecoveryItemPriority)
        {
            var displaySlot = FindDisplaySlot(agent, (byte*)director, recoveryItemId, out var inventorySlot);
            var useStatus = displaySlot < 0 ? uint.MaxValue : getUseStatus((byte*)director, inventorySlot, 0);
            var canUse = displaySlot >= 0 && useStatus == 0 && CanUseOnTarget(recoveryItemId, self, self);
            diagnostic.Append(
                $"\n  {recoveryItemId}:槽位={displaySlot}/背包={inventorySlot}/状态码={useStatus}/可用于自身={(canUse ? "是" : "否")}");
            if (displaySlot < 0 || useStatus != 0 || !canUse)
            {
                LastFailureReason = displaySlot < 0 ? $"找不到恢复药 {recoveryItemId} 的奇弈道具槽位"
                    : useStatus != 0 ? $"恢复药 {recoveryItemId} 的游戏状态不可用（状态码 {useStatus}）"
                    : $"恢复药 {recoveryItemId} 当前无法对自身使用";
                continue;
            }

            pendingRequest = new PendingRequest(recoveryItemId, player.GameObjectId, true, now.Add(RequestTimeout), displaySlot, inventorySlot, ruleSource);
            pendingRequestLastFailure = "尚未尝试分派";
            itemId = recoveryItemId;
            LastDiagnostic = $"选择阶段：{diagnostic}";
            return true;
        }

        LastDiagnostic = $"选择阶段（均不可用）：{diagnostic}";

        var recoveryFailure = "恢复药套装 140、恢复药 79/78/77/76、药粉 82/81/80 和吸血药 135 均不存在或当前不可用";
        if (target != null && !target.IsDead && target.CurrentHp > 0)
        {
            if (TryUseCrucibleItemOnTarget(134, target, now, ruleSource))
            {
                pendingRequest = pendingRequest! with { IsRecovery = true };
                itemId = 134;
                return true;
            }

            LastFailureReason = recoveryFailure + $"；吸血鬼之牙 134 不可用：{LastFailureReason}";
            return false;
        }

        LastFailureReason = recoveryFailure + "；吸血鬼之牙需要有效敌对目标";
        return false;
    }

    public void Reset()
    {
        nextRequestUtc = DateTime.MinValue;
        nextRecoveryUseUtc = DateTime.MinValue;
        itemDetailCleanupNotBeforeUtc = DateTime.MinValue;
        itemDetailCleanupDeadlineUtc = DateTime.MinValue;
        pendingRequest = null;
        pendingRequestLastFailure = string.Empty;
        dispatchedRecoveryItemId = 0;
        failedRecoveryItemId = 0;
        recoveryDispatchFailure = string.Empty;
        mappingDirector = 0;
        mappingTerritory = 0;
        Array.Clear(mappingInventoryItemIds);
        scheduledProbe = default;
        executionProbes.Clear();
        ruleDispatchResults.Clear();
        LastDiagnostic = string.Empty;
    }

    public bool TryTakeRuleDispatchResult(out RuleDispatchResult result)
    {
        if (ruleDispatchResults.Count == 0)
        {
            result = default;
            return false;
        }

        result = ruleDispatchResults.Dequeue();
        return true;
    }

    public bool TryTakeDispatchedRecoveryItem(out ushort itemId)
    {
        itemId = dispatchedRecoveryItemId;
        dispatchedRecoveryItemId = 0;
        return itemId != 0;
    }

    public bool TryTakeRecoveryDispatchFailure(out ushort itemId, out string reason)
    {
        itemId = failedRecoveryItemId;
        reason = recoveryDispatchFailure;
        failedRecoveryItemId = 0;
        recoveryDispatchFailure = string.Empty;
        return itemId != 0;
    }

    public void ProcessPendingRequest(DateTime now)
    {
        UpdateItemDetailCleanup(now);
        if (pendingRequest is not { } request)
        {
            return;
        }

        if (now >= request.DeadlineUtc)
        {
            LastFailureReason = $"待执行请求超时，最后原因：{pendingRequestLastFailure}";
            RecordRuleDispatchResult(request, false, LastFailureReason);
            if (request.IsRecovery)
            {
                failedRecoveryItemId = request.ItemId;
                recoveryDispatchFailure = LastFailureReason;
            }
            pendingRequest = null;
            pendingRequestLastFailure = string.Empty;
            return;
        }

        var target = DalamudApi.ObjectTable
            .OfType<IBattleChara>()
            .FirstOrDefault(actor => actor.GameObjectId == request.TargetId);
        if (target == null)
        {
            LastFailureReason = "待执行请求的目标暂时不可用";
            pendingRequestLastFailure = LastFailureReason;
            return;
        }

        if (!TryVerifyRequestSlot(request, out var verifiedDisplaySlot, out var verifiedInventorySlot, out var slotFailure))
        {
            LastFailureReason = slotFailure;
            RecordRuleDispatchResult(request, false, LastFailureReason);
            if (request.IsRecovery)
            {
                failedRecoveryItemId = request.ItemId;
                recoveryDispatchFailure = LastFailureReason;
            }

            pendingRequest = null;
            pendingRequestLastFailure = string.Empty;
            return;
        }

        if (!TryDispatchCrucibleItem(request.ItemId, verifiedDisplaySlot, verifiedInventorySlot, target, now))
        {
            pendingRequestLastFailure = LastFailureReason;
            return;
        }

        pendingRequest = null;
        pendingRequestLastFailure = string.Empty;
        RecordRuleDispatchResult(request, true,
            $"已分派道具 {request.ItemId}（显示槽位 {verifiedDisplaySlot}，背包槽位 {verifiedInventorySlot}）");
        if (request.IsRecovery)
        {
            dispatchedRecoveryItemId = request.ItemId;
            nextRecoveryUseUtc = now.Add(RecoveryUseInterval);
        }
    }

    private bool TryVerifyRequestSlot(PendingRequest request, out int displaySlot, out uint inventorySlot, out string failure)
    {
        displaySlot = -1;
        inventorySlot = SlotCount;
        failure = string.Empty;
        if (!InitializeNative())
        {
            failure = "奇弈道具原生签名不可用";
            return false;
        }

        var agentModule = AgentModule.Instance();
        var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
        var eventFramework = EventFramework.Instance();
        var director = eventFramework == null ? null : eventFramework->GetInstanceContentDirector();
        if (agent == null || director == null || (int)director->InstanceContentType != 22)
        {
            failure = agent == null ? "奇弈道具 Agent 不可用"
                : director == null ? "副本内容控制器不可用"
                : $"当前副本类型不是斗兽塔（InstanceContentType={(int)director->InstanceContentType}）";
            return false;
        }

        displaySlot = FindDisplaySlot(agent, (byte*)director, request.ItemId, out inventorySlot);
        failure = $"请求槽位与当前映射不一致（请求：显示槽位={request.DisplaySlot}/背包={request.InventorySlot}，"
            + $"当前：显示槽位={displaySlot}/背包={inventorySlot}，道具={request.ItemId}）";
        return displaySlot == request.DisplaySlot && inventorySlot == request.InventorySlot;
    }

    public bool TryUseCrucibleItemOnTarget(
        BeastmasterCrucibleItemType itemType,
        IBattleChara player,
        IBattleChara? target,
        DateTime now,
        out ushort itemId,
        RuleRequestSource? ruleSource = null)
    {
        itemId = 0;
        if (itemType == BeastmasterCrucibleItemType.Recovery)
        {
            return TryUseBestRecoveryItem(player, target, now, out itemId, ruleSource);
        }

        if (itemType == BeastmasterCrucibleItemType.VampireFang)
        {
            if (target != null && TryUseCrucibleItemOnTarget(134, target, now, ruleSource))
            {
                itemId = 134;
                return true;
            }

            if (target == null)
            {
                LastFailureReason = "吸血鬼之牙需要有效敌对目标";
            }
            return false;
        }

        var selfItemId = itemType switch
        {
            BeastmasterCrucibleItemType.DodgeBook => (ushort)137,
            BeastmasterCrucibleItemType.ReflectBook => (ushort)136,
            BeastmasterCrucibleItemType.TimeSand => (ushort)138,
            BeastmasterCrucibleItemType.StrengthMedicine => (ushort)104,
            _ => (ushort)0,
        };
        if (selfItemId != 0)
        {
            if (TryUseCrucibleItemOnTarget(selfItemId, player, now, ruleSource))
            {
                itemId = selfItemId;
                return true;
            }

            return false;
        }

        if (pendingRequest != null)
        {
            LastFailureReason = "已有奇弈道具请求等待执行";
            return false;
        }

        var fangFailures = new List<string>(FangItemPriority.Length);
        foreach (var fangItemId in FangItemPriority)
        {
            if (target != null && TryUseCrucibleItemOnTarget(fangItemId, target, now, ruleSource))
            {
                itemId = fangItemId;
                return true;
            }

            fangFailures.Add(target == null
                ? $"{fangItemId}:需要有效敌对目标"
                : $"{fangItemId}:{LastFailureReason}");
        }

        LastFailureReason = fangFailures.Count == 0
            ? "尚未配置任何牙的 ID"
            : "各种牙均不可用（" + string.Join("；", fangFailures) + "）";
        return false;
    }

    public unsafe bool TryUseCrucibleItemOnTarget(
        ushort itemId,
        IBattleChara target,
        DateTime now,
        RuleRequestSource? ruleSource = null)
    {
        if (pendingRequest != null || now < nextRequestUtc || target.IsDead || target.CurrentHp == 0)
        {
            LastFailureReason = pendingRequest != null ? "已有奇弈道具请求等待执行"
                : now < nextRequestUtc ? "奇弈道具请求节流中"
                : "目标已死亡或 HP 为 0";
            return false;
        }

        if (!InitializeNative())
        {
            LastFailureReason = "奇弈道具原生签名不可用";
            return false;
        }

        var actionManager = ActionManager.Instance();
        var hotbar = RaptureHotbarModule.Instance();
        var targets = TargetSystem.Instance();
        var agentModule = AgentModule.Instance();
        var agent = agentModule == null
            ? null
            : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
        var eventFramework = EventFramework.Instance();
        var director = eventFramework == null
            ? null
            : eventFramework->GetInstanceContentDirector();
        if (actionManager == null || hotbar == null || targets == null || agent == null || director == null
            || (int)director->InstanceContentType != 22)
        {
            LastFailureReason = actionManager == null ? "ActionManager 不可用"
                : hotbar == null ? "RaptureHotbarModule 不可用"
                : targets == null ? "TargetSystem 不可用"
                : agent == null ? "奇弈道具 Agent 不可用"
                : director == null ? "副本内容控制器不可用"
                : $"当前副本类型不是斗兽塔（InstanceContentType={(int)director->InstanceContentType}）";
            return false;
        }

        var targetObj = (GameObject*)target.Address;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        var self = player == null ? null : (GameObject*)player.Address;
        EnsureMapping(agent, director);
        var displaySlot = FindDisplaySlot(agent, (byte*)director, itemId, out var inventorySlot);
        var useStatus = displaySlot < 0 ? uint.MaxValue : getUseStatus((byte*)director, inventorySlot, 0);
        if (displaySlot < 0
            || useStatus != 0
            || !CanUseOnTarget(itemId, self, targetObj))
        {
            LastFailureReason = displaySlot < 0 ? $"找不到奇弈道具 {itemId} 的槽位"
                : useStatus != 0
                    ? $"奇弈道具 {itemId} 的游戏状态不可用（状态码 {useStatus}）"
                    : $"奇弈道具 {itemId} 当前无法对目标使用（目标、射程或视线不满足）";
            return false;
        }

        pendingRequest = new PendingRequest(itemId, target.GameObjectId, false, now.Add(RequestTimeout), displaySlot, inventorySlot, ruleSource);
        pendingRequestLastFailure = "尚未尝试分派";
        return true;
    }

    private void RecordRuleDispatchResult(PendingRequest request, bool success, string detail)
    {
        if (request.RuleSource is { } source)
        {
            ruleDispatchResults.Enqueue(new RuleDispatchResult(source, request.ItemId, success, detail));
        }
    }

    private unsafe bool TryDispatchCrucibleItem(ushort itemId, int displaySlot, uint inventorySlot, IBattleChara target, DateTime now)
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null || actionManager->AnimationLock > 0f)
        {
            LastFailureReason = actionManager == null ? "ActionManager 不可用" : "动作锁中";
            return false;
        }

        var hotbar = RaptureHotbarModule.Instance();
        var targets = TargetSystem.Instance();
        var agentModule = AgentModule.Instance();
        var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
        var eventFramework = EventFramework.Instance();
        var director = eventFramework == null ? null : eventFramework->GetInstanceContentDirector();
        if (hotbar == null || targets == null || agent == null || director == null
            || (int)director->InstanceContentType != 22)
        {
            LastFailureReason = hotbar == null ? "RaptureHotbarModule 不可用"
                : targets == null ? "TargetSystem 不可用"
                : agent == null ? "奇弈道具 Agent 不可用"
                : director == null ? "副本内容控制器不可用"
                : $"当前副本类型不是斗兽塔（InstanceContentType={(int)director->InstanceContentType}）";
            return false;
        }

        EnsureMapping(agent, director);
        var useStatus = displaySlot < 0 ? uint.MaxValue : getUseStatus((byte*)director, inventorySlot, 0);
        LastDiagnostic = $"分派阶段：道具={itemId}/槽位={displaySlot}/背包={inventorySlot}/状态码={useStatus}/AnimationLock={actionManager->AnimationLock:0.###}";
        if (displaySlot < 0 || useStatus != 0)
        {
            LastFailureReason = displaySlot < 0
                ? $"找不到奇弈道具 {itemId} 的槽位"
                : $"奇弈道具 {itemId} 的游戏状态不可用（状态码 {useStatus}）";
            return false;
        }

        var player = DalamudApi.ObjectTable.LocalPlayer;
        var self = player == null ? null : (GameObject*)player.Address;
        var targetObj = (GameObject*)target.Address;
        if (!CanUseOnTarget(itemId, self, targetObj))
        {
            LastFailureReason = $"奇弈道具 {itemId} 当前无法对目标使用（目标、射程或视线不满足）";
            LastDiagnostic += "/可用于目标=否";
            return false;
        }

        LastDiagnostic += "/可用于目标=是";
        var inventoryBefore = *(ushort*)((byte*)director + InventoryOffset + inventorySlot * InventoryStride);
        var lockBefore = actionManager->AnimationLock;
        var hpBefore = self == null ? 0u : ((IBattleChara)target).CurrentHp;
        var previousSoftTarget = targets->SoftTarget;
        byte executed = 0;
        var dispatchPath = string.Empty;
        try
        {
            targets->SoftTarget = targetObj;
            if (TryFindCrucibleHotbarSlot(displaySlot, out var hotbarId, out var hotbarSlotId))
            {
                executed = hotbar->ExecuteSlotById((uint)hotbarId, (uint)hotbarSlotId);
                dispatchPath = $"ExecuteSlotById={executed} 热键栏={hotbarId}/{hotbarSlotId}";
            }
            else if (TryDispatchViaAgent(agent, displaySlot, now))
            {
                executed = 1;
                dispatchPath = "Agent497 ReceiveEvent 后备路径";
            }
            else
            {
                LastFailureReason = $"找不到奇弈道具显示槽位 {displaySlot} 对应的热键栏槽位，Agent 后备分派也失败";
                LastDiagnostic += "/热键栏槽位=未找到/Agent后备=失败";
                return false;
            }
        }
        finally
        {
            targets->SoftTarget = previousSoftTarget;
        }

        var inventoryAfter = *(ushort*)((byte*)director + InventoryOffset + inventorySlot * InventoryStride);
        executionProbes.Add(
            $"[执行探针 {DateTime.Now:HH:mm:ss.fff}] 道具{itemId} 目标={target.GameObjectId} 显示槽={displaySlot}：{dispatchPath} "
            + $"执行前 AnimationLock={lockBefore:0.###}/背包itemId={inventoryBefore}/HP={hpBefore} → "
            + $"执行后 AnimationLock={actionManager->AnimationLock:0.###}/背包itemId={inventoryAfter}");
        if (executed == 0)
        {
            LastFailureReason = $"奇弈道具 {itemId} 的热键栏分派返回 0";
            return false;
        }

        scheduleExecutionProbe(itemId, displaySlot, inventorySlot, now);
        nextRequestUtc = now.AddMilliseconds(500);
        return true;
    }

    private bool TryDispatchViaAgent(byte* agent, int displaySlot, DateTime now)
    {
        if (agent == null || displaySlot is < 0 or >= SlotCount)
        {
            return false;
        }

        var agentModule = AgentModule.Instance();
        var itemDetail = agentModule == null
            ? null
            : agentModule->GetAgentByInternalId((AgentId)498);
        var itemDetailWasActive = itemDetail != null && ((AgentInterface*)itemDetail)->IsAgentActive();

        var selectArgs = stackalloc AtkValue[3];
        selectArgs[0] = new AtkValue { Type = (AtkValueType)3, Int = 6 };
        selectArgs[1] = new AtkValue { Type = (AtkValueType)3, Int = displaySlot };
        selectArgs[2] = new AtkValue { Type = AtkValueType.Undefined };
        var result = new AtkValue();
        ((AgentInterface*)agent)->ReceiveEvent(&result, selectArgs, 3, 0);

        var useArgs = stackalloc AtkValue[5];
        useArgs[0] = new AtkValue { Type = (AtkValueType)3, Int = 0 };
        useArgs[1] = new AtkValue { Type = (AtkValueType)3, Int = 0 };
        useArgs[2] = new AtkValue { Type = (AtkValueType)5, UInt = 0 };
        useArgs[3] = new AtkValue { Type = AtkValueType.Undefined };
        useArgs[4] = new AtkValue { Type = AtkValueType.Undefined };
        ((AgentInterface*)agent)->ReceiveEvent(&result, useArgs, 5, 3);
        if (!itemDetailWasActive)
        {
            itemDetailCleanupNotBeforeUtc = now.AddMilliseconds(100);
            itemDetailCleanupDeadlineUtc = now.AddSeconds(1);
        }
        return true;
    }

    private void UpdateItemDetailCleanup(DateTime now)
    {
        if (itemDetailCleanupDeadlineUtc == DateTime.MinValue
            || now < itemDetailCleanupNotBeforeUtc)
        {
            return;
        }

        if (now >= itemDetailCleanupDeadlineUtc)
        {
            itemDetailCleanupNotBeforeUtc = DateTime.MinValue;
            itemDetailCleanupDeadlineUtc = DateTime.MinValue;
            return;
        }

        var agentModule = AgentModule.Instance();
        var itemDetail = agentModule == null
            ? null
            : agentModule->GetAgentByInternalId((AgentId)498);
        if (itemDetail != null && ((AgentInterface*)itemDetail)->IsAgentActive())
        {
            ((AgentInterface*)itemDetail)->Hide();
            itemDetailCleanupNotBeforeUtc = DateTime.MinValue;
            itemDetailCleanupDeadlineUtc = DateTime.MinValue;
        }
    }

    private (ushort ItemId, int DisplaySlot, uint InventorySlot, DateTime DeadlineUtc) scheduledProbe;

    private void scheduleExecutionProbe(ushort itemId, int displaySlot, uint inventorySlot, DateTime now)
        => scheduledProbe = (itemId, displaySlot, inventorySlot, now.AddSeconds(1));

    public void UpdateExecutionProbe(DateTime now)
    {
        if (scheduledProbe.DeadlineUtc == DateTime.MinValue)
        {
            return;
        }

        if (now >= scheduledProbe.DeadlineUtc)
        {
            var (itemId, displaySlot, inventorySlot, _) = scheduledProbe;
            scheduledProbe = default;
            var agentModule = AgentModule.Instance();
            var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
            var eventFramework = EventFramework.Instance();
            var director = eventFramework == null ? null : eventFramework->GetInstanceContentDirector();
            var actionManager = ActionManager.Instance();
            var player = DalamudApi.ObjectTable.LocalPlayer;
            if (agent != null && director != null && actionManager != null && (int)director->InstanceContentType == 22)
            {
                var inventoryItemId = *(ushort*)((byte*)director + InventoryOffset + inventorySlot * InventoryStride);
                var foundDisplaySlot = FindDisplaySlot(agent, (byte*)director, itemId, out _);
                executionProbes.Add(
                    $"[执行探针 +1s] 道具{itemId}：AnimationLock={actionManager->AnimationLock:0.###}"
                    + $"/原背包槽itemId={inventoryItemId}/当前查找显示槽位={foundDisplaySlot}"
                    + $"/HP={(player == null ? 0 : player.CurrentHp)}");
            }
            else
            {
                executionProbes.Add($"[执行探针 +1s] 道具{itemId}：上下文不可用");
            }
        }
    }

    private bool TryFindCrucibleHotbarSlot(int displaySlot, out int hotbarId, out int slotId)
    {
        hotbarId = -1;
        slotId = -1;
        var hotbar = RaptureHotbarModule.Instance();
        if (hotbar == null)
        {
            return false;
        }

        const int hotbarCount = 18;
        for (var candidateHotbar = 0; candidateHotbar < hotbarCount; candidateHotbar++)
        {
            for (var candidateSlot = 0; candidateSlot < 16; candidateSlot++)
            {
                var slot = hotbar->GetSlotById((uint)candidateHotbar, (uint)candidateSlot);
                if (slot != null
                    && (byte)slot->CommandType == 36
                    && slot->CommandId == (uint)displaySlot)
                {
                    hotbarId = candidateHotbar;
                    slotId = candidateSlot;
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureMapping(byte* agent, InstanceContentDirector* director)
    {
        var directorAddress = (nint)director;
        var territory = DalamudApi.ClientState.TerritoryType;
        var changed = mappingDirector != directorAddress || mappingTerritory != territory;
        for (var inventorySlot = 0; inventorySlot < SlotCount; inventorySlot++)
        {
            var itemId = *(ushort*)((byte*)director + InventoryOffset + inventorySlot * InventoryStride);
            if (mappingInventoryItemIds[inventorySlot] != itemId)
            {
                changed = true;
            }

            mappingInventoryItemIds[inventorySlot] = itemId;
        }

        mappingDirector = directorAddress;
        mappingTerritory = territory;
        if (changed)
        {
            refreshMapping(agent);
        }
    }

    private int FindDisplaySlot(byte* agent, byte* director, ushort wantedItemId, out uint inventorySlot)
    {
        inventorySlot = SlotCount;
        for (var displaySlot = 0; displaySlot < SlotCount; displaySlot++)
        {
            var entry = agent + MappingOffset + displaySlot * MappingStride;
            var candidateInventorySlot = *(uint*)entry;
            var itemId = ((ushort*)entry)[2];
            if (candidateInventorySlot < SlotCount
                && itemId == wantedItemId
                && *(ushort*)(director + InventoryOffset + candidateInventorySlot * InventoryStride) == itemId)
            {
                inventorySlot = candidateInventorySlot;
                return displaySlot;
            }
        }

        return -1;
    }

    public string DescribeRecoveryItemSlot(ushort itemId)
    {
        if (!InitializeNative())
        {
            return "原生签名不可用";
        }

        var agentModule = AgentModule.Instance();
        var agent = agentModule == null ? null : (byte*)agentModule->GetAgentByInternalId((AgentId)497);
        var eventFramework = EventFramework.Instance();
        var director = eventFramework == null ? null : eventFramework->GetInstanceContentDirector();
        if (agent == null || director == null || (int)director->InstanceContentType != 22)
        {
            return "非斗兽塔上下文";
        }

        var displaySlot = FindDisplaySlot(agent, (byte*)director, itemId, out var inventorySlot);
        if (displaySlot < 0)
        {
            return $"道具{itemId}已不在任何显示槽位（已消耗或重排）";
        }

        var inventoryItemId = *(ushort*)((byte*)director + InventoryOffset + inventorySlot * InventoryStride);
        return $"道具{itemId}仍在显示槽位{displaySlot}/背包{inventorySlot}（背包内 itemId={inventoryItemId}），未被消耗";
    }

    private bool InitializeNative()
    {
        if (getUseStatus != null && refreshMapping != null)
        {
            return true;
        }

        if (nativeInitializationAttempted)
        {
            return false;
        }

        nativeInitializationAttempted = true;
        if (!DalamudApi.SigScanner.TryScanText(UseStatusSignature, out var statusAddress)
            || !DalamudApi.SigScanner.TryScanText(RefreshMappingSignature, out var refreshAddress))
        {
            DalamudApi.Log.Warning("奇弈恢复药原生签名未找到，自动恢复药已停用。");
            return false;
        }

        getUseStatus = (delegate* unmanaged<byte*, uint, byte, uint>)statusAddress;
        refreshMapping = (delegate* unmanaged<byte*, void>)refreshAddress;
        return true;
    }

    private static bool CanUseOnTarget(ushort itemId, GameObject* self, GameObject* target)
    {
        var actionId = GetTargetCheckAction(itemId);
        return actionId != 0
            && self != null
            && target != null
            && ActionManager.CanUseActionOnTarget(actionId, target)
            && ActionManager.GetActionInRangeOrLoS(actionId, self, target) == 0;
    }

    private static uint GetTargetCheckAction(ushort itemId)
        => itemId switch
        {
            >= 76 and <= 104 => FirstRecoveryActionId + itemId - FirstRecoveryItemId,
            >= 105 and <= 113 => 46987u + (uint)(itemId - 104) / 2u,
            >= 114 and <= 141 => 46992u + itemId - 114u,
            _ => 0,
        };

    public readonly record struct RuleRequestSource(string RuleSetName, string RuleName, int RuleIndex);

    public readonly record struct RuleDispatchResult(RuleRequestSource Source, ushort ItemId, bool Success, string Detail);

    private sealed record PendingRequest(
        ushort ItemId,
        ulong TargetId,
        bool IsRecovery,
        DateTime DeadlineUtc,
        int DisplaySlot,
        uint InventorySlot,
        RuleRequestSource? RuleSource);
}
