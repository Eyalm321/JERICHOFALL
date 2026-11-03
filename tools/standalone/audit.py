#!/usr/bin/env python3
"""Check package notices and exact asset inventory."""
from pathlib import Path
import hashlib, json, sys
from release_licenses import audit as audit_licenses

ROOT=Path(__file__).resolve().parents[2]

def audit(folder):
    folder=Path(folder).resolve()
    audit_licenses(folder, 'win-x64' if (folder/'Jerichofall.exe').exists() else 'linux-x64')
    count=sum(p.is_file() for p in folder.rglob('*'))
    content=folder/'Content'
    manifest=json.loads((content/'content-manifest.json').read_text())
    expected={'content-manifest.json'}
    for record in manifest['files']:
        name=record['path']; relative=Path(name)
        assert not relative.is_absolute() and '..' not in relative.parts and '\\' not in name
        assert name not in expected, 'Duplicate content file'
        expected.add(name)
        assert hashlib.sha256((content/name).read_bytes()).hexdigest()==record['sha256'], 'Changed content: '+name
    actual={p.relative_to(content).as_posix() for p in content.rglob('*') if p.is_file()}
    assert expected==actual, 'Unexpected or missing content: '+str(expected^actual)
    report=dict(passed=True,files=count,authoredContent=len(expected)-1,scope='Identity and content closure; assembly references checked separately. Not legal clearance.')
    (folder/'content-audit.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))

if __name__=='__main__': audit(sys.argv[1])
