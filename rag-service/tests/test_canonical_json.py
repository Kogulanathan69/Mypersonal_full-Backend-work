"""BuildMind canonical JSON v1 (BM-JCS-1): the shared vectors also run in the C# suite (CanonicalJsonTests)."""
import hashlib
import json
from pathlib import Path

import pytest

from app import canonical_json

VECTORS = json.loads((Path(__file__).resolve().parents[1] / "contract" / "canonical_json_vectors.v1.json").read_text(encoding="utf-8"))


def _strict(text):
    def pairs(items):
        keys = [k for k, _ in items]
        if len(set(keys)) != len(keys):
            raise canonical_json.CanonicalizationError("duplicate key")
        return dict(items)
    return json.loads(text, object_pairs_hook=pairs)


@pytest.mark.parametrize("vector", VECTORS["valid"], ids=lambda v: v["name"])
def test_valid_vectors(vector):
    value = _strict(vector["json"])

    assert canonical_json.canonicalize(value) == vector["canonical"]
    assert canonical_json.digest(value) == vector["sha256"] == hashlib.sha256(vector["canonical"].encode("utf-8")).hexdigest()


@pytest.mark.parametrize("vector", VECTORS["invalid"], ids=lambda v: v["name"])
def test_invalid_vectors_are_rejected(vector):
    with pytest.raises(canonical_json.CanonicalizationError):
        canonical_json.canonicalize(_strict(vector["json"]))


def test_vectors_cover_the_cross_language_risks():
    names = {v["name"] for v in VECTORS["valid"]} | {v["name"] for v in VECTORS["invalid"]}

    assert {"utf16-not-code-point-order", "string-escapes", "integers", "decimals", "duplicate-key", "lone-surrogate"} <= names
    assert VECTORS["algorithm"] == "SHA-256/BM-JCS-1"


@pytest.mark.parametrize("value", [float("nan"), float("inf"), True.__class__, object()])
def test_unsupported_values_are_rejected(value):
    with pytest.raises(canonical_json.CanonicalizationError):
        canonical_json.canonicalize({"a": value})


def test_negative_zero_and_integral_floats_are_plain_integers():
    assert canonical_json.canonicalize([-0.0, 3.0, 1e2]) == "[0,3,100]"
