# Netcode — First Major Pass: Execution Handoff

> **Audience:** the next agent, executing the first real netcode milestone.
> **Scope:** finish NETCODE_PLAN.md **Phase 0** (the 2 unchecked preflight
> items) and deliver **Phase 1** (NGO loopback baseline). Explicitly
> *not* Phase 2+ (Relay / Steam / client-side prediction).
> **Read first, in full:** `docs/NETCODE_PLAN.md` (the contract — this
> handoff does not restate it, it sequences it), `CLAUDE.md` hard
> invariants, `docs/changes/85-*.md` (what just changed and why it matters
> here), `docs/PHYSICS_PLAN.md` §1.
> **Do not start coding until the planner subagent has produced a
> reviewed plan** (CLAUDE.md workflow). This doc is the brief for that
> planner pass, not a substitute for it.

---

## 1. Ground truth as of session 85 (read this before trusting NETCODE_PLAN §15's checkboxes)

NETCODE_PLAN §15 Phase 0 lists most preflight items ✅. Session 85
**materially advanced the foundation** — update your mental model:

- **The blueprint is now the *sole* server-authoritative source for
  every gameplay-observable value.** Before session 85, plane/ground/
  thruster/rudder/rotor tuning and ramming damage were read from
  per-machine `tweakables.json`. Replicating a "correct" blueprint
  would *still* have desynced because two clients had different local
  sliders. That hole is closed (hard invariant #1 now fully enforced —
  `grep` confirms zero gameplay knobs on `Tweakables`). **This is the
  thing that actually unblocks Bucket B / §7.** Without it, netcode
  would have been built on sand.
- Gameplay config now lives in three server-ownable places, mapping
  cleanly onto NETCODE_PLAN §6 buckets:
  - `ImpactConfig` SO (`Resources/ImpactConfig.asset`) — ramming damage.
    → **Bucket A.** Content-hash it at connect (§6 Bucket A, §13).
  - `ChassisBlueprint` chassis-level config (`PlaneTuning` /
    `GroundTuning` / `ChassisDamping` / `ThrusterTuning`) +
    per-block `Entry.BlockConfig`. → **Bucket B**, rides the
    `SpawnRobotPayload` blueprint blob (§6 Bucket B, §7a).
  - Match shape stays on `MatchConfig` SerializeField on
    `ArenaController` (already correct).
- `BlueprintSerializer` is now **v4**. It is JSON and is **NOT the wire
  format** — Phase 0's unchecked "`BlueprintBlob` packed binary" item is
  still open and is part of this pass (see §3.2). When you add a content
  hash, **exclude `createdUtc`** from the hashed payload — it is
  `DateTime.UtcNow` per-serialize, so two byte-identical builds hash
  differently otherwise (flagged in the session-85 data-layer audit).
- **Verified in-engine by the user** post-session-85: the migration's
  back-compat path works (old saves/presets load behaviour-identical).
  You are building on confirmed-good ground.

### Phase 0 status after session 85

| Item | State |
|---|---|
| Server-auth mindset, projectiles CSP-ready, blueprint serializer, block-grid events, `IInputSource`, `IMovementProvider` | ✅ (pre-existing) |
| **No gameplay value is per-machine** | ✅ **new in 85** — the real unblock |
| `INetworkContext` in `Robogame.Core` (offline-default stub) | ⬜ **this pass, do first** |
| `BlueprintBlob` packed binary wire form alongside JSON | ⬜ **this pass** |

---

## 2. Hard-won architectural landmines (this session paid for these — do not relearn them)

1. **Assembly topology is load-bearing.** `Robogame.Robots` → references
   → `Robogame.Movement` (NOT the reverse). Movement-tier code **cannot**
   see `Robot`. Session 85 hit this: the per-chassis config could not be
   read off `Robot.Blueprint` from `PlaneControlSubsystem`; it rides
   `RobotDrive.Blueprint` instead (`RobotDrive` is in Movement and already
   references `Block`). **Implication for you:** `Robogame.Network`
   references everything and is fine, but any data a Net component needs
   to hand to a Movement-tier component must go through a type Movement
   already sees (`RobotDrive`, `BlockBehaviour`, `ChassisBlueprint`,
   interfaces) — never by making a gameplay asmdef reference Network.
   `Robogame.Network.asmdef` refs everything; gameplay asmdefs ref it
   **never** (NETCODE_PLAN §14). The one sanctioned bridge is the
   `INetworkContext` interface in `Robogame.Core`.
