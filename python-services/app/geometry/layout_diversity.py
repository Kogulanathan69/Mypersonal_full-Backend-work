"""D04 geometric comparison in the confirmed parcel's metric frame."""

from dataclasses import dataclass
from itertools import combinations
from math import hypot

from app.geometry.placement.engine import Bounds, Pair, PlacedRoom


def canonical_rooms(rooms: tuple[PlacedRoom, ...]) -> tuple[PlacedRoom, ...]:
    """Retain required slot identity and normalize signed zero and ordering."""
    return tuple(
        PlacedRoom(room.slot_id, room.room_type, tuple(float(x) or 0.0 for x in room.bounds), room.rotated)
        for room in sorted(rooms, key=lambda room: room.slot_id)
    )


def movement_threshold(bounds: Bounds) -> float:
    """Require 1% of the shorter footprint span, bounded to 5–50 cm."""
    left, bottom, right, top = bounds
    return max(0.05, min(0.5, 0.01 * min(right - left, top - bottom)))


def _center(bounds: Bounds) -> tuple[float, float]:
    left, bottom, right, top = bounds
    return ((left + right) / 2, (bottom + top) / 2)


@dataclass(frozen=True)
class Diversity:
    displacement_m: float
    relative_displacement_m: float
    topology_difference: float
    threshold_m: float

    @property
    def distinct(self) -> bool:
        return self.displacement_m >= self.threshold_m

    @property
    def score(self) -> float:
        return round(
            self.displacement_m / self.threshold_m
            + self.relative_displacement_m / self.threshold_m
            + self.topology_difference, 9
        )


def compare_layouts(
    first: tuple[PlacedRoom, ...], second: tuple[PlacedRoom, ...],
    first_walls: frozenset[Pair], second_walls: frozenset[Pair], footprint_bounds: Bounds,
) -> Diversity:
    """Compare two validated layouts of the same exact room program."""
    first, second = canonical_rooms(first), canonical_rooms(second)
    if [(r.slot_id, r.room_type) for r in first] != [(r.slot_id, r.room_type) for r in second]:
        raise ValueError("Layout comparison requires matching room slots.")
    displacement = max(
        (max(abs(x - y) for x, y in zip(a.bounds, b.bounds)) for a, b in zip(first, second)),
        default=0.0,
    )
    offsets = [
        (x - u, y - v)
        for (x, y), (u, v) in ((_center(a.bounds), _center(b.bounds)) for a, b in zip(first, second))
    ]
    relative = max((hypot(x - u, y - v) for (x, y), (u, v) in combinations(offsets, 2)), default=0.0)
    union = first_walls | second_walls
    topology = len(first_walls ^ second_walls) / len(union) if union else 0.0
    return Diversity(displacement, relative, topology, movement_threshold(footprint_bounds))
