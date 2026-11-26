"""Captain coordinate frame for land boundaries (Captain-local, versioned; JC-01 shared contract not ratified).

Pipeline:  source CRS coordinates (explicit, never guessed)
           --(M2 normalize_land_crs: PROJ, no ballpark, best transformation only)--> projected metric CRS
           --(explicit anchor = projected min corner floored to 1 m)--> local Cartesian metres (x = grid east, y = grid north).

* ``LOCAL`` source: local engineering coordinates with an explicit unit (m or ft); no geographic referencing claimed.
* Calculation CRS: explicit, or AUTO-SELECTED only for geographic sources inside Sri Lanka -> EPSG:5235
  (SLD99 / Sri Lanka Grid 1999), recorded as such. Anything else without an explicit projected CRS is rejected.
* Canonical geometry digest (cross-runtime): ring opened, rotated to start at the lexicographically smallest
  vertex, oriented counter-clockwise; numbers as fixed 6-decimal text; SHA-256 over ``FRAME_VERSION|crs|anchor|ring``.
* Physical-edge ids: UUIDv5 over (frame id, sorted endpoint texts) -> invariant to ring rotation and reversal.
* Edge split lineage: a new edge lying on a previous revision's edge records that edge as its parent.
"""
from __future__ import annotations

import hashlib
import math
import uuid
from dataclasses import dataclass

from app.geometry.land.crs import normalize_land_crs
from app.geometry.land.validation import LandGeometryError, validate_land_boundary

FRAME_VERSION = "captain-frame-v1"
EDGE_NAMESPACE = uuid.UUID("7b1c9e2a-3f4d-5e6f-8a9b-0c1d2e3f4a5b")
SRI_LANKA_BOUNDS = (79.5, 5.8, 82.0, 10.0)  # lon/lat
SRI_LANKA_GRID = "EPSG:5235"
FEET_TO_METRES = 0.3048
LINEAGE_TOLERANCE_M = 1e-4


def fmt(value: float) -> str:
    text = f"{value:.6f}"
    return "0.000000" if text == "-0.000000" else text


def canonical_ring(points: list[tuple[float, float]]) -> list[tuple[float, float]]:
    ring = [(round(x, 6), round(y, 6)) for x, y in points]
    if ring and ring[0] == ring[-1]:
        ring = ring[:-1]
    area2 = sum(x1 * y2 - x2 * y1 for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]))
    if area2 < 0:
        ring = list(reversed(ring))
    start = min(range(len(ring)), key=lambda i: (fmt(ring[i][0]), fmt(ring[i][1])))
    return ring[start:] + ring[:start]


def geometry_digest(crs: str, anchor: tuple[float, float], local_ring: list[tuple[float, float]]) -> str:
    ring = ";".join(f"{fmt(x)},{fmt(y)}" for x, y in canonical_ring(local_ring))
    payload = f"{FRAME_VERSION}|{crs}|{fmt(anchor[0])},{fmt(anchor[1])}|{ring}"
    return hashlib.sha256(payload.encode("ascii")).hexdigest()


def edge_id(frame_id: str, a: tuple[float, float], b: tuple[float, float]) -> str:
    ends = sorted([f"{fmt(a[0])},{fmt(a[1])}", f"{fmt(b[0])},{fmt(b[1])}"])
    return str(uuid.uuid5(EDGE_NAMESPACE, f"{frame_id}|{ends[0]}|{ends[1]}"))


@dataclass(frozen=True)
class Frame:
    document: dict


def _on_segment(p, a, b) -> bool:
    (px, py), (ax, ay), (bx, by) = p, a, b
    cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax)
    length = math.hypot(bx - ax, by - ay)
    if length == 0 or abs(cross) / length > LINEAGE_TOLERANCE_M:
        return False
    dot = (px - ax) * (bx - ax) + (py - ay) * (by - ay)
    return -LINEAGE_TOLERANCE_M <= dot / length <= length + LINEAGE_TOLERANCE_M


