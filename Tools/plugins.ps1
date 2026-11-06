<#
.SYNOPSIS
  Builds plugin projects and stages built plugins into a plugin folder (Phase 5.1).

.DESCRIPTION
  A plugin project is a folder with a *.csproj and a manifest.json (Samples\Sequencer, Samples\PluginKit\Hello,
  Samples\PluginKit\HelloUi, Tests\Plugins\<name>). The build is `dotnet build -c Release` with the MSBuild properties the
  projects read:
    HS2NextApi      a folder holding HS2Next.Api.dll (default Library\ScriptAssemblies; the build refuses when that copy is
                    older than a file under Assets\HS2Next\Api: compile in Unity first, or pass -ApiDir)
    HS2NextScripts  Library\ScriptAssemblies (projects that reference the app's own assembly, an unsupported reference)
    NewtonsoftJson  the Newtonsoft.Json.dll of Library\PackageCache (found as Tools\mono_tests.ps1 finds it)
    UnityManaged    the Unity install's Editor\Data\Managed\UnityEngine folder
    and for the plugin kit's projects (Samples\PluginKit\HS2NextPlugin.props, whose users set only HS2NextDir):
    HS2NextManaged       the HS2NextApi folder (HS2Next.Api.dll, HS2Next.Api.Unity.dll)
    HS2NextUnityManaged  the UnityManaged folder
    HS2NextJson          the NewtonsoftJson file
  References to these are never copied next to the plugin (Private false in the projects). With -ApiDir, a project whose
  manifest's apiVersion needs a newer minor than that folder's HS2Next.Api.dll (its informational version) is skipped by
  'all' with a line saying so (so 'all' against the 1.0 fixtures builds the 1.0 plugins only) and refused when named.

  Staging copies, for each built plugin, manifest.json, the assemblies the manifest lists, the private dependencies (the
  other DLLs of the build output that the host does not provide, and the files of the project's lib\ folder) into
  <target>\<guid>\lib\, and strings.tsv when the project has one, into <target>\<guid>\. Safety: every existing component
  of the destination path is checked for a reparse point (refused if any, so a junction such as a player's abdata can never
  be the target or lie above it); the tool writes only inside <target>\<guid>\, never deletes a folder and never deletes
  recursively; a DLL locked by a running Editor or player is renamed to <name>.old-<yyyyMMddHHmmss> and the new one copied;
  *.old-* files it finds unlocked in the plugin's folders are deleted one by one.

.PARAMETER Build
  A project folder, or 'all' (Samples\Sequencer, the kit's Samples\PluginKit\Hello and HelloUi, and every
  Tests\Plugins\<name> folder with a project and a manifest).
.PARAMETER Stage
  The plugin folder: 'editor' (<project>\Plugins), 'player' (Builds\App\Plugins) or a path. Stages what -Build names (or
  'all' when -Build is absent), limited by -Only.
.PARAMETER Only
  GUIDs (comma-separated) to stage.
.PARAMETER Verify
  A built DLL: prints its assembly references; with -Expect (or for the samples, whose sets are fixed: the Sequencer
  netstandard, HS2Next.Api, Newtonsoft.Json; the kit's Hello and HelloUi netstandard, HS2Next.Api) checks that they are
  exactly the expected names (exit 1 otherwise).
.PARAMETER Report
  With -Stage: a file that receives the staged files' full paths, one per line (callers remove exactly those afterwards).

.EXAMPLE
  & Tools\plugins.ps1 -Build all
  & Tools\plugins.ps1 -Build all -Stage Spike\out\plugins\core
  & Tools\plugins.ps1 -Build Tests\Plugins\Probe -Stage player -Only stp3.test.probe -Report $env:TEMP\staged.txt
  & Tools\plugins.ps1 -Verify Samples\Sequencer\bin\Release\netstandard2.1\Stp3.SampleSequencer.dll
  & Tools\plugins.ps1 -Build all -ApiDir Tests\Plugins\fixtures\api10 -Stage Spike\out\plugins\core10   # the 1.0 plugins

.NOTES
  Exit code: 0 = done; 1 = a build failed, a check failed or the target was refused.
#>
[CmdletBinding()]
param(
    [string]$Build,
    [string]$Stage,
    [string[]]$Only = @(),
    [string]$Verify,
    [string[]]$Expect = @(),
    [string]$Report,
    # a folder with the HS2Next.Api.dll to build against instead of Library\ScriptAssemblies
    [string]$ApiDir
)

$ErrorActionPreference = 'Stop'
$Project = Split-Path -Parent $PSScriptRoot
$ScriptAssemblies = Join-Path $Project 'Library\ScriptAssemblies'
# assembly names a plugin never ships (the host provides them)
$HostNames = @('HS2Next.Api', 'HS2Next.Api.Unity', 'HS2Next.Bridge', 'Newtonsoft.Json', 'Assembly-CSharp', 'Assembly-CSharp-firstpass', 'netstandard')
$SampleRefs = @('netstandard', 'HS2Next.Api', 'Newtonsoft.Json')
$KitRefs = @('netstandard', 'HS2Next.Api')   # the plugin kit's engine-free samples

function Fail([string]$msg) { Write-Host "[plugins] $msg"; exit 1 }

function Get-References([string]$dll) {
    $fs = [IO.File]::OpenRead($dll)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $names = foreach ($h in $md.AssemblyReferences) { $r = $md.GetAssemblyReference($h); $md.GetString($r.Name) + ' ' + $r.Version }
        $pe.Dispose()
        return @($names)
    } finally { $fs.Dispose() }
}

if ($Verify) {
    $dll = (Resolve-Path -LiteralPath $Verify).Path
    $refs = Get-References $dll
    Write-Host "[plugins] $([IO.Path]::GetFileName($dll)) references:"
    $refs | ForEach-Object { Write-Host "  $_" }
    $want = if ($Expect.Count -gt 0) { @($Expect | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
            elseif ([IO.Path]::GetFileName($dll) -eq 'Stp3.SampleSequencer.dll') { $SampleRefs }
            elseif (@('Stp3.SampleHello.dll', 'Stp3.SampleHelloUi.dll') -contains [IO.Path]::GetFileName($dll)) { $KitRefs } else { @() }
    if ($want.Count -gt 0) {
        $got = @($refs | ForEach-Object { ($_ -split ' ')[0] } | Sort-Object -Unique)
        $diff = Compare-Object ($want | Sort-Object -Unique) $got
        if ($diff) { Fail ("references differ from the expected set (" + ($want -join ', ') + "): " + (($diff | ForEach-Object { $_.SideIndicator + $_.InputObject }) -join ' ')) }
        Write-Host "[plugins] references OK (exactly $($want -join ', '))"
    }
    if (-not $Build -and -not $Stage) { exit 0 }
}

# ---------------------------------------------------------------- projects

function Get-Projects([string]$spec) {
    $dirs = @()
    if (-not $spec -or $spec -eq 'all') {
        $dirs += Join-Path $Project 'Samples\Sequencer'
        $dirs += Join-Path $Project 'Samples\PluginKit\Hello'
        $dirs += Join-Path $Project 'Samples\PluginKit\HelloUi'
        $tp = Join-Path $Project 'Tests\Plugins'
        if (Test-Path $tp) { $dirs += Get-ChildItem $tp -Directory | Sort-Object Name | ForEach-Object { $_.FullName } }
    } else {
        $dirs += $(if ([IO.Path]::IsPathRooted($spec)) { $spec } else { Join-Path $Project $spec })
    }
    $list = @()
    foreach ($d in $dirs) {
        if (-not (Test-Path -LiteralPath $d -PathType Container)) { if ($spec -and $spec -ne 'all') { Fail "no project folder $d" } else { continue } }
        $proj = @(Get-ChildItem -LiteralPath $d -File -Filter *.csproj)
        $man = Join-Path $d 'manifest.json'
        if ($proj.Count -ne 1 -or -not (Test-Path -LiteralPath $man)) {
            if ($spec -and $spec -ne 'all') { Fail "$d needs exactly one .csproj and a manifest.json" }
            continue
        }
        $m = Get-Content -LiteralPath $man -Raw -Encoding UTF8 | ConvertFrom-Json
        $list += [pscustomobject]@{ Dir = (Resolve-Path -LiteralPath $d).Path; Csproj = $proj[0].FullName; Guid = $m.guid; Assemblies = @($m.assemblies)
                                    Out = Join-Path $d 'bin\Release\netstandard2.1'; ApiVersion = "$($m.apiVersion)" }
    }
    return $list
}

$projects = Get-Projects $Build

if ($Build) {
    $api = if ($ApiDir) { (Resolve-Path -LiteralPath $ApiDir).Path } else { $ScriptAssemblies }
    $apiDll = Join-Path $api 'HS2Next.Api.dll'
    if (-not (Test-Path -LiteralPath $apiDll)) { Fail "no HS2Next.Api.dll in $api (compile the project in Unity once)" }
    if (-not $ApiDir) {
        $newer = Get-ChildItem (Join-Path $Project 'Assets\HS2Next\Api') -Recurse -File -Filter *.cs | Where-Object { $_.LastWriteTimeUtc -gt (Get-Item $apiDll).LastWriteTimeUtc } | Select-Object -First 1
        if ($newer) { Fail "Library\ScriptAssemblies\HS2Next.Api.dll is older than $($newer.FullName.Substring($Project.Length + 1)): compile in Unity first (or pass -ApiDir)" }
    }
    $nj = Get-ChildItem (Join-Path $Project 'Library\PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Runtime\Newtonsoft.Json.dll' } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $nj) { Fail 'Newtonsoft.Json.dll not found under Library\PackageCache (open the project in Unity once)' }
    if ($env:HS2N_UNITY) { $unityExe = $env:HS2N_UNITY }
    else {
        $pv = Join-Path $Project 'ProjectSettings\ProjectVersion.txt'
        $mv = Select-String -LiteralPath $pv -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
        $unityExe = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $mv.Matches[0].Groups[1].Value + '\Editor\Unity.exe')
    }
    $managed = Join-Path (Split-Path -Parent $unityExe) 'Data\Managed\UnityEngine'
    if ($ApiDir) {
        # the Api version of that folder (the informational version MAJOR.MINOR.PATCH): a project that needs a newer minor
        # is skipped by 'all' and refused when named
        $pv = [Diagnostics.FileVersionInfo]::GetVersionInfo($apiDll).ProductVersion
        $have = $null
        if (-not [Version]::TryParse(($pv -replace '^(\d+\.\d+).*$', '$1'), [ref]$have)) { Fail "cannot read the Api version of $apiDll ('$pv')" }
        $keep = @()
        foreach ($p in $projects) {
            $need = $null
            if (-not [Version]::TryParse($p.ApiVersion, [ref]$need) -or $need.Major -ne $have.Major -or $need.Minor -le $have.Minor) { $keep += $p; continue }
            if ($Build -ne 'all') { Fail "$($p.Guid) declares apiVersion $($p.ApiVersion); $api holds Api $pv" }
            Write-Host "[plugins] skipped $($p.Guid): apiVersion $($p.ApiVersion) needs a newer Api than $pv in $api"
        }
        $projects = $keep
    }
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
    foreach ($p in $projects) {
        $t0 = Get-Date
        & $dotnet build $p.Csproj -c Release -nologo -v:q "-p:HS2NextApi=$api" "-p:HS2NextScripts=$ScriptAssemblies" "-p:NewtonsoftJson=$nj" "-p:UnityManaged=$managed" `
            "-p:HS2NextManaged=$api" "-p:HS2NextUnityManaged=$managed" "-p:HS2NextJson=$nj" | Out-Host
        if ($LASTEXITCODE -ne 0) { Fail "build failed: $($p.Csproj)" }
        foreach ($a in $p.Assemblies) { if (-not (Test-Path -LiteralPath (Join-Path $p.Out $a))) { Fail "$($p.Guid): the listed assembly $a is not in $($p.Out)" } }
        Write-Host ("[plugins] built {0} in {1:N1} s" -f $p.Guid, ((Get-Date) - $t0).TotalSeconds)
    }
}

if (-not $Stage) { exit 0 }

# ---------------------------------------------------------------- staging

$target = switch ($Stage) {
    'editor' { Join-Path $Project 'Plugins' }
    'player' { Join-Path $Project 'Builds\App\Plugins' }
    default { if ([IO.Path]::IsPathRooted($Stage)) { $Stage } else { Join-Path (Get-Location) $Stage } }
}
$target = [IO.Path]::GetFullPath($target)

# every existing component of the target path must be a plain folder
$probe = $target
while ($probe) {
    if (Test-Path -LiteralPath $probe) {
        $it = Get-Item -LiteralPath $probe -Force
        if ($it.Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail "refused: $probe is a reparse point (junction or link); never stage through one" }
    }
    $parent = Split-Path -Parent $probe
    if ($parent -eq $probe) { break }
    $probe = $parent
}

$only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$staged = @()
$stamp = Get-Date -Format 'yyyyMMddHHmmss'

function Test-Locked([string]$path) {
    try { $fs = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None'); $fs.Dispose(); return $false } catch { return $true }
}

function Remove-OldCopies([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return }
    foreach ($f in Get-ChildItem -LiteralPath $dir -File -Filter '*.old-*') {
        if (-not (Test-Locked $f.FullName)) { Remove-Item -LiteralPath $f.FullName -Force; Write-Host "  removed $($f.Name)" }
    }
}

function Copy-Staged([string]$src, [string]$dstDir) {
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir | Out-Null }
    $it = Get-Item -LiteralPath $dstDir -Force
    if ($it.Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail "refused: $dstDir is a reparse point" }
    $dst = Join-Path $dstDir ([IO.Path]::GetFileName($src))
    if ((Test-Path -LiteralPath $dst) -and (Test-Locked $dst)) {
        $aside = "$dst.old-$stamp"
        Rename-Item -LiteralPath $dst -NewName ([IO.Path]::GetFileName($aside))
        Write-Host "  locked: renamed aside to $([IO.Path]::GetFileName($aside))"
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    $script:staged += $dst
}

foreach ($p in $projects) {
    if ($only.Count -gt 0 -and -not ($only | Where-Object { $_ -ieq $p.Guid })) { continue }
    $dir = Join-Path $target $p.Guid
    foreach ($a in $p.Assemblies) { if (-not (Test-Path -LiteralPath (Join-Path $p.Out $a))) { Fail "$($p.Guid) is not built ($a missing in $($p.Out)); run with -Build" } }
    Remove-OldCopies $dir
    Remove-OldCopies (Join-Path $dir 'lib')
    Copy-Staged (Join-Path $p.Dir 'manifest.json') $dir
    foreach ($a in $p.Assemblies) { Copy-Staged (Join-Path $p.Out $a) $dir }
    $libs = @(Get-ChildItem -LiteralPath $p.Out -File -Filter *.dll | Where-Object {
        $n = [IO.Path]::GetFileNameWithoutExtension($_.Name)
        ($p.Assemblies -notcontains $_.Name) -and ($HostNames -notcontains $n) -and -not $n.StartsWith('UnityEngine') -and -not $n.StartsWith('Unity.') })
    $projLib = Join-Path $p.Dir 'lib'
    if (Test-Path -LiteralPath $projLib) { $libs += @(Get-ChildItem -LiteralPath $projLib -File -Filter *.dll) }
    foreach ($l in $libs) { Copy-Staged $l.FullName (Join-Path $dir 'lib') }
    $strings = Join-Path $p.Dir 'strings.tsv'
    if (Test-Path -LiteralPath $strings) { Copy-Staged $strings $dir }
    Write-Host "[plugins] staged $($p.Guid) -> $dir ($($p.Assemblies.Count) assemblies, $($libs.Count) in lib)"
}
if ($Report) { Set-Content -LiteralPath $Report -Value $staged -Encoding utf8 }
exit 0
