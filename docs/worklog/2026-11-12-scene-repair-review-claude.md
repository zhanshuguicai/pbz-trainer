# 场景修复功能（1.0.3）、整体复查、联网调研
- 日期：2026-11-12（含 2026-11-11 的坏档排查）
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户 2026-11-11"我现在坏档了……戏梦山庄有一幅画掩盖的秘密小道仅能触发一次，你需要帮我再次解锁"；2026-11-12"先把这个解决卡死的功能做进修改器，再检查一遍这个修改器，去网上搜集信息，考虑还有没有需要加进去的优化功能"

## 目标
1. 帮用户重新打开戏梦山庄挂画后的密道；2. 把这个能力做成修改器的正式功能；3. 复查修改器；4. 调研同类修改器/模组，列出可加的功能。

## 做了什么（大体流程，按顺序）
1. 2026-11-11 用只读探针（probe adhoc.lua，F8）在画前扫描 15 米内物件，证据 `evidence/scene/probe1-UE4SS.log`、`probe2-UE4SS.log`：
   - 画：`BP_Guahua_Huanshu_C_UAID_BCFCE7058A5725FB02_1632900079`，剧情 `BPF_LengTuMiDao`，可见、有碰撞，自带函数 `Begain Dissolve`（溶解时间轴）。
   - 交互点"调查挂画"：`BP_FlowInteractActor_C_..._1219087244`，剧情 `BPF_LengTuGuaHua`，已执行完。
   - 旁边窄门 `BP_SlenderDoor_C`：DoorOpen=true。
2. 写了一次性解锁脚本（调用 `Begain Dissolve`，3 秒后关碰撞）放进探针 adhoc.lua，用户同意后同步进游戏。**结果未知**：之后游戏重启，UE4SS.log 被清空，没有 UNLOCK 行；用户也没反馈。
3. 做进修改器（PBZSuite 与修改器都升到 1.0.3）：
   - Lua：`sceneRequest` 计数变化时扫描 20 米内相关物件（有存档组件/剧情组件，或类名含 Door/Gate/Block/Wall/Switch/Guahua/PingZhang/Barrier/Interact/Chest），写 `runtime/scene.json`；`sceneOps`（dissolve/remove/restore，按路径找物件）各执行一次；首次操作记下原始隐藏/碰撞，恢复时还原；溶解过的不能恢复（报 dissolved）。
   - 修改器：新页"场景修复"（扫描、打开/溶解、移除、恢复原样），已认识的机关排最前（`PBZ.Core/Scene/SceneSnapshot.cs` 的 SceneKnown 表，目前只有幻术挂画）；第一次操作前备份存档；6 秒无回应提示游戏内模块是旧版。
4. 复查（code-review，high）全部未提交改动，9 条全部修复：连段点按会截断长按弹反；溶解后 3 秒内恢复会被撤销；恢复后成为隐形墙；恢复硬编码碰撞；日志检查被其他模组的错误掩盖；关闭时日志写入已释放对象可能崩溃；锁血时弹反结果全记为成功；扫描上限 80 可能丢掉目标；JsonDocument 未释放。
5. 联网调研（WeMod、Cheat Happens、VGTimes NYXEON、The Outerhaven 模组盘点），结论见下方"未完成与下一步"。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`（场景段、版本 1.0.3、弹反结果在锁血时记 unknown）
- 新增 `src/PBZManager/PBZ.Core/Scene/SceneSnapshot.cs`、`PBZManager/MainWindow.Scene.cs`、`PBZ.Core.Tests/SceneTests.cs`
- `SuiteConfig.cs`（SceneRequest/SceneOps）、`SuiteInstaller.cs`（scene.json 为运行时文件）、`SuiteLogCheck.cs`、`ParryCommand.cs`、`ParryRelay.cs`、`MainWindow.xaml(.cs)`、`PBZManager.csproj`、测试 2 个新增
- 探针 `probe/.../PBZProbe/adhoc.lua` 现在是一次性解锁脚本（游戏里同一份）
- 未提交

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build` 0 错误；`dotnet test` 111/111。`--snapshot` 看过场景修复页布局正常。
- 未验证：游戏内扫描与溶解（游戏未运行；`a["Begain Dissolve"](a)` 这种带空格的函数名调用方式未实测，失败时自动改为隐藏+可穿过）。桌面副本未更新（覆盖命令被自动权限检查拦下）。

## 状态
PARTIAL：代码完成并通过测试，待用户覆盖桌面副本、安装 PBZSuite 1.0.3、在戏梦山庄实测。

## 未完成与下一步
- 用户：关修改器 → 把 `src/PBZManager/PBZManager/bin/Debug/net10.0-windows` 下除 `data` 外的文件覆盖到桌面副本 → 关游戏点"安装 / 更新" → 进游戏到画前点"扫描附近"→ 选"幻术挂画"→"打开 / 溶解"。
- 调研得到的候选功能（同类修改器常见，按我们架构的可行性排序）：存点/传送回存点（也能救卡地形）、游戏速度、无限药/药壶、伤害倍率与敌人血量倍率、自动 QTE、移动加速/飞行穿墙、经验与等级修改；相机距离/FOV 已有独立模组 Phantom Tweaks。需用户选择后再做。

## 给接手者的提醒（坑、约定、别碰的地方）
- 场景修改不进存档，读档或离开区域会恢复；这是有意的（不碰剧情记录，风险最低）。
- 扫描里 `FindAllOf("Actor")` 只在请求时调用一次，绝不能放进循环。
- 游戏可能已有新版本（Cheat Happens 列出 302256，11-05/11-07 更新）；我们的白名单按 Steam build 号（26266538、26367565），用户 11-11 仍正常运行，若写入功能被自动停用，先看 build 号。
