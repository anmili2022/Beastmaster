using System.Numerics;

namespace Beastmaster;

public sealed record BeastmasterQuestDefinition(uint RowId, string Name, string Summary);

public sealed record BeastmasterQuestTarget(
    uint QuestRowId,
    byte Sequence,
    uint TerritoryType,
    uint MapRowId,
    Vector3 Position,
    string Zone,
    string Name);

public static class BeastmasterQuestGuide
{
    public static IReadOnlyList<BeastmasterQuestDefinition> Quests { get; } =
    [
        new(71026, "驯养魔兽之人", "在格里达尼亚新街接受的首个驯兽师任务。"),
        new(71027, "最初的搭档", "驯兽师职业任务。"),
        new(71028, "与魔兽心意相通", "驯兽师职业任务。"),
        new(71029, "露流派的驯兽术", "驯兽师职业任务。"),
        new(71030, "奇盘上的战斗", "驯兽师职业任务。"),
        new(71031, "向师姐学习哥布！", "驯兽师职业任务。"),
        new(71032, "同门较量", "驯兽师职业任务。"),
        new(71033, "赎罪的驯兽师", "驯兽师职业任务。"),
        new(71034, "贪食无厌加特勒", "驯兽师职业任务。"),
        new(71035, "牢不可破的牵绊", "驯兽师职业任务。"),
        new(71036, "猛者的试炼：高段第一盘", "驯兽师高段试炼任务。"),
        new(71037, "奇盘上的王者：高段第二盘", "驯兽师高段试炼任务。"),
        new(71045, "将驯兽之路登峰造极", "驯兽师职业任务。"),
    ];

    public static IReadOnlyList<BeastmasterQuestTarget> Targets { get; } =
    [
        new(
            71026,
            1,
            148,
            4,
            new Vector3(-318.654f, 60.947f, -129.382f),
            "黑衣森林中央林区",
            "第一步任务目标"),
    ];
}
