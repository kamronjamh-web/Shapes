"""Build the Square handgun review asset in Blender. No gameplay assets are replaced."""
import bpy
import json
import math
import re
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art" / "WeaponConcepts" / "SquareHandgun"
OUT.mkdir(parents=True, exist_ok=True)
(OUT / ".gdignore").write_text("", encoding="utf-8")
(OUT / "Renders").mkdir(exist_ok=True)

# Read the game's palette/progression instead of creating a second rarity scheme.
catalog_text = (ROOT / "Scripts/Equipment/Data/EquipmentCatalogDefaults.cs").read_text(encoding="utf-8")
rarity_rows = re.findall(r'CreateRarity\("([^"]+)", "([^"]+)"[^\n]+new Color\(([^)]+)\)', catalog_text)
RARITIES = [(key, name, tuple(float(value.strip().rstrip("f")) for value in rgb.split(",")))
            for key, name, rgb in rarity_rows]
assert len(RARITIES) == 6, "Expected the six canonical rarity definitions"
progression_rows = dict((key, (int(start), int(end))) for key, start, end in re.findall(
    r'\["([^"]+)"\] = new WeaponCustomizationProgression\((\d+), (\d+), \d+\)', catalog_text))

def linear_color(rgb):
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb)

def tier_expression(values, variable="tier"):
    return "".join(f"{value} if {variable}<{index + .5} else " for index, value in enumerate(values[:-1])) + str(values[-1])

def property_driver(owner, path, expression, variables, index=None):
    assert len(expression) < 256, f"Blender driver expression too long: {len(expression)}"
    curve = owner.driver_add(path) if index is None else owner.driver_add(path, index)
    driver = curve.driver
    driver.type = "SCRIPTED"
    for name, target, prop in variables:
        var = driver.variables.new()
        var.name = name
        var.type = "SINGLE_PROP"
        var.targets[0].id = target
        var.targets[0].data_path = f'["{prop}"]'
    driver.expression = expression
    return curve

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    if collection.name != "Collection":
        bpy.data.collections.remove(collection)
base_collection = bpy.data.collections.get("Collection")
base_collection.name = "SQ-01 | HANDGUN"
studio = bpy.data.collections.new("STUDIO | review only")
bpy.context.scene.collection.children.link(studio)
parts = {}
for name in ["01 | Sculpted frame", "02 | Square slide", "03 | Shape dock", "04 | Controls and finish", "05 | Modulator cartridges"]:
    collection = bpy.data.collections.new(name)
    base_collection.children.link(collection)
    parts[name[:2]] = collection

def material(name, color, metallic=0.0, roughness=0.45, emission=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*color, 1)
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat

steel = material("01 | Satin gunmetal", (0.11, 0.145, 0.175), 0.72, 0.32)
frame_mat = material("02 | Warm graphite frame", (0.16, 0.175, 0.17), 0.12, 0.52)
grip_mat = material("03 | Charcoal grip", (0.028, 0.039, 0.043), 0.03, 0.76)
groove_mat = material("04 | Recess black", (0.009, 0.016, 0.021), 0.13, 0.5)
edge_mat = material("05 | Machined edge", (0.28, 0.33, 0.35), 0.8, 0.27)
ivory = material("06 | Square ceramic", (0.81, 0.83, 0.74), 0.2, 0.37)
ink = material("07 | Square inset", (0.015, 0.025, 0.03), 0.25, 0.35)
rarity_accent = material("08 | Shape rarity accent - driven by SquareInsert", linear_color(RARITIES[2][2]), 0.3, 0.35, .12)
dot_mat = material("09 | Sight dots", (0.55, 0.7, 0.65), 0.12, 0.4)
floor_mat = material("Studio | backdrop", (0.045, 0.058, 0.071), 0.1, 0.65)

gun = bpy.data.objects.new("GunRoot", None)
base_collection.objects.link(gun)
gun["design"] = "Square / conventional compact handgun with a removable shape insert"
gun["status"] = "Visual review model; cosmetic geometry; not integrated into gameplay"
gun["forward_axis"] = "+Y in Blender"
gun["weapon_level"] = 5
gun.id_properties_ui("weapon_level").update(min=1, max=5, description="Preview level; socket unlocks follow the game's rarity and level progression")
gun["equipped_modulators"] = 3
gun.id_properties_ui("equipped_modulators").update(min=0, max=10, description="Number of example installed cartridges, capped by unlocked slots")
model_objects = []
anchors = []

