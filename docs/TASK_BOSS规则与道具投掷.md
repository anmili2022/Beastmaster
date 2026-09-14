# 任务：BOSS规则验证与道具投掷

## 任务概述

两个独立任务：

1. **验证现有规则系统**：BOSS持有指定BUFF ID时自动释放对应技能的规则是否正常工作
2. **新增道具投掷功能**：目标是指定DATAID的BOSS时，自动从背包投掷攻击道具

---

## 任务一：BOSS BUFF规则验证

### 背景

规则模式已支持五种检测类型：
- `SelfStatus`：自身BUFF检测
- `TargetStatus`：当前目标BUFF检测
- `DataIdStatus`：指定DATAID对象的BUFF检测
- `DataIdCast`：指定DATAID对象的读条检测
- `TargetCast`：当前目标读条检测

本次验证重点关注 `TargetStatus` 和 `DataIdStatus` 在BOSS战中的实际表现。

### 验证目标

确认以下场景规则能正确触发：

| 场景 | 条件类型 | 条件ID | 预期行为 |
|------|----------|--------|----------|
| BOSS给自己加增益BUFF | TargetStatus + Present |BUFF ID| 释放对应技能 |
| BOSS失去增益BUFF | TargetStatus + Missing |BUFF ID| 释放对应技能 |
| 场景中指定DATAID的BOSS有BUFF | DataIdStatus + Present |BUFF ID| 释放对应技能 |
| 场景中指定DATAID的BOSS无BUFF | DataIdStatus + Missing |BUFF ID| 释放对应技能 |

### 验证步骤

#### 准备工作

1. 在规则模式中创建测试规则集
2. 设置区域为斗兽塔（1339~1343）或目标副本区域
3. 创建测试规则：
   - 名称：`测试BUFF规则`
   - 检测：`TargetStatus`
   - 条件：`Present`
   - 检测ID：输入已知的BOSS增益BUFF ID
   - 技能：选择要释放的技能（如碎击斩 `44879`）
4. 开启规则诊断完整模式

#### 验证流程

1. 进入战斗，选中BOSS
2. 等待BOSS施加目标BUFF
3. 观察：
   - [ ] 规则是否命中（聊天框 `[驯兽师助手 HH:mm:ss]` 输出）
   - [ ] 技能是否成功释放
   - [ ] 技能释放后BUFF是否被驱散或效果是否生效
4. 等待BUFF消失
5. 观察：
   - [ ] 规则是否再次命中（Missing条件）
   - [ ] 技能是否释放

#### DataIdStatus验证

1. 创建第二条测试规则：
   - 检测：`DataIdStatus`
   - DataId：输入BOSS的NPC DataId
   - 条件：`Present`
   - 检测ID：BUFF ID
   - 技能：对应技能
2. 进入战斗，不选中BOSS（或选中其他目标）
3. 观察：
   - [ ] 规则是否通过DataId找到BOSS并检测BUFF
   - [ ] 技能是否对当前目标释放（规则技能始终对当前手动目标释放）

### 预期结果

- 规则命中时输出诊断信息
- 技能成功释放且无报错
- BUFF存在/缺失条件判断准确
- DataId检测能遍历场景中的BOSS

### 可能的问题

| 问题 | 原因分析 | 解决方案 |
|------|----------|----------|
| 规则不触发 | BUFF ID输入错误 | 使用DEBUG确认BUFF StatusId |
| 规则触发但技能失败 | 技能不可用/冷却中 | 检查GetActionStatus |
| DataId找不到对象 | DataId错误或对象已死亡 | 确认BOSS的BaseId |
| 诊断无输出 | 规则诊断开关未开启 | 检查规则诊断全局开关 |

---

## 任务二：目标DATAID道具投掷

### 需求描述

当当前目标是指定DATAID的BOSS时，自动从背包中投掷攻击道具（如奇弈道具中的攻击类道具）。

### 与现有功能的区别

| 对比项 | 低血量恢复药（已实现） | 目标DATAID投掷（新功能） |
|--------|------------------------|--------------------------|
| 触发条件 | 玩家血量 < 阈值 | 目标DATAID匹配 |
| 道具类型 | 恢复药（76/77/78） | 攻击道具（待定） |
| 目标 | 玩家自身 | 当前敌对目标 |
| 执行方式 | RaptureHotbarModule.ExecuteSlot | 待定（需验证） |
| 区域限制 | 斗兽塔 | 待定 |

### 技术方案

#### 方案A：复用规则模式

