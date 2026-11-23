"""M2-M6 geometry handoff V1 isolated reference adapter.

This module deliberately has no PyProj, Shapely, transport or persistence
dependency. M2 supplies already-projected coordinates and transformation
provenance. The adapter verifies the approved translation-only local frame,
stable edge remapping, revision binding and canonical digest before M6 uses
metric geometry. Real CRS transformation remains an approved-Linux gate.
"""

from dataclasses import dataclass
from decimal import Decimal, InvalidOperation, ROUND_HALF_EVEN
from hashlib import sha256
from math import hypot, isfinite, ulp
from struct import pack
from typing import Iterable, Mapping
from uuid import UUID


SCHEMA_VERSION = "m2-m6-land-handoff/1.0"
PRECISION_VERSION = "m2-m6-metric-v1"
DIGEST_DOMAIN = b"BuildMind.M2M6.LandGeometry.v1\0"
QUANTUM_M = Decimal("0.000001")
COMPARISON_TOLERANCE_M = Decimal("0.000001")
GOLDEN_TOLERANCE_M = Decimal("0.001")
ROUND_TRIP_TOLERANCE_M = Decimal("0.001")
MINIMUM_CLEARANCE_M = Decimal("0.000001")
SETBACK_MARGIN_M = Decimal("0.00000001")
CONTACT_TOLERANCE_M = Decimal("0.000000001")
MAX_COORDINATE_M = Decimal("1000000")
TRANSFORMATION_POLICY = "pyproj-always-xy-no-ballpark-only-best-v1"
TRANSFORMATION_EVIDENCE_DOMAIN = b"BuildMind.M2M6.TransformEvidence.v1\0"


class GeometryCompatibilityError(ValueError):
    """Fail-closed compatibility error with a stable code."""

    def __init__(self, code: str) -> None:
        self.code = code
        super().__init__(code)


def _uuid(value: UUID, code: str) -> UUID:
    if not isinstance(value, UUID) or value.int == 0:
        raise GeometryCompatibilityError(code)
    return value


def _decimal(value: Decimal | int | str, code: str) -> Decimal:
    try:
        result = value if isinstance(value, Decimal) else Decimal(str(value))
    except (InvalidOperation, ValueError):
        raise GeometryCompatibilityError(code) from None
    if not result.is_finite():
        raise GeometryCompatibilityError(code)
    return result


def _canonical_metres(value: Decimal | int | str, code: str) -> Decimal:
    result = _decimal(value, code)
    if result != result.quantize(QUANTUM_M, rounding=ROUND_HALF_EVEN):
        raise GeometryCompatibilityError("COORDINATE_PRECISION_UNSUPPORTED")
    if abs(result) > MAX_COORDINATE_M:
        raise GeometryCompatibilityError("COORDINATE_RANGE_UNSUPPORTED")
    return result


def _micrometres(value: Decimal) -> int:
    return int(value / QUANTUM_M)


@dataclass(frozen=True)
class PrecisionProfile:
    version: str = PRECISION_VERSION
    coordinate_quantum_m: Decimal = QUANTUM_M
    coordinate_comparison_tolerance_m: Decimal = COMPARISON_TOLERANCE_M
    golden_tolerance_m: Decimal = GOLDEN_TOLERANCE_M
    round_trip_tolerance_m: Decimal = ROUND_TRIP_TOLERANCE_M
    minimum_clearance_m: Decimal = MINIMUM_CLEARANCE_M
    setback_margin_m: Decimal = SETBACK_MARGIN_M
    contact_tolerance_m: Decimal = CONTACT_TOLERANCE_M

    def validate(self) -> None:
        expected = PrecisionProfile()
        if self != expected:
            raise GeometryCompatibilityError("PRECISION_PROFILE_UNSUPPORTED")


