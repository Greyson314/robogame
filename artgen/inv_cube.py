# artgen/inv_cube.py — inventor structure cube, v2 (session-132 cube
# call): continuous oak planking, no frame, no pegs. Four courses ring
# the cube with course heights aligned across faces; every course is
# split at a staggered butt joint so tiled cubes read as one hull, not
# a grid (see inv_cube_walls.py for the A/B/C/D comparison). Slightly-
# darker two-tone oak with directional grain (inventorlib.oak_grain).
# In-game this is ONE chamfered mesh + a plank albedo with 3-4 joint
# variants picked per block via MaterialPropertyBlock UV offset — the
# plank-box geometry here is study-only.

import paperlib as pl
import inventorlib as il

PFX = "InvCube_"

GAP = 0.012
DEPTH = 0.03
R_IN = 0.47          # planks span -R_IN..R_IN across the course axis
FACE = 0.485         # plank layer centre offset from cube centre

# Butt-joint positions per course (fraction of the run), per face —
# staggered so no two neighbouring courses share a joint line.
JOINTS = {
    "F": (0.34, 0.68, 0.47, 0.76),
    "B": (0.62, 0.30, 0.72, 0.42),
    "L": (0.55, 0.35, 0.65, 0.28),
    "R": (0.40, 0.70, 0.32, 0.58),
    "T": (0.44, 0.72, 0.33, 0.62),
    "D": (0.66, 0.38, 0.58, 0.30),
}


def _course_boards(run_lo, run_hi, joint_frac):
    j = run_lo + (run_hi - run_lo) * joint_frac
    return ((run_lo + GAP / 2, j - GAP / 2), (j + GAP / 2, run_hi - GAP / 2))


def build(loc=(-4.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    il.materials()
    root = il.root_empty(PFX + "Root", loc)
    ox_a, ox_b = il.oak_grain('X')
    oy_a, oy_b = il.oak_grain('Y')
    core = il.materials()["wood_dark"]

    pitch = 2 * R_IN / 4
    h = pitch - GAP

    # Dark core shows through the seams as shadow lines.
    il.box(f"{PFX}Core", (0, 0, 0), (0.94, 0.94, 0.94), [core], parent=root)

    for k in range(4):
        c = -R_IN + pitch * (k + 0.5)     # course centre (z or y)
        for face, salt in (("F", 0), ("B", 1), ("L", 2),
                           ("R", 3), ("T", 0), ("D", 1)):
            run = (-0.5, 0.5) if face in "FBTD" else (-R_IN, R_IN)
            for i, (b0, b1) in enumerate(
                    _course_boards(*run, JOINTS[face][k])):
                length = b1 - b0
                mid = (b0 + b1) / 2
                a_tone = (k + i + salt) % 2 == 0
                if face in ("F", "B"):
                    tone = ox_a if a_tone else ox_b
                    center = (mid, FACE * (-1 if face == "F" else 1), c)
                    size = (length, DEPTH, h)
                elif face in ("L", "R"):
                    tone = oy_a if a_tone else oy_b
                    center = (FACE * (-1 if face == "L" else 1), mid, c)
                    size = (DEPTH, length, h)
                else:
                    tone = ox_a if a_tone else ox_b
                    center = (mid, c, FACE * (1 if face == "T" else -1))
                    size = (length, h, DEPTH)
                il.box(f"{PFX}{face}{k}_{i}", center, size, [tone],
                       parent=root)
    return root
