<#
.SYNOPSIS
  Builds and runs the Unity-free headless tests under plain Mono, outside Unity.

.DESCRIPTION
  Uses the Mono csc and mono.exe of the Unity install named in Tools\unity_batch.ps1 and the Editor build of
  Newtonsoft.Json from Library\PackageCache (com.unity.nuget.newtonsoft-json). Output goes to
  %TEMP%\hs2next_mono_tests\<suite>\ (never into the project, so a running Unity is not disturbed).

  -Suite scene  three assemblies, as Unity splits them: Api.dll (Assets\HS2Next\Api\**, the HS2Next.Api asmdef),
                Core.dll (Runtime\Save\** without Unity\, Runtime\Scene\{Model,Commands,Events,Document,Routes,Clock}\** and,
                since Phase 5.R, Runtime\Scene\Runtime\Effects\{FogMath,SunShaftMath}.cs when present, since Phase 6.2
                Runtime\Scene\{Timeline,Overrides}\**, since Phase 6.3 Runtime\Scene\AdvancedPose\**, since Phase 6.4 Runtime\Scene\Materials\** and since Phase 6.5 Runtime\Scene\Constraints\** when present (the ui suite's
                Core.dll likewise); the
                Unity-free part of Assembly-CSharp: a successful build proves that it needs no UnityEngine) and
                SceneTests.exe (Editor\SaveFormat\TestRunner.cs, Editor\SaveFormat\DocTests_Fuzz.cs, Editor\SceneModel\**,
                Tools\mono_tests\TestMain.cs; like Assembly-CSharp-Editor it sees only the public surface of Core).
                Runs the groups of namespace HS2Next.Scenes.Tests with fixtures from Tests\.
  -Suite save   the Phase 2.1 harness from the repository files: Runtime\Save\** (without Unity\), Api\** and
                Editor\SaveFormat\** in one SaveTests.exe; runs the groups of HS2Next.Save.Tests with fixtures from
                Tests\SaveFormat\.
  -Suite pose   HS2 pose files: HS2Next.Pose.dll (Runtime\Import\Pose\**
                alone with System references only: a successful build proves it needs neither UnityEngine nor other
                project code), SaveCore.dll (Runtime\Save\** without Unity\ + Api\**, for the shared TestRunner) and
                PoseTests.exe (Editor\SaveFormat\TestRunner.cs, Editor\PoseFileTest_*.cs, Tools\mono_tests\TestMain.cs).
                Runs the groups of HS2Next.Import.Pose.Tests with fixtures from Tests\. -PoseRoot <pose folder> also runs
                the corpus census (group PoseCorpusGroup; baseline Tests\Pose\corpus_census.json; the measured census is
                written to the output folder). -SrcDir / -TestsDir compile staged copies instead of the project files.
  -Suite bridge the automation bridge: HS2Next.Api.dll (Api\**), HS2Next.Bridge.dll
                (Bridge\** without Unity\ and Tests\: a successful build proves that the transport, the dispatcher and the
                JSON layer need no UnityEngine) and BridgeTests.exe (Bridge\Tests\** without Unity\,
                Tools\mono_tests\BridgeMain.cs). Runs the groups of HS2Next.Bridge.Tests.BridgeTestGroups (HTTP on
                ephemeral 127.0.0.1 ports, about 10 s). -SrcDir: a staged copy of the Bridge folder.
  -Suite ui     the Unity-free UI core: HS2Next.UiCore.dll (Runtime\UI\Core\** alone with
                System and Newtonsoft.Json references only: a successful build proves the folder rule — no
                UnityEngine, no other project code), HS2Next.Api.dll and HS2Next.Core.dll as in -Suite scene (the field
                schema the inspector tables resolve against) and UiTests.exe (Editor\SaveFormat\TestRunner.cs,
                Editor\UiCore\**, Tools\mono_tests\TestMain.cs). Runs the groups of HS2Next.UI.Core.Tests; the presets and
                strings.tsv are read from Assets\HS2Next\Resources\UI. -SrcDir / -TestsDir: staged copies of
                Runtime\UI\Core / Editor\UiCore.
  -Suite maker  the Maker lists: HS2Next.Api.dll, HS2Next.SaveCore.dll (for the
                shared TestRunner), HS2Next.MakerLists.dll (Runtime\Content\Maker\** without Unity\ + Runtime\IO\ZipIndex.cs,
                with System references and Api only: a successful build proves the lists need no UnityEngine) and
                MakerTests.exe (Editor\SaveFormat\TestRunner.cs, Editor\MakerLists\**, Tools\mono_tests\TestMain.cs). Runs the
                groups of HS2Next.Content.Maker.Tests with the project root as fixtures: the zipmod cache
                Cache\modlists-hs2-install-*.tsv, the vanilla list fixture Cache\maker\vanilla_lists.tsv (MakerListsDump.Run
                writes it) and the zipmod manifests (cached in Cache\makerinfo-*.tsv); the install groups skip without them.
                -SrcDir / -TestsDir: staged copies of Runtime\Content\Maker / Editor\MakerLists.
  -Suite content the zipmod scan, the per-asset redirect and the settings keys (Phase 4.4): HS2Next.Api.dll, HS2Next.SaveCore.dll
                (the shared TestRunner), HS2Next.ContentCore.dll (Runtime\Content\{AssetRedirect, Hs2SerializedFile, Hs2Lzma, ZipmodSourceBase,
                ZipmodInfo, IContentSource, ListEntry, CharaListCsv, ListCategories, SettingsKeys, BundleOwners, BundleLru, TextureHeaders,
                TextureCheck}.cs, Studio\StudioCsv.cs,
                Runtime\IO\ZipIndex.cs and Tools\mono_tests\UnityContentStub.cs, the two UnityEngine types those files name;
                since Phase 5.3 also Runtime\App\Setup\**, the Unity-free first-run and folder classes, with Newtonsoft.Json)
                and ContentTests.exe (Editor\SaveFormat\TestRunner.cs, Editor\ContentRedirect\**, Tools\mono_tests\TestMain.cs).
                Runs the groups of HS2Next.Content.Tests with the project root as fixtures (the bundles of
                Editor\Fixtures\Redirect; Config\settings.example.json for SettingsKeysTests). -SrcDir / -TestsDir: staged copies of
                the Runtime\Content files / the test folder; -SetupDir: a staged copy of Runtime\App\Setup.
  -Suite plugins the plugin loader's Unity-free core and the sample's model (Phase 5.1): HS2Next.Api.dll, HS2Next.SaveCore.dll
                (the shared TestRunner), HS2Next.PluginCore.dll (Runtime\App\Plugins\Core\** with System, Newtonsoft.Json and
                Api references only: a successful build proves the folder rule - no UnityEngine, no other project code) and
                PluginTests.exe (Editor\SaveFormat\TestRunner.cs, Tests\Plugins\LoaderTests\**, the sample's model files
                Samples\Sequencer\src\{Sequence,Playback}.cs and its tests Samples\Sequencer.Tests\** when present,
                Tools\mono_tests\TestMain.cs). Runs the groups of HS2Next.App.Plugins.Tests (PL1-PL4: manifest, order, versions,
                folder switches, and the host parts that need no engine), then those of Stp3.Samples.Sequencer.Tests; the exit
                code is the sum. Fixtures are written to the run's temp folder. -SrcDir / -TestsDir: staged copies of
                Runtime\App\Plugins\Core / Tests\Plugins\LoaderTests.

