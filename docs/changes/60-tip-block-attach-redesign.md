# 60 — Tip-block attach redesign (SpringJoint + impact-handler exemption)

> Status: **shipped, untested in-engine.** Two-bug fix to the long-running
> "the hook destroys itself / the magnet vaporises everything" failure
> mode. Replaces the Locked ConfigurableJoint with a SpringJoint, and
> exempts tip blocks from MomentumImpactHandler's fallback IDamageable
> path. New doc [TIP_BLOCK_ATTACH.md](../TIP_BLOCK_ATTACH.md) explains
> the architecture end-to-end.

## Why this session

User reported the magnet "does a ton of damage while destroying
itself, has been a persistent issue going all the way back to the
first implementation of hooks." They asked for a deep dive on best
practices for tip-block attach mechanics + a fix that lets a
plane + rope + magnet latch, drag, and hold rope length.

## What was actually happening

Three damage / force paths were firing simultaneously on every
magnet contact; two of them were unwanted:

1. **`TipBlock.HandleCollision`** — intended path. Damages the
   contacted target via KE formula. (Magnet had `DamagePerKj = 0.8`,
   hook had 0, mace has 2.0.)
2. **`MomentumImpactHandler` fallback** — *the bug.* Line 166-167 in
   the chassis-mounted handler walked the other collider for an
   `IDamageable` and damaged it directly whenever the other body
   had no MomentumImpactHandler of its own. The rope-tip body
   doesn't carry one (it's not a chassis), so this fallback hit the
   tip block's BlockBehaviour on every contact and ate its ~150 HP
   inside a couple of seconds.
3. **Magnet pull → high-velocity slam cascade.** `AddForce 1500 N`
   accelerated the target up the pull cone; target slammed into the
   magnet at high relative velocity; the chassis-side
   MomentumImpactHandler then applied a big splash on the enemy
   *and* the bug from #2 hit the magnet hard. Both sides got
   chewed.

On top of that the **hook had never worked right** because its
attach mechanism was a `ConfigurableJoint` with `Locked` linear
motion and `breakForce = 1200 N`:

- A locked constraint between two free bodies applies whatever
  *impulse* the solver needs to keep them coincident — impulses
  spike under acceleration, trip the break threshold, grapple
  snaps.
- The band-aids told the story:
  `GrappledChassisSpring 600` (softened from 8000),
  `GrappledTipMass 25 kg` (fattened from 0.5), reattach cooldown.
  All compensating for the impulse-spike problem, none of them
  removing it.

## The fix

### Part A — MomentumImpactHandler tip-block exemption

[`MomentumImpactHandler.cs`](../../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs)
gained a tip-block guard before the IDamageable fallback:

```csharp
if (otherComp.GetComponentInParent<TipBlock>() != null) return;
```

Tip blocks are part of the chassis's *offensive toolkit*, not
standalone damageable entities. Their damage contract is now:

| Source | Hits tip block? |
|---|---|
| Direct ranged fire (ProjectileWorld bullets / bombs / cannons) | yes — they hit any block in the grid normally |
| `TipBlock.HandleCollision` as the attacker | no (self-damage already suppressed) |
| `MomentumImpactHandler` fallback (the bug) | **no** (session-60 fix) |
| Enemy's own MomentumImpactHandler self-splash | n/a — that hits the enemy grid, not us |

Added `Robogame.Movement` to Combat's `using` list. Combat already
references Movement in its asmdef, so no asmdef edit needed.

### Part B — SpringJoint replaces Locked ConfigurableJoint

[`HookBlock.cs`](../../Assets/_Project/Scripts/Movement/HookBlock.cs)
rewritten. Field `_grappleJoint` is now `SpringJoint`. `Attach`:

- `joint.minDistance = 0; joint.maxDistance = 0;`
- `joint.spring = 300; joint.damper = 80;` (inspector-tunable)
- `joint.breakForce = Mathf.Infinity; joint.breakTorque = Mathf.Infinity;`
- No motion locking; the spring pulls together, rope leash holds
  total length.

Removed: `_grappleBreakForce`, `_grappleBreakTorque` (inspector
fields), `GrappledChassisSpring`, `GrappledChassisDamper`,
`GrappledTipMass`, `_origChassisSpring`, `_origChassisSpringSaved`,
`_origTipMass`, `_origTipMassSaved`. All band-aids for the
impulse-spike problem the SpringJoint avoids by construction.

`Release` simplified — destroy joint, restore kinematic, clear
target, stamp release time. No spring/mass restoration needed.

`FixedUpdate` simplified — auto-release on `connectedBody == null`
(target chassis was destroyed). No breakForce trip detection path
because there's no break threshold.

### Part C — MagnetBlock now latches

[`MagnetBlock.cs`](../../Assets/_Project/Scripts/Movement/MagnetBlock.cs)
fully rewritten:

- **`DamagePerKj = 0`** (was 0.8). The magnet's job is *control*,
  not *kill*; damage emerges from the chassis-drag dynamics.
