"""Internal D02 evaluation with the existing D01 response envelope."""

from dataclasses import dataclass
from typing import Literal, TYPE_CHECKING

from pydantic import StrictBool, ValidationError

from app.geometry.footprint.area import AreaAssumptionError, AreaAssumptions, required_area_m2
from app.geometry.footprint.contracts import (
    CoordinatePoint,
    Diagnostic,
    EvidenceStatus,
    FeasibilityResult,
    MetricLand,
    PolygonBoundary,
    RevisionBinding,
)
from app.geometry.footprint.parcel import ConfirmedMetricParcel, ParcelError, validate_confirmed_parcel
from app.geometry.footprint.setbacks import FootprintError, area_tolerance_m2, build_footprint
from app.plans.generation.contracts import GenerationInput, RegulatoryEvidence, ReviewedConstraint

if TYPE_CHECKING:
    from shapely.geometry import LinearRing, Polygon

POLICY_VERSION = "d02-finite-edge-synthetic-v1"
FeasibilityStatus = Literal["FEASIBLE", "INFEASIBLE", "NEEDS_REVIEW", "UNSUPPORTED"]

_MESSAGES = {
    "FEASIBILITY_INPUT_INVALID": "The feasibility input violates the generation contract.",
    "INPUT_REVISION_MISMATCH": "The supplied snapshots do not match the exact feasibility input.",
    "POLYGON_INVALID": "The confirmed parcel failed metric polygon validation.",
    "PARCEL_COMPLEXITY_UNSUPPORTED": "The parcel exceeds the supported geometry complexity.",
    "PARCEL_PRECISION_UNSUPPORTED": "The parcel falls outside the supported numerical limits.",
    "SETBACK_EVIDENCE_MISSING": "Applicable setback evidence is missing for one or more parcel edges.",
    "REGULATORY_EVIDENCE_CONFLICT": "The supplied setback evidence contains an unresolved conflict.",
    "SETBACK_EVIDENCE_UNVERIFIED": "A matching synthetic reviewed setback snapshot is unavailable.",
    "SETBACK_INVALID": "The supplied setback evidence violates the supported contract.",
    "SETBACK_PRECISION_UNSUPPORTED": "A supplied setback falls outside the supported numerical limits.",
    "REQUIRED_AREA_POLICY_UNAVAILABLE": "Applicable required-area assumptions are unavailable.",
    "FOOTPRINT_DISCONNECTED": "The footprint may have multiple components that the draft contract cannot represent.",
    "FOOTPRINT_GEOMETRY_UNSUPPORTED": "The calculated geometry is outside the supported footprint type.",
    "FOOTPRINT_COMPLEXITY_UNSUPPORTED": "The footprint exceeds the draft response complexity limit.",
    "FOOTPRINT_CALCULATION_FAILED": "The buildable footprint could not be calculated safely.",
    "FOOTPRINT_DEPENDENCY_UNAVAILABLE": "The geometry calculation dependency is unavailable.",
    "BUILDABLE_AREA_INSUFFICIENT": "Even the optimistic buildable area is below the required area.",
    "EMPTY_BUILDABLE_REGION": "The supplied setbacks leave no positive-area buildable region.",
    "FOOTPRINT_PRECISION_INCONCLUSIVE": "The computed area bounds do not support a definite decision.",
    "PRELIMINARY_AREA_ONLY": "Synthetic setbacks allow enough total area. Room placement and compliance were not evaluated.",
}


def _diagnostic(code: str) -> Diagnostic:
    return Diagnostic(
        code=code,
        severity="WARNING" if code == "PRELIMINARY_AREA_ONLY" else "CRITICAL",
        message=_MESSAGES[code],
    )


class FeasibilityInputError(ValueError):
    """An invalid request cannot supply trusted identities for a D01 result."""

    def __init__(self) -> None:
        self.diagnostic = _diagnostic("FEASIBILITY_INPUT_INVALID")
        super().__init__(self.diagnostic.message)


class ReviewedSetbackSnapshot(RevisionBinding):
    """Explicit synthetic evidence selected by the caller for local development."""

    synthetic: StrictBool
    regulatoryEvidence: RegulatoryEvidence


@dataclass(frozen=True)
class FeasibilityAssessment:
    """The D01 result plus local accounting and trusted synthetic provenance."""

    result: FeasibilityResult
    removedAreaM2: float | None = None
    areaShortfallM2: float | None = None
    optimisticBuildableAreaM2: float | None = None
    appliedConstraints: tuple[ReviewedConstraint, ...] = ()


