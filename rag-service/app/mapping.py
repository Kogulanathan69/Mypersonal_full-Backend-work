"""Pure translation from the engine's STEP 4 retrieval + STEP 6 evidence output to contract v1.

Rules:
- An empty or unsupported retrieval outcome keeps its specific status: no source scope, no jurisdiction mapping or
  no lexical match.
- Only passages that the STEP 6 assessor kept in its source groups (EVIDENCE_CANDIDATE or AMBIGUOUS_EVIDENCE, with
  verified provenance) are returned. Insufficient or unverifiable passages are never presented as evidence.
- Every warning is preserved; scores are relevance only.
"""
from pathlib import PureWindowsPath
from typing import Any

from .contracts import Passage, SearchResponse, Source

_RETRIEVAL_STATUS = {
    "insufficient_jurisdiction_evidence": "INSUFFICIENT_JURISDICTION_EVIDENCE",
    "insufficient_source_evidence": "INSUFFICIENT_SOURCE_EVIDENCE",
    "no_matching_passages": "NO_MATCHING_EVIDENCE",
}

_EVIDENCE_STATUS = {
    "EVIDENCE_CANDIDATE": "EVIDENCE_FOUND",
    "AMBIGUOUS_EVIDENCE": "AMBIGUOUS_EVIDENCE",
    "JURISDICTION_UNVERIFIED": "INSUFFICIENT_JURISDICTION_EVIDENCE",
    "INSUFFICIENT_EVIDENCE": "NO_MATCHING_EVIDENCE",
}

_PRESENTABLE = {"EVIDENCE_FOUND", "AMBIGUOUS_EVIDENCE"}


def _unique(items: list[str]) -> list[str]:
    return list(dict.fromkeys(item for item in items if isinstance(item, str) and item))


def source_title(source_pdf: str) -> str:
    """File name only: internal folder layout is never exposed."""
    return PureWindowsPath(source_pdf).name


def to_search_response(result: dict[str, Any]) -> SearchResponse:
    retrieval = result["retrieval"]
    evidence = result["evidence"]
    retrieval_status = retrieval["status"]

    if retrieval_status in _RETRIEVAL_STATUS:
        status = _RETRIEVAL_STATUS[retrieval_status]
    elif retrieval_status == "retrieval_results":
        status = _EVIDENCE_STATUS.get(evidence["status"])
        if status is None:
            raise ValueError("Unknown evidence status")
    else:
        raise ValueError("Unknown retrieval status")

    warnings = _unique(
        list(retrieval.get("source_warnings", []))
        + list(evidence.get("warnings", []))
        + list(evidence.get("reasons", []))
    )

    passages: list[Passage] = []
    if status in _PRESENTABLE:
        results = retrieval["results"]
        kept = sorted(
            (assessment for items in evidence["groups"].values() for assessment in items),
            key=lambda assessment: assessment["input_position"],
        )
        for assessment in kept:
            if not assessment.get("provenance_verified") or assessment["status"] not in ("EVIDENCE_CANDIDATE", "AMBIGUOUS_EVIDENCE"):
                continue
            retrieved = results[assessment["input_position"]]
            citation = assessment["citation"]
            if retrieved["document_id"] != citation["document_id"] or retrieved["pdf_page_number"] != citation["pdf_page_number"]:
                raise ValueError("Assessment does not match its retrieved passage")
            passages.append(
                Passage(
                    passage_id=retrieved["chunk_id"],
                    document_id=citation["document_id"],
                    source_title=source_title(citation["source_pdf"]),
                    jurisdiction=assessment.get("jurisdiction"),
                    page=citation["pdf_page_number"],
                    start_char=citation["start_char"],
                    end_char=citation["end_char"],
                    excerpt=assessment["retrieved_passage"],
                    score=round(float(retrieved["score"]), 6),
                    evidence_status=assessment["status"],
                    warnings=_unique(
                        list(assessment.get("reasons", []))
                        + list(assessment.get("warnings", []))
                        + list(retrieved.get("source_warnings", []))
                    ),
                )
            )
        if not passages:
            status = "NO_MATCHING_EVIDENCE"
            warnings = _unique(warnings + ["NO_PRESENTABLE_PASSAGE"])

    return SearchResponse(status=status, passages=passages, warnings=warnings)


def to_sources(registry: dict[str, dict[str, Any]]) -> list[Source]:
    return [
        Source(
            document_id=document_id,
            source_title=source_title(entry["source_pdf"]),
            jurisdiction=entry.get("jurisdiction"),
            jurisdiction_verification_status=entry["jurisdiction_verification_status"],
            publication_date=entry.get("publication_date"),
            publication_date_verified=bool(entry.get("publication_date_verified")),
        )
        for document_id, entry in sorted(registry.items())
    ]
