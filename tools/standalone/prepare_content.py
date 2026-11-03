#!/usr/bin/env python3
"""Copy the game's editable asset tree and record its exact identities."""
from pathlib import Path
import argparse, hashlib, json
ROOT = Path(__file__).resolve().parents[2]

def prepare(destination):
    destination = Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    records = []
    for source in sorted((ROOT / 'assets').rglob('*')):
        if not source.is_file(): continue
        if source.is_symlink(): raise ValueError('Asset symlinks are not allowed')
        relative = source.relative_to(ROOT / 'assets').as_posix()
        data = source.read_bytes()
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        records.append(dict(path=relative, sha256=hashlib.sha256(data).hexdigest(), source='assets/' + relative))
    (destination / 'content-manifest.json').write_text(json.dumps(dict(schema='jerichofall-independent-content-v1',files=records),indent=2)+'\n')
    print(f'Prepared {len(records)} content files at {destination}')

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('destination',type=Path)
    prepare(parser.parse_args().destination)
