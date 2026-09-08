namespace Beastmaster;

public enum BeastmasterCatalogLocationType
{
    Starting,
    Field,
    Duty,
    Unknown,
}

public sealed record BeastmasterCatalogEntry(
    int Number,
    string Name,
    BeastmasterCatalogLocationType LocationType,
    string Location,
    ushort TerritoryType = 0,
    float? MapX = null,
    float? MapY = null,
    string Level = "",
    ushort MapRowId = 0,
    float? WorldX = null,
    float? WorldY = null,
    float? WorldZ = null,
    uint ContentFinderConditionId = 0)
{
    public string Key => $"catalog-{Number:00}";
}

public static class BeastmasterCatalog
{
    public static IReadOnlyList<BeastmasterCatalogEntry> Entries { get; } =
    [
        new(1, "库西", BeastmasterCatalogLocationType.Starting, "初始自带", 0, null, null, ""),
        new(2, "松鼠", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 23.1f, 17f, "1~2", 4),
        new(3, "迷途羊羔", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 23.8f, 25.6f, "3~4", 15),
        new(4, "陆鱼", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 22f, 22f, "4~6", 15),
        new(5, "奥猴", BeastmasterCatalogLocationType.Field, "黑衣森林北部林区", 154, 28.5f, 24.3f, "5~9", 7),
        new(6, "渡渡鸟", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 135, 30.9f, 18.2f, "4~9", 16),
        new(7, "矿爬虫", BeastmasterCatalogLocationType.Field, "西萨纳兰", 140, 20.3f, 28.6f, "6~8", 20),
        new(8, "凶蛛蝎", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 19f, 19f, "10", 4),
        new(9, "巨型陆蟹", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 15.3f, 14.3f, "10~13", 15),
        new(10, "胡蜂", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 16f, 12.5f, "10~13", 15),
        new(11, "兀鹫", BeastmasterCatalogLocationType.Field, "西萨纳兰", 140, 21f, 26f, "6", 20),
        new(12, "蔓德拉", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 21.4f, 16.3f, "5~7", 15),
        new(13, "死魂", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 18.5f, 28.3f, "14", 4),
        new(14, "跳蜥", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 20.5f, 18.5f, "4~8", 15),
        new(15, "壳蟹", BeastmasterCatalogLocationType.Field, "西萨纳兰", 140, 16.5f, 16.5f, "13", 20),
        new(16, "螳螂", BeastmasterCatalogLocationType.Field, "西拉诺西亚", 138, 21f, 23f, "16", 18, -11.055f, -22.468f, 50.278f),
        new(17, "粘液怪", BeastmasterCatalogLocationType.Duty, "封锁坑道铜铃铜山", 0, null, null, "17", 0, null, null, null, 3),
        new(18, "无头骑士", BeastmasterCatalogLocationType.Duty, "魔兽领域日影地修炼所", 0, null, null, "20", 0, null, null, null, 7),
        new(19, "蝙蝠", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 135, 26.5f, 15.9f, "7", 16),
        new(20, "陷阱草", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 23f, 26f, "10", 4),
        new(21, "席兹", BeastmasterCatalogLocationType.Field, "西拉诺西亚", 138, 24.1f, 23.6f, "16", 18, 105.702f, -16.159f, 164.859f),
        new(22, "仙人刺", BeastmasterCatalogLocationType.Field, "西萨纳兰", 140, 27f, 25f, "3~4", 20),
        new(23, "巨像", BeastmasterCatalogLocationType.Field, "南萨纳兰", 146, 24f, 12.3f, "29", 25),
        new(24, "碧企鹅", BeastmasterCatalogLocationType.Field, "东拉诺西亚", 137, 28.8f, 36.7f, "30", 17),
        new(25, "精金龟", BeastmasterCatalogLocationType.Field, "中萨纳兰", 141, 22f, 30f, "12", 21, -101.513f, 5.07f, 238.304f),
        new(26, "大水牛", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 18.5f, 17.5f, "8", 15),
        new(27, "乌菊石", BeastmasterCatalogLocationType.Field, "西萨纳兰", 140, 16.8f, 14.5f, "14", 20),
        new(28, "巨虫", BeastmasterCatalogLocationType.Field, "南萨纳兰", 146, 15.2f, 37.5f, "31", 25),
        new(29, "魔石精", BeastmasterCatalogLocationType.Field, "中萨纳兰", 141, 17.4f, 23.7f, "7", 21),
        new(30, "古菩猩猩", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 135, 25.2f, 24.5f, "12", 16),
        new(31, "巨蟾蜍", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 135, 24.6f, 23f, "4", 16),
        new(32, "蜂鸟", BeastmasterCatalogLocationType.Field, "东拉诺西亚", 137, 30.6f, 24f, "33", 17),
        new(33, "长须豹", BeastmasterCatalogLocationType.Field, "拉诺西亚外地", 180, 15.3f, 14.7f, "34", 30, -359.599f, 60.528f, -348.505f),
        new(34, "盗龙", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 31.2f, 20.3f, "9", 4),
        new(35, "烈阳火蛟", BeastmasterCatalogLocationType.Field, "南萨纳兰", 146, 25f, 39f, "32", 25),
        new(36, "树精", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 28.7f, 19.3f, "12~17", 4, 332.173f, -1.287f, -344.14f),
        new(37, "灵蚁", BeastmasterCatalogLocationType.Duty, "流沙迷宫樵鸣洞", 0, null, null, "38", 0, null, null, null, 12),
        new(38, "奇美拉", BeastmasterCatalogLocationType.Duty, "流沙迷宫樵鸣洞", 0, null, null, "38", 0, null, null, null, 12),
        new(39, "魔界花", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 13.5f, 22.3f, "31", 4, -427.656f, 49f, 33.517f),
        new(40, "幽灵", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 134, 20.3f, 19.6f, "7", 15, -58.393f, 27.135f, -119.884f),
        new(41, "蝾螈", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 148, 26.5f, 18.9f, "6", 4),
        new(42, "眼镜蛇", BeastmasterCatalogLocationType.Field, "摩杜纳", 156, 26.3f, 12.9f, "45", 14),
        new(43, "海德拉", BeastmasterCatalogLocationType.Duty, "海德拉讨伐战", 0, null, null, "50", 0, null, null, null, 75),
        new(44, "灯心蜻蛉", BeastmasterCatalogLocationType.Duty, "腐坏遗迹无限城市街古迹", 0, null, null, "50", 0, null, null, null, 22),
        new(45, "腐坏古菩猩猩", BeastmasterCatalogLocationType.Duty, "腐坏遗迹无限城市街古迹", 0, null, null, "50", 0, null, null, null, 22),
        new(46, "祖", BeastmasterCatalogLocationType.Duty, "领航明灯天狼星灯塔", 0, null, null, "50", 0, null, null, null, 17),
        new(47, "寒冰巨像", BeastmasterCatalogLocationType.Duty, "凛冽洞天披雪大冰壁", 0, null, null, "50", 0, null, null, null, 27),
        new(48, "真红龙虾", BeastmasterCatalogLocationType.Duty, "逆转要害沙斯塔夏溶洞", 0, null, null, "50", 0, null, null, null, 28),
        new(49, "大王花", BeastmasterCatalogLocationType.Duty, "巴哈姆特大迷宫入侵之章1", 0, null, null, "50", 0, null, null, null, 98),
        new(50, "贝希摩斯", BeastmasterCatalogLocationType.Duty, "水晶塔古代人迷宫", 0, null, null, "50", 0, null, null, null, 92),
    ];
}
