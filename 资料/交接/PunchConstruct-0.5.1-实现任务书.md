# PunchConstruct 0.5.1 实现任务书

> 交接对象：接手实现的 AI / 开发者  
> 工作区：`c:\Users\li177\Documents\铁斩波工作区`  
> 需求来源：`C:\Users\li177\Desktop\草稿纸2.txt`（0.5.1 内容草稿）  
> 目标：Slay the Spire 2 **0.113.0** + **BaseLib 3.4.8**，mod 版本 **0.5.1**  
> **0.4.28 已发布并冻结，禁止重建、重传或修改 0.4.28 的产物。**  
> **版本说明：原 0.4.29 开发批次自本版起统一改称 0.5.1；旧文档中的“0.4.29”均指本任务书。**

---

## 0. 先读这里

这份文档是给另一个 AI 直接执行用的，不是讨论稿。开始前必须：

1. 读完第 1 节「硬约束」和第 8 节「阻塞确认」。
2. 先做 0.113 / BaseLib 3.4.8 的基线构建，确认现有代码能编译。
3. 不要先写新内容；先确认第 8 节里的事件 ID 和古牙升级行为。
4. 不要碰 0.4.28 的 `dist/releases/0.4.28`、Steam 工坊包和 `dist/SteamWorkshop/content`。
5. 不要 `git add -A`；仓库根目录可能有含 SteamID 的日志压缩包。

---

## 1. 硬约束

### 1.1 只允许模组角色获得新内容

所有新增卡牌、遗物、药水、事件改版，只对 `ConstructCharacter`（拳击构装体）生效。

- 新卡必须 `[Pool(typeof(ConstructCardPool))]`。
- 新遗物必须 `[Pool(typeof(ConstructRelicPool))]`。
- 新药水必须 `[Pool(typeof(ConstructPotionPool))]`。
- 卡牌的 `Pool` 属性可以指向原版池用于美术/本地化来源，但**不能把新卡 ID 加进原版池的 `AllCardIds`**。
- 原版角色（铁甲战士、静默猎手、储君、亡灵契约师、故障机器人）不能在任何奖励、商店、事件、随机生成、飞溅、发现里看到这些内容。
- 原版角色的事件文本/选项必须保持原样；只有构装体走改版。

### 1.2 能用模型就不要补丁

优先用：

- `CustomCardModel`
- `CustomRelicModel`
- `CustomPotionModel`
- 现有事件适配入口（`CharacterText` / `NativeBridge/EventBridge`）

只有以下原生入口保留薄补丁，并必须加 QA：

1. **飞溅 / 发现**的卡牌生成过滤。
2. **古老牙齿**的原生替换映射。
3. **PUNCH_OFF**（原生拳击构装体事件）的选项替换。

不要为“新卡效果”写 Harmony 补丁；新卡效果应写在卡牌/能力模型里。

### 1.3 只读游戏文件

- 游戏目录：`D:\game\steam\steamapps\common\Slay the Spire 2`
- 可以读取 `sts2.dll`、`SlayTheSpire2.pck`、本地化 JSON。
- 不要修改游戏安装目录、不要替换游戏 DLL/PCK。

### 1.4 图片尺寸

| 用途 | 目标 |
|---|---|
| 卡图 | **1000×760**，RGB，无透明 |
| 遗物图标 | 128×128 |
| 遗物大图 | 256×256 |
| 药水 | 256×256，RGBA |
| 能力图标 | 64×64 与 256×256 |

`prepare_art.py` 对卡图比例差超过 1% 会补黑边；能精确 1000×760 就精确，不能就中心裁切，不要留黑边。

---

## 2. 环境基线

| 项 | 值 |
|---|---|
| 游戏 | StS2 **0.113.0** |
| BaseLib | **3.4.8**（`BaseLib.dll` FileVersion 3.4.8.0） |
| BaseLib 路径 | `D:\game\steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll` |
| 项目 | `PunchConstruct\PunchConstruct.csproj` |
| 目标框架 | `net9.0` |
| 当前版本 | 0.4.28 |
| 目标版本 | 0.5.1 |

### 2.1 基线任务

- [ ] `PunchConstruct.csproj`：`<Version>` 改为 `0.5.1`。
- [ ] 确认 `GameDir` 指向 0.113 安装目录。
- [ ] 确认 `BaseLibDir` 指向 3.4.8 的 `BaseLib.dll`。
- [ ] `ModEntry.Initialize` 的日志从 `game 0.111.0 / BaseLib 3.4.7` 改为 0.113.0 / 3.4.8；最好读取运行时版本，不要硬编码。
- [ ] `dotnet build PunchConstruct\PunchConstruct.csproj -c Release`，修 0.113 API 变更。
- [ ] 跑一次冒烟，确认 `patch.summary` 的 Required 补丁全绿。

---

## 3. 用户已确认的决定

| 编号 | 决定 |
|---|---|
| 1 | **机械飞升**的“每有 1 点力量/格挡”按 **力量 / 敏捷** 实现；伤害吃力量，格挡吃敏捷。 |
| 2 | **与我一斗**升级后伤害 6×2，并且保留敏捷 +1。 |
| 3 | **先古架势**只改效果，不改名；游戏内名称统一为 **古铜之拳**。普通架势从 1 开始，古铜之拳从 **5** 开始。 |
| 4 | **霸无敌**是**技能**，打出后下回合开始时触发。 |
| 5 | 所有新增内容**只进构造体池**；原版角色不能也不应出现。 |
| 6 | 0.113 已有 **拉伸**：`StretchPower`，下一次卡牌获得格挡时额外获得等同层数的格挡，触发后消耗。 |
| 7 | **缺陷构装体**是原生事件改版，背景图不变；按现有事件改版方式处理。 |
| 8 | 机械飞升、霸无敌的现成卡图在素材文件夹里：`资料/美术素材/卡牌/机械飞升.png`、`资料/美术素材/卡牌/霸无敌.png`（霸无敌 2026-11-17 换交接新美术，旧 `.jpg` 归档）。 |
| 9 | **发条机关** 2 费，升级后 1 费。 |
| 10 | **不倒不休**：回合开始时只加入 1 张拳斗；不再生成灼烧。当前实现没有“首次受伤加灼烧”逻辑，本次只需删除灼烧生成并同步文案。 |