def move_to(obj, collection):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)

def finish(obj, mat, group="04", bevel=0, segments=1, parent=gun):
    move_to(obj, parts[group])
    obj.parent = parent
    if mat:
        obj.data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new("Small fitted edge bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
    if obj.type == "MESH":
        mod = obj.modifiers.new("Weighted planar normals", "WEIGHTED_NORMAL")
        mod.keep_sharp = True
        for poly in obj.data.polygons:
            poly.use_smooth = False
    model_objects.append(obj)
    return obj

def mesh(name, verts, faces, mat, group="04", bevel=0, segments=1, parent=gun):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    parts[group].objects.link(obj)
    # Consistent outward normals are important for both Blender and later GLB review.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)
    return finish(obj, mat, group, bevel, segments, parent)

def prism_yz(name, profile, width, mat, group="04", bevel=0.0006, center_x=0, parent=gun):
    n = len(profile)
    verts = [(center_x + side * width / 2, y, z) for side in (-1, 1) for y, z in profile]
    faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    return mesh(name, verts, faces, mat, group, bevel, parent=parent)

def box(name, loc, size, mat, group="04", bevel=0.0006, parent=gun):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, group, bevel, parent=parent)

def loft_slide(name, sections, mat):
    verts = []
    for y, w, bottom, top, chamfer in sections:
        verts += [(x, y, z) for x, z in [
            (-w + chamfer, bottom), (w - chamfer, bottom), (w, bottom + chamfer),
            (w, top - chamfer), (w - chamfer, top), (-w + chamfer, top),
            (-w, top - chamfer), (-w, bottom + chamfer)]]
    faces = [tuple(reversed(range(8))), tuple(range(len(verts) - 8, len(verts)))]
    for section in range(len(sections) - 1):
        start = section * 8
        faces += [(start + i, start + (i + 1) % 8, start + 8 + (i + 1) % 8, start + 8 + i) for i in range(8)]
    return mesh(name, verts, faces, mat, "02", 0.0007, 1)

def ring_yz(name, outer, inner, width, mat, group="01", bevel=0.0008):
    count = len(outer)
    verts = [(side * width / 2, y, z) for side in (-1, 1) for loop in (outer, inner) for y, z in loop]
    faces = []
    for i in range(count):
        j = (i + 1) % count
        faces += [(i, j, count + j, count + i),
                  (2 * count + i, 3 * count + i, 3 * count + j, 2 * count + j),
                  (i, 2 * count + i, 2 * count + j, j),
                  (count + i, count + j, 3 * count + j, 3 * count + i)]
    return mesh(name, verts, faces, mat, group, bevel)

def ring_xy(name, center, outer, inner, depth, mat, parent=gun, group="03"):
    x, y, z = center
    profile = [(-outer[0]/2, -outer[1]/2), (outer[0]/2, -outer[1]/2),
               (outer[0]/2, outer[1]/2), (-outer[0]/2, outer[1]/2)]
    hole = [(-inner[0]/2, -inner[1]/2), (inner[0]/2, -inner[1]/2),
            (inner[0]/2, inner[1]/2), (-inner[0]/2, inner[1]/2)]
    verts = [(x + a, y + b, z + sign * depth/2) for sign in (-1, 1) for loop in (profile, hole) for a,b in loop]
    faces = []
    for i in range(4):
        j = (i + 1) % 4
        faces += [(i,j,4+j,4+i),(8+i,12+i,12+j,8+j), (i,8+i,8+j,j),(4+i,4+j,12+j,12+i)]
    return mesh(name, verts, faces, mat, group, 0.00035, parent=parent)

def cylinder(name, loc, radius, depth, mat, axis="X", vertices=12, group="04"):
    rotation = (0, math.pi/2, 0) if axis == "X" else ((math.pi/2,0,0) if axis == "Y" else (0,0,0))
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, group, bevel=0.00025)

