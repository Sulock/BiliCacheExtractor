#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
set -euo pipefail
cd "$(dirname "$0")"
recipe_root='FFmpeg-Builds-autobuild-2026-10-27-18-38'
if [[ ! -d "$recipe_root" ]]; then
    unzip ffmpeg-builds-20261027.zip
fi
cd "$recipe_root"
python3 - <<'PY'
from pathlib import Path
source = Path('build.sh').read_text()
needle = '    cd ffmpeg\n'
if source.count(needle) != 1:
    raise SystemExit('Unexpected upstream build script; refusing to change the source checkout.')
source = source.replace(needle, needle +
    '    git fetch origin 215351c1c37dc0345a6794f4c96af54cf30e1c7b\n'
    '    git checkout --detach 215351c1c37dc0345a6794f4c96af54cf30e1c7b\n')
Path('build-pinned.generated.sh').write_text(source)
PY
./makeimage.sh win64 gpl-shared
bash ./build-pinned.generated.sh win64 gpl-shared
