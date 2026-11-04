# T8 数值修改（锁满 / 固定值）、可改热键、图标；T5a 离线调查
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"测完看了，都没问题，现在把这个功能加入修改器界面内，允许修改键位，修改器的图标改成影之刃0的ui图标，一个圆圈加上一个穿过的斜杠，你可以上网搜一搜，随后进行下一步开发"；中途补充"'锁定'这一栏名字改为数值修改，再把功能扩展到可以自由选择固定的数值"
- 接续：[T8 锁定探针](2026-11-04-t8-lock-probe-claude.md)（第 2 轮结论见下）

## 目标
把探针验证过的写法做成正式功能：游戏内模块 + 修改器"数值修改"页 + 可改热键；换图标；然后开始 T5a。

## 做了什么（大体流程，按顺序）
1. 读第 2 轮日志（存为 `evidence/p2/t8-round2-UE4SS.log`）：血量写入后调用 `OnRep_Health(旧值)` 血条会刷新；杀气、绝技、影魂直接写 BaseValue/CurrentValue 即可（用户确认界面四项都正常）。背包的 `GetCurrentUltimateCharge` 一直为 0，绝技条跟随的是 `UltimateCharge` 属性。第 2 轮里锁定一直开着，没测"关闭后恢复"和读档。
2. PBZSuite 0.2.0：配置加 `lock` 段（总开关、四项开关、四项目标值，-1 = 锁满）；每 200 ms 按目标写入，固定值模式高了压回、低了补上，上限封顶；新 Pawn 出现后等 1 s 再写；血量 ≤0 或带 Dead 标签不写；配置轮询 1 s → 0.4 s；遥测 `modules.lock` 报告是否生效。
3. 修改器：新页"数值修改"（总开关、四项勾选、"固定为"输入框、热键按钮、游戏内生效状态、提示音开关）；系统级热键（RegisterHotKey，默认 Ctrl+F1 总开关、Ctrl+F2–F5 四项），点按钮后按新组合键改键，Esc 取消、Backspace/Delete 清除，单个字母数字键须带修饰键，重复键位拒绝；设置存 `%LOCALAPPDATA%\PBZManager\lock.json`；总开关不保存，每次启动为关，启动时清掉上次异常退出留下的开启状态，关闭修改器时写回关闭。
4. 图标：网上搜索与 Steam 缓存（logo 为书法字"影之刃零"，EXE 图标为角色画像）都没有"圆圈+斜杠"官方素材，按用户描述自绘（`tools/make-icon.ps1` 生成 `app.ico` 7 种尺寸）。
5. 探针 F8 改为只读：T5a 物品普查（钱、全部背包条目及分类字段）。
6. T5a 离线调查（用第 2 轮导出的 Lua 类型表 `ue4ss/Mods/shared/types`）：见"给接手者的提醒"。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`（0.2.0，锁定模块）
- `src/PBZManager/PBZ.Core/Suite/SuiteConfig.cs`（SuiteLock）、`PBZ.Core/Telemetry/Snapshot.cs`（Modules.Lock）、新增 `PBZ.Core/Lock/LockSettings.cs`
- `src/PBZManager/PBZManager/`：`MainWindow.xaml(.cs)`（数值修改页）、新增 `GlobalHotkeys.cs`、`app.ico`、`app.png`、`PBZManager.csproj`（图标、版本 0.2.0）
- 新增 `src/PBZManager/PBZ.Core.Tests/LockTests.cs`（11 个测试）、`tools/make-icon.ps1`
- `probe/.../PBZProbe/adhoc.lua`（T5a 只读普查，已同步到游戏目录）
- 文档：产品规则 15–17、`docs/p4-tasks.md`
- 非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build`：0 警告 0 错误；`dotnet test`：64/64（新增 11 个：热键解析与显示、重复键位、设置读写与清洗、固定值校验与取整、配置 lock 段读写、遥测 lock 标记）。
- `--snapshot` 截图：数值修改页与状态页渲染正常；状态页显示 PBZSuite"有新版本可更新"。
- 未验证：PBZSuite 0.2.0 还没装进游戏（需用户在修改器点"安装 / 更新"），游戏内锁满/固定值、热键在全屏下触发、提示音、关闭后恢复消耗、读档不残留都未实测；Lua 没有本机解释器做语法检查，只靠人工审读。
- 未验证：热键在游戏前台时是否被游戏吞掉（RegisterHotKey 一般不受影响）。

## 状态
PARTIAL：代码、界面、测试完成；游戏内实测待用户。

## 未完成与下一步
1. 用户点"安装 / 更新"，进游戏实测：总开关和四项热键、锁满、固定值（例如杀气 200、影魂 2）、关闭后恢复消耗、读档。
2. 同一次游戏里按 F8 跑 T5a 普查，回来整理材料分类表给用户确认。
3. T5a 写法：优先用游戏函数改数量（见下），不直接写 CurrentAmount。

## 给接手者的提醒（坑、约定、别碰的地方）
- 第 2 轮里 InCombat 始终为 0（两轮都是），但 T4a 时正常；可能这两次测试没有真正入战，值得留意。
- T5a 线索（类型表）：钱 = 角色 `ConsciousnessDust`（6232，`Coin` 恒 0）；`BP_RenewalPlayer:ChangeDustNum(bIsAdd, Num)`、`PBZBackPackSubsystem:AddDustNum(Num)`；物品 `AddPropsToBackPack(Num, ItemId, bPushWidget)`、`RemovePropsFromBackPack(Num, ItemId, bSendBroadcast)`、`GetItemNumInfo(PropID, CurrentNum, MaxNum)`；分类字段 `ItemDataStruct.MaterialType`（1 武器强化、2 杀器强化、3 武器摄魂、4 杀器摄魂、5 饰品强化、6 药强化、7 核心养成）、`ItemDetailType`（1 材料、2 剧情物品…）、`ItemType`（0 物品、1 书…）。
- 修改器每次启动会把配置里的锁定写成关闭（PushLock），这是有意的。
- 改 PBZSuite 后要用修改器"安装 / 更新"（不要用 `tools/pbzsuite.ps1`，否则修改器的安装清单对不上）。
