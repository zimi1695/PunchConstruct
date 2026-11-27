# PunchConstruct 问题汇总台账（0.5.1 记录清理 + 0.5.2 修复）

> 建立：2026-11-18。作者指示「把全部的问题汇总起来，先汇总记录，准备修复」。
> 本台账是本轮修复批次的**单一权威清单**，汇总：第三方 AI 审计 21 项的复核判定、作者实机 bug #27/#28、发布记录多层不一致、既有死代码/QA 重构待办、设计意图待确认项、外部阻塞与安全护栏。
> 状态：**汇总记录已完成**——新建本台账，并在 `PunchConstruct-0.5.1-待办.md` 顶部加交叉引用横幅（标注 §§2/4/5 过时）。**§F 设计意图已获作者定夺（2026-11-18）**：F1 保持 fail-open（改判 P1，不改行为）、F2 挑战模式故意出全量原版池（非问题）、F3 完全不管不加告警（非问题）、F4 权重流 RNG **必须与原版联机随机数同步方式完全一致**（作者强调：偏离原版会让随机数预测/roll 种器等第三方模组混乱失效）。F4 原版模型已探索并记录于 **§I**，实现暂缓待批准。**尚未改任何代码，也尚未修正陈旧记录值本身**（upload_status/哈希/公开状态等待 §A 执行）。修复须按 §H 顺序，并遵守 §G 护栏。
> 交叉引用：`PunchConstruct-0.5.1-待办.md`（§§2/4/5 已过时，待 §A 同步）、任务 #23/#25/#26/#27/#28/#29/#30/#31/#32。

---

## 0. 权威事实（锚定，避免再被陈旧副本误导）

- **当前发布候选 = DLL `858f76ded9b34c91b0449f7e67fbbe1b080eaf1ccbe3bd7183a417180a715dec`**（PCK `d67662e5…`、JSON `030ce557…`，277 份资源）。`dist/SteamWorkshop/content` = `bin/Release/net9.0` = `dist/releases/0.5.1/` 三者一致。
- **旧候选 `702f587a…` 已被取代**（Blitz/Dread 适配前的版本）；`d22fa5b4…`/`e3819999…`/`0c404043…` 更早作废。
- **陈旧副本（不是候选，勿引用）**：`dist/PunchConstruct/PunchConstruct.dll` = `e3819999…`；`bin/Debug` = `ee5b4e72…`。审计 P0-5 就是被这两份误导。
- **ZIP 实际值**：`dist/releases/PunchConstruct-0.5.1.zip` = **51,345,112 字节**，SHA256 **`2915f84b5eb1c37cc4aee241376eff75f322c17d248b4ed867596c12ff793234`**。（多份文档仍写旧值 51,343,686 / `0c36fc7d…`，见 A6。）
- **私测项 3835179658**：已用 858f76de 重传，2026-11-17T20:10:25+08:00，回下载哈希 PASS（`tools/runtime/test-workshop-0.5.1.json`）。
- **公开项 3826907330**：已公开上传 0.5.1，2026-11-17T21:59:27+08:00，visibility=public，回下载哈希=858f76de，作者明确同意，#27/#28 记为已知问题，passed=true（`tools/runtime/public-workshop-0.5.1.json`）。**这是完整发布记录的权威来源。**
- **QA 证据链（P0-5 纠错用）**：`dist/SteamWorkshop/verification.json`（QA 全过：4p-network :346 passed、host-loss :631 passed，generated 19:09）+ `content-current.json:50`（解释两项首跑失败已重跑通过）+ `test-workshop-0.5.1.json` + `public-workshop-0.5.1.json`。
  - **`tools/runtime/current-verification.json` 是首跑快照，不是全过证据**：failed_multiplayer=[coop-construct-4p-network]、failed_recovery=[recovery-construct-host-loss-mixed-3p-reverse]、full_local/full_release/full_invariant 均 false、不变量 14/2。两项失败为引擎内部竞态（NCard 重复回收 / ScriptManagerBridge 重载），重跑通过，非 mod 逻辑。
  - `dist/SteamWorkshop/verification.json` 只证明 QA 全过，其 `upload_status` 仍 NOT_UPLOADED，**不能当完整发布记录**。

---

## A. 发布记录不一致（0.5.1 已公开，记录滞后；纯模板/JSON/文档，**不碰 DLL、零哈希风险**）

> 根因比「忘了刷新」更深：**QA 报告模板源头硬编码旧基线**，重跑也会再生成错误覆盖说明。至少四层记录互相矛盾。对应任务 #23 + #29。

| 编号 | 位置 | 现状（错/旧） | 应改为 | 备注 |
|---|---|---|---|---|
| A1 | `tools/invariants.json:206` | coverage 写「游戏 0.111.0、BaseLib 3.4.7」 | 官方 QA 基线 **0.113.0 / BaseLib 3.4.8** | 模板源头；`report_local.py:236` `coverage=ledger['coverage']` 原样复制 |
| A2 | `tools/invariants.json:212` | 「82 张卡、4 件工坊遗物、10 个状态图标」 | 按**当前池**刷新（卡牌数已增长；精确值取 `ModelDb.CardPool<ConstructCardPool>().AllCards`，勿沿用旧稿 88/98） | 旧计数；状态图标数亦需核对 |
| A3 | `tools/release.py:55` | `'game':'0.111.0','baselib':'3.4.7'` | 0.113.0 / 3.4.8 | 另一处硬编码旧基线 |
| A4 | `tools/runtime/current-verification.json` | 首跑快照：2 项失败、三聚合标志 false、不变量 14/2、coverage 旧值 | **二选一**：① 用重跑结果重新生成（host-loss、4p-network 重跑已通过）；② 显式标注「首跑快照，不作为发布记录」并指向 dist 验收 | 先修 A1–A3 模板，否则重生成仍带旧 coverage |
| A5 | `dist/SteamWorkshop/verification.json` | `upload_status: NOT_UPLOADED` | 反映公开已上传（UPLOADED_PUBLIC），引用 public-workshop-0.5.1.json | 该文件由 workshop.py --verify 生成；**护栏：不改 workshop 日志命令**，更新记录字段或经作者许可重跑 verify |
| A6 | 6 份文档 ZIP 哈希/大小 | `0c36fc7d…` / 51,343,686 | **`2915f84b…` / 51,345,112** | 待办.md §2/§5、发布审查.txt、发布前清单.txt、问题记录.txt、实现任务书.md、兼容性台账.md |
| A7 | `PunchConstruct-0.5.1-待办.md` §§2/4/5 | 最终 DLL `702f587a…`；all=88 卡；私测「暂缓」；公开「未许可前保持 0.4.28」 | DLL **858f76de**；all 按当前池；私测已重传 20:10；公开已上传 21:59（作者同意） | 标注 702f587a 已取代；保留作者已确认决策（§1/§6）不动 |
| A8 | `README.md` / `dist/README.md` / 发布前清单 / 待办 | 「公开版本 0.4.28 / 私测待重传」 | 公开 0.5.1 已上线、私测 3835179658 已重传 | 多处 |
| A9 | `dist/SteamWorkshop/发布审查.txt` | 候选 DLL `702f587a…` | 858f76de | |
| A10 | `content-current.json:52-56` + `:50` validation | `uploaded:false`/`uploaded_at:null`/`workshop_item:null`/`workshop_visibility:null`；upload_evidence 与 validation 仍写「公开项仍是 0.4.28、公开 0.5.1 待作者同意」 | `uploaded:true`、`uploaded_at:2026-11-17T21:59:27+08:00`、`workshop_item:"3826907330"`、`workshop_visibility:"public"`；新增 public_upload 块引用 public-workshop-0.5.1.json；validation 文本同步 | 与 public-workshop-0.5.1.json 直接矛盾 |
| A11 | 陈旧副本 `dist/PunchConstruct`(e381999)、`bin/Debug`(ee5b4e) | 被审计误当候选 | 澄清用途或同步；**不删除作者产物**，bin/Debug 保留为调试构建 | 防止再被误引用 |

**权威记录保持不动**：`public-workshop-0.5.1.json`、`test-workshop-0.5.1.json` 已正确，作为同步目标值来源。

---

## B. 作者实机 bug（0.5.2；**需重建 + 35/35+4/4+4/4 + 重传私测 + verify + 更新记录**）

### B1 = #27 被寄生的自动机械 SEARCH 预览显示升级万能药
- **现象**：第二选项预览显示敲过（升级）的万能药，实际获得未升级基础版。
- **根因**：`WaveEvents.cs:181` 预览用 `HoverTipFactory.FromCard<ConstructPanacea>(true)`，但 `Search()`（`WaveEvents.cs:194`）`CreateCard<ConstructPanacea>` 后无 `CardCmd.Upgrade` → 获得基础版。
- **修复**：第 181 行 `(true)`→`(false)`。**勿改第 99 行** `FromCard<ScorchingIronWave>(true)`（Amalgamator COMBINE_DEFENDS 的 Reforge 确实升级 1 次，预览正确）。
- **QA**：断言 SEARCH 预览卡未升级、与实际获得一致。

### B2 = #28 古牙对构装体泄漏原版先古映射（中和→压制）
- **现象**：构装体持中和（Neutralize）等原版初始卡时，古牙显示「获得压制（Suppress）」，错误。
- **根因**：`AncientAdaptations.cs:69-76` `AncientWaveMapping.Apply` 在构装体场景**复制原版六项映射再追加** TonyWave→霸无敌。原版 `ArchaicTooth.TranscendenceUpgrades` 返回六项（Bash→Break、Neutralize→Suppress、Unleash→Protector、FallingStar→MeteorShower、Dualcast→Quadcast、FoolishShot→DivineShot）；`GetTranscendenceStarterCard`＝牌组首张命中键，故持中和即命中→显示压制。
- **作者决策（2026-11-18）**：古牙要有出现条件——**构装体没有东尼斩波（TonyWave）就不出现**，复用原版机制。
- **可复用机制（反编译已确认）**：`ArchaicTooth.SetupForPlayer(player)` 在 `GetTranscendenceStarterCard` 返回 null 时返回 false；古牙作为选项出现的唯一门槛在 **Orobas 先古事件 `OptionPool3`**：`if (archaicTooth.SetupForPlayer(base.Owner)) list.Add(...)`，false 则不加入。
- **修复**：`AncientWaveMapping.Apply` 在 `ConstructSetup` 时把字典**替换为仅** `{ TonyWave→ConstructDomineering }`（剔除原版六项），而非复制追加。无东尼→GetTranscendenceStarterCard null→SetupForPlayer false→Orobas 不提供古牙；有东尼→出现并转化为霸无敌。`ScopedAncientToothTransformation`、`DomineeringReplacement` 不变。
- **副作用核查**：`DustyTome.SetupForPlayer` 在 `ConstructSetup=false` 时取原版六项，不受本修复影响。
- **QA（须补三条）**：① 构装体持中和等但**无东尼**→SetupForPlayer false、Orobas OptionPool3 不含古牙；② 有东尼→true 且 AncientCard==ConstructDomineering（`DraftChecks.cs:115-131` 已覆盖正例）；③ 原版角色（铁甲持 Bash）仍 Bash→Break（回归）。
- **QA 不一致（一并处理）**：`PlaytestRunner.cs:1472-1475` 仍是过时断言（期望 IronWave→ScorchingIronWave 升 3 次），与当前设计矛盾；更新或移除，并确认 PlaytestRunner 是否仍在 35 项套件内。

---

## C. 稳健性加固（0.5.2；审计成立项，按价值/成本排序）

> 均为真实健壮性缺口，非发布阻断项。任何 .cs 改动触发 §G 重建管线。

| 编号 | 审计项 | 位置 | 判定 | 修复方案 | 优先 |
|---|---|---|---|---|---|
| C1 | P0-1 | `PatchRegistry.cs:105-108` | 成立：`Patch()` 后不查 `GetPatchedMethods()` 非空就 applied++；名字过滤型 TargetMethods 改名后静默空打 | 每类安装后查非空；Required 类零目标→fail-closed（记失败/RequiredFailure） | 高（便宜） |
| C2 | P2-20 | `ModEntry.cs:17` + `CombatRelicState.cs:17-25` | 成立且切中方针：空 Initialize() 触发静态构造→BaseLib `CopyOnClone()`，在 PatchRegistry 隔离之外；BaseLib 一改→TypeInitializationException→整 mod 加载失败 | Initialize() 包 try/catch + 降级标志；失败不冲出 ModEntry | 高（便宜） |
| C3 | P0-3 | `RelicAcquisition.cs:101/118` | 成立：硬编码 Ldloc_1，只校验替换点数量不校验局部类型；布局变→非法 IL→JIT 抛 InvalidProgramException（Guard 接不住） | **最小守卫**：先验 `LocalVariables[1].LocalType==typeof(Player)`，不符返回原 IL。**可选大改**：用 `Hook.ShouldGenerateTreasure`（Hook.cs:2566）替代 transpiler，但须覆盖 Blitz 两次额外 Pull（TreasureRoomRelicSynchronizer.cs:119-136），非一行替换 | 高（守卫）/中（大改） |
| C4 | P2-15 | `CardAcquisition.cs:118-119` | 成立：Targets 只按方法名匹配重载，与 RelicAcquisition.cs 的 RelicFactoryScope 按参数过滤不一致 | 加参数类型/数量过滤 | 中（便宜） |
| C5 | P1-6 | `RelicAcquisition.cs:48/62` | 成立：`GetAvailableDeque` 反射 null→Invoke NRE→Guard 回退→原生遗物漏入 | 启动预检 `Available!=null` + 可见降级标志 | 中 |
| C6 | P2-14 | `PresentationBridge.cs:123-130` | 成立：私有字段注入 `____hitCount/____attackerSfx/____tmpAttackerSfx`；Optional 可降级 | 启动查字段存在 + 日志一次 | 中 |
| C7 | P2-13 | `AdaptationRegistry.cs:29/34-36` | 成立（理论）：Disabled HashSet + .Any() 非线程安全；游戏逻辑单线程低风险 | ConcurrentDictionary 或 lock | 低 |
| C8 | P1-7 | `AdaptedRelicVariants.cs:112-113` | 成立但已缓解：仅「构装体+已适配类型」才抓 StackTrace（:69 门槛）；建议的 RelicCmd.Obtain<T> 前缀撞「Harmony 不能补丁开放泛型」 | 需绕开开放泛型限制（可补丁的闭合包装设 [ThreadStatic] 标志）或缓存/限界；低优先 | 低（需设计） |
| C9 | P1-10 | `FacingChecks.cs:116` 等 | 成立（架构 smell）：QA 编译进正式 DLL，DelayedFacingProbe 正式环境也装（Enabled=false 时廉价空返回）；与 #26 重合 | QA 独立程序集 / `#if QA` / 无 `--punchconstruct-` 参数不装 | 中（=#26） |
| C10 | P1-12 | `ShowcaseConsoleCmd.cs:85-88` | 成立但有护栏：ModCards 依赖美术前缀，QA 断言 ==26 漂移会响亮失败 | 显式白名单 / [Showcase] 标记 | 低 |
| C11 | P2-16 | `CharacterScope.cs:44-54` | 已缓解：Owner 改名→null 时已有 RelicPreviewContext 回退 + IsUnowned + 保守原生路径 | 三态（已知/未知/无主）细化，锦上添花 | 低 |
| C12 | 构思 | 全局 | 值得采纳：兼容性清单 + 启动预检 | 把 AccessTools/私有字段/IL 假设集中成表，一行报告，关键项缺失 fail-closed；系统性覆盖 C1/C2/C3/C5/C6 | 中（伞项） |
| C13 | 构思 | 记录 | 值得采纳：候选哈希台账 | 单一映射 候选 DLL/PCK/JSON/ZIP 哈希 → QA 证据 → 上传记录；防止 P0-5 式陈旧副本混淆；并入 #23 | 中 |
| C14 | P0-4 | `RelicAcquisition.cs:52-54/71` | **已探索（§I）**：种子已对齐原版（`Rng(player,角色Id,盐)`＝run种子+槽位+Id哈希+mixin），且不推进原生流；分歧在**私有 CWT 流不持久化（存读档随 Player 重建重置 counter+state）、不随原版联机同步（rejoin 可致各端分歧）** | 作者定夺：**必须与原版同步方式完全一致**（F4）。方向＝把权重流做成原版式「持久化+主机同步」流（镜像 PlayerRngSet/SerializablePlayer.Rng）；**禁止**改用消耗原生流的方案。护栏：不仓促改架构；实现暂缓待批准，详见 §I | 暂缓（已探索） |

