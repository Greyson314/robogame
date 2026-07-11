# 141 — Concoction full pass: colour chemistry, Laboratory bench, ammo-weapon scope

**Intent.** User-directed autonomous pass: concoctions grow speed +
spread levers, a deterministic colour identity that names them and dyes
their shots, a proper two-pane Laboratory, and selection on SMG/Cannon
alongside Bomb/Mortar. Decisions recorded in
[ADR-0005](../decisions/0005-concoction-scope-color-naming.md)
(**Proposed** — implemented on user-granted autonomy, awaiting review).

## What shipped

- **`ConcoctionColor` (new, Block):** weighted circular hue mixing
  (above-neutral slider parts pour pigment; dominance → saturation,
  total level → darkness), 12-band pigment name table, colour-derived
  default names ("Dark Madder Concoction", "Murky Orchid Concoction",
  sludge special "Black Bile"). Pure + EditMode-tested.
- **`Concoction` v2:** SpeedPct/SpreadPct (piecewise multiplier curve
  shared), `MixedColor`, surcharge factor 0.3/5-levers (anchors
  preserved: neutral +75%, max +150%). Serializer v2 with the
  JsonUtility zero-fill guard for v1 files.
- **Scope widening:** `IsConcoctableBlock` += SMG/Cannon; `CpuBudget`
  stacks ammo-multiplier + concoction surcharges (surcharge on BASE
  cost). Variant panel runs a combined layout for SMG/Cannon (ammo
  slider above, concoction chooser below; title fix so SMG stops
  reading "Bomb bay"), pigment chips on the caption + option rows,
  spd/spr in the readout.
- **Fire-time:** SMG (dmg/speed/spread/kb), Cannon (dmg/size→calibre/
  speed/kb), Mortar (speed via the muzzle-speed RESOLVER so the arc
  preview matches), BombBay (speed on drop). All four dye projectile +
  impact FX with the mixed pigment (`ProjectileSpec.TintImpact`;
  `VfxSpawner` grew tinted overloads that re-stamp pooled startColor
  every spawn — template colour cached per pool).
- **Laboratory rebuild (`LabController`):** journal pane (search filter,
  pigment-chip jar rows, two-click delete) + bench pane (five vial
  sliders each tinted with their reagent pigment, cauldron blob easing
  toward the live mix with two counter-rotating marbling blobs, wax-seal
  swatch by the name field, auto-name that chases the mix until the
  player types, "Bottle it" save with a pulse + LabSave cue). InkKit
  paper/blob/seal sprites; ink-on-paper discipline kept — mad-scientist
  tone carried by copy, motion and the pigment itself, not neon.

## Verification

EditMode tests: serializer v1→v2 back-compat, surcharge anchors,
colour determinism, name boundaries (test drafter hand-verified the
all-max case → "Dark Amethyst Concoction", dominance ≈0.43), CpuBudget
stacking, predicate. Full suite + live editor checks logged below.

## Known limits / next steps

- **On-hit effects deliberately NOT here** — separate "Rider Effects"
  feature (discrete Burn/Smoke/EMP + fleck accents), needs its own plan
  (status-effect subsystem, netcode state). This was the biggest scope
  cut against the user's lever list; both research passes recommended it.
- Kill-feed concoction chip needs damage-source attribution — deferred.
- Carrier-block tinted viewport (read the loadout off the bot) — deferred.
- Colorblind silhouette variants per dominant lever (spark/mist/ring) —
  deferred with the rider work.
- Palette-lock carve-out must be copied into art-direction.md when the
  12-token lock un-suspends (ADR-0005 §7).
- CFXR bomb-explosion prefab (Instantiate path) is not tinted; only the
  procedural shockwave + sparks are.
