# 132 — Inventor-aesthetic block studies + musical-SFX bones

**Date:** July 4, 2026
**Intent:** First visual pass on the inventor aesthetic
([research/inventor-aesthetic.md](../research/inventor-aesthetic.md))
applied to actual game components. Blender-only style studies (no
export, no Unity wiring) — same methodology as the session-131 SMG
studies. Plus the minimal infrastructure for instrument-voiced SFX,
shipped dark.

## The material grammar (proposed, not committed)

Wood = structure (spruce members, walnut frames/edges — laminate read
carries the paper-punk lamination language forward). Linen = skin
(membranes on movement blocks, drum wraps). Brass = mechanism fittings
(unchanged from paper family). Hemp cord = tension members (lashings,
rigging, wound-rope tires) — new, very contraption. Vermilion stays
the projectile/thrust channel. Cyan stays CPU/energy, now as a
physical "spark". Colors in `inventorlib.py` are linear-space values.

## What shipped (artgen/, Blender row at y = −5)

- `inventorlib.py` — lathe / rod / sweep / torus / ribbon / arc-seg
  helpers + inventor materials, layered on paperlib.
- `inv_cube.py` — structure cube: walnut timber frame, inset linen
  panels, brass peg caps. Cheap (12 beams + 6 panels); "solid where
  you shoot it."
- `inv_wheel.py` — cartwright wheel: 6 walnut felloes (visible
  joints), 8 turned spruce spokes, brass hub cap, 3-strand wound-rope
  tire. Contrast pass: dark rim so light spokes read.
- `inv_wing.py` — rib-and-membrane made literal: walnut leading spar,
  5 ribs, cambered linen membrane with scalloped trailing edge (the
  30 m "fabric wing" cue), cord lashings, brass mount.
- `inv_thruster.py` — bellows blower: linen accordion (deep cartoon
  pleats after v1 read as a drum), walnut boards, spruce cage staves,
  turned nozzle + brass ring + vermilion bore, side hand-crank.
- `inv_rotor.py` — **the flagship: da Vinci aerial screw.** Helical
  linen sail with thickness + sag, spiral spruce batten, radial ribs,
  hemp rigging from the masthead, laminated wooden yaw gear (rotating
  things stand on gears — family rule kept).
- `inv_smg.py` — SMG transposed onto the exact paper-punk bones
  (numbers copied from `smg_paperpunk.py`) so the comparison isolates
  material language: wood laminate loaf, walnut side plates, linen
  reel drum + gauge face, cord lashings at the barrel root, brass +
  vermilion unchanged.
- `inv_cpu.py` — CPU as gimballed gyroscope: two wooden gimbal rings,
  open brass flywheel (v1 solid disc hid the center), turned pedestal,
  and a small emissive cyan spark — "the idea that keeps the machine
  alive"; the sanctioned anachronism keeping cyan = CPU.

All idempotent (`clear_objects` by per-study prefix), run via
blender-mcp with `importlib.reload`. Paper-punk row remains at y = 0
for side-by-side comparison.

## Musical-SFX bones (infrastructure only, all cues unchanged)

`MusicalSfx.cs` (Core): global major-pentatonic table, three phrase
policies — ScaleRandom, ArpeggioUp (repeats within 1.2 s climb the
scale; a gap resets — the mortar-volley flourish), ChordTone
(root/fifth/octave for impacts). `AudioCueLibrary.Entry` gains a
`Phrase` field (default None → zero behavior change; jitter and
musical pitch are exclusive paths in `AudioRouter.ConfigureSource`).
Note *selection* only — authored clips at the scale root get
repitched within one octave, which audio.md already sanctions. Zero
alloc, statics domain-reload-reset. Nothing is wired to a phrase yet:
which cues go musical is a design call + needs root-note clips.

### Why this might be a terrible idea (adversarial pass, requested)

1. **Repetition fatigue is worse for notes than for noise.** Melodic
   intervals are memorable; memorable × 400 plays/match = grating.
   Don't Starve's instrument voices fire on *dialogue* (rare, 1:1 with
   attention); an SMG at 12 Hz is not dialogue. Mitigation: musical
   phrases only on low-rate, high-salience cues (mortar, module,
   kill); never on WeaponFire/hit-spark class cues.
2. **SFX is a threat-identification channel.** "Cannon = boom" is
   learned instantly; "enemy cannon = low piano" compresses worse and
   competes with the music bus for the same perceptual slot.
   Readability may pay for the whimsy.
3. **Harmony at scale is fragile.** Pentatonic-everything keeps 16
   players consonant but samey-cute; and if a real music system lands
   later in a different key or modulates, every musical cue clashes
   until SFX key-tracks the score — a coupling audio.md never planned.
