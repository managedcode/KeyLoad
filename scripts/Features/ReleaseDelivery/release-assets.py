#!/usr/bin/env python3
"""Verify source-bound KeyLoad release assets using only the Python standard library."""

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import tarfile
import zipfile
from pathlib import Path, PurePosixPath
from xml.etree import ElementTree

REPOSITORY = "managedcode/KeyLoad"
SOURCE_LABEL = "org.opencontainers.image.revision"
VERSION_LABEL = "org.opencontainers.image.version"
MAX_JSON_BYTES = 4 * 1024 * 1024
MAX_PACKAGE_BYTES = 2 * 1024 * 1024 * 1024
MAX_PACKAGE_CONTENT_BYTES = 4 * 1024 * 1024 * 1024
MAX_DATABASE_BYTES = 8 * 1024 * 1024 * 1024
MAX_IMAGE_BYTES = 16 * 1024 * 1024 * 1024
MAX_TOTAL_BYTES = 48 * 1024 * 1024 * 1024
MAX_TAR_CONTENT_BYTES = 32 * 1024 * 1024 * 1024
MAX_TAR_ENTRIES = 100_000
MAX_TAR_MEMBER_BYTES = 16 * 1024 * 1024
MAX_PACKAGE_COUNT = 100
MAX_ASSET_COUNT = 128
MANIFEST_NAME = "release-manifest.json"
CHECKSUMS_NAME = "SHA256SUMS"


class ReleaseAssetError(Exception):
    """Raised when a release asset is incomplete or bound to another source."""


def fail(message):
    raise ReleaseAssetError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            fail("Release JSON contains a duplicate object key.")
        result[key] = value
    return result


def is_plain_object(value):
    return isinstance(value, dict)


def regular_file(path, maximum):
    try:
        info = path.lstat()
    except OSError as error:
        fail(f"Required file is unavailable: {path.name} ({error.strerror}).")
    if not stat.S_ISREG(info.st_mode) or info.st_size <= 0 or info.st_size > maximum:
        fail(f"File is linked, empty, or exceeds its size limit: {path.name}.")
    return info.st_size


def read_json(path, maximum):
    regular_file(path, maximum)
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeError, json.JSONDecodeError):
        fail(f"JSON input is invalid: {path.name}.")


def validate_reservation(value):
    keys = {"schemaVersion", "repository", "sourceRevision", "runId", "baseVersion", "major", "minor", "date",
            "sequence", "version", "packageVersion", "tag", "assemblyVersion", "fileVersion"}
    if not is_plain_object(value) or set(value) != keys or type(value["schemaVersion"]) is not int \
            or value["schemaVersion"] != 2 or value["repository"] != REPOSITORY:
        fail("Release reservation has an unsupported shape or repository.")
    if not isinstance(value["sourceRevision"], str) or not re.fullmatch(r"[a-f0-9]{40}", value["sourceRevision"]):
        fail("Release reservation source revision is invalid.")
    if not isinstance(value["runId"], str) or not re.fullmatch(r"[1-9][0-9]{0,19}", value["runId"]):
        fail("Release reservation run id is invalid.")
    if not isinstance(value["baseVersion"], str) or not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.0-dev", value["baseVersion"]):
        fail("Release reservation base version is invalid.")
    match = re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.0-dev", value["baseVersion"])
    major, minor = int(match.group(1)), int(match.group(2))
    sequence = value["sequence"]
    if not (type(value["major"]) is int and type(value["minor"]) is int and value["major"] == major
            and value["minor"] == minor and 0 <= major <= 65534 and 0 <= minor <= 65534 and isinstance(sequence, int)
            and not isinstance(sequence, bool) and 1 <= sequence <= 65534):
        fail("Release reservation contains an out-of-range version component.")
    try:
        import datetime
        parsed = datetime.datetime.strptime(value["date"], "%y%m%d")
    except (TypeError, ValueError):
        fail("Release reservation UTC date is invalid.")
    if parsed.strftime("%y%m%d") != value["date"]:
        fail("Release reservation UTC date is not canonical.")
    version = f"{major}.{minor}.{value['date']}.{sequence}"
    expected = {"major": major, "minor": minor, "version": version, "packageVersion": f"{version}-dev", "tag": f"v{version}",
                "assemblyVersion": f"{major}.{minor}.0.0", "fileVersion": f"{major}.{minor}.0.{sequence}"}
    if any(value[key] != expectedValue for key, expectedValue in expected.items()):
        fail("Release reservation derived version fields do not agree.")
    return value


def safe_member_name(name):
    if not isinstance(name, str) or "\\" in name:
        return False
    value = PurePosixPath(name)
    return not value.is_absolute() and all(part not in ("", ".", "..") for part in value.parts)


