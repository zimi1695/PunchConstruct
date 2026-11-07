# 开发工具

`python tools/check_current.py boxing --rendered`检查八张无色新牌、获取限制、所有单位荆棘、左右勾拳顺序、格斗流程当回合生成、过载与满手牌计数。联机从0.4.20起增加boxing-generated／boxing-resolved两阶段快照。当前0.4.26候选完整功能验收要求35项；本轮content已同步为0.4.26（含死亡／建筑师击倒的朝向瞬变修复），Steam条目3826907330仍为线上0.4.24（已公开，尚未上传新版）。

## 本地使用

`run_all_checks.ps1` 一次串行执行本地 35 项（含 `compat`、`english`、`locale`、`localezh`）、联机 4 项、断线恢复 4 项，最后调用 `report_local.py` 汇总；`check_local.py` 对长批跑后偶发失败的 `shadernative` 会隔离重跑一次。

`launch.ps1`启动工作区离线试玩，`-PrepareOnly`仅同步最新DLL、清单与资源；运行中的本地试玩会阻止同步。用户存档在runtime/play-userdata，不被QA覆盖。`sync_runtime_art.py`校对资源，不写dist。手动分发content为0.4.26，已同步含 Native Bridge + 五域适配器重构与英文本地化的当前Release构建。已有试玩窗口须退出后由启动器同步；只依赖BaseLib。

