#!/usr/bin/env python3
"""Build a portable JERICHOFALL with .NET, FNA and our independent simulation."""
from pathlib import Path
import argparse, hashlib, json, os, shutil, subprocess, zipfile
from bootstrap import ROOT, LOCK, bootstrap, run
from prepare_content import prepare
from release_assets import verify as verify_assets
from release_licenses import audit as audit_licenses, NATIVE_FILES

def licenses(destination, rid):
    folder = destination / 'licenses'; folder.mkdir()
    source = ROOT / '.tools/fna-source'
    for relative in ['licenses','lib/FAudio','lib/FNA3D','lib/FNA3D/MojoShader','lib/Theorafile/licenses',
                     'lib/SDL2-CS','lib/SDL3-CS','lib/dav1dfile']:
        path = source / relative
        for item in path.iterdir():
            if item.is_file() and ('license' in item.name.lower()):
                shutil.copy2(item, folder / (relative.replace('/', '-') + '-' + item.name))
    shutil.copy2(ROOT / 'docs/third-party/SDL3-LICENSE.txt', folder)
    shutil.copy2(ROOT / 'licenses/CC-BY-4.0.txt', folder)
    package = Path(os.environ['NUGET_PACKAGES']) / ('microsoft.netcore.app.runtime.' + rid)
    runtime = json.loads((destination / 'Jerichofall.runtimeconfig.json').read_text())['runtimeOptions']
    version = next(f['version'] for f in runtime['includedFrameworks'] if f['name']=='Microsoft.NETCore.App')
    runtime_package = package / version
    if not runtime_package.is_dir(): raise RuntimeError('Missing matching .NET runtime licenses: ' + version)
    for name in ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT']:
        shutil.copy2(runtime_package / name, folder / ('DOTNET-' + name))

