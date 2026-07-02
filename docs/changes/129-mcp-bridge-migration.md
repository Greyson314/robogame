# 129 — MCP bridge migration: Unity official → CoplayDev MCP for Unity

Research + migration prep. Status: **prepared, validation pending** (needs
one editor session — see checklist below).

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

## Not yet done (validation checklist)

1. Open the editor, let the package import.
2. Window → MCP for Unity → Configure All Detected Clients (registers
   the server with Claude Code via `claude mcp add`).
3. Fresh Claude session: verify tool names (`claude mcp list`, ToolSearch
   "UnityMCP"), correct agent files if the registered name differs from
   `UnityMCP`, exercise read_console / play mode / manage_profiler /
   screenshot_multiview, dispatch qa-verifier once.
4. Decommission official bridge: disable in Project Settings → AI →
   Unity MCP, `claude mcp remove unity-mcp` (user scope), then decide
   whether `com.unity.ai.assistant` stays (Assistant window) or goes.
   `.claude/hooks/cleanup_orphan_relays.ps1` can retire with it.
5. Update the two bridge memories + hud/verification docs if tool
   behavior differs.
