# T4a PBZSuite telemetry
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"开始 T4a"

## 目标
游戏内套件 PBZSuite 的第一部分：200 ms 采样三字段写 JSON Lines，读取 config.json，模块开关框架；验证 Lua 能写文件并稳定运行 10 分钟。

## 做了什么（大体流程，按顺序）
1. 写 src/PBZSuite（Scripts/main.lua、enabled.txt、runtime/README.txt）：内置 JSON 编解码、config 轮询（1 s，按 revision 应用，坏格式拒绝）、telemetry 输出（每会话一个文件，8 MB 轮换）、规则 3b 的药槽读法、playerGeneration。
2. 写 tools/pbzsuite.ps1（Install/Update/Rollback，hash 清单，runtime/ 下生成文件回滚时归档到 evidence）。
3. 游戏已退出时安装（3 文件）并启动。
4. 主菜单阶段验证：telemetry 文件生成；noPlayer 时三字段 valid=false 值 null；手写 config revision 1（含 \u 转义）应用，坏格式被拒、revision 保持 1；最后写回 revision 3。
5. 用户进存档游玩约 19 分钟（战斗、受伤、喝药、摇铃、读图）。
6. 写 tools/telemetry-summary.py 汇总；归档 telemetry 到 evidence/t4a；在 p2-field-sources 补充事件判定要注意的现象。

## 改动（文件或产物；提交号，如有）
- 新增 src/PBZSuite/、tools/pbzsuite.ps1、tools/telemetry-summary.py。
- 游戏目录新增 ue4ss/Mods/PBZSuite（清单 evidence/pbzsuite-manifest.json）；runtime/ 有 config.json 与 telemetry 文件。
- 证据 evidence/t4a/telemetry-6aea1449e87e-0.jsonl（2.66 MB）。
- 修改 docs/p2-field-sources.md（T4a 补充节）。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- Lua 写文件：PASS。
- 稳定性：PASS。5054 条，序号无断档，0 条解析失败，0 条 errors，无新崩溃转储；中位间隔 216 ms，p99 323 ms；>1 s 的 5 次间隔均在读图换 Pawn 处（最长 13.6 s）。
- 读数与操作对应：PASS（受伤到 0.11、跌破半血、Kuye 4→3→2、摇铃 2→4、InCombat 0–5 变化、3 次 playerGeneration 变化）。与探针同时对照未做，读法与 P2 探针相同。
- 规则 3b 的 None/查不到分支：未实测（本次两槽一直有药）。
- UE4SS.log 中 "config revision 1 applied" 出现两行同时间戳，疑为日志重复输出，未影响行为，未深究。

## 状态
PASS：T4a 完成标准全部满足。

## 未完成与下一步
- T4b 管理器骨架、T4c EventEngine 回放测试（用 evidence/t4a 这份录制做主要回放数据）。
- 只读探针 PBZProbe 仍安装；PBZSuite 稳定后可回滚探针。

## 给接手者的提醒（坑、约定、别碰的地方）
- 改 Lua 后：游戏退出 → `tools/pbzsuite.ps1 -Mode Update`；不要热重载。
- 回滚顺序：pbzsuite → p2-probe → p1-loader。
- EventEngine 必须处理：InCombat 短暂归零、新 Pawn 首拍假血量、读图长间隔、非喝药回血。
