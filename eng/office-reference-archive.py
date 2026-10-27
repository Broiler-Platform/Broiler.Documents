#!/usr/bin/env python3
"""Publish immutable reference releases and fetch/verify the pinned archive.

Only the small reference-source.json belongs in Git. Text/PDF outputs live in
release assets, avoiding binary history and the repository's fixture guards.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import tarfile
import urllib.error
import urllib.parse
import urllib.request


def configuration(path):
    value = json.loads(Path(path).read_text(encoding="utf-8"))
    if value.get("schemaVersion") != 1:
        raise ValueError("Unsupported reference configuration schema")
    for key in ("releaseTag", "asset"):
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", value.get(key, "")):
            raise ValueError("Invalid reference " + key)
    if not value["asset"].endswith(".tar.gz"):
        raise ValueError("Reference asset must be a .tar.gz archive")
    if not re.fullmatch(r"ghcr\.io/broiler-platform/broiler-sdk-office@sha256:[0-9a-f]{64}", value.get("image", "")):
        raise ValueError("Conformance image must be pinned by digest")
    if not re.fullmatch(r"[0-9a-f]{40}", value.get("corpusRevision", "")):
        raise ValueError("Reference corpusRevision must be a full Git commit SHA")
    return value


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def package(directory, archive):
    directory, archive = Path(directory), Path(archive)
    if archive.resolve().is_relative_to(directory.resolve()):
        raise ValueError("Archive must be outside the reference directory")
    if not (directory / "manifest.json").is_file():
        raise ValueError("Reference generation is incomplete: no manifest.json")
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    if not isinstance(manifest.get("Environment"), dict) or not manifest.get("Samples"):
        raise ValueError("Reference generation is incomplete: invalid manifest")
    archive.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, "w:gz") as output:
        for path in sorted(directory.rglob("*")):
            if path.is_symlink() or not (path.is_file() or path.is_dir()):
                raise ValueError("Unexpected reference directory entry: " + path.name)
            output.add(path, arcname=path.relative_to(directory).as_posix(), recursive=False)
    Path(str(archive) + ".sha256").write_text(sha256(archive) + "  " + archive.name + "\n", encoding="ascii")


def extract(archive, destination):
    destination = Path(destination)
    if destination.exists() and any(destination.iterdir()):
        raise ValueError("Reference destination must be empty")
    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, "r:gz") as source:
        seen = set()
        for member in source:
            # Corpus/font snapshots use subdirectories. Reject links, devices,
            # duplicate paths and traversal before writing outside the root.
            name = PurePosixPath(member.name)
            if (not (member.isfile() or member.isdir()) or ".." in name.parts or
                    member.name in (".", "..", "") or "\\" in member.name or
                    ":" in member.name or name.is_absolute() or member.name in seen):
                raise ValueError("Invalid reference archive member: " + member.name)
            seen.add(member.name)
            target = destination.joinpath(*name.parts)
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.extractfile(member) as incoming, target.open("xb") as outgoing:
                shutil.copyfileobj(incoming, outgoing)
    if not (destination / "manifest.json").is_file():
        raise ValueError("Archive has no reference manifest")


def download(config, destination):
    server = os.environ.get("GITHUB_SERVER_URL", "https://github.com")
    repository = os.environ["GITHUB_REPOSITORY"]
    url = f"{server}/{repository}/releases/download/{config['releaseTag']}/{config['asset']}"
    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    archive = destination.parent / config["asset"]
    # This repository publishes public releases. No credentials are sent to
    # release/CDN URLs; redirects cannot forward a repository token.
    try:
        with urllib.request.urlopen(url + ".sha256", timeout=120) as response:
            expected = response.read(512).decode("ascii").split()[0]
        if not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise ValueError("Invalid archive SHA-256")
        with urllib.request.urlopen(url, timeout=120) as response, archive.open("wb") as output:
            shutil.copyfileobj(response, output)
    except urllib.error.HTTPError as error:
        raise ValueError(f"Pinned references {config['releaseTag']} unavailable (HTTP {error.code}). "
                         "Run the Office reference data workflow first; no live fallback is permitted.") from error
    if sha256(archive) != expected:
        raise ValueError("Reference archive SHA-256 mismatch")
    extract(archive, destination)
    print(f"Downloaded and verified {config['releaseTag']}: {expected}")


def api(method, url, data=None, headers=None):
    request_headers = {
        "Authorization": "Bearer " + os.environ["GH_TOKEN"],
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "Broiler-Office-References",
    }
    if headers:
        request_headers.update(headers)
    if isinstance(data, dict):
        data = json.dumps(data).encode("utf-8")
        request_headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=request_headers, method=method)
    with urllib.request.urlopen(request, timeout=600) as response:
        return json.load(response)


def require_new_release(config):
    repository = os.environ["GITHUB_REPOSITORY"]
    base = os.environ.get("GITHUB_API_URL", "https://api.github.com") + f"/repos/{repository}"
    try:
        api("GET", base + "/releases/tags/" + config["releaseTag"])
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
    else:
        raise ValueError("Reference release already exists. Choose a new releaseTag; releases are never overwritten.")


def publish(config, archive):
    archive = Path(archive)
    require_new_release(config)
    repository = os.environ["GITHUB_REPOSITORY"]
    base = os.environ.get("GITHUB_API_URL", "https://api.github.com") + f"/repos/{repository}"
    # Assets upload to a draft first, so consumers cannot discover a partial set.
    release = api("POST", base + "/releases", {
        "tag_name": config["releaseTag"], "target_commitish": os.environ["GITHUB_SHA"],
        "name": config["releaseTag"], "draft": True,
        "body": "LibreOffice text/PDF reference data. See manifest.json for input hashes, "
                "generation outcomes and toolchain identity. Generated by " +
                os.environ.get("GITHUB_SERVER_URL", "https://github.com") + "/" + repository +
                "/actions/runs/" + os.environ["GITHUB_RUN_ID"],
    })
    upload = release["upload_url"].split("{")[0]
    for path, name in ((archive, config["asset"]), (Path(str(archive) + ".sha256"), config["asset"] + ".sha256")):
        with path.open("rb") as stream:
            api("POST", upload + "?name=" + urllib.parse.quote(name), stream,
                {"Content-Type": "application/octet-stream", "Content-Length": str(path.stat().st_size)})
    published = api("PATCH", base + "/releases/" + str(release["id"]), {"draft": False, "make_latest": "false"})
    print("Published references: " + published["html_url"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("config", "package", "download", "check-release", "publish"))
    parser.add_argument("--config", default="tests/office/reference-source.json")
    parser.add_argument("--directory")
    parser.add_argument("--archive")
    args = parser.parse_args()
    config = configuration(args.config)
    if args.command == "config":
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            for key in ("image", "releaseTag", "asset", "corpusRevision"):
                output.write(key + "=" + config[key] + "\n")
    elif args.command == "package":
        package(args.directory, args.archive)
    elif args.command == "download":
        download(config, args.directory)
    elif args.command == "check-release":
        require_new_release(config)
    else:
        publish(config, args.archive)


if __name__ == "__main__":
    main()
