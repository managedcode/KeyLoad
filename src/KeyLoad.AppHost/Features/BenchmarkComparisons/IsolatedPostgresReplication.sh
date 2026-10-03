#!/bin/sh
set -eu

# Physical replication is separately authenticated; normal database rules do not cover it.
printf '%s\n' 'host replication postgres all scram-sha-256' >> "$PGDATA/pg_hba.conf"