- **`_pullForce = 600 N`** (was 1500). The pull is now a *guide*
  field, not a catapult.
- **`HandleCollision` override** creates a SpringJoint on contact
  with the same params as Hook (tuned slightly higher: spring 320,
  damper 110 — the pull field has already given the target some
  approach energy, more damping bleeds it off).
- **`ReleaseTether()`** public API mirrors `HookBlock.Release()`.
- **`OnDestroy()`** + `DetachFromHost()` both release cleanly so a
  destroyed magnet leaves no ghost joint pulling the chassis.

### Part D — Deep-dive doc

New [`docs/TIP_BLOCK_ATTACH.md`](../TIP_BLOCK_ATTACH.md) covers:
- The three concurrent constraints (Verlet chain, chassis↔tip
  leash, tip↔target spring) and what each one's responsible for.
- Why Locked ConfigurableJoint failed (impulse-spike → break loop).
- Why the SpringJoint works (bounded force = spring × distance).
- Damage contract per tip-block.
- Tuning surface and tuning advice.
- What we explicitly do NOT do (and why).

Added to [`CLAUDE.md`](../../CLAUDE.md) reading list so future
sessions land on it before touching tip-block code.

## Files

- **New:**
  - `docs/TIP_BLOCK_ATTACH.md`
- **Edited:**
  - `Scripts/Combat/MomentumImpactHandler.cs` — tip-block fallback exemption.
  - `Scripts/Movement/HookBlock.cs` — SpringJoint replacement, band-aid removal.
  - `Scripts/Movement/MagnetBlock.cs` — latch-on-contact, damage suppression, pull retune.
  - `CLAUDE.md` — link to new tip-block doc.

## Hard-invariant check

- **No Tweakable affects gameplay.** Spring constants, pull force,
  cooldown all live on SerializeField inspector fields on the tip
  block. PHYSICS_PLAN § 1.5: clean.
- **Server-authoritative shape.** SpringJoint runs in PhysX on the
  server; clients render. Same shape as the old ConfigurableJoint
  contract — drop-in for the netcode replay path.
- **Single Rigidbody per chassis.** Unchanged. The tip body remains
  scene-root, owned by RopeBlock; the SpringJoint connects two
  pre-existing Rigidbodies.
- **No per-frame allocations.** Magnet's pull loop still uses the
  static `Collider[32]` scratch buffer. HookBlock's FixedUpdate
  early-outs when no joint exists.

## Known follow-ups

- **Manual release input.** No R-key release for Hook/Magnet yet.
  Auto-release on target death works; deliberate-release for "let
  go to swing on the next pass" doesn't. Plumb via `IInputSource`
  (`ReloadPressed` is already there for ammo; new `ReleaseTether`
  channel would mirror it).
- **Tip-block ranged-fire damage path.** With the
  MomentumImpactHandler exemption, the *only* way a tip block dies
  is direct ranged fire on its grid cell. That's correct, but the
  block is small (a 1-cell magnet) and easy to miss. Either OK
  (tip blocks are durable on purpose) or a tuning lever for later.
- **Hook latch point.** Hook anchors at the contact world point.
  Magnet anchors there too. Could anchor at the *target's centre of
  mass* instead so the target hangs more like a fish on a line vs
  the exact contact spot. Defer until playtest shows the contact
  anchor reading wrong.
- **Multi-target hold.** Each tip block latches one target at a
  time. A swung magnet could in principle latch the first thing it
  touches and ignore the rest. That's already what the code does;
  leaving it because "magnet sticks to one thing" reads sensibly.
- **Bot use of magnet.** Bots have no path to author a magnet
  chassis today. Once they do, give them a release policy (release
  when target HP < X, release when chassis health < Y, etc).

## Verification

1. **Magnet contact damage.** Hold a chassis with rope + magnet
   stationary, swing the magnet manually into an enemy chassis.
   Enemy block count does NOT drop dramatically. Magnet's own
   block count stays at 1. *Pre-fix: both sides chewed each other.*
2. **Magnet latch.** Swing the magnet near an enemy. Pull field
   draws them in. Contact → spring joint visible (target follows
   the magnet's swing). Manual `MagnetBlock.ReleaseTether()` call
   (or wait for target death) cleanly detaches.
3. **Plane drag.** Build the user's example chassis: plane + rope +
   magnet. Swing the magnet into a ground chassis. Plane flies off
   — target trails behind the plane on the rope length, swinging
   like a pendulum. Rope does not snap. Magnet does not detach.
4. **Hook latch.** Same as legacy hook: J-shape catches a bar; the
   target is pulled to the contact point and held. No more snap
   under load.
5. **Chassis impact damage on enemy still works.** Ram an enemy
   tank with your own chassis (no rope). MomentumImpactHandler
   still applies splash damage to both sides as before — the tip-
   block exemption only affects the IDamageable fallback path,
   chassis-vs-chassis is unchanged.
