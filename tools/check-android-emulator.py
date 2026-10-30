"""Run only in CI on a freshly wiped emulator; never inspect a real phone."""
import json
import os
import socket
import subprocess
import time
import urllib.error
import urllib.request
import argparse
from pathlib import Path
import re
import xml.etree.ElementTree as ET

arguments = argparse.ArgumentParser()
arguments.add_argument('--apk', default='mobile/android/app/build/outputs/apk/debug/app-debug.apk')
arguments.add_argument('--upgrade')
arguments.add_argument('--probe',default='.build/android-qa/probe.apk')
arguments.add_argument('--output', default='test-results/android')
options = arguments.parse_args()
output = Path(options.output)
output.mkdir(parents=True, exist_ok=True)

if os.environ.get('GITHUB_ACTIONS') != 'true':
    raise SystemExit('This check is restricted to disposable GitHub Actions emulators.')
def adb(*arguments, required=True):
    result = subprocess.run(['adb', *arguments], capture_output=True, timeout=60)
    if required and result.returncode:
        raise RuntimeError('Emulator command failed; no private output is printed')
    return result.stdout
if adb('shell', 'getprop', 'ro.kernel.qemu').strip() != b'1':
    raise SystemExit('Refusing to run against a physical device.')
package = 'io.github.iii85iii.vpn'
adb('install', '-r', options.apk)
def launch():
    adb('shell', 'am', 'start', '-W', '-n', package+'/.MainActivity', '--ez', 'qa_screenshots', 'true')
launch()

def hierarchy():
    adb('shell', 'uiautomator', 'dump', '/sdcard/vpn-view.xml')
    return ET.fromstring(adb('exec-out', 'cat', '/sdcard/vpn-view.xml'))

