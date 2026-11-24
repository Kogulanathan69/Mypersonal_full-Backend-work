"""Captain land-boundary validation over the existing M2/M6 confirmed-parcel validator (no new geometry rules)."""
from typing import Literal

from app.geometry.footprint.contracts import ContractModel, MetricLand, NonNilUUID
from app.geometry.footprint.parcel import ConfirmedMetricParcel, ParcelError, validate_confirmed_parcel

SCHEMA = "captain-land-validation/1"


class LandValidationRequest(ContractModel):
    schemaVersion: Literal["captain-land-validation/1"]
    projectId: NonNilUUID
    landRevisionId: NonNilUUID
    land: MetricLand


class DependencyUnavailable(RuntimeError):
    """The geometry dependency (shapely) is unavailable."""


def validate_land(request: LandValidationRequest) -> dict:
    """Return a fixed-code verdict. ParcelError messages are fixed diagnostic codes by design."""
    try:
        snapshot = ConfirmedMetricParcel(
            projectId=request.projectId, landRevisionId=request.landRevisionId, land=request.land,
        )
        parcel = validate_confirmed_parcel(snapshot, request.projectId, request.landRevisionId)
    except ImportError:
        raise DependencyUnavailable() from None
    except ParcelError as error:
        return {"schemaVersion": SCHEMA, "valid": False, "code": str(error), "areaM2": None, "edgeCount": None}
    except ValueError:
        return {"schemaVersion": SCHEMA, "valid": False, "code": "POLYGON_INVALID", "areaM2": None, "edgeCount": None}
    return {
        "schemaVersion": SCHEMA,
        "valid": True,
        "code": "LAND_VALID",
        "areaM2": float(parcel.polygon.area),
        "edgeCount": len(parcel.edges),
    }
