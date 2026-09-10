# 驯兽师助手

用于追踪最终幻想 XIV 驯兽师任务链和魔兽图鉴收集进度的 Dalamud 插件。

## 功能

- 显示驯兽师任务链及客户端任务状态。
- 收到“成功结识了……种的魔兽！”消息时，自动记录当前角色的图鉴进度。
- 支持手动修改、隐藏已捕获魔兽，并按地图排列图鉴目标。
- 支持任务接取点和野外图鉴目标导航。
- 可配合 vnavmesh 和 Lifestream 完成同地图移动及跨地图传送。
- 魔兽图鉴显示属性、大招和释放技能信息。
- 自动输出悬浮窗显示当前魔兽和驯兽师量谱状态。
- 高级技能支持独立开关和只读可用性判断。

## 安装

在 Dalamud 设置的自定义插件仓库中添加：

```text
https://raw.githubusercontent.com/anmili2022/Beastmaster/main/repo.json
```

## 指令

- `/beastmaster`：打开驯兽师助手。
- `/驯兽师`：打开驯兽师助手。
- `/驯兽师 输出`：切换自动输出和暂停状态；关闭时开启，暂停时恢复，运行时暂停。
- `/驯兽师 暂停`：暂停自动输出但保留开关状态。
- `/驯兽师 恢复`：恢复已暂停的自动输出。
- `/驯兽师 关闭`：关闭自动输出。

以上自动输出子命令也支持英文命令 `/beastmaster output|pause|resume|off`。

## 构建

```powershell
dotnet build
```

构建结果位于 `output\Beastmaster.dll`。

量谱资料见 [docs/BST_GAUGE.md](docs/BST_GAUGE.md)，ACR 设计见 [docs/BST_ACR_DESIGN.md](docs/BST_ACR_DESIGN.md)，斗兽塔第一盘资料见 [docs/BEAST_ARENA_ROUND_1.md](docs/BEAST_ARENA_ROUND_1.md)，开发路线见 [docs/ROADMAP.md](docs/ROADMAP.md)。

发布流程见 [docs/release.md](docs/release.md)。