---

## 4. 代码地图

| 文件 | 作用 |
|---|---|
| `PunchConstruct/Cards.cs` | 小斩波、东尼斩波、与我一斗、灼热铁斩波、铁斩波形态等 |
| `PunchConstruct/BoxingCards.cs` | 无色/构装体系卡牌、`[Pool]` 模式 |
| `PunchConstruct/SystemCards.cs` | 不倒不休等系统牌 |
| `PunchConstruct/SystemPowers.cs` | 不倒不休能力、系统能力 |
| `PunchConstruct/GuardStance.cs` | 普通架势 |
| `PunchConstruct/AncientAdaptations.cs` | 古铜之拳、古老牙齿映射、先古替换 |
| `PunchConstruct/RegentAdaptations.cs` | 伽马爆破、彗星等储君卡费用/伤害适配 |
| `PunchConstruct/WorkshopRelics.cs` | 应急油壶等遗物 |
| `PunchConstruct/ConstructPools.cs` | 构造卡池、遗物池、药水池 |
| `PunchConstruct/CardAcquisition.cs` | 卡牌获取、`GroupCardGates`、`CardFactoryScope` |
| `PunchConstruct/CharacterText.cs` | 事件文本适配（含 `ConstructPunchOffText`） |
| `PunchConstruct/NativeBridge/EventBridge.cs` | 事件桥 |
| `PunchConstruct/WaveEvents.cs` | 事件规则、`WaveEventRules.Finish` |
| `PunchConstruct/RelicAcquisition.cs` | 遗物获取过滤 |
| `PunchConstruct/Loc.cs` | 中英文文本包装 |
| `PunchConstruct/EnglishText.Generated.cs` | 英文翻译表 |
| `PunchConstruct/assets/localization/` | 事件/遗物/卡牌 JSON |
| `tools/prepare_art.py` | 卡图/遗物图/状态图标导出 |
| `tools/check_*.py` | QA 与发布检查 |

---

## 5. 详细任务

### 5.1 现有内容修改

#### 5.1.1 先古架势 / 古铜之拳

文件：`PunchConstruct/AncientAdaptations.cs`

当前：

- `AncientGuardStance` 使用 `GuardStrengthNextTurnPower`，每次完全格挡给下回合临时力量。
- `NextAmount` 从 1 开始。

目标：

- 效果改成和 `GuardStance` 一样：每次完全格挡，获得 `VigorPower`，数值递增。
- 普通架势从 1 开始；古铜之拳从 **5** 开始。
- 每场战斗开始/结束都重置到各自起点。
- 文案：  
  `每次完全格挡伤害，获得X点[gold]活力[/gold]。此效果本场战斗中每次触发后+1。`
- 名称统一为 **古铜之拳**。
- 确认 `GuardStrengthNextTurnPower` 是否还有别的使用；没有就删除或保留但不引用，避免死代码。

QA：

- 普通架势第一次完全格挡给 1 活力，第二次 2，依此类推。
- 古铜之拳第一次给 5，第二次 6。
- 部分格挡不触发、不推进。
- 下场战斗重置。

#### 5.1.2 与我一斗

文件：`PunchConstruct/Cards.cs` 的 `FightMe`

当前：

- `DamageVar(6)`，造成 6×2。
- 升级只给敏捷 +1，伤害保持 6。

目标：

- 基础伤害改为 **5**，即 5×2。
- 升级后伤害 **6×2**，同时保留敏捷 +1。
- 升级实现：`DynamicVars.Damage.UpgradeValueBy(1)` 和 `DynamicVars["Dexterity"].UpgradeValueBy(1)`。
- 文案自动使用 `{Damage:diff()}`，检查升级前后显示。

QA：

- 未升级 5×2；升级 6×2；敏捷 2→3。
- 敌人获得的 1 点力量不变。

#### 5.1.3 不倒不休

文件：`PunchConstruct/SystemCards.cs` 的 `NeverBackDown`，`PunchConstruct/SystemPowers.cs` 的 `NeverBackDownPower`

当前：

- 每回合开始，把 1 张拳斗加入手牌，1 张灼烧加入弃牌堆。
- 卡牌文案同此。

目标：

- 每回合开始时，只把 **1 张拳斗**加入手牌。
- 不再生成灼烧。
- 当前代码没有“首次受伤加灼烧”逻辑，无需额外删除；只清理文案中残留的灼烧描述。
- 卡牌与能力文案同步改为：`回合开始时，将一张[gold]拳斗[/gold]加入你的手牌。`

建议实现：

- `NeverBackDownPower.AfterPlayerTurnStart` 只保留 `Fisticuffs` 生成逻辑。
- 删除 `Burn` 生成；`AfterDamageReceived` 的首次受伤逻辑当前不存在，无需处理。
- 保留现有的 `PileType.Hand` 添加与预览行为。

QA：

- 回合开始手牌多 1 张拳斗。
- 弃牌堆不增加灼烧。
- 受到伤害不触发任何额外效果。
- 卡牌与能力文案不再提到灼烧。

#### 5.1.4 伽马爆破 / 彗星

文件：`PunchConstruct/RegentAdaptations.cs`

当前：

```csharp
[typeof(GammaBlast)] = (2, 3),
[typeof(Comet)] = (3, 6),
```

目标：

- 伤害增量改为 0：
  - `GammaBlast` → `(2, 0)`
  - `Comet` → `(3, 0)`
- 费用/星费转换保留；不再增加伤害。
- 不要动其他储君卡的费用适配。

