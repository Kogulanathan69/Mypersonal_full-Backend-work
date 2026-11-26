import hashlib
import json
from pathlib import Path

import pytest

from app.validation.geometry_hash import compute_geometry_hash, is_geometry_hash, matches_geometry_hash


VECTORS = json.loads(
    (Path(__file__).parent / "fixtures" / "geometry_hash_v1.json").read_text(encoding="utf-8")
)["vectors"]


@pytest.mark.parametrize("vector", VECTORS, ids=lambda vector: vector["name"])
def test_exact_utf8_bytes_match_csharp_contract(vector: dict[str, str]) -> None:
    value, expected = vector["geometryJson"], vector["geometryHash"]
    assert expected == "sha256:" + hashlib.sha256(value.encode("utf-8")).hexdigest()
    assert compute_geometry_hash(value) == expected
    assert matches_geometry_hash(value, expected)


def test_representation_changes_change_hash() -> None:
    values = ['{"n":1}', '{"n":1.0}', '{ "n": 1 }']
    assert len({compute_geometry_hash(value) for value in values}) == len(values)


@pytest.mark.parametrize("value", [
    "", "a" * 64, "sha256:" + "A" * 64, "sha256:" + "g" * 64,
    "sha256:" + "a" * 63, "sha256:" + "a" * 65,
])
def test_noncanonical_hash_is_rejected(value: str) -> None:
    assert not is_geometry_hash(value)
    assert not matches_geometry_hash("{}", value)


def test_modified_exact_bytes_are_rejected() -> None:
    expected = compute_geometry_hash('{"n":1}')
    assert not matches_geometry_hash('{"n":1 }', expected)
