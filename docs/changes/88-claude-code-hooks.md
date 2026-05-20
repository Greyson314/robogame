# 88 — Claude Code project hooks (worktree guard, .cs compile reminder, session-log surfacing) + context7 MCP

> Status: **Tooling pass.** No gameplay changes. Adds `.claude/settings.json` with four project-scoped hooks, four PowerShell scripts under `.claude/hooks/`, and installs `context7` as a user-scope MCP for live third-party-library docs. Smoke-tested manually; hooks won't fire until next Claude Code session start.

## Why this session

Two motivations, neither gameplay:

1. **context7 MCP install.** Earlier audit (session 87) flagged context7 as the single highest-leverage MCP for any Unity 6 / NGO / URP work since the LLM-baked docs for those packages are unreliable and drift fast. Installed at user scope via HTTP transport, API key in the `CONTEXT7_API_KEY` header.
2. **Hooks to encode documented pain points.** Memory entry `editor_target_main_checkout.md` records edits landing in `.claude/worktrees/` paths that Unity never sees — a footgun this user has hit before. Two other smaller frictions: forgetting to check the Unity console after editing C#, and having to manually grep the highest-numbered `docs/changes/NN-*.md` at session start.

## What shipped

**MCP**
- `context7` registered at user scope, HTTP transport: `claude mcp add --scope user --header "CONTEXT7_API_KEY: ..." --transport http context7 https://mcp.context7.com/mcp`. Verified connected via `claude mcp list`. Tools only become available on next session restart.
- Cleaned up unrelated duplicate scope: `unity-mcp` was defined in both user and project scopes pointing at the same relay binary, triggering a conflicting-scopes warning on every `claude mcp list`. Removed the project-scoped entry (`claude mcp remove unity-mcp -s project`).

**Hook 1 — `PreToolUse` worktree-edit guard.** [`.claude/hooks/block_worktree_edits.ps1`](../../.claude/hooks/block_worktree_edits.ps1). Reads stdin JSON, normalises `/` to `\`, blocks (exit 2) when `tool_input.file_path` contains `\.claude\worktrees\`. Stderr message points at the main checkout and the memory entry. Maps the documented failure mode to a hard stop instead of a "hope Claude remembers" rule.

**Hook 2 — `PostToolUse` C# edit marker.** [`.claude/hooks/mark_cs_edit.ps1`](../../.claude/hooks/mark_cs_edit.ps1). When an Edit/Write/MultiEdit lands on a `.cs` path, touches `.utmp/claude-hooks/cs-edited.flag`. State is local-only (`.utmp/` is already gitignored).

**Hook 3 — `Stop` Unity console reminder.** [`.claude/hooks/stop_remind_unity_console.ps1`](../../.claude/hooks/stop_remind_unity_console.ps1). If the flag exists, emits a `hookSpecificOutput.additionalContext` JSON payload telling Claude to call `Unity_GetConsoleLogs` (or ask the user if the MCP bridge is down) before declaring done. Clears the flag after firing. Pairs with hook 2.

**Hook 4 — `SessionStart` current-log notice.** [`.claude/hooks/session_start_show_latest_log.ps1`](../../.claude/hooks/session_start_show_latest_log.ps1). Globs `docs/changes/*.md`, sorts by the numeric prefix, prints `Current session log: docs/changes/NN-slug.md` to stdout. Matchers cover `startup|resume|clear` so it fires on cold start, resume, and `/clear`.

**Settings glue.** [`.claude/settings.json`](../../.claude/settings.json) is new — wires the four hooks via `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...` invocations. Uses `${CLAUDE_PROJECT_DIR}` for portability across worktrees / clones.

**CLAUDE.md.** One paragraph added under `## Workflow → ### Project hooks` pointing at this session log. Kept surgical per the doc-brevity preference.

## Smoke tests run

All four scripts exercised manually from this session before the harness took the new settings.json:

- Worktree guard with a `.claude/worktrees/...` path: **exit 2, stderr block message**.
- Worktree guard with a normal `Assets/...` path: **exit 0, silent**.
- `mark_cs_edit` with a `.cs` path and `CLAUDE_PROJECT_DIR` set: **flag created at `.utmp/claude-hooks/cs-edited.flag`**.
- `stop_remind_unity_console` with the flag present: **emitted the `hookSpecificOutput` JSON to stdout, deleted the flag**.
- `session_start_show_latest_log`: **printed `Current session log (highest-numbered docs/changes entry): docs/changes/87-netcode-mppm-loopback.md`**.

Note that the hooks themselves will not fire in *this* session — Claude Code loads hook config at session start. First real exercise is the next session.

## Gotchas worth noting

- The block hook normalises slashes before matching. Tested with forward-slash paths because bash unescaped my first attempt at backslash JSON; the live harness sends correctly-escaped JSON so the backslash branch will be the one that fires in practice.
- The Stop hook intentionally only emits the reminder when a `.cs` edit was flagged. If you edit a `.cs` file by hand outside Claude (Rider, VS), the hook will not nag — that's not the failure mode it's catching.
- `context7`'s API key is in user-scoped `C:\Users\Grey\.claude.json` as a plain string header. Rotate via context7.com/dashboard if it ever feels too exposed.

## Out of scope / followups

- No `/profile-arena` skill yet — flagged as a possible next tooling step but not actioned.
- No `PreToolUse` enforcement on the third-party-package edit rule from `docs/PACKAGE_MODIFICATIONS.md`. Could be a future hook if those edits keep getting clobbered on package upgrades.
- No automated `Unity_GetConsoleLogs` call from the Stop hook — that would require the hook process to be able to talk MCP, which it can't. Reminder-via-context is the right design for now.
