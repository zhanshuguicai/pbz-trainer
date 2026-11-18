# 2.0.1：music 文件夹、默认设置进发布包、README
- 日期：2026-11-18
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"我想在文件夹里增加一个《music》文件夹，把播放器里的音乐放在里面；然后把我们本地目前的设置和歌曲作为默认设置加入release包里，在更新一下readme"

## 目标
歌曲集中到程序旁的 music 文件夹；本地当前设置作为发布包默认设置；更新 README；出新发布包。

## 做了什么（大体流程，按顺序）
1. 新增 `PBZ.Core/Music/MusicLibrary.cs`：Resolve（相对路径转完整路径）、Import（复制进 music，同名同内容复用，同名不同内容加 " (2)"）、Adopt（把规则里的歌全部收进 music）、Missing（列出缺的歌）。
2. 修改器：启动时 Adopt；选歌、播放设置、导入规则都走 Import；播放时 Resolve；缺歌时状态栏列出文件名。
3. `DataLocation.Prepare`：首次运行时从 exe 旁 `defaults/` 补齐 settings.json、lock.json；已有 data 的用户不受影响，旧版 AppData 迁移优先于默认设置。
4. `defaults/` 取自桌面副本当前的 settings.json、lock.json，歌曲路径改为 `music\...`。`music/README.txt` 说明要放哪两个文件。
5. 版本 2.0.1（修改器）；PBZSuite 未改，仍为 2.0.0，用户不必重装游戏内模块。
6. 更新 README、使用教程、CHANGELOG、产品规则第 25 条；.gitignore 排除 music 下的音频。
7. 更新桌面副本，打包 `PBZTrainer-2.0.1-win-x64.zip`，发布 Release v2.0.1。

## 与用户要求不同的地方
**歌曲没有放进发布包。** 两首歌（King Gnu《AIZO》、DJ 阿黛《雨爱》）是有版权的商业音乐，仓库和 Release 公开，上传等于公开分发。发布包只带默认规则和空的 music 文件夹；歌曲留在用户本机桌面副本的 music 文件夹。

## 改动（文件或产物；提交号，如有）
- 新增 `MusicLibrary.cs`、`MusicLibraryTests.cs`、`PBZManager/defaults/*`、`PBZManager/music/README.txt`
- 修改 `DataLocation.cs`、`MusicSettings.cs`、`MainWindow.xaml(.cs)`、`PlaybackDialog.cs`、`WpfAudioOutput.cs`、`PBZManager.csproj`、`.gitignore`、README、CHANGELOG、使用教程、产品规则
- 提交与标签 v2.0.1 见 git log

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- `dotnet build` 0 错误；`dotnet test` 123/123（新增 6 个；其中一个检查发布的默认文件不含本机路径，首次就拦下了替换失败）。
- 桌面副本：启动后两首歌进入 music，与原文件 cmp 一致，settings.json 变为相对路径；E:\Downloads 原文件保留。替换前的 settings.json 备份在会话临时目录。
- 发布包：解压中文名正常，38 个文件校验和一致，音频文件 0 个，defaults/music/README/CHANGELOG 不含本机路径。
- 未验证：干净电脑上首次运行显示 4 条默认规则。本机有旧版 AppData 数据，迁移优先，冒烟测试显示的是旧的 2 条规则；默认设置逻辑只由单元测试覆盖。
- 未验证：2.0.0 的玩家工具、场景修复仍未在游戏内实测；歌曲实际播放未试听。

## 状态
PARTIAL：功能和发布完成；歌曲按版权原因未进发布包，游戏内实测待用户。

## 未完成与下一步
- 用户在桌面副本里点"试听选中"确认能播放。
- 用户确认自动弹反的"连段按住"是否要保留为默认（见下）。

## 给接手者的提醒（坑、约定、别碰的地方）
- 默认设置里 `parryComboHold` 为 true，照搬用户当前设置；它是 2026-11-07 截图测试时被勾上的试验选项，不一定是用户本意。
- 不要把 music 下的音频提交到仓库或打进公开发布包。
- Bash 里用 sed 替换带反斜杠的 JSON 不可靠，用脚本文件。
