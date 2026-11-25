"""Door/window generation and physical-access validation (axis-aligned room rectangles)."""
from uuid import uuid4

from app.internal.openings import CORNER_CLEARANCE_M, generate_openings, validate_openings


def rect(rid, rtype, x0, y0, x1, y1):
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    walls = [{"id": f"{rid}-w{i}", "start": {"x": a[0], "y": a[1]}, "end": {"x": b[0], "y": b[1]}, "thickness": 0.1}
             for i, (a, b) in enumerate(zip(corners, corners[1:] + corners[:1]))]
    room = {"id": rid, "name": rid, "type": rtype, "polygon": [{"x": x, "y": y} for x, y in corners], "wallIds": [w["id"] for w in walls]}
    return room, walls


def floor(*rooms):
    built = [rect(*r) for r in rooms]
    return {"id": str(uuid4()), "level": 0, "rooms": [r for r, _ in built], "walls": [w for _, ws in built for w in ws], "openings": []}


def codes(found):
    return {c for c, _ in found}


def test_strip_layout_gets_entrance_internal_doors_and_windows_and_validates():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4), ("kit", "kitchen", 5, 0, 8, 4), ("bed", "bedroom", 0, 4, 4, 7), ("bath", "bathroom", 4, 4, 6, 7))
    plan = generate_openings(f, uuid4())
    assert not [c for c, s in plan.diagnostics if s == "CRITICAL"]
    doors = [o for o in f["openings"] if o["type"] == "door"]
    assert len(doors) >= 4  # entrance + kitchen + bedroom + bathroom (+ a door into residual circulation)
    assert {o["type"] for o in f["openings"]} == {"door", "window"}
    found = validate_openings(f)
    assert not [c for c, s in found if s == "CRITICAL"]
    assert not codes(found) & {"ROOM_NOT_REACHABLE", "NO_ENTRANCE", "PHYSICAL_ACCESS_NOT_EVALUATED"}


def test_openings_respect_corner_clearance_and_never_overlap():
    f = floor(("liv", "livingRoom", 0, 0, 6, 4), ("kit", "kitchen", 6, 0, 9, 4))
    generate_openings(f, uuid4())
    walls = {w["id"]: w for w in f["walls"]}
    for o in f["openings"]:
        w = walls[o["wallId"]]
        length = abs(w["end"]["x"] - w["start"]["x"]) + abs(w["end"]["y"] - w["start"]["y"])
        assert o["offset"] >= CORNER_CLEARANCE_M - 1e-9 and o["offset"] + o["width"] <= length - CORNER_CLEARANCE_M + 1e-9
    assert "OPENINGS_OVERLAP" not in codes(validate_openings(f))


def test_rooms_separated_by_a_passable_gap_connect_through_circulation_with_exterior_walls():
    f = floor(("liv", "livingRoom", 0, 0, 4, 4), ("bed", "bedroom", 5.5, 0, 9, 4))  # 1.5 m gap -> hall
    walls_before = len(f["walls"])
    plan = generate_openings(f, uuid4())
    assert not [c for c, s in plan.diagnostics if s == "CRITICAL"]
    assert ("CIRCULATION_IS_RESIDUAL_SPACE", "WARNING") in plan.diagnostics
    assert len(f["walls"]) > walls_before  # exterior walls close the hall at the envelope
    assert not codes(validate_openings(f)) & {"ROOM_NOT_REACHABLE", "NO_ENTRANCE"}


def test_a_sliver_gap_is_not_passable():
    # 0.5 m gap is narrower than the minimum passage; the kitchen can only be reached through it -> unsatisfiable
    f = floor(("liv", "livingRoom", 0, 0, 4, 4), ("bath", "bathroom", 4.5, 0, 6, 4), ("kit", "kitchen", 6, 0, 9, 4))
    assert ("PHYSICAL_ACCESS_UNSATISFIABLE", "CRITICAL") in generate_openings(f, uuid4()).diagnostics


def test_bathroom_is_never_a_pass_through():
    # kitchen reachable only through the bathroom -> unsatisfiable rather than routing through it
    f = floor(("liv", "livingRoom", 0, 0, 4, 4), ("bath", "bathroom", 4, 0, 6, 4), ("kit", "kitchen", 6, 0, 9, 4))
    assert ("PHYSICAL_ACCESS_UNSATISFIABLE", "CRITICAL") in generate_openings(f, uuid4()).diagnostics


