# Session 22 — Grapple hook: scaled-up tips, dumbbell target, joint-based latch

> Status: **shipped, awaiting in-game verification.** Three phases all
> landed:
> A — visual + collider scale-up (hook J-shape ~2 m tall; mace 1 m
> diameter; barbell → dumbbell shape, 7 m long).
> B — physics grapple: hook-tip `ConfigurableJoint` attach on contact,
> with break force, re-attach cooldown, null-body guard.
> C — wrap-around feasibility analysis. Path C (Verlet rope migration)
> is the right long-term answer; Phase B's tip-stick ships today as
> the proving-ground baseline.

## Intent

The user wants the Hook tip block to actually grab other bots via
physics. The previous hook was too small (~0.55 m) to wrap around a
chassis-scale target, the barbell test dummy turned out to be
better-named "dumbbell," and the mace also needed scaling up. Phase A
addresses the sizing; Phase B adds joint-based grapple attach; Phase C
documents the feasibility of true wrap-around vs the simpler
tip-attach approach actually shipped.

## Phase A — Sizing (this commit)

### `HookBlock` redesigned

The hook is now a J-shape sized to a chassis cell:

- **Shaft** (vertical, going down the rope): 0.45 × 0.40 × 1.70 m,
  spans segment-local Z ∈ [0, 1.70]. The rope attaches at the top.
- **Barb arm** (horizontal, extending forward at the shaft's bottom):
  0.45 × 1.70 × 0.40 m, spans Y ∈ [0, 1.70] at Z ∈ [1.70, 2.10].
- **Barb tip** (vertical, going back up at the end of the arm):
  0.45 × 0.40 × 1.50 m, spans Z ∈ [0.20, 1.70] at Y ∈ [1.70, 2.10].

The trap zone (the open volume inside the J) is roughly 1.5 m × 1.5 m,
big enough to fit a 1 m chassis cell.

Compound collider: three matching `BoxCollider`s on the host GameObject
— one per visual cube. Together they approximate the J's hit volume so
contact resolves correctly against the J's silhouette (not against a
single bounding box that would catch from any direction).

`BlockDef_Hook.mass` bumped 0.5 → 1.5 kg, max HP 60 → 120 to match.

### `MaceBlock` redesigned

- **Ball**: 1.0 m diameter (was 0.55).
- **Spikes**: 0.20 × 0.20 × 0.55 m each (was 0.10 × 0.10 × 0.30).
- **Sphere collider** radius 0.65 m on the host (was 0.40).

`BlockDef_Mace.mass` bumped 2.0 → 5.0 kg, max HP 90 → 180. The
~3.3× hook-to-mace mass ratio is preserved, so the kinetic-energy
differential between the two tip types is unchanged.

### Barbell → Dumbbell

The previous "barbell" preset was 13 cells long (3×3×3 ends + 9-cell
rod). The user clarified they meant a dumbbell — short handle between
chunky end weights.

New `Blueprint_DumbbellDummy.asset` shape:

- **End weight A**: 3×3×3 cube cluster at z ∈ [-3, -1].
- **Handle**: single CPU cell at (0, 0, 0) — exactly 1 cell wide, the
  hook's natural grip target.
- **End weight B**: 3×3×3 cube cluster at z ∈ [1, 3].

Total 55 cells, 7 m long along Z (vs 13 m for the old barbell).

### Renames

- `Blueprint_BarbellDummy.asset` → `Blueprint_DumbbellDummy.asset`
  (via `git mv` so the asset GUID is preserved).
- `BarbellDummyTests.cs` → `DumbbellDummyTests.cs` (rewritten for the
  new shape: tests now assert the 1-cell handle and 27-cell end
  weights instead of the 9-cell rod).
- `GameplayScaffolder.BuildBarbellDummyEntries` → `BuildDumbbellDummyEntries`,
  `BarbellDummyPath` → `DumbbellDummyPath`, "Barbell Dummy" display
  name → "Dumbbell Dummy".
