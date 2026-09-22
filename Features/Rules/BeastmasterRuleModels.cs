using System.Globalization;
using System.Text;

namespace Beastmaster;

public enum BeastmasterRuleConditionType
{
    SelfStatus,
    TargetStatus,
    DataIdStatus,
    DataIdCast,
    TargetCast,
    TargetDataId,
    SelfHp,
    TargetHp,
    TargetIsBoss,
    CurrentWhistle,
}

public enum BeastmasterRuleActionType
{
    Skill,
    CrucibleItem,
}

public enum BeastmasterCrucibleItemType
{
    Recovery,
    Fang,
    DodgeBook,
    ReflectBook,
    TimeSand,
    StrengthMedicine,
    VampireFang,
    StarSand,
    VampireMedicine,
    RecoverySet,
    Specific,
}

public enum BeastmasterRuleStatusCondition
{
    Present,
    Missing,
}

public enum BeastmasterRuleHpCondition
{
    Above,
    Below,
}

public enum BeastmasterRuleConditionJoinMode
{
    All,
    Any,
}

public enum BeastmasterRuleAreaMode
{
    All,
    Include,
    Exclude,
}

public enum BeastmasterRuleDiagnosticMode
{
    Off,
    Failures,
    Full,
}

[Serializable]
public sealed class BeastmasterRuleDefinition
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "新规则";
    public BeastmasterRuleConditionType ConditionType { get; set; }
    public BeastmasterRuleStatusCondition StatusCondition { get; set; } = BeastmasterRuleStatusCondition.Missing;
    public BeastmasterRuleConditionJoinMode ConditionJoinMode { get; set; } = BeastmasterRuleConditionJoinMode.All;
    public List<BeastmasterRuleCondition> Conditions { get; set; } = [];
    public BeastmasterRuleActionType ActionType { get; set; } = BeastmasterRuleActionType.Skill;
    public BeastmasterCrucibleItemType CrucibleItemType { get; set; } = BeastmasterCrucibleItemType.Recovery;
    public uint DataId { get; set; }
    public uint ConditionId { get; set; }
    public BeastmasterRuleHpCondition HpCondition { get; set; }
    public float HpThreshold { get; set; } = 50f;
    public byte WhistleIndex { get; set; } = 1;
    public uint ActionId { get; set; } = 44879;
    public uint CrucibleItemId { get; set; }

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        Conditions ??= [];
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "规则名称不能为空。";
            return false;
        }

        if (!Enum.IsDefined(ConditionType)
            || !Enum.IsDefined(StatusCondition)
            || !Enum.IsDefined(HpCondition)
            || !Enum.IsDefined(ConditionJoinMode))
        {
            error = "规则检测类型无效。";
            return false;
        }

        if (Conditions.Count > 10)
        {
            error = "每条规则最多配置 10 个检测条件。";
            return false;
        }

        if (Conditions.Count > 0)
        {
            foreach (var condition in Conditions)
            {
                if (!condition.TryValidate(out error))
                {
                    return false;
                }
            }
        }
        else if (!IsTargetDataIdRule && !IsHealthRule && !IsBossRule && !IsWhistleRule && ConditionId == 0)
        {
            error = IsStatusRule ? "BUFFID 必须大于 0。" : "读条 ID 必须大于 0。";
            return false;
        }

        if (Conditions.Count == 0 && IsHealthRule && (!float.IsFinite(HpThreshold) || HpThreshold is < 1f or > 100f))
        {
            error = "自身血量阈值必须在 1%~100% 之间。";
            return false;
        }

        if (Conditions.Count == 0 && RequiresDataId && DataId == 0)
        {
            error = "DataID 必须大于 0。";
            return false;
        }

        if (Conditions.Count == 0 && IsWhistleRule && WhistleIndex is < 1 or > 3)
        {
            error = "兽笛编号必须为 1、2 或 3。";
            return false;
        }

        if (ActionType == BeastmasterRuleActionType.Skill && !BeastmasterRuleActions.IsSupported(ActionId))
        {
            error = $"不支持规则技能 {ActionId}。";
            return false;
        }

        if (ActionType == BeastmasterRuleActionType.CrucibleItem
            && CrucibleItemType == BeastmasterCrucibleItemType.Specific
            && !BeastmasterRuleActions.IsCrucibleItemId(CrucibleItemId))
        {
            error = "请选择有效的奇弈道具。";
            return false;
        }

        return true;
    }

    public bool IsStatusRule
        => ConditionType is BeastmasterRuleConditionType.SelfStatus
            or BeastmasterRuleConditionType.TargetStatus
            or BeastmasterRuleConditionType.DataIdStatus;

    public bool RequiresDataId
        => ConditionType is BeastmasterRuleConditionType.DataIdStatus
            or BeastmasterRuleConditionType.DataIdCast
            or BeastmasterRuleConditionType.TargetDataId;

    public bool IsTargetDataIdRule
        => ConditionType is BeastmasterRuleConditionType.TargetDataId;

    public bool IsBossRule
        => ConditionType is BeastmasterRuleConditionType.TargetIsBoss;

    public bool IsWhistleRule
        => ConditionType is BeastmasterRuleConditionType.CurrentWhistle;

    public bool IsHealthRule
        => ConditionType is BeastmasterRuleConditionType.SelfHp
            or BeastmasterRuleConditionType.TargetHp;

    public void EnsureConditions()
    {
        Conditions ??= [];
        if (Conditions.Count == 0)
        {
            Conditions.Add(new BeastmasterRuleCondition
            {
                Type = ConditionType,
                StatusCondition = StatusCondition,
                DataId = DataId,
                ConditionId = ConditionId,
                HpCondition = HpCondition,
                HpThreshold = HpThreshold,
                WhistleIndex = WhistleIndex,
            });
        }
    }

    public void SyncLegacyFieldsFromFirstCondition()
    {
        if (Conditions is not { Count: > 0 }) return;
        var first = Conditions[0];
        ConditionType = first.Type;
        StatusCondition = first.StatusCondition;
        DataId = first.DataId;
        ConditionId = first.ConditionId;
        HpCondition = first.HpCondition;
        HpThreshold = first.HpThreshold;
        WhistleIndex = first.WhistleIndex;
    }

}

