from dataclasses import replace
from decimal import Decimal
import unittest
import json
from pathlib import Path
from uuid import UUID

from app.geometry.compatibility.m2_m6_v1 import (
    AffineTranslation,
    CrsProvenance,
    EdgeMapping,
    GeometryCompatibilityError,
    LandGeometryHandoff,
    LocalFrame,
    ParcelEdge,
    ParcelRing,
    ParcelVertex,
    area_tolerance,
    bind_setbacks_to_m6_indices,
    compute_geometry_digest,
    compute_transformation_evidence_digest,
    projected_to_local,
    validate_handoff,
)


PROJECT = UUID("10000000-0000-0000-0000-000000000001")
LAND = UUID("20000000-0000-0000-0000-000000000001")
RING = UUID("30000000-0000-0000-0000-000000000001")
VERTICES = tuple(UUID(f"40000000-0000-0000-0000-{index:012d}") for index in range(1, 6))
EDGES = tuple(UUID(f"50000000-0000-0000-0000-{index:012d}") for index in range(1, 6))
SOURCE_VERTICES = tuple(UUID(f"60000000-0000-0000-0000-{index:012d}") for index in range(1, 6))
SOURCE_EDGES = tuple(UUID(f"70000000-0000-0000-0000-{index:012d}") for index in range(1, 6))
COORDINATES = {
    VERTICES[0]: (Decimal("0.000000"), Decimal("0.000000")),
    VERTICES[1]: (Decimal("40.000000"), Decimal("5.000000")),
    VERTICES[2]: (Decimal("45.000000"), Decimal("35.000000")),
    VERTICES[3]: (Decimal("15.000000"), Decimal("50.000000")),
    VERTICES[4]: (Decimal("-10.000000"), Decimal("25.000000")),
}
PHYSICAL_EDGE = {
    frozenset((VERTICES[index], VERTICES[(index + 1) % 5])): (EDGES[index], SOURCE_EDGES[index])
    for index in range(5)
}
HOLE_RING = UUID("30000000-0000-0000-0000-000000000002")
HOLE_VERTICES = tuple(UUID(f"41000000-0000-0000-0000-{index:012d}") for index in range(1, 4))
HOLE_EDGES = tuple(UUID(f"51000000-0000-0000-0000-{index:012d}") for index in range(1, 4))
HOLE_COORDINATES = ((Decimal("10.000000"), Decimal("15.000000")),
                    (Decimal("20.000000"), Decimal("17.000000")),
                    (Decimal("14.000000"), Decimal("25.000000")))


def ring(order=VERTICES):
    vertices = tuple(ParcelVertex(vertex, *COORDINATES[vertex], SOURCE_VERTICES[VERTICES.index(vertex)])
                     for vertex in order)
    edges = []
    for index, start in enumerate(order):
        end = order[(index + 1) % len(order)]
        edge_id, source_edge_id = PHYSICAL_EDGE[frozenset((start, end))]
        edges.append(ParcelEdge(edge_id, start, end, source_edge_id, index))
    return ParcelRing(RING, vertices, tuple(edges))


def hole_ring():
    vertices = tuple(ParcelVertex(vertex, *HOLE_COORDINATES[index],
                                  UUID(f"61000000-0000-0000-0000-{index + 1:012d}"))
                     for index, vertex in enumerate(HOLE_VERTICES))
    edges = tuple(ParcelEdge(HOLE_EDGES[index], vertex, HOLE_VERTICES[(index + 1) % 3],
                             UUID(f"71000000-0000-0000-0000-{index + 1:012d}"), index)
                  for index, vertex in enumerate(HOLE_VERTICES))
    return ParcelRing(HOLE_RING, vertices, edges)


