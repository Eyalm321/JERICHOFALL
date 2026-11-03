#!/usr/bin/env python3
"""Export an allowlisted independent source distribution, without private history."""
from pathlib import Path
import argparse, hashlib, json, re, subprocess, sys, zipfile
from prepare_content import ROOT
sys.path.insert(0, str(ROOT / 'tools/repository'))
from check_contents import inspect_path, inspect_payload



def entries():
    tracked = set(subprocess.check_output(['git','ls-files'],cwd=ROOT,text=True).splitlines())
    selected = tracked
    if any(not (ROOT / name).is_file() or (ROOT / name).is_symlink() for name in selected):
        raise ValueError('Source inputs must be regular files')
    result = {name: (ROOT / name).read_bytes() for name in selected}
    revision = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    result['SOURCE_REVISION'] = (revision + '\n').encode()
    excluded: set[str] = set()
    issues = []
    for name, data in result.items():
        issues += inspect_path(name, '100644') + inspect_payload(name, data, excluded)
    if issues: raise ValueError('\n'.join(issues))
    manifest = dict(schema=1, source_commit=revision, scope='JERICHOFALL source distribution.',
                    files=[dict(path=name, sha256=hashlib.sha256(data).hexdigest()) for name,data in sorted(result.items())])
    result['source-manifest.json'] = (json.dumps(manifest,indent=2)+'\n').encode()
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    args=parser.parse_args()
    if not re.fullmatch(r'[0-9][A-Za-z0-9.\-]*', args.version): parser.error('Invalid version')
    if subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=ROOT,text=True).strip():
        raise SystemExit('Commit reviewed source changes before packaging.')
    root='JERICHOFALL-'+args.version+'-source'
    archive=ROOT/'build/standalone-releases'/(root+'.zip'); archive.parent.mkdir(parents=True,exist_ok=True)
    files=entries()
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for name,data in sorted(files.items()):
            info=zipfile.ZipInfo(root+'/'+name, (2026,11,3,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=(0o100755 if name.endswith('.sh') else 0o100644)<<16
            output.writestr(info,data)
    print(f'Source archive: {archive} ({len(files)} files)')


if __name__=='__main__': main()
