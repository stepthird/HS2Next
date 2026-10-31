# Full TypeTree scan (`typetree_scan.py`)

Read the TypeTree of every asset bundle of an HS2 install
(all of `abdata/**/*.unity3d` plus every `.unity3d` inside every `mods/**/*.zipmod` / `*.zip`)
and cache the results, so that the Compat stubs and the class / shader censuses come from the
complete set instead of a sample. Clean room: only bundle metadata is read (TypeTrees, MonoScript
names, shader names, object type counts); no game code is used.

Requirements: Python 3.14 with UnityPy (tested with 1.25.4). Run with `python -I`
(isolated mode ignores `PYTHONUTF8`; the script makes its own output UTF-8).

## Usage

```
# full scan (resumable; safe to interrupt and restart with the same command)
python -I Tools/typetree_scan.py scan "<HS2 install>" [--out Cache/typetree] [--workers 5] [--big-workers 1]

# test on a subset
python -I Tools/typetree_scan.py scan "<HS2 install>" --out "<scratch folder>" --include "mods/*Tunnel*" --limit 30
python -I Tools/typetree_scan.py scan "<HS2 install>" --out "<scratch folder>" --file-list my_relpaths.txt

# progress of a running (or finished) scan; does not touch the scan
python -I Tools/typetree_scan.py status [--out Cache/typetree]

# merge whatever has finished (works on partial scans; the scan also merges when it ends)
python -I Tools/typetree_scan.py merge [--out Cache/typetree] [--survey-dir Tools/survey]
```

Options of `scan`:

- `--workers N` processes for normal sources, `--big-workers N` processes for sources whose
  largest bundle is at least `--big-mb` (default 300 MB). UnityPy decompresses a whole bundle in
  memory (about 2.6x the bundle size at peak: 6.7 GB for a 2.6 GB map bundle), so the big pool
  should stay at 1 unless there is a lot of free RAM.
- `--include` / `--exclude` glob on the relpath (case-insensitive, repeatable), `--file-list`
  scans exactly the listed relpaths (one per line, in that order), `--limit N` keeps the first N.
- `--tasks-per-child` recycles worker processes (default 40) to keep memory from creeping.
- `--no-merge` skips the automatic merge at the end.

Long runs on Windows: start the scan detached (for example PowerShell
`Start-Process python -ArgumentList ... -WindowStyle Hidden -RedirectStandardOutput ...`), not as a
background job of a tool that may kill child processes. Check it with `status`.

## Cache layout (`--out`, default `<repo>/Cache/typetree`, gitignored)

```
results/<sha1(relpath)[:16]>_<size>_<mtime_ns>.json.gz   one result per source file
scan.log        one line per finished source + progress lines (bytes, MB/s, ETA)
progress.json   latest progress snapshot (used by `status`)
scan.lock       pid of the running scan; a second scan on the same cache refuses to start
merged.json     output of `merge`
```

A *source* is one abdata bundle or one zipmod (with all its inner bundles). The relpath is
relative to the game root with forward slashes. Result files are written atomically
(temp file + rename).

### Resume behaviour

The result file name contains the source's size and mtime. On start the scanner lists
`results/` and skips every source whose exact result name exists; a source whose size or mtime
changed is scanned again and its old result is deleted. Killing the scan loses only the sources
in flight. Errors (unreadable zip, non-bundle `.unity3d`, load failures) are recorded in the
result file and are not retried; delete that result file to retry a source. If a worker process
dies (for example out of memory) the pool is restarted and the sources in flight are retried;
a source that kills a worker twice is run alone at the end and, if it fails again, recorded as a
`crash` error.

### Result file (one source)

`source`, `kind` (`abdata` / `zipmod`), `size`, `mtime_ns`, `bytes` (bundle bytes scanned),
`seconds`, `bundles` (inner name, size, object count, Unity version), `types` (object counts per
Unity class), `shaders` (shader name -> count), `no_typetree`, `errors`, and:

- `scripts`: one entry per MonoBehaviour script key (assembly, namespace, class) with `count`,
  `so` / `nonso` (objects whose `m_GameObject` PathID is 0 = ScriptableObject, or not),
  `first_so`, `script_ids` (SerializedType hashes) and `variants`: each distinct serialized
  field list with its object count. A field node is `[type, name, [children...]]`; the base
  fields `m_GameObject m_Enabled m_Script m_Name m_EditorHideFlags m_EditorClassIdentifier`
  are left out.
- `unresolved`: MonoBehaviours whose MonoScript could not be read, keyed by script hash
  (`m_Script` null = missing script, or a script in a bundle that is not loaded).

The class of a MonoBehaviour is taken from its own `m_Script` PPtr, not from the SerializedType:
Unity shares one type entry between different scripts with the same layout (seen in a mod that
has both `Assembly-CSharp.dll FaceBlendShape` and `IL.dll FaceBlendShape`).

## Merge

`merge` reads the newest result of every source and writes:

