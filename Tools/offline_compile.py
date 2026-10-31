"""Offline compile check of Assembly-CSharp and Assembly-CSharp-Editor without starting Unity.

Uses Unity's own Roslyn compiler (Editor/Data/DotNetSdk/sdk/<version>/Roslyn/bincore/csc.dll run by
Editor/Data/NetCoreRuntime/dotnet.exe) with the response files of the last Unity compile (Library/Bee/artifacts/<dag>/
Assembly-CSharp.rsp and Assembly-CSharp-Editor.rsp: defines, references, analyzers), over the current working tree
(default) or, with --head, over the committed sources of HEAD (other people's unsaved or half-written files in the shared
tree then cannot break the check of one's own overlay), or, with --tree DIR, over the .cs files of an exported tree (the
export dry run: what the export ships compiles on its own; the response files still come from this project's Library):
  - the files the response file lists, unless deleted since;
  - .cs files under Assets that are new since that compile (tracked or untracked, not ignored), assigned as Unity does:
    under a folder holding an .asmdef / .asmref -> another assembly (skipped; tools/mono_tests.ps1 covers Api and Bridge),
    under Assets/Plugins or Assets/Standard Assets -> firstpass (skipped), with an "Editor" folder in the path -> the
    Editor assembly, else Assembly-CSharp;
  - the overlay file (--overlay), one "<scratch file>|<project-relative path>" per line ('#' comments): the scratch file
    is compiled in place of (or in addition to) that project path, so code can be checked before it is copied into Assets.
The Editor assembly is compiled against the Assembly-CSharp built here, not the one of the last Unity compile. The
project's own asmdef assemblies (HS2Next.Api, HS2Next.Api.Unity, HS2Next.Bridge) are compiled first from the same base and
overlay (their response files of the last Unity compile with the sources replaced; a new asmdef Unity has not compiled yet
takes the response file of a template assembly), and every later assembly references what was built here; --no-asm keeps
the last Unity build of them. Other assemblies (the compat stubs, packages) are referenced as Unity last built them.

The Unity install is derived as Tools/unity_batch.ps1 derives its -Unity default (HS2N_UNITY, else the Hub install of the
project's editor version; Tools/mono_tests.ps1 does the same); the dag
folder is the newest one under Library/Bee/artifacts holding Assembly-CSharp-Editor.rsp (the Editor's, not a player
build's), or the one --dag names. Outputs go to %TEMP%/hs2next_offline_compile (never into the project).

Usage:  python -I Tools/offline_compile.py [--head | --tree DIR] [--overlay FILE] [--runtime-only] [--player] [--no-asm] [--dag NAME] [--max-errors N] [-q]
Exit code: 0 = no compile errors; 1 = compile errors; 2 = the check could not run (no Unity install, no Library/Bee
response files: open the project in Unity once).
"""
import argparse
import glob
import io
import os
import re
import subprocess
import sys
import tarfile
import tempfile
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARTIFACTS = os.path.join(PROJ, "Library", "Bee", "artifacts")

# The project's asmdef assemblies compiled from source before Assembly-CSharp, in dependency order:
# (assembly name, folder, template assembly whose response file a new asmdef borrows until Unity has compiled it once).
PROJECT_ASMS = [
    ("HS2Next.Api", "Assets/HS2Next/Api", None),
    ("HS2Next.Api.Unity", "Assets/HS2Next/ApiUnity", "HS2Next.Bridge"),
    ("HS2Next.Bridge", "Assets/HS2Next/Bridge", None),
]


def fail_setup(msg):
    print("[offline_compile] cannot run: " + msg)
    sys.exit(2)


def unity_editor_dir():
    """The folder of Unity.exe, derived as unity_batch.ps1 derives its -Unity default: the environment variable
    HS2N_UNITY, else the Unity Hub install of the editor version in ProjectSettings/ProjectVersion.txt under Program Files."""
    exe = os.environ.get("HS2N_UNITY")
    if not exe:
        pv = os.path.join(PROJ, "ProjectSettings", "ProjectVersion.txt")
        try:
            text = open(pv, encoding="utf-8-sig").read()
        except OSError as e:
            fail_setup("cannot read ProjectSettings/ProjectVersion.txt (%s)" % e)
        m = re.search(r"^m_EditorVersion:\s*(\S+)", text, re.M)
        if not m:
            fail_setup("no m_EditorVersion in ProjectSettings/ProjectVersion.txt (set HS2N_UNITY)")
        program_files = os.environ.get("ProgramFiles", r"C:\Program Files")
        exe = os.path.join(program_files, "Unity", "Hub", "Editor", m.group(1), "Editor", "Unity.exe")
    return os.path.dirname(exe)


