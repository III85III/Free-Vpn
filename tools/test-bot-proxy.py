"""Local-only BAT experiment; not a bot launcher or a VPN isolation test."""

import http.server
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import unittest


CLIENT = r'''
import socket
import sys
import urllib.parse
import urllib.request
from pathlib import Path

mode, target, output = sys.argv[1:]
url = urllib.parse.urlsplit(target)
if url.scheme != 'http' or url.hostname != '127.0.0.1':
    raise RuntimeError('Only a generated loopback fixture is allowed')
if mode == 'raw':
    with socket.create_connection((url.hostname, url.port), timeout=3) as conn:
        conn.sendall(('GET /probe HTTP/1.1\r\nHost: ' + url.netloc + '\r\nConnection: close\r\n\r\n').encode('ascii'))
        parts = []
        while True:
            data = conn.recv(4096)
            if not data:
                break
            parts.append(data)
    result = b''.join(parts).split(b'\r\n\r\n', 1)[1].decode('ascii')
else:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({})) if mode == 'direct' else urllib.request.build_opener()
    with opener.open(target, timeout=3) as response:
        result = response.read().decode('ascii')
Path(output).write_text(result, encoding='ascii')
'''


class FixtureServer:
    def __init__(self, response):
        self.requests = []
        self.fail = False
        owner = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                owner.requests.append(self.path)
                body = response.encode('ascii')
                self.send_response(503 if owner.fail else 200)
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *_):
                pass

        self.server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    @property
    def url(self):
        return 'http://127.0.0.1:' + str(self.server.server_port)

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)


@unittest.skipUnless(os.name == 'nt', 'Tests the Windows BAT launch path')
class BatchProxyBehavior(unittest.TestCase):
    def setUp(self):
        self.parent_proxy_env = {k: v for k, v in os.environ.items() if k.lower().endswith('_proxy')}
        self.proxy = FixtureServer('proxy-fixture')
        self.direct = FixtureServer('direct-fixture')
        build = Path(__file__).resolve().parent.parent / '.build' / 'bot-proxy-tests'
        build.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix='batch fixture ', dir=build)
        self.folder = Path(self.temp.name)
        self.client = self.folder / 'fixture client.py'
        self.client.write_text(CLIENT, encoding='utf-8')
        self.batch = self.folder / 'fixture launch.bat'
        self.batch.write_text('@echo off\n"' + sys.executable + '" -I -S "' + str(self.client) + '" %*\n', encoding='utf-8')

    def tearDown(self):
        self.proxy.close()
        self.direct.close()
        self.temp.cleanup()
        after = {k: v for k, v in os.environ.items() if k.lower().endswith('_proxy')}
        self.assertTrue(self.parent_proxy_env == after, 'The parent proxy environment changed')

    def launch(self, mode):
        env = {k: v for k, v in os.environ.items() if not k.lower().endswith('_proxy')}
        for name in ('HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy', 'ALL_PROXY', 'all_proxy'):
            env[name] = self.proxy.url
        env['NO_PROXY'] = env['no_proxy'] = ''
        result_file = self.folder / (mode + '.txt')
        cmd = Path(os.environ.get('SystemRoot', r'C:\Windows')) / 'System32' / 'cmd.exe'
        target = self.direct.url + '/probe'
        command = '"' + str(cmd) + '" /d /s /c ""' + str(self.batch) + '" ' + mode + ' "' + target + '" "' + str(result_file) + '""'
        process = subprocess.run(command, env=env, cwd=self.folder, capture_output=True, text=True, timeout=8)
        return process, result_file.read_text(encoding='ascii') if result_file.exists() else None

    def test_proxy_aware_batch_uses_the_selected_proxy(self):
        process, result = self.launch('aware')
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'proxy-fixture')
        self.assertEqual(self.proxy.requests, [self.direct.url + '/probe'])
        self.assertEqual(self.direct.requests, [])

    def test_raw_socket_batch_can_bypass_the_same_proxy_environment(self):
        process, result = self.launch('raw')
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'direct-fixture')
        self.assertEqual(self.proxy.requests, [])
        self.assertEqual(self.direct.requests, ['/probe'])

    def test_explicit_direct_control_reaches_the_same_destination_directly(self):
        process, result = self.launch('direct')
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'direct-fixture')
        self.assertEqual(self.proxy.requests, [])
        self.assertEqual(self.direct.requests, ['/probe'])

    def test_proxy_aware_client_does_not_silently_fall_back_after_proxy_error(self):
        self.proxy.fail = True
        process, result = self.launch('aware')
        self.assertNotEqual(process.returncode, 0)
        self.assertIsNone(result)
        self.assertEqual(len(self.proxy.requests), 1)
        self.assertEqual(self.direct.requests, [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
