<#
.SYNOPSIS
  Unattended test suite of HS2Next: runs the batch tests through Tools\unity_batch.ps1, one
  Unity session after the other, and writes Spike\out\tests\<yyyyMMdd-HHmm>\summary.md with a copy of every report.

.DESCRIPTION
  Every test is one row of the row table below ("Row <id> <group> <areas> ..."), run in table order. Groups:
    headless   [TestTargets] [ContentIdTest] [SaveFormatTest] [ImportTest] [SceneModelTest] [PoseFileTest] [PoseFileTest.corpus]
               [ZipmodListTest] [StudioListTest] [CardExtTest] [CardResolveTest] (-limit 30)
               [CardImportTest] (-limit 60 -step 30) [JointCorrectionTest] [LookTest] [ExpressionTest] [DynamicBoneTest]
               [AnimationTest] [SceneRuntimeTest] [ItemMotionTest] [ItemMotionTest.play] [ItemTest] [CharaDocBuildTest]
               [FkWriterTest] [IkSolverTest] [PoseImportTest] [IkDriverTest] [CompareTest] (document builds) [ShapeMapTest.female_body] [ShapeMapTest.female_face]
               [ShapeMapTest.male_body] [ShapeMapTest.male_face] [ShapeMapTest.oor]
               [BridgeTest] (the bridge's groups + the Unity layer) [BridgeTest.play] (T2: 10 play sessions with the bridge on,
               5 with and 5 without domain reload; unity_batch -NoQuit, no graphics)
               [BridgeCoreTest] (the Core commands in edit mode, C1-C25)
               (ItemMotionTest: Phase 3.1 item motion N1-N8; .play: N9 frame order in play mode, unity_batch -NoQuit, no graphics)
               [MapTest] (Phase 3.2 maps P1-P8, edit mode) [MapTest.play] (maps in play mode, unity_batch -NoQuit, no graphics)
               [LightCameraTest] (Phase 3.3 lights, cameras, sound frame LC1-LC8, edit mode)
               [MakerCoreTest] (Phase 4.0 S1: character snapshots vs the baselines of Cache/maker/baseline, targets + 20)
               [MakerEditTest] [MakerEditTest.fuzz] (Phase 4.0 S2 morph through the scene, targets + 20; realization fuzz 40 x 40)
               [ThumbGuardTest] (v0.3 trial 2: the thumbnail header check against the suite's census; graphics: [ThumbGuardTest.play]
               [ThumbGuardTest.clash] [ThumbScroll.play]; player: [ThumbScroll.player] - every Maker thumbnail list scrolled)
               [HAnimeTest] [MosaicTest] (Phase 5.M: the H animation rows, the mosaic set; full: [PolicyTest] (the release
               policy) [HAnimeTest.corpus]; graphics: [MosaicTest.render] [MosaicTest.timing] in memory only)
               (Phase 5.1 plugins: [BridgeCoreTest] group C37 loads the staged test plugin set Spike\out\plugins\core,
               re-staged by the check with -c37Stage; full: [CommandsDoc] (BridgeHelpDump.Run, then
               gen_commands_md.py --check against Reference/COMMANDS.md); graphics: [PluginLoaderTest.play] (play mode,
               -pluginsDir Spike\out\plugins\play re-staged with -stage); player: [PluginCheck.player]
               (python -I Tools\plugin_player_check.py: the sample from Builds\App\Plugins\, PA1-PA9); mono: [Mono.plugins])
    graphics  [CharaRenderTest] (-targets -urp -diff) [RenderPipelineTest] [LookTest.render] [ExpressionTest.render] [AnimationTest.render]
               [SaveFormatTest.render] [SceneRuntimeTest.render] [ItemTest.render] [CharaDocBuildTest.render]
               [SceneRuntimeTest.play] (play mode: unity_batch -NoQuit)
               [ModShaderTest] (Phase 3.7 mod shaders M1-M7, edit mode; M5's fit regressions need a device)
               [BridgeAcceptance.play] (BridgeTest.RunPlay: the app in play mode over HTTP, A1-A14, S1-S6, H1-H5, E1-E3,
               T5 parity shots, I1, M1, LT1, FX1, IK1 (Phase 3.4 IK pose save / load); -NoQuit)
               [EffectsTest] (Phase 3.3 screen effects EF0-EF6, edit mode; EF2's strips need a device) [EffectsTest.render]
               (R1-R10 through URP, play mode, -NoQuit) [EffectsTest.play] (P1-P3: map hooks, save / load, -NoQuit)
               [DofTest.render] (Phase 3.3 item-focus DOF D0-D5) [SceneTexturesTest.render] (depth / opaque textures and the
               shaders reading them T1-T8) [SceneTexturesTest.play] (V1, particle play / pause, KriptoFX distortion, water maps; -NoQuit)
               [SkyAmbientTest] (Phase 3.3 sky / ambient / AA / falloff SA0-SA7, edit mode) [SkyAmbientTest.render] (R1 sky
               patterns, R2 ambient on a fitted sphere, R3 the falloff fit vs Built-in) [SkyAmbientTest.play] (P1 ambient through
               map loads, P2 TAA motion vectors; -NoQuit) [MeReportTest.play] (Phase 5.R NG1-NG4, the MaterialEditor shader report; -NoQuit)
               [CaptureTest.render] (Phase 3.6 screenshots S1-S9: sizes, the pixel cap, the 3.5 path, the box filter, R-M2,
               helpers / background / frame in captures, file names, the asynchronous encode; -NoQuit) [BackgroundTest.render]
               (Phase 3.6 background / frame B1-B10: the stretched mapping, occlusion, sky, DOF, frame alpha, missing files,
               FileRefs.FromPicked, the 3.5 pickers, save / load / import / undo; -NoQuit) [PauseTest.play] (Phase 3.6 global
               pause P1-P5 in Editor play with the UI: toolbar pause / step, R-M3 paused captures byte-identical, particles,
               R-M6 camera / gizmo / UI while paused, R-M4; -NoQuit) [RouteTest.play] (Phase 3.6 routes P1-P6 in Editor play
               with the UI: actions, inspector, tree rows, point gizmo drag, playback, pause, line in the view and not in captures;
               -NoQuit) [BridgeAcceptance.play.routes] (RT1 alone: routes, pause / step, screenshot, background, save / load)
    player     (default and -Full, not -Quick) [PlayerBuild.app] (the app player
               Builds\App through unity_batch), [PlayerSmoke] (the smoke player Builds\Smoke built through unity_batch, then
               run with -smoke), [BridgeAcceptance.player] (the app player with -bridgeAccept: A1-A14, S1-S6, E1-E3, T5 shots
               compared with BridgeAcceptance.play's, I1, M1, LT1, FX1, IK1), [BridgeChecks.player] (python -I Tools\player_checks.py against the app
               player; WARN while the script does not exist), [EffectsPlayerCheck.player] (python -I Tools\effects_player_check.py:
               the screen effects in the app player), [CapturePlayerCheck.player] (python -I Tools\capture_player_check.py:
               F11 / Shift+F11 through ui.input with the UI, R-M1 GPU memory of 4K / 16.6 Mpix / 8K screenshots). Players are started directly (no Unity mutex), one after the
               other, after 8 GB of free RAM; a run that passes its time limit is stopped by its PID (process tree).
    full       (-Full only) [AllCards] (Phase 5.2: one headless walk over every card and coordinate with one shared build
               per card - import, document parity P1, S1, the import statistics, S2, the loose-PNG references, the
               coordinates; it replaced ImportTest.full, CharaDocBuildTest.all, MakerCoreTest.all, MakerEditTest.all,
               CoordApply.all, CardImport.* and LooseTextureTest.cards, which stay as opt-in rows for targeted runs),
               [MakerCoreTest.baseline] (Phase 4.0: S1 baselines kept / written), [MapTest.corpus] (results .0-.6: every map
               row in chunks of 100, play mode, -nographics), [ItemTest.corpus] (results .0-.5: M9 in six chunks of 5,000
               rows: every Studio item row built, set, stepped, deleted; -nographics), the texture check rows
               [TextureCheckTest.*] and [BundleOwnershipTest] (Phase 5.2), [AllCards.render] (results .female / .male:
               CardImportTest -render over every card with the skip list of Tools\targets.json; not with -Quick);
               whole-suite only (a -Full without -Area / -Changed, or -Only naming the row): [MakerEditTest.fuzzfull]
    Phase 5.2: the edit-mode rows of the headless group run as one HeadlessBatch process (two or
  more selected; -NoBatch runs them one process each), except LeakCheck, ThumbGuardTest, the play rows and the 11 Maker build
  rows; every summary has a graphics line (the API, device losses in the suite's Unity logs, driver resets); -Gfx d3d11|d3d12
  re-runs graphics rows with that API; -GpuLock <file> (with -InputLock <file>) takes a lock file around every graphics Unity
  job and player run. The thumbnail guard step and the TextureRepair row are gone (the catalog's texture check patches every
  bundle at its open); the opt-in rows ModSweepTest (the mod-wide sweep), MakerSession.player and PerfCheck.player run only
  when -Only names them; ModSweep.player and AllCards.player (player group) run in the whole-suite -Full.
    Phase 5.3: graphics [SetupTest.play] (the setup stage, the guide's skip / -setup, the settings
  page's writes and the unknown keys in Editor play with the UI, port 17398); player [PlayerBuild.dist] (Tools\package_dist.ps1
  without -Zip after PlayerBuild.app: the distribution assembled from the tested build, DP2-DP4), [PlayerBuild.noargs] (a plain
  copy of Builds\Dist\HS2Next started with no argument on an override data root, then portable: Tools\noargs_check.py),
  [FirstRun.player] (the first-run guide walked over ui.* on an empty root: Tools\firstrun_check.py --cache warm); opt-in
  [FirstRun.cold] (the same with the default empty cache: the cold index) and [Startup.player] (Tools\startup_check.py: warm,
  cold and UI start-up times, port 17611). The three bare-exe rows run windowed on the product port 17322: they wait (at most 10
  min) until nothing listens there (else WARN, not run), take the GPU lock and create the input lock (-GpuLock / -InputLock),
  apply the game gate of Tools\session_gate.json and never start without the locks' files when -GpuLock is given. The runner
  clears HS2NEXT_DATA_ROOT (the bare-exe scripts set it for each player they start). The end-stall guard: a Unity row whose
  report is complete (written in this run, a summary line, not the play rows' provisional "did not finish" line) and whose
  process has neither exited nor written its report or its log for 3 minutes is stopped by PID (tree); the row keeps the
  report's verdict and its line says "WARN end stall <m> min (stopped)".
    mono(-Quick; any mode with -Only Mono.*; never with -NoMono) [Mono.scene] [Mono.ui] [Mono.maker] [Mono.save]
               [Mono.pose] [Mono.bridge] [Mono.content] [Mono.plugins]: Tools\mono_tests.ps1 -Suite <suite> under plain Mono
               (no Unity; Phase 5.3: Mono.bridge also runs python -I Tools\hs2n_bridge.py --self-test (TK5), Mono.content
               python -I Tools\player_launch_audit.py and its --self-test (UP9); a failing self-test fails the row), started as two
               background jobs (scene alone; the other five one after the other) when the suite starts and folded into the
               summary as rows when the Unity rows are done (TOTAL passed / failed; FAIL on a build failure or any failed
               check). While Mono.scene runs, the Unity row SceneModelTest leaves the appearance fuzz out
               (HS2N_APPEARANCE_FUZZ=off; Phase 5.2, LQ16) and Mono.scene runs it at the default 2,000 x 200.
  Before the first row the offline compile check (Tools\offline_compile.py) runs once; a tree that does not compile is a
  note in the summary and a warning on the console (the first Unity row's compile failure stops the suite as before);
  every row then passes -SkipOfflineCompile to unity_batch.
  A graphics card run that crashes natively is resumed after the crashed card (that card is reported as a crash; add
  it to "skip" in Tools\targets.json if it keeps crashing).
  Result per test: PASS / FAIL / WARN from the exit code and the report's summary line (README_tests.md lists the
  rules). The script exits 1 when any test FAILed.
    Inputs a public checkout may lack (Phase 5.4): the test targets come from Tools\targets.json
  when it exists, else from the committed Tools\targets.example.json (the same members with no cards; copy it to
  targets.json and name your cards). A row may declare development inputs (Row ... -DevInputs <paths relative to the
  project; wildcards allowed>): files the developer's machine has and a public checkout does not (recorded game
  measurements, baselines, the test targets). When one of them is absent the row is not started and reports WARN
  "development input absent: <path>"; the summary counts those rows on a line of their own. With every input present a
  row runs as before.

  Areas ("verification"): every row carries one or more area tags. -Area <a,b> keeps the rows of
  the group selection (-Quick / default / -Full) that carry one of the areas; -Changed <git revision or range> maps the
  changed paths to areas and rows through Tools\test_areas.json (a shared part - shader mapping, shared HLSL, the
  catalog, motion stages - also adds the areas chara-render and cards-full); -Only filters further. -List prints the rows
  that would run (tags, group, the last known minutes from Spike\out\tests\*\summary.md) and starts nothing.

.EXAMPLE
  pwsh -File Tools\run_tests.ps1               # headless + graphics + player groups
  pwsh -File Tools\run_tests.ps1 -Quick        # headless group + the plain-Mono suites in the background
  pwsh -File Tools\run_tests.ps1 -Quick -NoMono   # headless group only (SceneModelTest at the default fuzz size)
  pwsh -File Tools\run_tests.ps1 -Full         # everything, including the full card import / render passes
  pwsh -File Tools\run_tests.ps1 -Only LookTest,ShapeMapTest.*
  pwsh -File Tools\run_tests.ps1 -Full -Area items,maps -List
  pwsh -File Tools\run_tests.ps1 -Full -Changed HEAD -List      # the working tree against HEAD
  pwsh -File Tools\run_tests.ps1 -Changed HEAD~3..HEAD -List    # a commit range; one commit: -Changed <hash>^!
#>
[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$Full,
    [string[]]$Only,
    # Area selection (Tools\README_tests.md "Areas"): rows carrying one of these tags.
    [string[]]$Area,
    # git revision (working tree against it, untracked files included) or range (A..B, A...B, <rev>^!) whose changed
    # paths select areas and rows through Tools\test_areas.json.
    [string]$Changed,
    # Print the selected rows and start nothing.
    [switch]$List,
    [int]$RenderTimeoutMin = 180,
    # Testing aids for the full-group card runs: only the cards of this list file (paths relative to the card
    # directory, one per line), and extra CardImportTest arguments (e.g. -norepair).
    [string]$CardList,
    [string[]]$ImportArgs = @(),
    # No plain-Mono background job (the mono rows are not selected; SceneModelTest runs its default fuzz size in Unity).
    [switch]$NoMono,
    # No offline compile check before the first row.
    [switch]$NoOfflineCompile,
    # No HeadlessBatch: every member row runs in its own Unity process as before.
    [switch]$NoBatch,
    # The batch process's limit (minutes); a member's own limit is its row's -TimeoutMin.
    [int]$BatchTimeoutMin = 60,
    # The graphics API of the graphics rows (re-running a row that lost the device): d3d11 or d3d12; default Unity's (D3D12).
    [ValidateSet('d3d11', 'd3d12')][string]$Gfx,
    # A lock file taken around every graphics Unity job and every player run (created exclusively, removed when the process
    # ends); -InputLock: a lock file that must be absent too. A run waits at most -LockWaitMin for both, else its row FAILs.
    [string]$GpuLock,
    [string]$InputLock,
    [int]$LockWaitMin = 60
)

$ErrorActionPreference = 'Stop'
# The release policy: every Unity, player and Mono process this suite starts runs the
# official behaviour, whatever local private modules the developer's machine has turned on.
$env:HS2NEXT_PRIVATE_OFF = '1'
# No process this suite starts loads a plugin from <project>\Plugins or a player's Plugins folder: the plugin rows name
# their own staged folder (-pluginsDir, or an explicit load in the test host), and PluginCheck.player clears the switch
# for the one player it starts (its acceptance uses the default discovery next to the exe).
$env:HS2NEXT_NO_PLUGINS = '1'
# A player's data root (Phase 5.3): no process of this suite inherits a stray HS2NEXT_DATA_ROOT; the bare-exe scripts set it for
# each player they start, every other player row passes -settings, -cacheDir and -userData itself.
Remove-Item Env:HS2NEXT_DATA_ROOT -ErrorAction SilentlyContinue
$Project = Split-Path -Parent $PSScriptRoot
$Batch = Join-Path $PSScriptRoot 'unity_batch.ps1'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$TestsOut = Join-Path $Project 'Spike\out\tests'
$OutDir = Join-Path $TestsOut $Stamp
$SpikeOut = Join-Path $Project 'Spike\out'
function Split-List($v) { @($v | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
if ($Only) { $Only = Split-List $Only }
if ($Area) { $Area = Split-List $Area }

# ------------------------------------------------------------------ configuration from the project
$settingsFile = Join-Path $Project 'Config\settings.local.json'
$GameRoot = $null
# The cache folder the Editor rows use (the settings' cacheDir, else <project>\Cache): the zipmod scan writes its list cache
# (Cache\modlists-*.tsv) there, which the census steps of LooseTextureTest and the ThumbGuardTest rows read.
$CacheDir = Join-Path $Project 'Cache'
if (Test-Path $settingsFile) {
    $s = Get-Content -Raw $settingsFile | ConvertFrom-Json
    if ($s.hs2Root) { $GameRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($Project, $s.hs2Root)) }
    if ($s.cacheDir) { $CacheDir = [IO.Path]::GetFullPath([IO.Path]::Combine($Project, $s.cacheDir)) }
}
# The test targets: Tools\targets.json (local; git-ignored in a public checkout), else the committed Tools\targets.example.json
# (the same members, no cards). The rows whose Editor tests read targets.json itself declare it as a development input.
$TargetsFile = Join-Path $PSScriptRoot 'targets.json'
if (-not (Test-Path -LiteralPath $TargetsFile)) { $TargetsFile = Join-Path $PSScriptRoot 'targets.example.json' }
$Targets = if (Test-Path -LiteralPath $TargetsFile) { Get-Content -Raw -LiteralPath $TargetsFile | ConvertFrom-Json } else { $null }
if (-not $Targets) { $Targets = [pscustomobject]@{ visual = [pscustomobject]@{ female = ''; male = '' }; cards = [pscustomobject]@{ female = ''; male = '' }; skip = @() }; $TargetsFile = $null }
# Vanilla shader names: Tools\survey\shader_census.tsv rows of vanilla bundle groups (labels without "mod_"):
# 'chara' for the character bundles (chara_*), else 'studio' (studio items and maps, deferred to Phase 3.1).
# A public checkout has no census (a development input): every unfitted shader then counts as a mod shader (a note says so).
$VanillaShaders = @{}
$census = Join-Path $PSScriptRoot 'survey\shader_census.tsv'
if (Test-Path $census) {
    foreach ($l in Get-Content $census -Encoding UTF8) {
        $f = $l -split "`t"
        if ($f.Count -ge 3 -and $f[0] -notlike 'mod_*') {
            if ($f[0] -like 'chara*') { $VanillaShaders[$f[2]] = 'chara' }
            elseif (-not $VanillaShaders.ContainsKey($f[2])) { $VanillaShaders[$f[2]] = 'studio' }
        }
    }
}

# ------------------------------------------------------------------ helpers
$Results = [System.Collections.Generic.List[object]]::new()
$Notes = [System.Collections.Generic.List[string]]::new()

$CompileFailed = $false
function Want([string]$id) {
    if ($script:CompileFailed) { return $false }
    if (-not $Only) { return $true }
    foreach ($p in $Only) { if ($id -like $p) { return $true } }
    return $false
}

function Rel([string]$p) { $p.Substring($Project.Length).TrimStart('\') }

# ---- the GPU lock (-GpuLock <file>, -InputLock <file>): before every graphics Unity job and every player run the runner waits
# (at most -LockWaitMin) until both files are absent, creates the GPU lock exclusively (one line: the runner, the row, its PID,
# the start and the expected end) and removes it when that process ends, also on failure. The rows whose script takes the
# same locks itself (MakerSession.player, PerfCheck.player, ModSweep.player, AllCards.player) run without the runner's lock. Without -GpuLock
# nothing is locked.
function Enter-GpuLock([string]$id, [int]$timeoutMin) {
    if (-not $GpuLock) { return $true }
    $t0 = Get-Date
    while ($true) {
        $held = @(@($GpuLock, $InputLock) | Where-Object { $_ -and (Test-Path -LiteralPath $_) })
        if ($held.Count -eq 0) {
            try {
                $fs = [IO.File]::Open($GpuLock, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
                try {
                    $now = Get-Date
                    $b = [Text.UTF8Encoding]::new($false).GetBytes(("run_tests {0} {1} PID {2} start {3} expected end {4}`n" -f $Stamp, $id, $PID,
                        $now.ToString('yyyy-MM-dd HH:mm:ss'), $now.AddMinutes($timeoutMin).ToString('yyyy-MM-dd HH:mm:ss')))
                    $fs.Write($b, 0, $b.Length)
                } finally { $fs.Close() }
                return $true
            } catch { }   # another process created it first: wait again
        }
        if (((Get-Date) - $t0).TotalMinutes -ge $LockWaitMin) { return $false }
        Start-Sleep -Seconds 15
    }
}
function Exit-GpuLock { if ($GpuLock) { Remove-Item -LiteralPath $GpuLock -Force -ErrorAction SilentlyContinue } }

# The windowed rows (Phase 5.3: the bare exe takes the focus) also create the input lock: the GPU lock first (both files absent),
# then the input lock exclusively; when another process created it in between, the GPU lock is given back and the wait goes on.
# Returns $true when the locks are held (or no -GpuLock was given), $false after -LockWaitMin.
function Enter-WindowedLocks([string]$id, [int]$timeoutMin) {
    if (-not $GpuLock) { return $true }
    $t0 = Get-Date
    while ($true) {
        $left = $LockWaitMin - ((Get-Date) - $t0).TotalMinutes
        if ($left -le 0) { return $false }
        if (-not (Enter-GpuLock $id $timeoutMin)) { return $false }
        if (-not $InputLock) { return $true }
        try {
            $fs = [IO.File]::Open($InputLock, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try {
                $now = Get-Date
                $b = [Text.UTF8Encoding]::new($false).GetBytes(("run_tests {0} {1} PID {2} start {3} expected end {4} (windowed player)`n" -f $Stamp, $id, $PID,
                    $now.ToString('yyyy-MM-dd HH:mm:ss'), $now.AddMinutes($timeoutMin).ToString('yyyy-MM-dd HH:mm:ss')))
                $fs.Write($b, 0, $b.Length)
            } finally { $fs.Close() }
            return $true
        } catch { Exit-GpuLock; Start-Sleep -Seconds 15 }
    }
}
function Exit-WindowedLocks {
    if ($InputLock -and $GpuLock) { Remove-Item -LiteralPath $InputLock -Force -ErrorAction SilentlyContinue }
    Exit-GpuLock
}

# The game gate (Tools\session_gate.json, a local file naming the owner's game processes; Phase 5.2): the names of the gate's
# processes that run now (none without the file).
$GateNames = @()
$gateFile = Join-Path $PSScriptRoot 'session_gate.json'
if (Test-Path -LiteralPath $gateFile) { try { $GateNames = @((Get-Content -Raw -LiteralPath $gateFile | ConvertFrom-Json).processes | Where-Object { $_ }) } catch { } }
function Get-GameRunning {
    if ($GateNames.Count -eq 0) { return @() }
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $n = $_.Name; @($GateNames | Where-Object { $n -like "$_*" }).Count -gt 0 } | ForEach-Object { $_.Name } | Sort-Object -Unique)
}

# True when nothing listens on 127.0.0.1:<port> (the bare-exe rows use the product port 17322); waits up to $maxMin.
function Wait-PortFree([int]$port, [double]$maxMin) {
    $t0 = Get-Date
    while ($true) {
        $l = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
        if ($l.Count -eq 0) { return $true }
        if (((Get-Date) - $t0).TotalMinutes -ge $maxMin) { return $false }
        Write-Host ("[run_tests] port {0} is in use (pid {1}); waiting" -f $port, (($l | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique) -join ', '))
        Start-Sleep -Seconds 20
    }
}

# ---- the end-stall guard (Phase 5.3): a Unity row whose report is complete and
# whose Unity process neither exits nor writes its report or its log for $EndStallMin minutes is stopped by PID (tree). The
# watcher is a thread job beside unity_batch.ps1: it finds the row's Unity.exe by its -logFile, reads the report (written
# after the row's start, a summary line as Read-Summary knows them, not the play rows' provisional "did not finish" line) and
# writes <out>\<id>.endstall.txt when it stops the process; Invoke-Unity then keeps the report's verdict (the exit code of the
# stopped process does not count) and the row's line says "WARN end stall <m> min (stopped)". The HeadlessBatch processes
# (several reports per process) keep their own limits.
$EndStallMin = 3
$UnityLogDir = if ($env:HS2N_LOG_DIR) { $env:HS2N_LOG_DIR } else { Join-Path $env:SystemDrive 'tmp\hs2next_logs' }
$EndStalls = @{}
$EndStallWatch = {
    param([string]$reportPath, [datetime]$before, [string]$logPath, [string]$marker, [double]$stallMin, [double]$limitMin)
    $t0 = Get-Date
    $pid0 = $null
    $sumRe = '(\d+) PASS, (\d+) FAIL|^RESULT: (PASS|FAIL)|(\d+) passed, (\d+) failed|RESULT \S+: (\d+) sets, (\d+) FAIL|(\d+) of (\d+) spec counts match|^test targets: (PASS|FAIL)|^cards (\d+), resolved (\d+), missing (\d+)'
    while (((Get-Date) - $t0).TotalMinutes -lt $limitMin) {
        Start-Sleep -Seconds 15
        if (-not $pid0) {
            $u = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($logPath) })
            if ($u.Count -gt 0) { $pid0 = $u[0].ProcessId }
            continue
        }
        if (-not (Get-Process -Id $pid0 -ErrorAction SilentlyContinue)) { return }
        if (-not (Test-Path -LiteralPath $reportPath)) { continue }
        $rw = (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc
        if ($rw -le $before) { continue }
        $now = (Get-Date).ToUniversalTime()
        if (($now - $rw).TotalMinutes -lt $stallMin) { continue }
        $lw = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).LastWriteTimeUtc } else { $rw }
        if (($now - $lw).TotalMinutes -lt $stallMin) { continue }
        $text = @(Get-Content -LiteralPath $reportPath -Encoding UTF8 -ErrorAction SilentlyContinue)
        if (@($text | Where-Object { $_ -match 'did not finish' }).Count -gt 0) { continue }
        if (@($text | Where-Object { $_ -match $sumRe }).Count -eq 0) { continue }
        $last = @(Get-Content -LiteralPath $logPath -Tail 1 -ErrorAction SilentlyContinue) -join ''
        $min = ($now - $rw).TotalMinutes
        & taskkill.exe /PID $pid0 /T /F 2>&1 | Out-Null
        [IO.File]::WriteAllText($marker, ($min.ToString('F1', [Globalization.CultureInfo]::InvariantCulture) + "`t$pid0`t$last"), [Text.UTF8Encoding]::new($false))
        return
    }
}

# Runs one Unity job; returns @{ Code; Minutes; Report (copied path or $null) }. $noQuit: unity_batch -NoQuit (the method
# ends Unity itself, e.g. a play-mode test through Editor/PlayModeHarness.cs). A graphics job runs with -Gfx's API and under
# -GpuLock; exit 126 = the lock stayed held for -LockWaitMin (the job did not start).
function Invoke-Unity([string]$id, [string]$method, [string[]]$methodArgs, [bool]$graphics, [string]$report, [int]$timeoutMin = 90, [bool]$noQuit = $false) {
    $reportPath = if ($report) { Join-Path $Project $report } else { $null }
    $before = if ($reportPath -and (Test-Path $reportPath)) { (Get-Item $reportPath).LastWriteTimeUtc } else { [datetime]::MinValue }
    $t0 = Get-Date
    $console = Join-Path $OutDir ("{0}.console.txt" -f ($id -replace '[^A-Za-z0-9_.-]', '_'))
    $logName = "suite_${Stamp}_" + ($id -replace '[^A-Za-z0-9_.-]', '_')
    Write-Host ("[run_tests] {0} {1,-26} {2} {3}" -f (Get-Date -Format 'HH:mm:ss'), $id, $method, ($methodArgs -join ' '))
    # the offline compile check ran once before the first row
    $p = @{ Method = $method; MethodArgs = $methodArgs; LogName = $logName; Tail = 15; TimeoutMin = $timeoutMin; SkipOfflineCompile = $true }
    if (-not $graphics) { $p.NoGraphics = $true }
    if ($noQuit) { $p.NoQuit = $true }
    if ($graphics -and $Gfx) { $p.Gfx = $Gfx }
    $locked = $false
    if ($graphics -and $GpuLock) {
        if (-not (Enter-GpuLock $id $timeoutMin)) {
            "the GPU lock ($GpuLock$(if ($InputLock) { " / $InputLock" })) stayed held for $LockWaitMin min: the job did not start" | Out-File -FilePath $console -Encoding utf8
            return @{ Code = 126; Minutes = ((Get-Date) - $t0).TotalMinutes; Report = $null; Console = $console }
        }
        $locked = $true
    }
    # the end-stall guard beside the job (rows with a report)
    $stallMarker = Join-Path $OutDir ("{0}.endstall.txt" -f ($id -replace '[^A-Za-z0-9_.-]', '_'))
    Remove-Item -LiteralPath $stallMarker -Force -ErrorAction SilentlyContinue
    $watch = $null
    if ($reportPath) {
        $watch = Start-ThreadJob -ScriptBlock $EndStallWatch -ArgumentList $reportPath, $before, (Join-Path $UnityLogDir "$logName.log"), $stallMarker,
            $EndStallMin, ($timeoutMin + 10)
    }
    try {
        & $Batch @p *>&1 | Out-File -FilePath $console -Encoding utf8
        $code = $LASTEXITCODE
    } finally {
        if ($locked) { Exit-GpuLock }
        if ($watch) { Stop-Job -Job $watch -ErrorAction SilentlyContinue; Remove-Job -Job $watch -Force -ErrorAction SilentlyContinue }
    }
    $stall = $null
    if (Test-Path -LiteralPath $stallMarker) {
        $f = @((Get-Content -LiteralPath $stallMarker -Raw -Encoding UTF8) -split "`t")
        $stall = $f[0]
        $script:EndStalls[$id] = $stall
        $Notes.Add(("{0}: end stall - the report was complete and the Unity process (pid {1}) neither exited nor wrote its report or log for {2} min; stopped by the runner (exit {3} ignored; the report's verdict kept; the log's last line: {4})" -f $id,
            $f[1], $stall, $code, $(if ($f.Count -ge 3) { $f[2].Trim() } else { '?' })))
        Write-Host ("[run_tests] {0}: end stall {1} min after the complete report: Unity stopped by PID {2}" -f $id, $stall, $f[1]) -ForegroundColor Yellow
        $code = 0
    }
    $min = ((Get-Date) - $t0).TotalMinutes
    if ($code -ne 0 -and (Select-String -LiteralPath $console -Pattern 'error CS\d+' -Quiet)) {
        Write-Host "[run_tests] compile errors (see $console): stopping the suite" -ForegroundColor Red
        $script:CompileFailed = $true
    }
    $copied = $null
    if ($reportPath -and (Test-Path $reportPath) -and (Get-Item $reportPath).LastWriteTimeUtc -gt $before) {
        $copied = Join-Path $OutDir ("{0}__{1}" -f ($id -replace '[^A-Za-z0-9_.-]', '_'), (Split-Path -Leaf $reportPath))
        Copy-Item -LiteralPath $reportPath -Destination $copied -Force
    }
    return @{ Code = $code; Minutes = $min; Report = $copied; Console = $console }
}

# Deletes a folder tree without following links: every reparse point under it (a junction such as a player's abdata link to
# the HS2 install) is unlinked first (the link only), then the tree is removed.
function Remove-TreeSafe([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return }
    $item = Get-Item -LiteralPath $dir -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($item.FullName); return }
    $links = @(Get-ChildItem -LiteralPath $dir -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending)
    foreach ($l in $links) { if ($l.PSIsContainer) { [IO.Directory]::Delete($l.FullName) } else { [IO.File]::Delete($l.FullName) } }
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}

function Add-Result([string]$id, [string]$result, [double]$minutes, [string]$numbers, [string]$report) {
    # the end-stall guard's stops of this row's Unity processes (Invoke-Unity ids = the row id or the row id + '.<part>')
    $st = @($script:EndStalls.Keys | Where-Object { $_ -eq $id -or $_.StartsWith("$id.") } | Sort-Object | ForEach-Object { $script:EndStalls[$_] })
    foreach ($m in $st) { $numbers += "; WARN end stall $m min (stopped)" }
    $Results.Add([pscustomobject]@{ Test = $id; Result = $result; Minutes = $minutes; Numbers = $numbers; Report = $report })
    $color = switch ($result) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
    Write-Host ("[run_tests]          {0,-26} {1} ({2:N1} min) {3}" -f $id, $result, $minutes, $numbers) -ForegroundColor $color
}

# Generic report parsing: the first summary-like line. Returns @{ Result; Line }.
function Read-Summary([string]$file) {
    if (-not $file) { return @{ Result = 'FAIL'; Line = 'no report written' } }
    $lines = Get-Content -LiteralPath $file -Encoding UTF8
    foreach ($l in $lines) {
        if ($l -match '(\d+) PASS, (\d+) FAIL') { return @{ Result = $(if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' }); Line = $l.Trim() } }
        if ($l -match '^RESULT: (PASS|FAIL)') { return @{ Result = $Matches[1]; Line = $l.Trim() } }
        if ($l -match '(\d+) passed, (\d+) failed') { return @{ Result = $(if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' }); Line = $l.Trim() } }
        if ($l -match 'RESULT \S+: (\d+) sets, (\d+) FAIL') { return @{ Result = $(if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' }); Line = $l.Trim() } }
        if ($l -match '(\d+) of (\d+) spec counts match') { return @{ Result = $(if ($Matches[1] -eq $Matches[2]) { 'PASS' } else { 'FAIL' }); Line = $l.Trim() } }
        if ($l -match '^test targets: (PASS|FAIL)') { return @{ Result = $Matches[1]; Line = $l.Trim() } }
        if ($l -match '^cards (\d+), resolved (\d+), missing (\d+)') { return @{ Result = $(if ([int]$Matches[3] -eq 0) { 'PASS' } else { 'WARN' }); Line = $l.Trim() } }
    }
    return @{ Result = 'FAIL'; Line = 'no summary line found' }
}

# Combines exit code and report summary.
function Judge($run, $sum) {
    if ($run.Code -eq 124) { return 'FAIL' }
    if ($run.Code -ne 0) { return 'FAIL' }
    return $sum.Result
}

function Invoke-Simple([string]$id, [string]$method, [string[]]$a, [bool]$graphics, [string]$report, [bool]$noQuit = $false, [int]$timeoutMin = 90) {
    if (-not (Want $id)) { return }
    $r = Invoke-Unity $id $method $a $graphics $report $timeoutMin $noQuit
    $sum = Read-Summary $r.Report
    $res = Judge $r $sum
    $line = $sum.Line
    if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
    Add-Result $id $res $r.Minutes $line $r.Report
}

# ---- the thumbnail census for the ThumbGuardTest rows (v0.3 trial 2, package G; Phase 5.2 made it a diagnostic): the
# per-suite thumbnail guard step (the clothes / accessories / part censuses with --record-guard and Tools\texguard_repair.py
# before the clothes play rows) is gone - the catalog's texture check patches every bundle at its open (Phase 5.2,
# 6.6). The census of every Maker list's thumbnails stays as the expectation of the ThumbGuardTest
# rows that compare with it (ThumbGuardTest.census, ThumbGuardTest.play): Tools\survey\thumb_guard_census.py --lzma-ref
# <out>\thumbguard_all\lzma_ref.tsv --workers 6 (headers only, no --record-guard, nothing repaired; about 20 s) runs once per
# suite before the first of them; its defects.tsv / lzma_ref.tsv are their -census / -lzmaRef inputs.
$ThumbGuardAll = Join-Path $OutDir 'thumbguard_all'
$ThumbCensus = $null
function Invoke-ThumbCensus {
    if ($script:ThumbCensus) { return $script:ThumbCensus }
    $script:ThumbCensus = @{ Ok = $false; Text = '' }
    $mc = @(Get-ChildItem $CacheDir -Filter 'modlists-*.tsv' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    if (-not $GameRoot -or $mc.Count -eq 0) { $script:ThumbCensus.Text = 'no hs2Root in Config\settings.local.json or no modlists-*.tsv in the cache folder (a zipmod scan writes it)'; return $script:ThumbCensus }
    Write-Host ("[run_tests] {0} {1,-26} thumb_guard_census.py --lzma-ref (the expectation of the ThumbGuardTest rows)" -f (Get-Date -Format 'HH:mm:ss'), 'ThumbCensus')
    $c = @(& python -I (Join-Path $PSScriptRoot 'survey\thumb_guard_census.py') --hs2 $GameRoot --modcache $mc[0].FullName `
        --out $ThumbGuardAll --lzma-ref (Join-Path $ThumbGuardAll 'lzma_ref.tsv') --workers 6 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $c | Out-File (Join-Path $OutDir 'ThumbCensus.console.txt') -Encoding utf8
    # the count lines ("<class>`t<first class>`t(<any class>)") of the classes that occur in this install
    $found = @($c | ForEach-Object { if ($_ -match "^(malformed bundle|mip < 1|block size|all)\t(\d+)\t") { "$($Matches[1]) $($Matches[2])" } }) -join ', '
    $script:ThumbCensus.Ok = ($code -eq 0) -and (Test-Path -LiteralPath (Join-Path $ThumbGuardAll 'defects.tsv'))
    $script:ThumbCensus.Text = if ($code -eq 0) { "census: $found" } else { "thumb_guard_census.py exit $code (ThumbCensus.console.txt)" }
    return $script:ThumbCensus
}

# A ThumbGuardTest row that compares with the census: the census first (once per suite); without its defects.tsv the test
# would skip the comparison, so the row FAILs instead.
function Invoke-ThumbGuardRow([string]$id, [string]$method, [string[]]$a, [bool]$graphics, [string]$report, [bool]$noQuit, [int]$timeoutMin) {
    $cen = Invoke-ThumbCensus
    if (-not $cen.Ok) { Add-Result $id 'FAIL' 0 "no thumbnail census of this suite: $($cen.Text)" $null; return }
    Invoke-Simple $id $method $a $graphics $report $noQuit $timeoutMin
    $last = $Results | Where-Object { $_.Test -eq $id } | Select-Object -Last 1
    if ($last) { $last.Numbers += "; $($cen.Text)" }
}

# ---- HeadlessBatch (Phase 5.2): the edit-mode headless rows of the selection run in one
# Unity process (Editor/HeadlessBatch.cs) when two or more are selected; each member keeps its own result line, minutes and
# report. Members: group headless, a method row (no -Run block) or a -Run row listed in $BatchRunRows, not -NoQuit (the play
# rows), not -OptIn, not in $BatchExclude. The batch runs where the first member's row is; a member that dies with the
# process is run alone; a member past its own limit is FAIL (exit 124) as alone; a process whose managed heap grew past
# HeadlessBatch's -recycleMB (2000 MB) ends with a recycle line and the batch continues in a fresh process. Hygiene
# (Editor/TestHygiene.cs) FAILs a member that leaves a GameObject, a callback, a registry entry or a switch behind (named
# in its line). -NoBatch: every member in its own process as before.
# Not members: LeakCheck (no Unity), ThumbGuardTest (alone as before), and the 11 Maker build rows, which slow down 1.3x as the
# 2nd member of a process and 2-3x by the 9th (native state that grows with each Maker build and only a process restart
# bounds; L0c of Phase 5.2).
$BatchExclude = @('LeakCheck', 'ThumbGuardTest', 'MakerCoreTest', 'MakerUnitTest', 'MakerEditTest', 'MakerFaceTest', 'MakerBodyTest',
    'MakerHairTest', 'AssetRedirectTest', 'MakerClothesTest', 'MakerAccessoriesTest', 'MakerFileTest', 'MakerSchemaTest')
# -Run rows whose Unity part can be a member: id -> @{ Method; Args; Report; Pre = the step the runner runs before the batch
# (returns an error text or $null); Alone = the row run on its own after the batch when Pre failed (Phase 5.4: a first run on an
# empty cache has no zipmod list cache before the batch, and the batch's first catalog writes it) }
$BatchRunRows = @{
    'LooseTextureTest' = @{ Method = 'LooseTextureTest.Run'; Args = @('-c2Stride', '10'); Report = 'Spike\out\maker\loose_texture_report.txt'; Pre = { Invoke-LooseCensus }
        Alone = { Invoke-LooseRow 'LooseTextureTest' @('-c2Stride', '10') } }
}
function Is-BatchMember($row) {
    if ($NoBatch -or $row.Group -ne 'headless' -or $row.NoQuit -or $row.OptIn -or $row.Id -in $BatchExclude) { return $false }
    if ($row.Run) { return $BatchRunRows.ContainsKey($row.Id) }
    return [bool]$row.Method
}
$BatchDone = $false
function Invoke-HeadlessBatch([object[]]$members) {
    $script:BatchDone = $true
    $dir = Join-Path $OutDir 'batch'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $plan = Join-Path $dir 'plan.tsv'; $progress = Join-Path $dir 'batch_progress.tsv'
    $lines = @("# id`tmethod`treport`ttimeoutMin`toptions`targs...")
    $preFail = @{}
    foreach ($m in $members) {
        $method = $m.Method; $report = $m.Report; $margs = @($m.MethodArgs)
        if ($m.Run) {
            $b = $BatchRunRows[$m.Id]; $method = $b.Method; $report = $b.Report; $margs = @($b.Args)
            $err = & $b.Pre
            if ($err) { $preFail[$m.Id] = $err; continue }
        }
        # SceneModelTest leaves the appearance fuzz out while Mono.scene runs the default size beside it (LQ16)
        $opt = if ($m.Id -eq 'SceneModelTest' -and $MonoScene) { 'env=HS2N_APPEARANCE_FUZZ=off' } else { '-' }
        $lines += (@($m.Id, $method, $report, $m.TimeoutMin, $opt) + $margs) -join "`t"
    }
    [IO.File]::WriteAllLines($plan, [string[]]$lines, [Text.UTF8Encoding]::new($false))
    $t0 = Get-Date; $procs = 0; $recycles = 0; $alone = @()
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        $procs++
        $console = Join-Path $OutDir "HeadlessBatch.$attempt.console.txt"
        Write-Host ("[run_tests] {0} {1,-26} HeadlessBatch.Run ({2} members, process {3})" -f (Get-Date -Format 'HH:mm:ss'), 'HeadlessBatch', ($lines.Count - 1), $attempt)
        & $Batch -Method 'HeadlessBatch.Run' -MethodArgs @('-plan', $plan, '-out', $dir, '-resume', $progress) -NoGraphics -TimeoutMin $BatchTimeoutMin `
            -LogName "suite_${Stamp}_HeadlessBatch_$attempt" -Tail 15 -SkipOfflineCompile *>&1 | Out-File -FilePath $console -Encoding utf8
        if ($LASTEXITCODE -ne 0 -and (Select-String -LiteralPath $console -Pattern 'error CS\d+' -Quiet)) { $script:CompileFailed = $true; break }
        $pl = @(Get-Content -LiteralPath $progress -Encoding UTF8 -ErrorAction SilentlyContinue)
        if ($pl | Where-Object { $_ -like "done`t*" }) { break }
        $last = @($pl | Select-Object -Last 1) -join ''
        if ($last -like "recycle`t*") { $recycles++; continue }
        # the member that was running when the process ended (begun, never ended): run alone, recorded as its end line
        $ended = @($pl | Where-Object { $_ -match "^(end|timeout)`t" } | ForEach-Object { ($_ -split "`t")[1] })
        $dead = @($pl | Where-Object { $_ -match "^begin`t" } | ForEach-Object { ($_ -split "`t")[1] } | Where-Object { $_ -notin $ended })
        foreach ($d in $dead) {
            $m = $members | Where-Object { $_.Id -eq $d } | Select-Object -First 1
            $method = if ($m.Run) { $BatchRunRows[$m.Id].Method } else { $m.Method }
            $report = if ($m.Run) { $BatchRunRows[$m.Id].Report } else { $m.Report }
            $margs = if ($m.Run) { @($BatchRunRows[$m.Id].Args) } else { @($m.MethodArgs) }
            $keepFuzz = $env:HS2N_APPEARANCE_FUZZ
            if ($d -eq 'SceneModelTest' -and $MonoScene) { $env:HS2N_APPEARANCE_FUZZ = 'off' }
            try { $r = Invoke-Unity $d $method $margs $false $report $m.TimeoutMin $false }
            finally { if ($null -eq $keepFuzz) { Remove-Item Env:HS2N_APPEARANCE_FUZZ -ErrorAction SilentlyContinue } else { $env:HS2N_APPEARANCE_FUZZ = $keepFuzz } }
            $alone += $d
            Add-Content -LiteralPath $progress -Encoding utf8 -Value ("end`t{0}`t{1}`t{2}`t{3}`t-`tdied with the batch process; run alone" -f $d, $r.Code,
                ($r.Minutes * 60).ToString('F1', [Globalization.CultureInfo]::InvariantCulture), $(if ($r.Report) { 'ALONE:' + $r.Report } else { '-' }))
        }
        if ($dead.Count -eq 0 -and $last -notlike "timeout`t*") { $Notes.Add("HeadlessBatch: process $attempt ended without done, recycle or a dead member (see HeadlessBatch.$attempt.console.txt)"); break }
    }
    # one result line per member, in the runner's order
    $ends = @{}
    foreach ($l in @(Get-Content -LiteralPath $progress -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        $f = $l -split "`t"
        if ($f[0] -eq 'end') { $ends[$f[1]] = $f } elseif ($f[0] -eq 'timeout') { $ends[$f[1]] = @('timeout', $f[1], '124', $f[2], '-', 'ok', 'own time limit passed') }
    }
    foreach ($m in $members) {
        if ($preFail.ContainsKey($m.Id)) {
            # its step before the batch failed: run the row alone now when it has a way to (its step runs again first), else FAIL
            $alone1 = $BatchRunRows[$m.Id].Alone
            if ($alone1) {
                $Notes.Add("$($m.Id): its step before the batch failed ($($preFail[$m.Id])); run alone after the batch")
                & $alone1
            } else { Add-Result $m.Id 'FAIL' 0 $preFail[$m.Id] $null }
            continue
        }
        $e = $ends[$m.Id]
        if (-not $e) { Add-Result $m.Id 'FAIL' 0 'not run: the batch ended before this member' $null; continue }
        $code = [int]$e[2]; $min = [double]::Parse($e[3], [Globalization.CultureInfo]::InvariantCulture) / 60
        $rep = if ($e[4] -like 'ALONE:*') { $e[4].Substring(6) } elseif ($e[4] -ne '-') { Join-Path $dir $e[4] } else { $null }
        if ($rep -and $e[4] -notlike 'ALONE:*') { $copy = Join-Path $OutDir $e[4]; Copy-Item -LiteralPath $rep -Destination $copy -Force; $rep = $copy }
        $sum = Read-Summary $rep
        $res = Judge @{ Code = $code } $sum
        $line = $sum.Line; if ($code -ne 0) { $line = "exit $code; $line" }
        if ($e[5] -like 'FAIL*') { $res = 'FAIL'; $line += "; hygiene $($e[5])" }
        if ($e.Count -ge 7 -and $e[6] -like '*run alone*') { $line += '; run alone (the batch process died in it)' }
        if ($m.Id -eq 'SceneModelTest' -and $MonoScene) { $line += '; appearance fuzz off (Mono.scene runs it at 2,000 x 200)' }
        Add-Result $m.Id $res $min $line $rep
    }
    $Notes.Add(("HeadlessBatch: {0} members in {1} process(es) ({2} recycles), wall {3:N1} min{4}" -f $members.Count, $procs, $recycles,
        ((Get-Date) - $t0).TotalMinutes, $(if ($alone.Count) { "; run alone after the process died: $($alone -join ', ')" } else { '' })))
}

# LooseTextureTest's census step (Phase 4.2 package L, GB10): Tools/survey/loose_png_census.py (a few seconds once cached)
# writes Cache/survey/loose_png/refs.tsv, the reference list the test checks the catalog against. Returns an error text or $null.
function Invoke-LooseCensus {
    $mc = @(Get-ChildItem $CacheDir -Filter 'modlists-*.tsv' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    if (-not $GameRoot -or $mc.Count -eq 0) { return 'no hs2Root in Config\settings.local.json or no modlists-*.tsv in the cache folder (a zipmod scan writes it)' }
    $out = & python -I (Join-Path $PSScriptRoot 'survey\loose_png_census.py') --hs2 $GameRoot --modcache $mc[0].FullName --out (Join-Path $Project 'Cache\survey\loose_png') 2>&1
    $code = $LASTEXITCODE
    $out | Out-File (Join-Path $OutDir 'LooseTextureTest.census.console.txt') -Encoding utf8
    if ($code -ne 0) { return "loose_png_census.py exit $code" }
    return $null
}

# ---- CardImportTest report parsing
function Read-Import([string]$file) {
    $o = [ordered]@{ cards = 0; ok = 0; failed = 0; nonCards = 0; crashes = 0; skipped = 0; males = 0; mipAsserts = 0; guard = 0; otherErrors = 0
        stepErrors = 0; nonFinite = 0; setupFail = 0; stages = 0; stepsRun = 0; stageExceptions = 0; nonFiniteValues = 0; motionLines = 0
        shaders = @{}; guardCards = [System.Collections.Generic.List[string]]::new(); failLines = @() }
    if (-not $file) { return $o }
    $lines = Get-Content -LiteralPath $file -Encoding UTF8
    $inShaders = $false; $card = $null; $inSummary = $false
    foreach ($l in $lines) {
        if ($l -eq 'summary') { $inSummary = $true; continue }
        if ($inSummary) {
            if ($l -match '^  cards\s+(\d+)$') { $o.cards = [int]$Matches[1] }
            elseif ($l -match '^  ok\s+(\d+)$') { $o.ok = [int]$Matches[1] }
            elseif ($l -match '^  male cards \(sex 0, male path\)\s+(\d+)') { $o.males = [int]$Matches[1] }
            elseif ($l -match '^  failed\s+(\d+)$') { $o.failed = [int]$Matches[1] }
            elseif ($l -match '^    (.+?)\s+(\d+)$') {
                $cls = $Matches[1].Trim(); $cnt = [int]$Matches[2]   # (later -match / -like calls overwrite $Matches)
                $o.failLines += "$cls x$cnt"
                if ($cls -like 'card load: *' -and $cls -notlike '*HS2/AIS chara payload*') { $o.nonCards += $cnt }
                if ($cls -like 'native crash (skipped)*') { $o.skipped += $cnt }
            }
            elseif ($l -match '^  cards with texture mip assertions\s+(\d+)') { $o.mipAsserts = [int]$Matches[1] }
            elseif ($l -match '^  cards with the texture guard engaged\s+(\d+)') { $o.guard = [int]$Matches[1] }
            elseif ($l -match '^  cards with other logged errors\s+(\d+)') { $o.otherErrors = [int]$Matches[1] }
            elseif ($l -match '^  cards with motion setup failures\s+(\d+)') { $o.setupFail = [int]$Matches[1] }
            elseif ($l -match '^  cards with motion step errors\s+(\d+)') { $o.stepErrors = [int]$Matches[1] }
            elseif ($l -match '^  cards with non-finite bones\s+(\d+)') { $o.nonFinite = [int]$Matches[1] }
            elseif ($l -match '^  motion stages attached \(total\)\s+(\d+)') { $o.stages = [int]$Matches[1] }
            elseif ($l -match '^  motion steps run \(total\)\s+(\d+)') { $o.stepsRun = [long]$Matches[1] }
            elseif ($l -match '^  motion stage exceptions \(total\)\s+(\d+)') { $o.stageExceptions = [long]$Matches[1] }
            elseif ($l -match '^  non-finite values \(total\)\s+(\d+)') { $o.nonFiniteValues = [long]$Matches[1] }
        }
        if ($l -like '   motion: *') { $o.motionLines++ }
        if ($l -like 'shaders without a fitted URP replacement*') { $inShaders = $true; $inSummary = $false; continue }
        if ($inShaders) {
            if ($l.Trim() -eq '') { $inShaders = $false; continue }
            if ($l -match '^  (.+?)\s+(\d+)\s+(\d+)$') { $o.shaders[$Matches[1]] = @([int]$Matches[2], [int]$Matches[3]) }
            continue
        }
        if ($l -match '^== (.+?)  (\d+ ms|-  FAILED)') { $card = $Matches[1] }
        if ($card -and $l -like '*MISSING content texture.badmip*' -and -not $o.guardCards.Contains($card)) { $o.guardCards.Add($card) }
    }
    return $o
}

function Merge-Import($a, $b) {
    foreach ($k in 'cards', 'ok', 'failed', 'nonCards', 'crashes', 'skipped', 'males', 'mipAsserts', 'guard', 'otherErrors', 'stepErrors', 'nonFinite', 'setupFail',
                   'stages', 'stepsRun', 'stageExceptions', 'nonFiniteValues', 'motionLines') { $a[$k] += $b[$k] }
    foreach ($kv in $b.shaders.GetEnumerator()) {
        if ($a.shaders.ContainsKey($kv.Key)) { $a.shaders[$kv.Key] = @(($a.shaders[$kv.Key][0] + $kv.Value[0]), ($a.shaders[$kv.Key][1] + $kv.Value[1])) }
        else { $a.shaders[$kv.Key] = $kv.Value }
    }
    foreach ($c in $b.guardCards) { if (-not $a.guardCards.Contains($c)) { $a.guardCards.Add($c) } }
    $a.failLines += $b.failLines
    return $a
}

function Judge-Import($o, [int]$code) {
    $real = $o.failed - $o.nonCards - $o.crashes - $o.skipped
    $vChara = @($o.shaders.Keys | Where-Object { $VanillaShaders[$_] -eq 'chara' })
    $vStudio = @($o.shaders.Keys | Where-Object { $VanillaShaders[$_] -eq 'studio' })
    $res = 'PASS'
    if ($code -ne 0 -or $real -gt 0 -or $o.crashes -gt 0 -or $o.stepErrors -gt 0 -or $o.nonFinite -gt 0 -or $o.setupFail -gt 0 -or $o.stageExceptions -gt 0 -or
        $o.nonFiniteValues -gt 0 -or $vChara.Count -gt 0 -or $o.cards -eq 0) { $res = 'FAIL' }
    elseif ($o.skipped -gt 0 -or $o.mipAsserts -gt 0 -or $vStudio.Count -gt 0) { $res = 'WARN' }
    $txt = "cards $($o.cards), ok $($o.ok), failed $($o.failed) (non-character files $($o.nonCards), native crashes $($o.crashes), skipped by list $($o.skipped)), " +
           "guard engaged $($o.guard), mip asserts $($o.mipAsserts), step errors $($o.stepErrors), non-finite $($o.nonFinite), " +
           "motion: $($o.motionLines) cards, stages $($o.stages), steps $($o.stepsRun), stage exceptions $($o.stageExceptions), non-finite values $($o.nonFiniteValues), " +
           "unfitted shaders $($o.shaders.Count) (vanilla chara $($vChara.Count), vanilla studio/map $($vStudio.Count), mod $($o.shaders.Count - $vChara.Count - $vStudio.Count))"
    return @{ Result = $res; Text = $txt }
}

# Ordered card list as CardImportTest sees it (recursive *.png, '/' separators, ordinal order).
function Get-CardList([string]$dir) {
    $l = [System.Collections.Generic.List[string]]::new()
    foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.png') {
        if ($f.Extension -ne '.png') { continue }
        $l.Add($f.FullName.Substring($dir.Length).TrimStart('\', '/').Replace('\', '/'))
    }
    $l.Sort([System.StringComparer]::Ordinal)
    return , $l
}

function Write-Lines([string]$path, $lines) { [IO.File]::WriteAllLines($path, [string[]]@($lines), [Text.UTF8Encoding]::new($false)) }

# CardImportTest over a card directory; graphics runs resume after a native crash. (The row's selection decides; the row
# AllCards.render calls it twice with its own result ids.) A render run compiles its shader variants before their first draw
# (-syncShaders, Editor/BatchShaderCompile.cs).
function Invoke-CardImport([string]$id, [string]$sexRel, [bool]$render, [string[]]$extra = @()) {
    if (-not $GameRoot) { Add-Result $id 'FAIL' 0 'no game root (Config\settings.local.json)' $null; return }
    $dir = Join-Path $GameRoot ("UserData\" + $sexRel.Replace('/', '\'))
    $all = Get-CardList $dir
    $list = $null
    if ($CardList) {
        $want = [System.Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content -LiteralPath $CardList -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ }), [System.StringComparer]::Ordinal)
        $all = [System.Collections.Generic.List[string]]@($all | Where-Object { $want.Contains($_) })
        $list = Join-Path $OutDir "$id.cards.txt"
        Write-Lines $list $all
    }
    $skipRel = @($Targets.skip | Where-Object { $_ -like "$sexRel/*" } | ForEach-Object { $_.Substring($sexRel.Length + 1) })
    $skipFile = Join-Path $OutDir "$id.skip.txt"
    Write-Lines $skipFile $skipRel
    $baseArgs = @('-cards', $dir, '-skip', $skipFile)
    $baseArgs += $(if ($render) { @('-render', '-syncShaders') } else { @('-step', '30') })
    $baseArgs += $extra
    $baseArgs += $ImportArgs
    $total = $null; $minutes = 0.0; $crashed = [System.Collections.Generic.List[string]]::new(); $lastCode = 0
    $part = 0
    while ($true) {
        $part++
        $a = $baseArgs
        if ($list) { $a += @('-list', $list) }
        $r = Invoke-Unity "$id.part$part" 'CardImportTest.Run' $a $render 'Spike\out\chara\import_report.txt' $(if ($render) { $RenderTimeoutMin } else { 90 })
        $minutes += $r.Minutes; $lastCode = $r.Code
        $o = Read-Import $r.Report
        $total = if ($total) { Merge-Import $total $o } else { $o }
        $cur = Join-Path $SpikeOut 'chara\import_current.txt'
        if ($r.Code -eq 0 -or -not $render -or -not (Test-Path $cur) -or $part -ge 25) { break }
        # native crash: count the card, skip it and resume with the cards after it
        $c = (Get-Content -LiteralPath $cur -Encoding UTF8 | Select-Object -First 1).Trim()
        Remove-Item -LiteralPath $cur -Force
        $crashed.Add("$sexRel/$c")
        $total.cards += 1; $total.failed += 1; $total.crashes += 1
        $total.failLines += "native crash: $c"
        Write-Lines $skipFile (@(Get-Content -LiteralPath $skipFile -Encoding UTF8) + $c)
        $idx = $all.IndexOf($c)
        $rest = if ($idx -ge 0) { $all.GetRange($idx + 1, $all.Count - $idx - 1) } else { @() }
        if ($rest.Count -eq 0) { $lastCode = 0; break }
        $list = Join-Path $OutDir "$id.remaining$part.txt"
        Write-Lines $list $rest
        $lastCode = 0
    }
    $j = Judge-Import $total $lastCode
    $txt = $j.Text
    if ($crashed.Count -gt 0) { $txt += "; crashed: " + ($crashed -join ', '); $Notes.Add("$id crashed on: " + ($crashed -join ', ')) }
    $Results.Add([pscustomobject]@{ Test = $id; Result = $j.Result; Minutes = $minutes; Numbers = $txt; Report = $null; Import = $total })
    $color = switch ($j.Result) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
    Write-Host ("[run_tests]          {0,-26} {1} ({2:N1} min) {3}" -f $id, $j.Result, $minutes, $txt) -ForegroundColor $color
}

# ------------------------------------------------------------------ player helpers (Phase 2.5)
# Built players run as separate processes, started directly (no Unity mutex is needed, but one at a time), with the
# project's settings and cache given absolutely (in a player relative paths resolve against the player folder, and an
# empty cache means a cold zipmod scan of minutes). Outputs under Spike\out\player\. Ports: 17397 (-bridgeAccept), 17399
# (player_checks.py), 17394 (plugin_player_check.py); 17398 is the Editor play test's, 17322 the interactive default, 17321
# HS2's bridge.
$PlayerOut = Join-Path $SpikeOut 'player'
$AppExe = Join-Path $Project 'Builds\App\HS2Next.exe'
$SmokeExe = Join-Path $Project 'Builds\Smoke\HS2Next.exe'
$PlayerCache = $CacheDir

function Quote-Arg([string]$a) { if ($a -match '[\s"]') { '"' + ($a -replace '"', '\"') + '"' } else { $a } }

# Waits until $gb of physical memory is free (a player may run beside another agent's Unity or player); $false after $maxMin.
function Wait-FreeRam([double]$gb = 8, [int]$maxMin = 30) {
    $t0 = Get-Date
    while ($true) {
        $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
        if ($free -ge $gb) { return $true }
        if (((Get-Date) - $t0).TotalMinutes -ge $maxMin) { return $false }
        Write-Host ("[run_tests] free RAM {0:N1} GB < {1} GB, waiting" -f $free, $gb)
        Start-Sleep -Seconds 20
    }
}

# Starts $exe with $argv (quoted where needed) and waits up to $timeoutMin; on a timeout stops that process and its children
# by PID (taskkill /T; never by name). stdout / stderr go to <test id>.console.txt. Returns @{ Code (124 = timeout); Minutes; Console }.
# $gameGate (Phase 5.3, the bare-exe rows): while the process runs, a process of the game gate (Tools\session_gate.json) stops it
# by PID (tree) and the result is Code 3 ("aborted by the game gate").
function Invoke-Process([string]$id, [string]$exe, [string[]]$argv, [int]$timeoutMin, [bool]$lock = $true, [bool]$gameGate = $false) {
    $safe = $id -replace '[^A-Za-z0-9_.-]', '_'
    $console = Join-Path $OutDir "$safe.console.txt"
    $errFile = Join-Path $OutDir "$safe.stderr.txt"
    Write-Host ("[run_tests] {0} {1,-26} {2} {3}" -f (Get-Date -Format 'HH:mm:ss'), $id, $exe, ($argv -join ' '))
    $t0 = Get-Date
    # a player run takes the GPU lock (-GpuLock) unless its script takes the lock itself ($lock false); exit 126 = held too long
    $locked = $false
    if ($lock -and $GpuLock) {
        if (-not (Enter-GpuLock $id $timeoutMin)) {
            "the GPU lock ($GpuLock$(if ($InputLock) { " / $InputLock" })) stayed held for $LockWaitMin min: the run did not start" | Out-File -FilePath $console -Encoding utf8
            return @{ Code = 126; Minutes = ((Get-Date) - $t0).TotalMinutes; Console = $console }
        }
        $locked = $true
    }
    try {
        $p = Start-Process -FilePath $exe -ArgumentList ((@($argv | ForEach-Object { Quote-Arg $_ })) -join ' ') -WorkingDirectory $Project -PassThru -NoNewWindow `
            -RedirectStandardOutput $console -RedirectStandardError $errFile
        $null = $p.Handle   # keeps the exit code readable after the exit
        $code = 124
        $exited = $false
        if ($gameGate) {
            $deadline = (Get-Date).AddMinutes($timeoutMin)
            while ((Get-Date) -lt $deadline) {
                if ($p.WaitForExit(10000)) { $exited = $true; break }
                $g = Get-GameRunning
                if ($g.Count) {
                    Write-Host "[run_tests] $id`: the game gate ($($g -join ', ')): stopping pid $($p.Id) and its children" -ForegroundColor Yellow
                    & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
                    $null = $p.WaitForExit(30000)
                    $code = 3
                    break
                }
            }
        } else { $exited = $p.WaitForExit([int]($timeoutMin * 60000)) }
        if ($exited) { $code = $p.ExitCode }
        elseif ($code -eq 3) { }
        else {
            Write-Host "[run_tests] $id`: no exit within $timeoutMin min, stopping pid $($p.Id) and its children" -ForegroundColor Red
            & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
            $null = $p.WaitForExit(30000)
        }
    } finally { if ($locked) { Exit-GpuLock } }
    if ((Test-Path $errFile) -and (Get-Item $errFile).Length -gt 0) { Add-Content -LiteralPath $console -Value (Get-Content -LiteralPath $errFile) }
    Remove-Item -LiteralPath $errFile -ErrorAction SilentlyContinue
    return @{ Code = $code; Minutes = ((Get-Date) - $t0).TotalMinutes; Console = $console }
}

# Copies a player's report next to the summary; returns the copy or $null.
function Copy-PlayerReport([string]$id, [string]$file) {
    if (-not (Test-Path $file)) { return $null }
    $copied = Join-Path $OutDir ("{0}__{1}" -f ($id -replace '[^A-Za-z0-9_.-]', '_'), (Split-Path -Leaf $file))
    Copy-Item -LiteralPath $file -Destination $copied -Force
    return $copied
}

# A player build through unity_batch (PlayerBuild.Build / BuildApp): FAIL on a non-zero exit or a FAIL report (failed build or
# packed-asset check); WARN when the build left tracked files of the project modified (the URP assets, the scenes, the shader set).
# $app (the app player, Phase 5.4): also FAIL when the shader programs after URP stripping exceed $ProgramCap
# or when the full-file binary scan of the build (Tools\dist\binary_scan.py --all-files: a path, the project folder or a private
# token in an assembly built here or in a data file, a vendor file over its baseline) does not pass.
$ProgramCap = 18537
# The limit of a player build: 1-2 min with the project's shader cache, far longer in a fresh clone, whose first build compiles
# every shader variant (Phase 5.4: the test checkout's first app build had 110 of the shaders after 17 min).
$PlayerBuildTimeoutMin = 120
function Invoke-PlayerBuild([string]$id, [string]$method, [string]$report, [bool]$app = $false) {
    $r = Invoke-Unity $id $method @() $true $report $PlayerBuildTimeoutMin
    $res = 'FAIL'; $txt = "exit $($r.Code); no report"
    if ($r.Report) {
        $lines = Get-Content -LiteralPath $r.Report -Encoding UTF8
        $ok = ($lines | Select-Object -First 1) -eq 'RESULT: PASS'
        $build = @($lines | Where-Object { $_ -match '^result ' } | Select-Object -First 1) -join ''
        $packed = @($lines | Where-Object { $_ -match '^packed-asset check: ' } | Select-Object -First 1) -join ''
        $fails = @($lines | Where-Object { $_ -like 'FAIL *' })
        $res = if ($r.Code -eq 0 -and $ok) { 'PASS' } else { 'FAIL' }
        $txt = "exit $($r.Code); $($lines[0]); $build; $packed" + $(if ($fails.Count) { '; ' + ($fails -join '; ') } else { '' })
        # Phase 5.3 (DP1, DP4, the program counts per API): the build.json line, the player settings line, the programs
        $dp = @($lines | Where-Object { $_ -match '^(build file |player: |shader programs: |shader programs for compiler platform )' } | ForEach-Object { $_.Trim() })
        if ($dp.Count) { $txt += '; ' + ($dp -join '; ') }
        if ($app -and $res -eq 'PASS') {
            $pl = @($lines | Where-Object { $_ -match '^shader programs: .*-> (\d+) after scriptable' } | Select-Object -First 1) -join ''
            if ($pl -match '-> (\d+) after scriptable') {
                $n = [int]$Matches[1]
                if ($n -gt $ProgramCap) { $res = 'FAIL'; $txt += "; FAIL programs $n over the cap $ProgramCap" } else { $txt += "; programs $n within the cap $ProgramCap" }
            } else { $res = 'FAIL'; $txt += '; FAIL no shader program count in the report' }
        }
    }
    if ($app -and $res -eq 'PASS') {
        $scanOut = Join-Path $OutDir 'PlayerBuild.app__binary_scan.txt'
        $scan = @(& python -I (Join-Path $PSScriptRoot 'dist\binary_scan.py') --tree (Join-Path $Project 'Builds\App') --all-files --out $scanOut 2>&1 | ForEach-Object { "$_" })
        $sc = $LASTEXITCODE
        $sl = @($scan | Where-Object { $_ -like 'SUMMARY: *' } | Select-Object -First 1) -join ''
        $txt += "; binary scan --all-files exit ${sc}: $(if ($sl) { $sl } else { 'no SUMMARY line' })"
        if ($sc -ne 0) { $res = 'FAIL' }
    }
    $dirty = @()
    if ($res -eq 'PASS' -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $dirty = @(& git -C $Project status --porcelain -- 'Assets/HS2Next/Settings' 'Assets/HS2Next/Settings/Measure/MeasureURP.asset' 'Assets/UniversalRenderPipelineGlobalSettings.asset' `
            'Assets/DefaultVolumeProfile.asset' 'Assets/HS2Next/Scenes' 'Assets/HS2Next/Resources/HS2NextShaders.shadervariants' 2>$null)
        if ($dirty.Count -gt 0) { $res = 'WARN'; $txt += "; the build left tracked files modified: " + (($dirty | ForEach-Object { $_.Trim() }) -join ', ') }
    }
    Add-Result $id $res $r.Minutes $txt $r.Report
    return $res
}

# ------------------------------------------------------------------ the row table
# Area tags (README_tests.md "Areas"). A new area is one more name here; a new row is one Row line in its group's place:
#   Row '<id>' <headless|graphics|player|full> '<area,area>' '<Class>.Run' @(<args>) '<report under the project>' [-Graphics] [-NoQuit] [-TimeoutMin N]
# (graphics: on for the graphics group, -Graphics elsewhere) or, for a row with its own logic, Row '<id>' <group> '<areas>' -Run { ... }.
# -DevInputs '<path>,<path>' (Phase 5.4): what the row reads that only the developer's machine has - the test targets
# (Tools/targets.json), recorded game measurements (Tools/compare_*.json, Cache/compare, Cache/shapemeasure), the S1 baselines
# (Cache/maker/baseline) and census texts of Tools/survey. The -Quick rows' declarations were measured: a -Quick of the exported
# tree without these files, then with only targets.json. A row that only skips a part without
# its input (an internal WARN line: ItemTest M2 / M5, ItemMotionTest N1, RouteTest R3 / R12, BridgeCoreTest C5 / C15 / C35)
# declares nothing and runs.
$AreaTags = [ordered]@{
    'infra'        = 'the test machinery: targets, runner, game paths'
    'content'      = 'catalog, zipmod and Studio lists, content ids, card extension data'
    'save'         = 'save format: containers, documents, JSON layer'
    'import'       = 'HS2 card / coordinate import and building from documents'
    'scene'        = 'scene model and scene runtime'
    'pose'         = 'pose files, FK writer, pose import'
    'motion'       = 'motion stages: animation, look, expression, dynamic bones, joint correction'
    'chara-build'  = 'character assembly (headless builds, shape maps)'
    'chara-render' = 'character rendering (graphics runs with characters)'
    'cards-full'   = 'the all-card rows (-Full only)'
    'items'        = 'Studio items: appearance, motion, item commands'
    'maps'         = 'Studio maps (Phase 3.2)'
    'effects'      = 'Studio lights, cameras, screen effects, sky, ambient, sound frame (Phase 3.3)'
    'shaders'      = 'fitted URP shaders and the shader mapping'
    'bridge'       = 'automation bridge, app core commands, acceptance'
    'player'       = 'built players'
    'compare'      = 'HS2 comparisons (recorded dumps, game measurements)'
    'ui'           = 'the Studio UI, its operations layer and the character state realization (Phase 3.5)'
    'capture'      = 'screenshots, the pixel cap, background / frame images (Phase 3.6)'
    'routes'       = 'routes, the scene clock and the global pause (Phase 3.6)'
    'maker'        = 'the Maker core: character snapshots, baselines, edit units, lists (Phase 4.0)'
    'publication'  = 'the publication guard: export manifest, leak check, the binary scan of the builds'
    'plugins'      = 'the plugin loader, the plugin Api, the sample sequencer and the test plugins (Phase 5.1)'
    'setup'        = 'first run, settings, data roots and migration, the distribution package and the bare exe (Phase 5.3)'
    'api'          = 'the frozen public contract: the Api assemblies, the command surface, the plugin manifest schema (Tools/api)'
}
$Rows = [System.Collections.Generic.List[object]]::new()
function Row {
    param([Parameter(Position = 0)][string]$Id, [Parameter(Position = 1)][string]$Group, [Parameter(Position = 2)][string]$Tags,
          [Parameter(Position = 3)][string]$Method, [Parameter(Position = 4)][string[]]$MethodArgs = @(), [Parameter(Position = 5)][string]$Report,
          [switch]$Graphics, [switch]$NoQuit, [int]$TimeoutMin = 90, [scriptblock]$Run,
          # opt-in: the row runs only when -Only names it exactly (no wildcard); no group, area or -Changed selects it
          [switch]$OptIn,
          # whole suite only: the row runs in a -Full without -Area / -Changed narrowing (or when -Only names it exactly)
          [switch]$WholeSuite,
          # result ids of this row when they differ from the row id (for -List durations), e.g. 'ItemTest.corpus.*'
          [string]$ResultIds,
          # group mono: the suite of Tools\mono_tests.ps1 the row runs (in the background job, not through Unity)
          [string]$MonoSuite,
          # development inputs (Phase 5.4, PQ13): paths relative to the project ('/' or '\', wildcards allowed) the row reads
          # and a public checkout lacks; one absent = the row is not started and reports WARN
          [string[]]$DevInputs = @())
    if ($Group -notin 'headless', 'graphics', 'player', 'full', 'mono') { throw "row ${Id}: unknown group '$Group'" }
    if (($Group -eq 'mono') -ne [bool]$MonoSuite) { throw "row ${Id}: -MonoSuite belongs to the mono group (and the mono group needs it)" }
    $t = @(Split-List $Tags)
    if ($t.Count -eq 0) { throw "row ${Id}: no area tag" }
    foreach ($x in $t) { if (-not $AreaTags.Contains($x)) { throw "row ${Id}: unknown area '$x' (known: $($AreaTags.Keys -join ', '))" } }
    if (-not $Run -and -not $Method -and -not $MonoSuite) { throw "row ${Id}: neither a method nor -Run" }
    if ($Rows | Where-Object { $_.Id -eq $Id }) { throw "row ${Id}: defined twice" }
    $Rows.Add([pscustomobject]@{ Id = $Id; Group = $Group; Tags = $t; Method = $Method; MethodArgs = $MethodArgs; Report = $Report
        Graphics = ($Graphics.IsPresent -or $Group -eq 'graphics'); NoQuit = $NoQuit.IsPresent; TimeoutMin = $TimeoutMin; Run = $Run; OptIn = $OptIn.IsPresent
        WholeSuite = $WholeSuite.IsPresent; ResultIds = $(if ($ResultIds) { $ResultIds } else { $Id }); MonoSuite = $MonoSuite
        DevInputs = @(Split-List $DevInputs) })
}

# The development inputs of a row that are absent in this checkout (none: the row runs).
function Get-AbsentDevInputs($row) {
    @(@($row.DevInputs) | Where-Object { $_ } | Where-Object {
        @(Get-Item -Path (Join-Path $Project ($_ -replace '/', '\')) -Force -ErrorAction SilentlyContinue).Count -eq 0 })
}

# The Maker rows' -Quick splits (Phase 5.2 package Q; a decision: on only when a measured -Quick exceeds 15 min): with
# $true the -Quick runs of MakerClothesTest leave out WT9 (the census of every vanilla clothes row), MakerHairTest HT10 (the
# hair facts census) and MakerUnitTest builds the female visual target only; every other mode runs them whole.
$MakerQuickSplits = $true
function Quick-Split([string[]]$a) { if ($Quick -and $MakerQuickSplits) { return , $a } else { return , @() } }

# ---- headless group
# Phase 4.6 (integrator T; the -Quick limit of 25 min): PoseFileTest.corpus and StudioListTest (corpus-like: every
# pose file / every list row of the install), CardImportTest (CardImport.* cover it), CharaDocBuildTest (CharaDocBuildTest.all
# covers it), MakerEditTest.fuzz (MakerEditTest.fuzzfull covers it) and MaterialEditorShaderTest (6.7 min: fresh card builds and
# their shader compiles) stay in their places but in the full group, so only -Full runs them (a -Quick took 37.1 min
# with them, 67 rows; without them about 24 min expected)
# LeakCheck (Phase 4.6 package P): no Unity - python -I Tools\leak_check.py over the tracked files that
# Tools\export_manifest.json exports (hard: absolute and user-profile paths, e-mail addresses, the private tokens of the
# git-ignored Config\leak_tokens.local.txt, unclassified paths; soft classes counted); PASS = exit 0. First, so a compile
# failure of a Unity row never skips it.
Row 'LeakCheck'           headless 'publication,plugins' -Run {
    $t0 = Get-Date
    $rep = Join-Path $Project 'Spike\out\leak\leak_report.txt'
    $out = @(& python -I (Join-Path $PSScriptRoot 'leak_check.py') --out $rep 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $out | Out-File (Join-Path $OutDir 'LeakCheck.console.txt') -Encoding utf8
    $line = @($out | Where-Object { $_ -like 'SUMMARY: *' } | Select-Object -First 1) -join ''
    if (-not $line) { $line = 'no SUMMARY line' }
    if ($code -ne 0) { $line = "exit $code; $line" }
    $copied = $null
    if ((Test-Path -LiteralPath $rep) -and (Get-Item -LiteralPath $rep).LastWriteTime -ge $t0) {
        $copied = Join-Path $OutDir 'LeakCheck__leak_report.txt'; Copy-Item -LiteralPath $rep -Destination $copied -Force
    }
    Add-Result 'LeakCheck' $(if ($code -eq 0) { 'PASS' } else { 'FAIL' }) ((Get-Date) - $t0).TotalMinutes $line $copied
}
Row 'TestTargets'         headless 'infra'                       'TestTargets.Run' @() 'Spike\out\tests\targets_report.txt' -DevInputs 'Tools/targets.json'
Row 'ContentIdTest'       headless 'content'                     'ContentIdTest.Run' @() 'Spike\out\content\contentid.txt'
Row 'SaveFormatTest'      headless 'save'                        'SaveFormatTest.Run' @() 'Spike\out\save\saveformat_report.txt'
Row 'ImportTest'          headless 'import,save'                 'ImportTest.Run' @() 'Spike\out\import\import_report.txt' -DevInputs 'Tools/targets.json'
Row 'SceneModelTest'      headless 'scene,save'                  'SceneModelTest.Run' @() 'Spike\out\scene\scene_model_report.txt'
Row 'PoseFileTest'        headless 'pose'                        'PoseFileTest.Run' @() 'Spike\out\pose\posefile_report.txt'
Row 'PoseFileTest.corpus' full     'pose'                        'PoseFileTest.RunCorpus' @() 'Spike\out\pose\pose_corpus_report.txt'
Row 'ZipmodListTest'      headless 'content'                     'ZipmodListTest.Run' @() 'Spike\out\content\zipmod_lists.txt'
Row 'StudioListTest'      full     'content,items,maps'          'StudioListTest.Run' @() 'Spike\out\content\studio_lists.txt'
Row 'CardExtTest'         headless 'content,import'              'CardExtTest.Run' @() 'Spike\out\content\card_ext.txt'
Row 'CardResolveTest'     headless 'content,chara-build'         'CardResolveTest.Run' @('-limit', '30') 'Spike\out\chara\resolve.txt'
Row 'CardImportTest'      full     'chara-build,motion,shaders,content' -Run {
    $r = Invoke-Unity 'CardImportTest' 'CardImportTest.Run' @('-limit', '60', '-step', '30') $false 'Spike\out\chara\import_report.txt'
    $o = Read-Import $r.Report
    $j = Judge-Import $o $r.Code
    $Results.Add([pscustomobject]@{ Test = 'CardImportTest'; Result = $j.Result; Minutes = $r.Minutes; Numbers = $j.Text; Report = $r.Report; Import = $o })
    Write-Host ("[run_tests]          {0,-26} {1} ({2:N1} min) {3}" -f 'CardImportTest', $j.Result, $r.Minutes, $j.Text)
}
Row 'JointCorrectionTest' headless 'motion'                      'JointCorrectionTest.Run' @() 'Spike\out\motion\correct_report.txt'
Row 'LookTest'            headless 'motion'                      'LookTest.Run' @() 'Spike\out\motion\look_report.txt'
Row 'ExpressionTest'      headless 'motion'                      'ExpressionTest.Run' @() 'Spike\out\motion\face_report.txt'
Row 'DynamicBoneTest'     headless 'motion'                      'DynamicBoneTest.Run' @() 'Spike\out\motion\dynbone_report.txt'
Row 'AnimationTest'       headless 'motion'                      'AnimationTest.Run' @() 'Spike\out\motion\anim_report.txt'
Row 'SceneRuntimeTest'    headless 'scene,items,chara-build,save' 'SceneRuntimeTest.Run' @() 'Spike\out\scene\scene_runtime_report.txt' -DevInputs 'Tools/targets.json'
Row 'ItemMotionTest'      headless 'items,motion'                'HS2Next.Scenes.Runtime.Tests.ItemMotionTest.Run' @() 'Spike\out\items\item_motion_report.txt' -DevInputs 'Tools/targets.json'
Row 'ItemMotionTest.play' headless 'items,motion'                'HS2Next.Scenes.Runtime.Tests.ItemMotionTest.RunPlay' @() 'Spike\out\items\item_motion_play_report.txt' -NoQuit -TimeoutMin 20 -DevInputs 'Tools/targets.json'
Row 'ItemTest'            headless 'items,shaders'               'ItemTest.Run' @() 'Spike\out\items\item_report.txt'
Row 'MapTest'             headless 'maps,shaders'                'MapTest.Run' @() 'Spike\out\maps\map_report.txt'
Row 'MapTest.play'        headless 'maps,scene,bridge'           'MapTest.RunPlay' @() 'Spike\out\maps\map_play_report.txt' -NoQuit -TimeoutMin 60
Row 'LightCameraTest'     headless 'effects,scene'               'LightCameraTest.Run' @() 'Spike\out\effects\light_camera_report.txt'
Row 'CharaDocBuildTest'   full     'import,chara-build'          'CharaDocBuildTest.Run' @() 'Spike\out\import\docbuild_report.txt'
Row 'FkWriterTest'        headless 'pose,motion'                 'FkWriterTest.Run' @() 'Spike\out\pose\fk_report.txt' -DevInputs 'Tools/targets.json'
Row 'IkSolverTest'        headless 'motion,pose'                 'IkSolverTest.Run' @() 'Spike\out\pose\ik_solver_report.txt' -DevInputs 'Tools/targets.json'
# PoseImportTest: I3 (the plan of every pose file) runs in the full row PoseImportTest.corpus (Phase 5.2, LQ16)
Row 'PoseImportTest'      headless 'pose,import,scene'           'PoseImportTest.Run' @('-skip', 'I3') 'Spike\out\pose\pose_import_report.txt' -DevInputs 'Tools/targets.json,Tools/compare_poses.json'
Row 'PoseImportTest.corpus' full   'pose,import,scene'           'PoseImportTest.Run' @('-only', 'I3') 'Spike\out\pose\pose_import_report.txt' -TimeoutMin 30
Row 'IkDriverTest'        headless 'motion,pose,scene'           'IkDriverTest.Run' @() 'Spike\out\pose\ik_driver_report.txt' -DevInputs 'Tools/targets.json,Tools/compare_poses.json'
Row 'UiOpsTest'           headless 'ui,scene,bridge'             'UiOpsTest.Run' @() 'Spike\out\ui_ops\ui_ops_report.txt' -DevInputs 'Tools/targets.json'
Row 'PauseTest'           headless 'routes,motion,scene,bridge'  'PauseTest.Run' @() 'Spike\out\pause\pause_report.txt' -DevInputs 'Tools/targets.json'
Row 'RouteTest'           headless 'routes,scene,bridge'         'RouteTest.Run' @() 'Spike\out\routes\route_report.txt'
Row 'CharaStateTest'      headless 'ui,chara-build,scene'        'CharaStateTest.Run' @() 'Spike\out\chara_state\chara_state_report.txt' -DevInputs 'Tools/targets.json'
Row 'UiCoreTest'          headless 'ui'                          'UiCoreTest.Run' @() 'Spike\out\ui\ui_core_report.txt'
Row 'UiShellTest.play'    headless 'ui,bridge'                   'UiShellTest.Run' @() 'Spike\out\ui\ui_shell_report.txt' -NoQuit -TimeoutMin 20 -DevInputs 'Tools/targets.json'
Row 'CompareTest'         headless 'compare,motion,pose,chara-build' 'CompareTest.Run' @() 'Spike\out\compare\compare_report.txt' -DevInputs 'Cache/compare'
foreach ($set in 'female_body', 'female_face', 'male_body', 'male_face') {
    Row "ShapeMapTest.$set" headless 'chara-build,compare'       'ShapeMapTest.Run' @('-set', $set, '-label', "suite_$set") "Spike\out\shape\runs\suite_$set.txt" -DevInputs 'Cache/shapemeasure'
}
Row 'ShapeMapTest.oor'    headless 'chara-build,compare'       'ShapeMapTest.Run' @('-set', 'female_body_oor+female_face_oor+male_body_oor+male_face_oor', '-label', 'suite_oor') 'Spike\out\shape\runs\suite_oor.txt' -DevInputs 'Cache/shapemeasure'
Row 'BridgeTest'          headless 'bridge'                      'HS2Next.Bridge.Tests.BridgeTest.Run' @() 'Spike\out\bridge\bridge_report.txt'
Row 'BridgeTest.play'     headless 'bridge'                      'HS2Next.Bridge.Tests.BridgePlayCycleTest.Run' @() 'Spike\out\bridge\bridge_play_cycle_report.txt' -NoQuit -TimeoutMin 20
Row 'MakerCoreTest'       headless 'maker,chara-build'           'MakerCoreTest.RunS1' @() 'Spike\out\maker\maker_s1_report.txt' -DevInputs 'Tools/targets.json,Cache/maker/baseline'
Row 'MakerUnitTest'       headless 'maker,chara-build'           'MakerUnitTest.RunU' (Quick-Split @('-visual', 'female')) 'Spike\out\maker\maker_u_report.txt' -DevInputs 'Tools/targets.json'
Row 'MakerListTest'       headless 'maker,content'               'MakerListTest.Run' @() 'Spike\out\maker\maker_lists_report.txt'
Row 'MakerEditTest'       headless 'maker,scene,chara-build'     'MakerEditTest.RunS2' @() 'Spike\out\maker\maker_s2_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
Row 'MakerEditTest.fuzz'  full     'maker,scene'                 'MakerEditTest.RunFuzz' @() 'Spike\out\maker\maker_fuzz_report.txt' -TimeoutMin 60
Row 'MakerFaceTest'       headless 'maker,chara-build'           'MakerFaceTest.Run' @() 'Spike\out\maker\maker_face_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
Row 'MakerBodyTest'       headless 'maker,chara-build'           'MakerBodyTest.Run' @() 'Spike\out\maker\maker_body_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
Row 'MakerHairTest'       headless 'maker,chara-build'           'MakerHairTest.Run' (Quick-Split @('-skip', 'HT10')) 'Spike\out\maker\maker_hair_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
# MakerHairTest.fuzzvanilla (Phase 4.3 package A; opt-in): the realization fuzz with HairUnit.MeshOnEveryHair off (vanilla's
# prefab useMesh; the named switch keeps edit = fresh build too); report maker_fuzz_report_vanillamesh.txt
Row 'MakerHairTest.fuzzvanilla' headless 'maker,scene'          'MakerHairTest.RunFuzzVanillaMesh' @() 'Spike\out\maker\maker_fuzz_report_vanillamesh.txt' -OptIn -TimeoutMin 60
# Phase 4.4: the zipmod set and Sideloader's per-asset redirect (package R; the sweeps AR11 / AR12
# and the costs RU3 are the -Full row AssetRedirectTest.sweep) and the clothes units (package A, UM and WT1-WT12)
Row 'AssetRedirectTest'   headless 'content,chara-build,maker'   'AssetRedirectTest.Run' @('-only', 'AR1+AR2+AR3+AR4+AR5+AR6+AR7+AR8+AR9+AR10+RU1') 'Spike\out\maker\asset_redirect_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json,Tools/survey/redirect_step0_census.txt'
Row 'MakerClothesTest'    headless 'maker,chara-build'           'MakerClothesTest.Run' (Quick-Split @('-skip', 'WT9')) 'Spike\out\maker\maker_clothes_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
# Phase 4.5: the accessory units (package A: UM, XT1-XT6, XT8-XT12; XT7 is CharaStateTest S11)
Row 'MakerAccessoriesTest' headless 'maker,chara-build'          'MakerAccessoriesTest.Run' @() 'Spike\out\maker\maker_accessories_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
# Phase 4.6: the save options, the coordinate save, the load flags and the card path (package A: VT1-VT3, VT6-VT8, VT10,
# VT11) and the schema-driven acceptance row (package E: every Edit-tab row through the bridge = one entry, undo = the documents)
Row 'MakerFileTest'        headless 'maker,save,scene'           'MakerFileTest.Run' @() 'Spike\out\maker\maker_file_report.txt' -TimeoutMin 30 -DevInputs 'Tools/targets.json'
Row 'MakerSchemaTest'      headless 'maker,bridge,ui'            'MakerSchemaTest.Run' @() 'Spike\out\maker\maker_schema_report.txt' -TimeoutMin 20 -DevInputs 'Tools/targets.json'
# Phase 5.M: the release policy of the official build (package P: PT1-PT5 = MX7; every
# process this suite starts runs with HS2NEXT_PRIVATE_OFF=1), the H animation rows listed and played on the clothed SFW
# targets (package N: HA1-HA6, numbers only) and the mosaic set, methods (a) / (b) headless (package R: MX1-MX6, MX8)
# PolicyTest in the full group (5.M integrator T, a decision): a -Quick took 27.5 min (target 25); LeakCheck keeps
# the policy classes in -Quick, PT1-PT5 change only with the build hooks, the documents or the export manifest
Row 'PolicyTest'           full     'publication,maker,chara-build,content,plugins' 'PolicyTest.Run' @() 'Spike\out\policy\policy_report.txt' -TimeoutMin 20
Row 'HAnimeTest'           headless 'motion,ui,scene,pose'       'HAnimeTest.Run' @() 'Spike\out\motion\hanime_report.txt' -TimeoutMin 20 -DevInputs 'Tools/targets.json'
Row 'MosaicTest'           headless 'maker,chara-build,shaders'  'MosaicTest.Run' @() 'Spike\out\maker\mosaic_report.txt' -TimeoutMin 20 -DevInputs 'Tools/targets.json'
# Phase 5.2 package G: group 647158, the accessories as Studio items (GA1-GA5)
Row 'AccessoryItemsTest'   headless 'items,content,ui'           'AccessoryItemsTest.Run' @() 'Spike\out\items\accessory_items_report.txt' -TimeoutMin 15
# HAnimeTest.corpus (5.M package N; -Full only): every listed H row's controllers, override, states, sway lines and option
# items loaded without a character, bundles released every 200 rows; data failures of the installed lists are known
# classes with a maximum count
Row 'HAnimeTest.corpus'    full     'motion'                     'HAnimeTest.RunCorpus' @() 'Spike\out\motion\hanime_corpus_report.txt' -TimeoutMin 30
# v0.3 trial 2 (package G, the head-type list crash): the thumbnail header check before the upload, headless - the defect
# classes on synthetic headers, every list row's thumbnail bundle and header in the original and the repaired copies;
# the comparison with the census (and the LZMA decoder against Python's lzma) is the full row ThumbGuardTest.census
# (Phase 5.2, LQ16: the census runs once per suite before it, Invoke-ThumbCensus). The headers row itself is in the full group
# since the 5.2 integration (a decision: a -Quick took 16.1 min; the path it guarded is the catalog's texture
# check now, which -Quick covers through TC1 in Mono.content)
Row 'ThumbGuardTest'       full     'maker,ui,content'           'ThumbGuardTest.RunHeaders' @() 'Spike\out\maker\thumb_guard_headers_report.txt' -TimeoutMin 15
Row 'ThumbGuardTest.census' full   'maker,ui,content' -Run {
    Invoke-ThumbGuardRow 'ThumbGuardTest.census' 'ThumbGuardTest.RunHeaders' @('-census', (Join-Path $ThumbGuardAll 'defects.tsv'), '-lzmaRef', (Join-Path $ThumbGuardAll 'lzma_ref.tsv')) $false 'Spike\out\maker\thumb_guard_headers_report.txt' $false 15
}
# Phase 4.4 package M: MaterialEditor's shader names resolve through its registry (the plugin's names and the zipmods'
# declarations), never through the shaders loaded in the process (MS1-MS3)
Row 'MaterialEditorShaderTest' full 'chara-build,content'    'MaterialEditorShaderTest.Run' @() 'Spike\out\maker\material_editor_shader_report.txt' -TimeoutMin 20
# LooseTextureTest (Phase 4.2 package L, GB10): the loose-PNG census (Tools/survey/loose_png_census.py, a few seconds once
# cached) writes Cache/survey/loose_png/refs.tsv, then the catalog is checked against it (C1-C8). Phase 5.2 (LQ16): the
# headless row loads every 10th census reference in C2 (-c2Stride 10; a HeadlessBatch member with the census step before the
# batch); the full row LooseTextureTest.all loads them all.
function Invoke-LooseRow([string]$id, [string[]]$a) {
    $t0 = Get-Date
    $err = Invoke-LooseCensus
    if ($err) { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes $err $null; return }
    $r = Invoke-Unity $id 'LooseTextureTest.Run' $a $false 'Spike\out\maker\loose_texture_report.txt'
    $sum = Read-Summary $r.Report
    $line = $sum.Line; if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
    Add-Result $id (Judge $r $sum) ((Get-Date) - $t0).TotalMinutes $line $r.Report
}
Row 'LooseTextureTest'    headless 'content,maker' -Run { Invoke-LooseRow 'LooseTextureTest' @('-c2Stride', '10') }
Row 'LooseTextureTest.all' full    'content,maker' -Run { Invoke-LooseRow 'LooseTextureTest.all' @() }
# BridgeCoreTest: since Phase 5.1 also group C37 (the plugin loader in a test host, the command-layer checks CK2-CK5 and the
# engine-side points PX1-PX6); -c37Stage makes the check run Tools\plugins.ps1 -Build all -Stage Spike\out\plugins\core
# inside the Unity process, after Unity compiled the Api (the plugin build refuses an Api DLL older than its sources)
Row 'BridgeCoreTest'      headless 'bridge,scene,items,maps,chara-build,motion,pose,effects,ui,plugins' 'BridgeCoreTest.Run' @('-c37Stage') 'Spike\out\bridge\bridge_core_report.txt' -DevInputs 'Tools/targets.json'
# CommandsDoc (Phase 5.1 package C; -Full only): BridgeHelpDump.Run writes the help dump of every command
# (Spike\out\bridge\help_dump.json, about 2 min with the Unity start), then python -I Tools\gen_commands_md.py --check compares
# Reference/COMMANDS.md with what the dump gives (exit 1: the reference is stale - regenerate it without --check and commit it)
Row 'CommandsDoc'         full     'bridge'                      -DevInputs 'Tools/targets.json' -Run {
    $id = 'CommandsDoc'
    $t0 = Get-Date
    $r = Invoke-Unity $id 'BridgeHelpDump.Run' @() $false 'Spike\out\bridge\help_dump_report.txt' 20
    $first = if ($r.Report) { @(Get-Content -LiteralPath $r.Report -Encoding UTF8 | Select-Object -First 1) -join '' } else { 'no report written' }
    if ($r.Code -ne 0 -or $first -notlike 'SUMMARY: PASS*') { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes "help dump: exit $($r.Code); $first" $r.Report; return }
    $out = @(& python -I (Join-Path $PSScriptRoot 'gen_commands_md.py') --check 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $out | Out-File (Join-Path $OutDir 'CommandsDoc.check.console.txt') -Encoding utf8
    $msg = @($out | Where-Object { $_.Trim() } | Select-Object -Last 1) -join ''
    $res = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
    Add-Result $id $res ((Get-Date) - $t0).TotalMinutes ("$($first -replace '^SUMMARY: ', 'help dump ')" + "; gen_commands_md.py --check exit $code" + $(if ($msg) { ": $msg" } else { '' })) $r.Report
}
# ApiSurfaceTest (Phase 5.4 package A): the public contract against its listing Tools/api/*.surface.* - the public surface of
# HS2Next.Api and HS2Next.Api.Unity, the bridge protocol and every command not flagged experimental (help texts left out),
# the plugin manifest schema measured on the parser; MAJOR / MINOR / INFO classes against the Api version (AZ1-AZ5 of the
# freeze). Builds its command registry without an app: no game install, no catalog, no development input. About 5 s inside the
# HeadlessBatch.
Row 'ApiSurfaceTest'      headless 'api,bridge,plugins'          'ApiSurfaceTest.Run' @() 'Spike\out\api\api_surface_report.txt' -TimeoutMin 10

# ---- mono group (-Quick): the plain-Mono suites of Tools\mono_tests.ps1 in two background jobs beside the Unity rows
# (Mono.scene alone, the others one after the other); Mono.scene carries the appearance fuzz at 2,000 x 200 while the
# Unity row SceneModelTest runs it at the smoke size
Row 'Mono.scene'          mono 'scene,save,maker'                -MonoSuite scene
Row 'Mono.ui'             mono 'ui,setup'                        -MonoSuite ui
Row 'Mono.maker'          mono 'maker,content'                   -MonoSuite maker
Row 'Mono.save'           mono 'save'                            -MonoSuite save
Row 'Mono.pose'           mono 'pose'                            -MonoSuite pose
Row 'Mono.bridge'         mono 'bridge,setup'                    -MonoSuite bridge
Row 'Mono.content'        mono 'content,setup'                   -MonoSuite content
Row 'Mono.plugins'        mono 'plugins'                         -MonoSuite plugins

# ---- the all-card row of -Full (group full) runs before the graphics group (its walk also fills the texture check's verdict
# cache; table order = run order).
# AllCards (Phase 5.2 package Q): every file of the card folders (984: 957 character cards, 27
# other files counted) and then the coordinates, one shared build per card - ImportTest's per-file import (A2 / A3 / A6),
# CharaDocBuildTest's P1 against the S1 baseline, S1 + G1 (MakerCoreTest.all), CardImport's statistics on the same build (+ 30
# motion steps), the S2 pair (MakerEditTest.all), the loose-PNG references (LooseTextureTest.cards) and every coordinate worn on
# its visual target (CoordApply.all). It replaced those rows and CardImport.female / .male (+ .doc) in -Full (AX10: 85 of 86
# numbers equal on one commit); they stay as opt-in rows below. The walk runs in parts, each its own Unity process (-out
# <part folder>): the cards 200 at a time (-from / -limit; a part's S2 chain starts from the previous card of the sex and each
# sex's cyclic pair runs after its last card, so the pairs add up to 957), then the coordinates' import, female and male parts
# (-coordsOnly -coordPart) - in one process the per-card cost grows with the Unity native state (an impact run:
# 62.3 min in one process against about 40 in parts). An impact -Full (-Area / -Changed) wears every third coordinate
# (-coordEvery 3, AX4), the whole suite all of them. The key numbers are summed over the parts (S1, G1, S2, import, P1,
# coordinates, the minutes per step); the CardImport-format statistics of both card folders are merged and judged as the
# CardImport rows were (a vanilla character shader without a fitted replacement FAILs, a vanilla Studio / map shader or a mip
# assertion WARNs) and feed the summary's shader and patched-texture tables. A native crash (AX6): Spike\out\allcards\
# allcards_current.txt names the file in progress - the part resumes at the next file and the crashed file runs alone (-only);
# the row FAILs naming it.
function Read-AllCardsPart([string]$rep) {
    $o = @{ Summary = $null; Pass = 0; Fail = 0; S1 = @(0, 0, 0, 0, 0, 0); G1 = @(0, 0, 0, 0); S2 = @(0, 0, 0, 0, 0); Import = @(0, 0); P1 = @(0, 0, 0, 0, 0, 0)
        Coords = @(0, 0); Steps = [ordered]@{}; FailLines = @() }
    if (-not $rep -or -not (Test-Path -LiteralPath $rep)) { return $o }
    foreach ($l in Get-Content -LiteralPath $rep -Encoding UTF8) {
        if ($l -match '^SUMMARY: (\d+) PASS, (\d+) FAIL') { $o.Summary = $l.Trim(); $o.Pass = [int]$Matches[1]; $o.Fail = [int]$Matches[2] }
        if ($l -like 'FAIL *') { $o.FailLines += $l.Trim() }
        if ($l -match 'S1: snapshot = baseline.*?(\d+) equal, (\d+) differ, (\d+) without a baseline, (\d+) failed, (\d+) not character cards, of (\d+) files') {
            $o.S1 = @(1..6 | ForEach-Object { [int]$Matches[$_] }) }
        if ($l -match 'G1: .*?cards failing (\d+) of (\d+); slots (\d+), .*dead \(owned, not drawn\) (\d+)') { $o.G1 = @(1..4 | ForEach-Object { [int]$Matches[$_] }) }
        if ($l -match 'S2: morph.*?(\d+) equal, (\d+) differ, (\d+) failed of (\d+) pairs; rebuilt by rule (\d+)') { $o.S2 = @(1..5 | ForEach-Object { [int]$Matches[$_] }) }
        if ($l -match 'import: every file .*?(\d+) passed, (\d+) failed') { $o.Import = @([int]$Matches[1], [int]$Matches[2]) }
        if ($l -match 'P1: .*?(\d+) passed, (\d+) failed; cards (\d+), compared (\d+).*?rows (\d+), rows that differ (\d+)') { $o.P1 = @(1..6 | ForEach-Object { [int]$Matches[$_] }) }
        if ($l -match 'coordinates: every coordinate .*?(\d+) passed, (\d+) failed') { $o.Coords = @([int]$Matches[1], [int]$Matches[2]) }
        if ($l -match '^minutes per step: (.*); wall') {
            foreach ($kv in $Matches[1] -split ', ') { if ($kv -match '^(.+) ([\d.]+)$') { $o.Steps[$Matches[1]] = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture) } }
        }
    }
    return $o
}
Row 'AllCards'              full 'cards-full,maker,import,chara-build' -TimeoutMin 150 -Run {
    $id = 'AllCards'
    $t0 = Get-Date
    $coord = if ($WholeSuite) { @() } else { @('-coordEvery', '3') }
    $cur = Join-Path $SpikeOut 'allcards\allcards_current.txt'
    Remove-Item -LiteralPath $cur -Force -ErrorAction SilentlyContinue
    $files = 0
    if ($GameRoot) { foreach ($sx in $Targets.cards.female, $Targets.cards.male) { $files += (Get-CardList (Join-Path $GameRoot ("UserData\" + ([string]$sx).Replace('/', '\')))).Count } }
    if ($files -eq 0) { Add-Result $id 'FAIL' 0 'no card files (Config\settings.local.json hs2Root, Tools\targets.json cards)' $null; return }
    $plan = @()
    for ($f = 0; $f -lt $files; $f += 200) { $plan += , @("cards_$f", $f, [Math]::Min(200, $files - $f), @('-from', "$f", '-limit', "$([Math]::Min(200, $files - $f))", '-noCoords')) }
    foreach ($cp in 'import', 'female', 'male') { $plan += , @("coords_$cp", -1, 0, (@('-coordsOnly', '-coordPart', $cp) + $coord)) }
    $parts = @(); $min = 0.0; $res = 'PASS'; $crashes = @(); $imp = @{}
    foreach ($p in $plan) {
        $tag = $p[0]; $dir = Join-Path $OutDir "AllCards.$tag"
        $r = Invoke-Unity "$id.$tag" 'AllCardsTest.Run' (@($p[3]) + @('-out', $dir)) $false $null 90
        $min += $r.Minutes
        $o = Read-AllCardsPart (Join-Path $dir 'allcards_report.txt')
        if ($r.Code -ne 0 -or -not $o.Summary -or $o.Fail -gt 0) { $res = 'FAIL' }
        $parts += , @($tag, $r.Code, $o, $r.Minutes)
        # AX6: a native crash inside a card part resumes after the crashed file, which then runs alone
        $end = $p[1] + $p[2]
        for ($k = 1; $r.Code -ne 0 -and $p[1] -ge 0 -and (Test-Path -LiteralPath $cur) -and $k -le 5; $k++) {
            $c = @((Get-Content -LiteralPath $cur -Encoding UTF8 | Select-Object -First 1) -split "`t")
            Remove-Item -LiteralPath $cur -Force
            $idx = [int]$c[0]; $rel = if ($c.Count -ge 2) { $c[1] } else { '?' }
            $crashes += "file $idx ($rel)"
            $Notes.Add("AllCards crashed natively at file $idx ($rel) in part $tag; resumed at file $($idx + 1)")
            $ra = Invoke-Unity "$id.$tag.alone$k" 'AllCardsTest.Run' @('-only', $rel, '-out', (Join-Path $OutDir "AllCards.$tag.alone$k")) $false $null 30
            $min += $ra.Minutes
            if ($idx + 1 -ge $end) { break }
            $rdir = Join-Path $OutDir "AllCards.$tag.resume$k"
            $r = Invoke-Unity "$id.$tag.resume$k" 'AllCardsTest.Run' @('-from', "$($idx + 1)", '-limit', "$($end - $idx - 1)", '-noCoords', '-out', $rdir) $false $null 90
            $min += $r.Minutes
            $parts += , @("$tag.resume$k", $r.Code, (Read-AllCardsPart (Join-Path $rdir 'allcards_report.txt')), $r.Minutes)
        }
        foreach ($sx in 'female', 'male') {
            $src = Join-Path $dir "allcards_cardimport_$sx.txt"
            if (-not (Test-Path -LiteralPath $src)) { continue }
            $o2 = Read-Import $src
            if ($o2.cards -eq 0) { continue }
            $imp[$sx] = if ($imp[$sx]) { Merge-Import $imp[$sx] $o2 } else { $o2 }
        }
    }
    # the sums over the parts
    $s1 = @(0, 0, 0, 0, 0, 0); $g1 = @(0, 0, 0, 0); $s2 = @(0, 0, 0, 0, 0); $ip = @(0, 0); $p1 = @(0, 0, 0, 0, 0, 0); $co = @(0, 0); $steps = [ordered]@{}
    foreach ($q in $parts) {
        $o = $q[2]
        for ($i = 0; $i -lt 6; $i++) { $s1[$i] += $o.S1[$i]; $p1[$i] += $o.P1[$i] }
        for ($i = 0; $i -lt 4; $i++) { $g1[$i] += $o.G1[$i] }
        for ($i = 0; $i -lt 5; $i++) { $s2[$i] += $o.S2[$i] }
        $ip[0] += $o.Import[0]; $ip[1] += $o.Import[1]; $co[0] += $o.Coords[0]; $co[1] += $o.Coords[1]
        foreach ($kv in $o.Steps.GetEnumerator()) { $steps[$kv.Key] = [double]$steps[$kv.Key] + $kv.Value }
    }
    $pf = @($parts | Where-Object { $_[1] -ne 0 -or $_[2].Fail -gt 0 -or -not $_[2].Summary } | ForEach-Object { "$($_[0]): exit $($_[1]); $(if ($_[2].Summary) { $_[2].Summary } else { 'no report' })" + $(if ($_[2].FailLines.Count) { ' - ' + ($_[2].FailLines -join ' / ') } else { '' }) })
    $line = "$($parts.Count) parts ($files card files; $(if ($WholeSuite) { 'every coordinate' } else { 'coordinates every third (impact run)' })); " +
        "S1 $($s1[0]) equal, $($s1[1]) differ, $($s1[2]) without a baseline, $($s1[3]) failed, $($s1[4]) not character cards, of $($s1[5]) files; " +
        "G1 failing $($g1[0]) of $($g1[1]), slots $($g1[2]), dead $($g1[3]); S2 $($s2[0]) equal, $($s2[1]) differ, $($s2[2]) failed of $($s2[3]) pairs ($($s2[4]) by rule); " +
        "import $($ip[0]) passed, $($ip[1]) failed; P1 $($p1[0]) passed, $($p1[1]) failed, cards $($p1[2]), compared $($p1[3]), rows $($p1[4]), rows that differ $($p1[5]); " +
        "coordinates $($co[0]) passed, $($co[1]) failed; minutes per step " + (($steps.GetEnumerator() | ForEach-Object { "$($_.Key) {0:N1}" -f $_.Value }) -join ', ') +
        "; part minutes " + (($parts | ForEach-Object { "$($_[0]) {0:N1}" -f $_[3] }) -join ', ')
    if ($s1[1] -gt 0 -or $s1[2] -gt 0 -or $s1[3] -gt 0 -or $s2[1] -gt 0 -or $s2[2] -gt 0 -or $ip[1] -gt 0 -or $p1[1] -gt 0 -or $co[1] -gt 0 -or $g1[0] -gt 0) { $res = 'FAIL' }
    if ($pf.Count) { $line += '; failing parts: ' + ($pf -join ' | ') }
    if ($crashes.Count) { $res = 'FAIL'; $line += '; native crashes: ' + ($crashes -join ', ') }
    $merged = $null
    foreach ($sx in 'female', 'male') {
        if (-not $imp[$sx]) { continue }
        $j = Judge-Import $imp[$sx] 0
        $line += "; CardImport.${sx}: $($j.Text)"
        if ($j.Result -eq 'FAIL') { $res = 'FAIL' } elseif ($j.Result -eq 'WARN' -and $res -eq 'PASS') { $res = 'WARN' }
        $merged = if ($merged) { Merge-Import $merged $imp[$sx] } else { $imp[$sx] }
    }
    $Results.Add([pscustomobject]@{ Test = $id; Result = $res; Minutes = ((Get-Date) - $t0).TotalMinutes; Numbers = $line; Report = $null; Import = $merged })
    $color = switch ($res) { 'PASS' { 'Green' } 'WARN' { 'Yellow' } default { 'Red' } }
    Write-Host ("[run_tests]          {0,-26} {1} ({2:N1} min) {3}" -f $id, $res, ((Get-Date) - $t0).TotalMinutes, $line) -ForegroundColor $color
}
# The CardImport rows (every card headless, 30 motion steps; .doc: through the importer and the document build): opt-in since
# Phase 5.2 (AllCards covers them; since the card path left the tests both are document builds)
Row 'CardImport.female'     full 'cards-full' -OptIn             -Run { Invoke-CardImport 'CardImport.female' $Targets.cards.female $false }
Row 'CardImport.male'       full 'cards-full' -OptIn             -Run { Invoke-CardImport 'CardImport.male' $Targets.cards.male $false }
Row 'CardImport.female.doc' full 'cards-full' -OptIn             -Run { Invoke-CardImport 'CardImport.female.doc' $Targets.cards.female $false @('-source', 'document') }
Row 'CardImport.male.doc'   full 'cards-full' -OptIn             -Run { Invoke-CardImport 'CardImport.male.doc' $Targets.cards.male $false @('-source', 'document') }

# ---- the pre-upload texture check (Phase 5.2 package C; headless, -Full): the checks that
# replaced the thumbnail guard step and the TextureRepair row (TC1 is Mono.content). TextureCheckTest.parity (TC2): the check's
# patch of every bundle of the repair index against Tools\texguard_repair.py's copy, texture by texture (-cross <dir> of
# texguard_repair.py --cross-check is a manual extra: every object byte-equal); .load (TC3): every patch / patch-grey bundle
# opened through the catalog and every Texture2D loaded (0 mip assertions, 0 size errors); .cards (TC5): the guard-engaged
# cards with the repaired copies off = their S1 baselines; .cache (TC6): the verdict cache; .quarantine: the crash quarantine
# in one process (QC1-QC3; the real crash and the start after it, -phase crash / after, are a manual pair); .mesh: the
# truncated meshes completed, every prefab of the effects pack loads; .cost (TC7): the first checks, then a second session
# from the verdict cache (>= 99 %)
Row 'TextureCheckTest.parity' full 'content'                     'TextureCheckTest.RunParity' @() 'Spike\out\texcheck\texcheck_parity_report.txt' -TimeoutMin 30
Row 'TextureCheckTest.load'   full 'content,items,maps'          'TextureCheckTest.RunLoad' @() 'Spike\out\texcheck\texcheck_load_report.txt' -TimeoutMin 45
Row 'TextureCheckTest.cards'  full 'content,chara-build,maker'   'TextureCheckTest.RunCards' @() 'Spike\out\texcheck\texcheck_cards_report.txt' -TimeoutMin 30
Row 'TextureCheckTest.cache'  full 'content'                     'TextureCheckTest.RunCache' @() 'Spike\out\texcheck\texcheck_cache_report.txt' -TimeoutMin 15
Row 'TextureCheckTest.quarantine' full 'content'                 'TextureCheckTest.RunQuarantine' @() 'Spike\out\texcheck\texcheck_quarantine_unit_report.txt' -TimeoutMin 15
Row 'TextureCheckTest.mesh'   full 'content,items'               'TextureCheckTest.RunMesh' @() 'Spike\out\texcheck\texcheck_mesh_report.txt' -TimeoutMin 15
Row 'TextureCheckTest.cost'   full 'content' -Run {
    $id = 'TextureCheckTest.cost'
    $t0 = Get-Date
    $r1 = Invoke-Unity "$id.first" 'TextureCheckTest.RunCost' @() $false 'Spike\out\texcheck\texcheck_cost_report.txt' 20
    $s1 = Read-Summary $r1.Report
    $r2 = Invoke-Unity "$id.cached" 'TextureCheckTest.RunCost' @('-expect', 'cached') $false 'Spike\out\texcheck\texcheck_cost_report.txt' 20
    $s2 = Read-Summary $r2.Report
    $res = if ((Judge $r1 $s1) -eq 'PASS' -and (Judge $r2 $s2) -eq 'PASS') { 'PASS' } else { 'FAIL' }
    Add-Result $id $res ((Get-Date) - $t0).TotalMinutes ("first session: $(if ($r1.Code) { "exit $($r1.Code); " })$($s1.Line); second (from the cache): $(if ($r2.Code) { "exit $($r2.Code); " })$($s2.Line)") $r2.Report
}

# ---- bundle ownership, the LRU and the byte budget (Phase 5.2 package O; -Full): BO2-BO7,
# BOL, BOP, BOS headless (BO6: the S1 set built twice, once with a budget of 0, an eviction and an unload between every card);
# .maps: BOM in play mode (headless, -NoQuit): a replaced map releases its owner and counts as a destroyed realization. BO1 is
# Mono.content; BO8 / BO9 are AssetRedirectTest and ThumbGuardTest.
Row 'BundleOwnershipTest'      full 'content,maker,chara-build,items' 'BundleOwnershipTest.Run' @() 'Spike\out\bundles\bundle_ownership_report.txt' -TimeoutMin 30
Row 'BundleOwnershipTest.maps' full 'content,maps'               'BundleOwnershipTest.RunMaps' @() 'Spike\out\bundles\bundle_maps_report.txt' -NoQuit -TimeoutMin 20

# ---- graphics group (not with -Quick). The render and capture rows pass -syncShaders (Editor/BatchShaderCompile.cs: in that
# process shader variants compile before their first draw instead of drawing the Editor's placeholder; Phase 5.2, l.663);
# the .timing rows never do (their medians are warm and must not include the compile).
Row 'CharaRenderTest'     graphics 'chara-render,shaders' -DevInputs 'Tools/targets.json' -Run {
    $r = Invoke-Unity 'CharaRenderTest' 'CharaRenderTest.Run' @('-targets', '-urp', '-diff', '-syncShaders') $true 'Spike\out\chara\render_report_targets.txt'
    $res = 'FAIL'; $txt = "exit $($r.Code); no report"
    if ($r.Report) {
        $lines = Get-Content -LiteralPath $r.Report -Encoding UTF8
        $built = @($lines | Where-Object { $_ -match '^== .+: built in' }).Count
        $failed = @($lines | Where-Object { $_ -match 'BUILD FAILED' }).Count
        $fb = @{}
        foreach ($l in $lines) {
            if ($l -match '^   fallback \(URP/Lit\) shaders: (.+)$' -and $Matches[1] -ne 'none') {
                foreach ($e in $Matches[1] -split ', ') { if ($e -match '^(.+) x(\d+)$') { $fb[$Matches[1]] = 1 + [int]($fb[$Matches[1]]) } }
            }
        }
        $vanilla = @($fb.Keys | Where-Object { $VanillaShaders[$_] -eq 'chara' })
        $res = if ($r.Code -ne 0 -or $failed -gt 0 -or $built -lt @($Targets.visual.PSObject.Properties).Count -or $vanilla.Count -gt 0) { 'FAIL' } elseif ($fb.Count -gt 0) { 'WARN' } else { 'PASS' }
        $txt = "cards built $built, build failures $failed, unfitted (URP/Lit fallback) shaders $($fb.Count)" + $(if ($fb.Count) { ': ' + ($fb.Keys -join ', ') } else { '' })
    }
    Add-Result 'CharaRenderTest' $res $r.Minutes $txt $r.Report
}
Row 'RenderPipelineTest'       graphics 'shaders,chara-render,items,maps,player' 'RenderPipelineTest.Run' @('-syncShaders') 'Spike\out\render\render_pipeline_report.txt' -NoQuit -TimeoutMin 30
Row 'LookTest.render'          graphics 'motion,chara-render'    'LookTest.Run' @('-render', '-syncShaders') 'Spike\out\motion\look_report.txt'
Row 'ExpressionTest.render'    graphics 'motion,chara-render'    'ExpressionTest.Run' @('-render', '-syncShaders') 'Spike\out\motion\face_report.txt'
Row 'AnimationTest.render'     graphics 'motion,chara-render'    'AnimationTest.Run' @('-render', '-syncShaders') 'Spike\out\motion\anim_report.txt'
Row 'SaveFormatTest.render'    graphics 'save,chara-render'      'SaveFormatTest.RunGraphics' @('-syncShaders') 'Spike\out\save\saveformat_graphics_report.txt'
Row 'SceneRuntimeTest.render'  graphics 'scene,chara-render'     'SceneRuntimeTest.RunRender' @('-syncShaders') 'Spike\out\scene\scene_runtime_render_report.txt'
Row 'ItemTest.render'          graphics 'items,shaders'          'ItemTest.RunRender' @('-syncShaders') 'Spike\out\items\item_render_report.txt'
Row 'CharaDocBuildTest.render' graphics 'import,chara-render'    'CharaDocBuildTest.RunRender' @('-syncShaders') 'Spike\out\import\docbuild_render_report.txt'
Row 'SceneRuntimeTest.play'    graphics 'scene,maps,chara-build' 'SceneRuntimeTest.RunPlay' @() 'Spike\out\scene\scene_runtime_play_report.txt' -NoQuit
Row 'MapShaderTest'            graphics 'maps,shaders'           'MapShaderTest.Run' @() 'Spike\out\maps\map_shader_report.txt'
Row 'MapShaderTest.probe'      graphics 'maps,shaders'           'MapShaderTest.RunProbe' @() 'Spike\out\maps\map_shader_probe.txt' -NoQuit -TimeoutMin 30
Row 'ModShaderTest'            graphics 'shaders'                'ModShaderTest.Run' @() 'Spike\out\modshaders\mod_shader_report.txt' -TimeoutMin 30
Row 'MapRenderTest'            graphics 'maps,shaders'           'MapRenderTest.Run' @('-syncShaders') 'Spike\out\maps\map_render_report.txt' -NoQuit -TimeoutMin 40
Row 'EffectsTest'              graphics 'effects'                'EffectsTest.Run' @() 'Spike\out\effects\effects_report.txt'
Row 'EffectsTest.render'       graphics 'effects,player'         'EffectsTest.RunRender' @('-syncShaders') 'Spike\out\effects\effects_render_report.txt' -NoQuit -TimeoutMin 30
Row 'EffectsTest.play'         graphics 'effects,maps,save'      'EffectsTest.RunPlay' @() 'Spike\out\effects\effects_play_report.txt' -NoQuit -TimeoutMin 30
Row 'DofTest.render'           graphics 'effects,shaders'        'DofTest.RunRender' @('-syncShaders') 'Spike\out\effects\dof_report.txt' -TimeoutMin 20
Row 'SceneTexturesTest.render' graphics 'effects,shaders,maps,chara-render' 'SceneTexturesTest.RunRender' @('-syncShaders') 'Spike\out\effects\scene_textures_report.txt' -TimeoutMin 20
Row 'SceneTexturesTest.play'   graphics 'effects,maps,items,shaders' 'SceneTexturesTest.RunPlay' @() 'Spike\out\effects\scene_textures_play_report.txt' -NoQuit -TimeoutMin 30
Row 'SkyAmbientTest'           graphics 'effects,shaders'        'SkyAmbientTest.Run' @() 'Spike\out\effects\sky_ambient_report.txt' -TimeoutMin 20
Row 'SkyAmbientTest.render'    graphics 'effects,shaders,chara-render' 'SkyAmbientTest.RunRender' @('-syncShaders') 'Spike\out\effects\sky_ambient_render_report.txt' -TimeoutMin 20
Row 'SkyAmbientTest.play'      graphics 'effects,maps'           'SkyAmbientTest.RunPlay' @() 'Spike\out\effects\sky_ambient_play_report.txt' -NoQuit -TimeoutMin 30
# MeReportTest.play (Phase 5.R package N): Editor play with the UI, port 17623 - NG1-NG4, the
# me.shaderUnavailable finding of MaterialEditor swaps HS2Next cannot draw (the vanilla skin kept, the inspector badge and line)
Row 'MeReportTest.play'        graphics 'chara-build,ui,effects' 'MeReportTest.RunPlay' @() 'Spike\out\mereport\me_report_play.txt' -NoQuit -TimeoutMin 20
Row 'BridgeAcceptance.play'    graphics 'bridge,maps,items,chara-render,shaders,scene,save,effects,pose' 'BridgeTest.RunPlay' @() 'Spike\out\bridge\bridge_play_report.txt' -NoQuit -TimeoutMin 30
Row 'BridgeAcceptance.play.ui' graphics 'ui,bridge,routes,capture' 'BridgeTest.RunPlayUi' @() 'Spike\out\bridge\bridge_play_ui1_report.txt' -NoQuit -TimeoutMin 35
Row 'UiPanelsTest.play'        graphics 'ui'                     'UiPanelsTest.Run' @() 'Spike\out\ui\ui_panels_report.txt' -NoQuit -TimeoutMin 30
Row 'MakerListTest.thumbs'     graphics 'maker,ui'               'MakerListTest.Thumbs' @() 'Spike\out\maker\maker_thumbs_report.txt' -NoQuit -TimeoutMin 20
# v0.3 trial 2 (package G): ThumbGuardTest.play - Darkruler 2B's 512 x 511 DXT5 head thumbnail refused by its header without
# the guard's records and loaded from the repaired copy with them, every block-size thumbnail of the census loaded without an
# RGBA32 fallback; .clash - two thumbnail bundles with one internal name (unloading one leaves the other readable);
# ThumbScroll.play - TS: every thumbnail list of every Maker page scrolled to its end through ui.* for both SFW targets,
# every thumbnail from its bundle (-thumbDiskCache off), nothing pending, no fallback / failed read / mip assert / device loss
Row 'ThumbGuardTest.play'      graphics 'maker,ui' -Run {
    Invoke-ThumbGuardRow 'ThumbGuardTest.play' 'ThumbGuardTest.RunPlay' @('-census', (Join-Path $ThumbGuardAll 'defects.tsv')) $true 'Spike\out\maker\thumb_guard_play_report.txt' $true 15
}
Row 'ThumbGuardTest.clash'     graphics 'maker,ui'               'ThumbGuardTest.RunClash' @() 'Spike\out\maker\thumb_guard_clash_report.txt' -NoQuit -TimeoutMin 10
# (ThumbScroll.play runs in the whole suite only since Phase 5.2: an impact -Full keeps ThumbScroll.player)
Row 'ThumbScroll.play'         graphics 'maker,ui,bridge'        'BridgeTest.RunPlayThumbs' @('-thumbDiskCache', 'off') 'Spike\out\bridge\bridge_play_ts_report.txt' -NoQuit -TimeoutMin 30 -WholeSuite
Row 'MakerEditTest.leak'       graphics 'maker,scene'            'MakerEditTest.RunLeak' @() 'Spike\out\maker\maker_leak_report.txt' -TimeoutMin 40
Row 'MakerEditTest.timing'     graphics 'maker,scene'            'MakerEditTest.RunTiming' @() 'Spike\out\maker\maker_timing_report.txt' -TimeoutMin 30
Row 'MakerAcceptTest.s3'      graphics 'maker,chara-render'     'MakerAcceptTest.RunS3' @('-syncShaders') 'Spike\out\maker\maker_s3_report.txt' -TimeoutMin 40
Row 'MakerAcceptTest.timing'  graphics 'maker'                  'MakerAcceptTest.RunT' @() 'Spike\out\maker\maker_t_report.txt' -TimeoutMin 30
# MakerFaceTest.render and .timing run in separate Unity processes (one process running both lost its D3D12 device, 887a0005)
Row 'MakerFaceTest.render'   graphics 'maker,chara-render'     'MakerFaceTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_face_render_report.txt' -TimeoutMin 30
Row 'MakerFaceTest.timing'   graphics 'maker'                  'MakerFaceTest.RunTiming' @() 'Spike\out\maker\maker_face_timing_report.txt' -TimeoutMin 30
# MakerBodyTest.render and .timing likewise (Phase 4.2); .timing runs its edit-mode part, then (-play, -NoQuit) the play-mode
# part of UB2 in the same process: the row reads both reports (maker_body_timing_report.txt, maker_body_timing_play_report.txt)
Row 'MakerBodyTest.render'   graphics 'maker,chara-render'     'MakerBodyTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_body_render_report.txt' -TimeoutMin 30
Row 'MakerBodyTest.timing'   graphics 'maker' -Run {
    $id = 'MakerBodyTest.timing'
    $editRep = Join-Path $Project 'Spike\out\maker\maker_body_timing_report.txt'
    $t0 = (Get-Date).ToUniversalTime()
    $r = Invoke-Unity $id 'MakerBodyTest.RunTiming' @('-play') $true 'Spike\out\maker\maker_body_timing_play_report.txt' 40 $true
    $play = Read-Summary $r.Report
    $edit = @{ Result = 'FAIL'; Line = 'no edit-mode report written' }
    if ((Test-Path $editRep) -and (Get-Item $editRep).LastWriteTimeUtc -gt $t0) {
        $copy = Join-Path $OutDir ('{0}__{1}' -f $id, (Split-Path -Leaf $editRep))
        Copy-Item -LiteralPath $editRep -Destination $copy -Force
        $edit = Read-Summary $copy
    }
    $res = Judge $r $play
    if ($edit.Result -ne 'PASS') { $res = 'FAIL' }
    $line = "edit: $($edit.Line); play: $($play.Line)"; if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
    Add-Result $id $res $r.Minutes $line $r.Report
}
# MakerHairTest.render and .timing likewise, each in its own Unity process (Phase 4.3)
Row 'MakerHairTest.render'   graphics 'maker,chara-render'     'MakerHairTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_hair_render_report.txt' -TimeoutMin 30
Row 'MakerHairTest.timing'   graphics 'maker'                  'MakerHairTest.RunTiming' @() 'Spike\out\maker\maker_hair_timing_report.txt' -TimeoutMin 30
# MakerClothesTest.render and .timing likewise, each in its own Unity process (Phase 4.4)
Row 'MakerClothesTest.render' graphics 'maker,chara-render'    'MakerClothesTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_clothes_render_report.txt' -TimeoutMin 30
Row 'MakerClothesTest.timing' graphics 'maker'                 'MakerClothesTest.RunTiming' @() 'Spike\out\maker\maker_clothes_timing_report.txt' -TimeoutMin 30
# MakerAccessoriesTest.render and .timing likewise, each in its own Unity process (Phase 4.5)
Row 'MakerAccessoriesTest.render' graphics 'maker,chara-render' 'MakerAccessoriesTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_accessories_render_report.txt' -TimeoutMin 30
Row 'MakerAccessoriesTest.timing' graphics 'maker'             'MakerAccessoriesTest.RunTiming' @() 'Spike\out\maker\maker_accessories_timing_report.txt' -TimeoutMin 30
# MakerFileTest.render and .timing likewise, each in its own Unity process (Phase 4.6 package A: VT4 / VT5, VU1-VU4)
Row 'MakerFileTest.render'   graphics 'maker,chara-render,save' 'MakerFileTest.RunRender' @('-syncShaders') 'Spike\out\maker\maker_file_render_report.txt' -TimeoutMin 30
Row 'MakerFileTest.timing'   graphics 'maker'                  'MakerFileTest.RunTiming' @() 'Spike\out\maker\maker_file_timing_report.txt' -TimeoutMin 30
# MosaicTest.render and .timing (5.M package R), each in its own Unity process (a render and a timing suite never share
# one): MR1-MR5 keep every image in memory (code-made marker textures judged by pixel counts; nothing is written), MR6
# the costs of methods (a) and (b)
Row 'MosaicTest.render'    graphics 'chara-render,shaders,effects,capture' 'MosaicTest.RunRender' @('-syncShaders') 'Spike\out\maker\mosaic_render_report.txt' -TimeoutMin 30
Row 'MosaicTest.timing'    graphics 'chara-render,effects'       'MosaicTest.RunTiming' @() 'Spike\out\maker\mosaic_timing_report.txt' -TimeoutMin 30
# LooseTextureTest.quit (Phase 4.2 package L; opt-in): play mode with web requests, loose reads and asset loads / opens of the
# Maker thumbnails in flight, then the session ends - the quit release must leave nothing in flight (the 4.2 shutdown hang).
# Pass: exit 0 within 5 min, 0 FAIL, and the Unity log holds "[thumbs] quit (" (its path is in the runner's console)
Row 'LooseTextureTest.quit'   graphics 'ui,maker' -OptIn -Run {
    $id = 'LooseTextureTest.quit'
    $r = Invoke-Unity $id 'LooseTextureTest.RunQuit' @() $true 'Spike\out\maker\loose_quit_report.txt' 5 $true
    $sum = Read-Summary $r.Report
    $log = $null
    if (Test-Path $r.Console) { $m = Select-String -LiteralPath $r.Console -Pattern '\[unity_batch\] last \d+ lines of (.+)$' | Select-Object -First 1; if ($m) { $log = $m.Matches[0].Groups[1].Value.Trim() } }
    $quit = $log -and (Test-Path $log) -and (Select-String -LiteralPath $log -SimpleMatch '[thumbs] quit (' -Quiet)
    $res = Judge $r $sum
    if (-not $quit) { $res = 'FAIL' }
    $line = $sum.Line + $(if ($quit) { '; "[thumbs] quit (" logged' } else { '; no "[thumbs] quit (" line in the log' })
    if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
    Add-Result $id $res $r.Minutes $line $r.Report
}
Row 'GizmoTest.render'         graphics 'ui,shaders'             'GizmoTest.Run' @('-syncShaders') 'Spike\out\ui\gizmo_report.txt' -NoQuit -TimeoutMin 20
Row 'CaptureTest.render'       graphics 'capture,effects,scene,player' 'CaptureTest.RunRender' @('-syncShaders') 'Spike\out\capture\capture_render_report.txt' -NoQuit -TimeoutMin 30
Row 'BackgroundTest.render'    graphics 'capture,effects,ui'     'BackgroundTest.RunRender' @('-syncShaders') 'Spike\out\capture\background_render_report.txt' -NoQuit -TimeoutMin 30
Row 'PauseTest.play'           graphics 'routes,effects,maps,ui,motion' 'PauseTest.RunPlay' @() 'Spike\out\pause\pause_play_report.txt' -NoQuit -TimeoutMin 30
Row 'RouteTest.play'           graphics 'routes,ui'              'RouteTest.RunPlay' @() 'Spike\out\routes\route_play_report.txt' -NoQuit -TimeoutMin 20
Row 'BridgeAcceptance.play.routes' graphics 'routes,bridge,capture' 'BridgeTest.RunPlayRoutes' @() 'Spike\out\bridge\bridge_play_rt1_report.txt' -NoQuit -TimeoutMin 20
# PluginLoaderTest.play (Phase 5.1 packages L and S): play mode with the UI, port 17398, -pluginsDir
# Spike\out\plugins\play (the sample and the probes; -stage re-stages it from the current builds inside the Unity process):
# T3A, PL13 UI registration, the sample's SQ5-SQ11 over HTTP, PL14 leak rows, then a second session without a domain reload (T3B)
Row 'PluginLoaderTest.play'    graphics 'plugins,ui,bridge'      'PluginLoaderTest.Run' @('-stage') 'Spike\out\plugins\plugin_loader_play_report.txt' -NoQuit -TimeoutMin 20
# SetupTest.play (Phase 5.3 package F): Editor play with the UI, port 17398, on a fresh data root
# under Spike\out\setup (the launch override's dataRoot: the project's settings file is never written) - FR5 (skip: the Studio
# without HS2 content, the banner, setup.json skipped; -setup reopens the guide), SG1 (the settings page keeps unknown members,
# comments and order; a command-line value is locked), SG2 (an unknown key: the log line, the toast, app.settings.unknownKeys)
Row 'SetupTest.play'           graphics 'setup,ui,bridge'        'SetupTest.Run' @() 'Spike\out\setup\setup_play_report.txt' -NoQuit -TimeoutMin 20

# ---- player group (Phase 2.5; not with -Quick)
Row 'PlayerBuild.app' player 'player,shaders,setup' -Run { $null = Invoke-PlayerBuild 'PlayerBuild.app' 'PlayerBuild.BuildApp' 'Builds\App.build_report.txt' $true }

# ---- Phase 5.3: the distribution package and the bare exe.
# PlayerBuild.dist (package B's script): & Tools\package_dist.ps1 without -Zip assembles Builds\Dist\HS2Next from the tested
# Builds\App (the allow-list copied file by file, build.json rewritten to kind distribution, the package texts added) and checks
# it: DP2 (the copies hash-equal the app build, no link, no run-time folder), DP3 (leak_check --tree 0 hard; since Phase 5.4 the
# binary scan with --all-files: a pdb / source / game / path / profile / e-mail / token hit in an assembly built here or a data
# file fails, a vendor file passes within its baseline), DP4 (the build.json values). Exit 0 / 1; report
# Builds\Dist\package_report.txt (first line RESULT: PASS | FAIL). The zip is the release run's (-Zip), never this row's.
$DistDir = Join-Path $Project 'Builds\Dist\HS2Next'
Row 'PlayerBuild.dist' player 'player,setup,publication' -Run {
    $id = 'PlayerBuild.dist'
    $t0 = Get-Date
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null; return }
    $console = Join-Path $OutDir 'PlayerBuild.dist.console.txt'
    Write-Host ("[run_tests] {0} {1,-26} Tools\package_dist.ps1" -f (Get-Date -Format 'HH:mm:ss'), $id)
    & (Join-Path $PSScriptRoot 'package_dist.ps1') *>&1 | ForEach-Object { "$_" } | Out-File -FilePath $console -Encoding utf8
    $code = $LASTEXITCODE
    $rep = Copy-PlayerReport $id (Join-Path $Project 'Builds\Dist\package_report.txt')
    $lines = if ($rep -and (Get-Item -LiteralPath $rep).LastWriteTime -ge $t0.AddSeconds(-5)) { @(Get-Content -LiteralPath $rep -Encoding UTF8) } else { @() }
    $first = if ($lines.Count) { $lines[0] } else { 'no report written' }
    $res = if ($code -eq 0 -and $first -eq 'RESULT: PASS') { 'PASS' } else { 'FAIL' }
    $key = @($lines | Where-Object { $_ -like 'FAIL *' -or $_ -like 'build.json of the app build:*' -or $_ -like 'staged tree:*' -or $_ -like 'binaries hash-equal*' } | ForEach-Object { $_.Trim() })
    foreach ($k in 'leak check', 'binary string scan') {
        $l = @($lines | Where-Object { $_ -like "${k}*" } | Select-Object -First 1) -join ''
        if ($l -match 'SUMMARY: ([^;(]+)') { $key += "${k}: $($Matches[1].Trim())" }
    }
    Add-Result $id $res ((Get-Date) - $t0).TotalMinutes ("exit $code; $first" + $(if ($key.Count) { '; ' + ($key -join '; ') } else { '' })) $rep
}

# The bare-exe rows (package F's scripts): a plain copy of Builds\Dist\HS2Next (the script copies it under its output; the staged
# folder never runs in place) started WITHOUT arguments on the product port 17322, windowed. The runner waits at most 10 min until
# nothing listens on 17322 (else WARN "not run: port 17322 busy"), waits for the game gate, holds the GPU lock and the input lock
# around the script (with -GpuLock / -InputLock) and stops the script's process tree by PID when a game process appears (WARN,
# "aborted by the game gate"). The script sets HS2NEXT_DATA_ROOT for each player it starts (a bare exe without it would write into
# the owner's per-user folder), copies the per-user Player.log into its output after each exit and stops only its own players.
# Contract (Tools\README_tests.md "Player runs"): python -I Tools\<script> --dist <dist>
# --project <project> --out <out> [--port 17322] [--timeout-min N] [...]; report <out>\<report> with the first and last line
# "SUMMARY: N PASS, M FAIL" and one "PASS | FAIL <id> <text>" line per check; exit 0 pass, 1 a check failed, 2 setup error /
# refused, 3 aborted by the game gate.
function Invoke-BareExeRow([string]$id, [string]$scriptName, [string[]]$extra, [string]$outName, [string]$reportName, [int]$timeoutMin) {
    $t0 = Get-Date
    $script = Join-Path $PSScriptRoot $scriptName
    if (-not (Test-Path -LiteralPath $script)) { Add-Result $id 'WARN' 0 "Tools\$scriptName not present yet (not run)" $null; return }
    if (-not (Test-Path -LiteralPath (Join-Path $DistDir 'HS2Next.exe'))) { Add-Result $id 'FAIL' 0 'no distribution folder (Builds\Dist\HS2Next): run PlayerBuild.dist' $null; return }
    $tg = Get-Date
    while ((Get-GameRunning).Count -and ((Get-Date) - $tg).TotalMinutes -lt 30) { Write-Host "[run_tests] $id`: the game gate holds (waiting)"; Start-Sleep -Seconds 30 }
    if ((Get-GameRunning).Count) { Add-Result $id 'WARN' ((Get-Date) - $t0).TotalMinutes 'not run: a process of the game gate ran for 30 min' $null; return }
    if (-not (Wait-PortFree 17322 10)) { Add-Result $id 'WARN' ((Get-Date) - $t0).TotalMinutes 'not run: port 17322 busy' $null; return }
    if (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes 'less than 8 GB of free RAM for 30 min' $null; return }
    $out = Join-Path $PlayerOut $outName
    Remove-TreeSafe $out
    New-Item -ItemType Directory -Force $out | Out-Null
    if (-not (Enter-WindowedLocks $id $timeoutMin)) { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes "exit 126; the GPU / input locks stayed held for $LockWaitMin min" $null; return }
    try {
        if (-not (Wait-PortFree 17322 0)) { Add-Result $id 'WARN' ((Get-Date) - $t0).TotalMinutes 'not run: port 17322 busy' $null; return }
        $p = Invoke-Process $id 'python' (@('-I', $script, '--dist', $DistDir, '--project', $Project, '--out', $out, '--port', '17322', '--timeout-min', "$($timeoutMin - 2)") + $extra) $timeoutMin $false $true
    } finally { Exit-WindowedLocks }
    $rep = Copy-PlayerReport $id (Join-Path $out $reportName)
    $lines = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() }) } else { @() }
    $last = @($lines | Where-Object { $_ -like 'SUMMARY: *' } | Select-Object -Last 1) -join ''
    $res = switch ($p.Code) { 0 { 'PASS' } 3 { 'WARN' } default { 'FAIL' } }
    if ($res -eq 'PASS' -and -not ($last -match 'SUMMARY: (\d+) PASS, 0 FAIL')) { $res = 'FAIL' }
    $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 2 { ' (setup error / refused)' } 3 { ' (aborted by the game gate: repeat later)' } 124 { ' (no exit: stopped)' } default { '' } }
    $fl = @($lines | Where-Object { $_ -like 'FAIL *' } | Select-Object -First 4 | ForEach-Object { $_.Trim() })
    $key = @($lines | Where-Object { $_ -match '^(PASS|FAIL|INFO|WARN)\s+(SU4|FR8|TK6|UP7|UP8)\b' } | Select-Object -First 6 | ForEach-Object { ($_.Trim() -split '\s+', 2)[1] })
    Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($last) { $last } else { 'no report' }) + $(if ($fl.Count) { '; ' + ($fl -join '; ') } else { '' }) +
        $(if ($key.Count) { '; ' + ($key -join '; ') } else { '' })) $rep
}
# PlayerBuild.noargs (A1; UP7, UP8, TK6, QT1): run (1) no arguments, HS2NEXT_DATA_ROOT=<out>\データ root\ holding only
# Config\settings.local.json (hs2Root, the project's cache, bridge.allowQuit): endpoint.json naming the PID with tokenRequired,
# 401 without the token, ping.appReady with it, exactly the documented folders under the root, nothing next to the exe,
# app.paths mode override, app.quit exit 0; run (2) portable.txt beside the copied exe: the folders next to it
Row 'PlayerBuild.noargs' player 'player,setup,bridge' -Run { Invoke-BareExeRow 'PlayerBuild.noargs' 'noargs_check.py' @() 'noargs' 'noargs_report.txt' 12 }
# FirstRun.player (A2; FR4, FR6, FR7, QT1): the guide walked over ui.* on an empty override root with a planted legacy UserData
# next to the copied exe (the migration offer and copy), quit on page 3 and resumed, the project's cache chosen on page 4, the
# Studio reached, the written files checked, File > Quit
Row 'FirstRun.player' player 'player,setup,ui' -Run { Invoke-BareExeRow 'FirstRun.player' 'firstrun_check.py' @('--cache', 'warm') 'firstrun' 'firstrun_report.txt' 12 }
# FirstRun.cold (opt-in; A6: FR8, SU4): the same walk with the default (empty) cache under the root - the cold index with the
# progress page; bounded 22 min
Row 'FirstRun.cold' player 'player,setup,ui' -OptIn -Run { Invoke-BareExeRow 'FirstRun.cold' 'firstrun_check.py' @('--cache', 'cold') 'firstrun_cold' 'firstrun_report.txt' 22 }

# PlayerSmoke: the smoke player is a second, small build (about 0.7 min incremental) because the app player has no smoke
# scene (Phase 5.3 removes the smoke scene from release builds); then the -smoke run (exit 0 / 1 / 2, smoke_report.txt).
Row 'PlayerSmoke' player 'player,chara-render' -DevInputs 'Tools/targets.json' -Run {
    $t0 = Get-Date
    $b = Invoke-PlayerBuild 'PlayerSmoke.build' 'PlayerBuild.Build' 'Builds\Smoke.build_report.txt'
    if ($b -eq 'FAIL') { Add-Result 'PlayerSmoke' 'FAIL' ((Get-Date) - $t0).TotalMinutes 'the smoke build failed (PlayerSmoke.build)' $null }
    elseif (-not (Test-Path $settingsFile)) { Add-Result 'PlayerSmoke' 'FAIL' 0 'no Config\settings.local.json' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result 'PlayerSmoke' 'FAIL' ((Get-Date) - $t0).TotalMinutes 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'smoke'
        Remove-TreeSafe $out
        New-Item -ItemType Directory -Force $out | Out-Null
        $p = Invoke-Process 'PlayerSmoke' $SmokeExe @('-batchmode', '-smoke', $out, '-settings', $settingsFile, '-cacheDir', $PlayerCache,
            '-logFile', (Join-Path $out 'player.log')) 15
        $rep = Copy-PlayerReport 'PlayerSmoke' (Join-Path $out 'smoke_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        $render = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_ -like 'render front:*' } | ForEach-Object { ($_ -split ' -> ')[1] }) -join '' } else { '' }
        Add-Result 'PlayerSmoke' $res ((Get-Date) - $t0).TotalMinutes ("run exit $($p.Code)$(if ($p.Code -eq 124) { ' (timeout)' }); $($sum.Line)" + $(if ($render) { "; front $render" } else { '' })) $rep
    }
}

# BridgeAcceptance.player: the app player runs the acceptance against its own bridge (Runtime/App/PlayerAcceptance.cs) and
# compares its T5 shots with those of BridgeAcceptance.play (Spike\out\bridge\out\shots, written by the graphics group).
Row 'BridgeAcceptance.player' player 'player,bridge,maps,items,chara-render,effects,pose' -DevInputs 'Tools/targets.json' -Run {
    $id = 'BridgeAcceptance.player'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Test-Path $settingsFile)) { Add-Result $id 'FAIL' 0 'no Config\settings.local.json' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'accept'
        Remove-TreeSafe $out
        New-Item -ItemType Directory -Force $out | Out-Null
        $card = ([string]$Targets.visual.female) -replace '^chara/female/', ''
        $p = Invoke-Process $id $AppExe @('-batchmode', '-bridgeAccept', $out, '-acceptTimeoutSec', '900', '-acceptCard', $card,
            '-acceptCompare', (Join-Path $SpikeOut 'bridge\out\shots'), '-settings', $settingsFile, '-cacheDir', $PlayerCache,
            '-userData', (Join-Path $out 'userdata'), '-bridgeOutputDir', (Join-Path $out 'out'), '-bridgePort', '17397',
            '-logFile', (Join-Path $out 'player.log')) 20
        $rep = Copy-PlayerReport $id (Join-Path $out 'bridge_accept_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 3 { ' (watchdog)' } 124 { ' (no exit: stopped)' } default { ' (crash?)' } }
        $extra = @()
        if ($rep) {
            $lines = Get-Content -LiteralPath $rep -Encoding UTF8
            $ready = @($lines | ForEach-Object { if ($_ -match '^   app Ready ([\d.]+) s after the process start') { "ready $($Matches[1]) s after the process start" } })
            $par = @($lines | ForEach-Object { if ($_ -match 'render parity (parity_\S+) vs .*?: \d+x\d+, (\d+) of') { "$($Matches[1]) $($Matches[2]) px differ" } })
            $extra = @($ready) + $(if ($par.Count) { 'T5: ' + ($par -join ', ') } else { @() })
        }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; $($sum.Line)" + $(if ($extra.Count) { '; ' + ($extra -join '; ') } else { '' })) $rep
    }
}

# BridgeAcceptance.player.ui: the same player acceptance with the Studio UI on (-ui), so UI1 (Phase 3.5)
# runs after IK1 in the player; own output folder and port.
Row 'BridgeAcceptance.player.ui' player 'player,bridge,ui,routes,capture' -DevInputs 'Tools/targets.json' -Run {
    $id = 'BridgeAcceptance.player.ui'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Test-Path $settingsFile)) { Add-Result $id 'FAIL' 0 'no Config\settings.local.json' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'accept_ui'
        Remove-TreeSafe $out
        New-Item -ItemType Directory -Force $out | Out-Null
        $card = ([string]$Targets.visual.female) -replace '^chara/female/', ''
        $p = Invoke-Process $id $AppExe @('-batchmode', '-ui', '-bridgeAccept', $out, '-acceptTimeoutSec', '900', '-acceptCard', $card,
            '-acceptCompare', (Join-Path $SpikeOut 'bridge\out\shots'), '-settings', $settingsFile, '-cacheDir', $PlayerCache,
            '-userData', (Join-Path $out 'userdata'), '-bridgeOutputDir', (Join-Path $out 'out'), '-bridgePort', '17393',
            '-logFile', (Join-Path $out 'player.log')) 20
        $rep = Copy-PlayerReport $id (Join-Path $out 'bridge_accept_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 3 { ' (watchdog)' } 124 { ' (no exit: stopped)' } default { ' (crash?)' } }
        $ui1 = ''
        if ($rep) {
            $lines = Get-Content -LiteralPath $rep -Encoding UTF8
            $skip = @($lines | Where-Object { $_ -match 'UI[1-9] skipped' })
            if ($skip.Count) { $res = 'FAIL'; $ui1 = '; UI1 / UI2 / UI3 / UI4 / UI5 / UI6 / UI7 / UI8 / UI9 was skipped (the UI did not start)' }
        }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; $($sum.Line)$ui1") $rep
    }
}

# ThumbScroll.player (v0.3 trial 2, package G): TS in the app player with the UI (-acceptOnly TS: every thumbnail list of
# every Maker page scrolled to its end for both SFW targets, every thumbnail from its bundle) - the run that crashed the
# trial's player (access violation in the upload of a 512 x 511 DXT5 thumbnail) before the header check; own folder and port.
Row 'ThumbScroll.player' player 'maker,ui,player' -DevInputs 'Tools/targets.json' -Run {
    $id = 'ThumbScroll.player'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Test-Path $settingsFile)) { Add-Result $id 'FAIL' 0 'no Config\settings.local.json' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'ts'
        Remove-TreeSafe $out
        New-Item -ItemType Directory -Force $out | Out-Null
        $card = ([string]$Targets.visual.female) -replace '^chara/female/', ''
        $p = Invoke-Process $id $AppExe @('-batchmode', '-ui', '-bridgeAccept', $out, '-acceptOnly', 'TS', '-acceptTimeoutSec', '1800', '-acceptCard', $card,
            '-thumbDiskCache', 'off', '-settings', $settingsFile, '-cacheDir', $PlayerCache,
            '-userData', (Join-Path $out 'userdata'), '-bridgeOutputDir', (Join-Path $out 'out'), '-bridgePort', '17391',
            '-logFile', (Join-Path $out 'player.log')) 35
        $rep = Copy-PlayerReport $id (Join-Path $out 'bridge_accept_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 3 { ' (watchdog)' } 124 { ' (no exit: stopped)' } default { ' (crash?)' } }
        $extra = @()
        if ($rep) {
            $lines = Get-Content -LiteralPath $rep -Encoding UTF8
            $extra += @($lines | ForEach-Object { if ($_ -match '^\s+TS\.(female|male): (\d+) lists, (\d+) cells, (\d+) wheel events') { "$($Matches[1]) $($Matches[2]) lists / $($Matches[3]) cells / $($Matches[4]) wheels" } })
            $th = @($lines | Where-Object { $_ -match 'TS: ui\.state thumbs at the end: ' } | Select-Object -Last 1) -join ''
            foreach ($k in 'pending', 'fromBundles', 'guardSkipped', 'headerRejected', 'openBundles') { if ($th -match ('"' + $k + '":(\d+)')) { $extra += "$k $($Matches[1])" } }
            if (@($lines | Where-Object { $_ -match 'TS skipped' }).Count) { $res = 'FAIL'; $extra += 'TS was skipped (the UI did not start)' }
        }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; $($sum.Line)" + $(if ($extra.Count) { '; ' + ($extra -join ', ') } else { '' })) $rep
    }
}

# BridgeChecks.player: package Q's external checks against the app player (bridge prep T1 / T4 / T7 / T8). Contract:
# python -I Tools\player_checks.py --exe <exe> --project <project> --out <out> [--port] [--timeout-min]; report
# <out>\player_checks_report.txt whose last line is "SUMMARY: N PASS, M FAIL"; exit 0 pass, 1 fail, 2 setup error.
Row 'BridgeChecks.player' player 'player,bridge' -Run {
    $id = 'BridgeChecks.player'
    $script = Join-Path $PSScriptRoot 'player_checks.py'
    if (-not (Test-Path $script)) { Add-Result $id 'WARN' 0 'Tools\player_checks.py not present yet (skipped)' $null }
    elseif (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'checks'
        $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17399', '--timeout-min', '20') 25
        $rep = Copy-PlayerReport $id (Join-Path $out 'player_checks_report.txt')
        $last = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() } | Select-Object -Last 1) -join '' } else { '' }
        $res = 'FAIL'
        if ($p.Code -eq 0 -and $last -match 'SUMMARY: (\d+) PASS, (\d+) FAIL') { $res = if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 2 { ' (setup error)' } 124 { ' (no exit: stopped)' } default { '' } }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($last) { $last } else { 'no report' })) $rep
    }
}

