# 2.0.0 发布包、GitHub Release、桌面副本更新
- 日期：2026-11-12
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"都要做，做好"（回答"出 Release / 先实测"）
- 接续：[2.0.0 版本号与推送](2026-11-12-release-200-claude.md)

## 目标
出 2.0.0 Release 附件；更新用户实际使用的桌面副本；游戏内实测。

## 做了什么（大体流程，按顺序）
1. 桌面副本 `C:\Users\38365\Desktop\net10.0-windows`：复制 bin\Debug\net10.0-windows 下除 data 外的全部文件（data 保留）。产品版本 2.0.0，payload 里 SUITE_VERSION 2.0.0。
2. 发布版：`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none`。
3. 发布包沿用 1.0.1 包的结构（从 v1.0.1 Release 下载其 zip 取出 loader、两个 .bat、LICENSE-UE4SS.txt、THIRD-PARTY.txt），替换 PBZManager.exe、payload、README、CHANGELOG、使用教程.md、install-loader.ps1，重算 SHA256SUMS.txt。
   - 本机 Python 的 zipfile 把中文文件名写乱（文件系统编码问题），改用 .NET `ZipFile.CreateFromDirectory(..., Encoding.UTF8)` 打包；解压后中文名正常，35 个文件校验和全部一致。
4. 冒烟测试：从解压目录启动修改器截图，标题 v2.0.0，各页存在。此时窗口上有一次"安装 / 更新"点击（不是我的操作），因截图模式正在退出而在 `Brush()` 处空引用崩溃；核对游戏目录，PBZSuite 仍是 1.0.2，未被安装。
5. `gh release create v2.0.0`：上传 `PBZTrainer-2.0.0-win-x64.zip`（67,778,184 字节），说明含新功能、从 1.0.1 升级步骤、兼容版本与反馈方式。
6. 游戏内实测：申请电脑控制（修改器、游戏、Steam）被用户拒绝，未进行；改为给用户测试清单。启动了桌面副本修改器供用户操作。

## 改动（文件或产物；提交号，如有）
- GitHub Release v2.0.0 及附件；桌面副本程序文件。仓库内只改本日志与 HANDOFF。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- 发布包校验和 35/35；解压中文名正常；发布版启动截图正常；Release 资产状态 uploaded。
- 未验证：全部游戏内功能（用户拒绝电脑控制，需用户实测）。

## 状态
PARTIAL：发布完成；游戏内实测待用户。

## 未完成与下一步
- 用户：桌面副本修改器"状态"页点"安装 / 更新"（如提示文件被改过，确认覆盖）→ 开游戏 → 按清单测：场景修复（戏梦山庄画）、玩家工具各项、弹反。反馈不生效的项和 UE4SS.log 的 `[PBZSuite]` 行。

## 给接手者的提醒（坑、约定、别碰的地方）
- 本机 Python 写 zip / 文件时中文路径不可靠（同日 HANDOFF 写入也出过一次 Errno 22），打包用 .NET ZipFile。
- 修改器在关闭过程中被点按钮会在 `Brush()` 空引用崩溃（Application.Current 已为空）；只在退出瞬间发生，未修。
