# 条件播放器：播放中的歌只被更高优先级打断
- 日期：2026-11-07
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"关于播放器设计成同一优先级有在播放的话就不会被相同或更低的挤掉"

## 目标
正在播放的歌不被同级或更低优先级的规则替换。

## 做了什么（大体流程，按顺序）
1. `ConditionPlayer.Handle`：打断条件从 `playing.Priority > winner.Priority` 改为 `>=`（同级、包括同一条规则再次触发，都不打断）。
2. 规则页说明改为"只会被优先级更高的规则打断，同级或更低的不会挤掉它"。
3. 测试：`EveryTimeScope_RepeatsWithinABattle` 原来在同一首歌还在放时再触发，改为先放完再触发；新增 `PlayingSong_OnlyInterruptedByHigherPriority`（同规则再触发、同级、更低都不替换，更高的打断）。
4. 产品规则 24。

## 改动（文件或产物；提交号，如有）
- `PBZ.Core/Music/ConditionPlayer.cs`、`PBZManager/MainWindow.xaml`、`PBZ.Core.Tests/ConditionPlayerTests.cs`、`docs/decisions/2026-11-03-product-rules.md`。未提交（与 1.0.2 弹反改动一起，等用户实测后提交）。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 103/103（含 T4a 回放，默认规则结果不变）；修改器重编 0 错误。
- 未验证：实际听感（需实战）。

## 状态
PASS（代码与测试）。

## 未完成与下一步
- 随 1.0.2 一起实测、提交、发布。

## 给接手者的提醒（坑、约定、别碰的地方）
- 歌放完（或淡出）后，同级规则可以再次播放；"每场一次"限制仍按原规则生效。
