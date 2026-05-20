# Game Feel — Plan & Handoff

> **Audience.** A future Claude Code session (or human) picking up the
> game-feel discipline pass cold.
>
> **TL;DR.** Operationalise the four pillars of game feel (pushback,
> hit flashes, sound design, VFX) plus the closing-the-loop principle
> for Robogame's specific shape: block-based vehicular combat with a
> slapstick tone, an existing audio + VFX system from sessions 29–30,
> and an MP-readiness contract that constrains where feel logic can
> live.
>
> **Source material.** A Game Feel breakdown video citing Andre
> Antunes' Mixin' Jam demo (BotW, Tunic, Metroid Dread, Sekiro,
> Hearthstone, Expedition 33 as references). The pillars in §3 below
> are paraphrased from that video; the Robogame-specific extensions
> in §4 are this project's responsibility.
>
> **Status.** Plan. No code in this branch yet. Implementation should
> follow the planner-first workflow from `CLAUDE.md`.

---

## 1. Why this exists

Game feel is the connective tissue between simulation and the player's
senses. For a block-based combat game it's load-bearing in a way it
isn't for most genres: every hit is a discrete event on a discrete
block, which means there are far more "moments" per second that need
feedback than in a typical action game where one swing produces one
hit. Get the per-hit feel wrong and combat reads as numb regardless
of how good the underlying physics is.

The good news: Robogame already has the systems. `VfxSpawner`
(session 29) and `AudioRouter` + `AudioCueLibrary` (session 30) are
the two biggest scaffolds, and `CLAUDE.md` invariant 8 already
mandates that every new feature ship with VFX and audio. What's
missing is a discipline doc that names the *choreography* across
systems on each gameplay event, and a polish pass that closes the
gaps between "audio plays, VFX plays, damage applies" and "the hit
feels like a hit."

This plan is that discipline doc.

## 2. Principles (carried over from the video)

Three meta-principles before the per-pillar breakdown. Anything in
this plan that conflicts with these is wrong.

**Close the loop.** Every player input that produces a gameplay
outcome should produce sensory feedback that confirms the outcome.
Effective hits feel different from ineffective hits. Destroyed blocks
feel different from grazed blocks. The simulation and the senses tell
the same story.

**Player purpose and player fantasy.** Robogame's fantasy is
"build a goofy machine and break other goofy machines." The feel
target is therefore slapstick over realism, big-readable over
physically-accurate. This is committed in
`GAME_DESIGN_PILLARS.md` §"Slapstick over realism for combat feel."
Defer to that pillar when a realism-vs-readability call comes up.

**Feel is a budget item, not a free polish layer.** Per-hit shaders,
camera shake, hitstop, and particle bursts have a real perf cost,
and some of them have networking constraints. `PERFORMANCE.md` and
`NETCODE_PLAN.md` apply. Feel work that misses the perf budget or
violates the server-authoritative contract is not feel work; it's
tech debt.

## 3. The four pillars, mapped

### 3.1 Pushback

**The video.** Hit targets visibly react to impact: BotW enemies
stagger, Tunic enemies slide. Combined with hit flashes the player
reads "I hit it and it mattered."

**Robogame.** Targets are chassis Rigidbodies. Pushback is therefore
already physically present whenever a projectile hit applies an
impulse. The math lives in `MomentumImpactHandler` (per
`PHYSICS_PLAN.md` §3) and uses the kinetic-energy formula
`(reduced_mass * v_rel^2) / 2` with a velocity threshold.

What's almost certainly missing today:

- **Projectile hits applying a pushback impulse on the target
  chassis.** The mass-velocity rule is shipped for ramming
  (chassis-vs-chassis) and for tip blocks (`Hook` / `Mace`). It is
  not obviously wired for `ProjectileWorld` hits. A 5 kg cannon
  shell at 80 m/s should rock a 200 kg buggy noticeably. Confirm
  in audit.
