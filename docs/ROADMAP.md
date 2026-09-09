# 驯兽师助手开发路线

## 项目位置

- 项目目录：`E:\git\Beastmaster`
- 项目文件：`Beastmaster.csproj`
- 插件清单：`Beastmaster.json`
- 构建命令：`dotnet build`
- 构建产物：`output\Beastmaster.dll`
- 中文命令：`/驯兽师`
- 英文命令：`/beastmaster`
- GitHub 仓库：`anmili2022/Beastmaster`
- 自定义仓库：`https://raw.githubusercontent.com/anmili2022/Beastmaster/main/repo.json`
- Dalamud API 文档：`https://dalamud.dev/api/`

## 当前状态

插件已进入公开发布阶段，基础功能全部完成。最新版本 `0.1.8.0`，标题栏动态显示版本号。

### 已完成功能

**任务系统**

- 13 项驯兽师线性任务链（71026→71027→...→71037→71045）
- 客户端实时任务状态读取，每 2 秒自动刷新
- 前置条件绿/红文字提示（已完成/未完成）
- 开始 NPC 导航：Lifestream 跨地图传送 + vnavmesh 路径寻找
- 任务目标坐标已核对：71026 Sequence 1（TerritoryType=148，Map=4，X=-318.654, Y=60.947, Z=-129.382）

**魔兽图鉴**

- 50 条图鉴数据，与灰机wiki对齐（名称、等级、获取方式）
- 每条显示：编号、名称、属性、技能、等级、所属地图、获取方式、完成状态
- 属性按猛/坚/魔/翔彩色显示
- 技能列显示当前魔兽大招和释放技能
- 鼠标悬停技能列显示 ActionId、技能等级、射程和范围
- 8 列表格布局，支持点击导航按钮直达
- 飞行导航自动上坐骑，8 秒超时或战斗中降级为步行
- 停止导航按钮
- "按地图排序"黄色复选框（当前地图优先）
- "按等级排序"复选框，区间等级按最低等级排序
- "隐藏已捕获魔兽"复选框
- **WIKI** 链接按钮，跳转 `ff14.huijiwiki.com/wiki/魔兽图鉴`
- **等级** 列，数据来源于wiki

**推荐装备**

- 新增推荐装备栏目
- 记录驯兽师 50 级开荒装：武器、盾牌、防具、饰品和职业证
- 记录第二套 50 级 BIS，可在推荐装备页切换方案
- 装备名称可点击访问对应的灰机 Wiki 物品详情页
- 推荐装备显示背包和对应兵装库中的持有状态及数量
- 推荐装备持有状态改为固定 ItemId 检测，避免装备同名或名称变化导致误判

**快捷指令**

- "魔兽图鉴"按钮：通过 UIModule 执行 `/魔兽图鉴`（不使用 ICommandManager）
- 驯兽师魔兽体型按钮：`/beastpetsize all small`、`medium`、`large`

**自动记录**

- `BeastmasterCatalogChatTracker`：监听聊天消息，匹配 `成功结识了……种的魔兽！` 自动标记完成
- `羊羔` 别名映射到 `迷途羊羔`
- 设置页"捕获消息自动记录"开关（默认开启）

**自动捕获**

- 设置页提供自动捕获开关，默认关闭
- 只对当前手动选择的目标运行，不自动选择或切换目标
- 支持副本内运行
- 目标无「待捕获」状态时优先释放捕获
- 捕获后按驯兽师连击释放碎击斩、碎咬斧、裂盾劈
- 悬浮窗显示「自动捕获中...」或「自动攻击中...」以及下一个技能
- 悬浮窗「尝试捕获」关闭后只执行 1→2→3 连击
- 左侧独立「自动输出」栏目管理自动输出总开关
- 自动输出悬浮窗标题为「驯兽ACR」
- 当前职业不是驯兽师时自动隐藏悬浮窗，切回驯兽师后自动恢复
- 任务开始 NPC 导航复用飞行导航逻辑，支持自动上坐骑和步行回退
- 悬浮窗显示当前魔兽、属性、技力、兽力、御兽之心、兽灵之心和决策原因
- 悬浮窗可直接切换高级技能总开关、御兽协作（黄豆）和兽灵协作（蓝豆）
- 悬浮窗可直接切换“释放”开关
- 悬浮窗可直接调整捕获血量阈值，默认 80%
- 自动输出栏目提供「详细模式」开关，默认关闭
- 捕获状态按 Status.SourceId 区分自身施加和他人施加的状态
- 捕获请求增加等待结果和目标切换重置，避免短时间重复请求
- 悬浮窗右键打开设置页面

