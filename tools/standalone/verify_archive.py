#!/usr/bin/env python3
"""Verify and extract a portable archive into a new directory for native smoke tests."""
from pathlib import Path, PurePosixPath
import hashlib, json, sys, zipfile

archive=Path(sys.argv[1]).resolve(); destination=Path(sys.argv[2]).resolve()
if destination.exists(): raise SystemExit('Verification destination must be new: '+str(destination))
with zipfile.ZipFile(archive) as package:
    names=package.namelist()
    if len(names)!=len(set(names)): raise SystemExit('Duplicate archive members')
    roots={PurePosixPath(n).parts[0] for n in names}
    if len(roots)!=1: raise SystemExit('Archive needs one game folder')
    prefix=next(iter(roots))+'/'
    for name in names:
        path=PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts or '\\' in name: raise SystemExit('Unsafe archive path')
    manifest=json.loads(package.read(prefix+'release-manifest.json'))
    expected={record['path'] for record in manifest['files']}|{'release-manifest.json'}
    if expected!={n[len(prefix):] for n in names}: raise SystemExit('Archive file list differs from its manifest')
    for record in manifest['files']:
        if hashlib.sha256(package.read(prefix+record['path'])).hexdigest()!=record['sha256']:
            raise SystemExit('Changed file: '+record['path'])
    for name in names:
        output=destination/name[len(prefix):]
        output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(package.read(name))
    if manifest['platform']=='linux-x64':
        (destination/'Jerichofall').chmod(0o755);(destination/'play.sh').chmod(0o755)
print(json.dumps(dict(passed=True,archive=archive.name,files=len(expected),source=manifest['source_commit'])))