class SetbackEvidenceError(ValueError):
    """A fixed diagnostic code for rejected or incomplete evidence."""


def _setback_rules(
    evidence: RegulatoryEvidence, edge_count: int
) -> tuple[tuple[float, ...], tuple[ReviewedConstraint, ...]]:
    distances: dict[int, float] = {}
    applied = []
    for constraint in evidence.constraints:
        edge = constraint.parcelEdgeIndex
        if not 0 <= edge < edge_count:
            raise SetbackEvidenceError("SETBACK_INVALID")
        if constraint.applicability.status != "APPLICABLE":
            continue
        if edge in distances and distances[edge] != constraint.distanceM:
            raise SetbackEvidenceError("REGULATORY_EVIDENCE_CONFLICT")
        distances[edge] = constraint.distanceM
        applied.append(constraint)
    if len(distances) != edge_count:
        raise SetbackEvidenceError("SETBACK_EVIDENCE_MISSING")
    return (
        tuple(distances[i] for i in range(edge_count)),
        tuple(sorted(applied, key=lambda rule: (rule.parcelEdgeIndex, rule.ruleId))),
    )


def _metric_footprint(polygon: "Polygon", land: MetricLand) -> MetricLand:
    if len(polygon.interiors) > 100 or any(
        len(ring.coords) > 10_000 for ring in (polygon.exterior, *polygon.interiors)
    ):
        raise FootprintError("FOOTPRINT_COMPLEXITY_UNSUPPORTED")

    def points(ring: "LinearRing") -> list[CoordinatePoint]:
        return [CoordinatePoint(x=x, y=y) for x, y in ring.coords]

    return MetricLand(
        schemaVersion=land.schemaVersion,
        coordinateSystem=land.coordinateSystem,
        boundary=PolygonBoundary(
            exterior=points(polygon.exterior),
            holes=[points(ring) for ring in polygon.interiors],
        ),
    )