def sha256_file(path, maximum):
    size = regular_file(path, maximum)
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return {"file": path.name, "bytes": size, "sha256": digest.hexdigest()}


def read_package(path, expected_version):
    package_size = regular_file(path, MAX_PACKAGE_BYTES)
    try:
        with zipfile.ZipFile(path) as package:
            infos = package.infolist()
            if not infos or len(infos) > 100_000 or sum(item.file_size for item in infos) > MAX_PACKAGE_CONTENT_BYTES:
                fail(f"Package archive exceeds its bounded entry/content limits: {path.name}.")
            for item in infos:
                mode = item.external_attr >> 16
                if not safe_member_name(item.filename) or stat.S_ISLNK(mode):
                    fail(f"Package archive contains an unsafe path: {path.name}.")
            if package.testzip() is not None:
                fail(f"Package archive has a CRC failure: {path.name}.")
            nuspecs = [item for item in infos if "/" not in item.filename and item.filename.endswith(".nuspec")]
            if len(nuspecs) != 1 or nuspecs[0].file_size > 1024 * 1024:
                fail(f"Package must contain exactly one bounded root nuspec: {path.name}.")
            root = ElementTree.fromstring(package.read(nuspecs[0]))
    except (OSError, zipfile.BadZipFile, ElementTree.ParseError):
        fail(f"Package archive is unreadable: {path.name}.")
    metadata = next((element for element in root if element.tag.rsplit("}", 1)[-1] == "metadata"), None)
    if metadata is None:
        fail(f"Package nuspec lacks metadata: {path.name}.")
    values = {element.tag.rsplit("}", 1)[-1]: (element.text or "").strip() for element in metadata}
    package_id = values.get("id", "")
    version = values.get("version", "")
    if not re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", package_id) or version != expected_version:
        fail(f"Package id or embedded version is invalid: {path.name}.")
    return {"id": package_id, "version": version, "file": path.name, "bytes": package_size,
            "sha256": sha256_file(path, MAX_PACKAGE_BYTES)["sha256"]}


def checked_tar(path, maximum):
    size = regular_file(path, maximum)
    names = set()
    total = 0
    count = 0
    try:
        with tarfile.open(path, mode="r:gz") as archive:
            for member in archive:
                count += 1
                if count > MAX_TAR_ENTRIES or not safe_member_name(member.name) or member.name in names:
                    fail(f"Tar archive has too many entries or an unsafe/duplicate path: {path.name}.")
                names.add(member.name)
                if not (member.isfile() or member.isdir()):
                    fail(f"Tar archive contains a link or special file: {path.name}.")
                total += member.size
                if total > MAX_TAR_CONTENT_BYTES:
                    fail(f"Tar archive expands beyond its size limit: {path.name}.")
    except (OSError, tarfile.TarError):
        fail(f"Tar archive is unreadable: {path.name}.")
    if not names:
        fail(f"Tar archive contains no files: {path.name}.")
    return {"file": path.name, "bytes": size, "sha256": sha256_file(path, maximum)["sha256"], "entries": count}, names


def verify_database_distribution(path, reservation):
    version = reservation["version"]
    expected = f"keyload-database-{version}-linux-x64.tar.gz"
    if path.name != expected:
        fail("Database distribution filename does not match the reserved release version.")
    record, names = checked_tar(path, MAX_DATABASE_BYTES)
    required = {"server/KeyLoad.Server", "server/KeyLoad.Server.dll", "server/KeyLoad.Server.runtimeconfig.json",
                "server/KeyLoad.Server.deps.json", "cli/KeyLoad.Cli", "cli/KeyLoad.Cli.dll", "compose.yml",
                "initialize.sh", "README.md", ".env.example", "release-version.json"}
    if not required.issubset(names) or "server" not in names or "cli" not in names:
        fail("Database distribution lacks the server, CLI, or RF3 deployment payload.")
    with tarfile.open(path, mode="r:gz") as archive:
        if not archive.getmember("server").isdir() or not archive.getmember("cli").isdir():
            fail("Database distribution server and CLI roots must be explicit directories.")
        if any(not archive.getmember(name).isfile() for name in required - {"server", "cli"}):
            fail("Database distribution is missing a required regular file.")
        member = archive.getmember("release-version.json")
        if not member.isfile() or member.size > MAX_JSON_BYTES:
            fail("Database distribution reservation is missing or oversized.")
        stream = archive.extractfile(member)
        payload = stream.read(MAX_JSON_BYTES + 1) if stream else b""
    try:
        embedded = json.loads(payload.decode("utf-8"), object_pairs_hook=unique_object)
    except (UnicodeError, json.JSONDecodeError):
        fail("Database distribution reservation is malformed JSON.")
    if validate_reservation(embedded) != reservation:
        fail("Database distribution reservation does not match the release identity.")
    return record


