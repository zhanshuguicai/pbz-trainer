# T4d 事件音乐
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户选"脱战后淡出停止"；测试后用户反馈"非常好"

## 目标
管理器按事件播放音乐：每场首次半血放 aizo，药喝空放雨爱 DJ（优先），战斗结束淡出；实战验证。

## 做了什么（大体流程，按顺序）
1. 产品规则补 8（脱战淡出）、9（脱战确认 5 s，Claude 决定）、10（脱战时喝空也播，Claude 决定）。
2. PBZ.Core/Music：MusicDirector（雨爱优先、半血不打断雨爱、任何 BattleEnded 淡出）+ 4 个测试；MusicSettings（settings.json，原子写）。
3. PBZManager：WpfAudioOutput（MediaPlayer，0.3 s 淡入、可配淡出、缺文件报错不崩）；"事件音乐"面板（开关、选择、试听、音量、停止）；游戏关闭时淡出；events.log 日志。
4. 写用户本机 settings.json：两首 E:\Downloads 下的 mp3、音量 0.6、开关打开。
5. 用户试听与实战。

## 改动（文件或产物；提交号，如有）
- 新增 PBZ.Core/Music/*、PBZ.Core.Tests/MusicDirectorTests.cs、PBZManager/WpfAudioOutput.cs；修改 MainWindow、App（--snapshot）。
- %LOCALAPPDATA%\PBZManager\settings.json、events.log。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- 构建 0 警告 0 错误；测试 43/43。
- 实战（events.log）：13:08 试听两首并停止；第 2 场 13:10:04 HalfHealth → aizo，13:10:24 FlasksEmpty → 切雨爱 DJ，13:10:54 脱战 → 淡出。用户主观确认"非常好"。
- 遥测文件超过 8 MB 期间管理器持续读到 seq 18057，未见中断（是否已发生轮换未单独确认）。
- 未验证：死亡触发的淡出（未死亡）；游戏关闭时淡出。

## 状态
PASS：T4d 完成标准满足。

## 未完成与下一步
- 用户提出改为"高度集成的修改器"：锁定血量/杀气/能量、自定义条件播放器、强化物品数量、自动弹反闪避。需更新架构与任务（ADR 0002）。

## 给接手者的提醒（坑、约定、别碰的地方）
- 音乐只在管理器运行时有；音频只存路径。
- 截图检查界面用 `PBZManager.exe --snapshot <png> 10`，延迟太短会截到未刷新的界面。