def subtract(target, cutter):
    bpy.context.view_layer.objects.active = target
    mod = target.modifiers.new("Visible recess", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    # Put the subtraction before bevels and normals.
    bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

# The receiver and grip are one custom silhouette, rather than stacked boxes.
frame = prism_yz("Frame | continuous receiver and swept grip", [
    (-.099,.030),(.115,.030),(.128,.018),(.119,-.012),(.051,-.016),
    (.020,-.024),(-.028,-.027),(-.036,-.064),(-.044,-.116),
    (-.057,-.139),(-.099,-.144),(-.114,-.132),(-.109,-.113),
    (-.091,-.063),(-.083,-.019),(-.099,-.013),(-.107,.004)],
    .045, frame_mat, "01", .0022)
slide = loft_slide("Slide | long tapered chamfered shell", [
    (-.108,.025,.028,.067,.004),(-.097,.029,.026,.073,.005),
    (.097,.026,.026,.069,.0045),(.126,.024,.028,.066,.004),
    (.135,.021,.030,.062,.003)], steel)

# A real shallow pocket makes the square read as an inserted object, not a decal.
bpy.ops.mesh.primitive_cube_add(size=1, location=(0,-.050,.073))
cutter = bpy.context.object
cutter.name = "Temporary shape-pocket cutter"
cutter.dimensions = (.039,.039,.025)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
subtract(slide, cutter)
box("Dock | dark pocket floor", (0,-.050,.0608), (.037,.037,.001), groove_mat, "03", .0003)
ring_xy("Dock | fitted rim", (0,-.050,.0725), (.043,.043), (.034,.034), .0028, rarity_accent)

socket = bpy.data.objects.new("ShapeSocket", None)
parts["03"].objects.link(socket)
socket.parent = gun
socket.empty_display_type = "PLAIN_AXES"
socket.empty_display_size = .018
socket.location = (0,-.050,.068)
socket["purpose"] = "Swap the insert here for later equipped-shape variants. Local +Z is the insertion axis."
insert = box("SquareInsert | removable ceramic cartridge", (0,0,0), (.030,.030,.009), rarity_accent, "03", .0012, socket)
insert["shape_key"] = "square"
insert["rarity_tier"] = 2
insert.id_properties_ui("rarity_tier").update(min=0, max=5, description="0 Common / 1 Uncommon / 2 Rare / 3 Legendary / 4 Mythic / 5 Divine. Drives all handgun accents.")
insert["rarity_legend"] = " / ".join(f"{i} {row[1]}" for i, row in enumerate(RARITIES))
accent_bsdf = rarity_accent.node_tree.nodes.get("Principled BSDF")
for channel in range(3):
    expression = tier_expression([linear_color(row[2])[channel] for row in RARITIES])
    for input_name in ("Base Color", "Emission Color"):
        property_driver(accent_bsdf.inputs[input_name], "default_value", expression,
                        [("tier", insert, "rarity_tier")], channel)
    property_driver(rarity_accent, "diffuse_color", expression, [("tier", insert, "rarity_tier")], channel)
box("SquareInsert | dark square face", (0,0,.0047), (.0215,.0215,.0009), ink, "03", .00035, insert)
box("SquareInsert | indexing notch", (.012,-.008,.005), (.0038,.006,.0007), ivory, "03", .0003, insert)
box("Dock | positive latch", (.024,-.050,.0645), (.005,.014,.006), edge_mat, "03", .001)
box("Dock | latch witness", (.0267,-.050,.0646), (.0007,.007,.002), rarity_accent, "03", .0001)

# The same shape is legible in profile through a small side witness window.
for side in (-1,1):
    prism_yz(f"Dock | side recess {side}", [(-.066,.050),(-.038,.050),(-.038,.065),(-.066,.065)],
             .0014, groove_mat, "03", .0005, side * .029)
    prism_yz(f"Dock | square witness {side}", [(-.059,.052),(-.047,.052),(-.047,.064),(-.059,.064)],
             .0016, rarity_accent, "03", .0004, side * .0298)
    prism_yz(f"Dock | witness face {side}", [(-.0565,.0545),(-.0495,.0545),(-.0495,.0615),(-.0565,.0615)],
             .0008, ink, "03", .0001, side * .0308)

# Open guard with an actual aperture and a single bent trigger silhouette.
ring_yz("Frame | open shaped trigger guard",
    [(-.041,-.022),(.022,-.022),(.041,-.031),(.048,-.044),(.040,-.062),(.029,-.071),(-.024,-.068),(-.045,-.053)],
    [(-.030,-.032),(.018,-.032),(.031,-.038),(.036,-.045),(.030,-.054),(.023,-.060),(-.019,-.058),(-.033,-.049)],
    .023, frame_mat, bevel=.0013)
prism_yz("Trigger | curved silhouette", [(-.014,-.030),(-.010,-.033),(-.010,-.039),(-.014,-.047),(-.022,-.053),(-.025,-.049),(-.018,-.042),(-.017,-.034)],
         .010, steel, bevel=.0008)

# Sculpted grip panels, subtly cut into the frame silhouette.
grip_profile = [(-.077,-.029),(-.048,-.031),(-.049,-.056),(-.056,-.115),
                (-.065,-.129),(-.094,-.132),(-.101,-.123),(-.088,-.079)]
for side in (-1,1):
    prism_yz(f"Grip | contoured panel {side}", grip_profile, .003, grip_mat, "01", .0015, side * .023)
    for stripe in range(8):
        z = -.047 - stripe * .010
        rear = -.081 - stripe * .0019
        front = -.049 - stripe * .0013
        points = [(rear,z-.0008),(front,z+.003),(front-.0005,z+.005),(rear-.0005,z+.0012)]
        prism_yz(f"Grip | angled texture {side} {stripe:02}", points, .0008, frame_mat, "04", .0001, side * .025)
    cylinder(f"Grip | upper panel fastener {side}", (side*.0256,-.061,-.039), .0023, .001, steel)
    cylinder(f"Grip | lower panel fastener {side}", (side*.0256,-.080,-.122), .0023, .001, steel)
prism_yz("Grip | fitted heel plate", [(-.103,-.134),(-.055,-.132),(-.052,-.142),(-.101,-.148),(-.115,-.140)], .047, grip_mat, "01", .0011)
for sign in (-1, 1):
    prism_yz(f"Accent | grip heel inlay {sign}", [(-.101,-.134),(-.058,-.1325),(-.057,-.1345),(-.102,-.136)],
             .0008, rarity_accent, "04", .0002, sign * .024)
    # Follow the tapered slide sides instead of floating a straight strip beside them.
    verts = [(sign*x, y, z) for y,x in [(-.069,.0294),(.097,.0264),(.121,.0248)] for z in (.031,.0332)]
    mesh(f"Accent | slide seam {sign}", verts, [(0,1,3,2),(2,3,5,4)], rarity_accent)

# Grip cuts are actual shallow negative-space cuts in the slide.
for side in (-1,1):
    for i in range(4):
        y = -.096 + i*.005
        bpy.ops.mesh.primitive_cube_add(size=1, location=(side*.0295,y,.052))
        cutter = bpy.context.object
        cutter.dimensions = (.006,.0018,.024)
        cutter.rotation_euler.x = math.radians(-13)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        subtract(slide, cutter)
    for i in range(3):
        y = .071 + i*.006
        prism_yz(f"Slide | front traction groove {side} {i}", [(y,.039),(y+.0018,.039),(y-.0018,.061),(y-.0036,.061)],
                 .0005, groove_mat, "04", .0001, side * .0265)

# Simple cosmetic muzzle, sight posts, and familiar external controls.
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.0085, depth=.026,
    location=(0,.138,.046), rotation=(math.pi/2,0,0))
