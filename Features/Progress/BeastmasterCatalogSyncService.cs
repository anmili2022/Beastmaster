using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Beastmaster;

public sealed unsafe class BeastmasterCatalogSyncService
{
    private const string AddonName = "XBMMonsterNotebook";
    private const int PageCount = 2;
    private const int EntriesPerPage = 25;
    private const int MaxAtkValues = 4096;
    private const int PageValueIndex = 10;
    private const int TotalValueIndex = 7;
    private const int FirstEntryValueIndex = 24;
    private const int EntryStride = 8;
    private const uint CapturedIconBase = 242000;
    private const uint MissingIcon = 242051;

    private readonly BeastmasterProgressService progressService;
    private readonly Dictionary<int, bool> states = new();
    private int requestedPage = -1;
    private int pageRequestAttempts;
    private long nextActionAt;
    private long scanStartedAt;
    private bool scanning;
    private string status = "尚未同步。";
    private int? expectedCapturedTotal;

    public BeastmasterCatalogSyncService(BeastmasterProgressService progressService)
    {
        this.progressService = progressService;
    }

    public string Status => status;
    public bool IsScanning => scanning;
    public string Diagnostic { get; private set; } = string.Empty;

    public void RequestSync()
    {
        if (progressService.CurrentCharacterKey.Length == 0)
        {
            status = "请先登录角色。";
            return;
        }

        states.Clear();
        requestedPage = -1;
        pageRequestAttempts = 0;
        nextActionAt = 0;
        scanStartedAt = Environment.TickCount64;
        expectedCapturedTotal = null;
        Diagnostic = string.Empty;
        scanning = true;
        status = "正在读取当前角色的魔兽图鉴…";
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
                Stop("角色已登出，同步已取消。", clearStates: true);
                return;
            }

            var addon = GetAddon();
            if (addon == null)
            {
                if (Environment.TickCount64 >= nextActionAt)
                {
                    OpenNotebook();
                    nextActionAt = Environment.TickCount64 + 500;
                }

                return;
            }

            var page = ReadPage(addon);
            if (expectedCapturedTotal is null)
            {
                expectedCapturedTotal = page.CapturedTotal;
            }
            else if (expectedCapturedTotal != page.CapturedTotal)
            {
                throw new InvalidOperationException("图鉴两页的已捕获总数不一致。");
            }

            foreach (var entry in page.Entries)
            {
                states[entry.Number] = entry.Captured;
            }

            if (states.Count == BeastmasterCatalog.Entries.Count)
            {
                var unlocked = states.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet();
                if (expectedCapturedTotal != unlocked.Count)
                {
                    throw new InvalidOperationException("图鉴状态与已捕获总数不一致。");
                }

                var changed = progressService.ReplaceCatalogProgress(unlocked);
                Stop($"同步完成：已解锁 {unlocked.Count}/{BeastmasterCatalog.Entries.Count}，更新 {changed} 项。", clearStates: false);
                return;
            }

            var currentPageValue = GetPage(addon);
            if (currentPageValue is not (0 or 1))
            {
                status = "等待图鉴页码数据刷新…";
                return;
            }

            var currentPage = currentPageValue.Value;

            if (requestedPage >= 0)
            {
                if (currentPage == requestedPage)
                {
                    requestedPage = -1;
                    pageRequestAttempts = 0;
                }
                else if (Environment.TickCount64 >= nextActionAt)
                {
                    if (pageRequestAttempts >= 3)
                    {
                        throw new InvalidOperationException("图鉴翻页未响应。");
                    }

                    pageRequestAttempts++;
                    nextActionAt = Environment.TickCount64 + 500;
                    if (!RequestPage(addon, (uint)requestedPage))
                    {
                        status = $"图鉴翻页未接受，正在重试 ({pageRequestAttempts}/3)…";
                        return;
                    }

                    status = $"正在等待第 {requestedPage + 1} 页刷新…";
                    return;
                }
            }

            if (Environment.TickCount64 < nextActionAt)
            {
                return;
            }

            requestedPage = currentPage == 0 ? 1 : 0;
            pageRequestAttempts = 1;
            nextActionAt = Environment.TickCount64 + 500;
            if (!RequestPage(addon, (uint)requestedPage))
            {
                status = "图鉴翻页未接受，准备重试…";
                return;
            }

