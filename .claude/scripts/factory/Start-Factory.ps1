<#
.SYNOPSIS
  Start a Robogame Factory shift (docs/loop/CHARTER.md D13): preflight, then a
  Windows Terminal tab running `claude` with the /loop prompt.

.DESCRIPTION
  Preflight, in order:
    1. the repo root (this script's clone); refuse Grey's own checkout unless -AllowMainCheckout
    2. one shift at a time: .utmp\factory\shift.json (stale locks are reported and replaced)
    3. git: fetch origin, fast-forward the current branch if behind, report uncommitted
       work (the loop's first wake absorbs it; charter D13); refuse a merge/rebase in progress
    4. tools: git, claude, python (warn only), the Unity exe run-tests.sh expects (warn only)
    5. the hot-file size budget (~200 KB) — warn when over
    6. the rig: is something serving MCP on 127.0.0.1:8080? If not and -NoEditor was not
       given, start this clone's own Unity Editor (it auto-starts the MCP server)
    7. .env: DISCORD_WEBHOOK_FACTORY present by name (never printed)
  Then: write the lock, stamp the tick, write .utmp\factory\run-shift.ps1 with the exact
  claude invocation, and open it in a new Windows Terminal tab (falls back to a plain
  PowerShell window when wt.exe is absent).

.PARAMETER Model      claude --model (default opus; LOOP-STATE § SHIFT)
.PARAMETER Effort     claude --effort (default high)
.PARAMETER PermissionMode  claude --permission-mode (default bypassPermissions: nobody is at the keyboard)
.PARAMETER MaxHours   written into shift.json; the LOOP honors it (charter D13), the OS does not
.PARAMETER NoEditor   do not start the factory's Editor when nothing serves 8080 (batch-only shift)
.PARAMETER DryRun     run the preflight and print the launch; start nothing

.NOTES
  Written 2026-09-16 on the hive; not executed on Windows yet. First run: HANDOFF (1).
#>
[CmdletBinding()]
param(
    [string]$Model = 'opus',
    [string]$Effort = 'high',
    [string]$PermissionMode = 'bypassPermissions',
    [double]$MaxHours = 0,
    [switch]$NoEditor,
    [switch]$AllowMainCheckout,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$GreyCheckout = 'C:\Users\Grey\Desktop\mutedtuple\robogame'
$Prompt = '/loop You are the Robogame Factory. docs/loop/CHARTER.md is your constitution — read it, then the state files it names, then act under it. The context window is scratch; the state files are your only memory, so write state before you stop and schedule your own next wakeup, and end the shift the way the charter says.'

function Fail([string]$m) { Write-Host "PREFLIGHT FAIL: $m" -ForegroundColor Red; exit 2 }
function Warn([string]$m) { Write-Host "preflight warn: $m" -ForegroundColor Yellow }
function Ok([string]$m)   { Write-Host "preflight ok:   $m" }

# 1. repo root
$Root = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $Root) { Fail "not inside a git checkout" }
$Root = [System.IO.Path]::GetFullPath($Root)
if (($Root.TrimEnd('\') -ieq $GreyCheckout) -and -not $AllowMainCheckout) {
    Fail "this is Grey's own checkout ($GreyCheckout); the factory runs in its own clone (README). Pass -AllowMainCheckout to override."
}
Set-Location $Root
Ok "clone $Root"

# 2. shift lock
$Utmp = Join-Path $Root '.utmp\factory'
New-Item -ItemType Directory -Force -Path $Utmp | Out-Null
$Lock = Join-Path $Utmp 'shift.json'
if (Test-Path $Lock) {
    $old = Get-Content $Lock -Raw | ConvertFrom-Json
    $alive = $false
    if ($old.pid) { $alive = [bool](Get-Process -Id $old.pid -ErrorAction SilentlyContinue) }
    if ($alive) { Fail "a shift is already running (pid $($old.pid), started $($old.started)). Stop it first (Stop-Factory.ps1) or close its tab." }
    if (-not $old.pid -and ((Get-Date).ToUniversalTime() - [datetime]::Parse($old.started).ToUniversalTime()).TotalMinutes -lt 3) { Fail "a shift started less than 3 minutes ago and its tab has not claimed the lock yet; wait, or delete $Lock if that launch failed" }
    Warn "stale shift lock from $($old.started) (pid $($old.pid) gone): the last shift died without its end-of-shift routine; the first wake absorbs its work (D13)"
}

# 3. git
& git fetch origin --quiet 2>$null
$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
$gitDir = (& git rev-parse --git-dir).Trim()
if ((Test-Path (Join-Path $gitDir 'MERGE_HEAD')) -or (Test-Path (Join-Path $gitDir 'rebase-merge')) -or (Test-Path (Join-Path $gitDir 'rebase-apply'))) {
    Fail "a merge or rebase is in progress in this clone; finish or abort it first"
}
$behind = 0
$upstream = (& git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null)
if ($upstream) {
    $behind = [int](& git rev-list --count "HEAD..$upstream")
    if ($behind -gt 0) {
        if ($DryRun) { Warn "$behind commit(s) behind $upstream (would fast-forward)" }
        else {
            & git pull --ff-only --quiet
            if ($LASTEXITCODE -ne 0) { Fail "fast-forward from $upstream failed; resolve by hand" }
            Ok "fast-forwarded $branch from $upstream ($behind commit(s))"
        }
    }
}
$dirty = @(& git status --porcelain)
if ($dirty.Count -gt 0) {
    Warn "$($dirty.Count) uncommitted path(s) — the first wake applies the orphan rule (charter D13):"
    $dirty | Select-Object -First 15 | ForEach-Object { Write-Host "    $_" }
} else { Ok "working tree clean on $branch" }

# 4. tools
foreach ($t in 'git', 'claude') {
    if (-not (Get-Command $t -ErrorAction SilentlyContinue)) { Fail "'$t' is not on PATH" }
}
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { Warn "python not on PATH: ping.py / land.py / usage_tally.py will not run (README § Desktop setup, step 2)" }
else {
    $pyv = (& python --version 2>&1)
    if ("$pyv" -notmatch '^Python 3') { Warn "python on PATH is not Python 3 ($pyv) — the App Execution Alias shim?" } else { Ok "$pyv" }
}
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe'
$rt = Join-Path $Root '.claude\scripts\run-tests.sh'
if (Test-Path $rt) {
    $m = Select-String -Path $rt -Pattern '^UNITY_EXE="([^"]+)"' | Select-Object -First 1
    if ($m) { $unity = $m.Matches[0].Groups[1].Value -replace '/', '\' }
}
if (-not (Test-Path $unity)) { Warn "Unity exe not found at $unity (run-tests.sh will fail until edited)" } else { Ok "Unity $unity" }

# 5. hot-file budget
$hot = @('docs\loop\CHARTER.md', 'docs\loop\LOOP-STATE.md', 'docs\loop\NEEDS-GREY.md', 'docs\loop\INBOX.md', 'docs\research\game-design-pillars.md')
$bytes = 0
foreach ($h in $hot) { $p = Join-Path $Root $h; if (Test-Path $p) { $bytes += (Get-Item $p).Length } else { Warn "hot file missing: $h" } }
$kb = [math]::Round($bytes / 1024)
if ($bytes -gt 200KB) { Warn "hot set is $kb KB, over the ~200 KB budget (charter D13): split something before the shift" } else { Ok "hot set $kb KB" }

# 6. rig
$serving = $null
try { $serving = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction Stop | Select-Object -First 1 } catch { $serving = $null }
$mode = 'batch'
if ($serving) {
    $mode = 'editor'
    Ok "something is serving 127.0.0.1:8080 (MCP for Unity) — an Editor is up; the loop targets THIS clone's instance with set_active_instance"
} elseif ($NoEditor) {
    Warn "no MCP server on 8080 and -NoEditor given: batch-only shift (run-tests.sh only; no console, screenshots or profiler)"
} elseif (Test-Path $unity) {
    $mode = 'editor-starting'
    if ($DryRun) { Warn "would start the factory's Editor: $unity -projectPath $Root" }
    else {
        Start-Process -FilePath $unity -ArgumentList @('-projectPath', "`"$Root`"") | Out-Null
        Ok "started the factory's Editor on this clone (MCP comes up when it finishes loading; the loop verifies)"
    }
} else {
    Warn "no Editor and no Unity exe: batch-only shift"
}

# 7. .env
$envFile = Join-Path $Root '.env'
if (Test-Path $envFile) {
    $has = Select-String -Path $envFile -Pattern '^DISCORD_WEBHOOK_FACTORY=.{20,}' -Quiet
    if ($has) { Ok ".env has DISCORD_WEBHOOK_FACTORY" } else { Warn ".env lacks DISCORD_WEBHOOK_FACTORY: pings will dry-run" }
} else { Warn "no .env in the clone root: pings will dry-run (README § Desktop setup, step 3)" }

# launch
$started = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$runner = Join-Path $Utmp 'run-shift.ps1'
$lockObj = [ordered]@{ started = $started; pid = $null; mode = $mode; maxHours = $MaxHours; model = $Model; effort = $Effort; permissionMode = $PermissionMode; branch = $branch; root = $Root }
$runnerText = @"
`$ErrorActionPreference = 'Continue'
Set-Location '$Root'
`$host.UI.RawUI.WindowTitle = 'robogame-factory'
# this tab's own PowerShell is the shift's process of record: the wt.exe launcher exits at once
`$lockPath = '$Lock'
try { `$l = Get-Content `$lockPath -Raw | ConvertFrom-Json; `$l.pid = `$PID; (`$l | ConvertTo-Json) | Set-Content `$lockPath -Encoding UTF8 } catch {}
Write-Host 'Robogame Factory shift — started $started — mode $mode — leave this tab open' -ForegroundColor Cyan
& claude --model '$Model' --effort '$Effort' --permission-mode '$PermissionMode' '$($Prompt.Replace("'", "''"))'
Write-Host 'claude exited; the shift is over. If LOOP-STATE was not written, the next preflight reports the orphaned work.' -ForegroundColor Yellow
try { if (Test-Path `$lockPath) { `$l = Get-Content `$lockPath -Raw | ConvertFrom-Json; `$l | Add-Member -NotePropertyName ended -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')) -Force; (`$l | ConvertTo-Json) | Set-Content (Join-Path (Split-Path `$lockPath) 'shift-last.json') -Encoding UTF8; Remove-Item `$lockPath } } catch {}
"@
if ($DryRun) {
    Write-Host "`nDRY RUN — would write $Lock and $runner, stamp the tick, and open:" -ForegroundColor Cyan
    Write-Host $runnerText
    exit 0
}
($lockObj | ConvertTo-Json) | Set-Content -Path $Lock -Encoding UTF8   # pid null until the tab claims it
Set-Content -Path $runner -Value $runnerText -Encoding UTF8
Add-Content -Path (Join-Path $Utmp 'loop-tick.txt') -Value "$started launcher: shift started (mode $mode, $Model/$Effort, maxHours $MaxHours)"
$wt = Get-Command wt.exe -ErrorAction SilentlyContinue
if ($wt) {
    $proc = Start-Process -FilePath $wt.Source -ArgumentList @('-w', '0', 'new-tab', '--title', 'robogame-factory', '-d', "`"$Root`"", 'powershell', '-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$runner`"") -PassThru
} else {
    $proc = Start-Process -FilePath 'powershell' -ArgumentList @('-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$runner`"") -PassThru
}
Write-Host "`nShift started $started in a new tab (mode $mode). The tab records its own pid in the lock. Stop with Stop-Factory.ps1." -ForegroundColor Green
