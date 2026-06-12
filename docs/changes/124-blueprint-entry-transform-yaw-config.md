# Session 124 — Close the `BlueprintEntryTransform.Apply` field-drop gap

## Intent

Session 123 flagged a pre-existing bug as a known unknown: `Apply`
rebuilds each `ChassisBlueprint.Entry` through the 5-arg ctor, so
`BlockConfig`, `ConcoctionId`, and `Yaw` silently defaulted to `0`/`""`/`0`
on the transformed entry. A mirrored thruster lost its tuned thrust, a
mirrored bomb its concoction, a mirrored yawed block its yaw — the exact
field-drop class `IBlueprintEntryTransform` exists to make impossible. It
had drifted across schema v4 (BlockConfig), v7 (ConcoctionId), and v8
(Yaw). This session routes all three through the interface.

## What changed

- **`IBlueprintEntryTransform`** gains `TransformBlockConfig`,
  `TransformConcoctionId`, `TransformYaw`. `Apply` now assigns them after
  the ctor (same shape as the session-123 `Teeter` fix) and the KNOWN GAP
  comment is gone.
- **`MirrorTransform`** implements the three. `BlockConfig` and
  `ConcoctionId` are orientation-free → straight copies. `Yaw` delegates
  to the new `BlockMirror.MirrorYaw`.
- **`BlockMirror.MirrorYaw(yawDeg, sourceUp, axis)`** — the real rule.

## The yaw reflection rule

Yaw is *not* pitch's parity rule. Pitch/teeter are angles about a mount
axis the mirror itself reflects, so they negate iff the up flips. Yaw is a
rotation *about* up, layered on `OrientationFromUp`, so its reflection has
two parts:

1. **Sense flip.** A reflection negates a rotation about any axis: for an
   improper transform `M`, `M·R(n,θ)·M⁻¹ = R(M·n, −θ)`. Raw reflected yaw
   is `−yaw` about the mirrored up.
2. **Base-forward offset.** Yaw is measured relative to the deterministic
   forward `OrientationFromUp` picks — the up's seed (chassis `+Z`, or
   `+X` when up∥Z) projected ⊥ up. When the mirror flips that seed axis the
   reconstructed base forward points the opposite way → a 180° offset.

Combined: `yaw' = baseOffset − yaw`, normalised to 0/90/180/270. The seed
axis is `+Z` for ordinary mounts and `+X` for polar (±Z) mounts; an
X-mirror flips `+X`, a Z-mirror flips `+Z`. So:

| mount up | X-mirror | Z-mirror |
|---|---|---|
| ±X, ±Y (non-polar) | `−yaw` (offset 0) | `180 − yaw` (offset 180) |
| ±Z (polar) | `180 − yaw` (offset 180) | `−yaw` (offset 0) |

Worked check (side thruster, up=+X, yaw=90, X-mirror): source forward
points −Y; its mirror image still points −Y. Rule gives yaw'=270, and
`OrientationFromUp(−X, 270)·forward = −Y`. ✓ The naive "preserve when up
flips" rule would give yaw'=90 → +Y, wrong.

## Preset impact — none

Shipped presets author through `ScriptedChassisBuilder`, whose mirror runs
`BuildSession.TryPlace` with `MirrorEnabled` (the session path), not
`Apply`. `ScriptedChassisBuilder.Place` tops out at
`(blockId, cell, up, dims, worldPitch)` — there is no way to set yaw,
config, or concoction inside a `MirrorX/Z` block. So no preset mirrors a
configured/yawed/concocted block, and every preset layout is byte-identical
regardless of the rule. The only `Apply` caller is `BlueprintBuilder.Mirror`
(the pure-data builder used by validator/snapshot tests); this fix closes
that path and future-proofs the contract.

## Out of scope (noted, not fixed)

- The **live build-mode mirror** is a separate path: `BlockGhostRenderer`
  poses the mirror ghost with `req.Yaw` unchanged, and `BuildSession`'s
  session-mirror has its own field handling. Whether the player's live
  mirror reflects yaw correctly is a distinct question from the `Apply`
  contract and wasn't touched here.

## Tests

6 new `BlueprintEntryTransformTests`: config+concoction carry through
`Apply`; yaw carries through `Apply`; and four `MirrorYaw` cases pinning
the table above (non-polar/polar × X/Z). EditMode 341/342, PlayMode
114/115 — 0 failures (the 1 EditMode inconclusive + 1 PlayMode `[Ignore]`
are the documented pre-existing baseline).