subtract(slide, bpy.context.object)
cylinder("Muzzle | dark recess", (0,.126,.046), .0082, .001, groove_mat, axis="Y", vertices=16)
box("Sight | front tapered post", (0,.111,.0705), (.005,.013,.006), groove_mat, bevel=.0008)
box("Sight | front contrast dot", (0,.1043,.0716), (.0024,.0006,.0022), dot_mat, bevel=.0002)
for side in (-1,1):
    box(f"Sight | rear notch ear {side}", (side*.008,-.095,.077), (.006,.010,.008), groove_mat, bevel=.0008)
    box(f"Sight | rear dot {side}", (side*.008,-.1003,.077), (.0023,.0007,.002), dot_mat, bevel=.0002)
    prism_yz(f"Controls | slim slide release {side}", [(-.078,.012),(-.044,.012),(-.039,.018),(-.044,.020),(-.078,.018)],
             .0026, steel, bevel=.00065, center_x=side*.0245)
    cylinder(f"Controls | frame pin {side}", (side*.0239,-.021,.013), .0032, .0016, edge_mat)
    cylinder(f"Controls | grip latch {side}", (side*.0252,-.044,-.020), .0037, .0020, steel)
    for i in range(3):
        box(f"Frame | accessory recess {side} {i}", (side*.0226,.054+i*.014,-.005), (.0008,.007,.005), groove_mat, bevel=.00025)