- **Visible scale-pulse on the hit block.** The video calls out
  Andre's capsules growing on hit then settling. Robogame's
  block-level equivalent is a brief uniform-scale pulse on the cell
  that took the hit, scoped to the local visual mesh, no physics
  consequence.
- **Per-chassis "stagger" on big hits.** When a hit deals more than
  a threshold fraction of total HP (or destroys a CPU-adjacent
  block), the chassis briefly loses control authority for ~120 ms,
  visualised as a damped oscillation around the hit-direction axis.
  Server-authoritative because it changes movement output.

### 3.2 Hit flashes

**The video.** A brief colour swap on the hit target communicates
"this was an effective hit." Metroid Dread layers tier-of-success
into the flash colour (yellow flash signals parry windows).

**Robogame.** Per-block flash on damage. Cel-shaded URP makes this
straightforward via a material-property block override on the
relevant `MeshRenderer`. Two requirements:

- **On-palette.** Per `ART_DIRECTION.md`, no off-palette colours. The
  flash colour is one of the 12 WorldPalette tokens. White-flash is a
  common default; for Robogame I'd recommend the brightest palette
  yellow as the impact colour and the brightest red for "this block
  is about to die" (HP < 25%).
- **Tiered.** Robogame can copy Metroid Dread's tier-of-success
  layering directly. Three tiers: graze (HP > 50% remaining after
  hit), crit (HP < 50% but block still alive), kill (block destroyed
  this hit). Different flash colour and intensity per tier. This is
  the cheapest way to make individual block hits read as different
  events.

### 3.3 Sound design

**The video.** Layered cues. Swing sound, impact sound, ambient
music transitions for tier-of-engagement (BotW Guardians piano,
Sekiro parry clang). Confirmation through audio.

**Robogame.** `AudioRouter` + `AudioCueLibrary` shipped in session
30 with 21 cues. Likely-shipped cues: weapon fire, impact,
explosion, rotor whine loop, HUD clicks. Likely-missing cues for a
proper feel pass:

- **Tier-of-success impact variants.** One cue per tier (graze, crit,
  kill) per material class (armour, weapon block, structural panel).
  At a minimum: 3 tiers × 2 material classes = 6 cues. Library entry
  blank is fine; the gameplay site calls `AudioRouter.PlayOneShot`
  with the cue name and the missing-cue logger surfaces it for the
  audio pass, per the contract in `AUDIO_PLAN.md`.
- **Match-state cues.** Round start, round end, comeback-mechanic
  trigger, objective tick. These are global, not per-hit.
- **Build-mode cues.** Block snap (placement satisfying), block
  remove (un-thunk), validation rejection (overlap-check failure
  per `SCALABLE_PARTS_PLAN.md`).
- **Audio ducking on big events.** A CPU-block destruction or a
  round-win trigger should briefly duck ambient and SFX so the
  cinematic cue reads. This is a mixer concern, not a per-cue
  concern; lives in the mixer setup, not in `AudioCue` data.

### 3.4 VFX

**The video.** Sparks, particles, debris, lingering marks. Andre's
demo uses sparks from impact, ricochet off ground, persistent decals.

**Robogame.** `VfxSpawner` shipped in session 29 with procedural
particle kinds (muzzle flashes, hit sparks, debris dust, thruster
plume). The gaps to close:

- **Block-destruction debris.** When a block goes from alive to
  destroyed, the destroyed cell's mesh shatters into small physics
  debris (non-networked, per `NETCODE_PLAN.md` §7c). Lifetime ~3 s,
  fades out on-palette. This is the single biggest visual moment
  in combat and the cheapest way to make destruction feel weighty.
- **Lingering scorch marks.** Decal at hit point on the chassis that
  persists until the block is destroyed. Reads as accumulated damage
  without needing per-block HP bars.
- **Trail VFX.** Projectiles already have visual lifetimes via
  `ProjectileWorld`. Confirm that fast projectiles (cannon, not
  bomb) show a tracer that reads at projectile speed.
