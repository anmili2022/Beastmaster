using Dalamud.Bindings.ImGui;
using System.Diagnostics;
using System.Numerics;

namespace Beastmaster;

public sealed class PluginUI
{
    private static readonly (string Key, string Label)[] MainSections =
    [
        ("quests", "驯兽师任务链"),
        ("catalog", "魔兽图鉴"),
        ("commands", "快捷指令"),
        ("settings", "设置"),
        ("debug", "DEBUG"),
    ];

    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterProgressService progressService;
    private readonly BeastmasterQuestService questService;
    private readonly BeastmasterNavigationService navigationService;
    private readonly BeastmasterDebugDataService debugDataService;
    private string debugQuery = "驯兽";
    private string debugResult = "点击按钮读取客户端资料。";
    private DateTime nextQuestStatusRefreshUtc = DateTime.MinValue;
    private bool isMainWindowOpen;

    public PluginUI(
        BeastmasterConfiguration configuration,
        BeastmasterProgressService progressService,
        BeastmasterQuestService questService,
        BeastmasterNavigationService navigationService,
        BeastmasterDebugDataService debugDataService)
    {
        this.configuration = configuration;
        this.progressService = progressService;
        this.questService = questService;
        this.navigationService = navigationService;
        this.debugDataService = debugDataService;
    }

    public void OpenMainWindow()
    {
        isMainWindowOpen = true;
    }

