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
  The stall watcher: beside the wait, a thread job reads this job's log. When the log has not changed for -StallMin minutes
  (default 30; 0 turns the watcher off), or for -StallEndMin minutes (default 3) once its last 16 KB show that Unity is shutting
  down, it stops this script's own Unity by PID with its process tree, prints why and the script exits 125. It never looks for
  other Unity processes. -StallSelfTest checks this with a fake Unity (no Unity, no project, about a minute).
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
  & Tools\unity_batch.ps1 -StallSelfTest

.NOTES
  Exit code: Unity's exit code (0 = ok; 1 = compile errors or the method threw; 124 = timeout; 125 = lock wait timeout, or the
  stall watcher stopped Unity: the "[unity_batch] stalled:" line says which).
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
    [switch]$SkipOfflineCompile,            # no offline compile check before the job (run_tests.ps1 runs it once)
    [double]$StallMin = 30,                 # stop this job's Unity after so many minutes without a change of its log; 0 = off
    [double]$StallEndMin = 3,               # the same, once the log's tail shows that Unity is shutting down
    [switch]$StallSelfTest,                 # the watcher's self-test with a fake Unity; nothing else runs
    [string]$MutexName = 'Global\HS2Next_unity_batch'   # the runner mutex; only the self-test passes another name
)

$ErrorActionPreference = 'Stop'