[Serializable]
public sealed class BeastmasterRuleCondition
{
    public BeastmasterRuleConditionType Type { get; set; }
    public BeastmasterRuleStatusCondition StatusCondition { get; set; } = BeastmasterRuleStatusCondition.Missing;
    public uint DataId { get; set; }
    public uint ConditionId { get; set; }
    public BeastmasterRuleHpCondition HpCondition { get; set; }
    public float HpThreshold { get; set; } = 50f;
    public byte WhistleIndex { get; set; } = 1;

    public bool IsStatusRule => Type is BeastmasterRuleConditionType.SelfStatus
        or BeastmasterRuleConditionType.TargetStatus
        or BeastmasterRuleConditionType.DataIdStatus;

    public bool RequiresDataId => Type is BeastmasterRuleConditionType.DataIdStatus
        or BeastmasterRuleConditionType.DataIdCast
        or BeastmasterRuleConditionType.TargetDataId;

    public bool IsHealthRule => Type is BeastmasterRuleConditionType.SelfHp
        or BeastmasterRuleConditionType.TargetHp;

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(StatusCondition) || !Enum.IsDefined(HpCondition))
        {
            error = "条件类型无效。";
            return false;
        }

        if (!IsHealthRule
            && Type != BeastmasterRuleConditionType.TargetDataId
            && Type != BeastmasterRuleConditionType.TargetIsBoss
            && Type != BeastmasterRuleConditionType.CurrentWhistle
            && ConditionId == 0)
        {
            error = IsStatusRule ? "BUFFID 必须大于 0。" : "读条 ID 必须大于 0。";
            return false;
        }

        if (RequiresDataId && DataId == 0)
        {
            error = "DataID 必须大于 0。";
            return false;
        }

        if (IsHealthRule && (!float.IsFinite(HpThreshold) || HpThreshold is < 1f or > 100f))
        {
            error = "血量阈值必须在 1%~100% 之间。";
            return false;
        }

        if (Type == BeastmasterRuleConditionType.CurrentWhistle && WhistleIndex is < 1 or > 3)
        {
            error = "兽笛编号必须为 1、2 或 3。";
            return false;
        }

        return true;
    }
}

