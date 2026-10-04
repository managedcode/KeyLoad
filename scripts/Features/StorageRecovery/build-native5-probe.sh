#!/usr/bin/env bash
set -euo pipefail
readonly repository=$(git rev-parse --show-toplevel)
exec bash "$repository/scripts/Features/StorageRecovery/build-prior-probe.sh" \
  --epoch=5 --destination="$repository/artifacts/native5-probe"
