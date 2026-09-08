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

## 当前状态

插件已进入公开发布阶段，基础功能全部完成。最新版本 `0.1.7.0`，标题栏动态显示版本号。

### 已完成功能

**任务系统**

- 13 项驯兽师线性任务链（71026→71027→...→71037→71045）
- 客户端实时任务状态读取，每 2 秒自动刷新
- 前置条件绿/红文字提示（已完成/未完成）
- 开始 NPC 导航：Lifestream 跨地图传送 + vnavmesh 路径寻找
- 任务目标坐标已核对：71026 Sequence 1（TerritoryType=148，Map=4，X=-318.654, Y=60.947, Z=-129.382）

**魔兽图鉴**

- 50 条图鉴数据，与灰机wiki对齐（名称、等级、获取方式）
- 每条显示：编号、名称、等级、所属地图、获取方式、完成状态
- 6 列表格布局，支持点击导航按钮直达
- 飞行导航自动上坐骑，8 秒超时或战斗中降级为步行
- 停止导航按钮
- "按地图排序"黄色复选框（当前地图优先）
- "隐藏已捕获魔兽"复选框
- **WIKI** 链接按钮，跳转 `ff14.huijiwiki.com/wiki/魔兽图鉴`
- **等级** 列，数据来源于wiki

**快捷指令**

- "魔兽图鉴"按钮：通过 UIModule 执行 `/魔兽图鉴`（不使用 ICommandManager）

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

**配置**

- Version 6 结构：HideCapturedBeasts、AutoCompleteCatalogFromChat、AutoCaptureEnabled、AutoCaptureTryCapture
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
| 40 | 幽灵 | 134 | 15 | `X=-58.393, Y=27.135, Z=-119.884` |

**DEBUG 页面**

- 按名称搜索：任务、物品、NPC、怪物、副本、职业
- 任务查询额外输出坐标、前置、JournalGenre 等
- 当前所有任务状态采集（运行时 ID、Sequence、Flags、Variables）
- 当前角色和位置信息
- 「读取自动捕获 ID」输出技能 Action、捕获状态 Status 和当前目标状态

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
- 魔兽图鉴 50 条名称与 wiki 对齐，但暗光明骑士（第50条）可能对应贝希摩斯，需实际游戏验证

## 后续建议（优先级从高到低）

### 1. 补全任务目标坐标

- 采集 13 个任务每个 Sequence 的实际地图坐标
- 坐标经客户端核对后启用目标导航按钮

### 2. 任务追踪自动化

- 监听任务 Sequence 变化，自动更新完成状态
- 任务完成时弹出提示

### 3. FATE 自动追踪

- 检测驯兽师相关 FATE 出现
- FATE 到期提醒

### 4. 悬浮目标窗口

- 紧凑窗口显示当前阶段、下一个目标、导航按钮
- 适配不同字体和缩放

### 5. 收藏扫描

- 扫描背包/装备/兵装库中的魔兽相关物品
- 按角色保存扫描结果

## 工程约束

- 不修改或依赖 `E:\git\Phantom` 的运行时配置
- `Beastmaster` 使用独立程序集、命令、配置和版本号
- 静态资料、角色状态、追踪服务和 UI 绘制分开
- 数据模型使用稳定键，不使用显示名称作为唯一标识
- 配置结构变化时递增 `Version` 并实现迁移
- 自动识别不能移除手动操作能力
- 每次提交前执行 `dotnet build` 确认 0 错误
