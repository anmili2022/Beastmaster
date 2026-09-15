using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Beastmaster;

public sealed unsafe class BeastmasterNotebookSyncService
{
    private const string AddonName = "XBMMonsterNotebook";
    private const int PageValueIndex = 10;
    private const int NumberTextIndex = 229;
    private const int IconIndex = 231;
    private const int LevelIndex = 258;
    private const int ExperienceIndex = 261;
    private const int ExperienceRequiredIndex = 262;
    private const int EntriesPerPage = 25;
    private const int PageCount = 2;
    private const uint IconBase = 242000;
    private const long TimeoutMs = 20000;

    private readonly BeastmasterProgressService progressService;
    private readonly Dictionary<int, (int Level, int Experience, int ExperienceRequired)> results = new();
    private bool scanning;
    private long scanStartedAt;
    private long nextActionAt;
    private int memberIndex;
    private bool selectSentForCurrent;
    private string pendingSnapshot = string.Empty;
    private string stableSnapshot = string.Empty;
    private int invalidLevelFrames;
    private string status = "尚未同步。";

    public BeastmasterNotebookSyncService(BeastmasterProgressService progressService)
    {
        this.progressService = progressService;
    }

    public string Status => status;
    public bool IsScanning => scanning;
    public string Diagnostic { get; private set; } = string.Empty;
    public int ProgressCount => results.Count;
    public int TotalCount => EntriesPerPage * PageCount;

    public void RequestSync()
    {
        if (progressService.CurrentCharacterKey.Length == 0)
        {
            status = "请先登录角色。";
            return;
        }

        results.Clear();
        memberIndex = 0;
        selectSentForCurrent = false;
        pendingSnapshot = string.Empty;
        stableSnapshot = string.Empty;
        invalidLevelFrames = 0;
        scanStartedAt = Environment.TickCount64;
        nextActionAt = 0;
        Diagnostic = string.Empty;
        scanning = true;
        status = "请与劳妲对话打开原生魔兽图鉴后开始同步…";
    }

    public void Update()
    {
        if (!scanning)
        {
            return;
        }

        try
        {
            if (progressService.CurrentCharacterKey.Length == 0)
            {
                Stop("角色已登出，同步已取消。");
                return;
            }

            if (Environment.TickCount64 - scanStartedAt >= TimeoutMs)
            {
                throw new InvalidOperationException($"同步超时：{DescribeStuckState()}");
            }

            var addon = GetAddon();
            if (addon == null)
            {
                status = "请与劳妲对话打开原生魔兽图鉴后再同步。";
                return;
            }

            var page = GetPage(addon);
            if (page == null)
            {
                status = "等待图鉴页码刷新…";
                return;
            }

            if (memberIndex >= EntriesPerPage * PageCount)
            {
                Finish();
                return;
            }

            var targetPage = memberIndex / EntriesPerPage;
            if (page != targetPage)
            {
                selectSentForCurrent = false;
                pendingSnapshot = string.Empty;
                stableSnapshot = string.Empty;
                if (Environment.TickCount64 >= nextActionAt)
                {
                    if (RequestPage(addon, (uint)targetPage))
                    {
                        status = $"正在翻到第 {targetPage + 1} 页…";
                    }
                    else
                    {
                        status = "图鉴翻页未接受，重试中…";
                    }

                    nextActionAt = Environment.TickCount64 + 200;
                }

                return;
            }

            var pageIndex = memberIndex % EntriesPerPage;
            if (!selectSentForCurrent && Environment.TickCount64 >= nextActionAt)
            {
                if (!SendSelectEvent((uint)pageIndex))
                {
                    throw new InvalidOperationException("图鉴选中事件发送失败。");
                }

                selectSentForCurrent = true;
                nextActionAt = Environment.TickCount64 + 100;
            }

            var detail = ReadDetail(addon, memberIndex + 1);
            if (detail == null)
            {
                return;
            }

            results[memberIndex + 1] = detail.Value;
            memberIndex++;
            selectSentForCurrent = false;
            pendingSnapshot = string.Empty;
            stableSnapshot = string.Empty;
            nextActionAt = 0;
            status = $"已读取 {memberIndex}/{EntriesPerPage * PageCount} 只…";
        }
        catch (Exception ex)
        {
            Diagnostic = ex.Message;
            Stop($"同步失败：{ex.Message}");
            DalamudApi.ChatGui.Print($"[驯兽师助手] 同步兽级经验失败：{ex.Message}");
        }
    }

    public void Start()
    {
        DalamudApi.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Update();
    }

    private unsafe AtkUnitBase* GetAddon()
    {
        var address = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName(AddonName, 1).Address;
        return address != null && address->IsVisible ? address : null;
    }

