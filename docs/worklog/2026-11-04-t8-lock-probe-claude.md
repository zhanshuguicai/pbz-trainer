# T8 锁定探针（第 1、2 轮）
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"按顺序执行吧"（HANDOFF 下一步 1–4）；第 1 轮实测反馈"影魂点没问题但是另外三项都没实现"

## 目标
确认血量、杀气、绝技能量、影魂点数四项"锁满"的写法：写入生效、界面刷新、不被改回、读档不残留。

## 做了什么（大体流程，按顺序）
1. 游戏未运行时备份存档到 `backups/before-t8-20261104-005928`（29 个文件，与源一致）。
2. 第 1 轮探针（adhoc.lua，F8 开关）：每 200 ms 把低于上限的属性 BaseValue/CurrentValue 写成上限并回读；列出候选函数。watch.lua 加四项采样。
3. 用户实测，读日志（存为 `evidence/p2/t8-round1-UE4SS.log`）。
4. 写第 2 轮探针：血量写入后调用 `OnRep_Health(旧值)`（GAS 里会广播属性变化，预期刷新血条）；采样背包的绝技 getter；打印候选函数参数表；调用 `GenerateLuaTypes()` 生成类型文件供离线查函数。已同步进游戏目录，待实测。

## 改动（文件或产物；提交号，如有）
- `probe/p2-stage/ue4ss/Mods/PBZProbe/adhoc.lua`（第 2 轮，含写入）、`watch.lua`（加 shachi/ult/ultCount/spirit 采样）；已同步到游戏目录。
- 新增 `evidence/p2/t8-round1-UE4SS.log`、存档备份目录。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
第 1 轮日志事实：
- 四项写入后回读均为上限，且保持：杀气每次技能 480→321.6，下一拍被写回 480；血量一次 683.4→1139；影魂 4→5 反复补回；绝技属性 3→100。
- 用户反馈只有影魂点在界面上生效。推测：直接写内存不触发 GAS 属性变化广播，血条/杀气条不刷新；绝技条读的是背包子系统（`CurrentUltimateCharge` 一直为 0），不是 `UltimateCharge` 属性。
- 采样期间 InCombat 一直为 0；没测读档。
- `pc.CheatManager` 为空，没有现成作弊命令可用；候选：`BasicAttributeSet::OnRep_Health`、`PBZBackPackSubsystem::ChangeUltimatePercentage/UseUltimateCharge/SetMaxUltimateCharge`、`Respawn Recover Attributes`、`Health Post Func`。
- 第 2 轮：未验证（等用户实测）。

## 状态
PARTIAL：写入路径确认可写，界面刷新未解决，等第 2 轮实测。

## 未完成与下一步
- 第 2 轮实测：看血条是否随 OnRep 刷新；从参数表和类型文件找杀气、绝技的正确写法。
- 之后做 PBZSuite 锁定模块、热键、修改器锁定页；读档残留测试。

## 给接手者的提醒（坑、约定、别碰的地方）
- adhoc.lua 里注册的循环在本次游戏进程里一直存在；改 adhoc 后要重启游戏才换掉旧循环（第 2 轮用了新的全局表 PBZT8b）。
- 探针 main.lua 头部写的"只读"已不适用于 adhoc.lua（它会写属性）。