# PluginCheck.player (Phase 5.1; package S's script): the sample sequencer in the app player
# by the default discovery next to the exe - python -I Tools\plugin_player_check.py builds the sample (Tools\plugins.ps1),
# stages it into Builds\App\Plugins\stp3.sample.sequencer\ (plain file copies), starts the player without -pluginsDir and
# with HS2NEXT_NO_PLUGINS cleared for that process only (port 17394), runs PA1-PA9 over HTTP and removes exactly the files it
# staged. Exit 0 pass, 1 a check failed, 2 setup error (no exe / settings / fixture, the port taken, a plugin outside the test
# set in Builds\App\Plugins\, the owner's game running). Report <out>\plugin_player_check_report.txt, first and last line
# "SUMMARY: N PASS, M FAIL"; the key numbers add PA8's measurement line (PM1-PM5).
Row 'PluginCheck.player' player 'player,plugins' -Run {
    $id = 'PluginCheck.player'
    $script = Join-Path $PSScriptRoot 'plugin_player_check.py'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'plugins'
        $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17394', '--timeout-min', '20') 25
        $rep = Copy-PlayerReport $id (Join-Path $out 'plugin_player_check_report.txt')
        $lines = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() }) } else { @() }
        $last = if ($lines.Count) { $lines[-1] } else { '' }
        $res = 'FAIL'
        if ($p.Code -eq 0 -and $last -match 'SUMMARY: (\d+) PASS, (\d+) FAIL') { $res = if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed)' } 2 { ' (setup error)' } 124 { ' (no exit: stopped)' } default { '' } }
        $pm = @($lines | Where-Object { $_ -match '^PA8\s' } | ForEach-Object { ($_ -replace '^PA8\s+\S+\s+', '') }) -join ''
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($last) { $last } else { 'no report' }) + $(if ($pm) { "; PA8 $pm" } else { '' })) $rep
    }
}