---

## D. 死代码清理 / QA 重构 / 既有 0.5.2 待办

- **D1 = #25**：移除 `GuardTemporaryStrength.cs` 死代码 + `ConstructPowerIcons.cs:16` brass_nut + `EnglishText.Generated.cs:37/183`；改写 CoopChecks/StatusTooltipChecks/PlaytestRunner/SmokeCheck/EnglishChecks 断言**古铜之拳增量活力（Vigor）**。与下一批 .cs 重建合并。
- **D2 = #26**：QA 代码设计重构评估（与 C9/P1-10 重合）；输出结论供作者决策。
- **D3（待办 §3 既有 0.5.2 项）**：
  - 共享遗物池：`BridgeConstructRelicVariants` 不再全局 `SharedRelicGrabBag.Remove(original)`，改按玩家过滤抽取，混编时原版队友仍能抽到原生遗物。
  - 混编 2p 事件 QA：被寄生的自动机械 + PunchOff，断言构装体/原版各自文本与选项互不影响。
  - 齿轮工艺品获取渠道断言：普通遗物奖励/宝箱不出，只允许商店与事件。
  - 发条机关真实抽牌链：抽到状态牌→抽 1→再抽到状态牌。
  - 霸无敌跨回合/存读档：2p、存读档、跨回合，确认延迟结算 CardPlay 仍有效；失败则改存 card+数值或加存档字段。
  - PunchOff 选项不变 QA：`ConstructPunchOffText.Targets()` 只含本地化方法，不含 GenerateInitialOptions。
  - 其他 P2 审计：真实 RitsuLib + RandomForeseer smoke、可移植构建路径、多人版本一致性、ASSETS.md。

---

## E. 审计判定为「非问题 / 已缓解」（留档，**不修**，防止误改）

| 编号 | 审计项 | 判定与依据 |
|---|---|---|
| E1 | P0-5 | **事实错误**。当前候选 858f76de 有完整 QA（dist verification 全过 + content-current:50 重跑解释）；私测/公开均已上传 858f76de。审计被陈旧副本 e381999/ee5b4e 误导，且不知 0.5.1 已公开。正确证据链见 §0。 |
| E2 | P2-18 | **不是 bug**。`CardAcquisition.cs:100-101` Weight＝「牌组含组内任一卡→整组×2（含自己）」，与 `角色设计.md:42`、`兼容性台账.md:117`、发布前清单:12、问题记录:56「组内任一卡入牌组后整组权重×2」一致。审计「其他卡」前提错。 |
| E3 | P1-8 | 低/理论。变体 NeowsTalisman/PaelsClaw 在 AfterObtained 一次性升级/附魔，**无持久计数**；CopyState 4 字段对正常获取路径足够。审计「剩余次数/升级数会丢」例子错。 |
| E4 | P1-9 | 魔法数字是**故意护栏**。ShowcaseChecks.cs:29/71 计数正确（22/7/3、26/9/3）；改成从选择器派生＝同义反复，削弱测试。保留硬编码（可选日志派生值）。 |
| E5 | P1-11 | 部分错。pcdeck 用 AddRelicInternal 发 RelicObtained 事件，但**跳过 AfterObtained 拾取效果**（RelicCmd.Obtain:53 才调）；debug-only、开局战斗外、QA 断言无升级。可接受。 |
| E6 | P2-17 | 前提错。RelicWeights.Preferred 的 `ModelDb.GetId<T>()` 是 **mod 自己的类型**（OpenEndWrench/SpinningScrew/SpareKnuckles/EmergencyOilCan），游戏更新不会删；前 9 项已是字符串常量。低风险。 |

---

## F. 设计意图（作者已定夺 2026-11-18）

- **F1 = P0-2 → 作者决策：保持现状（甲）。** 运行期 Required 适配器失败继续 fail-open：悄悄禁用该 hook、退回原生、角色照常可选，只记 error 日志，**不加界面提示、不隐藏角色**。审计 P0-2 **改判 P1 可观测性/降级缺口**，不改行为。依据：失败按单 hook 隔离（`AdaptationRegistry.cs:12-16/63-65`，QA `CompatibilityChecks.cs:136-151` 断言），不崩溃、不波及同域其它 hook；`HideFromVanillaCharacterSelect`（`ConstructCharacter.cs:24`）只跟启动期 `PatchRegistry.RequiredFailure` 挂钩，运行期 Fail 不触发隐藏。**无代码改动。**
- **F2 = P2-19 → 作者决策：故意的（甲），非问题。** 挑战模式就是要出该角色**全量原版卡池**，不受「组内已有卡则整组权重×2」门槛约束。`CardAcquisition.cs:55/60-67/71-76` ChallengeAllows 在组门槛之前的顺序**保持不变**。与作者既有确认（文档整理前记录:1056）一致。**无代码改动。**
- **F3 = P2-21 → 作者决策：完全不管（乙），非问题。** `ModEntry.cs:31-34` 只读取+打印 gameVersion，**不加版本门禁、也不加告警**。未测试版本照常加载、角色照常可选。**无代码改动。**
- **F4 = P0-4 → 作者决策：必须与原版联机随机数同步方式完全一致（重要）。** 作者原话：「游戏原版联机的随机数怎么同步就怎么做，这个很重要，因为这个会影响到其他模组如随机数预测和 roll 种器的适配导致其模组完全混乱失效，所以必须尊重原版游戏。」要求**先探索然后记录**（已完成，见 **§I**）。护栏「不仓促改 ConstructRelicRolls RNG 架构」仍生效；实现归 0.5.2+，须按 §I 的 native-alignment 方向、经作者批准后合并重建。**本次只探索+记录，未改代码。**

---

## G. 外部阻塞 / 安全 / 护栏