[Serializable]
public sealed class BeastmasterRuleSetDefinition
{
    private const int MaximumRules = 100;
    private const int MaximumTerritories = 100;

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "默认规则集";
    public string Description { get; set; } = string.Empty;
    public BeastmasterRuleAreaMode AreaMode { get; set; }
    public List<ushort> TerritoryIds { get; set; } = [];
    public BeastmasterRuleDiagnosticMode DiagnosticMode { get; set; } = BeastmasterRuleDiagnosticMode.Failures;
    public List<BeastmasterRuleDefinition> Rules { get; set; } = [];

    public static BeastmasterRuleSetDefinition CreateBuiltInArenaRules(bool attentionEnabled = false, bool provokeEnabled = false)
        => new()
        {
            Name = "默认规则集",
            Description = "斗兽塔技能",
            Enabled = true,
            AreaMode = BeastmasterRuleAreaMode.Include,
            TerritoryIds = [1339, 1340, 1341, 1342, 1343],
            DiagnosticMode = BeastmasterRuleDiagnosticMode.Failures,
            Rules =
            [
                new()
                {
                    Name = "持续吸引",
                    Enabled = attentionEnabled,
                    ConditionType = BeastmasterRuleConditionType.SelfStatus,
                    StatusCondition = BeastmasterRuleStatusCondition.Missing,
                    ConditionId = 2413,
                    ActionId = 46751,
                },
                new()
                {
                    Name = "持续挑衅",
                    Enabled = provokeEnabled,
                    ConditionType = BeastmasterRuleConditionType.SelfStatus,
                    StatusCondition = BeastmasterRuleStatusCondition.Missing,
                    ConditionId = 5586,
                    ActionId = 46750,
                },
                new()
                {
                    Name = "最终爆发-1层",
                    Enabled = true,
                    ConditionType = BeastmasterRuleConditionType.TargetDataId,
                    DataId = 19344,
                    ActionType = BeastmasterRuleActionType.CrucibleItem,
                    CrucibleItemType = BeastmasterCrucibleItemType.Fang,
                },
            ],
        };

