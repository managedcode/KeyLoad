#!/bin/sh
set -eu
umask 077

# The fresh PG18 mount is owned only by this selected cell and node.
if [ "$(id -u)" = 0 ]; then
    mkdir -p "$PGDATA"
    chown -R postgres:postgres /var/lib/postgresql
    chmod 700 /var/lib/postgresql "$PGDATA"
    exec gosu postgres /bin/sh "$0" "$@"
fi

if [ -n "${KEYLOAD_POSTGRES_PRIMARY:-}" ] && [ ! -s "$PGDATA/PG_VERSION" ]; then
    : "${PGPASSWORD:?Postgres replication password environment required}"
    : "${PGAPPNAME:?Postgres standby identity environment required}"
    attempts=0
    until pg_isready -h "$KEYLOAD_POSTGRES_PRIMARY" -p 5432 -U postgres -t 1 >/dev/null 2>&1; do
        attempts=$((attempts + 1))
        if [ "$attempts" -ge 120 ]; then
            printf '%s\n' 'PostgresNativeBootstrapTimeout' >&2
            exit 1
        fi
        sleep 1
    done
    timeout 120 pg_basebackup \
        --dbname="host=$KEYLOAD_POSTGRES_PRIMARY port=5432 user=postgres application_name=$PGAPPNAME" \
        --pgdata="$PGDATA" --write-recovery-conf --wal-method=stream --no-password
fi

exec /usr/local/bin/docker-entrypoint.sh "$@"
