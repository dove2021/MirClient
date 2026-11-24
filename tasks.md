# 任务清单：修复老兵（NPC）外观显示为腐肉团

## 根因（已确认）
服务端 `Merchant.txt` 给老兵配 `Appr=41`，客户端 `GetNpcOffset(41)=2520`，从 `NPC.wil` 取索引 2520 的图 = 腐肉团。该 Appr 编号体系与当前客户端不匹配（旁证：客户端把 Appr 42–47 设为不可见，而配置里书店/铁匠等正是 42–46）。与 CharDesc 修复无关（Feature 字节未变，StatusEx 不参与取图）。

- [x] T1. 定位服务端 Feature 来源：`Merchant.txt` Appr=41 + `RaceImg=50` → `GetFeature`=MakeMonsterFeature(50,0,41)
- [x] T2. 定位客户端取图链路：`RACEfeature=50`→TNpcActor；`GetNpcOffset(41)=2520`→NPC.wil 索引 2520=腐肉团
- [x] T3. 排除 CharDesc 修复回归：Feature 首字段位置未变，StatusEx 仅用于 MaxHP，不影响取图
- [ ] T4. 获取客户端数据目录（含 NPC.wil），用 WIL 工具确认索引 2520=腐肉团，并找到老兵人形图的正确起始索引
- [ ] T5. 反查 `GetNpcOffset` 得到老兵（及书店/铁匠/杂货铺/药店/酒馆）的正确 Appr；判定是情形 A（改配置）还是情形 B（补资源）
- [ ] T6. 情形 A：更新 `src\GameSrv\Envir\Merchant.txt` 与 `bin\Release\Envir\Merchant.txt`（同步检查 `Npcs.txt`）
- [ ] T7. 重启 GameSrv，客户端验证老兵/各 NPC 外观与名字正常、怪物无回归
- [ ] T8. 情形 B：提示用户补齐匹配的 NPC.wil 资源，不改配置
