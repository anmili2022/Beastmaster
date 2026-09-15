namespace Beastmaster;

public sealed record BeastmasterAchievementGroup(string Key, string Name, int[] AchievementIds);

public static class BeastmasterAchievementCatalog
{
    public static IReadOnlyList<BeastmasterAchievementGroup> Groups { get; } =
    [
        new("rate", "魔兽率舞", [4028, 4029, 4030, 4031, 4032]),
        new("clear", "斗兽争奇", [4033, 4034, 4035]),
        new("high-clear", "出奇制胜", [4036, 4037]),
        new("beast-iii", "不足为奇", [4038, 4039, 4040]),
        new("high-beast-iii", "奇高一着", [4041, 4042]),
        new("rating", "盘评价", [4043, 4044, 4045, 4046, 4047, 4048]),
        new("high-rating", "高段评价", [4049, 4050, 4051, 4052]),
        new("overall", "全盘综合", [4053, 4054, 4055]),
        new("training", "训练有素", [4056, 4057, 4058, 4059, 4060]),
        new("high-reward", "高段奖励", [4061, 4062, 4063, 4064, 4065, 4066, 4067, 4068]),
        new("ranking", "排名", [4075, 4076, 4077]),
    ];

    public static int AchievementCount => Groups.Sum(group => group.AchievementIds.Length);
}
