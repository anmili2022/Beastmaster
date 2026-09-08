# 驯兽师助手开发路线

## 项目位置

- 项目目录：`E:\git\Beastmaster`
- 项目文件：`Beastmaster.csproj`
- 插件清单：`Beastmaster.json`
- 构建命令：`dotnet build`
- 构建产物：`output\Beastmaster.dll`
- 中文命令：`/驯兽师`
- 英文命令：`/beastmaster`

## 当前状态

第一步“独立插件骨架”和第二步“基础进度模型”已经完成。当前正在整理真实驯兽师任务资料，并已建立任务与导航 UI 原型。

当前包含：

- `Plugin/BeastmasterPlugin.cs`：插件入口、命令注册和事件清理。
- `Infrastructure/DalamudApi.cs`：最小 Dalamud 服务注入。
- `Configuration/BeastmasterConfiguration.cs`：配置加载和保存。
- `UI/PluginUI.cs`：类似肝武助手的左右分栏主窗口。
- 左侧业务入口依次为“驯兽师任务链”“驯兽师图鉴”“快捷指令”，另有“设置”和“DEBUG”工具入口。
- 快捷指令栏目提供“魔兽图鉴”按钮，通过游戏客户端聊天输入接口执行原生文本命令 `/魔兽图鉴`；不能使用只分发 Dalamud 注册命令的 `ICommandManager.ProcessCommand`。
- 驯兽师图鉴当前录入 50 项用户提供的分布资料，区分野外坐标、副本、初始自带和未知位置；资料在客户端核对前保留“暂译/待核对”标记。
- 图鉴完成状态支持按角色手动保存，未登录时只允许查看。
- 图鉴 1 号已确认名称为 `库西种`，获取方式为初始自带。
- 所有具有明确野外地图坐标的图鉴条目提供导航按钮：同地图直接寻路，跨地图使用 Lifestream 传送并在读图后继续；副本、初始自带和未知位置条目不提供导航。
- 图鉴飞行导航会先自动上坐骑，检测到骑乘状态后才提交 vnavmesh 飞行请求；战斗中或等待 8 秒仍无法上坐骑时自动降级为步行导航。
- 图鉴页顶部提供停止导航按钮，可取消等待传送、等待上坐骑和当前 vnavmesh 路径。
- 图鉴页提供持久化的“按地图排序”选项；启用后按区域或副本名称分组，同一位置内保持图鉴编号顺序。
- 设置页包含依赖插件、常用设置和导航设置；主窗口不再提供全局启用开关。
- 任务页读取客户端任务完成/进行中状态，并可导航到具有有效客户端接取坐标的开始 NPC。
- 任务目标导航在取得并核对任务序列坐标前保持禁用，不使用推测坐标。
- `Beastmaster.json`：独立插件清单。

当前 `dotnet build` 成功，输出 `Beastmaster.dll`。本机存在 20 条与 `Phantom` 项目相同的 Dalamud SDK 程序集解析警告，但没有编译错误。

## 下一任务：基础进度模型

下一任务只完成“静态阶段 + 手动完成 + 按角色保存”，暂不加入导航、聊天监听、FATE 或收藏扫描。

### 目标

1. 读取当前登录角色的稳定标识。
2. 定义驯兽师阶段和目标的静态模型。
3. 按角色保存各目标的手动完成状态。
4. 在主窗口显示阶段列表、目标列表和完成度。
5. 未登录时允许查看资料，但不创建角色进度。

### 建议新增文件

```text
Features/
└── Progress/
    ├── BeastmasterProgressModels.cs
    ├── BeastmasterGuide.cs
    └── BeastmasterProgressService.cs
```

### 建议模型

```csharp
public enum BeastmasterObjectiveType
{
    Quest,
    Monster,
    Fate,
    Duty,
    Item,
    TalkToNpc,
    Explore,
}

public sealed record BeastmasterObjective(
    string Key,
    string Name,
    BeastmasterObjectiveType Type,
    string Description);

public sealed record BeastmasterStage(
    string Key,
    string Name,
    string Summary,
    IReadOnlyList<BeastmasterObjective> Objectives);

public sealed class BeastmasterCharacterProgress
{
    public HashSet<string> CompletedObjectives { get; set; } = new(StringComparer.Ordinal);
}
```

在配置中新增：

```csharp
public string SelectedStageKey { get; set; } = string.Empty;

public Dictionary<string, BeastmasterCharacterProgress> ProgressByCharacter
    { get; set; } = new(StringComparer.Ordinal);
```