@dataclass(frozen=True)
class AffineTranslation:
    a: Decimal
    b: Decimal
    c: Decimal
    d: Decimal
    tx: Decimal
    ty: Decimal

    def validate(self, origin_x: Decimal, origin_y: Decimal) -> None:
        if tuple(_decimal(value, "LOCAL_AFFINE_INVALID") for value in
                 (self.a, self.b, self.c, self.d)) != (
                     Decimal(1), Decimal(0), Decimal(0), Decimal(1)):
            raise GeometryCompatibilityError("LOCAL_AFFINE_UNSUPPORTED")
        tx = _decimal(self.tx, "LOCAL_AFFINE_INVALID")
        ty = _decimal(self.ty, "LOCAL_AFFINE_INVALID")
        if tx != -origin_x or ty != -origin_y:
            raise GeometryCompatibilityError("LOCAL_AFFINE_INCONSISTENT")


@dataclass(frozen=True)
class ParcelVertex:
    vertex_id: UUID
    x: Decimal
    y: Decimal
    source_vertex_id: UUID

    def validate(self) -> None:
        _uuid(self.vertex_id, "VERTEX_ID_INVALID")
        _uuid(self.source_vertex_id, "VERTEX_LINEAGE_INVALID")
        _canonical_metres(self.x, "COORDINATE_INVALID")
        _canonical_metres(self.y, "COORDINATE_INVALID")


@dataclass(frozen=True)
class ParcelEdge:
    edge_id: UUID
    start_vertex_id: UUID
    end_vertex_id: UUID
    source_edge_id: UUID
    normalized_index: int

    def validate(self) -> None:
        _uuid(self.edge_id, "EDGE_ID_INVALID")
        _uuid(self.start_vertex_id, "EDGE_VERTEX_REFERENCE_INVALID")
        _uuid(self.end_vertex_id, "EDGE_VERTEX_REFERENCE_INVALID")
        _uuid(self.source_edge_id, "EDGE_LINEAGE_INVALID")
        if self.start_vertex_id == self.end_vertex_id:
            raise GeometryCompatibilityError("EDGE_VERTEX_REFERENCE_INVALID")
        if isinstance(self.normalized_index, bool) or not isinstance(self.normalized_index, int) \
                or self.normalized_index < 0:
            raise GeometryCompatibilityError("EDGE_INDEX_INVALID")


@dataclass(frozen=True)
class ParcelRing:
    ring_id: UUID
    vertices: tuple[ParcelVertex, ...]
    edges: tuple[ParcelEdge, ...]

    def validate(self) -> None:
        _uuid(self.ring_id, "RING_ID_INVALID")
        if len(self.vertices) < 3 or len(self.edges) != len(self.vertices):
            raise GeometryCompatibilityError("RING_TOPOLOGY_INVALID")
        vertex_ids = [item.vertex_id for item in self.vertices]
        edge_ids = [item.edge_id for item in self.edges]
        source_vertex_ids = [item.source_vertex_id for item in self.vertices]
        source_edge_ids = [item.source_edge_id for item in self.edges]
        if len(set(vertex_ids)) != len(vertex_ids) or len(set(source_vertex_ids)) != len(source_vertex_ids):
            raise GeometryCompatibilityError("VERTEX_ID_DUPLICATE")
        if len(set(edge_ids)) != len(edge_ids) or len(set(source_edge_ids)) != len(source_edge_ids):
            raise GeometryCompatibilityError("EDGE_ID_DUPLICATE")
        for vertex in self.vertices:
            vertex.validate()
        for edge in self.edges:
            edge.validate()
        by_index = {edge.normalized_index: edge for edge in self.edges}
        if set(by_index) != set(range(len(self.vertices))):
            raise GeometryCompatibilityError("EDGE_REMAP_INVALID")
        for index, first in enumerate(self.vertices):
            second = self.vertices[(index + 1) % len(self.vertices)]
            edge = by_index[index]
            if {edge.start_vertex_id, edge.end_vertex_id} != {first.vertex_id, second.vertex_id}:
                raise GeometryCompatibilityError("EDGE_REMAP_INVALID")
            length = hypot(float(first.x - second.x), float(first.y - second.y))
            if length < float(MINIMUM_CLEARANCE_M):
                raise GeometryCompatibilityError("PARCEL_PRECISION_UNSUPPORTED")


