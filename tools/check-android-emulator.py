"""Run only in CI on a freshly wiped emulator; never inspect a real phone."""
import json
import os
import socket
import subprocess
import time
import urllib.error
import urllib.request

if os.environ.get('GITHUB_ACTIONS') != 'true':
    raise SystemExit('This check is restricted to disposable GitHub Actions emulators.')
def adb(*arguments, required=True):
    result = subprocess.run(['adb', *arguments], capture_output=True, timeout=20)
    if required and result.returncode:
        raise RuntimeError('Emulator command failed; no private output is printed')
    return result.stdout
if adb('shell', 'getprop', 'ro.kernel.qemu').strip() != b'1':
    raise SystemExit('Refusing to run against a physical device.')
package = 'io.github.iii85iii.vpn'
adb('install', '-r', 'mobile/android/app/build/outputs/apk/debug/app-debug.apk')
adb('shell', 'am', 'start', '-W', '-n', package+'/.MainActivity')
for attempt in range(80):
    data = adb('exec-out', 'run-as', package, 'cat', 'files/engine/config.json', required=False)
    token = adb('exec-out', 'run-as', package, 'cat', 'files/engine/local-api.token', required=False).strip()
    try:
        config = json.loads(data)
        if token:
            break
    except ValueError:
        pass
    time.sleep(.5)
else:
    raise RuntimeError('Native engine readiness failed on the emulator')
port = int(config['dashboard']['listen'].rsplit(':', 1)[1])
with socket.socket() as temporary:
    temporary.bind(('127.0.0.1', 0))
    forwarded = temporary.getsockname()[1]
adb('forward', f'tcp:{forwarded}', f'tcp:{port}')
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def request(path, authenticated=True):
    headers = {'X-Local-Token': token.decode()} if authenticated else {}
    with opener.open(urllib.request.Request(f'http://127.0.0.1:{forwarded}'+path, headers=headers), timeout=5) as response:
        return json.load(response)
try:
    for attempt in range(20):
        try:
            state = request('/api/status')
            break
        except OSError:
            time.sleep(.5)
    else:
        raise RuntimeError('Authenticated local API did not become available')
    if state.get('state', {}).get('phase') != 'off':
        raise RuntimeError('Fresh emulator must remain disconnected')
    if request('/api/accounts').get('accounts'):
        raise RuntimeError('Fresh emulator unexpectedly contains accounts')
    try:
        request('/api/accounts', authenticated=False)
        raise RuntimeError('Unauthenticated account API was accepted')
    except urllib.error.HTTPError as error:
        if error.code not in (401, 403):
            raise RuntimeError('Unexpected unauthenticated response') from None
    print('PASS Android native launch, local authenticated API, empty accounts, disconnected state, unauthenticated API rejection')
    adb('shell', 'am', 'force-stop', package)
finally:
    adb('forward', '--remove', f'tcp:{forwarded}', required=False)
