# T7 条件播放器 + 方向调整为集成修改器
- 日期：2026-11-03
- 执行者：Claude Code，claude-opus-5-5，单人
- 来源：用户"不如把这做成一个高度集成的修改器……"；选择题回答：能量点数=绝技能量条+影/魂点数，锁满，顺序 播放器→锁定→物品→弹反

## 目标
记录新方向（ADR 0002、任务表、规则 11–14），并交付第一项：可自定义条件与歌曲的条件播放器。

## 做了什么（大体流程，按顺序）
1. 写 docs/adr/0002-integrated-trainer.md、重写 docs/p4-tasks.md、产品规则补 11–14；补 T4d 日志。
2. EventEngine 改为只产出原子事件：BattleStarted、BattleEnded、HealthBelow（多阈值）、FlasksEmpty、Death（Dead 标签或血量归零，每条命一次）、Respawn（代数 >0 后的新 Pawn）。"每场一次"与优先级移出引擎。
3. 新 ConditionPlayer（规则：条件、阈值、歌曲、优先级、每场一次/每次、脱战淡出）；MusicSettings 改为规则表并自动迁移 T4d 格式；两条旧规则作为默认规则。
4. 回放发现：硬仗里血量跌破 50% 两次（467 s、535 s 回血后再掉）——引擎如实报告，默认规则仍只放一次 aizo；首次进入存档（代数 0→1）不算重生。
5. 界面改为分页：状态、条件播放器（可编辑规则表、试听、恢复默认）、锁定/物品/战斗辅助占位页；窗口标题改为"影之刃零 修改器"；--snapshot 增加 --tab。

## 改动（文件或产物；提交号，如有）
- 新增 docs/adr/0002-integrated-trainer.md、PBZ.Core/Music/ConditionPlayer.cs（原 MusicDirector.cs）、PBZManager/RuleRow.cs、PBZ.Core.Tests/ConditionPlayerTests.cs。
- 修改 EventEngine.cs、MusicSettings.cs、WpfAudioOutput.cs、MainWindow.xaml(.cs)、App.xaml.cs、EventEngineRuleTests.cs、ReplayTests.cs、p4-tasks.md、产品规则。
- 用户 settings.json 已迁移为规则格式（两条规则、音量 0.39 为用户此前调整的值）。非 Git 仓库，无提交。

## 验证（跑了什么、结果；没验证的写"未验证"和原因）
- 构建 0 警告 0 错误；测试 53/53（引擎边界 24、规则层 11、回放 6、解析/跟读 6、安装/配置 6）。
- 回放：默认规则在 T4a 录制上输出"play half50 → fade"，与 T4d 行为一致；2 次 Respawn；无 Death。
- 界面截图：两页渲染正常，状态页实时显示战斗中（交战计数 8）并记录"第 1 场战斗开始"。
- 未验证：新条件（开战、脱战、阵亡、重生、多阈值）的实战播放；Death 依赖的 CharacterState.Dead 标签从未在实测中出现过（只在单元测试里用）。

## 状态
PASS（代码与回放层面）；新条件的实战效果待用户试用。

## 未完成与下一步
- T8 锁定：先写探针确认血量/杀气/绝技能量/影魂点数的写法和界面刷新。
- 用户实战试用新条件，尤其阵亡。

## 给接手者的提醒（坑、约定、别碰的地方）
- 规则层的"每场一次"只对战斗内事件生效（BattleId>0）；战斗外事件每次都放。
- "脱战"条件只认 reason=left combat；读图、阵亡导致的战斗结束不触发它。
- Respawn 在 _generation>0 时才报；首次进存档不算。
