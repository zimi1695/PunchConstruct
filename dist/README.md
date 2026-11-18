# 交付文件

当前手动分发使用 [SteamWorkshop/content](SteamWorkshop/content)，版本 **0.5.1**（最终 DLL `858f76de…`、PCK `d67662e5…`、277 份资源；左勾拳削弱、`pcdeck`、愚者搬运、任务清单改动与极速（Blitz）／恐惧（Dread）模式适配均已合入；本地 35/35、联机 4/4（含 4p 弱网）、恢复 4/4 与 `workshop.py --verify` 全通过，content 已同步、归档与 ZIP 已重建；0.113.1 隔离预检 6/6 已在最终 DLL 复核，官方验收仍为 0.113.0；私密测试条目 3835179658 已按最终 DLL 重传并校验通过）。公开版本仍为 **0.4.28**，保持冻结；目录名沿用上传工具要求，条目 3826907330 已公开。

关闭游戏，把content里的DLL、PCK、JSON三个文件复制到 `游戏目录/mods/PunchConstruct`。你和朋友使用同一份文件。见 [安装说明](SteamWorkshop/安装说明.txt)。

| 位置 | 实际版本与用途 |
|---|---|
| SteamWorkshop/content | 当前0.5.1最终候选（DLL `858f76de…`、PCK `d67662e5…`），供手动安装与朋友试玩；已通过全量QA并同步，私密测试条目 3835179658 已重传并校验通过，公开条目尚未上传 |
| releases/0.4.24、releases/0.4.25、releases/0.4.26、releases/0.4.28、releases/0.5.1 | 每个版本的三文件归档，用于 GitHub Release；0.4.27 仅保留 ZIP 附件；工坊只上最新版 |
| SteamWorkshop/workshop.json、发布说明.txt | 当前0.5.1本地发布文案；修改文件不等于已经上传 |
| SteamWorkshop/verification.json、验收日志 | verification.json 已按最终 0.5.1 候选 DLL `858f76de…`（PCK `d67662e5…`、277 份资源）重建，`targeted_package_checks=PASS`、`network_multiplayer=PASS_LOCAL_ENET`；公开条目 upload_status=NOT_UPLOADED。私密测试条目 3835179658 已按最终 DLL 重传并校验通过（远端元数据＋回下载哈希＋清单）；公开条目 3826907330 仍为 0.4.28。0.113.1 隔离预检（startup、smoke、additions、pool、modes、selection 6/6）已在最终 DLL 复核 PASS，证据 `tools/runtime/*-0.113.1-preview.json`，官方验收仍以 0.113.0 为准。GitHub main 与标签 `v0.4.28` 已推送（提交 `6493ae5`），Release `v0.4.28` 已附 `PunchConstruct-0.4.28.zip`；0.5.1 尚未提交 GitHub 或发布公开 Release。验收结果见 tools/runtime/current-verification.json |
| PunchConstruct | `tools/package.py` 的当前构建输出（现为0.5.1），供本机试玩脚本读取；不对外分发、不入库 |

旧 ZIP、0.4.16 好友包和旧安装脚本已删除，工作区不再保留这些历史产物；请按上面的手动安装方式操作。
Steam条目3826907330已于2026-11-05公开，2026-11-09 已上传 0.4.28 到公开条目与测试条目；2026-11-07另建私密测试条目3835179658「拳击构装体(测试)」。旧0.5.1候选曾于2026-11-15上传私密测试条目3835179658（对应已作废的 `d22fa5b4…`）；最终 DLL `858f76de…` 已通过全量QA并同步content，2026-11-17 已按最终DLL重传私密测试条目并校验通过（远端元数据＋回下载哈希＋清单）；公开条目3826907330仍为0.4.28，待作者明确同意后再更新。当前文件校验和验收结果见 [发布审查](SteamWorkshop/发布审查.txt)。