.EXAMPLE
  & Tools\mono_tests.ps1                         # suite scene, every group
  & Tools\mono_tests.ps1 -Suite scene -Groups Tree,Commands
  & Tools\mono_tests.ps1 -Suite save
  & Tools\mono_tests.ps1 -Suite pose
  & Tools\mono_tests.ps1 -Suite pose -PoseRoot '<HS2 root>\UserData\studio\pose' -Groups Corpus
  & Tools\mono_tests.ps1 -Suite bridge           # or -Groups Batch,Safety
  & Tools\mono_tests.ps1 -Suite ui               # or -Groups Keymap,Schema
  & Tools\mono_tests.ps1 -Suite maker            # or -Groups Rules (synthetic rows only)
  & Tools\mono_tests.ps1 -Suite content          # the zipmod set, the per-asset redirect, the scan caches (or -Groups Version)
  & Tools\mono_tests.ps1 -Suite plugins          # the loader core (PL1-PL4) and the sample sequencer's model (SQ1-SQ4)
  & Tools\mono_tests.ps1 -NoRun                  # build only
  $env:HS2N_SCENE_FUZZ = 10000; & Tools\mono_tests.ps1 -Groups Fuzz
  & Tools\mono_tests.ps1 -Suite scene -Full     # the Maker model fuzz at 10,000 seeds x 300 operations

