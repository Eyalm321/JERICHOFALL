#!/usr/bin/env python3
"""Audit Git's index, including ZIP members, for unwanted executable, secret and archive content."""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
BLOCKED_ROOTS = {"analysis", "build", "private", ".tools", ".serena", ".venv"}
BLOCKED_SUFFIXES = {".dll", ".exe", ".pdb", ".so", ".rar", ".iso", ".zip", ".xgs", ".xsb", ".xwb", ".pem", ".key"}
SECRETS = [re.compile(rb"gh[pousr]_[A-Za-z0-9]{30,}"),
           re.compile(rb"github_pat_[A-Za-z0-9_]{40,}"),
           re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]
MAX_FILE = 95 * 1024 * 1024


def inspect_payload(name: str, data: bytes, excluded: set[str], depth: int = 0) -> list[str]:
    issues = []
    if hashlib.sha256(data).hexdigest() in excluded:
        issues.append(f"{name}: matches an excluded file identity")
    if data.startswith((b"MZ", b"\x7fELF")):
        issues.append(f"{name}: executable image is forbidden in this source repository")
    if any(pattern.search(data) for pattern in SECRETS):
        issues.append(f"{name}: possible credential/private key (value suppressed)")
    if data.startswith(b"PK\x03\x04"):
        if depth >= 3:
            return issues + [f"{name}: nested archive depth exceeded"]
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                members = archive.infolist()
                if sum(x.file_size for x in members) > MAX_FILE or len(members) > 4096:
                    return issues + [f"{name}: archive inspection limit exceeded"]
                for member in members:
                    path = PurePosixPath(member.filename)
                    if path.is_absolute() or ".." in path.parts or "\\" in member.filename:
                        issues.append(f"{name}: unsafe archive member")
                    elif not member.is_dir():
                        issues += inspect_payload(f"{name}!{member.filename}", archive.read(member), excluded, depth + 1)
        except (zipfile.BadZipFile, RuntimeError, NotImplementedError) as error:
            issues.append(f"{name}: cannot inspect archive: {type(error).__name__}")
    return issues


def inspect_path(name: str, mode: str) -> list[str]:
    path = PurePosixPath(name)
    parts = [x.lower() for x in path.parts]
    if mode not in {"100644", "100755"}:
        return [f"{name}: symlinks/submodules/unmerged entries are not allowed"]
    if (parts[0] in BLOCKED_ROOTS or any(x in {"bin", "obj", "__pycache__", "saves", "decompiled", "artifacts"} for x in parts)
            or path.suffix.lower() in BLOCKED_SUFFIXES or ".so." in path.name.lower()
            or (path.name.startswith(".env") and path.name != ".env.example")
            or path.name.lower().endswith((".tar.gz", ".tar.zst", ".tar"))):
        return [f"{name}: excluded dependency, reference, build, save or secret path"]
    if parts[0] == "assets" and any(x in {"preview", "previews", "review", "verification"} for x in parts):
        return [f"{name}: development captures are excluded"]
    return []


def check_index() -> dict:
    excluded: set[str] = set()
    entries = subprocess.check_output(["git", "ls-files", "--stage", "-z"], cwd=ROOT).split(b"\0")
    failures, total, count = [], 0, 0
    process = subprocess.Popen(["git", "cat-file", "--batch"], cwd=ROOT,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    assert process.stdin and process.stdout
    try:
        for entry in entries:
            if not entry:
                continue
            metadata, raw_name = entry.split(b"\t", 1)
            mode, object_id, stage = metadata.decode().split()
            name = raw_name.decode("utf-8")
            failures += inspect_path(name, mode)
            if stage != "0":
                failures.append(f"{name}: unresolved merge stage")
            process.stdin.write(object_id.encode() + b"\n")
            process.stdin.flush()
            header = process.stdout.readline().decode().strip().split()
            if len(header) != 3 or header[1] != "blob":
                raise ValueError(f"Expected Git blob for {name}")
            size = int(header[2])
            data = process.stdout.read(size)
            if len(data) != size or process.stdout.read(1) != b"\n":
                raise ValueError(f"Truncated Git blob for {name}")
            count += 1
            total += size
            if size > MAX_FILE:
                failures.append(f"{name}: oversized repository file")
            failures += inspect_payload(name, data, excluded)
    finally:
        process.stdin.close()
        process.wait()
    if not count:
        failures.append("No files in Git index")
    return dict(passed=not failures, files=count, bytes=total,
                failures=failures,
                limits="Checks archive members, executable magic and selected secret/path patterns. Asset ownership requires provenance review.")


if __name__ == "__main__":
    result = check_index()
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["passed"] else 1)
