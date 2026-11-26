"""The test-only acceptance service serves exactly the shared scenario packs, authenticated, over the real app."""
import importlib
import json
import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app import canonical_json
from support import AUTH_HEADER, TEST_TOKEN

SCENARIOS = json.loads((Path(__file__).resolve().parents[1] / "contract" / "constraints_scenarios.v0.json").read_text(encoding="utf-8"))
ENDPOINT = "/internal/v1/regulations/constraints"


def _load(monkeypatch, **env):
    for key in ("BUILDMIND_ACCEPTANCE_SYNTHETIC", "BUILDMIND_RAG_INTERNAL_TOKENS", "BUILDMIND_RAG_AUTH", "BUILDMIND_RAG_ENVIRONMENT"):
        monkeypatch.delenv(key, raising=False)
    for key, value in env.items():
        monkeypatch.setenv(key, value)
    sys.modules.pop("acceptance_app", None)
    return importlib.import_module("acceptance_app")


@pytest.fixture
def acceptance(monkeypatch):
    module = _load(monkeypatch, BUILDMIND_ACCEPTANCE_SYNTHETIC="1", BUILDMIND_RAG_INTERNAL_TOKENS=TEST_TOKEN)
    return TestClient(module.app)


@pytest.mark.parametrize("scenario", SCENARIOS["scenarios"], ids=lambda s: s["name"])
def test_each_mount_serves_exactly_the_shared_scenario_pack(acceptance, scenario):
    response = acceptance.post(f"/scenario/{scenario['name']}{ENDPOINT}", json=scenario["request"], headers={AUTH_HEADER: TEST_TOKEN})

    assert response.status_code == 200
    pack = response.json()
    assert pack == scenario["response"]
    assert pack["evidence_pack_id"] == canonical_json.digest({k: v for k, v in pack.items() if k != "evidence_pack_id"})


def test_mounts_require_the_internal_token(acceptance):
    scenario = SCENARIOS["scenarios"][0]

    response = acceptance.post(f"/scenario/{scenario['name']}{ENDPOINT}", json=scenario["request"])

    assert response.status_code == 401


@pytest.mark.parametrize("env", [
    {"BUILDMIND_RAG_INTERNAL_TOKENS": TEST_TOKEN},                                  # no explicit synthetic opt-in
    {"BUILDMIND_ACCEPTANCE_SYNTHETIC": "1"},                                        # no token
    {"BUILDMIND_ACCEPTANCE_SYNTHETIC": "1", "BUILDMIND_RAG_AUTH": "disabled", "BUILDMIND_RAG_ENVIRONMENT": "development"},
])
def test_acceptance_service_refuses_to_start_unsafely(monkeypatch, env):
    with pytest.raises(RuntimeError):
        _load(monkeypatch, **env)