def compiler(editor_dir):
    dotnet = os.path.join(editor_dir, "Data", "NetCoreRuntime", "dotnet.exe")
    cscs = sorted(glob.glob(os.path.join(editor_dir, "Data", "DotNetSdk", "sdk", "*", "Roslyn", "bincore", "csc.dll")))
    if not os.path.isfile(dotnet):
        fail_setup("not found: " + dotnet)
    if not cscs:
        fail_setup("no Roslyn csc.dll under " + os.path.join(editor_dir, "Data", "DotNetSdk", "sdk"))
    return dotnet, cscs[-1]


def find_dag(name, player):
    if not os.path.isdir(ARTIFACTS):
        fail_setup("no Library/Bee/artifacts (open the project in Unity once)")
    if name:
        d = os.path.join(ARTIFACTS, name)
        if not os.path.isfile(os.path.join(d, "Assembly-CSharp.rsp")):
            fail_setup("no Assembly-CSharp.rsp in " + d)
        return d
    found = []
    for d in glob.glob(os.path.join(ARTIFACTS, "*.dag")):
        rsp = os.path.join(d, "Assembly-CSharp.rsp")
        if not os.path.isfile(rsp):
            continue
        editor = os.path.isfile(os.path.join(d, "Assembly-CSharp-Editor.rsp"))
        if editor != (not player):
            continue
        found.append((os.path.getmtime(rsp), d))
    if not found:
        fail_setup("no %s dag folder with Assembly-CSharp.rsp under Library/Bee/artifacts" % ("player" if player else "Editor"))
    return max(found)[1]


