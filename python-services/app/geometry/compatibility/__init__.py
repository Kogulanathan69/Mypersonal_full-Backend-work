"""Isolated M2-to-M6 geometry compatibility contracts."""

from .m2_m6_v1 import (
    AffineTranslation,
    CrsProvenance,
    EdgeMapping,
    GeometryCompatibilityError,
    LandGeometryHandoff,
    LocalFrame,
    ParcelEdge,
    ParcelRing,
    ParcelVertex,
    PrecisionProfile,
    area_tolerance,
    bind_setbacks_to_m6_indices,
    compute_geometry_digest,
    projected_to_local,
    validate_handoff,
)

__all__ = [
    "AffineTranslation", "CrsProvenance", "EdgeMapping", "GeometryCompatibilityError",
    "LandGeometryHandoff", "LocalFrame", "ParcelEdge", "ParcelRing",
    "ParcelVertex", "PrecisionProfile", "area_tolerance", "bind_setbacks_to_m6_indices",
    "compute_geometry_digest", "projected_to_local", "validate_handoff",
]