- **G1 = #30 GitHub 完整发布**：✅ **已完成（2026-11-18）**。main `72277cd` 已推送（195 文件，+4939/−1196）、`v0.5.1` 标签已推送、Release（id 449470960）已创建并附 ZIP（`PunchConstruct-0.5.1.zip`，51,345,112 字节 / SHA256 `2915f84b…`，state=uploaded）。Release：https://github.com/zimi1695/PunchConstruct/releases/tag/v0.5.1 。排除 `tools/runtime-0.113.1/`（3.0 GB）、`tools/_refscratch/`；ZIP 不入仓库（.gitignore）改作 Release 附件；扫描无令牌/SteamID 混入。**凭据经 GCM（旧凭据失效→`git credential reject` 清除→重新认证后 push 成功），全程未使用作者贴出的 PAT 明文**；该 PAT 已在对话暴露，**仍须作者撤销**。
- **G2 Workshop 宣传图空壳（屡犯）**：每次上传后跑 `tools/verify_previews.py`；若空壳→先清空 previews/ 上传删除→再放回重传。已记入项目记忆 + tools/README.md + 两份发布文档横幅。
- **G3 美术授权 UNRESOLVED**：无 Watcher 授权（verification.json）；外部边界。
- **G4 外部验证边界 NOT_VERIFIED**：steam_invites / hot_rejoin / workshop_subscription；full_playthrough NOT_RUN。
- **G5 清理**：管线结束删除 `tools/_refscratch/`。
- **G6 护栏 recap**：
  - 0.4.28 已发布并冻结：不重建/不重传/不改 `dist/releases/0.4.28`；公开项 3826907330 已按作者同意更新到 0.5.1（0.4.28 归档仍冻结）。
  - 公开上传/创建条目须作者明确许可（0.5.1 已获许可）；私测只用 3835179658。
  - 官方 QA 仍 0.113.0 / BaseLib 3.4.8；0.113.1 只预检 + 作者实机确认。
  - **任何 PunchConstruct/*.cs 改动（含 QA）→ 改变 DLL 哈希 → 必须重建、重跑 35/35+4/4+4/4、重传私测、重跑 workshop.py --verify、更新 ZIP/SHA256/verification.json/content-current.json**。
  - 公开发布只用最终归档，不再重新 build 覆盖候选。
  - 不仓促改 ConstructRelicRolls RNG 架构。
  - 公开措辞用「不会再导致整个模组加载失败、补丁失效」，不写「完全兼容」。
  - 不改 workshop 日志命令，直到作者明确要求。

---

## H. 修复顺序（两轮复核共识）

1. **先清记录（§A，纯模板/JSON/文档，不碰 DLL）**：
   ① 修模板 A1–A3（invariants.json coverage + release.py 旧基线）→ ② 用重跑结果重生成 current-verification 或明确标注「首跑快照、不作发布记录」（A4）→ ③ 统一 upload_status / ZIP 哈希 / 公开已上线 到 verification.json、content-current、README、dist/README、待办、发布审查、发布前清单、问题记录、实现任务书、兼容性台账（A5–A10）→ ④ 澄清陈旧副本（A11）。= 任务 #23 + #29。
2. **0.5.2 代码批量（§B + §C 选项 + §D）**：作者点头后合并重建 → 35/35+4/4+4/4 → 重传私测 3835179658 → workshop --verify → 更新 ZIP/SHA256/verification.json/content-current.json → 作者许可后公开。
3. **§F 设计意图（已定夺 2026-11-18）**：F1/F2/F3 均保持现状、**无代码改动**（F1 改判 P1，F2/F3 判非问题）。F4 须按 **§I** 的 native-alignment 方向实现（权重流持久化+主机同步，镜像原版），归 0.5.2+、经作者批准后合并重建；护栏「不仓促改 RNG 架构」仍生效。
4. **§G1 GitHub**：✅ 已完成（main `72277cd` + `v0.5.1` tag + Release 附 ZIP，详见 §G1）。暴露的 PAT 仍待作者撤销。

> 下一步等作者拍板：是否先执行第 1 步记录清理（低风险、可立即完成）；代码批量（第 2 步，含 F4 native-alignment）按护栏需重建管线，待作者批准。

---

## I. F4 原版联机 RNG 同步探索记录（2026-11-18，只探索未改代码）

> 作者决策：权重流 RNG **必须与原版联机随机数同步方式完全一致**，否则随机数预测/roll 种器等依赖原生 RNG 序列的第三方模组会混乱失效。本节记录原版模型、模组现状、精确分歧、对齐方向与实现前待核实项。反编译来源 `C:/Users/li177/AppData/Local/Temp/sts2decomp/`（按命名空间分目录）。

### I.1 原版 RNG 模型（证据）

- **PRNG = Xoshiro256\*\***（`MegaRandom.cs:10/51`），Splitmix64 从 ulong 种子初始化（`:87-105`）。状态＝4 个 ulong `_s0.._s3`；纯确定性，无隐藏熵。
- **Rng 包装**（`Rng.cs`）：`_counter`（已消耗抽数）+ MegaRandom。构造器：
  - `Rng(ulong seed)`（:39）全新流；`Rng(SerializableRng)`（:44）恢复 counter+state（读档用）。
  - **`Rng(Player player, ModelId id, ulong mixin=0)`（:61-64）＝按内容派生**：种子 `player.RunState.Rng.Seed + GetPlayerSlotIndex(player) + GetDeterministicHashCode(id.Entry) + mixin`。用**槽位**而非 NetId（每日跑一致）。注释：同一 run 再见同一内容会返回相同结果，若可能重复须传 mixin。
  - `Rng(ulong seed, string name)`（:66）＝seed + name 确定性哈希。
  - `ToSerializable()`（:349）导出 counter+s0..s3；`LoadFromSerializable()`（:75）原地恢复（保引用）。
- **持久化格式** `SerializableRng`（`SerializableRng.cs`）＝`counter` + `state0..state3`（5 字段），JSON 存盘 + 网络包两用。
- **run 级流** `RunRngSet`（`RunRngSet.cs`）：12 个命名流（`RunRngType`：UpFront/Shuffle/UnknownMapPoint/CombatCardGeneration/CombatPotionGeneration/CombatCardSelection/CombatEnergyCosts/CombatTargets/MonsterAi/Niche/CombatOrbs/**TreasureRoomRelics**），各 `new Rng(Seed, snake_case(name))`。`StringSeed` 哈希成 `Seed`。`ToSerializable/FromSave/LoadFromSerializable` 全量持久化；**seed 跑中不可变**（`:178-180` 改则抛）。
- **每玩家流** `PlayerRngSet`（`PlayerRngSet.cs`）：4 个命名流（`PlayerRngType`：Rewards/Shops/Transformations/NonCombatGeneration），各 `new Rng(Seed, name)`；同样全量持久化、seed 不可变（`:87-89`）。
- **抽袋** `RelicGrabBag`（`RelicGrabBag.cs`）：`Populate` 开局用 rng `UnstableShuffle` 洗一次各稀有度 deque（:70-93）；`PullFromFront/Back`（:130/157）**取队首/队尾命中 filter 者、不再 roll**；`GetAvailableDeque`（:220）做稀有度回退+剔除 disallowed+refresh；`ToSerializable`（:276）持久化 **deque 顺序**（relic id 列表）。

### I.2 原版联机同步机制（证据）

- **每玩家 RNG 随玩家序列化持久化+同步**：`Player.ToSerializable()` 含 `Rng = PlayerRng.ToSerializable()`（`Player.cs:396`）；`Player.LoadFromSerializable` 调 `PlayerRng.LoadFromSerializable(player.Rng)`（`Player.cs:442`）；`SerializablePlayer.Rng` 是 `SerializablePlayerRngSet`（`SerializablePlayer.cs:53/135`）→ 进 run 存档 + 网络传输。
- **战斗全量状态同步带每玩家 rngSet**：`NetFullCombatState.cs:192/230/464`（`playerState.rngSet = player.PlayerRng.ToSerializable()`）。
- **run 级 RNG + 共享袋由主机广播**：`SyncRngMessage`（`SyncRngMessage.cs`）注释「Sent to all clients from host at the start of combat to sync RNG state」，载 `SerializableRunRngSet rng` + `SerializableRelicGrabBag sharedRelicGrabBag`；`CombatStateSynchronizer.cs:47`（注册 handler）/`:86`（OnSyncRngMessageReceived）/`:236`（发送）。
- **结论**：原版每条流都是「命名 + 确定性种子 + 全量持久化（counter+s0..s3）+ 读档原地恢复 + 联机随玩家状态/主机广播同步」，seed 跑中 immutable。

### I.3 模组现状（证据）

- **权重流** `ConstructRelicRolls`（`RelicAcquisition.cs:46-75`）：`WeightStreams = ConditionalWeakTable<Player, Rng>`（:52）；`WeightStream(player) = new Rng(p, ModelDb.GetId<ConstructCharacter>(), 0x52454C4943574549UL)`（:53-54，盐＝ASCII "RELICWEI"）。**用的正是原版按内容派生构造器** → 种子＝run种子+槽位+角色Id哈希+固定盐。
- **刻意不推进原生流**：注释（:49-51/68-70）「用自己的确定性流、绝不推进原生 RNG，让回放原生序列的 RNG 预测器保持对齐」；`Apply` 用 `WeightStream(player).WeightedNextItem(candidates, RelicWeights.Weight)`（:71）替换原生「取队首」，并 `deque.Remove`（:72）。
- **WindDollPriority.Next**（`RelicAcquisition.cs:205-216`）刻意先 `rng.NextItem(items)` 消耗原生抽数再覆盖结果，「让回放原生流的预测器停在同一随机计数器」。
- **宝袋路径** `ConstructTreasureRelics`（:77-147）：transpiler 把共享袋 PullFromFront 替换为 `Pull`，传入 synchronizer 的 `_rng`（原生 TreasureRoomRelics 流）；`Pull` 设 `RelicFactoryScope.CurrentPlayer/CurrentRng` 后调 `bag.PullFromFront`（仍被 ConstructRelicRolls 钩住→用私有权重流）。
- **死字段**：`RelicFactoryScope.CurrentRng`（:38）在 `Pull` 写入（:136）但**生产代码从不读取**（全仓 grep 仅 QA `DraftChecks.cs:236`/`TreasureRelicChecks.cs:60` 断言作用域恢复后为 null）。即宝袋路径把原生 `_rng` 传进一个无人消费的字段；实际加权仍用私有流。（次要清理项，归 §D。）

### I.4 精确分歧（已对齐 vs 未对齐）

- **已对齐原版**：① 种子派生方式（run种子+槽位+Id+盐）→ 各端创建时一致、roll 种器改 run种子即可控制权重流；② 不推进任何原生流 → 原生预测器回放原生流时计数器不被模组污染。
- **未对齐原版（核心缺口）**：
  1. **不持久化**：权重流存于 CWT<Player,Rng>，键是 Player 对象引用。存读档时 Player 重建 → CWT 条目丢失 → `WeightStream` 重新 `new Rng(...)`，**counter+state 归零重置**。原版 PlayerRngSet 经 SerializablePlayer.Rng 持久化并 `LoadFromSerializable` 保序。后果：读档后模组加权序列**从头回放**（读档前抽过的加权遗物会重复），可被 save-scum 重 roll，且与「连续序列」预期不符。
  2. **不显式联机同步**：权重流不在 RunRngSet/PlayerRngSet/NetFullCombatState/SyncRngMessage 内，纯靠各端从 run种子+槽位本地重算 + 主机权威动作回放顺序一致来维持同步。**_live 会话_大概率一致**（同种子、同回放序）；但**读档/rejoin** 时各端 Player 重建时机不同 → 私有流独立归零 → 主机在 counter N、重加入客机在 counter 0 → **加权遗物选择各端分歧**。原版用「每玩家 RNG 随玩家状态同步 + 主机广播」规避此问题。
- **对第三方模组的影响（作者关切）**：roll 种器/预测器依赖原生种子与序列。模组种子已派生自 run种子（种器可控✓）、且不污染原生流计数器✓；真正风险是**私有流不持久化/不同步**导致的读档回放与 rejoin 分歧，使「同一 run 同一内容应得同一结果」的原版契约在模组加权上被破坏。注意：模组替换了遗物选择算法（加权代替取队首），这是作者批准的玩法特性，预测器无法预知模组私有盐——此点属特性固有，非本次要消除的分歧。

### I.5 native-alignment 修复方向（记录，未实现；护栏：不仓促改架构）

- **目标**：把权重流做成原版式「命名 + 确定性种子 + 全量持久化 + 读档恢复 + 联机主机同步」流，镜像 PlayerRngSet。
- **首选（最小改动、镜像原版）**：把每个构装体玩家权重流的 `SerializableRng`（counter+s0..s3）存入模组存档数据（如 BaseLib `SavedSpireField`，按玩家槽位键），读档经 `Rng.LoadFromSerializable` 原地恢复；联机确保该状态随玩家战斗状态/rejoin 同步（镜像 NetFullCombatState.rngSet 的作用）。种子仍用现有 `Rng(player,角色Id,盐)`（已对齐）。
- **禁止方案**：改用消耗原生流（如 player.PlayerRng.Rewards / run.Rng.TreasureRoomRelics）来做加权——虽自带持久化+同步，但**会推进原生流**，破坏原生预测器对齐，违背作者「尊重原版」与模组既有设计。
- **实现前待核实**：① BaseLib `SavedSpireField` 是否随原版玩家序列化自动联机同步（决定要不要额外 mod 同步消息）；② `ActionQueueSynchronizer` 是否主机权威回放遗物 pull（保证各端私有流推进序一致）；③ rejoin 是否重建 Player 并重置 CWT（确认分歧路径）；④ 宝袋共享流（无主）与每玩家流的持久化键如何区分（槽位 vs 共享）。
- **须补 QA**：存读档加权序列连续（不归零）；2p/4p 读档+rejoin 各端加权选择一致；roll 种器改 run种子→权重流确定性随之改变；原生流计数器不被模组加权推进（预测器对齐回归）。
- **附带清理**：`RelicFactoryScope.CurrentRng` 死字段（I.3）一并评估移除或接回真实消费。

> 本节为探索记录，**未改任何代码**。实现归 0.5.2+，须作者批准并按 §G 重建管线（35/35+4/4+4/4、重传私测、verify、更新 ZIP/哈希/记录）。

---

## J. 0.5.2 执行记录（2026-11-18，代码批量已落地 + 全量 QA 通过）

> 作者指示「开始代码修复…版本号 0.5.2」，随后「允许走全量 QA…完成全部 0.5.2 内容创作修改验收」。最终候选 **DLL `fa70dbed6357e9a93075fa0cc19aa1024125b9f5490f8ec709f7167db904049d`**（PCK `d67662e5…` 与 0.5.1 同——美术未变；JSON `9ceef44e…`；277 资源）。

### J.1 已落地代码改动
- **版本号**：`PunchConstruct.json` + `PunchConstruct.csproj` `<Version>` 0.5.1→0.5.2。
- **#27（B1）**：`WaveEvents.cs:181` SEARCH 预览 `FromCard<ConstructPanacea>(true)`→`(false)`（与 Search() 实际给基础版一致；:99 未动）。
- **#28（B2）**：`AncientAdaptations.cs` AncientWaveMapping.Apply 构装体场景字典**替换为仅** {TonyWave→ConstructDomineering}（剔除原版六项）→无东尼则 SetupForPlayer false、Orobas 不出古牙；`PlaytestRunner.cs:1472-1475` 过时断言改为 TonyWave→霸无敌。
- **#25（D1）**：删除 `GuardTemporaryStrength.cs`（三个临时力量 power 类，生产已无引用，两遗物均用 VigorPower）；同步清理 ConstructPowerIcons brass_nut 映射、EnglishText.Generated.cs + english_text.json/template 临时力量条目、CoopChecks/StatusTooltipChecks/PlaytestRunner/EnglishChecks/SmokeCheck 引用（QA 改测活力/无力量；快照阶段 temporary-strength-expired→second-turn，check_coop.py/release_evidence.py 同步）；SmokeCheck 图标计数 11→10。
- **§C 稳健性**：C1 PatchRegistry 空目标 fail-closed；C2 CombatRelicState.CopyOnClone 移出静态构造入 Initialize() + ModEntry try/catch 降级标志；C3 ConstructTreasureRelics transpiler 验 LocalVariables[1]==Player 否则返回原 IL；C4 CardAcquisition.Targets 加参数类型过滤；C5 ConstructRelicRolls GetAvailableDeque null 守卫回退原生；C6/C12 新增 CompatibilityPrecheck 启动反射预检；C7 AdaptationRegistry Disabled 改 ConcurrentDictionary；C9/#26 新增 [QaOnly] + PatchRegistry 无 QA 参数跳过（DelayedFacingProbe/PlaytestInertEnemy/QaStartupPreload 生产不装）。
- **D3 共享遗物池**：`RelicBridge.cs:159` 删除全局 `SharedRelicGrabBag.Remove(original)`（保留 per-player :158）→混编原版队友保留原生遗物。
- **D3 新增 QA**：ClockworkChannelChecks（齿轮只商店渠道，64 种子奖励/宝箱稀有度抽不出）、发条真实抽牌链（叠两 Burn 真 Draw 递归，CardPilePosition.Top）、PunchOffTargetsCheck（Targets 只三本地化方法）、共享袋保留断言（RelicChecks）。
- **独立审查 3 缺口（作者指示归 0.5.2，非 0.5.3）**：① RelicAcquisition treasure transpiler 补 pull/_rng 判空（null 返回原 IL，避免 ldfld null 非法 IL）；② ModEntry CompatibilityPrecheck.Run() 包 try/catch（诊断失败不中断初始化）；③ CombatRelicStateDegraded 增消费点（启动行显示 DEGRADED）+ 修正注释。修复后候选 **DLL `2bc0f3a3e1b5b487b929842ed692c513c514eb5c394c452c46e29cdf064439e3`**（PCK d67662e5、JSON 9ceef44e 不变；ZIP 51,346,311 B / `55a02af4…`），编译 0 警告 0 错误。

### J.2 验收状态
- **全量 QA（冻结 0.113.0 / BaseLib 3.4.8，已在 2bc0f3a3 重跑）**：local 35/35 + coop 4/4（含 4p 弱网）+ recovery 4/4 **全 PASS**；`workshop.py --verify` targeted=PASS、network=PASS_LOCAL_ENET、missing=[]（verification.json 已切 2bc0f3a3）。弱网 4p-network 首两次瞬态失败（一次战斗中丢包/断流结算竞态、一次 peer-4 启动握手竞态被误判版本不符），第三次重跑 PASS——与 §0 记录的 0.5.1 首跑同类，非 mod 逻辑。current-verification full_local=true、full_invariant=16/16；full_release=false（未上传+外部轴 NOT_VERIFIED）。
- **文档校验**：`check_documents.py` 已转绿（10/10 PASS）——README/tools/README/角色设计/问题记录/发布前清单/dist/README/安装说明/发布审查 均含 0.5.2；公开 workshop.json changeNote/描述升 0.5.2（98种卡牌/23张原创设计/BaseLib3.4.8/0.113.0 断言通过）；发布说明.txt 重生成；invariants 卡数 82→98、save-upgrade 升 0.5.2、角色设计基线 0.111.0→0.113.0。
- **0.113.1 隔离预检**：startup/smoke/additions/pool/modes/selection **6/6 PASS**（已在最终候选 2bc0f3a3 复核，含 Blitz/Dread modes；版本行 v0.5.2.0）。
- **归档**：`dist/releases/0.5.2/` 三文件 + `PunchConstruct-0.5.2.zip`（**51,346,311 字节**，SHA256 **`55a02af474bb0eb4658b2cc984835563f30aab30ce72fb5c2dc3681af1c6bfb6`**，根目录三文件、与 content 逐字节一致；含 3 缺口修复，DLL `2bc0f3a3…`）。`dist/PunchConstruct` 与私测工作区 content 已同步到 2bc0f3a3；current-verification.json full_local=true、full_invariant=16/16（full_release=false 因未上传+外部轴未验证）。
- **私测重传 3835179658**：工作区 `tools/runtime/workshop-test-0.5.2` 已备妥（private/测试标题/2bc0f3a3/6 预览）；**阻塞：Steam 客户端未运行**，待作者启动 Steam 后重传 + verify_previews + 回下载校验。
- **公开 0.5.2**：待作者明确许可（公开项 3826907330 现仍 0.5.1）。

### J.3 本批未做（留档）
- **F4/C14（RNG native-alignment）**：归 0.5.2+，护栏「不仓促改 RNG 架构」，待作者批准（§I）。
- **C8（AdaptedRelicVariants StackTrace）**：低优先、需绕开开放泛型限制的设计项；已被 :69 门槛缓解，本批不改。C10/C11 有护栏/已缓解，低优先不改。
- **霸无敌 2p rejoin**：_pending 战斗内私有 list，跨回合（live）有效、存读档不持久化战斗（无残留）；2p rejoin/全量重建丢失 pending（原生 power 只同步 id+amount 所致）。proper 修复需改持久化（存 card-id+数值），属架构项，留作者定夺。
- **D3 其他 P2**：真实 RitsuLib/RandomForeseer smoke＝外部边界（G4 NOT_VERIFIED）；可移植构建路径＝csproj GameDir/BaseLibDir 已可 /p: 覆盖；多人版本一致性＝F3 决定不加门禁（ModEntry 打印版本）；ASSETS.md＝素材归属见 素材清单.json + 工坊描述（G3 授权 UNRESOLVED）。

---

## K. 第二轮代码质量审计复核（2026-11-19，只读核实，未改 .cs）

> 第三方 AI 第二轮静态审计（A1–A7 fail-open / B1–B11 上游脆弱 / C1–C8 测试）。逐条对代码核实。**编号独立于第一轮 P-编号与第三轮 A/B/C，勿混淆。** 当前候选 2bc0f3a3（§J）。总判定：大部分成立、质量高；**A6/C4 驳回，A1/B8 降级**。

| 编号 | 位置 | 判定 | 依据 / 修复方向 | 优先 |
|---|---|---|---|---|
| K-A1 | `AdaptationRegistry.cs:64-81`+`ModEntry.cs:40` | **降级（=§F1 P0-2，作者已定夺甲）** | 运行期 Required adapter Fail 只禁用 hook+写日志，不进 RequiredFailure；但**安装期** Required（5 bridge）对改名/零目标 fail-closed。属可观测性缺口，非整体 fail-open。§F1 已决定不改行为 | P1 可观测 |
| K-A2 | NativeBridge 88 `[HarmonyPatch]` / 5 `[PatchPolicy(true)]` | 成立 | event 14、presentation 25 补丁 **0 Required**；全仓库 Required 仅 CombatBridge:23 / RelicBridge:125,169 / RewardBridge:64,126。需「缺了不能玩」白名单+枚举测试（=K-C2） | 中（白名单待作者定） |
| K-A3 | `PatchRegistry.cs:67-75/76-80/98` | 成立 | partial-load 缺失类型不记 Failures；GetTypes 全失败 `return` 留 RequiredFailure=false；HasPatch 抛异常 required 硬编码 false。Required 机制在最需要时失效。修：partial 记缺失按 Required、inspect 失败沿用 PatchPolicy、全失败置 RequiredFailure | 高（便宜，扩展 §C1） |
| K-A4 | `check_current.py:380`+`report_local.py:101/147`+`qa_logs.py:32` | **成立（本轮最有价值）** | result.dll_sha256 取自暂存 `target/` 文件；report 写死 `matches_current_dll=True`；qa_logs 只验 session.start.dllSha256 存在+64 位，**不比对 digest**。游戏已在 `Diagnostics.cs:76` 对 assembly.Location 自证，门禁未消费。修：qa_logs/report 比对 `session.start.dllSha256==digest`，coop 每 peer、recovery 每角色都查 | 高（便宜） |
| K-A5 | `PlaytestRunner.cs:114`/`SmokeCheck.cs:243` | 成立但降级 | 仅 2 模式打印文本哈希；但所有模式经 Diagnostics.Initialize 写 session.start，JSONL 自证已全覆盖，文本行只是冗余。真缺口是 K-A4 | 低 |
| K-A6 | `CombatBridge.cs:27-34` 等 ref 写回 | **驳回（非缺陷）** | 写回 `__result=result` 无条件，但全部 Guard 调用 fallback=true（grep 无 `fallback:false`），失败→原生执行→返回值覆盖 __result，污染不生效。仅将来新增 fallback:false 的 ref guard 才有风险 | 不修 |
| K-A7 | `ConstructCharacter.cs:24` | 成立（设计权衡） | HideFromVanillaCharacterSelect 只挡选人，读档无门禁；per-hook guard 回落原生不崩溃。建议读档至少告警；阻断旧档可能 brick 存档，需作者权衡 | 低-中 |
| K-B1 | `PresentationAdapter.cs:101-114/124-138` | 成立 | FakeMerchantAnimation/CampAnimation 任意 ldstr 后插 Ldarg_1/Ldarg_0，不验 IsStatic/参数类型；坏 IL JIT 时炸，Run guard 接不住。修：验签名+窗口化 ldstr 匹配+不确定返回原 IL | 中-高 |
| K-B2 | `RelicAcquisition.cs:215/231` | 成立 | WindDollPriority `owner=Field(<>4__this)` 未判空→Ldfld null 坏 IL；`IsWind GetField("relic")!` 改名 NRE，注入 Next/Shuffle 运行期不被 guard 包。修：owner null 返回原 IL+warn、IsWind 缓存 FieldInfo+判空 | 中-高 |
| K-B3 | `RelicAcquisition.cs:190-200` | 成立 | FakeMerchantExclusions `_inventoryRelics` null→LoadsField(null) 不命中→原样返回**零诊断**（同文件 ConstructTreasureRelics:97/105 已加固，双标准）。修：field null 写 transpiler.skipped | 中 |
| K-B4 | `WaveEvents.cs:65/102/108` | 成立 | 事件点击委托 Receive/Combine/Reforge→Finish 反射+原生调用在选项生成 guard **之外**；改名/抛异常逃进事件 UI。修：委托体内包 Guard 或安装期解析 MethodInfo 缺失则 Required 失败（同文件 ConstructFusionReward:123 已有判空范式） | 中-高 |
| K-B5 | `GuardDamagePresentation.cs:19/45` | 成立（安全降级） | `.Single` 新增重载安装期抛（非 Required→warn 禁用）；`fast==.1f&&standard==.2f` 精确比较，改常量静默回落原生时序（良性）。修：候选列表取首+计数、浮点容差 | 中/低 |
| K-B6 | `RelicAdaptations.cs:49-57` | 成立 | HelmetMirror `SetUsed(true)` 在 try 前，PowerCmd.Apply 抛→finally 只清 Applying→本场唯一镜像被吃。修：成功后 SetUsed 或 catch 回滚 | 中（低概率） |
| K-B7 | `RelicIcons.cs:12/29/49` | 成立（低实践风险） | `(GetInstanceId,operation)` 永久缓存，纹理释放后 id 复用可能错配；遗物图标资源长驻风险低。修：key 改 relic.Id.Entry+operation+ResourcePath 或 CWT | 低-中 |
| K-B8 | `Diagnostics.cs:67-92/78` | **降级** | Initialize 全有或全无确脆弱（一个 null manager 跳过全部订阅）；但「QA 误判没跑→PASS」**错**——qa_logs 对缺失 session.start/lifecycle 报 error 并 fail。`baseline=0.113.0` 硬编码是有意区分 QA 基线/运行版本。修：每订阅单独 try | 中（驳回假通过定性） |
| K-B9 | `Diagnostics.cs:34-45` | 成立（有缓解） | 每条日志同步 CreateDirectory+AppendAllText+分片 GetFiles 删除，锁内游戏线程；NativeLog 重复抑制（5s/128）缓解量。修：后台单消费者队列+退出超时 Flush | 中 |
| K-B10 | `AdaptationRegistry.cs:35` | 成立 | 单参 `Enabled(adapter)` 全仓库无调用（grep 仅两参），死代码。删或让 Fail 在 Required adapter 真正使用 | 低 |
| K-B11 | `AncientShop.cs:20`+`check_coop.py:150` | 成立 | 复制 Shops 流额外推进一次（注释承认 intentional divergence），phases 无 ancient-shop。修：加 2p/4p ancient-shop phase 或断言仅单人触发 | 中 |
| K-C1 | `CompatibilityChecks.cs:155-176` | 成立 | PolicyTable 只锁 4 adapter Required 映射、PatchHealth 只锁 RequiredFailures==0；无「Required 失败→隐藏/阻开局」负向用例（K-A1 存活原因）。加负向用例 | 中 |
| K-C2 | NativeBridge 全类 | 成立 | 无 bridge Required 白名单枚举测试。加：枚举 NativeBridge 断言核心清单带 `[PatchPolicy(true)]`，新类显式归类 | 中 |
| K-C3 | `report_local.py:94-95` | 成立 | `dll_sha256!=digest` 行静默 continue，只体现 missing，无法区分没跑/跑错构建。收集 stale 列表输出最好 FAIL | 中（便宜） |
| K-C4 | `RelicAcquisition.cs:48` | **驳回（已修）** | 审计「GetAvailableDeque 热路径每次按名反射」**错**：`static readonly MethodInfo` 启动解析一次缓存（§C5 已落地），热路径只 Invoke；CompatibilityPrecheck 已启动校验。仅 Invoke 是反射（正常） | 不修 |
| K-C5 | `RelicAdaptations.cs:21` | 成立（低） | SoupWavePatch.Transforming 无锁静态 HashSet 跨 async；游戏单线程低风险。注释单线程不变量+断言或 CWT | 低 |
| K-C6 | `RelicPreviewContext.cs:19` | 成立 | Register `AssertMutable` 在 try 外，冻结/共享模型抛→presentation guard 接住但永久禁用 preview hook。内部 catch 降级无预览 owner | 中 |
| K-C7 | `Diagnostics.cs:43-45` | 成立（非当前布局） | 保留清理按 LastWriteTime 删 pc-*.jsonl，共享 userdata 会互删；当前 QA 隔离 userdata 不触发。按 session 前缀保留或进程锁 | 低 |
| K-C8 | `PatchRegistry.cs:29` | 成立 | Result 无 Kind，FailedNames 混 no-target/threw/inspect-failed。Result 增 Kind | 低 |

**与第一轮关系**：第二轮 A1 重述第一轮 P0-2（§F1 已定夺保持现状）；C4 重复第一轮 GetAvailableDeque 但该项 §C5 已修（缓存+预检），属审计误计。

---

## L. 第三轮代码质量审计复核（2026-11-19，只读核实，未改 .cs）

> 第三方 AI 第三轮静态审计（A 丢效果/算错 / B 露中文 / C 上游脆弱 / D 状态缓存死代码 / E 测试缺口）。逐条核实。**撤回项接受**；C5 症状、D2 理由更正；A6 属作者已定夺文案项。当前候选 2bc0f3a3。

**撤回接受**：ObserverColdObservation「单人无效」撤回正确——`ObserverChecks.cs:199-204` 单人 harness 断言 power Amount=2、打 Fight Me 后=1，observer 是 35 run 之一且 PASS → GetTeammatesOf 含自身。仅剩联机 AllAllies 语义顺手看，非缺陷。

### L-A 会丢效果 / 算错
| 编号 | 位置 | 判定 | 依据 / 修复 | 优先 |
|---|---|---|---|---|
| L-A1 | `Powers.cs:17,45-47`/`ClockworkCards.cs:114,147-149` | 成立（一半已留档） | `_pending` 普通 List 非 SavedSpireField，存读档/rejoin 重建丢失（**=§J.3 霸无敌 2p rejoin 已留档架构项**）；且先 Clear→Remove→逐段付，中途抛异常剩余永久丢（**新**）。修：先付后清（便宜）+ pending 改可序列化记录（card-id/升级/四数值，架构项与 F4 同批待批准） | P0 先付后清 / P2 持久化 |
| L-A2 | `SpinningScrew.cs:56-62` | **否定（原生语义已核实）** | 原生 `Hook.ModifyHandDraw` 只在整数计数变化时把监听者加入 `modifiers`（sts2 `Hook` 182268-182273），`Hook.AfterModifyingHandDraw` 只遍历 `modifiers`（181307-181311）。本遗物对 `player != Owner` 返回原值，故队友抽牌不会清除 `ScrewDrawReady` 或误闪；2p 交错用例仍建议保留为非阻塞回归 | P3 |
| L-A3 | `Powers.cs:77-80` | 成立（高，玩家可见） | IronWaveForm `_triggeredThisTurn++` 在 `UnblockedDamage>0` 前，全格挡/0 伤害也消耗触发且不给格挡，与「前 N 次造成伤害时」文案不符。修：++ 移入 if 内 | P0 便宜 |
| L-A4 | `SystemPowers.cs:94` | 成立（中） | UnarmBlockReplacement.Replace 两次 await 后 `GetPower<UnarmToArmsPower>()!`，power 被移除→NRE；async 续段逃出同步 Guard（见 L-async）。修：Summon 前重判 HasPower/判空 | P1 |
| L-A5 | `ClockworkCards.cs:128-132` | 成立（高，显示） | 霸无敌 tooltip Energy/Cards 取 `_pending.FirstOrDefault`，Waves 列全部伤害/格挡→两段数值不同（升级/未升级混）时自相矛盾。实际结算:150-157 用各段自身值正确，仅显示错。修：Waves 内逐段列 energy/cards 或模板说明可变 | P0 |
| L-A6 | `AdaptedRelicVariants.cs:250` | **已修复（2026-11-24）** | 汤文案已改为「所有未附魔的小斩波」并同步 16 locale `relics.json` + `EnglishText.Generated.cs`；`SoupChecks`/`ReliabilityChecks` 断言只替换未附魔小斩波、跳过已附魔牌 | P0 文案 |
| L-A7 | `GuardStance.cs:32-35`/`AncientAdaptations.cs:36-39` | 成立（机制高/概率低） | `NextAmount++` 在 GuardStancePowerGain.Apply 前，Apply 抛→计数已涨 Vigor 未发，下次更大。修：成功后++或 catch 回滚 | P1（低概率） |

### L-B 会露中文
| 编号 | 位置 | 判定 | 依据 / 修复 | 优先 |
|---|---|---|---|---|
| L-B1 | `BoxingCards.cs:59,63`+`EnglishText.Generated.cs:70,250` | 成立（玩家可见） | 英文模板:250 保留 `{RightTitle}`，AddExtraArgsToDescription:63 塞硬编码「右勾拳+/右勾拳」→英文 UI「Add a 右勾拳 to your hand.」；英文表:70 只有「右勾拳」=Right Hook，**无「右勾拳+」**。修：`Loc.Pick(IsUpgraded?"右勾拳+":"右勾拳")`+表补「右勾拳+」，或取 `ModelDb.Card<RightHook>().Title` | P0 |
| L-B2 | `Powers.cs:30` | 成立 | DelayedWavePower.Description `string.Join("；",…$"{Damage}伤害、{Block}格挡")` 全硬编码中文绕过 Loc.Pick；同仓库 `ClockworkCards.cs:131-132` 却用 Loc.Pick（双标准）。修：改 Loc.Pick | P0 |
| L-B3 | `ConstructPools.cs:10`/`ObserverCards.cs:25` | 成立 | 卡池 Title 硬编码「拳击构装体」「观者」，角色本体已 Loc.Pick 池名没走。修：Title 走 Loc.Pick | P0 |
| L-B4 | `EnglishChecks.cs:43-49,102-106` | 成立（系统盲区） | 只扫静态 Localization 模板+英文表值 CJK，不扫英文模式**运行期 Description**（GetDescriptionForPile）→B1/B2/B3 动态参数逃出。注：`BoxingChecks.cs:69` 确扫运行期 GetDescriptionForPile 但**中文模式**断言中文存在，非英文 CJK 缺失。修：EnglishChecks 增英文模式运行期描述遍历断言无 CJK（系统收口） | P1 |

### L-C 上游一改就炸 / 静默失效
| 编号 | 位置 | 判定 | 依据 / 修复 | 优先 |
|---|---|---|---|---|
| L-C1 | `RegentAdaptations.cs:21-23` | 成立 | 3 个 FieldRefAccess(_owner/_baseStarCost/_starCostSet) 静态初始化无 try/catch，改名→TypeInitializationException→Owns 全挂连带 Soup/Abrasive/SicEm/Transform/AncientShopPrice。`CharacterScope.cs:26-42` 已有 try/catch+null 降级范式（双标准）。修：统一 SoftRef 注册表 null 降级 | P2 |
| L-C2 | `GuardStancePowerGain.cs:51,56` | 成立（潜在高影响） | `power=Field(...,"power")` null→Ldfld null 不抛→坏 IL JIT 炸；注入目标是 **PowerCmd.Apply/ModifyAmount.MoveNext（核心原生）**。Wait:67 0.1f/0.25f 精确比较改常量良性退化。修：field null 返回原 IL+warn | P2 |
| L-C3 | `AncientShop.cs:46` | 成立 | GetCost 反射每次价格显示 Invoke，无缓存/判空/不在 Precheck，改名 NRE。修：缓存 MethodInfo+判空+入 Precheck | P2 |
| L-C4 | `CardAcquisition.cs:317-324` | 成立 | PrepareRestock `_cardPool ?.GetValue` null-safe 早退→改名时静默跳过「清空单卡池防 null card」保护，注释:314-316 描述的危险路径正好发生。修：field null 写 skipped+warn，归 Required | P2 |
| L-C5 | `AdaptedRelicVariants.cs:112-113,84-85` | 成立（=§C8 已留档） | CalledFromGenericObtain 用 `new StackTrace().GetFrames()` 嗅探 RelicCmd 泛型帧，async/内联漏判。**症状更正**：漏判→Substitute(:67-79) 把脚本遗物换成 **variant（非 Circlet**；Circlet 是 ConstructRelicRolls 空候选路径），脚本 cast 回 T 可能炸。需绕开放泛型限制设计 | P3（需设计） |
| L-C6 | `CompatibilityPrecheck.cs:16-24` | 成立（=§C12 伞项） | 只校验 4 点（GetAvailableDeque+AttackCommand×3），实际软反射面≥10（RegentAdaptations×3/PresentationAdapter×3/CardAcquisition×1/AncientShop×1/GuardStancePowerGain×1/ConstructSoupOffer×1）。修：SoftRef 注册表，Precheck 遍历 | P1-P2 |
| L-async | CombatBridge/SystemPowers 等约 6-8 ref Task 适配器 | 成立（结构，扩展 §F1） | Guard 只包同步段；UnarmBlockReplacement.Apply 同步返回 bool 后 Replace async 续段（`SystemPowers:88-96`）异常不被熔断记录/禁用，下次继续炸。UnarmBlock/SicEm/OstyHealthAttack/DelayedWaveFacing/ColorfulPhilosophers/WaveEvents 同模式。§F1 作者已定夺运行期 fail-open 保持现状，但「设计对 async 半段未生效」是新事实。修：async 续段包 Guard 或安装期解析 MethodInfo | P1（需作者确认是否纳入 §F1 例外） |

### L-D 状态 / 缓存 / 死代码
| 编号 | 位置 | 判定 | 依据 / 修复 | 优先 |
|---|---|---|---|---|
| L-D1 | `CombatRelicState.cs` | **已修复（2026-11-24）** | `NativeHelmetUsed` 声明 + `CopyOnClone` 已删除；全仓库无读写，原生 `RuinedHelmet` 已由变体替换为 `ConstructHelmet`，不残留镜像状态 | P3 |
| L-D2 | `AdaptedRelicVariants.cs:87-93` | 成立（模式） | CopyState 只迁 FloorAddedToDeck/IsWax/IsMelted/Status 不迁 SavedSpireField。实际无害：`NativeHelmetUsed` 已删（L-D1），且 Swap 在获取期（战斗前）`HelmetUsed` 默认 false。新增带 SavedSpireField 变体时需补迁移 | P3 |
| L-D3 | `AdaptedRelicVariants.cs:317`/`CardAcquisition.cs:216` | **已修复（2026-11-24）** | 17 = bit0 `NoRarityModification` + bit4 `NoCardPoolModifications`，**不含** `IsFromCombat`；与原生 SeaGlass/ColorfulPhilosophers 完全一致。两处改为 `CardAcquisition.EventRewardFlags` | P3 |
| L-D4 | `ObserverCards.cs:41-51` | **否定** | BaseLib `CustomCardModel.CustomFrameMaterial` 已用 `_initializedFrameMaterial`/`_frameMaterial` 按模型实例缓存，`CreateCustomFrameMaterial` 不会每帧调用；无需改 | P3 |
| L-D5 | `CardAcquisition.cs:303-310` | 成立但已有 QA 保护 | `PlaytestRunner.cs:837` 已断言原生无色商店恰好 2 槽；上游若改槽位数，QA 会直接失败。暂不改为按槽数派生 | P3 |

### L-E 测试缺口（最小用例）
A1 pending 存读档结算 / A2 2p 交错抽牌 / A3 全格挡不减 Remaining / B1+B2 英文运行期描述无 CJK（=B4 修法）/ C2 空字段假 state machine 返回原 IL / C5 async 包装 RelicCmd.Obtain<T> 拿变体。

### L-F 系统收口（审计建议，采纳）
- **SoftRef**：所有 Field/Method/Property 解析走一个注册表，null 登记+降级；CompatibilityPrecheck 遍历注册表而非手写 4 行（收口 C1/C2/C3/C4/C6）。
- **Persisted**：跨回合状态一律 SavedSpireField；新增 DeepCloneFields 强制配套存读档用例（收口 A1）。
- **Loc**：禁 description.Add 直塞中文；EnglishChecks 改运行期描述扫描（收口 B1/B2/B3/B4）。
- **Gate**：session.start.dllSha256 进 report_local.py（=K-A4，仍未做）。

**计量与结论**：玩家可见缺陷本轮约 8（A1/A2/A3/A5/A6/B1/B2/B3），其余为假设点/加固/死代码。审计「总找总有是必然、按模式收口才收敛」框架成立，采纳。发布验收结论不变：本地定点证据为真，发布门禁仍不成立（full_release=false，未上传+外部轴 NOT_VERIFIED）。

**版本归属**：第二/三轮均为 0.5.2 已 QA 通过候选（2bc0f3a3）之外的新发现。任何 .cs 修复→改 DLL→§G6 全管线重跑（重建+35/35+4/4+4/4+重传私测+verify+ZIP/记录）。归 0.5.2（重 QA）还是 0.5.3 **待作者拍板**。本批只核实+记录，未改任何 .cs。

**附带记录修正（2026-11-19）**：核实 5 处产物 DLL（bin/Release、dist/PunchConstruct、content、releases/0.5.2、私测工作区）均为 2bc0f3a3；修正 content-current.json:63/90 与 §J.2:227/229 残留的 fa70dbed 文字引用（previous_build:12 的 fa70dbed 正确保留）。

---

## M. 第四轮兼容性专项审计复核（2026-11-19，只读核实，未改 .cs）

> 第三方 AI 第四轮按兼容性失效模式扫（K1–K8 + 8 轴矩阵 + 5 件事）。审计已接受第三轮 C5/D2/A6 三处更正，不再计入。逐条核实。**总判定：成立，且与第二/三轮一致——风险集中在「最后一公里」，非弥散。** 作者自评「从来没有兼容性自测、凭感觉摸」**部分成立但过绝对**，见 M-0。

### M-0 既有兼容性自测（事实校正，避免「零自测」误判）
`CompatibilityChecks.Run`（`--punchconstruct-compatcheck`，35 项之一）已是**真实 in-engine 冲突仿真**，13 组：PostfixChain / PrefixSkip / GuardFallback / AdapterIsolation / PolicyTable / TranspilerDegradation / PatchHealth / SaveMigration / NullScope / IdentityScope / StructuralBoundary / CharacterScopeCoverage / RequiredPolicies——在私有 fixture 方法上真装 Harmony patch、走桥接回落、不碰游戏状态。另有 14 locale 英文表无 CJK + 回退、0.113.1 预检 6/6、per-class patch + per-hook fuse + Required fail-closed。**结论：前半段（patch 安装/熔断/顺序/语言值/存档迁移/scope）有自测且高于同类平均；缺的是后半段——静态反射解析、IL 字段有效性、版本门禁、跨 mod 池/顺序、locale key 对齐——这部分确实无系统门禁、凭感觉。** 审计本身的「前半段高于平均、后半段没享受同一范式」论断准确。

### M-K 八个失效模式
| 编号 | 位置 | 判定 | 依据 / 修复 | 优先 |
|---|---|---|---|---|
| M-K1 | `GuardDamagePresentation.cs:29-30` | **成立（最可能「更新即炸」单点；⚠️ GuardStancePowerGain 例与「按类型找局部」修法已被 §N-1 更正）** | 硬编码 Roslyn 序号名 `<targetList>5__3`/`<results>5__2`；0.114 改 Damage 方法体（加局部/调序）→序号变→Field null→`Ldfld null` 不抛→坏 IL JIT 炸在原生伤害核心。IlBridge.ReplaceCall 的 catch 包不住（构造期无异常，且只验调用替换数不验字段操作数）。同模式 GuardStancePowerGain:51 `power`、WindDoll:215 `<>4__this`、FakeMerchant:190 `_inventoryRelics`。修：IlBridge 增 RequireField（解析不到返回原 IL+skipped）；GuardDamagePresentation 改「按类型找局部」非序号名 | P0/P1 |
| M-K2 | 8 个 `Transpile` 仅 3 走 IlBridge | 成立 | IlBridge：GuardDamagePresentation/GuardStancePowerGain/ConstructFusionReward(WaveEvents:118)。手写无「找到了吗」检查：`CardAcquisition.cs:246` + `TransformAffinity`（实在 `TransformationRules.cs:41`，审计文件名小误）替换每个 Rng.NextItem<CardModel> 不计次数/0 次不写日志→换选牌 API 静默失效；FakeMerchant/WindDoll 不验字段/调用。`ConstructTreasureRelics:91` 是全仓库最佳范式（验 locals[1] 类型/pull/rng/替换数 1-2/每次跳过写原因）但手写未抽公共入口。修：全部收口 IlBridge RequireField/RequireCall | P1 |
| M-K3 | RegentAdaptations:21-23 / PresentationAdapter / GuardVigorConsumption:15 | 成立（** poisoning 子集需细分**） | **真 poisoning（静态初始化抛 TypeInitializationException）**：FieldRefAccess（Regent×3 _owner/_baseStarCost/_starCostSet；Presentation IconSize:256/IconModel:258/EntryPlayer:273）+ `GuardVigorConsumption:15` AccessTools.Method(...).MakeGenericMethod(null) 。**仅 null 降级（AccessTools.Method/Field/PropertyGetter 返回 null，使用期 NRE，多在 Guard 内）**：Presentation Connections:221/Peer:222/HandleDisconnect:223/PackedOutline:260、RelicAcquisition:48（已有 null 守卫）。审计「7 个都 TypeInitializationException」不准——只有 FieldRefAccess/MakeGenericMethod-on-null 才毒化整类。`CharacterScope.cs:26-42` 已有 try/catch+null+保守路径范式未推广。修：SoftRef 注册表统一解析+降级 | P1-P2 |
| M-K4 | `PatchRegistry.cs:67-75/93-113` | 成立（=第二轮 K-A3） | partial-load 某 Required 类未被遍历→Failures 空→RequiredFailure=false→角色可选桥缺失；HasPatch(:93) 抛异常时 required(:113) 还没读→记非 Required。BaseLib 删类型/属性时最可能发生。修：先读 required 再 HasPatch；ReflectionTypeLoadException 视为 Required 失败；加「预期 Required 类名必须全在 applied 集合」断言 | P1 |
| M-K5 | `CompatibilityChecks.PrefixSkip:112-123` | 成立 | 只测「第三方 prefix 先装、我们后装、foreign skip 赢」。未测：我们 Priority.First 先装第三方后装 / 双方都 First 都 return false / 第三方 transpiler 先改过我们 IlBridge 找不到调用。PatchRegistry 不报 foreignOwners（Harmony `GetPatchInfo(method).Owners` 现成）。注：TranspilerDegradation(:234-255) 已测 IlBridge 0/1/多/抛异常降级。修：补反向顺序用例 + 启动每个 Required bridge 打一行 foreignOwners | P2 |
| M-K6 | `NativeCardOwnership.cs:14` + `ConstructPools.GenerateAllCards:18-44` | 成立 | Ownership 用 `AllCardPools.FirstOrDefault(p=>p is not ConstructCardPool && p.AllCardIds.Contains(id))`，注释写「keep original ownership」实现是「第一个匹配」→第三方全卡池/扩展池若也列 IronWave 且枚举在前→拿错池（卡框/稀有度/图鉴）。GenerateAllCards 硬编码 **61** 个裸 `ModelDb.Card<T>()`（审计「~70」略高），池构建期求值，任一原生卡被移除/未注册→抛→角色模型加载失败，**在所有 Harmony guard 之前**＝「更新后角色直接不见」最短路径。修：Ownership 按原生池类型/命名空间解析或优先保留已是原生池的 __instance.Pool；GenerateAllCards 用 TryCard<T>()+明确报缺哪张 | P1 |
| M-K7 | `RelicAcquisition.cs:53-55` WeightStream | 成立（**=F4/C14 §I 已探索+留档，非新**） | CWT<Player,Rng> 从 seed 派生但位置不持久化，rejoin/存读档 Player 重建→流从头→权重序列与连续会话不同；注释「peers agree」只保证起点不保证位置。与 A1 _pending 同类（跨会话状态没进 SavedSpireField）。§I 已记录精确分歧+native-alignment 方向，作者定夺必须与原版同步方式一致，护栏不仓促改架构，待批准 | 暂缓（F4 同批） |
| M-K8 | `EnglishChecks.Json:108-122` + locale 循环:56-64 | 成立（**描述需更正**） | **核心成立**：无 key 集合对齐——只遍历各 locale 自身 key 查 CJK，缺 key/多 key 不 fail；新增文案只改 zhs/eng→其余 locale 缺 key 回退取决于 BaseLib/游戏。**更正**：审计「其他 13 locale 只查存在/解析/非空」不准——FallbackLanguages 循环:60-61 调 Json 默认 english=true→**13 个回退 locale 也查值无 CJK**；只有 zht(:64 english=false) 正确跳过 CJK。修：以 eng key 集合为基准，缺/多 key 都 fail | P1 |

### M-Rec 五件事（0.5.3 兼容性预算）判定
1. **dllSha256 进门禁**（=K-A4，第三次提）：✅ 采纳，最便宜最该先做。
2. **transpiler 全收口 IlBridge RequireField/RequireCall + GuardDamagePresentation 去序号名**：✅ 采纳（K1/K2 收口）。
3. **SoftRef 注册表 + Precheck 遍历 + 启动 DEGRADED**：✅ 采纳（K3/K6 收口，=§C12 伞项）。
4. **启动版本裁决 max_tested_game/baselib + PASS/DEGRADED/UNSUPPORTED 三态、UNSUPPORTED 隐藏角色**：⚠️ **与作者既有 §F3（P2-21）决策冲突**——F3 已定夺「完全不管（乙）：不加版本门禁、也不加告警，未测试版本照常加载、角色照常可选」。此建议正好反转 F3。**需作者重新定夺 F3 才能做**，不自动采纳。K4 partial-load 修复部分可独立做（不涉版本门禁）。
5. **GenerateAllCards TryCard + NativeCardOwnership 按类型 + locale key parity**：✅ 采纳（K6/K8）。

其余 K5 反向顺序测试 / K7 RNG 持久化（=F4）/ K8 全 locale key 检查：测试与架构项，K7 归 F4 同批待批准。

### M-结论
- 审计「总审总有 ≠ 兼容性极差」框架成立：20+ 反射点里高概率炸的只有 K1（序号名）+ K4（partial-load），其余「上游改了才炸」；大多数地方已是安全静默降级。真正危险是四种**静默且远程**失效：坏 IL（JIT 才炸）/ 类型中毒（整域禁用）/ partial-load（角色可选桥缺失）/ 错误解析（拿别 mod 池）。
- **正确范式仓库里已存在**（CharacterScope 软引用、IlBridge 调用计数、ConstructTreasureRelics 局部校验、PatchRegistry per-class 隔离），问题是**没有 lint/CI 强制新代码走它**。所以收敛靠「把既有好范式变成唯一入口 + 补后半段门禁」，不是逐条追打。
- 作者「凭感觉摸」的痛点正解＝**把兼容性变成可测**：SoftRef 全解析断言 + transpiler 替换数断言 + partial-load 负向用例 + locale key parity + foreignOwners 报告 + dllSha256 门禁。这六条做成 COMPAT 模式扩展，兼容性就从「感觉」变「门禁」。
- 发布验收结论不变：本地定点证据为真，发布门禁仍不成立。**本轮只核实+记录，未改 .cs。** 版本归属（0.5.3 兼容性预算）与 rec4 是否反转 F3 待作者拍板。

---

## N. 第五轮 AI 诊断复核（2026-11-19，只读核实＋本机反编译验证，未改 .cs）

> 第五轮是对第四轮判定的回应：接受更正、补硬证据、并**更正复核方（我）两处不准**。已用本机反编译 `C:/Users/li177/AppData/Local/Temp/sts2decomp` ＋独立 locale key 比对核实。**总判定：五轮里最高质量，采纳；我两处错误确认并更正。**

### N-1 复核方两处错误，确认更正（反编译为证）
- **GuardStancePowerGain 不是「坏 IL」例子（更正 §M-K1 / 第二轮 K-C2）**：PowerCmd 有两个泛型 `Apply<T>`（:36 IEnumerable targets / :71 Creature target）＋非泛型 `Apply`（:105 `PowerModel power`）＋非泛型 `ModifyAmount`（:220 `PowerModel power`）。`GuardStancePowerGain.Targets():41` 有 `!m.IsGenericMethod`，只收非泛型 Apply/ModifyAmount——两者都有 `power` 参数（async 参数提升为同名字段），故 `AccessTools.Field(stateMachine,"power")` 当前解析**非 null，不会注入 null 字段**。真风险＝`!IsGenericMethod` 是承重过滤但**无断言**：0.114 若把在用重载改泛型→hook 静默 no-op（非坏 IL）。修法：逐 target 解析 power、null 只跳过该 target＋写日志，并断言「非泛型 Apply/ModifyAmount 各≥1 且含 power」。
- **GuardDamagePresentation「按类型找局部」有歧义（更正第四轮我采纳的审计修法）**：8 参 Damage（`CreatureCmd.cs:285`）状态机有**两个** `List<DamageResult>` 局部——`results`（:295，<results>5__2，返回值）与 `damageResults`（:314，<damageResults>5__10，每目标内层）。按类型找命中两个。正确锚点＝**IL 位置**（`CustomScaledWait`(:492, 0.1f/0.2f) 调用后第一个 ldfld List<DamageResult> → results）。更优：`DamageResult.Receiver`（`DamageResult.cs:14`＝被击中 Creature）让 Wait 去掉 targets 参数，<targetList>5__3 依赖消失→K1 从「两个序号名」降到「一个类型锚点」，形状变只保守不缩短。**实现待核**：Receiver 对 Osty/Wuppy 替身是实际受击者（OriginalTarget 才是原目标），单目标全格挡常规路径 Receiver==target，替身边界需确认；IL 偏移锚点落地时用 fixture/Inspect 复核。

### N-2 诊断新增硬证据，核实成立
- **qa_logs.py:32 只 `len==64` 不比对**：确认（=K-A4）。「不是没自测，是自测没闭环」定性准确。
- **K8 locale key parity 当前全绿**：独立只读比对，16 语言目录（eng+13 回退+zhs+zht）×4 表，以 eng 为基准 **missing=0 / extra=0**。故 K8 是**门禁缺口**（防未来只改 zhs/eng），非当前数据缺口；现在加 parity 直接绿。
- **K3 懒解析洞见**：SoftRef 注册表若自身 `static readonly` 初始化 FieldRefAccess 会变成**新毒化点**。正确＝懒解析 `static T? => _x ??= TryResolve()`，失败返回 null 走保守路径。成立且关键。

### N-3 F3「诊断不裁决」——采纳，与 F3 不冲突
rec4 拆 (a) max_tested 元数据 (b) 启动 PASS/DEGRADED (c) UNSUPPORTED 隐藏角色+告警。**(a)(b) 只写日志不改行为**，与 F3「未测试版本照常加载、角色照常可选」一致（0.5.2 已有 CombatRelicStateDegraded 启动行先例）；去掉 UNSUPPORTED 词，DEGRADED 仅表示软引用未解析/可选 bridge 未装，不阻断/不隐藏/不弹窗。**(c) 才反转 F3，需作者明确「推翻 F3」才做。** 默认 (a)(b)。

### N-4 六门禁（重排＋DoD）＋三补＋lint，采纳
1. **dllSha256 门禁**：每 session dllSha256==构建哈希，**all 非 any**；扩 PCK/JSON/ZIP 制品链。DoD：塞旧 evidence 目录→非零退出。
2. **locale key parity**：脚本以 eng 为基准比 15 非 eng×4 表，缺/多 fail。DoD：只给 zhs 加一 key→红。
3. **PatchRegistry 两 fail-open＋partial-load 负向**：required 读到 HasPatch 前；HasPatch 抛且 Required→Failures；ReflectionTypeLoadException 用「预期 Required 类名清单」兜底（缺→RequiredFailure，可选缺→DEGRADED）。seam＝`ApplyAll(IEnumerable<Type>)` 内部重载供 QA 传缺类列表。DoD：三断言绿。
4. **IlBridge 收口＋锚点**：`RequireField(owner,name,expectedType)`/`RequireCall`/`ReplaceCall(expectedCount)`，0/多/null 字段→skipped＋原 IL；5 手写 transpiler 迁入；GuardStancePowerGain 逐 target 跳过＋Wait power null 守卫；GuardDamagePresentation IL 锚点＋去 targets。DoD：改名/缺字段合成 fixture 全回原 IL＋日志有 site。
5. **SoftRef 懒解析＋Precheck** 遍历逐条 try/catch＋启动 `COMPAT DEGRADED: n/N unresolved`。DoD：注入缺失 resolver→启动 DEGRADED、功能降级、不抛 TypeInitializationException。
6. **foreignOwners**：`GetPatchInfo(method)?.Owners` 只对 Required bridge、只打非本 mod owner、try/catch。最低，最后。
- **三补**：制品链 PCK/JSON/ZIP；降级计数（transpiler.skipped/adapter.disabled/patch.failed）进启动 COMPAT 行总数；NativeCardOwnership 负向用例（假池 IronWave 排原生池前→断言仍解析原生池）。
- **K6 修法更正（采纳诊断，更正我第四轮「TryCard+明确报错」含糊）**：TryCard **不 throw 到池构建外**——收集缺卡名、跳过、写 error、标 DEGRADED（throw 正好制造「角色不见」，违背 fail-open）。NativeCardOwnership 优先保留 `__instance.Pool` 已是原生池，找不到再回退，不 FirstOrDefault 全池扫。
- **lint（收敛关键）**：FieldRefAccess/软反射 AccessTools.* 只允许 SoftRef.cs/IlBridge.cs/CompatibilityPrecheck.cs；Transpile 只走 IlBridge；ModelDb.Card< 只走 TryCard。**实现注**：lint 须区分「运行期状态软引用」（必走 SoftRef）与「Harmony patch 目标解析」（TargetMethods 里 AccessTools.Method 合法），一刀切会误伤合法桥接。

### N-5 批次与管线
Batch1 静态/工具链（1+2+3+COMPAT 行）/ Batch2 IL 锚点（4）/ Batch3 SoftRef+foreignOwners（5+6）/ Batch4 需许可（in-engine 负向 COMPAT、0.113.1 注入、2p rejoin/交错抽牌、运行期 locale 扫描）。**管线注**：dllSha256 门禁＋locale parity 是 tools-only（不改 DLL）；PatchRegistry/IlBridge/SoftRef/COMPAT 行/TryCard 是 .cs→改 DLL→§G6 全管线。建议 **Batch1-3 合并一次重建＋一次全量 QA**（35/35+4/4+4/4＋0.113.1 预检＋verify＋ZIP/记录），而非每批重 QA。Batch4 游戏内负向/注入需作者许可。

### N-结论
诊断「总审总有是静态阅读的性质、不是 mod 的性质；真正风险是静默且远程失效；六门禁把这类压到近零；最大风险是门禁被绕过→需 lint」——成立，采纳。这是把「凭感觉摸」落成「可测＋防绕过」的正确方案。**本轮只核实＋反编译验证＋记录，未改 .cs。** 待作者：(a) 确认 F3 按 (a)(b) 诊断不裁决（还是要 (c) 推翻 F3）；(b) 版本归属 0.5.3；(c) 是否开 Batch1-3 合并重建＋全量 QA。

---

## O. 第六轮静态深挖复核（2026-11-19，只读核实＋反编译验证，未改 .cs）

> 第六轮负结果多于正结果（特意证伪），自更正上一轮两处。**总判定：成立，六轮里最克制；无「0.5.2 今天 0.113.0 主路径必炸」新问题。** 真正新增值得单记＝6.2 await 后收尾软锁 + 6.4 级联中毒；其余是把第五轮门禁设计落到具体行号。候选 2bc0f3a3 不受影响。

### O-1 新增成立项（核实）
| 编号 | 位置 | 判定 | 依据 / 修复 | 归批 |
|---|---|---|---|---|
| O-6.1 | `PresentationAdapter.cs:195-208` MapDrawingTranspiler + `:210-219` MapDrawingSend | 成立（K1/K2 漏网新实例） | build 注入 `Ldfld AccessTools.Field(NMapDrawings,"_playerCollection")`，字段改名→null→Ldfld null 不抛→IlBridge.ReplaceCall 只数替换数不验操作数→坏 IL 推到 JIT；MapDrawingSend:212 直接 `players.Players`，运行期 null→NRE 在原生方法内不在 Guard。今天字段存在、栈形正确（Inspect），非当前坏 IL，是改名/早调用边角。修：RequireField 失败返回 original＋MapDrawingSend players 判空退回 net.SendMessage＋_playerCollection 入 Precheck | Batch2/3 |
| O-6.2 | `RewardBridge.cs:63-75`（[PatchPolicy(true)]）+ `CardAcquisition.cs:205-225` Offer | **成立（本轮唯一今天条件可触发软锁）** | Prefix 同步 return false、__result=Offer(async)；Guard 只包同步 Apply。Offer:222 `await RewardsCmd.OfferCustom` 后 :223 `AccessTools.Method(EventModel,"SetEventFinished").Invoke`——改名→null.Invoke NRE / 事件奖励期被拆除断线→TargetInvocationException，均在返回 Task 里→Fail 不调用、hook 不禁用、RequiredFailure 仍 false→玩家选完奖励事件未标完成→**软锁事件房间**。需运行期负向用例（断线/拆除）确认，本轮未跑。修：接管前 SoftRef 验 SetEventFinished 可解析，不可解析 return true 走原生；可解析则 Offer 整体 try/catch→Fail("reward","PhilosopherRandomRewards")+写 reward.philosopher.finish_failed | Batch1(SoftRef)/3(AsyncGuard) |
| O-6.3 | `PatchRegistry.cs:87/113/162/166-170` + `ModEntry.cs:39/52` | **成立（fail-closed 核心里的 fail-open）** | 外层 try(:60-165) 只有 finally 无 catch；`OS.GetCmdlineArgs()`(:87) 与 `GetCustomAttribute<PatchPolicyAttribute>()`(:113) 在逐类 try(:116) 之外→任一抛→循环中断→finally appliedOnce=true(:169)→RequiredFailure(:162) 没执行停 false→补丁半装但 mod 认为无 required failure、角色可见。ModEntry:39 ApplyAll 无顶层 catch、:52 版本横幅无 try。修：ApplyAll 顶层 catch 置 RequiredFailure=true＋写 patch.apply_aborted；required/属性读取移进逐类 try；版本横幅单独 try/catch 退回程序集版本 | Batch1 |
| O-6.4 | `RegentAdaptations.cs:21-23` | 成立（K3 新实例，比 CharacterScope 危险因无兜底） | 3 个裸静态 FieldRefAccess 无 try/catch；`RegentAdaptations.Owns` grep **≈19 调用点/12 文件**（审计「21」略高）：SicEm×5/Osty/AncientShop/Abrasive/TransformationRules×2/PersistentCardKeywords/PresentationAdapter/CardAcquisition/StarterRelicEligibility/WaveEvents×2。任一字段改名→TypeInitializationException（缓存）→每次调用逐个被 Run 捕获禁用→**整片 Regent/Osty 适配级联掉线**非单点降级。修：OwnerField 复用 CharacterScope nullable accessor；StarBase/StarSet 懒解析 SoftRef，失败 Apply 跳过星费改写＋写一次 compat.reflection_missing | Batch3 |
| O-6.6 | `CardAcquisition.cs:332-339` PhilosopherColors + `CompatibilityPrecheck.cs:7-24` | 成立 | PhilosopherColors.Apply 把 `AccessTools.Method(ColorfulPhilosophers,"OfferRewards").Invoke` 放进 EventOption lambda(:337)，点击期执行完全在 Run 外→改名点击 NRE（=第二轮 B4 类）。Precheck 注释「one startup pass over the soft reflection assumptions」但只查 4 项＝**声明与实现不一致**；生产软反射点远不止 4（_playerCollection/SetEventFinished/GetCost/UpdateDirection/ENetHost 内部/NRelic._iconSize/_model/MerchantEntry._player…）。修：注入 helper 全函数化（判空退原生）＋Require* 转译期验操作数＋lint 禁 build 里裸 AccessTools.*；Precheck 遍历 SoftRef 注册表，checks 由注册表长度算不硬编码 4 | Batch1/3 |

### O-2 更正与计数（采纳诊断自更正＋我核出的小误）
- **6.3 ReleaseInfoManager 更正（采纳，并更正我第二轮 B8）**：反编译 `ReleaseInfoManager.cs:21` `Instance => _instance ?? (_instance = new ReleaseInfoManager())` 懒构造**永不为 null**；真风险是构造器 :35 `LoadConfig()` 文件读取/反序列化抛，或退出期 GD.Print 抛。我第二轮 B8「Instance 为 null→Initialize catch」措辞不准（应为「getter 抛→catch」）；Diagnostics.Initialize 有 try 会接住，但 **ModEntry:52 横幅无 try** 才是逃逸点。
- **6.5 async 续段是 6 不是 7（核实成立）**：NativeBridge `ref Task` 共 7 桥——UnarmBlock(CombatBridge:27)/GuardVigorConsumption(:40 Postfix)/SicEm(:90)/OstyAttack(:148)/AnimationSound(PresentationBridge:137)/WaveFacing(:257)/Philosopher(RewardBridge:68)。其中 **BridgeAnimationSound 只把 __result 换 Task.CompletedTask 无续段风险**，故续段风险 6 个。真正 mod 侧 await 后 NRE 是 UnarmBlock(SystemPowers:94 GetPower!) 与 Philosopher(6.2)；其余续段主要原生调用。门禁契约对 6 个都不成立（续段异常不 Fail/不禁用/不进 RequiredFailure）。修：`AsyncGuard.Run(adapter,hook,task)` try{await}catch{Fail;throw;}，Task<T> 只记录重抛不伪造返回。
- **6.7 GetPower! 计数小误**：生产 `GetPower<…>()!` 确仅 `SystemPowers.cs:94` 一处（核实）；但「其余 27 处都在 *Checks.cs」不准——`PlaytestRunner.cs` 另有 8 处（QA，非 *Checks.cs 命名）。实质（生产仅 1 处＝A4）成立。
- **6.4 Owns 21→≈19**、**6.1/6.6 行号**：核实近似成立，计数小偏差不影响判定。

### O-3 负结果（6.7，采纳；我独立核实了承重项）
生产 GetPower! 仅 SystemPowers:94；DateTime/Guid/new Random/TickCount 仅 Diagnostics 无玩法 RNG；ModelDb.GetId<T> 仅 (category,entry) 构造不查 _contentById→RelicWeights 静态初始化非毒化点；CardFactory.CreateForReward/CreateForMerchant 0.113.0 全同步→[ThreadStatic] CardFactoryScope 今天安全（仍应加「目标非 async」断言）；<Damage>d__13 仅 1 个 CustomScaledWait(:492)→当前 ReplaceCall 非静默 no-op，K1 风险是两个序号名非替换次数；DollRoom NextItem/StableShuffle 今天全 DollChoice→IsWind GetField("relic")! 安全，风险仅过滤过宽；GetAvailableDeque 返回活 deque→deque.Remove 有效无重复遗物 bug；Rng(Player,ModelId,ulong) 种子两端一致→F4 方向没问题、问题仍是 K7 位置不持久化；SeaGlassSelection finally 在首个 await 前(:321 vs :323)→[ThreadStatic] 今天安全。**这些证伪支持「主干已扫干净、剩最后一公里」结论。**（我独立核实了 GetPower!/Damage wait/Rng 种子/GetAvailableDeque 等承重项，其余 Inspect 负结果采纳未逐一重跑。）

### O-4 Batch1-3 增量（不改管线，只加四件）
1. SoftRef 收口追加 RegentAdaptations 三字段（6.4）＋ NMapDrawings._playerCollection（6.1）＋ EventModel.SetEventFinished（6.2）＋ ColorfulPhilosophers.OfferRewards（6.6）。
2. MapDrawing transpiler 同时做「字段解析失败返回 original」＋「MapDrawingSend 运行期判空」（6.1）。
3. AsyncGuard 按 **6 个**接（AnimationSound 不进清单）（6.5）；Philosopher Offer 接管前 SoftRef 预验＋整体 try/catch（6.2）。
4. PatchRegistry 顶层 catch＋GetCustomAttribute/OS.GetCmdlineArgs 移进逐类 try（6.3）；Precheck 遍历 SoftRef 注册表 checks 不硬编码（6.6）；ModEntry 版本横幅单独 try/catch（6.3）。

### O-结论
- **候选 2bc0f3a3 不受影响**：6.1/6.3/6.4/6.6 是改名/早调用/上游变更路径，6.2 是条件可触发软锁（需断线/拆除负向用例确认），均非「今天 0.113.0 必炸」。发布验收结论不变。
- **记录更正**：「所有软反射都已预检」目前**不成立**——应表述为「**4 项已预检，其余待 SoftRef 收口**」。CompatibilityPrecheck.cs:7-10 注释过度声明，接注册表后由长度算 checks。（台账 §M-K6 已记 4/≥10，此处统一口径。）
- 六轮追打到此收敛：新增项全部落在第五轮 Batch1-3 门禁设计的同一收口面上（SoftRef/IlBridge Require*/AsyncGuard/PatchRegistry fail-closed/Precheck 遍历）。**继续静态深挖的边际价值递减，建议切入实现。** 本轮只核实＋反编译验证＋记录，未改 .cs。待作者拍：F3 (a)(b) 诊断不裁决 / 版本 0.5.3 / 开 Batch1-3 合并重建＋全量 QA。

