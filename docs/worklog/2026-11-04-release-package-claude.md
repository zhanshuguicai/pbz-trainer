# GitHub Release 1.0.1：自带运行时的发布包、保姆式教程、一键加载器脚本
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"release附上编译好的修改器并且附上保姆式使用教程和readme，关了，编译吧"
- 接续：[界面主题与 1.0.1 发布](2026-11-04-ui-theme-release-101-claude.md)

## 目标
在 GitHub 建 v1.0.1 Release，附可直接使用的修改器、保姆式教程和 README。

## 做了什么（大体流程，按顺序）
1. 重编本机 Debug 输出（0 警告 0 错误，payload 1.0.1）。
2. 发布版：`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none`（运行时包本机 NuGet 缓存已有，未联网下载）；单文件 140 MB + payload/。冒烟测试截图正常。
3. 冒烟测试发现并修复：
   - "游戏内模块是旧版本"提示被后续代码改回绿色 → 改为先设绿色再按需改橙色。
   - 新文件夹里首次运行会从旧 AppData 迁移旧清单，把游戏里的模块判成"被改过"并拒绝更新 → `SuiteInstaller.OnlyOwnFiles()/TakeOver()`：目录里只有本工具文件（任一版本）与运行输出时，确认后接管并覆盖更新；新增测试。
4. `release/install-loader.ps1`：不给 -GameDir 时先查默认 Steam 目录，再从注册表 SteamPath 与 libraryfolders.vdf 找所有 Steam 库；校验目录里有 PBZ.exe；保存为带 BOM 的 UTF-8（Windows PowerShell 5.1 才能正确显示中文）。
5. 新增 `docs/使用教程.md`（下载解压、三步安装、确认成功、五个功能逐项、热键表、录屏、更新、卸载、数据位置、常见问题）。README 加下载与教程链接。
6. 发布包 `PBZTrainer-1.0.1-win-x64.zip`（64.6 MB，36 个文件）：PBZManager.exe、payload、loader（= probe/p1-stage，与游戏内一致）、install-loader.ps1、安装加载器.bat、卸载加载器.bat、使用教程.md、README.md、CHANGELOG.md、LICENSE-UE4SS.txt、THIRD-PARTY.txt、SHA256SUMS.txt；不含 data/。解压测试：中文文件名正常，脚本中文输出正常，游戏运行时正确拒绝且未写入清单。
7. 提交、推送、创建 GitHub Release v1.0.1 并上传 zip（结果见 HANDOFF）。

## 改动（文件或产物；提交号，如有）
- `src/PBZManager/PBZ.Core/Suite/SuiteInstaller.cs`、`PBZManager/MainWindow.xaml.cs`、`PBZ.Core.Tests/SuiteTests.cs`（+1）
- `release/install-loader.ps1`、新增 `docs/使用教程.md`、`README.md`
- Release 资产：`PBZTrainer-1.0.1-win-x64.zip`（不在仓库内）

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 95/95；发布版启动截图；zip 解压与加载器脚本拒绝路径测试。
- 未验证：在一台没装过任何东西的电脑上从零安装（本机已装加载器，脚本的实际复制路径只在 P1 时以 probe/p1-loader.ps1 验证过，新脚本逻辑相同）；SmartScreen / 杀毒软件实际表现。

## 状态
PASS（发布完成，见 HANDOFF 的 Release 链接）。

## 未完成与下一步
- 可选：仓库许可证（未替用户选）；提交邮箱是否改为 GitHub noreply（用户决定）。

## 给接手者的提醒（坑、约定、别碰的地方）
- 含中文的 .ps1 必须存为带 BOM 的 UTF-8；.bat 保持纯 ASCII。
- 单文件发布时 payload/ 仍在 exe 旁边（未打进单文件），修改器按 AppContext.BaseDirectory 找它。
- 发布包里不能带 data/（个人设置、歌曲路径、存档备份）。