            status = $"正在等待第 {requestedPage + 1} 页刷新…";
        }
        catch (InvalidOperationException ex) when (Environment.TickCount64 - scanStartedAt < 15000)
        {
            Diagnostic = ex.Message;
            nextActionAt = Environment.TickCount64 + 250;
            status = $"等待图鉴数据刷新…{ex.Message}";
        }
        catch (Exception ex)
        {
            Diagnostic = ex.Message;
            Stop($"同步失败，未修改进度：{ex.Message}", clearStates: true);
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

    private static unsafe CatalogPage ReadPage(AtkUnitBase* addon)
    {
        if (addon->AtkValues == null || addon->AtkValuesCount > MaxAtkValues || addon->AtkValuesCount <= FirstEntryValueIndex + (EntriesPerPage - 1) * EntryStride + 5)
        {
            throw new InvalidOperationException("图鉴字段数量未通过校验。");
        }

        var page = GetPage(addon) ?? throw new InvalidOperationException("图鉴页码未刷新。");
        var totalText = ReadString(addon->AtkValues[TotalValueIndex]);
        var parts = totalText.Split('/');
        if (parts.Length != 2 || parts[1] != "50" || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var capturedTotal))
        {
            throw new InvalidOperationException("图鉴总数未通过校验。");
        }

        var result = new List<(int Number, bool Captured)>(EntriesPerPage);
        for (var index = 0; index < EntriesPerPage; index++)
        {
            var valueIndex = FirstEntryValueIndex + index * EntryStride;
            var numberValue = addon->AtkValues[valueIndex];
            var capturedValue = addon->AtkValues[valueIndex + 2];
            var iconValue = addon->AtkValues[valueIndex + 4];
            var textValue = addon->AtkValues[valueIndex + 5];
            var number = 1 + page * EntriesPerPage + index;
            var captured = capturedValue.TypeCode() == 2 && capturedValue.Bool;

            var expectedIcon = captured ? CapturedIconBase + (uint)number : MissingIcon;
            var expectedText = number.ToString(CultureInfo.InvariantCulture);
            if (numberValue.TypeCode() != 5 || numberValue.UInt != number
                || ReadString(textValue) != expectedText
                || capturedValue.TypeCode() != 2
                || iconValue.TypeCode() != 5
                || iconValue.UInt != expectedIcon)
            {
                throw new InvalidOperationException(
                    $"图鉴第 {number:00} 项结构未通过校验。\n"
                    + $"页码={page}，AtkValuesCount={addon->AtkValuesCount}\n"
                    + $"编号字段：{FormatValue(numberValue)}，期望 TypeCode=5 UInt={number}\n"
                    + $"捕获字段：{FormatValue(capturedValue)}，期望 TypeCode=2 Bool={captured}\n"
                    + $"图标字段：{FormatValue(iconValue)}，期望 TypeCode=5 UInt={expectedIcon}\n"
                    + $"文本字段：{FormatValue(textValue)}，期望文本={expectedText}");
            }

            result.Add((number, captured));
        }

        var pageCaptured = result.Count(entry => entry.Captured);
        if (pageCaptured > capturedTotal || pageCaptured > EntriesPerPage)
        {
            throw new InvalidOperationException("图鉴收集数量未通过校验。");
        }

        return new CatalogPage(result, capturedTotal);
    }

    private unsafe void OpenNotebook()
    {
        var module = AgentModule.Instance();
        if (module == null)
        {
            throw new InvalidOperationException("游戏界面尚未加载。");
        }

        var agent = module->GetAgentByInternalId((AgentId)500);
        if (agent == null)
        {
            throw new InvalidOperationException("魔兽图鉴界面不可用。");
        }

        agent->Show();
    }

    private static unsafe bool RequestPage(AtkUnitBase* addon, uint page)
    {
        var values = stackalloc AtkValue[2];

        var callback = new AtkValue();
        callback.Type = (AtkValueType)3;
        callback.Int = 3;
        Unsafe.Write(values, callback);

        var pageValue = new AtkValue();
        pageValue.Type = (AtkValueType)5;
        pageValue.UInt = page;
        Unsafe.Write(values + 1, pageValue);

        return addon->FireCallback(2, values, true);
    }

    private static unsafe string ReadString(AtkValue value)
    {
        var type = value.TypeCode();
        if (type is not (8 or 10))
        {
            return string.Empty;
        }

        return value.String.ToString() ?? string.Empty;
    }

    private static unsafe string FormatValue(AtkValue value)
    {
        var text = ReadString(value).Replace("\r", "\\r").Replace("\n", "\\n");
        return $"Type=0x{(int)value.Type:X}, TypeCode={value.TypeCode()}, UInt={value.UInt}, Int={value.Int}, Bool={value.Bool}, String=\"{text}\"";
    }

    private void Stop(string message, bool clearStates)
    {
        scanning = false;
        requestedPage = -1;
        pageRequestAttempts = 0;
        scanStartedAt = 0;
        if (clearStates)
        {
            states.Clear();
        }

        status = message;
    }

    private sealed record CatalogPage(IReadOnlyList<(int Number, bool Captured)> Entries, int CapturedTotal);
}

internal static class BeastmasterAtkValueExtensions
{
    public static unsafe int TypeCode(this AtkValue value) => (int)value.Type & 0xF;
}
