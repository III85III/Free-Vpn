"""Collect notices from resolved module directories; never copy source profiles."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--module', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
args.output.mkdir(parents=True, exist_ok=True)
def go(*arguments):
    return subprocess.check_output(['go', *arguments], cwd=args.module, text=True, encoding='utf-8')

modules = {}
for line in go('list', '-deps', '-f', '{{if .Module}}{{.Module.Path}}|{{.Module.Version}}|{{.Module.Dir}}{{end}}', '.').splitlines():
    if '|' not in line:
        continue
    name, version, directory = line.split('|', 2)
    if name.startswith('github.com/III85III/'):
        continue
    modules[name] = (version, Path(directory))
binding = json.loads(go('list', '-m', '-json', 'golang.org/x/mobile'))
modules[binding['Path']] = (binding['Version'], Path(binding['Dir']))
inventory = []
for name, (version, directory) in sorted(modules.items()):
    notices = [p for p in directory.iterdir() if p.is_file() and any(p.name.upper().split('.')[0] == n for n in ('LICENSE', 'COPYING', 'NOTICE', 'PATENTS', 'AUTHORS'))]
    if not notices:
        raise RuntimeError('Dependency notice not found: ' + name)
    destination = args.output / (name.replace('/', '_') + '@' + version)
    destination.mkdir(exist_ok=True)
    for notice in notices:
        shutil.copyfile(notice, destination / notice.name)
    inventory.append({'module': name, 'version': version})
shutil.copyfile(root / 'licenses/GPL-3.0.txt', args.output / 'III85III-GPL-3.0.txt')
shutil.copyfile(Path(go('env', 'GOROOT').strip()) / 'LICENSE', args.output / 'Go-LICENSE.txt')
(args.output / 'modules.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
print('Collected notices for', len(inventory), 'resolved dependencies; no local paths recorded.')
