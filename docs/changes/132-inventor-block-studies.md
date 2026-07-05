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

## Open / next

- User verdict on the seven studies — which graduate to export +
  Unity wiring (WeaponModelRig for SMG; blocks need per-type prefab
  paths).
- Texture stage still pending for ink construction marks (part
  numbers, registration ticks, mirror-writing easter egg).
- If musical SFX proceeds: pick 2–3 pilot cues, author root-note
  instrument clips, then judge in the Rotor Tower stress scene.
