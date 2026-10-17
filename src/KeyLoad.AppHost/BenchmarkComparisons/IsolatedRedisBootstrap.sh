#!/bin/sh
set -eu
umask 077

: "${KEYLOAD_REDIS_PASSWORD:?Missing native Redis credential}"
case "$KEYLOAD_REDIS_PASSWORD" in
    *[!0-9a-f]*) exit 1 ;;
esac
[ "${#KEYLOAD_REDIS_PASSWORD}" -eq 64 ] || exit 1

config=/data/isolated-redis.conf
{
    printf '%s\n' 'dir /data' 'bind 0.0.0.0' 'protected-mode yes' 'appendonly yes' 'appendfsync always' 'save ""'
    printf 'requirepass %s\nmasterauth %s\n' "$KEYLOAD_REDIS_PASSWORD" "$KEYLOAD_REDIS_PASSWORD"
    if [ -n "${KEYLOAD_REDIS_PRIMARY:-}" ]; then
        [ "$KEYLOAD_REDIS_PRIMARY" = primary.dev.internal ] || exit 1
        printf '%s\n' 'replicaof primary.dev.internal 6379'
    fi
} > "$config"

unset KEYLOAD_REDIS_PASSWORD REDIS_PASSWORD
exec /usr/local/bin/docker-entrypoint.sh redis-server "$config"