@dataclass(frozen=True)
class LocalFrame:
    origin_x: Decimal
    origin_y: Decimal
    affine: AffineTranslation
    exterior: ParcelRing
    holes: tuple[ParcelRing, ...] = ()
    frame_type: str = "localCartesian"
    unit: str = "m"
    x_direction: str = "right"
    y_direction: str = "up"

    def validate(self) -> None:
        if (self.frame_type, self.unit, self.x_direction, self.y_direction) != (
                "localCartesian", "m", "right", "up"):
            raise GeometryCompatibilityError("COORDINATE_SYSTEM_UNSUPPORTED")
        origin_x = _canonical_metres(self.origin_x, "LOCAL_ORIGIN_INVALID")
        origin_y = _canonical_metres(self.origin_y, "LOCAL_ORIGIN_INVALID")
        self.affine.validate(origin_x, origin_y)
        self.exterior.validate()
        ring_ids = [self.exterior.ring_id, *(hole.ring_id for hole in self.holes)]
        if len(set(ring_ids)) != len(ring_ids):
            raise GeometryCompatibilityError("RING_ID_DUPLICATE")
        all_vertex_ids: list[UUID] = []
        all_edge_ids: list[UUID] = []
        for ring in (self.exterior, *self.holes):
            ring.validate()
            all_vertex_ids.extend(vertex.vertex_id for vertex in ring.vertices)
            all_edge_ids.extend(edge.edge_id for edge in ring.edges)
        if len(set(all_vertex_ids)) != len(all_vertex_ids):
            raise GeometryCompatibilityError("VERTEX_ID_DUPLICATE")
        if len(set(all_edge_ids)) != len(all_edge_ids):
            raise GeometryCompatibilityError("EDGE_ID_DUPLICATE")
        _validate_topology(self.exterior, self.holes)


def _points(ring: ParcelRing) -> tuple[tuple[Decimal, Decimal], ...]:
    return tuple((_canonical_metres(v.x, "COORDINATE_INVALID"),
                  _canonical_metres(v.y, "COORDINATE_INVALID")) for v in ring.vertices)


def _cross(a, b, c) -> Decimal:
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def _on_segment(p, a, b) -> bool:
    return _cross(a, b, p) == 0 and min(a[0], b[0]) <= p[0] <= max(a[0], b[0]) \
        and min(a[1], b[1]) <= p[1] <= max(a[1], b[1])


def _segments_intersect(a, b, c, d) -> bool:
    abc, abd, cda, cdb = _cross(a, b, c), _cross(a, b, d), _cross(c, d, a), _cross(c, d, b)
    return ((abc == 0 and _on_segment(c, a, b)) or (abd == 0 and _on_segment(d, a, b)) or
            (cda == 0 and _on_segment(a, c, d)) or (cdb == 0 and _on_segment(b, c, d)) or
            ((abc > 0) != (abd > 0) and (cda > 0) != (cdb > 0)))


def _self_intersects(points) -> bool:
    size = len(points)
    return any(_segments_intersect(points[i], points[(i + 1) % size], points[j], points[(j + 1) % size])
               for i in range(size) for j in range(i + 1, size)
               if j != (i + 1) % size and i != (j + 1) % size)


def _rings_intersect(a, b) -> bool:
    return any(_segments_intersect(a[i], a[(i + 1) % len(a)], b[j], b[(j + 1) % len(b)])
               for i in range(len(a)) for j in range(len(b)))


def _strictly_inside(point, ring) -> bool:
    if any(_on_segment(point, ring[i], ring[(i + 1) % len(ring)]) for i in range(len(ring))):
        return False
    inside = False
    for i, a in enumerate(ring):
        b = ring[(i + 1) % len(ring)]
        if (a[1] > point[1]) != (b[1] > point[1]) and \
                point[0] < (b[0] - a[0]) * (point[1] - a[1]) / (b[1] - a[1]) + a[0]:
            inside = not inside
    return inside