.NOTES
  Exit code: 0 = all passed; 1 = build failed; otherwise the number of failed checks (capped at 100).
#>
[CmdletBinding()]
param(
    [ValidateSet('scene', 'save', 'pose', 'bridge', 'ui', 'maker', 'content', 'plugins')][string]$Suite = 'scene',
    [string[]]$Groups = @(),
    [switch]$NoRun,
    # Suite scene: the -Full sizes of the long groups (HS2N_APPEARANCE_FUZZ=full: the Maker model fuzz 10,000 x 300).
    [switch]$Full,
    [string]$OutDir,
    # Suite scene: the test sources (default Assets\HS2Next\Editor\SceneModel). A copy outside Assets lets work in
    # progress be compiled here before it reaches the project, where a broken file stops every Unity run.
    # Suite pose: the folder holding PoseFileTest_*.cs (default Assets\HS2Next\Editor).
    [string]$TestsDir,
    # Suite pose: the runtime sources (default Assets\HS2Next\Runtime\Import\Pose).
    [string]$SrcDir,
    # Suite pose: a pose library folder for the corpus census (e.g. <HS2 root>\UserData\studio\pose).
    [string]$PoseRoot,
    # Suite content: a staged copy of Runtime\App\Setup (the first-run and folder classes, Phase 5.3).
    [string]$SetupDir
)

$ErrorActionPreference = 'Stop'
$Project = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Project 'Assets\HS2Next'

# Unity install: derived as unity_batch.ps1 derives its -Unity default (HS2N_UNITY, else the Unity Hub install of the
# editor version in ProjectSettings\ProjectVersion.txt under Program Files).
if ($env:HS2N_UNITY) { $unityExe = $env:HS2N_UNITY }
else {
    $pv = Join-Path $Project 'ProjectSettings\ProjectVersion.txt'
    $m = if (Test-Path -LiteralPath $pv) { Select-String -LiteralPath $pv -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1 } else { $null }
    if (-not $m) { throw "no m_EditorVersion in $pv (set HS2N_UNITY)" }
    $unityExe = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $m.Matches[0].Groups[1].Value + '\Editor\Unity.exe')
}
$monoRoot = Join-Path (Split-Path -Parent $unityExe) 'Data\MonoBleedingEdge'
$mono = Join-Path $monoRoot 'bin\mono.exe'
$csc = Join-Path $monoRoot 'lib\mono\4.5\csc.exe'
$facade = Join-Path $monoRoot 'lib\mono\4.5\Facades\netstandard.dll'
foreach ($f in $mono, $csc, $facade) { if (-not (Test-Path $f)) { throw "not found: $f" } }
$nj = Get-ChildItem (Join-Path $Project 'Library\PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'Runtime\Newtonsoft.Json.dll' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nj) { throw 'Newtonsoft.Json.dll not found under Library\PackageCache (open the project in Unity once)' }

if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) "hs2next_mono_tests\$Suite" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
Copy-Item $nj $OutDir -Force

