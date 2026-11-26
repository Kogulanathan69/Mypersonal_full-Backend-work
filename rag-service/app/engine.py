"""Engine seam: the reused STEP 4 retrieval + STEP 6 evidence assessment over a prebuilt, read-only corpus.

The corpus (original PDFs, extracted page records, approved manifest and index) is NEVER stored in this repository.
It is located through environment variables and verified by the reused loaders: original-PDF SHA-256 must equal
the approved document identity, and extracted records must match their recorded hashes.
"""
import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol

CORPUS_ROOT_VARIABLE = "BUILDMIND_RAG_CORPUS_ROOT"
MANIFEST_VARIABLE = "BUILDMIND_RAG_MANIFEST"
INDEX_VARIABLE = "BUILDMIND_RAG_INDEX"


class RetrievalEngine(Protocol):
    def search(
        self,
        query: str,
        k: int,
        filters: dict[str, str] | None,
        requested_document_ids: list[str] | None,
    ) -> dict[str, Any]: ...

    def registry(self) -> dict[str, dict[str, Any]]: ...


@dataclass(frozen=True)
class EngineSettings:
    corpus_root: Path
    manifest: Path
    index: Path

    @staticmethod
    def from_environment() -> "EngineSettings | None":
        values = [os.environ.get(name, "").strip() for name in (CORPUS_ROOT_VARIABLE, MANIFEST_VARIABLE, INDEX_VARIABLE)]
        if not all(values):
            return None
        return EngineSettings(Path(values[0]), Path(values[1]), Path(values[2]))


class Step6Engine:
    """Reuses the verified STEP 4/6 code unchanged (rag-service/regulation_retrieval, see PROVENANCE.md)."""

    def __init__(self, settings: EngineSettings):
        from regulation_retrieval.corpus_step4 import load_step4_corpus
        from regulation_retrieval.evidence_step6.service import EvidenceValidationService

        index = json.loads(settings.index.read_text(encoding="utf-8"))
        documents = load_step4_corpus(settings.corpus_root, settings.manifest)
        self._service = EvidenceValidationService(index, documents)
        self._registry = index["source_registry"]

    def search(self, query, k, filters, requested_document_ids):
        return self._service.search(query, k, filters or None, requested_document_ids)

    def registry(self):
        return self._registry

    def page_text(self, document_id: str, page: int) -> str | None:
        """Exact extracted page text from the hash-verified corpus (used to verify constraint provenance)."""
        record = self._service.assessor.records.get(document_id)
        if record is None or not 1 <= page <= len(record["pages"]):
            return None
        return record["pages"][page - 1]["text"]