# Ten independent side sockets; five on each cheek of the receiver. The shape
# stays on top, while elongated keyed cartridges read as a different item class.
modulator_text = (ROOT / "Scripts/Equipment/Data/Modulators/ModulatorCatalog.Tier1.cs").read_text(encoding="utf-8")
modulator_palette = {}
for key in ("sharpened_form", "quickened_core", "long_reach", "expanded_shape", "accelerated_flight"):
    match = re.search(r'"' + key + r'",\s*"([^"]+)".*?new Color\(([^)]+)\)', modulator_text, re.S)
    assert match, f"Missing example modulator {key}"
    rgb = tuple(float(value.strip().rstrip("f")) for value in match[2].split(","))
    modulator_palette[key] = (match[1], material(f"Modulator | {match[1]}", linear_color(rgb), .25, .38, .12))

slot_formulas = [f"{start}+{end-start}*(lv-1)/4" for start,end in (progression_rows[row[0]] for row in RARITIES)]
unlock_expression = f"floor(({tier_expression(slot_formulas, 't')})+.5)"
slot_vars = [("t", insert, "rarity_tier"), ("lv", gun, "weapon_level"), ("count", gun, "equipped_modulators")]
cartridges = []
modulator_examples = []

def visibility_driver(obj, hide_when):
    property_driver(obj, "hide_render", hide_when, slot_vars)
    property_driver(obj, "hide_viewport", hide_when, slot_vars)

