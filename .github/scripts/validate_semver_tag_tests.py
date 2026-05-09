#!/usr/bin/env python3

import unittest

from validate_semver_tag import parse_semver, validate_release_tag


class ValidateSemverTagTests(unittest.TestCase):
    def test_accepts_first_valid_tag(self) -> None:
        valid, message = validate_release_tag("v1.0.0", [])

        self.assertTrue(valid)
        self.assertIn("valid SemVer", message)

    def test_rejects_non_semver_tag(self) -> None:
        valid, message = validate_release_tag("release-1", ["v1.0.0"])

        self.assertFalse(valid)
        self.assertIn("not valid", message)

    def test_rejects_build_metadata_because_docker_tags_do_not_support_plus(self) -> None:
        valid, message = validate_release_tag("v1.2.3+build.1", ["v1.2.2"])

        self.assertFalse(valid)
        self.assertIn("not valid", message)

    def test_ignores_current_release_tag_when_comparing_history(self) -> None:
        valid, message = validate_release_tag("v1.2.3", ["v1.2.3"])

        self.assertTrue(valid)
        self.assertIn("no previous", message)

    def test_rejects_release_less_than_previous_tag(self) -> None:
        valid, message = validate_release_tag("v1.2.2", ["v1.2.3"])

        self.assertFalse(valid)
        self.assertIn("must be greater", message)

    def test_accepts_release_greater_than_previous_tag(self) -> None:
        valid, message = validate_release_tag("v1.2.4", ["v1.2.3"])

        self.assertTrue(valid)
        self.assertIn("greater than previous", message)

    def test_prerelease_sorts_below_final_release(self) -> None:
        valid, message = validate_release_tag("v1.3.0-rc.1", ["v1.2.9"])
        self.assertTrue(valid)
        self.assertIn("greater than previous", message)

        valid, message = validate_release_tag("v1.3.0-rc.1", ["v1.3.0"])
        self.assertFalse(valid)
        self.assertIn("must be greater", message)

    def test_rejects_prerelease_numeric_identifier_with_leading_zero(self) -> None:
        self.assertIsNone(parse_semver("v1.2.3-rc.01"))


if __name__ == "__main__":
    unittest.main()
