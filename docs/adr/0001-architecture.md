# ADR 0001：管理器 + 游戏内套件的整体架构

- 日期：2026-11-03
- 状态：已采纳（Claude 中枢决定；用户授权"继续 P3"）
- 依据：[三字段数据来源](../p2-field-sources.md)、[产品规则](../decisions/2026-11-03-product-rules.md)、[完整规划](../影之刃零Mod_Claude完整交接与实施规划_20261103.md) 第 9–11 节

## 背景

- 游戏侧只能通过 UE4SS Lua 读写游戏对象；Lua 不方便播放 mp3、没有可用的测试运行器（本机无 Lua 解释器）。
- 管理器侧本机已有 .NET SDK 10.0.401 与 Windows 桌面运行时；2026-11-03 实测 `dotnet new wpf` 可编译，WPF `MediaPlayer` 能解码两首目标 mp3（3:35.9 / 3:45.9），不需要 NuGet 包。
- UE4SS 已知坑：采样循环内全局搜索会崩（P2 v1），热重载会崩（联机 mod 笔记）。

## 决定

### 1. 两个进程，一个游戏内套件

```
PBZ.exe ── UE4SS ── Mods/PBZSuite (Lua)            PBZManager.exe (.NET 10 WPF)
                     ├ telemetry   采样三字段 ──写──▶ runtime/telemetry-*.jsonl ──读──▶ EventEngine ─▶ AudioPlayer
                     ├ parry       Mod 1                                                  │
                     ├ items       Mod 3       ◀──读── runtime/config.json ◀──写── 设置/开关/热键
                     └ bridge      命令/结果   ◀──读── runtime/commands/*.json ◀─写── 物品编辑请求
                                                ──写──▶ runtime/results.jsonl ──读──▶ 界面
```

- 游戏内只装一个 UE4SS mod `PBZSuite`，三个功能是它的模块，由 `config.json` 里的开关在运行中启停，不靠增删 mod 文件夹，也就不需要重启或热重载。
- 管理器负责：安装/卸载 PBZSuite（按清单，沿用 P1/P2 脚本的规则）、配置、播放音乐、物品编辑界面、诊断。

### 2. 通信用本地文件，不开端口、不用命名管道

- 目录：`<游戏>/platform/steam/ue4ss/Mods/PBZSuite/runtime/`（纯 ASCII 路径，Lua `io.open` 可用）。
- 游戏 → 管理器：只追加的 JSON Lines。每次游戏启动新开 `telemetry-<sessionId>.jsonl`；超过 8 MB 换新文件。管理器按字节偏移读新行，只处理完整行。
- 管理器 → 游戏：`config.json` 由管理器写临时文件再 `File.Replace` 替换；Lua 每 1 秒读一次，比较 `revision` 字段。物品命令每条一个文件 `commands/<requestId>.json`，Lua 处理后删除并在 `results.jsonl` 写结果。
- 理由：Lua 侧零依赖、不阻塞游戏线程（只做小文件读写）；崩溃任一方不会卡住另一方；全部内容可直接作为证据存档。不选命名管道：Lua 只能同步读写，管理器不在时会阻塞或报错。不选回环 TCP：UE4SS Lua 无 socket 库，且需要额外开放端口。
- 代价：延迟约 200–400 ms（采样 200 ms + 管理器轮询 100 ms），对播放音乐可以接受。

### 3. 事件判定放在管理器（C#），游戏侧只上报快照

- Lua telemetry 每 200 ms（`LoopInGameThreadWithDelay`）采一次：血量、最大血量、InCombat、Dead、两个药槽 ID 与剩余次数、Pawn 地址代数；任何一项读不到就写 `null` 并带 `valid=false`，不复用旧值。
- 快照 schema 沿用规划第 8 节，`schemaVersion=1`，含 `sessionId`、`sequence`、`monotonicMs`、`gameBuild`、`playerGeneration`。
- C# `EventEngine` 是纯函数式状态机：输入快照序列，输出 `BattleStarted / BattleEnded / HalfHealth / FlasksEmpty` 事件。规则逐条对应产品规则 2、2a、3、3b、4：
  - 战斗：InCombat 计数 > 0 连续 ≥ 2 个有效样本 → 新 battleId；= 0 连续 ≥ 3 秒 → 脱战。未知状态不当作脱战。
  - 半血：仅在本场战斗中，上一有效样本比值 > 0.5 且当前 ≤ 0.5 时触发一次。入战首样本已 ≤ 0.5 不触发。
  - 喝空：两槽合计从 > 0 变为 0 时触发一次；入战首样本已为 0 不触发；任一槽 unknown 时暂停判断。
  - 同一样本同时满足两者：只播雨爱 DJ。
- 理由：C# 可以用 P2 录下的真实日志做回放单元测试（`evidence/p2/run2-*.log` 转成快照）；规则改动不需要动游戏内文件。
- 音乐由管理器的单一 `AudioPlayer` 播放（WPF `MediaPlayer`，支持停止、切曲、音量、淡入淡出）；找不到文件只报错，不影响其他功能。管理器没开时没有音乐，这是已接受的限制。

### 4. Mod 1、Mod 3 的逻辑在游戏内执行

- 自动弹反需要帧级时机，必须在 Lua 里完成；热键 `F7` 由 Lua `RegisterKeyBind` 处理，管理器只改配置里的热键与开关。实现前需要单独的只读探针阶段（敌人攻击阶段、红/蓝分类、可用的弹反输入路径），见任务 T5b。
- 物品修改在 Lua 里执行：先用只读枚举生成"强化/养成材料 + 货币"分类表；写入优先找游戏自身的库存函数（如 `AddPropsToBackPack`、`AddDustNum`），直接改 `CurrentAmount` 只作为验证过持久化后的备选。每次写前备份存档，写后回读。见任务 T5a。

### 5. 默认值与安全

- 所有模块默认关闭，界面显示的"已启用"以游戏侧在 telemetry 里回报的状态为准。
- 游戏版本（EXE SHA-256 / buildid）与适配记录不符时，Lua 只启用 telemetry，写入类模块拒绝执行并上报原因。
- 命令白名单：只接受已实现的命令类型和受限参数，不接受任意 Lua、地址或函数名。

## 不做的事

- 不在 Lua 里播放音频或启动外部进程。
- 不监听网络端口。
- 不把音频文件复制进项目或安装包，管理器只保存用户选的路径。

## 后果

- P4 可以先做"telemetry + 管理器 + 音乐"这条线并完整验收，再分别推进 Mod 3 与 Mod 1 的调查。
- 本机没有 Lua 解释器，Lua 代码只能在游戏里验证；因此 Lua 侧保持精简，复杂逻辑尽量放 C#。