- **Thruster / rotor "stress" VFX.** When a thruster is at max
  output or a rotor is over-spun, the existing plume / whine should
  intensify visibly. Communicates "you are pushing this build past
  comfort" without a HUD readout.

## 4. Robogame-specific extensions beyond the video

Block-based vehicular combat is enough of a niche that the four
pillars don't fully cover the design space. These are the additions.

### 4.1 Block-by-block feedback at scale

A chassis hit by a single bullet is one event. A chassis caught in a
splash blast is 5–10 events on the same frame. Naively triggering
the full pillar stack on every block (flash + scale-pulse + spark +
audio + camera shake) for 10 events instantly produces sensory
overload and tanks the frame.

**Decision: aggregate per-frame.** Multiple block hits on the same
chassis in the same physics step collapse to one "burst" feedback
event: a single camera shake scaled by total damage, a single
audio cue with a "burst" variant, individual per-block flashes
preserved. The aggregation lives at the chassis level, not the
block level.

This also matches the `BlockHitBatch` networking shape in
`NETCODE_PLAN.md` §7b — one batch per tick per chassis. Feel
aggregation and network aggregation use the same boundary.

### 4.2 Tier-of-readability for block role

A weapon block destroyed feels different from an armour cube
destroyed. The video's tier-of-success applies to *outcome*; this is
tier-of-readability applied to *target role*.

**Decision: three role tiers for VFX/audio variants.** Cosmetic
blocks (panels, rods, decorative cubes) get the lightest treatment.
Functional blocks (weapons, thrusters, hovers, rotors) get medium
treatment. Critical blocks (CPU, control-anchor) get the cinematic
treatment — slow-mo hitch, brighter flash, distinctive cue.

The CPU-block destruction is the single most important "moment" in
combat per Robocraft precedent (75% CPU loss = death). Treat it
accordingly.

### 4.3 Vehicle feel as a separate discipline

The video focuses on combat feel. Robogame is also a driving game,
and helicopter feel ≠ tank feel ≠ plane feel ≠ boat feel. Per-chassis
movement curves matter:

- **Wheeled ground vehicles.** Grip ramp-up on acceleration, weight
  transfer in turns, suspension travel under load. Camera roll
  should respond to chassis roll with a damped offset.
