namespace Beastmaster;

public enum BeastmasterRuleAutoOutputTarget
{
    BasicCombo,
    Capture,
    AutoWhistle,
    FinalStrike,
    Release,
    RecoveryItem,
    BeastHeartCooperation,
    BeastSoulCooperation,
    PhysicalThirdForm,
    MagicalThirdForm,
    Drum,
    Cheer,
    SafeShield,
    Borrow,
    BeastSkill,
}

public sealed class BeastmasterRuleActionOverrides
{
    private readonly bool?[] booleanOverrides = new bool?[Enum.GetValues<BeastmasterRuleAutoOutputTarget>().Length];
    private readonly float[] finalStrikeThresholds = new float[3];
    private readonly bool[] finalStrikeWhistleEnabled = new bool[3];
    private readonly bool[] finalStrikeWhistleOverridden = new bool[3];
    private readonly float[] releaseThresholds = new float[3];
    private readonly bool[] releaseWhistleEnabled = new bool[3];
    private readonly bool[] releaseWhistleOverridden = new bool[3];
    private bool finalStrikeWaitForRelease;
    private bool finalStrikeWaitForReleaseOverridden;
    private bool releaseBossOnly;
    private bool releaseBossOnlyOverridden;

    public bool Any => booleanOverrides.Any(value => value.HasValue)
        || finalStrikeWhistleOverridden.Any(value => value)
        || releaseWhistleOverridden.Any(value => value)
        || finalStrikeWaitForReleaseOverridden
        || releaseBossOnlyOverridden;

    public void Clear()
    {
        Array.Clear(booleanOverrides);
        Array.Clear(finalStrikeWhistleOverridden);
        Array.Clear(releaseWhistleOverridden);
        Array.Clear(finalStrikeWhistleEnabled);
        Array.Clear(releaseWhistleEnabled);
        Array.Clear(finalStrikeThresholds);
        Array.Clear(releaseThresholds);
        finalStrikeWaitForRelease = false;
        finalStrikeWaitForReleaseOverridden = false;
        releaseBossOnly = false;
        releaseBossOnlyOverridden = false;
    }

    public void SetBoolean(BeastmasterRuleAutoOutputTarget target, bool value)
        => booleanOverrides[(int)target] = value;

    public bool GetBoolean(BeastmasterRuleAutoOutputTarget target, bool fallback)
        => booleanOverrides[(int)target] ?? fallback;

    public bool IsBooleanOverridden(BeastmasterRuleAutoOutputTarget target)
        => booleanOverrides[(int)target].HasValue;

    public void SetFinalStrikeWhistle(int index, bool enabled, float threshold)
    {
        if (index is < 0 or > 2) return;
        finalStrikeWhistleEnabled[index] = enabled;
        finalStrikeThresholds[index] = threshold;
        finalStrikeWhistleOverridden[index] = true;
    }

    public bool TryGetFinalStrikeWhistle(int index, out bool enabled, out float threshold)
    {
        enabled = false;
        threshold = 0f;
        if (index is < 0 or > 2 || !finalStrikeWhistleOverridden[index]) return false;
        enabled = finalStrikeWhistleEnabled[index];
        threshold = finalStrikeThresholds[index];
        return true;
    }

    public void SetFinalStrikeWaitForRelease(bool value)
    {
        finalStrikeWaitForRelease = value;
        finalStrikeWaitForReleaseOverridden = true;
    }

    public bool GetFinalStrikeWaitForRelease(bool fallback)
        => finalStrikeWaitForReleaseOverridden ? finalStrikeWaitForRelease : fallback;

    public void SetReleaseWhistle(int index, bool enabled, float threshold)
    {
        if (index is < 0 or > 2) return;
        releaseWhistleEnabled[index] = enabled;
        releaseThresholds[index] = threshold;
        releaseWhistleOverridden[index] = true;
    }

    public bool TryGetReleaseWhistle(int index, out bool enabled, out float threshold)
    {
        enabled = false;
        threshold = 0f;
        if (index is < 0 or > 2 || !releaseWhistleOverridden[index]) return false;
        enabled = releaseWhistleEnabled[index];
        threshold = releaseThresholds[index];
        return true;
    }

    public void SetReleaseBossOnly(bool value)
    {
        releaseBossOnly = value;
        releaseBossOnlyOverridden = true;
    }

    public bool GetReleaseBossOnly(bool fallback)
        => releaseBossOnlyOverridden ? releaseBossOnly : fallback;
}
