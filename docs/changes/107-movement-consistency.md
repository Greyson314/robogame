# 107 — Movement consistency: CoM/CoL/CoT overlays + hover mass scaling

> Status: **Shipped (code).** No asset bake needed. A `/ideate` movement round
> (focus: movement physics, balance, consistency). Two features shipped; a third
> approved idea was found already implemented (see below).

## Idea 1 — CoM / CoL / CoT garage overlays

`CenterOverlay` (new, `Gameplay`): a build-mode visualisation of three
translucent world-space spheres over the chassis — **Center of Mass** (white,
the live `rb.worldCenterOfMass`), **Center of Lift** (blue, area-weighted aero
blocks), **Center of Thrust** (orange, thrust-weighted thrusters / hover blades
/ rotors). The *spatial mismatch* between them is the signal: a CoL behind CoM
pitches up, a CoT off the roll axis corkscrews. This makes the session-106 wing
physics legible — "why does my plane fight me?" becomes a glance.

Read-only: no Tweakable, no gameplay mutation. CoM is the aggregate Robot
already computes; CoL/CoT are one weighted pass over the block grid. The spheres
are collider-less children of a standalone gizmo root (invariant #4), built with
`RuntimeMaterials.UnlitTransparent`, and only exist in the garage. Toggle **G**;
a small legend + state shows mid-left while building. Wired by `GarageController`
beside the other build tools.

## Idea 2 — hover blade mass + inertia scaling

Closes the consistency gap session 106 left open: hover blades now run on the
same physics contract as aero blocks. `Robot.EffectiveMass` scales a hover
blade's mass with its N×N footprint (anchored so the default size 2 ==
`Definition.Mass` — `HoverTank` preset unchanged), and `BlockInertiaBounds`
routes hover blades through their real swept bounds so footprint feeds the
box-inertia path. Lift already scaled with N², so matching the mass keeps
lift-per-mass honest: a size-4 pad lifts 4× but weighs 4×, so giant hover pads
are no longer free, and one big central pad rocks more than four symmetric small
ones. New PlayMode test: size-4 blade = 4× a size-2's mass.

## Idea 3 — thrust-offset torque (found already implemented)

Approved, then discovered the mechanic already exists: `ThrusterBlock.Tick`
already applies `AddForceAtPosition(transform.forward * thrust,
transform.position)` — and so do aero (`AeroSurfaceBlock`), hover
(`HoverBladeBlock`), rudder, and wheel forces. Off-CoM thrust already induces
real pitch/yaw torque; the briefing premise ("thrusters push through CoM") was
wrong. No code change made (fail-loud over fabricated work). The net-new value
this round is that idea 1's **CoT overlay now visualises** that thrust offset.
The only remaining lever — making the effect more *felt* by trimming the
auto-leveling/damping assists — is a risky feel change surfaced to the user, not
done autonomously.

## Files

New: `Gameplay/CenterOverlay.cs`. Edited: `Gameplay/GarageController.cs` (wire
overlay), `Robot/Robot.cs` (`EffectiveMass` + `BlockInertiaBounds` extend to
hover), `Tests/PlayMode/Movement/WingInertiaTests.cs` (hover test).

## Verification

EditMode 271/272, PlayMode 111/112 — **0 failures** (the 1+1 are the documented
`MinimalArena` skips). Unity compiles clean. The overlay is compile-verified;
its in-garage visuals are unverified in-editor (needs a playtest glance).

## Invariant compliance

- **#1** overlay is read-only; hover mass is code-config, no Tweakable.
- **#4** overlay spheres are collider-less, off the chassis compound body.
- **#6** `RecalculateAggregates` is build/damage-time; overlay update is a
  bounded per-frame pass, allocation-free.
