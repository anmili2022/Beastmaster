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
    float? MapY = null)
{
    public string Key => $"catalog-{Number:00}";
}

public static class BeastmasterCatalog
{
    public static IReadOnlyList<BeastmasterCatalogEntry> Entries { get; } =
    [
        new(1, "库西", BeastmasterCatalogLocationType.Starting, "初始自带"),
        new(2, "松鼠", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 23.1f, 17f),
        new(3, "迷途羊羔", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 23.8f, 25.6f),
        new(4, "陆鱼", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 22f, 22f),
        new(5, "奥猴", BeastmasterCatalogLocationType.Field, "黑衣森林北部林区", 155, 28.5f, 24.3f),
        new(6, "渡渡鸟", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 137, 30.9f, 18.2f),
        new(7, "矿爬虫", BeastmasterCatalogLocationType.Field, "西萨纳兰", 147, 20.3f, 28.6f),
        new(8, "凶蛛蝎", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 19f, 19f),
        new(9, "巨型陆蟹", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 15.3f, 14.3f),
        new(10, "胡蜂", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 16f, 12.5f),
        new(11, "兀鹫", BeastmasterCatalogLocationType.Field, "西萨纳兰", 147, 21f, 26f),
        new(12, "蔓德拉", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 21.4f, 16.3f),
        new(13, "死魂", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 18.5f, 28.3f),
        new(14, "跳蜥", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 20.5f, 18.5f),
        new(15, "壳蟹", BeastmasterCatalogLocationType.Field, "西萨纳兰", 147, 16.5f, 16.5f),
        new(16, "螳螂", BeastmasterCatalogLocationType.Field, "西拉诺西亚", 149, 21f, 23f),
        new(17, "粘液怪", BeastmasterCatalogLocationType.Duty, "封锁坑道铜铃铜山"),
        new(18, "无头骑士", BeastmasterCatalogLocationType.Duty, "魔兽领域日影地修炼所"),
        new(19, "蝙蝠", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 137, 26.5f, 15.9f),
        new(20, "陷阱草", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 23f, 26f),
        new(21, "席兹", BeastmasterCatalogLocationType.Field, "西拉诺西亚", 149, 24.1f, 23.6f),
        new(22, "仙人刺", BeastmasterCatalogLocationType.Field, "西萨纳兰", 147, 27f, 25f),
        new(23, "巨像", BeastmasterCatalogLocationType.Field, "南萨纳兰", 153, 24f, 12.3f),
        new(24, "碧企鹅", BeastmasterCatalogLocationType.Field, "东拉诺西亚", 156, 28.8f, 36.7f),
        new(25, "精金龟", BeastmasterCatalogLocationType.Field, "中萨纳兰", 152, 22f, 30f),
        new(26, "大水牛", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138, 18.5f, 17.5f),
        new(27, "乌菊石", BeastmasterCatalogLocationType.Field, "西萨纳兰", 147, 16.8f, 14.5f),
        new(28, "巨虫", BeastmasterCatalogLocationType.Field, "南萨纳兰", 153, 15.2f, 37.5f),
        new(29, "魔石精", BeastmasterCatalogLocationType.Field, "中萨纳兰", 152, 17.4f, 23.7f),
        new(30, "古菩猩猩", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 137, 25.2f, 24.5f),
        new(31, "巨蟾蜍", BeastmasterCatalogLocationType.Field, "拉诺西亚低地", 137, 24.6f, 23f),
        new(32, "蜂鸟", BeastmasterCatalogLocationType.Field, "东拉诺西亚", 156, 30.6f, 24f),
        new(33, "长须豹", BeastmasterCatalogLocationType.Field, "拉诺西亚外地", 180, 15.3f, 14.7f),
        new(34, "盗龙", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 31.2f, 20.3f),
        new(35, "烈阳火蛟", BeastmasterCatalogLocationType.Field, "南萨纳兰", 153, 25f, 39f),
        new(36, "树精", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 28.7f, 19.3f),
        new(37, "灵蚁", BeastmasterCatalogLocationType.Duty, "流沙迷宫樵鸣洞"),
        new(38, "奇美拉", BeastmasterCatalogLocationType.Duty, "流沙迷宫樵鸣洞"),
        new(39, "魔界花", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 13.5f, 22.3f),
        new(40, "幽灵", BeastmasterCatalogLocationType.Field, "中拉诺西亚", 138),
        new(41, "蝾螈", BeastmasterCatalogLocationType.Field, "黑衣森林中央林区", 154, 26.5f, 18.9f),
        new(42, "眼镜蛇", BeastmasterCatalogLocationType.Field, "摩杜纳", 157, 26.3f, 12.9f),
        new(43, "海德拉", BeastmasterCatalogLocationType.Duty, "海德拉讨伐战"),
        new(44, "灯心蜻蛉", BeastmasterCatalogLocationType.Duty, "腐坏遗迹无限城市街古迹"),
        new(45, "腐坏古菩猩猩", BeastmasterCatalogLocationType.Duty, "腐坏遗迹无限城市街古迹"),
        new(46, "祖", BeastmasterCatalogLocationType.Duty, "领航明灯天狼星灯塔"),
        new(47, "寒冰巨像", BeastmasterCatalogLocationType.Duty, "凛冽洞天披雪大冰壁"),
        new(48, "真红龙虾", BeastmasterCatalogLocationType.Duty, "逆转要害沙斯塔夏溶洞"),
        new(49, "大王花", BeastmasterCatalogLocationType.Duty, "巴哈姆特大迷宫入侵之章1"),
        new(50, "贝希摩斯", BeastmasterCatalogLocationType.Duty, "水晶塔古代人迷宫"),
    ];
}
