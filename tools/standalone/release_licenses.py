"""Require license notices for the actual packaged native and managed dependencies."""
from pathlib import Path
import hashlib, json

NOTICE_FILES = [
    'licenses-LICENSE', 'licenses-lzxdecoder.LICENSE', 'licenses-monoxna.LICENSE',
    'lib-FAudio-LICENSE', 'lib-FNA3D-LICENSE', 'lib-FNA3D-MojoShader-LICENSE.txt',
    'lib-Theorafile-licenses-LICENSE', 'lib-Theorafile-licenses-libogg.LICENSE',
    'lib-Theorafile-licenses-libtheora.LICENSE', 'lib-Theorafile-licenses-libvorbis.LICENSE',
    'lib-SDL2-CS-LICENSE', 'lib-SDL3-CS-LICENSE', 'lib-dav1dfile-LICENSE',
    'SDL3-LICENSE.txt', 'DOTNET-LICENSE.TXT', 'DOTNET-THIRD-PARTY-NOTICES.TXT',
    'CC-BY-4.0.txt',
]
NATIVE_FILES = {
    'linux-x64': ['lib64/libFAudio.so.0', 'lib64/libFNA3D.so.0', 'lib64/libSDL3.so.0', 'lib64/libtheorafile.so'],
    'win-x64': ['FAudio.dll', 'FNA3D.dll', 'SDL3.dll', 'libtheorafile.dll'],
}


def audit(folder, rid):
    folder = Path(folder)
    required = ['LICENSE', 'ASSET_LICENSE.md', 'NOTICE.md', 'LICENSING.md',
                'release-assets.json'] + ['licenses/' + name for name in NOTICE_FILES]
    required += NATIVE_FILES[rid]
    for name in required:
        path = folder / name
        if not path.is_file() or path.stat().st_size < 40:
            raise ValueError('Missing or empty release license/input: ' + name)
    # The native archive also contains a proprietary Agility SDK; never ship it
    # or an unreviewed new native dependency through a recursive copy.
    if (folder / 'D3D12').exists():
        raise ValueError('Unexpected Agility SDK in release')
    if rid == 'linux-x64':
        actual = {p.relative_to(folder).as_posix() for p in (folder / 'lib64').rglob('*') if p.is_file()}
        if actual != set(NATIVE_FILES[rid]):
            raise ValueError('Unexpected native library set')
    asset_inventory = json.loads((folder / 'release-assets.json').read_text())
    content = json.loads((folder / 'Content/content-manifest.json').read_text())
    if asset_inventory['packaged_files'] != content['files']:
        raise ValueError('Packaged assets differ from licensed inventory')
    result = dict(schema=1, platform=rid, passed=True,
                  scope='Required notices and inventory closure only; not legal clearance.',
                  notices=[dict(path=name, sha256=hashlib.sha256((folder / name).read_bytes()).hexdigest())
                           for name in required if name not in NATIVE_FILES[rid]],
                  native_files=NATIVE_FILES[rid])
    (folder / 'license-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    return result