仅编译：

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-local'
dotnet build PunchConstruct/PunchConstruct.csproj -c Release --no-restore
```

`build.ps1`会同时调用打包；`package.py`、`release.py`也是发布工具，只有明确要求导出／发布时使用。日常整理、修复和验证不运行这些打包入口。

## 文档与素材

- `check_observer_art.py`：只读比对13张采用观者纹理与本机Watcher包的导入记录及真实纹理，`--record`仅更新现有来源表的路径核对记录。
- `check_documents.py`：只读核对82张牌的连续编号、名称／类型／品质统计、源码成员、素材来源／编号／导出覆盖、当前文档链接和关键规则，不启动游戏。
- `prepare_art.py`：按已采用素材导出纹理并更新素材清单。卡面和状态图标的审阅编号从主卡牌清单读取，轮廓图也计入清单，源图不改。
- `prepare_text.py`：由角色对白正文生成已接入对白；文件头的草稿状态不参与导出。
- `preview_icons.py`：根据状态图标清单生成全部已采用图标总览。
- `Inspect`与`game-reference`：本地游戏接口、IL和原版资料。游戏参考与历史资料不是当前角色设定。

## 定向验证

统一入口：`python tools/check_current.py 模式`。使用独立临时副本和测试存档，结束后清除副本，仅保留`runtime`对应日志。一般仅加载BaseLib与本模组；modes另加载临时QaExitProbe，在133项原生种子／挑战检查结束后释放离开场景树的闲置卡牌缓存，等待渲染帧再退出，不改测试内容或正常游戏。该QA程序集及指纹登记在日志和结果中，不装入候选／试玩。通过判定同时检查成功标记、失败标记、退出码及未分类错误，PASS之后的资源／脚本错误同样判失败。已在BaseLib单独启动中复现的精确底层诊断形式，以及调试房间未自动保存时原版清理两个不存在临时存档文件的提示，会单独分类；其他异常不放行。

单人专项的新归档同时保留DLL／PCK／清单的SHA256、临时QA助手指纹及原始游戏日志；`result.json`的`build`记录实际测试文件。`persist`与`observerpersist`的写档和续玩日志分别保留在同一个证据目录，避免只留下第二次启动的记录。历史归档保持原样，不能因工具补充了字段而视作已经重测。

| 模式 | 范围 |
|---|---|
| animation | 原版构装体单拳／双拳／防御的真实动画与声音调用；重拳、重拳、双连拳节奏，多段攻击，伤害段数不变，技能／能力去重，原版角色隔离和跨战斗重置；真实原版动作帧逐步录制，静音隔离 |
| adapt | 紧追不放取消护卫门槛、角色伤害／召唤、逐段触发、叠加／预览／致死；出击与守护者的角色攻击、当前／最大奥斯提生命加伤、缺席基础伤害、升级／预览／活力消耗／战斗复制；地毯原生奖励抽样、去重、限定标志、恢复和角色隔离 |
| startup | 干净隔离环境，仅BaseLib与本模组，无QA参数，普通启动至主菜单及公共预加载完成，不开局；使用--rendered验证实际渲染 |
| startupbase | 只加载BaseLib的普通启动对照，核对底层诊断是否依赖本角色；不用于验证角色发布文件 |
| observerpersist | 两个全新进程间，用旧Watcher卡ID经原版继续恢复8张牌、强化和附魔；仅修改隔离测试档 |
| observer | 独立观者八牌、真言／神格、预见、回防、旋身、斋戒及旧卡ID迁移；神格粒子／眼睛／着色器及退出清理；断言未加载Watcher |
| vigor | 定向复现格挡荆棘反伤后活力不再消耗；连续攻击、三段攻击、伤害实际加成及剩余活力清理；实际异鸟三连击比较无遗物、旧处理、压缩等待和未格挡节奏 |
| pool | 仅菜单核对完整名单、归属、品质、边框和药水工厂 |
| icons | 仅加载十张状态图标与大图，核对各自主体占比 |
| regentvisual | 菜单实际卡牌控件检查七张能量替代星费牌、升级／降级／费用刷新、原角色隔离及真正0星牌 |
| relicvisual | 使用实际渲染器检查遗物栏小图／轮廓、预览大图、刷新和角色隔离；仅菜单，不开局 |
| architect | 直接跳转建筑师，检查首遇／重逢对白、逐句点击、原版结尾及通关结算 |
| additions | 愈战愈勇1张灼烧／5动量普通拳斗、基础消耗／强化去消耗、升降级关键词、空手自动变牌、满手溢出、动画与弃牌计数；不倒不休金卡2／1费、均生成普通拳斗及灼烧、混合叠加；自旋螺丝与对应素材 |
| focus | 震荡、架势递增活力、先古替换、古铜之拳下回合临时力量累计／生效／消退／永久力量保留及跨战斗重置；黄铜螺帽图标／悬浮说明；跨回合实际NPower控件实例连续保留；原版和遗物触发的能力获得耗时 |
| soup | 自动筛选小斩波可合法附加的原版附魔，核对普通／升级及已有永恒的附魔小斩波全部跳过，原牌与附魔不变；未附魔小斩波保留强化后变为0费永恒铁斩波，已有铁斩波／灼热铁斩波不变；包含克隆和序列化；仅直接事件房间 |
| persist | 建立营养汤／魔典／储君费用存档，关闭进程，再启动全新进程从主菜单继续；另留seed日志 |
| update | 无色来源、镇纸、海玻璃、树叶药膏及近期卡牌 |
| edge | 商店补货、体系门槛与权重、锻造／召唤／灾厄；全部17种自定义状态的实际HoverTips及飞镖状态，覆盖数值、条件、叠加、剩余次数与隐藏内部状态 |
| boundary | 头盔跨战斗重置、重复／跨组权重、能力联动 |
| relics | 宝箱／普通奖励／商店获取排除及角色隔离，木偶、种子、水盆、飞镖与延迟结算；另测灼热铁斩波连续8次升级、复制／还原／降级及两种木偶单独／叠加的实际出牌 |
| treasure | 单独复现原生宝箱共享池抽取；64组种子、混组角色隔离、同种子结果一致、玩家及随机数作用域恢复 |
| workshop | 四件遗物的获取与品质、旧ID兼容、图标尺寸、击败结算、油壶能量与随机数、自旋螺丝在实际敌人回合后判定及存档字段 |
| events | 巨大扭蛋实际领取随机遗物与两张小斩波，原角色隔离；直接跳脑蛭、三色色彩哲学家、重拳出击并实际领取；核对9张候选跨轮去重及六张体系原创准入 |
| capsulecontrol／capsuleconstruct | 铁甲战士／构装体单独领取巨大扭蛋，并进入原生重拳出击场景；比较事件退出渲染诊断，不代替完整events专项 |
| eventscontrol | 铁甲战士实际完成脑蛭、三色色彩哲学家和重拳出击的相同领取流程；构装体特有规则断言跳过，只作原生事件渲染退出对照，不算角色功能验收 |
| shadernative／shaderoriginal | 直接进入蜈蚣战斗并显示再生特效，检查本角色兼容路径／原角色原生路径、三段实际材质、纹理绑定及退出恢复 |
| eventscene／eventroom／eventbrain／eventphilosopher | 分离原版特效、原版房间、脑蛭领取与哲学家领取，用于定位退出诊断；不代替完整events |
| doll | 玩偶三条路线 |
| end | 放弃／死亡、结算、回菜单及重开；--rendered另逐帧核对死亡朝向、0.133秒姿态硬切的固定过渡及原版敌人隔离 |
| facing | 直接跳转帝王蟹战斗，实际攻击左右蟹钳，核对画面朝向、背击倍率、重复攻击与死亡朝向；使用--rendered检查画面 |
| smoke | 基础模型、先古对白选择与资源冒烟检查 |
| compat | 真实安装第三方 Postfix／Prefix、注入不匹配 IL，验证 NativeBridge 边界、CharacterScope 覆盖与 Required 策略 |
| english | 运行时切换英文，逐条扫描卡牌／能力／遗物／角色与四张 JSON 表的中文残留；同时校验 14 个语言镜像表 |
| locale | 以德语启动真实游戏，验证非中文语言使用英文镜像表、本地化标题与四张表可解析 |
| localezh | 以繁体中文启动真实游戏，验证 Chinese 分支保留中文表与本地化标题 |

有明确问题时使用对应模式、控制台命令或直接调试房间，不运行整局自动游玩。旧`playtest.ps1`仍可用于指定视觉专项，不把综合游玩模式作为默认测试。历史日志对应当时构建，不等于每次改动都全部重跑。ordinary_launch_check.ps1现在调用startup --rendered，使用同一临时环境与清理流程。启动检查只识别对照中已有的精确底层诊断形式，其他Godot ERROR、脚本异常和加载异常均判失败。

专项开始前会等待原版Common资源加载会话真正完成，避免只做菜单检查后过早退出引发资源加载错误；普通游戏不使用该等待。普通专项超时180秒，modes与poster为300秒。真实渲染专项把窗口用`--position`停在屏幕右下角，游戏内主音量／音效／环境音由临时配置设为0，测试期间无声音。poster的一次性临时档会把原生「进阶难度已解锁」教程标记为已看，避免这个由最高进阶提升触发的原生弹窗盖住选人界面；正式存档不受影响。无界面诊断中原版屏幕／特效资源解析失败与dummy渲染器的RID诊断按已知形式忽略；模组自身资源（`res://PunchConstruct/`、`res://mods/`）解析失败仍判失败。并行批跑时若只有原版Spine选人贴图预载失败、该项检查本身仍PASS，可单独重跑该项，不改判定标准，并在文档中记录原因。

