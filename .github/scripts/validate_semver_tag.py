#!/usr/bin/env python3
"""Validate release tag ordering against existing SemVer git tags."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass


SEMVER_PATTERN = re.compile(
    r"^v?"
    r"(?P<major>0|[1-9]\d*)\."
    r"(?P<minor>0|[1-9]\d*)\."
    r"(?P<patch>0|[1-9]\d*)"
    r"(?:-(?P<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"$"
)


@dataclass(frozen=True)
class SemVer:
    major: int
    minor: int
    patch: int
    prerelease: tuple[str, ...]
    original: str


def parse_semver(tag: str) -> SemVer | None:
    match = SEMVER_PATTERN.match(tag)
    if match is None:
        return None

    prerelease = tuple((match.group("prerelease") or "").split("."))
    if prerelease == ("",):
        prerelease = ()

    for identifier in prerelease:
        if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
            return None

    return SemVer(
        major=int(match.group("major")),
        minor=int(match.group("minor")),
        patch=int(match.group("patch")),
        prerelease=prerelease,
        original=tag,
    )


def compare_identifiers(left: str, right: str) -> int:
    left_numeric = left.isdigit()
    right_numeric = right.isdigit()

    if left_numeric and right_numeric:
        return (int(left) > int(right)) - (int(left) < int(right))

    if left_numeric:
        return -1

    if right_numeric:
        return 1

    return (left > right) - (left < right)


def compare_semver(left: SemVer, right: SemVer) -> int:
    core_left = (left.major, left.minor, left.patch)
    core_right = (right.major, right.minor, right.patch)
    if core_left != core_right:
        return (core_left > core_right) - (core_left < core_right)

    if not left.prerelease and not right.prerelease:
        return 0

    if not left.prerelease:
        return 1

    if not right.prerelease:
        return -1

    for left_identifier, right_identifier in zip(left.prerelease, right.prerelease):
        identifier_result = compare_identifiers(left_identifier, right_identifier)
        if identifier_result != 0:
            return identifier_result

    return (len(left.prerelease) > len(right.prerelease)) - (
        len(left.prerelease) < len(right.prerelease)
    )


def list_git_tags() -> list[str]:
    result = subprocess.run(
        ["git", "tag", "--list"],
        check=True,
        capture_output=True,
        text=True,
    )
    return [tag for tag in result.stdout.splitlines() if tag]


def validate_release_tag(release_tag: str, existing_tags: list[str]) -> tuple[bool, str]:
    release_version = parse_semver(release_tag)
    if release_version is None:
        return (
            False,
            f"Release tag '{release_tag}' is not valid Docker-compatible SemVer. "
            "Use vMAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH, optionally with a prerelease suffix.",
        )

    semver_tags = [
        parsed
        for tag in existing_tags
        if tag != release_tag
        for parsed in [parse_semver(tag)]
        if parsed is not None
    ]

    if not semver_tags:
        return True, f"Release tag '{release_tag}' is valid SemVer; no previous SemVer tags found."

    latest = max(semver_tags, key=SemVerSortKey)
    if compare_semver(release_version, latest) <= 0:
        return (
            False,
            f"Release tag '{release_tag}' must be greater than previous SemVer tag '{latest.original}'.",
        )

    return True, f"Release tag '{release_tag}' is greater than previous SemVer tag '{latest.original}'."


class SemVerSortKey:
    def __init__(self, version: SemVer) -> None:
        self.version = version

    def __lt__(self, other: SemVerSortKey) -> bool:
        return compare_semver(self.version, other.version) < 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("release_tag", help="Release tag to validate")
    parser.add_argument(
        "--existing-tag",
        dest="existing_tags",
        action="append",
        default=None,
        help="Existing tag to compare against; repeatable. Defaults to git tag --list.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    existing_tags = args.existing_tags if args.existing_tags is not None else list_git_tags()

    valid, message = validate_release_tag(args.release_tag, existing_tags)
    print(message)
    return 0 if valid else 1


if __name__ == "__main__":
    sys.exit(main())