# MapPlayerCheck.player: the map shader targets in the app player (Tools/map_player_check.py, Phase 3.2 package B): SpeedTree7,
# the HS2Next map shaders, Terrain Lit and the skyboxes resolve (every maps-block slot on its target), captures for viewing.
Row 'MapPlayerCheck.player' player 'player,maps,shaders' -Run {
    $id = 'MapPlayerCheck.player'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'maps'
        Remove-TreeSafe $out
        $p = Invoke-Process $id 'python' @('-I', (Join-Path $PSScriptRoot 'map_player_check.py'), '--exe', $AppExe, '--out', $out, '--port', '17398') 25
        $rep = Copy-PlayerReport $id (Join-Path $out 'map_player_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        Add-Result $id $res $p.Minutes ("exit $($p.Code); $($sum.Line)") $rep
    }
}

# EffectsPlayerCheck.player: the screen effects in the app player (Tools/effects_player_check.py, Phase 3.3 package B): the
# renderer's post-processing, the shipped template profile, the base grade, each effect changing a map-10 capture.
Row 'EffectsPlayerCheck.player' player 'player,effects' -Run {
    $id = 'EffectsPlayerCheck.player'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'effects'
        Remove-TreeSafe $out
        $epcArgs = @('-I', (Join-Path $PSScriptRoot 'effects_player_check.py'), '--exe', $AppExe, '--out', $out, '--port', '17398')
        # the Editor's run of the same cases (effects_player_check.py --url against BridgeServe, --out Spike\out\effects\editor_ref):
        # with it the player's effects are compared with the Editor's (same sign, mean within 8 / 255; Phase 5.R A3)
        $epcRef = Join-Path $SpikeOut 'effects\editor_ref\effects_stats.json'
        if (Test-Path $epcRef) { $epcArgs += @('--ref', $epcRef) }
        $p = Invoke-Process $id 'python' $epcArgs 20
        $rep = Copy-PlayerReport $id (Join-Path $out 'effects_player_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        Add-Result $id $res $p.Minutes ("exit $($p.Code); $($sum.Line)") $rep
    }
}

