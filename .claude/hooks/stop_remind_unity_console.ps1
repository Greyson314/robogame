# Stop hook: if any .cs file was edited this session (flagged by
# mark_cs_edit.ps1), inject a reminder to verify a clean compile via the
# Unity MCP console before declaring done. Clears the flag after firing.

$root = $env:CLAUDE_PROJECT_DIR
if (-not $root) { exit 0 }

$flag = Join-Path $root '.utmp\claude-hooks\cs-edited.flag'
if (-not (Test-Path $flag)) { exit 0 }

$msg = "C# files were edited this session. Before declaring the task done, call Unity_GetConsoleLogs (or Unity_ReadConsole) to verify a clean compile. If the Unity MCP bridge is down, ask the user to confirm a clean compile."

$payload = @{
    hookSpecificOutput = @{
        hookEventName     = 'Stop'
        additionalContext = $msg
    }
}
$payload | ConvertTo-Json -Compress

Remove-Item $flag -Force
exit 0
