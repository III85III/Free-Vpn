"""Run the app's isolated empty-profile check on a macOS build machine."""
import pathlib
import subprocess
import sys

app = pathlib.Path(sys.argv[1]).resolve()
result = subprocess.run([str(app / 'Contents/MacOS/III85III VPN'), '--smoke-test'],
                        capture_output=True, text=True, timeout=100)
passed = result.returncode == 0 and 'PASS: macOS app opens' in result.stdout
if not passed:
    # Never print arbitrary engine logs, account data, or local API tokens.
    print(f'FAIL: macOS empty-profile check exited {result.returncode}.')
    raise SystemExit(1)
print(result.stdout.strip())
