<#
.SYNOPSIS
  Runs one Unity batch-mode job on the HS2Next project, sharing the single editor instance safely.

.DESCRIPTION
  Only one Unity editor may open the project at a time, and several agents run tests in turn.
  This script
    0. takes the machine-wide mutex Global\HS2Next_unity_batch (held until its Unity exits), so two runners can no
       longer both see "no lock file" and start two editors at once (the second one exits with code 1 right after
       "Successfully changed project path"),
    1. waits until Temp\UnityLockfile is gone (a stale lock without a Unity.exe on this project
       is removed after -StaleAfterSec seconds),
    2. waits until at least -MinFreeGB of physical memory is free,
    3. starts Unity -batchmode [-nographics] -projectPath <project> [-executeMethod <Method> <Args>] -logFile <log> -quit
       (-Compile always adds -nographics),
    4. waits for the Unity.exe process itself (not Start-Process -Wait: the compiler server keeps that hanging),
    5. prints the exit code, compile errors ("error CS"), exceptions, and the last lines of the log.
  -NoQuit leaves out -quit: the editor keeps running after the method returns, and the method (or what it starts,
  e.g. a play-mode test through Editor/PlayModeHarness.cs) must end the process with EditorApplication.Exit; the
  -TimeoutMin limit still applies. Without -NoQuit nothing changes.
  -Gfx d3d11 | d3d12 starts a graphics job with that graphics API (-force-d3d11 / -force-d3d12); without it Unity picks its
  default (D3D12 on this machine). A -NoGraphics or -Compile job has no device: -Gfx is ignored there (with a note).
  Before step 0 the offline compile check (Tools/offline_compile.py: Assembly-CSharp and -Editor of the working tree with
  Unity's compiler, without Unity, about 10-40 s) runs and prints a WARNING with the first errors when the tree does not
  compile; it never blocks the job. -SkipOfflineCompile or the environment variable HS2N_OFFLINE_COMPILE=off leaves it out
  (run_tests.ps1 runs it once per suite and passes -SkipOfflineCompile to every row).

.EXAMPLE
  pwsh Tools\unity_batch.ps1 -Compile
  pwsh Tools\unity_batch.ps1 -Method CardImportTest.Run -Args '-limit','30' -NoGraphics -LogName import30
  pwsh Tools\unity_batch.ps1 -Method CharaRenderTest.Run -Args '-urp','-diff' -LogName render
  & Tools\unity_batch.ps1 -Method SceneRuntimeTest.RunPlay -NoQuit -LogName scene_play
  & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -Gfx d3d11 -LogName ui_panels_d3d11

.NOTES
  Exit code: Unity's exit code (0 = ok; 1 = compile errors or the method threw; 124 = timeout; 125 = lock wait timeout).
  With -NoQuit and Editor/PlayModeHarness.cs also 2 = play-mode guard, 3 = play mode not entered, 4 = play mode left early.
  Logs go to <LogDir>\<LogName>.log (the directory is created).
  Defaults that depend on the machine are derived, not written in: -Project = the folder above Tools; -Unity = the
  environment variable HS2N_UNITY, else the Unity Hub install of the editor version in ProjectSettings\ProjectVersion.txt
  under Program Files; -LogDir = HS2N_LOG_DIR, else tmp\hs2next_logs on the system drive.
#>
[CmdletBinding()]
param(
    [string]$Method,
    # Arguments after -executeMethod. Pass an array (-MethodArgs '-limit','60'); its elements are passed whole, commas
    # included (-MethodArgs '-mstOnly','M1,M5'). Only a single string such as "'-limit','60'" or "-limit,60" (what
    # `pwsh -File` hands over from another shell) is split on commas and stripped of quotes. -Args is accepted as an alias.
    [Alias('Args')][string[]]$MethodArgs = @(),
    [switch]$NoGraphics,
    [switch]$Compile,                       # no -executeMethod: just import/compile and quit
    [switch]$NoQuit,                        # no -quit: the method ends Unity itself (EditorApplication.Exit)
    [ValidateSet('d3d11', 'd3d12')][string]$Gfx,   # the graphics API of a graphics job (-force-<api>); default: Unity's
    [string]$LogName,
    [string]$Project,                       # default: the folder above Tools
    [string]$Unity,                         # default: HS2N_UNITY, else the Hub install of the project's editor version
    [string]$LogDir,                        # default: HS2N_LOG_DIR, else tmp\hs2next_logs on the system drive
    [double]$MinFreeGB = 8,
    [int]$TimeoutMin = 90,
    [int]$LockWaitMin = 120,
    [int]$StaleAfterSec = 90,
    [int]$Tail = 40,
    [switch]$SkipOfflineCompile             # no offline compile check before the job (run_tests.ps1 runs it once)
)

$ErrorActionPreference = 'Stop'
if (-not $Project) { $Project = Split-Path -Parent $PSScriptRoot }
if (-not $Unity) {
    if ($env:HS2N_UNITY) { $Unity = $env:HS2N_UNITY }
    else {
        $pv = Join-Path $Project 'ProjectSettings\ProjectVersion.txt'
        $m = if (Test-Path -LiteralPath $pv) { Select-String -LiteralPath $pv -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1 } else { $null }
        if (-not $m) { throw "no m_EditorVersion in $pv (give -Unity or set HS2N_UNITY)" }
        $Unity = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $m.Matches[0].Groups[1].Value + '\Editor\Unity.exe')
    }
}
if (-not $LogDir) { $LogDir = if ($env:HS2N_LOG_DIR) { $env:HS2N_LOG_DIR } else { Join-Path $env:SystemDrive 'tmp\hs2next_logs' } }
if (-not $Compile -and -not $Method) { throw 'Give -Method <Class.Method> or -Compile.' }
if ($NoQuit -and -not $Method) { throw '-NoQuit needs -Method (the method must call EditorApplication.Exit).' }
if (-not (Test-Path $Unity)) { throw "Unity not found: $Unity" }
New-Item -ItemType Directory -Force $LogDir | Out-Null
if (-not $LogName) { $LogName = if ($Method) { ($Method -replace '[^A-Za-z0-9_.-]', '_') } else { 'compile' } }
$log = Join-Path $LogDir "$LogName.log"
$lock = Join-Path $Project 'Temp\UnityLockfile'

