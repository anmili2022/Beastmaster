# 发布流程

## 首次发布准备

项目创建为公开 GitHub 仓库并推送 `main` 后，自定义插件仓库地址为：

```text
https://raw.githubusercontent.com/anmili2022/Beastmaster/main/repo.json
```

首个版本已经预设为 `0.1.0.0`。首次推送源码后，从干净且与 `origin/main` 同步的 `main` 分支执行：

```powershell
.\scripts\release.ps1 0.1.0.0
```

## 后续发布

版本格式为 `主.次.修订.构建`，tag 不使用 `v` 前缀。每次发布选择一个尚未使用的新版本，例如：

```powershell
.\scripts\release.ps1 0.1.1.0
```

脚本会检查分支与远端状态，更新 `Beastmaster.csproj`、`Beastmaster.json` 和 `repo.json`，执行 Release 构建，提交并推送版本，然后创建 tag。GitHub Actions 会打包以下文件并创建 Release：

```text
Beastmaster.dll
Beastmaster.json
Beastmaster.deps.json
```

只预览版本文件变化：

```powershell
.\scripts\release.ps1 0.1.1.0 -DryRun
```

推送 tag 后不等待 CI：

```powershell
.\scripts\release.ps1 0.1.1.0 -NoWait
```

## 发布后检查

1. GitHub Release 中存在 `Beastmaster.zip`。
2. zip 内只有 DLL、插件清单和 deps 文件。
3. `repo.json` 的版本和三个下载链接指向本次 Release。
4. 在 Dalamud 中添加自定义仓库地址，验证安装、图标、启动和更新。
