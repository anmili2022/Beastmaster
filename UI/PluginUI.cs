using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using Lumina.Excel.Sheets;
using System.Diagnostics;
using System.Numerics;

namespace Beastmaster;

public sealed class PluginUI
{
    private static readonly (string Key, string Label)[] MainSections =
    [
        ("quests", "驯兽师任务链"),
        ("catalog", "魔兽图鉴"),
        ("equipment", "推荐装备"),
        ("commands", "快捷指令"),
        ("auto-output", "自动输出"),
        ("settings", "设置"),
        ("debug", "DEBUG"),
    ];

    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterProgressService progressService;
    private readonly BeastmasterQuestService questService;
    private readonly BeastmasterNavigationService navigationService;
    private readonly BeastmasterDebugDataService debugDataService;
    private readonly BeastmasterAutoCaptureService autoCaptureService;
    private readonly BeastmasterCatalogSyncService catalogSyncService;
    private string debugQuery = "驯兽";
    private string debugActionId = "44890";
    private string debugResult = "点击按钮读取客户端资料。";
    private bool debugUseAdjustedActionId = true;
    private int debugSearchType;
    private int debugProjectDataType;
    private int debugCurrentStateType;
    private DateTime nextQuestStatusRefreshUtc = DateTime.MinValue;
    private DateTime nextGaugeRefreshUtc = DateTime.MinValue;
    private BeastmasterGaugeSnapshot gaugeSnapshot = BeastmasterGaugeSnapshot.Unavailable("等待读取");
    private bool autoOutputCollapsed;
    private int selectedEquipmentSet;
    private DateTime nextEquipmentRefreshUtc = DateTime.MinValue;
    private readonly Dictionary<string, (uint ItemId, uint EquippedCount, uint InventoryCount, uint ArmoryCount)> equipmentOwnership = new(StringComparer.Ordinal);
    private bool isMainWindowOpen;

    public PluginUI(
        BeastmasterConfiguration configuration,
        BeastmasterProgressService progressService,
        BeastmasterQuestService questService,
        BeastmasterNavigationService navigationService,
        BeastmasterDebugDataService debugDataService,
        BeastmasterAutoCaptureService autoCaptureService,
        BeastmasterCatalogSyncService catalogSyncService)
    {
        this.configuration = configuration;
        this.progressService = progressService;
        this.questService = questService;
        this.navigationService = navigationService;
        this.debugDataService = debugDataService;
        this.autoCaptureService = autoCaptureService;
        this.catalogSyncService = catalogSyncService;
    }

    public void OpenMainWindow()
    {
        isMainWindowOpen = true;
    }

    public void Draw()
    {
        RefreshGaugeSnapshot();
        DrawAutoCaptureOverlay();
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

    private void DrawAutoCaptureOverlay()
    {
        const uint beastmasterClassJobId = 43;
        if (!autoCaptureService.IsEnabled
            || !DalamudApi.ClientState.IsLoggedIn
            || DalamudApi.ObjectTable.LocalPlayer == null
            || DalamudApi.PlayerState.ClassJob.RowId != beastmasterClassJobId)
        {
            return;
        }

        ImGui.SetNextWindowPos(new Vector2(20f, 180f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin(
                "##BeastmasterAutoCaptureOverlay",
                ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav))
        {
            ImGui.End();
            return;
        }

        DrawAutoOutputHeader();
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            configuration.SelectedMainSection = "settings";
            configuration.Save();
            isMainWindowOpen = true;
        }

        if (autoOutputCollapsed)
        {
            ImGui.End();
            return;
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.55f, 1f), autoCaptureService.StatusText);
        ImGui.Text($"下一个技能：{autoCaptureService.NextActionName}");
        if (configuration.ShowGaugeInOverlay && !string.IsNullOrWhiteSpace(autoCaptureService.NextActionReason))
        {
            ImGui.TextDisabled($"原因：{autoCaptureService.NextActionReason}");
        }
        if (configuration.ShowGaugeInOverlay)
        {
            DrawOverlayGaugeSummary();
        }
        DrawOverlayTargetStatus();
        if (configuration.ShowGaugeInOverlay)
        {
            DrawOverlayAdvancedCandidates();
        }
        ImGui.Separator();
        var tryCapture = autoCaptureService.TryCapture;
        if (ImGui.Checkbox("尝试捕获", ref tryCapture))
        {
            autoCaptureService.SetTryCapture(tryCapture);
        }
        ImGui.SameLine();
        DrawCompactCaptureHpThreshold();
        var basicComboEnabled = autoCaptureService.BasicComboEnabled;
        if (ImGui.Checkbox("基础技能（1→2→3）", ref basicComboEnabled))
        {
            autoCaptureService.SetBasicComboEnabled(basicComboEnabled);
        }
        DrawAdvancedActionToggles();
        DrawBeastArenaActionToggles();

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            configuration.SelectedMainSection = "settings";
            configuration.Save();
            isMainWindowOpen = true;
        }

