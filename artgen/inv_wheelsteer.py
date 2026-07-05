# artgen/inv_wheelsteer.py — inventor study: steerable wheel.
# The cartwheel in a walnut caster fork, topped with a mini laminated
# steering gear + brass kingpin — the "rotating things stand on gears"
# rule applied to steering. Wheel is the shared make_wheel at 0.72x.

import paperlib as pl
import inventorlib as il
from inv_wheel import make_wheel

PFX = "InvWSteer_"


def build(loc=(-10.0, -8.0, 0.45)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Mini steering gear + brass kingpin at the top of the fork.
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.17), 0.03, 'Z',
                  0.50, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(12, 0.155, 0.20),
                  0.035, 'Z', 0.465, [m["wood"], m["wood_dark"]],
                  parent=root)
    il.lathe(f"{PFX}Kingpin",
             [(0.030, 0.515), (0.042, 0.545), (0.030, 0.575),
              (0.005, 0.595)],
             [m["brass"]], segs=10, parent=root)

    # Fork crown + two walnut prongs straddling the wheel.
    il.box(f"{PFX}Crown", (0, 0, 0.415), (0.30, 0.24, 0.07),
           [m["wood_dark"]], parent=root)
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}Prong{side}",
                      [(-0.115, 0.40), (0.115, 0.40), (0.075, 0.02),
                       (-0.075, 0.02)],
                      0.032, 'X', sign * 0.125, [m["wood_dark"]],
                      cap_slots=(0, 0), edge_slot=0, parent=root)
        pl.brad(f"{PFX}AxleCap{side}", sign * 0.141, sign * 0.175,
                0.036, 0.05, m["brass"], parent=root)

    # The wheel itself, swinging in the fork.
    hub = il.root_empty(PFX + "Hub", (0, 0, 0))
    hub.parent = root
    hub.location = (0, 0, 0.05)
    make_wheel(PFX + "W_", hub, m, s=0.72, axle=False)
    return root