4. **It raises the bar for every future feature.** Invariant #8 says
   every feature ships with VFX + audio; if the audio identity is
   "instruments", each new block needs a *musical* voice, not a USFX
   clip. Half-committed, it reads as a bug ("why did my gun play a
   xylophone?").
5. **Client-local phrase state.** The ascending run depends on event
   arrival order; two clients hear different runs. Cosmetic-only
   today, but "the flourish" will never be reproducible content
   (kill-cams, replays).

Counter-position: the slapstick + capybara + Don't Starve lineage is
coherent, and the 80/20 is *stingers on rare events* (match start,
kill, volley landing), not per-shot melody. The infra above is
exactly big enough to pilot that on the mortar and small enough to
delete in one commit if it's cursed.

## Revision pass (same session, user review)

Direction confirmed — visual work continues, audio parked for later.

- **Woods darkened a full step** (`inventorlib.py` linear values), linen
  gained a procedural weave bump (`_weave`, idempotent nodes). Shared
  datablocks, so all studies updated without rebuilds.
- **New authoring habit: components are sized to the mechanic they
  replace, not to one cell.** Most non-block components will span
  multiple blocks. First application: the aerial screw is now a wide,
  thin ~2.9 m disc (low helical rise, short mast) because it visually
  replaces the whole helicopter assembly. Rigging kept to the final
  upper turn — crossing cords read as an umbrella frame when squashed.
- **Wing rebuilt as a vague bat-wing profile** (user: profile, not
  literal anatomy): five tapered spars fanning from a root boss, swept
  along the membrane surface, fatter than the membrane so the skeleton
  reads from both faces; deep scallops; brass tip beads; ~2-block span.
- **SMG fully rerolled** — paper-punk loaf bones discarded on user
  call. New concept: crank-organ pellet gun (cask receiver + brass
  hoops, turned flared barrel + vermilion bore, linen pellet sack on a
  brass feed pipe LEFT, crank + exposed drive gear RIGHT, turned yoke
  columns). `smg_style_reclaimed.py` / `smg_style_steampunk.py` deleted
  (git history keeps them; reclaimed's metal-and-plants idea stays
  recorded in inventor-aesthetic.md as arena-layer candidate).
  `smg_paperpunk.py` kept — it's the source for the in-game interim
  SMG_Paper.fbx.

## Structure-cube direction (decided this session)

Frame+linen is dead as the bulk tile — high internal contrast tiles
into a waffle (see `inv_cube_walls.py`, A/B/C/D wall comparison; the
frame+panel language stays a candidate for special one-off blocks).
User's dark-oak-planks instinct steered to: **continuous oak
planking, slightly darker than mid, with directional grain** — dark
collapses to a blob at combat distance and spends the walnut accent.
`inv_cube.py` v2: four courses ringing the cube, staggered butt
joints, two-tone oak + grain (`inventorlib.oak_grain`, `OAK_A/B`),
no frame, no pegs. In-game plan: ONE chamfered mesh + plank albedo
with 3-4 joint variants picked per block via MaterialPropertyBlock UV
offset (MPB path already exists for damage tint, batching survives).

## Full component pass (same session, third leg)

Tiling walls deleted (scene + `inv_cube_walls.py` git-rm'd — decision
recorded above, git history keeps the script). Then sixteen new
studies at y = −8 covering the rest of the block roster
(`inventorlib` grew `ribbon` axis param + glass/mint materials;
`inv_wheel.make_wheel` is now reusable):

- **WheelSteer** — cartwheel in a walnut caster fork under a mini
  steering gear + brass kingpin. **AeroFin** — 3-spar pennant of the
  wing fan. **Rudder** — sternpost + brass gudgeons, vertical fan
  blade, tiller arm. **Drill** — Archimedes auger: iron flight on a
  tapered oak shaft off a laminated gear collar. **Spring** —
  full-elliptic carriage leaf spring, cord-whipped eyes (rotated to
  face front). **HoverBlade** — pitched-linen pinwheel fan in an oak
  hoop (v1 vanes read as shards; overlap + gentle pitch fixed it).
- **Cannon** — iron barrel + brass hoops on stepped walnut cheeks,
  scalloped oak wheels, cascabel, red bore. **Mortar** — staved oak
  tub bombard at 42° on wedge cheeks, deck bomb. **BombBay** —
  open-bottom crate, cord X-braces, trapdoors mid-swing, laminated
  iron bomb dropping. **GrappleMagnet** — deck winch: hemp drum, oak
  trough, horseshoe loaded poles-forward, crank.
- **Rope** — flanged spool of hemp + cleat. **Hook** — 3-fluke iron
  grapnel, brass points. **Mace** — laminated wood ball + iron studs.
  **Magnet** — THE cartoon horseshoe (vermilion, pale pole shoes,
  brass eye); `inv_tips.horseshoe` shared with the launcher.
- **Modules** (family rule: same apparatus, contents differ): walnut
  plinth + brass-collared glass bell jar; EMP = mini tesla coil with
  cyan spark, Repair = mint draught. Blink/Shield/Smoke/Invis/Mines
  follow the recipe.

Every gameplay block id now has an inventor-language study except the
plain CPU/cube/wheel/wing/thruster/rotor/SMG set from earlier legs.

## Unity wiring (fourth leg, user go-ahead)

All 22 studies exported to FBX (`artgen/inv_export.py`, paperlib
frame conversion): four weapons to `Art/Models/Weapons/*_Inv.fbx`,
the rest to `Art/Models/Blocks/Inv/`. Rig rules encoded in the
exporter: cannon rake zeroed (TurretYoke owns pitch and forces
identity on the Turret node), the mortar tub's display tilt BAKED
into child geometry so the yoke exports identity and MortarBlock's
runtime lob isn't doubled, wheel wrapped in a −90°-about-Y empty so
its axle lands on Unity +Y (= WheelBlock's spin axis) at exactly 1 m
diameter, bomb bay exported doors-closed / no bomb.

Code: `BlockDefinition` gained optional `_visualModel/Scale/Offset`
(behaviours are runtime-AddComponent'ed, so models must ride the
definition — mirrors `WeaponStatsDefinition`). `WheelBlock.EnsureRig`
instantiates the model under `Spin` (scale = 2 × physics radius) and
skips the tyre/hubcap primitives; stem/hub/spin rig unchanged. New
`InventorModelWiring` editor menu (Robogame → Art → Wire Inventor
Models) idempotently points Weapon_Smg / Cannon_Default /
Mortar_Default / Bomb_Default (`_turretModel`, offset −0.5; bay 0)
and BlockDef_Wheel(+Steer) (`_visualModel`) at the FBXs. Kept OUT of
BlockDefinitionWizard on purpose — its stat-restamping clobber (131)
must never ride along with art wiring.

**Wired this leg:** SMG, cannon, mortar, bomb bay, wheel, steer wheel.
**Sweep remaining** (each needs its own moving-part integration in
the behaviour, FBXs already in-repo): rotor (spin child), thruster
(bellows + nozzle), wing/fin/rudder (aero surfaces), hoverblade,
spring, drill (auger spin), rope, tips (rope-end attach), grapple
(procedural rig today, no model path), modules, CPU. Cube stays on
the MK Toon primitive until the plank albedo exists (perf: the cube
is THE batched block; a 49-object prefab would nuke instancing).

## Live verification (bridge session)

Wiring menu ran clean: 6 definitions updated, asset diffs audited —
model fields only, zero stat re-stamps (the 131 clobber lesson held).
Play-mode garage: crank-organ SMG live on the Tank turret, cartwright
wheels replacing the tyre primitives at correct diameter. Spot-check
instrumentation (ShootPoint offset from root) caught **one real bug**:
the mortar's baked rotation used −90°X and aimed the tub backward
(muzzle −Z). Exporter sign fixed (+90°X: mouth +Z→−Y), re-exported,
reimported: muzzle now root-local (0, 0.41, +0.69) ✓. Cannon verified
+Z forward. Suite was green on this exact code pre-wiring (0 failures
both modes); asset changes since are data-only, so no extra
qa-verifier pass. perf-checker skipped per rule: zero physics objects
added (visual swaps only).

## Mortar re-verify + plank albedo (user-away session)

User reported mortars as red cubes + not firing — that was the
pre-refresh editor window (FBXs/wiring existed on disk, editor hadn't
imported). Reproduced their path live: placed a mortar via
`BlockGrid.PlaceBlock` in play mode — TurretModel builds, host mesh
hides, yoke drives to lob elevation, and reflection-invoked
`FireMortar()` launched a live shell (tracer confirmed on screen).
Zero console errors. If their saved bot still shows red cubes, it's a
stale scene instance — reload rebuilds it.

**Plank texture landed** (the hesitation was geometry-vs-texture: 49
plank objects would break the batched cube; a painted albedo costs
nothing). `InventorPlankTexture` (menu: Robogame → Art → Generate
Plank Texture) generates a 256² oak-plank albedo (4 courses,
staggered joints, Perlin grain, painted course chamfer — sRGB bytes
derived from inventorlib's linear oak tones) and applies it to
`BlockMat_Structure` (`_AlbedoMap`+fallback slots, tint → white so
damage MPB darkening still multiplies). `BlockMaterials` re-applies
it after every rebuild so Build Everything can't silently revert the
cube to slate. Verified in-game: the Tank hull reads as planked oak.
Joint variants via per-block MPB UV offset remain the phase-2 step.
BlockDef asset churn (24 files) = the new `_visualModel` fields
serializing with defaults; scene churn committed separately as chore.

## Red-cubism deep pass (user report: mortar red + dead in arena)

Reproduced the REAL pipeline end-to-end (garage → CurrentBlueprint →
Launch → ChassisAssembler → arena): on current code the mortar builds,
binds, and fires in the arena — the user's red-cube session was a
stale-editor state (import-window class). But the investigation
surfaced three systemic red-cubism causes, all fixed:

1. **Silent block drops:** `ChassisAssembler` ignored
   `PlaceBlock == null` — a rejected block vanished between garage and
   arena with zero log. Now warns with id + cell (Fail Loud).
2. **Bind-once-in-Awake input:** Mortar/Cannon/BombBay bound
   `IInputSource` once in Awake; any activation-order change (arena
   spawn, future netcode possession) leaves dead weapons. All three now
   lazily re-resolve in Update — the same fix `DrillBlock` already
   carried with a comment admitting the staleness.
3. **No generic model path:** blocks without bespoke visual components
   (drill, rope, spring, tips, modules) rendered the raw red host cube
   forever. `BlockDefinition` gained `_visualModelStatic`;
   `BlockBehaviour.Initialize` now attaches the authored model
   generically (host mesh hidden, before `CacheRenderers` so damage
   tint reaches it). Opt-in per definition — moving-part blocks stay
   component-driven. Wired: drill (Archimedes auger verified live on
   the user's `ddd` bot), rope, spring, hook, mace, magnet, EMP +
   Repair modules. Red-cube states remaining BY DESIGN: blocks whose
   sweep integration is pending (rotor/thruster/wing/fin/rudder/
   hoverblade/grapple/CPU keep procedural or primitive visuals until
   their moving-part consumers land).

Diagnostic pattern worth keeping: the user save at
`%AppData%..\LocalLow\MutedTuple\Robogame\blueprints\*.robot.json` can
be parsed via `BlueprintSerializer.TryFromJson` and pushed through
`GameStateController.SetCurrentBlueprint` to test the exact
launch-frozen pipeline without touching build-mode UI.

## Mortar fire-cycle verification (real input path)

User re-report: mortar "worked for a second" then stopped firing.
Verified the complete chain in the arena with synthetic Input System
mouse events (real device path — PlayerInputHandler → FireHeld →
WeaponFireGate → WeaponAmmoState → FireMortar): mortar fired all 5
rounds, auto-reload ran (3.5 s), pool refilled, firing resumed.
Console clean throughout. The authored rhythm explains the feel:
2.2 s between lobs, 5-round clip, 3.5 s locked reload — clicking
instead of holding lands most presses inside the cooldown, and the
reload window is 3.8 s of nothing. Feedback audit: WeaponEmpty ✓ and
ReloadComplete ✓ have clips; **ReloadStart has no clip** (reload
begins silently) — wire a clip in AudioCueLibrary. The user's hard
failures (red cube, zero fire) all occurred against mid-session
editor states while this session was actively compiling/refreshing;
every clean-state repro passes. If it recurs in a fresh session:
capture WHERE (garage vs which arena) + console before anything else.

## THE mortar bug (caught live in the user's session)

User screenshot: mortar as a red cube in the arena, ammo HUD showing
the pool, "turned red as soon as I clicked save." Live inspection of
their running session: the mortar block had **no MortarBlock
component** and the robot had **no RobotWeaponBinder at all**. Root
cause: `ChassisAssembler`'s `hasWeapon` detection was a hand-synced id
list — {Weapon, BombBay, Cannon, GrappleMagnet} — **missing
BlockIds.Mortar**. A mortar-only bot assembled with no WeaponMount and
no weapon binder: red host cube, dead trigger. Every earlier repro
carried an SMG, which set hasWeapon and masked the bug completely;
"worked for a second" = their earlier bot still had an SMG; "broke on
save" = save triggers reassembly of a fresh root. Fix: weapon
detection by `BlockDefinition.Category == Weapon` via the library —
the same registry-over-hand-synced-list move as ADR-0003 phase B
(tips are Weapon-category too; the binder's ShouldBind skips them, so
an idle binder is the only cost). Verified on the exact failing
config (mortar-only bot, no SMG): binder present, block bound, tub
rendered, 5 rounds fired via synthetic held input, impacts on
terrain.

## Open / next

- User verdict on the seven studies — which graduate to export +
  Unity wiring (WeaponModelRig for SMG; blocks need per-type prefab
  paths).
- Texture stage still pending for ink construction marks (part
  numbers, registration ticks, mirror-writing easter egg).
- If musical SFX proceeds: pick 2–3 pilot cues, author root-note
  instrument clips, then judge in the Rotor Tower stress scene.
