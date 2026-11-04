# 条件播放器扩充（18 种事件、淡出列、附加条件）+ T5a 物品与钱
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"关于歌曲部分，你尽量多的添加能检测到的'事件触发器'，并且还要给渐弱淡出的触发事件留一列，你怎么看；功能看起来没啥问题，继续下一步开发，钱你找的应该是对的"；补充"还有要留出多条件判定的空间，比如现在不在战斗把血喝完也能触发雨爱，加一个'在战斗中'就可以避免这种"

## 目标
1. 条件播放器：尽可能多的可检测事件；每条规则可选淡出事件；规则可加多个附加条件。
2. T5a：钱与强化/养成材料的数量修改。

## 做了什么（大体流程，按顺序）
1. 读本次实测日志：PBZSuite 0.2.0 已加载，锁定按配置生效；F8 普查 143 项、钱 ConsciousnessDust=15969（= GetCurrentDust，Coin 恒 0）。存 `evidence/t5a/survey-UE4SS.log`、`items.txt`。物品名称这次没读到（`ItemName.Key` 为空），正式模块改读 `ItemName.ValueCache`。
2. EventEngine 新增事件：HealthAbove、BigHit、FlaskUsed、FlasksRefilled、EnemiesAtLeast、BattleLong、ShachiFull、UltimateReady、UltimateUsed、SpiritFull、SpiritEmpty；阈值改为按事件类型的 `Watch` 表（保留 HealthThresholds 兼容）。
3. ConditionPlayer：`RuleTriggers` 统一管理 18 种触发（名称、参数类型 %/个/秒、范围、说明）；PlayRule 加 `StopTrigger`/`StopThreshold`（null = 放完）与 `Conditions`（RuleConditions：战斗状态、当前血量低于/高于、敌人至少、药至多）；Handle 带当前状态 RuleState 判断条件，只过滤开始、不过滤淡出。
4. 迁移：T7 的 `fadeOnBattleEnd` true → 淡出条件"战斗结束（任何原因）"，false → 不淡出；id 为 flasks0 的旧规则加"仅战斗中"。发现并修了一个坑：StopTrigger 为 null 时会被 JSON 省略、重读变回默认，已强制写出。
5. 界面：规则表列改为 触发事件 / 参数(单位) / 附加条件(编辑…对话框) / 歌曲 / 优先级 / 次数 / 淡出条件 / 淡出参数；窗口加宽到 1240；状态页事件列表加新事件文字。
6. T5a 游戏侧（PBZSuite 0.3.0）：配置 `inventoryRequest`（变化时写 runtime/inventory.json）和 `itemOps`（seq 递增，各执行一次；游戏启动时已在文件里的不重放）；物品用 `AddPropsToBackPack`/`RemovePropsFromBackPack`，钱用 `pawn:ChangeDustNum(isAdd, n)`，执行后回读并写结果；只允许 MaterialType 1–7 与钱；需 compatible=true。
7. T5a 修改器侧：`PBZ.Core/Items/Inventory.cs`（读 inventory.json、分类名、ItemEdits 追加编号）；"物品"页（从游戏读取、只显示可修改、钱改为、改为列、应用修改、选中填 999、清空），每次运行第一次修改前复制存档到 `data\save-backups\<时间>`，结果写状态栏和 events.log。

## 改动（文件或产物；提交号，如有）
- PBZ.Core：`Events/EventEngine.cs`、`Music/ConditionPlayer.cs`（重写）、`Music/MusicSettings.cs`、`Suite/SuiteConfig.cs`（ItemOp 等）、新增 `Items/Inventory.cs`
- PBZManager：`RuleRow.cs`（重写）、新增 `ConditionsDialog.cs`、`ItemRow.cs`，`MainWindow.xaml(.cs)`
- 测试：新增 `TriggerTests.cs`（8）、`ItemsTests.cs`（3），`Feed.cs` 可带数值，`ConditionPlayerTests.cs` 适配
- `src/PBZSuite/Scripts/main.lua` 0.3.0；文档：产品规则 10 修订、18–20 新增，`docs/p4-tasks.md`
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test`：77/77（含 T4a 19 分钟回放，默认规则结果不变）。PBZ.Core 与界面代码编译 0 错误；最后一步复制 exe 失败，因为用户的修改器正开着，待关闭后重编。
- 未验证：界面截图（同上原因，且避免在 Claude 这边启动）；PBZSuite 0.3.0 未装进游戏；物品/钱修改、回读、重载后保持均未实测；物品中文名能否从 ValueCache 读到未知；新事件只有单元测试，未实战。

## 状态
PARTIAL：代码与测试完成，待重编、安装 0.3.0、实测；分类表待用户确认。

## 未完成与下一步
1. 用户关修改器 → 重编 → 用户打开修改器、游戏关闭时"安装 / 更新"到 0.3.0。
2. 进游戏：物品页"从游戏读取"，改一种材料（例如 Weapon_Material_02 6→10）和钱，看游戏内数量；存档、读档后是否保持。
3. 用户确认可改范围（规则 19）。
4. 然后 T5b 自动弹反/闪避调查。

## 给接手者的提醒（坑、约定、别碰的地方）
- itemOps 的 seq 用首个毫秒时间戳起递增；游戏启动时会把文件里已有的 op 当作已执行（防止重放）。
- "喝药"事件按两槽合计减少判断，换药也可能触发；"杀气满"等在锁定开启时基本不会再触发。
- RuleConditions 只过滤规则的开始；淡出不看附加条件。
- 改 PlayRule 字段要注意 MusicSettings 的 JSON 选项 WhenWritingNull，可空字段需要 `JsonIgnore(Never)` 才能保存 null。