def keyed_cartridge(name, width, height, depth, z, mat, parent):
    cut = .002
    x, y = width/2, height/2
    profile = [(-x+cut,-y),(x-cut,-y),(x,-y+cut),(x,y-cut),(x-cut,y),(-x+cut,y),(-x,y-cut),(-x,-y+cut)]
    verts = [(a,b,z+c*depth/2) for c in (-1,1) for a,b in profile]
    faces = [tuple(reversed(range(8))),tuple(range(8,16))]
    faces += [(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    return mesh(name, verts, faces, mat, "05", .00025, parent=parent)

for slot_index in range(10):
    sign = 1 if slot_index % 2 == 0 else -1
    row = slot_index // 2
    y = -.008 + row * .021
    anchor = bpy.data.objects.new(f"ModulatorSocket{slot_index+1:02}", None)
    parts["05"].objects.link(anchor)
    anchor.parent = gun
    anchor.location = (sign*.021,y,.010)
    anchor.rotation_euler.y = sign * math.pi/2
    anchor.empty_display_size = .007
    anchor["slot_index"] = slot_index
    anchor["insertion_axis"] = "Local +Z points outward from receiver"
    anchors.append(anchor)

    bpy.ops.mesh.primitive_cube_add(size=1, location=(sign*.0235,y,.010))
    cutter = bpy.context.object
    cutter.dimensions = (.008,.0165,.0205)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    subtract(frame, cutter)
    ring_xy(f"Modulator {slot_index+1:02} | recessed socket lip", (0,0,.001),
            (.023,.019), (.019,.015), .001, edge_mat, anchor, "05")
    box(f"Modulator {slot_index+1:02} | socket interior", (0,0,-.0014),
        (.019,.015,.0006), groove_mat, "05", .0001, anchor)

    # A flat cover means locked; exposed contacts mean available but empty.
    cover = keyed_cartridge(f"Modulator {slot_index+1:02} | locked cover", .018,.014,.001,.0003, grip_mat, anchor)
    visibility_driver(cover, f"{slot_index} < ({unlock_expression})")
    slash = box(f"Modulator {slot_index+1:02} | locked witness", (0,0,.00095),
                (.009,.001,.0003), edge_mat, "05", .0001, anchor)
    slash.rotation_euler.z = .55
    visibility_driver(slash, f"{slot_index} < ({unlock_expression})")
    for contact in (-1,1):
        obj = box(f"Modulator {slot_index+1:02} | empty contact {contact}", (contact*.005,0,-.0008),
                  (.002,.008,.0004), edge_mat, "05", .0001, anchor)
        visibility_driver(obj, f"{slot_index} >= ({unlock_expression}) or count > {slot_index}")

    key = list(modulator_palette)[slot_index % len(modulator_palette)]
    name, mod_mat = modulator_palette[key]
    cartridge = keyed_cartridge(f"Modulator {slot_index+1:02} | {name}", .018,.0135,.0036,.0001, steel, anchor)
    cartridge["modulator_key"] = key
    cartridge["preview_example"] = True
    cartridges.append(cartridge)
    face = keyed_cartridge(f"Modulator {slot_index+1:02} | inset face", .014,.010,.0005,.002, grip_mat, cartridge)
    pip = box(f"Modulator {slot_index+1:02} | type color", (0,.0031,.0025),
              (.010,.0022,.0006), mod_mat, "05", .00015, cartridge)
    badge = box(f"Modulator {slot_index+1:02} | raised key", (-.005,0,.0025),
                (.0015,.0025,.0006), ivory, "05", .00015, cartridge)
    installed_parts = [cartridge,face,pip,badge]
    # One to three bars remain readable if two modulator colors are similar.
    for bar in range(slot_index % 3 + 1):
        glyph = box(f"Modulator {slot_index+1:02} | identity bar {bar}",
                    (-.002+bar*.002,-.001,.0025), (.0007,.003,.0005), mod_mat, "05", .0001, cartridge)
        installed_parts.append(glyph)
    for obj in installed_parts:
        visibility_driver(obj, f"count <= {slot_index} or {slot_index} >= ({unlock_expression})")
    modulator_examples.append({"socket":anchor.name,"example_key":key,"example_name":name})

# A modest rectangular marking, kept on the side rather than the player's sight line.
def label(name, text, position, rotation, size, mat, group="04"):
    data = bpy.data.curves.new(name, "FONT")
    data.body = text
    data.size = size
    data.extrude = 0.00002
    data.align_x = "CENTER"
    data.align_y = "CENTER"
    obj = bpy.data.objects.new(name, data)
    parts[group].objects.link(obj)
    obj.location = position
    obj.rotation_euler = rotation
    obj.parent = gun
    obj.data.materials.append(mat)
    model_objects.append(obj)
    return obj

label("Marking | square identifier", "SQ / 01", (.0276,.017,.053), (math.pi/2,0,math.pi/2), .0042, ivory)

muzzle_anchor = bpy.data.objects.new("MuzzleAnchor", None)
parts["04"].objects.link(muzzle_anchor)
muzzle_anchor.parent = gun
muzzle_anchor.location = (0,.139,.046)
muzzle_anchor.empty_display_size = .008

# Presentation uses the actual model geometry, with no composited illustration.
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.render.engine = "CYCLES"
scene.cycles.samples = 40
scene.cycles.use_denoising = True
scene.render.resolution_x = 1600
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.11,.14,.18,1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = .42
scene.view_settings.view_transform = "AgX"
scene.view_settings.exposure = -0.65

try:
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == "OPTIX"
    if any(device.use for device in prefs.devices):
        scene.cycles.device = "GPU"
except Exception as error:
    print("Cycles CPU fallback:", error, flush=True)

bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.154))
floor = bpy.context.object
floor.name = "Studio ground"
move_to(floor, studio)
floor.data.materials.append(floor_mat)

def light(name, location, energy, size, color):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name,data)
    studio.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector((0,0,0))-obj.location).to_track_quat("-Z","Y").to_euler()

light("Studio | broad key", (.25,-.22,.48), 16, .40, (1,.91,.80))
light("Studio | cool fill", (-.29,-.05,.22), 11, .32, (.68,.80,1))
light("Studio | edge light", (.12,.35,.28), 22, .24, (.91,.95,1))

