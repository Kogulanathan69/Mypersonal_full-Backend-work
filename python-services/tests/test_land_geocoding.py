from app.geometry.land.geocoding import (
    GeocodeCandidate,
    GeocodingStatus,
    resolve_location,
)


class FakeGeocoder:
    def __init__(self, candidates=None, error=False):
        self.candidates = candidates or []
        self.error = error

    def geocode(self, locality):
        if self.error:
            raise RuntimeError("provider unavailable")
        return self.candidates


def test_unique_candidate_is_verified():
    result = resolve_location(
        "Jaffna",
        FakeGeocoder([GeocodeCandidate("Jaffna", 80.0, 9.6)]),
    )
    assert result.status == GeocodingStatus.VERIFIED
    assert result.location.source == "GEOCODER_VERIFIED"


def test_no_candidate_is_not_found():
    result = resolve_location("Jaffna", FakeGeocoder())
    assert result.status == GeocodingStatus.NOT_FOUND
    assert result.location is None


def test_multiple_candidates_are_ambiguous():
    result = resolve_location(
        "Kandy",
        FakeGeocoder([
            GeocodeCandidate("Kandy", 80.6, 7.2),
            GeocodeCandidate("Kandy District", 80.7, 7.3),
        ]),
    )
    assert result.status == GeocodingStatus.AMBIGUOUS
    assert result.location is None


def test_provider_error_is_unavailable():
    result = resolve_location("Jaffna", FakeGeocoder(error=True))
    assert result.status == GeocodingStatus.UNAVAILABLE
    assert result.location is None


def test_invalid_candidate_coordinates_rejected():
    import pytest

    with pytest.raises(ValueError, match="INVALID_GEOCODE_LONGITUDE"):
        GeocodeCandidate("Jaffna", 999, 9.6)


def test_blank_locality_rejected():
    import pytest

    with pytest.raises(ValueError, match="LOCALITY_REQUIRED"):
        resolve_location("  ", FakeGeocoder())
