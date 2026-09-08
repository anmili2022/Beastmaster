namespace Beastmaster;

public static class BeastmasterGuide
{
    public static IReadOnlyList<BeastmasterStage> Stages { get; } =
    [
        new(
            "sample-introduction",
            "示例阶段一：初识驯兽师",
            "仅用于验证阶段与手动进度功能，不代表真实游戏流程。",
            [
                new("sample-introduction-read", "阅读示例说明", BeastmasterObjectiveType.Explore, "确认此处展示的是临时示例资料。"),
                new("sample-introduction-npc", "与示例 NPC 交谈", BeastmasterObjectiveType.TalkToNpc, "手动勾选以测试单项目标进度。"),
                new("sample-introduction-quest", "完成示例任务", BeastmasterObjectiveType.Quest, "此任务名称和流程均为占位内容。"),
            ]),
        new(
            "sample-training",
            "示例阶段二：基础训练",
            "仅用于验证不同阶段之间的选择和独立完成度。",
            [
                new("sample-training-monster", "击败示例怪物", BeastmasterObjectiveType.Monster, "不会自动追踪击杀，请手动勾选。"),
                new("sample-training-item", "取得示例物品", BeastmasterObjectiveType.Item, "不会扫描背包，请手动勾选。"),
                new("sample-training-duty", "完成示例挑战", BeastmasterObjectiveType.Duty, "不会读取副本状态，请手动勾选。"),
            ]),
    ];
}