---

## P. 0.5.2 兼容性收口代码批（2026-11-19，作者「开始构建,版本号仍为0.5.2,不跑全量qa只做代码」）

> 作者拍板：版本仍 **0.5.2**（非 0.5.3），只做代码、不跑全量 QA。F3 默认 **(a)(b) 诊断不裁决**（未推翻 F3，不做 (c) UNSUPPORTED 隐藏角色）。本批落地第二~六轮收敛的玩家可见 P0 + Batch1-3 门禁收口。**编译 0 警告 0 错误；lint PASS；locale parity PASS；prepare_text 277 串。**

### P.1 新候选（code-only，QA 未跑）
- **DLL `97dddc561abfd19f487cee79008d20ca5483dc7a15c0db52583300c61672d543`**（1,173,504 B）
- **PCK `99a6c13b64400dce2c44f39fa11989556340e83104e6c075711cc4df7b6ae6b1`**（汤文案 locale 更新→PCK 变；277 资源）
- **JSON `9ceef44e94803370351bb8b7127674fd8046010ea685dde6356d4893349f6c38`**（不变，版本仍 0.5.2）
- 产物位置：`bin/Release` 与 `dist/PunchConstruct`（package.py 输出）已 97dddc56。**content/releases/0.5.2/verification.json/content-current.json 仍指向上一个已 QA 通过候选 2bc0f3a3**——本批未同步、未重 QA、未重传、未重建 ZIP（按作者指示暂缓）。**97dddc56 未经 QA 验证，不得当作发布候选。**
> **2026-11-24 追加**：后续代码批（统一健康状态、点击期 SoftRef、英文文案、工具收口、机械飞升预览、低风险清理）已使 `bin/Release` 变为 DLL **`7413d13d59bbb1e301d9845eb5e5150be66e2230cea6bb4440202bf949466f22`**（1,194,496 B）。P.1 的 97dddc56 仅代表 2026-11-19 时点，已被取代；`dist/PunchConstruct`、content、releases、verification、content-current 仍为旧候选，未同步。当前代码批仍未 QA，不是发布候选。

