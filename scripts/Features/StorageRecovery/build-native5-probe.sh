#!/usr/bin/env bash
set -euo pipefail

readonly prior_revision=2532f781fec8f2546a033c396a7dbce0e9b4b781
readonly repository=$(git rev-parse --show-toplevel)
readonly destination="$repository/artifacts/native5-probe"
readonly driver=tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochPriorSourceProbe.cs
readonly fixture=tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochUpgradeFixture.cs
readonly project=tests/KeyLoad.CrashHost/KeyLoad.CrashHost.csproj

[[ ! -e "$destination" && ! -L "$destination" ]]
git cat-file -e "$prior_revision^{commit}"
readonly temporary=$(mktemp -d "${TMPDIR:-/tmp}/keyload-native5-source.XXXXXX")
trap 'rm -rf -- "$temporary"' EXIT
mkdir "$temporary/source"
git archive --format=tar "$prior_revision" > "$temporary/source.tar"
tar -xf "$temporary/source.tar" -C "$temporary/source"

# Only the probe and its raw-input fixture are overlaid. The prior provider,
# serializer, identity validator, snapshot reader and project inputs stay exact.
cp "$repository/$driver" "$temporary/source/$driver"
cp "$repository/$fixture" "$temporary/source/$fixture"
python3 - "$temporary/source/tests/KeyLoad.CrashHost/Program.cs" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
original = path.read_text()
expected = 'using KeyLoad.CrashHost;\n\nawait CrashHostApplication.RunAsync(args);\n'
if original != expected:
    raise SystemExit('The immutable prior CrashHost entry point differs.')
path.write_text('using KeyLoad.CrashHost;\n\nif (!await EpochPriorSourceProbe.TryRunAsync(args))\n{\n    await CrashHostApplication.RunAsync(args);\n}\n')
PY

dotnet restore "$temporary/source/$project" -p:RestorePackagesWithLockFile=false \
  --disable-build-servers -m:1 -nodeReuse:false
dotnet publish "$temporary/source/$project" --no-restore --configuration Release \
  --disable-build-servers -m:1 -nodeReuse:false --output "$temporary/publish"

python3 - "$repository" "$temporary/source.tar" "$temporary/publish" "$prior_revision" "$driver" "$fixture" <<'PY'
import hashlib
import json
from pathlib import Path
import subprocess
import sys

repository, archive, publish = map(Path, sys.argv[1:4])
revision = sys.argv[4]
def describe(root, relative):
    path = root / relative
    if path.is_symlink() or not path.is_file():
        raise SystemExit('The native5 probe contains a non-regular file.')
    return dict(path=relative, bytes=path.stat().st_size,
                sha256=hashlib.file_digest(path.open('rb'), 'sha256').hexdigest())
files = [describe(publish, path.relative_to(publish).as_posix())
         for path in sorted(publish.rglob('*')) if path.is_file()]
drivers = [describe(repository, path) for path in sys.argv[5:]]
if not any(item['path'] == 'KeyLoad.CrashHost.dll' for item in files):
    raise SystemExit('The actual prior executable is missing.')
receipt = dict(schemaVersion=1, sourceRevision=revision, dataEpoch=5,
               journalVersion=4, checkpointVersion=3,
               sourceTree=subprocess.check_output(['git', 'rev-parse', revision + '^{tree}'],
                                                  cwd=repository, text=True).strip(),
               originalArchiveSha256=hashlib.file_digest(archive.open('rb'), 'sha256').hexdigest(),
               driverSources=drivers, files=files, githubQualified=False,
               producer='immutable source export plus isolated test driver')
(publish / 'native5-probe.json').write_text(json.dumps(receipt, indent=2) + '\n')
PY

mkdir -p "$repository/artifacts"
[[ ! -e "$destination" && ! -L "$destination" ]]
mv "$temporary/publish" "$destination"
