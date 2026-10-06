#!/bin/sh
set -eu

server_binary=/app/KeyLoad.Server.dll
server_process_file=/coverage/.native-server-process
server_exit_file=/coverage/.native-server-exit

server_identity_matches() {
    current_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null) || return 1
    [ "$current_start_ticks" = "$server_start_ticks" ] || return 1
    current_command_line=$(tr '\000' ' ' < "/proc/$server_pid/cmdline" 2>/dev/null) || return 1
    [ "$current_command_line" = "$server_command_line" ] || return 1
    case "$current_command_line" in
        "dotnet $server_binary "|"/usr/share/dotnet/dotnet $server_binary ") return 0 ;;
        *) return 1 ;;
    esac
}

: "${KEYLOAD_NATIVE_COVERAGE_NODE:?}"
: "${KEYLOAD_NATIVE_COVERAGE_SESSION:?}"
: "${KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST:?}"
: "${KEYLOAD_NATIVE_COVERAGE_IMAGE_ID:?}"
: "${KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256:?}"

[ ! -e "$server_process_file" ] && [ ! -L "$server_process_file" ] || exit 125
[ ! -e "$server_exit_file" ] && [ ! -L "$server_exit_file" ] || exit 125

dotnet "$server_binary" &
server_pid=$!
server_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null) || exit 125
server_command_line=$(tr '\000' ' ' < "/proc/$server_pid/cmdline" 2>/dev/null) || exit 125
case "$server_command_line" in
    "dotnet $server_binary "|"/usr/share/dotnet/dotnet $server_binary ") ;;
    *) exit 125 ;;
esac
case "$server_start_ticks" in
    ''|*[!0-9]*) exit 125 ;;
esac

umask 077
(
    set -C
    printf '%s %s %s %s %s %s %s\n' \
        "$KEYLOAD_NATIVE_COVERAGE_NODE" "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
        "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" \
        "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" "$server_pid" "$server_start_ticks" \
        > "$server_process_file"
) || {
    if server_identity_matches; then
        kill -TERM "$server_pid" 2>/dev/null || :
    fi
    wait "$server_pid" 2>/dev/null || :
    exit 125
}

if wait "$server_pid"; then
    server_exit_status=0
else
    server_exit_status=$?
fi
(
    set -C
    printf '%s %s %s %s %s %s %s %s\n' \
        "$KEYLOAD_NATIVE_COVERAGE_NODE" "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
        "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" \
        "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" "$server_pid" "$server_start_ticks" \
        "$server_exit_status" > "$server_exit_file"
) || exit 125
exit "$server_exit_status"
