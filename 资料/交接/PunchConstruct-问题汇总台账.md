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

- **G1 = #30 GitHub 完整发布**：BLOCKED 待作者 GCM 自认证。**已暴露的 PAT（ghp_…）必须撤销**，令牌绝不以明文出现在命令/文件/仓库/远程 URL。排除 `tools/runtime-0.113.1/`、`tools/_refscratch/`、含 SteamID 的日志压缩包；**不 git add -A**。流程：commit + push main + v0.5.1 tag + Release 附 ZIP。
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
4. **§G1 GitHub**：待作者 GCM 认证 + 撤销暴露 PAT 后执行。

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