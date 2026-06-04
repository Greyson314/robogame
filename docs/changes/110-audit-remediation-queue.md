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

1. ~~**Remaining safe quick wins**~~ — **DONE** commit `7323af0` (session 111):
   - **#18** NameplateOverlay name memoized in a `List` parallel to `_robots`.
   - **#27** AudioRouter sweeps dead loop handles in `Update`.
   - **#19** BuoyancyController exposes a parallel `List` (`IReadOnlyList`);
     WaterMeshAnimator iterates by index (no per-vertex enumerator boxing).

2. ~~**#1 CRITICAL — CSP replay double-step.**~~ — **DONE** commit `02d2b9a5`
   (session [111](111-prediction-scene-csp.md)). ADR-0002 Accepted; isolated
   prediction PhysicsScene + colliderless mirror, drive subsystems redirected
   onto it (`SetForceTarget`). The planned force/torque transfer was abandoned
   (`GetAccumulatedTorque` drops `AddForceAtPosition` torque) — caught by the new
   `PredictionMirrorTest`. PlayMode green (113/114). `perf-checker` deferred:
   the mirror is created only on a networked owner-client (`IsOwner && !IsServer`),
   never in SP play, so the profileable SP path adds zero physics objects.

3. ~~**Doc-drift sweep** (#11/#12/#43/#44/#48/#50)~~ — **DONE** commit `ac87cfd`
   (physics.md §2, build-menu strings, DigZone/DigChunk remarks, cross-refs,
   terraforming LOD seam) + NetworkSceneFlow status note in netcode.md §10
   (kept, not deleted — it's intentional infra).

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