### P.2 已落地代码
- **玩家可见 P0**：A3 IronWaveForm 触发计数移入 `UnblockedDamage>0`（全格挡不再吃触发）；A7 GuardStance/AncientGuardStance 成功后再 `NextAmount++`；A4 SystemPowers:94 Summon 前判空 UnarmToArmsPower；A1 DelayedWave/Domineering **先付后清**（中途失败不丢剩余段）；A5 霸无敌 tooltip 逐段列 energy/cards/damage/block（去 FirstOrDefault 矛盾，模板改「每段」）；B2 DelayedWavePower.Description 走 Loc.Pick；B1 LeftHook RightTitle 走 Loc.Pick＋英文表补「右勾拳+」=Right Hook+；B3 两卡池 Title 走 Loc.Pick（补「观者」=Observer）；A6 汤文案「所有未附魔的小斩波」（AdaptedRelicVariants:246＋prepare_text:68＋english_text.json，全 16 locale 重生成）。
- **Batch1 PatchRegistry fail-closed**：required 读到 HasPatch 前；policy 读取失败按 Required 进 Failures；HasPatch 抛且 Required→Failures；ReflectionTypeLoadException 保留可加载类型＋roster 兜底；GetTypes 全失败→RequiredFailure=true；ApplyAll 顶层 catch→RequiredFailure=true＋patch.apply_aborted；OS.GetCmdlineArgs 防御；**ExpectedRequired 五桥 roster**（BridgeUnarmBlock/ConstructRelicVariants/ConstructRelicMigration/PhilosopherRandomRewards/ConstructColorlessShop）缺任一→fail-closed；seam `ApplyAll(IEnumerable<Type>)`＋纯函数 `MissingRequired`；ModEntry 版本横幅 try/catch（ReleaseInfoManager 懒构造非 null，风险是 LoadConfig 抛）。
- **Batch1 F3(a)(b) COMPAT 启动行**：`[PunchConstruct COMPAT] PASS/DEGRADED`（soft-ref n/N、patch failed、il skipped、adapter disabled、pool missing）＋compat.startup JSON＋max_tested game 0.113.1/BaseLib 3.4.8 元数据。**只诊断，不门禁/不隐藏/不弹窗**（尊重 F3）。
- **Batch2 IlBridge 收口**：新增 RequireField(owner,name,expectedType)/RequireCall/ReplaceCall(expectedCount)/ReplaceAll(MethodInfo＋谓词两版)/Skip＋Skipped 计数；GuardDamagePresentation **去两个序号名**→IL 位置+类型锚点取 results、用 `DamageResult.OriginalTarget` 砍掉 targets 依赖（替身边界保守）；GuardStancePowerGain 逐 target RequireField(power)＋Wait 判空＋Targets<2 告警（!IsGenericMethod 承重墙可见化）；MapDrawing RequireField(_playerCollection)＋MapDrawingSend 运行期判空＋Single→FirstOrDefault；CardAcquisition/TransformAffinity 迁 ReplaceAll 谓词版（0 匹配写 skipped）；FakeMerchantExclusions/WindDollPriority RequireField＋0 匹配日志＋IsWind/Shuffle 判空。
- **Batch3 SoftRef 懒解析**：新增 `SoftRef.cs`（Ref<T> 懒解析，构造只存 lambda 不毒化；Descriptor 首次使用解析+失败 null+日志；Registry/ProbeAll/Count/Unresolved）；迁移 RegentAdaptations×3（6.4 级联中毒点，Owns 降级 false/星费跳过）、GuardVigorConsumption GetData/Command/StartingAmount、PresentationAdapter IconSize/IconModel/EntryPlayer/PackedOutline；**CompatibilityPrecheck 遍历注册表**（force-init 消费类型＋GetAvailableDeque/AttackCommand×3 探针，checks 由 SoftRef.Count 算不硬编码 4，暴露 Degraded/Unresolved/Total）。
- **Batch3 AsyncGuard**：新增 `AsyncGuard.cs`（Wrap/Wrap<T> try{await}catch{Fail;throw;}，不伪造 Task<T> 返回）；接 6 桥 UnarmBlock/GuardVigorConsumption/SicEm/OstyAttack/WaveFacing/Philosopher（AnimationSound 不进清单）；**Philosopher 6.2 软锁修**：接管前 SoftRef 验 SetEventFinished，不可解析 return true 走原生；Offer 收尾 try/catch→reward.philosopher.finish_failed＋rethrow 让 AsyncGuard 熔断。
- **Batch3 foreignOwners**：PatchRegistry.Audit 对每个已补丁方法 `Harmony.GetPatchInfo(method).Owners` 报非本 mod owner（try/catch，patch.foreign_owners）。
- **K6**：GenerateAllCards 改 `TryCard<T>`（收集缺卡名+跳过+error+PoolDegraded，**不 throw 到池外**）；NativeCardOwnership 只在**原生游戏程序集**池里解析（第三方全卡池不能凭枚举顺序抢归属）。
- **门禁/工具**：qa_logs.archive_diagnostics 增 expected_dll，**每 session dllSha256 必须==候选哈希（all 非 any）**闭环；check_current/check_coop(×2)/check_recovery(×2) 传 expected_dll；新增 `tools/check_locale_parity.py`（16 locale×4 表对 eng key 对齐，缺/多 fail）＋`tools/lint_compat.py`（禁裸 FieldRefAccess/inline Method().MakeGenericMethod 于 SoftRef 外、禁 ConstructPools 裸 ModelDb.Card<；区分 Harmony 目标解析合法）。
- **负向用例**：CompatibilityChecks.RequiredRosterNegative（MissingRequired 完整 roster→空、缺桥→逐个报缺）。
- **2026-11-24 收口（同 0.5.2，code-only）**：
  - **统一健康状态**：新增 `Health.cs`，把 Required 补丁失败、启动后意外 `IlBridge.Skip`、启动禁用适配器、`CombatRelicStateDegraded`、Precheck 异常、`ConstructCardPool.PoolDegraded`、`RelicWeights.Degraded` 合并为一条 COMPAT PASS/DEGRADED；`CompatibilityChecks` 新增 `PoolHealth()`（强制建池）与 `HealthGate()`；`ModEntry` 输出 reasons 与 compat.startup 字段。
  - **点击期反射**：`WaveEvents` Finish/Bugslayer/Amalgamator/InfestedAutomaton、`PhilosopherColors/RandomRewards`、`ConstructDustyTome`、`AncientShop` setter/GetCost、`CharacterText` NextButton/AncientDialogue staging、`TransformationRules`、`DelayedWaveFacing`、`PresentationAdapter` 肖像/网络/大厅、`RelicAcquisition` GetAvailableDeque/treasure/WindDoll 全部迁 `SoftRef` 或 `IlBridge`；解析失败保留原生行为，不再裸 `Invoke`。
  - **池/权重**：`ConstructPools.TryCard` 的日志兜底不向池外抛；`NativeCardOwnership` 优先保留已是原生池的 `__pool`；`RelicWeights` native ModelId 改 `Lazy` 逐项 try/catch 并暴露 `Degraded`；`PoolShutdown` 反射判空降级返回 0。
  - **英文**：`6Block`→`6 Block`（`"格挡"` 改为 `" Block"`）；新增 `Loc.Count` 修 `draw 1 card(s)`；`prepare_text.py` 输出 277 串，16 locale 同步。
  - **工具**：`lint_compat.py` 修 `MakeGenericMethod` 嵌套/跨行假阴性，扫描 `tools/NativeProbe/PoolShutdown.cs`，新增静态初始化与生产运行期反射规则；`report_local.py` 按 DLL hash 归档同版本报告；`run_all_checks.ps1` 未显式传 `--package-dir` 时默认测试新构建，不再回落到旧候选。
  - **低风险清理（2026-11-24）**：删除 `CombatRelicState.NativeHelmetUsed`（仅声明 + `CopyOnClone`，全仓库无读写；原生 `RuinedHelmet` 已由变体替换为 `ConstructHelmet`）；两处 `(CardCreationFlags)17` 改为具名 `CardAcquisition.EventRewardFlags = NoRarityModification | NoCardPoolModifications`（与原生 SeaGlass/ColorfulPhilosophers 完全一致）；`report_local.py` 新增 `stale_checks`/`stale_multiplayer`/`stale_recovery` 与 `stale_evidence_count`，旧 DLL 证据不再静默消失但仍不计入通过，缺当前证据继续走 `missing_*` fail-closed。D4 经反编译确认 BaseLib `CustomFrameMaterial` 已按实例缓存，无需改；D5 现有 QA 已断言原生无色商店 2 槽。
  - **边界**：本批只做代码，未跑全量 QA、未同步 content/releases/verification、未重传；游戏内行为未验证。

