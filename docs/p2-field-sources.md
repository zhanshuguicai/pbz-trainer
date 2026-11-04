# 三字段数据来源（P2 实测结论，2026-11-03）

适用：PBZ.exe `CL-301524_JKS-17219`、Steam build 26266538、UE4SS 1ceefac8 + PR #1484 配置。游戏更新后需重新验证。
探针：[probe/p2-stage/ue4ss/Mods/PBZProbe](../probe/p2-stage/ue4ss/Mods/PBZProbe/)；原始记录在 [evidence/p2](../evidence/p2/)。

## 取值路径

| 字段 | 读法（Lua，游戏线程） | 类型 |
|---|---|---|
| 当前血量 | `pc.Pawn.AttributeSetBase.Health.CurrentValue` | float |
| 最大血量 | `pc.Pawn.AttributeSetBase.Health_Max.CurrentValue`（不用 BaseValue：基础 850，装备后 1079.5/1241.4） | float |
| 战斗中 | `pc.Pawn.AbilitySystemComponent:GetGameplayTagCount({TagName = FName("CharacterState.InCombat")}) > 0` | int 计数 |
| 死亡 | 同上，标签 `CharacterState.Dead` | int 计数 |
| 药槽药 ID | `FindFirstOf("PBZBackPackSubsystem").TopMedicineID` / `.BottomMedicineID` | FName |
| 药剩余次数 | 同一子系统 `MedicineInfos`（TMap<FName, PBZMedicineInfo>）中对应 ID 的 `CurrentTime` | int |
| 药上限（参考） | `PBZMedicineInfo.MaxTime + PBZBackPackSubsystem.MedicineExtraUsage` | int |
| 物品数量（Mod 3） | 同一子系统 `Inventories`（TMap<FName, PBZInventoryInfo>）的 `CurrentAmount`；`ItemId`、`ItemDataStruct.ItemName.Key` 可做分类 | int |

`pc` 取 `UEHelpers.GetPlayerController()`。玩家类 `BP_Renewal_Hun_C`（联机 mod 笔记中前期为 `BP_Renewal_Hun_Young_C`），不要按类名找玩家。

## 实测证据（evidence/p2/run2-probe.log、run2-medicine.log）

| 操作 | 结果 |
|---|---|
| 站立/菜单 | 血量 1079.5/1079.5 稳定 5 分钟，无伪归零 |
| 最大血量变化 | 10:39:28 Health_Max 1079.5→1241.4，当前血量 10:40:59 跟随回满（原因未确认，推测换装或成长） |
| 受伤 3 次 | 1241.4→1205.2→1169.0→1132.8，每次约 36.2 |
| 喝药回血 | 10:42:18 起每 0.2s 回约 10–15，约 2s 回满，渐进式 |
| 入战/脱战 | InCombat 0→2（10:41:55，受伤前 11s）→0（10:42:13）；喝药未引起抖动 |
| 喝药到 0 | FeiJiu 3→2→1→0，Kuye 4→3→2→1→0；每次 −1，DisplayTime=CurrentTime，0 后稳定 |
| 死亡/重生/摇铃/读档 | 未测（用户决定不测） |

## T4a 长时间录制补充（evidence/t4a/telemetry-6aea1449e87e-0.jsonl，约 19 分钟）

- InCombat 是计数（观察到 0–5，随交战敌人数变化），且频繁短暂归零：如 t=642.4 归零、643.3 又为 1。脱战判断必须有持续时间确认。
- 每次新 Pawn（playerGeneration 变化：首次进入、读图）第一拍血量为 `850/977.5` 的基础值，下一拍才是装备后的真实值。新代际的首个样本不能参与半血判断。
- 摇铃补药实录：t=790.9 脱战中 Kuye 2→4（补到上限）。
- 喝药之外还有其他回血来源（t=492 每拍 +77 且药数不变），喝药不是回血的唯一信号。
- 读图卡顿可达 10–14 秒无样本；EventEngine 不能把长时间无样本当作脱战确认。

## 实现注意

- 采样循环用 `LoopInGameThreadWithDelay`；循环内不做 `FindAllOf`。v1 每 100ms `FindAllOf` + 外层链遍历，战斗中崩在 UE4SS.dll（evidence/p2/crash_*.dmp）。
- `FindFirstOf("PBZBackPackSubsystem")` 结果缓存，失效时重找（WorldSubsystem，换图会重建）。
- TMap 用 `map:ForEach(function(k, v) ... end)`，键值取 `:get()`；值是 UObject。
- 喝药是渐进回血：半血判断取当前采样值，不要把回血过程当作新的下穿。
- 未测生命周期：死亡/重生/读档时 Pawn、子系统可能重建，读不到时必须输出 unknown，不复用旧值。
