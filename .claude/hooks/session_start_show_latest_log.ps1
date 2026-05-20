# SessionStart hook: print the highest-numbered docs/changes/NN-*.md so
# Claude immediately knows which session log is current. Per CLAUDE.md the
# highest-numbered file is treated as the active WIP entry.

$root = $env:CLAUDE_PROJECT_DIR
if (-not $root) { exit 0 }

$dir = Join-Path $root 'docs\changes'
if (-not (Test-Path $dir)) { exit 0 }

$latest = Get-ChildItem -Path $dir -Filter '*.md' -File |
    Where-Object { $_.BaseName -match '^\d+-' } |
    Sort-Object { [int]($_.BaseName -split '-')[0] } -Descending |
    Select-Object -First 1

if ($latest) {
    Write-Output "Current session log (highest-numbered docs/changes entry): docs/changes/$($latest.Name)"
}
exit 0
