using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 10;
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
    public bool AutoOutputPaused { get; set; }
    public bool AutoCaptureTryCapture { get; set; } = true;
    public float CaptureHpThreshold { get; set; } = 80f;
    public bool ShowGaugeInOverlay { get; set; }
    public bool AdvancedActionsEnabled { get; set; }
    public bool BeastHeartCooperationEnabled { get; set; }
    public bool BeastSoulCooperationEnabled { get; set; }
    public bool PhysicalThirdFormEnabled { get; set; }
    public bool MagicalThirdFormEnabled { get; set; }
    public bool AutoReleaseEnabled { get; set; } = true;
    public bool WhistleRotationEnabled { get; set; }
    public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter { get; set; }
        = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        ProgressByCharacter ??= new Dictionary<string, BeastmasterCharacterProgress>(StringComparer.Ordinal);

        if (Version < 8)
        {
            CaptureHpThreshold = Math.Clamp(CaptureHpThreshold, 1f, 100f);
            Version = 8;
            Save();
        }

        if (Version < 9)
        {
            PhysicalThirdFormEnabled = false;
            MagicalThirdFormEnabled = false;
            Version = 9;
            Save();
        }

        if (PhysicalThirdFormEnabled && MagicalThirdFormEnabled)
        {
            MagicalThirdFormEnabled = false;
            Save();
        }

        if (Version < 10)
        {
            AutoOutputPaused = false;
            Version = 10;
            Save();
        }
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
