#!/usr/bin/env bash
set -euo pipefail

# AC-BC-028: authenticated transport; validators never impersonate GitHub.
readonly evidence_gate_filename=github-evidence.mjs
readonly evidence_timeout=120s evidence_pair_limit=10000 evidence_zero=0 evidence_one=1
readonly evidence_repository=managedcode/KeyLoad evidence_api_root=repos/managedcode/KeyLoad/actions
readonly evidence_input=--input evidence_mode=--mode evidence_site=--site-revision
readonly evidence_workflow=--workflow-revision evidence_requested=--requested-run
readonly evidence_validate=validate evidence_publish=publish evidence_selected=selected
readonly evidence_needs_attempt=needs_attempt evidence_select_command=select evidence_prove_command=prove
readonly evidence_attempts_directory=attempts evidence_workflow_file=workflow.json
readonly evidence_runs_file=runs-pages.json evidence_run_file=run.json evidence_jobs_file=jobs-pages.json
readonly evidence_artifacts_file=artifacts-pages.json evidence_selection_file=selection.json
readonly evidence_envelope_file=proof-envelope.json evidence_proof_file=metadata-proof.json
readonly evidence_workflow_route=workflows/ci.yml
readonly evidence_runs_route='workflows/ci.yml/runs?branch=main&event=push&per_page=100'
readonly evidence_jobs_suffix='jobs?per_page=100' evidence_artifacts_suffix='artifacts?per_page=100'
readonly evidence_envelope_check='.ok == true and (.result | type == "object")'
readonly evidence_state_selector=.result.state evidence_run_selector=.result.runId
readonly evidence_attempt_selector=.result.runAttempt evidence_result_selector=.result
readonly evidence_sha_pattern='^[a-f0-9]{40}$' evidence_id_pattern='^[1-9][0-9]*$'
readonly evidence_equals='=' evidence_argument_prefix='--' evidence_absolute_prefix='/'
readonly evidence_fetch_marker=present
readonly evidence_runs_component=runs evidence_null_device=/dev/null evidence_private_umask=077
readonly evidence_gate="$(cd -- "$(dirname -- "${BASH_SOURCE[$evidence_zero]}")" && pwd)/$evidence_gate_filename"
declare -A evidence_options=()

parse_options() {
  local argument name value
  for argument in "$@"; do
    [[ "$argument" == "$evidence_argument_prefix"*"$evidence_equals"* ]] || return "$evidence_one"
    name="${argument%%"$evidence_equals"*}"
    value="${argument#*"$evidence_equals"}"
    case "$name" in
      "$evidence_input"|"$evidence_mode"|"$evidence_site"|"$evidence_workflow"|"$evidence_requested") ;;
      *) return "$evidence_one" ;;
    esac
    [[ "${evidence_options[$name]+$evidence_fetch_marker}" != "$evidence_fetch_marker" ]] || return "$evidence_one"
    evidence_options["$name"]="$value"
  done
  [[ "${evidence_options[$evidence_input]:-}" == "$evidence_absolute_prefix"* ]]
  [[ "${evidence_options[$evidence_mode]:-}" == "$evidence_validate" || "${evidence_options[$evidence_mode]:-}" == "$evidence_publish" ]]
  [[ "${evidence_options[$evidence_site]:-}" =~ $evidence_sha_pattern ]]
  [[ "${evidence_options[$evidence_workflow]:-}" =~ $evidence_sha_pattern ]]
  [[ "${GH_REPO:-}" == "$evidence_repository" && -n "${GH_TOKEN:-}" ]]
}

fetch_metadata() {
  local destination="$1"
  shift
  [[ ! -e "$destination" && ! -L "$destination" ]]
  timeout "$evidence_timeout" gh api "$@" > "$destination"
  [[ -s "$destination" ]]
}

