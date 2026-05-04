# Session 19 — Blueprint authoring cleanup, rotor stem fix, bigger helicopter, hook/mace, barbell

> Status: **in progress.** Multi-phase autonomous run while the user is
> away. Each phase commits at its natural boundary. Pushes are left for
> the user.

## Multi-phase intent

The user asked for a five-part run, in dependency order:

1. A cleaning pass on the systems that produce a fresh chassis blueprint,
   so future "make me a new bot" requests are cheap and high-confidence.
   Plus self-testing — automated checks I can run from outside the
   editor that verify a blueprint will look like what I intended.
2. Fix the rotor visual so the foils appear at the **top** of the
   helicopter, not at the stem. The user reported the foils currently
   sit at the rotor's mast level (overlapping the CPU/pilot light),
   which makes it hard to tell whether session 17's B2/B3 (frame spin
   instability) is a real physics bug or just the blades clipping the
   tail rudder.
3. Make the default helicopter "fairly significantly larger," with the
   rotor blades as the absolute topmost blocks. Two guns (the user
   hasn't tried that loadout before).
4. Add the ability for blocks to be attached to the ends of ropes —
   critical going forward. Hooks and maces are the two starting tip
   types; both deal contact damage via the kinetic-energy formula in
   `docs/PHYSICS_PLAN.md` §3.
5. Spawn a "barbell"-shaped test dummy in the default arena: two big
   masses connected by a rod. Hookable, smackable.

The user explicitly noted bypass permissions are on for this run and
asked me to be careful with destructive commands. No `git push`, no
`git reset`, no force operations. Commits are allowed and expected.

## Phase 1 — Blueprint authoring cleanup (this commit)

Three new files in `Assets/_Project/Scripts/Block/`:

- **`BlueprintBuilder.cs`** — fluent API for assembling
  `ChassisBlueprint.Entry` lists. Replaces `entries.Add(new
  ChassisBlueprint.Entry(...))` boilerplate with chained calls:
  `Block`, `Row`, `Box`, `MirrorX/Z`, `RotorWithFoils`, `RotorBare`.
  Outputs an immutable `BlueprintPlan` struct that the editor
  scaffolder writes into a real ScriptableObject, or that runtime
  code materialises via `BlueprintPlan.ToBlueprint()`.

- **`BlueprintValidator.cs`** — pure-data checks on a `BlueprintPlan`:
  CPU presence, no duplicate cells, all cells reachable from the CPU
  via face-adjacency, optional unknown-block-id check when a
  `BlockDefinitionLibrary` is supplied. Errors gate validity;
  warnings (e.g. multiple CPUs) don't.

- **`BlueprintAsciiDump.cs`** — text representation of a blueprint,
  one Y-layer at a time. Lets reviewers see the shape of a chassis
  without opening Unity.

`GameplayScaffolder.CreateOrUpdateBlueprint` now calls the validator
at scaffold time and logs warnings or errors to the Console when a
preset is malformed.

### Self-testing

Three test files in `Assets/_Project/Tests/EditMode/Blueprints/`:

- **`BlueprintBuilderTests.cs`** — unit tests for the builder API.
  Covers `Block`, `Row`, `Box`, `MirrorX` (including mount-up flip
  on negative-X side), `RotorWithFoils` (default +Y axis and
  horizontal +X axis variant), `BuildValidated` throwing on no-CPU
  blueprints.
- **`BlueprintValidatorTests.cs`** — covers each error / warning
  branch: empty entries, no CPU, duplicate cells, orphaned cells,
  multi-CPU warning, RotorWithFoils connectivity passing.
- **`PresetBlueprintTests.cs`** — loads every shipped preset asset
  off disk via AssetDatabase and runs the validator on it. Also
  writes a markdown snapshot to `docs/blueprint-snapshots/presets.md`
  with ASCII layouts per preset, so the human reviewer (and a future
  AI session) can read the chassis shapes without opening Unity.

### Why the existing presets aren't refactored to use the builder

Refactoring all eight existing scaffolder methods (`BuildGroundEntries`,
`BuildPlaneEntries`, etc.) to use the new builder is touching hot,
working code for purely cosmetic reasons. The builder is additive: new
blueprints (the bigger helicopter, the barbell dummy) use it; old ones
keep their `entries.Add(...)` style. The validator catches malformed
blueprints regardless of which authoring style was used.

## Phase 3 — Rotor stem visual + adoption shift to mechanism cell

The user reported the foils sat at the rotor's stem rather than its
head, overlapping the cabin/CPU light. Two coupled changes:

### Visual (RotorBlock.BuildBlockVisual)

The rotor block visually owns two cells now: its own grid cell (the
stem) plus the cell one step along the spin axis (the mechanism). In
rotor-block local space:

- `MastHeight=0.55` and `SpinHeight=0.78` constants are gone.
- New `MechanismHeight=1.0` puts the spin pivot at the center of the
  cell above. Mast cylinder spans local Y=-0.5..+1.0 (1.5 m tall,
  filling the rotor cell and rising to the disc). Disc and bars
  parent under the spin pivot at the mechanism cell center.

### Adoption (RotorBlock.AdoptAdjacentAerofoils)

The scan iterates lateral cells around the **mechanism cell** rather
than around the rotor's own cell:

```
mechanismCell = rotorCell + Vector3Int.RoundToInt(spinAxisGrid);
foilCell      = mechanismCell + lateralOffset;
```

For a default-orientation main rotor at `(0,1,0)`, foils now adopt at
`(±1, 2, 0)` and `(0, 2, ±1)`. Tail rotors with a horizontal spin axis
adopt their foils in the YZ plane around the mechanism cell at
`(rotorCell.x + 1, *, *)`.

### Mechanism cube + hidden mesh

The blueprint convention: place an invisible structural Cube at the
mechanism cell so the foils' face-adjacency through that cube carries
connectivity back to the chassis. Without it, the foils would be
orphans and the runtime connectivity check would detach them as
debris.

`RotorBlock.HideMechanismCellMesh()` finds that cube at the mechanism
cell and disables its renderer (collider preserved so damage routing
still works). Called from `Start` so every chassis block has already
gone through its own `Awake`/`OnEnable` by the time the lookup runs.

### Helicopter blueprint update

[`GameplayScaffolder.BuildHelicopterEntries`](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
is rewritten to use the new `BlueprintBuilder`:

```csharp
return BlueprintBuilder.Create("Helicopter", ChassisKind.Ground)
    .Block(BlockIds.Cube, 0, 0,  1)
    .Block(BlockIds.Cpu,  0, 0,  0)
    .Row(BlockIds.Cube, new Vector3Int(0, 0, -1), new Vector3Int(0, 0, -3))
    .Block(BlockIds.AeroFin, 0, 1, -3)
    .RotorWithFoils(new Vector3Int(0, 1, 0))
    .RotorBare(new Vector3Int(1, 0, -3), spinAxis: Vector3Int.right)
    .Build()
    .Entries;
```

Foil ring now lives at `y=2` — above the rotor's stem, not next to it.

### Tests updated

`Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs` — three
test cases moved their foil placements from `y=1` to `y=2` to reflect
the new mechanism-cell scan. Axial-cull and zero-baseline-cost tests
unchanged.

## Phase 4 — Bigger helicopter, two guns

Default helicopter scales up from ~11 cells to ~38. New layout, top-down:

- **Body (y=0):** 3-wide × 9-deep central column with sides at z=-1..2.
  CPU at the cabin centre; nose tip at (0,0,3); tail boom 4 cells at
  x=0, z=-2..-5.
- **Hardpoint guns (y=0):** outboard at (±2, 0, 0). Mirrored across X.
  Each connects via face-adjacency through the corresponding cabin
  side at (±1, 0, 0).
- **Cabin roof (y=1):** 3×4 slab at z=-1..2, anchoring the rotor stem.
- **Tail fin (y=1):** AeroFin at the boom tip, (0, 1, -5).
- **Tail rotor (y=0):** bare cosmetic spinner at (1, 0, -4) on the
  boom segment's +X face. Spin axis +X, no foils — purely visual.
- **Main rotor + foil ring:** stem at (0, 2, 0); mechanism cube and
  four foils at y=3 (the absolute topmost cells on the chassis,
  matching the user's request).

`HelicopterBlueprintTests` (new) covers four invariants:

- Foils are at the absolute topmost Y layer.
- Exactly two `BlockIds.Weapon` blocks.
- Guns mirror across X (same Y, same Z, opposite X).
- Full validator pass.
- `RotorsGenerateLift = true`.

Each `WeaponBlock` owns its own `ProjectileGun` and yokes/aims at the
chassis's shared `WeaponMount` aim point — multi-gun chassis was
already supported, this is the first preset to exercise it.

## Phase 5 — Hook + Mace tip blocks (rope-end damage)

New placeable blocks that adopt onto an adjacent rope's tip segment
at game-start and deal contact damage on collision per PHYSICS_PLAN §3.

### New block ids and definitions

- `BlockIds.Hook` — light + sharp, mass 0.5 kg, 60 HP.
- `BlockIds.Mace` — heavy + blunt, mass 2.0 kg, 90 HP.

Both go through `BlockDefinitionWizard.CreateTestDefinitions` so a
re-scaffold creates the asset files automatically. Mass differential
is the gameplay differentiator: same swing speed, ~4× the kinetic
energy on a mace vs a hook.

### TipBlock base class + subclasses

`Movement/TipBlock.cs` (new) — abstract base. Holds the cooldown dict,
the kinetic-energy formula, the chassis-self-damage filter. Subclasses
implement `BuildTipVisual` to draw their own mesh + collider.

`Movement/HookBlock.cs` — shaft + barb, box collider.
`Movement/MaceBlock.cs` — sphere with six axial spikes, sphere collider.

Damage formula matches PHYSICS_PLAN §3 spec:

```
μ = m_self × m_other / (m_self + m_other)   (collapses to m_self vs static)
KE_J = ½ × μ × |v_rel|²
KE_kJ = KE_J × 0.001
damage = KE_kJ × Tweakables.RopeDamagePerKj
```

Speed gate: `Tweakables.RopeMinSpeed` (default 4 m/s).
Per-pair cooldown: `Tweakables.RopeHitCooldown` (default 0.10 s).

### Adoption flow

`RobotTipBlockBinder` (new, mirrors `RobotRopeBinder`) attaches the
right MonoBehaviour at placement time. `RopeBlock.Build` now calls
`TryAdoptTipBlock` after spawning the chain: scans grid neighbours
for a `TipBlock`, reparents it under the last segment, sums the tip's
mass into the segment's rigidbody, and adds a
`TipCollisionForwarder` on the segment that forwards
`OnCollisionEnter` to the tip's `HandleCollision`.

`DestroySegments` now calls `ReleaseAdoptedTip` first so the tip
GameObject doesn't get destroyed alongside the segments on a Rebuild.

### Tweakables

`Combat.RopeDamagePerKj` (default 2.0, range 0..50).
`Combat.RopeMinSpeed` (default 4.0, range 0..20).
`Combat.RopeHitCooldown` (default 0.10, range 0.02..1.0).

Per PHYSICS_PLAN §5 these are MP debt — server picks canonical values
once netcode lands. Documented in the registration block.

### BlueprintBuilder helpers

- `RopeWithHook(ropeCell)` — places a Rope plus a Hook directly below.
- `RopeWithMace(ropeCell)` — places a Rope plus a Mace directly below.

`BlueprintAsciiDump` glyph map extended: `h` for Hook, `m` for Mace.

### Tests

`BlueprintBuilderTests` covers `RopeWithHook` / `RopeWithMace`
placement and validates a CPU + cube + rope + hook chain passes
connectivity. PlayMode tests for the actual contact damage are
deferred — the formula is shape-mirrored on `MomentumImpactHandler`,
which is already covered by ramming integration tests.

## Phases ahead

- **Phase 6** — barbell dummy + arena spawn wiring.