### P.3 本批未做（留档，待后续）
- **B4 EnglishChecks 运行期英文描述扫描**（系统 Loc 门禁，防未来新泄漏）：未做；B1/B2/B3 实际泄漏已修。
- **A1 pending 持久化（SavedSpireField）/ K7 WeightStream 位置持久化**：架构项，归 F4 同批待作者批准（先付后清已做，持久化未做）。
- **A2 SpinningScrew AfterModifyingHandDraw 无 player 参数**：2026-11-24 已静态核实为非缺陷（原生只对计数变化的监听者调用 After 钩子）；仅补注释，2p 交错抽牌仍作为非阻塞回归建议。
- **D2 CopyState SavedSpireField 迁移**：仍未做；变体替换发生在获取期，当前 `HelmetUsed` 默认 false 故无害，新增带 SavedSpireField 的变体时需补迁移。
- **L-D1/L-D3/L-D4/L-D5**：2026-11-24 已收口或核实（见 P.2）；D4 被 BaseLib 实例级 material 缓存覆盖，D5 已有原生 2 槽 QA 断言。
- **C5 StackTrace  redesign / K5 反向跨 mod 顺序测试 / NativeCardOwnership 假池负向 / 运行期 locale 扫描**：需设计或 in-engine（Batch4，需许可）。
- **report_local C3 stale 行可见性**：2026-11-24 已收口（见 P.2）；旧构建证据列出但不计数，缺当前证据仍 fail-closed。
- **F3 (c)**：未做（需作者明确「推翻 F3」）。

