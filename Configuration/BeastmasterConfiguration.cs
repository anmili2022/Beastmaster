using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 7;
    public string SelectedStageKey { get; set; } = string.Empty;
    public string SelectedMainSection { get; set; } = "quests";
    public bool HideCompletedQuests { get; set; }
    public bool SortCatalogByLocation { get; set; }
    public bool SortCatalogByLevel { get; set; }
    public bool HideCapturedBeasts { get; set; }
    public bool AutoCompleteCatalogFromChat { get; set; } = true;
    public bool UseFlightNavigation { get; set; } = true;
    public bool SetFlagOnNavigation { get; set; } = true;
    public bool ShowNavigationLogs { get; set; } = true;
    public bool AutoCaptureEnabled { get; set; }
    public bool AutoCaptureTryCapture { get; set; } = true;
    public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter { get; set; }
        = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        ProgressByCharacter ??= new Dictionary<string, BeastmasterCharacterProgress>(StringComparer.Ordinal);

        if (Version < 7)
        {
            Version = 7;
            Save();
        }
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
