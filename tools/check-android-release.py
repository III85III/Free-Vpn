"""Inspect release contents without extracting account data or private material."""
import argparse
import struct
import zipfile
from pathlib import PurePosixPath

parser = argparse.ArgumentParser()
parser.add_argument('apk')
args = parser.parse_args()
with zipfile.ZipFile(args.apk) as archive:
    names = archive.namelist()
    forbidden = {'accounts.json', 'local-api.token', 'password.dpapi', 'config.json'}
    for name in names:
        path = PurePosixPath(name)
        if path.name in forbidden or path.suffix.lower() in {'.p12', '.jks', '.keystore', '.log', '.bat'}:
            raise SystemExit('Unexpected private or development file in release')
    assert 'assets/privacy.txt' in names, 'Missing privacy document'
    assert 'assets/fonts/OFL.txt' in names, 'Missing font notice'
    assert any(n.startswith('assets/licenses/') for n in names), 'Missing dependency notices'
    native = [n for n in names if n.startswith('lib/') and n.endswith('.so')]
    assert {n.split('/')[1] for n in native} == {'arm64-v8a', 'x86_64'}, 'Unexpected native architectures'
    for name in native:
        elf = archive.read(name)
        assert elf[:6] == b'\x7fELF\x02\x01', 'Expected little-endian ELF64'
        offset = struct.unpack_from('<Q', elf, 32)[0]
        size, count = struct.unpack_from('<HH', elf, 54)
        for index in range(count):
            kind, flags, file_offset, virtual, physical, filesz, memsz, alignment = struct.unpack_from('<IIQQQQQQ', elf, offset+index*size)
            if kind == 1:
                assert alignment >= 16384 and (virtual-file_offset) % 16384 == 0, 'Native load segment is not 16 KB aligned'
            if kind == 0x6474e552:
                assert (virtual+memsz) % 16384 == 0, 'Native RELRO endpoint is not 16 KB aligned'
    assert b'qa_screenshots' not in archive.read('classes.dex'), 'Debug screenshot override present in release'
    assert b'qa_tun' not in archive.read('classes.dex'), 'Debug tunnel test entry point present in release'
print('PASS: expected architectures; 16 KB native load/RELRO alignment; privacy and license assets; no known private files; no debug screenshot override.')
