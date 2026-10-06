#!/bin/sh
set -eu

server_binary=/app/KeyLoad.Server.dll
server_process_file=/coverage/.native-server-process
server_exit_file=/coverage/.native-server-exit
startup_poll_milliseconds=${KEYLOAD_NATIVE_COVERAGE_STARTUP_POLL_MILLISECONDS:?}
startup_timeout_milliseconds=${KEYLOAD_NATIVE_COVERAGE_STARTUP_TIMEOUT_MILLISECONDS:?}
settlement_seconds=${KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS:?}

is_positive_decimal() {
    case "$1" in
        ''|0|0*|*[!0-9]*) return 1 ;;
        *) return 0 ;;
    esac
}

sleep_milliseconds() {
    sleep_seconds=$(( $1 / 1000 ))
    sleep_fraction=$(( $1 % 1000 ))
    sleep_value=$(printf '%d.%03d' "$sleep_seconds" "$sleep_fraction")
    sleep "$sleep_value"
}

server_generation_is_live() {
    current_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null) || return 1
    [ "$current_start_ticks" = "$server_start_ticks" ] || return 1
    process_state=$(awk '$1 == "State:" { print $2 }' "/proc/$server_pid/status" 2>/dev/null) || return 1
    [ "$process_state" != Z ]
}

stop_and_join_original_child() {
    [ -n "$server_start_ticks" ] || return 1
    settle_attempts=$(( (settlement_seconds * 1000 + startup_poll_milliseconds - 1) / startup_poll_milliseconds ))
    term_attempts=$(( settle_attempts / 2 ))
    kill_attempts=$(( settle_attempts - term_attempts ))
    if server_generation_is_live; then kill -TERM "$server_pid" 2>/dev/null || :; fi
    while [ "$term_attempts" -gt 0 ] && server_generation_is_live; do
        term_attempts=$((term_attempts - 1))
        sleep_milliseconds "$startup_poll_milliseconds"
    done
    if server_generation_is_live; then kill -KILL "$server_pid" 2>/dev/null || :; fi
    while [ "$kill_attempts" -gt 0 ] && server_generation_is_live; do
        kill_attempts=$((kill_attempts - 1))
        sleep_milliseconds "$startup_poll_milliseconds"
    done
    if server_generation_is_live; then return 1; fi
    wait "$server_pid" 2>/dev/null || :
}

stop_and_join_unobserved_child() {
    settle_attempts=$(( (settlement_seconds * 1000 + startup_poll_milliseconds - 1) / startup_poll_milliseconds ))
    term_attempts=$(( settle_attempts / 2 ))
    kill_attempts=$(( settle_attempts - term_attempts ))
    kill -TERM "$server_pid" 2>/dev/null || :
    while [ "$term_attempts" -gt 0 ] && kill -0 "$server_pid" 2>/dev/null; do
        term_attempts=$((term_attempts - 1))
        sleep_milliseconds "$startup_poll_milliseconds"
    done
    if kill -0 "$server_pid" 2>/dev/null; then kill -KILL "$server_pid" 2>/dev/null || :; fi
    while [ "$kill_attempts" -gt 0 ] && kill -0 "$server_pid" 2>/dev/null; do
        kill_attempts=$((kill_attempts - 1))
        sleep_milliseconds "$startup_poll_milliseconds"
    done
    if kill -0 "$server_pid" 2>/dev/null; then return 1; fi
    wait "$server_pid" 2>/dev/null || :
}

: "${KEYLOAD_NATIVE_COVERAGE_NODE:?}"
: "${KEYLOAD_NATIVE_COVERAGE_SESSION:?}"
: "${KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST:?}"
: "${KEYLOAD_NATIVE_COVERAGE_IMAGE_ID:?}"
: "${KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256:?}"
is_positive_decimal "$startup_poll_milliseconds" || exit 125
is_positive_decimal "$startup_timeout_milliseconds" || exit 125
is_positive_decimal "$settlement_seconds" || exit 125
[ "$startup_poll_milliseconds" -le "$startup_timeout_milliseconds" ] || exit 125

[ ! -e "$server_process_file" ] && [ ! -L "$server_process_file" ] || exit 125
[ ! -e "$server_exit_file" ] && [ ! -L "$server_exit_file" ] || exit 125

dotnet "$server_binary" &
server_pid=$!
attempt_limit=$(( (startup_timeout_milliseconds + startup_poll_milliseconds - 1) / startup_poll_milliseconds ))
server_start_ticks=
while [ "$attempt_limit" -gt 0 ]; do
    if server_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null) \
        && is_positive_decimal "$server_start_ticks"; then
        break
    fi
    if ! kill -0 "$server_pid" 2>/dev/null; then
        wait "$server_pid" 2>/dev/null || :
        exit 125
    fi
    attempt_limit=$((attempt_limit - 1))
    sleep_milliseconds "$startup_poll_milliseconds"
done
[ -n "$server_start_ticks" ] || {
    server_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null || :)
    if is_positive_decimal "$server_start_ticks"; then
        stop_and_join_original_child || exit 125
    else
        stop_and_join_unobserved_child
    fi
    exit 125
}

umask 077
(
    set -C
    printf '%s %s %s %s %s %s %s\n' \
        "$KEYLOAD_NATIVE_COVERAGE_NODE" "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
        "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" \
        "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" "$server_pid" "$server_start_ticks" \
        > "$server_process_file"
) || {
    stop_and_join_original_child || exit 125
    exit 125
}

attempt_limit=$(( (startup_timeout_milliseconds + startup_poll_milliseconds - 1) / startup_poll_milliseconds ))
server_ready=0
while [ "$attempt_limit" -gt 0 ]; do
    current_start_ticks=$(awk '{print $22}' "/proc/$server_pid/stat" 2>/dev/null || :)
    if [ "$current_start_ticks" != "$server_start_ticks" ]; then
        break
    fi
    server_command_line=$(tr '\000' ' ' < "/proc/$server_pid/cmdline" 2>/dev/null || :)
    case "$server_command_line" in
        "dotnet $server_binary "|"/usr/share/dotnet/dotnet $server_binary ")
            server_ready=1
            break
            ;;
    esac
    if ! kill -0 "$server_pid" 2>/dev/null; then
        break
    fi
    attempt_limit=$((attempt_limit - 1))
    sleep_milliseconds "$startup_poll_milliseconds"
done

if [ "$server_ready" -ne 1 ]; then
    stop_and_join_original_child || exit 125
    exit 125
fi

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
