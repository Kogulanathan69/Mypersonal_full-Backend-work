"""Measure UTF-8 encoding and SHA-256 cost for synthetic M3-shaped plan payloads."""

import hashlib
import json
from time import perf_counter_ns


LIMIT = 256 * 1024
ITERATIONS = 300


def identifier(value):
    return f"00000000-0000-0000-0000-{value:012d}"


def geometry(room_count):
    rooms = []
    walls = []
    openings = []
    for index in range(room_count):
        x = index * 4
        polygon = [{"x": x + dx, "y": y} for dx, y in [(0, 0), (4, 0), (4, 4), (0, 4)]]
        wall_ids = [identifier(10_000 + index * 4 + edge) for edge in range(4)]
        for edge, wall_id in enumerate(wall_ids):
            walls.append({"id": wall_id, "start": polygon[edge], "end": polygon[(edge + 1) % 4], "thickness": 0.15})
        openings.append({"id": identifier(20_000 + index), "type": "door", "wallId": wall_ids[0], "offset": 1, "width": 0.9})
        rooms.append({"id": identifier(100 + index), "name": f"කාමරය {index}", "type": "bedroom", "polygon": polygon, "wallIds": wall_ids})

    return json.dumps(
        {
            "schemaVersion": "0.1-draft",
            "projectId": identifier(1),
            "planId": identifier(2),
            "landRevisionId": identifier(3),
            "requirementsRevisionId": identifier(4),
            "coordinateSystem": {"type": "localCartesian", "unit": "m", "origin": {"x": 0, "y": 0}, "xDirection": "right", "yDirection": "up"},
            "floors": [{"id": identifier(5), "level": 0, "rooms": rooms, "walls": walls, "openings": openings}],
        },
        ensure_ascii=False,
        separators=(",", ":"),
    )


def main():
    small = geometry(8)
    padding = " " * (LIMIT - len(small.encode("utf-8")))
    fixtures = {
        "residential_8_rooms": small,
        "large_24_rooms": geometry(24),
        "stress_512_rooms": geometry(512),
        "boundary_256_kib": small + padding,
        "over_boundary": small + padding + " ",
    }
    print("fixture,utf8_bytes,within_limit,mean_encode_sha256_us")
    for name, text in fixtures.items():
        size = len(text.encode("utf-8"))
        hashlib.sha256(text.encode("utf-8")).digest()
        start = perf_counter_ns()
        for _ in range(ITERATIONS):
            hashlib.sha256(text.encode("utf-8")).digest()
        elapsed_us = (perf_counter_ns() - start) / ITERATIONS / 1000
        print(f"{name},{size},{size <= LIMIT},{elapsed_us:.2f}")


if __name__ == "__main__":
    main()
