# artgen/inv_thruster.py — inventor study: thruster.
# A bellows blower: linen accordion pleats between two walnut boards,
# caged by four spruce staves, blowing through a turned wooden nozzle
# with a brass muzzle ring and a vermilion bore (thrust channel keeps the
# red = "where the push comes out" rule). One lovable oversized detail:
# a little hand crank on the side, as if the capybara pumps it.
# Nozzle points -Y (exhaust aft), matching weapon forward convention.

import paperlib as pl
import inventorlib as il

PFX = "InvThr_"


def build(loc=(2.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Bellows: zig-zag lathe profile along Y. Fewer, deeper pleats — the
    # accordion must read as squeezable at gameplay distance.
    prof = [(0.25, -0.14)]
    y = -0.14
    for i in range(8):
        y += 0.061
        prof.append((0.345 if i % 2 == 0 else 0.20, y))
    prof.append((0.25, y + 0.055))
    il.lathe(f"{PFX}Bellows", prof, [m["linen"]], segs=20, axis='Y',
             parent=root)

    # Boards fore and aft of the accordion.
    il.lathe(f"{PFX}BoardAft", [(0.34, -0.19), (0.34, -0.13)],
             [m["wood_dark"]], segs=20, axis='Y', parent=root)
    il.lathe(f"{PFX}BoardFore", [(0.34, 0.35), (0.34, 0.42)],
             [m["wood_dark"]], segs=20, axis='Y', parent=root)

    # Intake on the fore board: brass ring around an ink-dark hole.
    il.torus(f"{PFX}IntakeRing", 0.16, 0.024, [m["brass"]],
             center=(0, 0.425, 0), axis='Y', segs=20, sides=7, parent=root)
    il.lathe(f"{PFX}IntakeHole", [(0.135, 0.421), (0.135, 0.431)],
             [m["ink"]], segs=16, axis='Y', parent=root)

    # Four spruce cage staves holding the accordion straight.
    for i, (sx, sz) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
        x, z = sx * 0.255, sz * 0.255
        il.rod(f"{PFX}Stave{i}", (x, -0.16, z), (x, 0.38, z), 0.021,
               [m["wood"]], parent=root)

    # Turned nozzle cone out the aft board, brass ring, red bore.
    il.lathe(f"{PFX}Nozzle",
             [(0.30, -0.19), (0.28, -0.24), (0.19, -0.36),
              (0.125, -0.46), (0.13, -0.50)],
             [m["wood"]], segs=18, axis='Y', parent=root)
    il.torus(f"{PFX}NozzleRing", 0.135, 0.018, [m["brass"]],
             center=(0, -0.485, 0), axis='Y', segs=16, sides=7, parent=root)
    il.lathe(f"{PFX}Bore", [(0.102, -0.513), (0.102, -0.498)],
             [m["channel"]], segs=14, axis='Y', parent=root)

    # The crank: brass axle out the +X side, walnut arm, brass handle.
    il.rod(f"{PFX}CrankAxle", (0.24, 0.10, 0.0), (0.42, 0.10, 0.0),
           0.018, [m["brass"]], parent=root)
    il.rod(f"{PFX}CrankArm", (0.41, 0.10, -0.02), (0.41, 0.10, 0.15),
           0.024, [m["wood_dark"]], parent=root)
    il.rod(f"{PFX}CrankGrip", (0.40, 0.10, 0.13), (0.52, 0.10, 0.13),
           0.020, [m["brass"]], parent=root)
    return root