QA：

- 对比原生 `DynamicVars.Damage.BaseValue`，构装体与原生角色数值一致。
- 费用仍按适配后的 2/3。

#### 5.1.5 应急油壶

文件：`PunchConstruct/WorkshopRelics.cs` 的 `EmergencyOilCan`

当前：

- 回合结束有未使用能量时，随机一名敌人获得 1 易伤 + 1 虚弱。
- 文案写“一名随机敌人”。

目标：

- 对**所有敌人**施加 1 易伤 + 1 虚弱。
- 删除 `Owner.RunState.Rng.CombatTargets.NextItem` 的随机选择。
- 文案改为“所有敌人”。

QA：

- 3 个敌人时，3 个都获得 1 易伤 + 1 虚弱。
- 没有敌人时不触发。
- 没有未使用能量时不触发。

#### 5.1.6 古老牙齿 → 霸无敌

文件：

- `PunchConstruct/AncientAdaptations.cs`
- `PunchConstruct/assets/localization/zhs/relics.json`
- `PunchConstruct/assets/localization/*/relics.json`（其他语言同步）

当前：

- `ScopedAncientToothTransformation`：`IronWave` → `ScorchingIronWave`，并强化 3 次。
- `AncientWaveMapping`：`IronWave` ID → `ScorchingIronWave`。
- `relics.json`：`PUNCHCONSTRUCT_ADAPTED_ARCHAIC_TOOTH.description` 写“铁斩波→灼热铁斩波”。

目标：

- 古牙替换目标改为 **东尼斩波 → 霸无敌**。
- `ScopedAncientToothTransformation`：
  - 类型判断从 `IronWave` 改为 `TonyWave`。
  - 保留“仅构装体”的 owner 判断。
  - 结果从 `ScorchingIronWave` 改为 `ConstructDomineering`。
- `AncientWaveMapping`：
  - 映射 `ModelDb.GetId<TonyWave>()` → `ModelDb.Card<ConstructDomineering>()`。
  - 删除/替换 `IronWave` 的映射。
- `relics.json` 描述改成“将一张东尼斩波变化为霸无敌”。
- **不要删除灼热铁斩波**；它仍是构造池卡，只是不再是古牙目标。
- 古牙是否给升级版霸无敌：见第 8 节阻塞项。旧古牙强化 3 次；霸无敌只有一级升级（0 费），默认按“升级版”实现，除非用户另行确认。

QA：

- 构装体拿古牙：东尼斩波被替换为霸无敌；其他初始牌不变。
- 原版角色拿古牙：仍走原版替换，不受影响。
- 附魔/升级继承规则按现有古牙逻辑；确认替换后的费用/升级状态。

#### 5.1.7 飞溅 / 发现不应出现 mod 牌

原生类：

- `MegaCrit.Sts2.Core.Models.Cards.Splash`（飞溅）
- `MegaCrit.Sts2.Core.Models.Cards.Discovery`（发现）

0.113 反编译结论：

- `Splash.OnPlay` 用 `Owner.UnlockState.CharacterCardPools` 枚举所有已解锁角色池；如果构装体已解锁，`ConstructCardPool` 会进入候选，原版角色就能抽到 mod 攻击牌。
- `Discovery.OnPlay` 用 `Owner.Character.CardPool.GetUnlockedCards`；理论上原版角色只走自己的池，但也要实测，防止自定义无色卡通过池映射泄漏。

修复原则：

- 在现有 `CardFactory` / `CardFactoryScope` / `GroupCardGates` 补丁点加过滤，不新增全面补丁。
- 当 `!CharacterScope.IsConstruct(player)` 时：
  - 剔除 `ConstructCardPool` 的卡；
  - 剔除程序集为 `PunchConstruct` 的卡；
  - 保留原版卡和其他模组的卡。
- 该过滤同时覆盖 `Splash` 和 `Discovery`，因为两者都走 `CardFactory.GetDistinctForCombat`。
- 先写 QA 复现“原版角色 + 构装体已解锁”场景，再确认修复。

QA：

- 原版角色使用飞溅 100 次：候选里 0 张 mod 牌。
- 原版角色使用发现 100 次：候选里 0 张 mod 牌。
- 构装体使用飞溅/发现：仍能正常看到自己的 mod 牌（如果设计允许）。
- 其他模组卡不受影响。

---

### 5.2 新卡

所有新卡：

- `[Pool(typeof(ConstructCardPool))]`
- 只对构装体开放
- 不加入原版池
- 卡图 1000×760
- 需要中英文文案
- 需要 QA

#### 5.2.1 发条机关

| 项 | 值 |
|---|---|
| 类型 | 能力 |
| 费用 | 2；升级 1 |
| 品质 | 待定，建议 Rare |
| 效果 | 回合结束时，将一张灼烧加入弃牌堆；每抽到一张状态牌，获得 1 点活力、1 点格挡、抽 1 张牌 |
| 获取 | 构造池（普通奖励/商店按现有规则） |
| 卡图 | `资料/历史/旧稿/复位弹簧.png`（1254×1254 RGBA） |
| 能力图标 | 同图缩到 64×64 / 256×256 |

实现提示：

- 新 `ConstructClockworkMechanism` 卡 + `ClockworkMechanismPower`。
- 回合结束：`BeforeSideTurnEnd` 或 `AfterSideTurnEnd`，把 `Burn` 加到 `PileType.Discard`。
- 抽到状态牌：`AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)`；检查 `card.Type == CardType.Status` 且 `card.Owner == Owner.Player`。
- 触发：`PowerCmd.Apply<VigorPower>(..., 1, ...)`；`CreatureCmd.GainBlock(Owner, 1, ValueProp.Unpowered, null)`；`CardPileCmd.Draw(context, 1, Owner.Player)`。
- 注意抽牌链：抽到状态牌 → 再抽 1 可能继续触发；这是设计允许的，QA 要验证不会卡死/溢出。
- 卡图：`复位弹簧.png` 是方形透明图标；直接放卡图会被补黑边。需要做 1000×760 背景构图（例如深色机械蓝图/黄铜底），图标居中。不要直接把 1254×1254 塞进卡图槽。