def build_frame(source_crs: str, source_unit: str | None, calculation_crs: str | None, exterior: list,
                previous_edges: list[dict] | None = None) -> dict:
    if not isinstance(source_crs, str) or not source_crs.strip():
        raise LandGeometryError("SOURCE_CRS_REQUIRED")
    ring = [(float(p[0]), float(p[1])) for p in exterior]
    if len(ring) < 3:
        raise LandGeometryError("RING_TOO_SHORT")
    closed = ring if ring[0] == ring[-1] else ring + [ring[0]]
    provenance: dict = {"frameVersion": FRAME_VERSION}

    if source_crs.strip().upper() == "LOCAL":
        if source_unit not in ("m", "ft"):
            raise LandGeometryError("SOURCE_UNIT_REQUIRED")
        factor = 1.0 if source_unit == "m" else FEET_TO_METRES
        projected = [(x * factor, y * factor) for x, y in closed]
        validate_land_boundary(tuple(projected), ())
        crs = "LOCAL"
        provenance.update({"kind": "LOCAL_ENGINEERING", "georeferenced": False, "unitConversionFactor": factor})
    else:
        from pyproj import CRS, Transformer, proj_version_str
        try:
            source = CRS.from_user_input(source_crs)
        except Exception:
            raise LandGeometryError("UNSUPPORTED_OR_INVALID_CRS") from None
        selection = "EXPLICIT"
        if not calculation_crs:
            lons, lats = [x for x, _ in closed], [y for _, y in closed]
            w, s, e, n = SRI_LANKA_BOUNDS
            if source.is_geographic and w <= min(lons) and max(lons) <= e and s <= min(lats) and max(lats) <= n:
                calculation_crs, selection = SRI_LANKA_GRID, "AUTO_SELECTED_SRI_LANKA_GRID"
            elif source.is_projected:
                calculation_crs, selection = source_crs, "SOURCE_IS_PROJECTED"
            else:
                raise LandGeometryError("CALCULATION_CRS_REQUIRED")
        result = normalize_land_crs([list(p) for p in closed], source_crs, calculation_crs)
        projected = list(result.metric_geometry.exterior)
        transformer = Transformer.from_crs(CRS.from_user_input(source_crs), CRS.from_user_input(calculation_crs), always_xy=True,
                                           allow_ballpark=False, only_best=True)
        crs = result.calculation_crs
        provenance.update({"kind": "GEOREFERENCED", "georeferenced": True, "sourceCrs": result.source_crs,
                           "calculationCrs": crs, "calculationCrsSelection": selection, "projVersion": proj_version_str,
                           "transformation": transformer.description[:200],
                           "transformationAccuracyM": transformer.accuracy if transformer.accuracy >= 0 else None,
                           "wgs84GeoJson": result.geojson})

    # LOCAL coordinates are kept as supplied (anchor 0,0); projected frames get an explicit anchor (min corner, 1 m).
    anchor = (0, 0) if crs == "LOCAL" else (math.floor(min(x for x, _ in projected)), math.floor(min(y for _, y in projected)))
    local = [(round(x - anchor[0], 6), round(y - anchor[1], 6)) for x, y in projected]
    open_local = local[:-1] if local[0] == local[-1] else local
    digest = geometry_digest(crs, anchor, open_local)
    frame_id = str(uuid.uuid5(EDGE_NAMESPACE, f"{FRAME_VERSION}|{crs}|{fmt(anchor[0])},{fmt(anchor[1])}"))
    canon = canonical_ring(open_local)
    edges = []
    for a, b in zip(canon, canon[1:] + canon[:1]):
        parent = None
        for previous in previous_edges or []:
            pa, pb = tuple(previous["start"]), tuple(previous["end"])
            if _on_segment(a, pa, pb) and _on_segment(b, pa, pb):
                parent = previous["edgeId"]
                break
        bearing = (math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])) + 360) % 360
        edges.append({"edgeId": edge_id(frame_id, a, b), "start": [a[0], a[1]], "end": [b[0], b[1]],
                      "lengthM": round(math.hypot(b[0] - a[0], b[1] - a[1]), 6), "gridBearingDeg": round(bearing, 6),
                      "role": "UNASSIGNED", "parentEdgeId": parent})
    return {
        "frame": {"frameId": frame_id, "crs": crs, "anchor": [anchor[0], anchor[1]], "unit": "m",
                  "axes": {"x": "gridEast", "y": "gridNorth"}, **provenance},
        "sourceExterior": [[x, y] for x, y in ring],
        "localExterior": [{"x": x, "y": y} for x, y in open_local],
        "geometryDigest": digest,
        "edges": edges,
    }


# --- internal API contract ---------------------------------------------------------------------------------------
from typing import Literal  # noqa: E402

from pydantic import BaseModel, ConfigDict, Field  # noqa: E402


class _PreviousEdge(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    edgeId: str = Field(min_length=36, max_length=36)
    start: list[float] = Field(min_length=2, max_length=2)
    end: list[float] = Field(min_length=2, max_length=2)


class LandFrameRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)
    schemaVersion: Literal["captain-land-frame/1"]
    sourceCrs: str = Field(min_length=1, max_length=64)
    sourceUnit: Literal["m", "ft"] | None = None
    calculationCrs: str | None = Field(default=None, min_length=1, max_length=64)
    exterior: list[list[float]] = Field(min_length=3, max_length=1000)
    previousEdges: list[_PreviousEdge] = Field(default_factory=list, max_length=1000)


def frame_from_request(request: LandFrameRequest) -> dict:
    if any(len(p) != 2 or not all(math.isfinite(v) for v in p) for p in request.exterior):
        raise LandGeometryError("INVALID_SOURCE_COORDINATE")
    return {"schemaVersion": "captain-land-frame/1",
            **build_frame(request.sourceCrs, request.sourceUnit, request.calculationCrs, request.exterior,
                          [e.model_dump() for e in request.previousEdges])}
