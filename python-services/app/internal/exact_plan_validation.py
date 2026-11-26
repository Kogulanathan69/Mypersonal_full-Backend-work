"""BM-INT-01: exact UTF-8 plan geometry validation boundary."""

import json
import math
from typing import Literal

from pydantic import Field

from app.internal.plan_validation import (
    PlanValidationRequest,
    validate_plan,
)
from app.validation.geometry_hash import (
    compute_geometry_hash,
    matches_geometry_hash,
)

SCHEMA = "captain-plan-validation/2"


class ExactPlanValidationRequest(PlanValidationRequest):
    schemaVersion: Literal["captain-plan-validation/2"]
    geometry: None = Field(default=None, exclude=True)
    geometryJson: str = Field(min_length=2, max_length=1048576)
    geometryHash: str = Field(min_length=71, max_length=71)


class GeometryBindingError(ValueError):
    """Invalid or mismatched exact geometry payload."""


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise GeometryBindingError("Duplicate JSON property")
        result[key] = value
    return result


def _reject_constant(value):
    raise GeometryBindingError("Non-finite JSON constant")


MAX_JSON_DEPTH = 64


def _finite_float(value):
    number = float(value)
    if not math.isfinite(number):
        raise GeometryBindingError("Non-finite JSON number")
    return number


def _check_json_depth(raw):
    depth = 0
    in_string = False
    escaped = False

    for character in raw:
        if in_string:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                in_string = False
            continue

        if character == '"':
            in_string = True
        elif character in "{[":
            depth += 1
            if depth > MAX_JSON_DEPTH:
                raise GeometryBindingError("JSON depth exceeded")
        elif character in "}]":
            depth -= 1
            if depth < 0:
                raise GeometryBindingError("Invalid JSON nesting")

    if depth != 0 or in_string:
        raise GeometryBindingError("Invalid JSON structure")


def _validate_unicode_scalars(value):
    """Reject unpaired Unicode surrogates in decoded JSON."""
    pending = [value]

    while pending:
        current = pending.pop()

        if isinstance(current, str):
            current.encode("utf-8", errors="strict")
        elif isinstance(current, dict):
            pending.extend(current.keys())
            pending.extend(current.values())
        elif isinstance(current, list):
            pending.extend(current)


def validate_exact_plan(request: ExactPlanValidationRequest) -> dict:
    try:
        if "geometry" in request.model_fields_set:
            raise GeometryBindingError("Ambiguous geometry payload")

        raw = request.geometryJson

        if len(raw.encode("utf-8", errors="strict")) > 1048576:
            raise GeometryBindingError("Geometry payload too large")

        if not matches_geometry_hash(raw, request.geometryHash):
            raise GeometryBindingError("Geometry hash mismatch")

        _check_json_depth(raw)

        geometry = json.loads(
            raw,
            object_pairs_hook=_unique_object,
            parse_constant=_reject_constant,
            parse_float=_finite_float,
        )

        _validate_unicode_scalars(geometry)

        if not isinstance(geometry, dict):
            raise GeometryBindingError("Geometry must be an object")

        legacy_request = PlanValidationRequest(
            schemaVersion="captain-plan-validation/1",
            projectId=request.projectId,
            landRevisionId=request.landRevisionId,
            land=request.land,
            setbackDistancesM=request.setbackDistancesM,
            expectedRoomCounts=request.expectedRoomCounts,
            geometry=geometry,
        )

    except (ValueError, UnicodeError, RecursionError, OverflowError):
        raise GeometryBindingError("Invalid geometry binding") from None

    result = validate_plan(legacy_request)
    result["schemaVersion"] = SCHEMA
    result["validatorVersion"] = SCHEMA
    result["geometryHash"] = compute_geometry_hash(raw)
    return result