def handoff(order=VERTICES, *, digest=True):
    frame = LocalFrame(
        Decimal("370000.000000"), Decimal("765000.000000"),
        AffineTranslation(Decimal(1), Decimal(0), Decimal(0), Decimal(1),
                          Decimal("-370000.000000"), Decimal("-765000.000000")),
        ring(order), (hole_ring(),),
    )
    provenance = CrsProvenance("EPSG:4326", "EPSG:32644", True, "m", "m",
        "pyproj-always-xy-no-ballpark-only-best-v1", "00" * 32,
        source_geometry_digest="ab" * 32, projected_coordinate_digest="bc" * 32,
        local_coordinate_digest="cd" * 32, origin_x=frame.origin_x, origin_y=frame.origin_y,
        operation_code="32644")
    provenance = replace(provenance, transformation_evidence_digest=compute_transformation_evidence_digest(provenance))
    value = LandGeometryHandoff(PROJECT, LAND, "EPSG:32644", "ab" * 32, provenance, frame)
    return replace(value, geometry_digest=compute_geometry_digest(value)) if digest else value


class M2M6GeometryCompatibilityV1Tests(unittest.TestCase):
    def code(self, expected, operation):
        with self.assertRaises(GeometryCompatibilityError) as captured:
            operation()
        self.assertEqual(expected, captured.exception.code)

    def test_non_zero_origin_is_applied_exactly_once(self):
        self.assertEqual(
            (Decimal("12.345679"), Decimal("-1.000000")),
            projected_to_local("370012.3456789", "764999", "370000.000000", "765000.000000"),
        )

    def test_valid_handoff_and_digest(self):
        value = handoff()
        self.assertIs(value, validate_handoff(value, PROJECT, LAND))
        self.assertEqual(64, len(value.geometry_digest))
        self.assertEqual(1, len(value.local_frame.holes))
        vector = json.loads((Path(__file__).parent / "fixtures" / "m2-m6-canonical-digest-vector.v1.json").read_text())
        self.assertEqual(vector["expectedSha256"], value.geometry_digest)

    def test_digest_is_invariant_to_orientation_and_start_vertex(self):
        original = handoff()
        rotated = handoff(VERTICES[2:] + VERTICES[:2])
        reversed_ring = handoff(tuple(reversed(VERTICES)))
        self.assertEqual(original.geometry_digest, rotated.geometry_digest)
        self.assertEqual(original.geometry_digest, reversed_ring.geometry_digest)

    def test_wrong_project_and_land_fail_closed(self):
        value = handoff()
        self.code("PROJECT_BINDING_MISMATCH", lambda: validate_handoff(value, UUID(int=1), LAND))
        self.code("LAND_REVISION_MISMATCH", lambda: validate_handoff(value, PROJECT, UUID(int=2)))

    def test_missing_or_changed_digest_fails_closed(self):
        value = handoff(digest=False)
        self.code("GEOMETRY_DIGEST_MISMATCH", lambda: validate_handoff(value, PROJECT, LAND))
        self.code("GEOMETRY_DIGEST_MISMATCH", lambda: validate_handoff(
            replace(value, geometry_digest="00" * 32), PROJECT, LAND))

    def test_rotation_scaling_and_double_origin_are_rejected(self):
        value = handoff(digest=False)
        rotated = replace(value.local_frame.affine, a=Decimal(0), b=Decimal(-1), c=Decimal(1), d=Decimal(0))
        self.code("LOCAL_AFFINE_UNSUPPORTED", lambda: replace(value.local_frame, affine=rotated).validate())
        doubled = replace(value.local_frame.affine, tx=Decimal("-740000.000000"))
        self.code("LOCAL_AFFINE_INCONSISTENT", lambda: replace(value.local_frame, affine=doubled).validate())

    def test_invalid_or_non_metre_crs_provenance_is_rejected(self):
        value = handoff()
        non_metre = replace(value.crs_provenance, axis_unit_x="ft")
        self.code("CRS_PROVENANCE_INVALID", lambda: validate_handoff(
            replace(value, crs_provenance=non_metre), PROJECT, LAND))
        inconsistent = replace(value.crs_provenance, calculation_crs="EPSG:3857")
        self.code("CRS_PROVENANCE_INVALID", lambda: validate_handoff(
            replace(value, crs_provenance=inconsistent), PROJECT, LAND))

    def test_more_than_six_fractional_digits_are_rejected(self):
        value = handoff(digest=False)
        first = replace(value.local_frame.exterior.vertices[0], x=Decimal("0.0000001"))
        changed = replace(value.local_frame.exterior,
                          vertices=(first, *value.local_frame.exterior.vertices[1:]))
        self.code("COORDINATE_PRECISION_UNSUPPORTED", changed.validate)

    def test_edge_mapping_is_bijective_and_preserves_unequal_setbacks(self):
        exterior = ring(tuple(reversed(VERTICES)))
        mappings = tuple(EdgeMapping(edge.edge_id, edge.normalized_index) for edge in exterior.edges)
        setbacks = {edge_id: Decimal(f"{index}.000000") for index, edge_id in enumerate(EDGES, 1)}
        distances = bind_setbacks_to_m6_indices(exterior, mappings, setbacks)
        for edge in exterior.edges:
            self.assertEqual(setbacks[edge.edge_id], distances[edge.normalized_index])

    def test_incomplete_duplicate_and_wrong_edge_mapping_fail_closed(self):
        exterior = ring()
        valid = [EdgeMapping(edge.edge_id, edge.normalized_index) for edge in exterior.edges]
        setbacks = {edge_id: Decimal("1.000000") for edge_id in EDGES}
        self.code("EDGE_REMAP_INCOMPLETE", lambda: bind_setbacks_to_m6_indices(exterior, valid[:-1], setbacks))
        duplicate = [*valid[:-1], EdgeMapping(valid[-1].edge_id, 0)]
        self.code("EDGE_REMAP_INVALID", lambda: bind_setbacks_to_m6_indices(exterior, duplicate, setbacks))
        missing = dict(setbacks)
        missing.pop(EDGES[-1])
        self.code("SETBACK_EDGE_COVERAGE_MISSING", lambda: bind_setbacks_to_m6_indices(exterior, valid, missing))

    def test_edge_id_is_not_derived_from_normalized_position(self):
        original = {edge.edge_id for edge in ring().edges}
        changed = {edge.edge_id for edge in ring(VERTICES[1:] + VERTICES[:1]).edges}
        self.assertEqual(original, changed)

    def test_area_tolerance_uses_each_approved_floor(self):
        self.assertEqual(1e-10, area_tolerance(0.0, 0.0))
        self.assertEqual(4e-7, area_tolerance(25.0, 40.0))
        self.assertGreaterEqual(area_tolerance(1e300, 0.0), 32 * __import__("math").ulp(1e300))

    def test_self_intersecting_exterior_fails_closed(self):
        value = handoff(digest=False)
        vertices = list(value.local_frame.exterior.vertices)
        vertices[1] = replace(vertices[1], x=Decimal("45.000000"), y=Decimal("35.000000"))
        vertices[2] = replace(vertices[2], x=Decimal("40.000000"), y=Decimal("5.000000"))
        broken = replace(value, local_frame=replace(value.local_frame,
            exterior=replace(value.local_frame.exterior, vertices=tuple(vertices))))
        self.code("RING_TOPOLOGY_INVALID", lambda: validate_handoff(broken, PROJECT, LAND, require_digest=False))

    def test_hole_outside_exterior_fails_closed(self):
        value = handoff(digest=False)
        hole = value.local_frame.holes[0]
        moved = tuple(replace(vertex, x=vertex.x + Decimal("100.000000")) for vertex in hole.vertices)
        broken = replace(value, local_frame=replace(value.local_frame, holes=(replace(hole, vertices=moved),)))
        self.code("HOLE_OUTSIDE_EXTERIOR", lambda: validate_handoff(broken, PROJECT, LAND, require_digest=False))


if __name__ == "__main__":
    unittest.main()
