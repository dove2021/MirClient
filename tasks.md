# 任务清单 — 跑路卡住/弹回修复

## 阶段 0：确认运行时配置（验证项，不改代码）
- [ ] 读取运行中 GameSrv 的 config.conf，确认 `RunIntervalTime` / `HumanGetMsgTime` 实际值（预期 600 / 200），确认服务端兜底限速存在。
- [ ] 读取运行中 GameGate config.conf，确认 `MoveInterval=570` / `IsMoveInterval=1` / `OverSpeedPunishMethod=3` 当前生效。

## 阶段 1：主修复（改动 1）
- [ ] 停掉 GameGate 后台服务（StopCommand）。
- [ ] 编辑 `GameGate\bin\Release\config.conf`：`[Integer] MoveInterval` 570 → 480。
- [ ] 后台重启 GameGate，确认端口 7300 监听、无异常。

## 阶段 2：诊断日志（改动 2）
- [ ] 停掉 GameSrv 后台服务（DLL 解锁）。
- [ ] 在 `PlayObject.Attack.cs` 的 `ClientRunXY`、`ClientWalkXY` 中，返回 false 且 delayTime==0 的分支加 `LogService.Info` 诊断日志（角色名、CurrX/Y、nX/nY、差值、MoveTick 间隔、SpaceMoved）。
- [ ] 编译 `GameSrv\GameSrv.csproj -c Release`，确认成功。
- [ ] 后台重启 GameSrv，确认端口 5000 监听、无启动异常。

## 阶段 3：用户验证
- [ ] 通知用户进游戏连续跑步 2~3 分钟复现。
- [ ] 收集 GameSrv 诊断日志，统计弹回次数与坐标差。
- [ ] 判定根因是否确认（坐标差是否为 2 格的漏处理）。

## 阶段 4：收尾
- [ ] 若根因确认且弹回消失：将改动 2 日志降级为 `LogService.Debug` 或移除（按用户决策）。
- [ ] 若仍频繁弹回：分析日志，评估是否实施改动 3（服务端位置容忍）。
- [ ] 向用户汇报结果。
