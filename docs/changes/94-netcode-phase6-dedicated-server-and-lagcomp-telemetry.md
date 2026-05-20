# 94 — Netcode Phase 6: dedicated server + lag-comp telemetry

> Status: **Phase 6 shipped (telemetry only).** `StartServer()` path +
> `-server / -port / -lobbyId` CLI args + headless build target docs +
> Lag-comp history infrastructure (ring buffer + registry + ray-vs-sphere
> telemetry). Lag-comp does NOT apply damage — `ProjectileWorld`'s live
> sweep stays authoritative. EditMode 250+/253 (6 new tests, baseline
> unchanged).

## Why this scope and not "real" lag-comp

The doc's lag-comp motivation is "PvP hit-feel at 100ms+ RTT" — that
framing matches hitscan weapons. Mid-planning we realized this game's
weapons are slow projectile (SMG pellet ≈ 80 m/s, cannon ball arcing,
bomb bay). The doc itself says "pellets slow enough that simple
snapshot-time hit testing is acceptable for early access." Variant C
(bounding-volume rewind) applied to damage would effectively turn
every weapon into hitscan at fire time — destroying the leadable /
dodgeable feel that's the entire point of slow projectiles.

The user picked telemetry-only after I surfaced this. We ship the
lag-comp infrastructure (history, registry, query, ray-vs-sphere) and
*log* when lag-comp would have called a hit, but `ProjectileWorld`'s
live sweep remains the sole damage authority. If a future hitscan
weapon type ships, flipping telemetry → authoritative is a one-method
change in `NetworkRobotCombat.RunLagCompTelemetry`.

The "I shot them, why didn't it land?" diagnostic value is real:
under 200 ms RTT, the log fires whenever a remote client's claimed-tick
aim would have intersected a robot's historical bounding sphere but
the live sweep at server-now-time missed. Concrete signal for the 4v4
MPPM playtest.

## What landed

**`LagCompHistory`** (`Scripts/Network/Robot/LagCompHistory.cs`). Per-
robot `NetworkBehaviour` that holds a 25-entry ring of `RobotBoundsSnapshot`
records (Pos, Radius, Tick). 500 ms history at 50 Hz tick rate. Cached
sphere radius — set once at chassis build via `SetChassisBounds(float)`,
not recomputed per tick. `TryQueryAt(tick, out snap)` scans for the
nearest tick in the buffer; rejects queries more than `Capacity` ticks
away from any live sample (the rewind-window guard).

**`LagCompRegistry`** (`Scripts/Network/Robot/LagCompRegistry.cs`).
Static `Dictionary<ulong, LagCompHistory>` keyed by `NetworkObjectId`.
`SubsystemRegistration` reset is mandatory per the project's "statics
survive domain reload" failure mode. `QueryAll(tick, list)` fills a
caller-supplied list — zero allocation in steady state.
`QueryAllInvocationCount` exposed as a test seam.

**`NetworkRobotCombat` integration.** On server-side chassis build,
attach `LagCompHistory` to the chassis root and call `SetChassisBounds`
with a radius computed as `max(||entry.Position||) + 0.5` over all
blueprint entries. Per-FixedUpdate `Sample(transform.position, tick)`
on the server. In `FireCommandServerRpc` after the existing aim and
cooldown gates, run `RunLagCompTelemetry(cmd)`: skip if shooter is the
host's own client (zero RTT, no rewind needed); otherwise
`LagCompRegistry.QueryAll(cmd.Tick, scratch)`, ray-vs-sphere
intersection against every entry except the shooter, log the nearest
hit under `MaxLagCompRangeMetres = 800f`. `LagCompTelemetryHitCount`
counter exposed for diagnostics.

**`StartServer()` + CLI args** (`NetworkBootstrap.cs`). Third entry
point alongside `StartHost` and `StartClient`. Pure server — no
loopback client; bind address only. `ParseAndApplyCliArgs` reads
`-server`, `-port`, `-lobbyId` from `Environment.GetCommandLineArgs`;
auto-starts only when `Application.isBatchMode` is true (so editor /
interactive builds aren't surprised). `-lobbyId` is parsed but unused
until Phase 5 Steam — wire-stable now so the launch flag doesn't need
re-deployment when Steam lobby join lands.

**Headless Linux build target** — Player Settings configuration, not
code. Documented in a comment block at the top of the `Phase 6 — dedicated-
server CLI` section of `NetworkBootstrap.cs`: Linux platform, Server
Build enabled, IL2CPP, Strip Engine Code. Launch line: `./RobogameDedicatedServer -batchmode -nographics -server -port 47777`.

**`NetDevHud` F8** — adds an F8 hotkey to `StartServer()` from the
editor for in-editor headless testing, IMGUI status line reflects.

## Tests

Six EditMode tests in `Tests/EditMode/Network/LagCompHistoryTests.cs`:
exact-tick query, closest-within-window query, far-outside-window
returns false, empty-buffer returns false, ring overflow overwrites
oldest, Reset clears but preserves radius.

The networked side — registration on spawn, lag-comp query inside
`FireCommandServerRpc`, ray-vs-sphere telemetry — is exercised
integration-style under MPPM (or via the eventual headless-server CI
smoke test, when a Linux build environment is available — see §16
carry-forward).

## What's deferred

- **Lag-comp damage application** — telemetry-only by design; flip to
  authoritative requires a `NetworkRobotCombat` change AND a decision
  about how to suppress double-damage with `ProjectileWorld`'s live
  sweep. Not on this game's roadmap until a hitscan weapon ships.
- **`com.unity.multiplayer.tools` Network Profiler integration** —
  the package was added in Phase 3.6 for the latency HUD but its
  profiler views aren't surfaced; that's a Phase 7 dev-quality task.
- **Headless server CI smoke test** — would launch the dedicated build
  in a Linux container, connect a synthetic client, fire one shot,
  assert `BlockHitEvent` is received. Requires Linux build infra not
  available locally. Flagged for whenever GitHub Actions / cloud CI
  for this project is wired.
- **Lag-comp under split TickRate / physics-rate** — `Debug.Assert`
  not added in this session. Today `NetworkBootstrap.TickRateHz` = 50
  which matches `Time.fixedDeltaTime = 0.02f` exactly. If TickRate
  ever diverges from physics rate, the lag-comp tick stamping needs
  re-auditing (the FireCommand.Tick is the NGO `LocalTime.Tick`, not
  a physics-tick count).

## Files

New: `Scripts/Network/Robot/LagCompHistory.cs`,
`Scripts/Network/Robot/LagCompRegistry.cs`,
`Tests/EditMode/Network/LagCompHistoryTests.cs`. Edited:
`Scripts/Network/Robot/NetworkRobotCombat.cs` (chassis-radius
compute + FixedUpdate sampling + telemetry hook in FireCommandServerRpc),
`Scripts/Network/Bootstrap/NetworkBootstrap.cs` (`StartServer` +
CLI-arg parser + headless build target docs),
`Scripts/Network/Bootstrap/NetDevHud.cs` (F8 hotkey + IMGUI line),
`docs/subsystems/netcode.md` (§15 Phase 6 ticked), this log + README
index.