# CapturePlayerCheck.player: screenshots in the app player (Tools/capture_player_check.py, Phase 3.6 package C): F11 and
# Shift+F11 through ui.input with the Studio UI (the fit size, <UserData>/cap), R-M1 GPU memory of 4K / 16.6 Mpix / 8K renders.
Row 'CapturePlayerCheck.player' player 'player,capture' -Run {
    $id = 'CapturePlayerCheck.player'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'capture'
        Remove-TreeSafe $out
        $p = Invoke-Process $id 'python' @('-I', (Join-Path $PSScriptRoot 'capture_player_check.py'), '--exe', $AppExe, '--out', $out, '--port', '17398') 30
        $rep = Copy-PlayerReport $id (Join-Path $out 'capture_player_report.txt')
        $sum = Read-Summary $rep
        $res = if ($p.Code -ne 0) { 'FAIL' } else { $sum.Result }
        Add-Result $id $res $p.Minutes ("exit $($p.Code); $($sum.Line)") $rep
    }
}

# ---- Phase 5.2 player rows whose scripts take the GPU / input locks themselves (Tools\maker_session.py's helpers: the lock
# folder HS2NEXT_LOCK_DIR, the game gate of the local Tools\session_gate.json): the runner starts them without its -GpuLock
# (a lock it held would make them refuse with exit 2). Port 17396 (MakerSession.player, PerfCheck.player, AllCards.player;
# they never run together), 17392 (ModSweep.player).
# MakerSession.player (package K; opt-in, 65-75 min): python -I Tools\maker_session.py drives
# the app player over the bridge for 60 minutes (two SFW targets edited through every Maker list, their own coordinate
# re-applied, Studio item churn with undo / redo, a map per cycle, scene save / new / load and a character re-added every
# third cycle) under the game gate, the GPU / input locks, free RAM and a 75-min wall bound; samples the process's GPU memory
# (Windows counters), the working set, content.stats, app.perf and frame times every 30 s. Report <out>\summary.txt, first
# and last line "SUMMARY: <status>; <minutes> of 60 min, <n> cycle(s) done; KS1 ..., KS8 ..."; exit 0 pass, 1 a KS check
# failed or the run stopped (memory guard, device loss, error), 2 setup error / refused, 3 aborted by the game gate (WARN:
# repeat later).
Row 'MakerSession.player' player 'player,maker,ui,content' -OptIn -Run {
    $id = 'MakerSession.player'
    $script = Join-Path $PSScriptRoot 'maker_session.py'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $SpikeOut ('session\' + (Get-Date -Format 'yyyyMMdd-HHmm'))
        $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17396', '--minutes', '60', '--wall-min', '75') 80 $false
        $rep = Copy-PlayerReport $id (Join-Path $out 'summary.txt')
        $first = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Select-Object -First 1) -join '' } else { '' }
        $res = switch ($p.Code) { 0 { 'PASS' } 3 { 'WARN' } default { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a KS check failed or the run stopped)' } 2 { ' (setup error / refused)' } 3 { ' (aborted by the game gate: repeat later)' } 124 { ' (no exit: stopped)' } default { '' } }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($first) { $first } else { 'no summary' })) $rep
    }
}

