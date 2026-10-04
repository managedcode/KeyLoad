#!/usr/bin/env bash
set -euo pipefail

[[ $# == 2 && "$1" == --epoch=* && "$2" == --destination=* ]]
readonly prior_epoch="${1#--epoch=}"
readonly destination="${2#--destination=}"
case "$prior_epoch" in
  5)
    readonly prior_revision=7784b6b46b98ce994dd98070dc1f58fe4e506b91
    readonly prior_tree=b03bf1301a03b3fe00f419c3c7bf5285a34b63f9
    readonly prior_checkpoint=3
    ;;
  6)
    readonly prior_revision=2801b03091efc5cf45b1268c6570457539f12f27
    readonly prior_tree=678ac682c90294306a0ae4092c4c80b382c4a18b
    readonly prior_checkpoint=4
    ;;
  *) exit 2 ;;
esac
[[ "$destination" == /* && "$destination" != / ]]
readonly repository=$(git rev-parse --show-toplevel)
readonly driver=tests/KeyLoad.CrashHost/Features/StorageRecovery/Helpers/EpochPriorSourceProbe.cs
readonly driver_destination=tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochPriorSourceProbe.cs
readonly fixture=tests/KeyLoad.CrashHost/Features/StorageRecovery/Fixtures/EpochUpgradeFixture.cs
readonly fixture_destination=tests/KeyLoad.CrashHost/Features/StorageRecovery/EpochUpgradeFixture.cs
readonly project=tests/KeyLoad.CrashHost/KeyLoad.CrashHost.csproj

[[ ! -e "$destination" && ! -L "$destination" ]]
git cat-file -e "$prior_revision^{commit}"
[[ "$(git rev-parse "$prior_revision^{tree}")" == "$prior_tree" ]]
readonly temporary=$(mktemp -d "${TMPDIR:-/tmp}/keyload-prior-source.XXXXXX")
trap 'rm -rf -- "$temporary"' EXIT
mkdir "$temporary/source"
git archive --format=tar "$prior_revision" > "$temporary/source.tar"
tar -xf "$temporary/source.tar" -C "$temporary/source"

# Only the probe and its raw-input fixture are overlaid. The prior provider,
# serializer, identity validator, snapshot reader and project inputs stay exact.
cp "$repository/$driver" "$temporary/source/$driver_destination"
cp "$repository/$fixture" "$temporary/source/$fixture_destination"
python3 - "$temporary/source/tests/KeyLoad.CrashHost/Program.cs" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
original = path.read_text()
expected = 'using KeyLoad.CrashHost;\n\nawait CrashHostApplication.RunAsync(args);\n'
if original != expected:
    raise SystemExit('The immutable prior CrashHost entry point differs.')
path.write_text('using KeyLoad.CrashHost;\n\nawait EpochPriorSourceProbe.RunAsync(args);\n')
PY

dotnet restore "$temporary/source/$project" -p:RestorePackagesWithLockFile=false \
  --disable-build-servers -m:1 -nodeReuse:false
dotnet publish "$temporary/source/$project" --no-restore --configuration Release \
  --disable-build-servers -m:1 -nodeReuse:false --output "$temporary/publish"

python3 - "$repository" "$temporary/source.tar" "$temporary/publish" "$prior_revision" "$prior_epoch" "$prior_checkpoint" "$driver" "$fixture" <<'PY'
import hashlib
import json
from pathlib import Path
import subprocess
import sys

repository, archive, publish = map(Path, sys.argv[1:4])
revision = sys.argv[4]
epoch, checkpoint = map(int, sys.argv[5:7])
def describe(root, relative):
    path = root / relative
    if path.is_symlink() or not path.is_file():
        raise SystemExit('The immutable prior probe contains a non-regular file.')
    return dict(path=relative, bytes=path.stat().st_size,
                sha256=hashlib.file_digest(path.open('rb'), 'sha256').hexdigest())
files = [describe(publish, path.relative_to(publish).as_posix())
         for path in sorted(publish.rglob('*')) if path.is_file()]
drivers = [describe(repository, path) for path in sys.argv[7:]]
if not any(item['path'] == 'KeyLoad.CrashHost.dll' for item in files):
    raise SystemExit('The actual prior executable is missing.')
receipt = dict(schemaVersion=1, sourceRevision=revision, dataEpoch=epoch,
               journalVersion=4, checkpointVersion=checkpoint,
               sourceTree=subprocess.check_output(['git', 'rev-parse', revision + '^{tree}'],
                                                  cwd=repository, text=True).strip(),
               originalArchiveSha256=hashlib.file_digest(archive.open('rb'), 'sha256').hexdigest(),
               driverSources=drivers, files=files, githubQualified=False,
               producer='immutable source export plus isolated test driver')
(publish / f'native{epoch}-probe.json').write_text(json.dumps(receipt, indent=2) + '\n')
PY

mkdir -p -- "$(dirname -- "$destination")"
[[ ! -e "$destination" && ! -L "$destination" ]]
mv "$temporary/publish" "$destination"
