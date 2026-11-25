"""Captain preliminary 2D plan-geometry validation (server-side evidence for saves and edits).

Checks the M3 draft PlanGeometry against the confirmed metric land boundary only:
* the strict M3 schema (closed rings, wall/opening references, unique entity IDs);
* one floor at level 0 (the MVP scope);
* every room is a valid, simple, positive-area polygon inside the parcel (and inside the buildable footprint when
  setback distances are supplied);
* rooms do not overlap (beyond a 1e-6 m2 numerical tolerance);
* openings fit inside their parent wall;
* optional requirement match: room counts per type equal the expected counts.

It establishes NO regulatory compliance, structural soundness, physical access or construction readiness.
"""
import json
from app.internal.openings import validate_openings
from collections import Counter
from hashlib import sha256
from typing import Annotated, Literal

from pydantic import Field

from app.geometry.footprint.contracts import ContractModel, MetricLand, NonNilUUID
from app.geometry.footprint.parcel import ConfirmedMetricParcel, ParcelError, validate_confirmed_parcel
from app.geometry.footprint.setbacks import FootprintError, build_footprint
from app.plan_editor.schemas.geometry import PlanGeometry

SCHEMA = "captain-plan-validation/1"
VALIDATOR_VERSION = "captain-plan-validation/1"
OVERLAP_TOLERANCE_M2 = 1e-6
COVER_TOLERANCE_M = 1e-6

_MESSAGES = {
    "PLAN_SCHEMA_INVALID": "The plan geometry violates the draft schema.",
    "LAND_INVALID": "The confirmed land boundary failed validation.",
    "FLOOR_SCOPE_UNSUPPORTED": "Only a single ground floor (level 0) is supported.",
    "ROOM_POLYGON_INVALID": "A room polygon is not a valid simple polygon with positive area.",
    "ROOM_OUTSIDE_PARCEL": "A room lies outside the confirmed land boundary.",
    "ROOM_OUTSIDE_BUILDABLE_AREA": "A room lies outside the buildable area implied by the supplied setbacks.",
    "ROOMS_OVERLAP": "Two rooms overlap.",
    "OPENING_OUTSIDE_WALL": "An opening does not fit inside its wall.",
    "OPENING_WALL_NOT_FOUND": "An opening references a wall that does not exist.",
    "OPENINGS_OVERLAP": "Two openings overlap on the same wall.",
    "WINDOW_NOT_ON_EXTERIOR": "A window does not open from a room to the outside.",
    "DOOR_CONNECTS_NO_SPACES": "A door does not connect two different spaces.",
    "OPENING_CORNER_CLEARANCE": "An opening is closer to a wall corner than the design preference.",
    "PHYSICAL_ACCESS_NOT_EVALUATED": "The plan has no doors; physical access was not evaluated.",
    "NO_ENTRANCE": "No door opens to the outside.",
    "ROOM_NOT_REACHABLE": "At least one room cannot be reached through doors from an entrance.",
    "ROOM_COUNTS_MISMATCH": "Room counts per type differ from the saved requirements.",
    "SETBACKS_INVALID": "The supplied setback distances cannot be applied to this parcel.",
    "NO_ROOMS": "The plan contains no rooms.",
}

Distance = Annotated[float, Field(ge=0, allow_inf_nan=False)]


class PlanValidationRequest(ContractModel):
    schemaVersion: Literal["captain-plan-validation/1"]
    projectId: NonNilUUID
    landRevisionId: NonNilUUID
    land: MetricLand
    setbackDistancesM: list[Distance] | None = Field(default=None, max_length=128)
    expectedRoomCounts: dict[str, Annotated[int, Field(ge=0, le=100)]] | None = None
    geometry: dict


class DependencyUnavailable(RuntimeError):
    pass


# Non-blocking findings: they require explicit acknowledgement downstream (apply/confirm), never silent acceptance.
WARNING_CODES = frozenset({"ROOM_COUNTS_MISMATCH", "OPENING_CORNER_CLEARANCE", "PHYSICAL_ACCESS_NOT_EVALUATED", "NO_ENTRANCE",
                           "ROOM_NOT_REACHABLE"})


