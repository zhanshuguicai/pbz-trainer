# 2.0.2：默认关闭"连段时按住弹反键"
- 日期：2026-11-18
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"默认关闭"（回答"试验性的连段按住要不要在默认设置里开着"）
- 接续：[2.0.1](2026-11-18-music-folder-defaults-claude.md)

## 目标
发布包默认设置里关闭 `parryComboHold`，并发布新包。

## 做了什么（大体流程，按顺序）
1. `PBZManager/defaults/lock.json` 的 `parryComboHold` 改为 false，测试加断言。
2. 修改器版本 2.0.2（PBZSuite 未改，仍为 2.0.0），更新 README 下载文件名、CHANGELOG、使用教程标题。
3. 打包 `PBZTrainer-2.0.2-win-x64.zip`，发布 Release v2.0.2；桌面副本程序文件更新到 2.0.2。

## 改动（文件或产物；提交号，如有）
- `defaults/lock.json`、`MusicLibraryTests.cs`、`PBZManager.csproj`、`MainWindow.xaml`、README、CHANGELOG、使用教程、HANDOFF；提交与标签 v2.0.2 见 git log

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build` 0 错误；`dotnet test` 123/123。
- 发布包：38 个文件校验和一致，音频文件 0 个，`defaults/lock.json` 中 parryComboHold 为 false，exe 版本 2.0.2。
- 未验证：干净电脑上的首次运行（本机有旧版 AppData 数据，会优先迁移）。

## 状态
PASS：默认值已改并发布。

## 未完成与下一步
- 用户本机 `data/lock.json` 的连段按住仍是开着的：默认设置只影响首次运行，这里没有替用户改。要关的话在"战斗辅助"页取消勾选。

## 给接手者的提醒（坑、约定、别碰的地方）
- 改 `defaults/` 只影响新用户，不影响已有 `data/` 的用户。
