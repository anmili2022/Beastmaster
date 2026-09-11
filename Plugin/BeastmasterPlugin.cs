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
    private readonly BeastmasterCountdownService countdownService;
    private readonly BeastmasterSequenceService sequenceService;

    public string Name => "Beastmaster";

    public BeastmasterConfiguration Configuration { get; }

    public BeastmasterPlugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);

        Configuration = pluginInterface.GetPluginConfig() as BeastmasterConfiguration
            ?? new BeastmasterConfiguration();
        Configuration.Initialize(pluginInterface);
        var progressService = new BeastmasterProgressService(Configuration);
        catalogSyncService = new BeastmasterCatalogSyncService(progressService);
        catalogSyncService.Start();
        var questService = new BeastmasterQuestService();
        countdownService = new BeastmasterCountdownService(Configuration);
        sequenceService = new BeastmasterSequenceService(Configuration, countdownService);
        var debugDataService = new BeastmasterDebugDataService(countdownService);
        navigationService = new BeastmasterNavigationService(pluginInterface, Configuration);
        catalogChatTracker = new BeastmasterCatalogChatTracker(Configuration, progressService);
        autoCaptureService = new BeastmasterAutoCaptureService(Configuration, sequenceService);
        ui = new PluginUI(Configuration, progressService, questService, navigationService, debugDataService, autoCaptureService, catalogSyncService, sequenceService);

        DalamudApi.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手；子命令：输出、暂停、恢复、关闭。",
        });
        DalamudApi.Commands.AddHandler(ChineseCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手；子命令：输出、暂停、恢复、关闭。",
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
        navigationService.Dispose();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim())
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
