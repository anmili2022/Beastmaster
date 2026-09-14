using Dalamud.Plugin.Services;
using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using DalamudAgentId = Dalamud.Game.Agent.AgentId;
using GameAgentId = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId;

namespace Beastmaster;

public sealed unsafe class BeastmasterPetPartyService : IDisposable
{
    private const string AddonName = "XBMPetParty";
    private const int MemberCountIndex = 5;
    private const int FirstMemberIndex = 6;
    private const int MemberStride = 77;
    private const int CapacityIndex = 1162;
    private const uint IconBase = 242000;
    private const int MaximumDiagnosticEntries = 200;

    private long nextReadAt;
    private long nextApplyAt;
    private long stateStartedAt;
    private ApplyState applyState;
    private BeastmasterPartyPreset? applyingPreset;
    private int applyingMemberIndex;
    private bool clearOnly;
    private int targetNotebookPage = -1;
    private string applyError = string.Empty;
    private string lastDiagnosticSnapshot = string.Empty;
    private readonly List<string> diagnosticEntries = [];

    public bool DiagnosticsEnabled { get; set; }

    public string Diagnostics => diagnosticEntries.Count == 0
        ? "尚未记录编队操作。"
        : string.Join(Environment.NewLine, diagnosticEntries);

    public BeastmasterPetPartySnapshot Snapshot { get; private set; }
        = BeastmasterPetPartySnapshot.Unavailable("魔兽编队界面未打开");

    public bool IsApplying => applyState != ApplyState.Idle;
    public string ApplyStatus => IsApplying ? GetApplyStateText() : applyError;

    public bool TryTestAddMember(int catalogNumber)
    {
        if (!Snapshot.Available || Snapshot.MemberCount != 0)
        {
            applyError = "单步加入测试要求第一盘队伍为空。";
            return false;
        }

        if (catalogNumber is < 1 or > 50 || !IsNotebookVisible())
        {
            applyError = "请先从魔兽编队打开原生图鉴，并选择有效的图鉴编号。";
            return false;
        }

        SendAgentEvent(500, 0, 7, (ulong)(catalogNumber - 1));
        applyError = $"已发送图鉴 {catalogNumber:00} 单步加入请求，等待编队快照确认。";
        return true;
    }

    public bool TryTestClear()
    {
        if (IsApplying || !Snapshot.Available || Snapshot.MemberCount == 0)
        {
            applyError = IsApplying ? "已有编队操作正在执行。"
                : !Snapshot.Available ? Snapshot.Reason
                : "当前队伍已经为空。";
            return false;
        }

        applyingPreset = new BeastmasterPartyPreset { Name = "单步清空验证" };
        applyingMemberIndex = 0;
        clearOnly = true;
        applyError = string.Empty;
        ChangeApplyState(ApplyState.RequestClear, 0);
        return true;
    }