#### 5.2.2 机械飞升

| 项 | 值 |
|---|---|
| 类型 | 攻击 |
| 费用 | 3（2026-11-17 任务清单由 1 改为 3） |
| 品质 | 白卡 / Common |
| 池 | 无色（`Pool => ColorlessCardPool`），但 `[Pool(typeof(ConstructCardPool))]` |
| 效果 | 造成 12 伤害，获得 12 格挡；每有 1 点力量，伤害 +5；每有 1 点敏捷，格挡 +5 |
| 升级 | 获得保留；每点力量/敏捷加成改为 +8 |
| 获取 | 构造池（无色） |
| 卡图 | `资料/美术素材/卡牌/机械飞升.png`（1439×1093 RGB） |

实现提示：

- 伤害：`12 + 5 * StrengthPower.Amount`；升级 `+8`。
- 格挡：`12 + 5 * DexterityPower.Amount`；升级 `+8`。
- 升级加 `CardKeyword.Retain`。
- 取力量/敏捷用 `Owner.Creature.GetPower<StrengthPower>()` / `GetPower<DexterityPower>()`，或现有 helper；不要把格挡值当敏捷。
- 卡图比例 1439/1093 ≈ 1.317，和 1000/760 很接近，`prepare_art.py` 直接 resize 即可，不要补黑边。

#### 5.2.3 燃尽

| 项 | 值 |
|---|---|
| 类型 | 技能 |
| 费用 | 0；升级仍 0 |
| 品质 | 白卡 / Common |
| 池 | 无色（`Pool => ColorlessCardPool`），但 `[Pool(typeof(ConstructCardPool))]` |
| 效果 | 获得 1 力量、1 敏捷、3 层瓦解 |
| 升级 | 瓦解降为 1 层 |
| 获取 | 构造池（无色） |
| 卡图 | `资料/历史/旧稿/燃尽/04_侧前视_最新.jpg`，确认后裁到 1000×760 |

实现提示：

- `PowerCmd.Apply<StrengthPower>(..., 1, ...)`
- `PowerCmd.Apply<DexterityPower>(..., 1, ...)`
- `PowerCmd.Apply<DisintegrationPower>(..., 3, ...)` 给自己；这是负面效果，确认文案明确。
- 卡图从归档 jpg 转 RGB，中心裁切到 1000×760；不要黑边。

#### 5.2.4 霸无敌

| 项 | 值 |
|---|---|
| 类型 | 技能 |
| 费用 | 1；升级 0 |
| 品质 | 先古 / Ancient，仅古牙获取 |
| 关键词 | 保留 |
| 效果 | 打出后，下回合开始时获得 1 能量、抽 1 牌，对所有敌人造成 15 伤害、获得 15 格挡；升级后改为获得 2 能量、抽 2 牌（伤害／格挡仍 15）。能量文字用能量图标（2026-11-17 任务清单重写） |
| 获取 | 古老牙齿替换东尼斩波 |
| 卡图 | `资料/美术素材/卡牌/霸无敌.jpg`，换用交接新美术并按先古卡 606×852 肖像框裁剪（不拉伸） |

实现提示：

- 卡牌加 `CardKeyword.Retain`。
- 打出时施加一个 `ConstructDomineeringPower`，记录“下回合触发一次”。
- 下回合 `AfterPlayerTurnStart`：
  - 获得 1 能量、抽 1 牌（升级 2 能量／2 抽牌）；
  - 对 `CombatState.HittableEnemies` 全部造成 15 伤害；
  - 获得 15 格挡；
  - 移除自身，保证只触发一次。
- 参考 `DelayedWavePower` 的延迟结算；伤害来源用能力/卡牌按 0.113 API 选择。
- 卡图换用交接新美术，按先古卡 606×852 肖像框中心裁剪，保留人物主体，不拉伸变形（2026-11-17 任务清单）。
- 品质/是否升级版古牙奖励见第 8 节。

#### 5.2.5 核心电涌

| 项 | 值 |
|---|---|
| 类型 | 攻击 |
| 费用 | 1 |
| 品质 | 金卡 / Rare |
| 池 | 来源故障机器人（`Pool => DefectCardPool`），但 `[Pool(typeof(ConstructCardPool))]` |
| 效果 | 造成 11 伤害，获得 1 层人工制品，消耗 |
| 升级 | 15 伤害 |
| 获取 | 构造池；只对构装体 |
| 卡图 | `资料/美术素材/一代素材/核心电涌_CoreSurge_1024.png`（500×380 P） |

实现提示：

- `CardKeyword.Exhaust`。
- `DamageCmd.Attack(11)`；`PowerCmd.Apply<ArtifactPower>(..., 1, ...)`。
- 升级 `Damage.UpgradeValueBy(4)`。
- 卡图 500×380 是 P 模式；转 RGB 后放大到 1000×760 会偏糊，建议重绘/超分；至少不要黑边。

#### 5.2.6 万能药

| 项 | 值 |
|---|---|
| 类型 | 技能 |
| 费用 | 0 |
| 品质 | 白卡 / Common |
| 池 | 无色（`Pool => ColorlessCardPool`），但 `[Pool(typeof(ConstructCardPool))]` |
| 效果 | 获得 1 层人工制品，消耗 |
| 升级 | 2 层人工制品 |
| 获取 | 仅商店或缺陷构装体事件；不进普通奖励 |
| 卡图 | `资料/美术素材/一代素材/万能药_Panacea_1024.png`（500×380 P） |

实现提示：

