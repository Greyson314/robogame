# 53 — Rope follow-ups: tip-face direction, hologram length, chain collider

> Three follow-up bugs to session 52's rope redesign. The new chain
> direction works, but the rule for "what face is the rope's tip" was
> stale, the hologram's segment-length constant didn't match the
> runtime tweakable, and the chain's static cylinder had no collider so
> the player couldn't aim at it.

## What changed

### Rope tip-face direction flipped

[`BlockConnectivity`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)'s
rope rule still said "tip face is `-up`" — that was correct under the
pre-session-52 chain direction (chain extended toward chassis), but
wrong now that the chain extends OUTWARD from the chassis face.
Updated to `placementUp == up` (same shape as the rotor's spin-axis
exception). Both `IsConnectiveFace` (validator-side) and
`AcceptsPlacement` (runtime, with grid + placement def). User's "1
block down from the top of the rope rejects with leaf error" hit this
exactly.

### Hologram length now matches the placed rope

[`BlockGhostFactory.BuildRope`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
read a hardcoded `defaultSegLen = 0.4f`. The actual runtime tweakable
default is `0.5f` (per `Tweakables.cs:345`), so the hologram came out
80% of the placed rope's length. Now reads the live tweakable
(`Tweakables.RopeSegmentLength` / `Tweakables.RopeSegmentRadius`)
with the same `Mathf.Max` guards as `RopeBlock.LiveSegmentLength`.

### Static chain has a collider

[`BuildStaticVisual`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
was destroying the cylinder's `CapsuleCollider` immediately after
`CreatePrimitive`. With the host cube hidden (per session 52), the
chain had no clickable surface — the player couldn't aim at it for
placement targeting, removal, or even getting a status message. Keep
the collider; targeting hits resolve to the rope's `BlockBehaviour`
via `GetComponentInParent` (same path the host cube uses).

The arena's live-mode multi-segment cylinders still destroy their
colliders — only the static (build-mode) single cylinder gets one,
because that's the placement-targeting surface.

## What this enables (per the user's report)

1. Place a rope on a chassis cube's -Y face → chain hangs DOWN
   (session 52's fix), and now the player can aim at the chain's
   bottom cap (free end) → place a hook at the cell beyond the free
   end.
2. The hologram's length matches the placed rope's length 1:1.
3. Hovering the chain shows a status message (target cell + face) so
   the player can debug placement attempts.

## Files

`BlockConnectivity.cs`, `BlockGhostFactory.cs`, `RopeBlock.cs`. No
new tests (the rule + collider changes are covered transitively by
play-test on the rope-on-bottom-of-chassis scenario; the hologram
length match is a one-line constant fix).

## Verification

1. From-scratch chassis → place rope on cube's -Y face → chain hangs
   down. Aim at the chain's bottom end (free end) → place a hook.
   Should accept (no more "host is leaf" rejection).
2. Open variant panel → drag rope segment-count slider to e.g. 4 →
   look at the hologram. It should be 4 × 0.5 = 2 cells long. Place
   the rope. Placed cylinder should be the same length.
3. Hover the chain in build mode → bottom-right HUD should show the
   target cell + reason (e.g. "Host is leaf at (0,-1,0)" if you're
   aiming at the chain's side, or accept if aiming at the cap).
