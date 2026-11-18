# PunchConstruct 0.5.1 待办（修订）

> **汇总台账（2026-11-18）**：全部问题（审计 21 项复核判定 + #27/#28 + 发布记录多层不一致 + 死代码/QA 重构 + 设计待确认 + 护栏）已汇总到 [`PunchConstruct-问题汇总台账.md`](PunchConstruct-问题汇总台账.md)，作为修复批次单一权威清单。
> **本文件 §§2/4/5 已过时**（仍写最终 DLL `702f587a…`、ZIP `0c36fc7d…`/51,343,686、私测「暂缓」、公开「未许可」）。实际：候选 **858f76de**、ZIP **2915f84b…/51,345,112**、私测已重传、公开 0.5.1 已上线（作者同意）。这些将在台账 §A 记录清理中统一同步；§1/§6 的作者已确认决策保持不变。

> 目标：收尾 0.5.1 发布前的确认项；不再改多人事件门槛，不做重复遗物防御。  
> 修订依据：2026-11-15 评审结论与作者确认。  
> 已确认：
> - **被寄生的自动机械（InfestedAutomaton）保持当前门槛**：全构装体队伍才走 mod 门槛；混编队伍保留原生 `IsAllowed`。不改 `ConstructInfestedAutomatonAllowed`。
> - **齿轮工艺品可以重复持有**：已有齿轮再获得一个就有 2 个，各自触发，开局 2 层人工制品；事件触发条件保持现状，不做事件内重复防御。
> - **PunchOff 只做文本分流**，不改选项/动作/奖励。
> - **核心电涌保持 11→15（+4）**；1 代就是 +4，旧稿的 11→14 是事实错误，不修改。
> - **拳手炖汤保持 `TargetType.Self`**；所有构造药水只影响自己。
> - **共享遗物池的混编公平性问题**放到 0.5.2，不在 0.5.1 动 RNG/共享袋。
> - **左勾拳削弱**：基础版仍为 6 伤害／4 格挡但去掉保留；升级版仍为 6 伤害／4 格挡并获得保留；右勾拳不变。
> - **录像命令 `pcdeck all|changes`** 只用于调试录制：直接替换牌组与遗物，跳过“获得时”效果；不进入正式玩法说明。

---

## 1. 已关闭（不修改）

- [x] 被寄生的自动机械：不改全队门槛；保留当前混编行为。
- [x] 重复齿轮工艺品：允许持有 2 个，不补防御。
- [x] 核心电涌+：保持 +4，不改。
- [x] 拳手炖汤：保持 `TargetType.Self`。
- [x] PunchOff：当前已是纯文本分流，不改行为。

---

## 2. 发布前必须确认

- [x] 在最终 DLL `702f587a…` 上重跑本地 35/35、联机 4/4（含 4p 弱网）、断线恢复 4/4；`full_local_verification=true`、不变量 16/16。旧候选（`d22fa5b4…`、`e3819999…`、`0c404043…`）均已被取代，不再作为发布候选。
- [x] 用最终 DLL 重建候选三文件（DLL `702f587a…`、PCK `d67662e5…`、JSON `030ce557…`，277 份资源）、归档 `dist/releases/0.5.1/`、重建 ZIP（`PunchConstruct-0.5.1.zip`，51,343,686 字节，SHA256 `0c36fc7d…`），更新 `verification.json` 与 `content-current.json`；`workshop.py --verify` 全 PASS。
- [ ] 重新上传私有测试项 **3835179658**，并核对 Steam 回下载三文件哈希。**暂缓**：本次 Steam 未运行，作者指示先做文档；暂存就绪，待 Steam 可用后按最终 DLL 重传。
- [ ] 作者在 0.113.1 实机确认：只启用测试项 **3835179658**，公开项 **3826907330** 保持禁用；确认启动日志 `[PunchConstruct] v0.5.1.0 loaded (game v0.113.1 / BaseLib 3.4.8.0; per-hook patches + relic variants)` 和基本玩法。（隔离预检 startup/smoke/additions 140/pool 已在最终 DLL 上 PASS，实机待作者确认。）
- [ ] 作者明确许可公开发布 0.5.1；未许可前公开项 3826907330 保持 0.4.28。
- [ ] 公开发布前核对 0.4.28 三件套哈希不变。
- [ ] 公开发布前确认公开项当前仍是 0.4.28，私有测试项是 0.5.1。
- [ ] 公开发布后 Steam 回下载哈希与候选一致，启动正常。
- [x] 更新 `verification.json`、`content-current.json`、`资料/发布前清单.txt`、`dist/README.md`、`dist/SteamWorkshop/发布审查.txt` 等发布记录（本轮文档同步）。

---

## 3. 0.5.2 待办（不在 0.5.1 动）