def _validate_topology(exterior: ParcelRing, holes: tuple[ParcelRing, ...]) -> None:
    outer = _points(exterior)
    area2 = sum(outer[i][0] * outer[(i + 1) % len(outer)][1] - outer[(i + 1) % len(outer)][0] * outer[i][1]
                for i in range(len(outer)))
    if area2 == 0 or _self_intersects(outer):
        raise GeometryCompatibilityError("RING_TOPOLOGY_INVALID")
    hole_points = []
    for hole in holes:
        points = _points(hole)
        area2 = sum(points[i][0] * points[(i + 1) % len(points)][1] - points[(i + 1) % len(points)][0] * points[i][1]
                    for i in range(len(points)))
        if area2 == 0 or _self_intersects(points):
            raise GeometryCompatibilityError("RING_TOPOLOGY_INVALID")
        if any(not _strictly_inside(point, outer) for point in points) or _rings_intersect(points, outer):
            raise GeometryCompatibilityError("HOLE_OUTSIDE_EXTERIOR")
        hole_points.append(points)
    for i, left in enumerate(hole_points):
        for right in hole_points[i + 1:]:
            if _rings_intersect(left, right) or _strictly_inside(left[0], right) or _strictly_inside(right[0], left):
                raise GeometryCompatibilityError("HOLE_OVERLAP")


@dataclass(frozen=True)
class CrsProvenance:
    source_crs: str
    calculation_crs: str
    calculation_is_projected: bool
    axis_unit_x: str
    axis_unit_y: str
    transformation_policy: str
    transformation_evidence_digest: str
    evidence_version: str = "m2-m6-transform-evidence/1.0"
    always_xy: bool = True
    source_geometry_digest: str = ""
    projected_coordinate_digest: str = ""
    local_coordinate_digest: str = ""
    origin_x: Decimal = Decimal(0)
    origin_y: Decimal = Decimal(0)
    operation_authority: str = "EPSG"
    operation_code: str = "unknown"

    def validate(self, calculation_crs: str, source_digest: str, origin_x: Decimal, origin_y: Decimal) -> None:
        if not self.source_crs.strip() or self.calculation_crs != calculation_crs \
                or not self.calculation_is_projected or self.axis_unit_x != "m" or self.axis_unit_y != "m" \
                or self.transformation_policy != TRANSFORMATION_POLICY or self.evidence_version != "m2-m6-transform-evidence/1.0" \
                or not self.always_xy or self.source_geometry_digest != source_digest \
                or self.origin_x != origin_x or self.origin_y != origin_y \
                or not self.operation_authority.strip() or not self.operation_code.strip():
            raise GeometryCompatibilityError("CRS_PROVENANCE_INVALID")
        hashes = (self.transformation_evidence_digest, self.source_geometry_digest,
                  self.projected_coordinate_digest, self.local_coordinate_digest)
        if any(len(value) != 64 or any(char not in "0123456789abcdef" for char in value) for value in hashes):
            raise GeometryCompatibilityError("CRS_PROVENANCE_DIGEST_INVALID")
        if self.transformation_evidence_digest != compute_transformation_evidence_digest(self):
            raise GeometryCompatibilityError("CRS_PROVENANCE_DIGEST_MISMATCH")


@dataclass(frozen=True)
class LandGeometryHandoff:
    project_id: UUID
    land_revision_id: UUID
    calculation_crs: str
    source_geometry_digest: str
    crs_provenance: CrsProvenance
    local_frame: LocalFrame
    precision: PrecisionProfile = PrecisionProfile()
    geometry_digest: str | None = None
    schema_version: str = SCHEMA_VERSION


@dataclass(frozen=True)
class EdgeMapping:
    edge_id: UUID
    parcel_edge_index: int


def projected_to_local(projected_x: Decimal | int | str, projected_y: Decimal | int | str,
                       origin_x: Decimal | int | str, origin_y: Decimal | int | str) -> tuple[Decimal, Decimal]:
    """Apply the approved translation exactly once and quantize once."""
    px = _decimal(projected_x, "PROJECTED_COORDINATE_INVALID")
    py = _decimal(projected_y, "PROJECTED_COORDINATE_INVALID")
    ox = _canonical_metres(origin_x, "LOCAL_ORIGIN_INVALID")
    oy = _canonical_metres(origin_y, "LOCAL_ORIGIN_INVALID")
    local_x = (px - ox).quantize(QUANTUM_M, rounding=ROUND_HALF_EVEN)
    local_y = (py - oy).quantize(QUANTUM_M, rounding=ROUND_HALF_EVEN)
    return (_canonical_metres(local_x, "COORDINATE_INVALID"),
            _canonical_metres(local_y, "COORDINATE_INVALID"))