- **Helicopter.** Rotor spin-up curve (already exists per session
  19's stem mechanism). Lateral drift when not actively
  countering. Tilt-into-turn.
- **Plane.** Stall feedback (control authority drops, audio cue,
  visual airflow particles). Banking authority increases with
  airspeed.
- **Boat.** Wave-induced pitch/roll, foam wake (already exists per
  session 11), spray on bow at speed.

Each of these is a Phase 4 item in the plan below. Each gets its
own per-chassis-type tuning pass.

### 4.4 Slapstick exaggeration calibration

Per the `GAME_DESIGN_PILLARS.md` slapstick commitment, impacts
should err on the side of cartoonish over physically-accurate. This
is a *direction*, not a free hand. Concretely:

- **Pushback impulses scaled 1.3–1.5× over realistic.** A buggy
  taking a cannon shell should hop visibly, not just lurch.
- **Debris bounces extra.** Detached debris gets a slightly elastic
  collision response and a goofy spin imparted on detach.
- **Big hits are bigger than realistic.** A chopper losing a rotor
  should swing wildly, not just gently descend.

Avoid: comedy *sounds* (boings, comedy honks). The visual tone is
slapstick; the audio palette stays grounded so impacts retain
weight. This is the same discipline as cel-shading + grounded
materials.

### 4.5 Camera as a feel tool

Not in the video's four pillars but adjacent. The camera is the
single most expressive feel surface that doesn't cost network
bandwidth.

- **Camera shake.** Scaled by impact magnitude. Capped to prevent
  nausea. Player-side accessibility toggle in settings (lives in
  `Tweakables`, see invariants below).
- **FOV punch.** Brief outward FOV pulse on big hits / boost
  activation / weapon fire.
- **Roll response.** Camera tilts a few degrees on hard turns to
  reinforce chassis roll.
- **Hitstop.** A 1–3 frame freeze on big hits. **Local-visual-only**
  — it freezes the camera and the visual mesh interpolation, not
  the underlying physics tick. Crucial for MP correctness.

## 5. Decisions committed

These are settled. Future sessions should not re-litigate without
explicit user approval.

### 5.1 Visual feel is local; gameplay outcome is server-authoritative

The pattern from `NETCODE_PLAN.md` §9 (clients predict the visual,
server confirms the math) generalises here. Camera shake, hit flashes,
particles, hitstop, audio cues all run on each client based on local
events. The actual damage, pushback impulse, and stagger state run on
the server and replicate. Visual desync between clients is acceptable
as long as the math agrees.

### 5.2 Aggregate per-chassis-per-tick

See §4.1. Multiple block hits on the same chassis in the same physics
step share one camera shake, one screen-level audio burst, one
chassis-level VFX. Per-block flash and per-block VFX still play
individually. This matches `BlockHitBatch` aggregation in netcode.

### 5.3 Three tiers of outcome, three tiers of role

Outcome: graze / crit / kill (per §3.2). Role: cosmetic / functional /
critical (per §4.2). Combinatorially that's nine distinct feedback
shapes. Most can share assets with intensity variation; the
critical-tier cues should be unique.

### 5.4 Hitstop is local-visual-only

Never freezes the physics tick. Never freezes the input buffer. Only
freezes the camera transform interpolation and the visual mesh
position interpolation, for 1–3 frames at 60 Hz. Server is unaware
hitstop happened. This is the only safe shape under MP.

### 5.5 Accessibility is first-class

Settings panel exposes:

- Camera shake intensity (0–100%, default 70%)
- Hitstop intensity (0–100%, default 100%)
- Flash brightness (0–100%, default 100%)
- Particle density (0–100%, default 100%)
- Screen FX overall (0–100%, default 100%)

These are pure presentation knobs and live in `Tweakables` per the
`PHYSICS_PLAN.md` §5 contract. They affect what the local machine
renders, not what other players see, not what damage is dealt.

## 6. Phased work plan

Each phase is independently shippable. Each ends with a session entry
under `docs/changes/NN-slug.md`.

### Phase 0 — Audit

Map the current state to the pillars. For each of the four pillars,
answer: what's wired today, what's stubbed, what's missing. Specific
checks:

- Does `MomentumImpactHandler` apply impulse on `ProjectileWorld`
  hits, or only on chassis-vs-chassis ramming?
- What hit flash logic exists, if any? Material-property-block
  override path or per-shader uniform?
- Inventory the 21 audio cues from session 30 against the cue
  list in §3.3.
- Inventory the procedural VFX kinds from session 29 against §3.4.
- Camera controller path: does it have any shake / FOV / roll
  hooks today?

**Exit criterion:** a written audit doc at
`docs/changes/NN-game-feel-audit.md` listing what's wired vs.
missing per pillar.

### Phase 1 — Close the loop on combat hits

Highest-leverage phase. Gets every projectile hit to play the full
pillar stack: pushback impulse + per-block flash + spark VFX +
tiered audio cue + chassis-level aggregated camera shake.

- Wire `MomentumImpactHandler` impulses for `ProjectileWorld` hits
  if missing.
- Implement per-block flash via material-property-block override.
  Tiered colour by graze/crit/kill outcome.
- Add tier-of-success audio cue variants. Library entries can be
  blank; the gameplay site calls `AudioRouter.PlayOneShot` with
  the cue name and the missing-cue logger surfaces them for the
  audio pass per `AUDIO_PLAN.md`.
- Add chassis-level shake aggregator: one shake per chassis per
  tick, magnitude proportional to total damage on that chassis
  this tick.
- Test in stress tower (`PHYSICS_PLAN.md` §4) to confirm aggregation
  holds the perf budget under high-hit scenarios.

**Exit criterion:** a one-shot from any weapon at any chassis
produces visible pushback, on-palette flash, spark VFX, audible
impact cue tiered by outcome, and shake scaled by damage. Holds 60
FPS during a 5-rotor stress-tower combat sequence.

### Phase 2 — Block-destruction cinematic moment

The single biggest readability win. Destroyed blocks shatter into
non-networked physics debris that lasts ~3 s before fading.
Critical-tier blocks (CPU, weapons) get distinctive cinematic
treatment.

- Hook `BlockBehaviour.Destroyed` event into `VfxSpawner` for
  per-cell debris burst.
- Critical-tier blocks get a brief slow-mo (local-visual-only,
  per §5.4) and a distinctive audio cue.
- Lingering scorch decals at destroyed cells, on-palette.

**Exit criterion:** killing a robot reads as a kill. CPU-block
destruction is unmistakable across audio + visual + camera.

### Phase 3 — Camera as a feel tool

Camera shake aggregator (already in Phase 1) plus FOV punch, roll
response, hitstop.

- FOV punch on weapon fire, big hits, boost activation.
- Damped roll on chassis roll.
- Hitstop logic in the camera controller, scoped to camera +
  visual interpolation, never to physics tick.
- Settings panel sliders per §5.5.

**Exit criterion:** all four camera feel tools shipped, all
respecting accessibility sliders, all local-only.

### Phase 4 — Vehicle feel pass

Per-chassis-type tuning. One sub-phase per chassis archetype:
wheeled, helicopter, plane, boat. Each sub-phase tunes movement
curves, adds chassis-specific feel cues (engine pitch ramp, rotor
whine modulation, plane stall warning, boat spray particles), and
captures a before/after video for the dev log.

**Exit criterion:** each chassis archetype "feels like itself"
during a 5-minute test drive. Not a measurable criterion; this is
a judgment call.

### Phase 5 — Build mode juice

Smaller phase, big tinkerer-audience win. Block snap satisfaction:
brief scale pulse on placement, "thunk" cue, settling animation
when a block is placed adjacent to an existing structure. Reject
feedback for invalid placements (overlap-check from
`SCALABLE_PARTS_PLAN.md`).

**Exit criterion:** placing 10 blocks in a row feels rhythmic and
satisfying.

### Phase 6 — Match-state moments

Round start / round end / objective tick / comeback-mechanic
trigger. Each gets a coordinated audio + VFX + camera moment.
Depends on the win-condition design landing first (per the open
question in `GAME_DESIGN_PILLARS.md`).

**Exit criterion:** a full match has clear sensory beats at each
state transition.

## 7. Invariants to respect

Carried over from `CLAUDE.md`, `PHYSICS_PLAN.md`,
`NETCODE_PLAN.md`, and `AUDIO_PLAN.md`.

1. **Visual feel is local; gameplay outcome is server-authoritative.**
   See §5.1.
2. **Hitstop never touches the physics tick.** Local-visual-only.
   See §5.4.
3. **Accessibility sliders live in `Tweakables`.** Pure-presentation
   knobs; do not affect what other players see. Anything tier-of-success
   that affects the actual damage output (not just the flash colour)
   must move to per-block / per-chassis blueprint config.
4. **No off-palette colours.** Flash colours come from the 12-token
   WorldPalette per `ART_DIRECTION.md`.
5. **Every new gameplay event ships with VFX + audio.** Per
   `CLAUDE.md` invariant 8. Even if a cue or VFX kind doesn't exist
   yet, declare it at the call site so the missing-cue logger
   surfaces it.
6. **No per-frame allocations.** Camera shake state, FOV pulse
   curves, hitstop timers all live on pre-allocated structs. No
   `new` in `Update` / `LateUpdate`.
7. **Profile per phase.** The stress tower (`PHYSICS_PLAN.md` §4)
   is the canonical perf check. Capture before / after for any
   feel pass that touches per-tick code paths.

## 8. Open questions for the user

These need a decision before the relevant phase lands. None block
Phase 0.

- **Flash colour palette tokens.** Which two WorldPalette tokens map
  to "graze flash" and "kill flash"? Recommendation: brightest
  yellow for hit, brightest red for kill. Defer to user taste.
- **Hitstop intensity defaults.** 1, 2, or 3 frames at 60 Hz for
  "big hit"? Recommend 2 frames default with the slider exposed.
  Anything above 3 starts feeling laggy.
- **Slapstick calibration multiplier.** §4.4 suggests 1.3–1.5× over
  realistic for pushback impulses. Pick a value, or pick a per-block
  multiplier table. Recommend a single global multiplier exposed as
  a *blueprint-level* (not Tweakable) constant so it's MP-safe.
- **Critical-tier block list.** §4.2 says CPU and "control-anchor"
  blocks get the cinematic treatment. Confirm this list. Should
  primary weapon blocks count as critical, or only the CPU?
- **Slow-mo on critical kills, or only on round-win?** Slow-mo is
  the loudest feel tool and overusing it numbs it. Recommend
  reserving for CPU-destruction and round-win events only.

## 9. Non-goals

- **Realistic physics-accurate impact response.** Slapstick wins
  per `GAME_DESIGN_PILLARS.md`.
- **Cinematic camera cuts during combat.** Camera authority stays
  with the player; we only modulate the existing chase cam.
- **Comedy audio palette.** No boings, no honks. Visual is
  slapstick; audio stays grounded.
- **Per-player feel tuning that affects gameplay.** Accessibility
  sliders are presentation-only.
- **Replay system.** Out of scope. Belongs in a separate plan.

## 10. References

- `Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs` —
  kinetic-energy damage / pushback math. Audit in Phase 0.
- `Assets/_Project/Scripts/Combat/ProjectileWorld.cs` —
  unified projectile integrator (session 32). Pushback hook lives
  here or in the hit callback.
- `Assets/_Project/Scripts/Block/BlockBehaviour.cs` — per-block
  damage events. Flash + destruction VFX hooks attach here.
- `VfxSpawner` (session 29) — VFX dispatch. Procedural particle
  kinds catalogue.
- `AudioRouter` + `AudioCueLibrary` (session 30) — audio dispatch.
  Library entries are SOs.
- `docs/AUDIO_PLAN.md` — audio plumbing rules and the missing-cue
  logger contract.
- `docs/changes/29-vfx-and-audio-bones.md` — VFX kinds inventory.
- `docs/changes/30-audio-v1.md` — initial cue inventory.
- `docs/PHYSICS_PLAN.md` §3 — kinetic-energy damage spec, the
  authoritative shape for impact calculations.
- `docs/PHYSICS_PLAN.md` §4 — stress tower, the canonical perf
  check.
- `docs/NETCODE_PLAN.md` §7 — `BlockHitBatch` aggregation
  boundary. Feel aggregation should match.
- `docs/NETCODE_PLAN.md` §9 — client-predicted-visual /
  server-authoritative-math pattern. The model for §5.1.
- `docs/ART_DIRECTION.md` — palette and forbidden-list rules.
  Flash colours obey.
- `docs/GAME_DESIGN_PILLARS.md` §"Slapstick over realism" — the
  tone commitment that calibrates §4.4.
- `CLAUDE.md` — hard invariants. Re-read before starting any phase.

## 11. How to start

Per the project workflow in `CLAUDE.md`:

1. Run the Planner subagent (`.claude/agents/planner.md`) over
   Phase 0 to produce the audit checklist.
2. Read `MomentumImpactHandler`, `ProjectileWorld`, `BlockBehaviour`,
   `VfxSpawner`, `AudioRouter`, and the camera controller end-to-end.
3. Land the audit in `docs/changes/NN-game-feel-audit.md`.
4. Phase 1 onward, run the Test Drafter in parallel — perf tests
   for the aggregator and visual regression captures of the stress
   tower combat are the pieces most worth testing alongside.

---

*Plan written: 2026-05-08. Update this file when phases land — it's
the source of truth for the feature, not a write-once doc.*
