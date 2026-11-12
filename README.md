# 影之刃零 修改器（PBZ Trainer）

《影之刃零》（Phantom Blade Zero，Steam 版）的单机本地修改器。由两部分组成：

- **修改器**（`src/PBZManager`，WPF / .NET 10）：界面、规则、热键、音乐播放、安装与卸载游戏内模块。
- **游戏内模块 PBZSuite**（`src/PBZSuite`，UE4SS Lua）：读取游戏状态、执行数值锁定与物品修改，通过本地文件与修改器通信。

只修改本机进程中的单机游戏状态，不联网，不涉及他人。

**普通玩家请直接下载 [Releases](https://github.com/zhanshuguicai/pbz-trainer/releases) 里的 `PBZTrainer-1.0.1-win-x64.zip`（自带运行环境、加载器和一键安装脚本），然后照着 [使用教程](docs/使用教程.md) 一步步做。**

## 功能

| 页面 | 内容 |
|---|---|
| 状态 | 游戏位置与版本校验、加载器与模块状态、实时血量 / 战斗 / 药 / Boss、事件记录 |
| 条件播放器 | 22 种触发事件（开战、脱战、血量跌破/回升、一次掉血、喝药、药喝空、补药、阵亡、重生、被围攻、战斗持续、杀气满、绝技满/放出、影魂满/用完、Boss 战开始/结束、Boss 血量跌破、击败 Boss）；附加条件（战斗状态、血量上下限、敌人数、剩余药、Boss 战状态、Boss 血量）；淡出条件；每条规则可设多首歌（随机/轮流）、音量、开始秒数、循环方式、淡入淡出、冷却；导入导出规则；热键 |
| 数值修改 | 血量、杀气、绝技能量、影/魂点数：锁满或固定为指定值；实时显示当前/上限；系统级可改热键 |
| 物品 | 钱与强化 / 摄魂 / 养成材料的数量修改（通过游戏自身函数加减并回读），修改前自动备份存档；剧情物品等只读 |
| 玩家工具 | 游戏速度、移动速度、造成 / 受到伤害倍率、无限药、飞行（可穿墙）、记录位置与传送、等级与经验修改（试验）；热键 |
| 场景修复 | 坏档时打开一次性机关（例如戏梦山庄冷图密道的幻术挂画）：扫描附近机关，打开 / 溶解、移除或恢复；改动不进存档 |
| 战斗辅助 | 自动弹反 / 闪避：游戏提前约 0.3 秒通知来袭攻击，修改器在命中前按下真实的弹反键或闪避键（仅游戏在前台时）；提前量可调，显示受击统计 |

写入类功能默认关闭，每次启动修改器时都是关闭状态，关闭修改器时自动关闭；游戏版本未通过校验时一律停用。

## 兼容版本

| Steam build | PBZ.exe SHA-256 |
|---|---|
| 26266538 | `7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4` |
| 26367565 | `4415933CE33476F14C837ABB61A1B60AF8B78CD81783896501913A7AAC69E423` |

游戏更新后修改器会自动停用写入功能，需重新验证后在 `PBZ.Core/Game/GameInstall.cs` 加入新版本。

## 需要

- Windows 10/11，[.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)
- 游戏目录中已安装 UE4SS（本项目使用 UE4SS 3.0.1 + PR #1484 的 PBZ 配置）。仓库不包含 UE4SS 二进制；发布包里带有加载器和 `安装加载器.bat`。从源码使用时，把加载器文件放在 `release/loader/` 再运行 `release/install-loader.ps1`（自动在 Steam 库中找游戏）。
- 从源码构建需要 .NET 10 SDK；发布包自带运行环境，不需要另装。

## 构建与运行

```bash
dotnet build src/PBZManager/PBZManager.sln
```

```bash
dotnet test src/PBZManager/PBZ.Core.Tests
```

运行 `src/PBZManager/PBZManager/bin/Debug/net10.0-windows/PBZManager.exe`，在“状态”页点“安装 / 更新”把 PBZSuite 装进游戏（游戏需关闭）。修改器的设置保存在 exe 旁的 `data/`。

## 撤销

1. 退出游戏，在修改器“状态”页点“卸载”（运行记录会归档到 `data/archive`）。
2. 如需移除加载器：`powershell -ExecutionPolicy Bypass -File release/install-loader.ps1 -Mode Rollback`。

## 文档

- 架构：[ADR 0001](docs/adr/0001-architecture.md)、[ADR 0002](docs/adr/0002-integrated-trainer.md)
- 产品规则（每条注明来源）：[docs/decisions](docs/decisions/2026-11-03-product-rules.md)
- 游戏数据读法：[docs/p2-field-sources.md](docs/p2-field-sources.md)
- 开发记录：[docs/worklog](docs/worklog/)、当前状态：[HANDOFF.md](HANDOFF.md)
- 变更记录：[CHANGELOG.md](CHANGELOG.md)

## 声明

非官方工具，与游戏开发商无关。仅用于单机游戏；使用前请自行备份存档（物品修改前修改器会自动备份一次）。