**量谱与高级技能**

- 从 `JobGaugeManager.Instance()->CurrentGauge` 只读读取驯兽师量谱
- 技力：`CurrentGauge +0x10`，上限 250
- 兽力：`CurrentGauge +0x11`，上限 250
- 当前兽笛：`CurrentGauge +0x13`
- 御兽之心/兽灵之心：`CurrentGauge +0x18` 位字段
- 每 100 毫秒采样一次，UI 使用缓存，避免每帧访问游戏内存
- 自动输出增加统一 Action 可用性结果和失败原因
- 高级技能支持手动大招；御兽协作（黄豆）和兽灵协作（蓝豆）互斥，协作流程自动包含大招
- 高级技能开启后，当前魔兽释放技能通过运行时调整 ID 和 Action 状态判断，冷却中回退基础技能

**配置**

- Version 8 结构：HideCapturedBeasts、SortCatalogByLevel、AutoCompleteCatalogFromChat、AutoCaptureEnabled、AutoCaptureTryCapture、CaptureHpThreshold、ShowGaugeInOverlay（详细模式）、AdvancedActionsEnabled、BeastHeartCooperationEnabled、BeastSoulCooperationEnabled
- 捕获血量阈值 `CaptureHpThreshold` 持久化保存，范围 1%~100%
- 按角色（ContentId）独立保存图鉴进度
- 角色键优先使用 ContentId 十进制字符串，回退使用 Name@World

**导航**

- 跨地图导航传送后自动继续寻路，使用 `BetweenAreas` 状态检测传送完成

**反馈入口**

- 侧边栏「反馈与建议」按钮（设置与 DEBUG 之间），跳转 Discord 频道

**副本与数据核对**

- 副本图鉴条目保存客户端 `ContentFinderCondition.RowId`，打开任务搜索器优先使用 ID
- DEBUG 页面提供「读取图鉴副本 ID」按钮
- 37 条野外图鉴的 TerritoryType、Map.RowId 已全部核对；已采集世界坐标按客户端资料持续补充
- vnavmesh 未就绪时每 500 毫秒检查一次，准备完成后自动恢复导航

### 已核对野外坐标

以下坐标来自国服客户端 DEBUG「当前位置」读取，导航优先使用世界坐标：

| 图鉴 | 魔兽 | TerritoryType | Map.RowId | 世界坐标 |
|---:|---|---:|---:|---|
| 16 | 螳螂 | 138 | 18 | `X=-11.055, Y=-22.468, Z=50.278` |
| 21 | 席兹 | 138 | 18 | `X=105.702, Y=-16.159, Z=164.859` |
| 25 | 精金龟 | 141 | 21 | `X=-101.513, Y=5.070, Z=238.304` |
| 33 | 长须豹 | 180 | 30 | `X=-359.599, Y=60.528, Z=-348.505` |
| 36 | 树精 | 148 | 4 | `X=332.173, Y=-1.287, Z=-344.140` |
| 39 | 魔界花 | 148 | 4 | `X=-427.656, Y=49.000, Z=33.517` |
| 40 | 妖魂 | 134 | 15 | `X=-57.661, Y=34.287, Z=-84.685` |

**DEBUG 页面**

- 按名称搜索：任务、物品、NPC、怪物、副本、职业
- 任务查询额外输出坐标、前置、JournalGenre 等
- 当前所有任务状态采集（运行时 ID、Sequence、Flags、Variables）
- 当前角色和位置信息
- 「读取自动捕获 ID」输出技能 Action、捕获状态 Status 和当前目标状态
- 「读取驯兽师量谱原始数据」只读输出 JobGauges 地址附近 64 字节，不写入内存
- 「读取魔兽属性映射」输出 50 个图鉴魔兽的 DataId、属性、IconId、大招和释放技能
- DEBUG 提供「读取当前目标状态」和「读取当前连击状态」诊断按钮
- DEBUG 提供「读取协力验证数据」按钮，一次输出量谱、目标、关键 Action 状态码和自身属性状态
- DEBUG 提供「读取推荐装备物品 ID」按钮，输出两套装备的精确匹配和候选 ItemId
- 图鉴、量谱和自动输出复用 `BeastmasterSkillProfile` 统一技能资料
- 所有 DEBUG 读取按钮自动复制结果到剪贴板

