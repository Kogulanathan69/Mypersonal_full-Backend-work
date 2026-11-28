
import pytest
from pydantic import ValidationError

from app.geometry.land.location import LandLocation, LandLocationInput


def test_missing_location_has_explicit_status():
    assert LandLocation().status == "MISSING"


def test_user_selected_location():
    location = LandLocation(
        locality="Jaffna",
        source="USER_SELECTED",
        longitude=80.0,
        latitude=9.7,
    )
    assert location.status == "USER_PROVIDED"


def test_coordinates_must_be_provided_as_a_pair():
    with pytest.raises(ValidationError):
        LandLocation(longitude=80.0)


@pytest.mark.parametrize(
    "coordinates",
    [
        {"longitude": 181.0, "latitude": 9.7},
        {"longitude": 80.0, "latitude": 91.0},
    ],
)
def test_out_of_range_coordinates_are_rejected(coordinates):
    with pytest.raises(ValidationError):
        LandLocation(**coordinates)


def test_verified_location_requires_name_and_coordinates():
    with pytest.raises(ValidationError):
        LandLocation(source="GEOCODER_VERIFIED")


def test_locality_requires_explicit_source():
    with pytest.raises(ValidationError):
        LandLocation(locality="Jaffna")


def test_source_without_location_data_is_rejected():
    with pytest.raises(ValidationError):
        LandLocation(source="USER_SELECTED")


def test_customer_cannot_claim_geocoder_verified():
    with pytest.raises(ValidationError):
        LandLocationInput(
            locality="Jaffna",
            source="GEOCODER_VERIFIED",
            longitude=80.0,
            latitude=9.7,
        )


def test_user_input_converts_to_unverified_location():
    user_input = LandLocationInput(
        locality="Jaffna",
        source="USER_SELECTED",
        longitude=80.0,
        latitude=9.7,
    )

    location = user_input.to_land_location()

    assert location.source == "USER_SELECTED"
    assert location.status == "USER_PROVIDED"


def test_internal_verified_location_requires_complete_data():
    with pytest.raises(ValidationError):
        LandLocation(
            locality="Jaffna",
            source="GEOCODER_VERIFIED",
        )

def test_location_input_without_data_is_missing():
    assert LandLocationInput().status == "MISSING"


def test_location_input_rejects_coordinates_without_pair():
    with pytest.raises(ValidationError):
        LandLocationInput(longitude=80.0)


@pytest.mark.parametrize(
    "coordinates",
    [
        {"longitude": 181.0, "latitude": 9.7},
        {"longitude": 80.0, "latitude": 91.0},
    ],
)
def test_location_input_rejects_out_of_range_coordinates(coordinates):
    with pytest.raises(ValidationError):
        LandLocationInput(**coordinates)


def test_location_input_accepts_user_selected_coordinate_pair():
    location = LandLocationInput(
        locality="Jaffna",
        source="USER_SELECTED",
        longitude=80.0,
        latitude=9.7,
    )

    assert location.status == "USER_PROVIDED"
    assert location.to_land_location().status == "USER_PROVIDED"
