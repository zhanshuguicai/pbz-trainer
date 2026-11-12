# 影之刃零 修改器（原"Mod 管理器"）

## 项目一句话，详细文档在哪
为本机 Steam 正式版《影之刃零》做一个集成修改器：实时状态、自定义条件播放器、数值修改（血量/杀气/绝技能量/影魂点数，锁满或固定值）、强化材料与钱的数量修改、自动弹反闪避。
- 方向与架构：[ADR 0001](docs/adr/0001-architecture.md)（两进程、本地文件通信）→ [ADR 0002](docs/adr/0002-integrated-trainer.md)（改为集成修改器）
- 任务表与完成标准：[P4 任务](docs/p4-tasks.md)；产品规则（用户确认 + Claude 决定，逐条标来源）：[产品规则](docs/decisions/2026-11-03-product-rules.md)
- 游戏数据读法与实测现象：[三字段数据来源](docs/p2-field-sources.md)；最初的交接规划：[完整交接](docs/影之刃零Mod_Claude完整交接与实施规划_20261103.md)（历史背景，状态以本文件为准）

## 当前状态（2026-11-12，版本 2.0.0 已发布）
- 全部功能完成并经用户实测：状态与遥测、条件播放器（22 种事件含 Boss、附加条件、淡出、每条规则播放设置、热键、导入导出）、数值修改（锁满/固定值）、物品与钱、自动弹反/闪避（真实按键，未受击约 85%）。界面按《影之刃零》风格重做。
- 版本 2.0.0（修改器与 PBZSuite；1.0.2、1.0.3 未单独发布，并入 2.0.0）。2.0.0 已推送并发布：https://github.com/zhanshuguicai/pbz-trainer/releases/tag/v2.0.0 （附 `PBZTrainer-2.0.0-win-x64.zip`）。此前 1.0.1：公开仓库 https://github.com/zhanshuguicai/pbz-trainer（main，标签 v1.0.1 = 提交 e648ff9），只含代码与文档；本机目录即该仓库的工作副本。Release：https://github.com/zhanshuguicai/pbz-trainer/releases/tag/v1.0.1 ，附 `PBZTrainer-1.0.1-win-x64.zip`（自带运行时的单文件修改器、加载器、一键脚本、保姆式教程 docs/使用教程.md）。
- 游戏：Steam appid 4115450，已验证 build 26266538 与 26367565（2026-11-04 03:29 更新）。游戏更新后修改器自动停用写入功能。
- 游戏目录：UE4SS + PR #1484 配置、`Mods/PBZSuite`（游戏里是 1.0.0 的 Boss 修复版，1.0.1 只差版本号，待用修改器更新）、`Mods/PBZProbe`（只读探针）。
- 代码：`src/PBZSuite/`（Lua）、`src/PBZManager/PBZManager.sln`（PBZ.Core、117 个测试、WPF）。修改器数据在 exe 旁 `data\`。
- **用户实际运行的是桌面副本 `C:\Users\38365\Desktop\net10.0-windows\PBZManager.exe`（有自己的 `data\`：规则、热键、提前量 100、连段按住开）**，不是项目 bin 里那份。2026-11-07 覆盖为 1.0.2；2026-11-12 已覆盖为 2.0.0（data 未动）；游戏内 PBZSuite 仍是 1.0.2，待用户在该修改器里点"安装 / 更新"。以后更新：关掉它 → 复制 bin\Debug\net10.0-windows 下除 data 外的全部文件过去。
- 用户未要求派工，全部由 Claude（Opus）执行。

## 进行中
- 2.0.0 已发布：1.0.2 自动弹反 + 场景修复页 + 玩家工具页（游戏/移动速度、伤害倍率、无限药、飞行、传送、等级经验）。117 个测试通过，**游戏内全部未实测**。日志：[玩家工具](docs/worklog/2026-11-12-player-tools-claude.md)、[场景修复与复查](docs/worklog/2026-11-12-scene-repair-review-claude.md)、[弹反 1.0.2](docs/worklog/2026-11-07-parry-timing-types-claude.md)。

## 下一步
1. 用户在桌面副本点"安装 / 更新"到 PBZSuite 2.0.0（用户拒绝了电脑控制，实测由用户做）→ 逐项实测：戏梦山庄画前"场景修复"扫描并溶解；玩家工具各项（看 UE4SS.log 的 `player op`、`scale ... error`）；实战弹反（对照 `parry #序号` 与 `data\parry-relay.log`），遇黑光记下时间。
2. 实测有问题先修，再出 2.0.1（打包方法见 [2.0.0 发布包日志](docs/worklog/2026-11-12-release-200-package-claude.md)：沿用上一版 zip 结构，用 .NET ZipFile 打包）。
3. 自动 QTE：用户 2026-11-12 决定不做。
4. 可选：许可证；提交邮箱是否改为 GitHub noreply；按攻击类型分别设提前量；补测钱修改与读档保持、物品中文名、手柄虚拟输入（产品规则 22）。

