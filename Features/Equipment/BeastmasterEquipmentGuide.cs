namespace Beastmaster;

public sealed record BeastmasterEquipmentEntry(string Slot, string Name, string Category, uint ItemId = 0);

public static class BeastmasterEquipmentGuide
{
    public static IReadOnlyList<BeastmasterEquipmentEntry> Level50Starter { get; } =
    [
        new("主手", "兽主手斧", "武器", 50742),
        new("副手", "兽主青铜重盾", "盾牌", 50743),
        new("头部", "呢绒包头巾", "防具", 2810),
        new("身体", "呢绒衬衫", "防具", 3147),
        new("手部", "龙神强袭手套", "防具", 8961),
        new("腿部", "呢绒垮裤", "防具", 3408),
        new("脚部", "龙神强袭筒靴", "防具", 8963),
        new("耳饰", "以太之光耳坠", "饰品", 24589),
        new("项链", "龙神强攻项环", "饰品", 8967),
        new("手镯", "无装备", "饰品"),
        new("戒指 1", "初学者戒指", "饰品", 44410),
        new("戒指 2", "改良型加隆德强攻指环", "饰品", 8901),
        new("职业证", "驯兽师之证", "职业", 47474),
    ];

    public static IReadOnlyList<BeastmasterEquipmentEntry> Level50BestInSlot { get; } =
    [
        new("主手", "兽王手斧", "武器", 51736),
        new("副手", "兽王青铜重盾", "盾牌", 51737),
        new("头部", "兽主独角冠+4", "防具", 50772),
        new("身体", "兽主裘皮衣+4", "防具", 50773),
        new("手部", "兽主护臂+4", "防具", 50774),
        new("腿部", "兽主工作裤+4", "防具", 50775),
        new("脚部", "兽主靴+4", "防具", 50776),
        new("耳饰", "兽主耳环", "饰品", 51745),
        new("项链", "龙神强攻项环", "饰品", 8967),
        new("手镯", "改良型加隆德强攻手镯", "饰品", 8898),
        new("戒指 1", "改良型加隆德强攻指环", "饰品", 8901),
        new("戒指 2", "龙神强攻指环", "饰品", 8968),
        new("食品", "炖剑山芋", "食品", 49245),
    ];
}