事件专项全部结束、即将退出QA进程时，释放原生卡牌池中已闲置且离开场景树的控件，等待渲染帧后退出，防止把进程缓存误当作玩法泄漏；正常游玩不调用。`NativeProbe`是独立QA程序，不引用或加载PunchConstruct，可用`dotnet build tools/NativeProbe/NativeProbe.csproj -c Release`和`python tools/check_native.py --release-pools`进行相同原生奖励流程对照；仍使用BaseLib、隔离档和静音，不装入试玩或发布文件。去掉`--release-pools`会保留原生缓存退出诊断，不放宽失败判定。

`python tools/check_graphics_compat.py`逐字核对蜈蚣兼容场景及Shader仅有登记的引用／乘加差异，并验证原包与输出指纹。

`python tools/check_local.py`针对当前本地DLL按版本要求执行角色功能专项（0.4.23共31项），最多并行3个；活力节奏对照在其余专项全部结束后单独运行。`--package-dir dist/SteamWorkshop/content`则对现有候选的实际三个文件执行同一验收，写入`runtime/candidate-batch-版本.json`，不重新导出。每完成一项便记录进度，中断不冒充全部通过。modes需先编译`tools/QaExitProbe/QaExitProbe.csproj`。`shaderoriginal`另为原版角色诊断对照，仍严格记录其原生fma错误，不算本角色功能验收，也不改动原版角色来消除此对照的报错。`python tools/report_local.py`按实际DLL指纹汇总单人及2／3／4人日志，原版对照列入`diagnostic_controls`，列出缺失、失败与覆盖边界，更新`runtime/current-verification.json`；同时逐字核对当前候选资源和本地验收状态，不生成候选、不沿用另一份DLL的结论。

