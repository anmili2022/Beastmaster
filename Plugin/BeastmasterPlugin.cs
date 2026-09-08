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

    public string Name => "Beastmaster";

    public BeastmasterConfiguration Configuration { get; }

    public BeastmasterPlugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);

        Configuration = pluginInterface.GetPluginConfig() as BeastmasterConfiguration
            ?? new BeastmasterConfiguration();
        Configuration.Initialize(pluginInterface);
        var progressService = new BeastmasterProgressService(Configuration);
        var questService = new BeastmasterQuestService();
        var debugDataService = new BeastmasterDebugDataService();
        navigationService = new BeastmasterNavigationService(pluginInterface, Configuration);
        catalogChatTracker = new BeastmasterCatalogChatTracker(progressService);
        ui = new PluginUI(Configuration, progressService, questService, navigationService, debugDataService);

        DalamudApi.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手。",
        });
        DalamudApi.Commands.AddHandler(ChineseCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开驯兽师助手。",
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
        navigationService.Dispose();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        ui.OpenMainWindow();
    }
}
