# 影之刃零 修改器（原"Mod 管理器"）

## 项目一句话，详细文档在哪
为本机 Steam 正式版《影之刃零》做一个集成修改器：实时状态、自定义条件播放器、数值修改（血量/杀气/绝技能量/影魂点数，锁满或固定值）、强化材料与钱的数量修改、自动弹反闪避。
- 方向与架构：[ADR 0001](docs/adr/0001-architecture.md)（两进程、本地文件通信）→ [ADR 0002](docs/adr/0002-integrated-trainer.md)（改为集成修改器）
- 任务表与完成标准：[P4 任务](docs/p4-tasks.md)；产品规则（用户确认 + Claude 决定，逐条标来源）：[产品规则](docs/decisions/2026-11-03-product-rules.md)
- 游戏数据读法与实测现象：[三字段数据来源](docs/p2-field-sources.md)；最初的交接规划：[完整交接](docs/影之刃零Mod_Claude完整交接与实施规划_20261103.md)（历史背景，状态以本文件为准）

## 当前状态（2026-11-04，1.0.1）
- 全部功能完成并经用户实测：状态与遥测、条件播放器（22 种事件含 Boss、附加条件、淡出、每条规则播放设置、热键、导入导出）、数值修改（锁满/固定值）、物品与钱、自动弹反/闪避（真实按键，未受击约 85%）。界面按《影之刃零》风格重做。
- 版本 1.0.1（修改器与 PBZSuite）。仓库公开在 GitHub（地址见最新工作日志），只含代码与文档。
- 游戏：Steam appid 4115450，已验证 build 26266538 与 26367565（2026-11-04 03:29 更新）。游戏更新后修改器自动停用写入功能。
- 游戏目录：UE4SS + PR #1484 配置、`Mods/PBZSuite`（游戏里是 1.0.0 的 Boss 修复版，1.0.1 只差版本号，待用修改器更新）、`Mods/PBZProbe`（只读探针）。
- 代码：`src/PBZSuite/`（Lua）、`src/PBZManager/PBZManager.sln`（PBZ.Core、94 个测试、WPF）。修改器数据在 exe 旁 `data\`。
- 用户未要求派工，全部由 Claude（Opus）执行。

## 进行中
- 无。

## 下一步
1. 用户关修改器后 `dotnet build` 重编正式输出，再"安装 / 更新"到 PBZSuite 1.0.1。
2. 可选：GitHub Release 附打包的修改器（`dotnet publish -c Release`，需用户确认）；为仓库选许可证（未替用户选）。
3. 补测：钱修改与读档保持、物品中文名（目前显示 ID）、手柄虚拟输入（产品规则 22）。

## 已知问题与坑
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
- [界面主题与 1.0.1 发布](docs/worklog/2026-11-04-ui-theme-release-101-claude.md)
- [音乐 1.0 扩充、T6 开头](docs/worklog/2026-11-04-music-boss-rich-claude.md)
- [T5b 改为真实按键](docs/worklog/2026-11-04-t5b-real-input-claude.md)
- [T5a 完成、T5b 调查与探针](docs/worklog/2026-11-04-t5a-done-t5b-probe-claude.md)
- [T8 数值修改、热键、图标](docs/worklog/2026-11-04-t8-value-lock-claude.md)