    public void Start()
    {
        DalamudApi.Framework.Update += OnFrameworkUpdate;
        DalamudApi.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, OnAddonReceiveEvent);
        DalamudApi.AgentLifecycle.RegisterListener(AgentEvent.PreReceiveEvent, (DalamudAgentId)501, OnAgentReceiveEvent);
        DalamudApi.AgentLifecycle.RegisterListener(AgentEvent.PreReceiveEvent, (DalamudAgentId)500, OnAgentReceiveEvent);
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= OnFrameworkUpdate;
        DalamudApi.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, OnAddonReceiveEvent);
        DalamudApi.AgentLifecycle.UnregisterListener(AgentEvent.PreReceiveEvent, (DalamudAgentId)501, OnAgentReceiveEvent);
        DalamudApi.AgentLifecycle.UnregisterListener(AgentEvent.PreReceiveEvent, (DalamudAgentId)500, OnAgentReceiveEvent);
    }

    public void ClearDiagnostics()
    {
        diagnosticEntries.Clear();
        lastDiagnosticSnapshot = string.Empty;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (Environment.TickCount64 < nextReadAt)
        {
            return;
        }

        nextReadAt = Environment.TickCount64 + 250;
        try
        {
            Snapshot = ReadSnapshot();
            RecordSnapshotChange(Snapshot);
            UpdateApplyState();
        }
        catch (Exception ex)
        {
            Snapshot = BeastmasterPetPartySnapshot.Unavailable("读取魔兽编队失败");
            DalamudApi.Log.Warning(ex, "读取第一盘魔兽编队失败。");
        }
    }

    public bool TryApply(BeastmasterPartyPreset preset)
    {
        if (IsApplying)
        {
            applyError = "已有编队应用正在执行。";
            return false;
        }

        if (!Snapshot.Available)
        {
            applyError = Snapshot.Reason;
            return false;
        }

        if (!preset.TryValidate(out var error))
        {
            applyError = error;
            return false;
        }

        var targetCapacity = Math.Min(preset.SlotCount, Snapshot.Capacity);
        applyingPreset = new BeastmasterPartyPreset
        {
            Name = preset.Name,
            SlotCount = targetCapacity,
            Members = [.. preset.Members.Take(targetCapacity)],
        };
        applyingMemberIndex = 0;
        clearOnly = false;
        targetNotebookPage = -1;
        applyError = string.Empty;
        if (preset.SlotCount < Snapshot.Capacity)
        {
            applyError = $"提示：当前编队有 {Snapshot.Capacity} 个栏位，应用后将空余 {Snapshot.Capacity - preset.SlotCount} 个位置。";
        }
        else if (preset.SlotCount > Snapshot.Capacity)
        {
            applyError = $"提示：当前编队只有 {Snapshot.Capacity} 个栏位，将只应用预设前 {Snapshot.Capacity} 个位置。";
        }
        applyState = ApplyState.RequestClear;
        nextApplyAt = 0;
        stateStartedAt = Environment.TickCount64;
        return true;
    }

    public void CancelApply(string reason)
    {
        if (!IsApplying)
        {
            return;
        }

        applyState = ApplyState.Idle;
        applyingPreset = null;
        clearOnly = false;
        targetNotebookPage = -1;
        applyError = reason;
    }

    private void UpdateApplyState()
    {
        if (!IsApplying || applyingPreset == null || Environment.TickCount64 < nextApplyAt)
        {
            return;
        }

        if (!Snapshot.Available)
        {
            CancelApply("魔兽编队界面已关闭，应用已中止。");
            return;
        }

        nextApplyAt = Environment.TickCount64 + 250;
        if (Environment.TickCount64 - stateStartedAt > 5000)
        {
            CancelApply($"{GetApplyStateText()}超时，应用已中止。当前队伍可能已部分更新。");
            return;
        }

        switch (applyState)
        {
            case ApplyState.RequestClear:
                if (Snapshot.MemberCount == 0)
                {
                    ChangeApplyState(applyingPreset.Members.Count == 0 ? ApplyState.Completed : ApplyState.OpenNotebook, 0);
                    break;
                }

                SendClearRequest();
                ChangeApplyState(ApplyState.WaitClearConfirmation, 250);
                break;
            case ApplyState.WaitClearConfirmation:
                if (IsClearConfirmationVisible())
                {
                    if (!ConfirmClearParty())
                    {
                        CancelApply("无法确认清空队伍窗口。");
                        break;
                    }

                    ChangeApplyState(ApplyState.WaitEmpty, 250);
                }
                break;
            case ApplyState.WaitEmpty:
                if (Snapshot.MemberCount == 0)
                {
                    if (clearOnly)
                    {
                        applyState = ApplyState.Idle;
                        applyingPreset = null;
                        clearOnly = false;
                        applyError = "单步清空验证成功：当前队伍为 0/10。";
                    }
                    else
                    {
                        ChangeApplyState(applyingPreset.Members.Count == 0
                            ? ApplyState.Completed
                            : IsNotebookVisible() ? ApplyState.SelectMember : ApplyState.OpenNotebook, 0);
                    }
                }
                break;
            case ApplyState.OpenNotebook:
                SendAgentEvent(501, 0, 5);
                ChangeApplyState(ApplyState.WaitNotebook, 250);
                break;
            case ApplyState.WaitNotebook:
                if (IsNotebookVisible())
                {
                    ChangeApplyState(ApplyState.SelectMember, 0);
                }
                break;
            case ApplyState.SelectMember:
                if (applyingMemberIndex >= applyingPreset.Members.Count)
                {
                    ChangeApplyState(ApplyState.Completed, 0);
                    break;
                }

                var number = applyingPreset.Members[applyingMemberIndex];
                targetNotebookPage = (number - 1) / 25;
                if (GetNotebookPage() != targetNotebookPage)
                {
                    RequestNotebookPage(targetNotebookPage);
                    ChangeApplyState(ApplyState.WaitNotebookPage, 250);
                    break;
                }

                var pageIndex = (number - 1) % 25;
                SendAgentEvent(500, 0, 5, (ulong)pageIndex);
                SendAgentEvent(500, 0, 7, (ulong)pageIndex);
                ChangeApplyState(ApplyState.WaitMember, 250);
                break;
            case ApplyState.WaitNotebookPage:
                if (GetNotebookPage() == targetNotebookPage)
                {
                    ChangeApplyState(ApplyState.SelectMember, 0);
                }
                break;
            case ApplyState.WaitMember:
                if (Snapshot.MemberCount == applyingMemberIndex + 1
                    && Snapshot.Members[^1].CatalogNumber == applyingPreset.Members[applyingMemberIndex])
                {
                    applyingMemberIndex++;
                    ChangeApplyState(ApplyState.SelectMember, 0);
                }
                break;
            case ApplyState.Completed:
                if (!Snapshot.Members.Select(member => member.CatalogNumber).SequenceEqual(applyingPreset.Members))
                {
                    CancelApply("最终编队与预设不一致，应用已中止。");
                    break;
                }

                applyState = ApplyState.Idle;
                applyingPreset = null;
                clearOnly = false;
                targetNotebookPage = -1;
                applyError = "编队应用完成。";
                break;
        }
    }

    private void ChangeApplyState(ApplyState state, int delayMilliseconds)
    {
        applyState = state;
        stateStartedAt = Environment.TickCount64;
        nextApplyAt = stateStartedAt + delayMilliseconds;
    }

    private string GetApplyStateText()
        => applyState switch
        {
            ApplyState.RequestClear => "请求清空当前队伍",
            ApplyState.WaitClearConfirmation => "等待清空确认窗口",
            ApplyState.WaitEmpty => "等待队伍清空",
            ApplyState.OpenNotebook => "打开魔兽图鉴",
            ApplyState.WaitNotebook => "等待魔兽图鉴打开",
            ApplyState.SelectMember => $"准备加入第 {applyingMemberIndex + 1} 只魔兽",
            ApplyState.WaitMember => $"等待确认第 {applyingMemberIndex + 1} 只魔兽",
            ApplyState.Completed => "核对最终编队",
            _ => string.Empty,
        };

    private static bool IsNotebookVisible()
    {
        var addon = DalamudApi.GameGui.GetAddonByName("XBMMonsterNotebook", 1);
        return !addon.IsNull && addon.IsVisible;
    }

    private static unsafe int GetNotebookPage()
    {
        var address = DalamudApi.GameGui.GetAddonByName("XBMMonsterNotebook", 1).Address;
        var addon = (AtkUnitBase*)address;
        if (addon == null || addon->AtkValues == null || addon->AtkValuesCount <= 10)
        {
            return -1;
        }

        var page = addon->AtkValues[10];
        return page.TypeCode() is 3 or 4 or 5 && page.UInt <= 1 ? (int)page.UInt : -1;
    }

    private static unsafe bool RequestNotebookPage(int page)
    {
        var address = DalamudApi.GameGui.GetAddonByName("XBMMonsterNotebook", 1).Address;
        var addon = (AtkUnitBase*)address;
        if (addon == null || addon->AtkValues == null || page is < 0 or > 1)
        {
            return false;
        }

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = (AtkValueType)3, Int = 3 };
        values[1] = new AtkValue { Type = (AtkValueType)5, UInt = (uint)page };
        return addon->FireCallback(2, values, true);
    }

    private static unsafe bool IsClearConfirmationVisible()
    {
        var addon = DalamudApi.GameGui.GetAddonByName("SelectYesno", 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            return false;
        }

        var unit = (AtkUnitBase*)addon.Address;
        if (unit == null || unit->AtkValues == null)
        {
            return false;
        }

        for (var index = 0; index < unit->AtkValuesCount; index++)
        {
            var value = unit->AtkValues[index];
            if (value.TypeCode() is 8 or 10
                && (value.String.ToString() ?? string.Empty).Contains("清空整个队伍", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe bool ConfirmClearParty()
    {
        var addon = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("SelectYesno", 1).Address;
        if (addon == null || !addon->IsVisible || !IsClearConfirmationVisible())
        {
            return false;
        }

        var value = new AtkValue
        {
            Type = (AtkValueType)3,
            Int = 0,
        };
        return addon->FireCallback(1, &value, true);
    }

    private static unsafe bool SendAgentEvent(uint agentId, ulong eventKind, params ulong?[] values)
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId((GameAgentId)agentId);
        if (agent == null)
        {
            return false;
        }

        var args = stackalloc AtkValue[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            args[index] = values[index].HasValue
                ? new AtkValue
                {
                    Type = (AtkValueType)3,
                    Int = (int)values[index]!.Value,
                }
                : new AtkValue
            {
                Type = AtkValueType.Undefined,
            };
        }

        var result = new AtkValue();
        agent->ReceiveEvent(&result, args, (uint)values.Length, eventKind);
        return true;
    }

    private static unsafe bool SendClearRequest()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId((GameAgentId)501);
        if (agent == null)
        {
            return false;
        }

        var args = stackalloc AtkValue[5];
        args[0] = new AtkValue { Type = (AtkValueType)3, Int = 0 };
        args[1] = new AtkValue { Type = (AtkValueType)3, Int = 2 };
        args[2] = new AtkValue { Type = (AtkValueType)5, UInt = 0 };
        args[3] = new AtkValue { Type = AtkValueType.Undefined };
        args[4] = new AtkValue { Type = AtkValueType.Undefined };
        var result = new AtkValue();
        agent->ReceiveEvent(&result, args, 5, 7);
        return true;
    }

    private enum ApplyState
    {
        Idle,
        RequestClear,
        WaitClearConfirmation,
        WaitEmpty,
        OpenNotebook,
        WaitNotebook,
        WaitNotebookPage,
        SelectMember,
        WaitMember,
        Completed,
    }

    private void OnAddonReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!DiagnosticsEnabled || !Snapshot.Available || args is not AddonReceiveEventArgs receiveArgs)
        {
            return;
        }

        var addonName = args.AddonName;
        var relevantAddon = addonName.StartsWith("XBM", StringComparison.Ordinal)
            || addonName.Contains("ContextMenu", StringComparison.OrdinalIgnoreCase)
            || addonName.Contains("SelectString", StringComparison.OrdinalIgnoreCase)
            || addonName.Contains("SelectIconString", StringComparison.OrdinalIgnoreCase)
            || addonName.Contains("SelectYesno", StringComparison.OrdinalIgnoreCase);
        var eventName = receiveArgs.AtkEventType.ToString();
        var noisyEvent = eventName is "MouseMove" or "TimerTick" or "TimelineActiveLabelChanged"
            or "MouseOver" or "MouseOut" or "LinkMouseOver" or "LinkMouseOut"
            or "ListItemRollOver" or "ListItemRollOut";
        if (!relevantAddon || noisyEvent)
        {
            return;
        }

        AddDiagnostic($"[{DateTime.Now:HH:mm:ss.fff}] Event Addon={args.AddonName} | AtkEventType={receiveArgs.AtkEventType} | EventParam={receiveArgs.EventParam} | AtkEvent=0x{receiveArgs.AtkEvent:X} | AtkEventData=0x{receiveArgs.AtkEventData:X}");
    }

    private void OnAgentReceiveEvent(AgentEvent type, AgentArgs args)
    {
        if (!DiagnosticsEnabled || args is not AgentReceiveEventArgs receiveArgs)
        {
            return;
        }

        var valueCount = (int)Math.Min(receiveArgs.ValueCount, 20u);
        var values = (AtkValue*)receiveArgs.AtkValues;
        var renderedValues = new List<string>(valueCount);
        for (var index = 0; index < valueCount && values != null; index++)
        {
            renderedValues.Add($"[{index}]={FormatAgentValue(values[index])}");
        }
        var rendered = string.Join(", ", renderedValues);
        var agentId = args.AgentId;
        AddDiagnostic($"[{DateTime.Now:HH:mm:ss.fff}] Agent {agentId} ReceiveEvent | Kind={receiveArgs.EventKind} | Count={receiveArgs.ValueCount} | {rendered}");
    }

    private static string FormatAgentValue(AtkValue value)
        => value.TypeCode() switch
        {
            2 => $"Bool:{value.Bool}",
            3 => $"Int:{value.Int}",
            4 or 5 => $"UInt:{value.UInt}",
            8 or 10 => $"String:\"{value.String.ToString() ?? string.Empty}\"",
            _ => $"Type:{value.Type}",
        };

    private void RecordSnapshotChange(BeastmasterPetPartySnapshot snapshot)
    {
        if (!DiagnosticsEnabled || !snapshot.Available)
        {
            return;
        }

        var current = $"{snapshot.MemberCount}/{snapshot.Capacity}|{string.Join(',', snapshot.Members.Select(member => member.CatalogNumber))}";
        if (current == lastDiagnosticSnapshot)
        {
            return;
        }

        lastDiagnosticSnapshot = current;
        AddDiagnostic($"[{DateTime.Now:HH:mm:ss.fff}] Snapshot {current}");
    }

    private void AddDiagnostic(string entry)
    {
        diagnosticEntries.Add(entry);
        if (diagnosticEntries.Count > MaximumDiagnosticEntries)
        {
            diagnosticEntries.RemoveRange(0, diagnosticEntries.Count - MaximumDiagnosticEntries);
        }
    }

    private static BeastmasterPetPartySnapshot ReadSnapshot()
    {
        var addon = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName(AddonName, 1).Address;
        if (addon == null || !addon->IsVisible || addon->AtkValues == null)
        {
            return BeastmasterPetPartySnapshot.Unavailable("魔兽编队界面未打开");
        }

        if (addon->AtkValuesCount <= CapacityIndex)
        {
            return BeastmasterPetPartySnapshot.Unavailable("魔兽编队字段数量不足");
        }

        var memberCount = ReadNumber(addon->AtkValues[MemberCountIndex]);
        var capacity = ReadNumber(addon->AtkValues[CapacityIndex]);
        if (memberCount > capacity || !BeastmasterPetPartySnapshot.SupportedCapacities.Contains((int)capacity))
        {
            return BeastmasterPetPartySnapshot.Unavailable("当前编队栏位数量不是支持的 10、12、14 或 15");
        }

        var members = new List<BeastmasterPetPartyMember>((int)memberCount);
        for (var position = 0; position < memberCount; position++)
        {
            var start = FirstMemberIndex + position * MemberStride;
            if (start + MemberStride > addon->AtkValuesCount)
            {
                return BeastmasterPetPartySnapshot.Unavailable("成员字段超出编队数据范围");
            }

            var icon = ReadNumber(addon->AtkValues[start + 1]);
            var number = ReadNumber(addon->AtkValues[start + 76]);
            var name = ReadText(addon->AtkValues[start + 3]);
            if (number is < 1 or > 50 || icon != IconBase + number)
            {
                return BeastmasterPetPartySnapshot.Unavailable($"第 {position + 1} 位成员结构无效");
            }

            members.Add(new BeastmasterPetPartyMember(position + 1, (int)number, name));
        }

        return new BeastmasterPetPartySnapshot(true, (int)memberCount, (int)capacity, members, "魔兽编队已读取");
    }

    private static uint ReadNumber(AtkValue value)
        => value.TypeCode() switch
        {
            3 when value.Int >= 0 => (uint)value.Int,
            4 or 5 => value.UInt,
            _ => uint.MaxValue,
        };

    private static string ReadText(AtkValue value)
        => value.TypeCode() is 8 or 10 ? value.String.ToString() ?? string.Empty : string.Empty;
}
