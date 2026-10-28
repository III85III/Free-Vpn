"""Local-only BAT experiment; not a bot launcher or a VPN isolation test."""

import http.server
import os
from pathlib import Path
import select
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import threading
import unittest


CLIENT = r'''
import socket
import ssl
import sys
import urllib.parse
import urllib.request
from pathlib import Path

mode, target, output, certificate = sys.argv[1:]
url = urllib.parse.urlsplit(target)
if url.scheme not in ('http', 'https') or url.hostname != '127.0.0.1':
    raise RuntimeError('Only a generated loopback fixture is allowed')
context = ssl.create_default_context(cafile=certificate) if certificate != '-' else None
if mode == 'raw':
    connection = socket.create_connection((url.hostname, url.port), timeout=3)
    if url.scheme == 'https':
        connection = context.wrap_socket(connection, server_hostname=url.hostname)
    with connection as conn:
        conn.sendall(('GET /probe HTTP/1.1\r\nHost: ' + url.netloc + '\r\nConnection: close\r\n\r\n').encode('ascii'))
        parts = []
        while True:
            data = conn.recv(4096)
            if not data:
                break
            parts.append(data)
    result = b''.join(parts).split(b'\r\n\r\n', 1)[1].decode('ascii')
else:
    handlers = [urllib.request.HTTPSHandler(context=context)]
    if mode == 'direct':
        handlers.append(urllib.request.ProxyHandler({}))
    opener = urllib.request.build_opener(*handlers)
    with opener.open(target, timeout=3) as response:
        result = response.read().decode('ascii')
Path(output).write_text(result, encoding='ascii')
'''


class FixtureServer:
    def __init__(self, response, tls_context=None):
        self.requests = []
        self.connects = []
        self.allowed_connect = None
        self.fail = False
        owner = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_CONNECT(self):
                owner.connects.append(self.path)
                if owner.fail or self.path != owner.allowed_connect:
                    self.send_error(503)
                    return
                host, port = self.path.rsplit(':', 1)
                # This fixture is never an arbitrary destination relay.
                if host != '127.0.0.1':
                    self.send_error(403)
                    return
                with socket.create_connection((host, int(port)), timeout=3) as upstream:
                    self.send_response(200, 'Connection established')
                    self.end_headers()
                    peers = (self.connection, upstream)
                    while True:
                        ready, _, _ = select.select(peers, [], [], 4)
                        if not ready:
                            return
                        for source in ready:
                            data = source.recv(65536)
                            if not data:
                                return
                            (upstream if source is self.connection else self.connection).sendall(data)

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
        self.scheme = 'https' if tls_context else 'http'
        if tls_context:
            self.server.socket = tls_context.wrap_socket(self.server.socket, server_side=True)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    @property
    def url(self):
        return self.scheme + '://127.0.0.1:' + str(self.server.server_port)

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
        self.tls = None
        build = Path(__file__).resolve().parent.parent / '.build' / 'bot-proxy-tests'
        build.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix='batch fixture ', dir=build)
        self.folder = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.addCleanup(self.proxy.close)
        self.addCleanup(self.direct.close)
        self.client = self.folder / 'fixture client.py'
        self.client.write_text(CLIENT, encoding='utf-8')
        self.batch = self.folder / 'fixture launch.bat'
        self.batch.write_text('@echo off\n"' + sys.executable + '" -I -S "' + str(self.client) + '" %*\n', encoding='utf-8')

    def tearDown(self):
        if self.tls:
            self.tls.close()
        after = {k: v for k, v in os.environ.items() if k.lower().endswith('_proxy')}
        self.assertTrue(self.parent_proxy_env == after, 'The parent proxy environment changed')

    def prepare_https(self):
        openssl = shutil.which('openssl')
        if not openssl:
            candidate = Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'Git/usr/bin/openssl.exe'
            openssl = str(candidate) if candidate.is_file() else None
        if not openssl:
            raise RuntimeError('OpenSSL is required to generate disposable TLS test certificates')
        cert, key = self.folder / 'fixture.pem', self.folder / 'fixture.key'
        subprocess.run([openssl, 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                        '-subj', '/CN=127.0.0.1', '-addext', 'subjectAltName=IP:127.0.0.1',
                        '-keyout', str(key), '-out', str(cert)], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(cert, key)
        self.tls = FixtureServer('tls-destination-fixture', context)
        self.proxy.allowed_connect = '127.0.0.1:' + str(self.tls.server.server_port)
        return str(cert)

    def launch(self, mode, https=False):
        env = {k: v for k, v in os.environ.items() if not k.lower().endswith('_proxy')}
        for name in ('HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy', 'ALL_PROXY', 'all_proxy'):
            env[name] = self.proxy.url
        env['NO_PROXY'] = env['no_proxy'] = ''
        result_file = self.folder / (mode + '.txt')
        cmd = Path(os.environ.get('SystemRoot', r'C:\Windows')) / 'System32' / 'cmd.exe'
        certificate = self.prepare_https() if https else '-'
        target = (self.tls.url if https else self.direct.url) + '/probe'
        command = '"' + str(cmd) + '" /d /s /c ""' + str(self.batch) + '" ' + mode + ' "' + target + '" "' + str(result_file) + '" "' + certificate + '""'
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

    def test_https_uses_connect_with_verified_destination_certificate(self):
        process, result = self.launch('aware', https=True)
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'tls-destination-fixture')
        self.assertEqual(self.proxy.connects, [self.proxy.allowed_connect])
        self.assertEqual(self.tls.requests, ['/probe'])
        self.assertEqual(self.proxy.requests, [])

    def test_https_raw_client_bypasses_the_same_environment(self):
        process, result = self.launch('raw', https=True)
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'tls-destination-fixture')
        self.assertEqual(self.proxy.connects, [])
        self.assertEqual(self.tls.requests, ['/probe'])

    def test_https_explicit_direct_control_avoids_proxy(self):
        process, result = self.launch('direct', https=True)
        self.assertEqual(process.returncode, 0, process.stderr[:300])
        self.assertEqual(result, 'tls-destination-fixture')
        self.assertEqual(self.proxy.connects, [])
        self.assertEqual(self.tls.requests, ['/probe'])

    def test_failed_connect_does_not_fall_back_to_direct_tls(self):
        self.proxy.fail = True
        process, result = self.launch('aware', https=True)
        self.assertNotEqual(process.returncode, 0)
        self.assertIsNone(result)
        self.assertEqual(self.proxy.connects, [self.proxy.allowed_connect])
        self.assertEqual(self.tls.requests, [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
