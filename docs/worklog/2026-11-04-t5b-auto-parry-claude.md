# T5b 自动弹反 / 闪避：探针结果与正式模块
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"没问题，继续最后一项"；探针实测后"测完了"
- 接续：[T5a 完成、T5b 调查与探针](2026-11-04-t5a-done-t5b-probe-claude.md)

## 目标
根据探针数据做出自动弹反/闪避：游戏内模块、F7 开关、修改器"战斗辅助"页。

## 做了什么（大体流程，按顺序）
1. 读第 1 轮探针日志（存 `evidence/t5b/round1-UE4SS.log`）：
   - 玩家组件属性名 `Deflect Component`；319 个接收帧。帧出现到命中的提前量：中位 0.283 s，p10 0.092 s，p90 0.299 s，最大 0.498 s。
   - `IsDodge=true` 30 个（Knockback 18、Normal 12），提前约 0.48 s，全部来自 Boss（MoTian_LongZhu）技能 GEx_02/GEx_04/Skill_05/Skill_14。其余 IsDodge=false：Normal 199、Knockback 62、Light 18、Strong 6（另 4 个创建时已 IsHit）。
   - 远程波 `BP_MoTian_Wave_C` 的 HitGameTime 为 -3.4e38（无确定命中时间）。
   - 输入动作：`/PBZDeflectSystem/Input/IA_Deflect`、`IA_Dodge`、`IA_PerfectDodge`、`IA_DodgeHold`、`IA_DeflectDebug`；探针 F6 误选了 IA_Dash。F6/F7 注入只记录到"called"，角色是否响应日志里看不出。
2. PBZSuite 0.4.0：独立 16 ms 循环；每帧读 `ReceiveFrames`，命中前 `leadMs`（默认 120）按一次——IsDodge 按 IA_Dodge，否则 IA_Deflect（`EnhancedInputLocalPlayerSubsystem:InjectInputForAction`）；无确定时间的帧出现即按；两次按键至少 80 ms；每个帧只处理一次；按键后到"命中 + 0.25 s"检查血量，下降记为受击。遥测 `modules.parry`（是否生效）与 `parry`（弹反/闪避次数、受击、未受击）。需 compatible 与 `modules.parry`。
3. 修改器："战斗辅助"页（总开关 + F7 热键按钮、弹反/闪避分开勾选、提前量 0–300 ms、本局统计、不可用原因）；配置 `parry` 段；设置存 lock.json（ParryDeflect/ParryDodge/ParryLeadMs，热键动作 "parry" 默认 F7）；每次启动关闭、关闭修改器时关闭。
4. 测试：新增 `ParryTests.cs`（3 个）。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`（0.4.0）
- PBZ.Core：`Suite/SuiteConfig.cs`（SuiteParry）、`Telemetry/Snapshot.cs`（ParrySample）、`Lock/LockSettings.cs`（Parry 动作与选项、Items 列表）
- PBZManager：`MainWindow.xaml(.cs)`（战斗辅助页、热键、PushParry）
- `PBZ.Core.Tests/ParryTests.cs`；文档：产品规则 21、`docs/p4-tasks.md`、HANDOFF。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test`：80/80。代码编译 0 错误；最后复制 exe 失败（用户的修改器开着），待关闭后重编。
- 未验证：注入 IA_Deflect/IA_Dodge 是否真的触发弹反/闪避；提前量是否合适；对 Boss 的红光（IsDodge）攻击是否躲得开；关闭后无残留；界面截图。

## 状态
PARTIAL：代码与测试完成，待重编、安装 0.4.0、实战。

## 未完成与下一步
- 实测并按统计调提前量；若注入无效，改试 `IA_PerfectDodge`、或找按键→能力的路径（GA_Hun_DeflectEscape 等）。
- 然后 T6 联调打包。

## 给接手者的提醒（坑、约定、别碰的地方）
- 帧窗口只有约 0.3 s，所以 parry 用独立 16 ms 循环，别并进 200 ms 采样。
- 探针（PBZProbe F8）的 16 ms 循环和 F6/F7 绑定只在本次游戏进程按过 F8 才存在；F7 现在也是修改器的系统热键（会被修改器拦截）。
- `IsDodge` 帧目前只在 Boss 技能里见过；普通敌人的红光攻击未覆盖到。
