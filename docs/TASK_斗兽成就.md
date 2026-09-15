# 斗兽成就实施方案

## 目标

在「斗兽奇弈」栏目的「斗兽成就」子页签中，展示驯兽师相关 44 项成就的完成情况，并新增「同步当前角色」按钮，将当前角色的成就完成状态按角色持久化保存。

核心原则：**成就以 ID（Lumina `Achievement` sheet 的 RowId）为稳定标识**，不使用中文名称作为唯一键。

## 数据来源

### 1. 成就完成状态（客户端内存，FFXIVClientStructs）

已确认 FFXIVClientStructs 提供现成只读 API，位于 `FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement`：

| API | 说明 |
|---|---|
| `Achievement.Instance()` | 静态单例，返回 `Achievement*`，签名 `48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 04 30 FF C3` |
| `State`（`+0x08`） | `AchievementState` 枚举：`Invalid=0` / `Requested=1` / `Loaded=2` |
| `IsLoaded()` | 等价于 `State == Loaded`，成就数据是否已从服务器加载 |
| `RequestCompletedAchievements()` | 主动向服务器请求成就完成数据 |
| `IsComplete(int achievementId)` | 检查指定成就 ID 是否完成，返回 `bool` |

注意：成就完成数据**仅在请求后加载**（玩家打开成就菜单或调用 `RequestCompletedAchievements()`）。同步前必须先确保 `IsLoaded()`，未加载时调用 `RequestCompletedAchievements()` 并轮询等待。

### 2. 成就静态数据（Lumina `Achievement` sheet）

已通过反射确认 `Lumina.Excel.Sheets.Achievement` 字段：

| 字段 | 类型 | 用途 |
|---|---|---|
| `RowId` | `uint` | 成就 ID（即稳定标识） |
| `Name` | `ReadOnlySeString` | 成就名称 |
| `Description` | `ReadOnlySeString` | 成就描述 |
| `Icon` | `uint` | 图标 ID |
| `Title` | `RowRef<Title>` | 称号（如「危险玩家」「兽群之主」） |
| `Points` | `byte` | 成就点数（5 / 10 / 20） |
| `AchievementCategory` | `RowRef<AchievementCategory>` | 成就分类 |

名称、描述、点数、称号均从 Lumina 按 RowId 读取，避免在代码里硬编码 44 条中文文本，且能跟随客户端本地化（国服中文）。

## 成就清单与分组

共 44 项成就，按逻辑分为 11 组。每组仅保存成就 ID 列表；展示文本从 Lumina 读取。

| 分组 Key | 分组名 | 成就 ID |
|---|---|---|
| `rate` | 魔兽率舞 | 4028, 4029, 4030, 4031, 4032 |
| `clear` | 斗兽争奇 | 4033, 4034, 4035 |
| `high-clear` | 出奇制胜 | 4036, 4037 |
| `beast-iii` | 不足为奇 | 4038, 4039, 4040 |
| `high-beast-iii` | 奇高一着 | 4041, 4042 |
| `rating` | 盘评价 | 4043, 4044, 4045, 4046, 4047, 4048 |
| `high-rating` | 高段评价 | 4049, 4050, 4051, 4052 |
| `overall` | 全盘综合 | 4053, 4054, 4055 |
| `training` | 训练有素 | 4056, 4057, 4058, 4059, 4060 |
| `high-reward` | 高段奖励 | 4061, 4062, 4063, 4064, 4065, 4066, 4067, 4068 |
| `ranking` | 排名 | 4075, 4076, 4077 |

## 数据模型

### 静态模型 `BeastmasterAchievementCatalog`

```csharp
public sealed record BeastmasterAchievementGroup(string Key, string Name, int[] AchievementIds);

public static class BeastmasterAchievementCatalog
{
    public static IReadOnlyList<BeastmasterAchievementGroup> Groups { get; }
}
```

参照 `BeastmasterCatalog` 的写法：静态只读列表，不写内存。

### 持久化模型（配置升级 Version 35）

在 `BeastmasterCharacterProgress` 新增字段：

```csharp
public HashSet<int> CompletedAchievements { get; set; } = new();
```

`BeastmasterConfiguration.Version` 由 34 升级为 35，`Initialize` 中新增迁移分支（为已有角色的 `CompletedAchievements` 补默认值）。

