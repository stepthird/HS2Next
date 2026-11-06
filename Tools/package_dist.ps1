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
       THIRD_PARTY.txt (THIRD_PARTY.md), settings.example.json (Config). No Plugins folder (a run-time folder). Then the
       plugin kit and the API documentation:
       a. the XML documentation of HS2Next.Api and HS2Next.Api.Unity, generated into <Dist>\doc\ by
          `python -I Tools\offline_compile.py --doc <Dist>\doc --asm-only -q` from the sources this script runs on (a
          missing doc comment fails it); -XmlDir takes the two files from a folder instead (tests of this script; refused
          with -Zip);
       b. <Stage>\PluginKit\: the kit sources of Samples\PluginKit (bin, obj and the kit's out folder never copied; only
          .cs, .csproj, .props, .sln, .config, .json, .tsv, .md and .xml files: any other file or a reparse point fails
          the run), and PLUGINS.md, PLUGINS_ja.md, PLUGINS_zh-Hans.md and COMMANDS.md from Reference\;
       c. the two XML files into <Stage>\HS2Next_Data\Managed\ (next to the assemblies, for IDEs) and
          <Stage>\PluginKit\doc\.
    4. Checks: every copied file hash-equals the app folder's (build.json aside); the staged tree holds no reparse point and
       no UserData, Config, Cache or Plugins; the top level is the allow-list, the package files and PluginKit; the kit
       (KT1): its required files present, every file a kit source, a kit document or an XML file, no bin, obj or out
       folder; each README holds its plugin section (KT6: the heading in its language over its underline); the XML pair
       in Managed\ and PluginKit\doc\ byte-equal to the generated files (KT7), and no line of it holding a section sign or
       the name of an internal document (the doc comments state behaviour only); Tools\leak_check.py --tree over the
       staged tree (every file classified as
       shipped by Tools\dist\package_manifest.json; Tools\dist\package_allow.json allows the example path of
       settings.example.json and the crawler addresses of the engine's browser table): 0 hard hits; Tools\dist\binary_scan.py
       --all-files over every file of the staged tree (counts only): nothing in the assemblies built here or in the data files
       (no compiler debug path, caller-file path, game path, other drive path, profile path, address or private token), the
       vendor files (the engine, Mono, D3D12, DirectStorage) within their measured bounds; the package texts, the kit and
       the XML files are the leak check's.
    5. -Zip (the release run): <Dist>\HS2Next-<version>-win64.zip with the entries in ordinal order under the top folder
       HS2Next\, and <zip>.sha256 ("<hash> *<name>"). Every entry gets the same time: the clock time of build.json's
       builtUtc on the 2-second grid of zip times (an entry time has no zone, so the files' local times would show the
       builder's UTC offset next to builtUtc); the same build gives the same zip bytes. A release needs a build of a clean
       commit (build.json dirty false), and the XML and the kit must come from the commit the app was built from:
       build.json's commit is HEAD, git status shows no change under Assets\HS2Next\Api, Assets\HS2Next\ApiUnity,
       Samples\PluginKit and the kit's documents in Reference\, and every kit source is tracked. -AllowDirty turns these
       failures into report lines.
    6. The report <Dist>\package_report.txt: the first line RESULT: PASS or FAIL, then the counts, sizes, the build.json
       values, the leak summary, the binary-scan counts and the zip.
  Never run the staged folder itself (a player writes next to its exe): copy it first.

.EXAMPLE
  & Tools\package_dist.ps1                 # the row PlayerBuild.dist: assemble and check, no zip
  & Tools\package_dist.ps1 -Zip            # the release run
  & Tools\package_dist.ps1 -Dist <scratch>\dist -Zip -AllowDirty   # a test zip in a folder of its own (row PluginKit.dist)

.NOTES
  Exit code 0 = PASS, 1 = FAIL (the report says why). Python 3 (python on PATH) runs the two checks with -I.
#>
[CmdletBinding()]
param(
    [string]$App,                 # default <project>\Builds\App
    [string]$Dist,                # default <project>\Builds\Dist
    [switch]$Zip,
    [switch]$AllowDirty,
    [string]$XmlDir               # tests of this script: HS2Next.Api.xml and HS2Next.Api.Unity.xml from here (not with -Zip)
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
# the plugin kit: its sources (the same layout as in the package), its documents and the XML documentation
$KitSrc = 'Samples\PluginKit'
$KitName = 'PluginKit'
$KitDocs = [ordered]@{
    'PLUGINS.md'         = 'Reference\PLUGINS.md'
    'PLUGINS_ja.md'      = 'Reference\PLUGINS_ja.md'
    'PLUGINS_zh-Hans.md' = 'Reference\PLUGINS_zh-Hans.md'
    'COMMANDS.md'        = 'Reference\COMMANDS.md'
}
$KitExt = @('.cs', '.csproj', '.props', '.sln', '.config', '.json', '.tsv', '.md', '.xml')
$KitBuildDirs = @('bin', 'obj')                    # build output of a kit project: never copied (nor the kit's out\)
$KitRequired = @('HS2NextPlugin.props', 'PluginKit.sln', 'NuGet.config', 'Hello\Hello.csproj', 'Hello\manifest.json',
    'HelloUi\HelloUi.csproj', 'HelloUi\manifest.json', 'HelloUi\strings.tsv')
$XmlNames = @('HS2Next.Api.xml', 'HS2Next.Api.Unity.xml')
$ManagedRel = 'HS2Next_Data\Managed'
# the heading of each README's plugin section (KT6), as code points so that this file stays ASCII
$ReadmePluginHeading = [ordered]@{
    'README.txt'         = 'Plugins'
    'README_ja.txt'      = -join [char[]](0x30D7, 0x30E9, 0x30B0, 0x30A4, 0x30F3)
    'README_zh-Hans.txt' = -join [char[]](0x63D2, 0x4EF6)
}

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
        if ($Zip) {
            # the XML documentation and the kit are made from this tree: it must be the commit the app was built from
            $why = @()
            $headFull = "$(& git -C $Project rev-parse HEAD 2>$null)".Trim()
            $builtFull = if ($bj.commit) { "$(& git -C $Project rev-parse --verify --quiet "$($bj.commit)^{commit}" 2>$null)".Trim() } else { '' }
            if (-not $headFull) { $why += 'git cannot read HEAD' }
            elseif ($builtFull -ne $headFull) { $why += "build.json's commit '$($bj.commit)' is not HEAD $($headFull.Substring(0, 7))" }
            $changed = @(& git -C $Project status --porcelain -- 'Assets/HS2Next/Api' 'Assets/HS2Next/ApiUnity' ($KitSrc -replace '\\', '/') @($KitDocs.Values | ForEach-Object { $_ -replace '\\', '/' }) 2>$null | Where-Object { "$_".Trim() })
            if ($changed.Count) { $why += ("{0} changed or untracked paths under the Api folders, the kit sources or the kit's documents: {1}" -f $changed.Count, ((@($changed | Select-Object -First 5) | ForEach-Object { "$_".Trim() }) -join ', ')) }
            if (-not $why.Count) { Say "release rule: build.json's commit is HEAD; the Api folders, the kit sources and the kit's documents are as committed" }
            elseif ($AllowDirty) { Say ("release rule not met (-AllowDirty): " + ($why -join '; ')) }
            else { Fail ("the XML documentation and the kit must come from the commit the app was built from: " + ($why -join '; ')) }
        }
    }
    if ($Zip -and $XmlDir) { Fail '-XmlDir is for tests of this script; a zip always generates its XML documentation' }
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

    # ---- 3a. the XML documentation of the two API assemblies (generated from this tree, or -XmlDir in tests)
    $xmlSrc = $null
    if ($XmlDir) {
        $xmlSrc = [IO.Path]::GetFullPath($XmlDir)
        Say "XML documentation: taken from $xmlSrc (-XmlDir)"
    } else {
        $xmlSrc = Join-Path $Dist 'doc'
        New-Item -ItemType Directory -Force $xmlSrc | Out-Null
        foreach ($n in $XmlNames) { $f = Join-Path $xmlSrc $n; if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
        $docOut = @(& python -I (Join-Path $Project 'Tools\offline_compile.py') --doc $xmlSrc --asm-only -q 2>&1 | ForEach-Object { "$_" } | Where-Object { $_.Trim() })
        $docCode = $LASTEXITCODE
        Say ("XML documentation: offline_compile.py --doc --asm-only into $xmlSrc, exit $docCode" + $(if ($docOut.Count) { ': ' + $docOut[-1].Trim() } else { '' }))
        if ($docCode -ne 0) { Fail ("the XML documentation was not generated without errors (exit $docCode): " + ((@($docOut | Select-Object -Last 6) | ForEach-Object { $_.Trim() }) -join ' | ')) }
    }
    $xmlInfo = [ordered]@{}
    foreach ($n in $XmlNames) {
        $f = Join-Path $xmlSrc $n
        if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { Fail "XML documentation missing: $f"; continue }
        if ($srcFiles.ContainsKey((Join-Path $ManagedRel $n))) { Fail "the app build already holds $ManagedRel\$n (the package adds it)"; continue }
        $xmlInfo[$n] = [pscustomobject]@{ Bytes = (New-Object IO.FileInfo($f)).Length; Hash = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }
        foreach ($rel in @($ManagedRel, (Join-Path $KitName 'doc'))) {
            $d = Join-Path $Stage $rel
            if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
            Copy-Item -LiteralPath $f -Destination (Join-Path $d $n)
        }
    }

    # ---- 3b. the plugin kit: the sources of Samples\PluginKit (no build output) and its documents from Reference\
    $kitRoot = Join-Path $Project $KitSrc
    $kitStage = Join-Path $Stage $KitName
    $kitFiles = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $kitRoot -PathType Container)) { Fail "the kit sources are missing: $KitSrc\" }
    else {
        $stack = New-Object System.Collections.Generic.Stack[string]
        $stack.Push($kitRoot)
        while ($stack.Count -gt 0) {
            $d = $stack.Pop()
            foreach ($e in ([IO.DirectoryInfo]$d).EnumerateFileSystemInfos()) {
                $rel = $e.FullName.Substring($kitRoot.Length).TrimStart('\')
                if (Test-Reparse $e) { Fail "a reparse point in the kit sources: $KitSrc\$rel"; continue }
                if ($e -is [IO.DirectoryInfo]) {
                    if ($KitBuildDirs -contains $e.Name -or ($d -eq $kitRoot -and $e.Name -eq 'out')) { continue }
                    $stack.Push($e.FullName)
                    continue
                }
                if ($KitExt -notcontains $e.Extension.ToLowerInvariant()) { Fail "a kit source of a type the kit does not ship: $KitSrc\$rel"; continue }
                $kitFiles.Add($rel)
            }
        }
    }
    foreach ($rel in $kitFiles) {
        $dst = Join-Path $kitStage $rel
        $dir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
        Copy-Item -LiteralPath (Join-Path $kitRoot $rel) -Destination $dst
    }
    foreach ($k in $KitDocs.Keys) {
        $src = Join-Path $Project $KitDocs[$k]
        if (-not (Test-Path -LiteralPath $src)) { Fail "kit document source missing: $($KitDocs[$k])"; continue }
        if (-not (Test-Path -LiteralPath $kitStage)) { New-Item -ItemType Directory -Force $kitStage | Out-Null }
        Copy-Item -LiteralPath $src -Destination (Join-Path $kitStage $k)
    }
    Say ("plugin kit sources: {0} files of {1}\ (bin, obj and out left out), documents {2}" -f $kitFiles.Count, $KitSrc, (@($KitDocs.Keys) -join ', '))

    # ---- 4. checks
    $links = New-Object System.Collections.Generic.List[string]
    $staged = Get-TreeFiles $Stage $links
    $stagedBytes = 0L; foreach ($f in $staged) { $stagedBytes += (New-Object IO.FileInfo(Join-Path $Stage $f)).Length }
    if ($links.Count) { Fail ("the staged tree holds reparse points: " + ($links -join ', ')) }
    $top = @(([IO.DirectoryInfo]$Stage).EnumerateFileSystemInfos() | ForEach-Object { $_.Name } | Sort-Object)
    $forbidden = @($top | Where-Object { @('UserData', 'Config', 'Cache', 'Plugins', 'abdata', 'portable.txt') -contains $_ })
    if ($forbidden.Count) { Fail ("the staged tree holds run-time entries: " + ($forbidden -join ', ')) }
    $expectedTop = @($AllowFiles + $AllowDirs + @($PackageFiles.Keys) + @($KitName)) | Sort-Object
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

    # KT1: the kit holds its required files, nothing but its sources, documents and XML files, and no build output
    $kitStaged = @($staged | Where-Object { $_.StartsWith("$KitName\", [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.Substring($KitName.Length + 1) })
    $kitWant = @($kitFiles) + @($KitDocs.Keys) + @($XmlNames | ForEach-Object { "doc\$_" })
    $kitNeed = @($KitRequired) + @($KitDocs.Keys) + @($XmlNames | ForEach-Object { "doc\$_" })
    $kitMissing = @($kitNeed | Where-Object { $kitStaged -notcontains $_ })
    $kitUnknown = @($kitStaged | Where-Object { $kitWant -notcontains $_ })
    $kitBuild = @($kitStaged | Where-Object { @(($_ -split '\\') | Select-Object -SkipLast 1 | Where-Object { @('bin', 'obj', 'out') -contains $_ }).Count })
    $kitNoCs = @('Hello', 'HelloUi' | Where-Object { $p = $_; -not @($kitStaged | Where-Object { $_.StartsWith("$p\", [StringComparison]::OrdinalIgnoreCase) -and $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) }).Count })
    if ($kitMissing.Count) { Fail ("the plugin kit lacks: " + ($kitMissing -join ', ')) }
    if ($kitUnknown.Count) { Fail ("the plugin kit holds files that are not kit files: " + (($kitUnknown | Select-Object -First 5) -join ', ')) }
    if ($kitBuild.Count) { Fail ("the plugin kit holds build output (bin, obj or out): " + (($kitBuild | Select-Object -First 5) -join ', ')) }
    if ($kitNoCs.Count) { Fail ("a kit sample has no source file: " + ($kitNoCs -join ', ')) }
    $kitBytes = 0L; foreach ($f in $kitStaged) { $kitBytes += (New-Object IO.FileInfo(Join-Path $kitStage $f)).Length }
    $xmlText = @($XmlNames | ForEach-Object { if ($xmlInfo.Contains($_)) { '{0} {1} B' -f ($_ -replace '^HS2Next\.(.*)\.xml$', '$1'), $xmlInfo[$_].Bytes } else { "$_ missing" } }) -join ' / '
    Say ("plugin kit: {0} files ({1:N1} KB), XML docs {2}; required files {3} / {4}, other files {5}, build output {6} (KT1)" -f $kitStaged.Count, ($kitBytes / 1KB), $xmlText,
        ($kitNeed.Count - $kitMissing.Count), $kitNeed.Count, $kitUnknown.Count, $kitBuild.Count)

    # KT7: the XML pair next to the assemblies and in the kit, byte-equal to the generated files
    $xmlSame = 0
    foreach ($n in $xmlInfo.Keys) {
        $h1 = (Get-FileHash -LiteralPath (Join-Path $Stage (Join-Path $ManagedRel $n)) -Algorithm SHA256).Hash
        $h2 = (Get-FileHash -LiteralPath (Join-Path $kitStage (Join-Path 'doc' $n)) -Algorithm SHA256).Hash
        if ($h1 -eq $xmlInfo[$n].Hash -and $h2 -eq $xmlInfo[$n].Hash) { $xmlSame++ } else { Fail "$n differs between $ManagedRel\, $KitName\doc\ and the generated file" }
    }
    Say ("XML documentation in $ManagedRel\ and $KitName\doc\ byte-equal to the generated files: $xmlSame / $($XmlNames.Count) (KT7)")
    # the XML is made from the development tree's doc comments: they must not cite sections or internal documents
    $xmlClean = 0
    $cite =[regex]('[' + [char]0x00A7 + ']|\b(?:SPEC|PREP|REVIEW)_\w+|\bPLAN(?:_\w+|\.md)?\b|\bDocs/\w')
    foreach ($n in $xmlInfo.Keys) {
        $hits = @([IO.File]::ReadAllLines((Join-Path $xmlSrc $n), [Text.Encoding]::UTF8) | Where-Object { $cite.IsMatch($_) }).Count
        if ($hits) { Fail "$n holds $hits lines with a section sign or an internal document name (the doc comments of the API must state behaviour only)" }
        else { $xmlClean++ }
    }
    Say ("XML documentation without section signs or internal document names: $xmlClean / $($XmlNames.Count)")

    # KT6: each README holds its plugin section: the heading in its language over a line of dashes
    $readmeOk = @()
    foreach ($k in $ReadmePluginHeading.Keys) {
        $f = Join-Path $Stage $k
        $rl = if (Test-Path -LiteralPath $f) { @([IO.File]::ReadAllLines($f, [Text.Encoding]::UTF8)) } else { @() }
        $at = -1
        for ($i = 0; $i -lt $rl.Count - 1; $i++) { if ($rl[$i].Trim() -ceq $ReadmePluginHeading[$k] -and $rl[$i + 1] -match '^-{3,}\s*$') { $at = $i + 1; break } }
        if ($at -lt 0) { Fail "$k has no plugin section (its heading over a line of dashes)" } else { $readmeOk += "$k line $at" }
    }
    Say ("README plugin sections: {0} / {1}{2} (KT6)" -f $readmeOk.Count, $ReadmePluginHeading.Count, $(if ($readmeOk.Count) { ': ' + ($readmeOk -join ', ') } else { '' }))

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