- `CardKeyword.Exhaust`。
- `PowerCmd.Apply<ArtifactPower>(..., 1, ...)`；升级 2。
- 获取限制：在 `CardAcquisition.Eligible`/商店/事件逻辑里加 `ConstructPanacea` 的专属判断；普通奖励、飞溅、发现、随机生成都不出现。
- 卡图 500×380 同样需要转 RGB/放大或重绘。

---

### 5.3 新遗物：齿轮工艺品

| 项 | 值 |
|---|---|
| 名称 | 齿轮工艺品 |
| 稀有度 | Shop |
| 池 | `[Pool(typeof(ConstructRelicPool))]` |
| 效果 | 每场战斗开始时，获得 1 层人工制品 |
| 获取 | 仅商店或缺陷构装体事件 |
| 图标 | `资料/美术素材/一代素材/齿轮工艺品_ClockworkSouvenir_large.png`（256×256 P） |

实现提示：

- `ConstructClockworkSouvenir : CustomRelicModel`。
- `PackedIconPath`/`PackedIconOutlinePath`/`BigIconPath` 指向 `PunchConstruct/art/...`。
- 战斗开始：`AfterCombatStart` 或 `BeforeCombatStart` 中 `PowerCmd.Apply<ArtifactPower>(..., 1, ...)`；按 0.113 生命周期选，QA 验证。
- 获取限制：
  - 商店：只对构装体生成；`RelicRarity.Shop` + `ConstructRelicPool`。
  - 普通遗物奖励、宝箱、事件随机遗物：排除。
  - 缺陷构装体事件直接 `RelicCmd.Obtain<ConstructClockworkSouvenir>(owner)`，绕过商店限制。
- 美术：256×256 源图做 128/256；在 `prepare_art.py` 的 `RELIC_FIT` 加条目，透明背景，居中。

---

### 5.4 新药水

所有药水：

- `CustomPotionModel`
- `[Pool(typeof(ConstructPotionPool))]`
- 只对构装体
- 需要 `CustomPackedImagePath` / `CustomPackedOutlinePath` / `CustomLargeImagePath`
- 需要 `Localization`
- 需要 QA

#### 5.4.1 先古药水

| 项 | 值 |
|---|---|
| 稀有度 | Common |
| 效果 | 获得 1 层人工制品 |
| 使用 | 战斗内，自身 |
| 美术 | 金色色调；瓶型来源待确认（建议格挡药水瓶型换金色，或一代先古药水原图） |

实现：

- `PowerCmd.Apply<ArtifactPower>(context, Owner.Creature, 1, Owner.Creature, this)`。
- `PotionUsage.CombatOnly`，`TargetType.Self`。
- 药水池只挂 `ConstructPotionPool`。

#### 5.4.2 浑浊机油

| 项 | 值 |
|---|---|
| 稀有度 | Uncommon |
| 效果 | 获得 10 点活力、10 点拉伸 |
| 使用 | 战斗内，自身 |
| 美术 | 格挡药水换色，内容物灰色 |

实现：

- `PowerCmd.Apply<VigorPower>(..., 10, ...)`
- `PowerCmd.Apply<StretchPower>(..., 10, ...)`
- 拉伸按 0.113 原生行为：下一次卡牌获得格挡时额外 +10，触发后消耗；只对卡牌格挡生效，不是遗物/药水格挡。
- 美术：提取 `BLOCK_POTION` 的药水图，内容物改灰色；保持瓶身高光/轮廓。

#### 5.4.3 拳手炖汤

| 项 | 值 |
|---|---|
| 稀有度 | Rare |
| 效果 | 本场战斗中，所有名字带“斩波”的牌获得重放 1 |
| 使用 | 战斗内，目标按原生士兵炖汤（AnyPlayer）或自身，待确认 |
| 美术 | 士兵炖汤换色，整体灰色 |

实现：

- 参考原生 `SoldiersStew`：
  - `PotionUsage.CombatOnly`
  - `TargetType.AnyPlayer`（或 Self）
  - `OnUse` 遍历 `target.Player.PlayerCombatState.AllCards`
  - 对符合条件的卡 `item.BaseReplayCount++`
- 过滤“斩波”：建议用类型集合而不是中文标题，避免多语言问题：
  - `SmallWave`
  - `TonyWave`
  - `IronWave`
  - `ScorchingIronWave`
  - `IronWaveForm`（“铁斩波形态”也含“斩波”，需确认是否算）
  - 其他名字含“斩波”的卡
- 美术：提取 `SOLDIERS_STEW` 的药水图，整体灰色；保持轮廓/高光。

---

### 5.5 原生事件改版：被寄生的自动机械（历史标题：缺陷构装体）

> **0.5.1 修订注记（2026-11-15）**：本节的历史判断有误。实际改版的原生事件是 `InfestedAutomaton`（被寄生的自动机械），不是 `PunchOff`；`PunchOff` 只做文本分流。下文把“缺陷构装体”默认映射到 `PUNCH_OFF` 的内容仅作历史记录，不要按它实现。当前实现见 `PunchConstruct/WaveEvents.cs` 的 `ConstructInfestedAutomaton`。

#### 5.5.1 事实与风险

0.113 的本地化/类型里**没有**一个独立叫“缺陷构装体”的事件。当前唯一可复用的原生构装体事件是：

- 类：`MegaCrit.Sts2.Core.Models.Events.PunchOff`
- 中文：`PUNCH_OFF`，拳击构装体事件
- 布局：`EventLayoutType.Combat`，会生成两只拳击构装体互殴
- 现有 mod 适配：`ConstructPunchOffText` 已经把 `PUNCH_OFF` 的文本映射到 `PUNCHCONSTRUCT_PUNCH_OFF`

因此本任务默认：**改版 `PUNCH_OFF` 事件，复用其背景/布局，只对构装体替换文案和选项。**  
如果用户指的是另一个原生事件 ID，替换事件类型和本地化前缀，流程相同；不要新建 CustomEventModel。