- `ArenaController._barbell{Blueprint,Position,Name}` →
  `_dumbbell{Blueprint,Position,Name}`. `[FormerlySerializedAs]`
  attributes preserve the existing scene wire-up — Arena.unity's
  serialized `_barbellBlueprint` value carries over to
  `_dumbbellBlueprint` automatically. No Build Everything required
  for the field rename.
- `PresetBlueprintTests.PresetPaths` updated to point at the new
  asset path.

### `TipBlock.IgnoreChassisColliders` multi-collider fix

The old `GetComponent<Collider>()` only returned the first collider
on the host. With the hook's new compound collider (3 BoxColliders),
the other two would have collided with the chassis as the rope
swung. Switched to `GetComponents<Collider>()` and pair every host
collider against every chassis collider. Same pattern, scaled.

## Phase B — Tip-attach grapple physics

The hook now creates a `ConfigurableJoint` between the rope's last
segment Rigidbody and the contacted target Rigidbody on first
collision. Locked linear motion + free angular motion + tunable
break force / break torque give "the hook bites and holds, target
spins freely under the bite" behaviour.

### `TipBlock.HandleCollision` widened to `protected internal virtual`

So `HookBlock` can override and call `base.HandleCollision(collision)`
to keep the kinetic-energy damage path intact. The
`TipCollisionForwarder` dispatch is unchanged — it calls through the
base type, which resolves to the override at runtime.

### `HookBlock` grapple state + lifecycle

New private fields:

- `_grappleJoint` — the `ConfigurableJoint`. Null while not grappled.
- `_grappleTarget` — the contacted Rigidbody. Tracked separately
  from `_grappleJoint.connectedBody` so the FixedUpdate guard can
  tell apart "joint broken" from "target destroyed."
- `_releaseTime` — `Time.time` of the last release. Gates the
  re-attach cooldown.

New serialized inspector fields (NOT Tweakables — these affect
gameplay outcomes per `PHYSICS_PLAN §1.5` and migrate to per-block
blueprint config when `PHYSICS_PLAN §6` lands):

- `_grappleBreakForce` (default 1200 N).
- `_grappleBreakTorque` (default 800 N·m).
- `_reattachCooldown` (default 0.5 s).

New public surface:

- `IsGrappled` — true while `_grappleJoint != null`.
- `GrappleTarget` — the contacted Rigidbody, or null.
- `Release()` — destroy the joint cleanly + arm the cooldown.

Override path:

- `AttachToHost(host, ownerChassis)` — calls base then resets
  `_releaseTime` so a fresh adoption can grapple immediately.
- `DetachFromHost()` — calls `Release()` first so the joint doesn't
  outlive the destroyed segment, then base.
- `HandleCollision(collision)` — calls base (damage path runs
  unconditionally), then attempts a grapple attach if not already
  grappled, not on cooldown, and the contact is with an external
  Rigidbody (not the owner chassis).
- `FixedUpdate()` — only does work while grappled. Catches PhysX
  break (joint component already destroyed → ref null) and target
  destruction (`connectedBody` null on a still-alive joint) and
  calls `Release()` cleanly.

### Why no `OnJointBreak`

PhysX fires `OnJointBreak` on the GameObject that owns the joint —
which is the host segment, not the `HookBlock` (`HookBlock` is a
child). Wiring it would require a helper component on the segment,
and the FixedUpdate null-poll is one frame late at worst. Per the
planner's recommendation, skipped.

### Tests

`Tests/PlayMode/Movement/HookGrappleTests.cs`:

- `IsGrappled_WhenNeverAttached_IsFalse`.
- `Release_WhenNotGrappled_DoesNotThrowAndArmsCooldown`.
- `FixedUpdate_WhenTargetDestroyedMidGrapple_ReleasesCleanly` — the
  load-bearing test. Manually inserts a joint + target via
  reflection, destroys the target, waits two fixed updates,
  asserts `IsGrappled` flips false without any nullref.