function Get-ProjectUnity {
    # Unity.exe processes whose command line names this project.
    Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Project*" }
}

# Offline compile check (a warning only): a tree that does not compile shows up here in well under a minute instead of
# after a Unity start; the job runs anyway (the Unity compile is the judge).
if (-not $SkipOfflineCompile -and $env:HS2N_OFFLINE_COMPILE -ne 'off') {
    $oc = Join-Path $PSScriptRoot 'offline_compile.py'
    $py = Get-Command python -ErrorAction SilentlyContinue
    if ((Test-Path $oc) -and $py) {
        $ocOut = Join-Path $LogDir "$LogName.offline_compile.txt"
        $q = Start-Process -FilePath $py.Source -ArgumentList @('-I', ('"' + $oc + '"'), '-q') -WorkingDirectory $Project -PassThru -NoNewWindow `
            -RedirectStandardOutput $ocOut -RedirectStandardError "$ocOut.err"
        $null = $q.Handle
        if (-not $q.WaitForExit(300000)) {
            & taskkill.exe /PID $q.Id /T /F 2>&1 | Out-Null
            Write-Host '[unity_batch] WARNING: the offline compile check did not finish within 5 min (stopped); starting the job anyway' -ForegroundColor Yellow
        } else {
            $ocLines = @(Get-Content -LiteralPath $ocOut -ErrorAction SilentlyContinue)
            if ($q.ExitCode -eq 1) {
                Write-Host "[unity_batch] WARNING: the working tree does not compile (offline check, $ocOut); starting the job anyway:" -ForegroundColor Yellow
                $ocLines | Where-Object { $_ -match 'error [A-Z]+\d+' } | Select-Object -First 20 | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Yellow }
            } elseif ($q.ExitCode -ne 0) {
                Write-Host ("[unity_batch] WARNING: the offline compile check could not run (exit {0}): {1}" -f $q.ExitCode, (($ocLines | Select-Object -Last 1) -join '')) -ForegroundColor Yellow
            } else {
                Write-Host ('[unity_batch] offline compile: ' + (($ocLines | Select-Object -Last 1) -join ''))
            }
        }
        Remove-Item -LiteralPath "$ocOut.err" -ErrorAction SilentlyContinue
    }
}

# 0. runner mutex (released when this script ends; an abandoned mutex of a killed runner counts as acquired)
$t0 = Get-Date
$mutex = New-Object System.Threading.Mutex($false, 'Global\HS2Next_unity_batch')
$owned = $false
try { $owned = $mutex.WaitOne([TimeSpan]::FromMinutes($LockWaitMin)) }
catch [System.Threading.AbandonedMutexException] { $owned = $true }
if (-not $owned) { Write-Host '[unity_batch] runner mutex wait timeout'; exit 125 }
$code = 125
try {

# 1. lock
$staleSince = $null; $lockTimedOut = $false
while (Test-Path $lock) {
    $busy = @(Get-ProjectUnity)
    if ($busy.Count -eq 0) {
        if (-not $staleSince) { $staleSince = Get-Date }
        if (((Get-Date) - $staleSince).TotalSeconds -ge $StaleAfterSec) {
            Write-Host "[unity_batch] stale lockfile (no Unity.exe on the project for $StaleAfterSec s): removing"
            Remove-Item $lock -Force -ErrorAction SilentlyContinue
            break
        }
    } else { $staleSince = $null }
    if (((Get-Date) - $t0).TotalMinutes -ge $LockWaitMin) { Write-Host '[unity_batch] lock wait timeout'; $lockTimedOut = $true; break }
    Start-Sleep -Seconds 10
}
if ($lockTimedOut) { $code = 125 } else {

# 2. memory
while ($true) {
    $os = Get-CimInstance Win32_OperatingSystem
    $free = $os.FreePhysicalMemory / 1MB
    if ($free -ge $MinFreeGB) { break }
    Write-Host ("[unity_batch] free RAM {0:N1} GB < {1} GB, waiting" -f $free, $MinFreeGB)
    Start-Sleep -Seconds 20
}

# 3. start
$argv = @('-batchmode', '-projectPath', $Project, '-logFile', $log)
if (-not $NoQuit) { $argv += '-quit' }
# -Compile never needs a graphics device: without one, Unity cannot hang in the GPU driver while it tears the device down
# (seen once: a compile that failed with "Exiting" stayed stuck with one thread and the GPU context, holding the lock).
if ($NoGraphics -or $Compile) { $argv += '-nographics' }
if ($Gfx) {
    if ($NoGraphics -or $Compile) { Write-Host "[unity_batch] -Gfx $Gfx ignored: the job runs without a graphics device" }
    else { $argv += "-force-$Gfx" }
}
$methodArgv = @()
# Only a single string is the flattened form ("'-limit','60'" / "-limit,60" from `pwsh -File`): split it on commas. The
# elements of a real array stay whole, so a value list such as @('-mstOnly','M1,M5') or @('-maps','17,29') reaches the
# method as one argument (earlier every element was split, and parsers that read only the next argument saw M1).
$flattened = @($MethodArgs | Where-Object { $null -ne $_ }).Count -eq 1
foreach ($a in $MethodArgs) {
    if ($null -eq $a) { continue }
    $pieces = if ($flattened) { $a -split ',' } else { @($a) }
    foreach ($piece in $pieces) {
        $t = if ($flattened) { $piece.Trim().Trim("'", '"') } else { $piece }
        if ($t.Length -gt 0) { $methodArgv += $t }
    }
}
if ($Method) { $argv += @('-executeMethod', $Method) + $methodArgv }
# Start-Process joins the list with spaces and does not quote: wrap arguments that contain spaces.
$argv = $argv | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
if (Test-Path $log) { Remove-Item $log -Force }
Write-Host ("[unity_batch] {0} start: {1}" -f (Get-Date -Format 'HH:mm:ss'), ($argv -join ' '))
$p = Start-Process -FilePath $Unity -ArgumentList $argv -PassThru -WindowStyle Hidden

# 4. wait on Unity.exe itself
$deadline = (Get-Date).AddMinutes($TimeoutMin)
while (-not $p.HasExited) {
    if ((Get-Date) -gt $deadline) {
        Write-Host "[unity_batch] timeout after $TimeoutMin min: killing Unity"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        if ((Test-Path $lock) -and @(Get-ProjectUnity).Count -eq 0) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
        $code = 124
        break
    }
    Start-Sleep -Seconds 5
}
if ($code -ne 124) {
    $code = $p.ExitCode
    Write-Host ("[unity_batch] {0} exit {1} ({2:N1} min)" -f (Get-Date -Format 'HH:mm:ss'), $code, ((Get-Date) - $t0).TotalMinutes)
}
# A failed compile leaves the lock behind; never remove one that another Unity on this project holds.
if ((Test-Path $lock) -and @(Get-ProjectUnity).Count -eq 0) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
}   # not $lockTimedOut

} finally {
    if ($owned) { try { $mutex.ReleaseMutex() } catch { } }
    $mutex.Dispose()
}
if ($code -eq 124 -or $code -eq 125) { exit $code }

# 5. report
if (Test-Path $log) {
    $lines = Get-Content $log
    $errs = $lines | Select-String -Pattern 'error CS\d+', 'Exception', 'Error:' -CaseSensitive | Select-Object -First 30
    if ($errs) { Write-Host '[unity_batch] errors / exceptions (first 30):'; $errs | ForEach-Object { Write-Host ('  ' + $_.Line) } }
    Write-Host "[unity_batch] last $Tail lines of $log"
    $lines | Select-Object -Last $Tail | ForEach-Object { Write-Host ('  ' + $_) }
} else { Write-Host "[unity_batch] no log written at $log" }
exit $code
