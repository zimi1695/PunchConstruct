# 交付文件

当前手动分发使用 [SteamWorkshop/content](SteamWorkshop/content)，版本 **0.5.2**（候选 DLL `0a6ea643…`、PCK `99a6c13b…`、JSON `9ceef44e…`、277 份资源；在 0.5.1 基础上修复 SEARCH 预览、古牙构装体映射、清理临时力量死代码、共享遗物池按玩家过滤与稳健性加固，含独立审查 3 缺口修复、愚者卡边框与脑蛭无色池修复、0.114 适配；代码编译 0 警告 0 错误，**0.114.0 本地 35/35、精确包 35/35、联机 4/4（含 4p 弱网）与恢复 4/4 验收通过**；不变量 16/16，0.113.0 兼容基线证据保留）。公开版本为 **0.5.1**（DLL `858f76de…`，已上线条目 3826907330）；0.4.28 归档保持冻结。

关闭游戏，把content里的DLL、PCK、JSON三个文件复制到 `游戏目录/mods/PunchConstruct`。你和朋友使用同一份文件。见 [安装说明](SteamWorkshop/安装说明.txt)。

| 位置 | 实际版本与用途 |
|---|---|
| SteamWorkshop/content | 当前0.5.2候选（DLL `0a6ea643…`、PCK `99a6c13b…`），供手动安装与朋友试玩；0.114.0本地35/35、精确包35/35、联机4/4与恢复4/4通过；公开条目现为0.5.1 |
| releases/0.4.24、releases/0.4.25、releases/0.4.26、releases/0.4.28、releases/0.5.1、releases/0.5.2 | 每个版本的三文件归档，用于 GitHub Release；0.4.27 仅保留 ZIP 附件；工坊只上最新版。0.5.2 ZIP 由 `tools/release.py` 从精确三件套确定性生成，`--check` 可逐字节复现 |
| SteamWorkshop/workshop.json、发布说明.txt | 当前0.5.2本地发布文案；修改文件不等于已经上传 |
| SteamWorkshop/verification.json、验收日志 | verification.json 已按 0.5.2 候选 DLL `0a6ea643…`（PCK `99a6c13b…`、277 份资源）重建，`targeted_package_checks=PASS`、`network_multiplayer=PASS_LOCAL_ENET`（0.114 联机4/4与恢复4/4通过，0.113 基线证据仍保留）、`game=0.114.0`。公开条目 3826907330 现为 0.5.1（已上线）；0.5.2 私测待 Steam、公开待作者许可。验收结果见 tools/runtime-0.114/current-verification.json（本地35/35、不变量16/16；full_release=false 因未上传+外部轴未验证） |
| PunchConstruct | `tools/package.py` 的当前构建输出（现为0.5.2），供本机试玩脚本读取；不对外分发、不入库 |

旧 ZIP、0.4.16 好友包和旧安装脚本已删除，工作区不再保留这些历史产物；请按上面的手动安装方式操作。
Steam条目3826907330已于2026-11-05公开，2026-11-09 上传 0.4.28，2026-11-17 经作者同意上传 0.5.1（DLL `858f76de…`，回下载哈希校验通过）；2026-11-07另建私密测试条目3835179658「拳击构装体(测试)」，2026-11-17 20:10 已按 0.5.1 最终DLL重传并校验通过。0.5.2 候选（DLL `0a6ea643…`）0.114 本地 35/35、精确包 35/35、联机 4/4 与恢复 4/4 已通过，不变量 16/16；ZIP 已由 `release.py` 确定性重建（SHA256 `fb4e66d0…`）；私测待Steam重传、公开待作者许可。当前文件校验和验收结果见 [发布审查](SteamWorkshop/发布审查.txt)。
