
"""M2 land location DTOs and validation."""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


class LandLocationInput(BaseModel):
    """Untrusted location data supplied by a user or API client."""

    model_config = ConfigDict(extra="forbid", strict=True)

    locality: str | None = Field(default=None, min_length=1, max_length=200)
    source: Literal["USER_SELECTED", "UNKNOWN"] = "UNKNOWN"
    longitude: float | None = Field(
        default=None, ge=-180, le=180, allow_inf_nan=False
    )
    latitude: float | None = Field(
        default=None, ge=-90, le=90, allow_inf_nan=False
    )

    @field_validator("locality")
    @classmethod
    def normalize_locality(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("Locality must not be blank")
        return value

    @model_validator(mode="after")
    def validate_location(self):
        if (self.longitude is None) != (self.latitude is None):
            raise ValueError(
                "Longitude and latitude must be provided together"
            )

        has_location_data = (
            self.locality is not None or self.longitude is not None
        )

        if has_location_data and self.source == "UNKNOWN":
            raise ValueError(
                "Location details require an explicit source"
            )

        if self.source != "UNKNOWN" and not has_location_data:
            raise ValueError(
                "A location source requires locality or coordinates"
            )

        return self

    @property
    def status(self) -> str:
        if self.locality is not None or self.longitude is not None:
            return "USER_PROVIDED"
        return "MISSING"

    def to_land_location(self) -> "LandLocation":
        """Convert validated user input into an unverified domain location."""
        return LandLocation(
            locality=self.locality,
            source=self.source,
            longitude=self.longitude,
            latitude=self.latitude,
        )


class LandLocation(BaseModel):
    """Internal land location, including trusted verification metadata."""

    model_config = ConfigDict(extra="forbid", strict=True)

    locality: str | None = Field(default=None, min_length=1, max_length=200)
    source: Literal["USER_SELECTED", "GEOCODER_VERIFIED", "UNKNOWN"] = "UNKNOWN"
    longitude: float | None = Field(
        default=None, ge=-180, le=180, allow_inf_nan=False
    )
    latitude: float | None = Field(
        default=None, ge=-90, le=90, allow_inf_nan=False
    )

    @field_validator("locality")
    @classmethod
    def normalize_locality(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("Locality must not be blank")
        return value

    @model_validator(mode="after")
    def validate_location(self):
        if (self.longitude is None) != (self.latitude is None):
            raise ValueError(
                "Longitude and latitude must be provided together"
            )

        has_location_data = (
            self.locality is not None or self.longitude is not None
        )

        if has_location_data and self.source == "UNKNOWN":
            raise ValueError(
                "Location details require an explicit source"
            )

        if self.source == "GEOCODER_VERIFIED" and (
            self.locality is None or self.longitude is None
        ):
            raise ValueError(
                "Verified locality requires a locality name and coordinates"
            )

        if self.source != "UNKNOWN" and not has_location_data:
            raise ValueError(
                "A location source requires locality or coordinates"
            )

        return self

    @property
    def status(self) -> str:
        if self.source == "GEOCODER_VERIFIED":
            return "VERIFIED"
        if self.locality is not None or self.longitude is not None:
            return "USER_PROVIDED"
        return "MISSING"
