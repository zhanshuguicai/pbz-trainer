# 修改器数据位置修复 + 数值修改页显示实时数值
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户截图（PBZSuite 显示"存在来源不明的 PBZSuite 文件夹，请人工检查"）；随后"把所有数值的'实时状态'这一栏同步到数值修改界面；该如何避免这个问题，给你权限吗；编译"
- 接续：[T8 数值修改](2026-11-04-t8-value-lock-claude.md)

## 目标
1. 查清用户从资源管理器打开的修改器为什么认不出自己装的 PBZSuite，并修好。
2. 数值修改页显示四项的实时"当前 / 上限"。

## 做了什么（大体流程，按顺序）
1. 我这边启动的修改器显示"有新版本可更新"，用户那边显示"来源不明"。用 UI Automation 读了用户窗口的文字，确认它读不到安装清单（数值修改页写着"PBZSuite 未安装"）。
2. 找到原因：Claude 桌面应用是 MSIX 打包应用，由它启动的进程（包括我跑的 PowerShell、bash、`--snapshot` 启动的修改器）写进 `%LOCALAPPDATA%` 的新文件会被重定向到 `AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\PBZManager`。以前由 Claude 这边启动的修改器建的安装清单、设置都在那里，用户从资源管理器打开的修改器看不到。
3. 修复：新增 `PBZ.Core/DataLocation.cs`，修改器数据改放在 exe 旁的 `data\`。首次启动若 `data\` 为空，从旧位置逐个文件迁移（先当前进程看到的 `%LOCALAPPDATA%\PBZManager`，再各打包应用的 LocalCache 副本，每个文件取第一个有的），状态栏和 events.log 记录来源。
4. 实时数值：PBZSuite 遥测加 `values`（shachi / ultimate / spirit 的 current、max、valid）；`Snapshot.Values` 解析（旧行没有该字段时为无效）；数值修改页加"当前 / 上限"列，约 0.2 s 刷新，游戏未连接时显示"—"。

## 改动（文件或产物；提交号，如有）
- 新增 `src/PBZManager/PBZ.Core/DataLocation.cs`、`PBZ.Core.Tests/DataLocationTests.cs`
- 修改 `PBZ.Core/Telemetry/Snapshot.cs`（ValuesSample）、`PBZManager/MainWindow.xaml(.cs)`（数据目录、实时数值列）、`PBZ.Core.Tests/LockTests.cs`（+1）、`src/PBZSuite/Scripts/main.lua`（遥测 values）
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build`：0 警告 0 错误；`dotnet test`：66/66（新增：迁移逐文件取首个来源且只迁一次；遥测 values 解析与旧行兼容）。
- 未运行修改器、未截图：故意不在 Claude 这边先启动，避免用容器里的旧设置抢先完成迁移。界面待用户从资源管理器打开后，用 UI Automation 读文字核对。
- 未验证：用户真实 `%LOCALAPPDATA%\PBZManager` 里有没有用户自己的旧设置（Claude 这边看到的是合并视图，分不清）。
- 未验证：PBZSuite 0.2.0 游戏内实测（同前）。

## 状态
PARTIAL：代码完成、构建测试通过；迁移和界面待用户首次启动后核对。

## 未完成与下一步
- 用户从资源管理器打开修改器 → 检查状态栏迁移提示和 PBZSuite 状态 → "安装 / 更新" → 进游戏按 T8 步骤实测，并看"当前 / 上限"是否刷新。
- 之后 T5a（F8 物品普查已在探针里）。

## 给接手者的提醒（坑、约定、别碰的地方）
- **由 Claude 桌面应用启动的任何进程，写进 AppData 的新文件用户看不到**。修改器自身数据已改到 exe 旁 `data\`；以后别再把需要和用户共享的东西放进 AppData。项目目录和游戏目录的写入不受影响（游戏读到了这边同步的探针脚本）。
- 用户不用给额外权限；这是打包应用的文件重定向，不是权限问题。
- 遥测 schemaVersion 仍为 1，`values` 是可选新字段。