def parse_docker_manifest(archive, manifest_member, reference):
    member = archive.extractfile(manifest_member)
    if member is None:
        fail("Docker image archive lacks manifest.json.")
    raw = member.read(4 * 1024 * 1024 + 1)
    if len(raw) > 4 * 1024 * 1024:
        fail("Docker image manifest exceeds its size limit.")
    try:
        manifest = json.loads(raw)
    except (UnicodeError, json.JSONDecodeError):
        fail("Docker image manifest is invalid JSON.")
    if not isinstance(manifest, list) or len(manifest) != 1 or not isinstance(manifest[0], dict):
        fail("Docker image archive must contain one image manifest.")
    item = manifest[0]
    config = item.get("Config")
    if not safe_member_name(config) or reference not in item.get("RepoTags", []):
        fail("Docker image manifest does not match its versioned reference.")
    return config


def inspect_docker_archive(path, image, expected_version, source_revision):
    record, _ = checked_tar(path, MAX_IMAGE_BYTES)
    image_id = image["id"]
    if not re.fullmatch(r"sha256:[a-f0-9]{64}", image_id):
        fail("Docker inspect image id is invalid.")
    config_bytes = None
    try:
        with tarfile.open(path, mode="r:gz") as archive:
            manifest_member = archive.getmember("manifest.json")
            if not manifest_member.isfile() or manifest_member.size > 4 * 1024 * 1024:
                fail("Docker image manifest is missing or oversized.")
            config_path = parse_docker_manifest(archive, manifest_member, image["reference"])
            config_member = archive.getmember(config_path)
            if not config_member.isfile() or config_member.size > MAX_TAR_MEMBER_BYTES:
                fail("Docker image configuration is missing or oversized.")
            source = archive.extractfile(config_member)
            config_bytes = source.read(MAX_TAR_MEMBER_BYTES + 1) if source else None
    except (OSError, tarfile.TarError):
        fail(f"Docker image export is unreadable: {path.name}.")
    if config_bytes is None or len(config_bytes) > MAX_TAR_MEMBER_BYTES \
            or "sha256:" + hashlib.sha256(config_bytes).hexdigest() != image_id:
        fail("Docker image export configuration does not match the inspected image id.")
    try:
        config = json.loads(config_bytes)
    except (UnicodeError, json.JSONDecodeError):
        fail("Docker image configuration is invalid JSON.")
    archived_labels = (config.get("config") or {}).get("Labels") or {}
    labels = image["labels"]
    if not is_plain_object(labels) or labels != archived_labels:
        fail("Docker inspect labels do not match the exported image configuration.")
    if labels.get(SOURCE_LABEL) != source_revision or labels.get(VERSION_LABEL) != expected_version:
        fail("Docker image labels do not match the release source/version.")
    return {**record, "role": image["role"], "reference": image["reference"], "id": image_id,
            "labels": labels}


def validate_image_inputs(images, asset_root, version, source_revision):
    if not isinstance(images, list) or len(images) != 2:
        fail("Exactly the server and benchmarks image exports are required.")
    expected_names = {"server": f"keyload-server-{version}-linux-amd64.docker.tar.gz",
                      "benchmarks": f"keyload-benchmarks-{version}-linux-amd64.docker.tar.gz"}
    roles = set()
    records = []
    for image in images:
        keys = {"role", "reference", "id", "labels", "archive"}
        if not is_plain_object(image) or set(image) != keys or not isinstance(image["role"], str) \
                or image["role"] not in expected_names or image["role"] in roles:
            fail("Docker image metadata has a duplicate role or invalid shape.")
        roles.add(image["role"])
        repository = "keyload-server" if image["role"] == "server" else "keyload-benchmarks"
        reference_pattern = r"ghcr\.io/managedcode/" + repository + r":(?:" + re.escape(version) + r"|v" + re.escape(version) + r")"
        if not isinstance(image["reference"], str) or not re.fullmatch(reference_pattern, image["reference"]):
            fail("Docker image reference is not version tagged.")
        if not is_plain_object(image["labels"]) or any(not isinstance(key, str) or not isinstance(value, str)
                                                        for key, value in image["labels"].items()):
            fail("Docker inspect labels must be string key/value metadata.")
        archive = image["archive"]
        if not isinstance(archive, str) or archive != expected_names[image["role"]]:
            fail("Docker image archive filename does not match its role/version.")
        records.append(inspect_docker_archive(asset_root / archive, image, version, source_revision))
    if roles != set(expected_names):
        fail("Both required Docker image roles must be supplied.")
    return sorted(records, key=lambda item: item["role"])


