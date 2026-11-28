"""Deterministic Blender generator for BuildMind residence BM-CV2-RES-001.

Blender 4.5+ bundled Python only. Geometry is illustrative marketing content,
not construction or engineering documentation.
"""

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

GENERATOR_VERSION = "1.2.0"
EXPECTED_ID = "BM-CV2-RES-001"
CC0_ROOT = Path(__file__).resolve().parents[1] / "materials" / "cc0-polyhaven"
COLLECTIONS = (
    "Site and parcel", "Ground floor", "Upper floor", "Walls and slabs",
    "Roofs and overhangs", "Doors and windows", "Architectural glazing",
    "Timber screens", "Interior furnishings", "Pool and landscaping",
    "Materials", "Lights", "Registered cameras",
)


def parse_args():
    raw = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--geometry", required=True)
    p.add_argument("--cameras", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--manifest", required=True)
    p.add_argument("--render-shot")
    p.add_argument("--output-image")
    p.add_argument("--width", type=int, default=480)
    p.add_argument("--height", type=int, default=270)
    p.add_argument("--samples", type=int, default=8)
    p.add_argument("--profile", choices=("late-afternoon", "blue-hour"), default="late-afternoon")
    return p.parse_args(raw)


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def validate(g, c):
    errors = []
    if g.get("schemaVersion") != 1 or c.get("schemaVersion") != 1:
        errors.append("schemaVersion must be 1")
    if g.get("geometryVersion") != EXPECTED_ID or c.get("geometryVersion") != EXPECTED_ID:
        errors.append("geometryVersion mismatch")
    if g.get("units") != "metres":
        errors.append("units must be metres")
    cs = g.get("coordinateSystem", {})
    if cs != {"handedness": "right", "east": "+x", "up": "+y", "south": "+z"}:
        errors.append("unsupported coordinate system")
    fp = g.get("footprint", {})
    if (fp.get("x1", 0) - fp.get("x0", 0), fp.get("z1", 0) - fp.get("z0", 0)) != (16, 9):
        errors.append("footprint must be 16 x 9 metres")
    if len(g.get("parcel", [])) != 6 or len(g.get("rooms", [])) != 7:
        errors.append("expected six parcel vertices and seven rooms")
    if len(g.get("walls", [])) != 14 or len(c.get("shots", [])) != 9:
        errors.append("expected fourteen walls and nine cameras")
    ids = set()
    for wall in g.get("walls", []):
        if wall["id"] in ids:
            errors.append("duplicate wall id: " + wall["id"])
        ids.add(wall["id"])
        a, b = wall["a"], wall["b"]
        length = math.hypot(b[0] - a[0], b[1] - a[1])
        last = -1.0
        for opening in wall.get("openings", []):
            kind, start, end, sill, head = opening
            if kind not in ("door", "window") or not (0 <= start < end <= length):
                errors.append("invalid opening bounds on " + wall["id"])
            if not (0 <= sill < head <= wall["height"]):
                errors.append("invalid opening height on " + wall["id"])
            if start < last:
                errors.append("overlapping openings on " + wall["id"])
            last = end
    shot_ids = [s["id"] for s in c.get("shots", [])]
    if shot_ids != [f"SHOT-{i:02d}" for i in range(1, 10)]:
        errors.append("camera records must be SHOT-01 through SHOT-09")
    if g.get("stair", {}).get("status") != "reserved-volume":
        errors.append("stair must remain a reserved volume")
    if errors:
        raise ValueError("; ".join(errors))


def src_point(p):
    return Vector((float(p[0]), -float(p[2]), float(p[1])))


def collection(name):
    return bpy.data.collections[name]


def move_to(obj, col_name):
    for col in tuple(obj.users_collection):
        col.objects.unlink(obj)
    collection(col_name).objects.link(obj)


def box(name, center, size, material=None, col="Ground floor", bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("Edge softness", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    if material:
        obj.data.materials.append(material)
    move_to(obj, col)
    return obj


def cylinder(name, center, radius, depth, material, col, vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    move_to(obj, col)
    return obj


def material(name, color, roughness, metallic=0.0, transmission=0.0, ior=1.45, noise=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["IOR"].default_value = ior
    bsdf.inputs["Transmission Weight"].default_value = transmission
    if noise:
        tex = nodes.new("ShaderNodeTexNoise")
        tex.inputs["Scale"].default_value = noise[0]
        tex.inputs["Detail"].default_value = 2.0
        tex.inputs["Roughness"].default_value = 0.55
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (*noise[1], 1)
        ramp.color_ramp.elements[1].color = (*noise[2], 1)
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = noise[3]
        bump.inputs["Distance"].default_value = 0.002
        links.new(tex.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
        links.new(tex.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    if name == "MAT-CONCRETE-01":
        pores = nodes.new("ShaderNodeTexNoise")
        pores.name = "Fine concrete pores"
        pores.inputs["Scale"].default_value = 115.0
        pores.inputs["Detail"].default_value = 1.2
        pores.inputs["Roughness"].default_value = 0.42
        pore_bump = nodes.new("ShaderNodeBump")
        pore_bump.name = "Sub-millimetre pore relief"
        pore_bump.inputs["Strength"].default_value = 0.11
        pore_bump.inputs["Distance"].default_value = 0.0008
        links.new(pores.outputs["Fac"], pore_bump.inputs["Height"])
        links.new(pore_bump.outputs["Normal"], bsdf.inputs["Normal"])
    if name == "MAT-TIMBER-01":
        coord = nodes.new("ShaderNodeTexCoord")
        wave = nodes.new("ShaderNodeTexWave")
        wave.name = "Directional architectural grain"
        wave.wave_type = "BANDS"
        wave.bands_direction = "X"
        wave.inputs["Scale"].default_value = 7.5
        wave.inputs["Distortion"].default_value = 4.0
        wave.inputs["Detail"].default_value = 3.0
        grain_ramp = nodes.new("ShaderNodeValToRGB")
        grain_ramp.color_ramp.elements[0].color = (0.075, 0.018, 0.004, 1)
        grain_ramp.color_ramp.elements[1].color = (0.42, 0.13, 0.025, 1)
        grain_bump = nodes.new("ShaderNodeBump")
        grain_bump.inputs["Strength"].default_value = 0.08
        grain_bump.inputs["Distance"].default_value = 0.0012
        links.new(coord.outputs["Generated"], wave.inputs["Vector"])
        links.new(wave.outputs["Color"], grain_ramp.inputs["Fac"])
        links.new(grain_ramp.outputs["Color"], bsdf.inputs["Base Color"])
        links.new(wave.outputs["Color"], grain_bump.inputs["Height"])
        links.new(grain_bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def image_node(nodes, path, colorspace, label):
    node = nodes.new("ShaderNodeTexImage")
    node.name = label
    node.label = label
    node.image = bpy.data.images.load(str(path), check_existing=True)
    node.image.colorspace_settings.name = colorspace
    node.extension = "REPEAT"
    node.interpolation = "Linear"
    return node


def apply_cc0_surface(mat, folder, diffuse, roughness, normal, scale, tint=None):
    """Replace procedural colour with audited 1K CC0 PBR maps when all maps exist."""
    paths = [folder / diffuse, folder / roughness, folder / normal]
    if not all(path.is_file() for path in paths):
        print("CC0_SURFACE_FALLBACK " + mat.name)
        return False
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    coord = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (scale, scale, scale)
    links.new(coord.outputs["Generated"], mapping.inputs["Vector"])
    color = image_node(nodes, paths[0], "sRGB", mat.name + " CC0 diffuse")
    rough = image_node(nodes, paths[1], "Non-Color", mat.name + " CC0 roughness")
    normal_tex = image_node(nodes, paths[2], "Non-Color", mat.name + " CC0 normal")
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.42
    for texture in (color, rough, normal_tex):
        links.new(mapping.outputs["Vector"], texture.inputs["Vector"])
    if tint:
        mix = nodes.new("ShaderNodeMixRGB")
        mix.blend_type = "MIX"
        mix.inputs["Fac"].default_value = tint[1]
        mix.inputs[2].default_value = tint[0]
        links.new(color.outputs["Color"], mix.inputs[1])
        links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        links.new(color.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(rough.outputs["Color"], bsdf.inputs["Roughness"])
    links.new(normal_tex.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    mat["cc0_source"] = "Poly Haven"
    mat["cc0_files"] = [str(path) for path in paths]
    return True


def make_materials():
    mats = {
        "concrete": material("MAT-CONCRETE-01", (0.48, 0.45, 0.40), 0.72, noise=(4.5, (0.38,0.36,0.33), (0.56,0.52,0.46), 0.12)),
        "timber": material("MAT-TIMBER-01", (0.34, 0.12, 0.035), 0.48, noise=(3.0, (0.12,0.03,0.01), (0.52,0.20,0.045), 0.09)),
        "stone": material("MAT-STONE-01", (0.56, 0.54, 0.48), 0.80, noise=(8.0, (0.42,0.40,0.36), (0.66,0.64,0.57), 0.08)),
        "glass": material("MAT-GLASS-01", (0.88, 0.96, 1.0), 0.025, transmission=1.0, ior=1.45),
        "metal": material("MAT-METAL-01", (0.012, 0.016, 0.018), 0.26, metallic=0.9),
        "interior": material("MAT-INTERIOR-01", (0.78, 0.72, 0.62), 0.65),
        "floor": material("MAT-FLOOR-01", (0.35, 0.31, 0.25), 0.58, noise=(5.0, (0.24,0.20,0.16), (0.43,0.38,0.30), 0.05)),
        "upholstery": material("MAT-UPHOLSTERY-01", (0.12, 0.17, 0.15), 0.86, noise=(85.0, (0.08,0.11,0.10), (0.17,0.22,0.19), 0.04)),
        "water": material("MAT-WATER-01", (0.025, 0.16, 0.20), 0.12, transmission=0.82, ior=1.333),
        "land": material("MAT-LANDSCAPE-01", (0.10, 0.16, 0.075), 0.92, noise=(2.2, (0.055,0.09,0.04), (0.14,0.21,0.09), 0.06)),
        "leaf": material("MAT-FOLIAGE-01", (0.035, 0.16, 0.055), 0.78),
        "warm": material("MAT-LIGHT-DIFFUSER-01", (1.0, 0.55, 0.22), 0.4),
    }
    apply_cc0_surface(
        mats["concrete"], CC0_ROOT / "concrete",
        "concrete_diff_1k.jpg", "concrete_rough_1k.jpg", "concrete_nor_gl_1k.jpg", 2.2,
        ((0.72, 0.70, 0.66, 1.0), 0.58),
    )
    apply_cc0_surface(
        mats["timber"], CC0_ROOT / "fine_grained_wood",
        "fine_grained_wood_col_1k.jpg", "fine_grained_wood_rough_1k.jpg",
        "fine_grained_wood_nor_gl_1k.jpg", 3.5,
    )
    return mats


def leaf_mass(name, center, scale, material, col="Pool and landscaping", rotation=0.0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16, radius=1.0, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    obj.rotation_euler[2] = rotation
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    move_to(obj, col)
    return obj


def build_tree(index, x, z, mats):
    trunk = cylinder(f"Tree trunk {index:02d}", (x, -z, 1.7), 0.16, 3.4, mats["timber"], "Pool and landscaping", 12)
    trunk.scale.x = 0.82
    # Layered, asymmetrical crown volumes are lighter than particle foliage and avoid a faceted monolith.
    offsets = ((0.0,0.0,4.15,1.25,0.95,1.35),(-0.65,0.12,3.8,0.82,0.68,1.0),(0.62,-0.18,4.0,0.9,0.72,1.05),(0.08,0.5,4.65,0.72,0.62,0.92))
    for j, (ox,oy,oz,sx,sy,sz) in enumerate(offsets, 1):
        leaf_mass(f"Tree canopy {index:02d}.{j:02d}", (x+ox,-z+oy,oz), (sx,sy,sz), mats["leaf"], rotation=0.31*j+0.17*index)


def build_shrub(name, x, z, radius, mats):
    for j, (ox, oy, sz) in enumerate(((-0.28,0.02,0.72),(0.22,-0.12,0.82),(0.02,0.25,0.62)), 1):
        leaf_mass(f"{name} {j:02d}", (x+ox,-z+oy,0.46+0.20*j), (radius, radius*0.78, sz), mats["leaf"], rotation=j*0.7)


def rect_from_source(name, x0, z0, x1, z1, y0, height, mat, col, bevel=0.0):
    return box(name, ((x0+x1)/2, -(z0+z1)/2, y0+height/2), (x1-x0, z1-z0, height), mat, col, bevel)


def wall_piece(name, a, b, dist0, dist1, y0, y1, thickness, mat, col):
    if dist1 - dist0 <= 1e-5 or y1 - y0 <= 1e-5:
        return None
    ax, az = a; bx, bz = b
    length = math.hypot(bx-ax, bz-az)
    ux, uz = (bx-ax)/length, (bz-az)/length
    p0 = (ax+ux*dist0, az+uz*dist0)
    p1 = (ax+ux*dist1, az+uz*dist1)
    cx, cz = (p0[0]+p1[0])/2, (p0[1]+p1[1])/2
    obj = box(name, (cx, -cz, (y0+y1)/2), (dist1-dist0, thickness, y1-y0), mat, col)
    obj.rotation_euler[2] = -math.atan2(uz, ux)
    return obj


def build_wall(wall, mats):
    a, b = wall["a"], wall["b"]
    length = math.hypot(b[0]-a[0], b[1]-a[1])
    base, top = wall["baseY"], wall["baseY"] + wall["height"]
    if wall["id"] == "G-GLAZING":
        mat = mats["glass"]
    else:
        mat = mats["interior"] if wall["id"].startswith("G-I") else mats["concrete"]
    openings = sorted(wall.get("openings", []), key=lambda o: o[1])
    cuts = [0.0] + [v for o in openings for v in (o[1], o[2])] + [length]
    for i in range(0, len(cuts)-1, 2):
        wall_piece(f"Wall {wall['id']} solid {i//2+1:02d}", a, b, cuts[i], cuts[i+1], base, top, wall["thickness"], mat, "Walls and slabs")
    for idx, opening in enumerate(openings, 1):
        kind, start, end, sill, head = opening
        if sill > 0:
            wall_piece(f"Wall {wall['id']} sill {idx:02d}", a, b, start, end, base, base+sill, wall["thickness"], mat, "Walls and slabs")
        if head < wall["height"]:
            wall_piece(f"Wall {wall['id']} lintel {idx:02d}", a, b, start, end, base+head, top, wall["thickness"], mat, "Walls and slabs")
        # Opening infill: glazing for windows, charcoal frame/leaf for doors.
        inset = 0.035
        is_glazed = kind == "window" or wall["id"] == "G-GLAZING"
        fill_mat = mats["glass"] if is_glazed else mats["timber"]
        fill_col = "Architectural glazing" if is_glazed else "Doors and windows"
        wall_piece(f"{kind.title()} {wall['id']} {idx:02d}", a, b, start+inset, end-inset, base+sill+inset, base+head-inset, 0.035, fill_mat, fill_col)
        # Minimal top/side frame members.
        frame = 0.055
        for label, d0, d1, yy0, yy1 in (
            ("L", start, start+frame, base+sill, base+head),
            ("R", end-frame, end, base+sill, base+head),
            ("T", start, end, base+head-frame, base+head),
        ):
            wall_piece(f"Frame {wall['id']} {idx:02d} {label}", a, b, d0, d1, yy0, yy1, 0.07, mats["metal"], "Doors and windows")


def build_scene(g, c, profile):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name in COLLECTIONS:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    mats = make_materials()

    # Site and authoritative polygons.
    parcel = g["parcel"]
    xs, zs = [p[0] for p in parcel], [p[1] for p in parcel]
    rect_from_source("Site ground", min(xs)-2, min(zs)-2, max(xs)+2, max(zs)+2, -0.22, 0.20, mats["land"], "Site and parcel")
    fp = g["footprint"]
    rect_from_source("Ground floor slab", fp["x0"], fp["z0"], fp["x1"], fp["z1"], -0.04, 0.18, mats["stone"], "Ground floor", 0.025)
    rect_from_source("Ground interior finish", fp["x0"]+0.12, fp["z0"]+0.12, fp["x1"]-0.12, fp["z1"]-0.12, 0.14, 0.035, mats["floor"], "Ground floor")
    rect_from_source("Upper floor plate", -3, -3.5, 9.5, 4, 3.02, 0.18, mats["stone"], "Upper floor", 0.02)

    for wall in g["walls"]:
        build_wall(wall, mats)
    for slab in g["slabs"]:
        rect_from_source(slab["id"], slab["x0"], slab["z0"], slab["x1"], slab["z1"], slab["y"], slab["thickness"], mats["concrete"], "Roofs and overhangs", 0.035)

    # Timber upper screen from authoritative bounds/count.
    lv = g["louvres"]
    step = (lv["x1"] - lv["x0"]) / max(1, lv["count"] - 1)
    for i in range(lv["count"]):
        x = lv["x0"] + i*step
        box(f"Upper timber screen {i+1:02d}", (x, -lv["z"], lv["baseY"]+lv["height"]/2), (0.075, 0.26, lv["height"]), mats["timber"], "Timber screens", 0.015)
    box("Upper screen top rail", ((lv["x0"]+lv["x1"])/2,-lv["z"],lv["baseY"]+lv["height"]-0.08),(lv["x1"]-lv["x0"]+0.16,0.30,0.12),mats["metal"],"Timber screens",0.015)
    box("Upper screen bottom rail", ((lv["x0"]+lv["x1"])/2,-lv["z"],lv["baseY"]+0.08),(lv["x1"]-lv["x0"]+0.16,0.30,0.12),mats["metal"],"Timber screens",0.015)
    # Restrained fascia lines clarify roof thickness without changing slab bounds.
    for slab in g["slabs"]:
        y = slab["y"] + slab["thickness"] * 0.48
        rect_from_source(f"{slab['id']} charcoal edge", slab["x0"], slab["z1"]-0.055, slab["x1"], slab["z1"], y, 0.10, mats["metal"], "Roofs and overhangs", 0.01)

    # Reserved stair volume: transparent marker, not invented stairs.
    sb = g["stair"]["bounds"]
    stair = rect_from_source("Reserved stair volume - no detailed flight", sb["x0"], sb["z0"], sb["x1"], sb["z1"], 0.16, 3.0, mats["glass"], "Interior furnishings")
    stair.display_type = "WIRE"
    stair.hide_render = True

    # Original lightweight furnishings supported by recorded room layout.
    box("Living sofa seat", (-7.12, 0.25, 0.46), (0.92, 2.72, 0.34), mats["upholstery"], "Interior furnishings", 0.16)
    box("Living sofa back", (-7.49, 0.25, 0.92), (0.20, 2.72, 0.72), mats["upholstery"], "Interior furnishings", 0.12)
    for side_y in (-1.02, 1.52):
        box(f"Living sofa arm {side_y:+.2f}", (-7.13, side_y, 0.69), (0.86, 0.18, 0.48), mats["upholstery"], "Interior furnishings", 0.11)
    for i, yy in enumerate((-0.58, 0.25, 1.08), 1):
        cushion = box(f"Living cushion {i:02d}", (-7.19, yy, 0.83), (0.28, 0.66, 0.55), mats["upholstery"], "Interior furnishings", 0.13)
        cushion.rotation_euler[1] = math.radians(-7)
    box("Living coffee table top", (-5.65, -0.10, 0.48), (1.45, 0.82, 0.13), mats["stone"], "Interior furnishings", 0.065)
    for sx in (-0.68, 0.68):
        for sy in (-0.3, 0.3):
            box(f"Coffee table leg {sx} {sy}", (-5.65+sx, -0.10+sy, 0.25), (0.08,0.08,0.5), mats["metal"], "Interior furnishings")
    box("Dining table", (1.4, 2.0, 0.78), (2.4, 1.0, 0.10), mats["timber"], "Interior furnishings", 0.065)
    for i, (x,y) in enumerate(((0.5,1.25),(1.4,1.25),(2.3,1.25),(0.5,2.75),(1.4,2.75),(2.3,2.75)),1):
        box(f"Dining chair {i:02d}", (x,y,0.46), (0.48,0.48,0.10), mats["upholstery"], "Interior furnishings", 0.075)
        box(f"Dining chair back {i:02d}", (x,y+(0.20 if y>2 else -0.20),0.78), (0.48,0.08,0.62), mats["timber"], "Interior furnishings", 0.04)
    box("Kitchen island", (1.5, 3.45, 0.58), (3.0, 0.75, 1.0), mats["stone"], "Interior furnishings", 0.04)
    box("Kitchen cabinetry", (1.5, 4.18, 1.05), (3.8, 0.42, 2.0), mats["timber"], "Interior furnishings", 0.03)
    box("Living woven rug", (-5.1, 0.05, 0.205), (3.4, 2.25, 0.035), mats["stone"], "Interior furnishings", 0.03)
    # Verandah remains within the authoritative room bounds x=-8..-1, z=1..4.5.
    rect_from_source("Verandah timber deck", -7.8, 1.18, -1.18, 4.28, 0.16, 0.075, mats["timber"], "Ground floor", 0.025)
    for i, x in enumerate((-7.55, -1.45), 1):
        box(f"Verandah column {i:02d}", (x, -4.0, 1.55), (0.16, 0.16, 2.8), mats["metal"], "Ground floor", 0.02)
    box("Verandah lounge base", (-5.8, -2.85, 0.48), (2.0, 0.82, 0.28), mats["upholstery"], "Interior furnishings", 0.13)
    box("Verandah lounge back", (-5.8, -3.2, 0.84), (2.0, 0.14, 0.62), mats["upholstery"], "Interior furnishings", 0.11)
    for i, x in enumerate((-6.25,-5.35),1):
        box(f"Verandah cushion {i:02d}",(x,-3.07,0.82),(0.72,0.18,0.52),mats["upholstery"],"Interior furnishings",0.12)
    box("Verandah side table", (-3.95, -2.72, 0.45), (0.72, 0.72, 0.12), mats["stone"], "Interior furnishings", 0.05)
    for i, x in enumerate((-7.15, -2.0), 1):
        cylinder(f"Verandah planter {i:02d}", (x, -3.7, 0.48), 0.38, 0.65, mats["stone"], "Pool and landscaping", 12)
        build_shrub(f"Verandah plant {i:02d}", x, 3.7, 0.42, mats)

    # Pool, path and low-poly landscaping anchors.
    pool = g["pool"]
    rect_from_source("Pool basin", pool["x0"]-0.18, pool["z0"]-0.18, pool["x1"]+0.18, pool["z1"]+0.18, -0.05, 0.18, mats["stone"], "Pool and landscaping", 0.06)
    rect_from_source("Pool water", pool["x0"], pool["z0"], pool["x1"], pool["z1"], 0.14, 0.035, mats["water"], "Pool and landscaping")
    # Four coping strips retain authoritative pool bounds while improving the material junction.
    rect_from_source("Pool coping north",pool["x0"]-0.28,pool["z0"]-0.28,pool["x1"]+0.28,pool["z0"],0.13,0.10,mats["stone"],"Pool and landscaping",0.025)
    rect_from_source("Pool coping south",pool["x0"]-0.28,pool["z1"],pool["x1"]+0.28,pool["z1"]+0.28,0.13,0.10,mats["stone"],"Pool and landscaping",0.025)
    rect_from_source("Pool coping west",pool["x0"]-0.28,pool["z0"],pool["x0"],pool["z1"],0.13,0.10,mats["stone"],"Pool and landscaping",0.025)
    rect_from_source("Pool coping east",pool["x1"],pool["z0"],pool["x1"]+0.28,pool["z1"],0.13,0.10,mats["stone"],"Pool and landscaping",0.025)
    for i, (x,z) in enumerate(g["pathStones"], 1):
        rect_from_source(f"Path stone {i:02d}", x-0.42,z-0.34,x+0.42,z+0.34,0.01,0.10,mats["stone"],"Pool and landscaping",0.04)
    for i, (x,z) in enumerate(g["trees"], 1):
        build_tree(i, x, z, mats)
    for i, (x,z,r) in enumerate(((-12.2,1.1,0.62),(-11.2,7.8,0.72),(-8.8,6.8,0.58),(8.8,5.5,0.68),(11.2,-3.8,0.74),(6.8,-6.4,0.56)),1):
        build_shrub(f"Landscape shrub {i:02d}",x,z,r,mats)

    # Procedural lighting profiles.
    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    if profile == "blue-hour":
        bg.inputs["Color"].default_value = (0.025,0.05,0.10,1)
        bg.inputs["Strength"].default_value = 0.34
    else:
        hdri_path = CC0_ROOT / "environment" / "residential_garden_1k.hdr"
        if hdri_path.is_file():
            tex_coord = world.node_tree.nodes.new("ShaderNodeTexCoord")
            mapping = world.node_tree.nodes.new("ShaderNodeMapping")
            mapping.inputs["Rotation"].default_value[2] = math.radians(212)
            environment = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
            environment.name = "CC0 residential garden daylight"
            environment.image = bpy.data.images.load(str(hdri_path), check_existing=True)
            world.node_tree.links.new(tex_coord.outputs["Generated"], mapping.inputs["Vector"])
            world.node_tree.links.new(mapping.outputs["Vector"], environment.inputs["Vector"])
            world.node_tree.links.new(environment.outputs["Color"], bg.inputs["Color"])
            bg.inputs["Strength"].default_value = 0.62
            world["cc0_source"] = "Poly Haven residential_garden"
        else:
            sky = world.node_tree.nodes.new("ShaderNodeTexSky")
            sky.name = "Procedural late-afternoon sky"
            sky.sky_type = "NISHITA"
            sky.sun_elevation = math.radians(18)
            sky.sun_rotation = math.radians(225)
            sky.altitude = 0.15
            sky.air_density = 1.05
            sky.dust_density = 1.6
            sky.ozone_density = 1.0
            world.node_tree.links.new(sky.outputs["Color"], bg.inputs["Color"])
            bg.inputs["Strength"].default_value = 0.26
    sun_data = bpy.data.lights.new("Late afternoon sun", "SUN")
    sun_data.energy = 2.35; sun_data.angle = math.radians(5.0); sun_data.color = (1.0,0.82,0.68)
    sun = bpy.data.objects.new("Late afternoon sun", sun_data); collection("Lights").objects.link(sun); sun.rotation_euler=(math.radians(42),0,math.radians(-128))
    for i, (loc, energy, size) in enumerate((((-4.2,0.3,2.25),180,2.2),((1.2,2.2,2.25),150,1.8),((2.5,-2.0,2.25),110,1.4),((-5.2,1.2,2.4),90,2.8)),1):
        ld=bpy.data.lights.new(f"Warm practical {i:02d}","AREA"); ld.energy=energy; ld.shape="DISK"; ld.size=size; ld.color=(1.0,0.56,0.30)
        lo=bpy.data.objects.new(f"Warm practical {i:02d}",ld); lo.location=loc; lo.rotation_euler=(0,0,0); collection("Lights").objects.link(lo)

    # All registered cameras use the explicit source->Blender conversion.
    for shot in c["shots"]:
        data=bpy.data.cameras.new(shot["id"]+" "+shot["name"]); data.lens=float(shot["lensMm"]); data.sensor_fit="HORIZONTAL"; data.clip_start=0.05; data.clip_end=500
        cam=bpy.data.objects.new(shot["id"]+" "+shot["name"],data); cam.location=src_point(shot["position"]); target=src_point(shot["target"])
        cam.rotation_euler=(target-cam.location).to_track_quat("-Z","Y").to_euler(); cam["source_position"]=shot["position"]; cam["source_target"]=shot["target"]; cam["crop_note"]=shot["crop"]
        collection("Registered cameras").objects.link(cam)

    scene=bpy.context.scene
    scene["geometry_version"]=EXPECTED_ID; scene["generator_version"]=GENERATOR_VERSION
    scene["coordinate_conversion"]="Blender X=source X; Blender Y=-source Z; Blender Z=source Y"
    scene["stair_status"]="reserved-volume"
    scene.unit_settings.system="METRIC"; scene.unit_settings.scale_length=1.0
    scene.render.engine="CYCLES"; scene.cycles.device="CPU"; scene.render.threads_mode="FIXED"; scene.render.threads=2
    scene.cycles.samples=8; scene.cycles.use_denoising=True; scene.cycles.use_adaptive_sampling=True
    scene.cycles.max_bounces=4; scene.cycles.diffuse_bounces=2; scene.cycles.glossy_bounces=2; scene.cycles.transmission_bounces=4; scene.cycles.transparent_max_bounces=4
    scene.render.image_settings.file_format="PNG"; scene.render.image_settings.color_mode="RGBA"; scene.render.film_transparent=False
    scene.view_settings.look="AgX - Medium High Contrast"
    scene.view_settings.exposure = 0.15
    return mats


def inventory(scene):
    meshes=[o for o in bpy.data.objects if o.type=="MESH"]
    return {
        "generatorVersion": GENERATOR_VERSION,
        "blenderVersion": bpy.app.version_string,
        "geometryVersion": scene["geometry_version"],
        "coordinateConversion": scene["coordinate_conversion"],
        "objectCount": len(bpy.data.objects),
        "meshCount": len(meshes),
        "polygonCount": sum(len(o.data.polygons) for o in meshes),
        "materialCount": len(bpy.data.materials),
        "cameraCount": len(bpy.data.cameras),
        "lightCount": len(bpy.data.lights),
        "collections": sorted(c.name for c in bpy.data.collections),
    }


def main():
    args=parse_args(); gp=Path(args.geometry).resolve(); cp=Path(args.cameras).resolve(); out=Path(args.output).resolve(); manifest=Path(args.manifest).resolve()
    g=load_json(gp); c=load_json(cp); validate(g,c); build_scene(g,c,args.profile)
    out.parent.mkdir(parents=True,exist_ok=True); manifest.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out))
    inv=inventory(bpy.context.scene); inv.update({"geometrySource":str(gp),"geometrySHA256":sha256(gp),"cameraSource":str(cp),"cameraSHA256":sha256(cp),"model":str(out),"modelSHA256":sha256(out),"modelBytes":out.stat().st_size})
    if args.render_shot:
        cam=next((o for o in bpy.data.objects if o.type=="CAMERA" and o.name.startswith(args.render_shot+" ")),None)
        if cam is None: raise ValueError("unknown render shot: "+args.render_shot)
        if not args.output_image: raise ValueError("--output-image required with --render-shot")
        scene=bpy.context.scene; scene.camera=cam; scene.render.resolution_x=args.width; scene.render.resolution_y=args.height; scene.render.resolution_percentage=100; scene.cycles.samples=args.samples
        scene.render.filepath=str(Path(args.output_image).resolve()); bpy.ops.render.render(write_still=True)
        inv["render"]={"shot":args.render_shot,"width":args.width,"height":args.height,"samples":args.samples,"output":str(Path(args.output_image).resolve())}
    manifest.write_text(json.dumps(inv,indent=2),encoding="utf-8")
    print("BUILD_RESIDENCE_RESULT "+json.dumps(inv,sort_keys=True))


if __name__ == "__main__":
    main()