def tap(text):
    for attempt in range(5):
        tree=hierarchy()
        for node in tree.iter('node'):
            if node.get('text','').casefold() == text.casefold() or node.get('content-desc','').casefold() == text.casefold():
                x1,y1,x2,y2 = map(int,re.findall(r'\d+',node.get('bounds','')))
                if x2 > x1 and y2 > y1:
                    adb('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2))
                    time.sleep(.7)
                    return
        # Keep gestures inside app controls, away from Android's back edges.
        scrolling=next((n for n in tree.iter('node') if n.get('class')=='android.widget.HorizontalScrollView'),None)
        if scrolling is not None:
            x1,y1,x2,y2=map(int,re.findall(r'\d+',scrolling.get('bounds','')))
            # Stay inside the control: Google's gesture navigation treats an
            # edge-origin swipe as Back and backgrounds the root activity.
            inset=(x2-x1)//4
            adb('shell','input','swipe',str(x2-inset),str((y1+y2)//2),str(x1+inset),str((y1+y2)//2),'300')
        else:
            scrolling=next((n for n in tree.iter('node') if n.get('class')=='android.widget.ScrollView' and n.get('package')==package),None)
            if scrolling is not None:
                x1,y1,x2,y2=map(int,re.findall(r'\d+',scrolling.get('bounds','')))
                adb('shell','input','swipe',str((x1+x2)//2),str(y1+(y2-y1)*3//4),str((x1+x2)//2),str(y1+(y2-y1)//4),'300')
    ET.ElementTree(tree).write(output/'ui-failure.xml')
    screenshot('ui-failure')
    raise RuntimeError('UI control unavailable: '+text)

def screenshot(name):
    (output/(name+'.png')).write_bytes(adb('exec-out','screencap','-p'))
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
    for attempt in range(12):
        if any(node.get('text','')=='Your Firefox account' for node in hierarchy().iter('node')):break
        time.sleep(.5)
    else:raise RuntimeError('Fresh account setup introduction was not shown')
    screenshot('account-intro')
    if any(node.get('text','') in ('Create account','Sign in') for node in hierarchy().iter('node')):raise RuntimeError('Account introduction must be informational only')
    tap('Got it')
    time.sleep(.5)
    screenshot('home')
    tap('Settings')
    tap('Mozilla accounts')
    tap('Add an account')
    fields=[node for node in hierarchy().iter('node') if node.get('class')=='android.widget.EditText']
    if len(fields)!=2:
        raise RuntimeError('Initial sign-in must contain email and password only')
    screenshot('sign-in')
    (output/'before-cancel.txt').write_bytes(adb('shell','dumpsys','activity','top'))
    tap('Cancel')
    (output/'after-cancel.txt').write_bytes(adb('shell','dumpsys','activity','top'))
    tap('Settings')
    screenshot('settings')
    tap('Privacy')
    time.sleep(.7)
    if not any('Privacy' in node.get('text','') for node in hierarchy().iter('node')):
        raise RuntimeError('Privacy document was not visible')
    screenshot('privacy')
    tap('Back to settings')
    adb('shell','settings','put','system','accelerometer_rotation','0')
    adb('shell','settings','put','system','user_rotation','1')
    time.sleep(1)
    screenshot('landscape')
    adb('shell','settings','put','system','user_rotation','0')
    adb('shell','settings','put','global','private_dns_mode','off')
    adb('install','-r',options.probe)
    adb('shell','appops','set',package,'ACTIVATE_VPN','allow')
    adb('shell','am','force-stop',package)
    adb('logcat','-c',required=False)
    (output/'tunnel-start.txt').write_bytes(adb('shell','am','start','-W','-f','0x20000000','-n',package+'/.MainActivity','--ez','qa_screenshots','true','--ez','qa_tun','true'))
    time.sleep(3)
    adb('shell','am','start','-W','-n','io.github.iii85iii.vpn.probe/.ProbeActivity')
    time.sleep(7)
    dns=adb('logcat','-d','-s','III85IIIProbe:I','*:S',required=False).decode(errors='replace')
    (output/'tunnel-probe.txt').write_text(dns)
    if 'PASS native TUN DNS' not in dns:
        screenshot('tunnel-failure')
        (output/'vpn-service.txt').write_bytes(adb('shell','dumpsys','activity','services',package,required=False))
        (output/'vpn-system.txt').write_bytes(adb('logcat','-d','-s','Vpn:I','VpnManagerService:I','ConnectivityService:I','AndroidRuntime:E','*:S',required=False)[-100000:])
        raise RuntimeError('Synthetic TUN DNS traffic did not reach the native VPN interface')
    (output/'tunnel.txt').write_text('PASS: real Android VPN interface/native TUN answered a synthetic DNS name with a fake address. No Mozilla account or upstream traffic used.\n')
    adb('shell','am','force-stop',package)
    if options.upgrade:
        # Only this disposable empty-account emulator: persist a synthetic required version.
        mandatory=b'<?xml version="1.0" encoding="utf-8"?><map><long name="required_update" value="23004" /><boolean name="account_intro_seen" value="true" /></map>'
        adb('shell',f'run-as {package} mkdir -p shared_prefs')
        gate_write=subprocess.run(['adb','shell',f'run-as {package} sh -c "cat > shared_prefs/interface.xml"'],input=mandatory,capture_output=True,timeout=20)
        if gate_write.returncode:raise RuntimeError('Could not stage the mandatory-update fixture')
        launch()
        time.sleep(2)
        tree=hierarchy()
        texts=[node.get('text','') for node in tree.iter('node')]
        if 'Exit' not in texts or 'Later' in texts or 'Connect' in texts:raise RuntimeError('Mandatory update did not block VPN access')
        screenshot('required-update')
        adb('shell','input','keyevent','4')
        time.sleep(.5)
        if any(n.get('text','')=='Connect' for n in hierarchy().iter('node')):raise RuntimeError('Back bypassed mandatory update')
        launch()
        time.sleep(1)
        if not any(n.get('text','')=='Exit' for n in hierarchy().iter('node')):raise RuntimeError('Required update did not survive reopening')
        (output/'required-update.txt').write_text('PASS: cached required version blocks Connect, has no Later, Back exits and reopening preserves the gate.\n')
        adb('shell','am','force-stop',package)
        clear_gate=subprocess.run(['adb','shell',f'run-as {package} sh -c "cat > shared_prefs/interface.xml"'],input=b'<?xml version="1.0" encoding="utf-8"?><map><boolean name="account_intro_seen" value="true" /></map>',capture_output=True,timeout=20)
        if clear_gate.returncode:raise RuntimeError('Could not clear the synthetic required version')
        adb('shell',f'run-as {package} sh -c "echo preserved > files/update-probe.txt"')
        adb('shell','am','force-stop',package)
        transfer=subprocess.run(['adb','shell',f'run-as {package} sh -c "cat > cache/qa-upgrade.apk"'],input=Path(options.upgrade).read_bytes(),capture_output=True,timeout=60)
        if transfer.returncode:raise RuntimeError('Could not stage the synthetic update')
        adb('shell','appops','set',package,'REQUEST_INSTALL_PACKAGES','allow')
        adb('shell','am','start','-W','-f','0x20000000','-n',package+'/.MainActivity','--ez','qa_screenshots','true','--ez','qa_update','true')
        time.sleep(2)
        # Verification hands directly to PackageInstaller; no second in-app install tap.
        confirmed=False
        for attempt in range(35):
            for node in hierarchy().iter('node'):
                if node.get('class')=='android.widget.Button' and node.get('text','').casefold() in ('update','install'):
                    screenshot('update-confirmation')
                    x1,y1,x2,y2=map(int,re.findall(r'\d+',node.get('bounds','')))
                    adb('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2));confirmed=True;break
            if confirmed:break
            time.sleep(.5)
        if not confirmed:raise RuntimeError('Android did not present user confirmation for the in-app update')
        for attempt in range(90):
            details=adb('shell','dumpsys','package',package).decode(errors='replace')
            if 'versionCode=23004 ' in details:break
            time.sleep(1)
        else:raise RuntimeError('The in-app PackageInstaller update did not complete')
        (output/'in-app-update.txt').write_text('PASS: APK staged in private cache, same-package/signature verification, PackageInstaller session, Android user confirmation, higher-version installation. No production signing key in CI.\n')
        launch()
        if adb('exec-out','run-as',package,'cat','files/update-probe.txt').strip()!=b'preserved':
            raise RuntimeError('In-place update lost app-private data')
        for attempt in range(40):
            try:
                new_config=json.loads(adb('exec-out','run-as',package,'cat','files/engine/config.json'))
                new_port=int(new_config['dashboard']['listen'].rsplit(':',1)[1])
                if new_port!=port:
                    adb('forward',f'tcp:{forwarded}',f'tcp:{new_port}')
                    token=adb('exec-out','run-as',package,'cat','files/engine/local-api.token').strip()
                    request('/api/status')
                    break
            except (OSError,ValueError):
                pass
            time.sleep(.5)
        else:
            raise RuntimeError('Updated application did not restart its native API')
        if request('/api/accounts').get('accounts'):
            raise RuntimeError('Update introduced an unexpected account')
    (output/'checks.txt').write_text('PASS: fresh native startup; authenticated API; missing-token rejection; empty accounts; disconnected state; two-step sign-in form; privacy page; rotation; synthetic native TUN DNS; in-app PackageInstaller higher-version update. No real Mozilla account/upstream test.\n')
    print('PASS Android native launch, local authenticated API, empty accounts, disconnected state, unauthenticated API rejection')
    adb('shell', 'am', 'force-stop', package)
finally:
    (output/'crashes.txt').write_bytes(adb('logcat','-b','crash','-d',required=False))
    (output/'navigation.txt').write_bytes(adb('logcat','-d','-s','ActivityTaskManager:I','ActivityManager:I','AndroidRuntime:E','libc:F','*:S',required=False)[-100000:])
    adb('forward', '--remove', f'tcp:{forwarded}', required=False)