def canonical_hash(value: object) -> str:
    return sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def _result(valid: bool, requirements_match: bool | None, codes: list[str], geometry_hash: str | None) -> dict:
    unique = list(dict.fromkeys(codes))
    return {
        "schemaVersion": SCHEMA,
        "validatorVersion": VALIDATOR_VERSION,
        "geometryValid": valid,
        "requirementsMatch": requirements_match,
        "geometryHash": geometry_hash,
        # A room-count mismatch is reported via requirementsMatch=false and needs explicit acknowledgement downstream;
        # it is a WARNING. Every geometry failure is CRITICAL (blocking).
        "diagnostics": [{"code": c, "severity": "WARNING" if c in WARNING_CODES else "CRITICAL",
                         "message": _MESSAGES[c]} for c in unique],
    }


def validate_plan(request: PlanValidationRequest) -> dict:
    try:
        from shapely.geometry import Polygon
    except ImportError:
        raise DependencyUnavailable() from None

    try:
        # Strict JSON-mode validation (no type coercion), as for generated alternatives.
        geometry = PlanGeometry.model_validate_json(json.dumps(request.geometry), strict=True)
    except ValueError:
        return _result(False, None, ["PLAN_SCHEMA_INVALID"], None)
    geometry_hash = canonical_hash(request.geometry)
    if str(geometry.projectId) != str(request.projectId) or str(geometry.landRevisionId) != str(request.landRevisionId):
        return _result(False, None, ["PLAN_SCHEMA_INVALID"], geometry_hash)

    try:
        parcel = validate_confirmed_parcel(
            ConfirmedMetricParcel(projectId=request.projectId, landRevisionId=request.landRevisionId, land=request.land),
            request.projectId, request.landRevisionId)
    except (ParcelError, ValueError):
        return _result(False, None, ["LAND_INVALID"], geometry_hash)

    codes: list[str] = []
    buildable = None
    if request.setbackDistancesM is not None:
        try:
            if len(request.setbackDistancesM) != len(parcel.edges):
                raise FootprintError("SETBACK_INVALID")
            buildable = build_footprint(parcel, tuple(request.setbackDistancesM)).candidate
        except FootprintError:
            codes.append("SETBACKS_INVALID")

    if len(geometry.floors) != 1 or geometry.floors[0].level != 0:
        return _result(False, None, ["FLOOR_SCOPE_UNSUPPORTED"], geometry_hash)
    floor = geometry.floors[0]
    if not floor.rooms:
        codes.append("NO_ROOMS")

    shapes = []
    for room in floor.rooms:
        polygon = Polygon([(p.x, p.y) for p in room.polygon])
        if not polygon.is_valid or polygon.area <= 0:
            codes.append("ROOM_POLYGON_INVALID")
            continue
        if not parcel.polygon.buffer(COVER_TOLERANCE_M).covers(polygon):
            codes.append("ROOM_OUTSIDE_PARCEL")
        elif buildable is not None and not buildable.buffer(COVER_TOLERANCE_M).covers(polygon):
            codes.append("ROOM_OUTSIDE_BUILDABLE_AREA")
        shapes.append(polygon)
    for index, first in enumerate(shapes):
        for second in shapes[index + 1:]:
            if first.intersection(second).area > OVERLAP_TOLERANCE_M2:
                codes.append("ROOMS_OVERLAP")

    # Openings and physical access (shared with generation; see app.internal.openings).
    codes.extend(code for code, _ in validate_openings(request.geometry["floors"][0]))

    requirements_match = None
    if request.expectedRoomCounts is not None:
        actual = Counter(room.type for room in floor.rooms)
        expected = Counter({k: v for k, v in request.expectedRoomCounts.items() if v > 0})
        requirements_match = actual == expected
        if not requirements_match:
            codes.append("ROOM_COUNTS_MISMATCH")

    return _result(not any(c not in WARNING_CODES for c in codes), requirements_match, codes, geometry_hash)