2. **`ChassisAssembler` is the single chassis construction chokepoint**
   (`ChassisFactory` is a thin facade over it). It sets `robot.Blueprint`
   and `robotDrive.Blueprint` *before the root activates* (deactivated-
   build pattern — `AddComponent` runs `OnEnable` synchronously, see
   CLAUDE.md). `SpawnRobotPayload` deserialization on clients **must**
   call this same path (NETCODE_PLAN §6 Bucket B: "reuses our
   singleplayer construction code path 1:1"). Per-block config flows
   `Entry.BlockConfig → BlockBehaviour.ConfigValue` set right after
   `grid.PlaceBlock`; that wiring already exists and is exercised by the
   blueprint round-trip.
3. **The canonical block ordering IS the netcode contract** (invariant
   #2). `ChassisBlueprint.SetEntries` → `BlockEntries.SortCanonical`
   (sorted by `Vector3Int`) is the only mutation path. `BlockHitEvent.
   blockIndex` (NETCODE_PLAN §7b) indexes into *this* ordering. Do not
   add a code path that mutates entries without going through
   `SetEntries`, and do not make `BlockConfig`/`Dims`/`Pitch` part of the
   sort key (they aren't — keep it that way).
4. **Hand-authored `.meta`/`.asset` GUIDs exist** (`ImpactConfig.cs` +
   `.asset`, `BlueprintMovementConfig.cs`, `GrappleMagnetTests.cs`).
   Unity validated them in-engine this session (user confirmed). NGO
   adds `NetworkObject` (a GUID-bearing component) + prefab registration —
   when you author the Robot network prefab, let Unity generate its
   GUIDs; don't hand-roll those.
5. **You cannot run Unity/tests from the agent shell.** `dotnet build
   robogame.slnx -clp:ErrorsOnly` is your compile gate. Functional
   verification = Unity **MPPM** (NETCODE_PLAN §16, "use from Phase 1
   onward") and is a user-side / in-editor step. Say so honestly; do not
   claim a netcode behaviour works without an MPPM run.

---

## 3. Implementation plan — ordered, each step independently committable + build-green

### 3.1 `INetworkContext` (Phase 0, do first — unblocks everything, zero behaviour change)

- Add `Assets/_Project/Scripts/Core/INetworkContext.cs`:
  `bool IsServer; bool IsClient; bool IsHost; bool IsOnline;`
- Add `NetworkContext.cs` (Core) — singleton, **defaults to offline**
  (`IsServer=true, IsClient=true, IsHost=false, IsOnline=false` so
  singleplayer code that asks "am I authoritative?" always gets yes).
- No NGO dependency yet — this is a plain stub in Core.
- Find every place gameplay does something that must become
  server-only later (respawn, scoring, damage application, projectile
  spawn, match-state transitions). **Do not gate them yet.** Just
  inventory them in the commit message / a scratch list for §3.4.
- **Exit:** `dotnet build` clean; singleplayer behaves identically;
  `NetworkContext.Instance.IsServer` is true everywhere it's queried.

### 3.2 `BlueprintBlob` packed binary (Phase 0)

- Add a compact binary encode/decode for `ChassisBlueprint` *alongside*
  the existing v4 JSON (do not replace JSON — it's the human/debug/disk
  form). Mirror the existing `BrushOpCodec` discipline (session 81 — a
  17-byte wire-stable codec already in the repo; same patterns:
  versioned header, fixed-width fields, round-trip + tamper tests).
- It must encode: entries (id-as-interned-index or short string table,
  pos, up, dims, pitch, **blockConfig**), the 4 chassis configs, kind,
  rotorsGenerateLift. **Exclude** `displayName` and `createdUtc` from
  the hashed region.
- Add EditMode tests: blob round-trip == JSON round-trip (same
  `ChassisBlueprint`), and a content-hash that is stable across two
  serializes of the same blueprint (the `createdUtc` trap).
- **Exit:** blob ↔ blueprint round-trips byte-stable; hash stable;
  `dotnet build` clean. (Wire format is now real; NETCODE_PLAN §6
  Bucket B has its payload.)

### 3.3 NGO packages + `NetworkBootstrap` (Phase 1)

- Add NGO + UTP packages (pin versions; record in
  `docs/PACKAGE_MODIFICATIONS.md` if any source edits needed — unlikely).
- `Assets/_Project/Scripts/Network/Bootstrap/NetworkBootstrap.cs`:
  creates/configures `NetworkManager` with `UnityTransport`. Lives in
  the Bootstrap scene additively (NETCODE_PLAN §10 — Bootstrap stays
  loaded; arena additive on top). It implements `INetworkContext` and
  registers it so `NetworkContext` now reflects real NGO state when a
  session is running, offline otherwise.
- `ContentHashGuard.cs`: on connect, compare server vs client hash of
  { all `BlockDefinition`s, `ImpactConfig`, weapon/bomb defs }. Reject
  mismatched clients with a clear message (NETCODE_PLAN §6 Bucket A /
  §13). Reuse the §3.2 hash util.
- Dev HUD: "Host on 7777" / "Join 127.0.0.1:7777" (NETCODE_PLAN §15
  Phase 1). This replaces the F1 menu's role for net testing only;
  don't disturb the existing DevHud.
- **Exit:** two MPPM instances establish an NGO connection over UTP
  loopback; no robots yet; `NetworkContext` flips correctly.

### 3.4 The `Net*` sibling components (Phase 1 core — NETCODE_PLAN §5 pattern)

Gameplay components stay untouched; thin Net siblings carry the wire.
All under `Assets/_Project/Scripts/Network/Robot/`. Build in this order:

1. **`NetworkRobot`** — owns spawn/despawn/ownership. On the server,
   spawns the `NetworkObject` and sends `SpawnRobotPayload`
   { playerId, teamId, blueprintBlob (§3.2), spawnPose }. On every
   client (incl. host) deserialize → drive the **existing**
   `ChassisAssembler`/`ChassisFactory.Build` path (do NOT reimplement
   construction; §6 Bucket B / §7a). Reuses session-85's confirmed
   blueprint round-trip.
2. **`NetworkRobotState`** — subscribes to existing `Robot`/`BlockGrid`
   events on the server; replicates aggregate alive/dead + health-tier
   `NetworkVariable`s (NETCODE_PLAN §6 Bucket D / §7 open-question:
   broadcast HP only on tier boundary, not per graze).
3. **`NetworkBlockGrid`** — server routes `BlockBehaviour.Damaged/
   Destroyed` into a per-tick `BlockHitBatch` `ClientRpc`
   (`BlockHitEvent { ushort blockIndex; ushort hpAfter; byte hitFlags }`
   — §7b). Client applies the *same* destruction + structural-integrity
   logic locally; server's RPC carries the authoritative orphan-index
   list for tie-breaks. Idempotent on receive (§6 Bucket D).
4. **`NetworkRobotMovement`** — Phase 1 uses **stock `NetworkTransform`**
   (send-only on owner, replicate on remotes). CSP is Phase 3 — do NOT
   build prediction now (NETCODE_PLAN §15 Phase 1 explicitly defers it).
   Remote input arrives via a `NetworkInputSource : IInputSource`
   (buffers wire commands instead of reading the keyboard — the
   existing interface makes this a drop-in; `RobotDrive`/subsystems
   don't change).
5. **`NetworkRobotCombat`** — `ServerRpc(FireCommand)` →
   server-authoritative `ProjectileWorld.Spawn` → `ClientRpc
   (ProjectileSpawnEvent)` for client tracers (NETCODE_PLAN §9). Clients
   never spawn authoritative projectiles.

Now go back to §3.1's inventory and gate each server-only action with
`NetworkContext.IsServer` (respawn, scoring, damage application,
match-state, projectile authority). Singleplayer/offline still returns
true everywhere → no behaviour change.

- **Exit (Phase 1 / NETCODE_PLAN §15):** two MPPM instances, each
  spawns a robot from its blueprint, both drive + shoot, damage and
  destruction replicate. "Playable, laggy, ugly 1v1 over LAN." Tag it.

### 3.5 Connection / scene flow (NETCODE_PLAN §10)

Wire the §10 sequence using NGO `NetworkSceneManager` (synchronized
load handshake — clients cannot spawn into the arena before the server
says scene-loaded). `ArenaController` already owns the match lifecycle
(`MatchController` is plain C# specifically so a `NetworkBehaviour`
wrapper can host it — see architecture.md gotcha). Use the
`INetworkContext` bridge so `ArenaController` skips local-only respawn
in net mode rather than referencing Network directly.

---

## 4. Testing strategy for this pass

- **Compile gate (agent):** `dotnet build robogame.slnx -clp:ErrorsOnly`
  after every step. Note Unity regenerates gitignored `.csproj` —
  new `.cs` files need a `<Compile Include>` line added manually for
  the agent build (see session 85 commits for the pattern).
- **Functional (user / in-editor):** Unity **MPPM**, 2 instances
  (NETCODE_PLAN §16). Loopback only this pass; no conditioning yet.
- **Automated:** a PlayMode test that hosts a server, connects a
  synthetic client, fires a fixed input sequence, asserts the
  `BlockHitBatch` round-trips and the client's grid matches the
  server's (NETCODE_PLAN §16). Extend `MatchFlowTests` patterns from
  session 85 (real assertions, no `Assert.Pass` stubs — CLAUDE.md
  Rule 9). The session-85 `BlueprintSerializerTests` v4/v3 suite is
  your regression net for the blob codec (§3.2).
- Honest reporting: state explicitly that functional behaviour is
  MPPM-verified or, if you couldn't run it, that it is compile-verified
  only and needs an MPPM pass. (Session 84/85 precedent.)

---

## 5. Escalate to the architect (user) — do not decide these solo

These are NETCODE_PLAN §17 open questions sharpened by session 85.
Surface them *before* the step that forces the decision, not after:

1. **String IDs in the blob.** Entries store `BlockId` strings. A wire
   blob wants an interned id table or an enum. Changing block-id
   representation touches the §3.2 codec *and* the canonical-sort
   contract. Propose the table approach; get sign-off before §3.2.
2. **Health-tier granularity** (§7 open question) — confirm the
   full→cracked→critical→dead tiering with the user; it affects how
   `NetworkRobotState` thresholds and the §3.4.2 NetworkVariables.
3. **`createdUtc` / blueprint identity.** Confirm the hashed region
   excludes displayName + createdUtc (recommended) so identical builds
   hash equal for the §3.3 content guard.
4. **Tick rate** (NETCODE_PLAN §4) — Phase 1 can run NGO defaults, but
   confirm the intended snapshot Hz before §3.4.4 so `NetworkTransform`
   send-rate isn't set twice.

---

## 6. Definition of done for this pass

- [ ] Phase 0 closed: `INetworkContext` shipped offline-default;
      `BlueprintBlob` codec + stable content hash + tests.
- [ ] NGO/UTP added; `NetworkBootstrap` + `ContentHashGuard` + dev
      host/join HUD.
- [ ] `NetworkRobot` / `NetworkRobotState` / `NetworkBlockGrid` /
      `NetworkRobotMovement` (stock NT) / `NetworkRobotCombat`.
- [ ] Server-only actions gated via `NetworkContext.IsServer`;
      singleplayer/offline behaviour byte-identical.
- [ ] `dotnet build` clean throughout; per-step commits; session log
      `docs/changes/NN-*.md` + architecture.md updated.
- [ ] **MPPM exit run:** 1v1 loopback — spawn from blueprint, drive,
      shoot, damage + destruction replicate. (User/in-editor.)
- [ ] Phase 1 tagged; CHANGES/dev-log entry; NETCODE_PLAN §15 Phase 1
      checkboxes ticked.

Out of scope (do not start): Relay/Lobby (Phase 2), CSP/reconciliation
(Phase 3), Steam (Phase 5), dedicated server (Phase 6). If Phase 1
reveals a Phase 0 gap, fix the gap — don't reach forward.