        ImGui.End();
    }

    private void DrawAutoOutputHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.35f, 1f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("驯兽ACR");
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(autoOutputCollapsed ? "左键展开悬浮窗" : "左键折叠悬浮窗");
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            autoOutputCollapsed = !autoOutputCollapsed;
        }

        ImGui.SameLine();
        var headerStatus = !autoCaptureService.IsEnabled
            ? "关闭"
            : autoCaptureService.IsPaused ? "暂停" : "自动";
        if (DrawOverlayStatusBadge(
            headerStatus,
            autoCaptureService.IsEnabled && !autoCaptureService.IsPaused
                ? new Vector4(0.2f, 0.42f, 0.28f, 1f)
                : new Vector4(0.3f, 0.3f, 0.34f, 1f),
            autoCaptureService.IsEnabled && !autoCaptureService.IsPaused
                ? new Vector4(0.45f, 1f, 0.58f, 1f)
                : new Vector4(0.7f, 0.7f, 0.75f, 1f)))
        {
            if (autoCaptureService.IsEnabled)
            {
                autoCaptureService.SetPaused(!autoCaptureService.IsPaused);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(!autoCaptureService.IsEnabled
                ? "请先在自动输出栏目开启自动输出"
                : autoCaptureService.IsPaused ? "点击恢复自动输出" : "点击暂停自动输出");
        }
    }

    private static bool DrawOverlayStatusBadge(string label, Vector4 background, Vector4 textColor)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 2f));
        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, background);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(2);
        return clicked;
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

    private void RefreshGaugeSnapshot()
    {
        if (DateTime.UtcNow < nextGaugeRefreshUtc)
        {
            return;
        }

        gaugeSnapshot = BeastmasterGaugeSnapshot.Read();
        nextGaugeRefreshUtc = DateTime.UtcNow.AddMilliseconds(100);
    }

    private void DrawOverlayGaugeSummary()
    {
        if (!gaugeSnapshot.Available)
        {
            return;
        }

        var entry = gaugeSnapshot.SummonEntry;
        ImGui.Text(entry == null
            ? $"当前魔兽：{(gaugeSnapshot.SummonDataId == 0 ? "未召唤" : gaugeSnapshot.SummonName)}"
            : $"当前魔兽：{entry.Name} [{entry.Attribute}]");
        DrawOverlayGaugeBar("技力", gaugeSnapshot.Tp, BeastmasterGaugeSnapshot.MaximumGauge, new Vector4(0.95f, 0.75f, 0.2f, 1f));
        DrawOverlayGaugeBar("兽力", gaugeSnapshot.BeastPower, BeastmasterGaugeSnapshot.MaximumGauge, new Vector4(0.35f, 0.7f, 1f, 1f));
        ImGui.Text($"御兽之心：{gaugeSnapshot.BeastHeartStacks} 层 | 兽灵之心：{gaugeSnapshot.BeastSoulStacks} 层");
    }

    private void DrawOverlayTargetStatus()
    {
        ImGui.Text("当前目标");
        var target = DalamudApi.TargetManager.Target;
        if (target is not Dalamud.Game.ClientState.Objects.Types.IBattleChara)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("无有效目标");
            return;
        }

        ImGui.SameLine();
        ImGui.Text(target.Name.TextValue);
        ImGui.SameLine();
        ImGui.TextDisabled($"{autoCaptureService.TargetHpPercent:0.#}% · {autoCaptureService.TargetStatus}");
        if (configuration.ShowGaugeInOverlay)
        {
            ImGui.TextDisabled($"捕获：{autoCaptureService.CaptureState}");
        }
    }

    private static void DrawOverlayGaugeBar(string label, float value, float maximum, Vector4 color)
    {
        var fraction = Math.Clamp(value / maximum, 0f, 1f);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(fraction, new Vector2(-1f, 14f), $"{label} {value:0}/{maximum:0}");
        ImGui.PopStyleColor();
    }

    private void DrawCompactCaptureHpThreshold()
    {
        var threshold = Math.Clamp(configuration.CaptureHpThreshold, 1f, 100f);
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputFloat("##overlay-capture-threshold", ref threshold, 1f, 5f, "%.0f%%"))
        {
            threshold = Math.Clamp(threshold, 1f, 100f);
            configuration.CaptureHpThreshold = threshold;
            configuration.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("捕获血量阈值：目标低于此百分比时释放捕获");
        }
    }

    private void DrawOverlayAdvancedCandidates()
    {
        if (!ImGui.CollapsingHeader("高级技能候选##BeastmasterAdvancedCandidates"))
        {
            return;
        }

        var entry = gaugeSnapshot.SummonEntry;
        if (entry == null)
        {
            ImGui.TextDisabled("未识别当前魔兽，无法生成高级技能候选。");
            return;
        }

        ImGui.TextColored(GetAttributeColor(entry.Attribute), $"属性：{entry.Attribute}");
        DrawAdvancedCandidate(
            configuration.BeastHeartCooperationEnabled ? "御兽协作（黄豆）" : "兽灵协作（蓝豆）",
            configuration.BeastHeartCooperationEnabled
                ? $"{GetActionName(47093)} → 属性斧"
                : $"属性斧 → {GetActionName(47093)}",
            configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled,
            gaugeSnapshot.Tp >= 100 && gaugeSnapshot.BeastPower >= 100
                ? "技力和兽力满足基础门槛"
                : $"资源不足：技力 {gaugeSnapshot.Tp}/100，兽力 {gaugeSnapshot.BeastPower}/100");
        var thirdFormEnabled = configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled;
        DrawAdvancedCandidate(
            configuration.PhysicalThirdFormEnabled ? "三式（物理）" : "三式（魔法）",
            GetThirdFormActionName(gaugeSnapshot),
            thirdFormEnabled,
            GetThirdFormReason(gaugeSnapshot));
        ImGui.TextDisabled($"释放：运行时调整技能（{(configuration.AutoReleaseEnabled ? "开启" : "关闭")}）");
        if (ImGui.Button($"手动释放大招##manual-ultimate-overlay"))
        {
            autoCaptureService.TryUseUltimate();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(autoCaptureService.ManualActionStatus);
    }

    private static void DrawAdvancedCandidate(string type, string actionName, bool enabled, string reason)
    {
        ImGui.Text($"{type}：{actionName}");
        ImGui.SameLine();
        ImGui.TextDisabled(enabled ? reason : "开关已关闭");
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
        DrawSidebarButton(MainSections[3]);
        DrawSidebarButton(MainSections[4]);

        ImGui.Separator();
        DrawSidebarLabel("工具");
        DrawSidebarButton(MainSections[5]);

        if (ImGui.Button("反馈与建议", new Vector2(ImGui.GetContentRegionAvail().X, 30f)))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://discord.com/channels/1258981591124938762/1546882305686118420")
            {
                UseShellExecute = true,
            });
        }

        DrawSidebarButton(MainSections[6]);
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
            case "equipment":
                DrawEquipment();
                break;
            case "commands":
                DrawCommands();
                break;
            case "auto-output":
                DrawAutoOutput();
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
        if (ImGui.Button("WIKI##quest-wiki"))
        {
            Process.Start(new ProcessStartInfo("https://ff14.huijiwiki.com/wiki/%E9%A9%AF%E5%85%BD%E5%B8%88#%E7%89%B9%E8%81%8C%E4%BB%BB%E5%8A%A1")
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

        var hideCompleted = configuration.HideCompletedQuests;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("隐藏已完成任务", ref hideCompleted))
        {
            configuration.HideCompletedQuests = hideCompleted;
            configuration.Save();
        }
        ImGui.PopStyleColor();
        ImGui.Separator();

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
                navigationService.NavigateQuestTarget(target!);
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
        DrawGameCommandButton("魔兽图鉴", "/魔兽图鉴");
        DrawGameCommandButton("驯兽师魔兽-小", "/beastpetsize all small");
        DrawGameCommandButton("驯兽师魔兽-中", "/beastpetsize all medium");
        DrawGameCommandButton("驯兽师魔兽-大", "/beastpetsize all large");
    }

    private void DrawEquipment()
    {
        RefreshEquipmentOwnership();
        ImGui.Text("推荐装备");
        ImGui.TextDisabled("驯兽师 50 级开荒装和 BIS 配置。");
        var equipmentSet = selectedEquipmentSet;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo("装备方案", ref equipmentSet, "开荒装\0BIS\0"))
        {
            selectedEquipmentSet = equipmentSet;
        }
        ImGui.Separator();

        if (!ImGui.BeginTable(
                "BeastmasterEquipmentTable",
                4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("部位", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("装备", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("分类", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("持有", ImGuiTableColumnFlags.WidthFixed, 210f);
        ImGui.TableHeadersRow();

        var entries = selectedEquipmentSet == 0
            ? BeastmasterEquipmentGuide.Level50Starter
            : BeastmasterEquipmentGuide.Level50BestInSlot;
        foreach (var equipment in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(equipment.Slot);
            ImGui.TableNextColumn();
            if (equipment.Name == "无装备")
            {
                ImGui.TextDisabled(equipment.Name);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1f, 1f));
                ImGui.Text(equipment.Name);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("点击访问 Wiki 装备详情");
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    OpenEquipmentWiki(equipment.Name);
                }
            }
            ImGui.TableNextColumn();
            ImGui.TextDisabled(equipment.Category);
            ImGui.TableNextColumn();
            DrawEquipmentOwnership(equipment);
        }

        ImGui.EndTable();
        ImGui.Spacing();
        ImGui.TextDisabled(selectedEquipmentSet == 0
            ? "开荒装：优先考虑获取难度和快速成型。"
            : "BIS：优先考虑 50 级最终战斗属性。 ");
    }

    private static void OpenEquipmentWiki(string equipmentName)
    {
        Process.Start(new ProcessStartInfo(
            $"https://ff14.huijiwiki.com/wiki/{Uri.EscapeDataString($"物品:{equipmentName}")}")
        {
            UseShellExecute = true,
        });
    }

    private void DrawEquipmentOwnership(BeastmasterEquipmentEntry equipment)
    {
        if (equipment.Name == "无装备")
        {
            ImGui.TextDisabled("-");
            return;
        }

        if (!equipmentOwnership.TryGetValue(equipment.Name, out var ownership) || ownership.ItemId == 0)
        {
            ImGui.TextDisabled("待补 ItemId");
            return;
        }

        var equippedCount = ownership.EquippedCount;
        var inventoryCount = ownership.InventoryCount;
        var armoryCount = ownership.ArmoryCount;
        if (equippedCount > 0)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"已装备 ({equippedCount})");
            ImGui.SameLine();
        }
        ImGui.TextColored(
            inventoryCount > 0 ? new Vector4(0.4f, 0.9f, 0.5f, 1f) : new Vector4(0.65f, 0.65f, 0.7f, 1f),
            inventoryCount > 0 ? $"背包 有 ({inventoryCount})" : "背包 无");
        ImGui.SameLine();
        ImGui.TextColored(
            armoryCount > 0 ? new Vector4(0.4f, 0.8f, 1f, 1f) : new Vector4(0.65f, 0.65f, 0.7f, 1f),
            armoryCount > 0 ? $"兵装库 有 ({armoryCount})" : "兵装库 无");
    }

    private void RefreshEquipmentOwnership()
    {
        if (DateTime.UtcNow < nextEquipmentRefreshUtc)
        {
            return;
        }

        equipmentOwnership.Clear();
        var selectedEntries = selectedEquipmentSet == 0
            ? BeastmasterEquipmentGuide.Level50Starter
            : BeastmasterEquipmentGuide.Level50BestInSlot;
        foreach (var equipment in selectedEntries.Where(item => item.Name != "无装备"))
        {
            if (equipment.ItemId == 0)
            {
                equipmentOwnership[equipment.Name] = (0, 0, 0, 0);
                continue;
            }

            equipmentOwnership[equipment.Name] = (
                equipment.ItemId,
                CountItems(equipment.ItemId, [GameInventoryType.EquippedItems]),
                CountItems(equipment.ItemId, InventoryTypes()),
                CountItems(equipment.ItemId, ArmoryTypes(equipment.Slot)));
        }

        nextEquipmentRefreshUtc = DateTime.UtcNow.AddSeconds(1);
    }

    private static uint CountItems(uint itemId, IEnumerable<GameInventoryType> types)
    {
        var count = 0u;
        foreach (var type in types)
        {
            foreach (var inventoryItem in DalamudApi.GameInventory.GetInventoryItems(type))
            {
                if (!inventoryItem.IsEmpty && inventoryItem.BaseItemId == itemId)
                {
                    count += (uint)inventoryItem.Quantity;
                }
            }
        }

        return count;
    }

    private static IEnumerable<GameInventoryType> InventoryTypes()
    {
        yield return GameInventoryType.Inventory1;
        yield return GameInventoryType.Inventory2;
        yield return GameInventoryType.Inventory3;
        yield return GameInventoryType.Inventory4;
    }

    private static IEnumerable<GameInventoryType> ArmoryTypes(string slot)
    {
        if (slot == "主手") yield return GameInventoryType.ArmoryMainHand;
        else if (slot == "副手") yield return GameInventoryType.ArmoryOffHand;
        else if (slot == "头部") yield return GameInventoryType.ArmoryHead;
        else if (slot == "身体") yield return GameInventoryType.ArmoryBody;
        else if (slot == "手部") yield return GameInventoryType.ArmoryHands;
        else if (slot == "腿部") yield return GameInventoryType.ArmoryLegs;
        else if (slot == "脚部") yield return GameInventoryType.ArmoryFeets;
        else if (slot == "耳饰") yield return GameInventoryType.ArmoryEar;
        else if (slot == "项链") yield return GameInventoryType.ArmoryNeck;
        else if (slot == "手镯") yield return GameInventoryType.ArmoryWrist;
        else if (slot.StartsWith("戒指", StringComparison.Ordinal)) yield return GameInventoryType.ArmoryRings;
        else if (slot == "职业证") yield return GameInventoryType.ArmorySoulCrystal;
    }

    private static void DrawGameCommandButton(string label, string command)
    {
        if (ImGui.Button(label) && !GameCommandService.Execute(command))
        {
            DalamudApi.ChatGui.Print($"[驯兽师助手] 无法执行 {command}。");
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
        if (ImGui.Button("同步当前角色已解锁魔兽"))
        {
            catalogSyncService.RequestSync();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(catalogSyncService.Status);
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
        var sortByLevel = configuration.SortCatalogByLevel;
        if (ImGui.Checkbox("按等级排序", ref sortByLevel))
        {
            configuration.SortCatalogByLevel = sortByLevel;
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
        var currentMapName = DalamudApi.DataManager.GetExcelSheet<TerritoryType>()
            .TryGetRow(currentTerritory, out var currentTerritoryRow)
            ? currentTerritoryRow.Map.Value.PlaceName.Value.Name.ExtractText()
            : string.Empty;
        var completedCount = entries.Count(entry => progressService.IsCompleted(entry.Key));
        ImGui.ProgressBar((float)completedCount / entries.Count, new Vector2(-1f, 0f), $"{completedCount}/{entries.Count}");

        ImGui.Spacing();

        if (!ImGui.BeginTable(
                "BeastmasterCatalogTable",
                8,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0f, 0f)))
        {
            return;
        }

        ImGui.TableSetupColumn("完成", ImGuiTableColumnFlags.WidthFixed, 46f);
        ImGui.TableSetupColumn("编号 / 魔兽", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("属性", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("技能", ImGuiTableColumnFlags.WidthFixed, 118f);
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
                .OrderBy(entry => !entry.Location.Equals(currentMapName, StringComparison.Ordinal))
                .ThenBy(entry => entry.Location, StringComparer.Ordinal)
                .ThenBy(entry => configuration.SortCatalogByLevel ? GetCatalogMinimumLevel(entry.Level) : int.MaxValue)
                .ThenBy(entry => entry.Number);
        }
        else if (configuration.SortCatalogByLevel)
        {
            sortedEntries = displayedEntries
                .OrderBy(entry => GetCatalogMinimumLevel(entry.Level))
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
            ImGui.TextColored(GetAttributeColor(entry.Attribute), entry.Attribute.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped($"{GetActionName(entry.UltimateActionId)} /\n{GetActionName(entry.ReleaseActionId)}");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                DrawActionTooltip("大招", entry.UltimateActionId);
                DrawActionTooltip("释放", entry.ReleaseActionId);
                ImGui.EndTooltip();
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Level);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Location);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetCatalogCoordinate(entry));
            ImGui.TableNextColumn();
            if (entry.LocationType == BeastmasterCatalogLocationType.Field
                && entry.MapX.HasValue
                && entry.MapY.HasValue)
            {
                if (ImGui.SmallButton($"导航##catalog-nav-{entry.Number}"))
                {
                    navigationService.Navigate(entry);
                }
            }
            else if (entry.LocationType == BeastmasterCatalogLocationType.Duty
                && ImGui.SmallButton($"副本##catalog-duty-{entry.Number}"))
            {
                navigationService.OpenDutyFinder(entry);
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

    private static int GetCatalogMinimumLevel(string level)
    {
        var digits = new string(level.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
    }

    private static Vector4 GetAttributeColor(BeastmasterAttribute attribute)
        => attribute switch
        {
            BeastmasterAttribute.猛 => new Vector4(0.95f, 0.35f, 0.3f, 1f),
            BeastmasterAttribute.坚 => new Vector4(0.35f, 0.65f, 1f, 1f),
            BeastmasterAttribute.魔 => new Vector4(1f, 0.82f, 0.25f, 1f),
            BeastmasterAttribute.翔 => new Vector4(0.4f, 0.9f, 0.5f, 1f),
            _ => new Vector4(0.6f, 0.6f, 0.6f, 1f),
        };

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action)
            ? action.Name.ExtractText()
            : actionId.ToString();

    private static void DrawActionTooltip(string type, uint actionId)
    {
        var actions = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (!actions.TryGetRow(actionId, out var action))
        {
            ImGui.TextDisabled($"{type}：ActionId {actionId} 未找到");
            return;
        }

        ImGui.Text($"{type}：{action.Name.ExtractText()}");
        ImGui.TextDisabled($"ActionId：{actionId} · 等级：{action.ClassJobLevel} · 射程：{action.Range} · 范围：{action.EffectRange}");
    }

    private void DrawSettings()
    {
        ImGui.Text("设置");
        ImGui.Separator();

        ImGui.Text("依赖插件");
        DrawDependency("vnavmesh", navigationService.IsVnavmeshInstalled, "同地图路径规划与移动");
        DrawDependency("Lifestream", navigationService.IsLifestreamInstalled, "跨地图传送");
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

    private void DrawAutoOutput()
    {
        ImGui.Text("自动输出");
        ImGui.TextDisabled("配置自动捕获和自动攻击行为。");
        ImGui.Separator();

        var enabled = autoCaptureService.IsEnabled;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("自动输出", ref enabled))
        {
            autoCaptureService.SetEnabled(enabled);
        }
        ImGui.PopStyleColor();
        var paused = autoCaptureService.IsPaused;
        if (ImGui.Checkbox("暂停", ref paused))
        {
            autoCaptureService.SetPaused(paused);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("暂停时保留自动输出总开关，但不执行任何动作");
        ImGui.TextDisabled("仅对当前手动选择的敌对目标生效；无捕获状态时优先捕获，再执行 1→2→3 连击。");
        DrawCaptureHpThreshold();
        DrawSettingCheckbox(
            "详细模式",
            "显示悬浮窗中的原因、捕获状态、当前魔兽、量谱和高级技能候选等详细信息。默认关闭。",
            nameof(configuration.ShowGaugeInOverlay),
            configuration.ShowGaugeInOverlay);
        ImGui.Spacing();
        DrawAdvancedActionToggles();
        DrawBeastArenaActionToggles();

        ImGui.Spacing();
        ImGui.Text("当前模式");
        ImGui.Text(autoCaptureService.IsEnabled
            ? autoCaptureService.IsPaused
                ? "已暂停"
                : autoCaptureService.TryCapture ? "自动捕获中..." : "自动攻击中..."
            : "未开启");
        ImGui.TextDisabled("开启后可在悬浮窗中切换“尝试捕获”，右键悬浮窗可打开设置。");

        ImGui.Spacing();
        DrawBeastmasterGauge();

        ImGui.Spacing();
        DrawBeastmasterGaugeGuide();
    }

    private void DrawAdvancedActionToggles()
    {
        if (!ImGui.CollapsingHeader("高级技能##BeastmasterAdvancedActions"))
        {
            return;
        }

        ImGui.Indent();
        var beastHeartEnabled = configuration.BeastHeartCooperationEnabled;
        if (ImGui.Checkbox("御兽协作（黄豆）", ref beastHeartEnabled))
        {
            configuration.BeastHeartCooperationEnabled = beastHeartEnabled;
            if (beastHeartEnabled)
            {
                configuration.BeastSoulCooperationEnabled = false;
            }
            configuration.Save();
        }

        var beastSoulEnabled = configuration.BeastSoulCooperationEnabled;
        if (ImGui.Checkbox("兽灵协作（蓝豆）", ref beastSoulEnabled))
        {
            configuration.BeastSoulCooperationEnabled = beastSoulEnabled;
            if (beastSoulEnabled)
            {
                configuration.BeastHeartCooperationEnabled = false;
            }
            configuration.Save();
        }

        var physicalThirdFormEnabled = configuration.PhysicalThirdFormEnabled;
        if (ImGui.Checkbox("三式（物理）", ref physicalThirdFormEnabled))
        {
            configuration.PhysicalThirdFormEnabled = physicalThirdFormEnabled;
            if (physicalThirdFormEnabled)
            {
                configuration.MagicalThirdFormEnabled = false;
            }
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("万象流转·物理");
        }

        var magicalThirdFormEnabled = configuration.MagicalThirdFormEnabled;
        if (ImGui.Checkbox("三式（魔法）", ref magicalThirdFormEnabled))
        {
            configuration.MagicalThirdFormEnabled = magicalThirdFormEnabled;
            if (magicalThirdFormEnabled)
            {
                configuration.PhysicalThirdFormEnabled = false;
            }
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("万象流转·魔法");
        }

        var autoWhistleEnabled = configuration.AutoWhistleEnabled;
        if (ImGui.Checkbox("自动兽笛", ref autoWhistleEnabled))
        {
            configuration.AutoWhistleEnabled = autoWhistleEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("当前没有魔兽时，按兽笛 1→2→3 使用首个可用技能；释放后等待 1 秒确认召唤。");
        }

        var autoFinalStrikeEnabled = configuration.AutoFinalStrikeEnabled;
        if (ImGui.Checkbox("自动最后一击", ref autoFinalStrikeEnabled))
        {
            configuration.AutoFinalStrikeEnabled = autoFinalStrikeEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("宝宝血量低于设定百分比时，对当前敌对目标使用最后一击。");
        }

        var finalStrikeThreshold = Math.Clamp(configuration.AutoFinalStrikeHpThreshold, 1f, 100f);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.InputFloat("##AutoFinalStrikeThreshold", ref finalStrikeThreshold, 1f, 5f, "%.0f%%"))
        {
            configuration.AutoFinalStrikeHpThreshold = Math.Clamp(finalStrikeThreshold, 1f, 100f);
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("自动最后一击的宝宝血量阈值，范围 1%~100%。");
        }

        var releaseEnabled = configuration.AutoReleaseEnabled;
        if (ImGui.Checkbox("释放", ref releaseEnabled))
        {
            configuration.AutoReleaseEnabled = releaseEnabled;
            configuration.Save();
        }

        // 暂时隐藏兽笛循环连招入口，保留实现以便后续恢复。
        // var whistleRotationEnabled = configuration.WhistleRotationEnabled;
        // if (ImGui.Checkbox("兽笛循环连招", ref whistleRotationEnabled))
        // {
        //     configuration.WhistleRotationEnabled = whistleRotationEnabled;
        //     configuration.Save();
        // }
        // if (ImGui.IsItemHovered())
        // {
        //     ImGui.BeginTooltip();
        //     ImGui.TextUnformatted("兽笛1（非战斗）→释放→最后一击→兽笛2→释放→最后一击→兽笛3→释放");
        //     ImGui.TextDisabled("完成后自动关闭，并等待兽笛1冷却结束。");
        //     ImGui.EndTooltip();
        // }
        // ImGui.TextDisabled($"状态：{autoCaptureService.WhistleRotationStatus}");

        ImGui.Unindent();
    }

    private void DrawBeastArenaActionToggles()
    {
        if (!ImGui.CollapsingHeader("斗兽塔技能##BeastArenaActions"))
        {
            return;
        }

        ImGui.Indent();
        var keepAttentionEnabled = configuration.ArenaKeepAttentionEnabled;
        if (ImGui.Checkbox("持续吸引（宝宝抗）", ref keepAttentionEnabled))
        {
            configuration.ArenaKeepAttentionEnabled = keepAttentionEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("仅在区域 1339~1343 生效；自身没有被保护（2413）时使用吸引注意（46751）。");
        }

        var keepProvokeEnabled = configuration.ArenaKeepProvokeEnabled;
        if (ImGui.Checkbox("持续挑衅（自己抗）", ref keepProvokeEnabled))
        {
            configuration.ArenaKeepProvokeEnabled = keepProvokeEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("仅在区域 1339~1343 生效；自身没有仇恨上升（5586）时使用挑衅（46750）。");
        }
        ImGui.Unindent();
    }

    private void DrawCaptureHpThreshold()
    {
        var threshold = Math.Clamp(configuration.CaptureHpThreshold, 1f, 100f);
        ImGui.SetNextItemWidth(180f);
        if (ImGui.InputFloat("捕获血量阈值", ref threshold, 1f, 5f, "%.0f%%"))
        {
            threshold = Math.Clamp(threshold, 1f, 100f);
            configuration.CaptureHpThreshold = threshold;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("目标低于此血量时释放捕获");
    }

    private void DrawBeastmasterGauge()
    {
        ImGui.Separator();
        ImGui.Text("当前量谱");
        ImGui.TextDisabled("只读显示当前驯兽师量谱状态；数据来自 JobGaugeManager.CurrentGauge。");

        var snapshot = gaugeSnapshot;
        ImGui.TextColored(
            snapshot.Available
                ? new Vector4(0.35f, 0.85f, 0.55f, 1f)
                : new Vector4(0.9f, 0.55f, 0.35f, 1f),
            snapshot.Status);

        if (!snapshot.Available)
        {
            return;
        }

        DrawCurrentSummon(snapshot);

        if (ImGui.BeginTable(
                "BeastmasterGaugeState",
                2,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("字段", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("当前值", ImGuiTableColumnFlags.WidthStretch);
            DrawGaugeRow("技力", $"{snapshot.Tp} / {BeastmasterGaugeSnapshot.MaximumGauge}", "基础技能资源");
            DrawGaugeRow("兽力", $"{snapshot.BeastPower} / {BeastmasterGaugeSnapshot.MaximumGauge}", "兽心技能资源");
            DrawGaugeRow("当前兽笛", snapshot.WhistleIndex is >= 1 and <= 3
                ? $"{snapshot.WhistleIndex} 号"
                : "未召唤", "当前兽笛类型");
            DrawGaugeRow("宝宝血量", snapshot.SummonMaxHp > 0
                ? $"{snapshot.SummonHpPercent:0.#}%（{snapshot.SummonCurrentHp}/{snapshot.SummonMaxHp}）"
                : "不可用", "自动最后一击依据");
            DrawGaugeRow("御兽之心", $"{snapshot.BeastHeartStacks} 层", "协作量谱");
            DrawGaugeRow("兽灵之心", $"{snapshot.BeastSoulStacks} 层", "协作量谱");
            DrawGaugeRow("黑白状态", GetBlackWhiteStatus(snapshot), "生息 4599 / 死灭 4600");
            ImGui.EndTable();
        }

        ImGui.TextDisabled($"当前决策：{GetGaugeDecision(snapshot)}");
        ImGui.TextDisabled($"高级技能判断：{autoCaptureService.AdvancedActionStatus}");

        if (ImGui.CollapsingHeader("量谱原始数据##BeastmasterGaugeRaw"))
        {
            ImGui.TextDisabled($"Address: 0x{snapshot.Address.ToInt64():X}");
            ImGui.TextWrapped($"48 bytes：{snapshot.FormatRawBytes()}");
            ImGui.TextDisabled("字段偏移：技能量 +0x10，魔兽技力 +0x11，当前兽笛 +0x13，御兽之心/兽灵之心 +0x18。");
        }
    }

    private static void DrawCurrentSummon(BeastmasterGaugeSnapshot snapshot)
    {
        if (snapshot.SummonDataId == 0)
        {
            ImGui.TextDisabled("当前魔兽：未召唤");
            return;
        }

        var entry = BeastmasterCatalog.Entries.FirstOrDefault(
            item => item.Number == snapshot.SummonDataId - 18915);
        if (entry == null)
        {
            ImGui.TextDisabled($"当前魔兽：{snapshot.SummonName}（DataId {snapshot.SummonDataId}）");
            return;
        }

        ImGui.Text("当前魔兽");
        ImGui.SameLine();
        ImGui.Text(entry.Name);
        ImGui.SameLine();
        ImGui.TextColored(GetAttributeColor(entry.Attribute), $"[{entry.Attribute}]");
        ImGui.TextDisabled($"大招：{GetActionName(entry.UltimateActionId)} | 释放：{GetActionName(entry.ReleaseActionId)}");
    }

    private static string GetGaugeDecision(BeastmasterGaugeSnapshot snapshot)
    {
        if (snapshot.SummonDataId == 0)
        {
            return "等待召唤兽";
        }

        if (snapshot.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            return $"积累技力（{snapshot.Tp}/{BeastmasterGaugeSnapshot.MaximumGauge}）";
        }

        if (snapshot.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            return $"积累兽力（{snapshot.BeastPower}/{BeastmasterGaugeSnapshot.MaximumGauge}）";
        }

        return snapshot.BeastHeartStacks > 0
            ? "技力与兽力已满足，可结合御兽之心安排协作技"
            : "技力与兽力已满足，可执行连招";
    }

    private static string GetBlackWhiteStatus(BeastmasterGaugeSnapshot snapshot)
        => (snapshot.HasWhiteStatus, snapshot.HasPurpleStatus) switch
        {
            (true, true) => "白（生息）与黑/紫（死灭）同时激活",
            (true, false) => "白（生息）",
            (false, true) => "黑/紫（死灭）",
            _ => "未激活",
        };

    private string GetThirdFormActionName(BeastmasterGaugeSnapshot snapshot)
    {
        if (!configuration.PhysicalThirdFormEnabled && !configuration.MagicalThirdFormEnabled)
        {
            return "-";
        }

        var actionId = snapshot.BeastHeartStacks >= 3 && (snapshot.HasWhiteStatus || snapshot.HasPurpleStatus)
            ? 44905u
            : (snapshot.HasWhiteStatus, snapshot.HasPurpleStatus) switch
        {
            (true, _) => configuration.PhysicalThirdFormEnabled ? 44931u : 44933u,
            (false, true) => configuration.PhysicalThirdFormEnabled ? 44930u : 44932u,
            _ => 44905u,
        };
        return GetActionName(actionId);
    }

    private static string GetThirdFormReason(BeastmasterGaugeSnapshot snapshot)
        => snapshot.BeastHeartStacks >= 3 && (snapshot.HasWhiteStatus || snapshot.HasPurpleStatus)
            ? $"{GetBlackWhiteStatus(snapshot)}，御兽之心 {snapshot.BeastHeartStacks} 层，可使用鼓劲"
            : snapshot.HasWhiteStatus || snapshot.HasPurpleStatus
                ? $"{GetBlackWhiteStatus(snapshot)}，等待御兽之心 3 层（当前 {snapshot.BeastHeartStacks} 层）"
                : "等待生息（白）或死灭（黑/紫）状态";

    private static void DrawGaugeRow(string name, object value, string description)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(name);
        ImGui.TableNextColumn();
        ImGui.Text(value.ToString());
        ImGui.SameLine();
        ImGui.TextDisabled(description);
    }

    private static void DrawBeastmasterGaugeGuide()
    {
        if (!ImGui.CollapsingHeader("量谱说明##BeastmasterGaugeGuide"))
        {
            return;
        }

        ImGui.TextDisabled("以下说明整理自驯兽师职业量谱和技能逻辑。量谱区只读显示当前状态，不会修改游戏数据。");
        ImGui.Separator();

        DrawGuideTitle("技力 / 兽力");
        ImGui.TextWrapped("学会兽心特性后，技能栏会显示驯兽师专用的技能量谱。连续成功时，技力会增加；技力越高，战技威力越高。");
        ImGui.TextWrapped("技力和兽力是驯兽师职业量谱中的两种资源，当前上限均为 250。资源越高，越容易满足对应技能和协作技的使用要求。");

        ImGui.Spacing();
        DrawGuideTitle("御兽之心");
        ImGui.TextWrapped("学会兽心 II 特性后，画面会显示御兽之心的状态。驯兽师成功通过兽心技鼓舞发动兽心协作技后，会获得一档御兽之心。");
        ImGui.TextWrapped("发动技能鼓劲可以消耗全部御兽之心，并提升驯兽师的技力。消耗的御兽之心档数越高，技力提升越高。");

        ImGui.Spacing();
        DrawGuideTitle("协作量谱");
        ImGui.TextWrapped("协作量谱显示当前兽心协作技的状态。兽心技共有翔、猛、坚、魔四种属性，每次发动兽心技时，都会点亮对应的属性圆格。");
        ImGui.TextWrapped("当驯兽师或魔兽一方发动兽心技后，只要另一方在 7 秒内发动技能，即可触发兽心协作技的追击伤害。");
        ImGui.TextWrapped("协作量谱下方显示的数字是协作次数。协作次数越多，兽心协作技的威力越高。");

        ImGui.Spacing();
        DrawGuideTitle("生息 / 死灭");
        ImGui.TextWrapped("参考“翔→猛→坚→魔→翔”的属性循环，按顺时针顺序触发兽心协作技，可以发动更强的生息或死灭协作技。");
        ImGui.TextWrapped("触发兽心协作技二式后，驯兽师将获得生息或死灭的兽心技属性；该属性会点亮协作量谱的中心，并影响后续协作技。");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.25f, 1f), "职业量谱的完整说明可随时在技能菜单中查看。");
    }

    private static void DrawGuideTitle(string title)
    {
        ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), title);
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
                case nameof(configuration.BeastHeartCooperationEnabled):
                    configuration.BeastHeartCooperationEnabled = value;
                    if (value) configuration.BeastSoulCooperationEnabled = false;
                    break;
                case nameof(configuration.BeastSoulCooperationEnabled):
                    configuration.BeastSoulCooperationEnabled = value;
                    if (value) configuration.BeastHeartCooperationEnabled = false;
                    break;
                case nameof(configuration.AutoReleaseEnabled):
                    configuration.AutoReleaseEnabled = value;
                    break;
                case nameof(configuration.ShowGaugeInOverlay):
                    configuration.ShowGaugeInOverlay = value;
                    break;
            }

            configuration.Save();
        }

        ImGui.TextDisabled(description);
    }

    private void DrawDebug()
    {
        ImGui.Text("DEBUG");
        ImGui.TextDisabled("选择资料类型后读取；读取结果会自动复制到剪贴板。");
        ImGui.Separator();

        ImGui.Text("技能状态");
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 72f));
        ImGui.InputText("##DebugActionId", ref debugActionId, 10);
        ImGui.SameLine();
        if (ImGui.Button("查询##DebugActionStatus"))
        {
            try
            {
                SetDebugResult(uint.TryParse(debugActionId, out var actionId)
                    ? debugDataService.GetActionStatusDebug(actionId, debugUseAdjustedActionId)
                    : "技能状态\n请输入有效的数字 ActionId。");
            }
            catch (Exception ex)
            {
                SetDebugResult($"技能状态\n查询异常: {ex.Message}");
            }
        }

        ImGui.Checkbox("使用 GetAdjustedActionId", ref debugUseAdjustedActionId);
        ImGui.Spacing();

        ImGui.Text("关键词查询");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##DebugQuery", ref debugQuery, 128);

        DrawDebugActionRow(
            "##DebugSearchType",
            ref debugSearchType,
            "职业\0区域\0任务\0物品\0NPC\0怪物\0副本\0",
            "查询##DebugSearch",
            RunDebugSearch);

        ImGui.Spacing();
        ImGui.Text("项目资料");
        DrawDebugActionRow(
            "##DebugProjectDataType",
            ref debugProjectDataType,
            "驯养魔兽之人任务\0当前所有任务状态\0驯兽师任务链\0图鉴副本 ID\0自动捕获 ID\0魔兽属性映射\0魔兽图鉴客户端数据\0推荐装备物品 ID\0",
            "读取##DebugProjectData",
            RunDebugProjectData);

        ImGui.Spacing();
        ImGui.Text("当前状态");
        DrawDebugActionRow(
            "##DebugCurrentStateType",
            ref debugCurrentStateType,
            "驯兽师量谱原始数据\0当前目标状态\0当前连击状态\0协力验证数据\0当前角色\0当前位置\0目标捕获判定\0",
            "读取##DebugCurrentState",
            RunDebugCurrentState);

        ImGui.Separator();
        if (ImGui.BeginChild("DebugResult", Vector2.Zero, true))
        {
            ImGui.TextUnformatted(debugResult);
        }

        ImGui.EndChild();
    }

    private static void DrawDebugActionRow(
        string comboId,
        ref int selectedIndex,
        string options,
        string buttonLabel,
        System.Action action)
    {
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 72f));
        ImGui.Combo(comboId, ref selectedIndex, options);
        ImGui.SameLine();
        if (ImGui.Button(buttonLabel))
        {
            action();
        }
    }

    private void RunDebugSearch()
    {
        SetDebugResult(debugSearchType switch
        {
            0 => debugDataService.FindClassJobs(debugQuery),
            1 => debugDataService.FindTerritories(debugQuery),
            2 => debugDataService.FindQuests(debugQuery),
            3 => debugDataService.FindItems(debugQuery),
            4 => debugDataService.FindNpcs(debugQuery),
            5 => debugDataService.FindMonsters(debugQuery),
            6 => debugDataService.FindDuties(debugQuery),
            _ => "未知查询类型。",
        });
    }

    private void RunDebugProjectData()
    {
        SetDebugResult(debugProjectDataType switch
        {
            0 => debugDataService.FindQuests("驯养魔兽之人"),
            1 => questService.GetActiveQuestsDebug(),
            2 => debugDataService.FindBeastmasterQuestChain(),
            3 => debugDataService.FindCatalogDuties(),
            4 => debugDataService.FindAutoCaptureData(),
            5 => debugDataService.FindBeastmasterAttributes(),
            6 => debugDataService.GetBeastmasterCatalogProbe(),
            7 => debugDataService.FindRecommendedEquipmentIds(),
            _ => "未知项目资料类型。",
        });
    }

    private void RunDebugCurrentState()
    {
        SetDebugResult(debugCurrentStateType switch
        {
            0 => debugDataService.GetBeastmasterGaugeRaw(),
            1 => debugDataService.GetCurrentTargetDebug(),
            2 => debugDataService.GetComboDebug(),
            3 => debugDataService.GetCooperationValidationDebug(),
            4 => debugDataService.GetCharacter(),
            5 => debugDataService.GetLocation(),
            6 => debugDataService.GetCaptureCheckDebug(),
            _ => "未知当前状态类型。",
        });
    }

    private void SetDebugResult(string result)
    {
        debugResult = result;
        ImGui.SetClipboardText(result);
    }
}
