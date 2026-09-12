using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 25;
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
    public bool OverlayThreeColumnMode { get; set; }
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
    public bool AutoDrumEnabled { get; set; }
    public bool AutoCheerEnabled { get; set; }
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
    public bool RuleModeEnabled { get; set; } = true;
    public bool RuleDiagnosticsEnabled { get; set; }
    public bool AutoOutputDiagnosticsEnabled { get; set; }
    public bool RangeWaitChatMessagesEnabled { get; set; }
    public int SelectedRuleSetIndex { get; set; }
    public int SelectedRuleIndex { get; set; }
    public List<BeastmasterRuleSetDefinition> RuleSets { get; set; } = [];
    public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter { get; set; }
        = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        ProgressByCharacter ??= new Dictionary<string, BeastmasterCharacterProgress>(StringComparer.Ordinal);
        Sequences ??= [];
        RuleSets ??= [];
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

        if (Version < 18)
        {
            RuleModeEnabled = true;
            RuleSets = [BeastmasterRuleSetDefinition.CreateBuiltInArenaRules(
                ArenaKeepAttentionEnabled,
                ArenaKeepProvokeEnabled)];
            Version = 18;
            Save();
        }

        if (Version < 19)
        {
            var placeholder = RuleSets.Count == 1
                && RuleSets[0].Name == "默认规则集"
                && RuleSets[0].Rules.Count == 0;
            var hasBuiltInArenaRules = RuleSets.Any(ruleSet =>
                ruleSet.Rules.Any(rule => rule.ConditionType == BeastmasterRuleConditionType.SelfStatus
                    && rule.StatusCondition == BeastmasterRuleStatusCondition.Missing
                    && rule.ConditionId == 2413
                    && rule.ActionId == 46751)
                && ruleSet.Rules.Any(rule => rule.ConditionType == BeastmasterRuleConditionType.SelfStatus
                    && rule.StatusCondition == BeastmasterRuleStatusCondition.Missing
                    && rule.ConditionId == 5586
                    && rule.ActionId == 46750));
            if (!hasBuiltInArenaRules)
            {
                var builtIn = BeastmasterRuleSetDefinition.CreateBuiltInArenaRules(
                    ArenaKeepAttentionEnabled,
                    ArenaKeepProvokeEnabled);
                if (placeholder) RuleSets[0] = builtIn;
                else RuleSets.Insert(0, builtIn);
            }

            RuleModeEnabled = true;
            Version = 19;
            Save();
        }

        if (Version < 20)
        {
            RangeWaitChatMessagesEnabled = false;
            Version = 20;
            Save();
        }

        if (Version < 21)
        {
            AutoOutputDiagnosticsEnabled = RangeWaitChatMessagesEnabled;
            Version = 21;
            Save();
        }

        if (Version < 22)
        {
            Version = 22;
            Save();
        }

        if (Version < 23)
        {
            AutoDrumEnabled = false;
            AutoCheerEnabled = false;
            Version = 23;
            Save();
        }

        if (Version < 24)
        {
            RuleDiagnosticsEnabled = false;
            Version = 24;
            Save();
        }

        if (Version < 25)
        {
            OverlayThreeColumnMode = false;
            Version = 25;
            Save();
        }

        if (RuleSets.Count == 0)
        {
            RuleSets.Add(BeastmasterRuleSetDefinition.CreateBuiltInArenaRules(
                ArenaKeepAttentionEnabled,
                ArenaKeepProvokeEnabled));
            RuleModeEnabled = true;
            Save();
        }
        foreach (var ruleSet in RuleSets)
        {
            ruleSet.TerritoryIds ??= [];
            ruleSet.Rules ??= [];
        }
        SelectedRuleSetIndex = Math.Clamp(SelectedRuleSetIndex, 0, RuleSets.Count - 1);
        SelectedRuleIndex = Math.Clamp(SelectedRuleIndex, 0, Math.Max(0, RuleSets[SelectedRuleSetIndex].Rules.Count - 1));

        AutoFinalStrikeWhistleOneHpThreshold = Math.Clamp(AutoFinalStrikeWhistleOneHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleTwoHpThreshold = Math.Clamp(AutoFinalStrikeWhistleTwoHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleThreeHpThreshold = Math.Clamp(AutoFinalStrikeWhistleThreeHpThreshold, 1f, 100f);
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
