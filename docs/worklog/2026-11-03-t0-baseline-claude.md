# T0 基线复核与产品规则确认
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人（中枢兼执行；用户未要求派工，未调用 Codex）
- 来源：用户"开始吧"，并逐条回答 6 项待确认规则

## 目标
只读复核接手时的游戏与项目状态，建立 EXE hash 基线；记录用户确认的产品规则；定位音频文件；检查工具链。

## 做了什么（大体流程，按顺序）
1. 读完整规划、HANDOFF、最近校正日志。
2. 按规划第 6 节只读核对：进程、EXE 版本/长度/SHA-256、appmanifest buildid、loader 残留（含常见代理 dll 名）、UE4SS 包 hash、PR #1484 三文件 hash。
3. 在用户下载目录查找音频：`C:/Users/38365/Downloads` 无音频；在 `E:/Downloads` 找到两首目标曲目，读取时长与码率并记录 hash。
4. 检查 Python、.NET、git、cmake、MSVC、Lua。
5. 写入产品规则决定文件与 T0 证据。

## 改动（文件或产物；提交号，如有）
- 新增 [产品规则确认](../decisions/2026-11-03-product-rules.md)
- 新增 [T0 基线证据](../../evidence/t0-baseline-20261103.json)
- 本日志；更新 HANDOFF.md。项目不是 Git 仓库，无提交。
- 未改动游戏目录、存档、音频文件。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- 无 PBZ 进程；buildid 26266538 = TargetBuildID；EXE 版本 CL-301524_JKS-17219，长度 511072344，与历史一致。
- 新 EXE 基线 SHA-256 `7B4FCD28…09973B4`（历史无此基线，首次记录）。
- 游戏 bin 目录只有 D3D12/、PBZ.exe、steam_api64.dll、tbbmalloc.dll；无任何 loader 残留。
- UE4SS 包与 PR 三文件 SHA-256 均与既有记录一致。
- 工具链：Python 3.14.7；.NET SDK 10.0.401（无 8.x SDK，有 8/9/10 桌面运行时）；无 cmake、无 PATH 上的 MSVC。
- 未重跑静态签名脚本：EXE 版本/长度未变，且现在有了 hash 基线，按规划不必重做。

## 状态
PASS：基线与历史一致，可进入 P1；产品规则已定。

## 未完成与下一步
- T1/P1：新备份存档 → 新安装清单 → 安装 UE4SS + PR 三配置（默认 mods 全禁用）→ 由用户经 Steam 启动 → 看反射初始化 → 正常退出。写入游戏目录与启动游戏前需用户逐步同意。
- P3 时决定管理器目标框架：本机只有 .NET 10 SDK，可直接用 net10.0-windows，或用 10 SDK 编 net8。

## 给接手者的提醒（坑、约定、别碰的地方）
- 音频在 `E:/Downloads`，不是 C 盘下载目录；AIZO.flac 疑似不完整，雨爱 flac 为 .crdownload，均不用。
- 规则 3"初始 0 不触发"是 Claude 对用户"是的"的解读，已在决定文件中标注。
