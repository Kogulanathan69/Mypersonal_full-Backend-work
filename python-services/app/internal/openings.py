"""Door/window generation and physical-access validation for Captain 2D plans (ground floor, room rectangles).

Model (versioned ``OPENING_MODEL_VERSION``; dimensions are DESIGN PREFERENCES, not regulatory minimums, because no
reviewed regulation for door/window sizes, lighting or ventilation exists, G06):

* **Envelope** = bounding box of all walls. Space inside the envelope that no room covers is **circulation** (hall).
  Only circulation components at least ``MIN_PASSAGE_M`` wide are passable. Where circulation meets the envelope
  boundary, generation adds **exterior walls** (walls not owned by any room), so the building is closed.
* Every opening is classified by **probes** ``PROBE_M`` either side of its wall at its midpoint: a room, a
  circulation component, or OUTSIDE. A **door** must connect two different spaces; a **window** must connect a
  room to OUTSIDE.
* **Generation**: one entrance door from OUTSIDE (living room preferred, else circulation), then doors along a
  breadth-first access tree (circulation and living/kitchen first; bathrooms never pass-through; bedrooms only as a
  fallback, flagged), then one window per habitable room on an outside-facing wall portion. A candidate whose rooms
  are not all reachable is rejected (``PHYSICAL_ACCESS_UNSATISFIABLE``).
* **Validation** (generated or edited plans) re-derives spaces and reports invalid openings (CRITICAL) and
  unreachable rooms (WARNING for edited plans; generation never emits one).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from math import hypot
from uuid import UUID, uuid5

OPENING_MODEL_VERSION = "captain-openings-v1"
OPENING_PREFERENCES_VERSION = OPENING_MODEL_VERSION
EPS = 1e-6
PROBE_M = 0.3
MIN_PASSAGE_M = 0.9
SAMPLE_STEP_M = 0.05
CORNER_CLEARANCE_M = 0.15
ENTRANCE_DOOR_WIDTH_M = 1.0
DOOR_WIDTHS_M = {"bathroom": 0.8}
DEFAULT_DOOR_WIDTH_M = 0.9
WINDOW_WIDTHS_M = {"livingRoom": 1.5, "bedroom": 1.2, "kitchen": 1.0, "bathroom": 0.6, "diningRoom": 1.2, "study": 1.2}
ENTRANCE_PREFERENCE = ("livingRoom", "diningRoom", "kitchen", "study")
HUB_ORDER = {"livingRoom": 1, "diningRoom": 2, "kitchen": 3, "study": 4, "bedroom": 6, "bathroom": 9}
EXTERIOR_WALL_THICKNESS_M = 0.2
OUTSIDE = "OUTSIDE"


@dataclass(frozen=True)
class Wall:
    id: str
    sx: float
    sy: float
    ex: float
    ey: float

    @property
    def length(self) -> float:
        return hypot(self.ex - self.sx, self.ey - self.sy)

    def at(self, t: float) -> tuple[float, float]:
        return (self.sx + (self.ex - self.sx) * t / self.length, self.sy + (self.ey - self.sy) * t / self.length)

    @property
    def normal(self) -> tuple[float, float]:
        return (-(self.ey - self.sy) / self.length, (self.ex - self.sx) / self.length)


@dataclass
class Spaces:
    """Classifies points into room ids, circulation components ('HALL:<n>') or OUTSIDE."""
    rooms: list  # (id, type, shapely Polygon)
    halls: list  # shapely Polygons (passable components)
    slivers: list
    envelope: object

    def classify(self, x: float, y: float) -> str | None:
        from shapely.geometry import Point
        point = Point(x, y)
        if not self.envelope.buffer(EPS).covers(point):
            return OUTSIDE
        for rid, _, polygon in self.rooms:
            if polygon.buffer(-EPS).contains(point):
                return rid
        for index, hall in enumerate(self.halls):
            if hall.buffer(EPS).covers(point):
                return f"HALL:{index}"
        return None  # sliver or wall line: not passable


def _spaces(floor: dict) -> Spaces:
    from shapely.geometry import Polygon, box
    from shapely.ops import unary_union
    rooms = [(r["id"], r["type"], Polygon([(p["x"], p["y"]) for p in r["polygon"]])) for r in floor["rooms"]]
    xs = [v for w in floor["walls"] for v in (w["start"]["x"], w["end"]["x"])] or [0]
    ys = [v for w in floor["walls"] for v in (w["start"]["y"], w["end"]["y"])] or [0]
    envelope = box(min(xs), min(ys), max(xs), max(ys))
    residual = envelope.difference(unary_union([p for _, _, p in rooms])) if rooms else envelope
    parts = list(getattr(residual, "geoms", [residual])) if not residual.is_empty else []
    halls, slivers = [], []
    for part in parts:
        if part.area <= EPS:
            continue
        (halls if not part.buffer(-MIN_PASSAGE_M / 2).is_empty else slivers).append(part)
    return Spaces(rooms, halls, slivers, envelope)


def _walls(floor: dict) -> dict[str, Wall]:
    return {w["id"]: Wall(w["id"], w["start"]["x"], w["start"]["y"], w["end"]["x"], w["end"]["y"]) for w in floor["walls"]}


def _sides(spaces: Spaces, wall: Wall, t: float) -> tuple[str | None, str | None]:
    x, y = wall.at(t)
    nx, ny = wall.normal
    return spaces.classify(x + nx * PROBE_M, y + ny * PROBE_M), spaces.classify(x - nx * PROBE_M, y - ny * PROBE_M)


def _intervals(spaces: Spaces, wall: Wall) -> list[tuple[float, float, frozenset]]:
    """Maximal wall intervals with a constant pair of adjacent spaces."""
    out, start, current = [], 0.0, None
    steps = max(2, int(wall.length / SAMPLE_STEP_M))
    for i in range(steps + 1):
        t = wall.length * i / steps
        pair = frozenset(s for s in _sides(spaces, wall, t) if s is not None)
        if current is None:
            current, start = pair, t
        elif pair != current:
            out.append((start, t, current))
            current, start = pair, t
    out.append((start, wall.length, current))
    return [(a, b, p) for a, b, p in out if len(p) == 2 and b - a > EPS]


def _fit(lo: float, hi: float, width: float, blocked: list[tuple[float, float]]) -> float | None:
    segments = [(lo + CORNER_CLEARANCE_M, hi - CORNER_CLEARANCE_M)]
    for a, b in blocked:
        a, b = a - CORNER_CLEARANCE_M, b + CORNER_CLEARANCE_M
        segments = [piece for s, e in segments for piece in ((s, min(e, a)), (max(s, b), e)) if piece[1] - piece[0] > EPS]
    best = max(segments, key=lambda s: s[1] - s[0], default=None)
    if best is None or best[1] - best[0] + EPS < width:
        return None
    return (best[0] + best[1]) / 2 - width / 2


@dataclass
class Result:
    openings: list[dict] = field(default_factory=list)
    diagnostics: list[tuple[str, str]] = field(default_factory=list)


def _exterior_walls(spaces: Spaces, namespace: UUID) -> list[dict]:
    """Walls closing circulation along the envelope boundary (not owned by any room)."""
    from shapely.geometry import LineString
    boundary = spaces.envelope.boundary
    walls = []
    for index, hall in enumerate(spaces.halls + spaces.slivers):
        coords = list(hall.exterior.coords)
        for a, b in zip(coords, coords[1:]):
            segment = LineString([a, b])
            if segment.length > EPS and boundary.buffer(EPS).covers(segment):
                walls.append({"id": str(uuid5(namespace, f"exterior-wall|{index}|{a}|{b}")),
                              "start": {"x": float(a[0]), "y": float(a[1])}, "end": {"x": float(b[0]), "y": float(b[1])},
                              "thickness": EXTERIOR_WALL_THICKNESS_M})
    return walls


def generate_openings(floor: dict, namespace: UUID) -> Result:
    floor["walls"] = floor["walls"] + _exterior_walls(_spaces(floor), namespace)
    spaces = _spaces(floor)
    walls = _walls(floor)
    types = {rid: rtype for rid, rtype, _ in spaces.rooms}
    room_ids = set(types)
    links: dict[str, list[tuple[Wall, float, float, str]]] = {}
    for wall in walls.values():
        for a, b, pair in _intervals(spaces, wall):
            first, second = sorted(pair)
            links.setdefault(first, []).append((wall, a, b, second))
            links.setdefault(second, []).append((wall, a, b, first))
    result = Result()
    used: dict[str, list[tuple[float, float]]] = {}

    def place(kind: str, wall: Wall, lo: float, hi: float, width: float, key: str) -> bool:
        offset = _fit(lo, hi, width, used.get(wall.id, []))
        if offset is None:
            return False
        result.openings.append({"id": str(uuid5(namespace, f"opening|{key}")), "type": kind, "wallId": wall.id,
                                "offset": round(offset, 4), "width": width})
        used.setdefault(wall.id, []).append((offset, offset + width))
        return True

    def rank(node: str) -> tuple:
        return (0 if node.startswith("HALL:") else HUB_ORDER.get(types.get(node, ""), 5), node)

    # Entrance from OUTSIDE.
    entrance_targets = sorted({other for _, _, _, other in links.get(OUTSIDE, []) if other in room_ids or other.startswith("HALL:")},
                              key=lambda n: (ENTRANCE_PREFERENCE.index(types[n]) if types.get(n) in ENTRANCE_PREFERENCE else
                                             (len(ENTRANCE_PREFERENCE) if n.startswith("HALL:") else 99), n))
    reached = {OUTSIDE}
    for target in entrance_targets:
        if types.get(target) in ("bathroom", "bedroom"):
            continue
        options = sorted((o for o in links[OUTSIDE] if o[3] == target), key=lambda o: o[2] - o[1], reverse=True)
        if any(place("door", w, a, b, ENTRANCE_DOOR_WIDTH_M, f"entrance|{target}") for w, a, b, _ in options):
            reached.add(target)
            break
    if len(reached) == 1:
        result.diagnostics.append(("PHYSICAL_ACCESS_UNSATISFIABLE", "CRITICAL"))
        return result

    for allow_bedroom in (False, True):
        frontier = sorted((n for n in reached if n != OUTSIDE), key=rank)
        while frontier:
            current = frontier.pop(0)
            if types.get(current) == "bathroom" or (types.get(current) == "bedroom" and not allow_bedroom):
                continue
            for wall, a, b, other in sorted(links.get(current, []), key=lambda o: rank(o[3])):
                if other in reached or other == OUTSIDE:
                    continue
                width = DOOR_WIDTHS_M.get(types.get(other, ""), DEFAULT_DOOR_WIDTH_M)
                if place("door", wall, a, b, width, f"door|{current}|{other}"):
                    reached.add(other)
                    if types.get(current) == "bedroom":
                        result.diagnostics.append(("ACCESS_THROUGH_BEDROOM", "WARNING"))
                    frontier.append(other)
                    frontier.sort(key=rank)
        if room_ids <= reached:
            break
    if not room_ids <= reached:
        result.diagnostics.append(("PHYSICAL_ACCESS_UNSATISFIABLE", "CRITICAL"))
        return result

    for rid in sorted(room_ids):
        width = WINDOW_WIDTHS_M.get(types[rid])
        if width is None:
            continue
        options = sorted((o for o in links.get(rid, []) if o[3] == OUTSIDE), key=lambda o: o[2] - o[1], reverse=True)
        if not any(place("window", w, a, b, width, f"window|{rid}") for w, a, b, _ in options):
            result.diagnostics.append(("ROOM_WITHOUT_EXTERNAL_WINDOW", "WARNING"))
    if spaces.halls:
        result.diagnostics.append(("CIRCULATION_IS_RESIDUAL_SPACE", "WARNING"))
    floor["openings"] = result.openings
    return result


def validate_openings(floor: dict) -> list[tuple[str, str]]:
    """(code, severity) findings for a floor (generated or edited)."""
    spaces = _spaces(floor)
    walls = _walls(floor)
    room_ids = {rid for rid, _, _ in spaces.rooms}
    findings: list[tuple[str, str]] = []
    spans: dict[str, list[tuple[float, float]]] = {}
    graph: dict[str, set[str]] = {}
    doors = 0
    for opening in floor.get("openings", []):
        wall = walls.get(opening["wallId"])
        if wall is None:
            findings.append(("OPENING_WALL_NOT_FOUND", "CRITICAL"))
            continue
        start, end = opening["offset"], opening["offset"] + opening["width"]
        if start < -EPS or end > wall.length + EPS:
            findings.append(("OPENING_OUTSIDE_WALL", "CRITICAL"))
            continue
        if start < CORNER_CLEARANCE_M - EPS or end > wall.length - CORNER_CLEARANCE_M + EPS:
            findings.append(("OPENING_CORNER_CLEARANCE", "WARNING"))
        if any(start < b - EPS and a < end - EPS for a, b in spans.get(wall.id, [])):
            findings.append(("OPENINGS_OVERLAP", "CRITICAL"))
        spans.setdefault(wall.id, []).append((start, end))
        left, right = _sides(spaces, wall, (start + end) / 2)
        if opening["type"] == "window":
            if not (OUTSIDE in (left, right) and ({left, right} - {OUTSIDE}) <= room_ids and {left, right} != {OUTSIDE}):
                findings.append(("WINDOW_NOT_ON_EXTERIOR", "CRITICAL"))
            continue
        doors += 1
        if left is None or right is None or left == right:
            findings.append(("DOOR_CONNECTS_NO_SPACES", "CRITICAL"))
            continue
        graph.setdefault(left, set()).add(right)
        graph.setdefault(right, set()).add(left)
    if doors == 0:
        findings.append(("PHYSICAL_ACCESS_NOT_EVALUATED", "WARNING"))
        return findings
    if OUTSIDE not in graph:
        findings.append(("NO_ENTRANCE", "WARNING"))
    seen, stack = {OUTSIDE}, [OUTSIDE]
    while stack:
        for nxt in graph.get(stack.pop(), ()):
            if nxt not in seen:
                seen.add(nxt)
                stack.append(nxt)
    if not room_ids <= seen:
        findings.append(("ROOM_NOT_REACHABLE", "WARNING"))
    return findings
