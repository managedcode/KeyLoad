#!/bin/sh
set -eu
umask 077

if [ -n "${KEYLOAD_MONGO_REPLICATION_KEY:-}" ]; then
    key_directory=/tmp/keyload-mongo-key
    mkdir -p "$key_directory"
    chmod 700 "$key_directory"
    printf '%s' "$KEYLOAD_MONGO_REPLICATION_KEY" > "$key_directory/replication.key"
    chmod 600 "$key_directory/replication.key"
    chown mongodb:mongodb "$key_directory" "$key_directory/replication.key"
    unset KEYLOAD_MONGO_REPLICATION_KEY
fi

exec /usr/local/bin/docker-entrypoint.sh "$@"
