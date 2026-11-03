#!/usr/bin/env python3
"""Freeze the released asset inputs and conversions; this is not legal clearance."""
from pathlib import Path
import argparse, hashlib, json, tempfile
from prepare_content import ROOT, prepare


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory():
    with tempfile.TemporaryDirectory() as temporary:
        prepare(temporary)
        records = json.loads((Path(temporary) / 'content-manifest.json').read_text())['files']
    sources = sorted({r['source'].split('!')[0] for r in records})
    return dict(schema=1, license='CC-BY-4.0',
                credit='Eyal Mizrachi and JERICHOFALL contributors; created with AI assistance',
                scope='Rights held by the licensor only; no claim over facts or uncopyrightable AI output.',
                sources=[dict(path=p, sha256=digest(ROOT / p)) for p in sources],
                packaged_files=records)


def verify(content):
    approved = json.loads((ROOT / 'docs/release-assets.json').read_text())
    actual = json.loads((Path(content) / 'content-manifest.json').read_text())['files']
    if actual != approved['packaged_files']:
        raise ValueError('Content differs from the reviewed asset inventory. Review origins before refreshing it.')
    for record in approved['sources']:
        if digest(ROOT / record['path']) != record['sha256']:
            raise ValueError('Source asset changed: ' + record['path'])
    return approved


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--refresh', action='store_true', help='Record assets after reviewing their provenance')
    args = parser.parse_args()
    result = inventory()
    target = ROOT / 'docs/release-assets.json'
    if args.refresh:
        target.write_text(json.dumps(result, indent=2) + '\n')
    elif result != json.loads(target.read_text()):
        raise SystemExit('Asset inventory differs; review and explicitly refresh it.')
    print(f"Asset inventory: {len(result['sources'])} inputs, {len(result['packaged_files'])} packaged files")