- `merged.json`: `scripts` (per key: total `count`, `so`/`nonso`, `scriptableObject` = value of
  the first instance, `so_mixed`, merged `fields`, `conflicts`, `variant_count`, `script_ids`,
  `sources` = [relpath, count]), `unresolved` (those whose script hash did not map to exactly one
  class; the others are added to that class as `unresolved_count`), `shaders`, `types`, `errors`,
  `totals`.
- `Tools/survey/class_census_full.tsv`: assembly, namespace.class, objects, sources,
  scriptable_object.
- `Tools/survey/shader_census_full.tsv`: shader name, count, sources.

Field merge rule (same as the original `gen_compat_stubs.py`): sources are processed in relpath
order (`abdata/...` before `mods/...`, so vanilla wins), the first node seen for a field name is
kept, fields only present in later variants are appended, and a later node with the same name
but another top-level type is listed in `conflicts`. `--file-list` merges only the listed
relpaths in that order. `merge --survey-dir ""` skips the TSVs.

Merging is safe at any time, also while a scan is running; it only reads finished results.

## Generating the Compat stubs

```
python -I Tools/gen_compat_stubs.py <Assets dir or temp dir> [--merged Cache/typetree/merged.json]
python -I Tools/gen_compat_stubs.py <dir> --sample "<HS2 install>" [--zip-sample 400] [--rescan]   # old sampling path
```

The default reads `merged.json`. Package assemblies (UnityEngine.UI, TextMeshPro,
post-processing) and unresolved scripts are skipped, as before.

Check done: the old sample set (423 abdata bundles + 400 zipmods, rebuilt with the
same walk and seeds as the old `collect()`) was scanned with `typetree_scan.py`, merged with
`--file-list` in the old order, and generated into a temp dir. All 104 `.cs`/`.asmdef` files are
identical to `Assets/Compat` and `Assets/Plugins/Compat`. `compat_manifest.json` differs only in
two counts: one MonoBehaviour that the old scanner credited to `Assembly-CSharp.dll FaceBlendShape`
belongs to `IL.dll FaceBlendShape` (the shared-type case above), so the counts are 41/1 instead
of 40/2 and the two entries swap places in the count-sorted list.

## First full run

7,917 sources (1,979 abdata bundles, 5,938 zipmods incl. 4 `.zip`), 14,364 bundles, 330.8 GB,
5 + 1 workers: 22m39s (about 245 MB/s on average, 330-360 MB/s while the small pool is busy;
the tail is the single big-bundle worker). Peak worker memory 6.7 GB (2.6 GB map bundle), the
whole scan stayed under about 8 GB. Result: 378 script classes in 25 assemblies (355 after
the package filter of `gen_compat_stubs.py`), 2,340 shader names, 1,752 MonoBehaviours with a
null `m_Script` (missing scripts, 54 sources), 2 shader read errors, no load errors. The result
cache is 26 MB, a merge takes a few seconds once the files are in the OS cache (about 1 minute cold).

## Regenerating the stubs (from the full scan)

```
python -I Tools/gen_compat_stubs.py Assets
```

Run from the repo root; it overwrites `Assets/Compat` and `Assets/Plugins/Compat` in place. The
generator never deletes files: if a class drops out of `merged.json`, compare the file lists of the
old and new sets and delete the orphans together with their `.meta`. Output uses LF line endings
on every platform, and file names that collide case-insensitively get a `_2` suffix
(`UVScroll` / `UvScroll` in Assembly-CSharp).

Filtered: `UnityEngine.UI.dll` (9 classes, 5 sources), `Unity.TextMeshPro.dll` (1 class, 1
source) and `Unity.Postprocessing.Runtime.dll` (13 classes, 108 sources, 2,080 objects). The first
two come from the real packages. PPv2 is not in the project (URP only), so the old post-processing
components stay missing scripts. Nothing else is skipped. Unity 6.6 accepts the asmdef names
`Assembly-UnityScript` (6 classes, 1 zipmod), `UnityEngine.Timeline` (5 classes, 1 zipmod) and
`UnityEngine.Networking`. `com.unity.timeline` is not installed; if it is added later, its
`UnityEngine.Timeline` namespace will clash with the stubs.

Result: 355 classes in 21 assemblies (Assembly-CSharp 267, Assembly-CSharp-firstpass 18, plus 19
asmdefs), up from 94. That is 261 new `.cs` files, 9 new asmdefs and 16 changed stubs, and no
orphans. A changed stub carries the union of the field layouts seen, for example DynamicBone gains
`m_Roots`, `m_Friction`, `m_FrictionDistrib`, `m_BlendWeight` and `m_Multithread`. The compile
has 0 errors. As before, CS0436 warnings appear for the classes that exist in both
Assembly-CSharp and IL.dll. The missing-script counts of the bundle loads stay unchanged (2 in oo_base, 26
in studio/00): those objects have a null `m_Script` in the shipped bundles. `CardResolveTest
-limit 30` gives the same report, and the 18 PNGs of `CharaRenderTest -urp` are byte-identical.
