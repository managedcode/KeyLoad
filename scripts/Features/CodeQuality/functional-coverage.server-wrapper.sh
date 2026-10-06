#!/bin/sh
set -eu

coverage_identity_file=/opt/keyload-coverage/identity/runtime-identity.env
coverage_manifest_file=/opt/keyload-coverage/identity/context-manifest.json
coverage_source_receipt_file=/opt/keyload-coverage/identity/server-source-receipt.json
coverage_server_dll=/app/KeyLoad.Server.dll
coverage_server_pdb=/app/KeyLoad.Server.pdb
coverage_settings=/opt/keyload-coverage/settings.xml
coverage_tool=/opt/keyload-coverage/tool/dotnet-coverage.dll
coverage_report_name=native.coverage
coverage_receipt_name=terminal.json
coverage_server_process_file=/coverage/.native-server-process
coverage_server_exit_file=/coverage/.native-server-exit
coverage_stop_requested=0
coverage_signal=
coverage_watchdog_pid=
coverage_connect_pid=
coverage_collector_pid=
coverage_server_pid=
coverage_server_start_ticks=
coverage_connect_status=125
coverage_server_status=125
coverage_shutdown_status=125
coverage_collector_status=125
coverage_first_failure=0
coverage_server_term_sent=0
coverage_connect_waited=0
coverage_collector_waited=0

coverage_fail() {
    printf '%s\n' 'Native coverage server settlement failed.' >&2
    exit 125
}

coverage_is_decimal() {
    case "$1" in
        ''|0|0*|*[!0-9]*) return 1 ;;
        *) return 0 ;;
    esac
}

coverage_is_exit_status() {
    case "$1" in
        0|[1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5]) return 0 ;;
        *) return 1 ;;
    esac
}

coverage_is_hex_digest() {
    printf '%s\n' "$1" | grep -Eq '^[0-9a-f]{64}$'
}

coverage_is_guid() {
    printf '%s\n' "$1" | grep -Eq '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
}

coverage_is_label() {
    [ "${#1}" -le 64 ] || return 1
    case "$1" in ''|*[!A-Za-z0-9._-]*) return 1 ;; *) return 0 ;; esac
}

coverage_sha256() {
    coverage_hash_line=$(sha256sum "$1") || return 1
    printf '%s' "${coverage_hash_line%% *}"
}

coverage_read_identity() {
    coverage_identity_value=$(sed -n "s/^$1=//p" "$coverage_identity_file")
    [ -n "$coverage_identity_value" ] || return 1
    [ "$(printf '%s\n' "$coverage_identity_value" | wc -l | tr -d '[:space:]')" = 1 ] || return 1
}