    public bool AppliesTo(ushort territoryId)
        => AreaMode switch
        {
            BeastmasterRuleAreaMode.All => true,
            BeastmasterRuleAreaMode.Include => TerritoryIds.Contains(territoryId),
            BeastmasterRuleAreaMode.Exclude => !TerritoryIds.Contains(territoryId),
            _ => false,
        };

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "规则集名称不能为空。";
            return false;
        }

        if (!Enum.IsDefined(AreaMode) || !Enum.IsDefined(DiagnosticMode))
        {
            error = "规则集区域或诊断模式无效。";
            return false;
        }

        TerritoryIds ??= [];
        Rules ??= [];
        if (TerritoryIds.Count > MaximumTerritories || TerritoryIds.Any(id => id == 0))
        {
            error = $"区域 ID 必须大于 0，且最多配置 {MaximumTerritories} 个。";
            return false;
        }

        if (Rules.Count > MaximumRules)
        {
            error = $"每个规则集最多包含 {MaximumRules} 条规则。";
            return false;
        }

        for (var index = 0; index < Rules.Count; index++)
        {
            if (!Rules[index].TryValidate(out var ruleError))
            {
                error = $"第 {index + 1} 条规则无效：{ruleError}";
                return false;
            }
        }

        return true;
    }

    public string Export()
    {
        var builder = new StringBuilder()
            .AppendLine("BSTRULESET|1")
            .AppendLine($"名称|{Sanitize(Name)}")
            .AppendLine($"说明|{Sanitize(Description)}")
            .AppendLine($"启用|{Enabled}")
            .AppendLine($"区域模式|{AreaMode}")
            .AppendLine($"区域|{string.Join(',', TerritoryIds.Distinct())}")
            .AppendLine($"诊断|{DiagnosticMode}");

        foreach (var rule in Rules)
        {
            rule.EnsureConditions();
            builder.AppendLine()
                .AppendLine("[规则]")
                .AppendLine($"名称|{Sanitize(rule.Name)}")
                .AppendLine($"启用|{rule.Enabled}")
                .AppendLine($"检测|{rule.ConditionType}")
                .AppendLine($"条件|{rule.StatusCondition}")
                .AppendLine($"执行|{rule.ActionType}")
                .AppendLine($"奇弈道具类型|{rule.CrucibleItemType}")
                .AppendLine($"条件关系|{rule.ConditionJoinMode}")
                .AppendLine($"DataId|{rule.DataId}")
                .AppendLine($"检测ID|{rule.ConditionId}")
                .AppendLine($"血量条件|{rule.HpCondition}")
                .AppendLine($"血量阈值|{rule.HpThreshold.ToString(CultureInfo.InvariantCulture)}")
                .AppendLine($"兽笛|{rule.WhistleIndex}")
                .AppendLine($"技能|{rule.ActionId}")
                .AppendLine($"奇弈道具|{rule.CrucibleItemId}");
            for (var conditionIndex = 0; conditionIndex < rule.Conditions.Count; conditionIndex++)
            {
                var condition = rule.Conditions[conditionIndex];
                builder.AppendLine($"条件{conditionIndex}类型|{condition.Type}")
                    .AppendLine($"条件{conditionIndex}条件|{condition.StatusCondition}")
                    .AppendLine($"条件{conditionIndex}DataId|{condition.DataId}")
                    .AppendLine($"条件{conditionIndex}检测ID|{condition.ConditionId}")
                    .AppendLine($"条件{conditionIndex}血量条件|{condition.HpCondition}")
                    .AppendLine($"条件{conditionIndex}血量阈值|{condition.HpThreshold.ToString(CultureInfo.InvariantCulture)}")
                    .AppendLine($"条件{conditionIndex}兽笛|{condition.WhistleIndex}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static bool TryImport(string text, out BeastmasterRuleSetDefinition? ruleSet, out string error)
    {
        ruleSet = null;
        error = string.Empty;
        var lines = text.Replace("\r", string.Empty).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "BSTRULESET|1")
        {
            error = "第一行必须是 BSTRULESET|1。";
            return false;
        }

        var result = new BeastmasterRuleSetDefinition();
        BeastmasterRuleDefinition? currentRule = null;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;
            if (line == "[规则]")
            {
                currentRule = new BeastmasterRuleDefinition();
                result.Rules.Add(currentRule);
                continue;
            }

            var separator = line.IndexOf('|');
            if (separator <= 0)
            {
                error = $"第 {index + 1} 行格式错误。";
                return false;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            if (!TryAssign(result, currentRule, key, value))
            {
                error = $"第 {index + 1} 行的字段或值无效：{key}。";
                return false;
            }
        }

        result.TerritoryIds = result.TerritoryIds.Distinct().ToList();
        result.EnsureImportedConditions();
        if (!result.TryValidate(out error)) return false;
        ruleSet = result;
        return true;
    }

    private static bool TryAssign(BeastmasterRuleSetDefinition ruleSet, BeastmasterRuleDefinition? rule, string key, string value)
    {
        if (rule == null)
        {
            switch (key)
            {
                case "名称": ruleSet.Name = value; return true;
                case "说明": ruleSet.Description = value; return true;
                case "启用" when bool.TryParse(value, out var enabled): ruleSet.Enabled = enabled; return true;
                case "区域模式" when Enum.TryParse<BeastmasterRuleAreaMode>(value, out var areaMode): ruleSet.AreaMode = areaMode; return true;
                case "诊断" when Enum.TryParse<BeastmasterRuleDiagnosticMode>(value, out var diagnostic): ruleSet.DiagnosticMode = diagnostic; return true;
                case "区域":
                    if (value.Length == 0) return true;
                    foreach (var item in value.Split(','))
                    {
                        if (!ushort.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var territory)) return false;
                        ruleSet.TerritoryIds.Add(territory);
                    }
                    return true;
                default: return false;
            }
        }

        if (key.StartsWith("条件", StringComparison.Ordinal)
            && key.Length > 2
            && char.IsDigit(key[2])
            && TryAssignIndexedCondition(rule, key, value))
        {
            return true;
        }

        switch (key)
        {
            case "名称": rule.Name = value; return true;
            case "启用" when bool.TryParse(value, out var enabled): rule.Enabled = enabled; return true;
            case "检测" when Enum.TryParse<BeastmasterRuleConditionType>(value, out var condition): rule.ConditionType = condition; return true;
            case "条件" when Enum.TryParse<BeastmasterRuleStatusCondition>(value, out var status): rule.StatusCondition = status; return true;
            case "条件关系" when Enum.TryParse<BeastmasterRuleConditionJoinMode>(value, out var joinMode): rule.ConditionJoinMode = joinMode; return true;
            case "执行" when Enum.TryParse<BeastmasterRuleActionType>(value, out var actionType): rule.ActionType = actionType; return true;
            case "奇弈道具类型" when Enum.TryParse<BeastmasterCrucibleItemType>(value, out var itemType): rule.CrucibleItemType = itemType; return true;
            case "DataId" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var dataId): rule.DataId = dataId; return true;
            case "检测ID" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var conditionId): rule.ConditionId = conditionId; return true;
            case "血量条件" when Enum.TryParse<BeastmasterRuleHpCondition>(value, out var hpCondition): rule.HpCondition = hpCondition; return true;
            case "血量阈值" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hpThreshold): rule.HpThreshold = hpThreshold; return true;
            case "兽笛" when byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var whistleIndex): rule.WhistleIndex = whistleIndex; return true;
            case "技能" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var actionId): rule.ActionId = actionId; return true;
            case "奇弈道具" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId): rule.CrucibleItemId = itemId; return true;
            default: return false;
        }
    }

    private static bool TryAssignIndexedCondition(BeastmasterRuleDefinition rule, string key, string value)
    {
        var fieldStart = 2;
        while (fieldStart < key.Length && char.IsDigit(key[fieldStart])) fieldStart++;
        if (fieldStart == 2
            || !int.TryParse(key[2..fieldStart], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index is < 0 or >= 10)
        {
            return false;
        }

        while (rule.Conditions.Count <= index) rule.Conditions.Add(new BeastmasterRuleCondition());
        var condition = rule.Conditions[index];
        var field = key[fieldStart..];
        return field switch
        {
            "类型" when Enum.TryParse<BeastmasterRuleConditionType>(value, out var type) => Set(() => condition.Type = type),
            "条件" when Enum.TryParse<BeastmasterRuleStatusCondition>(value, out var status) => Set(() => condition.StatusCondition = status),
            "DataId" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var dataId) => Set(() => condition.DataId = dataId),
            "检测ID" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) => Set(() => condition.ConditionId = id),
            "血量条件" when Enum.TryParse<BeastmasterRuleHpCondition>(value, out var hpCondition) => Set(() => condition.HpCondition = hpCondition),
            "血量阈值" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) => Set(() => condition.HpThreshold = threshold),
            "兽笛" when byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var whistleIndex) => Set(() => condition.WhistleIndex = whistleIndex),
            _ => false,
        };

        static bool Set(Action action) { action(); return true; }
    }

    private void EnsureImportedConditions()
    {
        foreach (var rule in Rules) rule.EnsureConditions();
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace('|', ' ');
}

