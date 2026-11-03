# Build and package

Install .NET SDK 10, Python 3 and Git. Linux play needs a graphical session,
OpenGL and glibc 2.31 or later. The supported Windows target is x86-64.

```sh
python3 tools/standalone/build.py
bash tools/play.sh
```

Windows: open `build/standalone/Play.cmd`. The builder downloads pinned FNA
source and native libraries, checks their identities and publishes a portable
game. Required third-party notices are bundled. The native artifact currently
expires upstream on January 31, 2027; a reviewed replacement or durable mirror
will be needed for long-term builds. Unpinned substitutions are rejected.

## Validate

```sh
python3 tools/standalone/prepare_content.py build/check-content
dotnet run --project tests/Jerichofall.Checks -c Release -- build/check-content
python3 -m unittest discover -s tools/repository/tests -v
python3 -m unittest discover -s tools/standalone/tests -v
python3 tools/standalone/release_assets.py
```

After staging, `python3 tools/repository/check_contents.py` checks tracked files
for executable payloads, unsafe paths, archive contents and selected
secret patterns. Changes to asset inputs require provenance review followed by
`python3 tools/standalone/release_assets.py --refresh`.

Linux graphical verification:

```sh
xvfb-run -a python3 tools/standalone/smoke.py build/standalone build/render-checks
```

Windows uses the same smoke command without `xvfb-run`. It runs 900 frames and
captures 15 scenes, including menus, both battle modes and death spirits.

## Release archives

From a clean committed checkout:

```sh
python3 tools/standalone/build.py --rid all --package 0.2.0-alpha.3
python3 tools/standalone/source_package.py --version 0.2.0-alpha.3
```

Outputs go to `build/standalone-releases`. Include all three ZIPs in the final
checksum file. Building does not publish. Source archives contain a revision
record and per-file manifest and can build without Git history. Run both OS
checks on the exact candidate archives before publishing.
