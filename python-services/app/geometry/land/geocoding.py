"""Provider-independent geocoding contract for M2 land locations."""

from dataclasses import dataclass
from enum import Enum
from typing import Protocol, Sequence
from math import isfinite

from app.geometry.land.location import LandLocation


class GeocodingStatus(str, Enum):
    VERIFIED = "VERIFIED"
    AMBIGUOUS = "AMBIGUOUS"
    NOT_FOUND = "NOT_FOUND"
    UNAVAILABLE = "UNAVAILABLE"


@dataclass(frozen=True)
class GeocodeCandidate:
    locality: str
    longitude: float
    latitude: float

    def __post_init__(self) -> None:
        if not isinstance(self.locality, str) or not self.locality.strip():
            raise ValueError("INVALID_GEOCODE_LOCALITY")
        if isinstance(self.longitude, bool) or not isinstance(self.longitude, (int, float)):
            raise ValueError("INVALID_GEOCODE_LONGITUDE")
        if isinstance(self.latitude, bool) or not isinstance(self.latitude, (int, float)):
            raise ValueError("INVALID_GEOCODE_LATITUDE")
        if not isfinite(self.longitude) or not -180 <= self.longitude <= 180:
            raise ValueError("INVALID_GEOCODE_LONGITUDE")
        if not isfinite(self.latitude) or not -90 <= self.latitude <= 90:
            raise ValueError("INVALID_GEOCODE_LATITUDE")


@dataclass(frozen=True)
class GeocodingResult:
    status: GeocodingStatus
    location: LandLocation | None = None


class Geocoder(Protocol):
    """Adapter contract. Provider must return only an unambiguous exact match."""

    def geocode(self, locality: str) -> Sequence[GeocodeCandidate]:
        ...


def resolve_location(locality: str, geocoder: Geocoder) -> GeocodingResult:
    """Resolve a locality without treating uncertain results as verified."""
    if not isinstance(locality, str) or not locality.strip():
        raise ValueError("LOCALITY_REQUIRED")

    query = locality.strip()

    try:
        candidates = tuple(geocoder.geocode(query))
    except Exception:
        return GeocodingResult(GeocodingStatus.UNAVAILABLE)

    if not candidates:
        return GeocodingResult(GeocodingStatus.NOT_FOUND)

    if len(candidates) != 1:
        return GeocodingResult(GeocodingStatus.AMBIGUOUS)

    candidate = candidates[0]
    if not isinstance(candidate, GeocodeCandidate):
        return GeocodingResult(GeocodingStatus.UNAVAILABLE)

    location = LandLocation(
        locality=candidate.locality.strip(),
        source="GEOCODER_VERIFIED",
        longitude=float(candidate.longitude),
        latitude=float(candidate.latitude),
    )
    return GeocodingResult(GeocodingStatus.VERIFIED, location)