### P.4 下一步（待作者）
全量 QA（35/35+4/4+4/4＋0.113.1 预检）→ content 同步 → releases/0.5.2 重建＋ZIP＋SHA256 → workshop --verify → verification.json/content-current.json 切当前 QA 通过的构建（当前代码批为 7413d13d；若 QA 前再改则以实际重建为准）→ 私测重传 3835179658（需 Steam）→ 公开待许可。**在 QA 通过前当前构建不是发布候选；2bc0f3a3 仍是最后已验证候选。** 本批编译/lint/parity 绿，但游戏内行为未验证。

### P.5 2026-11-24 追加：机械飞升预览数值
- **现象**：1 点力量时卡面只显示 13（12＋原生 1），实际结算为 17（12＋5×1）；格挡同样少显示。
- **根因**：`ConstructMechanicalAscension.OnPlay` 先预加 `(Scale-1)×力量/敏捷`，再交给原生管线加自身的 +1；卡面 `DamageVar/BlockVar.UpdateCardPreview` 只看裸基础值 12，因此只显示原生 +1。
- **修复**：`ClockworkCards.cs` 新增 `ScaledDamageVar`/`ScaledBlockVar`，用与出牌相同的“放大后基础值”重跑原生预览；虚弱/易伤等乘区仍作用于整体，升级后的 Scale=8 自动生效。`CanonicalVars` 改用两者。
- **QA**：`AdditionsChecks.cs` 增断言（2 力量/3 敏捷时预览 22/27，与实际结算一致）。仅代码、编译 0 警告 0 错误、lint PASS；游戏内未验证。
- **当前构建**：DLL `7413d13d59bbb1e301d9845eb5e5150be66e2230cea6bb4440202bf949466f22`（1,194,496 B）；未 QA，不是发布候选。**已被 §Q 的 `be3fdda6…` 取代。**

