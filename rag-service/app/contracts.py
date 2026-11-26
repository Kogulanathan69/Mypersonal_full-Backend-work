"""Evidence contract v1 (BE-M7-D03, lead-approved 2026-11-19).

Retrieval returns cited source passages and an explicit evidence status. No answer is generated, no legal currency
or applicability is asserted, and no compliance decision is made.
"""
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StrictInt, StringConstraints

CONTRACT_VERSION = "1"

EvidenceStatus = Literal[
    "EVIDENCE_FOUND",
    "NO_MATCHING_EVIDENCE",
    "INSUFFICIENT_JURISDICTION_EVIDENCE",
    "INSUFFICIENT_SOURCE_EVIDENCE",
    "AMBIGUOUS_EVIDENCE",
]

PassageEvidenceStatus = Literal["EVIDENCE_CANDIDATE", "AMBIGUOUS_EVIDENCE"]

DocumentId = Annotated[str, StringConstraints(pattern=r"^[a-f0-9]{64}$")]


class SearchRequest(BaseModel):
    """POST /internal/v1/regulations/search. Unknown fields are rejected."""

    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)

    query: str = Field(min_length=1, max_length=500)
    jurisdiction: str | None = Field(default=None, min_length=1, max_length=200)
    document_ids: list[DocumentId] | None = Field(default=None, min_length=1, max_length=3)
    k: StrictInt = Field(default=5, ge=1, le=10)


class Passage(BaseModel):
    passage_id: str
    document_id: str
    source_title: str
    jurisdiction: str | None
    page: int = Field(ge=1, description="One-based PDF page position (not the printed page label).")
    start_char: int = Field(ge=0, description="Unicode code-point offset within the extracted page text.")
    end_char: int = Field(ge=1)
    excerpt: str
    score: float = Field(description="Lexical relevance (BM25), not legal confidence.")
    evidence_status: PassageEvidenceStatus
    warnings: list[str]


class SearchResponse(BaseModel):
    status: EvidenceStatus
    passages: list[Passage]
    warnings: list[str]
    legal_status_verified: Literal[False] = False
    answer_generated: Literal[False] = False


class Source(BaseModel):
    document_id: str
    source_title: str
    jurisdiction: str | None
    jurisdiction_verification_status: str
    publication_date: str | None
    publication_date_verified: bool
    legal_status: Literal["not_verified"] = "not_verified"


class SourcesResponse(BaseModel):
    sources: list[Source]
    legal_status_verified: Literal[False] = False
