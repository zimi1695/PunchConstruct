"""只读核验 Steam 创意工坊条目的封面与附加宣传图 URL 是否非空。

用法：python tools/verify_previews.py [item_id]
默认核验公开条目 3826907330。附加预览空壳（URL 全空）是反复出现的上传故障，
每次 ModUploader 上传后都应跑本脚本确认 6 张附加宣传图 URL 非空。
修复见 tools/README.md「附加宣传图空壳护栏」。只读，不改条目；os._exit 避免 SteamAPI_Shutdown 挂起。
"""
import ctypes as C
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import steam_workshop_check as s

ITEM = int(sys.argv[1]) if len(sys.argv) > 1 else 3826907330


def main():
    out = {'item_id': ITEM, 'url': f'https://steamcommunity.com/sharedfiles/filedetails/?id={ITEM}'}
    with s.Steam() as steam:
        q = steam.function('SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest',
            C.c_uint64, C.c_void_p, C.POINTER(C.c_uint64), C.c_uint32)(
                steam.ugc, (C.c_uint64 * 1)(ITEM), 1)
        try:
            steam.function('SteamAPI_ISteamUGC_SetReturnAdditionalPreviews', C.c_bool,
                C.c_void_p, C.c_uint64, C.c_bool)(steam.ugc, q, True)
            steam.function('SteamAPI_ISteamUGC_SetAllowCachedResponse', C.c_bool,
                C.c_void_p, C.c_uint64, C.c_uint32)(steam.ugc, q, 0)
            call = steam.function('SteamAPI_ISteamUGC_SendQueryUGCRequest', C.c_uint64,
                C.c_void_p, C.c_uint64)(steam.ugc, q)
            steam.call_result(call, s.QueryResult, 3401)
            cover = C.create_string_buffer(2048)
            if steam.function('SteamAPI_ISteamUGC_GetQueryUGCPreviewURL', C.c_bool,
                C.c_void_p, C.c_uint64, C.c_uint32, C.c_void_p, C.c_uint32)(
                    steam.ugc, q, 0, cover, len(cover)):
                out['cover_url'] = cover.value.decode('utf-8', 'replace')
            n = steam.function('SteamAPI_ISteamUGC_GetQueryUGCNumAdditionalPreviews',
                C.c_uint32, C.c_void_p, C.c_uint64, C.c_uint32)(steam.ugc, q, 0)
            get = steam.function('SteamAPI_ISteamUGC_GetQueryUGCAdditionalPreview', C.c_bool,
                C.c_void_p, C.c_uint64, C.c_uint32, C.c_uint32, C.c_void_p, C.c_uint32,
                C.c_void_p, C.c_uint32, C.POINTER(C.c_int))
            previews = []
            for i in range(n):
                url = C.create_string_buffer(2048)
                name = C.create_string_buffer(512)
                ptype = C.c_int()
                ok = get(steam.ugc, q, 0, i, url, len(url), name, len(name), C.byref(ptype))
                previews.append({'i': i, 'ok': bool(ok),
                                 'name': name.value.decode('utf-8', 'replace'),
                                 'type': ptype.value,
                                 'url': url.value.decode('utf-8', 'replace')})
            out['num_additional_previews'] = n
            out['previews'] = previews
            out['all_urls_nonempty'] = bool(out.get('cover_url')) and all(p['url'] for p in previews)
        finally:
            steam.function('SteamAPI_ISteamUGC_ReleaseQueryUGCRequest', C.c_bool,
                C.c_void_p, C.c_uint64)(steam.ugc, q)
    print(json.dumps(out, ensure_ascii=False, indent=2))
    sys.stdout.flush()
    os._exit(0 if out.get('all_urls_nonempty') else 1)


if __name__ == '__main__':
    main()