### 角色键规则

按以下优先级生成角色键：

1. 当前角色 `ContentId`，格式建议为十进制字符串。
2. 无法取得 `ContentId` 时使用 `角色名@服务器`。
3. 未登录时返回空值，不创建进度容器。

需要在 `DalamudApi` 中增加实际使用到的服务，例如 `IClientState` 和 `IPlayerState`。不要提前注入后续阶段才需要的全部服务。

### 临时数据

在正式驯兽师资料整理完成前，只放 2 个明确标记为示例的阶段，每个阶段放 2 至 4 个目标。示例数据只用于验证模型和 UI，不应伪装成真实游戏资料。

### UI 范围

主窗口先采用简单布局：

```text
左侧：阶段列表
右侧：阶段名称、说明、完成度和目标复选框
```

这一阶段不要创建复杂总览、悬浮窗、卡片系统或大型主题样式。

### 验收标准

- 登录角色后勾选目标，关闭并重载插件后状态仍然存在。
- 不同角色拥有独立进度。
- 未登录时不会写入空角色或公共角色数据。
- 切换阶段后保持最后选择的阶段。
- 阶段完成度计算正确。
- `dotnet build` 为 0 个错误。

## 第三步：录入真实资料

基础模型稳定后再整理真实驯兽师数据。

### DEBUG 客户端资料采集

主窗口提供独立的 `DEBUG` 左侧栏目，用于从当前国服客户端读取整理静态目录所需的原始资料。需要客户端核对的内容应提供显式按钮，由用户按需读取并复制结果后回传，不在后台自动采集。

当前采集范围：

- 按名称关键词查询职业、任务、物品、NPC、怪物和副本，输出显示名称与 `RowId`。
- 任务查询额外输出 `IssuerStart`、`IssuerLocation`、`JournalGenre`、`ClassJobCategory`、前置任务以及开始 NPC 的地图和世界坐标。
- “采集当前所有任务状态”读取当前普通任务列表，输出完整任务 `RowId`、运行时任务 ID、名称、`Sequence`、`Flags`、变量和接取职业，供后续所有任务目标采集复用。
- 读取当前角色的名称、服务器和 `ContentId`。
- 读取当前位置的 `TerritoryType`、区域、地图 `RowId` 和世界坐标。
- 查询结果只显示在 DEBUG 页并允许复制，不写入角色进度配置。

已核对的客户端资料：

- 国服职业名称：`驯兽师`，`ClassJob.RowId = 43`。
- 驯兽师首个任务：`驯养魔兽之人`，`Quest.RowId = 71026`。
  - `IssuerStart = 1059317`，开始 NPC 为 `慌张的冒险者`。
  - `IssuerLocation = 12562772`，格里达尼亚新街，`TerritoryType = 132`，`Map.RowId = 2`。
  - 世界坐标：`X = 28.467`，`Y = -8.2`，`Z = 122.706`。
  - `JournalGenre = 198`，`ClassJobCategory = 34`，`PreviousQuest = 70058`。
  - 任务接取后的运行时状态：`RuntimeQuestId = 5490`，`Sequence = 1`，`Flags = 0`，`AcceptClassJob = 31`，`Variables = 0,0,0,0,0,0`。
  - `Sequence = 1` 的任务目标位于黑衣森林中央林区：`TerritoryType = 148`，`Map.RowId = 4`，世界坐标 `X = -318.654`、`Y = 60.947`、`Z = -129.382`。
  - 只有运行时任务序列与已核对目标定义精确匹配时才启用任务目标导航；其他序列继续等待采集。