coverage_verify_identity_file() {
    [ -f "$coverage_identity_file" ] && [ ! -L "$coverage_identity_file" ] || coverage_fail
    [ -f "$coverage_manifest_file" ] && [ ! -L "$coverage_manifest_file" ] || coverage_fail
    [ -f "$coverage_source_receipt_file" ] && [ ! -L "$coverage_source_receipt_file" ] || coverage_fail
    [ "$(coverage_sha256 "$coverage_manifest_file")" = "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" ] || coverage_fail

    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256 || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256 || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SERVER_MVID || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SERVER_MVID" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256 || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SETTINGS_SHA256 || coverage_fail
    coverage_settings_sha256=$(coverage_sha256 "$coverage_settings") || coverage_fail
    [ "$coverage_identity_value" = "$coverage_settings_sha256" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS" ] || coverage_fail
    coverage_read_identity KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES || coverage_fail
    [ "$coverage_identity_value" = "$KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES" ] || coverage_fail

    [ "$coverage_settings_sha256" = "$(coverage_sha256 "$coverage_settings")" ] || coverage_fail
    [ "$(coverage_sha256 "$coverage_source_receipt_file")" = "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" ] || coverage_fail
    [ "$(coverage_sha256 "$coverage_server_dll")" = "$KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256" ] || coverage_fail
    [ "$(coverage_sha256 "$coverage_server_pdb")" = "$KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256" ] || coverage_fail
    [ -f "$coverage_tool" ] && [ ! -L "$coverage_tool" ] || coverage_fail
    [ -f "$coverage_settings" ] && [ ! -L "$coverage_settings" ] || coverage_fail
}

coverage_validate_environment() {
    : "${KEYLOAD_NATIVE_COVERAGE_NODE:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SESSION:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_IMAGE_ID:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SERVER_MVID:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SETTINGS_PATH:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_STARTUP_POLL_MILLISECONDS:?}"
    : "${KEYLOAD_NATIVE_COVERAGE_STARTUP_TIMEOUT_MILLISECONDS:?}"

    coverage_is_label "$KEYLOAD_NATIVE_COVERAGE_NODE" || coverage_fail
    coverage_is_label "$KEYLOAD_NATIVE_COVERAGE_SESSION" || coverage_fail
    coverage_is_hex_digest "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" || coverage_fail
    printf '%s\n' "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" | grep -Eq '^sha256:[0-9a-f]{64}$' || coverage_fail
    coverage_is_hex_digest "$KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256" || coverage_fail
    coverage_is_hex_digest "$KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256" || coverage_fail
    coverage_is_guid "$KEYLOAD_NATIVE_COVERAGE_SERVER_MVID" || coverage_fail
    coverage_is_hex_digest "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" || coverage_fail
    coverage_is_label "$KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION" || coverage_fail
    coverage_is_hex_digest "$KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST" || coverage_fail
    [ "$KEYLOAD_NATIVE_COVERAGE_SETTINGS_PATH" = "$coverage_settings" ] || coverage_fail
    [ "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY" = /coverage ] || coverage_fail
    coverage_is_decimal "$KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS" || coverage_fail
    coverage_is_decimal "$KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS" || coverage_fail
    coverage_is_decimal "$KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES" || coverage_fail
    coverage_is_decimal "$KEYLOAD_NATIVE_COVERAGE_STARTUP_POLL_MILLISECONDS" || coverage_fail
    coverage_is_decimal "$KEYLOAD_NATIVE_COVERAGE_STARTUP_TIMEOUT_MILLISECONDS" || coverage_fail
    [ "$KEYLOAD_NATIVE_COVERAGE_STARTUP_POLL_MILLISECONDS" -le "$KEYLOAD_NATIVE_COVERAGE_STARTUP_TIMEOUT_MILLISECONDS" ] || coverage_fail
    [ "$KEYLOAD_NATIVE_COVERAGE_STARTUP_TIMEOUT_MILLISECONDS" -le "$((KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS * 1000))" ] || coverage_fail
    [ -d "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY" ] && [ ! -L "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY" ] || coverage_fail
    [ ! -e "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY/$coverage_report_name" ] || coverage_fail
    [ ! -e "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY/$coverage_receipt_name" ] || coverage_fail
    [ ! -e "$coverage_server_process_file" ] && [ ! -L "$coverage_server_process_file" ] || coverage_fail
    [ ! -e "$coverage_server_exit_file" ] && [ ! -L "$coverage_server_exit_file" ] || coverage_fail
}

. /usr/local/lib/keyload-coverage-server-lifecycle.sh

coverage_write_terminal_receipt() {
    coverage_report_path="$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY/$coverage_report_name"
    coverage_receipt_path="$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY/$coverage_receipt_name"
    [ -f "$coverage_report_path" ] && [ ! -L "$coverage_report_path" ] || coverage_fail
    [ ! -e "$coverage_receipt_path" ] || coverage_fail
    coverage_report_bytes=$(wc -c < "$coverage_report_path" | tr -d '[:space:]')
    coverage_is_decimal "$coverage_report_bytes" || coverage_fail
    [ "$coverage_report_bytes" -le "$KEYLOAD_NATIVE_COVERAGE_MAX_REPORT_BYTES" ] || coverage_fail
    coverage_report_sha=$(coverage_sha256 "$coverage_report_path") || coverage_fail
    coverage_report_bytes_after=$(wc -c < "$coverage_report_path" | tr -d '[:space:]')
    coverage_report_sha_after=$(coverage_sha256 "$coverage_report_path") || coverage_fail
    [ "$coverage_report_bytes" = "$coverage_report_bytes_after" ] || coverage_fail
    [ "$coverage_report_sha" = "$coverage_report_sha_after" ] || coverage_fail
    umask 077
    (
        set -C
        printf '{"schemaVersion":1,"node":"%s","session":"%s","contextDigest":"%s","imageId":"%s","serverDllSha256":"%s","serverPdbSha256":"%s","serverMvid":"%s","sourceReceiptSha256":"%s","toolVersion":"%s","toolClosureDigest":"%s","settingsSha256":"%s","shutdownSeconds":%s,"settlementSeconds":%s,"serverProcess":{"pid":%s,"startTicks":%s,"exitStatus":%s},"report":{"fileName":"%s","length":%s,"sha256":"%s"},"shutdownCommandExitCode":0,"connectCommandExitCode":0,"collectorCommandExitCode":0,"stopSignal":"%s"}\n' \
            "$KEYLOAD_NATIVE_COVERAGE_NODE" "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
            "$KEYLOAD_NATIVE_COVERAGE_CONTEXT_DIGEST" "$KEYLOAD_NATIVE_COVERAGE_IMAGE_ID" \
            "$KEYLOAD_NATIVE_COVERAGE_SERVER_DLL_SHA256" "$KEYLOAD_NATIVE_COVERAGE_SERVER_PDB_SHA256" \
            "$KEYLOAD_NATIVE_COVERAGE_SERVER_MVID" "$KEYLOAD_NATIVE_COVERAGE_SOURCE_RECEIPT_SHA256" \
            "$KEYLOAD_NATIVE_COVERAGE_TOOL_VERSION" "$KEYLOAD_NATIVE_COVERAGE_TOOL_CLOSURE_DIGEST" \
            "$coverage_settings_sha256" "$KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS" \
            "$KEYLOAD_NATIVE_COVERAGE_SETTLEMENT_SECONDS" "$coverage_server_pid" \
            "$coverage_server_start_ticks" "$coverage_server_status" "$coverage_report_name" \
            "$coverage_report_bytes" "$coverage_report_sha" "$coverage_signal" > "$coverage_receipt_path"
    ) || coverage_fail
}

coverage_report_failure() {
    if [ "$coverage_connect_status" -ne 0 ]; then
        printf 'Native coverage connect command exited with status %s.\n' "$coverage_connect_status" >&2
    fi
    if [ "$coverage_server_status" -ne 0 ]; then
        printf 'Original Server process exited with status %s or lacked a complete exit witness.\n' "$coverage_server_status" >&2
    fi
    if [ "$coverage_shutdown_status" -ne 0 ]; then
        printf 'Native coverage shutdown command exited with status %s.\n' "$coverage_shutdown_status" >&2
    fi
    if [ "$coverage_collector_status" -ne 0 ]; then
        printf 'Native coverage collector exited with status %s.\n' "$coverage_collector_status" >&2
    fi
}

coverage_settle_originals() {
    coverage_arm_watchdog
    if [ "$coverage_server_term_sent" -eq 0 ]; then
        if ! coverage_signal_original_server; then
            [ "$coverage_first_failure" -ne 0 ] || coverage_first_failure=125
        fi
    fi
    coverage_wait_connect_or_signal
    coverage_wait_server_exit
    if [ "$coverage_connect_status" -ne 0 ] && [ "$coverage_first_failure" -eq 0 ]; then
        coverage_first_failure=$coverage_connect_status
    fi
    if ! coverage_validate_server_exit; then
        [ "$coverage_first_failure" -ne 0 ] || coverage_first_failure=125
    fi
    coverage_shutdown_collector
    coverage_wait_collector
    coverage_cancel_watchdog
}

trap coverage_on_term TERM
trap coverage_on_int INT
trap coverage_on_settlement_timeout USR1
coverage_validate_environment
coverage_verify_identity_file

dotnet "$coverage_tool" collect --server-mode \
    --session-id "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
    --settings "$coverage_settings" \
    --output "$KEYLOAD_NATIVE_COVERAGE_OUTPUT_DIRECTORY/$coverage_report_name" \
    --output-format coverage \
    --timeout "${KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS}000" \
    --disable-console-output \
    --nologo &
coverage_collector_pid=$!

dotnet "$coverage_tool" connect "$KEYLOAD_NATIVE_COVERAGE_SESSION" \
    /usr/local/bin/keyload-coverage-server-target \
    --timeout "${KEYLOAD_NATIVE_COVERAGE_SHUTDOWN_SECONDS}000" \
    --disable-console-output \
    --nologo &
coverage_connect_pid=$!

coverage_wait_connect_or_signal
if [ "$coverage_stop_requested" -eq 0 ]; then
    coverage_note_stop FAILURE
fi
coverage_settle_originals
if [ "$coverage_stop_requested" != 1 ] || [ "$coverage_signal" = FAILURE ] ||
   [ "$coverage_first_failure" -ne 0 ] || [ "$coverage_connect_status" -ne 0 ] ||
   [ "$coverage_server_status" -ne 0 ] || [ "$coverage_shutdown_status" -ne 0 ] ||
   [ "$coverage_collector_status" -ne 0 ]; then
    coverage_report_failure
    exit "${coverage_first_failure:-125}"
fi

coverage_write_terminal_receipt
rm -f "$coverage_server_process_file" "$coverage_server_exit_file"
exit 0
