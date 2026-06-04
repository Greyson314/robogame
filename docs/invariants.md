# Invariants

> **Tier 1.** Hard rules. Always current. Violating one of these is a
> regression, not a stylistic choice. If you think a rule needs to
> change, open an ADR — do not silently work around it.
>
> This file is the canonical source. Subsystem docs may restate a rule
> in context; if they conflict with this file, this file wins.

---

## 1. No `Tweakable` affects gameplay outcomes

Tweakables are per-machine and persisted to local JSON. The moment a
Tweakable drives damage, lift, hit detection, or anything observable to
another player, the game desyncs the second netcode lands. Gameplay
config lives on the chassis blueprint or a server-owned SO, never on
`Tweakables`.

Status as of session 85: fully enforced — `Tweakables` retains only
`Stress.*`, `Water.*`, and rope-feel knobs (dev / arena-canonical /
presentation). See [subsystems/physics.md § 1.5](subsystems/physics.md)
and [subsystems/netcode.md § 6](subsystems/netcode.md).

## 2. Building happens only in the garage

Blueprints are frozen at match start. The only mid-arena mutation is
*removal* (damage). The block-index ordering — sorted by
`Vector3Int` — is part of the netcode contract; it must be stable
across spawn so client and server arrive at the same `(cell →
blockIndex)` mapping. See [subsystems/netcode.md § 6 Bucket B](subsystems/netcode.md).

## 3. Server is authoritative for all gameplay state

Even in singleplayer, structure code as if the server is a separate
process. `NetworkContext` is offline-authoritative by default — with no
NGO session registered, `IsServer` and `IsClient` are both true and
`IsOnline` is false, so the same code path runs in single- and
multiplayer. Gate the *singleplayer-only / local* path on `!IsOnline`,
**not** on `IsServer` (an online host is also a server). Session 86
covers the reasoning.

## 4. Single Rigidbody per chassis

Free-body children of a moving Rigidbody fight the solver. If a
feature needs a free body, parent it under scene root, not under the
chassis. Compound colliders on the chassis root are the supported
pattern. See [subsystems/physics.md § 1](subsystems/physics.md).

**Carve-out (ADR-0002).** The CSP prediction mirror is the one
sanctioned second Rigidbody for a chassis: a colliderless, renderless
body in the owner client's `LocalPhysicsMode.Physics3D` prediction scene,
used only to re-simulate replay in isolation. It carries no gameplay
authority, is never networked, exists only on the owner client, and is
destroyed on despawn. Scope is strictly one mirror per prediction scene —
not a general licence for extra chassis bodies in the main scene. See
[decisions/0002](decisions/0002-prediction-scene-second-rigidbody.md).

## 5. Default to zero baseline cost

Every new physics block must have a configuration that adds zero
Rigidbodies and zero colliders. Anything heavier is opt-in via
per-chassis blueprint config or a debug tweakable. The `RotorBlock`
"ropes = 0" path is the established pattern.

## 6. No per-frame allocations

No `new` in `Update` / `FixedUpdate` / `OnCollision*`. Pre-size lists
at build time and reuse them. Budgets in
[best-practices.md § 16](best-practices.md).

## 7. Profile before claiming a perf characteristic

"Well under budget" without a Profiler capture or a static count from
a real measurement is not acceptable. The `perf-checker` subagent and
the idle-baseline harness exist to make this cheap; use them. See
[subsystems/performance.md](subsystems/performance.md) and
[subsystems/performance-pass.md](subsystems/performance-pass.md).

## 8. Every new feature ships with VFX + audio

As of session 30 the project has both pipelines wired (`VfxSpawner` +
procedural particle kinds; `AudioRouter` + `AudioCue` +
`AudioCueLibrary`). New gameplay systems — weapons, blocks, movement
modes, match-state events, UI — must include a good-faith pass at
both. If the clip or cue doesn't exist yet, declare the cue, leave the
library entry blank, and call `AudioRouter.PlayOneShot` at the
gameplay site anyway: the missing-cue logger surfaces it. Same for
VFX: pick the closest `VfxKind`, hook the call site, tune scale later.
**Do not ship a feature with both audio AND VFX deferred to "later".**
See [subsystems/audio.md](subsystems/audio.md) and session 29 for the
VFX kinds.

## 9. Terraforming is dig-only

Once a voxel is removed it stays removed for the lifetime of the
match. No additive brush, no regeneration, no Minecraft-style block
placement in-arena. This is the load-bearing simplification that lets
the netcode for terrain stay a thin op-log without a full sparse
voxel sync. See
[subsystems/terraforming.md § 2](subsystems/terraforming.md).

## 10. Triangle and chunk budgets for voxel terrain are hard ceilings

Per-chunk triangle count and the multi-chunk rebuild fan-out have
defined ceilings. Crossing them is the trigger for chunking work or
LOD work — not a "ship it and hope." See
[subsystems/terraforming.md § 7](subsystems/terraforming.md).

---

## How to change one of these

These rules exist because each one was either expensive to learn
(known failure modes — see CLAUDE.md), or a load-bearing simplification
that other systems are built around. Don't bypass them silently.

1. Write an ADR in `decisions/` proposing the change, citing the new
   constraint or finding.
2. Get explicit user approval before merging the ADR.
3. Once accepted, the ADR supersedes the rule here; update this file
   and link the ADR.

The discipline isn't "rules can't change." It's "rule changes are
visible, dated, and reviewable."
