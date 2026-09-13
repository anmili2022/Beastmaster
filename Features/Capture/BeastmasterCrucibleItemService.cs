using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HotbarSlot = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlot;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace Beastmaster;

internal sealed unsafe class BeastmasterCrucibleItemService
{
    private const string UseStatusSignature = "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 41 8B F8 48 8B D9 83 FA 0A 0F 83 ?? ?? ?? ?? 8B C2 48 8D 14 40 48 8D 34 91 0F B7 86 84 23 00 00";
    private const string RefreshMappingSignature = "48 89 5C 24 18 57 48 83 EC 30 48 8B D9 E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 48 8B F8 48 85 C0 0F 84 ?? ?? ?? ?? 48 89 6C 24 40 33 ED";
    private const int SlotCount = 10;
    private const int MappingOffset = 80;
    private const int MappingStride = 8;
    private const int InventoryOffset = 9092;
    private const int InventoryStride = 12;
    private const uint FirstRecoveryActionId = 46959;
    private static readonly ushort[] RecoveryItemPriority = [78, 77, 76];

    private DateTime nextUseUtc = DateTime.MinValue;
    private delegate* unmanaged<byte*, uint, byte, uint> getUseStatus;
    private delegate* unmanaged<byte*, void> refreshMapping;
    private bool nativeInitializationAttempted;

    public bool TryUseBestRecoveryItem(IBattleChara player, DateTime now, out ushort itemId)
    {
        itemId = 0;
        if (now < nextUseUtc || player.IsDead || player.CurrentHp == 0)
        {
            return false;
        }

        if (!InitializeNative())
        {
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
        if (actionManager == null || actionManager->AnimationLock > 0f
            || hotbar == null || targets == null || agent == null || director == null
            || (int)director->InstanceContentType != 22)
        {
            return false;
        }

        var self = (GameObject*)player.Address;
        refreshMapping(agent);
        foreach (var recoveryItemId in RecoveryItemPriority)
        {
            var displaySlot = FindDisplaySlot(agent, (byte*)director, recoveryItemId, out var inventorySlot);
            if (displaySlot < 0
                || getUseStatus((byte*)director, inventorySlot, 0) != 0
                || !CanUseOnSelf(recoveryItemId, self))
            {
                continue;
            }

            var slot = new HotbarSlot
            {
                CommandType = (HotbarSlotType)36,
                CommandId = (uint)displaySlot,
            };
            var previousSoftTarget = targets->SoftTarget;
            try
            {
                targets->SoftTarget = self;
                hotbar->ExecuteSlot(&slot);
                itemId = recoveryItemId;
                nextUseUtc = now.AddSeconds(2);
                return true;
            }
            finally
            {
                targets->SoftTarget = previousSoftTarget;
            }
        }

        return false;
    }

    public void Reset() => nextUseUtc = DateTime.MinValue;

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

    private static bool CanUseOnSelf(ushort itemId, GameObject* self)
    {
        var actionId = FirstRecoveryActionId + itemId - RecoveryItemPriority[^1];
        return ActionManager.CanUseActionOnTarget(actionId, self)
            && ActionManager.GetActionInRangeOrLoS(actionId, self, self) == 0;
    }
}
