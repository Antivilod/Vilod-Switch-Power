"""Audit a tracked source snapshot without local Git configuration or build outputs."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parent.parent
with tempfile.TemporaryDirectory(prefix='vsp-source-audit-') as directory:
    snapshot = Path(directory)
    tracked = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z'])
    for name in tracked.decode('utf-8').split('\0'):
        if not name:
            continue
        source = root / name
        if source.is_symlink() or not source.is_file():
            raise SystemExit('Tracked files must be regular files.')
        target = snapshot / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    result = subprocess.run([sys.executable, str(root / 'scripts/audit-public.py'), str(snapshot)])
    raise SystemExit(result.returncode)