def validate_handoff(handoff: LandGeometryHandoff, expected_project_id: UUID,
                     expected_land_revision_id: UUID, *, require_digest: bool = True) -> LandGeometryHandoff:
    if not isinstance(handoff, LandGeometryHandoff):
        raise GeometryCompatibilityError("HANDOFF_INVALID")
    if handoff.schema_version != SCHEMA_VERSION:
        raise GeometryCompatibilityError("SCHEMA_VERSION_UNSUPPORTED")
    _uuid(handoff.project_id, "PROJECT_BINDING_INVALID")
    _uuid(handoff.land_revision_id, "LAND_REVISION_INVALID")
    if handoff.project_id != expected_project_id:
        raise GeometryCompatibilityError("PROJECT_BINDING_MISMATCH")
    if handoff.land_revision_id != expected_land_revision_id:
        raise GeometryCompatibilityError("LAND_REVISION_MISMATCH")
    if not isinstance(handoff.calculation_crs, str) or not handoff.calculation_crs.strip():
        raise GeometryCompatibilityError("CALCULATION_CRS_INVALID")
    if not isinstance(handoff.crs_provenance, CrsProvenance):
        raise GeometryCompatibilityError("CRS_PROVENANCE_MISSING")
    handoff.crs_provenance.validate(handoff.calculation_crs, handoff.source_geometry_digest,
                                    handoff.local_frame.origin_x, handoff.local_frame.origin_y)
    if len(handoff.source_geometry_digest) != 64 or any(
            char not in "0123456789abcdef" for char in handoff.source_geometry_digest):
        raise GeometryCompatibilityError("SOURCE_GEOMETRY_DIGEST_INVALID")
    handoff.precision.validate()
    handoff.local_frame.validate()
    computed = compute_geometry_digest(handoff)
    if require_digest and handoff.geometry_digest != computed:
        raise GeometryCompatibilityError("GEOMETRY_DIGEST_MISMATCH")
    return handoff


def _field(tag: int, payload: bytes) -> bytes:
    return pack(">HI", tag, len(payload)) + payload


def _text(tag: int, value: str) -> bytes:
    return _field(tag, value.encode("utf-8", "strict"))


def _int64(tag: int, value: int) -> bytes:
    return _field(tag, pack(">q", value))


def compute_transformation_evidence_digest(value: CrsProvenance) -> str:
    chunks = [TRANSFORMATION_EVIDENCE_DOMAIN, _text(1, value.evidence_version), _text(2, value.source_crs),
              _text(3, value.calculation_crs), _int64(4, 1 if value.always_xy else 0),
              _field(5, bytes.fromhex(value.source_geometry_digest)),
              _field(6, bytes.fromhex(value.projected_coordinate_digest)),
              _field(7, bytes.fromhex(value.local_coordinate_digest)),
              _int64(8, _micrometres(_canonical_metres(value.origin_x, "LOCAL_ORIGIN_INVALID"))),
              _int64(9, _micrometres(_canonical_metres(value.origin_y, "LOCAL_ORIGIN_INVALID"))),
              _text(10, value.transformation_policy), _text(11, value.operation_authority),
              _text(12, value.operation_code)]
    return sha256(b"".join(chunks)).hexdigest()