def camera(name, location, target, scale):
    data = bpy.data.cameras.new(name)
    obj = bpy.data.objects.new(name,data)
    studio.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()
    data.type = "ORTHO"
    data.ortho_scale = scale
    data.lens = 60
    data.clip_start = .001
    return obj

hero = camera("Review | 01 three-quarter", (.41,.39,.26), (0,.006,-.025), .45)
side = camera("Review | 02 silhouette", (.52,.035,.09), (0,.002,-.030), .405)
rear = camera("Review | 03 slot and player angle", (.18,-.37,.30), (0,-.028,.018), .245)
mod_view = camera("Review | 05 recessed modulator bank", (.43,.16,.15), (0,.030,.016), .225)
scene.camera = hero

def refresh_review(tier=2, level=5, count=3):
    insert["rarity_tier"] = tier
    gun["weapon_level"] = level
    gun["equipped_modulators"] = count
    insert.update_tag()
    gun.update_tag()
    scene.frame_set(scene.frame_current)
    bpy.context.view_layer.update()

# Verify actual Blender driver evaluation, including palette and socket states.
for tier, (key, name, rgb) in enumerate(RARITIES):
    for level, expected_slots in ((1, progression_rows[key][0]), (5, progression_rows[key][1])):
        refresh_review(tier, level, 10)
        assert sum(not obj.hide_render for obj in cartridges) == expected_slots, f"Socket visibility mismatch: {key} level {level}"
        actual = accent_bsdf.inputs["Base Color"].default_value[:3]
        assert all(abs(a-b)<.00001 for a,b in zip(actual, linear_color(rgb))), f"Accent driver mismatch: {key}"
refresh_review(count=0)
assert all(obj.hide_render for obj in cartridges), "Empty equipment preview still contains cartridges"
refresh_review()

# Pose the project for immediate useful review when Blender opens it.
bpy.ops.object.select_all(action="DESELECT")
insert.select_set(True)
bpy.context.view_layer.objects.active = insert
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            space = area.spaces.active
            space.shading.type = "MATERIAL"
            space.overlay.show_extras = False
            space.overlay.show_floor = False
            space.region_3d.view_rotation = hero.rotation_euler.to_quaternion()
            space.region_3d.view_location = Vector((0,0,-.030))
            space.region_3d.view_distance = .48
            space.clip_start = .001
        elif area.type == "PROPERTIES":
            area.spaces.active.context = "OBJECT"

# Editable source keeps bevel/normal modifiers. The portable review export applies them.
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "SquareHandgun.blend"))
bpy.ops.object.select_all(action="DESELECT")
for obj in model_objects + [gun, socket, muzzle_anchor] + anchors:
    obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT / "SquareHandgun.glb"), export_format="GLB", use_selection=True,
                          use_visible=True, export_apply=True, export_cameras=False, export_lights=False, export_yup=True,
                          export_extras=True)

depsgraph = bpy.context.evaluated_depsgraph_get()
triangles = 0
for obj in model_objects:
    if obj.type not in {"MESH", "FONT"}:
        continue
    evaluated = obj.evaluated_get(depsgraph)
    evaluated_mesh = evaluated.to_mesh()
    evaluated_mesh.calc_loop_triangles()
    triangles += len(evaluated_mesh.loop_triangles)
    evaluated.to_mesh_clear()
report = {"model": "SQ / 01", "triangles_with_bevels": triangles,
          "model_objects": len(model_objects), "equipped_shape": "square",
          "source": "SquareHandgun.blend", "review_export": "SquareHandgun.glb",
          "socket": "GunRoot/ShapeSocket", "integration": "review only",
          "rarity_preview": "rare", "rarity_control": "SquareInsert.rarity_tier",
          "rarity_palette": [{"index":i,"key":row[0],"display_name":row[1],"srgb":row[2]} for i,row in enumerate(RARITIES)],
          "accent_material": rarity_accent.name, "modulator_capacity":10, "preview_equipped":3,
          "modulator_examples":modulator_examples,
          "checks": "Six rarity colors, level 1 and level 5 capacity, empty equipment visibility verified in Blender"}