## 已知问题与坑
- **WPF 分部类之间静态字段初始化顺序不确定**：MainWindow.*.cs 里引用 `DataDir` 的静态值要写成属性（2026-11-12 因此启动崩溃过一次）。
- 玩家工具按倍率改属性：游戏若每帧重算某属性（例如移动速度可能另有 FinalWalkSpeed），倍率会失效；受到伤害倍率用的是 TakenReductionMultiplier_*，含义由命名推断。
- **Lua 主代码块中途报错时，前面注册的循环照常运行**，界面看起来"半好"（2026-11-07 弹反循环因此没启动）。修改器现在会在状态页红字提示 UE4SS.log 里的 PBZSuite 脚本错误；改 main.lua 后要检查删掉的常量/函数是否还有人用。
- **注入 Enhanced Input / 直接启动能力不能代替按键**（evidence/t5b）；**UE4SS 的 LoopInGameThreadWithDelay 返回 true 不会停止**。
- **游戏更新会让修改器自动停用写入功能**（白名单在 `PBZ.Core/Game/GameInstall.cs`）；有新版本实测证据后再加。
- **Claude 桌面应用启动的进程（我的命令、`--snapshot`）写进 AppData 的新文件会被重定向到 `AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache`，用户看不到**。共享的东西不要放 AppData；修改器已改用 exe 旁 `data\`。
- **崩溃**：采样循环里不要 `FindAllOf`（P2 v1 因此在 UE4SS.dll 内崩溃）；用 `LoopInGameThreadWithDelay`；不要热重载。崩溃转储写在 `ue4ss/crash_*.dmp`，可用 `probe/read-minidump.py` 看。
- **事件判定**：InCombat 是计数且常短暂归零（脱战确认 5 s）；新 Pawn 第一拍是 850/977.5 假血量；读图可 10+ 秒无样本；有非喝药的回血来源。
- **阵亡**：`CharacterState.Dead` 标签从未在实测中出现过；另有"血量归零"的后备判断，均未实战验证。
- T8 两轮探针里 InCombat 一直是 0（T4a 时正常），可能那两次没真正入战，留意。
- 本机没有 Lua 解释器，Lua 改动只能人工审读，再进游戏看 `UE4SS.log`。
- **两份 PBZSuite 清单**：修改器的（%LOCALAPPDATA%）和脚本的（`evidence/pbzsuite-manifest.json`）；今后只用修改器装卸。
- **待用户确认的 Claude 决定**：脱战确认 5 s（规则 9）；数值修改默认热键、提示音、血量下限 1（规则 15、16）。
- 存档在游戏目录 `ProjectPBZ/Saved`（不在 AppData）；最近备份 `backups/before-t8-20261104-005928`。不要自动用旧备份覆盖存档。
- 音频在 `E:/Downloads`（AIZO.mp3、DJ阿黛-雨爱.mp3）；只存路径，不复制进项目或包。
- 游戏开着时 Windows 不刷新遥测文件的大小/时间，按文件名选最新会话（已在 TelemetryTailer 处理）。
- `ProcessConsoleExec` 钩子装不上，游戏内控制台不可用；`pc.CheatManager` 为空，没有现成作弊命令。
- 测试项目固定了本机 NuGet 缓存里已有的版本（MSTest 3.6.4、Test.Sdk 17.14.1），改版本会触发联网下载。
- 运行中的修改器会锁住 exe，重新编译前先关掉。电脑控制无法授权给未安装的新程序，看界面用 `--snapshot`。
- PR #1484 上游未合入；包名 1196-g236adf26 与日志自报 SHA 1ceefac8 不一致，保留差异。

## 怎么运行、怎么验证
- 构建与测试：`dotnet build src/PBZManager/PBZManager.sln`；`dotnet test src/PBZManager/PBZ.Core.Tests`（含 T4a 19 分钟真实录制回放）。
- 运行修改器：`src/PBZManager/PBZManager/bin/Debug/net10.0-windows/PBZManager.exe`；界面截图：`PBZManager.exe --snapshot <png> 10 --tab <页序号>`（数值修改页是 2）。
- 改了 Lua：游戏退出后在修改器点"安装 / 更新"。探针的 watch.lua/adhoc.lua 可在游戏运行时用 `probe/p2-probe.ps1 -Mode SyncWatch` 同步，F11/F8 生效。
- 重新生成图标：`tools/make-icon.ps1`。
- 看遥测：`python -I tools/telemetry-summary.py <runtime/telemetry-*.jsonl>`；修改器事件：`%LOCALAPPDATA%\PBZManager\events.log`；游戏侧日志：`ue4ss/UE4SS.log`（`[PBZSuite]`、`[PBZProbe]`）。
- 全部撤销（游戏退出后，按顺序）：修改器"卸载"或 `tools/pbzsuite.ps1 -Mode Rollback` → `probe/p2-probe.ps1 -Mode Rollback` → `probe/p1-loader.ps1 -Mode Rollback`。

## 最近工作日志
- [2.0.0 发布包、Release、桌面副本](docs/worklog/2026-11-12-release-200-package-claude.md)
- [2.0.0 版本号与推送](docs/worklog/2026-11-12-release-200-claude.md)
- [玩家工具 1.0.3、提交](docs/worklog/2026-11-12-player-tools-claude.md)
- [场景修复 1.0.3、复查、调研](docs/worklog/2026-11-12-scene-repair-review-claude.md)
- [自动弹反 1.0.2：精确排程、连段、攻击类型](docs/worklog/2026-11-07-parry-timing-types-claude.md)
