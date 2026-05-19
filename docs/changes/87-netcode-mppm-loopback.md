# 87 — Phase 1 MPPM loopback: bring-up, debug, milestone

> Status: **Phase 1 functional milestone hit.** Two MPPM instances over
> UTP loopback both spawn from blueprint, drive, shoot, with damage and
> destruction replicating. Tagged `phase-1-mppm-loopback`. ~10 commits
> on top of session 86's compile-verified code; everything `dotnet
> build`-green.

## Why this session

Session 86 closed Phase 1 in code (NGO/UTP packages, the five Net
siblings, scene flow, server gates). The handoff §6 MPPM exit was
user-side. The user is a netcode beginner; this was the bring-up +
debug pass to get the exit criterion actually firing end-to-end.

## What shipped

**Bring-up glue (sessions 86 deferred this so the user wouldn't have
to wire it):**
- `NetworkBootstrap` registers the robot prefab from
  `Resources/RobotNetPrefab` at runtime via `AddNetworkPrefab`, so the
  NetworkManager prefab list never has to be hand-edited.
- `NetworkRobotSpawner` (auto-bootstrapped) spawns each connected
  client's robot server-side, tracks them, and (per the late-joiner
  fix below) resends configs as needed.

**Three concrete fixes the run-iteration loop surfaced:**
1. **`NetDevHud` actions on hotkeys (F9/F10/F11), not IMGUI buttons.**
   The dead-button symptom: in the arena the cursor is locked and
   `FollowCamera`'s click-to-recapture eats the click before IMGUI
   processes it — exactly the gotcha already documented in
   architecture.md. Switched to Input-System keyboard hotkeys.
2. **Host connect address `127.0.0.1`, not `0.0.0.0`.** A host runs an
   internal loopback client too; that client connects to the *connect*
   address, so it must be a real reachable address. `0.0.0.0` is only
   valid as the *listen* (bind-all) address. Wrong choice failed the
   UTP socket immediately.
3. **Late-joiner robot config.** The host spawns its own robot at
   host-start, before any client is connected — its broadcast
   `ConfigureClientRpc` reached nobody. Fix: store the payload, expose
   `ServerSendConfigTo(clientId)`, and the spawner re-sends every
   existing robot's config targeted to a newly-connecting client.

**The real architectural one — proper §10 flow:** every test was
"two unrelated singleplayer games" because the arena was entered in
singleplayer mode *before* networking started — `ArenaController.Start`
spawned a local non-networked chassis and bound the camera to it
(Step-9's `IsServer` guard covered `RespawnPlayer` but not the direct
`Start` spawn). Implemented the NETCODE_PLAN §10 flow:
- Connect from the MainMenu (hotkeys already work there).
- Server, on first remote-client connect, drives
  `NetworkSceneManager.LoadScene(Arena)` over NGO's synchronized
  handshake — every peer transitions together.
- `ArenaController.Start` now runs with `IsOnline==true` → new online
  branch skips all local SP spawn.
- On `LoadEventCompleted` the server spawns every connected player's
  robot via `NetworkRobotSpawner.ServerSpawnAllConnected`.
- The owning client's robot raises `NetworkPlayerBridge.LocalOwnerRobotReady`
  (new Core type — keeps the gameplay-never-refs-Network asmdef
  contract); `ArenaController` binds the local camera + HUDs to that
  networked robot.

**Operational nits along the way:**
- `OnDestroy` calls `NetworkManager.Shutdown` if listening, so the UDP
  socket is released between Play sessions (lingering bind = "socket
  itself has failed" on the next host).
- Default port 7777 → **47777** (7777 was held by something on the
  dev box even after a clean Unity restart).
- Stripped the `[NetDiag]` bring-up scaffolding once the loop was
  green (was explicitly temporary).

## Plan corrections recorded

- Handoff §3.4 said "gate server-only actions with `IsServer`." For an
  online *host* that gate still passes (host is server), so the
  host's `ArenaController` would have spawned a local non-networked
  chassis *and* the networked one — double world. In our NGO design
  the Network layer owns spawning in any online session, so the
  correct gate is `IsOnline` (offline-only). Six ArenaController
  guards updated; singleplayer stays byte-identical because offline
  `IsOnline==false`.
- Step 86 chose NetworkVariable-vs-RPC for the spawn payload and
  picked RPC on the reasoning "no late-join in v1". The host's own
  robot makes every client effectively a late-joiner; the targeted-
  resend fix preserves the RPC choice without needing a managed
  NetworkVariable refactor.

## Deferred (recorded so it doesn't decay)

The explicit *validated* `FireCommand` `ServerRpc` (cooldown / aim
bounds, NETCODE_PLAN §9 step 2 / §13) and the cosmetic
`ProjectileSpawnEvent` `ClientRpc` tracer both need a sanctioned
`ProjectileWorld` spawn-observation hook (Combat-tier change). First
task of the next netcode phase. Phase-1 consequence today: damage +
destruction replicate cleanly; observer-side projectile tracers are
the "ugly" the Phase-1 exit criterion explicitly accepts.

## MPPM-exit procedure (now the canonical 1v1 test)

1. Editor: ensure Bootstrap, MainMenu, Arena are in Build Settings.
2. Enable **Window → Multiplayer Play Mode → Player 2**.
3. Press Play from `Bootstrap.unity`. Stay on MainMenu in both windows.
4. Click main window → **F9** (Host on 47777).
5. Click Player 2 window → **F10** (Join 127.0.0.1:47777).
6. Server auto-loads Arena synchronized → both robots visible in both
   windows, owner camera follows owner robot, fire/damage replicates.
   **F11** stops the session.

## Files

Edited: `NetworkBootstrap`, `NetDevHud`, `NetworkRobot`,
`NetworkRobotSpawner`, `ArenaController` (6 IsServer→IsOnline guards +
online Start branch + camera handler). New: `Core/NetworkPlayerBridge`.
NETCODE_PLAN §15 Phase 1 ticked; this log; README index updated.