def git_lines(*args):
    r = subprocess.run(["git", "-C", PROJ] + list(args), capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        fail_setup("git %s failed: %s" % (" ".join(args), r.stderr.strip()))
    return [l.strip().strip('"') for l in r.stdout.splitlines() if l.strip()]


_ignored = None


def git_ignored(p):
    """True when p is an untracked file that .gitignore excludes (part of the project for Unity, never committed)."""
    global _ignored
    if _ignored is None:
        _ignored = set(git_lines("ls-files", "-oi", "--exclude-standard", "--", "Assets"))
    return p in _ignored


def read_rsp(path):
    opts, files = [], []
    for ln in open(path, encoding="utf-8-sig").read().splitlines():
        m = re.match(r'^"(Assets/.*\.cs)"$', ln)
        if m:
            files.append(m.group(1))
        elif ln.strip():
            opts.append(ln)
    return opts, files


def main():
    ap = argparse.ArgumentParser(description="Offline compile check of Assembly-CSharp(-Editor) with Unity's Roslyn.")
    ap.add_argument("--head", action="store_true", help="compile HEAD's committed sources (+ the overlay), not the working tree")
    ap.add_argument("--tree", help="compile the .cs files under DIR/Assets (an exported tree), not the working tree")
    ap.add_argument("--overlay", help='file of "<scratch file>|<project-relative path>" lines')
    ap.add_argument("--runtime-only", action="store_true", help="Assembly-CSharp only")
    ap.add_argument("--player", action="store_true", help="the player build's dag (Assembly-CSharp without UNITY_EDITOR)")
    ap.add_argument("--no-asm", action="store_true", help="reference HS2Next.Api / .Api.Unity / .Bridge as Unity last built them")
    ap.add_argument("--dag", help="the dag folder name under Library/Bee/artifacts (default: discovered)")
    ap.add_argument("--out", help="output folder (default %%TEMP%%/hs2next_offline_compile)")
    ap.add_argument("--max-errors", type=int, default=60, help="error lines printed per assembly")
    ap.add_argument("-q", "--quiet", action="store_true", help="print only the summary and the errors")
    a = ap.parse_args()

    t0 = time.time()
    dotnet, csc = compiler(unity_editor_dir())
    dag = find_dag(a.dag, a.player)
    dag_rel = os.path.relpath(dag, PROJ).replace("\\", "/")
    out = a.out or os.path.join(tempfile.gettempdir(), "hs2next_offline_compile" + ("_player" if a.player else ""))
    os.makedirs(out, exist_ok=True)

    # the .cs files under Assets (working tree: tracked and untracked, not ignored; --head: HEAD's, extracted to the output
    # folder) and the folders of other assemblies
    head_src = {}
    tree_root = os.path.abspath(a.tree) if a.tree else None
    if tree_root and a.head:
        fail_setup("--head and --tree exclude each other")
    if tree_root:
        if not os.path.isdir(os.path.join(tree_root, "Assets")):
            fail_setup("no Assets folder under " + tree_root)
        tree, asm_files = set(), []
        for dirpath, dirnames, filenames in os.walk(os.path.join(tree_root, "Assets")):
            for fn in filenames:
                rel = os.path.relpath(os.path.join(dirpath, fn), tree_root).replace("\\", "/")
                if fn.endswith(".cs"):
                    tree.add(rel)
                elif fn.endswith((".asmdef", ".asmref")):
                    asm_files.append(rel)
    elif a.head:
        r = subprocess.run(["git", "-C", PROJ, "archive", "HEAD", "--", "Assets"], capture_output=True)
        if r.returncode != 0:
            fail_setup("git archive HEAD failed: " + r.stderr.decode("utf-8", "replace").strip())
        src_root = os.path.join(out, "head_src")
        with tarfile.open(fileobj=io.BytesIO(r.stdout)) as tf:
            for m in tf.getmembers():
                if m.isfile() and m.name.endswith(".cs"):
                    dst = os.path.join(src_root, *m.name.split("/"))
                    data = tf.extractfile(m).read()
                    if not os.path.isfile(dst) or open(dst, "rb").read() != data:
                        os.makedirs(os.path.dirname(dst), exist_ok=True)
                        with open(dst, "wb") as fh:
                            fh.write(data)
                    head_src[m.name] = dst
        tree = set(head_src)
        asm_files = [p for p in git_lines("ls-tree", "-r", "--name-only", "HEAD", "--", "Assets") if p.endswith((".asmdef", ".asmref"))]
    else:
        tree = set(p for p in git_lines("ls-files", "-co", "--exclude-standard", "--", "Assets") if p.endswith(".cs"))
        asm_files = [p for p in git_lines("ls-files", "-co", "--exclude-standard", "--", "*.asmdef", "*.asmref")]

    def source_of(p):
        """The file compiled for project path p, or None when it does not exist in the chosen base."""
        if p in overlay:
            return overlay[p]
        if tree_root:
            f = os.path.join(tree_root, *p.split("/"))
            return f if os.path.isfile(f) else None
        if a.head:
            if p in head_src:
                return head_src[p]
            # listed but not in HEAD: an ignored file the last Unity compile saw is taken from disk; anything else was
            # added or deleted since HEAD
            return p if git_ignored(p) and os.path.isfile(os.path.join(PROJ, p)) else None
        return p if os.path.isfile(os.path.join(PROJ, p)) else None

    def other_assembly(p):
        return any(p.startswith(d + "/") for d in asm_dirs) or p.startswith("Assets/Plugins/") or p.startswith("Assets/Standard Assets/")

    def is_editor(p):
        return "Editor" in p.split("/")[:-1]

    overlay = {}
    if a.overlay:
        for ln in open(a.overlay, encoding="utf-8-sig").read().splitlines():
            ln = ln.strip()
            if not ln or ln.startswith("#"):
                continue
            src, _, dst = ln.partition("|")
            src, dst = src.strip(), dst.strip().replace("\\", "/")
            if not dst or not os.path.isfile(src):
                print("[offline_compile] overlay line ignored (needs '<existing scratch file>|<project path>'): " + ln)
                continue
            overlay[dst] = os.path.abspath(src)
    # an overlaid .asmdef (a new assembly drafted in scratch) counts like one in the base
    asm_files = sorted(set(asm_files) | set(p for p in overlay if p.endswith((".asmdef", ".asmref"))))
    asm_dirs = [os.path.dirname(p) for p in asm_files]

    def asmdef_of(folder):
        """(project path, parsed JSON) of the .asmdef directly in folder in the chosen base, or (None, None)."""
        import json
        for p in asm_files:
            if os.path.dirname(p) != folder or not p.endswith(".asmdef"):
                continue
            if p in overlay:
                raw = open(overlay[p], "rb").read()
            elif tree_root:
                raw = open(os.path.join(tree_root, *p.split("/")), "rb").read()
            elif a.head:
                r = subprocess.run(["git", "-C", PROJ, "show", "HEAD:" + p], capture_output=True)
                raw = r.stdout if r.returncode == 0 else b"{}"
            else:
                raw = open(os.path.join(PROJ, *p.split("/")), "rb").read()
            try:
                return p, json.loads(raw.decode("utf-8-sig"))
            except ValueError:
                return p, {}
        return None, None

    def ref_line(dll):
        return '-r:"%s"' % dll.replace("\\", "/")

    if not a.quiet:
        print("[offline_compile] base %s; dag %s, compiler %s" % ("tree " + tree_root if tree_root else "HEAD " + git_lines("rev-parse", "--short", "HEAD")[0] if a.head else "working tree", dag_rel, os.path.relpath(csc, os.path.dirname(os.path.dirname(os.path.dirname(dotnet))))))
    # The project's asmdef assemblies, built from the base (+ overlay) in PROJECT_ASMS order; every later response file
    # references these builds instead of the last Unity build.
    built_asm = {}          # assembly name -> built ref dll
    auto_ref = {}           # assembly name -> autoReferenced (Assembly-CSharp(-Editor) reference it)
    total_errors = 0
    if not a.no_asm:
        for asm, folder, template in PROJECT_ASMS:
            path, js = asmdef_of(folder)
            if path is None:
                continue
            rsp_path = os.path.join(dag, asm + ".rsp")
            from_template = not os.path.isfile(rsp_path)
            if from_template:
                if not template or not os.path.isfile(os.path.join(dag, template + ".rsp")):
                    print("[offline_compile] %s: no response file of the last Unity compile and no template; not built" % asm)
                    continue
                rsp_path = os.path.join(dag, template + ".rsp")
            opts, _ = read_rsp(rsp_path)
            nested = [d for d in asm_dirs if d != folder and d.startswith(folder + "/")]
            srcs = []
            for p in sorted(tree | set(overlay)):
                if not p.endswith(".cs") or not p.startswith(folder + "/") or any(p.startswith(d + "/") for d in nested):
                    continue
                f = source_of(p)
                if f is not None:
                    srcs.append(f)
            wanted = set(js.get("references") or [])
            no_engine = js.get("noEngineReferences") is True
            lines, have = [], set()
            for ln in opts:
                if no_engine and ln.startswith("-r:") and ("/UnityEngine" in ln or "/UnityEditor" in ln):
                    continue        # the asmdef says the assembly needs no engine: prove it
                if ln.startswith("-out:"):
                    ln = '-out:"%s"' % os.path.join(out, asm + ".dll")
                elif ln.startswith("-refout:"):
                    ln = '-refout:"%s"' % os.path.join(out, asm + ".ref.dll")
                else:
                    m = re.match(r'^-r:"%s/(HS2Next[^"/]*)\.ref\.dll"$' % re.escape(dag_rel), ln)
                    if m:
                        ref = m.group(1)
                        if from_template and ref not in wanted:
                            continue
                        if ref in built_asm:
                            ln = ref_line(built_asm[ref])
                        have.add(ref)
                lines.append(ln)
            for ref in sorted(wanted - have):
                if ref in built_asm:
                    lines.append(ref_line(built_asm[ref]))
            lines += ['"%s"' % f.replace("\\", "/") for f in srcs]
            rsp = os.path.join(out, asm + ".rsp")
            with open(rsp, "w", encoding="utf-8") as fh:
                fh.write("\n".join(lines) + "\n")
            r = subprocess.run([dotnet, csc, "@" + rsp], cwd=PROJ, capture_output=True, text=True, encoding="utf-8", errors="replace")
            errs = [l for l in r.stdout.splitlines() if re.search(r"\berror [A-Z]+\d+", l)]
            if r.returncode != 0 and not errs:
                errs = ["csc exit %d without error lines: %s" % (r.returncode, (r.stdout + r.stderr).strip()[-2000:])]
            for e in errs[: a.max_errors]:
                print(e)
            print("[offline_compile] %s: %d files%s, errors %d" % (asm, len(srcs), " (response file of " + template + ")" if from_template else "", len(errs)))
            total_errors += len(errs)
            if errs:
                print("[offline_compile] FAIL: %d compile errors (%.0f s)" % (total_errors, time.time() - t0))
                return 1
            built_asm[asm] = os.path.join(out, asm + ".ref.dll")
            auto_ref[asm] = js.get("autoReferenced", True) is not False

    names = ["Assembly-CSharp"] + ([] if a.runtime_only or a.player else ["Assembly-CSharp-Editor"])
    rsps = {n: read_rsp(os.path.join(dag, n + ".rsp")) for n in names}
    listed_any = set(f for n in names for f in rsps[n][1])
    if not a.player and "Assembly-CSharp-Editor" not in names:
        listed_any |= set(read_rsp(os.path.join(dag, "Assembly-CSharp-Editor.rsp"))[1])

    built_ref = None
    for name in names:
        opts, listed = rsps[name]
        editor = name.endswith("-Editor")
        files, removed, added, over = [], [], [], []
        for p in listed:
            f = source_of(p)
            if f is None:
                removed.append(p)
                continue
            files.append(f)
            if p in overlay:
                over.append(p)
        for p in sorted((tree | set(overlay)) - listed_any):
            if other_assembly(p) or is_editor(p) != editor:
                continue
            f = source_of(p)
            if f is None:
                continue
            files.append(f)
            (over if p in overlay else added).append(p)
        lines = []
        for ln in opts:
            if ln.startswith("-out:"):
                ln = '-out:"%s"' % os.path.join(out, name + ".dll")
            elif ln.startswith("-refout:"):
                ln = '-refout:"%s"' % os.path.join(out, name + ".ref.dll")
            elif built_ref and ln == '-r:"%s/Assembly-CSharp.ref.dll"' % dag_rel:
                ln = '-r:"%s"' % built_ref
            else:
                m = re.match(r'^-r:"%s/(HS2Next[^"/]*)\.ref\.dll"$' % re.escape(dag_rel), ln)
                if m and m.group(1) in built_asm:
                    ln = ref_line(built_asm[m.group(1)])
            lines.append(ln)
        # a new auto-referenced project assembly (no line of its own in the response file yet)
        for asm in built_asm:
            if auto_ref.get(asm) and ref_line(built_asm[asm]) not in lines:
                lines.append(ref_line(built_asm[asm]))
        lines += ['"%s"' % f.replace("\\", "/") for f in files]
        rsp = os.path.join(out, name + ".rsp")
        with open(rsp, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        r = subprocess.run([dotnet, csc, "@" + rsp], cwd=PROJ, capture_output=True, text=True, encoding="utf-8", errors="replace")
        errs = [l for l in r.stdout.splitlines() if re.search(r"\berror [A-Z]+\d+", l)]
        if r.returncode != 0 and not errs:
            errs = ["csc exit %d without error lines: %s" % (r.returncode, (r.stdout + r.stderr).strip()[-2000:])]
        for e in errs[: a.max_errors]:
            print(e)
        if len(errs) > a.max_errors:
            print("... %d more error lines" % (len(errs) - a.max_errors))
        if not a.quiet:
            for p in added:
                print("  new since the last Unity compile: " + p)
            for p in removed:
                print("  deleted since the last Unity compile: " + p)
            for p in over:
                print("  overlay: " + p + " <- " + overlay[p])
        print("[offline_compile] %s: %d files (%d new, %d deleted, %d overlaid), errors %d" % (name, len(files), len(added), len(removed), len(over), len(errs)))
        total_errors += len(errs)
        if errs:
            if editor is False and "Assembly-CSharp-Editor" in names:
                print("[offline_compile] Assembly-CSharp-Editor not compiled (it needs Assembly-CSharp)")
            break
        built_ref = os.path.join(out, name + ".ref.dll")
    print("[offline_compile] %s: %d compile errors (%.0f s)" % ("FAIL" if total_errors else "OK", total_errors, time.time() - t0))
    return 1 if total_errors else 0


if __name__ == "__main__":
    sys.exit(main())