def build(sdk, rid, destination, managed):
    destination = destination.resolve()
    if destination.exists():
        if not (destination / 'Jerichofall.runtimeconfig.json').exists():
            raise SystemExit('Refusing to replace an unrelated output directory: ' + str(destination))
        shutil.rmtree(destination)
    run(sdk, 'publish', ROOT / 'src/Jerichofall/Jerichofall.csproj', '-c', 'Release', '-r', rid,
        '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false',
        '-p:DebugType=None', '-o', destination, '--nologo')
    # Both OS packages use precisely the same managed simulation and presentation.
    for name in ['Jerichofall.dll','Jerichofall.Core.dll','FNA.dll']:
        shutil.copy2(managed / name, destination / name)
    shutil.copy2(ROOT / '.tools/fna-build/FNA.dll.config', destination)
    native = ROOT / '.tools/fna-native'
    for name in NATIVE_FILES[rid]:
        target = destination / name; target.parent.mkdir(parents=True, exist_ok=True)
        source = native / name if rid == 'linux-x64' else native / 'x64' / name
        shutil.copy2(source, target)
    prepare(destination / 'Content')
    verify_assets(destination / 'Content')
    licenses(destination, rid)
    shutil.copy2(ROOT / 'docs/runtime-lock.json', destination)
    shutil.copy2(ROOT / 'NOTICE.md', destination)
    for name in ['LICENSE', 'ASSET_LICENSE.md']:
        shutil.copy2(ROOT / name, destination)
    for name in ['LICENSING.md', 'release-assets.json']:
        shutil.copy2(ROOT / 'docs' / name, destination / name)
    audit_licenses(destination, rid)
    (destination / 'README.txt').write_text('JERICHOFALL - independent engine alpha\n\n' +
        'Linux: bash play.sh\nWindows: double-click Play.cmd\nNo other game or .NET installation is needed.\n\n' +
        'Campaign: Enter to choose heroes, arrows to select checkpoints, Enter to begin.\n' +
        'P1: arrows, C jump, hold/release X or Z to fire, Left Shift dodge, V cycle.\n' +
        'P2: WASD, Space jump, hold/release J to fire, K dodge, L cycle.\n' +
        'Gamepad: A jump, X fire, B dodge, Y cycle. Esc/Start pause. F11 fullscreen.\n' +
        'Online Versus: host TCP 28060, select 2-4 players, join the host address.\n' +
        'Use the same release on every machine. There is no relay or matchmaking.\n\n' +
        'Independent simulation with movement, firing, stomp and spirit regression tests.\n' +
        'Human balance, controllers and Internet routing require playtesting.\n' +
        'Free open-source alpha: MIT code; listed assets CC BY 4.0 to the extent rights are held.\n' +
        'Created with AI assistance. See LICENSE, ASSET_LICENSE.md and LICENSING.md.\n' +
        'Third-party licenses are in licenses/. FNA remains under Ms-PL.\n' +
        'Online is unauthenticated and unencrypted; host only for people you trust.\n')
    if rid == 'linux-x64':
        launcher = destination / 'play.sh'
        launcher.write_text('#!/usr/bin/env bash\nset -euo pipefail\n'+
            'game_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)\n'+
            'export LD_LIBRARY_PATH="$game_root/lib64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"\n'+
            'exec "$game_root/Jerichofall" "$@"\n')
        launcher.chmod(0o755); (destination / 'Jerichofall').chmod(0o755)
    else:
        (destination / 'Play.cmd').write_bytes(b'@echo off\r\ncd /d "%~dp0"\r\nJerichofall.exe %*\r\nif errorlevel 1 pause\r\n')
    for pdb in destination.rglob('*.pdb'): pdb.unlink()
    run(sdk, 'run', '--project', ROOT / 'tests/Jerichofall.Checks', '-c', 'Release', '--', '--audit', destination)
    run('python' if os.name == 'nt' else 'python3', ROOT / 'tools/standalone/audit.py', destination)
    manifest = dict(schema=1, platform=rid, engine='Jerichofall.Core',
        source_commit=source_revision(),
        fna_commit=LOCK['commit'], native_archive_sha256=LOCK['archive_sha256'],
        files=[dict(path=p.relative_to(destination).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest())
               for p in sorted(destination.rglob('*')) if p.is_file()])
    (destination / 'release-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    return destination

def source_revision():
    recorded = ROOT / 'SOURCE_REVISION'
    if recorded.is_file(): return recorded.read_text().strip()
    return subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', choices=['linux-x64','win-x64','all'], default='win-x64' if os.name=='nt' else 'linux-x64')
    parser.add_argument('--output', type=Path, default=ROOT / 'build/standalone')
    parser.add_argument('--package', help='Archive version, e.g. 0.2.0-alpha.1; builds under build/standalone-releases')
    args=parser.parse_args()
    if args.package and not (ROOT/'SOURCE_REVISION').exists():
        if subprocess.check_output(['git','status','--porcelain','--untracked-files=normal'],cwd=ROOT,text=True).strip():
            parser.error('Commit reviewed source changes before packaging a versioned release.')
    sdk=bootstrap()
    managed=ROOT / 'build/standalone-managed'
    run(sdk,'build',ROOT / 'src/Jerichofall/Jerichofall.csproj','-c','Release','-p:DebugType=None','-o',managed,'--nologo')
    rids=['linux-x64','win-x64'] if args.rid=='all' else [args.rid]
    if len(rids)>1 and not args.package: parser.error('--rid all requires --package VERSION')
    archives=[]
    for rid in rids:
        destination=(ROOT / 'build/standalone-releases' / ('JERICHOFALL-'+args.package+'-'+rid)) if args.package else args.output
        build(sdk,rid,destination,managed)
        if args.package:
            archive=destination.with_name(destination.name+'.zip')
            with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as package:
                for path in sorted(destination.rglob('*')):
                    if path.is_file(): package.write(path,path.relative_to(destination.parent))
            archives.append(archive)
    if archives:
        sums=archives[0].parent / 'SHA256SUMS'
        sums.write_text(''.join(hashlib.sha256(a.read_bytes()).hexdigest()+'  '+a.name+'\n' for a in archives))
        print('Archives: '+', '.join(str(a) for a in archives))

if __name__=='__main__': main()