需要检查实际画面时使用`python tools/check_current.py 模式 --rendered`，仍然只执行所选专项。

验收报告分三层：`runtime/current-verification.json`保留`checks`等逐模式原始结果；`families`把共用同一套件入口的mode归并（relics／workshop、events／shadernative、smoke／pool／icons、persist／observerpersist）；`invariants`按16条玩家可见保证汇总，`invariant_summary`列出PASS／FAIL／MISSING、未映射与未分类项。`tools/invariants.json`是唯一台账，新增mode必须登记，否则`full_invariant_verification`为false。`coverage.unverified`显式列出未验证轴：真实第三方mod、Steam传输、热重连、其他平台、完整长局、工坊订阅、旧档升级、输入与显示、本地化深度、完整音频混音；`full_local_verification=true`不代表这些轴已覆盖。`english`在分类中单列为`data_audit`，不再与行为断言混算。

## 日志与自动测试约定

后续自动测试一律静音：临时测试档的总音量、音乐、音效和环境音均为0，显式游戏测试入口另设置主音量为0。此设置仅用于隔离测试档，不修改作者正常试玩音量；音效专项仍可检查事件是否调用。

单人与联机共用JSONL诊断结构。正常试玩日志位于`runtime/play-userdata/PunchConstructTest/PunchConstruct/logs`，原生完整日志仍是`runtime/play.log`；实际日志目录会在启动时以`[PunchConstruct LOG]`输出。Steam环境使用对应游戏用户目录下的`PunchConstruct/logs`，不写入模组安装目录。

每条记录包含schema、UTC时间、递增序号、进程会话、局次、级别、事件名、当前上下文和数据。启动记录包含DLL指纹、模组／BaseLib版本；开局记录包含种子、挑战参数和玩家槽位／角色。行动使用原生actionId，选牌使用choiceId；不同机器应以同种子、房间、行动／选择编号和玩家槽位对照，不使用各进程不同的session字段匹配。

记录房间、战斗、回合、行动开始／完成／耗时、等待选牌、发出／收到选择、退出、断开和状态分歧；行动异常、房间异常保留异常类型、信息和堆栈，分歧附玩家状态摘要。原生警告／错误另标为native.message，不能仅据此认定是本模组造成。玩家使用槽位，原生消息使用游戏自带Steam ID清理，不主动记录昵称、账号、聊天或网络地址；日志只保存在本机，不自动上传。

日志每份约2MiB轮转，最多保留12份；重复的相同原生警告／错误在5秒内合并并记录压制数量，最多缓存128种消息。轮转和写入异常不会中断游戏；无法写入时只提示一次，并保留原生游戏日志。日志不加入存档、状态校验或网络消息，不消费游戏随机数。

`python tools/diagnose.py`只读汇总正常试玩日志；`python tools/diagnose.py 路径 --events`查看完整时间线，也可用`--session`限定某个进程。联机排错保留所有玩家同一次运行的日志和原生日志。测试原始日志及结果按时间分别留在`runtime/qa/场景-时间/`，含构建指纹与日志SHA256；失败和中止也保留证据，不覆盖成通过。旧平铺日志仅为最近一次便捷副本。

## 联机定向验证

当前构建的联机通过组合以机器报告为准；完整联机与恢复矩阵已补齐，见[问题记录](../资料/问题记录.txt)。以下命令支持`--package-dir`只读测试已有三个文件，不更新安装或上传。

```powershell
python tools/check_coop.py --guest construct
python tools/check_coop.py --guest necrobinder
python tools/check_coop.py --guest ironclad
python tools/check_coop.py --guest necrobinder --players 3
python tools/check_coop.py --guest construct --players 4
python tools/check_coop.py --guest construct --latency-ms 70 --jitter-ms 20 --loss-percent 2 --blackout-seconds 3
python tools/check_coop.py --guest necrobinder --players 3 --latency-ms 90 --jitter-ms 30 --loss-percent 3 --blackout-seconds 4
python tools/check_coop.py --guest construct --players 4 --latency-ms 100 --jitter-ms 40 --loss-percent 5 --blackout-seconds 5
```