def assess_feasibility(
    request: GenerationInput,
    *,
    confirmed_parcel: ConfirmedMetricParcel,
    reviewed_setbacks: ReviewedSetbackSnapshot | None = None,
    area_assumptions: AreaAssumptions | None = None,
) -> FeasibilityAssessment:
    """Evaluate a preliminary local footprint with exact supplied snapshot binding.

    The caller owns the selection of the confirmed parcel and synthetic reviewed
    snapshot. This callable does not establish ownership, freshness or legal review.
    """
    try:
        request = GenerationInput.model_validate(request)
    except ValidationError:
        raise FeasibilityInputError() from None

    revision = (request.projectId, request.landRevisionId, request.requirementsRevisionId)
    land_area: float | None = None
    required_area: float | None = None
    rule_set_version: str | None = None
    optimistic_area: float | None = None
    requested_status = request.regulatoryEvidence.status
    evidence_status: EvidenceStatus = "UNVERIFIED" if requested_status == "REVIEWED" else requested_status

    def finish(
        status: FeasibilityStatus,
        *codes: str,
        footprint: MetricLand | None = None,
        buildable_area: float | None = None,
        shortfall: float | None = None,
        rules: tuple[ReviewedConstraint, ...] = (),
    ) -> FeasibilityAssessment:
        result = FeasibilityResult(
            schemaVersion="m6-feasibility-result/0.1-draft",
            projectId=revision[0],
            landRevisionId=revision[1],
            requirementsRevisionId=revision[2],
            status=status,
            buildableFootprint=footprint,
            landAreaM2=land_area,
            buildableAreaM2=buildable_area,
            requiredAreaM2=required_area,
            regulatoryEvidenceStatus=evidence_status,
            ruleSetVersion=rule_set_version,
            feasibilityPolicyVersion=POLICY_VERSION,
            diagnostics=[_diagnostic(code) for code in codes],
        )
        removed = land_area - buildable_area if land_area is not None and buildable_area is not None else None
        return FeasibilityAssessment(
            result=result,
            removedAreaM2=removed,
            areaShortfallM2=shortfall,
            optimisticBuildableAreaM2=optimistic_area,
            appliedConstraints=rules,
        )

    try:
        from shapely.errors import ShapelyError
    except ImportError:
        return finish("NEEDS_REVIEW", "FOOTPRINT_DEPENDENCY_UNAVAILABLE")

    try:
        parcel = validate_confirmed_parcel(confirmed_parcel, revision[0], revision[1])
    except ParcelError as error:
        code = str(error)
        status = "UNSUPPORTED" if code.endswith("_UNSUPPORTED") else "NEEDS_REVIEW"
        return finish(status, code)
    except ValidationError:
        return finish("NEEDS_REVIEW", "POLYGON_INVALID")
    except ImportError:
        return finish("NEEDS_REVIEW", "FOOTPRINT_DEPENDENCY_UNAVAILABLE")
    except ShapelyError:
        return finish("NEEDS_REVIEW", "FOOTPRINT_CALCULATION_FAILED")
    if parcel.land != request.land:
        return finish("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    land_area = parcel.polygon.area

    if area_assumptions is not None:
        try:
            required_area = required_area_m2(request, area_assumptions)
        except AreaAssumptionError as error:
            if str(error) == "INPUT_REVISION_MISMATCH":
                return finish("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        except ValidationError:
            pass  # Unknown required area is explicit in the result.

    if requested_status == "MISSING":
        return finish("NEEDS_REVIEW", "SETBACK_EVIDENCE_MISSING")
    if requested_status == "CONFLICTING":
        return finish("NEEDS_REVIEW", "REGULATORY_EVIDENCE_CONFLICT")
    if requested_status != "REVIEWED" or reviewed_setbacks is None:
        return finish("NEEDS_REVIEW", "SETBACK_EVIDENCE_UNVERIFIED")

    try:
        snapshot = ReviewedSetbackSnapshot.model_validate(reviewed_setbacks)
    except ValidationError:
        return finish("NEEDS_REVIEW", "SETBACK_INVALID")
    if snapshot.synthetic is not True:
        return finish("NEEDS_REVIEW", "SETBACK_INVALID")
    if snapshot.revision_key() != revision or snapshot.regulatoryEvidence != request.regulatoryEvidence:
        return finish("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")

    try:
        distances, rules = _setback_rules(snapshot.regulatoryEvidence, len(parcel.edges))
    except SetbackEvidenceError as error:
        if str(error) == "REGULATORY_EVIDENCE_CONFLICT":
            evidence_status = "CONFLICTING"
        return finish("NEEDS_REVIEW", str(error))

    evidence_status = "REVIEWED"
    rule_set_version = snapshot.regulatoryEvidence.ruleSetVersion
    try:
        bounds = build_footprint(parcel, distances)
        buildable_area = bounds.candidate.area
        if bounds.candidate.is_empty:
            footprint = None
        elif bounds.candidate is parcel.polygon:
            footprint = parcel.land
        else:
            footprint = _metric_footprint(bounds.candidate, parcel.land)
    except FootprintError as error:
        code = str(error)
        status = "NEEDS_REVIEW" if code == "FOOTPRINT_CALCULATION_FAILED" else "UNSUPPORTED"
        return finish(status, code)
    except ImportError:
        return finish("NEEDS_REVIEW", "FOOTPRINT_DEPENDENCY_UNAVAILABLE")
    except (ShapelyError, ValidationError, ArithmeticError):
        return finish("NEEDS_REVIEW", "FOOTPRINT_CALCULATION_FAILED")

    optimistic_area = bounds.upper_area_m2

    if required_area is None:
        codes = ("REQUIRED_AREA_POLICY_UNAVAILABLE",)
        if bounds.upper_area_m2 == 0:
            codes += ("EMPTY_BUILDABLE_REGION",)
        return finish("NEEDS_REVIEW", *codes, footprint=footprint, buildable_area=buildable_area, rules=rules)

    if buildable_area > 0 and buildable_area >= required_area:
        return finish(
            "FEASIBLE", "PRELIMINARY_AREA_ONLY",
            footprint=footprint, buildable_area=buildable_area, shortfall=0, rules=rules,
        )
    if bounds.upper_area_m2 == 0:
        return finish(
            "INFEASIBLE", "EMPTY_BUILDABLE_REGION", buildable_area=0, shortfall=required_area, rules=rules,
        )
    if required_area - bounds.upper_area_m2 > area_tolerance_m2(land_area, parcel.polygon.length):
        return finish(
            "INFEASIBLE", "BUILDABLE_AREA_INSUFFICIENT",
            footprint=footprint, buildable_area=buildable_area,
            shortfall=required_area - bounds.upper_area_m2, rules=rules,
        )

    return finish(
        "NEEDS_REVIEW", "FOOTPRINT_PRECISION_INCONCLUSIVE",
        footprint=footprint, buildable_area=buildable_area, rules=rules,
    )