**视觉冲突警告**：`PunchOff.AfterEventStarted` 会让两只构装体互殴。新文案是一具死掉的、半跪的构装体，背景/动画可能不匹配。用户明确说背景不变，所以默认保留布局/动画；实现后必须截图确认。若冲突，先问用户是否取消 `_punchCts` 或换事件，不要擅自改。

#### 5.5.2 文案

初始页：

```text
废料堆深处，一具[gold]构装体[/gold]半跪在地。
外壳裂开，齿轮锈死，胸口的[blue]核心[/blue]早已熄灭。
唯一还在动的，是它胸腔里一枚[gold]齿轮工艺品[/gold]。发条还在走，[sine]滴答，滴答[/sine]。
```

选项 1：修复

- 描述：`失去 [red]67[/red] 金币，获得 [gold]齿轮工艺品[/gold]。`
- 结果页：

```text
你买来零件，把锈死齿轮一件件换掉。
核心没亮。你修不好一台停机的机器。
你把它胸腔里那还在走动的[gold]齿轮工艺品[/gold]取出。
发条还在走，[sine]滴答，滴答[/sine]。
```

选项 2：翻找

- 描述：`获得 [gold]万能药[/gold]。`
- 结果页：

```text
你蹲下身，在它的残骸里翻找。
小瓶从裂开的胸腔滚出。
你拔开瓶塞，里面是[blue]万能药[/blue]。
你离开时，它仍保持着原来的姿势。
```

#### 5.5.3 本地化键

在 `PunchConstruct/assets/localization/*/events.json` 增加/替换：

```text
PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.description
PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.options.REPAIR.title
PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.options.REPAIR.description
PUNCHCONSTRUCT_PUNCH_OFF.pages.REPAIR.description
PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.options.SEARCH.title
PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.options.SEARCH.description
PUNCHCONSTRUCT_PUNCH_OFF.pages.SEARCH.description
```

`ConstructPunchOffText` 会自动把 `PUNCH_OFF.*` 映射到 `PUNCHCONSTRUCT_PUNCH_OFF.*`，所以选项标题/描述也能走同一套。

#### 5.5.4 代码

1. 新事件选项适配器：

```csharp
[HarmonyPatch(typeof(PunchOff), "GenerateInitialOptions")]
internal static class BridgeConstructPunchOffOptions
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PunchOff __instance, ref IReadOnlyList<EventOption> __result)
    {
        var options = __result;
        AdaptationRegistry.Run("event", "ConstructPunchOffOptions",
            () => ConstructPunchOffOptions.Apply(__instance, ref options));
        __result = options;
    }
}
```

2. 替换逻辑：

```csharp
internal static class ConstructPunchOffOptions
{
    internal static void Apply(PunchOff room, ref IReadOnlyList<EventOption> __result)
    {
        if (!CharacterScope.IsConstruct(room.Owner)) return;

        bool canRepair = room.Owner.Gold >= 67;
        string prefix = "PUNCHCONSTRUCT_PUNCH_OFF.pages.INITIAL.options.";
        EventOption Option(string key, Func<Task>? action, bool enabled) => new(
            room,
            enabled ? action : null!,
            new LocString("events", prefix + key + ".title"),
            new LocString("events", prefix + key + ".description"),
            prefix + key,
            []);

        __result = [
            Option("REPAIR", () => Repair(room), canRepair),
            Option("SEARCH", () => Search(room), true)
        ];
    }

    private static async Task Repair(PunchOff room)
    {
        await PlayerCmd.LoseGold(67, room.Owner, GoldLossType.Spent);
        await RelicCmd.Obtain<ConstructClockworkSouvenir>(room.Owner);
        WaveEventRules.Finish(room, "REPAIR");
    }

    private static async Task Search(PunchOff room)
    {
        await CardPileCmd.Add(room.Owner.RunState.CreateCard<ConstructPanacea>(room.Owner), PileType.Deck);
        WaveEventRules.Finish(room, "SEARCH");
    }
}
```

3. 注意：

- `PlayerCmd.LoseGold` 的第三参用 `GoldLossType.Spent`。
- `RelicCmd.Obtain<T>` 已存在于 mod 用法。
- `CardPileCmd.Add` 加到牌组；`CreateCard<ConstructPanacea>` 使用构造卡。
- `WaveEventRules.Finish` 会调用原生 `SetEventFinished`，页面描述走 `PUNCHCONSTRUCT_PUNCH_OFF.pages.REPAIR/SEARCH.description`。
- 如果玩家已有齿轮工艺品：决定是否跳过/给金币；默认 `RelicCmd.Obtain`，在 QA 中记录实际行为，必要时补重复处理。
- 原版角色：`Apply` 直接 return，保留原生 `NAB`/`I_CAN_TAKE_THEM`。

QA：

- 构装体金币 ≥67：修复扣 67，获得齿轮工艺品，事件结束。
- 金币 <67：修复选项禁用；翻找可用。
- 翻找：万能药进牌组，事件结束。
- 原版角色：仍是原生两个选项和文本。
- 事件背景/互殴动画截图；确认是否与文案冲突。

---

### 5.6 新卡/遗物/药水获取限制

- 万能药、齿轮工艺品：只从商店或缺陷构装体事件获取；不进普通奖励、宝箱、随机遗物、飞溅/发现。
- 核心电涌：只进构造体池；不加入原版故障机器人池。
- 机械飞升、燃尽：构造体无色池；不进原版无色池。
- 发条机关、霸无敌：构造体专属；霸无敌只从古牙获取。
- 所有新内容：原版角色在任何渠道都不可见。

实现位置：

- 卡牌：`CardAcquisition.Eligible`、`CardAcquisition.IncludeDesignedColorless`、商店/事件逻辑。
- 遗物：`RelicAcquisition`、商店遗物候选、事件奖励。
- 药水：`ConstructPotionPool` + 药水掉落逻辑。