每次启动2—4个独立游戏进程，使用原生ENet连接、原生大厅及真实网络行动／选牌消息。默认实际渲染、静音；不关闭原生状态校验。临时文件只协调测试布置和比较结果，不传输玩家选择。直接进入测试战斗与事件，不自动打完整局。仅结束该脚本自己启动的进程，用户存档和Steam游戏文件不变，不更新dist。

弱网参数启用仅绑定127.0.0.1的独立UDP代理，各客端经过独立端口与主机通信。latency是单程延迟，jitter是每个数据报的随机偏移，loss是双向随机丢包百分比；代理随机数独立于游戏随机数。blackout会在第一张愈战愈勇的真实手牌选择前丢弃双向数据报，持续指定秒数后恢复，不传递或伪造游戏选择。结果记录各方向实际收发、丢包、断流计数、延迟和阶段事件；必须确实发生丢包／断流并且全部端断言、原生状态校验和按版本要求的阶段快照通过才算成功。每轮上限300秒。

Windows可能在原生主机正常退出后向UDP代理报告端口关闭；只有所有端均已完成最后checked屏障时才作为退出事件保留。提前发生则仍判失败，游戏错误判定不变。网络压力结果以独立-network键写入current-verification.json，不覆盖普通直连证据；若已有当前构建的网络压力失败，汇总也会列为失败。历史失败结果始终保留在自己的qa目录。


测试覆盖独立召唤标记、遗物成长和归属、回合结束、普通／升级生成牌、延迟随机伤害、真实手牌变牌选择、战斗结束重置及海玻璃远端奖励选择；另检查连续战斗、各玩家同时提交攻击、队友死亡后的胜利与开口扳手归属；0.4.13另核对新螺丝的独立伤害标记／下回合抽牌、油壶同步目标随机数及指节击败格挡。比较战斗开始、标记、神格、下一回合、临时力量消退、变牌、奖励、并发行动、幸存者胜利九个阶段的所有进程快照。0.4.14另让每位玩家通过原生网络队列打出强化三次的灼热铁斩波，核对11伤害／11格挡；快照包含精确强化次数和动态数值。未知错误、诊断观察器异常、异常退出、缺失快照或任一快照不同均判失败。

`runtime/current-verification.json`按实际构建指纹汇总，旧coop-verification.json仅为历史。0.4.23完整验收要求31项功能、2／3／4人直连、四人弱网及四种恢复；missing_checks／missing_multiplayer／missing_recovery列出缺项。0.4.23共12阶段快照，包含拳法生成／结算和潘多拉魔盒；旧0.4.20—0.4.21为11阶段；旧构建仍按当时要求检查。

本机ENet测试不代替跨机器Steam线路、热重连或工坊订阅验证。作者与朋友已反馈此前联机总体正常；本次0.4.23实玩另记结果。`launch.ps1`关闭Steam接口，Steam邀请需从Steam启动手动安装版本。

`python tools/check_current.py modes --rendered`通过原生自定义模式界面输入种子、勾选挑战；逐项检查界面列出的挑战、原生初始化选牌、卡牌奖励、商店和磁盘保存重建，再检查代表性组合。此专项上限300秒，不自动推进地图。勾选原版角色卡池挑战时允许该角色完整卡池；普通模式仍使用选定名单与体系门槛，海玻璃和色彩哲学家的专用范围保持其设定。

## 联机断开与存档恢复

`RecoveryProbe`是独立QA模组，仅复制进临时环境，不能装入试玩或发布目录。支持2—4人，两阶段各启动全新游戏进程：先建立原生联机、准备升级／附魔牌和专属遗物并等待主机存档落盘；一名客端退出后，其余客端检查连接与对局仍保留，再由新进程实际加载主机磁盘存档、经原生LoadRunLobby重新组队，核对状态并实际网络出牌。每阶段上限150秒，静音、独立档，不推进整局。