# PerfCheck.player (package K; opt-in, about 10 min with PF9): python -I
# Tools\perf_player_check.py - PF1 part-change frame times (cold / warm / hovered, a mod head), PF2 the first redirected load,
# PF4 a list scroll, PF5 the hair part switch, PF6 the first draw-type switch, PF8 the LUT bake and frame timing, PF9 GPU memory
# of the 20 largest maps, UUA the UnloadUnusedAssets cost with 0 / 2 / 4 characters. A missed target is a measurement (MISSED,
# recorded), not a failure. Report <out>\perf_player_check_report.txt, first and last line "SUMMARY: N MET, M MISSED,
# K INFO, S SKIP, F FAIL"; exit 0 no FAIL, 1 a check failed, 2 setup error / refused, 3 aborted by the game gate.
Row 'PerfCheck.player' player 'player,maker,content' -OptIn -Run {
    $id = 'PerfCheck.player'
    $script = Join-Path $PSScriptRoot 'perf_player_check.py'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'perf'
        $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17396', '--only', 'PF1,PF2,PF4,PF5,PF6,PF8,PF9,UUA', '--timeout-min', '25') 30 $false
        $rep = Copy-PlayerReport $id (Join-Path $out 'perf_player_check_report.txt')
        $last = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() } | Select-Object -Last 1) -join '' } else { '' }
        $res = switch ($p.Code) { 0 { 'PASS' } 3 { 'WARN' } default { 'FAIL' } }
        Add-Result $id $res $p.Minutes ("exit $($p.Code); " + $(if ($last) { $last } else { 'no report' })) $rep
    }
}

