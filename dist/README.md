# 交付文件

当前手动分发使用 [SteamWorkshop/content](SteamWorkshop/content)，版本 **0.4.28**。目录名沿用上传工具要求；条目 3826907330 已公开。

关闭游戏，把content里的DLL、PCK、JSON三个文件复制到 `游戏目录/mods/PunchConstruct`。你和朋友使用同一份文件。见 [安装说明](SteamWorkshop/安装说明.txt)。

| 位置 | 实际版本与用途 |
|---|---|
| SteamWorkshop/content | 当前0.4.28，供手动安装与朋友试玩 |
| releases/0.4.24、releases/0.4.25、releases/0.4.26、releases/0.4.28 | 每个版本的三文件归档，用于 GitHub Release；0.4.27 仅保留 ZIP 附件；工坊只上最新版 |
| SteamWorkshop/workshop.json、发布说明.txt | 当前本地发布文案；修改文件不等于已经上传 |
| SteamWorkshop/verification.json、验收日志 | verification.json 已按 0.4.28 三文件复核 PASS，upload_status=UPLOADED_PUBLIC；公开条目 3826907330 与私密测试条目 3835179658 均已上传 0.4.28。GitHub main 与标签 `v0.4.28` 已推送（提交 `6493ae5`）。验收结果见 tools/runtime/current-verification.json |
| PunchConstruct | `tools/package.py` 的当前构建输出（现为0.4.28），供本机试玩脚本读取；不对外分发、不入库 |

旧 ZIP、0.4.16 好友包和旧安装脚本已删除，工作区不再保留这些历史产物；请按上面的手动安装方式操作。
Steam条目3826907330已于2026-11-01更新为0.4.24，未新建；已于2026-11-05公开，文案换为作者定稿版本；2026-11-04封面改用模组封面图，`SteamWorkshop/previews` 更新为 6 张宣传图作为附加预览。2026-11-07另建私密测试条目3835179658「拳击构装体(测试)」，原公开条目未动。2026-11-09 已上传 0.4.28 到公开条目与测试条目。当前文件校验和验收结果见 [发布审查](SteamWorkshop/发布审查.txt)。