```powershell
dotnet build tools/RecoveryProbe/RecoveryProbe.csproj -c Release
python tools/check_recovery.py
python tools/check_recovery.py --players 3 --party mixed
python tools/check_recovery.py --players 4
python tools/check_recovery.py --players 3 --party mixed --resume-order reverse
python tools/check_recovery.py --abrupt
python tools/check_recovery.py --kill-client --players 4 --resume-order reverse --package-dir dist/SteamWorkshop/content
python tools/check_recovery.py --kill-host --players 3 --party mixed --resume-order reverse --package-dir dist/SteamWorkshop/content
dotnet build tools/RecoveryProbe/RecoveryProbe.csproj -c Release -p:NativeOnly=true
python tools/check_recovery.py --independent --abrupt
```

`--party mixed`使用构装体主机、亡灵契约师客端和铁甲战士客端；其他配置为全构装体。构装体各槽位设置不同数量的测试牌和不同层数的附魔，避免同牌组掩盖身份互换。`--resume-order reverse`只在读档时倒转客端实际加入顺序，玩家网络身份保持原值；快照仍须逐玩家与保存前完全一致。该参数不伪造或改写原生身份分配。




`--kill-client`／`--kill-host`由脚本直接终止自己启动的对应隔离进程，不调用正常退出；存活端必须由真实原生断开通知／菜单状态完成断言，再用全新进程组队读主机磁盘存档。被故意终止的一端标记expected_termination，不冒充执行过退出后断言。文件只补齐已终止进程的QA屏障，不能满足存活端的原生断开条件；不修改游戏超时参数。两参数与--abrupt互斥。

`--native-control`仅更换原版角色但仍加载角色模组；`--independent`使用不引用角色程序集的NativeOnly产物，移除角色模组并在各进程检查程序集缺席。两种对照不能混称。NativeOnly的输出在bin/Native下，与普通QA产物分开。

本机原生ENet开局调用SetHostIsClosed(true)，会拒绝运行中新连接。`--live-join`仅用于保留原版加入界面的诊断，实际超时不能算热重连通过。没有为测试打开原生关闭的端口。Steam线路仍须单独验证。报告中的recovery_checks保存本角色退出／强制关闭／进程丢失恢复结果，diagnostic_controls保存热加入和原版／独立对照，失败保持失败；所有快照、原始日志和隔离存档留在qa独立目录。

## 创意工坊发布准备

content已同步0.4.26（Native Bridge + 五域适配器、英文本地化与 14 个语言镜像的 DLL），供手动复制mods及发给朋友。按该DLL真实渲染复核：35项专项全部通过；恢复4/4、联机4/4通过（2p构装体、3p死灵、4p构装体、4p弱网），`full_local_verification`与`full_invariant_verification`均为true。`dist/SteamWorkshop/verification.json`已按0.4.26候选三个文件复核通过：`targeted_package_checks`为PASS、`network_multiplayer`为PASS_LOCAL_ENET、`upload_status`为NOT_UPLOADED；核对完整资源237份、BaseLib唯一依赖与8个联机／恢复场景。远端公开条目3826907330线上仍为0.4.24（未动）；另建私密测试条目3835179658「拳击构装体(测试)」，内容与0.4.26候选一致，证据见`runtime/test-workshop-0.4.26.json`。正式上传后须重跑`workshop.py --verify`更新线上状态。`full_release_verification`仍为false，但该字段在`report_local.py`里恒为false、没有代码路径会置true。

`python tools/workshop.py --status`只读核对文件、资源、旧报告覆盖与发布文案；报告不匹配会如实显示，不能把content更新视为完整验收。状态见[发布前清单](../资料/发布前清单.txt)。

此前使用`python tools/workshop.py --prepare`生成`dist/SteamWorkshop`，采用官方上传工具的目录格式；不会上传，也不生成ZIP。封面从作者提供的 `资料/宣传图/模组封面.png` 等比缩小，不修改源图。

`python tools/check_current.py 模式 --package-dir dist/SteamWorkshop/content`直接检查发布候选的三个文件，记录包内文件的SHA256。完成专项后运行`python tools/workshop.py --verify`核对完整资源、BaseLib唯一依赖、存档未变及对应日志；另须通过同一DLL／PCK／清单的2／3／4人联机、普通及强制断开、四人客端进程丢失与三人混组主机进程丢失恢复。release_evidence.py只按实际三个文件指纹筛选，联机证据另复制进验收日志并记录全部校验值。--status为只读核对。旧release.py仍针对历史版本，不作为这次发布入口。