---

### 5.7 美术与本地化

#### 5.7.1 现有素材

| 用途 | 文件 | 尺寸/模式 | 处理 |
|---|---|---|---|
| 机械飞升 | `资料/美术素材/卡牌/机械飞升.png` | 1439×1093 RGB | 直接 resize 到 1000×760 |
| 霸无敌 | `资料/美术素材/卡牌/霸无敌.png` | 1065×1477（2026-11-17 交接新美术） | 先古卡面中心裁切到 606×852，不拉伸；旧 `.jpg` 已归档 |
| 发条机关 | `资料/历史/旧稿/复位弹簧.png` | 1254×1254 RGBA | 能力图标 64/256；卡图需 1000×760 背景构图 |
| 燃尽 | `资料/历史/旧稿/燃尽/04_侧前视_最新.jpg` | 归档 jpg | 裁切到 1000×760 |
| 核心电涌 | `资料/美术素材/一代素材/核心电涌_CoreSurge_1024.png` | 500×380 P | 转 RGB，放大/重绘到 1000×760 |
| 万能药 | `资料/美术素材/一代素材/万能药_Panacea_1024.png` | 500×380 P | 转 RGB，放大/重绘到 1000×760 |
| 齿轮工艺品 | `资料/美术素材/一代素材/齿轮工艺品_ClockworkSouvenir_large.png` | 256×256 P | 128/256 图标，透明背景 |
| 古铜之拳 | `资料/美术素材/遗物/先古架势.png` | 1254×1254 RGBA | 现有管线，128/256 |
| 先古药水 | 待定 | 256×256 | 金色调；瓶型来源见第 8 节 |
| 浑浊机油 | 原生 `BLOCK_POTION` | 256×256 | 内容物灰色 |
| 拳手炖汤 | 原生 `SOLDIERS_STEW` | 256×256 | 整体灰色 |

#### 5.7.2 `prepare_art.py`

- 卡图：加新条目；目标 1000×760；不要 4:3 直接 `pad` 出黑边。
- 遗物：加 `RELIC_FIT` 条目；128/256 透明，主体居中。
- 药水：当前脚本没有药水导出；需要扩展 `export`/新增导出，生成 `CustomPackedImagePath`/outline/large 对应的 `.ctex`。
- 所有输出保留 PNG 源文件，游戏读 `.ctex`。

#### 5.7.3 本地化

- 新卡/遗物/能力：`Loc.Card` / `Loc.Relic` / `Loc.Power` 内联中文 + `EnglishText.Generated.cs` 英文。
- 事件：`assets/localization/zhs|zht|eng/events.json`。
- 药水：新增 `potions.json`（如果没有）或对应 BaseLib 本地化；所有语言至少 zhs/zht/eng。
- 跑 `tools/check_documents.py`，保证文档/哈希/文案一致。

---

### 5.8 QA 与构建

#### 5.8.1 先做基线

- [x] `dotnet build PunchConstruct\PunchConstruct.csproj -c Release`
- [x] 0.113 启动，`patch.summary` Required 全绿。
- [x] 现有全量 QA 通过。

#### 5.8.2 新内容 QA

- [x] 先古架势/古铜之拳：1→2→3 与 5→6→7，部分格挡不触发，下场重置。
- [x] 与我一斗：5×2 / 6×2，敏捷 2/3。
- [x] 不倒不休：回合开始获得 1 张拳斗；不生成灼烧；受伤不触发额外效果。
- [x] 伽马爆破/彗星：伤害与原生一致，费用仍为 2/3。
- [x] 应急油壶：所有敌人 1 易伤 + 1 虚弱。
- [x] 古老牙齿：东尼斩波→霸无敌；原版角色古牙不变。
- [x] 发条机关：2 费 / 升级 1 费；回合结束加灼烧；抽状态牌给 1 活力/1 格挡/抽 1；抽牌链不崩。
- [x] 机械飞升：力量/敏捷缩放 +5/+8，升级保留。
- [x] 燃尽：0 费；1 力量/1 敏捷/3 瓦解，升级后瓦解 1 层。
- [x] 霸无敌：打出后下回合 15 伤害/15 格挡，只触发一次，升级 0 费。
- [x] 核心电涌：11/15 伤害，1 人工制品，消耗。
- [x] 万能药：1/2 人工制品，消耗；只在商店/事件。
- [x] 齿轮工艺品：战斗开始 1 人工制品；只在商店/事件。
- [x] 先古药水/浑浊机油/拳手炖汤：效果、稀有度、构造池限制。
- [x] 飞溅/发现：原版角色 100 次无 mod 牌。
- [x] 所有新内容：原版角色奖励/商店/事件/飞溅/发现均不可见。
- [x] 2p 混编、存读档、事件截图。
- [x] 卡图/图标尺寸与透明通道。

#### 5.8.3 发布前

- [x] 更新 `SmokeCheck`/`AdditionsChecks`/`DraftChecks` 的卡牌、遗物、药水数量期望。
- [x] `prepare_art.py` 生成全部新 `.ctex`，哈希/尺寸校验。
- [x] `check_documents.py`、`check_current.py`、`check_local.py` 通过。
- [x] 打包 0.5.1；**不要**重建/重传 0.4.28。

---

## 6. 之前遗留的审计项

这些不属于本次内容批次，但用户要求一并列出；不要默认全部插队，按优先级处理。

