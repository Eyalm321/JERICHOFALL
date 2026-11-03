#!/usr/bin/env python3
"""Acquire the pinned open-source renderer. No game installation is an input."""
from pathlib import Path
import hashlib, json, os, shutil, subprocess, urllib.request, zipfile

ROOT = Path(__file__).resolve().parents[2]
LOCK = json.loads((ROOT / 'docs/runtime-lock.json').read_text())

def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)

def dotnet():
    local = ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
    found = str(local) if local.exists() else shutil.which('dotnet')
    if not found: raise SystemExit('Install the .NET 10 SDK, then run this command again.')
    os.environ['DOTNET_ROOT'] = str(Path(found).resolve().parent)
    os.environ.setdefault('DOTNET_CLI_HOME', str(ROOT / '.tools/dotnet-home'))
    os.environ.setdefault('NUGET_PACKAGES', str(ROOT / '.tools/nuget-packages'))
    os.environ['DOTNET_NOLOGO'] = '1'
    os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    return found

def bootstrap():
    sdk = dotnet()
    version = subprocess.check_output([sdk, '--version'], text=True).strip()
    if not version.startswith('10.'): raise SystemExit('The build requires .NET SDK 10; found ' + version)
    source = ROOT / '.tools/fna-source'
    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        run('git', 'clone', LOCK['fna_repository'], source)
        run('git', '-C', source, 'checkout', LOCK['commit'])
    head = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
    if head != LOCK['commit']: raise SystemExit('FNA checkout does not match docs/runtime-lock.json; preserve it and use a fresh checkout.')
    run('git', '-C', source, 'submodule', 'update', '--init', '--recursive')
    project = source / 'FNA.Core.csproj'
    text = project.read_text()
    if '<TargetFramework>net8.0</TargetFramework>' in text:
        project.write_text(text.replace('<TargetFramework>net8.0</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>'))
    elif '<TargetFramework>net10.0</TargetFramework>' not in text: raise SystemExit('Unexpected FNA project target.')
    changed = subprocess.check_output(['git','-C',str(source),'diff','HEAD','--name-only'],text=True).splitlines()
    original = subprocess.check_output(['git','-C',str(source),'show','HEAD:FNA.Core.csproj'],text=True)
    expected = original.replace('<TargetFramework>net8.0</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>')
    if changed != ['FNA.Core.csproj'] or project.read_text() != expected:
        raise SystemExit('Unexpected FNA/source-submodule modifications; only the recorded framework patch is allowed.')
    run(sdk, 'build', project, '-c', 'Release', '-p:DebugType=None', '-p:PathMap=' + str(ROOT) + '=/src/JERICHOFALL', '-o', ROOT / '.tools/fna-build', '--nologo')
    archive = ROOT / '.tools/fna-native.zip'
    if not archive.exists():
        temporary = archive.with_suffix('.part')
        request = urllib.request.Request(LOCK['native_download'], headers={'User-Agent':'Jerichofall-build'})
        with urllib.request.urlopen(request, timeout=120) as response, temporary.open('wb') as output:
            shutil.copyfileobj(response, output)
        temporary.replace(archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != LOCK['archive_sha256']:
        raise SystemExit('Native library archive SHA-256 mismatch. Nothing was extracted.')
    destination = ROOT / '.tools/fna-native'
    with zipfile.ZipFile(archive) as package:
        for item in package.infolist():
            name = Path(item.filename)
            if name.is_absolute() or '..' in name.parts: raise SystemExit('Unsafe native library archive.')
        package.extractall(destination)
    return sdk

if __name__ == '__main__': bootstrap()
