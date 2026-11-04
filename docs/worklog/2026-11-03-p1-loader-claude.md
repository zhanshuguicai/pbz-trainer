# P1 最小加载：UE4SS + PR #1484 配置
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"开始 P1"；退出后用户确认"已正常退出"

## 目标
在不改 EXE、不启用任何玩法 mod 的前提下，验证 UE4SS 加 PR #1484 三配置能否完成反射初始化，并能正常退出。

## 做了什么（大体流程，按顺序）
1. 读旧安装/审阅日志、旧回滚脚本、暂存包和配置；确认 mods.txt/mods.json 全部禁用。
2. 定位存档：在游戏目录 `ProjectPBZ/Saved`（SaveGames + 3 个设置 .sav），不在 AppData。
3. 新备份 `backups/before-p1-20261103-082519`：29 文件，10,413,471 字节，逐个 hash 比对 0 不一致。
4. 组装 `probe/p1-stage`：ue4ss-1196 暂存包 + PR 三文件放进 `ue4ss/`（与 UE4SS.dll 同级），共 23 文件。
5. 写 `probe/p1-loader.ps1`（Install/Rollback）：安装前检查进程、EXE hash 对 T0 基线、所有目标不存在；先写 PLANNED 清单再复制并逐个校验；回滚只认本轮清单。
6. 执行安装：23 文件，无覆盖，清单状态 INSTALLED。
7. `steam://rungameid/4115450` 启动（工具未拒绝）；读 UE4SS.log。
8. 用户从游戏菜单正常退出；复核进程、文件、EXE。

## 改动（文件或产物；提交号，如有）
- 游戏目录 `platform/steam` 新增：dwmapi.dll、ue4ss/（23 文件）+ 运行生成的 UE4SS.log。保留安装，进入 P2。
- 新增 probe/p1-stage/、probe/p1-loader.ps1、evidence/p1-install-manifest.json、evidence/p1-UE4SS-init.log、backups/before-p1-20261103-082519/。
- 项目不是 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- MemberVariableLayout.ini loaded：PASS。
- PS 扫描 2.4s 成功，UE 5.7 / Shipping；自报 v3.0.1 Beta Git SHA 1ceefac8（与包名不一致的问题照旧保留）。
- Lua 签名：GNatives 0x14d9f9e20、GUObjectArray 0x14ddb3c40，与静态校验 RVA（0xd9f9e20 / 0xddb3c40 + 基址 0x140000000）一致：PASS。
- FName 构造函数验证、KismetStringLibrary/核心类定位、各钩子地址、8 个默认 mod 均 disabled、Event loop start、HashTables 自检（9822 类）：PASS。
- `ProcessConsoleExec` 钩子 5 次 "Failed to add hook"：FAILED，只影响游戏内控制台命令，不影响读数路线。
- 游戏响应：进程 Responding=True；主菜单与正常退出由用户人工确认。日志以 Event loop end 结束。
- 退出后：无 PBZ 进程；23 文件 hash 全部不变；ue4ss 下只多出 UE4SS.log；PBZ.exe hash 等于 T0 基线。
- 血量、血瓶、战斗状态：未验证，属于 P2。

## 状态
PASS：反射初始化成功、可正常退出、文件归属清晰、回滚依据完整。按规划保留安装进入 P2。

## 未完成与下一步
- P2：写只读 Lua 探针 mod，定位本地玩家、属性集、血瓶/消耗品、战斗状态拥有者，输出到项目内日志；需重启游戏加载（热重载已关闭）。
- 若要撤销：游戏退出后运行 `probe/p1-loader.ps1 -Mode Rollback`。

## 给接手者的提醒（坑、约定、别碰的地方）
- 存档在游戏目录 `ProjectPBZ/Saved`，不是 `%LOCALAPPDATA%/ProjectPBZ`（那里只有配置、日志、管线缓存）。
- 新增 P2 mod 文件不在 P1 清单里，回滚脚本会把它们视为未知文件并中止；P2 需要扩展清单或单独的回滚步骤。
- 每次启动都会新建 CrashReportClient/UECC-* 目录，不代表崩溃。
- 别在用户主目录全盘递归找文件，超过 5 分钟仍未完成。
