using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    [NonSerialized]
    private DateTime lastSaveFailureUtc = DateTime.MinValue;

    [NonSerialized]
    private readonly SemaphoreSlim saveSemaphore = new(1, 1);

    [NonSerialized]
    private readonly object saveTaskGate = new();

    [NonSerialized]
    private Task pendingSaveTask = Task.CompletedTask;

    public int Version { get; set; } = 45;
    public string SelectedStageKey { get; set; } = string.Empty;
    public string SelectedMainSection { get; set; } = "quests";
    public bool HideCompletedQuests { get; set; }
    public bool HideCompletedAchievements { get; set; }
    public bool SortCatalogByLocation { get; set; }
    public bool SortCatalogByLevel { get; set; }
    public bool SortCatalogByBeastLevel { get; set; }
    public bool SortCatalogByBeastLevelDescending { get; set; }
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
    public bool AutoReleaseWhistleOneEnabled { get; set; } = true;
    public bool AutoReleaseWhistleTwoEnabled { get; set; } = true;
    public bool AutoReleaseWhistleThreeEnabled { get; set; } = true;
    public bool AutoReleaseBossOnly { get; set; }
    public float AutoReleaseWhistleOneTargetHpThreshold { get; set; } = 100f;
    public float AutoReleaseWhistleTwoTargetHpThreshold { get; set; } = 100f;
    public float AutoReleaseWhistleThreeTargetHpThreshold { get; set; } = 100f;
    public bool AutoDrumEnabled { get; set; }
    public bool AutoCheerEnabled { get; set; }
    public bool AutoSafeShieldEnabled { get; set; }
    public bool AutoBorrowEnabled { get; set; }
    public bool AutoBeastSkillEnabled { get; set; }
    public bool AutoRecoveryItemEnabled { get; set; }
    public float AutoRecoveryItemHpThreshold { get; set; } = 30f;
    public bool AutoRecoveryItemDiagnosticsEnabled { get; set; }
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
    public int SelectedPartyPresetIndex { get; set; }
    public string SelectedArenaTab { get; set; } = "party";
    public List<BeastmasterPartyPreset> PartyPresets { get; set; } = [];
    public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter { get; set; }
        = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        ProgressByCharacter ??= new Dictionary<string, BeastmasterCharacterProgress>(StringComparer.Ordinal);
        foreach (var progress in ProgressByCharacter.Values)
        {
            progress.CompletedObjectives ??= new HashSet<string>(StringComparer.Ordinal);
            progress.BeastProgress ??= [];
            progress.CompletedAchievements ??= [];
        }
        Sequences ??= [];
        RuleSets ??= [];
        PartyPresets ??= [];
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

        if (Version < 26)
        {
            AutoSafeShieldEnabled = false;
            Version = 26;
            Save();
        }

        if (Version < 27)
        {
            AutoRecoveryItemEnabled = false;
            AutoRecoveryItemHpThreshold = 30f;
            Version = 27;
            Save();
        }

        if (Version < 28)
        {
            foreach (var rs in RuleSets)
                foreach (var r in rs.Rules)
                {
                    if (r.ActionType == 0) r.ActionType = BeastmasterRuleActionType.Skill;
                }
            Version = 28;
            Save();
        }

        if (Version < 29)
        {
            foreach (var rs in RuleSets)
                foreach (var rule in rs.Rules)
                    if (rule.ActionType == BeastmasterRuleActionType.CrucibleItem
                        && rule.CrucibleItemId == 128)
                        rule.CrucibleItemType = BeastmasterCrucibleItemType.Fang;
            Version = 29;
            Save();
        }

        if (Version < 30)
        {
            var defaultRuleSet = RuleSets.FirstOrDefault(ruleSet => ruleSet.Name == "默认规则集");
            if (defaultRuleSet != null && !defaultRuleSet.Rules.Any(rule => rule.Name == "最终爆发-1层"))
            {
                defaultRuleSet.Rules.Add(new BeastmasterRuleDefinition
                {
                    Name = "最终爆发-1层",
                    Enabled = true,
                    ConditionType = BeastmasterRuleConditionType.TargetDataId,
                    DataId = 19344,
                    ActionType = BeastmasterRuleActionType.CrucibleItem,
                    CrucibleItemType = BeastmasterCrucibleItemType.Fang,
                });
            }

            Version = 30;
            Save();
        }

        if (Version < 31)
        {
            foreach (var progress in ProgressByCharacter.Values)
            {
                progress.BeastProgress ??= [];
            }
            Version = 31;
            Save();
        }

        if (Version < 32)
        {
            PartyPresets ??= [];
            Version = 32;
            Save();
        }

        if (Version < 33)
        {
            SortCatalogByBeastLevel = false;
            SortCatalogByBeastLevelDescending = false;
            Version = 33;
            Save();
        }

        if (Version < 34)
        {
            SelectedArenaTab = "party";
            Version = 34;
            Save();
        }

        if (Version < 35)
        {
            foreach (var progress in ProgressByCharacter.Values)
            {
                progress.CompletedAchievements ??= [];
            }
            Version = 35;
            Save();
        }

        if (Version < 36)
        {
            AutoBorrowEnabled = false;
            AutoBeastSkillEnabled = false;
            Version = 36;
            Save();
        }

        if (Version < 37)
        {
            HideCompletedAchievements = false;
            Version = 37;
            Save();
        }

        if (Version < 38)
        {
            foreach (var ruleSet in RuleSets)
            {
                foreach (var rule in ruleSet.Rules)
                {
                    rule.HpThreshold = Math.Clamp(rule.HpThreshold <= 0f ? 50f : rule.HpThreshold, 1f, 100f);
                }
            }
            Version = 38;
            Save();
        }

        if (Version < 39)
        {
            foreach (var ruleSet in RuleSets)
            {
                foreach (var rule in ruleSet.Rules)
                {
                    var oldConditionType = (int)rule.ConditionType;
                    if (oldConditionType is 6 or 7)
                    {
                        rule.ConditionType = BeastmasterRuleConditionType.SelfHp;
                        rule.HpCondition = oldConditionType == 7
                            ? BeastmasterRuleHpCondition.Below
                            : BeastmasterRuleHpCondition.Above;
                    }
                }
            }
            Version = 39;
            Save();
        }

        if (Version < 40)
        {
            Version = 40;
            Save();
        }

        if (Version < 41)
        {
            foreach (var ruleSet in RuleSets)
            {
                foreach (var rule in ruleSet.Rules)
                {
                    rule.Conditions ??= [];
                    rule.EnsureConditions();
                    rule.ConditionJoinMode = Enum.IsDefined(rule.ConditionJoinMode)
                        ? rule.ConditionJoinMode
                        : BeastmasterRuleConditionJoinMode.All;
                }
            }
            Version = 41;
            Save();
        }

        if (Version < 42)
        {
            Version = 42;
            Save();
        }

        if (Version < 43)
        {
            AutoReleaseWhistleOneEnabled = true;
            AutoReleaseWhistleTwoEnabled = true;
            AutoReleaseWhistleThreeEnabled = true;
            AutoReleaseWhistleOneTargetHpThreshold = 100f;
            AutoReleaseWhistleTwoTargetHpThreshold = 100f;
            AutoReleaseWhistleThreeTargetHpThreshold = 100f;
            Version = 43;
            Save();
        }

        if (Version < 44)
        {
            AutoReleaseBossOnly = false;
            Version = 44;
            Save();
        }

        if (Version < 45)
        {
            AutoRecoveryItemDiagnosticsEnabled = false;
            Version = 45;
            Save();
        }

        if (PartyPresets.Count == 0)
        {
            PartyPresets.Add(new BeastmasterPartyPreset());
            Save();
        }
        SelectedPartyPresetIndex = Math.Clamp(SelectedPartyPresetIndex, 0, PartyPresets.Count - 1);

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
            foreach (var rule in ruleSet.Rules)
            {
                rule.Conditions ??= [];
                rule.EnsureConditions();
            }
        }
        SelectedRuleSetIndex = Math.Clamp(SelectedRuleSetIndex, 0, RuleSets.Count - 1);
        SelectedRuleIndex = Math.Clamp(SelectedRuleIndex, 0, Math.Max(0, RuleSets[SelectedRuleSetIndex].Rules.Count - 1));

        AutoFinalStrikeWhistleOneHpThreshold = Math.Clamp(AutoFinalStrikeWhistleOneHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleTwoHpThreshold = Math.Clamp(AutoFinalStrikeWhistleTwoHpThreshold, 1f, 100f);
        AutoFinalStrikeWhistleThreeHpThreshold = Math.Clamp(AutoFinalStrikeWhistleThreeHpThreshold, 1f, 100f);
        AutoReleaseWhistleOneTargetHpThreshold = Math.Clamp(AutoReleaseWhistleOneTargetHpThreshold, 1f, 100f);
        AutoReleaseWhistleTwoTargetHpThreshold = Math.Clamp(AutoReleaseWhistleTwoTargetHpThreshold, 1f, 100f);
        AutoReleaseWhistleThreeTargetHpThreshold = Math.Clamp(AutoReleaseWhistleThreeTargetHpThreshold, 1f, 100f);
        AutoRecoveryItemHpThreshold = Math.Clamp(AutoRecoveryItemHpThreshold, 1f, 100f);
    }

    public void Save()
    {
        if (pluginInterface == null)
        {
            return;
        }

        saveSemaphore.Wait();
        try
        {
            pluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if (now - lastSaveFailureUtc >= TimeSpan.FromSeconds(5))
            {
                lastSaveFailureUtc = now;
                DalamudApi.Log.Error(ex,
                    "Beastmaster 配置保存失败。请检查 pluginConfigs\\Beastmaster.json 的写入权限。",
                    Array.Empty<object>());
            }
        }
        finally
        {
            saveSemaphore.Release();
        }
    }

    public void QueueSave()
    {
        lock (saveTaskGate)
        {
            pendingSaveTask = pendingSaveTask.ContinueWith(
                _ => Save(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    public void FlushPendingSaves()
    {
        Task task;
        lock (saveTaskGate)
        {
            task = pendingSaveTask;
        }

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "等待 Beastmaster 配置后台保存完成时失败。", Array.Empty<object>());
        }
    }
}
