<#
.SYNOPSIS
  Fills the texture verdict cache of this machine ahead of a first session: runs the app player headless with the background
  texture check of every installed bundle and quits when it is done.

.DESCRIPTION
  Starts <Player> with -batchmode -nographics -noUi -texturePrescan -texturePrescanQuit, its own -userData (-UserData, default
  <project>\Spike\out\texcheck\userdata), -settings (default <project>\Config\settings.local.json) and -cacheDir (default
  <project>\Cache), so the run never reads or writes the app's per-user folder; waits for it (bounded by -TimeoutMin), then prints the verdict cache's counts (<cache>/texcheck/verdicts-v1.tsv). The app
  checks every bundle of the loaded zipmods and of the base game (LZMA bundles last) and writes one verdict line per bundle;
  later sessions open those bundles without a first check. Patched copies are written only when a bundle is opened, not by
  this scan. The player is started by path and stopped by its PID when the bound is reached.

.EXAMPLE
  & Tools\texcheck_prescan.ps1 -Player Builds\App\HS2Next.exe -Settings Config\settings.local.json
  & Tools\texcheck_prescan.ps1 -Player Builds\App\HS2Next.exe -CacheDir Cache -TimeoutMin 40
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Player,
    [string]$Settings,
    [string]$CacheDir,
    [int]$TimeoutMin = 40,
    [string]$LogFile,
    # the player's UserData (its crash marker and bridge files); never the app's per-user folder
    [string]$UserData
)
$ErrorActionPreference = 'Stop'
$Project = Split-Path -Parent $PSScriptRoot
$exe = (Resolve-Path $Player).Path
$args2 = @('-batchmode', '-nographics', '-noUi', '-texturePrescan', '-texturePrescanQuit')
if (-not $Settings) { $Settings = Join-Path $Project 'Config\settings.local.json' }
if (-not $CacheDir) { $CacheDir = Join-Path $Project 'Cache' }
$args2 += @('-settings', (Resolve-Path $Settings).Path)
$args2 += @('-cacheDir', (Resolve-Path $CacheDir).Path)
if (-not $UserData) { $UserData = Join-Path $Project 'Spike\out\texcheck\userdata' }
New-Item -ItemType Directory -Force $UserData | Out-Null
$UserData = (Resolve-Path $UserData).Path
$args2 += @('-userData', $UserData)
if (-not $LogFile) { $LogFile = Join-Path ([IO.Path]::GetTempPath()) 'hs2next_texcheck_prescan.log' }
$args2 += @('-logFile', $LogFile)
$t0 = Get-Date
$p = Start-Process -FilePath $exe -ArgumentList $args2 -PassThru -WindowStyle Hidden
Write-Host "[texcheck_prescan] started $exe (PID $($p.Id)), UserData $UserData, log $LogFile, bound $TimeoutMin min"
if (-not $p.WaitForExit($TimeoutMin * 60 * 1000)) {
    Stop-Process -Id $p.Id -Force -Confirm:$false
    Write-Host "[texcheck_prescan] stopped at the bound ($TimeoutMin min); the verdicts written so far stay"
}
$done = Select-String -Path $LogFile -Pattern '\[content\] texture prescan: (.*)' -ErrorAction SilentlyContinue | Select-Object -Last 1
if ($done) { Write-Host "[texcheck_prescan] $($done.Matches[0].Groups[1].Value)" }
$cache = if ($CacheDir) { (Resolve-Path $CacheDir).Path } else { Join-Path $Project 'Cache' }
$file = Join-Path $cache 'texcheck\verdicts-v1.tsv'
if (Test-Path $file) {
    $kinds = @{}
    foreach ($l in [IO.File]::ReadLines($file)) {
        if ($l.StartsWith('#')) { continue }
        $k = $l.Split("`t")[2]
        $kinds[$k] = 1 + [int]$kinds[$k]
    }
    Write-Host ("[texcheck_prescan] verdict cache {0}: {1} ({2:F1} min)" -f $file, (($kinds.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', '), ((Get-Date) - $t0).TotalMinutes)
} else { Write-Host "[texcheck_prescan] no verdict cache at $file (the player's cacheDir may differ: pass -CacheDir)" }
exit 0