    public void Draw()
    {
        if (!isMainWindowOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(900f, 600f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(700f, 460f), new Vector2(float.MaxValue, float.MaxValue));
        if (!ImGui.Begin($"驯兽师助手 v{GetType().Assembly.GetName().Version}", ref isMainWindowOpen))
        {
            ImGui.End();
            return;
        }

        DrawMainShell();
        ImGui.End();
    }

    private void DrawMainShell()
    {
        if (!ImGui.BeginTable(
                "BeastmasterMainShell",
                2,
                ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("导航", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableSetupColumn("内容", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawSidebar();
        ImGui.TableNextColumn();
        DrawContent();
        ImGui.EndTable();
    }

    private void DrawSidebar()
    {
        ImGui.Text("驯兽师助手");
        ImGui.TextDisabled("Beastmaster Progress Hub");
        ImGui.Separator();

        DrawSidebarLabel("内容");
        DrawSidebarButton(MainSections[0]);
        DrawSidebarButton(MainSections[1]);
        DrawSidebarButton(MainSections[2]);

        ImGui.Separator();
        DrawSidebarLabel("工具");
        DrawSidebarButton(MainSections[3]);
        DrawSidebarButton(MainSections[4]);
    }

    private void DrawSidebarButton((string Key, string Label) section)
    {
        var selected = configuration.SelectedMainSection == section.Key;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.23f, 0.25f, 1f));
        }

        if (ImGui.Button($"{section.Label}##section-{section.Key}", new Vector2(ImGui.GetContentRegionAvail().X, 34f)))
        {
            configuration.SelectedMainSection = section.Key;
            configuration.Save();
        }

        if (selected)
        {
            ImGui.PopStyleColor();
        }
    }

    private static void DrawSidebarLabel(string label)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(label);
    }

    private void DrawContent()
    {
        switch (configuration.SelectedMainSection)
        {
            case "quests":
                DrawQuests();
                break;
            case "catalog":
                DrawCatalog();
                break;
            case "commands":
                DrawCommands();
                break;
            case "settings":
                DrawSettings();
                break;
            case "debug":
                DrawDebug();
                break;
            default:
                configuration.SelectedMainSection = "quests";
                DrawQuests();
                break;
        }
    }

    private void DrawQuests()
    {
        ImGui.Text("驯兽师任务链");
        ImGui.SameLine();
        var stopButtonWidth = ImGui.CalcTextSize("停止导航").X + ImGui.GetStyle().FramePadding.X * 2f;
        var stopButtonX = ImGui.GetWindowContentRegionMax().X - stopButtonWidth;
        if (ImGui.GetCursorPosX() < stopButtonX)
        {
            ImGui.SetCursorPosX(stopButtonX);
        }

        if (ImGui.Button("停止导航"))
        {
            navigationService.Stop();
        }

        ImGui.TextDisabled("按客户端任务状态显示进度，并前往未完成任务的接取位置。");
        ImGui.Separator();

        var quests = BeastmasterQuestGuide.Quests;
        var identifiedQuests = quests.Where(quest => quest.RowId != 0).ToArray();
        var statusRowIds = identifiedQuests
            .SelectMany(quest => questService.GetPrerequisites(quest.RowId).Select(previous => previous.RowId).Append(quest.RowId))
            .Distinct()
            .ToArray();
        if (DateTime.UtcNow >= nextQuestStatusRefreshUtc)
        {
            questService.RefreshStatuses(statusRowIds);
            nextQuestStatusRefreshUtc = DateTime.UtcNow.AddSeconds(2);
        }

        var completedCount = identifiedQuests.Count(quest => questService.GetStatus(quest.RowId) == BeastmasterQuestStatus.Completed);
        ImGui.ProgressBar((float)completedCount / quests.Count, new Vector2(-1f, 0f), $"{completedCount}/{quests.Count}");
        ImGui.Spacing();

        foreach (var quest in quests)
        {
            if (quest.RowId == 0)
            {
                ImGui.Text($"{quest.Name}  [待采集]");
                ImGui.TextDisabled(quest.Summary);
                ImGui.BeginDisabled();
                ImGui.Button("导航到开始 NPC##issuer-pending");
                ImGui.SameLine();
                ImGui.Button("导航到任务目标##target-pending");
                ImGui.EndDisabled();
                ImGui.Separator();
                continue;
            }

            var questStatus = questService.GetStatus(quest.RowId);
            var completed = questStatus == BeastmasterQuestStatus.Completed;
            if (completed && configuration.HideCompletedQuests)
            {
                continue;
            }

            var status = completed
                ? "已完成"
                : questStatus == BeastmasterQuestStatus.Accepted
                    ? "进行中"
                    : "未接取";
            ImGui.Text($"{quest.Name}  [{status}]");

            foreach (var prerequisite in questService.GetPrerequisites(quest.RowId))
            {
                var prerequisiteComplete = questService.GetStatus(prerequisite.RowId) == BeastmasterQuestStatus.Completed;
                ImGui.TextDisabled($"前置任务：{prerequisite.Name}");
                ImGui.SameLine();
                ImGui.TextColored(
                    prerequisiteComplete
                        ? new Vector4(0.35f, 0.8f, 0.48f, 1f)
                        : new Vector4(0.9f, 0.32f, 0.3f, 1f),
                    prerequisiteComplete ? "[已完成]" : "[未完成]");
            }

            if (questService.TryGetIssuerLocation(quest.RowId, out var issuer))
            {
                ImGui.TextDisabled($"开始 NPC：{issuer.NpcName} · {issuer.Zone}");
                if (completed)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button($"导航到开始 NPC##issuer-{quest.RowId}"))
                {
                    navigationService.Navigate(issuer);
                }

                if (completed)
                {
                    ImGui.EndDisabled();
                }
            }
            else
            {
                ImGui.TextDisabled("开始 NPC：客户端未提供有效接取坐标");
            }

            ImGui.SameLine();
            BeastmasterQuestLocation? target = null;
            var hasTarget = questStatus == BeastmasterQuestStatus.Accepted
                && questService.TryGetCurrentTarget(quest.RowId, out target);
            if (!hasTarget)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button($"导航到任务目标##target-{quest.RowId}") && hasTarget)
            {
                navigationService.Navigate(target!);
            }

            if (!hasTarget)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("当前任务序列的目标坐标尚未采集。 ");
                }
            }

            ImGui.Separator();
        }

    }

    private static void DrawReserved(string title)
    {
        ImGui.Text(title);
        ImGui.Separator();
        ImGui.TextDisabled("此项目待定。");
    }

    private static void DrawCommands()
    {
        ImGui.Text("快捷指令");
        ImGui.Separator();
        if (ImGui.Button("魔兽图鉴"))
        {
            if (!GameCommandService.Execute("/魔兽图鉴"))
            {
                DalamudApi.ChatGui.Print("[驯兽师助手] 无法执行 /魔兽图鉴。");
            }
        }
    }

    private void DrawCatalog()
    {
        ImGui.Text("魔兽图鉴");
        ImGui.SameLine();
        if (ImGui.Button("WIKI"))
        {
            Process.Start(new ProcessStartInfo("https://ff14.huijiwiki.com/wiki/%E9%AD%94%E5%85%BD%E5%9B%BE%E9%89%B4")
            {
                UseShellExecute = true,
            });
        }

        ImGui.SameLine();
        var stopButtonWidth = ImGui.CalcTextSize("停止导航").X + ImGui.GetStyle().FramePadding.X * 2f;
        var stopButtonX = ImGui.GetWindowContentRegionMax().X - stopButtonWidth;
        if (ImGui.GetCursorPosX() < stopButtonX)
        {
            ImGui.SetCursorPosX(stopButtonX);
        }

        if (ImGui.Button("停止导航##catalog-stop-navigation"))
        {
            navigationService.Stop();
        }

        ImGui.TextDisabled("捕获成功时自动记录，也可按当前角色手动修改完成状态。");
        ImGui.Separator();

        var entries = BeastmasterCatalog.Entries;
        var sortByLocation = configuration.SortCatalogByLocation;
        var hideCaptured = configuration.HideCapturedBeasts;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("按地图排序", ref sortByLocation))
        {
            configuration.SortCatalogByLocation = sortByLocation;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("隐藏已捕获魔兽", ref hideCaptured))
        {
            configuration.HideCapturedBeasts = hideCaptured;
            configuration.Save();
        }
        ImGui.PopStyleColor();

        var displayedEntries = entries.AsEnumerable();
        if (configuration.HideCapturedBeasts)
        {
            displayedEntries = displayedEntries.Where(e => !progressService.IsCompleted(e.Key));
        }

        var currentTerritory = DalamudApi.ClientState.TerritoryType;
        var completedCount = entries.Count(entry => progressService.IsCompleted(entry.Key));
        ImGui.ProgressBar((float)completedCount / entries.Count, new Vector2(-1f, 0f), $"{completedCount}/{entries.Count}");

        ImGui.Spacing();

        if (!ImGui.BeginTable(
                "BeastmasterCatalogTable",
                6,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0f, 0f)))
        {
            return;
        }

        ImGui.TableSetupColumn("完成", ImGuiTableColumnFlags.WidthFixed, 46f);
        ImGui.TableSetupColumn("编号 / 魔兽", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn("区域 / 副本", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("坐标", ImGuiTableColumnFlags.WidthFixed, 112f);
        ImGui.TableSetupColumn("导航", ImGuiTableColumnFlags.WidthFixed, 62f);
        ImGui.TableHeadersRow();

        var canEdit = progressService.CurrentCharacterKey.Length > 0;
        IEnumerable<BeastmasterCatalogEntry> sortedEntries;
        if (configuration.SortCatalogByLocation)
        {
            sortedEntries = displayedEntries
                .OrderBy(entry => entry.TerritoryType != currentTerritory)
                .ThenBy(entry => entry.Location, StringComparer.Ordinal)
                .ThenBy(entry => entry.Number);
        }
        else
        {
            sortedEntries = displayedEntries.OrderBy(entry => entry.Number);
        }
        foreach (var entry in sortedEntries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var completed = progressService.IsCompleted(entry.Key);
            if (!canEdit)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Checkbox($"##catalog-complete-{entry.Number}", ref completed))
            {
                progressService.SetCompleted(entry.Key, completed);
            }

            if (!canEdit)
            {
                ImGui.EndDisabled();
            }

            ImGui.TableNextColumn();
            ImGui.Text($"{entry.Number}. {entry.Name}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Level);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Location);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetCatalogCoordinate(entry));
            ImGui.TableNextColumn();
            if (entry.LocationType == BeastmasterCatalogLocationType.Field
                && entry.MapX.HasValue
                && entry.MapY.HasValue
                && ImGui.SmallButton($"导航##catalog-nav-{entry.Number}"))
            {
                navigationService.Navigate(entry);
            }
        }

        ImGui.EndTable();
    }

    private static string GetCatalogCoordinate(BeastmasterCatalogEntry entry)
        => entry.LocationType switch
        {
            BeastmasterCatalogLocationType.Field when entry.MapX.HasValue && entry.MapY.HasValue
                => $"X:{entry.MapX:0.#}, Y:{entry.MapY:0.#}",
            BeastmasterCatalogLocationType.Duty => "副本",
            BeastmasterCatalogLocationType.Starting => "初始",
            _ => "未知",
        };

    private void DrawSettings()
    {
        ImGui.Text("设置");
        ImGui.Separator();

        ImGui.Text("依赖插件");
        DrawDependency("vnavmesh", navigationService.IsVnavmeshInstalled, "同地图路径规划与移动");
        DrawDependency("Lifestream", navigationService.IsLifestreamInstalled, "跨地图传送，当前版本尚未接入自动传送");
        ImGui.Spacing();

        ImGui.Text("常用设置");
        ImGui.TextDisabled($"当前角色：{progressService.CurrentCharacterLabel}");
        DrawSettingCheckbox("隐藏已完成任务", "任务页只显示未完成的驯兽师任务。", nameof(configuration.HideCompletedQuests), configuration.HideCompletedQuests);
        DrawSettingCheckbox("捕获消息自动记录", "收到成功结识消息时自动标记图鉴完成。", nameof(configuration.AutoCompleteCatalogFromChat), configuration.AutoCompleteCatalogFromChat);
        ImGui.Spacing();

        ImGui.Text("导航设置");
        DrawSettingCheckbox("飞行导航", "允许 vnavmesh 使用飞行路径。", nameof(configuration.UseFlightNavigation), configuration.UseFlightNavigation);
        DrawSettingCheckbox("设置地图标记", "点击导航时同步设置游戏地图 Flag。", nameof(configuration.SetFlagOnNavigation), configuration.SetFlagOnNavigation);
        DrawSettingCheckbox("显示导航日志", "在聊天栏显示导航开始和失败信息。", nameof(configuration.ShowNavigationLogs), configuration.ShowNavigationLogs);
    }

    private static void DrawDependency(string name, bool available, string purpose)
    {
        ImGui.TextColored(available ? new Vector4(0.35f, 0.8f, 0.48f, 1f) : new Vector4(0.9f, 0.42f, 0.38f, 1f), available ? "可用" : "未加载");
        ImGui.SameLine();
        ImGui.Text(name);
        ImGui.SameLine();
        ImGui.TextDisabled(purpose);
    }

    private void DrawSettingCheckbox(string label, string description, string key, bool value)
    {
        if (ImGui.Checkbox($"{label}##{key}", ref value))
        {
            switch (key)
            {
                case nameof(configuration.HideCompletedQuests):
                    configuration.HideCompletedQuests = value;
                    break;
                case nameof(configuration.AutoCompleteCatalogFromChat):
                    configuration.AutoCompleteCatalogFromChat = value;
                    break;
                case nameof(configuration.UseFlightNavigation):
                    configuration.UseFlightNavigation = value;
                    break;
                case nameof(configuration.SetFlagOnNavigation):
                    configuration.SetFlagOnNavigation = value;
                    break;
                case nameof(configuration.ShowNavigationLogs):
                    configuration.ShowNavigationLogs = value;
                    break;
            }

            configuration.Save();
        }

        ImGui.TextDisabled(description);
    }

    private void DrawDebug()
    {
        ImGui.Text("DEBUG");
        ImGui.TextDisabled("读取国服客户端名称、稳定标识和任务原始资料。");
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##DebugQuery", ref debugQuery, 128);

        DrawDebugSearchButtons();

        if (ImGui.Button("读取任务：驯养魔兽之人"))
        {
            debugResult = debugDataService.FindQuests("驯养魔兽之人");
        }

        ImGui.SameLine();
        if (ImGui.Button("采集当前所有任务状态"))
        {
            debugResult = questService.GetActiveQuestsDebug();
        }

        if (ImGui.Button("读取驯兽师任务链"))
        {
            debugResult = debugDataService.FindBeastmasterQuestChain();
        }

        if (ImGui.Button("读取当前角色"))
        {
            debugResult = debugDataService.GetCharacter();
        }

        ImGui.SameLine();
        if (ImGui.Button("读取当前位置"))
        {
            debugResult = debugDataService.GetLocation();
        }

        ImGui.SameLine();
        if (ImGui.Button("复制结果"))
        {
            ImGui.SetClipboardText(debugResult);
        }

        ImGui.Separator();
        if (ImGui.BeginChild("DebugResult", Vector2.Zero, true))
        {
            ImGui.TextUnformatted(debugResult);
        }

        ImGui.EndChild();
    }

    private void DrawDebugSearchButtons()
    {
        if (ImGui.Button("读取职业")) debugResult = debugDataService.FindClassJobs(debugQuery);
        ImGui.SameLine();
        if (ImGui.Button("读取任务")) debugResult = debugDataService.FindQuests(debugQuery);
        ImGui.SameLine();
        if (ImGui.Button("读取物品")) debugResult = debugDataService.FindItems(debugQuery);
        ImGui.SameLine();
        if (ImGui.Button("读取 NPC")) debugResult = debugDataService.FindNpcs(debugQuery);
        ImGui.SameLine();
        if (ImGui.Button("读取怪物")) debugResult = debugDataService.FindMonsters(debugQuery);
        ImGui.SameLine();
        if (ImGui.Button("读取副本")) debugResult = debugDataService.FindDuties(debugQuery);
    }
}
