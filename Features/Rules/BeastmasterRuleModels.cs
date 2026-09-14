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
}

public enum BeastmasterRuleStatusCondition
{
    Present,
    Missing,
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
    public BeastmasterRuleActionType ActionType { get; set; } = BeastmasterRuleActionType.Skill;
    public BeastmasterCrucibleItemType CrucibleItemType { get; set; } = BeastmasterCrucibleItemType.Recovery;
    public uint DataId { get; set; }
    public uint ConditionId { get; set; }
    public uint ActionId { get; set; } = 44879;
    public uint CrucibleItemId { get; set; }

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "规则名称不能为空。";
            return false;
        }

        if (!Enum.IsDefined(ConditionType) || !Enum.IsDefined(StatusCondition))
        {
            error = "规则检测类型无效。";
            return false;
        }

        if (ConditionType != BeastmasterRuleConditionType.TargetDataId && ConditionId == 0)
        {
            error = IsStatusRule ? "BUFFID 必须大于 0。" : "读条 ID 必须大于 0。";
            return false;
        }

        if (RequiresDataId && DataId == 0)
        {
            error = "DataID 必须大于 0。";
            return false;
        }

        if (ActionType == BeastmasterRuleActionType.Skill && !BeastmasterRuleActions.IsSupported(ActionId))
        {
            error = $"不支持规则技能 {ActionId}。";
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
            builder.AppendLine()
                .AppendLine("[规则]")
                .AppendLine($"名称|{Sanitize(rule.Name)}")
                .AppendLine($"启用|{rule.Enabled}")
                .AppendLine($"检测|{rule.ConditionType}")
                .AppendLine($"条件|{rule.StatusCondition}")
                .AppendLine($"执行|{rule.ActionType}")
                .AppendLine($"奇弈道具类型|{rule.CrucibleItemType}")
                .AppendLine($"DataId|{rule.DataId}")
                .AppendLine($"检测ID|{rule.ConditionId}")
                .AppendLine($"技能|{rule.ActionId}")
                .AppendLine($"奇弈道具|{rule.CrucibleItemId}");
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

        switch (key)
        {
            case "名称": rule.Name = value; return true;
            case "启用" when bool.TryParse(value, out var enabled): rule.Enabled = enabled; return true;
            case "检测" when Enum.TryParse<BeastmasterRuleConditionType>(value, out var condition): rule.ConditionType = condition; return true;
            case "条件" when Enum.TryParse<BeastmasterRuleStatusCondition>(value, out var status): rule.StatusCondition = status; return true;
            case "执行" when Enum.TryParse<BeastmasterRuleActionType>(value, out var actionType): rule.ActionType = actionType; return true;
            case "奇弈道具类型" when Enum.TryParse<BeastmasterCrucibleItemType>(value, out var itemType): rule.CrucibleItemType = itemType; return true;
            case "DataId" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var dataId): rule.DataId = dataId; return true;
            case "检测ID" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var conditionId): rule.ConditionId = conditionId; return true;
            case "技能" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var actionId): rule.ActionId = actionId; return true;
            case "奇弈道具" when uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId): rule.CrucibleItemId = itemId; return true;
            default: return false;
        }
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace('|', ' ');
}

public static class BeastmasterRuleActions
{
    public static readonly (uint ActionId, string Name)[] Supported =
    [
        (44879, "碎击斩"), (44880, "捕获"), (44881, "一号兽笛"), (44883, "碎咬斧"),
        (44884, "猛属性斧"), (44885, "裂盾劈"), (44886, "魔兽技"), (44887, "坚属性斧"),
        (44888, "魔属性斧"), (44889, "翔属性斧"), (44890, "释放"), (44891, "最后一击"),
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
            or 44890 or 44891 or 44893 or 44930 or 44931 or 44932 or 44933 or 47093;

    public static bool IsCrucibleItemId(uint itemId)
        => itemId is >= 76 and <= 143;

    public static bool IsCrucibleItemFriendly(uint itemId)
        => itemId is 76 or 77 or 78;

    public static string GetCrucibleItemTypeName(BeastmasterCrucibleItemType itemType)
        => itemType == BeastmasterCrucibleItemType.Recovery ? "恢复药" : "各种牙";

    public static string GetCrucibleItemName(uint itemId)
        => itemId switch
        {
            76 => "1级恢复药",
            77 => "2级恢复药",
            78 => "3级恢复药",
            128 => "火之牙",
            129 => "冰之牙",
            131 => "雷之牙",
            133 => "风之牙",
            138 => "时之沙",
            _ => $"奇弈道具 {itemId}",
        };

    public static readonly uint[] KnownCrucibleItemIds =
    [
        76, 77, 78,
        128, 129, 131, 133,
        138,
    ];
}