    private static unsafe int? GetPage(AtkUnitBase* addon)
    {
        if (addon == null || addon->AtkValues == null || addon->AtkValuesCount <= PageValueIndex)
        {
            return null;
        }

        var value = addon->AtkValues[PageValueIndex];
        return ((int)value.Type & 0xF) == 5 && value.UInt <= 1 ? (int)value.UInt : null;
    }

    private static unsafe bool RequestPage(AtkUnitBase* addon, uint page)
    {
        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = (AtkValueType)3, Int = 3 };
        values[1] = new AtkValue { Type = (AtkValueType)5, UInt = page };
        return addon->FireCallback(2, values, true);
    }

    private static unsafe bool SendSelectEvent(uint pageIndex)
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId((AgentId)500);
        if (agent == null)
        {
            return false;
        }

        var args = stackalloc AtkValue[2];
        args[0] = new AtkValue { Type = (AtkValueType)3, Int = 5 };
        args[1] = new AtkValue { Type = (AtkValueType)3, Int = (int)pageIndex };
        var result = new AtkValue();
        agent->ReceiveEvent(&result, args, 2, 0);
        return true;
    }

    private unsafe (int Level, int Experience, int ExperienceRequired)? ReadDetail(AtkUnitBase* addon, int expectedNumber)
    {
        if (addon->AtkValues == null || addon->AtkValuesCount <= ExperienceRequiredIndex)
        {
            return null;
        }

        var number = ReadNumber(addon->AtkValues[NumberTextIndex]);
        var icon = ReadNumber(addon->AtkValues[IconIndex]);
        if (number != expectedNumber || icon != IconBase + (uint)number)
        {
            pendingSnapshot = string.Empty;
            stableSnapshot = string.Empty;
            return null;
        }

        var level = ReadNumber(addon->AtkValues[LevelIndex]);
        var experience = ReadNumber(addon->AtkValues[ExperienceIndex]);
        var required = ReadNumber(addon->AtkValues[ExperienceRequiredIndex]);
        if (level is < 1 or > 25)
        {
            pendingSnapshot = string.Empty;
            stableSnapshot = string.Empty;
            invalidLevelFrames++;
            if (invalidLevelFrames >= 40)
            {
                throw new InvalidOperationException("当前打开的不是原生魔兽图鉴（兽级字段无效），请与劳妲对话打开原生魔兽图鉴后再同步。");
            }

            return null;
        }

        invalidLevelFrames = 0;

        if (level == 25)
        {
            experience = 0;
            required = 0;
        }
        else if (required is < 1 or > 999999 || experience >= required)
        {
            pendingSnapshot = string.Empty;
            stableSnapshot = string.Empty;
            return null;
        }

        var snapshot = $"{number}:{level}:{experience}:{required}";
        if (snapshot != pendingSnapshot)
        {
            pendingSnapshot = snapshot;
            stableSnapshot = string.Empty;
            return null;
        }

        if (snapshot == stableSnapshot)
        {
            return null;
        }

        stableSnapshot = snapshot;
        return ((int)level, (int)experience, (int)required);
    }

    private void Finish()
    {
        var changed = progressService.UpdateBeastProgress(results);
        Stop($"同步完成：已读取 {results.Count}/{EntriesPerPage * PageCount} 只，更新 {changed} 项。");
        DalamudApi.ChatGui.Print($"[驯兽师助手] 同步兽级经验完成：已读取 {results.Count}/{EntriesPerPage * PageCount} 只，更新 {changed} 项。");
    }

    private unsafe string DescribeStuckState()
    {
        var addon = GetAddon();
        if (addon == null)
        {
            return $"图鉴未打开（已读取 {memberIndex}/50 只）。";
        }

        var page = GetPage(addon);
        var targetPage = memberIndex / EntriesPerPage;
        var number = addon->AtkValues != null && addon->AtkValuesCount > NumberTextIndex
            ? ReadNumber(addon->AtkValues[NumberTextIndex])
            : uint.MaxValue;
        var level = addon->AtkValues != null && addon->AtkValuesCount > LevelIndex
            ? ReadNumber(addon->AtkValues[LevelIndex])
            : uint.MaxValue;
        return $"已读取 {memberIndex}/50 只，当前页={page?.ToString() ?? "?"}，目标第 {memberIndex + 1} 只（目标页 {targetPage}），详情编号={number}，兽级={level}。";
    }

    private void Stop(string message)
    {
        scanning = false;
        scanStartedAt = 0;
        status = message;
    }

    private static uint ReadNumber(AtkValue value)
        => value.TypeCode() switch
        {
            3 when value.Int >= 0 => (uint)value.Int,
            4 or 5 => value.UInt,
            8 or 10 when uint.TryParse(value.String.ToString(), out var parsed) => parsed,
            _ => uint.MaxValue,
        };
}
