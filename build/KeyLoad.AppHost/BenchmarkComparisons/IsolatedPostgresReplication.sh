#!/bin/sh
set -eu

# Physical replication is separately authenticated; normal database rules do not cover it.
printf '%s\n' 'host replication postgres all scram-sha-256' >> "$PGDATA/pg_hba.conf"

: "${KEYLOAD_POSTGRES_STANDBYS:?Postgres standby count environment required}"
case "$KEYLOAD_POSTGRES_STANDBYS" in
    0|1|2) ;;
    *) printf '%s\n' 'PostgresNativeStandbyCountInvalid' >&2; exit 1 ;;
esac

# Reserve before TCP readiness: parallel base backups must retain their starting WAL.
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-password --set ON_ERROR_STOP=1 <<SQL
SELECT pg_create_physical_replication_slot('benchmark_standby' || ordinal::text, true, false)
FROM generate_series(1, $KEYLOAD_POSTGRES_STANDBYS) AS ordinal;
SQL