- [ ] **共享遗物池**：`BridgeConstructRelicVariants` 不再全局 `SharedRelicGrabBag.Remove(original)`；改为按玩家过滤 `RelicGrabBag` 抽取，混编时原版队友仍能抽到原生遗物。
- [ ] **混编 2p 事件 QA**：被寄生的自动机械 + PunchOff，断言构装体/原版各自文本与选项，互不影响。
- [ ] **齿轮工艺品获取渠道断言**：普通遗物奖励/宝箱不出，只允许商店与事件。
- [ ] **发条机关真实抽牌链**：覆盖“抽到状态牌→抽 1→再抽到状态牌”。
- [ ] **霸无敌跨回合/存读档**：2p、存读档、跨回合，确认延迟结算的 `CardPlay` 仍有效；若失败，改存 card+数值或加存档字段。
- [ ] **PunchOff 选项不变 QA**：断言 `ConstructPunchOffText.Targets()` 只含本地化方法，不含 `GenerateInitialOptions`。
- [ ] **其他 P2 审计**：真实 RitsuLib + RandomForeseer smoke、QA 程序集分离、可移植构建路径、多人版本一致性、`ASSETS.md`。

---

## 4. 已完成

- [x] **NativeProbe 签名修复**：`Player.CreateForNewRun(...)` + `StartNewSingleplayerRun(..., GameMode.Standard, false)`；`dotnet build tools/NativeProbe/NativeProbe.csproj -c Release` 0 warning / 0 error。
- [x] `python tools/check_native.py --release-pools` PASS；证据 `tools/runtime/qa/native-events-20261115-195447-699056500/`。
- [x] **左勾拳削弱**：`LeftHook` 基础版改为 `[Exhaust]`、去掉 `Retain`；升级版 `OnUpgrade()` 增加 `Retain`，伤害／格挡仍为 6／4；右勾拳生成与保留不变。`BoxingChecks` 已断言基础/升级差异。
- [x] **录像命令 `pcdeck all|changes`**：`ShowcaseConsoleCmd` 直接替换牌组与遗物，跳过 `AfterObtained`；`all` 为 88 张卡／22 件遗物，`changes` 为 12 张卡／7 件遗物；战斗外可用，联机走主机权威。`ShowcaseChecks` 断言两组数量、无获取升级、无尼奥护符升级，并断言命令已注册到游戏控制台。
- [x] **录像脚本**：`资料/录像脚本/README.md`、`01-全部内容.txt`、`02-0.5.1改动.txt` 已写入；脚本只含 `pcdeck all` / `pcdeck changes` 一行命令及核对说明。
- [x] 最终 DLL `702f587a…` 构建 0 警告／0 错误；在任务清单改动（机械飞升 3 费、霸无敌重写、能量图标、三张交接新美术与裁剪）合入后重建，`additions` PASS，证据 `tools/runtime/qa/additions-20261117-130720-962641900`。
- [x] 最终 DLL 的本地 35/35、联机 4/4（含 4p 弱网）、断线恢复 4/4 重跑全通过；`workshop.py --verify` 全 PASS。私有测试项 3835179658 重传**暂缓**（Steam 未运行，暂存就绪）。
- [x] 0.113.1 隔离预检已在最终 DLL 上复核：startup/smoke/additions 140/pool 全 PASS（证据 `tools/runtime/*-0.113.1-preview.json`）；官方 35/35 仍在 0.113.0 QA 环境。
- [x] 文档同步与 `check_documents.py` 10/10（最终哈希更新后重跑）。

---

## 5. 发布护栏

- **0.4.28 已发布并冻结**：不重建、不重传、不修改 `dist/releases/0.4.28`、公开项 3826907330 和 `dist/SteamWorkshop/content`。
- **公开上传/创建 Workshop 条目必须作者明确许可**；私有测试只使用 3835179658。
- **官方 QA 仍是 0.113.0 / BaseLib 3.4.8**；0.113.1 只做预检和作者实机确认。
- 不 `git add -A`；仓库根目录可能有含 SteamID 的日志压缩包。
- 不改 workshop 日志命令，直到作者明确要求。
- 汤只改文案“所有未附魔的小斩波”，不改代码。
- 不仓促改 `ConstructRelicRolls` RNG 架构。
- 公开措辞用“不会再导致整个模组加载失败、补丁失效”，不要写“完全兼容”。
- **任何 `PunchConstruct/*.cs` 改动（包括 QA）都会改变 DLL 哈希**，必须重建、重跑 35/35 + 4/4 + 4/4、重传私有测试、重跑 `workshop.py --verify`、更新 ZIP/SHA256/`verification.json`/`content-current.json`。
- **本次重建后以新归档候选为准**：旧 0.5.1 归档（DLL `d22fa5b4…`／`e3819999…`／`0c404043…`）均已作废，不再上传。最终三文件为 DLL `702f587a…`、PCK `d67662e5…`、JSON `030ce557…`（277 份资源）；35/35 + 4/4 + 4/4 全通过后已覆盖 `dist/SteamWorkshop/content`、重建 `dist/releases/0.5.1/` 归档与 ZIP（`PunchConstruct-0.5.1.zip`，51,343,686 字节，SHA256 `0c36fc7d…`，三文件位于根目录、与 content 逐字节一致）。公开发布时只使用最终归档，不再重新 build 覆盖候选。

