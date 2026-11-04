# T4b 管理器骨架 + T4c 事件判定与回放测试
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"继续 T4b+T4c"

## 目标
.NET 10 解决方案：核心库（遥测解析、跟读、事件状态机、安装器、配置、版本校验）、MSTest 测试（含 T4a 真实录制回放）、WPF 管理器界面；接上正在运行的游戏验证。

## 做了什么（大体流程，按顺序）
1. 确认本机 NuGet 缓存有 MSTest 3.6.4 / Test.Sdk 17.14.1 及依赖，固定这些版本离线还原，未下载新包。
2. 建 src/PBZManager/PBZManager.sln：PBZ.Core（net10.0）、PBZ.Core.Tests、PBZManager（net10.0-windows WPF，payload 打包 src/PBZSuite）。
3. PBZ.Core：Snapshot 解析（schema v1，null/valid 原样保留）；TelemetryTailer（按文件名排序选最新会话、只返回完整行、首次接入跳过历史）；EventEngine（规则 2/2a/3/3b/4，纯状态机）；GameInstall（libraryfolders.vdf + appmanifest 定位）；GameCompatibility（EXE SHA-256 对照已验证版本，按大小+时间缓存）；SuiteConfigStore（临时文件 + File.Replace，revision 递增，旧文件缺字段补默认）；SuiteInstaller（清单、拒绝覆盖外来文件、运行时文件归档、接管脚本安装）。
4. 测试：规则边界 23 个、T4a 回放 4 个、解析/跟读 6 个、安装/配置 6 个。
5. 回放发现 3 s 脱战确认把同一场战斗（t=365.6–370.1 s 计数归零 4.5 s）切成两场 → 改为 5 s，并同步用例。
6. 首次运行管理器发现 parryHotkey 读成 null（旧 config 缺字段）→ 修 Read 补默认并加用例。
7. 加 `--snapshot <png> [秒]` 开发参数（computer-use 无法授权未注册的新程序），渲染窗口确认界面。

## 改动（文件或产物；提交号，如有）
- 新增 src/PBZManager/（sln、PBZ.Core、PBZ.Core.Tests、PBZManager）。
- 运行时：%LOCALAPPDATA%\PBZManager\pbzsuite-manifest.json（接管了脚本安装）、exe-hash-cache.json；游戏 runtime/config.json 由管理器写到 revision 4（compatible=true，parryHotkey 当时为 null，修复后下次写入会补 F7）。
- 证据 evidence/t4b-manager-live.png。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build PBZManager.sln`：0 警告 0 错误。
- `dotnet test PBZ.Core.Tests`：39/39 通过。
- T4a 回放：13 场战斗；HalfHealth 仅 1 次，466.8 s，位于第 4 场（463.3–547.2 s 硬仗）；FlasksEmpty 0 次；Pawn 代际变化处无音乐事件。
- 实机：管理器定位游戏、EXE 校验通过、接管已装 PBZSuite；写 config revision 4，游戏 12:48:09 应用（compatible=true），遥测回报 configRevision=4；界面实时显示血量 1424/1424、两槽药、会话与代数（截图证据）。
- 未验证：界面上的安装/卸载按钮在真实游戏目录的操作（只做了临时目录单元测试）；实战中界面事件列表的显示（截图时未在战斗）。

## 状态
PASS：T4b、T4c 完成标准满足（安装/卸载以单元测试覆盖，真实目录操作留待 T4d/T6）。

## 未完成与下一步
- T4d：AudioPlayer 与音乐设置（两首 mp3 路径、音量、淡入淡出），实战验证半血播 aizo、喝空播雨爱 DJ。
- 待用户确认的 Claude 决定：喝空在脱战时也触发；脱战确认 5 s；战斗结束后音乐是否停止尚未定（T4d 决定并说明）。

## 给接手者的提醒（坑、约定、别碰的地方）
- 现在有两份 PBZSuite 清单：管理器的（%LOCALAPPDATA%\PBZManager）和脚本的（evidence/pbzsuite-manifest.json）。以后由管理器安装/卸载；若改用脚本，先确认两份一致。
- 测试项目固定了缓存中已有的包版本，升级版本会触发联网下载。
- 运行中的管理器会锁住 exe，重新编译前先关掉它。
