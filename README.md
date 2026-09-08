# 驯兽师助手

用于追踪最终幻想 XIV 驯兽师任务链和魔兽图鉴收集进度的 Dalamud 插件。

## 功能

- 显示驯兽师任务链及客户端任务状态。
- 收到“成功结识了……种的魔兽！”消息时，自动记录当前角色的图鉴进度。
- 支持手动修改、隐藏已捕获魔兽，并按地图排列图鉴目标。
- 支持任务接取点和野外图鉴目标导航。
- 可配合 vnavmesh 和 Lifestream 完成同地图移动及跨地图传送。

## 安装

在 Dalamud 设置的自定义插件仓库中添加：

```text
https://raw.githubusercontent.com/anmili2022/Beastmaster/main/repo.json
```

## 指令

- `/beastmaster`：打开驯兽师助手。
- `/驯兽师`：打开驯兽师助手。

## 构建

```powershell
dotnet build
```

构建结果位于 `output\Beastmaster.dll`。

发布流程见 [docs/release.md](docs/release.md)。
