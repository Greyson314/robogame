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
    6. the rig, SERVER FIRST (CHG-020): a Claude session dials 127.0.0.1:8080 once at its
       start and never again, so 8080 must listen BEFORE the session is opened. When nothing
       serves 8080 the launcher starts MCP for Unity's server itself, by hand (no pidfile, no
       handshake, so no Editor's quit cleanup can kill it: LESSONS 9), waits for it to listen,
       then starts this clone's Editor if none is open, brings it to the foreground once (the
       package's auto-start only fires in a focused Editor: ASSUMPTIONS #12), and waits for
       the Editor to register on the server. Only then is the shift prompt printed.
       -NoEditor skips all of it (batch-only shift).
    7. .env: DISCORD_WEBHOOK_FACTORY present by name (never printed)
  Then: write the lock, stamp the tick, write .utmp\factory\run-shift.ps1 with the exact
  claude invocation, and open it in a new Windows Terminal tab (falls back to a plain
  PowerShell window when wt.exe is absent).

.PARAMETER Model      claude --model (default opus; LOOP-STATE § SHIFT)
.PARAMETER Effort     claude --effort (default high)
.PARAMETER PermissionMode  claude --permission-mode (default bypassPermissions: nobody is at the keyboard)
.PARAMETER MaxHours   written into shift.json; the LOOP honors it (charter D13), the OS does not
.PARAMETER NoEditor   do not start the server or the Editor when nothing serves 8080 (batch-only shift)
.PARAMETER DryRun     run the preflight and print the launch; start nothing
.PARAMETER Desktop    run the preflight, write the lock and the tick, bring the rig up, but open no
                      tab: Grey pastes the printed prompt into a Claude Desktop session on this clone

.NOTES
  Written 2026-09-16 on the hive; first ran on Windows the same day (HANDOFF step 7).
  Server-first rig sequencing added 2026-09-17 (CHG-020, BACKLOG 12).
  Saved as UTF-8 WITH BOM on purpose: Windows PowerShell 5.1 reads a BOM-less file as
  cp1252, and the em-dashes' 0x94 byte becomes a closing curly quote that breaks parsing.
#>
[CmdletBinding()]
param(
    [string]$Model = 'opus',
    [string]$Effort = 'high',
    [string]$PermissionMode = 'bypassPermissions',
    [double]$MaxHours = 0,
    [switch]$NoEditor,
    [switch]$AllowMainCheckout,
    [switch]$DryRun,
    [switch]$Desktop
)
$ErrorActionPreference = 'Stop'
$GreyCheckout = 'C:\Users\Grey\Desktop\mutedtuple\robogame'
$Prompt = '/loop You are the Robogame Factory. docs/loop/CHARTER.md is your constitution — read it, then the state files it names, then act under it. The context window is scratch; the state files are your only memory, so write state before you stop and schedule your own next wakeup, and end the shift the way the charter says.'
$McpUrl = 'http://127.0.0.1:8080'

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
$upstream = $null
try { $upstream = (& git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null) } catch { $upstream = $null }   # a branch with no upstream (a builder's) is not an error
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
if (-not $py) { Warn "python not on PATH: ping.py / land.py / mcp_http.py will not run (README § Desktop setup, step 2)" }
else {
    $pyv = (& python --version 2>&1)
    if ("$pyv" -notmatch '^Python 3') { Warn "python on PATH is not Python 3 ($pyv) — the App Execution Alias shim?"; $py = $null } else { Ok "$pyv" }
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

# 6. rig — server first (CHG-020). Order: server listens -> Editor open -> Editor focused -> Editor registered.
function Test-McpPort {
    try { return [bool](Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction Stop | Select-Object -First 1) } catch { return $false }
}
function Wait-McpPort([int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) { if (Test-McpPort) { return $true }; Start-Sleep -Seconds 2 }
    return (Test-McpPort)
}
function Get-McpServerVersion {
    # the package pins the server it launches to its own version (ServerCommandBuilder); mirror that
    $pkg = Get-ChildItem -Path (Join-Path $Root 'Library\PackageCache') -Filter 'com.coplaydev.unity-mcp@*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pkg) {
        $j = Get-Content (Join-Path $pkg.FullName 'package.json') -Raw -ErrorAction SilentlyContinue
        if ($j -and ($j -match '"version"\s*:\s*"([^"]+)"')) { return $Matches[1] }
    }
    return $null
}
function Get-FactoryEditor {
    # the Unity.exe whose -projectPath is THIS clone (either slash style), never Grey's checkout
    $needle = $Root.TrimEnd('\').ToLowerInvariant()
    Get-CimInstance Win32_Process -Filter "name='Unity.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $cl = "$($_.CommandLine)".ToLowerInvariant().Replace('/', '\')
        $cl -match '-projectpath' -and $cl.Contains($needle) -and $cl -notmatch '-batchmode'
    } | Select-Object -First 1
}
function Show-EditorOnce([int]$editorPid, [int]$seconds) {
    # the package's HTTP auto-start handler runs from EditorApplication.delayCall, which a fresh
    # unfocused Editor has been observed not to tick (ASSUMPTIONS #12, three observations 2026-09-17)
    Add-Type -Namespace Factory -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);' -ErrorAction SilentlyContinue
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        $p = Get-Process -Id $editorPid -ErrorAction SilentlyContinue
        if (-not $p) { return $false }
        if ($p.MainWindowHandle -ne 0 -and $p.MainWindowTitle -match 'Unity') {
            [Factory.Win]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
            [Factory.Win]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
            try { (New-Object -ComObject WScript.Shell).AppActivate($editorPid) | Out-Null } catch {}
            return $true
        }
        Start-Sleep -Seconds 3
    }
    return $false
}