---

## Q. 0.5.2 最终 QA 与发布同步（2026-11-24 晚）

> 作者授权重建并跑全量 QA。版本保持 0.5.2，F4 RNG 架构与 F3(c) 均未动。最终候选 **DLL `be3fdda61040d0c0c892abc1908b8abaeadaa0340701874a6e3f92e824d89312`（1,195,008 B）**、PCK `99a6c13b64400dce2c44f39fa11989556340e83104e6c075711cc4df7b6ae6b1`、JSON `9ceef44e94803370351bb8b7127674fd8046010ea685dde6356d4893349f6c38`（277 资源）。**本地 35/35、联机 4/4、恢复 4/4 全部 PASS**；`current-verification.json` 为 `full_local_verification=true`、`full_invariant_verification=true`（16/16）。精确候选包 35/35 重跑（带 `[Construct QA PACKAGE]` 哈希），`workshop.py --verify` 全 PASS；`0.113.1` 隔离预检 6/6 PASS。ZIP 已重建为 **51,363,405 B / SHA256 `13be71eabd5a695e96f5101e7a9f35ed10b20e95535d163ca2cff9a3cdb1f331`**。

### Q.1 本轮代码与门禁修正
- `Health.IgnoreDeliberateSkips(int)`：`CompatibilityChecks.TranspilerDegradation` 故意触发 3 次 `IlBridge.Skip`（缺目标／重复目标／重写异常），只把这 3 次推进启动基线；其他启动后跳过仍判 DEGRADED。compat 67 项 PASS。
- `AdditionsChecks` 机械飞升预览：先把卡放进手牌再 `UpdateDynamicVarPreview`，使原生 `runGlobalHooks` 生效；2 力量／3 敏捷预览 22/27 与实际结算一致。additions 142 项 PASS。
- `check_documents.py`：卡表解析同时接受 `ModelDb.Card<...>()` 与池内 `TryCard<...>()` 并排除泛型 `T`；营养汤对白补「未附魔的」；官方术语比较先去标签／空白，避免 `" Block"` 这类拼接片段误报。docs 全 PASS。
- `lint_compat.py` PASS；`check_locale_parity.py` PASS（16 locale × 4 表）。

### Q.2 QA 与证据
- 官方基线：game 0.113.0 / BaseLib 3.4.8。
- 全量：local 35/35（含 additions 142、compat 67）；coop 4/4；recovery 4/4。
- `coop-necrobinder-3p` 首轮在 `RIGHT_HOOK` 后报 checksum ID 38 分歧并伴随客户端 NRE；同一 DLL 隔离重跑 83 项 PASS，期间未改玩法代码，按瞬时校验和分歧记录（与 0.4.24／0.4.28 的同类抖动一致）。
- 精确候选包：`python tools/check_local.py --package-dir dist/SteamWorkshop/content` 35/35 PASS，日志带 `[Construct QA PACKAGE]` 三件套哈希。
- `python tools/workshop.py --verify`：`static_package=PASS`、`targeted_package_checks=PASS`、`network_multiplayer=PASS_LOCAL_ENET`、`missing_multiplayer_checks=[]`、`upload_status=NOT_UPLOADED`、`existing_local_play_files_unchanged=true`。
- `0.113.1` 隔离预检（`tools/runtime-0.113.1`，不碰 0.113.0 基线）：startup／smoke／additions／pool／modes／selection **6/6 PASS**。

### Q.3 发布同步
- `dist/SteamWorkshop/content`、`dist/releases/0.5.2/`、`dist/PunchConstruct/`、`tools/runtime/workshop-test-0.5.2/content` 均为同一三件套；277 资源。
- `dist/releases/PunchConstruct-0.5.2.zip`：**51,363,405 B，SHA256 `13be71eabd5a695e96f5101e7a9f35ed10b20e95535d163ca2cff9a3cdb1f331`**，三文件位于根目录、与 content 逐字节一致。
- `content-current.json` 已切到新构建，`previous_build=2bc0f3a3…`，`full_release_acceptance=false`、`local_acceptance=true`、`uploaded=false`。

### Q.4 仍待作者／环境
- 私测条目 `3835179658` 重传：**PENDING**（Steam 未运行）；暂存已就绪，上传后需 `steam_workshop_check.py --item 3835179658 --subscribe-download` + `verify_previews.py 3835179658`。
- 公开 `0.5.2`：需作者明确许可 + `0.113.1` 实机确认；封面版权仍为 `UNRESOLVED`（用户确认无 Watcher 作者授权，署名不等于授权）。
- `full_playthrough`、`steam_invites`、`steam_hot_rejoin`、`workshop_subscription` 仍为 `NOT_VERIFIED`；`full_release_verification=false` 属预期。
- F4／A1／K7、Batch4 in-engine 项、D2 迁移仍按原队列等待批准。

### Q.5 验收收尾（2026-11-25 上午，只读复核，未改 .cs／未重跑引擎）
- **修正一处证据时序缺陷**：`current-verification.json` 原写于 2026-11-24 20:04:18，比 `dist/SteamWorkshop/verification.json`（20:04:37）早 19 秒，因此记成 `candidate.local_verification=false` 且 `verification_sha256=53f667ee…`（旧报告）。重跑 `python tools/report_local.py` 后按当前候选报告重算：`candidate.matches_current_files=true`、`candidate.local_verification=true`、`verification_sha256=23254535068c27ac19c938e798201ae0479d50a2df1d43a9003ee72b6e2af603`；`upload_status=NOT_UPLOADED`、`uploaded=false`、`full_release_verification=false` 不变。
- **构建可追溯**：源码最后修改 `2026-11-24 19:10:44`（`Health.cs`／`CompatibilityChecks.cs`／`AdditionsChecks.cs`），`bin/Release/net9.0/PunchConstruct.dll` 构建于 `19:11:07`，哈希 `be3fdda6…`、1,195,008 B、文件版本 `0.5.2.0`，与候选一致，无构建后改源码。
- **四处同步点逐字节一致**：`dist/SteamWorkshop/content`、`dist/releases/0.5.2/`、`dist/PunchConstruct/`、`tools/runtime/workshop-test-0.5.2/content` 的 DLL／PCK／JSON 哈希均为 `be3fdda6…`／`99a6c13b…`／`9ceef44e…`。ZIP 内只有根目录三文件，逐字节等于 content；`51,363,405 B`、SHA256 `13be71ea…` 与 §Q.3 一致。
- **工具门禁复跑**：`check_documents.py` 10 项全 PASS；`lint_compat.py` PASS；`check_locale_parity.py` PASS（16 locale × 4 表）。
- **状态复核**：`workshop.py --status` 为 `package_matches_latest_build=true`、`resources_match_latest_source=true`、`verification_matches_candidate=true`、`description_synced=true`、`locally_recorded_upload_status=NOT_UPLOADED`；35 个精确候选本地检查全 true，8 个候选联机／恢复场景全 PASS，`missing_candidate_multiplayer=[]`。
- **结论**：0.5.2 本地验收到此收口（构建→全量 QA→精确包→verify→归档→文档一致）。剩余全部在上传侧：私测 `3835179658` 待 Steam 可用，公开待作者许可 + `0.113.1` 实机确认；`full_release_verification=false` 与四条 `NOT_VERIFIED` 轴属预期，不因本次收尾改变。

---

## R. 0.114.0 适配验收（2026-11-27）

> 目标切换为 0.114.0（Steam public-beta，build 26909018、commit a7b9a9c0）；版本保持 0.5.2，F4 RNG 架构与 F3(c) 未动。0.113／0.111 只保留代码级兼容保护与历史证据，不做真实旧版回归。

### R.1 适配内容
- 三张愚者搬运牌 `ChoBarbs`／`ChoReady`／`WahChoKi` 在 0.114 被数值削弱；改为 mod 自有 `CustomCardModel` 副本，保留 0.113 模组数值，ID 为 `PUNCHCONSTRUCT-CHO_BARBS`／`CHO_READY`／`WAH_CHO_KI`。
- 愚者卡运行时 `Pool` 覆盖为 `ModelDb.CardPool<FoolCardPool>()`，保留 `[Pool(typeof(ConstructCardPool))]` 仅用于获取，卡框不再显示构装体蓝框。
- Brain Leech 在 `NoCardPoolModifications` 下仍收窄 colorless-only 池；其他跳过标记来源保持原生。
- `DowngradeInternal` 在 0.114 改为逐级降级；QA 用 `NativeCardOps` 反射保留 0.113 全重置语义，不新增生产兼容补丁。
- `Murky Oil` 保持 Vigor-only 回退，维持 0.111 兼容。

### R.2 QA 与证据
- 隔离运行时 `tools/runtime-0.114`（游戏 0.114.0 / BaseLib 3.4.8，不碰 0.113.0 基线与正式存档）。
- 本地 35/35（5020 断言、30 套件项）、精确包 35/35（`[Construct QA PACKAGE]` 三件套哈希）、联机 4/4（2p 构装体、3p 死灵、4p 构装体、4p 弱网 100ms／40ms／5%／5s）、恢复 4/4（菜单 2p、强制 2p、进程丢失 4p 倒序、主机丢失 3p 混组倒序）全部 PASS。
- `report_local.py --runtime-dir tools/runtime-0.114`：`full_local_verification=true`、`full_invariant_verification=true`、不变量 16/16、`stale_evidence_count=0`。
- `workshop.py --verify --runtime-dir tools/runtime-0.114`：`targeted_package_checks=PASS`、`network_multiplayer=PASS_LOCAL_ENET`、`missing_multiplayer_checks=[]`、`upload_status=NOT_UPLOADED`、`game=0.114.0`。
- `workshop.py --status --runtime-dir tools/runtime-0.114`：35 项精确候选检查全 true、`missing_candidate_multiplayer=[]`、`verification_matches_candidate=true`、`description_synced=true`。
- `lint_compat.py` PASS；`check_documents.py --runtime-dir tools/runtime-0.114` 10/10 PASS；`check_locale_parity.py` PASS（16 locale × 4 表）。
- RecoveryProbe／RecoveryNativeProbe 已按 0.114 `sts2.dll` 重建（`CardModel.UpgradeInternal(bool)`）；0.113 旧探针的 `MissingMethodException` 证据归档在 `tools/runtime-0.114/qa-archive/20261127-acceptance`，不计入验收。

### R.3 发布同步
- `dist/releases/PunchConstruct-0.5.2.zip` 由 `release.py` 确定性重建：51,364,889 B、SHA256 `fb4e66d00f60e83de25a170a34f6c54db542bb706e3a16d46421b6fb454c6ee8`，三文件位于根目录，`release.py --check` PASS。
- `dist/SteamWorkshop/content`、`dist/releases/0.5.2/`、`dist/PunchConstruct/`、`tools/runtime/workshop-test-0.5.2/content` 与候选三文件逐字节一致（DLL `0a6ea643…`、PCK `99a6c13b…`、JSON `9ceef44e…`，277 资源）。
- `dist/SteamWorkshop/verification.json`、`tools/runtime-0.114/current-verification.json`、`content-current.json` 与文档已切到 0.114 候选；旧 QA 目录 70 个旧构建、4 个失败探针、7 个中断目录已归档，不计入统计。

### R.4 待作者／环境
- 私测条目 `3835179658` 重传仍为 **PENDING**（Steam 未运行）；暂存已同步到 `0a6ea643`。
- 公开 `0.5.2` 待作者明确许可；公开条目 `3826907330` 仍为 0.5.1。
- `full_playthrough`、`steam_invites`、`steam_hot_rejoin`、`workshop_subscription` 仍为 `NOT_VERIFIED`；`full_release_verification=false` 属预期。
- 0.114 是当前常青目标；0.113／0.111 不做真实旧版回归。