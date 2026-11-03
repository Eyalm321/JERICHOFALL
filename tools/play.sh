#!/usr/bin/env bash
set -euo pipefail
task_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$task_root"
if ! test -x "$task_root/build/standalone/play.sh"; then
    bash "$task_root/tools/build-standalone.sh"
fi
exec bash "$task_root/build/standalone/play.sh" "$@"
