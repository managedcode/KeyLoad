#!/usr/bin/env python3
"""Capture authenticated, exact-source GitHub release context using gh and git."""

import argparse
import datetime
import json
import os
import re
import subprocess
import sys
from urllib.parse import parse_qs, urlparse
import xml.etree.ElementTree as ElementTree
from pathlib import Path

REPOSITORY = "managedcode/KeyLoad"
RELEASE_PATH = ".github/workflows/release.yml"
CI_PATH = ".github/workflows/ci.yml"
MAX_RESPONSE_BYTES = 16 * 1024 * 1024
MAX_ITEMS = 10_000
MAX_PAGES = 100
REQUIRED_CI_JOBS = {"Repository checks", "analyzer-rules", "Build and tests", "docker-rf3"}
REPOSITORY_ID = None


class ContextError(Exception):
    """Raised when the live GitHub context is missing or inconsistent."""


def fail(message):
    raise ContextError(message)


def command(arguments, timeout=30, maximum=MAX_RESPONSE_BYTES):
    try:
        result = subprocess.run(arguments, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        fail("Required GitHub or Git command failed or timed out.")
    if result.returncode != 0 or len(result.stdout) > maximum:
        fail("Required GitHub or Git command returned an error or exceeded its output bound.")
    return result.stdout


def environment_context(environment):
    expected = {"GITHUB_ACTIONS": "true", "RUNNER_OS": "Linux", "GITHUB_REPOSITORY": REPOSITORY,
                "GITHUB_REF": "refs/heads/main", "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_WORKFLOW": "Release"}
    if any(environment.get(key) != value for key, value in expected.items()):
        fail("This helper requires the authenticated own-main Linux Release workflow context.")
    sha = environment.get("GITHUB_SHA", "")
    run_id = environment.get("GITHUB_RUN_ID", "")
    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    workflow_ref = environment.get("GITHUB_WORKFLOW_REF", "")
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not re.fullmatch(r"[1-9][0-9]{0,19}", run_id) \
            or not re.fullmatch(r"[1-9][0-9]{0,5}", attempt) \
            or workflow_ref != f"{REPOSITORY}/{RELEASE_PATH}@refs/heads/main":
        fail("The current release SHA, run, attempt, or workflow reference is invalid.")
    return {"sourceRevision": sha, "runId": run_id, "attempt": int(attempt)}


def parse_response(raw):
    try:
        header, body = raw.split(b"\r\n\r\n", 1)
        status = int(header.splitlines()[0].split()[1])
        headers = {}
        for line in header.decode("latin-1").splitlines()[1:]:
            if ":" in line:
                name, value = line.split(":", 1)
                headers[name.strip().casefold()] = value.strip()
        value = json.loads(body)
    except (ValueError, UnicodeError, json.JSONDecodeError):
        fail("GitHub API returned an invalid HTTP/JSON response.")
    if status != 200:
        fail(f"GitHub API read failed with HTTP {status}.")
    return headers, value


def api(route):
    if not route.startswith("repos/managedcode/KeyLoad/") or "\n" in route:
        fail("GitHub API route escaped the configured repository.")
    return parse_response(command(["gh", "api", "--include", route]))


def next_page_link(headers, route, page):
    links = headers.get("link", "")
    next_links = re.findall(r"<([^>]+)>\s*;\s*rel=\"([^\"]+)\"", links)
    targets = [target for target, relation in next_links if relation == "next"]
    if not targets:
        return False
    target = targets[0]
    parsed = urlparse(target)
    route_path = route.split("?", 1)[0]
    relative = route_path.removeprefix("repos/managedcode/KeyLoad/")
    allowed_paths = {"/repos/managedcode/KeyLoad/" + relative}
    repository_path = re.fullmatch(r"/repositories/[1-9][0-9]*/(.+)", parsed.path)
    if repository_path and REPOSITORY_ID is not None and int(parsed.path.split("/")[2]) == REPOSITORY_ID:
        allowed_paths.add("/repositories/" + str(REPOSITORY_ID) + "/" + relative)
    query = parse_qs(parsed.query)
    if parsed.scheme != "https" or parsed.netloc != "api.github.com" or parsed.path not in allowed_paths \
            or query.get("page") != [str(page + 1)] or query.get("per_page") != ["100"]:
        fail("GitHub API pagination link is unexpected or incomplete.")
    return True


def paginated_items(route, key):
    items = []
    for page in range(1, MAX_PAGES + 1):
        separator = "&" if "?" in route else "?"
        headers, payload = api(f"{route}{separator}per_page=100&page={page}")
        current = payload.get(key) if isinstance(payload, dict) else None
        if not isinstance(current, list) or len(current) > 100 or len(items) + len(current) > MAX_ITEMS:
            fail("GitHub API returned an invalid or over-limit paginated inventory.")
        items.extend(current)
        has_next = next_page_link(headers, route, page)
        if not has_next:
            return items
        if not current or page == MAX_PAGES or len(items) >= MAX_ITEMS:
            fail("GitHub API pagination is incomplete or exceeds the inventory limit.")
    fail("GitHub API pagination exceeded its page bound.")


def utc_datetime(value):
    if not isinstance(value, str) or not value.endswith("Z"):
        fail("GitHub run metadata lacks a canonical UTC created_at timestamp.")
    try:
        return datetime.datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError:
        fail("GitHub run metadata contains an invalid UTC timestamp.")


def workflow_metadata(workflow_id, expected_path, expected_name):
    _, workflow = api(f"repos/{REPOSITORY}/actions/workflows/{workflow_id}")
    if not isinstance(workflow, dict) or workflow.get("id") != workflow_id \
            or workflow.get("path") != expected_path or workflow.get("name") != expected_name \
            or workflow.get("state") != "active":
        fail("GitHub workflow metadata does not match the expected active workflow.")
    return workflow


def find_workflow(expected_path, expected_name):
    workflows = paginated_items(f"repos/{REPOSITORY}/actions/workflows", "workflows")
    matches = [item for item in workflows if isinstance(item, dict) and item.get("path") == expected_path]
    if len(matches) != 1 or not isinstance(matches[0].get("id"), int):
        fail("The repository does not have exactly one workflow at the expected path.")
    workflow_metadata(matches[0]["id"], expected_path, expected_name)
    return matches[0]["id"]


def current_release_run(context):
    global REPOSITORY_ID
    _, run = api(f"repos/{REPOSITORY}/actions/runs/{context['runId']}")
    workflow_id = run.get("workflow_id") if isinstance(run, dict) else None
    workflow = workflow_metadata(workflow_id, RELEASE_PATH, "Release") if isinstance(workflow_id, int) else None
    expected = {"id": int(context["runId"]), "run_attempt": context["attempt"], "head_sha": context["sourceRevision"],
                "head_branch": "main", "event": "workflow_dispatch", "name": "Release", "path": RELEASE_PATH}
    if workflow is None or any(run.get(key) != value for key, value in expected.items()) \
            or run.get("repository", {}).get("full_name") != REPOSITORY:
        fail("Current GitHub Release run metadata does not match the authenticated runner context.")
    repository_id = run["repository"].get("id")
    if not isinstance(repository_id, int) or repository_id < 1:
        fail("Current GitHub Release run lacks an authenticated repository id.")
    REPOSITORY_ID = repository_id
    if not isinstance(run.get("run_number"), int) or run["run_number"] < 1:
        fail("Current GitHub Release run number is invalid.")
    utc_datetime(run.get("created_at"))
    return workflow_id, run


def canonical_source_version():
    try:
        root = ElementTree.parse("Directory.Build.props").getroot()
    except (OSError, ElementTree.ParseError):
        fail("Central release version properties are unreadable.")
    values = {}
    for name in ("KeyLoadReleaseMajor", "KeyLoadReleaseMinor"):
        matches = [element.text or "" for element in root.iter() if element.tag.rsplit("}", 1)[-1] == name]
        if len(matches) != 1 or not re.fullmatch(r"0|[1-9][0-9]*", matches[0].strip()):
            fail("Central source release major/minor properties are missing or ambiguous.")
        values[name] = int(matches[0].strip())
        if values[name] > 65534:
            fail("Central source release version exceeds supported CLR metadata bounds.")
    return f"{values['KeyLoadReleaseMajor']}.{values['KeyLoadReleaseMinor']}.0-dev"


def existing_tags():
    raw = command(["git", "tag", "--list"], timeout=20, maximum=2 * 1024 * 1024)
    try:
        tags = raw.decode("utf-8").splitlines()
    except UnicodeError:
        fail("Local fetched Git tags are not valid UTF-8.")
    if len(tags) > MAX_ITEMS or any(not tag or len(tag) > 128 for tag in tags) or len(tags) != len(set(tags)):
        fail("Local fetched Git tag inventory is invalid or exceeds its bound.")
    return tags


def read_reservation(path):
    if path is None:
        return None
    target = Path(path)
    try:
        info = target.lstat()
        if not target.is_file() or target.is_symlink() or info.st_size > 16 * 1024:
            fail("Existing release reservation is linked, missing, or oversized.")
        return json.loads(target.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        fail("Existing release reservation cannot be read as JSON.")


def version_input(context, run, workflow_id, reservation_path):
    route = f"repos/{REPOSITORY}/actions/workflows/{workflow_id}/runs?event=workflow_dispatch&branch=main"
    all_runs = paginated_items(route, "workflow_runs")
    target_date = utc_datetime(run["created_at"]).date()
    daily = []
    current_matches = []
    for item in all_runs:
        if not isinstance(item, dict) or item.get("event") != "workflow_dispatch" or item.get("head_branch") != "main":
            fail("Release run inventory contains an unexpected workflow event or branch.")
        if item.get("repository", {}).get("full_name") != REPOSITORY:
            fail("Release run inventory contains a foreign repository run.")
        created = utc_datetime(item.get("created_at"))
        if created.date() == target_date:
            if not isinstance(item.get("id"), int) or not isinstance(item.get("run_number"), int):
                fail("Daily release run inventory lacks numeric identity fields.")
            daily.append({"id": str(item["id"]), "run_number": item["run_number"], "created_at": item["created_at"]})
            if str(item["id"]) == context["runId"]:
                current_matches.append(item)
    if len(daily) > MAX_ITEMS:
        fail("Daily release run inventory exceeds its bound.")
    if len(current_matches) != 1 or current_matches[0].get("run_number") != run.get("run_number") \
            or current_matches[0].get("created_at") != run.get("created_at"):
        fail("Current release run disagrees with the complete daily run inventory.")
    value = {"baseVersion": canonical_source_version(), "sourceRevision": context["sourceRevision"],
             "runId": context["runId"], "utcTimestamp": run["created_at"], "tags": existing_tags(), "dailyRuns": daily}
    reservation = read_reservation(reservation_path)
    if reservation is not None:
        value["reservation"] = reservation
    return value


def validate_ci_run(run, workflow_id, source_revision):
    expected = {"workflow_id": workflow_id, "path": CI_PATH, "name": "CI", "head_branch": "main",
                "head_sha": source_revision}
    if any(run.get(key) != value for key, value in expected.items()) \
            or run.get("repository", {}).get("full_name") != REPOSITORY \
            or run.get("event") not in ("push", "workflow_dispatch"):
        return False
    if any(not isinstance(run.get(key), int) or isinstance(run.get(key), bool) or run[key] < 1
           for key in ("id", "run_number", "run_attempt")):
        return False
    return True


def ci_jobs(run):
    attempt = run.get("run_attempt")
    if not isinstance(attempt, int) or attempt < 1:
        fail("Exact-source CI run has no valid current attempt.")
    route = f"repos/{REPOSITORY}/actions/runs/{run['id']}/attempts/{attempt}/jobs"
    jobs = paginated_items(route, "jobs")
    names = set()
    mandatory_names = set()
    records = []
    for job in jobs:
        if not isinstance(job, dict) or job.get("status") != "completed" or job.get("conclusion") != "success":
            fail("Exact-source CI attempt contains an incomplete, skipped, or failed job.")
        name = job.get("name")
        if not isinstance(name, str) or not name or name in names or not isinstance(job.get("id"), int):
            fail("Exact-source CI job inventory has invalid or duplicate identities.")
        names.add(name)
        normalized = "Build and tests" if name in ("Build and tests", "Build and tests (ubuntu-latest)") else name
        if normalized in REQUIRED_CI_JOBS:
            if normalized in mandatory_names:
                fail("Exact-source CI contains duplicate mandatory job identities.")
            mandatory_names.add(normalized)
        html_url = job.get("html_url")
        if not isinstance(html_url, str) or not html_url.startswith(f"https://github.com/{REPOSITORY}/actions/runs/{run['id']}/job/"):
            fail("Exact-source CI job URL is missing or foreign.")
        records.append({"id": job["id"], "name": name, "url": html_url, "status": job["status"], "conclusion": job["conclusion"]})
    if mandatory_names != REQUIRED_CI_JOBS:
        fail("Exact-source CI attempt lacks a mandatory build, analyzer, repository, or RF3 job.")
    return sorted(records, key=lambda item: item["name"])


def ci_receipt(context):
    workflow_id = find_workflow(CI_PATH, "CI")
    runs_route = f"repos/{REPOSITORY}/actions/workflows/{workflow_id}/runs?branch=main&head_sha={context['sourceRevision']}"
    runs = paginated_items(runs_route, "workflow_runs")
    candidates = [run for run in runs if isinstance(run, dict) and validate_ci_run(run, workflow_id, context["sourceRevision"])]
    if not candidates:
        fail("No authenticated main CI run exists for the exact release source SHA.")
    selected = max(candidates, key=lambda item: (item.get("run_number", 0), item.get("id", 0)))
    if selected.get("status") != "completed" or selected.get("conclusion") != "success":
        fail("The latest exact-source main CI run must finish successfully before release publication.")
    jobs = ci_jobs(selected)
    html_url = selected.get("html_url")
    if not isinstance(html_url, str) or html_url != f"https://github.com/{REPOSITORY}/actions/runs/{selected['id']}":
        fail("Exact-source CI run URL is missing or foreign.")
    return {"schemaVersion": 1, "repository": REPOSITORY, "workflowId": workflow_id, "workflowPath": CI_PATH,
            "sourceRevision": context["sourceRevision"], "runId": str(selected["id"]),
            "runNumber": selected["run_number"], "attempt": selected["run_attempt"], "event": selected["event"],
            "ref": "refs/heads/main", "status": selected["status"], "conclusion": selected["conclusion"],
            "runUrl": html_url, "jobs": jobs}


def write_immutable(path, value):
    target = Path(path)
    content = (json.dumps(value, sort_keys=True, indent=2) + "\n").encode("utf-8")
    try:
        with target.open("xb") as stream:
            stream.write(content)
    except FileExistsError:
        try:
            info = target.lstat()
            if target.is_symlink() or not target.is_file() or info.st_size != len(content):
                fail("Refusing to overwrite a different or linked GitHub context receipt.")
            existing = target.read_bytes()
            if existing != content:
                fail("Refusing to overwrite a different or linked GitHub context receipt.")
        except OSError:
            fail("Existing GitHub context receipt cannot be verified.")


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("version", "ci"))
    parser.add_argument("--output", required=True)
    parser.add_argument("--reservation")
    return parser.parse_args()


def main():
    args = arguments()
    if args.mode == "ci" and args.reservation is not None:
        fail("The ci mode does not accept a reservation file.")
    context = environment_context(os.environ)
    workflow_id, run = current_release_run(context)
    result = version_input(context, run, workflow_id, args.reservation) if args.mode == "version" else ci_receipt(context)
    write_immutable(args.output, result)
    print(json.dumps({"mode": args.mode, "output": args.output, "sourceRevision": context["sourceRevision"]}, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (ContextError, OSError, ValueError, KeyError, TypeError) as error:
        print(f"E_RELEASE_GITHUB_CONTEXT: {error}", file=sys.stderr)
        raise SystemExit(1)
