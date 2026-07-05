# artgen/inv_cube_walls.py — tiling mockup for the structure cube.
# A single cube can't show repetition artifacts, so this builds 3x3
# walls of four face treatments, side by side at y = -8:
#   A  control: walnut frame + linen panel (the session-132 study)
#   B  dark-oak planks per cube (user's suggestion)
#   C  mid-oak planks per cube, low-contrast tone alternation
#   D  continuous planking across cubes, staggered butt joints — the
#      "ship hull" read (in-engine this is a world-UV/albedo trick or
#      multi-cell art, not per-cube geometry)
# Front faces only (facing -Y): the question is how the surface tiles,
# not how the block is built. Real cubes would carry this as albedo +
# one chamfered mesh, not plank geometry (INV-6/perf: keep verts flat).

import paperlib as pl
import inventorlib as il

PFX = "InvWalls_"

GAP = 0.012          # plank seam
COURSES = 4          # planks per 1 m face
PLANK_D = 0.06

# Low-contrast oak pair vs low-contrast dark pair.
OAK_A = (0.30, 0.17, 0.075, 1.0)
OAK_B = (0.26, 0.145, 0.062, 1.0)
DARK_A = (0.075, 0.036, 0.017, 1.0)
DARK_B = (0.062, 0.028, 0.013, 1.0)


def mats_extra():
    return {
        "oak_a": pl.get_material("InvOakA", OAK_A, roughness=0.78),
        "oak_b": pl.get_material("InvOakB", OAK_B, roughness=0.78),
        "dark_a": pl.get_material("InvDarkA", DARK_A, roughness=0.8),
        "dark_b": pl.get_material("InvDarkB", DARK_B, roughness=0.8),
    }


def backing(name, x0, mats, parent):
    il.box(name, (x0 + 1.5, 0.05, 1.5), (3.0, 0.04, 3.0),
           [mats], parent=parent)


def plank_tile(prefix, x0, z0, tone_a, tone_b, salt, parent):
    """One 1 m face of horizontal planks, subtle two-tone alternation."""
    pitch = 1.0 / COURSES
    h = pitch - GAP
    for k in range(COURSES):
        tone = tone_a if (salt + k) % 2 == 0 else tone_b
        il.box(f"{prefix}P{k}",
               (x0 + 0.5, 0.0, z0 + pitch * (k + 0.5)),
               (1.0 - GAP, PLANK_D, h), [tone], parent=parent)


def frame_tile(prefix, x0, z0, m, parent):
    """Control: the session-132 frame + linen face."""
    B = 0.09
    il.box(f"{prefix}Bot", (x0 + 0.5, 0.0, z0 + B / 2), (1.0 - GAP, PLANK_D, B),
           [m["wood_dark"]], parent=parent)
    il.box(f"{prefix}Top", (x0 + 0.5, 0.0, z0 + 1 - B / 2), (1.0 - GAP, PLANK_D, B),
           [m["wood_dark"]], parent=parent)
    il.box(f"{prefix}L", (x0 + B / 2, 0.0, z0 + 0.5), (B, PLANK_D, 1 - 2 * B - GAP),
           [m["wood_dark"]], parent=parent)
    il.box(f"{prefix}R", (x0 + 1 - B / 2, 0.0, z0 + 0.5), (B, PLANK_D, 1 - 2 * B - GAP),
           [m["wood_dark"]], parent=parent)
    il.box(f"{prefix}Panel", (x0 + 0.5, 0.02, z0 + 0.5), (0.84, PLANK_D - 0.03, 0.84),
           [m["linen"]], parent=parent)


def wall_ABC(root, tag, x0, kind, m, mx):
    for ix in range(3):
        for iz in range(3):
            p = f"{PFX}{tag}_{ix}{iz}_"
            if kind == "frame":
                frame_tile(p, x0 + ix, iz, m, root)
            elif kind == "dark":
                plank_tile(p, x0 + ix, iz, mx["dark_a"], mx["dark_b"],
                           ix * 7 + iz * 3, root)
            else:
                plank_tile(p, x0 + ix, iz, mx["oak_a"], mx["oak_b"],
                           ix * 7 + iz * 3, root)
    backing(f"{PFX}{tag}_Back", x0, m["ink"], root)


def wall_hull(root, x0, mx, m):
    """Continuous courses across the full 3 m, staggered joints."""
    total = 3.0
    pitch = 1.0 / COURSES
    h = pitch - GAP
    lengths = [1.3, 0.9, 1.1, 0.7, 1.5]
    for k in range(COURSES * 3):
        z = pitch * (k + 0.5)
        start = 0.0
        j = k % len(lengths)
        # stagger: first board of each course is trimmed differently
        first_trim = 0.25 + 0.31 * ((k * 2) % 3)
        seg = first_trim
        i = 0
        while start < total - 0.05:
            seg = min(seg, total - start)
            tone = mx["oak_a"] if (k + i) % 2 == 0 else mx["oak_b"]
            il.box(f"{PFX}D_{k}_{i}",
                   (x0 + start + seg / 2 - GAP / 2, 0.0, z),
                   (seg - GAP, PLANK_D, h), [tone], parent=root)
            start += seg
            i += 1
            seg = lengths[(j + i) % len(lengths)]
    backing(f"{PFX}D_Back", x0, m["ink"], root)


def build(loc=(0.0, -8.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    mx = mats_extra()
    root = il.root_empty(PFX + "Root", loc)
    wall_ABC(root, "A", -8.0, "frame", m, mx)
    wall_ABC(root, "B", -4.0, "dark", m, mx)
    wall_ABC(root, "C", 0.0, "oak", m, mx)
    wall_hull(root, 4.0, mx, m)
    return root
