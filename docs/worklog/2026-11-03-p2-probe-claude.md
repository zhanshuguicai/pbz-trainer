# P2 只读探针：血量、药数量、战斗状态
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"开始 P2"；中途"发生了报错，这方面的探针去网上找找"；收尾"死亡摇铃不用测了，可以"

## 目标
用只读 UE4SS Lua 探针找到三字段的真实来源，并用受伤、喝药、喝到 0、入战/脱战做动态验证。

## 做了什么（大体流程，按顺序）
1. 写 PBZProbe v1（F9 导出、F10 采样、F11 重载 watch.lua），用 mod 目录 enabled.txt 加载，不改 P1 文件；写 probe/p2-probe.ps1 单独清单与回滚。
2. F9 导出：玩家 BP_Renewal_Hun_C，属性集 BasicAttributeSet（Health/Health_Max）；药线索 AddMedicineItemDatas。
3. v1 采样期间游戏崩溃（UE4SS.dll 内访问违例，RIP 乱值）；写 probe/read-minidump.py 解析转储。
4. 查网上现成方案：KintaroModding/phantombladezero_passiveregen（同 UE4SS 1ceefac8，读 AttributeSetBase、GetGameplayTagCount CharacterState.InCombat、LoopInGameThreadWithDelay）；EXACG/PhantomPact_4_Coop RECON_NOTES（PBZBackPackSubsystem.Inventories）。
5. 写 v2：LoopInGameThreadWithDelay、按键只置标志、循环内不 FindAllOf、F8 运行 adhoc.lua（可在游戏运行中替换）。回滚 v1、安装 v2、重启游戏。
6. 实测血量/战斗；F8 逐层展开背包子系统 → MedicineInfos（PBZMedicineInfo.CurrentTime）与 Inventories（PBZInventoryInfo.CurrentAmount）。
7. 实测两药槽喝到 0。用户确认"血瓶"= 两槽药合计，死亡/摇铃不测。

## 改动（文件或产物；提交号，如有）
- 新增 probe/p2-stage/ue4ss/Mods/PBZProbe/（Scripts/main.lua v2、watch.lua、adhoc.lua、enabled.txt）、probe/p2-probe.ps1、probe/read-minidump.py。
- 游戏目录新增 ue4ss/Mods/PBZProbe（4 文件，清单 evidence/p2-probe-manifest.json；v1 清单归档为 evidence/p2-probe-manifest-removed-*.json）。
- 新增 [三字段数据来源](../p2-field-sources.md)；[产品规则](../decisions/2026-11-03-product-rules.md) 追加 3b、7。
- 证据 evidence/p2/：discovery-1.log、run2-probe.log、adhoc-maps-1.log、adhoc-objects-1.log、run2-medicine.log、v1 崩溃转储。
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- 血量：PASS（稳定、受伤 3 次各 −36.2、喝药渐进回满、最大血量变化被跟随）。
- 战斗状态：PASS（InCombat 0→2→0，单轮；未重复多轮）。
- 药数量：PASS（FeiJiu 3→0、Kuye 4→0，每次 −1，0 后稳定）；其余三种药未实测。
- v2 稳定：两段采样共约 20 分钟无崩溃，ue4ss 下无新转储。
- 死亡、重生、摇铃补药、读档、传送：未验证，用户决定不测。
- 敌人血量串读：未专门验证；读法经 PlayerController.Pawn，结构上不会读到敌人。

## 状态
PASS：三字段来源确定并动态验证；生命周期项按用户决定豁免。

## 未完成与下一步
- P3：定架构（管理器技术栈、Lua 侧事件判定 vs 管理器侧判定、本机 IPC），确认 aizo/雨爱事件规则的实现细节。
- 游戏仍在运行，探针仍安装；产品 mod 落地前可保留探针用于调试。
- 钱（货币）字段未找到：Coin 属性为 0，疑似 Dust（UpdateDustNum/AddDustNum），Mod 3 前查。

## 给接手者的提醒（坑、约定、别碰的地方）
- 回滚顺序：先 `p2-probe.ps1 -Mode Rollback`，再 `p1-loader.ps1 -Mode Rollback`；P1 回滚遇到未知文件会中止。
- watch.lua/adhoc.lua 是可变文件，用 `p2-probe.ps1 -Mode SyncWatch` 在游戏运行时同步，F11/F8 生效。
- 别在采样循环里 FindAllOf；别热重载（联机 mod 笔记中两次崩溃都来自热重载）。
- `GetGameplayTagCount` 是本探针唯一调用的游戏函数（只读查询）。
