using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 17;
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
    public bool BasicComboEnabled { get; set; } = true;
    public float CaptureHpThreshold { get; set; } = 80f;
    public bool ShowGaugeInOverlay { get; set; }
    public bool AdvancedActionsEnabled { get; set; }
    public bool BeastHeartCooperationEnabled { get; set; }
    public bool BeastSoulCooperationEnabled { get; set; }
    public bool PhysicalThirdFormEnabled { get; set; }
    public bool MagicalThirdFormEnabled { get; set; }
    public bool AutoWhistleEnabled { get; set; }
    public bool AutoFinalStrikeEnabled { get; set; }
    public float AutoFinalStrikeHpThreshold { get; set; } = 20f;
    public bool ArenaKeepAttentionEnabled { get; set; }
    public bool ArenaKeepProvokeEnabled { get; set; }
    public bool AutoReleaseEnabled { get; set; } = true;
    public bool WhistleRotationEnabled { get; set; }
    public bool ForceCaptureEnabled { get; set; }
    public bool ActiveAttackEnabled { get; set; }
    public bool AutoFinalStrikeWhistleOneEnabled { get; set; }
    public bool AutoFinalStrikeWhistleTwoEnabled { get; set; }
    public bool AutoFinalStrikeWhistleThreeEnabled { get; set; }
    public float AutoFinalStrikeWhistleOneHpThreshold { get; set; } = 20f;
    public float AutoFinalStrikeWhistleTwoHpThreshold { get; set; } = 20f;
    public float AutoFinalStrikeWhistleThreeHpThreshold { get; set; } = 20f;
    public bool AutoFinalStrikeWaitForRelease { get; set; }
    public bool WaterOpenerSequenceEnabled { get; set; }
    public bool SequenceChatMessagesEnabled { get; set; }
    public int SelectedSequenceIndex { get; set; }
    public List<BeastmasterSequenceDefinition> Sequences { get; set; } = [];
    public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter { get; set; }
        = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        ProgressByCharacter ??= new Dictionary<string, BeastmasterCharacterProgress>(StringComparer.Ordinal);
        Sequences ??= [];
        if (Sequences.Count == 0)
        {
            Sequences.Add(BeastmasterSequenceDefinition.CreateWaterOpener());
            Save();
        }
        SelectedSequenceIndex = Math.Clamp(SelectedSequenceIndex, 0, Sequences.Count - 1);

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

        if (Version < 11)
        {
            if (!AdvancedActionsEnabled)
            {
                BeastHeartCooperationEnabled = false;
                BeastSoulCooperationEnabled = false;
                PhysicalThirdFormEnabled = false;
                MagicalThirdFormEnabled = false;
                AutoReleaseEnabled = false;
            }

            BasicComboEnabled = true;
            AutoWhistleEnabled = false;
            Version = 11;
            Save();
        }

        if (Version < 12)
        {
            AutoFinalStrikeEnabled = false;
            AutoFinalStrikeHpThreshold = 20f;
            Version = 12;
            Save();
        }

        AutoFinalStrikeHpThreshold = Math.Clamp(AutoFinalStrikeHpThreshold, 1f, 100f);

        if (Version < 13)
        {
            ArenaKeepAttentionEnabled = false;
            ArenaKeepProvokeEnabled = false;
            Version = 13;
            Save();
        }

        if (Version < 14)
        {
            ForceCaptureEnabled = false;
            Version = 14;
            Save();
        }

        if (Version < 15)
        {
            ActiveAttackEnabled = true;
            Version = 15;
            Save();
        }

        if (Version < 16)
        {
            AutoFinalStrikeWhistleOneEnabled = AutoFinalStrikeEnabled;
            AutoFinalStrikeWhistleTwoEnabled = AutoFinalStrikeEnabled;
            AutoFinalStrikeWhistleThreeEnabled = AutoFinalStrikeEnabled;
            AutoFinalStrikeWhistleOneHpThreshold = AutoFinalStrikeHpThreshold;
            AutoFinalStrikeWhistleTwoHpThreshold = AutoFinalStrikeHpThreshold;
            AutoFinalStrikeWhistleThreeHpThreshold = AutoFinalStrikeHpThreshold;
            AutoFinalStrikeWaitForRelease = false;
            WaterOpenerSequenceEnabled = false;
            Version = 16;
            Save();
        }

        if (Version < 17)
        {
            if (!Sequences.Any(sequence => sequence.Name == "测试序列"))
            {
                Sequences.Add(BeastmasterSequenceDefinition.CreateTestSequence());
            }

            Version = 17;
            Save();
        }

        AutoFinalStrikeWhistleOneHpThreshold = Math.Clamp(AutoFinalStrikeWhistleOneHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleTwoHpThreshold = Math.Clamp(AutoFinalStrikeWhistleTwoHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleThreeHpThreshold = Math.Clamp(AutoFinalStrikeWhistleThreeHpThreshold, 1f, 100f);
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