# Startup.player (Phase 5.3 package P; opt-in, about 3-5 min): python -I Tools\startup_check.py -
# SU1 warm starts (batch and with the UI) to ping.appReady, SU2 the cold start of a fresh copy of the player folder, SU3 the app's
# own stages, SU5 the first UI frame, the phases of each start and QUIT (app.quit exit 0 within 10 s); the app player with
# -batchmode, the project's settings and cache, its own UserData per start, port 17611. The runner holds the GPU and input locks
# (the script takes none); the script applies the game gate. Report <out>\startup_check_report.txt, first and last line
# "SUMMARY: N MET, M MISSED, K INFO, F FAIL" (a missed target is a measurement, not a failure); exit 0 no FAIL, 1 a start
# failed, 2 setup error / refused, 3 aborted by the game gate (WARN).
Row 'Startup.player' player 'player,setup' -OptIn -Run {
    $id = 'Startup.player'
    $t0 = Get-Date
    $script = Join-Path $PSScriptRoot 'startup_check.py'
    if (-not (Test-Path -LiteralPath $script)) { Add-Result $id 'WARN' 0 'Tools\startup_check.py not present yet (not run)' $null }
    elseif (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    elseif (-not (Enter-WindowedLocks $id 20)) { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes "exit 126; the GPU / input locks stayed held for $LockWaitMin min" $null }
    else {
        $out = Join-Path $PlayerOut 'startup'
        try { $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17611') 20 $false }
        finally { Exit-WindowedLocks }
        $rep = Copy-PlayerReport $id (Join-Path $out 'startup_check_report.txt')
        $lines = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() }) } else { @() }
        $last = @($lines | Where-Object { $_ -like 'SUMMARY: *' } | Select-Object -Last 1) -join ''
        $res = switch ($p.Code) { 0 { 'PASS' } 3 { 'WARN' } default { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a start failed)' } 2 { ' (setup error / refused)' } 3 { ' (aborted by the game gate: repeat later)' } 124 { ' (no exit: stopped)' } default { '' } }
        $su = @($lines | Where-Object { $_ -match '^(SU\d|QUIT)\s+(MET|MISSED|INFO|FAIL)\b' } | ForEach-Object { $_.Trim() })
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($last) { $last } else { 'no report' }) + $(if ($su.Count) { '; ' + ($su -join '; ') } else { '' })) $rep
    }
}

# ModSweep.player (package W; whole suite only, about 1 min per row): the Studio item rows that
# once ended an Editor process (Tools\mod_sweep_player_rows.txt, a tracked list: the effects pack's truncated meshes and the
# load-count crashes; the item corpus's crash list is empty since the 5.2 fixes) added one by one in a fresh app player with item.add, deleted, app.quit (python -I
# Tools\mod_sweep_player.py; the game gate, the GPU / input locks, the System log's nvlddmkm events per row). Report
# Spike\out\modsweep\player\mod_sweep_player_report.txt ("RESULT rows=N added=N refused=N died=N hung=N ..." and the last
# line "SUMMARY: N PASS, M FAIL"); exit 0 no row ended a player, 1 some row did, 2 setup error (WARN when the row list is
# absent: nothing to add).
Row 'ModSweep.player' player 'player,items,content' -WholeSuite -Run {
    $id = 'ModSweep.player'
    $rows = Join-Path $PSScriptRoot 'mod_sweep_player_rows.txt'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Test-Path -LiteralPath $rows)) { Add-Result $id 'WARN' 0 'no Tools\mod_sweep_player_rows.txt: no row to add' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $SpikeOut 'modsweep\player'
        $p = Invoke-Process $id 'python' @('-I', (Join-Path $PSScriptRoot 'mod_sweep_player.py'), '--rows', $rows, '--exe', $AppExe, '--out', $out, '--port', '17392') 40 $false
        $rep = Copy-PlayerReport $id (Join-Path $out 'mod_sweep_player_report.txt')
        $lines = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() }) } else { @() }
        $result = @($lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1) -join ''
        $last = if ($lines.Count) { $lines[-1] } else { '' }
        $res = 'FAIL'
        if ($p.Code -eq 0 -and $last -match 'SUMMARY: (\d+) PASS, (\d+) FAIL') { $res = if ([int]$Matches[2] -eq 0) { 'PASS' } else { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a row ended a player)' } 2 { ' (setup error / refused)' } 124 { ' (no exit: stopped)' } default { '' } }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($result) { "$result; " } else { '' }) + $(if ($last) { $last } else { 'no report' })) $rep
    }
}

