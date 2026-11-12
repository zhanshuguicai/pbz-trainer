# 玩家工具（1.0.3）：速度、伤害、无限药、飞行、传送、等级经验；提交 1.0.3
- 日期：2026-11-12
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"按顺序做完提交最后一版吧"（接 [场景修复与调研](2026-11-12-scene-repair-review-claude.md) 列出的 7 项候选功能）

## 目标
按顺序实现调研得到的候选功能，然后提交 1.0.3。

## 做了什么（大体流程，按顺序）
1. 查已有证据（evidence/p2 的属性导出、docs/p2-field-sources.md）：玩家 `BasicAttributeSet` 有 Attack、ShachiAttack、JogSpeed、SprintSpeed、WalkSpeed、CrouchSpeed、TakenReductionMultiplier_*、XP、Level；药为 `MedicineInfos[id].CurrentTime`，上限 `MaxTime + MedicineExtraUsage`。证据里没有任何 QTE 相关内容。
2. PBZSuite 新增"玩家工具"段（main.lua `playerTick`，每 200 ms）：
   - 持续设置 `config.cheats`：游戏速度（SetGlobalTimeDilation；游戏自己的慢动作结束、回到 1 时重新设上）、移动速度 / 造成伤害 / 受到伤害（按倍率改属性 BaseValue，CurrentValue 按比例；游戏自己改了基础值则以新值为准；换角色时清空记录）、无限药、飞行（MovementMode=Flying + 关碰撞；关时转 Falling 自然落地）。
   - 一次性 `config.playerOps`：teleport（K2_TeleportTo，回报出发点供撤销）、nudge（上/下/前）、xp、level。
   - 遥测新增 `player`：地图、位置、XP、等级、实际游戏速度、飞行、操作结果。
3. 修改器新页"玩家工具"（MainWindow.Player.cs）：倍率框、无限药、飞行、上升/下降/向前、记录点列表（data\teleports.json，可改名、删除、撤销上次传送、跨地图拦截）、等级与经验；5 个新热键（Ctrl+F9/F10/F11、Ctrl+↑/↓）。设置不保存：启动时与关闭时都推送正常值（与数值锁定同规则）。
4. 两项按调研原样未做：
   - "敌人血量倍率"：改敌人属性需要遍历敌人（循环里不能 FindAllOf），改用"造成伤害倍率"覆盖同一需求（填 100 接近一击必杀）。
   - **自动 QTE：未实现**。没有任何 QTE 机制的证据，需要先在游戏里遇到 QTE 时探查（哪个控件 / 能力 / 输入），无法凭空写。
5. 测试 6 个（配置字段名与 Lua 读取一致、范围限制、操作序号与上限、记录点存取、遥测解析、热键默认值不冲突），共 117 个通过。截图检查新页布局；顺带发现并修复一个启动崩溃（分部类静态字段初始化顺序，WaypointFile 改为属性）。
6. 更新 CHANGELOG（1.0.3）、README 功能表、使用教程（3.3a、3.3b、热键表）。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`；新增 `PBZ.Core/Player/PlayerTools.cs`、`PBZManager/MainWindow.Player.cs`、`PBZ.Core.Tests/PlayerToolsTests.cs`
- `SuiteConfig.cs`、`Snapshot.cs`、`LockSettings.cs`、`MainWindow.xaml(.cs)`、`CHANGELOG.md`、`README.md`、`docs/使用教程.md`
- 与 2026-11-07 的 1.0.2 改动、2026-11-12 场景修复一起提交为 1.0.3（提交号见 git log）

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build` 0 错误；`dotnet test` 117/117；`--snapshot` 看了玩家工具页。
- **全部游戏内效果未验证**（本次游戏未运行）。风险最高的几项：
  - 属性倍率是否会被游戏每帧重算覆盖（尤其移动速度可能另有 FinalWalkSpeed / CharacterMovement 决定）；
  - TakenReductionMultiplier 是否就是"受到伤害乘数"（命名推断）；
  - 直接写 `MedicineInfos` 里结构体字段是否生效、界面是否刷新；
  - 改 XP / Level 是否持久、是否影响加点。
- 桌面副本仍未更新（之前复制被自动权限检查拦下）。

## 状态
PARTIAL：代码完成、测试通过、已提交；待游戏内实测，自动 QTE 待探查。

## 未完成与下一步
1. 用户：覆盖桌面副本 → 安装 PBZSuite 1.0.3 → 逐项试玩家工具和场景修复，看 `ue4ss\UE4SS.log` 的 `[PBZSuite] player op` / `scale ... error` 行。
2. 自动 QTE：遇到 QTE 时用探针抓当时的控件 / 能力 / 标签，再设计。
3. 实测通过后推送并出 1.0.3 Release（需用户同意）。

## 给接手者的提醒（坑、约定、别碰的地方）
- WPF 分部类之间的静态字段初始化顺序不确定；跨文件用到 `DataDir` 的静态值要写成属性。
- 玩家工具的所有写入都受 `compatible` 门控；不兼容时 Lua 把一切当作正常值。
- 游戏速度只在游戏把时间膨胀恢复为 1 时重设，不会打断游戏自己的慢动作。
