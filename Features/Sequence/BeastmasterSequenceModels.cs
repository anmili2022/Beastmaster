using System.Text;

namespace Beastmaster;

[Serializable]
public sealed class BeastmasterSequenceDefinition
{
    private static readonly HashSet<uint> SupportedActionIds =
    [
        44879, 44881, 44883, 44885, 44886, 44890, 44891, 44892, 44893, 44894,
        44895, 44896, 44897, 44898, 44899, 44900, 44901, 44902, 44903, 44904, 44905,
    ];

    public string Name { get; set; } = "虫队模版序列";
    public string Description { get; set; } = "虫队模版-三号笛借用百兽肤起手";
    public List<BeastmasterSequenceStep> CountdownSteps { get; set; } = [];
    public List<BeastmasterSequenceStep> CombatSteps { get; set; } = [];

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "序列名称不能为空。";
            return false;
        }

        if (CountdownSteps.Count == 0 || CombatSteps.Count == 0)
        {
            error = "倒计时和进入战斗阶段都必须至少包含一个步骤。";
            return false;
        }

        if (CountdownSteps.Count > 100 || CombatSteps.Count > 100)
        {
            error = "每个阶段最多只能有 100 个步骤。";
            return false;
        }

        var previousTime = float.PositiveInfinity;
        foreach (var step in CountdownSteps)
        {
            var time = step.TimeSeconds ?? float.NaN;
            if (!float.IsFinite(time) || time > 0f || time < -60f || Math.Abs(time) >= previousTime)
            {
                error = "倒计时步骤必须按 T-时间从大到小排列，范围为 T-60 至 T-0，且不能重复时间。";
                return false;
            }

            previousTime = Math.Abs(time);
            if (!SupportedActionIds.Contains(step.ActionId))
            {
                error = $"倒计时阶段包含不支持的技能：{step.ActionId}。";
                return false;
            }
        }

        foreach (var step in CombatSteps)
        {
            if (!SupportedActionIds.Contains(step.ActionId))
            {
                error = $"进入战斗阶段包含不支持的技能：{step.ActionId}。";
                return false;
            }
        }

        return true;
    }

    public static BeastmasterSequenceDefinition CreateWaterOpener()
        => new()
        {
            CountdownSteps =
            [
                new(-9, 44894, "三号兽笛"),
                new(-5, 44895, "借用"),
                new(-4, 44881, "一号兽笛"),
                new(-2, 44896, "百兽肤"),
                new(0, 44893, "盾牌冲击"),
            ],
            CombatSteps =
            [
                new(null, 44879, "碎击斩"),
                new(null, 44905, "鼓劲"),
                new(null, 44890, "释放"),
                new(null, 44883, "碎咬斧"),
                new(null, 44904, "声援"),
                new(null, 44891, "最后一击"),
                new(null, 44885, "裂盾劈"),
                new(null, 44892, "二号兽笛"),
                new(null, 44890, "释放"),
                new(null, 44894, "三号兽笛"),
            ],
        };

    public static BeastmasterSequenceDefinition CreateTestSequence()
        => new()
        {
            Name = "测试序列",
            Description = "用于验证倒计时、兽笛确认、T-0 目标技能和进战后的第一步。",
            CountdownSteps =
            [
                new(-3, 44881, "一号兽笛"),
                new(0, 44893, "盾牌冲击"),
            ],
            CombatSteps =
            [
                new(null, 44879, "碎击斩"),
            ],
        };

    public string Export()
    {
        var builder = new StringBuilder()
            .AppendLine("BSTSEQ|1")
            .AppendLine($"名称|{Name}")
            .AppendLine($"说明|{Description}")
            .AppendLine()
            .AppendLine("[倒计时]");
        foreach (var step in CountdownSteps)
        {
            builder.AppendLine($"{step.TimeSeconds:0.###}|{step.ActionId}|{step.Label}");
        }

        builder.AppendLine().AppendLine("[战斗]");
        foreach (var step in CombatSteps)
        {
            builder.AppendLine($"{step.ActionId}|{step.Label}");
        }

        return builder.ToString().TrimEnd();
    }

    public static bool TryImport(string text, out BeastmasterSequenceDefinition? sequence, out string error)
    {
        sequence = null;
        error = string.Empty;
        var lines = text.Replace("\r", string.Empty).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "BSTSEQ|1")
        {
            error = "第一行必须是 BSTSEQ|1。";
            return false;
        }

        var result = new BeastmasterSequenceDefinition();
        var section = string.Empty;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;
            if (line is "[倒计时]" or "[战斗]")
            {
                section = line;
                continue;
            }
            if (line.StartsWith("名称|", StringComparison.Ordinal))
            {
                result.Name = line[3..];
                continue;
            }
            if (line.StartsWith("说明|", StringComparison.Ordinal))
            {
                result.Description = line[3..];
                continue;
            }

            var parts = line.Split('|');
            if (section == "[倒计时]" && parts.Length >= 3
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var time)
                && uint.TryParse(parts[1], out var countdownAction))
            {
                result.CountdownSteps.Add(new(time, countdownAction, parts[2]));
                continue;
            }
            if (section == "[战斗]" && parts.Length >= 2 && uint.TryParse(parts[0], out var combatAction))
            {
                result.CombatSteps.Add(new(null, combatAction, parts[1]));
                continue;
            }

            error = $"第 {index + 1} 行格式错误。";
            return false;
        }

        if (result.CountdownSteps.Count == 0 && result.CombatSteps.Count == 0)
        {
            error = "序列没有任何步骤。";
            return false;
        }

        if (!result.TryValidate(out error))
        {
            return false;
        }

        sequence = result;
        return true;
    }
}

[Serializable]
public sealed class BeastmasterSequenceStep
{
    public BeastmasterSequenceStep() { }

    public BeastmasterSequenceStep(float? timeSeconds, uint actionId, string label)
    {
        TimeSeconds = timeSeconds;
        ActionId = actionId;
        Label = label;
    }

    public float? TimeSeconds { get; set; }
    public uint ActionId { get; set; }
    public string Label { get; set; } = string.Empty;
}
