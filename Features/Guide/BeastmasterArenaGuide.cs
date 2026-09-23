namespace Beastmaster;

public sealed record BeastmasterArenaGuideMonster(string Name, bool IsBoss);

public sealed record BeastmasterArenaGuideRound(
    string Position,
    IReadOnlyList<BeastmasterArenaGuideMonster> Monsters,
    string Mechanic,
    string Comment);

public static class BeastmasterArenaGuide
{
    public const string Round1Author = "其他母肥角色@拂晓之间";
    public const string Round2Author = "其他母肥角色@拂晓之间";
    public const string Round3Author = "其他母肥角色@拂晓之间";
    public const string HighRound1Author = "其他母肥角色@拂晓之间";
    public const string HighRound2Author = "其他母肥角色@拂晓之间";

    public static IReadOnlyList<BeastmasterArenaGuideRound> Round1 { get; } =
    [
        new(
            string.Empty,
            [new("奇子·主教", true), new("奇子·骑士", true)],
            "先打主教，骑士架盾后用宝宝吸引仇恨从背后打。",
            "一直攻击举盾骑士，过一段时间会自己撤盾，不用等16分钟。"),
        new(
            string.Empty,
            [new("奇子·上级恶魔", true)],
            "注意观察「深渊贯穿」生成的圆形AOE范围，容易被误伤。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·夺灵魔", true)],
            "AOE多，注意自身血量。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·祸蛛蝎", true), new("奇子·灵蛛蝎", false)],
            "「致命尾刺」给的中毒可以消掉，「毒蛛网」给的消不掉，注意血量；小蝎子出现后尽快消灭。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·食人魔", true), new("奇子·妖火", false), new("奇子·大妖火", false)],
            "第一次「燃烧猛击」为BOSS当前位置扇形，第二回会穿梭至对面火圈放扇形，注意提前跑路；BOSS读条「火灵的守护」时可以提前去场边等候，留技能或使用道具击杀妖火。",
            "前期装备不好时容易杀不掉。"),
        new(
            string.Empty,
            [new("魅惑女妖 帕德索", true), new("奇子·梦魔法师", false), new("奇子·梦魔骑士", false)],
            "「血雨」抬手钢铁，不抬手月环；BOSS读条「召唤」后优先击杀梦魔法师，「欺瞒雾」使用吸引注意让宝宝接。",
            string.Empty),
    ];

    public static IReadOnlyList<BeastmasterArenaGuideRound> Round2 { get; } =
    [
        new(
            string.Empty,
            [new("奇子·曼提克", true)],
            "左右刀专场训练",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·牛头魔", true), new("奇子·冥鬼之眼", true)],
            "BOSS读条「致命射线」时，读条剩余2-3秒可提前站到石板上或不踩BOSS脚下这块；「以太波」直条比较宽，注意范围。尽量快点杀掉，后面机制多了踩石板时间比较紧。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·双足飞龙", true)],
            "风元精的直条比较慢，中间路被堵死时可从外面走去找BOSS。",
            "刚开始打还挺唬人的。"),
        new(
            string.Empty,
            [new("奇子·虚灵法师", true), new("奇子·僵尸", false)],
            "渐渐混乱不要超过16层（可能不准），圈尽量往四个角放。",
            "你们这是什么关卡怎么又有僵尸又有48。"),
        new(
            string.Empty,
            [new("奇子·牛魔老哥", true), new("奇子·牛魔老弟", true)],
            "「十吨重踏」跳过去的圈是即死，躲击退的时候不要踩进去。「声援」可被打断，不打断是死刑，大概率能秒杀满血宝宝。",
            "这也太有劲了呃呃。"),
        new(
            string.Empty,
            [new("奇子·恶魔", true), new("奇子·恶魔兵装", false), new("奇子·小恶魔", false)],
            "先打有连线的。",
            "抽不到怪也是一种运气"),
        new(
            string.Empty,
            [new("寻兽探奇 路斯福洛克斯", true), new("小地豆", false)],
            "不要踩场边沙坑。冒叹号的沙坑会放扇形，场中的四个炸弹能打进小地豆所在的沙坑，对小地豆有伤害。哥布可以打慢点，地震还挺疼的。",
            string.Empty),
    ];

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

    public static IReadOnlyList<BeastmasterArenaGuideRound> HighRound1 { get; } =
    [
        new(
            string.Empty,
            [new("奇子·博学林鸮", true), new("博学林鸮的羽毛", false)],
            "爆震去风圈，魔法锤去史莱姆，狂水去河童。第一个机制固定是白圈，可以提前走位。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·魔界花", true), new("奇子·幼苗", false), new("奇子·大口花", false), new("腐汁", false)],
            "小怪出了迅速击杀。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·尸生花", true), new("奇子·女王鹰蜂", false)],
            "踩中荆棘圈时按次伤害，不是DOT。BOSS读条「鲜花陷阱」时荆棘圈会扩大，「吐出」即击退，看好安全区即可。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·冰龙", true), new("冰元精", false)],
            "BOSS读条「无终的枯朽」时安全区范围比较小，「寒冰覆盖」会在所在位置放置黄圈，建议在安全区内三角形放置。",
            "或者利用一些可以快速移动的神秘小工具。"),
        new(
            string.Empty,
            [new("奇子·石像鬼", true)],
            "「突进连斩」为冲向玩家后先前半场刀接后半场刀；「圆环连斩」为脚下钢铁接向外扩展月环；「腐瘴气」时吃球降低BOSS身上的BUFF，但是一次吃不能超过五层。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·管理者", true), new("奇子·故障虫", false), new("奇子·发条步兵", false), new("奇子·恐慌装甲", false), new("奇子·无人机", false), new("奇子·眼镜蛇", false)],
            "BOSS血量较低，盯着使劲打就行了。",
            "出的概率似乎很低。"),
        new(
            string.Empty,
            [new("奇子·巨像", true)],
            "BOSS读条「岩壁」时注意观察尖刺方向，不要被石头撞。第一个巨像死掉后第二个会复活；「岩雨」会出现四块石头，碎掉的石头下为月环。「自爆」不是即死，反而是BOSS自爆完就死了。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·树精", true), new("奇子·蛞蝓", false), new("奇子·花苗", false), new("奇子·凶蛛蝎", false), new("奇子·比洛克", false)],
            "小怪出来迅速击杀。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·爆弹之母", true), new("奇子·榴弹怪", false), new("奇子·爆弹怪", false), new("奇子·雪弹怪", false), new("奇子·焰球", false)],
            "随机怪一般都比较简单，盯着BOSS杀就行。",
            string.Empty),
        new(
            string.Empty,
            [new("怨毒龙 博尔格尼", true), new("有毒物质", false)],
            "BOSS读条「猛毒吐息」时向后跳至场边，喷出扇形毒雾，实际安全范围为目标圈大小的圆形，注意观察BOSS面向；「猛毒黏液」将毒圈沿场边放置；BOSS读条「蠕动的痰」时远离毒圈放置目标，并在目标进入毒圈前击杀。",
            string.Empty),
    ];

    public static IReadOnlyList<BeastmasterArenaGuideRound> HighRound2 { get; } =
    [
        new(string.Empty, [new("奇子·夺心魔", true), new("奇子·毒性蘑菇", false)], "蘑菇要在水里死掉。", string.Empty),
        new(string.Empty, [new("奇子·佛劳洛斯", true), new("雷元精", false)], "打掉一两个雷元精就非常好跑了。", string.Empty),
        new(
            string.Empty,
            [new("奇子·夜魔人", true), new("奇子·深瞳", false), new("奇子·爆弹怪", false), new("光元精", false)],
            "开局突进接钢铁，突进接扇形。BOSS读条「黑暗帷幕」后隐身；爆弹怪会放黄圈，优先击杀深瞳。光元精读条「放逐」时，将攻击引导至场中以解除BOSS隐身状态。",
            "他逃，他追，他插翅难飞。"),
        new(
            string.Empty,
            [new("奇子·阿托莫斯", true), new("奇子·红格雷姆林", false), new("奇子·格雷姆林", false), new("奇子·软糊怪", false), new("奇子·奶冻怪", false), new("奇子·甜羹怪", false), new("奇子·石像魔", false), new("奇子·威胁扎哈克", false)],
            "小怪会不停在脚下放黄圈，清完小怪盯着BOSS打即可；似乎不清也不是很疼。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·火蛟", true), new("奇子·棘鼹", false), new("奇子·狱蟾蜍", false), new("奇子·蓝闪蝶", false)],
            "火蛟开局蹦向左侧接扇形；场地中的火球会释放黄圈。全部击杀后前往地图火龙卷处躲避月环。蝴蝶出现后优先击杀，不要让蟾蜍吃到蝴蝶，否则会获得体力恢复。",
            "基本不怎么走这边。"),
        new(
            string.Empty,
            [new("奇子·杜尔迦", true), new("奇子·转盘堡", false)],
            "「雷气吸收」时不仅要看没有连线的手，还要观察BOSS是否旋转，安全区会和BOSS一起转动；「气化炸弹」击退距离很远，前往四角放置。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·美杜莎", true), new("奇子·拉米亚", false), new("奇子·独眼巨人", false)],
            "「石化光照射」不要照到自己和小怪，小怪被照会附加增伤。二次召唤出的独眼巨人尽快击杀一个；可以利用BOSS射线石化巨人，石化后的巨人会被秒杀。",
            string.Empty),
        new(
            string.Empty,
            [new("奇子·奇美拉", true)],
            "观察三个头：冰结为钢铁，雷电为月环；红头发光为前方扇形，其余哪边头发光就是哪边的大扇形。「强袭吐息」为拉线冲刺加扇形，注意观察发光头部。",
            "有BMR难度会降低很多的一关。"),
        new(
            string.Empty,
            [new("奇子·巨人", true), new("奇子·独眼巨人", false), new("凝胶化雷电", false), new("凝胶化火焰", false)],
            "利用巨人的锤子打碎大史莱姆，连续锤击两次同属性史莱姆会有AOE。「巨躯狂怒」双手往前为钢铁，往后为后半场刀，单手拿锤为前半场刀。",
            "小火史莱姆像一堆大痘。。"),
        new(string.Empty, [new("奇子·斯芬克斯", true)], "锻炼脑力关，似乎选择魔物的关卡只有陆鱼这一个固定答案。", string.Empty),
        new(
            string.Empty,
            [new("奇子·莫古小剑", true), new("奇子·莫古小猛", false), new("奇子·莫古大医", false), new("奇子·莫古大术", false), new("奇子·莫古小医", false), new("奇子·莫古小术", false), new("奇子·莫古大剑", false), new("奇子·莫古小歌", false), new("奇子·莫古小贼", false)],
            "莫古小贼会偷斗兽币，攻击它会归还。整体机制还算和平。",
            "场面乱成一锅粥了快喝了吧（）"),
        new(string.Empty, [new("奇子·贝希摩斯", true), new("奇子·铁巨人", false), new("雷元精", false)], "我用0.01秒就算出来这条路没人走。", string.Empty),
        new(
            string.Empty,
            [new("奇子·冥鬼之眼王", true), new("死亡沙漏", false), new("奇子·哈帕利特", false), new("奇子·肮脏之眼", false)],
            "指针转一圈到沙漏位置前打掉沙漏。BOSS读条「5兽级即死」时，若在场宝宝等级为5的倍数，请手动引爆离场；第二次「死亡轮盘」若小怪在场，眼睛会读死宣，背对处理。",
            string.Empty),
        new(string.Empty, [new("魔斧之主 劳妲", true), new("奇子·塔纳托斯", false)], "到这了还不看攻略吗，你的胆子真是肥嘟嘟的。", string.Empty),
    ];
}
