<#
.SYNOPSIS
  Assembles the HS2Next distribution package from the tested app build (Builds\App) without building anything.

.DESCRIPTION
  One build, two kinds: PlayerBuild.BuildApp writes the app player with HS2Next_Data\StreamingAssets\build.json of kind "dev";
  the suite tests that build; this script copies it and changes only build.json, so the shipped binaries are the tested ones.
    1. Refuses when the app folder holds a top-level entry outside the allow-list and the folders a player writes next to
       itself (UserData, Cache, Config, Plugins, the abdata link - never copied), when an allow-listed entry is missing, when
       a reparse point (a link or junction) lies inside an allow-listed folder, or when build.json is missing or not kind dev.
    2. Copies the allow-list - HS2Next.exe, UnityPlayer.dll, UnityCrashHandler64.exe, HS2Next_Data\, MonoBleedingEdge\,
       D3D12\, dstorage.dll, dstoragecore.dll - into a fresh <Dist>\HS2Next\ (an earlier one is removed only after a walk
       finds no reparse point in it), file by file, never through a link; rewrites build.json with kind "distribution".
    3. Adds the package files: README.txt, README_ja.txt, README_zh-Hans.txt (Tools\dist), LICENSE.txt (LICENSE),
       THIRD_PARTY.txt (THIRD_PARTY.md), settings.example.json (Config). No plugin folder.
    4. Checks: every copied file hash-equals the app folder's (build.json aside); the staged tree holds no reparse point and
       no UserData, Config, Cache or Plugins; Tools\leak_check.py --tree over the staged tree (every file classified as
       shipped by Tools\dist\package_manifest.json; Tools\dist\package_allow.json allows the example path of
       settings.example.json and the crawler addresses of the engine's browser table): 0 hard hits; Tools\dist\binary_scan.py
       --all-files over every file of the staged tree (counts only): nothing in the assemblies built here or in the data files
       (no compiler debug path, caller-file path, game path, other drive path, profile path, address or private token), the
       vendor files (the engine, Mono, D3D12, DirectStorage) within their measured bounds; the package texts are the leak
       check's.
    5. -Zip (the release run): <Dist>\HS2Next-<version>-win64.zip with the entries in ordinal order under the top folder
       HS2Next\, and <zip>.sha256 ("<hash> *<name>"). Every entry gets the same time: the clock time of build.json's
       builtUtc on the 2-second grid of zip times (an entry time has no zone, so the files' local times would show the
       builder's UTC offset next to builtUtc); the same build gives the same zip bytes. A release needs a build of a clean
       commit (build.json dirty false) unless -AllowDirty.
    6. The report <Dist>\package_report.txt: the first line RESULT: PASS or FAIL, then the counts, sizes, the build.json
       values, the leak summary, the binary-scan counts and the zip.
  Never run the staged folder itself (a player writes next to its exe): copy it first.

.EXAMPLE
  & Tools\package_dist.ps1                 # the row PlayerBuild.dist: assemble and check, no zip
  & Tools\package_dist.ps1 -Zip            # the release run

.NOTES
  Exit code 0 = PASS, 1 = FAIL (the report says why). Python 3 (python on PATH) runs the two checks with -I.
#>
[CmdletBinding()]
param(
    [string]$App,                 # default <project>\Builds\App
    [string]$Dist,                # default <project>\Builds\Dist
    [switch]$Zip,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$Project = Split-Path -Parent $PSScriptRoot
if (-not $App) { $App = Join-Path $Project 'Builds\App' }
if (-not $Dist) { $Dist = Join-Path $Project 'Builds\Dist' }
$App = [IO.Path]::GetFullPath($App)
$Dist = [IO.Path]::GetFullPath($Dist)
$Stage = Join-Path $Dist 'HS2Next'
$ReportFile = Join-Path $Dist 'package_report.txt'
$t0 = Get-Date

$AllowFiles = @('HS2Next.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'dstorage.dll', 'dstoragecore.dll')
$AllowDirs = @('HS2Next_Data', 'MonoBleedingEdge', 'D3D12')
$RunTime = @('UserData', 'Cache', 'Config', 'Plugins', 'abdata')   # written next to a player at run time; never copied
$PackageFiles = [ordered]@{
    'README.txt'            = 'Tools\dist\README.txt'
    'README_ja.txt'         = 'Tools\dist\README_ja.txt'
    'README_zh-Hans.txt'    = 'Tools\dist\README_zh-Hans.txt'
    'LICENSE.txt'           = 'LICENSE'
    'THIRD_PARTY.txt'       = 'THIRD_PARTY.md'
    'settings.example.json' = 'Config\settings.example.json'
}
$BuildJsonRel = 'HS2Next_Data\StreamingAssets\build.json'

$lines = New-Object System.Collections.Generic.List[string]
$fails = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { $lines.Add($s); Write-Host "[package_dist] $s" }
function Fail([string]$s) { $fails.Add($s); Write-Host "[package_dist] FAIL $s" -ForegroundColor Red }

function Write-Report {
    $head = if ($fails.Count -eq 0) { 'RESULT: PASS' } else { 'RESULT: FAIL' }
    $all = @($head) + @($fails | ForEach-Object { "FAIL $_" }) + $lines + @(("time {0:N1} s" -f ((Get-Date) - $t0).TotalSeconds))
    New-Item -ItemType Directory -Force $Dist | Out-Null
    [IO.File]::WriteAllLines($ReportFile, [string[]]$all, (New-Object Text.UTF8Encoding($false)))
    Write-Host "[package_dist] $head ($ReportFile)"
}

function Test-Reparse([IO.FileSystemInfo]$i) { return [bool]($i.Attributes -band [IO.FileAttributes]::ReparsePoint) }

# The members of build.json as written (strings stay strings; dirty is true, false or null).
function Read-BuildJson([string]$text) {
    $o = [ordered]@{}
    foreach ($k in 'kind', 'version', 'commit', 'builtUtc') {
        $m = [regex]::Match($text, '"' + $k + '"\s*:\s*(?:"((?:[^"\\]|\\.)*)"|null)')
        $o[$k] = if ($m.Success -and $m.Groups[1].Success) { $m.Groups[1].Value } else { $null }
    }
    $d = [regex]::Match($text, '"dirty"\s*:\s*(true|false|null)')
    $o['dirty'] = if (-not $d.Success -or $d.Groups[1].Value -eq 'null') { $null } else { $d.Groups[1].Value -eq 'true' }
    return [pscustomobject]$o
}

# Every file under $root (relative paths), walking by hand: a reparse point is listed in $links and never entered.
function Get-TreeFiles([string]$root, [System.Collections.Generic.List[string]]$links) {
    $files = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($root)
    while ($stack.Count -gt 0) {
        $d = $stack.Pop()
        foreach ($e in ([IO.DirectoryInfo]$d).EnumerateFileSystemInfos()) {
            $rel = $e.FullName.Substring($root.Length).TrimStart('\')
            if (Test-Reparse $e) { $links.Add($rel); continue }
            if ($e -is [IO.DirectoryInfo]) { $stack.Push($e.FullName) } else { $files.Add($rel) }
        }
    }
    return , $files
}

try {
    Say "app build: $App"
    Say "staged package: $Stage"
    # ---- 1. the app folder
    if (-not (Test-Path -LiteralPath (Join-Path $App 'HS2Next.exe'))) { Fail "no app build: $App\HS2Next.exe (run PlayerBuild.app)"; Write-Report; exit 1 }
    $unexpected = @()
    foreach ($e in ([IO.DirectoryInfo]$App).EnumerateFileSystemInfos()) {
        if ($AllowFiles -contains $e.Name -or $AllowDirs -contains $e.Name) {
            if (Test-Reparse $e) { Fail "an allow-listed entry is a link: $($e.Name)" }
            continue
        }
        if ($RunTime -contains $e.Name) { continue }
        $unexpected += $e.Name
    }
    if ($unexpected.Count) { Fail ("unexpected entries in the app folder (not copied, not known): " + ($unexpected -join ', ')) }
    foreach ($n in $AllowFiles) { if (-not (Test-Path -LiteralPath (Join-Path $App $n) -PathType Leaf)) { Fail "allow-listed file missing: $n" } }
    foreach ($n in $AllowDirs) { if (-not (Test-Path -LiteralPath (Join-Path $App $n) -PathType Container)) { Fail "allow-listed folder missing: $n\" } }
    $srcBuildJson = Join-Path $App $BuildJsonRel
    $bj = $null
    $bjText = $null
    if (-not (Test-Path -LiteralPath $srcBuildJson)) { Fail "no $BuildJsonRel in the app build (a build before Phase 5.3: run PlayerBuild.app)" }
    else {
        # read the members from the text (ConvertFrom-Json would turn builtUtc into a local date); the JSON must parse
        $bjText = [IO.File]::ReadAllText($srcBuildJson, [Text.Encoding]::UTF8)
        $null = $bjText | ConvertFrom-Json
        $bj = Read-BuildJson $bjText
        if ($bj.kind -ne 'dev') { Fail "build.json of the app build has kind '$($bj.kind)', not dev" }
        Say ("build.json of the app build: kind {0}, version {1}, commit {2}, dirty {3}, builtUtc {4}" -f $bj.kind, $bj.version, $bj.commit, $bj.dirty, $bj.builtUtc)
        if ($Zip -and -not $AllowDirty -and $bj.dirty -ne $false) { Fail "a release package needs a build of a clean commit (build.json dirty is '$($bj.dirty)'; -AllowDirty overrides)" }
    }
    $srcFiles = @{}
    foreach ($n in $AllowDirs) {
        $p = Join-Path $App $n
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $links = New-Object System.Collections.Generic.List[string]
        $fs = Get-TreeFiles $p $links
        if ($links.Count) { Fail ("reparse points inside $n\: " + (($links | Select-Object -First 5) -join ', ')) }
        foreach ($f in $fs) { $srcFiles[(Join-Path $n $f)] = $true }
    }
    foreach ($n in $AllowFiles) { if (Test-Path -LiteralPath (Join-Path $App $n)) { $srcFiles[$n] = $true } }
    if ($fails.Count) { Write-Report; exit 1 }

    # ---- 2. a fresh staged folder, the copy, build.json
    New-Item -ItemType Directory -Force $Dist | Out-Null
    if (Test-Path -LiteralPath $Stage) {
        if (Test-Reparse (Get-Item -LiteralPath $Stage -Force)) { Fail "the staged folder is a link: $Stage"; Write-Report; exit 1 }
        $links = New-Object System.Collections.Generic.List[string]
        $null = Get-TreeFiles $Stage $links
        if ($links.Count) { Fail ("the earlier staged folder holds reparse points; not removed: " + (($links | Select-Object -First 5) -join ', ')); Write-Report; exit 1 }
        Remove-Item -LiteralPath $Stage -Recurse -Force
    }
    New-Item -ItemType Directory -Force $Stage | Out-Null
    $bytes = 0L
    foreach ($rel in ($srcFiles.Keys | Sort-Object)) {
        $dst = Join-Path $Stage $rel
        $dir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
        [IO.File]::Copy((Join-Path $App $rel), $dst, $false)
        $bytes += (New-Object IO.FileInfo($dst)).Length
    }
    Say ("copied {0} files of the allow-list ({1:N1} MB): {2}; {3}" -f $srcFiles.Count, ($bytes / 1MB), ($AllowFiles -join ', '), (($AllowDirs | ForEach-Object { "$_\" }) -join ', '))
    $dstBuildJson = Join-Path $Stage $BuildJsonRel
    if ($bjText) {
        # only the kind changes; every other byte stays as the build wrote it
        $rx = [regex]'"kind"\s*:\s*"dev"'
        if ($rx.Matches($bjText).Count -ne 1) { Fail "build.json of the app build does not hold exactly one kind member" }
        else {
            [IO.File]::WriteAllText($dstBuildJson, $rx.Replace($bjText, '"kind": "distribution"', 1), (New-Object Text.UTF8Encoding($false)))
            $backText = [IO.File]::ReadAllText($dstBuildJson, [Text.Encoding]::UTF8)
            $null = $backText | ConvertFrom-Json
            $back = Read-BuildJson $backText
            if ($back.kind -ne 'distribution' -or $back.commit -ne $bj.commit -or $back.version -ne $bj.version -or $back.builtUtc -ne $bj.builtUtc) {
                Fail "build.json of the package does not read back as kind distribution"
            } else { Say "build.json of the package: kind distribution (the other members as built)" }
        }
    }

    # ---- 3. the package files
    foreach ($k in $PackageFiles.Keys) {
        $src = Join-Path $Project $PackageFiles[$k]
        if (-not (Test-Path -LiteralPath $src)) { Fail "package file source missing: $($PackageFiles[$k])"; continue }
        Copy-Item -LiteralPath $src -Destination (Join-Path $Stage $k)
    }
    Say ("package files: " + (($PackageFiles.Keys | ForEach-Object { $_ }) -join ', '))

    # ---- 4. checks
    $links = New-Object System.Collections.Generic.List[string]
    $staged = Get-TreeFiles $Stage $links
    $stagedBytes = 0L; foreach ($f in $staged) { $stagedBytes += (New-Object IO.FileInfo(Join-Path $Stage $f)).Length }
    if ($links.Count) { Fail ("the staged tree holds reparse points: " + ($links -join ', ')) }
    $top = @(([IO.DirectoryInfo]$Stage).EnumerateFileSystemInfos() | ForEach-Object { $_.Name } | Sort-Object)
    $forbidden = @($top | Where-Object { @('UserData', 'Config', 'Cache', 'Plugins', 'abdata', 'portable.txt') -contains $_ })
    if ($forbidden.Count) { Fail ("the staged tree holds run-time entries: " + ($forbidden -join ', ')) }
    $expectedTop = @($AllowFiles + $AllowDirs + @($PackageFiles.Keys)) | Sort-Object
    $extra = @($top | Where-Object { $expectedTop -notcontains $_ })
    if ($extra.Count) { Fail ("unexpected top-level entries in the staged tree: " + ($extra -join ', ')) }
    $hashOk = 0; $hashBad = @()
    foreach ($rel in ($srcFiles.Keys | Sort-Object)) {
        if ($rel -eq $BuildJsonRel) { continue }
        $a = (Get-FileHash -LiteralPath (Join-Path $App $rel) -Algorithm SHA256).Hash
        $b = (Get-FileHash -LiteralPath (Join-Path $Stage $rel) -Algorithm SHA256).Hash
        if ($a -eq $b) { $hashOk++ } else { $hashBad += $rel }
    }
    if ($hashBad.Count) { Fail ("copied files that differ from the app build: " + (($hashBad | Select-Object -First 5) -join ', ')) }
    Say ("staged tree: {0} files, {1:N1} MB; top level: {2}" -f $staged.Count, ($stagedBytes / 1MB), ($top -join ', '))
    Say ("binaries hash-equal to the app build: {0} / {1} (build.json rewritten); reparse points 0; run-time folders 0" -f $hashOk, ($srcFiles.Count - [int]$srcFiles.ContainsKey($BuildJsonRel)))

    $leakOut = Join-Path $Dist 'leak_report.txt'
    $leak = & python -I (Join-Path $Project 'Tools\leak_check.py') --tree $Stage --manifest (Join-Path $Project 'Tools\dist\package_manifest.json') --allow (Join-Path $Project 'Tools\dist\package_allow.json') --out $leakOut 2>&1
    $leakCode = $LASTEXITCODE
    $leakSummary = if (Test-Path -LiteralPath $leakOut) { (Get-Content -LiteralPath $leakOut -TotalCount 1 -Encoding UTF8) } else { ($leak | Select-Object -Last 1) }
    Say "leak check (text files of the staged tree): $leakSummary (exit $leakCode; $leakOut)"
    if ($leakSummary -notmatch '^SUMMARY: 0 hard' -or $leakCode -ne 0) { Fail "the leak check of the staged tree is not 0 hard (exit $leakCode)" }

    $binOut = Join-Path $Dist 'binary_scan.txt'
    if (Test-Path -LiteralPath $binOut) { Remove-Item -LiteralPath $binOut -Force }
    $bin = & python -I (Join-Path $Project 'Tools\dist\binary_scan.py') --tree $Stage --all-files --out $binOut 2>&1
    $binCode = $LASTEXITCODE
    $binLines = if (Test-Path -LiteralPath $binOut) { @(Get-Content -LiteralPath $binOut -Encoding UTF8) } else { @($bin) }
    Say ("binary string scan: " + ($binLines | Select-Object -First 1) + " (exit $binCode; $binOut)")
    # the class and bound lines (the per-file lines stay in the scan's own report)
    foreach ($l in ($binLines | Select-Object -Skip 1 | Where-Object { $_ -notmatch '^\s' -and $_ -ne 'files with hits:' })) {
        Say ("   " + ($l -replace ': (\d+) hits in (\d+) files: .*', ': $1 hits in $2 files'))
    }
    if ($binCode -ne 0) { Fail "the binary string scan did not pass: a hit in an assembly built here or a data file, a vendor file over its bound, or a setup error (exit $binCode; $binOut)" }

    # ---- 5. the zip
    if ($Zip) {
        # one entry time for every entry: builtUtc's clock time, seconds on the 2-second grid of zip times
        $entryTime = $null
        $bu = [DateTime]::MinValue
        if ($bj -and $bj.builtUtc -and [DateTime]::TryParseExact($bj.builtUtc, 'yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture,
                ([Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal), [ref]$bu)) {
            $entryTime = New-Object DateTimeOffset($bu.Year, $bu.Month, $bu.Day, $bu.Hour, $bu.Minute, ($bu.Second - $bu.Second % 2), [TimeSpan]::Zero)
        }
        if ($fails.Count) { Fail 'no zip: the checks above failed' }
        elseif (-not $entryTime) { Fail "no zip: build.json has no builtUtc of the form yyyy-MM-ddTHH:mm:ssZ ('$($bj.builtUtc)'); the entry times come from it" }
        else {
            $ver = if ($bj -and $bj.version) { $bj.version } else { 'unknown' }
            $zipFile = Join-Path $Dist "HS2Next-$ver-win64.zip"
            if (Test-Path -LiteralPath $zipFile) { Remove-Item -LiteralPath $zipFile -Force }
            Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
            $entries = [string[]]@($staged | ForEach-Object { $_.Replace('\', '/') })
            [Array]::Sort($entries, [StringComparer]::Ordinal)
            $fsz = [IO.File]::Open($zipFile, [IO.FileMode]::CreateNew)
            try {
                $za = New-Object IO.Compression.ZipArchive($fsz, [IO.Compression.ZipArchiveMode]::Create)
                try {
                    foreach ($e in $entries) {
                        # the entry and its time are set here, the bytes copied from the staged file (no file time enters)
                        $ze = $za.CreateEntry("HS2Next/$e", [IO.Compression.CompressionLevel]::Optimal)
                        $ze.LastWriteTime = $entryTime
                        $src = [IO.File]::OpenRead((Join-Path $Stage ($e.Replace('/', '\'))))
                        try {
                            $dst = $ze.Open()
                            try { $src.CopyTo($dst) } finally { $dst.Dispose() }
                        } finally { $src.Dispose() }
                    }
                } finally { $za.Dispose() }
            } finally { $fsz.Dispose() }
            $hash = (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText("$zipFile.sha256", "$hash *$(Split-Path -Leaf $zipFile)`n", (New-Object Text.UTF8Encoding($false)))
            # read back: the entry order, the top folder, the count, the entry times (the clock time as stored, without a zone)
            $zr = [IO.Compression.ZipFile]::OpenRead($zipFile)
            try {
                $names = @($zr.Entries | ForEach-Object { $_.FullName })
                $inOrder = ($names -join "`n") -ceq ((@($entries | ForEach-Object { "HS2Next/$_" })) -join "`n")
                $topOk = @($names | Where-Object { -not $_.StartsWith('HS2Next/') }).Count -eq 0
                $otherTimes = @($zr.Entries | Where-Object { $_.LastWriteTime.DateTime -ne $entryTime.DateTime }).Count
            } finally { $zr.Dispose() }
            if (-not ($inOrder -and $topOk -and $names.Count -eq $staged.Count)) { Fail "the zip does not read back (entries $($names.Count) of $($staged.Count), order $inOrder, top folder $topOk)" }
            if ($otherTimes) { Fail "$otherTimes zip entries do not carry the entry time $($entryTime.ToString('yyyy-MM-dd HH:mm:ss')) (builtUtc)" }
            Say ("zip: {0} ({1:N1} MB, {2} entries in ordinal order under HS2Next/, every entry time {5} = builtUtc on the 2-s grid); sha256 {3} ({4}.sha256)" -f (Split-Path -Leaf $zipFile), ((New-Object IO.FileInfo($zipFile)).Length / 1MB), $names.Count, $hash, (Split-Path -Leaf $zipFile), $entryTime.ToString('yyyy-MM-dd HH:mm:ss'))
        }
    } else { Say 'zip: not made (-Zip makes it in the release run)' }
}
catch {
    Fail ("stopped: " + $_.Exception.Message)
}
Write-Report
exit $(if ($fails.Count -eq 0) { 0 } else { 1 })