# ---- the stall watcher's self-test (no Unity, no project, not the shared mutex).
# The fake is a small program named Unity.exe, because the watcher must not be able to tell it from the real one. It holds its log
# open as Unity does and ends by itself after two minutes, so that a failed run leaves nothing behind. The second fake is started
# here, not by the script under test: it is the process that must stay alive.
function Invoke-StallSelfTest {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $csc)) { Write-Host "[selftest] no compiler at $csc"; return 2 }
    $root = Join-Path ([IO.Path]::GetTempPath()) ('hs2n_stall_selftest_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $null = New-Item -ItemType Directory -Force (Join-Path $root 'bin')
    $src = Join-Path $root 'bin\fake.cs'
    $fake = Join-Path $root 'bin\Unity.exe'
    Set-Content -LiteralPath $src -Encoding ascii -Value @'
using System; using System.Diagnostics; using System.IO; using System.Threading;
static class FakeUnity
{
    const int LimitMs = 120000;
    static string Arg(string[] a, string n) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static int Main(string[] a)
    {
        if (Array.IndexOf(a, "-grandchild") >= 0) { Thread.Sleep(LimitMs); return 98; }
        string log = Arg(a, "-logFile"), project = Arg(a, "-projectPath");
        File.WriteAllText(log + ".pid", Process.GetCurrentProcess().Id.ToString());
        StreamWriter w = null;
        foreach (string step in File.ReadAllLines(Arg(a, "-plan") ?? Path.Combine(project, "plan.txt")))
        {
            char k = step[0]; string v = step.Length > 2 ? step.Substring(2) : "";
            if (k == 'w' || k == 'f')
            {
                if (w == null) w = new StreamWriter(new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
                if (k == 'w') w.WriteLine(v); else for (int i = 0; i < int.Parse(v); i++) w.WriteLine("filler line of the fake's log, not a marker");
            }
            else if (k == 's') Thread.Sleep(int.Parse(v));
            else if (k == 'x') return int.Parse(v);
            else if (k == 'h') { Thread.Sleep(LimitMs); return 99; }
            else if (k == 'l') { Directory.CreateDirectory(Path.Combine(project, "Temp")); File.WriteAllText(Path.Combine(project, "Temp", "UnityLockfile"), ""); }
            else if (k == 'c')
            {
                var c = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "-grandchild") { UseShellExecute = false, CreateNoWindow = true });
                File.WriteAllText(log + ".child", c.Id.ToString());
            }
        }
        return 0;
    }
}
'@
    & $csc /nologo /target:exe "/out:$fake" $src | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fake)) { Write-Host '[selftest] the fake did not compile'; return 2 }

    $script:stPass = 0; $script:stFail = 0
    $started = New-Object System.Collections.Generic.List[int]
    function Alive([int]$id) { return $null -ne (Get-Process -Id $id -ErrorAction SilentlyContinue) }
    function Say([bool]$ok, [string]$what) {
        if ($ok) { $script:stPass++ } else { $script:stFail++ }
        Write-Host ((& { if ($ok) { 'PASS ' } else { 'FAIL ' } }) + $what)
    }
    function Wait-File([string]$path, [int]$sec) {
        $t = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $path) -and $t.Elapsed.TotalSeconds -lt $sec) { Start-Sleep -Milliseconds 100 }
        return (Test-Path -LiteralPath $path)
    }
    # One run of this script against a fake: returns its exit code, its output, the fake's PID and the second fake's PID.
    function Run-Case([string]$name, [string[]]$plan, [double]$stall, [double]$end, [string]$second) {
        $dir = Join-Path $root $name
        $proj = Join-Path $dir 'project'
        $logs = Join-Path $dir 'logs'
        $null = New-Item -ItemType Directory -Force $proj, $logs
        Set-Content -LiteralPath (Join-Path $proj 'plan.txt') -Encoding ascii -Value $plan
        $other = 0
        if ($second) {
            # a second Unity.exe, not this run's child: on another project ('other') or on this one ('same')
            $otherProj = if ($second -eq 'same') { $proj } else { Join-Path $dir 'other' }
            $null = New-Item -ItemType Directory -Force $otherProj
            $otherPlan = Join-Path $dir 'other_plan.txt'
            Set-Content -LiteralPath $otherPlan -Encoding ascii -Value @('w the second fake', 'h')
            $otherLog = Join-Path $dir 'other.log'
            $q = Start-Process -FilePath $fake -ArgumentList @('-batchmode', '-projectPath', $otherProj, '-logFile', $otherLog, '-plan', $otherPlan) -PassThru -WindowStyle Hidden
            $other = $q.Id
            $started.Add($other)
            $null = Wait-File "$otherLog.pid" 10
        }
        $mutex = "Global\HS2Next_unity_batch_selftest_$PID"
        $out = @(& $PSCommandPath -Compile -Project $proj -Unity $fake -LogDir $logs -LogName 'run' -SkipOfflineCompile -MinFreeGB 0 -MutexName $mutex `
                -TimeoutMin 3 -LockWaitMin 1 -Tail 2 -StallMin $stall -StallEndMin $end *>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        $log = Join-Path $logs 'run.log'
        $fp = 0
        if (Test-Path -LiteralPath "$log.pid") { $fp = [int](Get-Content -LiteralPath "$log.pid" -Raw) ; $started.Add($fp) }
        $gc = 0
        if (Test-Path -LiteralPath "$log.child") { $gc = [int](Get-Content -LiteralPath "$log.child" -Raw); $started.Add($gc) }
        return @{ Code = $code; Out = ($out -join "`n"); Fake = $fp; Grand = $gc; Other = $other; Lock = (Join-Path $proj 'Temp\UnityLockfile') }
    }
    $short = 0.03    # 1.8 s
    try {
        # 1. a log that stopped, no end marker: this run's child and its tree stopped; a second Unity.exe on another project alive; the lock gone
        $r = Run-Case 'stall' @('l', 'c', 'w Initialize engine', 'w the last line of the fake', 'h') $short 30 'other'
        Say ($r.Code -eq 125) "stall: exit 125 (exit $($r.Code))"
        Say ($r.Out -match ('(?m)^\[unity_batch\] stalled: log unchanged for [0-9.]+ min, last line: the last line of the fake; stopped PID ' + $r.Fake + '\s*$')) "stall: the reason is printed: $(($r.Out -split "`n" | Where-Object { $_ -match 'stalled:' }) -join ' / ')"
        Say ($r.Fake -gt 0 -and -not (Alive $r.Fake)) "stall: the child (PID $($r.Fake)) is gone"
        Say ($r.Grand -gt 0 -and -not (Alive $r.Grand)) "stall: the child's own child (PID $($r.Grand)) is gone"
        Say ($r.Other -gt 0 -and (Alive $r.Other)) "stall: the second Unity.exe, not its child (PID $($r.Other)), is alive"
        Say (-not (Test-Path -LiteralPath $r.Lock)) 'stall: the lock file is removed'

        # 2. the second Unity.exe names the same project: still alive, and the lock stays with it (as the timeout path)
        $r = Run-Case 'sameproject' @('l', 'w Initialize engine', 'h') $short 30 'same'
        Say ($r.Code -eq 125 -and -not (Alive $r.Fake)) "same project: exit 125, the child is gone (exit $($r.Code))"
        Say ($r.Other -gt 0 -and (Alive $r.Other)) "same project: the second Unity.exe (PID $($r.Other)) is alive"
        Say (Test-Path -LiteralPath $r.Lock) 'same project: the lock file stays, another Unity on the project holds it'

        # 3. the end markers: the short limit applies only with one of them in the tail, whichever it is
        foreach ($m in @('Batchmode quit successfully invoked - shutting down!', 'CodeReloadManager destroyed', 'Exiting batchmode successfully now!')) {
            $r = Run-Case ('end' + $m.Length) @('l', 'w Initialize engine', "w $m", 'h') 30 $short $null
            Say ($r.Code -eq 125 -and -not (Alive $r.Fake) -and $r.Out -like "*last line: $m;*") "end marker «$m»: stopped by the short limit (exit $($r.Code))"
        }

        # 4. a log that keeps changing, with the writer's handle open, over longer than the limit: not stopped
        $ticks = @('l')
        for ($i = 0; $i -lt 8; $i++) { $ticks += 'w tick'; $ticks += 's 800' }
        $r = Run-Case 'alive' ($ticks + 'x 0') $short $short $null
        Say ($r.Code -eq 0 -and $r.Out -notmatch 'stalled:') "a changing log for 6.4 s under a limit of 1.8 s: not stopped (exit $($r.Code))"

        # 5. -StallMin 0: off, also with an end marker in the tail
        $r = Run-Case 'off' @('l', 'w CodeReloadManager destroyed', 's 5000', 'x 7') 0 $short $null
        Say ($r.Code -eq 7 -and $r.Out -notmatch 'stalled:') "-StallMin 0: not stopped, the job's own exit code (exit $($r.Code))"

        # 6. a marker further back than the tail does not shorten the limit
        $r = Run-Case 'oldmarker' @('l', 'w Batchmode quit successfully invoked - shutting down!', 'f 600', 's 5000', 'x 0') 30 $short $null
        Say ($r.Code -eq 0 -and $r.Out -notmatch 'stalled:') "a marker 26 KB back: the short limit does not apply (exit $($r.Code))"

        # 7. no log at all
        $r = Run-Case 'nolog' @('l', 'h') $short 30 $null
        Say ($r.Code -eq 125 -and -not (Alive $r.Fake) -and $r.Out -like '*last line: (no log);*') "no log written: stopped by the limit (exit $($r.Code))"
    } finally {
        foreach ($id in $started) { if (Alive $id) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } }
        Start-Sleep -Seconds 1
        # file by file, then the folders one by one: nothing here can follow a link
        Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        Get-ChildItem -LiteralPath $root -Recurse -Directory -Force -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { try { [IO.Directory]::Delete($_.FullName) } catch { } }
        try { [IO.Directory]::Delete($root) } catch { Write-Host "[selftest] could not remove $root" }
    }
    Write-Host ("SUMMARY: {0} PASS, {1} FAIL" -f $script:stPass, $script:stFail)
    return $(if ($script:stFail -gt 0) { 1 } else { 0 })
}
if ($StallSelfTest) { exit (Invoke-StallSelfTest) }

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

# The stall watcher, a thread job beside the wait below. It holds this job's own Process object and stops that PID with its process
# tree; it never looks for other Unity processes. Unchanged means: the size and the last 16 KB read through an open handle are the
# same (a file's directory entry may lag while its writer keeps it open). A log it cannot read never counts as unchanged, and only the
# polls it saw count as time, so a sleep of the machine is not a stall. The verdict goes into $state before the stop, so the wait
# below never sees the exit without it.
$StallWatch = {
    param([System.Diagnostics.Process]$proc, [string]$logPath, [double]$stallMin, [double]$endMin, [hashtable]$state)
    $tailBytes = 16384
    $endMarkers = 'Exiting batchmode', 'CodeReloadManager destroyed', 'Batchmode quit successfully invoked'
    $limits = @($stallMin, $endMin) | Where-Object { $_ -gt 0 }
    $pollSec = [Math]::Max(1.0, [Math]::Min(15.0, 60.0 * ($limits | Measure-Object -Minimum).Minimum / 4.0))
    $lastSize = [long]-2; $lastTail = ''; $quietSec = 0.0
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds ([int](1000 * $pollSec))
        if ($proc.HasExited) { return }
        $size = [long]-1; $tail = ''
        try {
            if (Test-Path -LiteralPath $logPath) {
                $fs = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
                try {
                    $size = $fs.Length
                    $n = [int][Math]::Min($size, [long]$tailBytes)
                    $null = $fs.Seek(-$n, [IO.SeekOrigin]::End)
                    $buf = New-Object byte[] $n
                    $got = $fs.Read($buf, 0, $n)
                    $tail = [Text.Encoding]::UTF8.GetString($buf, 0, $got)
                } finally { $fs.Dispose() }
            }
        } catch { $quietSec = 0.0; $lastSize = [long]-2; continue }
        if ($size -ne $lastSize -or $tail -cne $lastTail) { $lastSize = $size; $lastTail = $tail; $quietSec = 0.0; continue }
        $quietSec += $pollSec
        $min = $quietSec / 60.0
        $ending = @($endMarkers | Where-Object { $tail.Contains($_) }).Count -gt 0
        if (-not (($stallMin -gt 0 -and $min -ge $stallMin) -or ($ending -and $endMin -gt 0 -and $min -ge $endMin))) { continue }
        $lines = @($tail -split '\r?\n' | Where-Object { $_.Trim().Length -gt 0 })
        $last = if ($size -lt 0) { '(no log)' } elseif ($lines.Count -eq 0) { '(empty log)' } else { $lines[-1].Trim() }
        if ($last.Length -gt 200) { $last = $last.Substring(0, 200) + '...' }
        if ($proc.HasExited) { return }
        $state.Minutes = $min; $state.LastLine = $last; $state.Stopped = $true
        $state.Kill = (& taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-String).Trim()
        return
    }
}

# 0. runner mutex (released when this script ends; an abandoned mutex of a killed runner counts as acquired)
$t0 = Get-Date
$mutex = New-Object System.Threading.Mutex($false, $MutexName)
$owned = $false
$watch = $null
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
$null = $p.Handle   # kept open: Windows does not reuse this PID while the handle is held, so the watcher can only ever stop this process
$stall = [hashtable]::Synchronized(@{ Stopped = $false })
if ($StallMin -gt 0) {
    if (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue) {
        $watch = Start-ThreadJob -ScriptBlock $StallWatch -ArgumentList $p, $log, $StallMin, $StallEndMin, $stall
    } else { Write-Host '[unity_batch] no Start-ThreadJob in this PowerShell: no stall watcher, the -TimeoutMin limit stays' }
}

# 4. wait on Unity.exe itself
$deadline = (Get-Date).AddMinutes($TimeoutMin)
$stoppedAt = $null
while (-not $p.HasExited) {
    # a process stuck in a kernel wait may not end on a forced stop: the job ends anyway, and the lock stays with the process
    if ($stall.Stopped) {
        if (-not $stoppedAt) { $stoppedAt = Get-Date }
        elseif (((Get-Date) - $stoppedAt).TotalSeconds -ge 30) { break }
    }
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
if ($watch -and $watch.State -eq 'Failed') { Write-Host ('[unity_batch] WARNING: the stall watcher failed: ' + $watch.JobStateInfo.Reason) }
if ($stall.Stopped) {
    $code = 125
    Write-Host ("[unity_batch] stalled: log unchanged for {0} min, last line: {1}; stopped PID {2}" -f $stall.Minutes.ToString('F1', [Globalization.CultureInfo]::InvariantCulture), $stall.LastLine, $p.Id)
    if (-not $p.HasExited) { Write-Host ("[unity_batch] WARNING: PID {0} had not exited 30 s after the stop ({1})" -f $p.Id, $stall.Kill) }
    Start-Sleep -Seconds 3
} elseif ($code -ne 124) {
    $code = $p.ExitCode
    Write-Host ("[unity_batch] {0} exit {1} ({2:N1} min)" -f (Get-Date -Format 'HH:mm:ss'), $code, ((Get-Date) - $t0).TotalMinutes)
}
# A failed compile leaves the lock behind; never remove one that another Unity on this project holds.
if ((Test-Path $lock) -and @(Get-ProjectUnity).Count -eq 0) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
}   # not $lockTimedOut

} finally {
    if ($watch) { Stop-Job -Job $watch -ErrorAction SilentlyContinue; Remove-Job -Job $watch -Force -ErrorAction SilentlyContinue }
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