run_gate() {
  local command="$1" destination="$2"
  shift 2
  if ! node "$evidence_gate" "$command" "$@" > "$destination"; then
    cat "$destination" >&2
    return "$evidence_one"
  fi
  jq -e "$evidence_envelope_check" "$destination" > "$evidence_null_device"
}

fetch_attempt() {
  local run_id="$1" attempt="$2" directory="$3"
  [[ "$run_id" =~ $evidence_id_pattern && "$attempt" =~ $evidence_id_pattern ]]
  mkdir -p "$directory"
  local route="$evidence_api_root/$evidence_runs_component/$run_id/$evidence_attempts_directory/$attempt"
  fetch_metadata "$directory/$evidence_run_file" "$route"
  fetch_metadata "$directory/$evidence_jobs_file" --paginate --slurp "$route/$evidence_jobs_suffix"
}

select_comparison() {
  local directory="$1" iteration state run_id attempt
  local -a arguments=("$evidence_input=$directory" "$evidence_mode=${evidence_options[$evidence_mode]}")
  if [[ -n "${evidence_options[$evidence_requested]:-}" ]]; then
    arguments+=("$evidence_requested=${evidence_options[$evidence_requested]}")
  fi
  for ((iteration=evidence_zero; iteration<=evidence_pair_limit; iteration++)); do
    run_gate "$evidence_select_command" "$directory/$evidence_selection_file" "${arguments[@]}"
    state="$(jq -r "$evidence_state_selector" "$directory/$evidence_selection_file")"
    [[ "$state" != "$evidence_selected" ]] || return "$evidence_zero"
    [[ "$state" == "$evidence_needs_attempt" && "$iteration" -lt "$evidence_pair_limit" ]] || return "$evidence_one"
    run_id="$(jq -r "$evidence_run_selector" "$directory/$evidence_selection_file")"
    attempt="$(jq -r "$evidence_attempt_selector" "$directory/$evidence_selection_file")"
    fetch_attempt "$run_id" "$attempt" "$directory/$evidence_attempts_directory/$run_id/$attempt"
  done
  return "$evidence_one"
}

prove_selected() {
  local directory="$1" run_id attempt
  run_id="$(jq -r "$evidence_run_selector" "$directory/$evidence_selection_file")"
  attempt="$(jq -r "$evidence_attempt_selector" "$directory/$evidence_selection_file")"
  cp "$directory/$evidence_attempts_directory/$run_id/$attempt/$evidence_run_file" "$directory/$evidence_run_file"
  cp "$directory/$evidence_attempts_directory/$run_id/$attempt/$evidence_jobs_file" "$directory/$evidence_jobs_file"
  fetch_metadata "$directory/$evidence_artifacts_file" --paginate --slurp "$evidence_api_root/$evidence_runs_component/$run_id/$evidence_artifacts_suffix"
  local -a arguments=("$evidence_input=$directory" "$evidence_mode=${evidence_options[$evidence_mode]}"
    "$evidence_site=${evidence_options[$evidence_site]}" "$evidence_workflow=${evidence_options[$evidence_workflow]}")
  if [[ -n "${evidence_options[$evidence_requested]:-}" ]]; then
    arguments+=("$evidence_requested=${evidence_options[$evidence_requested]}")
  fi
  run_gate "$evidence_prove_command" "$directory/$evidence_envelope_file" "${arguments[@]}"
  jq "$evidence_result_selector" "$directory/$evidence_envelope_file" > "$directory/$evidence_proof_file"
}

main() {
  parse_options "$@"
  local directory="${evidence_options[$evidence_input]}"
  [[ ! -e "$directory" && ! -L "$directory" ]]
  umask "$evidence_private_umask"
  mkdir -p "$directory"
  fetch_metadata "$directory/$evidence_workflow_file" "$evidence_api_root/$evidence_workflow_route"
  fetch_metadata "$directory/$evidence_runs_file" --paginate --slurp "$evidence_api_root/$evidence_runs_route"
  select_comparison "$directory"
  prove_selected "$directory"
}

main "$@"
