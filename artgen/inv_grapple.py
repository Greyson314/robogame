# artgen/inv_grapple.py — inventor study: grapple-magnet launcher.
# A deck winch aimed forward: walnut A-posts holding a hemp-wound
# drum, an oak launching trough, and the cartoon horseshoe magnet
# loaded at the mouth, poles forward. Crank on the right. The rope
# mechanic is diegetic — the drum IS the ammo pool.

from math import radians

import paperlib as pl
import inventorlib as il
from inv_tips import horseshoe

PFX = "InvGrap_"


def build(loc=(9.8, -8.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    pl.card_panel(f"{PFX}GearBottom", pl.ngon_pts(12, 0.29), 0.03, 'Z',
                  0.015, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(10, 0.26, 0.32),
                  0.035, 'Z', 0.0475, [m["wood"], m["wood_dark"]],
                  parent=root)
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.24), 0.03, 'Z',
                  0.08, [m["wood"], m["wood_dark"]], parent=root)

    # A-posts + brass pivot brads + wound drum.
    post = [(-0.17, 0.095), (0.17, 0.095), (0.055, 0.40), (-0.055, 0.40)]
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}Post{side}", post, 0.036, 'X', sign * 0.14,
                      [m["wood_dark"]], cap_slots=(0, 0), edge_slot=0,
                      parent=root)
        pl.brad(f"{PFX}Brad{side}", sign * 0.158, sign * 0.185, 0.04,
                0.36, m["brass"], parent=root)
    il.lathe(f"{PFX}Drum",
             [(0.115, -0.115), (0.075, -0.09), (0.075, 0.09),
              (0.115, 0.115)],
             [m["wood_dark"]], segs=12, axis='X',
             center=(0, 0, 0.36), parent=root)
    for k, x in enumerate((-0.062, -0.021, 0.021, 0.062)):
        il.torus(f"{PFX}Wind{k}", 0.098, 0.024, [m["cord"]],
                 center=(x, 0, 0.36), axis='X', segs=20, sides=6,
                 parent=root)

    # Launch trough: open-top oak channel running forward.
    pl.arc_shell(f"{PFX}Trough", 0.125, 0.022, -0.08, -0.60, 195, 345,
                 8, [m["wood"], m["wood_dark"], m["wood"]],
                 center=(0.0, 0.30), parent=root)

    # The magnet, seated at the mouth, poles forward.
    aim = il.root_empty(PFX + "Aim", (0, 0, 0))
    aim.parent = root
    aim.location = (0, -0.56, 0.40)
    aim.rotation_euler = (radians(-90), 0, 0)
    horseshoe(PFX + "Mag_", aim, m, s=0.62)
    # Hemp line from drum to the magnet's eye.
    il.sweep(f"{PFX}Line",
             [(0, -0.02, 0.475), (0, -0.22, 0.475), (0, -0.44, 0.46),
              (0, -0.56, 0.425)],
             0.014, [m["cord"]], sides=5, parent=root)

    # Crank, right side.
    il.rod(f"{PFX}CrankAxle", (0.16, 0, 0.36), (0.27, 0, 0.36), 0.016,
           [m["brass"]], parent=root)
    il.rod(f"{PFX}CrankArm", (0.265, 0, 0.34), (0.265, 0, 0.50), 0.021,
           [m["wood_dark"]], parent=root)
    il.rod(f"{PFX}CrankGrip", (0.26, 0, 0.485), (0.36, 0, 0.485),
           0.017, [m["brass"]], parent=root)
    return root
