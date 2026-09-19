<#
.SYNOPSIS
  D-007 (Grey, 2026-09-19: "yes"): a per-user login task that serves the Unity MCP bridge on
  127.0.0.1:8080, so a Claude session never opens to a dead bridge (LESSONS 2, ASSUMPTIONS #8).

  -Run        what the task executes at logon: start the server by hand unless 8080 already listens.
              Hand-started = no --pidfile and no token, so no Editor's quit cleanup can kill it (LESSONS 9/15).
  -Install    register the task "RobogameFactory-McpServer" for the current user (AtLogOn, runs as you).
  -Uninstall  remove it. The same as: Unregister-ScheduledTask -TaskName RobogameFactory-McpServer -Confirm:$false
  -Status     say whether it is installed and whether 8080 listens.
#>
param([switch]$Run, [switch]$Install, [switch]$Uninstall, [switch]$Status)
$ErrorActionPreference = 'Stop'
$TaskName = 'RobogameFactory-McpServer'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Utmp = Join-Path $Root '.utmp\factory'
$McpUrl = 'http://127.0.0.1:8080'

function Test-McpPort {
    try { return [bool](Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction Stop | Select-Object -First 1) } catch { return $false }
}

if ($Run) {
    if (Test-McpPort) { exit 0 }
    # the package pins the server it launches to its own version (ServerCommandBuilder); mirror that
    $pkg = Get-ChildItem -Path (Join-Path $Root 'Library\PackageCache') -Filter 'com.coplaydev.unity-mcp@*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    $ver = $null
    if ($pkg) { $j = Get-Content (Join-Path $pkg.FullName 'package.json') -Raw -ErrorAction SilentlyContinue; if ($j -and ($j -match '"version"\s*:\s*"([^"]+)"')) { $ver = $Matches[1] } }
    $uvx = Get-Command uvx -ErrorAction SilentlyContinue
    if (-not $uvx) { $cand = Join-Path $env:USERPROFILE '.local\bin\uvx.exe'; if (Test-Path $cand) { $uvx = [pscustomobject]@{ Source = $cand } } }
    if (-not $ver -or -not $uvx) { exit 3 }
    New-Item -ItemType Directory -Force $Utmp | Out-Null
    $serverArgs = @('--offline', '--from', "mcpforunityserver==$ver", 'mcp-for-unity', '--transport', 'http', '--http-url', $McpUrl, '--project-scoped-tools')
    Start-Process -FilePath $uvx.Source -ArgumentList $serverArgs -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Utmp 'mcp-server-login.log') -RedirectStandardError (Join-Path $Utmp 'mcp-server-login.err.log') | Out-Null
    exit 0
}
if ($Install) {
    $me = "$env:USERDOMAIN\$env:USERNAME"
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Run"
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $me
    $principal = New-ScheduledTaskPrincipal -UserId $me -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Robogame Factory: serves the Unity MCP bridge on 127.0.0.1:8080 at login (D-007). Remove: Unregister-ScheduledTask -TaskName RobogameFactory-McpServer -Confirm:$false' -Force | Out-Null
    "installed: $TaskName (at logon of $me; runs $PSCommandPath -Run)"
    exit 0
}
if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    "removed: $TaskName"
    exit 0
}
$t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
"task $TaskName installed: $([bool]$t)$(if ($t) { " (state $($t.State))" })"
"8080 listening: $(Test-McpPort)"
