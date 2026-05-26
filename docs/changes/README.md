# Robogame — dev log

This directory is the catch-up brief for any future contributor (human
or AI) landing on the project mid-stream.

- Read [architecture.md](architecture.md) first — that's what's true
  *right now*.
- Then skim sessions in **reverse** chronological order (highest number
  first) for the *why* behind recent shape changes.

Style: dev log, not changelog. Each session entry covers user intent,
what shipped, what we learned. File links use repo-relative paths.

## Recent batch — what landed since session 44

**Building architecture refactor + rotor/rope playtest pass.**
Two-day arc covered in [session 54](54-session-wrap.md):

- Sessions 45–46: every step from
  [BUILDING_ARCHITECTURE_REVIEW.md](../BUILDING_ARCHITECTURE_REVIEW.md)
  §4. Major modules: `BlockEntries` (canonical sort enforced),
  `BlockGraph` (one BFS primitive), `PlacementRules` (editor +
  validator share rules), `IBlueprintEntryTransform` (compile-time
  guard against silently-dropped Entry fields), `BuildSession`
  (plain-C# build-mode model), `BlockGhostRenderer` +
  `PlacementFeedbackHud` (extracted from BlockEditor),
  `ChassisAssembler` (unified Build/BuildTarget + ChassisHandle).
- Sessions 47–51: rotor + foil pass — auto-companion mechanism
  cube, spin-axis-only connective face, world-intent pitch
  (`BlockOrientation`), `ComputeWingShift` rotor mode fix,
  rope adoption by rotor.
- Sessions 52–53: rope redesign — chain extends OUTWARD from
  chassis face (not toward), host cube always hidden, hologram
  = full chain length, static cylinder collider preserved so
  the chain itself is the placement target.

## Older batch — what landed sessions 17–21

**Helicopter chassis is the headline.** Sessions 17–21 fix the
"helicopter frame spins with the rotor" bug end-to-end:

- Session 17: rotor / aerofoil decoupling — adopt-don't-synthesise.
- Session 18: garage gate (foils stay under the chassis grid root in
  the garage); `Aero.WingSpan/Chord/Thickness` tweakables for live
  foil resizing.
- Session 19: blueprint authoring overhaul + the rotor visual now
  reads as a 2-cell-tall stem + mechanism. Bigger helicopter (38
  cells, two side guns, foils as the absolute topmost cells). Hook
  + Mace tip blocks for ropes (PHYSICS_PLAN §3 contact damage).
  Barbell test dummy spawned in the default arena.
- Session 20: plane simplified to a single 8-segment rope with a
  hook for hot-testing the rope-tip damage path.
- Session 21: two coupled fixes for the helicopter spin-out — lift
  forced coplanar with the spin axis, and foil-vs-chassis colliders
  ignore-paired so the foil cubes don't impulse-yaw the chassis as
  they sweep through the mechanism cube's volume.

**New authoring infrastructure** (session 19, phase 1):

- [`BlueprintBuilder`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
  fluent API: `Block`, `Row`, `Box`, `MirrorX/Z`, `RotorWithFoils`,
  `RotorBare`, `RopeWithHook`, `RopeWithMace`. Replaces the old
  `entries.Add(new ChassisBlueprint.Entry(...))` boilerplate.
- [`BlueprintValidator`](../../Assets/_Project/Scripts/Block/BlueprintValidator.cs)
  catches no-CPU / duplicate-cell / orphan / unknown-id errors at
  scaffold time. Wired into `GameplayScaffolder.CreateOrUpdateBlueprint`.
- [`BlueprintAsciiDump`](../../Assets/_Project/Scripts/Block/BlueprintAsciiDump.cs)
  prints chassis layouts one Y-layer at a time. Run
  `PresetBlueprintTests.DumpAllPresets_WritesAsciiSnapshot` (EditMode)
  to regenerate [docs/blueprint-snapshots/presets.md](../blueprint-snapshots/presets.md).

**New self-tests** (session 19 + 21):

- `Tests/EditMode/Blueprints/` — unit tests for the builder,
  validator, and every preset (including the new helicopter +
  barbell). Auto-writes the snapshot file.
- `Tests/PlayMode/Movement/RotorBlockTests.cs` —
  `RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad` exercises
  both the lift-direction and collider-sweep yaw paths and asserts
  chassis yaw stays under 1 rad/s after 30 fixed steps at 240 RPM.

**Carry-forward / open threads** are listed in the "Known unknowns
going forward" section at the bottom of this file.

## Sessions (newest first)

| # | Title |
|---|---|
| 96 | [Slice C — five juice polish wins. C1 live mid-edit collective pitch propagation: BuildSession.VariantChanged drives BlockEditor's grid-walk propagation to placed blocks via SetPitch/SetDims (closes foil-rotation-plan §10A — foil mesh tilts live, no more "re-place to apply"); RotorBlock subscribes to its own PitchChanged and forwards collective to every adopted foil. C2 crosshair + ammo state: AimReticle resolves the chassis WeaponAmmoState and tints the crosshair toward a desaturated _reloadColor when no pool can fire (blended 70% over the base/enemy tint so both signals coexist); small total-loaded ammo count below the crosshair. C3 damage-number clustering + combo pop: FloatingDamageOverlay tracks placed rects per OnGUI event and shoves overlapping new ones horizontally by _clusterThresholdPixels (default 60 px); one-shot scale-pop (1.4× → 1.0× over 0.35 s) when cumulative damage on an accumulator crosses _comboThreshold (default 100). C4 low-health vignette + audio: new LowHealthVignetteHud auto-added in ArenaController; four edge bands with sliced falloff, alpha scales with (threshold - HP)/threshold and a 0.85–1.0 sine pulse; new AudioCue.LowHealthAlert pings via AudioRouter.PlayUI on the leading edge and every _audioInterval (clip not yet authored per invariant #8). C5 scrap-pickup magnetic-pull trail: new VfxKind.MagnetTrail with a 3-particle palette-locked recipe; ScrapPickup.Update emits at 0.15 s cadence while being pulled toward a chassis (reuses pooled spawner, capped by MaxConcurrentPerKind=24). Slice B (rope tip at chain end) was already shipped in session 55 — confirmed by code audit; stale carry-forward removed from this README. EditMode 252/253, PlayMode 92/93 — green throughout](96-slice-c-juice-polish-wins.md) |
| 95 | [Slice A — netcode hygiene & test debt. Cleared all 6 baseline test failures (2 EditMode + 4 PlayMode). BlueprintBlobTests.ContentHash_StableAcrossReserialize dropped its obsolete sanity-check (DateTime.UtcNow's ~15 ms resolution on Windows means two adjacent calls can produce identical "o" strings, tripping the meta-assertion — primary h1==h2 invariant works correctly). NetworkContextTests.RegisterNull wrapped Register(null) with LogAssert.Expect for the contractual LogError. RotorBlockTests world-position tolerance relaxed 5 cm → 20 cm (RotorBlock places blades on a ring slightly outside cell centre per blade geometry — 12 cm steady-state shift is correct; original 5 cm tolerance was never realistic). Three DigZone tests (OnEnable_RegistersWithDigField + DrillBlock auto-poll + TerrainCratering bomb) shared a stale-registry root cause — added DigField.ResetForTesting() test-seam called from new [SetUp] in DigZoneTests. Phase 4 late-join activation wired: NetworkSceneFlow.HandleSceneEvent now handles SceneEventType.SynchronizeComplete and replays DestroyedBlockLog via NetworkBlockGrid.ServerSendDestructionLogTo for every non-host client sync (v1 lobbies still lock at round start per §10; the wiring means v2 mid-match join is a lobby-config flip). New ServerClientSynced event surfaced for future use. EditMode 252/253, PlayMode 92/93 — green (1 each remaining is documented [Ignore] / Inconclusive). MinimalArena.unity scaffold deferred — the test it unblocks is already [Ignore]'d with documented blocker, and hand-crafting a valid .unity asset without the editor is brittle](95-slice-a-netcode-hygiene-and-test-debt.md) |
| 94 | [Netcode Phase 6 — dedicated-server plumbing + lag-comp telemetry. NetworkBootstrap.StartServer pure-server entry point + CLI args (-server / -port / -lobbyId, auto-start only in batch mode). Headless Linux build target documented (Server Build flag + IL2CPP). NetDevHud F8. LagCompHistory per-robot 25-entry ring buffer (500ms at 50Hz) + LagCompRegistry static keyed by NetworkObjectId. NetworkRobotCombat samples chassis pose every server FixedUpdate and runs ray-vs-sphere intersection against history at FireCommand.Tick for every robot except shooter. Lag-comp deliberately telemetry-only — applying damage would turn slow projectile weapons (SMG 80 m/s, cannon, bomb) into hitscan-at-fire-time and destroy the leadable/dodgeable gameplay; ProjectileWorld's live sweep stays authoritative, lag-comp hits LOG only (LagCompTelemetryHitCount counter). Flipping to authoritative is one method if a hitscan weapon type ever ships. 6 new EditMode tests for ring buffer correctness. Multiplay/Hathora deployment + Steam auth deferred (user-only / Phase 5 dependency)](94-netcode-phase6-dedicated-server-and-lagcomp-telemetry.md) |
| 93 | [Netcode Phase 4 — block-destruction hardening. Robot.OrphansDetached event raised at end of RunConnectivityNextFrame with pre-detach positions; NetworkBlockGrid on the server maps positions to canonical blueprint indices and broadcasts OrphanBatchClientRpc so the server's BFS wins any tie-break against the client's local computation. BlockHitBatchClientRpc gains a monotonic uint seq with client-side dedup against duplicate delivery (NGO reliable is ordered; the seq makes the protection explicit). DestroyedBlockLog (server-side 512-entry capacity with overflow warning) records every destruction since spawn; ServerSendDestructionLogTo is reserved for v2 mid-match join but not yet wired into scene lifecycle (v1 locks lobbies at round start per §10). NetworkRobotCombat now samples real aim from RobotDrive.AimPoint and rejects FireCommands with aim deltas > 90° per accepted command (catches teleporting-aim hacks; Phase 6 lag-comp can tighten with aim-at-time-T data). RejectedFireCount aggregates cooldown + aim rejections. 7 new EditMode tests; network-level stress test deferred to MPPM 4v4 qualitative playtest per §15 exit criterion](93-netcode-phase4-block-destruction-hardening.md) |
| 92 | [Netcode Phase 3.6 — latency-injection HUD + determinism guard. Added com.unity.multiplayer.tools 2.2.8 to the manifest; new NetcodeFakeLatencyController (editor + dev-build only) wraps the package's NetworkSimulator with a 4-preset matrix matching §16's test rows (LAN / 100 ms RTT / 200 ms RTT / 200 ms+jitter+5% loss). NetDevHud F5 cycles, IMGUI shows active preset. New PlayMode determinism test (PredictionDeterminismTest.Replay_IdenticalInput_DriftsLessThanBudget) — stepped physics, identical input twice, asserts < 0.5 m/s drift; catches future non-determinism leaking into RobotDrive / NetworkInputSource. Visual mesh-offset ReconciliationSmoother still deferred — implementing it requires a block-prefab refactor and Rigidbody interpolation already hides the within-FixedUpdate snap; build only if MPPM under §16 matrix shows a jarring snap. Gotcha logged for next package add: Unity package manager local server can stop responding; manifest edit + Alt-Tab usually fixes; batch-mode test-rig may need the resolved cache hand-copied if the main editor holds a lock](92-netcode-phase3-6-latency-hud-and-determinism-guard.md) |
| 91 | [Netcode Phase 3.5 — full Fiedler CSP: NetworkInputSource extended into a replay-aware delegating bridge (delegates to live PlayerInputHandler outside replay; pinned to historical InputCommand during replay). NetworkRobot now adds NetworkInputSource on owner builds too (before ChassisAssembler.Assemble) and BindLive's PlayerInputHandler after. ClientCommandBuffer 128-slot ring re-added. NetworkRobotMovement.ReconcileAndReplay snaps Rigidbody to authoritative state then replays each unacked command via RobotDrive.ApplyMovement + Physics.Simulate(fixedDt), capped at 64 replay ticks. 25 Hz snapshot rate, redundant triple of (current, prev, prev-prev) commands per FixedUpdate. Latency-injection HUD deferred (UTP SetDebugSimulatorParameters is [Obsolete] in 2.x; Network Simulator package not in manifest) + mesh-offset smoother deferred (Rigidbody interpolation should hide the within-FixedUpdate snap+replay)](91-netcode-phase3-5-full-fiedler.md) |
| 90 | [Netcode Phase 1 close-out + Phase 3 lite: shipped the deferred FireCommand ServerRpc + cosmetic ProjectileSpawnEvent tracer (new ProjectileWorld.Spawned event, owner-sends-at-SMG-rate, server cooldown-validates via FireCooldownTable, observers + owner see muzzle flash + audio + tracer at server-echo latency). Phase 3 cut conservatively: owner Rigidbody dynamic + local NetworkTransform disabled (controls feel local), server snapshot @ 10 Hz hard-snaps owner only on > 1 m drift. Full Fiedler replay + reconciliation smoother + UTP latency HUD split out as Phase 3.5. 9 new EditMode tests passing (FireCooldownTable). Subagent road-test for sessions 88/89 — planner ×2, test-drafter ×1, qa-verifier ×1, perf-checker skipped (zero new physics objects)](90-netcode-phase2-fire-and-phase3-lite.md) |
| 87 | [Phase 1 MPPM loopback milestone: bring-up + debug (NetDevHud hotkeys, host-address fix, late-joiner targeted resend, default port 7777→47777), then the proper NETCODE_PLAN §10 connect-then-load-arena flow (connect from MainMenu, server drives NetworkSceneManager.LoadScene, ArenaController online branch skips local SP spawn, owner camera bound via new Core NetworkPlayerBridge). Handoff IsServer→IsOnline correction (online-host double-spawn). MPPM ×2 loopback confirmed end-to-end: spawn/drive/shoot/damage all replicate. Tagged phase-1-mppm-loopback](87-netcode-mppm-loopback.md) |
| 86 | [Netcode Phase 0 close + Phase 1 NGO loopback baseline: INetworkContext offline-default + BlueprintBlob wire codec/CRC-32 hash; NGO 2.4.0 + NetworkBootstrap/ContentHashGuard/NetDevHud; the 5 Net* siblings (spawn-via-ChassisAssembler, 4-tier health, per-tick BlockHitBatch, stock NetworkTransform+input RPC, server-auth combat); NetworkSceneFlow; 6 ArenaController IsOnline guards. 13 commits, dotnet-green; functional proof = user MPPM 1v1. Handoff-§3.4 IsServer→IsOnline correction (online-host double-spawn); §9 validated-FireCommand/tracer deferred (needs a Combat-tier ProjectileWorld hook)](86-netcode-phase0-phase1.md) |
| 85 | [Pre-netcode deep cleanup + the gameplay-Tweakable migration: dead-code deletion, 2 dead Kenney kits (~27 MB; pattern-pack KEPT — GUID check proved it's wired into block materials), OnGUI GC fixes, real assertions for stubbed netcode-critical tests. Then the headline: all ~22 gameplay-observable Tweakables moved off per-machine JSON onto server-authoritative config (ImpactConfig SO + BlueprintSerializer v4 + per-block Entry.BlockConfig) — hard invariant #1 fully cleared. 12 commits, build-green, untested in-engine](85-pre-netcode-cleanup-and-tweakable-migration.md) |
| 84 | [Performance pass 1: automated idle-baseline PlayMode harness (frame-time percentiles + GC/frame, asserts idle alloc < 2 KB), static triage of all Phase-2 suspects, and two real steady-state OnGUI GC fixes — ObjectiveHud (6 GUIStyle + concat/draw) and ScrapCarriedIndicator (per-event GetComponent + string interp). Phase-5 big rocks deferred as measurement-gated, not blind-landed](84-perf-pass-1.md) |
| 83 | [The whole arena floor is diggable: full-footprint 6×1×6 @ 1 m DigZone (192×32×192 m) seeded per-column to the shared HeightmapField (HillsGround.SampleHeight now delegates to it, so grass mesh + voxel surface align), triplanar Mat_DigZoneEarth dirt on chunks, Fluff stays as a decoupled grass overlay clipped per-column by a global dig-mask (2 Grass.hlsl robogame-mods). Grass mesh loses its collider so voxels are the sole ground. POI chamber moved underground. 12 new tests](83-full-ground-dig.md) |
| 82 | [Phase 7 op-log checkpointing + audio cue pass + drill aim widen: DigZone.Checkpoint(tick) captures SDF in .dig wire format and compacts _opLog using RFC-1982 serial-number arithmetic on the ushort tick (snapshot at 65 530 retains op at tick 5). 4 declared cues (DrillContact, DrillActive, BotDetected, BotStep) wired to USFX clips through AudioCueWizard's row table. Drill aim cone 30°→50° so straight-down/up drilling reads right. 4 new PlayMode tests including the snapshot + replay byte-identity machine gate](82-phase-7-checkpointing-and-audio.md) |
| 81 | [Phase 6 data plumbing: BrushOpCodec (17-byte wire-stable encode/decode), BrushOpValidator (kind / radius / zone-overlap rules), cumulative DigZone.OpLog + ReplayLog for late-join. 9 new tests: encode/decode round-trip, batch + offset safety, malformed-count rejection, all validator rules, bandwidth synthesis (10-min drill trace = 5.52 kbps), commutativity (50 ops in shuffled order converge byte-identical), late-join replay. Transport layer still gated on NETCODE Phase 1–4](81-phase-6-codec-validator-oplog.md) |
| 80 | [Phase 5 chamber + bot cues: DigZone gains a SerializeField list of InitialBrushSpec applied between SDF seed and occupancy build (POI authoring stand-in; real .dig baker is its own session). EnvironmentBuilder configures a single SphereSubtract chamber at world (77,-3,77) r=2.5m and spawns the VoxelChaserBot inside. New AudioCue.BotDetected (no-path → path edge) + BotStep with low-scale DebrisDust VFX every other waypoint. 1 new test for the order-of-operations invariant](80-phase-5-chamber-and-bot-cues.md) |
| 79 | [Tunneling fix (drill tip projects past cell center along transform.up + radius 0.8m → 1.5m so the chassis fits through what gets carved) + Phase 5 visual-playtest gate: VoxelChaserBot uses OccupancyGrid + A* to follow the player, spawns on the in-arena dig zone surface via EnvironmentBuilder. 4 new PlayMode tests (tip offset, chaser path-find, chaser follow, chaser fail-closed on Solid start)](79-tunneling-fix-and-voxel-chaser-bot.md) |
| 78 | [DrillBot preset (3×3 floor + front-mounted drill + 4 wheels) + drill firing gated on FireHeld (held left-click, mirrors weapons) + drill radius bumped 0.5→0.8m + terrain-vs-chassis impact damage suppressed (no more shearing the drill off when carving). 2 PlayMode tests for held/not-held auto-poll + DrillBot added to preset validation list](78-drillbot-preset-held-input-terrain-damage.md) |
| 77 | [Playtest fixes: DrillBlock polls DigField each FixedUpdate so a body-mounted drill carves when inside terrain volume (the contact-only path missed the geometric case). ProjectileWorld scales bomb crater 0.3× (was obliterating the small in-arena dig zone). EnvironmentBuilder disables LOD on the in-arena dig zone (eliminates LOD-mismatch seams between 4 chunks). 1 new PlayMode test](77-playtest-fixes-drill-bomb-arena-lod.md) |
| 76 | [Drill collision forwarder: DrillBlock on a chassis cell now receives contact events via DrillCollisionForwarder on the chassis root (mirrors TipCollisionForwarder pattern). Unblocks actually drilling terrain in arena gameplay — previously the drill's OnCollisionStay never fired because Unity routes physics messages to the Rigidbody host. RobotDrillBinder adds/refreshes the forwarder on bind. 3 PlayMode tests](76-drill-collision-forwarder.md) |
| 75 | [Terraforming Phase 5 foundation: OccupancyGrid (2 m cells, Solid/OpenWithFloor/OpenNoFloor) + A* (Cardinal6 / Full26 connectivity, fly/walk toggle) over voxel terrain. DigZone owns the grid; BuildFromChunkSdf rebuilds 8×8×8 slice per chunk on remesh. 8 new EditMode tests including the machine-gate tunnel path-exists assertion. No POI authoring or AI integration yet — just the data structure + algorithm](75-terraforming-phase-5-occupancy-foundation.md) |
| 74 | [Terraforming Phase 4c: LOD-boundary transition (surface-nets-native, not literal Lengyel). NeighbourLodStrides struct on DigChunk + populated by DigZone.BuildApronFor; SurfaceNetsMesher snaps fine boundary-strip vertex axes to coarse-cell-center lattice, suppresses fine-side boundary quads, degenerate-area filter as safety net. 4 PlayMode tests including the "no degenerate triangles at LOD boundary" machine gate](74-terraforming-phase-4c.md) |
| 73 | [Terraforming Phase 4 (partial): LOD reduction via per-chunk downsample-and-mesh (4a), camera-distance LOD selection in DigZone.Update (4b), per-chunk budget proxy test (4d). 4c transvoxel deferred — LOD boundaries show small seams as known artifact](73-terraforming-phase-4.md) |
| 72 | [Terraforming Phase 3: drill + bomb crater integration. CapsuleSubtract algorithm + DrillBlock (emits on contact, AudioCue.DrillContact + DebrisDust VFX) + TerrainCratering.OnBombDetonation wired into ProjectileWorld's bomb impact path. IDigZone.ApplyBrush promoted to Robogame.Core. 12 new tests (7 EditMode CapsuleSubtract + 5 PlayMode drill/crater)](72-terraforming-phase-3.md) |
| 71 | [Terraforming Phase 2d: .dig binary format + bake/load + SHA-256 content hash. DigZoneFormat.Write/Read with 68-byte header + per-chunk SDF payload; DigZone TextAsset loader integration; 6 EditMode format tests (round-trip + tamper detection) + 1 PlayMode bake/load test. Phase 2 milestone complete](71-terraforming-phase-2d.md) |
| 70 | [Terraforming Phase 2c: async Physics.BakeMesh on a worker (IJob), atomic collider swap — sharedMesh stays pinned at chunk.CurrentMesh throughout, never transiently null. DigZone.Update polls each chunk's PollBakeAndSwap. New [UnityTest] machine gate yields up to 60 frames asserting sharedMesh non-null + AreSame through bake completion. Verified autonomously](70-terraforming-phase-2c.md) |
| 69 | [Terraforming Phase 2b: apron-based seam-free meshing. DigChunk grows a (chunkSize+2)³ staging buffer; DigZone.BuildApronFor fills it from own SDF + 7 +direction neighbours (replicates own face when neighbour absent). New seam test (machine gate) pins boundary vertex agreement to 1e-4 m. Visible chunk-boundary cracks gone. Verified autonomously via run-tests.sh](69-terraforming-phase-2b.md) |
| 68 | [Stale bot-steering tests fixed (session 62 follow-up): DummyAiInputSourceTests.cs renamed to GroundBotInputSourceTests.cs, inline math helper replaced with GroundBotInputSource.ComputeSteer call, three test scenarios fixed for the actual −Z tangent at the +X point, three Assert.Pass stubs dropped](68-stale-bot-steering-tests-fix.md) |
| 67 | [Terraforming Phase 2a: multi-chunk DigZone container, new DigChunk MonoBehaviour, brush dispatch routes to affected chunks, scaffolder builds 2×2×2 grid. 10 PlayMode tests including new boundary-spanning brush test. No apron yet — seams visible (Phase 2b)](67-terraforming-phase-2a.md) |
| 66 | [Terraforming Phase 1c: Burst port of SurfaceNetsMesher (NativeArray + IJob.Run), DigZone zero-alloc mesh upload (Reinterpret + GetSubArray), new SurfaceNetsBenchmarkTests pinning < 1 ms median + zero-GC machine gate, ../subsystems/burst-notes.md](66-terraforming-phase-1c.md) |
| 65 | [Terraforming Phase 1b: DigZone MonoBehaviour + BrushApplicator (max-fold) + DigZone_Test scene scaffolder + 8 PlayMode tests. Plan upgrade: § 2 sign-convention fix (min→max), § 12 autonomy contract + per-phase machine gates](65-terraforming-phase-1b.md) |
| 64 | [Terraforming Phase 1a: Naive Surface Nets meshing algorithm + 12 EditMode tests (degenerate, half-space along XYZ, single-corner, sphere, determinism, buffer-reuse). New Robogame.Voxel asmdef. No Unity integration yet — Phase 1b](64-terraforming-phase-1a.md) |
| 63 | [Terraforming Phase 0: foundation interfaces (IDigZone / DigField / BrushKind / BrushOp / BrushOpBatch / Vector3Fixed) added to Robogame.Core. Zero behaviour change, dotnet build clean. Phase 1+ adds the meshing](63-terraforming-phase-0.md) |
| 62 | [Project-health sweep: deleted 5 dead-file scaffolders (ArenaBuilder, KenneyKit, RobotLayouts, DummyAiInputSource, ScrapPrefabScaffolder), slimmed ScaffoldHelpers 156→41 lines, retired 2 dead Tweakables, scrubbed stale comments. Pure deletion, no behaviour change](62-project-health-sweep.md) |
| 61 | [Grapple Magnet weapon: single-shot fire-and-retract launcher that lobs a rope+magnet up to 24 m, latches on enemy contact, instant retract on miss. New Grappler plane preset (twin-thrust nose-mount). Buggy preset retired](61-grapple-magnet-weapon.md) |
| 60 | [Tip-block attach redesign: SpringJoint replaces Locked ConfigurableJoint, MomentumImpactHandler exempts tip blocks, magnet latches + drags. Fixes the long-running "hook destroys itself" bug](60-tip-block-attach-redesign.md) |
| 59 | [Feel-good sweep: shared HudStyles font, scoreboard with frags, ScrapDepot recessed-hole visual + larger AOE, mountain-ring arena (no central obstacles), Magnet tip-block weapon](59-feel-good-sweep.md) |
| 58 | [Scrap-loop v1 (6-phase end-to-end): friendly tank + carry-weight penalty + depot AOE/score-tick + grinder + per-weapon-type ammo + reload](58-scrap-loop-v1.md) |
| 57 | [Default presets re-authored through BuildSession.TryPlace — same verb the player uses; hard-fail validation; auto-companion + cascade-remove move into the session](57-scripted-chassis-builds.md) |
| 56 | [Scrap-based scoring (team scrap → depots → first to 20 wins) + rope aim-sphere persistence fix](56-scrap-scoring.md) |
| 55 | [Rope tip-at-chain-end: slider in cells, tip lives at rope.cell + N*up, rope-bridge BFS edge](55-rope-tip-at-chain-end.md) |
| 54 | [Session wrap: building-architecture refactor + rotor/rope follow-ups (2-day arc, sessions 45–53 digest)](54-session-wrap.md) |
| 53 | [Rope follow-ups: tip-face direction (+up not -up), hologram length (use Tweakable segLen), chain collider preserved](53-rope-followups.md) |
| 52 | [Rope redesign: chain extends outward from chassis face, host cube hidden, hologram = full chain length](52-rope-redesign.md) |
| 51 | [World-intent pitch + rotor blade shift fix + rope adoption (rule of cool)](51-pitch-normalization-and-rotor-fixes.md) |
| 50 | [Per-face placement rules: rotor blade slots aero-only, rope tip face accepts hook/mace](50-rotor-aero-only-and-rope-tip.md) |
| 49 | [Auto-derive RotorsGenerateLift from grid contents (any rotor on chassis flips the flag)](49-rotor-auto-lift-flag.md) |
| 48 | [Rotor placement parity: auto-companion mechanism cube + cascade removal](48-rotor-auto-companion.md) |
| 47 | [Rotor placement fixes: FP overlap epsilon, hidden mechanism cube, spin-axis face connective](47-rotor-placement-fixes.md) |
| 46 | [BlockGhostRenderer extract + mirror-pitch sign-flip fix + placement-error HUD overlay](46-ghost-renderer-extract-and-mirror-pitch.md) |
| 45 | [Building architecture review steps 1–8: structural refactor (BlockEntries, BlockGraph, PlacementRules, BuildSession, ChassisAssembler, …)](45-architecture-review-implementation.md) |
| 44 | [Foil pitch Phase 4 (live readouts) + 3 fixes: rotor/foil deletion, leaf-bridge over-rejection, foil panel layout flip](44-foil-pitch-phase4-and-fixes.md) |
| 43 | [Foil pitch Phase 3: VariantConfigPanel rebuild — preset cards + primary slider + Advanced expander, foil + rotor sections](43-foil-pitch-phase3-ui.md) |
| 42 | [Foil pitch Phase 0+1+2+5: per-instance pitch / incidence on every aerofoil + rotor adopt-pass + visual mesh tilt](42-foil-pitch-audit.md) |
| 41 | [Wheels: Robocraft-style side-mount stem + tyre rig; default Tank/Buggy/starter rebuilt](41-wheels-side-mount.md) |
| 40 | [Garage features: mirror toggle (M) + leaf-block connectivity (no building on wings)](40-garage-mirror-and-connectivity.md) |
| 39 | [Scalable parts Phase 1.5: lift scales with planform area (2× wing → 2× lift; default chassis preserved)](39-scalable-parts-phase1.5-lift-scaling.md) |
| 38 | [Scalable parts Phase 1: swept-volume occupancy check (block placements that interpenetrate are now rejected)](38-scalable-parts-phase1-occupancy.md) |
| 37 | [Scalable parts: Phase 0 audit (no code; lift-vs-dims gap surfaced)](37-scalable-parts-audit.md) |
| 36 | [Follow-ups: animated flip, repair-pad beacon, aero regen visual, rope re-adoption + max-stretch break, hook orphan-joint cleanup](36-followups.md) |
| 35 | [Scrap pickups: drop on chassis death, collect by overlap, magnetic pull, foundational ScrapHeld counter + HUD](35-scrap-pickups.md) |
| 34 | [Snap-rotate flip (H key) + repair pad (gradual rebuild from blueprint over 10 s)](34-flip-and-repair-pad.md) |
| 33 | [Rope is inert in build mode → Hook/Mace placeable + removable from chassis grid; ghost previews for Hook/Mace/Rope/Rotor](33-rope-build-mode-tip-blocks.md) |
| 32 | [Projectile-system unification — single custom-stepped integrator (ProjectileWorld) replaces three Rigidbody-based MBs](32-projectile-unification.md) |
| 31 | [Cannon weapon, bomb-jitter / hook-punt / camera-aim bug fixes, damage-number summation, kill announcer, pause-on-settings, aim-line preview](31-cannon-bugfixes-features.md) |
| 30 | [Audio v1: Universal Sound FX wired into 21 cues, pooled voices, rotor whine loop, mixer-ready](30-audio-v1.md) |
| 29 | [VFX feel pass + audio system bones (muzzle flashes, hit sparks, debris dust, thruster plume; Audio settings + AudioRouter)](29-vfx-and-audio-bones.md) |
| 28 | [Pillar 1: singleplayer game loop (MatchController, AI bots, objective HUD, end overlay)](28-pillar-1-game-loop.md) |
| 27 | [Performance pass: docs, diagnostics, and conservative fixes](27-performance-pass.md) |
| 26 | [MP-readiness pass: combat per-block migration, inertia tensor, Verlet ropes, polish](26-mp-readiness-pass.md) |
| 25 | [Rope re-anchor on enable + cursor lock in build mode](25-rope-anchor-cursor-lock.md) |
| 24 | [Build cam free-look, hook adoption, aim self-skip, arch dummy](24-build-cam-tip-binder-aim-arch.md) |
| 23 | [Feel pass: rope-tip lifecycle, J-hook, helicopter symmetry, larger garage, free build cam, scroll zoom](23-feel-pass.md) |
| 22 | [Grapple hook: scaled-up tips, dumbbell target, joint-based latch (in progress)](22-grapple-hook-and-tip-resize.md) |
| 21 | [Helicopter frame stability: pure-axial rotor lift](21-helicopter-spin-axis-lift.md) |
| 20 | [Plane reconfigured as rope-tip test sandbox](20-plane-rope-tip-sandbox.md) |
| 19 | [Blueprint authoring cleanup, rotor stem, bigger heli, hook/mace, barbell (autonomous, in-progress)](19-blueprint-authoring-and-helicopter-overhaul.md) |
| 18 | [Helicopter foundations: garage gate + Aero foil tweakables (in-progress)](18-helicopter-foundations.md) |
| 17 | [Rotor / aerofoil decoupling, follow-ups (lift works, three new bugs)](17-rotor-foil-decoupling-followups.md) |
| 16 | [Rotor / aerofoil decoupling (WIP — three regressions outstanding)](16-rotor-foil-decoupling.md) |
| 15 | [Rotor follow-ups: tip collider, plane rotor, stress tower, physics plan](15-rotor-followups.md) |
| 14 | [Rotor block + spinning-rope ring + perf-discipline note](14-rotor-block.md) |
| 13 | [Rope block + GUI tweaks polish + momentum impact damage](13-rope-and-momentum-damage.md) |
| 12 | [Bomber preset + Bomb Bay block + health check / docs split](12-bomber-bombbay-and-audit.md) |
| 11 | [Polish: foam wake on chassis + connectivity flood-fill at placement](11-foam-wake-connectivity.md) |
| 10 | [Water visuals: Bitgem shader + Gerstner mesh + DevHud waves slider](10-water-bitgem.md) |
| 09 | [Build mode: in-garage block editor (Pass B Phase 3a)](09-build-mode-editor.md) |
| 08 | [Save/load foundations + "+ New Robot" button (Pass B kickoff)](08-save-load-blueprints.md) |
| 07 | [Phase 1 art pass: cel-shading, post-FX, ambient, skybox](07-art-direction-phase1.md) |
| 06 | [Settings panel + Tweakables registry](06-settings-tweakables.md) |
| 05 | [Plane "feel" pass](05-plane-feel.md) |
| 04 | [HitscanGun MissingReferenceException on Stop](04-hitscan-gun-fix.md) |
| 03 | [Chassis dropdown (Tank / Plane / Buggy)](03-chassis-dropdown.md) |
| 02 | [Launch button, three rounds of debugging](02-launch-button-debug.md) |
| 01 | [Pass A + garage/arena visual identity](01-pass-a-visual-identity.md) |
| 00 | [Background — initial refactor pass (pre-log)](00-background-pre-log.md) |

## Architecture

- [architecture.md](architecture.md) — current modules, runtime flow,
  patterns and gotchas.

## Known unknowns going forward

These are real items the next session should be aware of. None block
shipping the current branch; flagged so they don't decay into
"why is this broken".

- **Per-rotor `RotorsGenerateLift` opt-in.**
  Today the flag is auto-derived chassis-wide whenever any rotor is
  in the grid (session 49). Per-rotor opt-in needs per-cell blueprint
  config — same `ChassisBlueprint.Entry` extension other future
  schema additions will need. Tracked in
  [`ChassisBlueprint.RotorsGenerateLift`](../../Assets/_Project/Scripts/Block/ChassisBlueprint.cs)'s
  doc comment.

- **`BlockOccupancy` + `BlockGhostFactory` per-id switches** are still
  hardcoded. The structural refactor (session 45) intentionally
  stopped short of converting them to schema-driven dispatch tables
  — that's the right move when the second scalable shape lands per
  [`../subsystems/scalable-parts.md`](../../subsystems/scalable-parts.md) Phase 2.

- **Rope chain not visualising in garage.** User reported in session
  51; couldn't reproduce from code reading. Session 53's collider
  fix may have closed it (the chain was previously colliderless,
  which could have made it look "not present" in some camera
  angles). Needs verification on next playtest.

- **Foil pitch arc (sessions 42–44) — implementation still needs
  work.** The data model + adopt-pass + UI + live readouts all
  landed, but several items are explicitly deferred. Source of truth
  for what's left is [`docs/research/historical/foil-rotation-plan.md`](../FOIL_ROTATION_PLAN.md)
  § 10 *Carry-forward*. Headline items: live mid-edit collective
  propagation to existing blades (slider feels inert until the rotor
  is re-placed), select-and-retune UX for already-placed blocks (a
  persistent Phase 1.b carry from session 38), pitch ghost-preview
  tilt, and a tuning playtest pass against the shipped helicopter +
  plane.

- **Helicopter session-21 fixes need in-game verification.** The new
  PlayMode test passes analytically; the user reported the chassis
  still spinning before the second (collider-sweep) fix landed but
  hasn't yet flown the chassis with both fixes in place. If the
  chassis still spins after both fixes, the next suspect is PhysX
  per-step kinematic-MoveRotation interactions, which would need
  per-FixedUpdate diagnostic logging on the chassis angular velocity.

- **Tip-block rope-detach lifecycle.** When an adopted Hook / Mace's
  HP drops to zero mid-flight, `Robot.DetachAsDebris` reparents the
  tip GameObject to scene root and adds a Rigidbody to it. The rope
  segment's mass was bumped by the tip's mass at adoption; that mass
  isn't reverted on detach, so the segment becomes overweight relative
  to the actual chain. Edge case — flagged in
  [session 19](19-blueprint-authoring-and-helicopter-overhaul.md).

- **Tweakables defaults vs persisted JSON.** Bumping a registered
  default in code does NOT take effect for users with a saved value
  in `Application.persistentDataPath/tweakables.json`. The session-20
  rope segment count default 5→8 is the most recent example.
  Documented in `architecture.md`'s gotchas table.

- **B1 garage render of the helicopter.** Session 18 phase A added
  the kinematic-chassis early-return in `RotorBlock.BuildLiftRig`,
  which keeps foils under the chassis grid root in the garage.
  Should now display correctly, but worth a visual check during the
  same session-21 verification pass.

- **Per-block blueprint config (PHYSICS_PLAN §6).** Still future work.
  Foil-dimension `Aero.*` Tweakables and `Combat.Rope*` damage
  constants are MP debt — they need to move to per-block / per-chassis
  config before netcode lands. Session 19 docs spell this out
  explicitly so the migration target is clear.

- **Tail rotor visual sweep.** The default helicopter still has a
  bare cosmetic tail rotor at `(1, 0, -4)` with spin axis +X. With
  the session-21 ignore-pair fix, foil-vs-chassis collisions are
  suppressed only when `AdoptAdjacentAerofoils` runs — for a bare
  rotor (zero foils adopted), nothing pairs. That's fine because
  bare rotors have no orbiting foil colliders, but worth noting if
  someone adds foils to the tail rotor later: re-run
  `IgnoreFoilChassisContacts` to keep the contract.

- **Netcode carry-forwards (post-Phase-6).** Backend phases 0/1/3-lite/
  3.5/3.6/4/6 are all shipped (sessions 86–94). The remaining work
  splits cleanly into user-gated and code-gated:
  - **Phase 5 (Steam)** is gated on you provisioning an App ID on
    Steamworks Partner and choosing Facepunch.Steamworks vs
    Steamworks.NET bindings. The `ILobbyService` / `ITransportProvider`
    abstractions described in [netcode.md §14](../subsystems/netcode.md)
    haven't been scaffolded yet — that's the natural first slice once
    you have an App ID, since the abstractions can land with `Ugs*`
    impls while the `Steam*` impls wait for the App ID.
  - **Phase 6 cloud deployment** (Multiplay / Hathora) is your billing
    + provider decision. The headless `StartServer` build target is
    landed and locally runnable.
  - **Phase 6 lag-comp authoritative flip.** Today lag-comp is
    telemetry-only (logs `[Lag-comp telemetry]` when a remote-client
    shot would have hit at the shooter's tick but missed live). Only
    flip to applying damage if a hitscan weapon type ever ships —
    flipping for slow-projectile weapons would destroy
    leadable / dodgeable gameplay.
  - **Phase 3.6 `ReconciliationSmoother`** stayed deferred. Only build
    it if MPPM under the §16 latency matrix surfaces a jarring snap
    that Rigidbody interpolation doesn't hide.
  - **Phase 4 late-join activation.** ✅ Wired in session 95 —
    `NetworkSceneFlow` replays `DestroyedBlockLog` on
    `SceneEventType.SynchronizeComplete` for every non-host client.
    v1 lobbies still lock at round start (§10), so this only fires
    for the host's own join today; v2 mid-match join is now a
    lobby-config flip, not a fresh integration.
  - **Pre-existing baseline failures.** ✅ Cleared in session 95.
    EditMode 252/253, PlayMode 92/93 — 0 failures. The remaining
    1 inconclusive (EditMode) + 1 `[Ignore]` (PlayMode
    `MatchFlowTests.SpawnBot`, blocked on `Tests/Scenes/MinimalArena.unity`)
    are documented and won't decay into "why is this broken".