- 任务关键词“驯兽”命中：
  - `Quest.RowId = 68573`，原始客户端文本 ` 战史科士官与顽固的驯兽人`，去除图标后的名称为 `战史科士官与顽固的驯兽人`。
  - `Quest.RowId = 71029`，名称 `露流派的驯兽术`。
  - `Quest.RowId = 71033`，名称 `赎罪的驯兽师`。
  - `Quest.RowId = 71045`，名称 `将驯兽之路登峰造极`。
  - `Quest.RowId = 68573` 属于 `JournalGenre = 58`、`ClassJobCategory = 142`，不是驯兽师职业任务，已从任务页排除。
  - `Quest.RowId = 71029`：`JournalGenre = 198`、`ClassJobCategory = 203`、`PreviousQuest = 71028`；开始 NPC `嘉·尤哈·提亚`，黑衣森林中央林区，`TerritoryType = 148`、`Map.RowId = 4`，世界坐标 `119.923, -7.003, -87.474`。
  - `Quest.RowId = 71033`：`JournalGenre = 198`、`ClassJobCategory = 203`、`PreviousQuest = 71032`；开始 NPC `劳妲`，黑衣森林中央林区，`TerritoryType = 148`、`Map.RowId = 4`，世界坐标 `25.528, -6.003, 67.521`。
  - `Quest.RowId = 71045`：`JournalGenre = 0`、`ClassJobCategory = 203`、`PreviousQuest = 71037`；开始 NPC `西尔蒙`，黑衣森林中央林区，`TerritoryType = 148`、`Map.RowId = 4`，世界坐标 `19.453, -6.007, 59.934`。
  - 前置任务 `71028`、`71032`、`71037` 的名称没有“驯兽”，说明关键词查询不能覆盖完整任务链。DEBUG 提供按 `JournalGenre = 198` 或 `ClassJobCategory = 203` 查询任务链候选的专用按钮。
- 已通过任务链查询确认 13 项线性驯兽师任务，按客户端前置关系排序：
  - `71026` 驯养魔兽之人，前置 `70058`。
  - `71027` 最初的搭档，前置 `71026`。
  - `71028` 与魔兽心意相通，前置 `71027`。
  - `71029` 露流派的驯兽术，前置 `71028`。
  - `71030` 奇盘上的战斗，前置 `71029`。
  - `71031` 向师姐学习哥布！，前置 `71030`。
  - `71032` 同门较量，前置 `71031`。
  - `71033` 赎罪的驯兽师，前置 `71032`。
  - `71034` 贪食无厌加特勒，前置 `71033`。
  - `71035` 牢不可破的牵绊，前置 `71034`。
  - `71036` 猛者的试炼：高段第一盘，前置 `71035`。
  - `71037` 奇盘上的王者：高段第二盘，前置 `71036`。
  - `71045` 将驯兽之路登峰造极，前置 `71037`。
- `71026` 至 `71035` 使用 `JournalGenre = 198`；`71036`、`71037`、`71045` 的 `JournalGenre = 0`。除入口任务 `71026` 的 `ClassJobCategory = 34` 外，其余任务均为 `ClassJobCategory = 203`。
- 物品关键词“驯兽”命中：
  - `Item.RowId = 47474`，名称 `驯兽师之证`。
  - `Item.RowId = 51273`，名称 `驯兽师水晶奖杯`。
  - `Item.RowId = 51994`，名称 `肖像教材：驯兽师`。
- NPC 关键词“驯兽”命中：
  - `ENpcResident.RowId = 1005848`，名称 `灰党驯兽人`。
  - `ENpcResident.RowId = 1005860`，名称 `灰党驯兽人`。
  - `ENpcResident.RowId = 1008327`，名称 `灰党驯兽人`。
  - `ENpcResident.RowId = 1014710`，名称 `值班的驯兽人`。
  - `ENpcResident.RowId = 1022916`，名称 `阿拉米格解放军驯兽人`。
  - `ENpcResident.RowId = 1031541`，名称 `可疑的驯兽师`。
  - `ENpcResident.RowId = 1031546`，名称 `可疑的驯兽师`。
  - `ENpcResident.RowId = 1031550`，名称 `可疑的驯兽师`。
  - `ENpcResident.RowId = 1038650`，名称 `星战士团的驯兽人`。
  - `ENpcResident.RowId = 1041521`，名称 `天测园的驯兽师`。
  - `ENpcResident.RowId = 1051043`，名称 `佩鲁佩鲁族驯兽人`。
- 怪物关键词“驯兽”命中：
  - `BNpcName.RowId = 4501`，名称 `红莲节驯兽人`。
  - `BNpcName.RowId = 4965`，名称 `黑涡团驯兽人`。
  - `BNpcName.RowId = 9390`，名称 `第四军团驯兽师`。
  - `BNpcName.RowId = 10203`，名称 `第四军团驯兽师`。
  - `BNpcName.RowId = 11084`，名称 `种畜研究所的驯兽人`。
- 副本关键词“驯兽”在 `ContentFinderCondition` 中无匹配结果。
- 当前位置读取已验证：金碟游乐场 `TerritoryType = 144`，`Map.RowId = 196`。坐标只用于验证读取功能，不作为驯兽师资料录入。
- 当前角色读取已验证。角色名称、服务器和 `ContentId` 属于个人角色数据，不记录在项目文档或静态目录中。

