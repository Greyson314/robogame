# 🌐 Netcode & Multiplayer Plan

> Long-form design document for evolving Robogame from singleplayer-with-multiplayer-shaped-architecture into a shipping, Steam-published, server-authoritative multiplayer game.
>
> **Audience:** future me, future contributors, future AI agents working on this codebase.
>
> **Bias:** boring, well-trodden, Unity-blessed solutions over clever ones. Every decision below should be defensible by linking to either Unity documentation, an established Unity sample (Boss Room, Galactic Kittens), or a credible netcode reference (Glenn Fiedler's *Networked Physics*, Valve's *Source Multiplayer Networking*, Overwatch GDC talks).

---

## Table of Contents

- [1. Goals & Non-Goals](#1-goals--non-goals)
- [2. Stack Decision](#2-stack-decision)
- [3. Authority Model](#3-authority-model)
- [4. Tick Model & Determinism Stance](#4-tick-model--determinism-stance)
- [5. Network Architecture Layered on Existing Code](#5-network-architecture-layered-on-existing-code)
- [6. State Replication Strategy](#6-state-replication-strategy)
- [7. The Hard Problem: Replicating Block-Based Robots](#7-the-hard-problem-replicating-block-based-robots)
- [8. Movement: Client-Side Prediction & Reconciliation](#8-movement-client-side-prediction--reconciliation)
- [9. Combat: Server-Authoritative Projectiles + Client Tracers](#9-combat-server-authoritative-projectiles--client-tracers)
- [10. Scene & Lifecycle Flow](#10-scene--lifecycle-flow)
- [11. Steam Integration](#11-steam-integration)
- [12. Backend Services](#12-backend-services)
- [13. Anti-Cheat Surface](#13-anti-cheat-surface)
- [14. Folder, Assembly, and File Plan](#14-folder-assembly-and-file-plan)
- [15. Phased Rollout](#15-phased-rollout)
- [16. Testing Strategy](#16-testing-strategy)
- [17. Risks & Open Questions](#17-risks--open-questions)
- [18. References](#18-references)

---

## 1. Goals & Non-Goals

### Goals (in priority order)

1. **Steam-published 1v1 → up to 8v8 PvP** with per-player physics-driven block robots.
2. **Server-authoritative gameplay** — never trust the client for damage, hit detection, or block destruction.
3. **Smooth feel for the local player** via client-side prediction & reconciliation, even at 80–150 ms RTT.
4. **Cheap to host** at the small scale needed during early access — no dedicated-server bills until we have players to justify them. Host-via-Steam is the v1 happy path.
5. **A clean upgrade path to dedicated Linux headless servers** without rewriting gameplay code.
6. **Determinism-free** — we accept that PhysX is non-deterministic and design around it. No lockstep, no rollback netcode, no fancy physics syncing.

### Non-Goals

- ❌ **Rollback netcode** (Quake / GGPO / Unity Physics 1.x ECS rollback). Wrong fit for vehicular physics with many bodies.
- ❌ **MMO-scale concurrency.** Shard size is per-match (≤ 16 players). No interest cells, no sub-shards, no entity streaming.
- ❌ **Cross-platform parity at launch.** Steam-only initially; Xbox/PS/Switch are explicit follow-ups, not constraints on v1.
- ❌ **Mod / user-server hosting at launch.** Doors stay closed until anti-cheat surface is understood.
- ❌ **A custom transport.** Unity Transport (UTP) + Steam datagram transport cover us through Phase 5.

---

## 2. Stack Decision

| Concern | Choice | Why |
|---|---|---|
| **High-level netcode framework** | **Netcode for GameObjects (NGO) 2.x** | First-party Unity. The Boss Room sample (the canonical learning resource) is built on it. NetworkTransform / NetworkVariable / RPC primitives are exactly what we need. Active development; aligned with Unity 6. |
| **Transport layer** | **Unity Transport (UTP) 2.x**, with **Steam datagram relay (SDR)** transport when available | UTP is the default NGO transport and supports Relay out of the box. For Steam-published builds, we swap in a Steam-aware transport ([com.community.netcode.transport.steamworks-utp](https://github.com/Unity-Technologies/multiplayer-community-contributions) or [FacePunchTransport](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/Transports/com.community.netcode.transport.facepunch)) so that NAT punch and IP hiding go through Steam's relay network. |
| **Steamworks bindings** | **Facepunch.Steamworks** (preferred) or **Steamworks.NET** (fallback) | Facepunch.Steamworks has a saner async-friendly API. Steamworks.NET is more battle-tested and a closer 1:1 to the C SDK. Decision deferred to Phase 5 — both have NGO transport implementations. |
| **Lobby + matchmaking (early access)** | **Steam Lobbies** via Steamworks + a thin abstraction layer | Free, infinite scale, no UGS bill, peer-to-peer joinable. We never tell players an IP. |
| **Lobby + matchmaking (alt path / dev mode)** | **Unity Lobby + Relay (UGS)** | Useful in editor / for non-Steam internal builds. The abstraction layer lets us swap. |
| **Dedicated server hosting (Phase 6+)** | **Unity Multiplay (UGS)** or **Hathora** | Both run headless Linux Unity builds on demand. Multiplay has tighter NGO/UTP integration; Hathora is cheaper at low scale. |
| **Voice** | **Vivox** (UGS) | Free up to a generous tier; integrates with NGO out of the box. Punted to Phase 7. |
| **Player accounts / persistence** | **Steam User ID** (`CSteamID`) as primary key; backend TBD | At launch we store loadouts/blueprints in Steam Cloud. A real backend (PlayFab? Custom?) is a Phase 8 concern. |

### Why not Mirror / FishNet / Photon?

- **Mirror & FishNet** are excellent and have stronger CSP/lag-comp out of the box. But: NGO is the path Unity is investing in, ships with the Multiplayer Tools window, integrates with UGS, and is what Unity samples target. The cost of being non-canonical (smaller community, less doc, fewer Unity bug fixes hitting our setup) is not worth the marginal quality gain at this project's scope.
- **Photon (PUN/Fusion)** is technically excellent but has per-CCU pricing that scales painfully if the game succeeds, and it locks us into Photon Cloud. Hard no for a Steam game we want to ship cheap.

### Package list (pinned at adoption time, not now)

```
com.unity.netcode.gameobjects
com.unity.transport
com.unity.services.core
com.unity.services.authentication
com.unity.services.relay        // dev/internal builds
com.unity.services.lobby        // dev/internal builds
com.unity.multiplayer.tools     // editor-only debug HUD, network profiler
com.community.netcode.transport.steamworks-utp   // when Steam path lands
```

---

## 3. Authority Model

### Single source of truth = the server.

This is non-negotiable. Every multiplayer bug ever filed comes down to "two machines disagreed about who owned a piece of state." We pre-empt it by having one rule:

> **The server (or the host's server-side process) computes gameplay state. Everyone else animates a copy.**

### Client responsibilities

- **Send inputs**, not state. `InputCommand { tick, move, vertical, fireHeld, aimDir }` arrives on the server every tick.
- **Predict the local player** for snappy controls. Replay the same command stream on top of the last authoritative snapshot.
- **Interpolate remote players** between server snapshots (≈100 ms behind for jitter buffer).
- **Render visual-only effects** (tracers, muzzle flashes, screen shake, HUD pings) the moment they're locally relevant — these never need to be authoritative.

### Server responsibilities

- Run physics (`Rigidbody`, wheel friction, drive forces).
- Run combat (projectile sweeps, damage application, block destruction, structural integrity check).
- Run game rules (round timer, scoring, win conditions).
- Broadcast `NetworkVariable` updates and `ClientRpc` events on the authoritative tick.
- Validate every client request — discard inputs older than `tick - K`, reject impossible aim deltas, rate-limit fire commands.

### Host vs. dedicated server

- **Phase 2–5: listen-server / host model.** One client (the lobby leader) runs `NetworkManager.StartHost()` and is both server and player. The host has zero network latency; everyone else has full RTT. This is fine — Robocraft itself ran on it for years.
- **Phase 6+: dedicated server.** Same code, `StartServer()` instead of `StartHost()`. Headless Linux build (`-batchmode -nographics -server`). The fact that hosting was always a separate logical role from "local player" is what makes this swap painless.

### Local-only mode

Singleplayer / training / replays use NGO's local-only mode (`StartHost()` with no clients) **or** `NetworkManager`'s offline path. Either way, the same `Robot` / `BlockGrid` / `ProjectileGun` code runs. We do **not** maintain a parallel non-networked code path.

---

## 4. Tick Model & Determinism Stance

### Stance: PhysX is not deterministic. We do not pretend otherwise.

Two machines running the same physics inputs will diverge. Therefore:

- The **server simulates physics**. Period.
- Clients **interpolate / extrapolate visuals**.
- The local player gets **CSP for their own robot only** — the part of the world they are most sensitive to.
- We do **not** run physics on remote-client representations of remote players. Their `Rigidbody` is set to `Kinematic = true` and driven from network state.

### Tick rate

- **Physics tick:** 50 Hz (`Time.fixedDeltaTime = 0.02f`). Already what Unity defaults to; matches `FixedUpdate`.
- **Network send rate:** 30 Hz from server → clients (every other physics tick, plus delta compression). 60 Hz from client → server for inputs (every render frame, capped at 60).
- **Interpolation buffer:** 100 ms (= 3 server snapshots). Tunable per build.
- **Network tick** in NGO 2.x is configurable via `NetworkConfig.TickRate`. We set this once at startup; gameplay code reads `NetworkManager.LocalTime.Tick` for any tick-stamped logic (input commands, prediction).

### Why not 60 Hz network?

Bandwidth × player count × block count blows up fast. 30 Hz with good interpolation is indistinguishable on a fast-paced PvP feel test (Overwatch ships at ~21 Hz; Valorant at 128 Hz but with tiny state). We start at 30 and only raise it if the feel test fails.

---

## 5. Network Architecture Layered on Existing Code

The current architecture already separates concerns the way we'd want — that's the whole point of the README's "server-authoritative mindset" rule. The job now is to **augment** existing components rather than rewrite them.

### Pattern: gameplay components stay network-agnostic; thin "Net" siblings carry the wire.

```
┌─────────────────────────────────────────────────┐
│ Robot GameObject                                │
├─────────────────────────────────────────────────┤
│ Rigidbody                                       │
│ BlockGrid       ◄─── existing, untouched        │
│ Robot           ◄─── existing, untouched        │
│ RobotDrive      ◄─── existing, untouched        │
│                                                 │
│ NetworkObject                                   │
│ NetworkRobot          ◄─── NEW: owns spawn / blueprint / ownership │
│ NetworkRobotState     ◄─── NEW: replicates aggregate health, phase │
│ NetworkRobotMovement  ◄─── NEW: pose + velocity + input commands   │
│ NetworkBlockGrid      ◄─── NEW: per-block damage / removal events  │
│ NetworkRobotCombat    ◄─── NEW: fire RPCs, projectile authority    │
└─────────────────────────────────────────────────┘
```

Rule of thumb: **if a script implements gameplay rules, it lives in the existing module. If a script reads or writes the network, it lives in `Robogame.Network`.** The two communicate via interfaces (`IDamageable`, `IInputSource`, `IMovementProvider`) and C# events that already exist.

### Concretely

- `Robot.Damaged += ...` → handled by `NetworkRobotState` on the server, which replicates the aggregate.
- `BlockGrid.BlockRemoving += ...` → `NetworkBlockGrid` on the server emits a `ClientRpc` so peers remove the same cell.
- `IInputSource` → on a remote-controlled robot, the input source is `NetworkInputSource`, which buffers commands received over the wire instead of reading the keyboard.
- `IMovementProvider.ApplyMovement` → unchanged. The provider doesn't know or care whether it's running on a server or a CSP-rewinding client.

This is the same pattern Unity's *Boss Room* uses: `ServerCharacter` + `ClientCharacter` siblings, gameplay logic in plain MonoBehaviours.

---

## 6. State Replication Strategy

A taxonomy. Every piece of state in the game lands in exactly one bucket.

### Bucket A — Configuration (sent once, never changes)

Examples: block definitions, weapon stats, arena layout, round rules.

- Loaded from `ScriptableObject` on every machine independently.
- Identified by stable IDs (`BlockId`, `WeaponId`).
- **Never replicated.** If the server says "block 47 took damage", every client already knows what block 47 is.
- Mismatch protection: ship a content-hash check at connection time (server's loaded definitions hash vs. client's). Reject mismatched clients with a clear error.

### Bucket B — Per-match construction state (sent on spawn, rare delta)

Examples: each player's loadout / robot blueprint, team assignments, round number.

- Sent via a **`SpawnRobotPayload`** struct (`INetworkSerializable`) when a `NetworkObject` spawns: `playerId`, `teamId`, `blueprintBlob` (compressed `ChassisBlueprint`), `spawnPose`.
- Clients deserialize and call existing `ChassisFactory.Build(blueprint)` locally — this reuses our singleplayer construction code path 1:1.
- Block IDs within a robot are **deterministic from the blueprint** — client and server both number cells the same way (sorted by `Vector3Int`), so we can refer to blocks later by index without sending IDs at spawn time.

### Bucket C — Continuous state (sent on every snapshot)

Examples: each robot's pose, velocity, current input, throttle.

- Replicated via NGO's `NetworkTransform` for the visual transform **on remote clients only**.
- For the **owning** client, the local rigidbody is the source of truth (CSP) and `NetworkTransform` runs in send-only mode.
- Wrapped in `NetworkRobotMovement` so we can hot-swap to a custom snapshot type if `NetworkTransform` proves too generic (likely — see [§8](#8-movement-client-side-prediction--reconciliation)).

### Bucket D — Discrete events (sent reliably, exactly once)

Examples: block destroyed, weapon fired, robot died, round won.

- `ClientRpc` with reliable delivery.
- Idempotent on the receiving side: a duplicate "block 47 destroyed" event after we've already removed it should be a no-op, not a crash.
- Event payloads are tiny structs, never object references.

### Bucket E — UI / cosmetic (replicated lazily or not at all)

Examples: scoreboard ping, kill feed text, chat, name plates.

- Free to use slow `NetworkVariable`s with infrequent writes, or batched RPCs.
- Never gates gameplay.

### What goes on `NetworkVariable` vs. `ClientRpc`?

| Pattern | Use case |
|---|---|
| `NetworkVariable<T>` | "Current value of X" that any joining client needs to know. Health, score, round phase. NGO replays the latest value to late-joiners automatically. |
| `ClientRpc` | "X happened at time T" — fire-and-forget events. Hits, kills, spawns. Late-joiners don't need to replay these. |
| `ServerRpc` | Client → server requests. Fire weapon, request build, request respawn. Always validated. |

---

## 7. The Hard Problem: Replicating Block-Based Robots

This is the part that is genuinely novel for our project and where naive NGO usage will fall over. Spelling it out in detail.

### The naive approach (do not do this)

> "Each block is a `NetworkObject` with a `NetworkTransform` and a `NetworkVariable<float>` health."

**Why it fails:**
- A 100-block robot × 8 players × 30 Hz × 16 bytes/transform = ~3.8 MB/s outbound on the host. Murders bandwidth.
- 800 `NetworkObject`s causes spawn storms on join (NGO has to send a Spawn message per object).
- Per-frame transform sync of every block is irrelevant — they're all rigid-attached to the chassis root, their world poses are derivable from the chassis pose.

### The right approach

**One `NetworkObject` per robot. Blocks are children, not network objects.**

State per block consists of two things, handled separately:

#### a) Block existence (placement)

- Sent **once at spawn** via the `SpawnRobotPayload` blueprint blob.
- Reconstructed locally on every client by `ChassisFactory.Build(blueprint)` — same code path as singleplayer.
- After spawn, blocks only ever **disappear** (we don't add blocks mid-match). So the only mutation we need to replicate is removal.

#### b) Block damage / destruction

- Server-side `BlockBehaviour.Damaged` & `Destroyed` events route through `NetworkBlockGrid`.
- Each event packs into a tiny struct:

  ```csharp
  public struct BlockHitEvent : INetworkSerializable
  {
      public ushort blockIndex;   // index into the deterministically-ordered block list, NOT a NetworkObjectId
      public ushort hpAfter;      // 0 = destroyed
      public byte   hitFlags;     // crit, splash, structural-detach, etc.
      // 5 bytes total. With reliable RPC overhead: ~12 bytes on wire.
  }
  ```

- `BlockHitEvent`s are batched per network tick into a `BlockHitBatch` and sent via a single `ClientRpc` per robot per tick. A typical hit cluster (one bullet, splash 7 cells) costs ~84 bytes. Cheap.

- Client receives the batch and applies the same destruction logic locally — *including* the structural-integrity check. The server has already validated; the client just mirrors the result. If client and server disagree on which cells became orphan debris, the server's `ClientRpc` includes the list of orphan indices and the client trusts it.

#### c) Detached debris

- Detached chunks become **non-networked physics debris** — purely visual, lifetime ≈ a few seconds. Mismatch in debris flight paths between machines is invisible because debris doesn't affect gameplay. This is the same trick *Battlefield* uses for gibs.

### Why this works

- **Bandwidth:** one robot's worth of block state during a heavy firefight is ~1 KB/s. 16 robots × 1 KB/s = 16 KB/s ≪ a typical home connection.
- **Determinism not required:** we replicate the *outcome* (which blocks are gone), not the *physics* (which projectile hit which cell at what time). PhysX divergence is irrelevant.
- **Late-join works:** the server keeps the cumulative "blocks destroyed since spawn" list and replays it to the late-joiner after they receive the initial blueprint.

### Open question

Should disabled / damaged-but-alive blocks broadcast intermediate HP, or only destruction? Recommendation: broadcast HP only when it crosses a tier boundary (full → cracked → critical → dead) so the visual damage state syncs without per-hit traffic for grazes.

---

## 8. Movement: Client-Side Prediction & Reconciliation

We follow the standard model from Glenn Fiedler's *Networked Physics* and Valve's *Source Multiplayer Networking*.

### Per-frame loop on the owning client

```
1. Sample input → InputCommand { tick, move, vertical, fireHeld, aim }
2. Append to local command buffer (last ~120 commands ≈ 2 seconds at 60 Hz)
3. Send command to server (unreliable, with last 3 commands re-included for redundancy)
4. Apply InputCommand locally to Rigidbody via existing IMovementProvider
5. Render
```

### Per-server-tick loop on the server

```
1. Drain command queue for each client (clamped + rate-limited)
2. Apply command to that client's Rigidbody (existing IMovementProvider, unchanged)
3. Step physics
4. Snapshot { tick, pose, velocity, lastProcessedCommandTick } per robot
5. Broadcast snapshots
```

### Reconciliation on the owning client (when a server snapshot arrives)

```
1. Receive snapshot { tick = T, pose, velocity, lastProcessedCommand }
2. Snap local Rigidbody to (pose, velocity)
3. Replay every command from lastProcessedCommand+1 → current local tick
4. Result: local Rigidbody is now at the predicted state for current tick,
   consistent with server up to T, with all unacked inputs re-applied
```

If predicted vs. reconciled position differs by less than a threshold (~0.05 m, ~5°), we **smooth** the visual transform toward the corrected pose over a few hundred ms instead of snapping. The Rigidbody jumps (correct), but the visible mesh eases (pleasant). Boss Room demonstrates this pattern.

### Why not just `NetworkTransform`?

`NetworkTransform` works fine for **non-owning** clients (interpolation only, no prediction). For the **owning** client, it doesn't predict, so controls feel laggy at 100ms+ RTT. Hence the bespoke `NetworkRobotMovement` component for owners.

### Implementation note

NGO 2.x has been adding a Network Animator and a Tick System but does **not** have a built-in CSP framework yet. We write our own thin one in `Robogame.Network`. This is ~300 LOC of textbook netcode and we keep it isolated so future Unity primitives can replace it cleanly.

---

## 9. Combat: Server-Authoritative Projectiles + Client Tracers

The good news: our [Projectile.cs](Assets/_Project/Scripts/Combat/Projectile.cs) was deliberately built for this. It has **no Rigidbody**, runs a swept raycast in `FixedUpdate`, and applies damage on the same frame the hit happens. That's exactly what server-authoritative projectiles look like.

### Authoritative projectile flow

1. **Client presses fire** → `ProjectileGun.RequestFire()` builds a `FireCommand { tick, aimDir, muzzlePose }` and:
   - **Sends `ServerRpc(FireCommand)`** to the server.
   - **Locally spawns a "ghost" tracer** — a visual-only `Projectile` instance that flies with no damage. This is the click-feedback the player sees.

2. **Server receives `ServerRpc`** →
   - Validates: was the gun off cooldown at `tick`? Is the aim within plausible bounds vs. the last received aim snapshot? Is the player alive?
   - If valid: spawns an authoritative `Projectile` on the server, tracks it, applies damage on hit.
   - Sends `ClientRpc(ProjectileSpawnEvent { id, originPose, velocity, ownerId })` to all clients.

3. **Clients receive the event** →
   - The owning client **replaces** their ghost tracer with the authoritative one (or just lets the ghost die naturally — the visual lie is < 1 RTT and players don't notice).
   - Other clients **spawn a non-authoritative tracer** for the visual.

4. **Server detects hit** → `ClientRpc(BlockHitBatch)` (see §7). Visual hit FX play on every client.

### Lag compensation (Phase 6+)

For PvP hit-feel at 100ms+ RTT, the server should rewind the world to the shooter's local time before testing the projectile sweep. Implementation:

- Keep a circular buffer (~500 ms) of every robot's pose per network tick.
- On `FireCommand { tick = T }` arrival, lerp every other robot's collider state to time T before running the swept raycast.
- Restore state after.

This is the *Counter-Strike* / *Overwatch* approach. Worth deferring to Phase 6 because it adds complexity and our pellets are slow enough (80 m/s) that simple snapshot-time hit testing is acceptable for early access.

### Why not let clients simulate authoritatively?

- Aimbots become trivial (`SetAim(perfect_target)`).
- Wallhacks become trivial (the client computes hits, so it can claim hits through walls).
- One latency spike → desyncs that never resolve.

The cost of always doing damage server-side is one extra RTT for the *result of the hit*. Players tolerate that fine; they don't tolerate getting one-shot by a hacker.

---

## 10. Scene & Lifecycle Flow

### Connection sequence

```
1. Bootstrap scene loads (NetworkManager exists; not started yet)
2. User picks "Host" or "Join" in main menu
   - Host: SteamMatchmaking.CreateLobby() → on success, StartHost()
   - Join: lobby join callback → grab host SteamID → StartClient(hostSteamId)
3. After NGO connection established:
   - Client uploads its CurrentBlueprint blob via ServerRpc
   - Server validates blueprint (cell count, weapon count limits)
   - Server adds player to a pending-spawn list
4. Round-start trigger (host pressed start, or queue full):
   - Server NetworkSceneManager.LoadScene(arena)
   - On scene loaded callback, server spawns each player's Robot
     NetworkObject from ChassisFactory + their stored blueprint
   - Server flips RoundPhase NetworkVariable to InProgress
5. Round end: server flips RoundPhase to Postgame, broadcasts results,
   NetworkSceneManager.LoadScene(garage) after a delay.
```

### Scene management

- Use NGO's `NetworkSceneManager`. It handles the synchronized load handshake — clients can't spawn into the arena before the server says "scene loaded."
- Bootstrap scene stays loaded throughout (additive). Houses `NetworkManager`, `GameStateController`, `SettingsHud`. This is already roughly the structure we have.
- Per-arena scene loaded additively on top.

### Late join / mid-match join

- v1: **disabled**. Lobby locks at round start. Cleanest possible bootstrap.
- v2: spectator-only join, transition to player at next round.
- v3: full mid-match join with state catch-up. Real engineering. Out of scope for ≤ Phase 6.

### Host migration

- v1: **not supported.** Host disconnect = match over. Steam's lobby ownership transfer remains valid (lobby leader changes), but the in-progress match dies. Ship without it.
- v2 candidate: only if community demand justifies the implementation cost (it is *significant*).

---

## 11. Steam Integration

### Why Steam is the v1 target

- Largest PC audience for a niche PvP indie.
- Free relay infrastructure (SDR) hides player IPs and handles NAT punch.
- Free lobby system — no UGS bills, infinite scale.
- Free overlay, friends list, invites, rich presence.
- Steam Cloud for blueprint storage at zero ops cost.
- Workshop for community blueprints (Phase 8+ if we get there).

### Steamworks SDK integration plan

1. **App ID provisioned on Steamworks Partner site.** Local dev uses `480` (Spacewar) until we have one.
2. **Steamworks bindings** (Facepunch.Steamworks chosen — Phase 5 reconsider): drop the DLL into `Assets/Plugins/`, configured per-platform.
3. **Initialization**: `SteamClient.Init(appId)` in `GameBootstrap.Awake()`. `SteamClient.RunCallbacks()` in an `Update` somewhere persistent (or use `SteamClient.RunCallbacksAsync()` if available).
4. **Lobby flow**:
   - Create: `await SteamMatchmaking.CreateLobbyAsync(maxMembers)` → set lobby data (game mode, map, password hash).
   - Browse: `SteamMatchmaking.LobbyList.WithKeyValue(...)` → list UI.
   - Join: `await lobby.Join()` → fires `OnLobbyEntered` → host's SteamID becomes the connect target.
5. **Transport**: switch NGO's `UnityTransport` for `SteamNetworkingSocketsTransport` (community package). The host opens a P2P listener on its SteamID; clients connect to that SteamID. SDR handles the rest.
6. **Rich presence / invites**: `SteamFriends.SetRichPresence("status", "In Match - Tundra")`. Steam invite → joins our lobby → joins our NGO session.
7. **Steam Cloud for blueprints**: write blueprint JSON via `SteamRemoteStorage.FileWrite`. On launch, sync from cloud → local disk; user-facing blueprint slots resolve from disk.

### Editor / non-Steam dev path

We do **not** require Steam to run the game from the editor. The transport is selected at runtime:

```csharp
if (SteamClient.IsValid) SwitchTransport<SteamNetworkingTransport>();
else                     SwitchTransport<UnityTransport>();
```

Internal builds use UTP + Unity Relay; shipping builds use Steam.

### Steam-specific gotchas to plan for now

- **`steam_appid.txt`** must sit next to the executable (and in the project root for editor runs). `.gitignore` it.
- **Restart-via-Steam check**: `SteamApi.RestartAppIfNecessary(appId)` at process start in shipping builds, so launches outside of Steam re-launch under it.
- **Authentication tickets** (`SteamUser.GetAuthSessionTicket`) for proving identity to dedicated servers later.
- **Lobby chat**: Steam provides it for free; don't reinvent.

---

## 12. Backend Services

### Phase 2–5 (early access): zero backend.

- Lobbies on Steam.
- Storage in Steam Cloud.
- Identity via `CSteamID`.
- "Backend" is the host's machine.

### Phase 6+: dedicated servers.

Two viable hosting paths; pick based on price-at-scale once we have actual numbers.

| Provider | Pros | Cons |
|---|---|---|
| **Unity Multiplay** | Tightest integration with NGO + UGS Matchmaker. Auto-scales. Same Linux build as our internal CI artifact. | Costs ramp. UGS pricing has shifted twice in two years — re-evaluate at decision time. |
| **Hathora** | Cheaper at low CCU. Simpler pricing. Gives you a Docker image to deploy. | Smaller team, less Unity-specific tooling. |

### Phase 7+: Persistent player progression.

When (if) we add accounts, ranked play, cosmetics, etc., we evaluate:

1. **PlayFab** — Microsoft, mature, expensive.
2. **Nakama** — open-source, self-hostable, has good Unity bindings.
3. **Custom service** (Postgres + a thin .NET API on Fly.io or Railway) — most flexible, most ops burden.

Decision deliberately deferred. Premature backend-building is its own genre of project death.

---

## 13. Anti-Cheat Surface

We are not solving cheat detection. We *are* designing so that cheating buys you less.

### Hardenings baked into the architecture

- **No client damage authority.** Aim hacks become "I aim perfectly", not "I delete enemies at will."
- **Server-side cooldown / rate limits** on every `ServerRpc`. Macro-spamming Fire is just Fire-at-the-rate-the-gun-allows.
- **Server-side input bounds checking.** Aim deltas bounded per tick; movement vector magnitude clamped.
- **Content hash check on connect.** Modified `BlockDefinition` assets get the client kicked.
- **Authoritative HP / score state** never arrives at clients except via `NetworkVariable` from the server.

### Things we will not do at v1

- Kernel-mode anti-cheat (EAC, BattlEye). Requires storefront partnership and is overkill for early access of an indie.
- Client integrity attestation. Steam handles enough of this via VAC if we opt in (we do not, initially).

### Things we will do at Phase 6 (dedicated servers)

- VAC enable on Steam (free, modest effectiveness).
- Server-side replay logging — every match's input + snapshot stream archived for 24h. Reports trigger replay review.
- Server-side rate-limit telemetry — flag accounts whose `ServerRpc` patterns suggest macroing.

---

## 14. Folder, Assembly, and File Plan

The existing `Assets/_Project/Scripts/Network/` folder has `Robogame.Network.asmdef` and a `.gitkeep`. The plan is to populate it without polluting other modules.

### Final intended structure

```
Assets/_Project/Scripts/Network/
├── Robogame.Network.asmdef                  // refs: Core, Block, Robots, Combat, Movement, Player, Input, Gameplay
├── Bootstrap/
│   ├── NetworkBootstrap.cs                  // creates / configures NetworkManager; transport switching
│   ├── ContentHashGuard.cs                  // computes & validates content hash on connect
│   └── NetworkServiceLocator.cs             // tiny SL for Network-only singletons
├── Lobby/
│   ├── ILobbyService.cs                     // abstraction over Steam vs. Unity Lobby
│   ├── SteamLobbyService.cs                 // implementation
│   ├── UgsLobbyService.cs                   // implementation (editor / internal)
│   └── LobbyHud.cs                          // browse / create / join UI
├── Transport/
│   ├── ITransportProvider.cs
│   ├── UnityTransportProvider.cs
│   └── SteamTransportProvider.cs
├── Robot/
│   ├── NetworkRobot.cs                      // owns spawn, blueprint, ownership, despawn
│   ├── NetworkRobotState.cs                 // health phases, alive/dead NetVar
│   ├── NetworkRobotMovement.cs              // CSP for owner; replication for remotes
│   ├── NetworkBlockGrid.cs                  // BlockHitBatch ClientRpc fan-out
│   └── NetworkRobotCombat.cs                // FireCommand ServerRpc; ProjectileSpawnEvent ClientRpc
├── Prediction/
│   ├── ClientCommandBuffer.cs
│   ├── ServerCommandQueue.cs
│   ├── PredictionTickRunner.cs
│   └── ReconciliationSmoother.cs
├── Snapshot/
│   ├── INetSnapshot.cs
│   ├── RobotPoseSnapshot.cs
│   └── SnapshotInterpolator.cs              // for non-owning clients
├── Auth/
│   ├── ISteamSession.cs
│   └── SteamSessionTicketProvider.cs        // Phase 6 dedicated-server auth
├── Diagnostics/
│   ├── NetworkStatsHud.cs                   // RTT, loss, jitter, snapshot age (debug-only)
│   └── ReplayRecorder.cs                    // Phase 6+
└── Debug/
    └── NetcodeFakeLatencyController.cs      // forces UTP latency injection in editor
```

### Assembly references

`Robogame.Network.asmdef` references everything gameplay-side. Crucially, **gameplay `.asmdef`s do NOT reference `Robogame.Network`.** This enforces the "gameplay is network-agnostic" rule at compile time. Cross-cutting communication happens through the existing interfaces (`IInputSource`, `IDamageable`, `IMovementProvider`) and C# events on existing components.

The only exception will be `Robogame.Gameplay` (which contains scene controllers like `ArenaController`) — those need to know whether we're in net mode to skip e.g. local-only respawn. We address this with a thin `INetworkContext` interface in `Robogame.Core` that the Network module implements; gameplay queries it.

### `Robogame.Core` additions

```
Assets/_Project/Scripts/Core/
├── INetworkContext.cs                       // bool IsServer; bool IsClient; bool IsHost; bool IsOnline;
└── NetworkContext.cs                        // singleton, defaults to "offline" if no Network bootstrap ran
```

This is the *only* network-aware type that gameplay code is allowed to import.

---

## 15. Phased Rollout

Each phase is a shippable internal milestone. Each ends with a tag and a CHANGES.md entry.

### Phase 0 — Architecture preflight (this is where we are)

- ✅ Server-authoritative mindset baked into singleplayer code
- ✅ Projectiles are non-Rigidbody / swept raycast (CSP-ready)
- ✅ Blueprint serialization (`BlueprintSerializer`) exists
- ✅ Block grid mutations go through events (`BlockPlaced`, `BlockRemoving`)
- ✅ Input is abstracted via `IInputSource`
- ✅ Movement is abstracted via `IMovementProvider`
- ⬜ Add `INetworkContext` to `Robogame.Core` (offline-default stub)
- ⬜ Add `BlueprintBlob` packed binary representation (for wire) alongside the existing JSON form

**Exit criterion:** every gameplay system can be queried for "is this the authoritative instance?" and answers correctly in singleplayer (always true) and offline (always true) — laying the foundation for the same query returning "only on the server" later, without any other code changing.

### Phase 1 — NGO baseline (no Steam, no prediction)

Goal: two editor instances connect via UTP loopback, each spawns a robot, both can drive around and shoot, damage replicates.

- Add NGO + UTP packages.
- `NetworkBootstrap` creates `NetworkManager`, configured with `UnityTransport`.
- `NetworkRobot` + `NetworkRobotMovement` (using stock `NetworkTransform` for now — CSP comes in Phase 3).
- `NetworkRobotCombat` with `ServerRpc(FireCommand)` and `ClientRpc(ProjectileSpawnEvent)`.
- `NetworkBlockGrid` with `BlockHitBatch` `ClientRpc`.
- Tiny dev menu HUD: "Host on 7777" / "Join 127.0.0.1:7777" buttons, replacing the F1 menu's role for net testing.

**Exit criterion:** playable, laggy, ugly 1v1 over LAN.

### Phase 2 — UGS Relay + Lobby (no Steam yet)

- Add UGS Relay & Lobby packages.
- `UgsLobbyService` + `LobbyHud`.
- Switch transport to UTP-with-Relay for online play.
- Anonymous Unity Authentication.

**Exit criterion:** two devs in different houses can join via a 6-letter lobby code.

### Phase 3 — CSP for the local player

- `ClientCommandBuffer` / `PredictionTickRunner` / `ReconciliationSmoother`.
- Replace stock `NetworkTransform` for owner with `NetworkRobotMovement` custom snapshot.
- Latency injection via UTP simulator. Validate at 50, 100, 200 ms.

**Exit criterion:** controls feel local at 150 ms RTT. Reconciliation snaps are invisible during normal play.

### Phase 4 — Robust block destruction + structural integrity over the wire

- `BlockHitBatch` batching + ordering guarantees.
- Late-spawned debris cleanup.
- Mass-loss / CPU-loss replication and synchronized "robot destroyed" state.
- Stress test: 8 robots, sustained fire, no desyncs over 5 minutes.

**Exit criterion:** 4v4 internal playtest with zero "I shot it but it's still alive on my screen" reports.

### Phase 5 — Steam integration

- Add Facepunch.Steamworks (or Steamworks.NET).
- `SteamLobbyService`, `SteamTransportProvider`.
- App ID, `steam_appid.txt`, restart-via-Steam guard.
- Steam Cloud blueprint sync.
- Beta branch on Steam Partner.

**Exit criterion:** closed-friends Steam beta, joining via Steam friends list, no IP exchange.

### Phase 6 — Dedicated server build + lag compensation

- Headless Linux build target.
- `StartServer()` path; CLI args for port, lobby ID.
- Lag-compensated hit testing.
- Multiplay or Hathora deployment pipeline.
- Steam auth ticket validation on the server.

**Exit criterion:** auto-provisioned dedicated server takes a 4v4 match end to end, anti-cheat-relevant logs captured.

### Phase 7 — Voice (Vivox), nameplates, scoreboard, kill feed

- Quality-of-life multiplayer features.
- All the things that make it feel like a real game and not a tech demo.

### Phase 8 — Persistence, ranked, cosmetics

- The "real backend" question. Defer until Phase 7 ship clarifies whether the audience exists.

---

## 16. Testing Strategy

### Editor multiplayer

- **Multiplayer Play Mode (MPPM)** — Unity's built-in tooling for running multiple Player instances from one editor. Free, fast iteration. **Use this from Phase 1 onward.**
- **ParrelSync** as backup if MPPM proves flaky. (It has historically.)

### Network conditioning

- UTP's built-in `NetworkSimulatorParameters` — set RTT, loss, jitter from a debug HUD. Required to verify CSP feel without needing real-world conditions.
- Test matrix: { 30, 100, 200 } ms RTT × { 0, 2, 5 }% loss × { 0, 30 } ms jitter.

### Automated tests

- Play-mode test scene that hosts a server, spawns a synthetic client, fires a fixed input sequence, asserts on resulting snapshot deltas. Catches regressions in `BlockHitBatch` reliability and CSP reconciliation.
- Headless server CI smoke test: launch Linux server, connect a synthetic client, verify spawn → fire → hit → despawn round-trip in < 2 s. Runs in GitHub Actions on every PR.

### Determinism guard

- Even though we don't rely on determinism, divergence between server's predicted client position and the client's own prediction should stay tiny for similar inputs. A regression test asserts that drift is < 0.5 m / second of identical input. Catches "physics setting changed and broke prediction" bugs.

---

## 17. Risks & Open Questions

| # | Risk | Mitigation |
|---|---|---|
| R1 | NGO 2.x is still under heavy churn; major API breaks possible. | Pin package versions. Wrap NGO types behind our own interfaces in `Robogame.Network` so a future breaking change is local. |
| R2 | PhysX divergence is worse than we expect on robots with many joints (wheels, hover lifts). | Robots use a single `Rigidbody`; blocks are just colliders parented to the chassis. No joints between blocks. The risk is real for wheels — we may need to tighten `WheelCollider` or replace it with our custom `WheelDrive`. |
| R3 | Block-destruction event flood at high projectile rates. | Batch per tick (`BlockHitBatch`). If batches still saturate, downgrade graze hits to "HP dirty" flags and only send full destruction events. |
| R4 | Steam transport community packages unmaintained. | Fork our chosen one into the repo at adoption time. Worst case write our own thin wrapper around `SteamNetworkingSockets` — it's ~500 LOC for the NGO transport interface. |
| R5 | Late-join correctness (not v1, but v2). | Keep a server-side authoritative `BlockHitBatch` log per robot since spawn so we can deterministically replay it to a late-joiner after their initial blueprint construction. |
| R6 | Cheaters. | Server-authoritative everything. Beyond that, accept some level until budget allows EAC. |
| R7 | We bet on host-authoritative for v1 and the host's hardware/connection becomes a complaint vector. | This is well-understood and acceptable for early access (Robocraft did it for years). Phase 6 dedicated servers fix it. Communicate the host model honestly to early-access players. |

### Open design questions to revisit at each phase boundary

- **Q1**: Do we send block damage as deltas (HP-after) or as causes (hit-by-projectile-X-with-damage-Y)? Current plan is deltas (smaller). Re-evaluate when we add resistance/armor systems where the cause matters for replays.
- **Q2**: Do we replicate aim direction continuously, or just at fire time? Continuously is needed for nameplate aim cones / spectator view. Defer to Phase 7.
- **Q3**: How big can a blueprint blob get and stay in one MTU? Need to measure. If > 1200 bytes, fragment via NGO's reliable channel. Confirm `BlueprintSerializer` produces a packed byte form, not just JSON.
- **Q4**: Voice — Vivox vs. Steam Voice? Steam Voice is free with the platform and is the obvious choice for v1 once we're on Steam transport. Vivox only if we need cross-store voice eventually.

---

## 18. References

### Unity-specific (read these before writing netcode in this repo)

- [Netcode for GameObjects documentation](https://docs-multiplayer.unity3d.com/netcode/current/about/)
- [Boss Room sample — small-scale co-op reference](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop) — closest analog to our architecture
- [Multiplayer Center (Unity 6)](https://docs.unity3d.com/Manual/multiplayer-center.html) — package selector + comparisons
- [Unity Transport (UTP) docs](https://docs-multiplayer.unity3d.com/transport/current/about/)
- [Unity Multiplay docs](https://docs.unity.com/multiplay/)
- [NGO Multiplayer Tools](https://docs-multiplayer.unity3d.com/tools/current/about/) — profiler, runtime stats

### Foundational netcode reading

- [Glenn Fiedler — *Networked Physics* series](https://gafferongames.com/categories/networked-physics/) — the canonical reference. Read in order.
- [Valve — *Source Multiplayer Networking*](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking) — the lag compensation model we'll borrow.
- [Overwatch GDC talks](https://www.youtube.com/watch?v=W3aieHjyNvw) — server-authoritative state synchronization at scale.
- [Gabriel Gambetta — *Fast-Paced Multiplayer*](https://www.gabrielgambetta.com/client-server-game-architecture.html) — friendlier-than-Fiedler intro to CSP & reconciliation.

### Steam

- [Steamworks SDK docs](https://partner.steamgames.com/doc/sdk)
- [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) — chosen bindings
- [Steam Datagram Relay overview](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)
- [com.community.netcode.transport — multiplayer-community-contributions](https://github.com/Unity-Technologies/multiplayer-community-contributions)

### Internal docs (this repo)

- [README.md](../README.md) — architecture principles & multiplayer roadmap table
- [docs/BEST_PRACTICES.md](BEST_PRACTICES.md) — coding standards
- [docs/ROBOCRAFT_REFERENCE.md](ROBOCRAFT_REFERENCE.md) — what we are/aren't borrowing from the original

---

*Last updated: May 1, 2026 — initial draft. Update on every phase boundary.*
