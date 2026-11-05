"""Steam Workshop checks using the official uploader's native Steam API.

Default operation is read-only. No credentials, tickets or account IDs are logged.
"""
import ctypes as C
import argparse
import hashlib
import json
import os
from pathlib import Path
import time

ROOT = Path(__file__).resolve().parent.parent
APP_ID = 2868840


class EulaStatus(C.Structure):
    _fields_ = [('result', C.c_int), ('app_id', C.c_uint32),
                ('version', C.c_uint32), ('action_time', C.c_uint32),
                ('accepted', C.c_bool), ('needs_action', C.c_bool)]


class QueryResult(C.Structure):
    _fields_ = [('handle', C.c_uint64), ('result', C.c_int),
                ('returned', C.c_uint32), ('total', C.c_uint32),
                ('cached', C.c_bool), ('cursor', C.c_char * 256)]


class DetailsPrefix(C.Structure):
    _fields_ = [('item_id', C.c_uint64), ('result', C.c_int),
                ('file_type', C.c_int), ('creator_app', C.c_uint32),
                ('consumer_app', C.c_uint32), ('title', C.c_char * 129),
                ('description', C.c_char * 8000), ('owner', C.c_uint64),
                ('created', C.c_uint32), ('updated', C.c_uint32),
                ('added', C.c_uint32), ('visibility', C.c_int),
                ('banned', C.c_bool)]


class SubscribeResult(C.Structure):
    _fields_ = [('result', C.c_int), ('item_id', C.c_uint64)]


class Steam:
    def __init__(self):
        os.environ['SteamAppId'] = str(APP_ID)
        os.environ['SteamGameId'] = str(APP_ID)
        self.lib = C.CDLL(str(ROOT / 'tools/ModUploader/steam_api64.dll'))
        self.active = False

    def function(self, name, result, *args):
        fn = getattr(self.lib, name)
        fn.restype = result
        fn.argtypes = list(args)
        return fn

    def __enter__(self):
        error = C.create_string_buffer(1024)
        result = self.function('SteamAPI_InitFlat', C.c_int, C.c_void_p)(error)
        if result != 0:
            raise RuntimeError(f'Steam init {result}: {error.value.decode("utf-8", "replace")}')
        self.active = True
        self.utils = self.function('SteamAPI_SteamUtils_v010', C.c_void_p)()
        self.ugc = self.function('SteamAPI_SteamUGC_v021', C.c_void_p)()
        self.user = self.function('SteamAPI_SteamUser_v023', C.c_void_p)()
        self.apps = self.function('SteamAPI_SteamApps_v009', C.c_void_p)()
        if not all([self.utils, self.ugc, self.user, self.apps]):
            self.__exit__(None, None, None)
            raise RuntimeError('A required Steam interface is unavailable')
        return self

    def __exit__(self, *unused):
        if self.active:
            self.function('SteamAPI_Shutdown', None)()
            self.active = False

    def call_result(self, call, structure, callback, timeout=25):
        if not call:
            raise RuntimeError('Steam returned an invalid API call handle')
        done = self.function('SteamAPI_ISteamUtils_IsAPICallCompleted', C.c_bool,
                             C.c_void_p, C.c_uint64, C.POINTER(C.c_bool))
        get = self.function('SteamAPI_ISteamUtils_GetAPICallResult', C.c_bool,
                            C.c_void_p, C.c_uint64, C.c_void_p, C.c_int,
                            C.c_int, C.POINTER(C.c_bool))
        deadline = time.monotonic() + timeout
        failed = C.c_bool()
        while time.monotonic() < deadline:
            if done(self.utils, call, C.byref(failed)):
                if failed.value:
                    raise RuntimeError('Steam API call failed')
                result = structure()
                if not get(self.utils, call, C.byref(result), C.sizeof(result),
                           callback, C.byref(failed)) or failed.value:
                    raise RuntimeError('Steam API result could not be read')
                return result
            time.sleep(.1)
        raise TimeoutError('Steam API call timed out')


