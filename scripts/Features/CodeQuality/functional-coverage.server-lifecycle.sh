#!/bin/sh

coverage_arm_watchdog() {
    if [ -z "$coverage_watchdog_pid" ]; then
        coverage_parent_pid=$$
        (
            coverage_timer_pid=
            coverage_cancel_timer() {
                if [ -n "$coverage_timer_pid" ]; then
                    kill -TERM "$coverage_timer_pid" 2>/dev/null || :
                    wait "$coverage_timer_pid" 2>/dev/null || :
                fi
                exit 0
            }
            trap coverage_cancel_timer TERM INT
            sleep "$KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS" &
            coverage_timer_pid=$!
            if wait "$coverage_timer_pid"; then
                kill -USR1 "$coverage_parent_pid" 2>/dev/null || exit 0
            fi
            exit 0
        ) &
        coverage_watchdog_pid=$!
    fi
}

coverage_note_stop() {
    if [ "$coverage_stop_requested" -eq 0 ]; then
        coverage_stop_requested=1
        coverage_signal=$1
        coverage_arm_watchdog
    fi
}

coverage_on_term() {
    coverage_note_stop TERM
}

coverage_on_int() {
    coverage_note_stop INT
}

coverage_on_settlement_timeout() {
    printf '%s\n' 'Native coverage settlement exceeded its configured bound.' >&2
    exit 124
}

coverage_read_server_identity() {
    coverage_server_node=
    coverage_server_session=
    coverage_server_context=
    coverage_server_image=
    coverage_server_source=
    coverage_server_pid=
    coverage_server_start_ticks=
    coverage_extra=
    [ -f "$coverage_server_process_file" ] && [ ! -L "$coverage_server_process_file" ] || return 1
    [ "$(wc -c < "$coverage_server_process_file" | tr -d '[:space:]')" -le 512 ] || return 1
    [ "$(wc -l < "$coverage_server_process_file" | tr -d '[:space:]')" = 1 ] || return 1
    coverage_extra=
    IFS=' ' read -r coverage_server_node coverage_server_session coverage_server_context \
        coverage_server_image coverage_server_source coverage_server_pid coverage_server_start_ticks coverage_extra \
        < "$coverage_server_process_file"
    [ "$coverage_server_node" = "$KEYLOAD_NATIVE_COVERAGE_NODE" ] || return 1
    [ "$coverage_server_session" = "$KEYLOAD_NATIVE_COVERAGE_SESSION" ] || return 1
    [ "$coverage_server_context" = "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" ] || return 1
    [ "$coverage_server_image" = "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" ] || return 1
    [ "$coverage_server_source" = "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" ] || return 1
    coverage_is_decimal "$coverage_server_pid" || return 1
    coverage_is_decimal "$coverage_server_start_ticks" || return 1
    [ -z "$coverage_extra" ] || return 1
}

coverage_read_proc_identity() {
    coverage_current_start_ticks=$(awk '{print $22}' "/proc/$coverage_server_pid/stat" 2>/dev/null) || return 1
    coverage_current_command=$(tr '\000' ' ' < "/proc/$coverage_server_pid/cmdline" 2>/dev/null) || return 1
    [ "$coverage_current_start_ticks" = "$coverage_server_start_ticks" ] || return 1
    coverage_process_state=$(awk '$1 == "State:" { print $2 }' "/proc/$coverage_server_pid/status" 2>/dev/null) || return 1
    [ "$coverage_process_state" != Z ] || return 1
    case "$coverage_current_command" in
        "dotnet $coverage_server_dll "|"/usr/share/dotnet/dotnet $coverage_server_dll ") return 0 ;;
        *) return 1 ;;
    esac
}

coverage_read_proc_generation() {
    coverage_current_start_ticks=$(awk '{print $22}' "/proc/$coverage_server_pid/stat" 2>/dev/null) || return 1
    [ "$coverage_current_start_ticks" = "$coverage_server_start_ticks" ] || return 1
    coverage_process_state=$(awk '$1 == "State:" { print $2 }' "/proc/$coverage_server_pid/status" 2>/dev/null) || return 1
    [ "$coverage_process_state" != Z ]
}

coverage_same_server_instance_is_live() {
    coverage_current_start_ticks=$(awk '{print $22}' "/proc/$coverage_server_pid/stat" 2>/dev/null) || return 1
    [ "$coverage_current_start_ticks" = "$coverage_server_start_ticks" ] || return 1
    coverage_process_state=$(awk '$1 == "State:" { print $2 }' "/proc/$coverage_server_pid/status" 2>/dev/null) || return 1
    [ "$coverage_process_state" != Z ]
}

coverage_signal_original_server() {
    coverage_read_server_identity || return 1
    if coverage_read_proc_generation; then
        kill -TERM "$coverage_server_pid" || return 1
        coverage_server_term_sent=1
    fi
}

