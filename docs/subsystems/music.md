# Robogame — Combat Music & Musical Damage Feedback

> **Status.** v1 shipped (session 144, ADR-0006). Backing track +
> beat-quantised instrument stingers live in arenas; clips are
> generated placeholders awaiting a real audio pass.

## What this is

The slapstick damage-feedback layer: a rolling war-drum backing track
in arenas, and instrument stingers when projectile hits land — each
weapon voiced by an instrument, quantised to the track's beat grid,
consonant by construction. Sea of Thieves' trick, Unity-native.
Rationale and alternatives: [ADR-0006](../decisions/0006-musical-damage-feedback.md).

## Architecture

- **`MusicConductor`** (Core) — owns the backing track. Starts it via
  `PlayScheduled` so the grid is `startDsp + n × (60/BPM)` arithmetic
  on `AudioSettings.dspTime`. `NextSlotDsp(subdivision, offset, lead)`
  answers quantise queries; off-beat 8th = `(1, 0.5, …)`. Stops itself
  on scene unload. Track metadata: `MusicTrackDefinition` at
  `Resources/Music/CombatTrack.asset`.
- **`MusicalHits`** (Combat) — static fan-in, sibling of
  `DamageAttribution` but carrying `ProjectileKind`. Reported from
  `ProjectileWorld`'s three damage paths (direct / ring / area).
- **`MusicalHitDirector`** (Gameplay, on the arena camera, bound by
  `ArenaController` with `LookupSide`) — accumulates hits per
  instrument per quantise window, flushes ONE stinger per window via
  `AudioRouter.PlayScheduled`. Tiers by window damage
  (`MusicMath.TierFor`): note → flourish → phrase (kill, on-beat).
  Outgoing hits walk the global pentatonic; incoming hits play the
  same instrument an octave down (register separates teams, not key).
- **`MusicMath`** (Core, pure) — grid + tier math, covered by
  `MusicMathTests`.

Instrument map (in `MusicalHitDirector.InstrumentFor`): SMG → pluck,
cannon → brass, mortar → piano, bomb → timpani.

## Authoring contract

- Backing tracks are rendered at **exactly** `bars × beatsPerBar ×
  (60/BPM)` seconds — a sloppy loop seam breaks the grid.
- All pitched material (track drone + stinger clips) shares one root
  (currently D). Stinger note clips are recorded at the root;
  `MusicalSfx`'s pentatonic multipliers stay within one octave.
  Flourish/phrase clips are baked runs in key and play at pitch 1.
- Stinger cue rows live in `AudioCueWizard.s_rows` (Music bus, 2D,
  jitter 0 — the director owns pitch). `Assets/`-rooted paths bypass
  the USFX root.

## Regenerating the placeholder assets

Clips are synthesised offline (audio.md forbids *runtime* synthesis;
authored clips are authored, however they were born):
[`artgen/gen_music_assets.py`](../../artgen/gen_music_assets.py)
(python + numpy) writes WAVs into `Assets/_Project/Audio/Generated/`,
then **Robogame → Scaffold → Music → Build Combat Music** wires the
track asset and cue rows. Replacing placeholders with real recordings
is a pure asset swap: drop in same-named files (or edit the wizard
rows), respect the root-note contract, rebuild.

## Performance

Steady state allocates nothing: fixed per-instrument buckets, pooled
router voices, no LINQ/strings on the hit path. The director's
`Update` is two 4-element array scans. The conductor is one looping
`AudioSource`. Everything is client-side cosmetic (INV-3 safe);
quantisation delays presentation, never damage.

## Known gaps / next steps

- Placeholder timbre: synthesised approximations, not orchestral
  samples. Swap path above; FMOD is the sanctioned escalation if
  authored-per-key assets become the bottleneck (ADR-0006).
- Tip weapons (hook/mace), rams and drills don't report musical hits
  yet — `MusicalHits.Report` at their damage sites when wanted.
- Kill "wahoo!" cartoon animation hook (ADR-0006 Notes) — not built.
- Track intensity layers / garage theme — out of scope for v1.
