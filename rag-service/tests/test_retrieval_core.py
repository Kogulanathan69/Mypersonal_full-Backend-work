"""The copied STEP 4 retrieval behaves as documented, on synthetic data; and the copy stays unmodified."""
import hashlib
import json
from pathlib import Path

import pytest

from regulation_retrieval.service import RegulationRetrievalService
from synthetic_corpus import NORTH, NORTH_JURISDICTION, SOUTH, SOUTH_JURISDICTION, build_engine_parts, page_text

ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture(scope="module")
def retriever():
    index, _ = build_engine_parts()
    return RegulationRetrievalService(index)


def test_offsets_reconstruct_original_page_text_with_one_based_pages(retriever):
    response = retriever.search("rear space residential", k=5)

    assert response["status"] == "retrieval_results"
    for r in response["results"]:
        assert r["pdf_page_number"] >= 1
        assert page_text(r["document_id"], r["pdf_page_number"])[r["start_char"]:r["end_char"]] == r["text"]
        assert r["legal_status_verified"] is False and r["automated_compliance_approved"] is False


def test_jurisdiction_filter_applies_before_ranking(retriever):
    north = retriever.search("residential building", k=10, filters={"jurisdiction": NORTH_JURISDICTION})
    south = retriever.search("residential building", k=10, filters={"jurisdiction": SOUTH_JURISDICTION})

    assert {r["document_id"] for r in north["results"]} == {NORTH}
    assert {r["document_id"] for r in south["results"]} == {SOUTH}


def test_unknown_jurisdiction_and_no_hit_are_distinct(retriever):
    assert retriever.search("rear space", filters={"jurisdiction": "Nowhere"})["status"] == "insufficient_jurisdiction_evidence"
    assert retriever.search("zeppelin mooring mast")["status"] == "no_matching_passages"


def test_ranking_prefers_the_matching_provision(retriever):
    results = retriever.search("plot coverage", k=3)["results"]

    assert results[0]["document_id"] == SOUTH and "plot coverage" in results[0]["text"].lower()


@pytest.mark.parametrize("k,filters", [(0, None), (101, None), (5, {"unsupported": "x"}), (5, {"jurisdiction": ""})])
def test_invalid_engine_arguments_are_rejected(retriever, k, filters):
    with pytest.raises(ValueError):
        retriever.search("rear space", k=k, filters=filters)


def test_copied_engine_is_byte_identical_to_its_recorded_provenance():
    manifest = json.loads((ROOT / "regulation_retrieval" / "PROVENANCE.json").read_text(encoding="utf-8"))
    for entry in manifest["files"]:
        actual = hashlib.sha256((ROOT / "regulation_retrieval" / entry["path"]).read_bytes()).hexdigest()
        assert actual == entry["sha256"], entry["path"]