coverage_original_server_is_live() {
    coverage_read_proc_identity
}

coverage_wait_server_exit() {
    while coverage_same_server_instance_is_live; do
        sleep 1
    done
}

coverage_child_is_live() {
    coverage_child_pid=$1
    kill -0 "$coverage_child_pid" 2>/dev/null || return 1
    coverage_child_state=$(awk '$1 == "State:" { print $2 }' "/proc/$coverage_child_pid/status" 2>/dev/null || :)
    [ "$coverage_child_state" != Z ]
}

coverage_wait_child_result() {
    coverage_child_pid=$1
    coverage_child_status=125
    if wait "$coverage_child_pid"; then
        coverage_child_status=0
    else
        coverage_child_status=$?
    fi
}

coverage_wait_child() {
    coverage_child_pid=$1
    coverage_child_status=125
    coverage_child_waited=0
    while coverage_child_is_live "$coverage_child_pid"; do
        sleep 1
    done
    coverage_wait_child_result "$coverage_child_pid"
    coverage_child_waited=1
}

coverage_wait_connect_or_signal() {
    while [ "$coverage_connect_waited" -eq 0 ]; do
        if [ "$coverage_stop_requested" -eq 1 ] && [ "$coverage_server_term_sent" -eq 0 ]; then
            if ! coverage_signal_original_server; then
                coverage_first_failure=125
            fi
        fi
        if ! coverage_child_is_live "$coverage_connect_pid"; then
            coverage_wait_child_result "$coverage_connect_pid"
            coverage_connect_status=$coverage_child_status
            coverage_connect_waited=1
        else
            sleep 1
        fi
    done
}

coverage_validate_server_exit() {
    coverage_exit_node=
    coverage_exit_session=
    coverage_exit_context=
    coverage_exit_image=
    coverage_exit_source=
    coverage_exit_pid=
    coverage_exit_start_ticks=
    [ -f "$coverage_server_exit_file" ] && [ ! -L "$coverage_server_exit_file" ] || return 1
    [ "$(wc -c < "$coverage_server_exit_file" | tr -d '[:space:]')" -le 512 ] || return 1
    [ "$(wc -l < "$coverage_server_exit_file" | tr -d '[:space:]')" = 1 ] || return 1
    coverage_exit_pid=
    coverage_exit_start_ticks=
    coverage_server_status=125
    coverage_extra=
    IFS=' ' read -r coverage_exit_node coverage_exit_session coverage_exit_context \
        coverage_exit_image coverage_exit_source coverage_exit_pid coverage_exit_start_ticks \
        coverage_server_status coverage_extra < "$coverage_server_exit_file"
    [ "$coverage_exit_node" = "$KEYLOAD_NATIVE_COVERAGE_NODE" ] || return 1
    [ "$coverage_exit_session" = "$KEYLOAD_NATIVE_COVERAGE_SESSION" ] || return 1
    [ "$coverage_exit_context" = "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" ] || return 1
    [ "$coverage_exit_image" = "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" ] || return 1
    [ "$coverage_exit_source" = "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" ] || return 1
    [ "$coverage_exit_pid" = "$coverage_server_pid" ] || return 1
    [ "$coverage_exit_start_ticks" = "$coverage_server_start_ticks" ] || return 1
    coverage_is_exit_status "$coverage_server_status" || return 1
    [ -z "$coverage_extra" ] || return 1
    if coverage_original_server_is_live; then
        return 1
    fi
    [ "$coverage_server_status" = 0 ]
}

coverage_shutdown_collector() {
    if dotnet "$coverage_tool" shutdown "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
        --timeout "${KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS}000" --disable-console-output --nologo; then
        coverage_shutdown_status=0
    else
        coverage_shutdown_status=$?
        [ "$coverage_first_failure" -ne 0 ] || coverage_first_failure=$coverage_shutdown_status
    fi
}

coverage_wait_collector() {
    coverage_wait_child "$coverage_collector_pid"
    coverage_collector_status=$coverage_child_status
    coverage_collector_waited=$coverage_child_waited
    if [ "$coverage_collector_status" -ne 0 ] && [ "$coverage_first_failure" -eq 0 ]; then
        coverage_first_failure=$coverage_collector_status
    fi
}

coverage_cancel_watchdog() {
    if [ -n "$coverage_watchdog_pid" ]; then
        if kill -0 "$coverage_watchdog_pid" 2>/dev/null; then
            kill -TERM "$coverage_watchdog_pid" 2>/dev/null || coverage_fail
        fi
        if wait "$coverage_watchdog_pid"; then
            coverage_watchdog_pid=
        else
            coverage_fail
        fi
    fi
}
