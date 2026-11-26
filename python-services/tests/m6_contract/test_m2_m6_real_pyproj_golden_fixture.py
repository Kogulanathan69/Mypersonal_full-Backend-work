"""Real PyProj preparation for the Captain-approved M2/M6 joint fixture.

This test never mocks PyProj. A successful run writes a provisional evidence
artifact under TestResults/python; it does not claim independent verification.
"""

from decimal import Decimal, ROUND_HALF_EVEN
from hashlib import sha256
import json
from math import hypot, isfinite
from pathlib import Path
import platform
import os
import tempfile
import unittest
from uuid import UUID

from app.geometry.compatibility.m2_m6_v1 import (
    AffineTranslation, CrsProvenance, EdgeMapping, LandGeometryHandoff, LocalFrame,
    ParcelEdge, ParcelRing, ParcelVertex, bind_setbacks_to_m6_indices,
    compute_geometry_digest, compute_transformation_evidence_digest, projected_to_local, validate_handoff,
)


LABEL = "PROVISIONAL — AWAITING INDEPENDENT M2/M6 VERIFICATION"
QUANTUM = Decimal("0.000001")
ROUND_TRIP_TOLERANCE_M = 0.001
PROJECT = UUID("10000000-0000-0000-0000-000000000001")
LAND = UUID("20000000-0000-0000-0000-000000000001")
EXTERIOR_RING = UUID("30000000-0000-0000-0000-000000000001")
HOLE_RING = UUID("30000000-0000-0000-0000-000000000002")
SOURCE_PATH = Path(__file__).parent / "fixtures" / "m2-m6-real-pyproj-source.v1.json"
EVIDENCE_PATH = Path(__file__).parents[3] / "TestResults" / "python" / "m2-m6-real-pyproj-provisional.json"


def _load_runtime():
    try:
        import pyproj
        import shapely
        from pyproj import CRS, Transformer
        from shapely import Polygon
        from shapely.validation import explain_validity
    except ImportError as error:
        raise RuntimeError(f"REAL_PYPROJ_DEPENDENCY_UNAVAILABLE: {error}") from error
    return pyproj, shapely, CRS, Transformer, Polygon, explain_validity


def _canonical(value: float) -> Decimal:
    return Decimal(str(value)).quantize(QUANTUM, rounding=ROUND_HALF_EVEN)