# AllCards.player (package K for package Q, l.531 / l.532; whole suite only, about 15-25 min):
# python -I Tools\allcards_player.py builds every file of the card folders (Tools\targets.json cards: the 957 cards and 27
# non-character files) once in the app player, graphics with the UI off, through scene.new / chara.load cycles with the drawn
# materials of the five part sets read in the same request; counts failures by class, managed exceptions, native crashes (the
# walk resumes at the next file in a new player, at most 5 restarts), device loss, the slots drawn with the URP/Lit fallback
# (unfitted shaders), the textures the texture check patched / refused, and the build times. No image; card files by count /
# walk index only. Takes the GPU and input locks itself and applies the game gate. Report <out>\allcards_player_report.txt,
# first and last line "SUMMARY: N PASS, M FAIL" (AP1-AP4 checks, AP5-AP7 info); exit 0 pass, 1 a check failed or the run
# stopped, 2 setup error / refused, 3 aborted by the game gate (WARN).
Row 'AllCards.player' player 'player,cards-full,chara-render,content' -WholeSuite -Run {
    $id = 'AllCards.player'
    $script = Join-Path $PSScriptRoot 'allcards_player.py'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $PlayerOut 'allcards'
        $p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe, '--project', $Project, '--out', $out, '--port', '17396', '--timeout-min', '45') 55 $false
        $rep = Copy-PlayerReport $id (Join-Path $out 'allcards_player_report.txt')
        $last = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() } | Select-Object -Last 1) -join '' } else { '' }
        $res = switch ($p.Code) { 0 { 'PASS' } 3 { 'WARN' } default { 'FAIL' } }
        $why = switch ($p.Code) { 0 { '' } 1 { ' (a check failed or the run stopped)' } 2 { ' (setup error / refused)' } 3 { ' (aborted by the game gate: repeat later)' } 124 { ' (no exit: stopped)' } default { '' } }
        $extra = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_ -match '^AP[1-7] ' } | ForEach-Object { ($_ -split ' ', 3)[2] }) -join '; ' } else { '' }
        Add-Result $id $res $p.Minutes ("exit $($p.Code)$why; " + $(if ($last) { $last } else { 'no report' }) + $(if ($extra) { '; ' + $extra } else { '' })) $rep
    }
}

# ModSweep.playerHeader (package W; whole suite only, about 5 min): the Studio item rows of every
# bundle whose UnityFS header names a Unity release type other than f (python -I Tools\mod_sweep_report.py header-rows: the scan
# cache only, about 15 s; 459 rows on the reference install), added in turn in one app player (python -I Tools\mod_sweep_player.py
# --one-player: a fresh player after a death, one new UserData per pass, so the crash quarantine refuses a bundle after its first
# death; the script takes the GPU / input locks and the game gate itself). Report Spike\out\modsweep\player_header\
# mod_sweep_player_report.txt ("RESULT rows=N added=N refused=N died=N ... firstDeaths=N repeatDeaths=N quitDeaths=N players=N");
# PASS no death, WARN only first-use deaths that the quarantine then refused (the known one is an open item), FAIL otherwise
# (a death after the first use, a device loss, a setup error, no exit).
Row 'ModSweep.playerHeader' player 'player,items,content' -WholeSuite -Run {
    $id = 'ModSweep.playerHeader'
    if (-not (Test-Path $AppExe)) { Add-Result $id 'FAIL' 0 'no app player (Builds\App\HS2Next.exe): run PlayerBuild.app' $null }
    elseif (-not (Wait-FreeRam)) { Add-Result $id 'FAIL' 30 'less than 8 GB of free RAM for 30 min' $null }
    else {
        $out = Join-Path $SpikeOut 'modsweep\player_header'
        New-Item -ItemType Directory -Force $out | Out-Null
        $rowsFile = Join-Path $out 'header_rows.txt'
        $h = Invoke-Process "$id.rows" 'python' @('-I', (Join-Path $PSScriptRoot 'mod_sweep_report.py'), 'header-rows', '--out', $rowsFile) 5 $false
        if ($h.Code -ne 0 -or -not (Test-Path -LiteralPath $rowsFile)) { Add-Result $id 'FAIL' $h.Minutes "header-rows exit $($h.Code)" $null }
        else {
            $p = Invoke-Process $id 'python' @('-I', (Join-Path $PSScriptRoot 'mod_sweep_player.py'), '--one-player', '--rows', $rowsFile, '--exe', $AppExe, '--out', $out, '--port', '17392') 30 $false
            $rep = Copy-PlayerReport $id (Join-Path $out 'mod_sweep_player_report.txt')
            $lines = if ($rep) { @(Get-Content -LiteralPath $rep -Encoding UTF8 | Where-Object { $_.Trim() }) } else { @() }
            $result = @($lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1) -join ''
            $kv = @{}; foreach ($m in [regex]::Matches($result, '(\w+)=(\d+)')) { $kv[$m.Groups[1].Value] = [int]$m.Groups[2].Value }
            $res = if ($p.Code -eq 0) { 'PASS' }
                   elseif ($p.Code -eq 1 -and $kv['firstDeaths'] -gt 0 -and $kv['repeatDeaths'] -eq 0 -and $kv['quitDeaths'] -eq 0 -and $kv['deviceLoss'] -eq 0) { 'WARN' }
                   else { 'FAIL' }
            $why = if ($res -eq 'WARN') { ' (first-use deaths, quarantined afterwards)' } else { '' }
            Add-Result $id $res ($h.Minutes + $p.Minutes) ("exit $($p.Code)$why; " + $(if ($result) { $result } else { 'no report' })) $rep
        }
    }
}

# ---- full group (-Full only). The corpus rows carry only 'cards-full' (ItemTest.corpus: 'items'), so a change selects
# them only when Tools\test_areas.json gives it cards-full (characters, import, card data, shared parts).
# Phase 5.2 (AX10 = 85 of 86 numbers equal on one commit): the all-card rows AllCards
# replaced stay as opt-in rows for targeted runs (ImportTest.full, CharaDocBuildTest.all, MakerCoreTest.all, MakerEditTest.all,
# CoordApply.all, LooseTextureTest.cards; CardImport.* above); MakerEditTest.fuzzfull runs in the whole suite only (AX3)
Row 'ImportTest.full'       full 'cards-full'             'ImportTest.RunFull' @() 'Spike\out\import\import_report_full.txt' -OptIn
Row 'CharaDocBuildTest.all' full 'cards-full'                    'CharaDocBuildTest.Run' @('-all') 'Spike\out\import\docbuild_report.txt' -OptIn -TimeoutMin 60
# Phase 4.0: the S1 baselines of every card (missing ones written, existing ones kept; re-take with
# -overwrite by hand after a deliberate delta) and S1 on every card
Row 'MakerCoreTest.baseline' full 'maker'                        'MakerCoreTest.RunBaseline' @('-all') 'Spike\out\maker\maker_baseline_report.txt' -TimeoutMin 150
Row 'MakerCoreTest.all'     full 'cards-full,maker'              'MakerCoreTest.RunS1' @('-all') 'Spike\out\maker\maker_s1_report.txt' -OptIn -TimeoutMin 150
# Phase 4.0 package C: S2 on every card (957 pairs within sex, cyclic) and the realization fuzz at its -Full size (200 x 60)
Row 'MakerEditTest.all'     full 'cards-full,maker'              'MakerEditTest.RunS2' @('-all') 'Spike\out\maker\maker_s2_report.txt' -OptIn -TimeoutMin 240
Row 'MakerEditTest.fuzzfull' full 'maker'                        'MakerEditTest.RunFuzz' @('-full') 'Spike\out\maker\maker_fuzz_report.txt' -WholeSuite -TimeoutMin 240
# Phase 4.6 package A: every .hs2n* file under the project's UserData, Spike\out and Cache read by the frozen reader (no error,
# no warning); CoordApply.all: every installed coordinate card (female onto BR-Chan, male onto HERO107) worn in mode All = a
# fresh build of the merged documents, saved as .hs2ncoord and reloaded byte-equal (bundles released every 10 files)
Row 'MakerFileTest.userfiles' full 'save,maker'                   'MakerFileTest.RunUserFiles' @() 'Spike\out\maker\maker_file_userfiles_report.txt' -TimeoutMin 30
Row 'CoordApply.all'        full 'cards-full,maker'              'MakerFileTest.RunCoordApply' @() 'Spike\out\maker\maker_file_coordapply_report.txt' -OptIn -TimeoutMin 60
# Phase 4.2 package L (GB10): the character cards whose slots use a loose-PNG texture (Spike/out/maker/loose_cards.txt), and
# S1 against the stored baselines with every difference explained as a loose texture slot or the Y10 bust gravity (the check
# before an S1 re-take of either cause; equal baselines give 0 differences)
Row 'LooseTextureTest.cards' full 'cards-full,maker'           'LooseTextureTest.RunCards' @() 'Spike\out\maker\loose_cards_report.txt' -OptIn -TimeoutMin 30
Row 'LooseTextureTest.s1explain' full 'cards-full,maker'       'LooseTextureTest.RunS1Explain' @('-all') 'Spike\out\maker\loose_s1_explain_report.txt' -OptIn -TimeoutMin 90
# Phase 4.3 package A (10 item 6; opt-in): S1's build path on every card against the stored
# baselines, every differing line classified (mesh, deco, rendHair-empty; other = FAIL) - the check before the 4.3 re-take
Row 'MakerHairTest.s1explain' full 'cards-full,maker'          'MakerHairTest.RunS1Explain' @('-all') 'Spike\out\maker\maker_hair_s1explain_report.txt' -OptIn -TimeoutMin 90
# Phase 4.4 (10 item 7; opt-in): the checks before an S1 re-take of the redirect. The redirect's
# explain builds every card with the redirect on and compares it with a snapshot of the same tree with both switches off
# (written first into Cache\maker\s1off by MakerCoreTest.RunBaseline -all -dir ... -sideloader off): every differing line
# classified (redirect.body, .bodyUncensor, .hairCopy, .kimono, .modCopy; other = FAIL), the lines that differ from the S1
# baselines with the switches off counted apart; the clothes explain compares the switches-off build with the S1 baselines
# (clothes.break, clothes.state; other = FAIL)
Row 'AssetRedirectTest.s1explain' full 'cards-full,maker' -OptIn -Run {
    $id = 'AssetRedirectTest.s1explain'
    $off = Join-Path $Project 'Cache\maker\s1off'
    $t0 = Get-Date
    $r0 = Invoke-Unity "$id.off" 'MakerCoreTest.RunBaseline' @('-all', '-overwrite', '-dir', $off, '-sideloader', 'off') $false 'Spike\out\maker\maker_baseline_report.txt' 150
    $s0 = Read-Summary $r0.Report
    if ((Judge $r0 $s0) -ne 'PASS') { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes "the switches-off snapshots: exit $($r0.Code); $($s0.Line)" $r0.Report; return }
    $r = Invoke-Unity $id 'AssetRedirectTest.RunS1Explain' @('-all', '-offDir', $off) $false 'Spike\out\maker\asset_redirect_s1explain_report.txt' 150
    $sum = Read-Summary $r.Report
    $line = "switches-off snapshots: $($s0.Line); explain: $($sum.Line)"; if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
    Add-Result $id (Judge $r $sum) ((Get-Date) - $t0).TotalMinutes $line $r.Report
}
Row 'MakerClothesTest.s1explain' full 'cards-full,maker'       'MakerClothesTest.RunS1Explain' @('-all') 'Spike\out\maker\maker_clothes_s1explain_report.txt' -OptIn -TimeoutMin 120
# Phase 4.5 (9.2, 10 item 7; opt-in): S1's build path on every card against the stored baselines,
# every differing line classified (acc.hairType, acc.alpha4, acc.visibility; other = FAIL) - the check before the 4.5 re-take
Row 'MakerAccessoriesTest.s1explain' full 'cards-full,maker'   'MakerAccessoriesTest.RunS1Explain' @('-all') 'Spike\out\maker\maker_accessories_s1explain_report.txt' -OptIn -TimeoutMin 120
# Phase 5.M (integrator T; opt-in): S1's build path on every card against the stored baselines, every differing line
# classified (mosaic.material, mosaic.mesh, policy.meMosaicSkip, policy.extSummary, policy.uncensorOff; other = FAIL) -
# the check before the 5.M re-take; the classes per card in maker\mosaic_s1explain_cards.tsv
Row 'MosaicExplainTest.s1explain' full 'cards-full,maker'      'MosaicExplainTest.RunS1Explain' @('-all') 'Spike\out\maker\mosaic_s1explain_report.txt' -OptIn -TimeoutMin 120
# Phase 5.2 package S (ST5; opt-in): ModShaderS1Explain - the S1 snapshots of the visual targets + 20, the cards whose
# MaterialEditor edits name a replaced shader (-me) and the -only cards against their baselines, every differing line
# classified (shader.tail, me.keywords; other = FAIL with the tokens before => after) - the check before the 5.2 re-take
Row 'ModShaderTest.s1explain' full 'cards-full,maker,shaders'  'ModShaderS1Explain.Run' @('-me') 'Spike\out\modshaders\mod_shader_s1explain_report.txt' -OptIn -TimeoutMin 60
# Phase 4.4 (packages R / R2): AR11 loads every asset name of every redirectable path through the rule, AR12 opens every serving
# copy of the census's 5 CAB-sharing paths at once (the CAB rewrite), RU3 measures the costs (its upgrade of a version 6 scan
# cache reads every zipmod manifest: about 5 min of the row); headless only (a graphics device once lost itself on a bad-mip
# mod texture in the sweep); in -Full instead of -Quick (a decision)
Row 'AssetRedirectTest.sweep' full 'content,chara-build'         'AssetRedirectTest.Run' @('-only', 'AR11+AR12+RU3') 'Spike\out\maker\asset_redirect_report.txt' -TimeoutMin 60
# MapTest.corpus: every map row (684) loaded and unloaded in play mode, in chunks of 100 maps
# per Editor process (-nographics); a map whose bundle crashes Unity is recorded by the test and skipped on the rerun
Row 'MapTest.corpus'        full 'maps' -ResultIds 'MapTest.corpus.*' -Run {
    for ($chunk = 0; $chunk -lt 7; $chunk++) {
        $id = "MapTest.corpus.$chunk"
        $rep = "Spike\out\maps\map_corpus_report_$chunk.txt"
        $a = @('-from', "$($chunk * 100)", '-limit', '100', '-tag', "$chunk")
        $min = 0.0
        for ($try = 1; $try -le 4; $try++) {
            $r = Invoke-Unity $id 'MapTest.RunCorpus' $a $false $rep 180 $true
            $min += $r.Minutes
            if ($r.Code -eq 0 -or $r.Code -eq 1 -or $r.Code -eq 124) { break }
            Write-Host "[run_tests] $id crashed (exit $($r.Code)); rerun $($try + 1) skips the recorded map"
        }
        $sum = Read-Summary $r.Report
        $line = $sum.Line; if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
        Add-Result $id (Judge $r $sum) $min $line $r.Report
    }
}
Row 'ItemTest.corpus'       full 'items' -ResultIds 'ItemTest.corpus.*' -Run {
    # M9 in chunks of 5,000 rows (the Editor's asset-path table crashes natively after some 10,000 bundle loads in one
    # process); a row whose bundle crashes Unity is recorded by the test and skipped on the rerun
    for ($chunk = 0; $chunk -lt 6; $chunk++) {
        $id = "ItemTest.corpus.$chunk"
        $rep = "Spike\out\items\item_corpus_report_$chunk.txt"
        $a = @('-from', "$($chunk * 5000)", '-limit', '5000', '-tag', "$chunk")
        $min = 0.0
        for ($try = 1; $try -le 4; $try++) {
            $r = Invoke-Unity $id 'ItemTest.RunCorpus' $a $false $rep 60
            $min += $r.Minutes
            if ($r.Code -eq 0 -or $r.Code -eq 1 -or $r.Code -eq 124) { break }
            Write-Host "[run_tests] $id crashed (exit $($r.Code)); rerun $($try + 1) skips the recorded bundle"
        }
        $sum = Read-Summary $r.Report
        $line = $sum.Line; if ($r.Code -ne 0) { $line = "exit $($r.Code); $line" }
        Add-Result $id (Judge $r $sum) $min $line $r.Report
    }
}
# ModSweepTest (Phase 5.2 package W; opt-in, a night run of hours): the mod-wide sweep.
# Tools\mod_sweep_report.py worklist writes the work list of every list row of the zipmod set into Spike\out\modsweep\<stamp>
# (SW1: its counts = Tools\survey\zipmod_content_census.txt); headless Unity processes of ModSweepTest.Run (each at most 4,000
# bundle loads: the Editor dies natively after about 10,000 opens in one process) resume until `status` says done, a row left
# in flight by a dead chunk process running alone next (bound 6 min: crash, or a load-count crash); then `report` writes
# report.tsv and summary.txt (the item / map classes from the newest corpus rows files, the HS2 evidence of
# Tools\survey\mod_sweep_hs2.tsv). PASS: the work list complete (SW1), every row done, no FAIL line among SW2 / SW4 / SW6 and
# SW7 PASS or OPEN (OPEN: the graphics pass or the player pass is not folded in). The committed copy of the summary
# (Tools\survey\mod_sweep_summary.txt) is written by hand (`report --summary`).
Row 'ModSweepTest'          full 'content,maker,items,maps' -OptIn -Run {
    $id = 'ModSweepTest'
    $t0 = Get-Date
    $tool = Join-Path $PSScriptRoot 'mod_sweep_report.py'
    $dir = Join-Path $SpikeOut "modsweep\$Stamp"
    $wl = @(& python -I $tool worklist --dir $dir 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $wl | Out-File (Join-Path $OutDir 'ModSweepTest.worklist.console.txt') -Encoding utf8
    $sw1 = @($wl | Where-Object { $_ -match 'SW1' } | Select-Object -Last 1) -join ''
    if ($code -ne 0) { Add-Result $id 'FAIL' ((Get-Date) - $t0).TotalMinutes "worklist exit $code; $sw1" $null; return }
    $procs = 0; $iso = 0; $lastSt = ''; $same = 0; $stop = ''
    while ($true) {
        if (((Get-Date) - $t0).TotalMinutes -gt 600) { $stop = 'the wall bound (600 min)'; break }
        if ($procs -ge 60) { $stop = '60 processes'; break }
        $st = (& python -I $tool status --dir $dir) -join ' '
        if ($st -like 'done*') { break }
        if ($st -eq $lastSt) { $same++ } else { $same = 0 }
        if ($same -ge 3) { $stop = "no progress in three processes ($st)"; break }
        $lastSt = $st
        # a row left by a dead chunk process runs alone next (the short bound); otherwise a chunk
        $curF = Join-Path $dir 'current.txt'
        $mode = if (Test-Path -LiteralPath $curF) { @((Get-Content -LiteralPath $curF -Raw) -split "`t")[1] } else { '' }
        $to = 60
        if ($mode -eq 'chunk') { $to = 6; $iso++ }
        $r = Invoke-Unity "$id.$procs" 'ModSweepTest.Run' @('-dir', $dir) $false $null $to
        $procs++
        if ($script:CompileFailed) { $stop = 'compile errors'; break }
        if ($r.Code -eq 2) { $stop = 'a setup error (exit 2)'; break }
        if ($r.Code -eq 125) { $stop = 'the Unity lock wait timed out'; break }
    }
    $rp = @(& python -I $tool report --dir $dir 2>&1 | ForEach-Object { "$_" })
    $rp | Out-File (Join-Path $OutDir 'ModSweepTest.report.console.txt') -Encoding utf8
    $sumF = Join-Path $dir 'summary.txt'
    $copied = $null
    if (Test-Path -LiteralPath $sumF) { $copied = Join-Path $OutDir 'ModSweepTest__summary.txt'; Copy-Item -LiteralPath $sumF -Destination $copied -Force }
    $lines = if ($copied) { @(Get-Content -LiteralPath $copied -Encoding UTF8) } else { @() }
    $sw7 = @($lines | Where-Object { $_ -match '^(PASS|FAIL|OPEN) SW7 ' } | Select-Object -First 1) -join ''
    $bad = @($lines | Where-Object { $_ -match '^FAIL SW\d' })
    $res = if (-not $stop -and $sw7 -match '^(PASS|OPEN) ' -and $bad.Count -eq 0) { 'PASS' } else { 'FAIL' }
    $txt = "$procs processes ($iso isolation runs)$(if ($stop) { "; stopped: $stop" }); $sw1; $(if ($sw7) { $sw7 } else { 'no SW7 line' })" +
        $(if ($bad.Count) { '; ' + ($bad -join '; ') } else { '' })
    Add-Result $id $res ((Get-Date) - $t0).TotalMinutes $txt $copied
}
# AllCards.render (Phase 5.2 AX5; the CardRender.female / .male rows before 5.2): CardImportTest -render over every card with a
# graphics device (document builds since the card path left the tests; the views and the creators' RT hashes stay in memory,
# nothing is written), the skip list of Tools\targets.json; a native crash resumes after the crashed card
Row 'AllCards.render'       full 'cards-full' -Graphics -ResultIds 'AllCards.render.*' -Run {
    Invoke-CardImport 'AllCards.render.female' $Targets.cards.female $true
    Invoke-CardImport 'AllCards.render.male' $Targets.cards.male $true
}

# ------------------------------------------------------------------ selection
# Group selection: headless always; graphics and player unless -Quick; full with -Full (its graphics rows not with -Quick).
function In-Mode($row) {
    $named = [bool]($Only | Where-Object { $_ -eq $row.Id })
    if ($row.OptIn -and -not $named) { return $false }
    # whole suite only: a -Full without -Area / -Changed narrowing (or a path that selects every row), or -Only naming it
    if ($row.WholeSuite -and -not $named -and -not $WholeSuite) { return $false }
    switch ($row.Group) {
        'headless' { return $true }
        'graphics' { return (-not $Quick) }
        'player' { return (-not $Quick) }
        'full' { return ($Full.IsPresent -and -not ($Quick -and $row.Graphics)) }
        # mono: with -Quick, or in any mode when -Only names Mono rows; never with -NoMono
        'mono' { return (-not $NoMono) -and ($Quick -or [bool]($Only | Where-Object { $_ -like 'Mono*' -and $row.Id -like $_ })) }
    }
    return $false
}

# Glob -> regex: '**/' any folders (or none), '**' anything, '*' and '?' within one folder; case-insensitive.
function Convert-Glob([string]$g) {
    $sb = [System.Text.StringBuilder]::new('^')
    $i = 0
    while ($i -lt $g.Length) {
        $c = $g[$i]
        if ($c -eq '*' -and $i + 1 -lt $g.Length -and $g[$i + 1] -eq '*') {
            if ($i + 2 -lt $g.Length -and $g[$i + 2] -eq '/') { [void]$sb.Append('(?:.*/)?'); $i += 3 } else { [void]$sb.Append('.*'); $i += 2 }
            continue
        }
        if ($c -eq '*') { [void]$sb.Append('[^/]*') }
        elseif ($c -eq '?') { [void]$sb.Append('[^/]') }
        else { [void]$sb.Append([regex]::Escape([string]$c)) }
        $i++
    }
    [void]$sb.Append('$')
    return [regex]::new($sb.ToString(), 'IgnoreCase')
}

# Changed paths (relative to the project, '/' separated) of a git revision or range.
function Get-ChangedPaths([string]$spec) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw '-Changed needs git' }
    $isRange = $spec -match '\.\.|\^!|\^@|\^-'
    $paths = @(& git -C $Project diff --name-only --no-renames --relative $spec 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "-Changed ${spec}: git diff failed (not a revision or range?)" }
    if (-not $isRange) {
        # a single revision: the working tree against it, untracked files included
        $paths += @(& git -C $Project ls-files --others --exclude-standard 2>$null)
    }
    return @($paths | ForEach-Object { "$_".Trim() } | Where-Object { $_ } | Sort-Object -Unique)
}

