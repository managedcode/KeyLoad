#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
umask 077
if [[ -e .env || -L .env ]]; then
  printf '%s\n' 'Existing .env retained; cluster identity and credentials must survive restart.'
  exit 0
fi
version=$(python3 -c 'import json; print(json.load(open("release-version.json"))["version"])')
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]{6}\.[1-9][0-9]*$ ]]
incarnation=$(python3 -c 'import uuid; print(uuid.uuid4())')
set -o noclobber
{
  printf 'KEYLOAD_IMAGE=ghcr.io/managedcode/keyload-server:%s\n' "$version"
  printf 'KEYLOAD_CLUSTER_ID=keyload-%s\nKEYLOAD_INCARNATION=%s\n' "$incarnation" "$incarnation"
  printf 'KEYLOAD_SIGNING_KEY=%s\n' "$(openssl rand -base64 32)"
  printf 'KEYLOAD_PEER_SECRET=%s\n' "$(openssl rand -base64 32)"
  printf 'KEYLOAD_ADMIN_KEY=root.%s\n' "$(openssl rand -hex 32)"
} > .env
printf '%s\n' 'Created private .env. Back it up securely together with all three data volumes.'