def _source_digest(source: dict) -> str:
    canonical = {"sourceCrs": source["sourceCrs"], "coordinateOrder": source["coordinateOrder"],
                 "exterior": source["exterior"], "holes": [source["hole"]]}
    payload = json.dumps(canonical, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return sha256(payload).hexdigest()


def _ring(source_items, edge_items, local, ring_id):
    vertices = tuple(ParcelVertex(UUID(item["vertexId"]), local[index][0], local[index][1],
                                  UUID(item["vertexId"])) for index, item in enumerate(source_items))
    if len(edge_items) != len(vertices):
        raise AssertionError("edge coverage mismatch")
    edges = tuple(ParcelEdge(UUID(edge["edgeId"]), UUID(edge["startVertexId"]), UUID(edge["endVertexId"]),
                             UUID(edge["sourceEdgeId"]), index) for index, edge in enumerate(edge_items))
    return ParcelRing(ring_id, vertices, edges)


def build_evidence() -> tuple[dict, LandGeometryHandoff]:
    pyproj, shapely, CRS, Transformer, Polygon, explain_validity = _load_runtime()
    source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
    source_crs = CRS.from_epsg(4326)
    calculation_crs = CRS.from_epsg(32644)
    if not source_crs.is_geographic:
        raise AssertionError("source CRS is not geographic")
    if not calculation_crs.is_projected:
        raise AssertionError("calculation CRS is not projected")
    axes = calculation_crs.axis_info[:2]
    if len(axes) != 2 or any(axis.unit_conversion_factor != 1.0 for axis in axes):
        raise AssertionError("calculation axes are not metres")
    forward = Transformer.from_crs(source_crs, calculation_crs, always_xy=True,
                                   allow_ballpark=False, only_best=True)
    inverse = Transformer.from_crs(calculation_crs, source_crs, always_xy=True,
                                   allow_ballpark=False, only_best=True)

    def transform(items):
        projected, local, errors = [], [], []
        for item in items:
            longitude, latitude = item["longitude"], item["latitude"]
            easting, northing = forward.transform(longitude, latitude, errcheck=True)
            if not isfinite(easting) or not isfinite(northing):
                raise AssertionError("non-finite projected coordinate")
            local_xy = projected_to_local(easting, northing,
                                           source["origin"]["eastingM"], source["origin"]["northingM"])
            lon2, lat2 = inverse.transform(easting, northing, errcheck=True)
            e2, n2 = forward.transform(lon2, lat2, errcheck=True)
            error_m = hypot(e2 - easting, n2 - northing)
            if error_m > ROUND_TRIP_TOLERANCE_M:
                raise AssertionError(f"round-trip error {error_m} m exceeds tolerance")
            projected.append((easting, northing))
            local.append(local_xy)
            errors.append(error_m)
        return projected, local, errors

    projected_exterior, local_exterior, exterior_errors = transform(source["exterior"])
    projected_hole, local_hole, hole_errors = transform(source["hole"])
    polygon = Polygon([(float(x), float(y)) for x, y in local_exterior],
                      [[(float(x), float(y)) for x, y in local_hole]])
    validity = explain_validity(polygon)
    if not polygon.is_valid:
        raise AssertionError(f"approved fixture polygon is invalid: {validity}")

    exterior = _ring(source["exterior"], source["exteriorEdges"], local_exterior, EXTERIOR_RING)
    hole = _ring(source["hole"], source["holeEdges"], local_hole, HOLE_RING)
    source_digest = _source_digest(source)
    operations = [{"name": operation.name, "accuracy": operation.accuracy,
                   "method": operation.method_name} for operation in forward.operations]
    transform_metadata = {"description": forward.description, "accuracy": forward.accuracy,
                          "areaOfUse": str(forward.area_of_use), "operations": operations}
    frame = LocalFrame(Decimal("370000.000000"), Decimal("765000.000000"),
                       AffineTranslation(Decimal(1), Decimal(0), Decimal(0), Decimal(1),
                                         Decimal("-370000.000000"), Decimal("-765000.000000")),
                       exterior, (hole,))
    coordinate_payload = lambda values: sha256(json.dumps([[format(x, "f"), format(y, "f")] for x, y in values],
        separators=(",", ":")).encode()).hexdigest()
    provenance = CrsProvenance("EPSG:4326", "EPSG:32644", True, "m", "m",
        "pyproj-always-xy-no-ballpark-only-best-v1", "00" * 32, source_geometry_digest=source_digest,
        projected_coordinate_digest=coordinate_payload((*projected_exterior, *projected_hole)),
        local_coordinate_digest=coordinate_payload((*local_exterior, *local_hole)),
        origin_x=frame.origin_x, origin_y=frame.origin_y, operation_authority="EPSG", operation_code="32644")
    evidence_digest = compute_transformation_evidence_digest(provenance)
    provenance = provenance.__class__(**{**provenance.__dict__, "transformation_evidence_digest": evidence_digest})
    handoff = LandGeometryHandoff(PROJECT, LAND, "EPSG:32644", source_digest, provenance, frame)
    handoff = handoff.__class__(**{**handoff.__dict__, "geometry_digest": compute_geometry_digest(handoff)})
    validate_handoff(handoff, PROJECT, LAND)

    setbacks = {UUID(edge["edgeId"]): Decimal(str(edge["setbackM"])).quantize(QUANTUM)
                for edge in source["exteriorEdges"]}
    mappings = tuple(EdgeMapping(edge.edge_id, edge.normalized_index) for edge in exterior.edges)
    bound = bind_setbacks_to_m6_indices(exterior, mappings, setbacks)

    def serial(items, projected, local, errors):
        return [{"vertexId": item["vertexId"], "source": [item["longitude"], item["latitude"]],
                 "projectedM": [x, y], "localM": [format(lx, "f"), format(ly, "f")],
                 "roundTripErrorM": error}
                for item, (x, y), (lx, ly), error in zip(items, projected, local, errors)]

    evidence = {
        "label": LABEL, "sourceFixture": SOURCE_PATH.name,
        "runtime": {"python": platform.python_version(), "pyproj": pyproj.__version__,
                    "PROJ": pyproj.proj_version_str, "shapely": shapely.__version__,
                    "GEOS": shapely.geos_version_string},
        "sourceCrs": "EPSG:4326", "calculationCrs": "EPSG:32644",
        "coordinateOrder": ["longitude", "latitude"], "origin": source["origin"],
        "transformation": transform_metadata, "exterior": serial(source["exterior"], projected_exterior,
                                                                   local_exterior, exterior_errors),
        "hole": serial(source["hole"], projected_hole, local_hole, hole_errors),
        "polygon": {"isValid": polygon.is_valid, "diagnostic": validity, "areaM2": polygon.area},
        "setbacksByEdgeIdM": {str(key): format(value, "f") for key, value in setbacks.items()},
        "setbacksByM6IndexM": [format(value, "f") for value in bound],
        "sourceGeometryDigest": source_digest, "transformationEvidenceDigest": evidence_digest,
        "landGeometryDigest": handoff.geometry_digest,
        "maximumRoundTripErrorM": max((*exterior_errors, *hole_errors)),
    }
    return evidence, handoff


class RealPyProjGoldenFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evidence, cls.handoff = build_evidence()
        EVIDENCE_PATH.parent.mkdir(parents=True, exist_ok=True)
        payload = json.dumps(cls.evidence, indent=2, ensure_ascii=False) + "\n"
        temporary = None
        try:
            with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=EVIDENCE_PATH.parent,
                                             prefix=EVIDENCE_PATH.name + ".", suffix=".tmp", delete=False) as stream:
                temporary = Path(stream.name); stream.write(payload); stream.flush(); os.fsync(stream.fileno())
            os.replace(temporary, EVIDENCE_PATH)
        finally:
            if temporary is not None and temporary.exists(): temporary.unlink()

    def test_real_transform_and_topology(self):
        self.assertEqual(LABEL, self.evidence["label"])
        self.assertTrue(self.evidence["polygon"]["isValid"])
        self.assertLessEqual(self.evidence["maximumRoundTripErrorM"], ROUND_TRIP_TOLERANCE_M)

    def test_quantization_and_no_double_translation(self):
        for ring in (self.handoff.local_frame.exterior, *self.handoff.local_frame.holes):
            for vertex in ring.vertices:
                self.assertEqual(vertex.x, vertex.x.quantize(QUANTUM))
                self.assertEqual(vertex.y, vertex.y.quantize(QUANTUM))
        # Exact single translation: every local vertex equals (projected - origin) quantized once.
        # (Replaces an arbitrary |local| < 1000 m bound: the fixture origin (370000, 765000) is ~2.95 km
        # west of the parcel in EPSG:32644, which is a valid translation-only frame.)
        origin_x = Decimal(str(self.evidence["origin"]["eastingM"]))
        origin_y = Decimal(str(self.evidence["origin"]["northingM"]))
        items = (*self.evidence["exterior"], *self.evidence["hole"])
        self.assertTrue(items)
        for item in items:
            px, py = (Decimal(str(value)) for value in item["projectedM"])
            expected = ((px - origin_x).quantize(QUANTUM, rounding=ROUND_HALF_EVEN),
                        (py - origin_y).quantize(QUANTUM, rounding=ROUND_HALF_EVEN))
            self.assertEqual(expected, tuple(Decimal(value) for value in item["localM"]))
            # A second translation would move the vertex ~origin away from its projected-minus-origin value.
            self.assertNotEqual(expected, ((px - 2 * origin_x).quantize(QUANTUM), (py - 2 * origin_y).quantize(QUANTUM)))

    def test_edge_identity_and_unequal_setbacks(self):
        source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
        expected = {edge["edgeId"]: format(Decimal(str(edge["setbackM"])).quantize(QUANTUM), "f")
                    for edge in source["exteriorEdges"]}
        self.assertEqual(expected, self.evidence["setbacksByEdgeIdM"])
        self.assertEqual(5, len(set(expected.values())))
        ring = self.handoff.local_frame.exterior
        physical = {frozenset((edge.start_vertex_id, edge.end_vertex_id)): edge.edge_id for edge in ring.edges}
        for order in (ring.vertices[2:] + ring.vertices[:2], tuple(reversed(ring.vertices))):
            remapped = []
            for index, vertex in enumerate(order):
                next_vertex = order[(index + 1) % len(order)]
                remapped.append(physical[frozenset((vertex.vertex_id, next_vertex.vertex_id))])
            self.assertEqual(set(expected), {str(edge_id) for edge_id in remapped})
            self.assertEqual(expected, {str(edge_id): self.evidence["setbacksByEdgeIdM"][str(edge_id)]
                                        for edge_id in remapped})


if __name__ == "__main__":
    unittest.main()
