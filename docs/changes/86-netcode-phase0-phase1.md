# 86 — Netcode Phase 0 close + Phase 1 NGO loopback baseline

> Status: **13 commits, dotnet-build-green at every step.** Phase 0 is
> compile- *and* logic-verified (EditMode tests written). Phase 1 NGO
> code is compile-verified only — functional proof is the user-side
> MPPM 1v1 run (handoff §6). Executed from
> [NETCODE_PHASE1_HANDOFF.md](../NETCODE_PHASE1_HANDOFF.md) after a
> planner pass + 4 architect decisions.

## Why this session

Finish NETCODE_PLAN Phase 0 (2 open items) and deliver Phase 1 (NGO
loopback baseline). Explicitly NOT Phase 2+ (Relay/Steam/CSP).

## Architect decisions (handoff §5)

Interned string-table in the blob · health tiers 0.75/0.50/dead ·
exclude displayName+createdUtc from the hash · 50 Hz NGO tick.

## What shipped

**Phase 0.** `INetworkContext` + offline-default `NetworkContext`
(Core; offline ⇒ `IsServer=IsClient=true`, so singleplayer is
byte-identical and every "am I authoritative?" query already answers
right). `BlueprintBlob` binary wire codec alongside the v4 JSON —
BrushOpCodec discipline, interned string table, CRC-32 content hash
that excludes displayName/createdUtc (the createdUtc trap). 9 EditMode
blob tests + 4 NetworkContext-invariant tests.

**Phase 1 (NGO 2.4.0 / UTP 2.4.0).** `NetworkBootstrap` (auto-boot,
50 Hz tick, registers `INetworkContext` only while a session is live),
`ContentHashGuard` (Bucket-A connection-approval), dev-only `NetDevHud`.
The five Net siblings (NETCODE_PLAN §5): `NetworkRobot`+`SpawnRobotPayload`
(blob spawn through `ChassisAssembler` 1:1, post-spawn ClientRpc, no
late-join per §10), `NetworkRobotState` (4-tier health NetworkVariables,
written only on a band crossing), `NetworkBlockGrid`+`BlockHitEvent`
(per-tick BlockHitBatch ClientRpc; client replays the same TakeDamage so
the existing destruction + structural path runs identically),
`NetworkRobotMovement`+`NetworkInputSource` (stock server-auth
NetworkTransform, clients kinematic, owner input → ServerRpc; NO CSP),
`NetworkRobotCombat` (server-auth fire by reusing replicated input;
client firers disabled). `NetworkSceneFlow` (NetworkSceneManager
wrapper + RoundPhase NetworkVariable + ServerArenaLoaded seam). Six
`ArenaController` server-only guards.

## Plan corrections made (not blind-followed)

1. **Step-3 NGO bootstrap can't be agent-compile-gated** — the agent
   shell can't run Unity to resolve the package. Surfaced; user opened
   Unity once to resolve NGO + regenerate csprojs, then the gate was
   restored for Steps 4–10.
2. **Handoff §3.4 "gate on IsServer" was wrong for an online host.**
   Robots spawn via `NetworkRobot.ServerSpawn`, not ArenaController's
   local path, so a host (IsServer==true) would double-spawn. Guards
   corrected to **`IsOnline`** (offline-only). Offline still
   byte-identical; the online-host double-spawn is closed.

## Deliberately deferred (honest scope)

- **Validated `FireCommand` ServerRpc + cosmetic `ProjectileSpawnEvent`
  tracer (§9 step 2 / §13).** Needs a sanctioned `ProjectileWorld`
  spawn-observation hook = a Combat-tier change outside this pass's
  "don't touch gameplay" remit. Not shipping speculative dead RPCs.
  Phase-1 effect: observer clients may not see remote tracers ("ugly",
  per the exit criterion); damage + destruction *do* replicate.
- **Match-state / score replication.** Later phase; an online client
  has no local MatchController by design.

## MPPM-exit checklist (user / in-editor — cannot be done headless)

1. Confirm NGO/UTP resolved (bump the 2.4.0 pins if Package Manager
   offers a newer 2.x).
2. Author the **robot network prefab**: bare GameObject + `NetworkObject`
   + `NetworkRobot`/`NetworkRobotState`/`NetworkBlockGrid`/
   `NetworkRobotMovement`(+`NetworkTransform`)/`NetworkRobotCombat`. Let
   Unity generate its GUIDs (handoff §2.4). No blocks on it — they are
   built at runtime from the blob.
3. Put `NetworkManager` + `NetworkBootstrap` + `NetworkSceneFlow` in the
   Bootstrap scene; register the robot prefab in the NetworkManager
   prefab list.
4. Wire `NetworkSceneFlow.ServerArenaLoaded` → for each player call
   `NetworkRobot.ServerSpawn(prefab, blueprint, clientId, team, pose)`.
5. Run **MPPM ×2** loopback (NetDevHud Host / Join): each spawns from
   its blueprint, both drive + shoot, damage + destruction replicate.
   Tag Phase 1; tick NETCODE_PLAN §15 Phase 1.

## Hard-invariant check

- **#1 no gameplay Tweakable:** untouched (session 85 cleared it).
- **#2 canonical block index:** the blob re-runs `SetEntries`;
  `BlockHitEvent.BlockIndex` is that ordering on every peer.
- **#3 server-authoritative:** spawn/damage/projectiles/respawn/scoring
  all server-side; clients mirror.
- **#4 single Rigidbody:** no new bodies; clients set the existing one
  kinematic.
- **#6 no per-frame alloc:** idle ticks allocate nothing; batch arrays
  are event-time only.
- **Singleplayer byte-identical:** offline `IsOnline==false` ⇒ every
  ArenaController guard runs the original path.

## Files

New: Core/INetworkContext, Core/NetworkContext, Block/BlueprintBlob;
Network/Bootstrap/{NetworkBootstrap,ContentHashGuard,NetDevHud,
NetworkSceneFlow}; Network/Robot/{SpawnRobotPayload,NetworkRobot,
NetworkRobotState,BlockHitEvent,NetworkBlockGrid,NetworkInputSource,
NetworkRobotMovement,NetworkRobotCombat}; tests BlueprintBlobTests,
NetworkContextTests. Edited: ArenaController (6 guards),
Robogame.Network.asmdef, Packages/manifest.json.

## Follow-ups

- MPPM-exit checklist above (prefab + scene + spawn loop + the run).
- Next netcode phase: ProjectileWorld spawn hook → validated
  FireCommand + cosmetic tracer; match-state replication; then Phase 2
  (UGS Relay/Lobby).
- Unrelated: 5 `Mat_*` files showed modified mid-session, not authored
  here — left untouched for the user to resolve.
