# 110 — Audit remediation queue (carry-over)

> Status: **In progress — handoff to a fresh session.** The full-app audit
> ([109](109-full-app-code-review.md)) is done; the user approved acting on
> findings + a new "Continual Traces" concept. Five fixes shipped this session;
> the rest is queued below, prioritized. Start here.

## Done (committed this session)

Five audit fixes — commit `7c2bf80f`:

- **#2 HIGH** `Robot.HandleBlockRemoving` now deducts `EffectiveMass`, not raw
  `Definition.Mass` (mass/health-tier drift on scaled aero/hover).
- **#6 HIGH** `VfxSpawner.ResetStatics` destroys its two owned materials (kept
  the borrowed cube sharedMesh).
- **#33/#34/#35** VariantConfigPanel Entered-leak; BuildSession
  `RotorsGenerateLift` stale-true; splash hit-marker firing on no-damage.

## Queue (prioritized — do in this order)

1. **Remaining safe quick wins** (low-risk, self-contained):
   - **#18** `NameplateOverlay.FormatName` allocates per OnGUI — memoize the
     display name per-Robot in `RefreshRobotList` (sibling `ScrapCarriedIndicator`
     already does this).
   - **#27** `AudioRouter._loops` never sweeps caller-leaked handles (a block
     destroyed without `Stop()` leaks forever) — sweep dead `_source` in
     `Update`, mirroring the existing one-shot voice sweep.
   - **#19** WaterArena `Update` boxes a HashSet enumerator
     (`WaterMeshAnimator.cs:164`, `BuoyancyController.cs:48`) — expose `Active`
     as the concrete `HashSet`/a parallel `List`. (Lowest value; water arena only.)

2. **#1 CRITICAL — CSP replay double-step.** `Network/Robot/NetworkRobotMovement.cs:226`
   calls global `Physics.Simulate(dt)` per replay tick, advancing every Rigidbody.
   Replace with hand-integration of the local-player Rigidbody only; until done,
   mark replay loopback-only in `netcode.md §8`. **User is a netcode beginner —
   write the glue, explain the why, don't hand them plumbing.**

3. **Doc-drift sweep** (#11/#12/#43/#44/#48/#50): rewrite `physics.md §2` (rope is
   Verlet now, not joint chains; RotorBlock has no joint chain); decide
   `NetworkSceneFlow`'s fate (promote to the single flow owner or delete the dead
   class); fix the six retired-build-menu error strings and the stale
   `PHYSICS_PLAN`/§ cross-refs.

4. **Weapon-fork refactor — PLAN FIRST, get sign-off** (#3/#4/#8/#15/#25). The
   biggest debt: `IWeaponStats`/`abstract WeaponStatsDefinition` shared base; a
   single weapon-kind registry replacing the 7+ hand-synced lists
   (`RobotWeaponBinder`, `WeaponAmmoState.IsWeaponBlock`, `BlockConnectivity`,
   `NetworkRobotCombat` client-silence, `BlockDefinitionWizard`); extract a
   `TurretYoke` aim stepper (kills the duplicated `Vector3.up` spherical bug) and
   a `WeaponFireGate`; one `Teams.AreHostile` + `ProjectileGravity.ForMuzzle`
   predicate. Spans Combat + Network → write an ADR in `docs/decisions/`, get
   approval before code (architectural change — invariant/ADR discipline).

5. **"Continual Traces"** — NEW user concept (see memory `concept-continual-traces`):
   durable code **breadcrumb markers** linking code → decision/finding/rationale,
   discoverable over time. Design deliberately: marker syntax (e.g. `// TRACE[id]:`),
   what the id links to (ADRs / 109 findings / session logs), optional
   indexer/validator (editor menu or hook, like the existing scaffolders).
   **Confirm the syntax with the user before building** — it's a convention that
   will spread repo-wide.

## Notes

- Findings #9 (carve server-authority gate) and #31 (per-weapon cooldown) are
  documented Phase-6 netcode debt — fold their one-line "client-predicted; server
  applies Phase 6" markers into step 4's pass, don't chase separately.
- Full ranked finding list + file:line in [109](109-full-app-code-review.md).