| 优先级 | 项目 | 状态 |
|---|---|---|
| P0-3 | 证据链：`report_local.py:218` 硬编码 `full_release_verification=False`、`uploaded=False`；`current-verification.json` 与 `verification.json` 矛盾 | 已完成：`uploaded`／`upload_status` 从候选 `verification.json` 推导；`full_release_verification` 需本地、候选、上传与全部发布轴同时 PASS |
| P1-1 | Required 真 fail-closed：`ModEntry` 不检查 `RequiredFailures`；遗物替换失败放行原生；`BridgeConstructRelicMigration` 未标 Required | 已完成：`ModEntry` 检查 Required 失败并隐藏角色；遗物替换失败回退 Circlet；Migration 标 Required |
| P1-2 | `PatchRegistry`：`GetTypes()` 在 try 外、`appliedOnce` 提前、无按类回滚、单 Harmony ID | 已完成：类型枚举纳入异常处理；逐类 Harmony ID；失败类 `UnpatchAll` 回滚；`appliedOnce` 在 finally 收尾 |
| P1-3 | 遗物状态/共享池：只复制 `FloorAddedToDeck`；`SharedRelicGrabBag.Remove` 在隔离外；`CalledFromGenericObtain` 靠 `StackTrace` | 已完成：复制 `FloorAddedToDeck`；共享池清理纳入适配器隔离；移除脚本化 Obtain 桥，仅保留 StackTrace 兜底（Harmony 无法补 open generic） |
| P1-4 | `CharacterScope` 静态 `FieldRefAccess("_owner")` 单点故障 | 已完成：反射失败降级为预览上下文／保守原生路径并记录诊断 |
| P1-6 | `adapter.failed` 不记录异常类型/消息/堆栈；启动版本号硬编码 | 已完成：诊断记录异常类型、消息与堆栈；启动版本号动态读取 |
| P3 | 涟漪盆高亮、先古商店多一次 RNG、叶敷顺序、紧追不放任意宠物、Sash Whip 本回合、ConstructHelmet 文案、FightMe ID、万能药补货 | 已完成：逐项修复并纳入 0.5.1 专项 |
| P2 | 真实 RitsuLib + RandomForeseer 冒烟、QA 代码与正式 DLL 分离、构建路径可移植、联机版本一致性、`ASSETS.md` | 未做 |

2026-11-15 状态（旧候选，已被 2026-11-16 左勾拳削弱与 `pcdeck` 取代）：三文件 DLL `d22fa5b4…`、PCK `cd09d231…`、清单 `030ce557…`，277 份资源；旧候选本地 35/35、联机 4/4、断线恢复 4/4 全部通过，`full_local_verification=true`、不变量 16/16；旧 `workshop.py --verify` PASS，`upload_status=NOT_UPLOADED`。最终 DLL `e3819999…` 待全量重跑、归档与私有测试重传。0.4.28 保持冻结，不重建、不重传。

2026-11-17 状态（最终候选）：任务清单七项（机械飞升 3 费、霸无敌重写为下回合能量＋抽牌／升级 0 费、能量文字改能量图标、卸甲成军／厄运共鸣／霸无敌换交接新美术并按先古卡 606×852 裁剪、旧图归档）已合入。三文件 DLL `702f587a…`、PCK `d67662e5…`、清单 `030ce557…`，277 份资源；本地 35/35（4979 条断言）、联机 4/4（含 4p 弱网）、断线恢复 4/4 全通过，`full_local_verification=true`、不变量 16/16；`workshop.py --verify` 全 PASS，`upload_status=NOT_UPLOADED`。content 已同步、`dist/releases/0.5.1/` 与 ZIP（51,343,686 B，SHA256 `0c36fc7d…`）已重建；0.113.1 隔离预检复核 startup/smoke/additions 140/pool 全 PASS。私密测试条目 3835179658 待按最终 DLL 重传（Steam 未运行，暂缓、暂存就绪）；公开条目 3826907330 仍为 0.4.28，保持冻结，不重建、不重传。

---

## 7. 验收标准

1. 0.113 + BaseLib 3.4.8 构建通过，Required 补丁全绿。
2. 本任务书第 5 节所有功能可用，且只对构装体生效。
3. 原版角色在奖励、商店、事件、飞溅、发现中看不到任何新增 mod 内容。
4. 事件背景沿用原生 `PUNCH_OFF`，原版角色事件不变。
5. 所有新图符合尺寸/透明通道要求，无黑边。
6. 2p、存读档、事件截图通过。
7. 0.4.28 产物没有被改动。

---

## 8. 阻塞确认（先问用户）

1. **事件 ID**：0.113 本地化里没有独立“缺陷构装体”；默认改 `PUNCH_OFF`。如果用户指的是另一个原生事件，请提供事件名/截图/ID。`PUNCH_OFF` 的互殴动画与“死掉的构装体”文案可能冲突，是否保留动画？
2. **古牙给霸无敌的版本**：旧古牙强化 3 次；霸无敌只有一级升级（0 费）。默认给**升级版**，是否确认？
3. **发条机关、霸无敌的品质**：用户未指定；建议发条机关 Rare、霸无敌 Ancient/特殊。请确认。
4. **发条机关的卡图背景**：`复位弹簧.png` 是方形透明图标；卡图 1000×760 需要背景。用深色机械/黄铜底，还是用户另给图？
5. **先古药水瓶型**：用格挡药水瓶型换金色，还是一代先古药水原图？
6. **拳手炖汤目标**：按原生士兵炖汤 `AnyPlayer`，还是只对自己？“斩波”是否包含“铁斩波形态”？
7. **万能药/齿轮工艺品的商店范围**：只在构装体商店出现，还是所有商店都可能出现但只对构装体可购买？
8. **重复遗物**：修复时已有齿轮工艺品，是否跳过/给金币/允许重复？
9. **新卡审阅编号/模型 ID**：是否按现有 82 种之后顺延？

---

## 9. 不要做

- 不要 `git add -A`；根目录可能含 SteamID 日志压缩包。
- 不要修改游戏安装目录或替换原生 DLL/PCK。
- 不要把新卡/遗物/药水 ID 加进原版池。
- 不要为原版角色改事件文本/选项。
- 不要在未重跑 QA 的情况下重建/重传 0.4.28。
- 不要在没有真实第三方模组冒烟前宣称“完全兼容”。
