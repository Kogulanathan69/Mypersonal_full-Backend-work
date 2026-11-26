"""The shared contract example (also consumed by the C# RagServiceClient tests) is a valid v1 response."""
import json
from pathlib import Path

from app.contracts import SearchResponse

EXAMPLE = Path(__file__).resolve().parents[1] / "contract" / "search_response.v1.example.json"


def test_shared_example_is_a_valid_v1_response():
    response = SearchResponse.model_validate(json.loads(EXAMPLE.read_text(encoding="utf-8")))

    assert response.status == "EVIDENCE_FOUND"
    assert response.legal_status_verified is False and response.answer_generated is False
    assert response.passages[0].page == 1


def test_contract_flags_cannot_claim_verification_or_generation():
    data = json.loads(EXAMPLE.read_text(encoding="utf-8"))
    for flag in ("legal_status_verified", "answer_generated"):
        tampered = dict(data, **{flag: True})
        try:
            SearchResponse.model_validate(tampered)
        except ValueError:
            continue
        raise AssertionError(f"{flag}=true must be rejected by the contract model")