public static class BeastmasterRuleActions
{
    public static readonly (uint ActionId, string Name)[] Supported =
    [
        (44879, "碎击斩"), (44880, "捕获"), (44881, "一号兽笛"), (44883, "碎咬斧"),
        (44884, "闪崩斧"), (44885, "裂盾劈"), (44886, "魔兽技"), (44887, "寒风斧"),
        (44888, "回旋斧"), (44889, "风啸斧"), (44890, "释放"), (44891, "最后一击"),
        (44892, "二号兽笛"), (44893, "盾牌冲击"), (44894, "三号兽笛"), (44895, "借用"),
        (44896, "百兽肤"), (44897, "百虫肤"), (44898, "有翼飞掠"), (44899, "草木播种"),
        (44900, "水栖波"), (44901, "甲鳞肤"), (44902, "咒具碎魂"), (44903, "死尸净化"),
        (44904, "声援"), (44905, "鼓劲"), (44930, "狂猛怒火"), (44931, "利鹰坚爪"),
        (44932, "升魔暴落"), (44933, "灾祸天翔"), (46750, "挑衅"), (46751, "吸引注意"),
        (47093, "驯兽师大招"),
    ];

    public static bool IsSupported(uint actionId)
        => Supported.Any(action => action.ActionId == actionId);