**发布流程**

- `.github/workflows/release.yml`：CI 自动构建 + GitHub Release
- `scripts/release.ps1`：本地版本更新 + 推送 + 等待 CI
- `repo.json`：Dalamud 自定义插件仓库
- `docs/release.md`：发布操作手册
- `images/icon.png`：1280×1280 插件图标
- `.csproj` 自动复制 icon.png 到输出根目录

### 当前目录结构

```text
Beastmaster/
├── Beastmaster.csproj
├── Beastmaster.json
├── repo.json
├── README.md
├── images/
│   └── icon.png
├── .github/workflows/
│   └── release.yml
├── scripts/
│   └── release.ps1
├── docs/
│   ├── ROADMAP.md
│   ├── BST_GAUGE.md
│   ├── BST_ACR_DESIGN.md
│   ├── CHANGELOG.md
│   └── release.md
├── Configuration/
│   └── BeastmasterConfiguration.cs
├── Infrastructure/
│   ├── DalamudApi.cs
│   └── GameCommandService.cs
├── Features/
│   ├── Catalog/
│   │   ├── BeastmasterCatalog.cs
│   │   └── BeastmasterCatalogChatTracker.cs
│   ├── Capture/
│   │   ├── BeastmasterActionAvailability.cs
│   │   └── BeastmasterAutoCaptureService.cs
│   ├── Debug/
│   │   ├── BeastmasterDebugDataService.cs
│   │   └── BeastmasterGaugeSnapshot.cs
│   ├── Navigation/
│   │   └── BeastmasterNavigationService.cs
│   ├── Progress/
│   │   └── BeastmasterProgressService.cs
│   └── Quests/
│       ├── BeastmasterQuestGuide.cs
│       └── BeastmasterQuestService.cs
└── UI/
    └── PluginUI.cs
```

### 已知限制

- 20 条 Dalamud SDK 程序集解析警告（与 Phantom 项目相同），不影响运行
- 任务目标导航仅 71026 Sequence 1 已核对坐标；其他任务目标坐标待采集
- 高级技能大招已接入资源、目标、ActionManager 判断和自动请求；协作技仍等待运行时窗口数据
- 协作技已接入两段请求和 4 秒待续段逻辑，第二段按当前魔兽属性选择且不再被属性状态硬阻塞；7 秒运行时协作窗口仍需上线后采集并确认对应状态字段
- 技力、兽力字段已按当前截图确认上限为 250；游戏版本更新后仍需重新核对

## 后续建议（优先级从高到低）

### 1. 上线后验证量谱

- 对照原生量谱确认技力、兽力在技能操作前后的变化
- 确认当前兽笛字段在召唤、切换和消失时的值
- 采集协作量谱和 7 秒窗口的运行时状态数据

### 2. 协作技自动释放

- 采集并确认协作量谱运行时字段和 7 秒窗口
- 在属性循环和运行时窗口均确认后接入协作技释放
- 使用独立开关、资源门槛、目标状态和动作锁定保护

### 3. 自动输出测试

- 测试未登录、切换职业、切换地图、未召唤和召唤兽死亡
- 测试目标死亡、目标切换、捕获失败和技能不可用
- 测试高级技能开关在悬浮窗和设置页之间的同步

### 4. 资料维护

- 游戏版本更新后重新执行 DEBUG 魔兽属性映射
- 更新量谱偏移、ActionId、IconId 和技能名称

### 5. 任务功能增强

- 补全任务目标坐标
- 增加任务 Sequence 变化追踪和 FATE 自动提醒

## 工程约束

- 使用卫月官方 Dalamud API 文档（`https://dalamud.dev/api/`）核对插件服务、客户端接口和 API 版本变化
- 不修改或依赖 `E:\git\Phantom` 的运行时配置
- `Beastmaster` 使用独立程序集、命令、配置和版本号
- 静态资料、角色状态、追踪服务和 UI 绘制分开
- 数据模型使用稳定键，不使用显示名称作为唯一标识
- 配置结构变化时递增 `Version` 并实现迁移
- 自动识别不能移除手动操作能力
- 每次提交前执行 `dotnet build` 确认 0 错误
