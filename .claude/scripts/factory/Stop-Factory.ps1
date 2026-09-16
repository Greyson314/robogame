<#
.SYNOPSIS
  End a Robogame Factory shift: append a STOP line to docs/loop/INBOX.md, commit, push.
  The loop reads INBOX at every checkpoint (charter D12) and runs its end-of-shift routine (D13).

.PARAMETER Message   optional note after STOP
.PARAMETER NoPush    commit only (offline)
.PARAMETER Force     also delete the shift lock now (only when the session is already dead —
                     otherwise the running loop deletes it itself when it stops)

.NOTES
  Works from any clone of the repo, on any machine: it is a git operation. From the hive,
  `python .claude/scripts/factory/discord_intake.py` is not needed for a STOP; a plain
  `echo "- [$(date -u +%FT%TZ) via Stop-Factory] STOP" >> docs/loop/INBOX.md && git commit -am ... && git push` is the same thing.
#>
[CmdletBinding()]
param(
    [string]$Message = '',
    [switch]$NoPush,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$Root = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $Root) { Write-Host "not inside a git checkout" -ForegroundColor Red; exit 2 }
Set-Location $Root
$inbox = Join-Path $Root 'docs\loop\INBOX.md'
if (-not (Test-Path $inbox)) { Write-Host "no docs/loop/INBOX.md here" -ForegroundColor Red; exit 2 }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$line = "- [$stamp via Stop-Factory] STOP" + $(if ($Message) { ": $Message" } else { '' })
Add-Content -Path $inbox -Value $line -Encoding UTF8
& git add -- docs/loop/INBOX.md
& git commit -q -m "factory: STOP requested $stamp" -- docs/loop/INBOX.md
if ($LASTEXITCODE -ne 0) { Write-Host "commit failed" -ForegroundColor Red; exit 1 }
if (-not $NoPush) {
    & git push -q origin HEAD
    if ($LASTEXITCODE -ne 0) { Write-Host "push failed; the STOP is committed locally — push by hand" -ForegroundColor Yellow; exit 1 }
}
Write-Host "STOP written and pushed: the loop ends the shift at its next checkpoint (minutes when hot)." -ForegroundColor Green
$lock = Join-Path $Root '.utmp\factory\shift.json'
if ($Force -and (Test-Path $lock)) { Remove-Item $lock; Write-Host "shift lock removed (-Force)" }
elseif (Test-Path $lock) { Write-Host "the shift lock stays until the loop's end-of-shift routine removes it; if the session is dead, re-run with -Force." }
