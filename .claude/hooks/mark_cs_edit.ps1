# PostToolUse hook: drop a flag file when a .cs file is edited. Stop hook
# reads the flag to remind Claude to check the Unity console for compile
# errors before declaring done.

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $payload.tool_input.file_path
if (-not $path) { exit 0 }
if ($path -notmatch '\.cs$') { exit 0 }

$root = $env:CLAUDE_PROJECT_DIR
if (-not $root) { exit 0 }

$stateDir = Join-Path $root '.utmp\claude-hooks'
if (-not (Test-Path $stateDir)) {
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
}
$flag = Join-Path $stateDir 'cs-edited.flag'
Set-Content -Path $flag -Value '1' -Encoding Ascii
exit 0
