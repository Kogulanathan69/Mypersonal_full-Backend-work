from app.geometry.edge_lineage import (
    EdgeRing,
    LineageStatus,
    SourceEdge,
    compare_edge_lineage,
)


FRAME = "synthetic-frame-v1"


def _ring(ids=("south", "east", "north", "west"), frame=FRAME):
    points = ((0.0, 0.0), (10.0, 0.0), (10.0, 5.0), (0.0, 5.0))
    edges = tuple(
        SourceEdge(ids[index], points[index], points[(index + 1) % len(points)])
        for index in range(len(points))
    )
    return EdgeRing(frame, edges)


def test_start_vertex_rotation_preserves_explicit_edge_identity() -> None:
    source = _ring()
    candidate = EdgeRing(FRAME, source.edges[2:] + source.edges[:2])
    result = compare_edge_lineage(source, candidate)
    assert result.status == LineageStatus.PRESERVED


def test_ring_orientation_reversal_preserves_explicit_ids() -> None:
    source = _ring()
    candidate = EdgeRing(FRAME, tuple(
        SourceEdge(edge.edge_id, edge.end, edge.start) for edge in reversed(source.edges)
    ))
    result = compare_edge_lineage(source, candidate)
    assert result.status == LineageStatus.PRESERVED_REVERSED


def test_duplicate_and_missing_source_identity_are_rejected() -> None:
    source = _ring()
    duplicate = _ring(("south", "south", "north", "west"))
    missing = _ring(("south", "east", "", "west"))
    assert compare_edge_lineage(source, duplicate).code == "DUPLICATE_SOURCE_EDGE_ID"
    assert compare_edge_lineage(source, missing).code == "SOURCE_EDGE_ID_REQUIRED"


def test_split_merge_requires_explicit_authoritative_lineage() -> None:
    source = _ring()
    candidate = _ring(("south", "east", "new-north", "west"))
    result = compare_edge_lineage(source, candidate)
    assert result.status == LineageStatus.AMBIGUOUS
    assert result.code == "EDGE_SPLIT_MERGE_LINEAGE_REQUIRED"


def test_frame_identity_mismatch_fails_closed() -> None:
    result = compare_edge_lineage(_ring(), _ring(frame="other-frame"))
    assert result.status == LineageStatus.FRAME_MISMATCH


def test_positions_alone_do_not_establish_identity() -> None:
    source = _ring()
    same_positions_new_ids = _ring(("one", "two", "three", "four"))
    result = compare_edge_lineage(source, same_positions_new_ids)
    assert result.status == LineageStatus.AMBIGUOUS



def test_huge_integer_source_coordinate_fails_closed():
    source = _ring()
    first = source.edges[0]
    invalid = EdgeRing(
        FRAME,
        (
            SourceEdge(first.edge_id, (10 ** 400, 0), first.end),
            *source.edges[1:],
        ),
    )
    result = compare_edge_lineage(invalid, source)
    assert result.status == LineageStatus.INVALID
    assert result.code == "EDGE_GEOMETRY_INVALID"


def test_huge_integer_candidate_coordinate_fails_closed():
    source = _ring()
    first = source.edges[0]
    invalid = EdgeRing(
        FRAME,
        (
            SourceEdge(first.edge_id, first.start, (10 ** 400, 0)),
            *source.edges[1:],
        ),
    )
    result = compare_edge_lineage(source, invalid)
    assert result.status == LineageStatus.INVALID
    assert result.code == "EDGE_GEOMETRY_INVALID"


def test_self_intersecting_ring_is_rejected():
    points = ((0.0, 0.0), (2.0, 2.0), (0.0, 2.0), (2.0, 0.0))
    invalid = EdgeRing(
        FRAME,
        tuple(
            SourceEdge(str(index), points[index], points[(index + 1) % 4])
            for index in range(4)
        ),
    )
    result = compare_edge_lineage(invalid, _ring())
    assert result.status == LineageStatus.INVALID
    assert result.code == "EDGE_RING_INVALID"


def test_malformed_edge_object_is_rejected_safely():
    invalid = EdgeRing(FRAME, (object(), object(), object()))
    result = compare_edge_lineage(invalid, _ring())
    assert result.status == LineageStatus.INVALID
    assert result.code == "EDGE_GEOMETRY_INVALID"