function Get-Sources([string[]]$roots, [string[]]$exclude = @()) {
    $list = @()
    foreach ($r in $roots) {
        if (Test-Path $r -PathType Leaf) { $list += (Resolve-Path $r).Path; continue }
        if (-not (Test-Path $r)) { continue }
        $list += Get-ChildItem $r -Recurse -Filter *.cs | ForEach-Object { $_.FullName } |
            Where-Object { $p = $_; -not ($exclude | Where-Object { $p -like $_ }) }
    }
    return $list
}

function Invoke-Csc([string]$out, [string]$target, [string[]]$refs, [string[]]$sources) {
    $rsp = "$out.rsp"
    $lines = @('-nologo', '-langversion:9.0', '-debug:portable', '-optimize+', '-nowarn:1591', "-target:$target", "-out:`"$out`"",
        '-r:System.dll', '-r:System.Core.dll', '-r:System.IO.Compression.dll', '-r:System.IO.Compression.FileSystem.dll',
        '-r:System.Numerics.dll', "-r:`"$facade`"") + ($refs | ForEach-Object { "-r:`"$_`"" }) + ($sources | ForEach-Object { "`"$_`"" })
    Set-Content -Path $rsp -Value $lines -Encoding ascii
    & $mono $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED: $([IO.Path]::GetFileName($out))"; exit 1 }
}

$save = Join-Path $Assets 'Runtime\Save'
$saveUnity = (Join-Path $save 'Unity') + '\*'
$api = Join-Path $Assets 'Api'
$testMain = Join-Path $PSScriptRoot 'mono_tests\TestMain.cs'
$t0 = Get-Date

if ($Suite -eq 'scene') {
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $coreDll = Join-Path $OutDir 'HS2Next.Core.dll'
    $exe = Join-Path $OutDir 'SceneTests.exe'
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    $scene = Join-Path $Assets 'Runtime\Scene'
    $coreSrc = Get-Sources @($save) @($saveUnity)
    $coreSrc += Get-Sources @((Join-Path $scene 'Model'), (Join-Path $scene 'Commands'), (Join-Path $scene 'Events'), (Join-Path $scene 'Document'),
        (Join-Path $scene 'Routes'), (Join-Path $scene 'Clock'))
    # Phase 5.R: the Unity-free effect arithmetic of the scene runtime (a file not written yet joins when it exists)
    $coreSrc += @('FogMath.cs', 'SunShaftMath.cs') | ForEach-Object { Join-Path $scene "Runtime\Effects\$_" } | Where-Object { Test-Path -LiteralPath $_ }
    # Phase 6.2: the Unity-free Timeline engine and the override layer's core (a folder not written yet joins when it exists)
    $coreSrc += Get-Sources @((Join-Path $scene 'Timeline'), (Join-Path $scene 'Overrides'))
    # Phase 6.3: the Unity-free part of advanced pose (addresses, edits); Phase 6.4: of material edits (keys, edits)
    $coreSrc += Get-Sources @((Join-Path $scene 'AdvancedPose'), (Join-Path $scene 'Materials'), (Join-Path $scene 'Constraints'))
    Invoke-Csc $coreDll 'library' @($nj, $apiDll) $coreSrc
    $testSrc = Get-Sources @((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs'), (Join-Path $Assets 'Editor\SaveFormat\DocTests_Fuzz.cs'),
        $(if ($TestsDir) { $TestsDir } else { Join-Path $Assets 'Editor\SceneModel' }), $testMain)
    Invoke-Csc $exe 'exe' @($nj, $apiDll, $coreDll) $testSrc
    $runArgs = @('--ns', 'HS2Next.Scenes.Tests', '--fixtures', (Join-Path $Project 'Tests'))
    if ($Full) { $env:HS2N_APPEARANCE_FUZZ = 'full' }
} elseif ($Suite -eq 'pose') {
    $poseDll = Join-Path $OutDir 'HS2Next.Pose.dll'
    $saveDll = Join-Path $OutDir 'HS2Next.SaveCore.dll'
    $exe = Join-Path $OutDir 'PoseTests.exe'
    Invoke-Csc $poseDll 'library' @() (Get-Sources @($(if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Runtime\Import\Pose' })))
    Invoke-Csc $saveDll 'library' @($nj) ((Get-Sources @($save) @($saveUnity)) + (Get-Sources @($api)))
    $testsRoot = if ($TestsDir) { $TestsDir } else { Join-Path $Assets 'Editor' }
    $poseTests = @(Get-ChildItem $testsRoot -File -Filter 'PoseFileTest_*.cs' | Where-Object { $_.Extension -eq '.cs' } | ForEach-Object { $_.FullName })
    if ($poseTests.Count -eq 0) { throw "no PoseFileTest_*.cs under $testsRoot" }
    Invoke-Csc $exe 'exe' @($nj, $poseDll, $saveDll) (@((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs')) + $poseTests + @($testMain))
    $runArgs = @('--ns', 'HS2Next.Import.Pose.Tests', '--fixtures', (Join-Path $Project 'Tests'))
    if ($PoseRoot) { $env:HS2N_POSE_ROOT = (Resolve-Path $PoseRoot).Path } else { Remove-Item Env:HS2N_POSE_ROOT -ErrorAction SilentlyContinue }
    $env:HS2N_POSE_OUT = $OutDir
} elseif ($Suite -eq 'bridge') {
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $bridgeDll = Join-Path $OutDir 'HS2Next.Bridge.dll'
    $exe = Join-Path $OutDir 'BridgeTests.exe'
    $bridge = if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Bridge' }
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    Invoke-Csc $bridgeDll 'library' @($nj, $apiDll) (Get-Sources @($bridge) @(((Join-Path $bridge 'Unity') + '\*'), ((Join-Path $bridge 'Tests') + '\*')))
    $testSrc = (Get-Sources @((Join-Path $bridge 'Tests')) @((Join-Path $bridge 'Tests\Unity') + '\*')) + @((Join-Path $PSScriptRoot 'mono_tests\BridgeMain.cs'))
    Invoke-Csc $exe 'exe' @($nj, $apiDll, $bridgeDll) $testSrc
    $runArgs = @()
} elseif ($Suite -eq 'ui') {
    $uiDll = Join-Path $OutDir 'HS2Next.UiCore.dll'
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $coreDll = Join-Path $OutDir 'HS2Next.Core.dll'
    $exe = Join-Path $OutDir 'UiTests.exe'
    Invoke-Csc $uiDll 'library' @($nj) (Get-Sources @($(if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Runtime\UI\Core' })))
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    $scene = Join-Path $Assets 'Runtime\Scene'
    $coreSrc = Get-Sources @($save) @($saveUnity)
    $coreSrc += Get-Sources @((Join-Path $scene 'Model'), (Join-Path $scene 'Commands'), (Join-Path $scene 'Events'), (Join-Path $scene 'Document'),
        (Join-Path $scene 'Routes'), (Join-Path $scene 'Clock'))
    # Phase 5.R: the Unity-free effect arithmetic of the scene runtime (a file not written yet joins when it exists)
    $coreSrc += @('FogMath.cs', 'SunShaftMath.cs') | ForEach-Object { Join-Path $scene "Runtime\Effects\$_" } | Where-Object { Test-Path -LiteralPath $_ }
    # Phase 6.2: the Unity-free Timeline engine and the override layer's core (a folder not written yet joins when it exists)
    $coreSrc += Get-Sources @((Join-Path $scene 'Timeline'), (Join-Path $scene 'Overrides'))
    # Phase 6.3: the Unity-free part of advanced pose (addresses, edits); Phase 6.4: of material edits (keys, edits)
    $coreSrc += Get-Sources @((Join-Path $scene 'AdvancedPose'), (Join-Path $scene 'Materials'), (Join-Path $scene 'Constraints'))
    Invoke-Csc $coreDll 'library' @($nj, $apiDll) $coreSrc
    $testSrc = Get-Sources @((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs'),
        $(if ($TestsDir) { $TestsDir } else { Join-Path $Assets 'Editor\UiCore' }), $testMain)
    # Phase 6.1: the plugin forms' Unity-free part (Runtime\UI\Plugins\Core, with Api and UiCore) when it exists
    $formsCore = Join-Path $Assets 'Runtime\UI\Plugins\Core'
    $uiRefs = @($nj, $apiDll, $coreDll, $uiDll)
    if (Test-Path -LiteralPath $formsCore) { $formsDll = Join-Path $OutDir 'HS2Next.UiPlugins.dll'; Invoke-Csc $formsDll 'library' @($nj, $apiDll, $uiDll) (Get-Sources @($formsCore)); $uiRefs += $formsDll }
    Invoke-Csc $exe 'exe' $uiRefs $testSrc
    $runArgs = @('--ns', 'HS2Next.UI.Core.Tests', '--fixtures', (Join-Path $Assets 'Resources\UI'))
} elseif ($Suite -eq 'maker') {
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $saveDll = Join-Path $OutDir 'HS2Next.SaveCore.dll'
    $makerDll = Join-Path $OutDir 'HS2Next.MakerLists.dll'
    $exe = Join-Path $OutDir 'MakerTests.exe'
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    Invoke-Csc $saveDll 'library' @($nj, $apiDll) (Get-Sources @($save) @($saveUnity))
    $maker = if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Runtime\Content\Maker' }
    Invoke-Csc $makerDll 'library' @($apiDll) ((Get-Sources @($maker) @((Join-Path $maker 'Unity') + '\*')) + @((Join-Path $Assets 'Runtime\IO\ZipIndex.cs')))
    $testSrc = Get-Sources @((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs'),
        $(if ($TestsDir) { $TestsDir } else { Join-Path $Assets 'Editor\MakerLists' }), $testMain)
    Invoke-Csc $exe 'exe' @($nj, $apiDll, $saveDll, $makerDll) $testSrc
    $runArgs = @('--ns', 'HS2Next.Content.Maker.Tests', '--fixtures', $Project)
} elseif ($Suite -eq 'content') {
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $saveDll = Join-Path $OutDir 'HS2Next.SaveCore.dll'
    $contentDll = Join-Path $OutDir 'HS2Next.ContentCore.dll'
    $exe = Join-Path $OutDir 'ContentTests.exe'
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    Invoke-Csc $saveDll 'library' @($nj, $apiDll) (Get-Sources @($save) @($saveUnity))
    $content = if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Runtime\Content' }
    $contentSrc = @('AssetRedirect.cs', 'Hs2SerializedFile.cs', 'Hs2Lzma.cs', 'ZipmodSourceBase.cs', 'ZipmodInfo.cs', 'IContentSource.cs', 'ListEntry.cs',
        'CharaListCsv.cs', 'ListCategories.cs', 'SettingsKeys.cs', 'BundleOwners.cs', 'BundleLru.cs', 'TextureHeaders.cs', 'TextureCheck.cs') |
        ForEach-Object { Join-Path $content $_ } | Where-Object { Test-Path -LiteralPath $_ }   # a file not written yet joins when it exists
    $contentSrc += @((Join-Path $Assets 'Runtime\Content\Studio\StudioCsv.cs'), (Join-Path $Assets 'Runtime\IO\ZipIndex.cs'),
        (Join-Path $PSScriptRoot 'mono_tests\UnityContentStub.cs'))
    # Phase 5.3: the Unity-free first-run and folder classes (Runtime\App\Setup\**, no UnityEngine by the folder's rule)
    $contentSrc += Get-Sources @($(if ($SetupDir) { $SetupDir } else { Join-Path $Assets 'Runtime\App\Setup' }))
    Invoke-Csc $contentDll 'library' @($nj, $apiDll) $contentSrc
    $testSrc = Get-Sources @((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs'),
        $(if ($TestsDir) { $TestsDir } else { Join-Path $Assets 'Editor\ContentRedirect' }), $testMain)
    Invoke-Csc $exe 'exe' @($nj, $apiDll, $saveDll, $contentDll) $testSrc
    $runArgs = @('--ns', 'HS2Next.Content.Tests', '--fixtures', $Project)
} elseif ($Suite -eq 'plugins') {
    $apiDll = Join-Path $OutDir 'HS2Next.Api.dll'
    $saveDll = Join-Path $OutDir 'HS2Next.SaveCore.dll'
    $pluginDll = Join-Path $OutDir 'HS2Next.PluginCore.dll'
    $exe = Join-Path $OutDir 'PluginTests.exe'
    Invoke-Csc $apiDll 'library' @() (Get-Sources @($api))
    Invoke-Csc $saveDll 'library' @($nj, $apiDll) (Get-Sources @($save) @($saveUnity))
    Invoke-Csc $pluginDll 'library' @($nj, $apiDll) (Get-Sources @($(if ($SrcDir) { $SrcDir } else { Join-Path $Assets 'Runtime\App\Plugins\Core' })))
    $sampleSrc = Join-Path $Project 'Samples\Sequencer\src'
    $sampleTests = Join-Path $Project 'Samples\Sequencer.Tests'
    $testSrc = Get-Sources @((Join-Path $Assets 'Editor\SaveFormat\TestRunner.cs'),
        $(if ($TestsDir) { $TestsDir } else { Join-Path $Project 'Tests\Plugins\LoaderTests' }), $testMain)
    $pluginNs = @('HS2Next.App.Plugins.Tests')
    if (Test-Path (Join-Path $sampleSrc 'Sequence.cs')) {
        $testSrc += Get-Sources @((Join-Path $sampleSrc 'Sequence.cs'), (Join-Path $sampleSrc 'Playback.cs'), $sampleTests)
        if (Test-Path $sampleTests) { $pluginNs += 'Stp3.Samples.Sequencer.Tests' }
    }
    Invoke-Csc $exe 'exe' @($nj, $apiDll, $saveDll, $pluginDll) $testSrc
    $runArgs = @('--fixtures', $Project)
} else {
    $exe = Join-Path $OutDir 'SaveTests.exe'
    $src = (Get-Sources @($save) @($saveUnity)) + (Get-Sources @($api, (Join-Path $Assets 'Editor\SaveFormat'), $testMain))
    Invoke-Csc $exe 'exe' @($nj) $src
    $runArgs = @('--ns', 'HS2Next.Save.Tests', '--fixtures', (Join-Path $Project 'Tests\SaveFormat'))
}
Write-Host ("[mono_tests] {0} built in {1:N1} s -> {2}" -f $Suite, ((Get-Date) - $t0).TotalSeconds, $OutDir)
if ($NoRun) { exit 0 }
$Groups = @($Groups | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Suite -eq 'plugins') {
    # one run per test namespace (the loader's, then the sample's); the exit code is the sum, capped as TestMain caps it
    $sum = 0
    foreach ($ns in $pluginNs) {
        & $mono --debug $exe --ns $ns @runArgs @Groups
        $sum += $LASTEXITCODE
    }
    exit ([Math]::Min($sum, 100))
}
& $mono --debug $exe @runArgs @Groups
exit $LASTEXITCODE