def test_validation_rejects_bad_openings():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4), ("kit", "kitchen", 5, 0, 8, 4))
    generate_openings(f, uuid4())
    shared = next(w for w in f["walls"] if w["id"].startswith("liv") and w["start"]["x"] == 5)
    bad = [
        {"id": str(uuid4()), "type": "door", "wallId": str(uuid4()), "offset": 0.5, "width": 0.9},          # missing wall
        {"id": str(uuid4()), "type": "door", "wallId": shared["id"], "offset": 3.5, "width": 0.9},          # beyond wall
        {"id": str(uuid4()), "type": "window", "wallId": shared["id"], "offset": 1.5, "width": 1.0},        # internal window
    ]
    for opening in bad:
        found = validate_openings({**f, "openings": f["openings"] + [opening]})
        assert [c for c, s in found if s == "CRITICAL"], opening
    door = next(o for o in f["openings"] if o["type"] == "door")
    clash = {**door, "id": str(uuid4())}
    assert "OPENINGS_OVERLAP" in codes(validate_openings({**f, "openings": f["openings"] + [clash]}))


def test_removing_the_internal_door_is_reported_as_unreachable():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4), ("kit", "kitchen", 5, 0, 8, 4))
    generate_openings(f, uuid4())
    entrance_only = [o for o in f["openings"] if not (o["type"] == "door" and o["wallId"] == next(
        w["id"] for w in f["walls"] if w["id"].startswith("liv") and w["start"]["x"] == 5 and w["end"]["x"] == 5))]
    found = validate_openings({**f, "openings": entrance_only})
    assert "ROOM_NOT_REACHABLE" in codes(found)


def test_legacy_plan_without_doors_is_flagged_not_evaluated():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4))
    assert ("PHYSICAL_ACCESS_NOT_EVALUATED", "WARNING") in validate_openings(f)


def test_shrinking_a_room_behind_its_door_is_a_warning_not_a_blocking_error():
    # Door between living room and kitchen; then the kitchen is narrowed, leaving a 0.3 m sliver behind the door.
    f = floor(("liv", "livingRoom", 0, 0, 5, 4), ("kit", "kitchen", 5, 0, 8, 4))
    generate_openings(f, uuid4())
    kit = next(r for r in f["rooms"] if r["id"] == "kit")
    for p in kit["polygon"]:
        if p["x"] == 5:
            p["x"] = 5.3
    for w in f["walls"]:
        if w["id"].startswith("kit"):
            for end in ("start", "end"):
                if w[end]["x"] == 5:
                    w[end]["x"] = 5.3
    found = validate_openings(f)
    assert not [c for c, s in found if s == "CRITICAL"], found
    assert ("DOOR_OPENS_INTO_UNUSABLE_SPACE", "WARNING") in found or ("ROOM_NOT_REACHABLE", "WARNING") in found



def _window(wall_id, offset=1.0, width=1.0):
    return {"id": str(uuid4()), "type": "window", "wallId": wall_id, "offset": offset, "width": width}


def test_window_facing_a_sub_passage_gap_is_a_warning():
    # Living room shrunk to x=7.6 while the bedroom still reaches x=8: its east window faces a 0.4 m enclosed gap.
    f = floor(("liv", "livingRoom", 0, 0, 7.6, 4), ("bed", "bedroom", 0, 4, 8, 8))
    f["openings"] = [_window("liv-w1")]
    found = validate_openings(f)
    assert ("WINDOW_FACES_INTERIOR_SPACE", "WARNING") in found
    assert not [c for c, sev in found if sev == "CRITICAL"], found


def test_window_into_another_room_is_critical():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4), ("kit", "kitchen", 5, 0, 8, 4))
    f["openings"] = [_window("liv-w1")]   # liv east wall is shared with the kitchen
    assert ("WINDOW_NOT_ON_EXTERIOR", "CRITICAL") in validate_openings(f)


def test_window_on_the_exterior_is_valid():
    f = floor(("liv", "livingRoom", 0, 0, 5, 4))
    f["openings"] = [_window("liv-w1")]
    assert not [c for c, sev in validate_openings(f) if c.startswith("WINDOW")]


def test_window_facing_interior_circulation_is_a_warning():
    # Living room shrunk to x=6 while the bedroom reaches x=8: its east window faces a 2 m circulation hall.
    f = floor(("liv", "livingRoom", 0, 0, 6, 4), ("bed", "bedroom", 0, 4, 8, 8))
    f["openings"] = [_window("liv-w1")]
    found = validate_openings(f)
    assert ("WINDOW_FACES_INTERIOR_SPACE", "WARNING") in found
    assert not [c for c, sev in found if sev == "CRITICAL"], found