(OUT / "model-info.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
(OUT / "README.txt").write_text(
    "SQ / 01 - Square handgun review\n\n"
    "A conventional handgun silhouette with a shallow, visible square-cartridge dock.\n"
    "The frame is a continuous contoured mesh; the slide is tapered and chamfered.\n"
    "Open trigger guard, shaped grip panels, cut slide grooves, and separate controls.\n\n"
    "Open SquareHandgun.blend. The Review cameras show the silhouette and sockets.\n"
    "GunRoot groups the complete model. ShapeSocket owns the removable SquareInsert.\n"
    "The dock rim and side witness panel make the slotted square readable from above and in profile.\n"
    "The fourth render lifts the insert to show the open pocket; the source file opens with it seated.\n"
    "Later shapes can use the same socket with a different slide/upper silhouette.\n"
    "\nRARITY PREVIEW\n"
    "Select SquareInsert, open Object Properties > Custom Properties, and change rarity_tier.\n"
    "0 Common (white), 1 Uncommon (green), 2 Rare (blue), 3 Legendary (purple),\n"
    "4 Mythic (red), 5 Divine (gold). Values are read from EquipmentCatalogDefaults.cs.\n"
    "Insert shell, slot rim, side witness, slide seams and grip heel share one driven material.\n"
    "Material Preview or Rendered shading shows changes immediately; no add-on is needed.\n"
    "\nMODULATOR PREVIEW\n"
    "GunRoot custom properties: weapon_level (1-5), equipped_modulators (0-10).\n"
    "Five recessed cartridges per side; ModulatorSocket01-10 are independent attachment anchors.\n"
    "Slots alternate right/left from rear to front and follow the game's rarity/level unlocks.\n"
    "Installed = keyed cartridge with type marker; empty = open contacts; locked = dark blanking cap.\n"
    "Modulator markers retain their own catalog colors and are independent of shape rarity.\n"
    "Default preview: Rare, level 5, three example modulators, six unlocked bays.\n"
    "Examples show mounting/identification; the review file is not connected to player inventory.\n"
    "Renders/06_full_modulator_capacity.png shows all ten installed with Divine rarity.\n"
    "Renders/Rarities contains the six accent-color previews.\n\n"
    "Source retains editable modifiers and rarity/visibility drivers; GLB snapshots the Rare preview.\n"
    "Studio collection is presentation only and is excluded from the GLB.\n"
    f"Evaluated model: {triangles} triangles, {len(model_objects)} objects.\n"
    "This concept folder is ignored by Godot. No runtime weapon or game statistics changed.\n"
    "This is cosmetic game artwork, without functional internal mechanisms.\n",
    encoding="utf-8")

for cam, filename in [(hero,"01_three_quarter.png"),(side,"02_silhouette.png"),(rear,"03_shape_slot.png"),
                      (mod_view,"05_modulator_bays.png")]:
    scene.camera = cam
    scene.render.filepath = str(OUT / "Renders" / filename)
    bpy.ops.render.render(write_still=True)
    print("RENDER_SAVED", filename, flush=True)
insert.location.z = .043
scene.camera = rear
scene.render.filepath = str(OUT / "Renders" / "04_insert_lifted.png")
bpy.ops.render.render(write_still=True)
insert.location.z = 0
refresh_review(5,5,10)
scene.camera = mod_view
scene.render.filepath = str(OUT / "Renders" / "06_full_modulator_capacity.png")
bpy.ops.render.render(write_still=True)
(OUT / "Renders" / "Rarities").mkdir(exist_ok=True)
scene.render.resolution_percentage = 65
scene.camera = hero
for tier, (key,name,rgb) in enumerate(RARITIES):
    refresh_review(tier,5,3)
    scene.render.filepath = str(OUT / "Renders" / "Rarities" / f"{tier+1:02}_{key}.png")
    bpy.ops.render.render(write_still=True)
scene.render.resolution_percentage = 100
refresh_review()
scene.camera = hero
bpy.ops.object.select_all(action="DESELECT")
insert.select_set(True)
bpy.context.view_layer.objects.active = insert
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "SquareHandgun.blend"))
print("SQUARE_HANDGUN_COMPLETE", json.dumps(report), flush=True)