新增规则条件类型 `TargetDataIdItem`：
- 条件：当前目标DATAID匹配
- 动作：从背包投掷指定道具

优点：复用现有规则UI和执行框架
缺点：规则模式仅在战斗中运行，道具投掷可能需要战前使用

#### 方案B：独立功能模块

新增独立的"道具投掷"配置：
- 配置项：DATAID → 道具ID映射
- 触发条件：目标DATAID匹配
- 执行方式：参考CrucibleItemService的ExecuteSlot或UseItem

优点：独立控制，可战前触发
缺点：需要新增UI和配置

### 实现步骤

#### 第一步：确定道具执行方式

需要验证攻击道具的执行方式：

1. **检查道具类型**：
   - 奇弈道具（内部ID 76~143）→ ExecuteSlot + HotbarSlotType 36
   - 普通道具 → ActionManager.UseAction(ActionType.Item, itemId)

2. **验证背包读取**：
   - 普通道具：读取InventoryContainer
   - 奇弈道具：读取Agent 497 + InstanceContentDirector

3. **验证目标投掷**：
   - 需要设置SoftTarget为目标
   - 或使用ActionManager对目标使用道具

#### 第二步：配置数据结构

```csharp
// 新增配置项
public List<BeastmasterItemThrowRule> ItemThrowRules { get; set; } = [];

public sealed class BeastmasterItemThrowRule
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "投掷规则";
    public uint TargetDataId { get; set; }        // 目标BOSS的DataId
    public uint ItemId { get; set; }               // 要投掷的道具ID
    public BeastmasterItemThrowArea Area { get; set; } // 区域限制
    public List<ushort> TerritoryIds { get; set; } = [];
}
```

#### 第三步：执行逻辑

```csharp
public bool TryThrowItem(ActionManager* am, IBattleChara target, DateTime now)
{
    if (!Enabled || !InCombat) return false;
    if (!MatchesTargetDataId(target)) return false;
    if (!HasItemInInventory()) return false;
    if (IsOnCooldown()) return false;
    
    // 执行投掷
    return ExecuteItemThrow(target);
}
```

#### 第四步：UI配置

在自动输出栏目新增"道具投掷"配置区：
- 启用开关
- DATAID → 道具映射列表
- 区域限制
- 冷却时间设置

### 验证步骤

#### 准备工作

1. 确定目标BOSS的DataId
2. 确定要投掷的道具ID
3. 在背包中放置道具
4. 配置投掷规则

#### 验证流程

1. 进入副本，选中目标BOSS
2. 观察：
   - [ ] 是否检测到目标DATAID匹配
   - [ ] 道具是否成功投掷
   - [ ] 投掷后道具数量是否减少
   - [ ] 投掷效果是否对BOSS生效
3. 切换到非目标BOSS
4. 观察：
   - [ ] 是否不触发投掷
5. 道具用完后
6. 观察：
   - [ ] 是否有诊断提示

### 预期结果

- DATAID匹配时自动投掷道具
- 道具投掷成功且效果生效
- 道具用完后有诊断提示
- 非目标BOSS不触发投掷

### 可能的问题

| 问题 | 原因分析 | 解决方案 |
|------|----------|----------|
| 道具无法投掷 | 执行方式错误 | 验证ExecuteSlot或UseAction |
| 目标不匹配 | DataId输入错误 | 使用DEBUG确认BOSS DataId |
| 道具数量不减少 | 读取背包方式错误 | 检查Inventory读取逻辑 |
| 投掷无效果 | 道具类型不支持攻击 | 确认道具效果类型 |

---

## 优先级

| 任务 | 优先级 | 预估工作量 |
|------|--------|------------|
| 任务一：BOSS BUFF规则验证 | 高 | 0.5天（测试） |
| 任务二：目标DATAID道具投掷 | 高 | 2~3天（开发+测试） |

## 依赖项

- 任务一：现有规则模式代码
- 任务二：
  - BeastmasterCrucibleItemService（参考ExecuteSlot实现）
  - 道具ID和效果确认（需要游戏内验证）
  - 目标DATAID获取方式（ObjectTable.BaseId）

## 参考资料

- `Features/Rules/BeastmasterRuleModels.cs`：规则条件类型定义
- `Features/Rules/BeastmasterRuleService.cs`：规则执行逻辑
- `Features/Capture/BeastmasterCrucibleItemService.cs`：道具执行参考
- `docs/BST_ACR_DESIGN.md`：规则模式设计说明