def validate_asset_directory(asset_root, expected_inputs):
    actual = set()
    for item in asset_root.iterdir():
        info = item.lstat()
        if not stat.S_ISREG(info.st_mode):
            fail(f"Release asset directory contains a linked or non-file entry: {item.name}.")
        actual.add(item.name)
    allowed = expected_inputs | {MANIFEST_NAME, CHECKSUMS_NAME}
    if not expected_inputs.issubset(actual) or actual - allowed:
        fail("Release asset directory is incomplete or contains unverified files.")


def inventory_asset_directory(asset_root):
    items = list(asset_root.iterdir())
    if len(items) > MAX_ASSET_COUNT:
        fail("Release asset directory exceeds its entry limit.")
    total = 0
    for item in items:
        try:
            info = item.lstat()
        except OSError:
            fail(f"Release asset cannot be inspected: {item.name}.")
        if not stat.S_ISREG(info.st_mode) or info.st_size <= 0:
            fail(f"Release asset is linked, non-file, or empty: {item.name}.")
        total += info.st_size
        if total > MAX_TOTAL_BYTES:
            fail("Combined release asset inputs exceed their size limit.")
    return items


def write_immutable(path, content):
    data = content.encode("utf-8")
    try:
        with path.open("xb") as stream:
            stream.write(data)
    except FileExistsError:
        if regular_file(path, MAX_JSON_BYTES) != len(data) or path.read_bytes() != data:
            fail(f"Refusing to overwrite different release metadata: {path.name}.")


def build_manifest(reservation, packages, database, images):
    return {"schemaVersion": 2, "repository": REPOSITORY, "sourceRevision": reservation["sourceRevision"],
            "runId": reservation["runId"], "baseVersion": reservation["baseVersion"],
            "version": reservation["version"], "packageVersion": reservation["packageVersion"], "tag": reservation["tag"],
            "assemblyVersion": reservation["assemblyVersion"], "fileVersion": reservation["fileVersion"],
            "packages": packages, "database": database, "images": images}


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reservation", required=True)
    parser.add_argument("--assets", required=True)
    parser.add_argument("--images", required=True, help="JSON array of role/reference/id/labels/archive metadata")
    return parser.parse_args()


def main():
    args = parse_args()
    reservation = validate_reservation(read_json(Path(args.reservation), MAX_JSON_BYTES))
    image_inputs = read_json(Path(args.images), MAX_JSON_BYTES)
    asset_root = Path(args.assets)
    if asset_root.is_symlink() or not asset_root.is_dir():
        fail("Release asset directory must be a real directory.")
    version = reservation["version"]
    items = inventory_asset_directory(asset_root)
    package_paths = sorted((item for item in items if item.name.endswith(".nupkg")), key=lambda item: item.name)
    if not package_paths or len(package_paths) > MAX_PACKAGE_COUNT:
        fail("Release must contain a bounded nonempty set of NuGet packages.")
    database_path = asset_root / f"keyload-database-{version}-linux-x64.tar.gz"
    expected = {item.name for item in package_paths} | {database_path.name,
                f"keyload-server-{version}-linux-amd64.docker.tar.gz",
                f"keyload-benchmarks-{version}-linux-amd64.docker.tar.gz"}
    validate_asset_directory(asset_root, expected)
    packages = [read_package(item, reservation["packageVersion"]) for item in package_paths]
    if len({item["id"].casefold() for item in packages}) != len(packages):
        fail("NuGet package ids must be unique within the release.")
    database = verify_database_distribution(database_path, reservation)
    images = validate_image_inputs(image_inputs, asset_root, version, reservation["sourceRevision"])
    manifest = build_manifest(reservation, packages, database, images)
    manifest_text = json.dumps(manifest, sort_keys=True, indent=2) + "\n"
    write_immutable(asset_root / MANIFEST_NAME, manifest_text)
    payload_names = sorted(expected | {MANIFEST_NAME})
    checksum_text = "".join(f"{sha256_file(asset_root / name, MAX_IMAGE_BYTES if name.endswith('.docker.tar.gz') else MAX_DATABASE_BYTES if name.endswith('.tar.gz') else MAX_PACKAGE_BYTES)['sha256']}  {name}\n"
                            for name in payload_names)
    write_immutable(asset_root / CHECKSUMS_NAME, checksum_text)
    print(json.dumps({"manifest": MANIFEST_NAME, "checksums": CHECKSUMS_NAME,
                      "packageCount": len(packages), "imageCount": len(images), "version": version}, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (ReleaseAssetError, OSError, ValueError, KeyError, TypeError) as error:
        print(f"E_RELEASE_ASSET: {error}", file=sys.stderr)
        raise SystemExit(1)
