using Dalamud.Game.Command;
using Dalamud.Plugin;

namespace Beastmaster;

public sealed class BeastmasterPlugin : IDalamudPlugin
{
    private const string CommandName = "/beastmaster";
    private const string ChineseCommandName = "/驯兽师";
    private readonly PluginUI ui;
    private readonly BeastmasterNavigationService navigationService;
    private readonly BeastmasterCatalogChatTracker catalogChatTracker;
    private readonly BeastmasterAutoCaptureService autoCaptureService;
    private readonly BeastmasterCatalogSyncService catalogSyncService;
    private readonly BeastmasterAchievementSyncService achievementSyncService;
    private readonly BeastmasterResultProgressService resultProgressService;
    private readonly BeastmasterNotebookProgressService notebookProgressService;
    private readonly BeastmasterNotebookSyncService notebookSyncService;
    private readonly BeastmasterPetPartyService petPartyService;
    private readonly BeastmasterCountdownService countdownService;
    private readonly BeastmasterSequenceService sequenceService;
    private readonly BeastmasterRuleService ruleService;

    public string Name => "Beastmaster";

    public BeastmasterConfiguration Configuration { get; }

    public BeastmasterPlugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);

        Configuration = pluginInterface.GetPluginConfig() as BeastmasterConfiguration
            ?? new BeastmasterConfiguration();
        Configuration.Initialize(pluginInterface);
        var progressService = new BeastmasterProgressService(Configuration);
        petPartyService = new BeastmasterPetPartyService();
        petPartyService.Start();
        notebookProgressService = new BeastmasterNotebookProgressService(progressService);
        notebookProgressService.Start();
        notebookSyncService = new BeastmasterNotebookSyncService(progressService);
        notebookSyncService.Start();
        resultProgressService = new BeastmasterResultProgressService(progressService);
        resultProgressService.Start();
        catalogSyncService = new BeastmasterCatalogSyncService(progressService);
        catalogSyncService.Start();
        achievementSyncService = new BeastmasterAchievementSyncService(progressService);
        achievementSyncService.Start();
        var questService = new BeastmasterQuestService();
        countdownService = new BeastmasterCountdownService(Configuration);
        sequenceService = new BeastmasterSequenceService(Configuration, countdownService);
        var crucibleItemService = new BeastmasterCrucibleItemService();
        ruleService = new BeastmasterRuleService(Configuration, crucibleItemService);
        var debugDataService = new BeastmasterDebugDataService(countdownService);
        navigationService = new BeastmasterNavigationService(pluginInterface, Configuration);
        catalogChatTracker = new BeastmasterCatalogChatTracker(Configuration, progressService);
        autoCaptureService = new BeastmasterAutoCaptureService(Configuration, sequenceService, ruleService);
        ui = new PluginUI(Configuration, progressService, questService, navigationService, debugDataService, autoCaptureService, catalogSyncService, achievementSyncService, notebookSyncService, sequenceService, ruleService, petPartyService);

        DalamudApi.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手；子命令：输出、暂停、恢复、关闭、倒计时 [秒数]、取消倒计时。",
        });
        DalamudApi.Commands.AddHandler(ChineseCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手；子命令：输出、暂停、恢复、关闭、倒计时 [秒数]、取消倒计时。",
        });

        pluginInterface.UiBuilder.Draw += ui.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ui.OpenMainWindow;
        pluginInterface.UiBuilder.OpenConfigUi += ui.OpenMainWindow;

        DalamudApi.Log.Information("Beastmaster assistant loaded.");
    }

    public void Dispose()
    {
        DalamudApi.PluginInterface.UiBuilder.Draw -= ui.Draw;
        DalamudApi.PluginInterface.UiBuilder.OpenMainUi -= ui.OpenMainWindow;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi -= ui.OpenMainWindow;
        DalamudApi.Commands.RemoveHandler(CommandName);
        DalamudApi.Commands.RemoveHandler(ChineseCommandName);
        catalogChatTracker.Dispose();
        autoCaptureService.Dispose();
        countdownService.Dispose();
        catalogSyncService.Dispose();
        achievementSyncService.Dispose();
        resultProgressService.Dispose();
        notebookProgressService.Dispose();
        notebookSyncService.Dispose();
        petPartyService.Dispose();
        navigationService.Dispose();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            ui.OpenMainWindow();
            return;
        }

        if (trimmed.StartsWith("倒计时", StringComparison.Ordinal) || trimmed.StartsWith("countdown", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
            if (string.IsNullOrEmpty(remainder))
            {
                countdownService.StartCustomCountdown(10f);
            }
            else if (float.TryParse(remainder, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                countdownService.StartCustomCountdown(seconds);
            }
            else
            {
                DalamudApi.ChatGui.Print("[驯兽师助手] 用法：/驯兽师 倒计时 [秒数]（默认 10 秒）");
            }

            return;
        }

        if (trimmed.StartsWith("取消倒计时", StringComparison.Ordinal) || trimmed.StartsWith("cancelcountdown", StringComparison.OrdinalIgnoreCase))
        {
            countdownService.CancelCustomCountdown();
            return;
        }

        switch (trimmed)
        {
            case "输出":
            case "output":
                if (!autoCaptureService.IsEnabled)
                {
                    autoCaptureService.SetEnabled(true);
                    autoCaptureService.SetPaused(false);
                }
                else
                {
                    autoCaptureService.SetPaused(!autoCaptureService.IsPaused);
                    DalamudApi.ChatGui.Print(autoCaptureService.IsPaused
                        ? "[驯兽师助手] 自动输出已暂停。"
                        : "[驯兽师助手] 自动输出已恢复。");
                }

                return;
            case "暂停":
            case "pause":
                autoCaptureService.SetPaused(true);
                DalamudApi.ChatGui.Print("[驯兽师助手] 自动输出已暂停。使用 /驯兽师 恢复继续输出。");
                return;
            case "恢复":
            case "resume":
                if (!autoCaptureService.IsEnabled)
                {
                    DalamudApi.ChatGui.Print("[驯兽师助手] 自动输出尚未开启，请先使用 /驯兽师 输出。");
                    return;
                }

                autoCaptureService.SetPaused(false);
                DalamudApi.ChatGui.Print("[驯兽师助手] 自动输出已恢复。");
                return;
            case "关闭":
            case "off":
                autoCaptureService.SetEnabled(false);
                return;
            default:
                ui.OpenMainWindow();
                return;
        }
    }
}