$mcpHttp = Join-Path $Root '.claude\scripts\factory\mcp_http.py'
$projectName = Split-Path $Root -Leaf
$mode = 'batch'
$rigSteps = @()
if ($NoEditor) {
    Warn "-NoEditor given: batch-only shift (run-tests.sh only; no console, screenshots or profiler); the server and the Editor are left as they are"
    if (Test-McpPort) { $mode = 'editor'; Ok "something already serves $McpUrl; the loop may use it" }
} else {
    # 6a. server
    if (Test-McpPort) {
        Ok "MCP server already listening on $McpUrl"
    } else {
        $ver = Get-McpServerVersion
        $uvx = Get-Command uvx -ErrorAction SilentlyContinue
        if (-not $ver) { Warn "MCP for Unity package not found under Library\PackageCache (has the Editor ever imported this clone?): cannot pin the server version" }
        if (-not $uvx) { Warn "uvx not on PATH: cannot start the MCP server by hand" }
        if ($ver -and $uvx) {
            $serverArgs = @('--offline', '--from', "mcpforunityserver==$ver", 'mcp-for-unity', '--transport', 'http', '--http-url', $McpUrl, '--project-scoped-tools')
            $rigSteps += "start server: uvx $($serverArgs -join ' ')  (no --pidfile, no token: never an Editor's to kill; log .utmp\factory\mcp-server-manual.log)"
            if (-not $DryRun) {
                Start-Process -FilePath $uvx.Source -ArgumentList $serverArgs -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Utmp 'mcp-server-manual.log') -RedirectStandardError (Join-Path $Utmp 'mcp-server-manual.err.log') | Out-Null
                if (Wait-McpPort 60) { Ok "MCP server started by hand (mcpforunityserver==$ver) and listening on $McpUrl" }
                else { Warn "the hand-started server did not listen within 60 s (see .utmp\factory\mcp-server-manual.err.log); falling back to the Editor's own auto-start" }
            }
        }
    }
    # 6b. Editor
    $editor = Get-FactoryEditor
    if ($editor) {
        Ok "the factory's Editor is already open on this clone (pid $($editor.ProcessId))"
    } elseif (Test-Path $unity) {
        $rigSteps += "start Editor: $unity -projectPath $Root"
        if (-not $DryRun) {
            $proc = Start-Process -FilePath $unity -ArgumentList @('-projectPath', "`"$Root`"") -PassThru
            Ok "started the factory's Editor on this clone (pid $($proc.Id))"
            $editor = [pscustomobject]@{ ProcessId = $proc.Id }
        }
    } else {
        Warn "no Editor and no Unity exe: batch-only shift"
    }
    # 6c. focus it once, so the package's auto-start fires and it connects to the server
    if ($editor) {
        $rigSteps += "foreground the Editor once (pid $($editor.ProcessId)); wait up to 10 min for its window"
        if (-not $DryRun) {
            if (Show-EditorOnce $editor.ProcessId 600) { Ok "Editor window shown once (auto-start needs a focused Editor, ASSUMPTIONS #12)" }
            else { Warn "the Editor never showed a window within 10 min: a startup hang (LOOP-STATE § RIG, D-005 history)?" }
        }
    }
    # 6d. wait for the Editor to register on the server
    if ($editor -and $py -and (Test-Path $mcpHttp)) {
        $rigSteps += "wait up to 5 min for '$projectName' to register: python mcp_http.py wait-instance $projectName 300"
        if (-not $DryRun) {
            if (-not (Wait-McpPort 120)) { Warn "nothing listens on $McpUrl after the Editor's auto-start window; the shift starts batch-only" }
            else {
                $env:PYTHONIOENCODING = 'utf-8'
                $reg = & python $mcpHttp wait-instance $projectName 300 2>&1
                if ($LASTEXITCODE -eq 0) { $mode = 'editor'; Ok "bridge live: $reg" }
                else { $mode = 'editor-starting'; Warn "the Editor has not registered on the server yet ($reg); the loop verifies at its first wake" }
            }
        } else { $mode = 'editor' }
    } elseif ($editor) {
        if (Test-McpPort) { $mode = 'editor' } else { $mode = 'editor-starting' }
        Warn "cannot confirm the Editor registered (python or mcp_http.py missing); port check only: mode $mode"
    }
    if ($DryRun -and $rigSteps.Count -gt 0) { Write-Host "preflight rig (dry run, in this order):" -ForegroundColor Cyan; $rigSteps | ForEach-Object { Write-Host "    $_" } }
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
$lockObj = [ordered]@{ started = $started; pid = $null; host = $(if ($Desktop) { 'desktop' } else { 'tab' }); mode = $mode; maxHours = $MaxHours; model = $Model; effort = $Effort; permissionMode = $PermissionMode; branch = $branch; root = $Root }
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
($lockObj | ConvertTo-Json) | Set-Content -Path $Lock -Encoding UTF8   # pid null until the tab claims it (stays null for a Desktop shift)
if ($Desktop) {
    Add-Content -Path (Join-Path $Utmp 'loop-tick.txt') -Value "$started launcher: desktop shift started (mode $mode, model/effort set in the Desktop UI, maxHours $MaxHours)"
    if ($mode -eq 'editor') {
        Write-Host "`nPreflight done, lock written, the bridge is live (mode $mode). NOW open a session on this folder in Claude Desktop (not a worktree)," -ForegroundColor Green
    } else {
        Write-Host "`nPreflight done, lock written (mode ${mode}: the bridge is NOT confirmed; the session will dial 8080 once at start). Open a session on this folder in Claude Desktop (not a worktree)," -ForegroundColor Yellow
    }
    Write-Host "pick the model and effort in the UI, bypass permissions, and paste this as the first message:`n" -ForegroundColor Green
    Write-Host $Prompt
    Write-Host "`nEnd the shift with Stop-Factory.ps1 or '/inbox STOP' from the scribe." -ForegroundColor Green
    exit 0
}
Set-Content -Path $runner -Value $runnerText -Encoding UTF8
Add-Content -Path (Join-Path $Utmp 'loop-tick.txt') -Value "$started launcher: shift started (mode $mode, $Model/$Effort, maxHours $MaxHours)"
$wt = Get-Command wt.exe -ErrorAction SilentlyContinue
if ($wt) {
    $proc = Start-Process -FilePath $wt.Source -ArgumentList @('-w', '0', 'new-tab', '--title', 'robogame-factory', '-d', "`"$Root`"", 'powershell', '-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$runner`"") -PassThru
} else {
    $proc = Start-Process -FilePath 'powershell' -ArgumentList @('-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$runner`"") -PassThru
}
Write-Host "`nShift started $started in a new tab (mode $mode). The tab records its own pid in the lock. Stop with Stop-Factory.ps1." -ForegroundColor Green
