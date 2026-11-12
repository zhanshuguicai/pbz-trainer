# 自动弹反 1.0.2：精确排程、连段、按攻击类型应对、诊断日志
- 日期：2026-11-07
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"感觉这个自动弹反闪避插件经常性的无法成功生效，该如何优化"；补充"我刚刚甚至出现了站着不动时也不触发的问题；还有，黑光类攻击该如何应对"
- 接续：[Release 1.0.1](2026-11-04-release-package-claude.md)

## 目标
提高自动弹反成功率，查清"站着不动也不触发"，给黑光等攻击类型提供应对方式。

## 做了什么（大体流程，按顺序）
1. 读 2026-11-07 的实战数据（`evidence/t5b/round7-field-UE4SS.log`，未提交）：按键 310 次（弹反 298、闪避 12），未受击 237、受击 73（76%）。受击里：
   - 大量连号（497–502、288–301 等）= 连段，原因是 1.0.1 两次按键至少隔 80 ms，后面的按键被推迟；
   - 很多只掉 0–1 点血（格挡擦伤或持续伤害），被误算成失败；
   - 时机误差：按键在 16 ms 循环里到点才写，修改器再轮询；慢动作时按游戏时间算提前量会偏早。
2. PBZSuite 1.0.2：攻击帧**一出现就排程**，命令改为 `PBZPARRY 会话 序号 deflect|hold|dodge 按键毫秒 命中毫秒 end`（真实时间，用 GetGlobalTimeDilation 换算慢动作）；文件保留最近 12 条，同时来的几刀不会互相覆盖；去掉 80 ms 间隔。受击判定改为掉血 ≥ 最大血量 1%（且 ≥5）。每次按键记一行 `parry #序号 种类 h=句柄 类型/强度 seen=…ms press=…ms before hit: ok|HIT (-掉血)`。
3. 攻击类型：从 `ReceiveFrames[].SourceAction:Get().DeflectBulletType` 读取（0 CantDeflect、1 Normal、2 BlueEye、3 RedEye、4 PowerAttack、5 HoldDeflect）。每类可设 弹反（点按）/ 弹反（按住到命中）/ 闪避 / 不处理；默认 普通、蓝光、重击 → 点按，红光、不可弹反 → 闪避，长按弹反 → 按住。IsDodge 帧总是闪避（除非设为不处理）。设为不处理的帧也记一行日志。
4. 修改器：`PBZ.Core/Combat/ParryCommand.cs` 新格式解析（兼容 1.0.1 旧行）+ `ParryScheduler`（纯逻辑：按时按下/松开、点按 40 ms、按键未松开时先松开 15 ms 再按、按住到命中后 120 ms、闪避永远点按、游戏不在前台时跳过并松开所有键）；`ParryRelay` 每 1 ms 推进调度器、每 2 ms 读文件，关闭时松开按键；收到/按下/跳过逐条写入 `data/parry-relay.log`（与游戏日志按序号对照）。战斗辅助页加"连段时按住弹反键"（试验，默认关）和攻击类型应对表；配置 `parry.actions` 下发游戏。
5. 版本 1.0.2（修改器、PBZSuite）。新增 `ParrySchedulerTests.cs`（7 个）。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`
- PBZ.Core：`Combat/ParryCommand.cs`（重写，含 ParryScheduler）、`Suite/SuiteConfig.cs`（SuiteParry.Actions、SameAs、AttackTypes）、`Lock/LockSettings.cs`（ParryComboHold、ParryActions）
- PBZManager：`ParryRelay.cs`、`MainWindow.xaml(.cs)`、csproj 1.0.2
- `PBZ.Core.Tests/ParrySchedulerTests.cs`
- 未提交（用户未要求提交）。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 102/102；预览构建截图（战斗辅助页新选项与类型表正常）。
- 未验证：游戏内实战（需关游戏和修改器、重编、安装 1.0.2）；"站着不动不触发"的原因（新日志就是为定位它）；黑光对应哪种类型（需用户遇到黑光时记下时间，对照 UE4SS.log 的 type）；`SourceAction.DeflectBulletType` 在所有攻击上都能读到（读不到时 type=? 按 IsDodge 决定）。

## 状态
PARTIAL：代码与测试完成，待安装实测。

## 未完成与下一步
- 用户实测：对照 `ue4ss\UE4SS.log` 的 `parry #` 行与 `data\parry-relay.log` 的同序号行；统计各类型成功率，调提前量与类型默认值；确认黑光类型。
- 若某类型的命中窗口与其他不同，可做"按类型提前量"。

## 给接手者的提醒（坑、约定、别碰的地方）
- 命令行里的时间是写入那一刻起算的真实毫秒；读取延迟约 1–2 ms，未补偿。
- 用 Python 改含中文/反斜杠的文件时，补丁脚本先写成文件、字符串用 r''' '''，否则会整体失败（本次两次踩坑，均未写入半成品）。
