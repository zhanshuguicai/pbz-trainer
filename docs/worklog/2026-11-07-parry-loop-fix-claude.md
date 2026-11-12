# 修复 1.0.2 自动弹反循环未启动；修改器显示游戏内模块脚本错误
- 日期：2026-11-07
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户截图"一直显示等待游戏应用"
- 接续：[自动弹反 1.0.2](2026-11-07-parry-timing-types-claude.md)、[优先级](2026-11-07-music-priority-claude.md)

## 目标
查清 1.0.2 装好后自动弹反一直"等待游戏应用"的原因并修复；让这类问题以后在界面上直接可见。

## 做了什么（大体流程，按顺序）
1. 发现用户实际运行的是桌面副本（`C:\Users\38365\Desktop\net10.0-windows`，1.0.1），已把 1.0.2 覆盖过去（保留 data，旧程序备份在会话临时目录），用户随后装了 PBZSuite 1.0.2。
2. UE4SS.log：`main.lua:796: No overload found for function 'LoopInGameThreadWithDelay'`。1.0.2 重写弹反段时删掉了 `PARRY_TICK_MS`，传入 nil；主代码块在此中断，之前注册的遥测/配置循环照常运行，所以只有弹反"等待游戏应用"。恢复常量（16 ms），并扫描其余全大写常量均有定义。
3. 新增 `PBZ.Core/Suite/SuiteLogCheck.cs`：读 UE4SS.log 末尾，找最近一次 "Mod 'PBZSuite' ... starting mod." 之后的 PBZSuite 脚本错误；修改器每 5 s 检查一次，有错就在状态页"连接"一行红字提示并写 events.log。测试 2 个（用真实错误文本）。

## 改动（文件或产物；提交号，如有）
- `src/PBZSuite/Scripts/main.lua`、新增 `PBZ.Core/Suite/SuiteLogCheck.cs`、`PBZManager/MainWindow.xaml.cs`、新增 `PBZ.Core.Tests/SuiteLogCheckTests.cs`。未提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 105/105；构建 0 错误。
- 未验证：修复后的游戏内运行（用户需关游戏和修改器 → 覆盖桌面副本 → 安装 → 实战）。

## 状态
PARTIAL：已修，待安装实测。

## 未完成与下一步
- 覆盖桌面副本、安装 PBZSuite 1.0.2（修复版）、实战。

## 给接手者的提醒（坑、约定、别碰的地方）
- **Lua 主代码块中途报错时，前面注册的循环仍在运行**，界面看起来"半好"。每次装新版后先看状态页有没有红字，或搜 UE4SS.log 的 "Error executing script"。
- 本机没有 Lua 解释器，改 main.lua 后至少检查：删掉的常量/函数是否还被引用。
