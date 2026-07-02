# 129 — MCP bridge migration: Unity official → CoplayDev MCP for Unity

Research + migration prep. Status: **validated 2026-07-02** (see checklist
outcomes below).

## Why

The official bridge (`com.unity.ai.assistant` 2.9.0-pre.2) "Connection
revoked" friction was diagnosed to the source, not guessed:

- **Seat-entitlement eviction** (2.7.0+): direct MCP connections are
  capped at `Account.settings.ConnectionLimits.AllowedMcpConnections`
  (free tier = 0 per Unity Issue Tracker). Caps reset to Unlimited on
  every domain reload, re-tighten when entitlements load, and tightening
  **evicts live transports** (`ConnectionCensus.EvictOverflowAsync`).
  The evicted client reconnects into the tight cap → pinned Denied →
  the blanket "Connection revoked" error. Bug tracked by Unity, marked
  fixed in 2.7.0-pre.3, still reported broken through 2.12.0-pre.2.
- **Identity churn**: claude.exe is unsigned, so identity = exe path,
  which embeds the Claude Code version → every auto-update is a "new"
  client needing re-approval. Six stale denied records were in
  `Library/AI.MCP/connections-v2.asset`.

## Decision

Migrate to CoplayDev **MCP for Unity** (11.3k stars, MIT, v10.0.0
current; no seat gating, no approval flow). Pinned **v9.7.3**
(conservative; verified `ManageProfiler`, `Cameras` tool sources exist
at that tag). Tool mapping: `read_console`, `manage_editor`,
`manage_scene`, `manage_profiler` (14 actions), `manage_camera`
(screenshot_multiview). Known loss: no arbitrary-C#-eval equivalent of
`Unity_RunCommand` (mitigation: their `execute_custom_tool` supports
project-authored C# tools). Gains: `run_tests`, `manage_build`,
multi-instance routing (relevant to future MPPM netcode testing).

## Changed

- `Packages/manifest.json`: + `com.coplaydev.unity-mcp` @ v9.7.3 (git URL).
- `.claude/agents/qa-verifier.md`, `perf-checker.md`: tool names →
  `mcp__UnityMCP__*`; profiler steps generalized; agents told to
  ToolSearch + report actual names if a name doesn't resolve.
- `.claude/settings.json`: permission allowlist swapped to new prefix.
- `.claude/hooks/stop_remind_unity_console.ps1`: reminder names the new
  read_console tool.
- Installed `uv` 0.11.26 (their server prerequisite; Python 3.14 present).

## Validation outcomes (2026-07-02)

1. ✅ Package imported; server up on HTTP `127.0.0.1:8080`, session
   active as `robogame`.
2. ✅ Registered with Claude Code; `claude mcp list` shows `UnityMCP ✓`.
3. ✅ Tool names resolved exactly as written in the agent files
   (`mcp__UnityMCP__*`, no corrections needed). Exercised: read_console,
   manage_scene get_active/get_hierarchy paths, play → console check →
   stop (clean), manage_profiler profiler_status, manage_camera
   screenshot (inline image OK). `screenshot_multiview` action resolves
   (errored only on renderer-less Bootstrap scene — expected). One
   qa-verifier dispatch: PASS.
4. ✅ `claude mcp remove unity-mcp` done (was local scope, not user).
   Old bridge disabled THROUGH the new bridge: `execute_code` +
   `unity_reflect` located `Unity.AI.MCP.Editor.UnityMCPBridge`, set
   `Enabled=false` + `Stop()`; persisted via `MCPSettingsManager`
   (`bridgeEnabled=false`, saved). All 3 orphaned `relay_win.exe`
   killed, no respawn, port 9002 free. Correction to § Decision: the
   "no arbitrary-C#-eval" loss was wrong — v9.7.3 ships `execute_code`
   (Roslyn, method-body eval), a full `Unity_RunCommand` equivalent.
   Still open (user decision): whether `com.unity.ai.assistant` stays
   (Assistant window) or goes; `cleanup_orphan_relays.ps1` retires
   with it.
5. ✅ Memories updated: revoked-fix ladder marked RETIRED (applies only
   if the official bridge returns); headless-rig memory still accurate
   as written. No hud/verification doc changes needed — tool behavior
   matched the mapping in § Decision.
