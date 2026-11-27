# 玩家跑路卡住并被弹回 — 修复规格

## 1. 现象
玩家跑步/走路移动时，经常出现短暂"卡住"，随后角色被"弹回"到上一个位置。复现频率高，正常跑路即可触发。

## 2. 根因（已由代码证据确认）

弹回链路：
1. 客户端预测移动，先发 `CM_RUN`/`CM_WALK` 再本地改坐标。
2. **GameGate 丢弃了部分移动消息** → 服务端漏处理 → 服务端权威坐标 `CurrX/Y` 落后于客户端。
3. 客户端下一包请求目标 `nX/nY`，服务端 `RunTo` 按方向移动 2 格后 `CurrX != nX`（[PlayObject.Attack.cs:1322-1327](file:///d:/sources/github3/OpenMir2/src/M2Server/Player/PlayObject.Attack.cs)）→ `ClientRunXY` 返回 `false` 且 `delayTime==0` → 服务端发 `RM_MOVEFAIL`（[PlayObject.Message.cs:1059-1116](file:///d:/sources/github3/OpenMir2/src/M2Server/Player/PlayObject.Message.cs)）。
4. 客户端收到 `RM_MOVEFAIL` → `TActor.MoveFail`（[Actor.pas:3581-3589](file:///d:/sources/github3/MirClient/Source/MirClient/Actor.pas)）回滚到 `m_nOldx/m_nOldy` 并清空消息队列 → 表现为"弹回"。
5. 被 GameGate 延迟（而非丢弃）的消息造成短暂"卡住"。

GameGate 丢包的具体原因：
- 配置 `MoveInterval=570`、`IsMoveInterval=1`、`OverSpeedPunishMethod=DelaySend`（[GameGate config.conf](file:///d:/sources/github3/OpenMir2/src/GameGate/bin/Release/config.conf)）。
- 客户端跑步发包间隔约 500ms（`g_dwLastMoveTick`，[ClMain.pas](file:///d:/sources/github3/MirClient/Source/MirClient/ClMain.pas) 多处 `> 500` 判断，2227 行发包后更新），**500 < 570 → 正常跑步每包都被判为过快**。
- [ClientSession.cs:243-263](file:///d:/sources/github3/OpenMir2/src/GameGate/Services/ClientSession.cs)：过快且延迟队列已有消息（`delayMsgCount>=1`）时 `return` 丢弃；惩罚把 `MoveTick` 推后到未来，正反馈加速丢包。

## 3. 修复方案（分层，低风险优先）

### 改动 1（主修复）：放宽 GameGate 移动间隔过滤
- 文件：`d:\sources\github3\OpenMir2\src\GameGate\bin\Release\config.conf`
- 将 `[Integer] MoveInterval` 从 `570` 调整为 `480`（略低于客户端 ~500ms 发包间隔，留出帧率波动余量，避免正常跑步被误判过快）。
- 防加速不丢失：服务端 `RunIntervalTime=600` 仍在服务端做最终限速（过快走 `SendSelfDelayMsg` 延迟重发，不弹回、不放行），GameGate 仅为前置过滤。
- 不重新编译 GameGate（改配置即可），重启 GameGate 生效。

### 改动 2（诊断验证，临时）：在服务端弹回分支加坐标日志
- 文件：`d:\sources\github3\OpenMir2\src\M2Server\Player\PlayObject.Attack.cs`
- 在 `ClientRunXY` / `ClientWalkXY` 返回 `false` 且 `delayTime==0`（即会发 `RM_MOVEFAIL`）的分支，输出 `LogService.Info`：角色名、`CurrX/Y`、请求的 `nX/nY`、坐标差、`MoveTick` 间隔、`SpaceMoved`。
- 用途：部署后让用户复现，确认弹回时坐标差是否为 2 格（=漏处理一次移动），验证根因。
- 验证通过后：保留为 `LogService.Debug` 或移除（由用户决定，默认降级保留）。
- 需重新编译 `GameSrv`。

### 改动 3（可选，仅当改动 1+2 验证后仍频繁弹回时）：服务端对坐标脱节做容忍
- 位置：`ClientRunXY`/`ClientWalkXY` 中 `RunTo` 成功但 `CurrX != nX` 的分支。
- 不发 `RM_MOVEFAIL` 弹回，改为按服务端权威位置向客户端发送位置同步（如 `SM_TURN` 或现有位置修正消息），使客户端平滑纠正而非硬回滚。
- 风险较高（影响移动权威模型），**必须在改动 1+2 数据确认后再实施**，默认不做。

## 4. 验证步骤
1. 确认运行中 GameSrv 的 `RunIntervalTime` 实际值（读其 `config.conf`，应为 600 或自定义值，确保服务端兜底限速存在）。
2. 应用改动 1（改 GameGate config.conf MoveInterval=480）→ 停 GameGate → 后台重启 GameGate。
3. 应用改动 2 → 停 GameSrv → 编译 `GameSrv\GameSrv.csproj -c Release` → 后台重启 GameSrv。
4. 让用户进入游戏连续跑步 2~3 分钟，观察是否还卡住/弹回。
5. 查看 GameSrv 诊断日志：统计弹回次数及坐标差分布。
6. 判定：弹回基本消失且坐标差日志不再出现 → 根因确认，完成；若仍出现，分析日志后决定是否实施改动 3。

## 5. 风险与回滚
- 改动 1：`MoveInterval` 降低 → 前置过滤变松，但服务端 `RunIntervalTime=600` 兜底，正常玩家不受影响；加速挂仍被服务端限速。回滚：改回 570 重启 GameGate。
- 改动 2：仅加日志，无行为变化；编译失败回滚 Edit。
- 改动 3：默认不实施。

## 6. 待批准决策
- 是否同意先实施改动 1（MoveInterval=480）+ 改动 2（诊断日志），部署后由你复现验证？
- 改动 2 的日志验证通过后，是降级为 Debug 保留还是直接移除？