def compute_geometry_digest(handoff: LandGeometryHandoff) -> str:
    """Canonical SHA-256 invariant to ring rotation and orientation.

    Stable identities, not array position, define physical geometry. Indexes are
    deliberately excluded; their bijection is validated independently.
    """
    chunks = [DIGEST_DOMAIN,
              _text(1, handoff.schema_version),
              _text(2, str(handoff.project_id)),
              _text(3, str(handoff.land_revision_id)),
              _text(4, handoff.calculation_crs),
              _int64(5, _micrometres(_canonical_metres(handoff.local_frame.origin_x, "LOCAL_ORIGIN_INVALID"))),
              _int64(6, _micrometres(_canonical_metres(handoff.local_frame.origin_y, "LOCAL_ORIGIN_INVALID"))),
              _text(7, handoff.precision.version),
              _field(8, bytes.fromhex(handoff.source_geometry_digest)),
              _int64(9, _micrometres(_canonical_metres(handoff.local_frame.affine.a, "LOCAL_AFFINE_INVALID"))),
              _int64(10, _micrometres(_canonical_metres(handoff.local_frame.affine.b, "LOCAL_AFFINE_INVALID"))),
              _int64(11, _micrometres(_canonical_metres(handoff.local_frame.affine.c, "LOCAL_AFFINE_INVALID"))),
              _int64(12, _micrometres(_canonical_metres(handoff.local_frame.affine.d, "LOCAL_AFFINE_INVALID"))),
              _int64(13, _micrometres(_canonical_metres(handoff.local_frame.affine.tx, "LOCAL_AFFINE_INVALID"))),
              _int64(14, _micrometres(_canonical_metres(handoff.local_frame.affine.ty, "LOCAL_AFFINE_INVALID"))),
              _text(15, handoff.crs_provenance.source_crs),
              _text(16, handoff.crs_provenance.transformation_policy),
              _field(17, bytes.fromhex(handoff.crs_provenance.transformation_evidence_digest))]
    rings = [(0, handoff.local_frame.exterior), *((1, ring) for ring in handoff.local_frame.holes)]
    for ring_type, ring in sorted(rings, key=lambda item: (item[0], str(item[1].ring_id))):
        ring.validate()
        chunks.extend((_int64(20, ring_type), _text(21, str(ring.ring_id))))
        for vertex in sorted(ring.vertices, key=lambda item: str(item.vertex_id)):
            chunks.extend((_text(22, str(vertex.vertex_id)),
                           _text(23, str(vertex.source_vertex_id)),
                           _int64(24, _micrometres(_canonical_metres(vertex.x, "COORDINATE_INVALID"))),
                           _int64(25, _micrometres(_canonical_metres(vertex.y, "COORDINATE_INVALID")))))
        for edge in sorted(ring.edges, key=lambda item: str(item.edge_id)):
            endpoints = sorted((str(edge.start_vertex_id), str(edge.end_vertex_id)))
            chunks.extend((_text(26, str(edge.edge_id)), _text(27, str(edge.source_edge_id)),
                           _text(28, endpoints[0]), _text(29, endpoints[1])))
    return sha256(b"".join(chunks)).hexdigest()


def area_tolerance(area_m2: float, perimeter_m: float) -> float:
    if not isfinite(area_m2) or not isfinite(perimeter_m) or area_m2 < 0 or perimeter_m < 0:
        raise GeometryCompatibilityError("AREA_TOLERANCE_INPUT_INVALID")
    return max(1e-10, 32 * ulp(area_m2), perimeter_m * 1e-8)


def bind_setbacks_to_m6_indices(exterior: ParcelRing, mappings: Iterable[EdgeMapping],
                                setbacks_by_edge_id: Mapping[UUID, Decimal | int | str]) -> tuple[Decimal, ...]:
    """Verify a bijection and return distances in M6 positional order."""
    exterior.validate()
    mapping_list = tuple(mappings)
    if len(mapping_list) != len(exterior.edges):
        raise GeometryCompatibilityError("EDGE_REMAP_INCOMPLETE")
    edge_ids = {edge.edge_id for edge in exterior.edges}
    mapped_ids = [item.edge_id for item in mapping_list]
    mapped_indexes = [item.parcel_edge_index for item in mapping_list]
    if set(mapped_ids) != edge_ids or len(set(mapped_ids)) != len(mapped_ids) \
            or set(mapped_indexes) != set(range(len(exterior.edges))) \
            or len(set(mapped_indexes)) != len(mapped_indexes):
        raise GeometryCompatibilityError("EDGE_REMAP_INVALID")
    if set(setbacks_by_edge_id) != edge_ids:
        raise GeometryCompatibilityError("SETBACK_EDGE_COVERAGE_MISSING")
    distances: list[Decimal | None] = [None] * len(exterior.edges)
    for mapping in mapping_list:
        distance = _decimal(setbacks_by_edge_id[mapping.edge_id], "SETBACK_INVALID")
        if distance < 0 or distance != distance.quantize(QUANTUM_M, rounding=ROUND_HALF_EVEN):
            raise GeometryCompatibilityError("SETBACK_INVALID")
        distances[mapping.parcel_edge_index] = distance
    return tuple(value for value in distances if value is not None)
