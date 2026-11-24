# 修复老兵（NPC）外观显示为腐肉团

## 1. 问题描述
盟重省老兵（Merchant NPC）头顶名字已正确显示"老兵"，但角色外观是一团暗红色腐肉/肉块，而不是人形 NPC。周围怪物（鸡、鹿）外观正常。

## 2. 根因（已用代码证据确认）

**服务端 `Merchant.txt` 给老兵配置的 `Appr=41`，与该客户端 `NPC.wil` 的外观索引体系不匹配，导致客户端从 `NPC.wil` 取到了错误的图（腐肉团）。**

### 完整链路证据
1. **服务端配置**（`bin\Release\Envir\Merchant.txt`）：
   ```
   老兵 3 325 333 老兵 0 41 0 0 0
   ; 注释写 "41=老兵 42=书店 43=铁匠 44=杂货铺 45=药店 46=酒馆"
   ```
   格式第 7 字段 = 外观（Appr），由 [LocalDB.cs:458](file:///D:/sources/github3/OpenMir2/src/GameSrv/DB/LocalDB.cs#L458) 读入：`Appr = (ushort)HUtil32.StrToInt(sAppr, 0)`。

2. **服务端 Feature 计算**：
   - `Merchant` 构造函数设 `RaceImg = ActorRace.Merchant`（=50，[Race.cs:28](file:///D:/sources/github3/OpenMir2/src/OpenMir2/Enums/Race.cs#L28)），`Appr` 来自配置 = 41。
   - `GetFeature`（[BaseObject.Base.cs:909](file:///D:/sources/github3/OpenMir2/src/M2Server/Actor/BaseObject.Base.cs#L909)）= `MakeMonsterFeature(RaceImg=50, MonsterWeapon=0, Appr=41)` = `MakeLong(MakeWord(50,0), 41)`（[M2Share.cs:1961](file:///D:/sources/github3/OpenMir2/src/M2Server/M2Share.cs#L1961)）。

3. **客户端解析**（Feature 字节布局正确）：
   - `RACEfeature` = Feature 低字节 = 50 → [PlayScn.pas:3268](file:///D:/sources/github3/MirClient/Source/MirClient/PlayScn.pas#L3268) `case RACEfeature of 50: Actor := TNpcActor.Create` ✓（创建的是 NPC 类，正确）。
   - `APPRfeature` = Feature 高字 = 41 → [PlayScn.pas:3394](file:///D:/sources/github3/MirClient/Source/MirClient/PlayScn.pas#L3394) `m_wAppearance := APPRfeature(cfeature)`。

4. **取图偏移错误**（根因落点）：
   - `TNpcActor.CalcActorFrame`（[Actor.pas:3701](file:///D:/sources/github3/MirClient/Source/MirClient/Actor.pas#L3701)）：`m_nBodyOffset := GetNpcOffset(m_wAppearance)`。
   - `GetNpcOffset(41)`（[Actor.pas:1559](file:///D:/sources/github3/MirClient/Source/MirClient/Actor.pas#L1559)）命中 `33..41: Result := (nAppr - 26) * 60 + 1620` → **(41−26)×60+1620 = 2520**。
   - `TNpcActor.LoadSurface`（[Actor.pas:3982-3992](file:///D:/sources/github3/MirClient/Source/MirClient/Actor.pas#L3982)）：`m_btRace=50` 且 Appr=41 不在 `[100,130)`/`[130,131]` → 走 else → `m_BodySurface := g_WNpcImgImages.GetCachedImage(2520 + 当前帧, ...)` → **从 NPC.wil 取索引 2520 的图 = 腐肉团**。

### 旁证（Merchant.txt 的 Appr 编号不适用于此客户端）
客户端 `TNpcActor.LoadSurface`（[Actor.pas:3996](file:///D:/sources/github3/MirClient/Source/MirClient/Actor.pas#L3996)）对 `m_wAppearance in [42..47, 54..58]` 直接设 `m_BodySurface := nil`（不可见）。而 Merchant.txt 注释把书店/铁匠/杂货铺/药店/酒馆配成 42–46——这些值在该客户端会让 NPC **完全不可见**。说明文件里这套 41–46 的 Appr 编号体系与当前客户端不匹配。

### 排除：与本次 CharDesc 修复无关
- `Feature` 是 `CharDesc` 首字段，在末尾加 `StatusEx` 不改变 Feature 的 4 字节位置与值。
- `StatusEx` 仅在 `SM_HEALTHSPELLCHANGED` 用于 `Actor.m_Abil.MaxHP`（[ClMain.pas:10225](file:///D:/sources/github3/MirClient/Source/MirClient/ClMain.pas#L10225)），不参与取图。
- 故老兵肉团是**预先存在的配置/资源不匹配**，非名字修复引入的回归。

## 3. 待确认的关键点（决定修复方向）
客户端 `.wil` 资源不在工作区（`d:\sources\github3` 下无任何 `.wil`）。需确认两件事以选定正确修复：

1. **客户端数据目录路径**：运行时 `NPC.wil`/`Monster*.wil` 所在目录（Delphi 客户端加载资源的位置）。
2. **索引 2520 的实际内容**：用 WIL 查看工具打开客户端 `NPC.wil`，确认索引 2520 是否就是腐肉团；并在 `NPC.wil` 中找到老兵应有的人形图所在索引。

基于以上两种结果，修复分两种情形：
- **情形 A（Appr 编号错）**：NPC.wil 中存在正确的老兵人形图，只是 Appr=41 映射错了。→ 修正 `Merchant.txt` 中各 NPC 的 Appr（必要时同步 `Npcs.txt`）。
- **情形 B（资源缺失/版本错）**：NPC.wil 本身版本不对或缺失老兵图。→ 需获取与该客户端匹配的 `NPC.wil` 资源，改配置无效。

## 4. 修复方案（默认按情形 A，找到正确 Appr 后改配置）

**目标文件**：
- `D:\sources\github3\OpenMir2\src\GameSrv\Envir\Merchant.txt`（源码，提交用）
- `D:\sources\github3\OpenMir2\src\GameSrv\bin\Release\Envir\Merchant.txt`（运行时，实际生效）

**步骤**：
1. 拿到客户端 `NPC.wil`，用 WIL 工具定位老兵人形图的起始索引 `N`。
2. 反查 `GetNpcOffset`（[Actor.pas:1551-1576](file:///D:/sources/github3/MirClient/Source/MirClient/Actor.pas#L1551)），找到满足 `GetNpcOffset(Appr) == N` 且 `Appr` 不落在 `LoadSurface` 不可见区间 `[42..47, 54..58]` 的 Appr 值，即为老兵的正确 Appr。
3. 同理确定书店/铁匠/杂货铺/药店/酒馆的正确 Appr（若它们也显示异常）。
4. 更新 `Merchant.txt` 两行（源码 + 运行时）中各 NPC 的第 7 字段；同步检查 `Npcs.txt`（比奇省，当前全部 Appr=42，42 属不可见区间，几乎肯定也错）。
5. 重启 GameSrv 验证。

> 若步骤 1/2 确认是情形 B（资源问题），则停止改配置，转而提示用户补齐正确的 `NPC.wil` 资源。

## 5. 验证步骤
1. 重启 GameSrv（后台任务方式，避免进程被回收）。
2. 客户端进盟重省，确认老兵显示为人形 NPC（不再是腐肉团）。
3. 确认书店/铁匠/杂货铺/药店/酒馆（如可见）外观正常、名字正常。
4. 确认怪物（鸡/鹿）外观无回归。
5. 检查 gamesrv 日志无新增异常。

## 6. 不做的事
- 不改客户端代码（取图逻辑 `GetNpcOffset`/`LoadSurface` 是客户端原版逻辑，服务端应适配它，而非反向）。
- 不改 `CharDesc`（已确认与本问题无关）。
- 不改数据库（NPC 外观来自本地 Merchant.txt，非 DB）。
