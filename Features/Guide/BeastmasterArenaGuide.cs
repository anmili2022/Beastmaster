namespace Beastmaster;

public sealed record BeastmasterArenaGuideMonster(string Name, bool IsBoss);

public sealed record BeastmasterArenaGuideRound(
    string Position,
    IReadOnlyList<BeastmasterArenaGuideMonster> Monsters,
    string Mechanic,
    string Comment);

public static class BeastmasterArenaGuide
{
    public static IReadOnlyList<BeastmasterArenaGuideRound> Round3 { get; } =
    [
        new(
            "第一步",
            [new("奇子·游侠骑士", true), new("奇子·主教", true)],
            "骑士会不断召唤小怪，击杀小怪时请保证每次骑士读条「幻影生成」时可连线的球不超过四个。生成的幻影在场内为超大钢铁，在场边为直线。",
            "路边的主教不要随便踩"),
        new(
            "第二步",
            [new("奇子·尤弥尔", true), new("奇子·鱼人", true)],
            "输出够可尝试在鱼人读条「麻痹尖刺」前将鱼人击杀，读完后鱼人会有反伤；输出不够则先打碎壳子、然后使用止步宝宝对尤弥尔进行输出。鱼人「恐慌洗礼」会附加恐慌（宝宝可以帮吃）。大海啸的击退距离很长，记得跑路。",
            "。。。"),
        new(
            "岔路一左",
            [new("奇子·祖", true), new("奇子·公雏鸟", false), new("奇子·母雏鸟", false)],
            "吃机制时注意不要打碎蛋，正常孵化的就杀掉。「前方/后方乱流」注意刀的方向就行。",
            "太难抓，差评"),
        new(
            "岔路一右",
            [new("奇子·卡托布莱帕斯", true)],
            "召唤出的魔球有环就是月环没环就是钢铁，有眼睛的球炸的时候要背对。",
            "这下是真有机制要背对。"),
        new(
            "第五步",
            [new("奇子·拉哈穆", true), new("奇子·巨像", true)],
            "尽快击杀小怪，「沙尘暴」的使命可以让宝宝吃（真伙伴哈哈）。「大地摇动」是扇形攻击，要和宝宝离得远一点哦。",
            string.Empty),
        new(
            "第六步",
            [new("奇子·塞壬", true), new("奇子·蹒跚鬼", false), new("奇子·爬行鬼", false)],
            "「混乱之歌」场边无范围提示扇形，记得挑衅，宝宝也不能吃这个。远离在地上爬的小怪。「亡者之歌」钢铁范围很大月环范围很小。「诱导之歌」有长距离强制移动+场边扇形。",
            "这种黏在地上的小怪最难扣了。"),
        new(
            "抽牌抽牌",
            [new("奇子·勇士", true), new("奇子·仙人刺", false), new("奇子·仙人花", false), new("奇子·士兵", false), new("奇子·守卫", false)],
            "AOE 杀 AOE 杀 麻将顺序为钢铁顺序，先打骑士。",
            "扎我一身刺，差评。这格性价比不高。"),
        new(
            "最终BOSS",
            [new("贪食无厌 加特勒", true), new("奇子·塔纳托斯", true)],
            "这个一句话怕是说不完哦。留意宝宝位置，小心吃平A。",
            "宝宝你怎么掉血了。"),
    ];
}