def preflight():
    report = {'checked_at': time.strftime('%Y-%m-%d %H:%M:%S'),
              'app_id': APP_ID, 'operation': 'read_only_preflight',
              'uploaded': False, 'public': False}
    try:
        with Steam() as steam:
            report['steam_initialized'] = True
            report['logged_on'] = steam.function('SteamAPI_ISteamUser_BLoggedOn',
                C.c_bool, C.c_void_p)(steam.user)
            report['game_license'] = steam.function('SteamAPI_ISteamApps_BIsSubscribedApp',
                C.c_bool, C.c_void_p, C.c_uint32)(steam.apps, APP_ID)
            call = steam.function('SteamAPI_ISteamUGC_GetWorkshopEULAStatus',
                C.c_uint64, C.c_void_p)(steam.ugc)
            status = steam.call_result(call, EulaStatus, 3420)
            report['app_workshop_eula'] = {
                'result': status.result, 'app_id': status.app_id,
                'version': status.version, 'accepted': status.accepted,
                'needs_action': status.needs_action}
    except Exception as exc:
        report['error'] = str(exc)
    path = ROOT / 'tools/runtime/steam-preflight-0.4.15.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return report


def check_item(item_id, subscribe_download=False):
    report = {'checked_at': time.strftime('%Y-%m-%d %H:%M:%S'),
              'item_id': item_id, 'operation': 'subscribe_download' if subscribe_download else 'read_only_item',
              'url': f'https://steamcommunity.com/sharedfiles/filedetails/?id={item_id}'}
    try:
        config = json.loads((ROOT / 'dist/SteamWorkshop/workshop.json').read_text(encoding='utf-8'))
        with Steam() as steam:
            query = steam.function('SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest',
                C.c_uint64, C.c_void_p, C.POINTER(C.c_uint64), C.c_uint32)(steam.ugc, (C.c_uint64 * 1)(item_id), 1)
            try:
                if not steam.function('SteamAPI_ISteamUGC_SetReturnChildren', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_bool)(steam.ugc, query, True):
                    raise RuntimeError('Cannot request dependencies')
                if not steam.function('SteamAPI_ISteamUGC_SetReturnLongDescription', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_bool)(steam.ugc, query, True):
                    raise RuntimeError('Cannot request full description')
                steam.function('SteamAPI_ISteamUGC_SetAllowCachedResponse', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_uint32)(steam.ugc, query, 0)
                call = steam.function('SteamAPI_ISteamUGC_SendQueryUGCRequest', C.c_uint64,
                    C.c_void_p, C.c_uint64)(steam.ugc, query)
                result = steam.call_result(call, QueryResult, 3401)
                report['query_result'] = result.result
                if result.result != 1 or result.returned != 1:
                    raise RuntimeError(f'Item query failed: {result.result}, {result.returned} results')
                buffer = C.create_string_buffer(16384)
                if not steam.function('SteamAPI_ISteamUGC_GetQueryUGCResult', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_uint32, C.c_void_p)(steam.ugc, query, 0, buffer):
                    raise RuntimeError('Cannot read item metadata')
                details = DetailsPrefix.from_buffer(buffer)
                report.update(remote_result=details.result, title=details.title.decode('utf-8'),
                    app_id=details.consumer_app, visibility=details.visibility, banned=details.banned,
                    title_matches=details.title.decode('utf-8') == config['title'],
                    description_matches=details.description.decode('utf-8') == config['description'])
                children = (C.c_uint64 * 16)()
                if not steam.function('SteamAPI_ISteamUGC_GetQueryUGCChildren', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_uint32, C.POINTER(C.c_uint64), C.c_uint32)(
                        steam.ugc, query, 0, children, 16):
                    raise RuntimeError('Cannot read dependencies')
                report['dependencies'] = [x for x in children if x]
                preview = C.create_string_buffer(2048)
                if steam.function('SteamAPI_ISteamUGC_GetQueryUGCPreviewURL', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_uint32, C.c_void_p, C.c_uint32)(steam.ugc, query, 0, preview, len(preview)):
                    report['preview_url'] = preview.value.decode('utf-8')
                visibility_map = {'public': 0, 'friends_only': 1, 'private': 2, 'unlisted': 3}
                expected_visibility = visibility_map.get(config.get('visibility', 'private'), 2)
                if not (details.item_id == item_id and details.result == 1 and details.consumer_app == APP_ID
                        and details.visibility == expected_visibility and not details.banned and report['title_matches']
                        and report['description_matches'] and report['dependencies'] == config['dependencies']):
                    raise RuntimeError('Remote metadata does not match the local candidate')
                report['remote_metadata'] = 'PASS'
            finally:
                steam.function('SteamAPI_ISteamUGC_ReleaseQueryUGCRequest', C.c_bool,
                    C.c_void_p, C.c_uint64)(steam.ugc, query)
            state_fn = steam.function('SteamAPI_ISteamUGC_GetItemState', C.c_uint32,
                C.c_void_p, C.c_uint64)
            report['initial_state'] = state_fn(steam.ugc, item_id)
            if subscribe_download:
                if not report['initial_state'] & 1:
                    call = steam.function('SteamAPI_ISteamUGC_SubscribeItem', C.c_uint64,
                        C.c_void_p, C.c_uint64)(steam.ugc, item_id)
                    result = steam.call_result(call, SubscribeResult, 1313)
                    report['subscribe_result'] = result.result
                    if result.result != 1 or result.item_id != item_id:
                        raise RuntimeError(f'Subscribe failed: {result.result}')
                if not steam.function('SteamAPI_ISteamUGC_DownloadItem', C.c_bool,
                    C.c_void_p, C.c_uint64, C.c_bool)(steam.ugc, item_id, True):
                    raise RuntimeError('Steam rejected DownloadItem')
                deadline = time.monotonic() + 150
                last_print = 0
                while time.monotonic() < deadline:
                    state = state_fn(steam.ugc, item_id)
                    if state & 4 and not state & (8 | 16 | 32):
                        break
                    if time.monotonic() - last_print > 5:
                        downloaded, total = C.c_uint64(), C.c_uint64()
                        steam.function('SteamAPI_ISteamUGC_GetItemDownloadInfo', C.c_bool,
                            C.c_void_p, C.c_uint64, C.POINTER(C.c_uint64), C.POINTER(C.c_uint64))(
                                steam.ugc, item_id, C.byref(downloaded), C.byref(total))
                        print(f'Download state={state} bytes={downloaded.value}/{total.value}', flush=True)
                        last_print = time.monotonic()
                    time.sleep(.2)
                else:
                    raise TimeoutError('Steam download did not complete within 150 seconds')
            report['state'] = state_fn(steam.ugc, item_id)
            size, stamp, folder = C.c_uint64(), C.c_uint32(), C.create_string_buffer(2048)
            installed = steam.function('SteamAPI_ISteamUGC_GetItemInstallInfo', C.c_bool,
                C.c_void_p, C.c_uint64, C.POINTER(C.c_uint64), C.c_void_p, C.c_uint32,
                C.POINTER(C.c_uint32))(steam.ugc, item_id, C.byref(size), folder, len(folder), C.byref(stamp))
            report['installed'] = installed
            if installed:
                path = Path(folder.value.decode('utf-8'))
                report['install_path'] = str(path)
                report['installed_bytes'] = size.value
                report['installed_files'] = sorted(p.relative_to(path).as_posix() for p in path.rglob('*') if p.is_file())
                hashes = {name: hashlib.sha256((path / name).read_bytes()).hexdigest() for name in
                    ('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')}
                report['downloaded_hashes'] = hashes
                report['files_match_candidate'] = all(digest == hashlib.sha256((ROOT / 'dist/SteamWorkshop/content' / name).read_bytes()).hexdigest()
                                                     for name, digest in hashes.items())
                if not report['files_match_candidate']:
                    raise RuntimeError('Downloaded content differs from tested candidate')
            elif subscribe_download:
                raise RuntimeError('Steam reports no installed path after download')
    except Exception as exc:
        report['error'] = str(exc)
    report['passed'] = report.get('remote_metadata') == 'PASS' and 'error' not in report and (
        not subscribe_download or report.get('files_match_candidate') is True)
    output = ROOT / 'tools/runtime/steam-workshop-0.4.15.json'
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--item', type=int)
    parser.add_argument('--subscribe-download', action='store_true')
    args = parser.parse_args()
    if args.subscribe_download and not args.item:
        parser.error('--subscribe-download requires --item')
    if args.item:
        raise SystemExit(0 if check_item(args.item, args.subscribe_download)['passed'] else 1)
    preflight()