    public static bool UsesAdjustedActionId(uint actionId)
        => actionId is 44886 or 44890 or 44895;

    public static bool RequiresTarget(uint actionId)
        => actionId is 44879 or 44880 or 44883 or 44884 or 44885 or 44887 or 44888 or 44889
            or 44890 or 44891 or 44893 or 44930 or 44931 or 44932 or 44933 or 46750 or 46751 or 47093;

    public static bool IsCrucibleItemId(uint itemId)
        => itemId is >= 76 and <= 143;

    public static bool IsCrucibleItemFriendly(uint itemId)
        => !RequiresCrucibleItemTarget(itemId);

    public static bool RequiresCrucibleItemTarget(uint itemId)
        => itemId is 84 or 96 or 128 or 129 or 130 or 131 or 132 or 133 or 134 or 139;

    public static string GetCrucibleItemTypeName(BeastmasterCrucibleItemType itemType)
        => itemType switch
        {
            BeastmasterCrucibleItemType.Recovery => "恢复类道具",
            BeastmasterCrucibleItemType.Fang => "各种牙",
            BeastmasterCrucibleItemType.DodgeBook => "闪躲之书",
            BeastmasterCrucibleItemType.ReflectBook => "反射之书",
            BeastmasterCrucibleItemType.TimeSand => "时之沙",
            BeastmasterCrucibleItemType.StrengthMedicine => "魔兽刚力药",
            BeastmasterCrucibleItemType.VampireFang => "吸血鬼之牙",
            BeastmasterCrucibleItemType.StarSand => "星之沙",
            BeastmasterCrucibleItemType.VampireMedicine => "魔兽吸血药",
            BeastmasterCrucibleItemType.RecoverySet => "魔兽恢复药套装",
            BeastmasterCrucibleItemType.Specific => "指定道具",
            _ => itemType.ToString(),
        };

