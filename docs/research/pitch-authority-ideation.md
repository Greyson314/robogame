# Pitch-authority ideation — killing the nose-thruster meta (session 172)

> Tier-3 research note. Ideation only — nothing here is decided. Context:
> user wants the realistic-looking plane to also be the mechanically
> sound one, unlike Robocraft's sky-pointing nose thrusters.

## Root cause (design-pilot diagnosis)

Robocraft's wings had no control mapping — ALL maneuvering ran through
thrusters, whose torque is speed-independent and scales with lever arm.
One knob, one obvious answer: extremity-mounted perpendicular thrusters.

Robogame already fixed the structural half (ADR-0009): control authority
is foil-geometry-derived, canards work automatically (stock Plane has
them), tail moment arm already rewards long fuselages. The residual
problem is narrower: **aero authority dies at low speed (∝ v²), and the
only speed-independent pitch source we offer is a raw thruster with no
legitimate-looking form factor.**

## Idea sheet (by channel)

- **Vectoring thruster** — tilt nozzle under pitch input, reusing
  `AeroControl.Deflection` on `ThrusterBlock.transform.forward`.
  Self-limiting (cos-loss on forward thrust). Biggest silhouette win.
- **RCS port mesh variant** — flush puffer-jet skin for a tiny thruster.
  Art-only; legitimizes the instinct rather than redirecting it.
- **Commanded gyro pitch/roll** — extend `GyroBlock` (today: yaw command
  + damping only). No artificial cap needed: measured aero damping
  (~16 rad/s² per rad/s at 50 m/s, session 166) naturally overtakes a
  modest gyro at speed. ⚠ Space Engineers' buried-gyro-brick meta is the
  cautionary tale — CPU cost curve (rotor-RPM-style quadratic) is
  load-bearing, not polish.
- **Legibility** — canard HUD hint when a foil lands ahead of the CoM
  sphere; hinged-flap visual split instead of whole-plank twitch.
- **Deferred**: CPU-pricing thrusters by lever-arm off-axis-ness (risks
  punishing legit VTOLs); ballast trim block; fly-by-wire blend.

Shortlisted order: vectoring thruster + RCS skin → legibility pass →
gyro command (only with the cost tax attached).

## The wing slider question (open)

User idea: a per-foil "pitch power" slider whose downside is PHYSICAL,
not CPU cost — set it to ~50% to balance pitch power vs X. Candidate
X's, ranked:

1. **Lift ↔ control split (flap fraction)** — but note: an all-moving
   slab still lifts with body AoA (user caught this). Two honest
   re-framings:
   - **A (camber allocation)**: fixed fraction carries baked incidence =
     free cruise lift; movable fraction is symmetric — lifts only via
     body AoA. 100% control = aerobatic symmetric wing: must hold
     nose-up trim to cruise (drag cost), flies identically inverted
     (emergent style perk). Maps to the existing geometric-pitch term —
     tuning-layer, not a new system.
   - **B (damping trade)**: fixed fraction buys weathervane damping;
     100% = full lift retained but no passive stability — drifty,
     overshooting, hands-on. Deeper skill knob, worse first-contact UX.
2. **Stall margin** — throw beyond `_stallAoA` (~20°) self-defeats;
   natural cap, mostly existing code (`_postStallLift` cliff tuning).
3. **Energy bleed** — deflection drag per maneuver; rate-vs-energy
   fighter identity.
4. **Flutter at high q** — real, great VFX, most new-system-shaped.

Current lean (not decided): framing **A** as the slider's meaning, with
a milder dose of B's damping loss on the same slider; stall (#2) stays
underneath as the free natural cap. Next step would be a planner pass
against `AeroSurfaceBlock`/`FoilDefaults` to spec the scalars.
