# Boss 修复实测通过；界面按《影之刃零》风格重做；1.0.1 发布到 GitHub
- 日期：2026-11-04
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"没问题了，最后一部是我希望ni能按照影之刃0的美术风格优化我们修改器的ui，改完就可以1.01版提交github了"；问答：仓库"公开"，内容"只放代码和文档"
- 接续：[音乐 1.0 扩充](2026-11-04-music-boss-rich-claude.md)

## 目标
记录 Boss 修复结果；重做界面风格；整理仓库并发布 1.0.1。

## 做了什么（大体流程，按顺序）
1. Boss：两次失败后用只读探针（`evidence/boss/round3-probe-UE4SS.log`）查明 Boss 血条是 HUD 内的 `WB_BossHPBar_C`（`Character`、`BossName`），`RegisterBossHandles` 恒为空；PBZSuite 改为读该控件，重编安装后用户实测"没问题了"。另加"游戏内模块是旧版本"提示（遥测 suiteVersion 与 payload 的 SUITE_VERSION 比较）。
2. 界面主题（`App.xaml` 全量重写）：墨黑底 #121011、面板 #1A1717、宣纸色文字 #E9E2D3、刀光红 #C3202F；标题华文行楷、分组与标签页楷体、正文雅黑；顶部横幅（图标、标题、红色渐隐笔触、版本）、底部状态栏；自定义模板：分组框（红色竖标）、按钮、勾选框（红底勾）、输入框、下拉框（含弹出层）、标签页（红色下划线）、表格、滚动条、提示框。对话框同步深色。
3. 修正：标签页字体被页面内容继承（改为只作用于标签头）；根容器背景；下拉框选中项显示 "PressKey { … }"（PressKey、Choice 重写 ToString）；表格内下拉列垂直居中；热键注册失败颜色改用主题色。
4. 版本 1.0.1（修改器、PBZSuite）。新增 README.md、CHANGELOG.md、.gitignore（排除 bin/obj、backups、evidence、UE4SS 二进制与安装包、release/loader、运行输出）。敏感信息检查：无令牌/密码/邮箱；两份早期文档含本机用户名路径，保留。
5. git 初始化、提交、创建公开仓库并推送（结果见下）。

## 改动（文件或产物；提交号，如有）
- `PBZManager/App.xaml`（主题）、`MainWindow.xaml(.cs)`（横幅、状态栏、版本提示）、`ConditionsDialog.cs`、`PlaybackDialog.cs`、`RuleRow.cs`、csproj（1.0.1、app.png 资源）
- `PBZ.Core/Telemetry/Snapshot.cs`（SuiteVersion）、`PBZ.Core/Combat/ParryCommand.cs`（ToString）
- `src/PBZSuite/Scripts/main.lua`（1.0.1，Boss 读血条控件）
- 新增 README.md、CHANGELOG.md、.gitignore；文档：HANDOFF。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet test` 94/94；预览构建 0 错误；五个页面截图核对。
- Boss：用户实测通过（名字、血量、规则触发、淡出）。
- 未验证：正式 exe 输出目录未重编（用户的修改器开着，预览构建在临时目录完成）；PBZSuite 1.0.1 未装进游戏（只改了版本号，逻辑与已实测的版本相同）。

## 状态
PASS（功能与界面）；发布见 HANDOFF。

## 未完成与下一步
- 用户关修改器后重编正式输出，安装 PBZSuite 1.0.1。
- 可选：GitHub Release 附打包好的修改器（需单独确认）；为仓库选许可证（未替用户选择）。

## 给接手者的提醒（坑、约定、别碰的地方）
- WPF：TabItem 上设置的字体和前景色会被页面内容继承，标签样式只能放在标签头的 ContentPresenter 上。
- 自定义 ComboBox 模板里选中项不走 DisplayMemberPath，绑定对象需要合适的 ToString。
- 仓库不含 UE4SS 与加载器文件，安装加载器需自备 `release/loader/`。