以上关键词查询结果是客户端候选资料。除职业名称与 `ClassJob.RowId` 外，仍需结合任务前后置关系、职业限制和可靠来源判断哪些条目属于正式驯兽师成长阶段，不能只因名称命中就录入静态目录。

实现客户端 API 时可以参考 `E:\git\Phantom` 中已经验证过的 Dalamud、Lumina 和 ImGui 用法，但不得依赖或修改该项目的运行时配置，也不得把其中的导航、FATE、自动追踪或收藏扫描逻辑带入当前阶段。

工作内容：

- 确认国服实际职业名称和游戏内中文文本。
- 整理解锁任务、职业任务、等级阶段和关键目标。
- 为每项资料记录可靠来源。
- 记录任务 RowId、物品 RowId、地图 TerritoryType 等稳定标识。
- 将示例阶段替换为经过核对的真实资料。

验收标准：静态目录可完整浏览，名称和标识经过客户端数据或可信资料核对。

## 第四步：目标导航

从 `Phantom` 参考并抽取 `VnavService`，但将其改为接收通用驯兽师目标，而不是武器目标。

工作内容：

- 地图坐标转世界坐标。
- 设置游戏地图 Flag。
- Lifestream 跨地图传送。
- vnavmesh 寻路。
- 停止导航。
- 显示导航状态和失败原因。

规则：只有经过核对的明确坐标才能启用直接导航。不确定坐标只显示地图或文字提示。

验收标准：同地图和跨地图目标均可导航，导航失败不会破坏进度，未安装 IPC 依赖时插件仍可正常使用手动功能。

## 第五步：自动追踪

在手动进度和导航稳定后增加自动识别。

优先顺序：

1. 客户端可直接读取的职业、等级、解锁和物品状态。
2. 当前 FATE 状态。
3. 聊天中的击杀和任务完成文本。
4. 需要推断的复杂事件。

所有自动识别都必须保留手动勾选和取消入口。聊天文本匹配需要考虑客户端语言和重复消息，并做好去重。

验收标准：自动状态与手动状态不会互相覆盖错误；重复事件不重复累计；无法识别时仍能手动完成。

## 第六步：收藏扫描

参考 `Phantom/Features/Yokai/YokaiProgressService.cs`，建立独立收藏目录和扫描服务。

工作内容：

- 定义收藏分类和奖励条目。
- 扫描背包、装备和兵装库。
- 按实际需要读取陆行鸟鞍囊、雇员缓存、收藏柜或投影台缓存。
- 区分“客户端明确拥有”和“缓存中找到”。
- 按角色保存最近扫描结果和时间。

验收标准：扫描范围在 UI 中清楚显示；未加载的缓存不会被当作“未持有”；不同角色结果独立。

## 第七步：悬浮目标窗口

只显示当前阶段最重要的下一步目标：

- 当前阶段与完成度。
- 下一个未完成目标。
- 传送、导航和停止导航操作。
- 折叠、隐藏已完成和关闭选项。

窗口需要适配不同字体和缩放比例，避免固定高度、固定文本宽度和无条件同行布局。

## 第八步：提醒和外部 IPC

最后再考虑：

- FATE 关注和提醒。
- EdgeTTS 语音。
- AutoDuty 副本执行。
- 其他插件 IPC。

这些能力必须是可选增强项。依赖插件未安装或 IPC 不可用时，驯兽师助手的基础功能仍需正常工作。

## 工程约束

- 不修改或依赖 `E:\git\Phantom` 的运行时配置。
- `Beastmaster` 使用独立程序集、命令、配置和版本号。
- 静态资料、角色状态、追踪服务和 UI 绘制分开。
- 不把所有页面继续堆入一个大型 `PluginUI.cs`。
- 数据模型使用稳定键，不使用显示名称作为唯一标识。
- 配置结构发生变化时递增 `Version` 并实现必要迁移。
- 自动识别不能移除手动操作能力。
- 每个任务结束前执行 `dotnet build`。

## 新任务建议提示词

```text
请继续开发 E:\git\Beastmaster。先阅读 docs/ROADMAP.md，然后只完成“下一任务：基础进度模型”，包括静态示例阶段、按角色保存手动进度和简单阶段 UI。不要实现导航、自动追踪、FATE 或收藏扫描。完成后运行 dotnet build。
```
