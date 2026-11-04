# 用户明确授权后的标准加载器安装试验
- 日期：2026-11-03
- 执行者：Codex worker /root/pbz_data_probe，S 档 gpt-6.1-sol / medium，第 2 轮（沿用节点，不因工具拒绝升级）；工具未提供实际模型回显。
- 来源：用户“你来安装试试”；中枢批准同目录安装官方包、所有默认 mod 禁用并正常启动验证。

## 目标
完成一次正常可逆安装，确认 UE4SS 是否能完成反射初始化；未通过前不制作管理器或 Mod。

## 做了什么（大体流程，按顺序）
1. 重读 HANDOFF.md 与首轮日志，确认 dwmapi.dll、ue4ss、override.txt 三目标均不存在；包 SHA-256 与首轮相同。
2. 新建带时间戳的存档备份，不覆盖旧备份；禁用 mods.txt 的全部 1 和 mods.json 的全部 true，含 Keybinds；扫描设置 30 秒。
3. 先写计划清单，再复制 dwmapi.dll 与 ue4ss 目录，共 20 文件，逐个记录实际 hash；无 override.txt、无覆盖既有游戏文件。
4. 通过 Steam 正常启动。日志证明 DLL 已加载，工具识别 UE5.7/Shipping；GUObjectArray/GNatives 缺失，持续扫描，未完成反射初始化。
5. 中枢观察到主菜单并通过游戏菜单正常退出；worker 确认无 PBZ 进程后归档运行日志。
6. 执行受 hash 约束的回滚脚本，移除本轮 20 个新增文件，运行日志归档保留，游戏 EXE 与存档未恢复或修改。
7. 查询官方仓库，发现开放且未合入的 PR #1484，精确对应本机版本，并描述相同签名失败。保存实际 diff 供下一轮审阅；本轮未应用该配置。

## 改动（文件或产物；提交号，如有）
- evidence/loader-retry-planned.json、loader-retry-actual.json：完整路径/hash 清单。
- evidence/UE4SS-retry-runtime.log：运行日志。
- evidence/loader-retry-rollback-result.json 与 rollback-runtime-*：回滚结果和归档。
- probe/rollback-loader-retry.ps1：正常退出后验证 hash、拒绝未知文件、仅撤销清单文件的脚本。
- evidence/upstream-pr1484-review-only.json：官方仓库未合入 PR 的实际文件 diff；其中 PR 元数据请求有一次连接超时，文件 diff 请求成功。
- backups/before-loader-retry-*：本轮私有备份。
- 项目内解压包配置已禁用所有默认 Mod。无提交或推送，未改 HANDOFF.md。

## 验证（跑了什么、结果；没验证的写“未验证”和原因）
- 包 SHA-256 一致：75DC8B081D324FE0598A87B26E37D13A84D3648CBB1D08EB48C514376407A11B。
- 安装/Steam 启动：PASS，本次工具接受执行。
- UE4SS DLL 已加载：PASS，产生新日志；下载文件名为1196-g236adf26，而日志自报 Git SHA 1ceefac8，按实际记录，未假定两者相同。
- 反射初始化：FAILED，GUObjectArray/GNatives 持续缺失，日志至 attempt 53；30秒配置未在观察时间内停止重试。此为兼容失败证据，不能判定保护阻挡。
- 游戏主菜单与菜单正常退出：中枢 UI 观察；worker 通过进程状态确认退出。
- 回滚：PASS，脚本回报移除20个记录文件、归档1个运行日志；无游戏 EXE/存档修改。
- 血量、血瓶、战斗状态：未验证，反射初始化未完成。

## 状态
BLOCKED：标准官方包能加载，但缺少 PBZ 引擎布局/签名适配，三个字段均未读取。下一轮仅审阅精确匹配 PR 配置后可做有限适配试验。

## 未完成与下一步
- 审阅 PR #1484 三份新增配置实码及内容哈希，核实无 EXE 修改、玩法写入或保护绕过，再按中枢授权试验。
- 通过初始化后仍必须真实受伤、喝药、入战/脱战验证三字段，不能把主菜单可用算验收。

## 给接手者的提醒（坑、约定、别碰的地方）
- PR #1484 未合入，https://github.com/UE4SS-RE/RE-UE4SS/pull/1484 。不要执行正文示例 ResetHealthToMax 或写 GAS 属性。
- 不做含蓝图类型的 USMap dump，PR明确记录可能崩溃。
- 本轮已回滚标准加载器；下一轮不能假定游戏中仍有 ue4ss。
