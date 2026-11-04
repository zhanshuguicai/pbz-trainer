# T5a 实测通过；T5b 自动弹反/闪避调查与第 1 轮探针
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"没问题，继续最后一项"（确认可改物品范围与物品功能；最后一项 = T5b）
- 接续：[条件播放器扩充、T5a](2026-11-04-triggers-items-claude.md)

## 目标
记录 T5a 结果；开始 T5b：找到"敌人攻击即将命中"的信号、攻击种类、触发弹反/闪避的办法。

## 做了什么（大体流程，按顺序）
1. 查 T5a 记录：`data/events.log` 与 UE4SS.log 一致——Weapon_Material_08 4→10，ok；修改前存档备份到 `data/save-backups/20261104-034952`。用户确认范围（产品规则 19 由"待确认"转为确认）。
2. T5b 离线调查（Lua 类型表）：
   - 敌人出招调用 `UDeflectComponent:SendDeflectFrame(...)`；被攻击方的 `UDeflectComponent.ReceiveFrames`（TMap<int32, FDeflectReceiveFrame>）收到帧，含 `HitGameTime`（预计命中的游戏时间）、`IsDodge`、`DeflectStrength`/`DeflectDirection` 标签、`Bullet`、`SourceAction`。
   - 攻击类型枚举 `EDeflectBulletType`：CantDeflect、Normal、BlueEye、RedEye、PowerAttack、HoldDeflect。`GAA_DeflectFrame` 有 `Accept Red Eye` 字段。
   - 玩家侧：`GA_Hun_DeflectEscape`、各武器 `*_PerfectDodge`、`*_HoldDeflect`；`UDeflectComponent:GetNearestValidReceivedFrame(...)`。
   - 输入：Enhanced Input，`IEnhancedInputSubsystemInterface:InjectInputForAction(Action, RawValue, Modifiers, Triggers)` 可注入输入动作；玩家基础蓝图里能看到的输入动作只有 IA_Dash 等，弹反输入动作名未知。
3. 写第 1 轮探针（adhoc.lua，已同步，游戏运行中可直接用）：F8 找玩家 DeflectComponent、列出与弹反/闪避相关的 InputAction、开始每 16 ms 记录新出现的接收帧（离命中多久、IsDodge、标签、子弹类与类型）；F6/F7 手动注入一次闪避/弹反候选输入动作（探针里唯一的写入，不会自动触发）。

## 改动（文件或产物；提交号，如有）
- `probe/p2-stage/ue4ss/Mods/PBZProbe/adhoc.lua`（T5b 探针）；文档：产品规则 19 改为已确认、`docs/p4-tasks.md`、HANDOFF。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- T5a：物品修改实测 1 项成功并回读一致（见上）。未验证：钱的修改、读档后保持（用户说"没问题"，未单独记录）；物品中文名仍读不到（`ValueCache` 为空，界面显示 ID）。
- T5b：探针未运行（等用户实测）。Lua 无本机解释器检查。

## 状态
T5a PASS（单项实测）；T5b PARTIAL：调查完成，探针待实测。

## 未完成与下一步
- 用户实测探针 → 读 `T5B` 日志：帧提前多久出现、红/蓝/普通怎么区分、哪个 InputAction 是弹反/闪避、注入是否生效。
- 然后做 PBZSuite 自动弹反/闪避模块（F7 开关、修改器"战斗辅助"页），记录成功/误触/漏触。

## 给接手者的提醒（坑、约定、别碰的地方）
- 探针的 16 ms 循环注册后在本次游戏进程里一直存在；F8 只切换记录开关。
- 注入输入只在按 F6/F7 时发生一次。