# Maps paths to areas / row patterns through Tools\test_areas.json. Returns @{ Areas; Rows; All; Files (per path); Unmapped; Shared }.
function Resolve-Changed([string[]]$paths) {
    $file = Join-Path $PSScriptRoot 'test_areas.json'
    $cfg = Get-Content -Raw -LiteralPath $file -Encoding UTF8 | ConvertFrom-Json
    $entries = @()
    foreach ($e in $cfg.paths) {
        foreach ($a in @($e.areas)) { if ($a -and -not $AreaTags.Contains($a)) { throw "test_areas.json: unknown area '$a' in '$($e.glob)'" } }
        $entries += [pscustomobject]@{ Glob = $e.glob; Re = (Convert-Glob $e.glob); Areas = @($e.areas | Where-Object { $_ }); Rows = @($e.rows | Where-Object { $_ })
            Shared = [bool]$e.shared; All = [bool]$e.all; Ignore = [bool]$e.ignore }
    }
    $sharedAdds = @($cfg.sharedAdds)
    foreach ($a in $sharedAdds) { if (-not $AreaTags.Contains($a)) { throw "test_areas.json: unknown sharedAdds area '$a'" } }
    $o = @{ Areas = [System.Collections.Generic.SortedSet[string]]::new(); Rows = [System.Collections.Generic.List[string]]::new(); All = $false
        Files = [System.Collections.Generic.List[object]]::new(); Unmapped = [System.Collections.Generic.List[string]]::new()
        Shared = [System.Collections.Generic.List[string]]::new(); Ignored = 0; Folders = 0 }
    foreach ($p0 in $paths) {
        $p = $p0.Replace('\', '/')
        if ($p.EndsWith('.meta')) {
            $p = $p.Substring(0, $p.Length - 5)
            # a folder's .meta (the folder exists, or it is gone and the name has no extension): known, no area, not listed
            $abs = Join-Path $Project $p
            if ((Test-Path -LiteralPath $abs -PathType Container) -or (-not (Test-Path -LiteralPath $abs) -and -not [IO.Path]::HasExtension($p))) { $o.Folders++; continue }
        }
        $hits = @($entries | Where-Object { $_.Re.IsMatch($p) })
        if ($hits.Count -eq 0) { $o.Unmapped.Add($p0); $o.Files.Add([pscustomobject]@{ Path = $p0; Areas = @(); Rows = @(); Shared = $false; All = $false; Unmapped = $true }); continue }
        if ($hits | Where-Object { $_.Ignore }) { $o.Ignored++; continue }
        $fa = [System.Collections.Generic.SortedSet[string]]::new(); $fr = @(); $sh = $false; $all = $false
        foreach ($h in $hits) {
            foreach ($a in $h.Areas) { [void]$fa.Add($a) }
            $fr += $h.Rows
            if ($h.Shared) { $sh = $true }
            if ($h.All) { $all = $true }
        }
        if ($sh) { foreach ($a in $sharedAdds) { [void]$fa.Add($a) }; $o.Shared.Add($p0) }
        if ($all) { $o.All = $true }
        foreach ($a in $fa) { [void]$o.Areas.Add($a) }
        foreach ($r in $fr) { if (-not $o.Rows.Contains($r)) { $o.Rows.Add($r) } }
        $o.Files.Add([pscustomobject]@{ Path = $p0; Areas = @($fa); Rows = @($fr | Select-Object -Unique); Shared = $sh; All = $all; Unmapped = $false })
    }
    return $o
}

$SelAreas = [System.Collections.Generic.SortedSet[string]]::new()
$SelRows = [System.Collections.Generic.List[string]]::new()
$SelAll = $false
$SelActive = [bool]$Area -or [bool]$Changed
$ChangedInfo = $null
# set below once -Changed is resolved: a -Full that is not narrowed by -Area / -Changed (or whose changes select every row)
$WholeSuite = $false
foreach ($a in @($Area)) {
    if (-not $a) { continue }
    if (-not $AreaTags.Contains($a)) { throw "-Area: unknown area '$a' (known: $($AreaTags.Keys -join ', '))" }
    [void]$SelAreas.Add($a)
}
if ($Changed) {
    $cp = Get-ChangedPaths $Changed
    $ChangedInfo = Resolve-Changed $cp
    $ChangedInfo.PathCount = $cp.Count
    foreach ($a in $ChangedInfo.Areas) { [void]$SelAreas.Add($a) }
    foreach ($r in $ChangedInfo.Rows) { $SelRows.Add($r) }
    if ($ChangedInfo.All) { $SelAll = $true }
}
$WholeSuite = $Full.IsPresent -and (-not $SelActive -or $SelAll)

# Why a row is in the area selection ('' = not selected).
function Selection-Reason($row) {
    if (-not $SelActive -or $SelAll) { return 'all' }
    $hit = @($row.Tags | Where-Object { $SelAreas.Contains($_) })
    if ($hit.Count) { return ($hit -join ',') }
    foreach ($p in $SelRows) { if ($row.Id -like $p) { return "row $p" } }
    return ''
}

$Selected = @($Rows | Where-Object { (In-Mode $_) -and (Selection-Reason $_) -and (Want $_.Id) })
$OutsideMode = @($Rows | Where-Object { -not (In-Mode $_) -and $SelActive -and (Selection-Reason $_) -and (Want $_.Id) })
# Development inputs (Phase 5.4, PQ13): a selected row whose declared input is absent is not started; it reports WARN
# "development input absent: <path>" at its place in the table, and the summary counts these rows on a line of their own.
$DevAbsent = [ordered]@{}
foreach ($row in $Selected) { $miss = @(Get-AbsentDevInputs $row); if ($miss.Count) { $DevAbsent[$row.Id] = $miss } }
function Add-DevAbsent($row) { Add-Result $row.Id 'WARN' 0 ('development input absent: ' + ($DevAbsent[$row.Id] -join ', ')) $null }

$selText = @()
if ($Area) { $selText += "-Area $($Area -join ',')" }
if ($Changed) {
    $selText += "-Changed $Changed ($($ChangedInfo.PathCount) paths, $($ChangedInfo.Ignored) documentation" +
        $(if ($ChangedInfo.Folders) { ", $($ChangedInfo.Folders) folder metas" } else { '' }) +
        $(if ($ChangedInfo.Unmapped.Count) { ", $($ChangedInfo.Unmapped.Count) unmapped" } else { '' }) + ')'
}

# Last known minutes per row from the newest summaries that list it (rows with several results: their sum).
function Get-LastDurations {
    $d = @{}
    if (-not (Test-Path $TestsOut)) { return $d }
    $sums = @(Get-ChildItem -LiteralPath $TestsOut -Directory | Where-Object { $_.Name -match '^\d{8}-\d{4}$' } | Sort-Object Name -Descending)
    foreach ($s in $sums) {
        $f = Join-Path $s.FullName 'summary.md'
        if (-not (Test-Path $f)) { continue }
        $tab = @{}
        foreach ($l in Get-Content -LiteralPath $f -Encoding UTF8) {
            if ($l -match '^\| (.+?) \| (PASS|FAIL|WARN) \| ([\d.]+) \|') { $tab[$Matches[1]] = @([double]$Matches[3], $Matches[2]) }
        }
        foreach ($row in $Rows) {
            if ($d.ContainsKey($row.Id)) { continue }
            $m = @($tab.Keys | Where-Object { $_ -like $row.ResultIds })
            if ($m.Count -eq 0) { continue }
            $min = ($m | ForEach-Object { $tab[$_][0] } | Measure-Object -Sum).Sum
            $res = if ($m | Where-Object { $tab[$_][1] -eq 'FAIL' }) { 'FAIL' } elseif ($m | Where-Object { $tab[$_][1] -eq 'WARN' }) { 'WARN' } else { 'PASS' }
            $d[$row.Id] = [pscustomobject]@{ Minutes = $min; Stamp = $s.Name; Result = $res }
        }
    }
    return $d
}

function Write-Selection {
    $mode = if ($Quick -and $Full) { '-Quick -Full' } elseif ($Quick) { '-Quick' } elseif ($Full) { '-Full' } else { 'default' }
    Write-Host ("[run_tests] mode {0}{1}{2}" -f $mode, $(if ($selText) { '; ' + ($selText -join '; ') } else { '' }), $(if ($Only) { "; -Only $($Only -join ',')" } else { '' }))
    if ($ChangedInfo) {
        $files = $ChangedInfo.Files
        $show = if ($files.Count -le 30) { $files } else { @($files | Where-Object { $_.Unmapped -or $_.Shared -or $_.All }) }
        foreach ($f in $show) {
            $what = if ($f.Unmapped) { 'UNMAPPED (add a glob to Tools\test_areas.json)' } else {
                @($(if ($f.All) { 'all rows' }) ) + @($f.Areas) + @($f.Rows | ForEach-Object { "row $_" }) -join ', ' }
            if (-not $what) { $what = '(known, no area)' }
            Write-Host ("  {0} -> {1}{2}" -f $f.Path, $what, $(if ($f.Shared) { '  [shared part]' } else { '' }))
        }
        if ($files.Count -gt 30) { Write-Host "  ($($files.Count - $show.Count) more paths not shown: no shared part, all mapped)" }
        Write-Host ("  areas: {0}{1}{2}" -f $(if ($SelAreas.Count) { ($SelAreas -join ', ') } else { 'none' }),
            $(if ($SelRows.Count) { "; rows: $($SelRows -join ', ')" } else { '' }),
            $(if ($ChangedInfo.Shared.Count) { "; shared-part rule fired (paths: $($ChangedInfo.Shared.Count)): + chara-render, cards-full" } else { '' }))
        if ($SelAll) { Write-Host '  a path selects every row (Packages / ProjectSettings)' }
    }
}

if ($List) {
    Write-Selection
    $dur = Get-LastDurations
    Write-Host ''
    Write-Host ("{0,-26} {1,-9} {2,-50} {3,7}  {4}" -f 'test', 'group', 'areas', 'last', 'last run')
    $total = 0.0; $unknown = @(); $monoJob = @{ scene = 0.0; rest = 0.0 }
    foreach ($row in $Selected) {
        $d = $dur[$row.Id]
        # the mono rows run in two background jobs beside the Unity rows: their minutes are not part of the total
        if ($d -and $row.Group -eq 'mono') { $monoJob[$(if ($row.MonoSuite -eq 'scene') { 'scene' } else { 'rest' })] += $d.Minutes }
        elseif ($d) { $total += $d.Minutes }
        else { $unknown += $row.Id }
        $why = Selection-Reason $row
        $tags = ($row.Tags -join ',') + $(if ($why -like 'row *') { " ($why)" } else { '' })
        Write-Host ("{0,-26} {1,-9} {2,-50} {3,7}  {4}" -f $row.Id, $row.Group, $tags, $(if ($d) { '{0:N1}' -f $d.Minutes } else { '-' }),
            $(if ($d) { "$($d.Stamp) $($d.Result)" } else { '' }))
        if ($DevAbsent.Contains($row.Id)) { Write-Host ("{0,-26} WARN, not started here: development input absent: {1}" -f '', ($DevAbsent[$row.Id] -join ', ')) -ForegroundColor Yellow }
    }
    if ($DevAbsent.Count) { Write-Host ("[run_tests] development inputs absent: {0} of these rows would report WARN and not start" -f $DevAbsent.Count) -ForegroundColor Yellow }
    if (-not $TargetsFile -or (Split-Path -Leaf $TargetsFile) -ne 'targets.json') { Write-Host ("[run_tests] test targets: {0}" -f $(if ($TargetsFile) { 'Tools\targets.example.json (no Tools\targets.json)' } else { 'none (no Tools\targets.json, no Tools\targets.example.json)' })) -ForegroundColor Yellow }
    $monoRows = @($Selected | Where-Object { $_.Group -eq 'mono' })
    Write-Host ("[run_tests] {0} rows; last known total {1:N1} min{2}{3}" -f $Selected.Count, $total,
        $(if ($monoRows.Count) { " of Unity / player rows; the $($monoRows.Count) mono rows run in the background (last known: scene job {0:N1} min, other job {1:N1} min)" -f $monoJob.scene, $monoJob.rest } else { '' }),
        $(if ($unknown.Count) { " (no duration yet: $($unknown -join ', '))" } else { '' }))
    $bm = @($Selected | Where-Object { Is-BatchMember $_ })
    if ($bm.Count -ge 2) { Write-Host ("[run_tests] HeadlessBatch: {0} of these rows run in one Unity process (their last minutes above come from batched or single runs)" -f $bm.Count) }
    if ($OutsideMode.Count) {
        Write-Host ("[run_tests] also selected by area but outside this mode: {0}" -f (($OutsideMode | ForEach-Object {
            "$($_.Id) [$($_.Group)$(if ($_.Group -eq 'full' -and $_.Graphics) { ', not with -Quick' })]" }) -join ', '))
    }
    exit 0
}

if ($SelActive) { Write-Selection }
if ($Selected.Count -eq 0) {
    Write-Host '[run_tests] no row selected; nothing to run' -ForegroundColor Yellow
    exit 0
}
if ($OutsideMode.Count) { Write-Host ("[run_tests] selected by area but outside this mode (not run): {0}" -f (($OutsideMode | ForEach-Object { $_.Id }) -join ', ')) -ForegroundColor Yellow }

# ------------------------------------------------------------------ run
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (-not $Quick) { New-Item -ItemType Directory -Force $PlayerOut | Out-Null }
$suiteStart = Get-Date
# the display driver's resets so far (System log, nvlddmkm event 153): the summary's graphics line counts the new ones
$DriverResetsBefore = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; Id = 153 } -ErrorAction SilentlyContinue).Count

# Offline compile check, once (unity_batch would otherwise run it before every row): a warning and a note, never a stop.
# (Not for the mono rows and LeakCheck alone: they start no Unity.)
if (-not $NoOfflineCompile -and @($Selected | Where-Object { $_.Group -ne 'mono' -and $_.Id -ne 'LeakCheck' }).Count -gt 0) {
    $oc = Join-Path $PSScriptRoot 'offline_compile.py'
    if ((Test-Path $oc) -and (Get-Command python -ErrorAction SilentlyContinue)) {
        Write-Host ("[run_tests] {0} offline compile check (Tools\offline_compile.py)" -f (Get-Date -Format 'HH:mm:ss'))
        $ocOut = @(& python -I $oc -q 2>&1 | ForEach-Object { "$_" })
        $ocCode = $LASTEXITCODE
        $ocOut | Out-File (Join-Path $OutDir 'offline_compile.console.txt') -Encoding utf8
        $ocLast = @($ocOut | Select-Object -Last 1) -join ''
        if ($ocCode -eq 0) { Write-Host "[run_tests]          $ocLast" }
        else {
            Write-Host "[run_tests] WARNING: offline compile exit $ocCode ($ocLast); see offline_compile.console.txt" -ForegroundColor Yellow
            $Notes.Add("offline compile check: exit $ocCode, $ocLast (offline_compile.console.txt)")
        }
    }
}

# The plain-Mono suites: two background jobs (Mono.scene alone; the others one after the other), started before the
# Unity rows and collected after them. Each job runs Tools\mono_tests.ps1 -Suite <suite> and returns per suite the exit
# code, the minutes and the console file. The jobs never see the fuzz switch (it is set only around SceneModelTest).
$MonoSel = @($Selected | Where-Object { $_.Group -eq 'mono' -and -not $DevAbsent.Contains($_.Id) })
$MonoJobs = @()
$MonoScene = [bool]($MonoSel | Where-Object { $_.MonoSuite -eq 'scene' })
if ($MonoSel.Count) {
    $jobBody = {
        param([string]$tools, [string]$outDir, [string[]]$suites)
        if ($env:HS2N_APPEARANCE_FUZZ -in 'smoke', 'off') { Remove-Item Env:HS2N_APPEARANCE_FUZZ }
        foreach ($s in $suites) {
            $t0 = Get-Date
            $console = Join-Path $outDir "Mono.$s.console.txt"
            $code = -1
            try {
                & (Join-Path $tools 'mono_tests.ps1') -Suite $s *>&1 | ForEach-Object { "$_" } | Out-File -FilePath $console -Encoding utf8
                $code = $LASTEXITCODE
            } catch { Add-Content -LiteralPath $console -Value ("mono_tests.ps1 threw: " + $_) -Encoding utf8 }
            # the Python self-tests of the suite's area (Phase 5.3): TK5 with the bridge suite, UP9 with the content suite
            $self = @()
            $py = @()
            if ($s -eq 'bridge') { $py = @(, @('TK5', 'hs2n_bridge.py', '--self-test')) }
            elseif ($s -eq 'content') { $py = @(@('UP9', 'player_launch_audit.py'), @('UP9 self-test', 'player_launch_audit.py', '--self-test')) }
            foreach ($c in $py) {
                $out = @(& python -I (Join-Path $tools $c[1]) @($c | Select-Object -Skip 2) 2>&1 | ForEach-Object { "$_" })
                $pc = $LASTEXITCODE
                Add-Content -LiteralPath $console -Value (@("", "== $($c[0]): python -I Tools\$($c[1]) $(@($c | Select-Object -Skip 2) -join ' ') (exit $pc)") + $out) -Encoding utf8
                $sl = @($out | Where-Object { $_ -like 'SUMMARY: *' } | Select-Object -Last 1) -join ''
                $self += [pscustomobject]@{ Name = $c[0]; Code = $pc; Summary = $(if ($sl) { $sl } else { 'no SUMMARY line' }) }
            }
            [pscustomobject]@{ Suite = $s; Code = $code; Minutes = ((Get-Date) - $t0).TotalMinutes; Console = $console; SelfTests = $self }
        }
    }
    $sceneSuites = @($MonoSel | Where-Object { $_.MonoSuite -eq 'scene' } | ForEach-Object { $_.MonoSuite })
    $restSuites = @($MonoSel | Where-Object { $_.MonoSuite -ne 'scene' } | ForEach-Object { $_.MonoSuite })
    foreach ($set in @(, $sceneSuites) + @(, $restSuites)) {
        if ($set.Count -eq 0) { continue }
        Write-Host ("[run_tests] {0} {1,-26} background job: mono_tests.ps1 -Suite {2}" -f (Get-Date -Format 'HH:mm:ss'), ('Mono.' + ($set -join '/')), ($set -join ', '))
        $MonoJobs += Start-Job -Name ('hs2n_mono_' + ($set -join '_')) -ScriptBlock $jobBody -ArgumentList $PSScriptRoot, $OutDir, $set
    }
}

# HeadlessBatch: the members run as one batch at the place of the first member (two or more members selected; one member
# selected = its own process as before)
$BatchMembers = @($Selected | Where-Object { (Is-BatchMember $_) -and -not $DevAbsent.Contains($_.Id) })
if ($BatchMembers.Count -lt 2) { $BatchMembers = @() }
$BatchIds = @($BatchMembers | ForEach-Object { $_.Id })
foreach ($row in $Selected) {
    if ($row.Group -eq 'mono') { continue }   # (the background jobs)
    if ($DevAbsent.Contains($row.Id)) { Add-DevAbsent $row; continue }   # (not started: a development input is absent)
    if (-not (Want $row.Id)) { continue }   # (after a compile failure)
    if ($BatchIds.Count -and $row.Id -in $BatchIds) {
        if (-not $BatchDone) { Invoke-HeadlessBatch $BatchMembers }
        continue
    }
    # SceneModelTest leaves the appearance fuzz out while Mono.scene runs the default size beside it (Phase 5.2, LQ16)
    $fuzzOff = $row.Id -eq 'SceneModelTest' -and $MonoScene
    $keepFuzz = $env:HS2N_APPEARANCE_FUZZ
    if ($fuzzOff) { $env:HS2N_APPEARANCE_FUZZ = 'off' }
    try {
        if ($row.Run) { & $row.Run }
        else { Invoke-Simple $row.Id $row.Method $row.MethodArgs $row.Graphics $row.Report $row.NoQuit $row.TimeoutMin }
    }
    finally {
        if ($fuzzOff) {
            if ($null -eq $keepFuzz) { Remove-Item Env:HS2N_APPEARANCE_FUZZ -ErrorAction SilentlyContinue } else { $env:HS2N_APPEARANCE_FUZZ = $keepFuzz }
            $last = $Results | Where-Object { $_.Test -eq 'SceneModelTest' } | Select-Object -Last 1
            if ($last) { $last.Numbers += '; appearance fuzz off (Mono.scene runs it at 2,000 x 200)' }
        }
    }
}

# The mono rows: wait for the jobs (at most 60 min after the Unity rows), then one result row per suite.
if ($MonoJobs.Count) {
    Write-Host ("[run_tests] {0} waiting for the mono background jobs" -f (Get-Date -Format 'HH:mm:ss'))
    $null = Wait-Job -Job $MonoJobs -Timeout 3600
    $monoOut = @{}
    foreach ($j in $MonoJobs) {
        if ($j.State -eq 'Running') { Stop-Job -Job $j }
        foreach ($o in @(Receive-Job -Job $j -ErrorAction SilentlyContinue)) { if ($o -and $o.PSObject.Properties['Suite']) { $monoOut[$o.Suite] = $o } }
        Remove-Job -Job $j -Force
    }
    foreach ($row in $MonoSel) {
        $o = $monoOut[$row.MonoSuite]
        if (-not $o) { Add-Result $row.Id 'FAIL' 0 'no result from the background job (timeout after 60 min or the job failed)' $null; continue }
        $lines = if (Test-Path -LiteralPath $o.Console) { @(Get-Content -LiteralPath $o.Console -Encoding UTF8) } else { @() }
        # a suite that runs its test exe more than once (plugins: the loader's namespace, then the sample's) writes one TOTAL
        # line per run: the key number is their sum with each run's line
        $tots = @($lines | Where-Object { $_ -match '^TOTAL passed \d+, failed \d+' })
        $tot = if ($tots.Count -gt 1) {
            $np = 0; $nf = 0
            foreach ($t in $tots) { if ($t -match '^TOTAL passed (\d+), failed (\d+)') { $np += [int]$Matches[1]; $nf += [int]$Matches[2] } }
            "TOTAL passed $np, failed $nf ($($tots.Count) runs: " + (($tots | ForEach-Object { $_ -replace '^TOTAL ', '' }) -join '; ') + ')'
        } else { @($tots | Select-Object -Last 1) -join '' }
        $res = 'FAIL'
        if ($o.Code -eq 0 -and $tot -match '^TOTAL passed \d+, failed 0\b') { $res = 'PASS' }
        $txt = if ($tot) { $tot } elseif ($lines | Where-Object { $_ -like 'BUILD FAILED*' }) { (@($lines | Where-Object { $_ -like 'BUILD FAILED*' }) | Select-Object -First 1) } else { 'no TOTAL line' }
        if ($o.Code -ne 0) { $txt = "exit $($o.Code); $txt" }
        # the Python self-tests of the suite (Phase 5.3: TK5, UP9): their SUMMARY lines; a non-zero exit fails the row
        foreach ($st in @($o.SelfTests)) {
            if (-not $st) { continue }
            $txt += "; $($st.Name): $($st.Summary)$(if ($st.Code -ne 0) { " (exit $($st.Code))" })"
            if ($st.Code -ne 0) { $res = 'FAIL' }
        }
        Add-Result $row.Id $res $o.Minutes $txt $o.Console
    }
}
foreach ($row in @($Selected | Where-Object { $_.Group -eq 'mono' -and $DevAbsent.Contains($_.Id) })) { Add-DevAbsent $row }

# ------------------------------------------------------------------ summary
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# HS2Next test suite $Stamp")
[void]$sb.AppendLine()
$mode = @(); if ($Quick) { $mode += '-Quick' }; if ($Full) { $mode += '-Full' }; $mode += $selText; if ($Only) { $mode += "-Only $($Only -join ',')" }
[void]$sb.AppendLine("Mode: $(if ($mode) { $mode -join ' ' } else { 'default (headless + graphics + player)' }); wall time $([math]::Round(((Get-Date) - $suiteStart).TotalMinutes, 1)) min; reports copied next to this file.")
# the graphics API, the device losses of this suite's Unity logs (887a0005 / 887a0006 / device removed) and the driver resets
# (Phase 5.2). A row that lost the device: re-run it with -Only (the same API, then -Gfx d3d11),
# both bounded, under the input lock; two losses of one row in one week -> its Row line gets the API.
$logDir = if ($env:HS2N_LOG_DIR) { $env:HS2N_LOG_DIR } else { Join-Path $env:SystemDrive 'tmp\hs2next_logs' }
$lost = @(Get-ChildItem -LiteralPath $logDir -Filter "suite_${Stamp}_*.log" -ErrorAction SilentlyContinue |
    Where-Object { Select-String -LiteralPath $_.FullName -Pattern '887a0005|887a0006|device removed|DXGI_ERROR_DEVICE_REMOVED' -Quiet } |
    ForEach-Object { $_.BaseName.Substring("suite_${Stamp}_".Length) })
$resets = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; Id = 153 } -ErrorAction SilentlyContinue).Count - $DriverResetsBefore
[void]$sb.AppendLine()
[void]$sb.AppendLine("Graphics: $(if ($Gfx) { $Gfx.ToUpper() + ' (-Gfx)' } else { 'D3D12 (Unity default)' }), device losses $($lost.Count)$(if ($lost.Count) { ' (rows ' + ($lost -join ', ') + ')' }), driver resets $resets")
# the inputs of a public checkout (Phase 5.4): which targets file, the rows not started for an absent development input
[void]$sb.AppendLine()
[void]$sb.AppendLine("Test targets: $(if (-not $TargetsFile) { 'none (no Tools\targets.json, no Tools\targets.example.json)' } elseif ((Split-Path -Leaf $TargetsFile) -eq 'targets.json') { 'Tools\targets.json' } else { 'Tools\targets.example.json (no Tools\targets.json)' }); development inputs absent: $($DevAbsent.Count) row(s) WARN, not started$(if ($DevAbsent.Count) { ' - ' + (($DevAbsent.Keys | ForEach-Object { "$_ ($($DevAbsent[$_] -join ', '))" }) -join '; ') })")
if ($ChangedInfo) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Areas from -Changed: $(if ($SelAreas.Count) { $SelAreas -join ', ' } else { 'none' })$(if ($SelRows.Count) { "; rows $($SelRows -join ', ')" })$(if ($ChangedInfo.Shared.Count) { '; shared-part rule fired' })$(if ($ChangedInfo.Unmapped.Count) { "; unmapped paths: $($ChangedInfo.Unmapped -join ', ')" })")
}
if ($OutsideMode.Count) { [void]$sb.AppendLine(); [void]$sb.AppendLine("Selected by area but outside this mode (not run): $(($OutsideMode | ForEach-Object { $_.Id }) -join ', ')") }
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Test | Result | Minutes | Key numbers |')
[void]$sb.AppendLine('|---|---|---:|---|')
foreach ($r in $Results) { [void]$sb.AppendLine("| $($r.Test) | $($r.Result) | $([math]::Round($r.Minutes, 1)) | $($r.Numbers -replace '\|', '/') |") }
$imports = @($Results | Where-Object { $_.PSObject.Properties['Import'] -and $_.Import })
if ($imports.Count -gt 0) {
    $shaders = @{}; $guardCards = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $imports) {
        foreach ($kv in $r.Import.shaders.GetEnumerator()) {
            $key = $kv.Key
            if (-not $shaders.ContainsKey($key)) { $shaders[$key] = @{} }
            $shaders[$key][$r.Test] = $kv.Value
        }
        foreach ($c in $r.Import.guardCards) { $guardCards.Add("$($r.Test): $c") }
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Shaders without a fitted URP replacement (materials / cards per run)')
    [void]$sb.AppendLine()
    if ($shaders.Count -eq 0) { [void]$sb.AppendLine('none') }
    else {
        [void]$sb.AppendLine('| Shader | Origin | Runs |')
        [void]$sb.AppendLine('|---|---|---|')
        foreach ($k in ($shaders.Keys | Sort-Object)) {
            $runs = ($shaders[$k].GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key) $($_.Value[0])/$($_.Value[1])" }) -join '; '
            $origin = switch ($VanillaShaders[$k]) { 'chara' { 'vanilla chara' } 'studio' { 'vanilla studio/map' } default { 'mod only' } }
            [void]$sb.AppendLine("| $k | $origin | $runs |")
        }
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Cards with patched textures (texture.badmip: the texture check or the guard)')
    [void]$sb.AppendLine()
    if ($guardCards.Count -eq 0) { [void]$sb.AppendLine('none') } else { foreach ($c in $guardCards) { [void]$sb.AppendLine("- $c") } }
    $fl = @($imports | ForEach-Object { $t = $_.Test; $_.Import.failLines | ForEach-Object { "${t}: $_" } })
    if ($fl.Count -gt 0) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('## Import failures by class')
        [void]$sb.AppendLine()
        foreach ($l in $fl) { [void]$sb.AppendLine("- $l") }
    }
}
if ($VanillaShaders.Count -eq 0 -and @($Results | Where-Object { $_.Test -like 'CardImport*' -or $_.Test -like 'AllCards*' -or $_.Test -eq 'CharaRenderTest' }).Count) {
    $Notes.Add('no Tools\survey\shader_census.tsv (a development input): unfitted shaders were not split into vanilla and mod shaders, so a vanilla character shader without a fitted replacement did not FAIL a row')
}
if ($Notes.Count -gt 0) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Notes')
    [void]$sb.AppendLine()
    foreach ($n in $Notes) { [void]$sb.AppendLine("- $n") }
}
[IO.File]::WriteAllText((Join-Path $OutDir 'summary.md'), $sb.ToString(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host ("{0,-26} {1,-5} {2,7}  {3}" -f 'test', 'res', 'min', 'key numbers')
foreach ($r in $Results) {
    $n = if ($r.Numbers.Length -gt 150) { $r.Numbers.Substring(0, 147) + '...' } else { $r.Numbers }
    Write-Host ("{0,-26} {1,-5} {2,7:N1}  {3}" -f $r.Test, $r.Result, $r.Minutes, $n)
}
$failCount = @($Results | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Host ("[run_tests] graphics: {0}, device losses {1}{2}, driver resets {3}" -f $(if ($Gfx) { $Gfx.ToUpper() } else { 'D3D12' }), $lost.Count,
    $(if ($lost.Count) { ' (' + ($lost -join ', ') + ')' } else { '' }), $resets)
if ($DevAbsent.Count) { Write-Host ("[run_tests] development inputs absent: {0} row(s) WARN, not started ({1})" -f $DevAbsent.Count, ($DevAbsent.Keys -join ', ')) -ForegroundColor Yellow }
Write-Host ("[run_tests] {0} tests, {1} FAIL; summary: {2}" -f $Results.Count, $failCount, (Rel (Join-Path $OutDir 'summary.md')))
exit $(if ($failCount -gt 0) { 1 } else { 0 })