(The full collision-driver test for "first contact creates the joint"
is left as a smoke test for now — driving a real PhysX contact in
isolation requires a `TipCollisionForwarder` setup that the test
scaffold doesn't have. The Attach path is short and exercised by the
in-game flight test.)

## Phase C — Wrap-around feasibility analysis

The user described the desired behaviour as: "if you pass a hook
attached to a rope under a thing that looks like a barbell, it should
wrap around that object and latch in some way." That's true geometric
wrap, not just a tip-stick.

Phase B ships tip-stick (the hook latches at the contact point, no
rope wrap). This section captures why we're doing tip-stick first and
what the path to true wrap-around looks like.

### Why true wrap-around is hard with PhysX

Per `docs/PHYSICS_PLAN.md` §2, the rope's middle segments don't have
colliders by default. The chain is built from `ConfigurableJoint`-
linked Rigidbodies, and per-segment collision causes the chain to
"explode" under contact: joints try to pull a contact-stuck segment
back, contact resolution fights the pull, angular limits get violated,
the chain visibly snaps to a mangled pose for one frame and then
settles. This is documented and avoided.

Without per-segment collision, the rope cannot physically drape
around a target. It either passes through everything or only the tip
contacts.

### Three approximation paths

#### A. Tip-stick (Phase B, shipping today)

Hook's tip latches onto the contact point. The rope is a straight
chain from chassis to tip; pulling the chassis pulls the target via
the joint. **Pros:** simple, stable, ships now. **Cons:** no visual
wrap; the player has to throw the hook so it directly contacts the
target rather than passing under and around.

#### B. Geometric via-points

After tip-stick attach, raycast each FixedUpdate from chassis →
tip. If the ray intersects another collider, insert a "via-point"
at the intersection and route the rope through it. Multiple
via-points dynamically added/removed as the chassis moves. The rope
chain visually routes around obstacles.

**Pros:** approximates wrap-around without per-segment physics.
Used by Worms, several platformers. **Cons:** the rope's PHYSICS is
still chassis → tip joint; the via-points are visual only. Tug-of-
war pulls don't really feel like the rope is "snagged" on the via
geometry. Implementation is non-trivial — needs to decide when via-
points form, when they're removed (line-of-sight check), and how
they affect the rope's effective tension.

#### C. Verlet rope migration

Per `PHYSICS_PLAN §2`, the long-term rope tech is a custom Verlet /
PBD solver. Per-segment collision becomes a single capsule cast per
step. The rope can then truly drape over geometry, and the wrap-
around emerges naturally from the simulation.

**Pros:** the right answer architecturally, also fixes the rope-
explosion + networking-cost problems. **Cons:** Verlet rope is
~weekend of work for a polished implementation, and the existing
PhysX-joint code path is fragile but working. The PHYSICS_PLAN
migration triggers (1.5 ms PhysX simulate budget under the rotor
stress tower, or per-segment collision needed for a flail weapon, or
networking lands) all favour migration NOW for combat-relevant rope.

### Recommendation

**Ship Phase B (tip-stick) and let the user feel it.** If the player
finds tip-stick "unsatisfying because the hook should wrap," that's a
strong signal to move directly to path C (Verlet) rather than wedging
in path B (via-points), since via-points are extra work that becomes
obsolete the moment Verlet lands.

Path C effort estimate (per `PHYSICS_PLAN §2` "long afternoon to
short weekend depending on how nice we want the API to be"):

- Half day: bare Verlet solver + chain that visually drapes.
- Half day: capsule casts per segment for world collision.
- Half day: replace `RopeBlock`'s PhysX-joint chain with the Verlet
  chain, preserving the existing `RopeBlock` public API + the
  TipBlock adoption hook.
- Half day: damage routing through Verlet tip + integration tests.

Migration target: when the user explicitly asks for wrap-around, or
when networking lands and the per-segment replication cost becomes a
problem.
