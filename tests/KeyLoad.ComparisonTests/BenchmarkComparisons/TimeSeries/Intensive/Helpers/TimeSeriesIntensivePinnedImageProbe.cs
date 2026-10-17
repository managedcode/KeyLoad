namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePinnedImageProbe
{
    internal const string Script = """
        set -eu
        entrypoint="$1"
        entrypoint_path=$(command -v "$entrypoint")
        test -x "$entrypoint_path"
        printf 'entrypointPath\t%s\n' "$entrypoint_path"
        printf 'rootUid\t%s\n' "$(id -u)"
        test "$(id -u)" = 0
        printf 'kernel\t%s\n' "$(uname -s)"
        if test -r /etc/os-release; then
            . /etc/os-release
            printf 'distribution\t%s\n' "$ID"
            printf 'distributionVersion\t%s\n' "${VERSION_ID:-unavailable}"
        else
            exit 1
        fi
        for tool in postgres pg_basebackup pg_isready timeout; do
            tool_path=$(command -v "$tool")
            test -x "$tool_path"
            printf 'tool.%s\t%s\n' "$tool" "$tool_path"
        done
        version=$(postgres --version)
        printf 'postgresVersion\t%s\n' "$version"
        major=$(printf '%s\n' "$version" | sed -n 's/^postgres (PostgreSQL) \([0-9][0-9]*\).*/\1/p')
        test "$major" = 18
        printf 'postgresMajor\t%s\n' "$major"
        if command -v gosu >/dev/null 2>&1; then
            switch_name=gosu
        elif command -v su-exec >/dev/null 2>&1; then
            switch_name=su-exec
        else
            exit 1
        fi
        switch_path=$(command -v "$switch_name")
        test -x "$switch_path"
        printf 'userSwitchName\t%s\n' "$switch_name"
        printf 'userSwitchPath\t%s\n' "$switch_path"
        test "$PGDATA" = /var/lib/postgresql/18/docker
        mkdir -p "$PGDATA"
        chown -R postgres:postgres /var/lib/postgresql
        chmod 700 /var/lib/postgresql "$PGDATA"
        postgres_uid=$("$switch_path" postgres id -u)
        test "$postgres_uid" != 0
        printf 'postgresUid\t%s\n' "$postgres_uid"
        "$switch_path" postgres /bin/sh -c '
            set -eu
            test "$(id -u)" != 0
            umask 077
            probe="$PGDATA/.keyload-image-feasibility"
            printf "%s\n" image-feasibility > "$probe"
            test -s "$probe"
            rm "$probe"
        '
        printf 'pgData\t%s\n' "$PGDATA"
        printf 'writeVerified\ttrue\n'
        """;
}