    public static string GetCrucibleItemTypeDescription(BeastmasterCrucibleItemType itemType)
        => itemType switch
        {
            BeastmasterCrucibleItemType.Recovery => "按恢复药优先级选择可用道具，对自身恢复 HP。",
            BeastmasterCrucibleItemType.Fang => "按火、冰、水、雷、土、风之牙及星之沙的优先级，对当前敌对目标造成属性攻击。",
            BeastmasterCrucibleItemType.DodgeBook => "对自身使用闪躲之书，获得闪躲效果。",
            BeastmasterCrucibleItemType.ReflectBook => "对自身使用反射之书，获得反射效果。",
            BeastmasterCrucibleItemType.TimeSand => "对自身使用时之沙，获得时之沙效果。",
            BeastmasterCrucibleItemType.StrengthMedicine => "对自身使用魔兽刚力药，获得攻击强化效果。",
            BeastmasterCrucibleItemType.VampireFang => "对当前敌对目标使用吸血鬼之牙，造成伤害并恢复自身 HP。",
            BeastmasterCrucibleItemType.StarSand => "对当前敌对目标使用星之沙，造成伤害并施加对应效果。",
            BeastmasterCrucibleItemType.VampireMedicine => "对自身使用魔兽吸血药，获得吸血效果。",
            BeastmasterCrucibleItemType.RecoverySet => "对自身使用魔兽恢复药套装，恢复大量 HP。",
            BeastmasterCrucibleItemType.Specific => "从完整目录中选择一个奇弈道具；攻击道具使用当前敌对目标，魔兽金针和回唤兽笛使用当前目标，其余对自身使用。",
            _ => string.Empty,
        };

    public static string GetCrucibleItemName(uint itemId)
        => itemId switch
        {
            76 => "1级恢复药",
            77 => "2级恢复药",
            78 => "3级恢复药",
            79 => "4级恢复药",
            80 => "1级魔兽药粉",
            81 => "2级魔兽药粉",
            82 => "3级魔兽药粉",
            83 => "魔兽解毒药",
            84 => "魔兽金针",
            85 => "魔兽眼药",
            86 => "1级魔兽抗毒药",
            87 => "2级魔兽抗毒药",
            88 => "1级魔兽抗麻痹药",
            89 => "2级魔兽抗麻痹药",
            90 => "1级魔兽抗失明药",
            91 => "2级魔兽抗失明药",
            92 => "1级魔兽抗石化药",
            93 => "2级魔兽抗石化药",
            94 => "1级魔兽抗睡眠药",
            95 => "2级魔兽抗睡眠药",
            96 => "回唤兽笛",
            97 => "魔兽烟雾弹",
            98 => "1级魔兽复活药",
            99 => "2级魔兽复活药",
            100 => "盗贼之眼",
            101 => "商人之眼",
            102 => "魔兽硬肤药",
            103 => "魔兽缩时药",
            104 => "魔兽刚力药",
            105 => "魔兽刚力猛药",
            106 => "魔兽魔力药",
            107 => "魔兽魔力猛药",
            108 => "魔兽特攻药",
            109 => "魔兽特攻猛药",
            110 => "魔兽敏捷药",
            111 => "魔兽敏捷猛药",
            112 => "魔兽耐力药",
            113 => "魔兽耐力猛药",
            114 => "魔兽加速药",
            115 => "魔兽羽毛",
            116 => "1级魔兽耐火药",
            117 => "2级魔兽耐火药",
            118 => "1级魔兽耐水药",
            119 => "2级魔兽耐水药",
            120 => "1级魔兽耐土药",
            121 => "2级魔兽耐土药",
            122 => "1级魔兽耐雷药",
            123 => "2级魔兽耐雷药",
            124 => "1级魔兽耐风药",
            125 => "2级魔兽耐风药",
            126 => "1级魔兽耐冰药",
            127 => "2级魔兽耐冰药",
            128 => "火之牙",
            129 => "冰之牙",
            130 => "水之牙",
            131 => "雷之牙",
            132 => "土之牙",
            133 => "风之牙",
            134 => "吸血鬼之牙",
            135 => "魔兽吸血药",
            136 => "反射之书",
            137 => "闪躲之书",
            138 => "时之沙",
            139 => "星之沙",
            140 => "魔兽恢复药套装",
            141 => "魔兽治愈套装",
            142 => "铸魔之书",
            143 => "钢刺之书",
            _ => $"奇弈道具 {itemId}",
        };