---

## 6. 作者实机反馈待修（2026-11-17 记录，2026-11-18 排查完成；任务 #27／#28）

> 作者在私密测试项 3835179658 实机试玩反馈。2026-11-18 已反编译原版 0.113.1 `sts2.dll` 排查清楚根因与可复用机制（见下），**尚未改代码**。任何 `.cs` 改动须与下一批合并重建并重跑 35/35 + 4/4 + 4/4、重传私有测试、`workshop.py --verify`、更新 ZIP/SHA256/`verification.json`/`content-current.json`。公开 0.5.1 已发布，这两项归入下一版（0.5.2）。

- [ ] **#27 被寄生的自动机械 SEARCH 预览升级不一致**（根因已确认）：第二选项预览显示敲过（升级）的万能药，实际获得未升级基础版。`WaveEvents.cs:181` 预览用 `HoverTipFactory.FromCard<ConstructPanacea>(true)`，但 `Search()`（`WaveEvents.cs:194`）`CreateCard<ConstructPanacea>` 后无 `CardCmd.Upgrade` → 获得基础版。**修复**：第 181 行 `(true)`→`(false)`。**勿改第 99 行** `FromCard<ScorchingIronWave>(true)`——那是 Amalgamator 的 COMBINE_DEFENDS（`Reforge`→`ScorchingReplacement(source,1)` 确实升级 1 次），预览正确。补 QA：断言 SEARCH 预览卡未升级、与实际获得一致。
- [ ] **#28 古牙对构装体泄漏原版先古映射**（根因已确认，作者决策已定）：构装体持中和（Neutralize）等原版初始卡时，古牙显示「获得压制（Suppress）」，错误。
  - **根因**：`AncientAdaptations.cs:69-76` `AncientWaveMapping.Apply` 在构装体场景**复制原版六项映射再追加** TonyWave→霸无敌（`new Dictionary(__result){ [TonyWave]=ConstructDomineering }`）。原版 `ArchaicTooth.TranscendenceUpgrades` getter 返回六项（Bash→Break、Neutralize→Suppress、Unleash→Protector、FallingStar→MeteorShower、Dualcast→Quadcast、FoolishShot→DivineShot）；`GetTranscendenceStarterCard`＝牌组里第一张命中映射键的卡，故持中和即命中→显示压制。
  - **作者决策（2026-11-18）**：古牙要有出现条件——**构装体没有东尼斩波（TonyWave）就不出现**，复用原版机制。
  - **可复用机制（已反编译确认）**：原版 `ArchaicTooth.SetupForPlayer(player)` 在 `GetTranscendenceStarterCard` 返回 null 时返回 false（注释原文 "Returns false if the player doesn't have the base starter card to transcend"）。古牙作为选项出现的唯一门槛在 **Orobas 先古事件 `OptionPool3`**：`if (archaicTooth.SetupForPlayer(base.Owner)) list.Add(RelicOption(archaicTooth))`，false 则不加入（全空时显示 OPTION_POOL_3_LOCKED）。构装体常规遗物池 `ConstructRelicPool` 不含古牙；`EventRelicPool` 仅池归属；`RelicAdaptations.cs:105` 只是描述文本本地化键，均非出现路径。
  - **修复方向**：`AncientWaveMapping.Apply` 在 `ConstructSetup` 时把字典**替换为仅** `{ TonyWave→ConstructDomineering }`（剔除原版六项），而非复制追加。如此构装体无东尼斩波→`GetTranscendenceStarterCard` 返回 null→`SetupForPlayer` false→Orobas 不提供古牙（正好复用原版门槛）；有东尼斩波→出现并转化为霸无敌。`ScopedAncientToothTransformation`（只 redirect TonyWave）与 `DomineeringReplacement` 不变。
  - **副作用核查**：`DustyTome.SetupForPlayer` 用 `ArchaicTooth.TranscendenceCards` 排除转化卡，但它在 `ConstructSetup=false` 时调用该 getter（`ConstructSetup` 仅在 `ArchaicTooth.SetupForPlayer`/`GetTranscendenceStarterCard` 前缀置位），取原版六项，不受本修复影响。
  - **QA 缺口（须补）**：① 构装体持中和等原版起始卡但**无东尼斩波**→`SetupForPlayer` 返回 false、Orobas `OptionPool3` 不含古牙；② 有东尼斩波→true 且 `AncientCard==ConstructDomineering`（`DraftChecks.cs:115-131` 已覆盖正例）；③ 原版角色（如铁甲战士持 Bash）仍 Bash→Break，不受影响（回归断言）。
  - **QA 不一致（须一并处理）**：`PlaytestRunner.cs:1472-1475` 仍是**过时断言**——期望古牙把 IronWave→ScorchingIronWave（升 3 次），与当前 TonyWave→霸无敌设计矛盾（`DraftChecks.cs:121` 才期望霸无敌）。修复时更新或移除该过时断言，确认 PlaytestRunner 是否仍在 35 项套件内。
