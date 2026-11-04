# 交付文件

当前手动分发使用 [SteamWorkshop/content](SteamWorkshop/content)，版本 **0.4.24**。目录名沿用上传工具要求；本轮构建完成后会上传私密条目。

关闭游戏，把content里的DLL、PCK、JSON三个文件复制到 `游戏目录/mods/PunchConstruct`。你和朋友使用同一份文件。见 [安装说明](SteamWorkshop/安装说明.txt)。

| 位置 | 实际版本与用途 |
|---|---|
| SteamWorkshop/content | 当前0.4.24，供手动安装与朋友试玩 |
| SteamWorkshop/workshop.json、发布说明.txt | 当前本地发布文案；修改文件不等于已经上传 |
| SteamWorkshop/verification.json、验收日志 | verification.json已对当前content三个文件逐文件复核通过（`targeted_package_checks`、`network_multiplayer`为PASS）；验收日志目录已含本次复核的日志副本 |
| PunchConstruct | `tools/package.py` 的当前构建输出（现为0.4.24），供本机试玩脚本读取；不对外分发、不入库 |

旧 ZIP、0.4.16 好友包和旧安装脚本已删除，工作区不再保留这些历史产物；请按上面的手动安装方式操作。
Steam私密条目已于2026-11-01更新为0.4.24：同一私密条目，未新建、未公开，文案换为作者定稿版本；2026-11-04封面改用模组封面图，`SteamWorkshop/previews` 更新为 6 张宣传图作为附加预览。当前文件校验和验收结果见 [发布审查](SteamWorkshop/发布审查.txt)。没有重新生成ZIP。