`python tools/check_candidate_network.py`顺序补齐当前候选缺失的8项联机／恢复验收，包含四人弱网。已通过且三个文件指纹完全相同的证据直接复用；不同构建绝不复用。进度写入runtime/candidate-network-版本.json，测试直接跳转房间，不运行整局。

创意工坊标题、正文、可见性和BaseLib工坊ID位于`dist/SteamWorkshop/workshop.json`。仅`content`上传；验证报告及本地存档校验值不在上传目录。未取得作者明确同意前，不运行上传、创建条目或公开发布。观者素材已注明Boninall及来源页面，用户确认没有该作者授权；来源标注不能当作许可结论。

官方上传工具v0.2.0已放在`tools/ModUploader`，下载包SHA256与官方发布一致；`--help`与`upload --help`已验证。获得作者最后上传许可后，才在工作区根目录运行`.\tools\ModUploader\ModUploader.exe upload -w .\dist\SteamWorkshop`。帮助命令不初始化Steam；此前0.4.15私密上传已验证当时账号权限与协议状态；再次上传须重新核对当前状态并取得作者许可。


## 选人动画专项

`python tools/check_current.py poster --rendered`仅进入真实选人界面，检查分层、实际动作／粒子、循环、标题可见性、切换清理、独立实例和屏幕比例。录制128帧供预览；隔离档关闭后台限帧，不改作者设置。当前完整验收项由checks_for_version动态决定，0.4.23为31项。

Steam条目3826907330已于2026-10-27上传0.4.23，当时private、未公开，2026-11-05转为公开：工坊订阅回下载三文件哈希与候选一致，从Steam启动加载0.4.23＋BaseLib 3.4.7且零错误。不能重复称从未上传或再次创建条目。0.4.15历史状态见runtime/history/steam-release-0.4.15.json。

## 多人宝箱手势专项

`python tools/check_coop.py --treasure-only --players 2 --guest construct`直接进入共享宝箱，使用真实ENet鼠标状态同步和真实视口移动／按下／释放事件检查选择；`--players 4 --guest ironclad`覆盖混合角色及四个手势方向。检查三种窗口比例和不同端尺寸、指向与猜拳纹理、旧错误映射对照、悬停命中及领取结果。scenario为coop-treasure，与完整战斗套件分开，不把单独宝箱通过计入完整联机验收。全部静音隔离。该宝箱修复已包含于当前源码与content；活动试玩文件保留至作者退出，随后由启动器同步；条目已公开。

## 草稿第一段专项

`python tools/check_current.py draft --rendered`仅创建临时静音存档，直达漩涡、融合者及受控战斗，检查涅奥护符、先古营养汤候选条件、古老牙齿预览与实际领取、已附魔小斩波排除、递增强化与存档、打击木偶加成、品质隔离、不倒不休状态说明及自制选人音效播放路由。`soup`另检查所有已附魔小斩波被汤跳过后的原样保留、克隆与存读档；`additions --rendered`检查实际回合发牌、灼烧动画与弃牌计数。测试工具本身只构建临时QA资源包；按作者最新指示另将已核对版本同步到工作区试玩，不自动更新分发文件或Steam；本次content已由作者明确要求另行同步。

## 变牌、魔盒与音效专项

`additions --rendered`实际领取潘多拉魔盒，检查5张小斩波／永恒保护、原版角色隔离、卡池与原生预览；同时观察真实变牌卡面模型更新帧与音效调用，覆盖普通／升级、灼烧、满手牌和取消。`animation --rendered`检查攻击原生播放通路、静音及原版动作；0.4.22直接FMOD实例与截断方案已撤回。`facing --rendered`核对实际螃蟹转向与死亡；`selection --rendered`检查重做的选人声仍有真实混音信号。联机从0.4.23增加pandora快照，核对原生批量变化与本地结果预览归属。

0.4.23声音回归：selection从原生音量设置入口建立条件，检查最终Master混音；不再手动调高中间总线来掩盖初始化遗漏。原生FMOD输出和Godot流音频为不同通路，不能用有效FMOD句柄或SFX中间总线峰值证明最终听得到。测试同时静音两套输出，再按原设置恢复。首版选人WAV恢复0.78秒，尚待作者试听。
