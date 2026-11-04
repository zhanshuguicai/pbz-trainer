# T5b：注入输入无效的排查，改为修改器按真实按键
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"不对啊，你的输入似乎并没有用，没有动作"；之后三轮探针反馈"没有动作"；补充"能否分为手柄输入和键鼠输入的版本都有"
- 接续：[游戏更新适配、歌曲循环](2026-11-04-game-update-loop-claude.md)

## 目标
查清自动弹反为什么没有动作，并换成能用的触发方式。

## 做了什么（大体流程，按顺序）
1. 0.4.0 实战日志（`evidence/t5b/round2-UE4SS.log`）：模块在跑，87 次弹反、5 次闪避"按键"无报错，但角色无动作，受击 17。
2. 探针 T5C（round3）：只有一个 EnhancedInputLocalPlayerSubsystem（属于 PBZLocalPlayer），不是拿错对象。玩家能力里有各武器的 HoldDeflect、PerfectDodge、GA_Hun_DeflectEscape。
3. 探针 T5D（round4）：列出 53 个已绑定的输入动作——右键 = IA_Deflect + IA_Stance，左 Shift = IA_Dodge / IA_DodgeHold / IA_PerfectDodge / IA_Sprint；IA_Dash 未绑定（上一轮的冲刺测试无效）。用户亲手按键时：右键启动 `GA_Hun_Long_Stance_C`，左 Shift 启动 `GA_Hun_AdvancedDash_*`。
4. 探针 T5E（round5）：注入 IA_Stance 1 帧 / 6 帧、IA_Dodge 2 帧、IA_Deflect+IA_Stance 6 帧——**全部无任何能力启动**；ASC.TryActivateAbility(Stance) 返回 true 且能力生效，但用户看不到动作；TryActivateAbility(AdvancedDash) 返回 false。结论：这个游戏不接受注入的 Enhanced Input，直接启动能力也不能代替按键。
5. 发现 UE4SS 坑：`LoopInGameThreadWithDelay` 回调返回 true 不会停止，探针循环在该游戏进程里一直跑（日志刷 "done"）。探针 adhoc 改回只读、不再起循环；需要重启游戏清掉。
6. 改方案：PBZSuite 0.4.1 判断时机后写 `runtime/parry-cmd.txt`（"PBZPARRY 会话 序号 deflect|dodge end"）；修改器新 `ParryRelay` 后台线程每 2 ms 读取，新序号就用 SendInput 按真实按键（默认 弹反=鼠标右键，闪避=左 Shift 短按 40 ms，键盘用扫描码），**只在 PBZ.exe 窗口位于前台时按**；开启前已有的命令只记为基线不按。战斗辅助页加"弹反键 / 闪避键"选择和本程序按键次数统计。
7. 手柄：游戏键鼠与手柄绑定同时生效，键鼠版对手柄玩家同样有效；真正的虚拟手柄需要用户自行安装 ViGEmBus 驱动，暂不做，等键鼠版实测后再定。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`（0.4.1：命令文件代替注入）
- 新增 `PBZ.Core/Combat/ParryCommand.cs`（命令解析、PressKey 表）、`PBZManager/ParryRelay.cs`
- `PBZ.Core/Lock/LockSettings.cs`（ParryDeflectKey / ParryDodgeKey）、`PBZManager/MainWindow.xaml(.cs)`
- `PBZ.Core.Tests/ParryTests.cs`（+3）；`probe/.../adhoc.lua`（只读）
- 证据：`evidence/t5b/round2..5-UE4SS.log`
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 83/83；代码编译 0 错误，最后复制 exe 被用户开着的修改器锁住，待关闭后重编。
- 未验证：SendInput 真实按键在游戏里是否触发弹反/闪避与时机；手柄玩家实际体验。

## 状态
PARTIAL：方案改完，待重启游戏、重编、安装 0.4.1、实战。

## 未完成与下一步
- 用户关游戏和修改器 → 重编 → 装 0.4.1 → 实战看统计（受击率）、调提前量。
- 若用户要真正的手柄版：调研 ViGEmBus 方案（需用户自行安装驱动）。
- 然后 T6。

## 给接手者的提醒（坑、约定、别碰的地方）
- **注入 Enhanced Input、直接 TryActivateAbility 在本游戏都不能代替按键**，不要再走这条路。
- **UE4SS 的 LoopInGameThreadWithDelay 返回 true 不会停**；需要停止的循环要自己用标志位空转。
- 真实按键只在游戏前台时发出；按键时用户自己同时按别的键不受影响，但用户自己按右键/Shift 会和自动按键叠加。