## 进度服务扩展

`BeastmasterProgressService` 新增：

```csharp
public bool IsAchievementCompleted(int achievementId);
public int ReplaceAchievementProgress(IReadOnlySet<int> completedIds); // 返回变更数，与 ReplaceCatalogProgress 同构
```

## 同步服务 `BeastmasterAchievementSyncService`

参照 `BeastmasterCatalogSyncService` 的异步状态机，但不依赖打开窗口：

```csharp
public sealed unsafe class BeastmasterAchievementSyncService
{
    public string Status { get; }      // 同步状态文本
    public bool IsScanning { get; }
    public string Diagnostic { get; }  // 失败诊断
    public void RequestSync();         // 启动同步
    public void Update();              // 每帧推进状态机
    public void Start();               // 订阅 Framework.Update
    public void Dispose();
}
```

### 同步流程

1. `RequestSync()`：校验已登录；记录开始时间；`scanning = true`。
2. `Update()`（每帧）：
   - 未登录 → 取消同步。
   - 取 `Achievement.Instance()`，为空 → 输出「成就系统不可用」。
   - `!IsLoaded()` → 调用 `RequestCompletedAchievements()`，设置「等待成就数据加载…」，每 500ms 重试。
   - `IsLoaded()` → 遍历 44 个成就，`IsComplete(id)` 汇总为 `completedIds`，调用 `progressService.ReplaceAchievementProgress(completedIds)`。
   - 输出「同步完成：已完成 X/44，更新 Y 项」。
   - 全程超时保护（15 秒），异常时输出诊断、不改进度。

### 安全边界

- 全程只读，不写入游戏内存，不修改成就状态。
- 空指针、未加载、超时均安全降级，不抛异常。
- 客户端版本更新后 FFXIVClientStructs 签名失效时，输出诊断、不影响其他功能。

## UI 设计

`DrawBeastArenaAchievements` 从 `static` 改为实例方法，接入 `progressService` 与 `achievementSyncService`。

布局：

```
斗兽成就                            [同步当前角色成就]  <状态文本>
─────────────────────────────────────────────
进度条 [已完成/44]

魔兽率舞
  4028 魔兽率舞1   让10种魔兽成为同伴。       [5] ✓
  4029 魔兽率舞2   让20种魔兽成为同伴。       [5] ✓
  ...

斗兽争奇
  ...
```

- 每个成就算一行：`ID` `名称` `描述`，右侧显示点数（`[5]`/`[10]`/`[20]`）与完成标记（✓ 绿色 / ✗ 灰色）。
- 有称号的成就（`Title` 非空）在描述后追加「称号：xxx」。
- 完成后显示绿色，未完成灰色。
- 顶部「同步当前角色成就」按钮调用 `achievementSyncService.RequestSync()`；同步中禁用按钮，显示状态文本。
- 若 `Diagnostic` 非空，提供「复制同步诊断」按钮（与图鉴同步一致）。

## 实施步骤

1. 新增 `Features/Achievement/BeastmasterAchievementCatalog.cs`（静态分组数据）。
2. `BeastmasterProgressModels.cs`：`BeastmasterCharacterProgress` 增加 `CompletedAchievements`。
3. `BeastmasterConfiguration.cs`：`Version` → 35，`Initialize` 增加迁移分支（补 `CompletedAchievements` 默认值）。
4. `BeastmasterProgressService.cs`：增加 `IsAchievementCompleted` / `ReplaceAchievementProgress`。
5. 新增 `Features/Achievement/BeastmasterAchievementSyncService.cs`。
6. `PluginUI.cs`：`DrawBeastArenaAchievements` 改为实例方法并实现 UI；构造函数接入 `achievementSyncService`。
7. `BeastmasterPlugin.cs`：实例化、`Start`、`Dispose` 同步服务，传入 UI。
8. `dotnet build` 确认 0 错误。

## 完成标准

- 44 项成就按 ID 稳定映射，名称/描述/点数/称号从 Lumina 读取，不硬编码中文。
- 「同步当前角色成就」能读取当前角色完成状态并按角色持久化。
- 未登录、成就数据未加载、客户端结构异常时安全降级，不影响其他功能。
- 重新登录不串角色；未同步时状态文本显示「尚未同步」，同步后显示「同步完成：已完成 X/44，更新 Y 项」。
