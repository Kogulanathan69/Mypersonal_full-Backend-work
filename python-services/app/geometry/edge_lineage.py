"""Validation of caller-supplied source-edge lineage.

Identity is never derived from coordinate equality.  The caller must supply a
stable non-empty edge ID for every edge in both revisions.
"""

from dataclasses import dataclass
from enum import StrEnum
from math import isfinite


@dataclass(frozen=True)
class SourceEdge:
    edge_id: str
    start: tuple[float, float]
    end: tuple[float, float]


@dataclass(frozen=True)
class EdgeRing:
    frame_id: str
    edges: tuple[SourceEdge, ...]


class LineageStatus(StrEnum):
    PRESERVED = "PRESERVED"
    PRESERVED_REVERSED = "PRESERVED_REVERSED"
    INVALID = "INVALID"
    FRAME_MISMATCH = "FRAME_MISMATCH"
    AMBIGUOUS = "AMBIGUOUS"


@dataclass(frozen=True)
class LineageResult:
    status: LineageStatus
    code: str


def _valid_point(point: object) -> bool:
    if not isinstance(point, tuple) or len(point) != 2:
        return False
    try:
        return all(
            not isinstance(value, bool)
            and isinstance(value, (int, float))
            and isfinite(value)
            for value in point
        )
    except (OverflowError, ValueError, TypeError):
        return False


def _validate(ring: EdgeRing) -> str | None:
    if not isinstance(ring, EdgeRing) or not isinstance(ring.frame_id, str) or not ring.frame_id.strip():
        return "FRAME_ID_REQUIRED"
    if not isinstance(ring.edges, tuple):
        return "EDGE_RING_INVALID"
    if len(ring.edges) < 3:
        return "EDGE_RING_TOO_SHORT"
    if any(not isinstance(edge, SourceEdge) for edge in ring.edges):
        return "EDGE_GEOMETRY_INVALID"
    identifiers = [edge.edge_id for edge in ring.edges]
    if any(
        not isinstance(value, str) or not value.strip() or len(value) > 128
        for value in identifiers
    ):
        return "SOURCE_EDGE_ID_REQUIRED"
    if len(set(identifiers)) != len(identifiers):
        return "DUPLICATE_SOURCE_EDGE_ID"
    for index, edge in enumerate(ring.edges):
        if not _valid_point(edge.start) or not _valid_point(edge.end) or edge.start == edge.end:
            return "EDGE_GEOMETRY_INVALID"
        if edge.end != ring.edges[(index + 1) % len(ring.edges)].start:
            return "EDGE_RING_NOT_CONTIGUOUS"
    try:
        from shapely.geometry import LinearRing

        geometry = LinearRing([edge.start for edge in ring.edges])
        if geometry.is_empty or not geometry.is_valid or not geometry.is_simple:
            return "EDGE_RING_INVALID"
    except (ImportError, ArithmeticError, ValueError, TypeError, OverflowError):
        return "EDGE_RING_INVALID"
    return None


def _is_rotation(candidate: list[str], expected: list[str]) -> bool:
    if len(candidate) != len(expected):
        return False
    if not candidate:
        return True
    # KMP over the doubled expected sequence keeps cyclic comparison linear.
    pattern = candidate
    prefix = [0] * len(pattern)
    for index in range(1, len(pattern)):
        matched = prefix[index - 1]
        while matched and pattern[index] != pattern[matched]:
            matched = prefix[matched - 1]
        if pattern[index] == pattern[matched]:
            matched += 1
        prefix[index] = matched
    matched = 0
    for value in (expected + expected[:-1]):
        while matched and value != pattern[matched]:
            matched = prefix[matched - 1]
        if value == pattern[matched]:
            matched += 1
        if matched == len(pattern):
            return True
    return False


def compare_edge_lineage(source: EdgeRing, candidate: EdgeRing) -> LineageResult:
    source_error = _validate(source)
    if source_error:
        return LineageResult(LineageStatus.INVALID, source_error)
    candidate_error = _validate(candidate)
    if candidate_error:
        return LineageResult(LineageStatus.INVALID, candidate_error)
    if source.frame_id != candidate.frame_id:
        return LineageResult(LineageStatus.FRAME_MISMATCH, "COORDINATE_FRAME_MISMATCH")
    source_ids = [edge.edge_id for edge in source.edges]
    candidate_ids = [edge.edge_id for edge in candidate.edges]
    if set(source_ids) != set(candidate_ids) or len(source_ids) != len(candidate_ids):
        return LineageResult(LineageStatus.AMBIGUOUS, "EDGE_SPLIT_MERGE_LINEAGE_REQUIRED")
    if _is_rotation(candidate_ids, source_ids):
        return LineageResult(LineageStatus.PRESERVED, "EDGE_LINEAGE_PRESERVED")
    if _is_rotation(candidate_ids, list(reversed(source_ids))):
        return LineageResult(LineageStatus.PRESERVED_REVERSED, "EDGE_LINEAGE_PRESERVED_REVERSED")
    return LineageResult(LineageStatus.AMBIGUOUS, "EDGE_ORDER_LINEAGE_AMBIGUOUS")
