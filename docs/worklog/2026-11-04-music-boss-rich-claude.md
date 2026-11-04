# 条件播放器 1.0 扩充：Boss 事件与条件、每条规则的播放设置、音乐热键、规则导入导出；T6 开头
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"基本没问题，虽然达不到百分百但也差不多了，继续吧"（T5b 通过）；"先等等，音乐播放器的条件部分再加上对boss单位的条件，例如boss半血了播放"；"我希望你提交1.0前尽量多的丰富扩充音乐播放的功能"
- 接续：[T5b 改为真实按键](2026-11-04-t5b-real-input-claude.md)

## 目标
记录 T5b 结果；开始 T6；按用户要求在 1.0 前扩充条件播放器（Boss 相关为首）。

## 做了什么（大体流程，按顺序）
1. T5b 实战（`evidence/t5b/round6-realinput-UE4SS.log`、遥测）：本程序按键 119 次（弹反 110、闪避 9），判定后未受击 102、受击 17（约 86%）。用户："基本没问题，虽然达不到百分百但也差不多了"。
2. T6 开头：读规划第 11 节；安装器改为两阶段（先复制为 *.pbznew 并校验，再替换；失败删临时文件，原安装不变）；修 bug：卸载时把 inventory.json / inventory.json.tmp / parry-cmd.txt 当未知文件而拒绝；新增 3 个安装器测试（同版本重装、升级保留运行输出、中途失败恢复）。版本统一 1.0.0（修改器、PBZSuite）。新增 `release/install-loader.ps1`（加载器安装/回滚，校验两个已验证游戏版本，清单放包内）。暂停打包，转做音乐功能。
3. Boss 调查（类型表）：`UPBZHUDLayout.RegisterBossHandles`（TMap<弱引用 Actor, 句柄>）= 当前显示血条的 Boss；`GetBossHPBarInstances()` → `WB_BossHPBar_C`（`Character`、`BossName`）；`AProjectPBZCharacter.bIsBoss`；Boss 血量同样在 `AttributeSetBase.Health/Health_Max`。
4. PBZSuite：遥测加 `boss`（active、count、id、name、current、max、valid）；HUD 对象缓存，失效后最多每 2 s FindFirstOf 一次；每遇新 Boss 记一行 `boss bar:` 日志。
5. 核心：BossSample；引擎事件 BossFightStarted / BossFightEnded（defeated|gone）/ BossHealthBelow（阈值）/ BossDefeated（每场一次；血条消失前血量 ≤2% 也算击败）；HUD 读不到时不结束。触发器 +4（共 22 种）；附加条件 + Boss 战状态（不限/仅 Boss 战中/仅非 Boss 战）、Boss 血量低于 X%。
6. 每条规则的播放设置 RulePlayback：多首歌（随机且不连着重复 / 轮流）、音量 0–200%、开始秒数（循环也从此处重来）、播放方式（自动/循环/一遍）、淡入秒、淡出秒（空=总设置）、冷却秒（按游戏时间）。IAudioOutput.Play 改为 PlayRequest。
7. 界面：规则表加"播放设置"列与对话框；选歌可多选；条件对话框加 Boss 两项；播放器页加热键（开关 Ctrl+F8、停止当前歌曲 Ctrl+F6，可改）、导出/导入规则；状态页显示当前 Boss 与血量；窗口加宽到 1400。
8. 测试：新增 `MusicRichTests.cs`（8 个），Feed 可带 Boss。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`（1.0.0：boss 遥测）
- PBZ.Core：`Telemetry/Snapshot.cs`、`Events/EventEngine.cs`、`Music/ConditionPlayer.cs`、`Suite/SuiteInstaller.cs`、`Lock/LockSettings.cs`
- PBZManager：`WpfAudioOutput.cs`、`RuleRow.cs`、`ConditionsDialog.cs`、新增 `PlaybackDialog.cs`、`MainWindow.xaml(.cs)`、csproj 1.0.0
- 测试：`MusicRichTests.cs`、`SuiteTests.cs`（+3）、fake 适配
- 新增 `release/install-loader.ps1`
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 94/94。代码编译 0 错误；复制 exe 被用户开着的修改器锁住，待关闭后重编。
- 未验证：Boss 遥测在游戏里能否读到（HUD 对象、血条 TMap、名字）；播放设置的实际听感（开始秒数、音量、淡入淡出、循环从开始秒数重来）；导入导出界面。

## 状态
PARTIAL：代码与测试完成；待重编、安装 PBZSuite 1.0.0、打一场 Boss 实测。T6 打包暂停中。

## 未完成与下一步
- 实测 Boss：看 UE4SS.log `boss bar:` 行、状态页 Boss 显示、"Boss 血量跌破 50%"规则。
- 然后继续 T6：发布包（README、CHANGELOG、许可证、哈希、回滚说明）、干净安装验证。

## 给接手者的提醒（坑、约定、别碰的地方）
- Boss 只认 HUD 血条：没有血条的强敌不算 Boss；多个 Boss 同时出现时只跟踪第一个。
- 冷却用事件里的游戏时间（MonotonicMs），读档后游戏时间可能重置。
- 用户问过 Win+G 录屏录不到音乐：Game Bar 默认只录游戏声音，需在 设置 → 游戏 → 捕获 → "录制的音频" 选"全部"（修改器是单独进程）。
