# cleanup_orphan_relays.ps1 — SessionStart hook.
#
# Kills ORPHANED Unity MCP relay processes: relay_win.exe whose spawning
# parent has died (a Claude Code hard-restart / crash). An orphaned relay keeps
# holding the Unity Bridge's single connection slot ("1 direct connection
# allowed at a time"), so the next Claude Code session's relay gets rejected and
# every mcp__unity-mcp__* call returns "Connection revoked". Freeing the slot at
# session start makes the bridge reconnect cleanly without a manual Stop/Start.
#
# SAFE BY CONSTRUCTION — only kills relays whose parent PID no longer resolves to
# a running process. That spares:
#   * Unity's own relay        (parent = the live Unity.exe — hosts the bridge)
#   * THIS session's relay      (parent = the live claude.exe running this hook)
#   * any concurrent session's  (parent = that session's live claude.exe)
# Worst case (Windows PID reuse) is a missed orphan, never a wrongful kill.
#
# Wired in .claude/settings.json under hooks.SessionStart. See
# docs memory project-unity-mcp-revoked-fix and docs/changes/119.

$ErrorActionPreference = 'SilentlyContinue'

$killed = 0
Get-CimInstance Win32_Process -Filter "Name='relay_win.exe'" | ForEach-Object {
    $parent = Get-Process -Id $_.ParentProcessId -ErrorAction SilentlyContinue
    if (-not $parent) {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        if ($?) { $killed++ }
    }
}

if ($killed -gt 0) {
    Write-Host "[relay-cleanup] Freed $killed orphaned Unity MCP relay(s) holding the bridge slot."
}