    public static string GetCrucibleItemDescription(uint itemId)
        => itemId switch
        {
            76 => "恢复自身 10% 体力。",
            77 => "恢复自身 23% 体力。",
            78 => "恢复自身 36% 体力。",
            79 => "恢复自身 50% 体力。",
            80 => "恢复自身及周围队员 10% 体力。",
            81 => "恢复自身及周围队员 25% 体力。",
            82 => "恢复自身及周围队员 40% 体力。",
            83 => "解除中毒；成功治愈后恢复 25% 体力。",
            84 => "对当前魔兽目标解除石化，恢复 50% 体力并附加石肤。",
            85 => "解除失明；成功治愈后恢复 25% 体力。",
            86 or 87 => "提高中毒耐性；2级作用于自身及周围队员。",
            88 or 89 => "提高麻痹耐性；2级作用于自身及周围队员。",
            90 or 91 => "提高失明耐性；2级作用于自身及周围队员。",
            92 or 93 => "提高石化耐性；2级作用于自身及周围队员。",
            94 or 95 => "提高睡眠耐性；2级作用于自身及周围队员。",
            96 => "让当前目标中陷入无法战斗状态的魔兽复活。",
            97 => "战斗开始前尝试回避战斗；对强敌和头目无效。",
            98 => "附加重生，无法战斗时有 70% 几率自动复活。",
            99 => "附加重生，无法战斗时有 95% 几率自动复活。",
            100 => "附加盗贼之眼，下次战斗的战利品掉落率变为 3 倍。",
            101 => "附加商人之眼，下次战斗的斗兽币获得量变为 2 倍。",
            102 => "附加硬肤药，受到伤害减轻 20%。",
            103 => "附加缩时药，攻击间隔及技能咏唱、复唱时间缩短 15%。",
            104 => "附加刚力药，物理伤害提高 30%。",
            105 => "以眩晕为代价，物理伤害提高 45%。",
            106 => "附加魔力药，魔法伤害提高 30%。",
            107 => "以噩梦为代价，魔法伤害提高 50%。",
            108 => "附加特攻药，暴击发动率提高 30%。",
            109 => "以石化为代价，暴击发动率提高 50%。",
            110 => "附加敏捷药，回避率提高 25%。",
            111 => "以失明为代价，回避率提高 25%。",
            112 => "恢复 10% 体力，并提高最大体力 20%。",
            113 => "以猛毒为代价，恢复 10% 体力并提高最大体力 30%。",
            114 => "提高移动速度、回避率和中毒耐性。",
            115 => "增加 100 点技力；持有高昂戒指时增加 250 点。",
            >= 116 and <= 127 => "提高对应属性伤害的无效化及吸收几率；2级效果更强。",
            >= 128 and <= 133 => "对当前敌对目标及其周围敌人造成对应属性范围魔法伤害。",
            134 => "对当前敌对目标及其周围敌人造成风属性范围魔法伤害。",
            135 => "附加吸血攻击，恢复造成伤害 10% 的体力。",
            136 => "附加反射，反射除特定攻击外的魔法攻击。",
            137 => "附加 5 档闪躲，使物理攻击无效化。",
            138 => "附加时之沙，战斗败北后回到战斗开始前。",
            139 => "对当前敌对目标及其周围敌人发动星体风暴，并降低火属性耐性。",
            140 => "体力低于 50% 时自动恢复 40% 体力。",
            141 => "附加自动治愈，可免疫一次部分异常状态。",
            142 => "附加铸魔，使自身和魔兽的所有攻击附带魔法属性。",
            143 => "附加钢刺，使自身和魔兽的所有攻击附带物理属性。",
            _ => string.Empty,
        };

    public static readonly uint[] KnownCrucibleItemIds =
    [
        76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91,
        92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107,
        108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
        124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139,
        140, 141, 142, 143,
    ];
}
