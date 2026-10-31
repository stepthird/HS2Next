"""binary_scan - the string scan of a player folder's binaries (the distribution package or an app player folder).

Reads files as bytes, takes the printable runs in ASCII and in UTF-16LE, and looks for what must not ship: absolute drive
paths, user-profile paths, e-mail addresses and the private tokens of the local token file (Config/leak_tokens.local.txt,
read with Tools/leak_check.py's reader). The report holds classes, counts and file names only, never the text of a hit (nor
of a token).

Files and their kinds:
  built      a managed assembly compiled here: HS2Next_Data/Managed/<name>.dll when the project's compiler output folder
             (Library/ScriptAssemblies) holds one of the same name (the app, the compatibility stubs, the packages compiled
             from source)
  vendor     a file its vendor built: the executable (Unity's player stub), the other managed assemblies, the engine's and
             the runtime's files at the top level and under MonoBleedingEdge/, D3D12/ and HS2Next_Data/Plugins/
  data       (--all-files) every other file under HS2Next_Data/ (the serialized assets, the boot and build files) and any
             other file the build wrote
  text       (--all-files) the package texts at the top level (*.txt, *.md, *.json): Tools/leak_check.py --tree checks them
             with the package allow-list; not read here
Without --all-files only the executable and HS2Next_Data/Managed/*.dll are read (runs of 4 or more characters). With
--all-files every file of the folder is read; reparse points (links, junctions) are never entered and the folders a player
writes next to itself (UserData, Cache, Config, Plugins, the abdata link) are skipped. In the files outside the default set
the runs under 10 characters are binary noise for the path-shaped classes, and so are a drive path or an e-mail address
under 10 characters (an 8-character address shape inside the serialized assets was measured); private tokens are searched in
every run of 4 or more characters.

Classes of a hit:
  vendor     a drive path, user-profile path or e-mail address in a vendor file: the vendor's build-machine paths and
             public addresses. Bounded per file: at most the measured baseline (VENDOR_BASELINE) + 10 %, rounded up; a
             vendor file over its bound (or with hits and no baseline) fails
  pdb        a drive path ending in .pdb in a built or data file: the compiler's debug path (the player build maps the
             project folder to "." for the compiler)                                     -> failure
  source     a drive path ending in .cs in a built or data file: a caller-file argument   -> failure
  game       a drive path naming the game's install folder (HoneySelect) in a built or data file; a private token inside
             such a path is counted with it                                              -> failure
  path       any other drive path in a built or data file                                -> failure
  profile    a user-profile folder path (the Windows or macOS users folder, or a Unix home folder) outside a drive path,
             in a built or data file                                                     -> failure
  email      an e-mail address in a built or data file                                   -> failure
  token      a private token in any file read (outside a game path)                      -> failure
Exit 0 when nothing failed, 1 otherwise, 2 when the folder is not a player folder or the setup is missing.

Usage (Python 3, standard library only; run with -I)
  python -I Tools/dist/binary_scan.py --tree Builds/Dist/HS2Next [--all-files] [--out <report>] [--tokens <file>] [--built <dir>]
  python -I Tools/dist/binary_scan.py --tree Builds/App --all-files --print-vendor   # the vendor counts, for VENDOR_BASELINE
"""
import argparse
import importlib.util
import math
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

DRIVE = re.compile(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\x00-\x1f\"<>|*?]{2,}")
PROFILE = re.compile(r"[\\/](?:Users|home)[\\/]", re.I)
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")
CLASSES = ("vendor", "pdb", "source", "game", "path", "profile", "email", "token")
FAILING = ("pdb", "source", "game", "path", "profile", "email", "token")
RUN_DEFAULT = 4          # printable runs read in the executable and the managed assemblies
RUN_WHOLE = 10           # files read whole: shorter runs (and shorter path or address hits) are binary noise
RUN_TOKEN = 4            # private tokens: every run of this length or more, in every file read
RUN_TIME = ("userdata", "cache", "config", "plugins", "abdata")   # written next to a player at run time; never read
VENDOR_DIRS = ("MonoBleedingEdge/", "D3D12/", "HS2Next_Data/Plugins/")
TEXT_EXT = (".txt", ".md", ".json")

# Hits per vendor file (drive paths, profile paths and addresses, distinct strings per file) of the Unity 6000.6.5f1 player
# and its runtime, measured with --print-vendor on the app build; the bound of a file is this count + 10 % (rounded up).
# Re-measure after a Unity upgrade or a new runtime component.
VENDOR_BASELINE = {
    "D3D12/D3D12Core.dll": 6,
    "HS2Next.exe": 1,
    "HS2Next_Data/Managed/Mono.Security.dll": 1,
    "HS2Next_Data/Managed/Newtonsoft.Json.dll": 1,
    "HS2Next_Data/Managed/System.Net.Http.dll": 1,
    "HS2Next_Data/Managed/System.Runtime.CompilerServices.Unsafe.dll": 1,
    "HS2Next_Data/Managed/System.ServiceModel.Internals.dll": 1,
    "HS2Next_Data/Managed/System.dll": 1,
    "HS2Next_Data/Managed/Unity.Collections.LowLevel.ILSupport.dll": 1,
    "HS2Next_Data/Managed/Unity.Scripting.dll": 1,
    "HS2Next_Data/Managed/UnityEngine.UnityWebRequestWWWModule.dll": 1,
    "HS2Next_Data/Managed/UnityEngine.dll": 1,
    "HS2Next_Data/Managed/mscorlib.dll": 2,
    "MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll": 1,
    "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll": 187,
    "MonoBleedingEdge/etc/mono/2.0/DefaultWsdlHelpGenerator.aspx": 1,
    "MonoBleedingEdge/etc/mono/4.0/DefaultWsdlHelpGenerator.aspx": 1,
    "MonoBleedingEdge/etc/mono/4.5/DefaultWsdlHelpGenerator.aspx": 1,
    "MonoBleedingEdge/etc/mono/browscap.ini": 32,
    "UnityCrashHandler64.exe": 12,
    "UnityPlayer.dll": 973,
    "dstorage.dll": 3,
    "dstoragecore.dll": 44,
}


def load_leak_check():
    """Tools/leak_check.py as a module (its token reader); None when the file is missing."""
    path = os.path.join(ROOT, "Tools", "leak_check.py")
    if not os.path.isfile(path):
        return None
    spec = importlib.util.spec_from_file_location("leak_check", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def runs(data, minimum):
    ascii_run = re.compile(rb"[\x20-\x7e]{%d,}" % minimum)
    utf16_run = re.compile(rb"(?:[\x20-\x7e]\x00){%d,}" % minimum)
    for m in ascii_run.finditer(data):
        yield m.group(0).decode("ascii")
    for m in utf16_run.finditer(data):
        yield m.group(0).decode("utf-16-le")


def classify_path(p):
    low = p.lower().rstrip()
    if low.endswith(".pdb"):
        return "pdb"
    if low.endswith(".cs"):
        return "source"
    if "honeyselect" in low.replace(" ", ""):
        return "game"
    return "path"


def scan_file(full, kind, tokens, whole):
    """Counts per class for one file. kind: built | vendor | data. whole: read with the noise rules of files read whole."""
    counts = dict((c, 0) for c in CLASSES)
    tokens_in_game = 0
    with open(full, "rb") as f:
        data = f.read()
    least = RUN_WHOLE if whole else RUN_DEFAULT
    seen = set()
    for s in runs(data, least):
        if s in seen:
            continue
        seen.add(s)
        spans = []
        for m in DRIVE.finditer(s):
            if whole and len(m.group(0).rstrip()) < RUN_WHOLE:
                continue
            c = "vendor" if kind == "vendor" else classify_path(m.group(0))
            counts[c] += 1
            spans.append((m.start(), m.end(), c))
        for m in PROFILE.finditer(s):
            if not any(a <= m.start() < b for a, b, _ in spans):
                counts["vendor" if kind == "vendor" else "profile"] += 1
        for m in EMAIL.finditer(s):
            if whole and len(m.group(0)) < RUN_WHOLE:
                continue
            counts["vendor" if kind == "vendor" else "email"] += 1
        if least <= RUN_TOKEN:
            for n, rx in tokens or []:
                for m in rx.finditer(s):
                    if any(a <= m.start() < b and c == "game" for a, b, c in spans):
                        tokens_in_game += 1
                    else:
                        counts["token"] += 1
    if least > RUN_TOKEN and tokens:
        # the token search reads the shorter runs as well (a short name alone in a serialized string is a run of its length)
        seen = set()
        for s in runs(data, RUN_TOKEN):
            if s in seen:
                continue
            seen.add(s)
            spans = [(m.start(), m.end()) for m in DRIVE.finditer(s)
                     if len(m.group(0).rstrip()) >= RUN_WHOLE and "honeyselect" in m.group(0).lower().replace(" ", "")]
            for n, rx in tokens:
                for m in rx.finditer(s):
                    if kind != "vendor" and any(a <= m.start() < b for a, b in spans):
                        tokens_in_game += 1
                    else:
                        counts["token"] += 1
    return counts, tokens_in_game


def is_reparse(p):
    try:
        return bool(os.lstat(p).st_file_attributes & 0x400)   # FILE_ATTRIBUTE_REPARSE_POINT (Windows)
    except (AttributeError, OSError):
        return os.path.islink(p)


def default_files(tree):
    """The executable and the managed assemblies (relative paths), or None when the folder is not a player folder."""
    managed = os.path.join(tree, "HS2Next_Data", "Managed")
    if not os.path.isfile(os.path.join(tree, "HS2Next.exe")) or not os.path.isdir(managed):
        return None
    files = ["HS2Next.exe"] + sorted("HS2Next_Data/Managed/" + n for n in os.listdir(managed) if n.lower().endswith(".dll"))
    return [f for f in files if os.path.isfile(os.path.join(tree, f)) and not is_reparse(os.path.join(tree, f))]


def all_files(tree, skipped, links):
    """Every file of the folder (relative paths, sorted); reparse points and the run-time folders are listed, never entered."""
    out = []
    for dirpath, dirnames, filenames in os.walk(tree, followlinks=False):
        keep = []
        for d in dirnames:
            full = os.path.join(dirpath, d)
            rel = os.path.relpath(full, tree).replace("\\", "/")
            if is_reparse(full):
                links.append(rel)
            elif dirpath == tree and d.lower() in RUN_TIME:
                skipped.append(rel)
            else:
                keep.append(d)
        dirnames[:] = keep
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, tree).replace("\\", "/")
            if is_reparse(full):
                links.append(rel)
            elif dirpath == tree and fn.lower() in RUN_TIME:
                skipped.append(rel)
            else:
                out.append(rel)
    return sorted(out)


def kind_of(rel, built_names, default_set):
    low = rel.lower()
    if low.startswith("hs2next_data/managed/") and low.endswith(".dll") and "/" not in rel[len("HS2Next_Data/Managed/"):]:
        return "built" if os.path.basename(low) in built_names else "vendor"
    if rel in default_set:
        return "vendor"   # the executable
    if "/" not in rel:
        if low.endswith((".exe", ".dll")):
            return "vendor"
        if low.endswith(TEXT_EXT):
            return "text"
        return "data"
    if any(low.startswith(d.lower()) for d in VENDOR_DIRS):
        return "vendor"
    return "data"


def bound(n):
    return int(math.ceil(n * 1.1))


def main():
    ap = argparse.ArgumentParser(description="String scan of a player folder's binaries (counts only).")
    ap.add_argument("--tree", required=True)
    ap.add_argument("--all-files", action="store_true", help="read every file of the folder (data files, the engine's files)")
    ap.add_argument("--out")
    ap.add_argument("--tokens", default=os.path.join(ROOT, "Config", "leak_tokens.local.txt"))
    ap.add_argument("--built", default=os.path.join(ROOT, "Library", "ScriptAssemblies"),
                    help="the project's compiler output folder (assemblies of the same name count as built here)")
    ap.add_argument("--print-vendor", action="store_true", help="print the vendor hits per file as VENDOR_BASELINE entries")
    a = ap.parse_args()
    tree = os.path.abspath(a.tree)
    base = default_files(tree)
    if base is None:
        print("binary_scan: not a player folder (HS2Next.exe and HS2Next_Data/Managed): " + tree)
        return 2
    if not os.path.isdir(a.built):
        print("binary_scan: no compiler output folder " + a.built + " (open the project in Unity once)")
        return 2
    lc = load_leak_check()
    if lc is None:
        print("binary_scan: Tools/leak_check.py is missing (its token reader reads the token file)")
        return 2
    try:
        tokens = lc.read_tokens(a.tokens)
    except lc.SetupError as e:
        print("binary_scan: the token file: %s" % e)
        return 2
    built_names = set(n.lower() for n in os.listdir(a.built) if n.lower().endswith(".dll"))
    skipped, links = [], []
    files = all_files(tree, skipped, links) if a.all_files else base
    default_set = set(base)
    total = dict((c, 0) for c in CLASSES)
    holders = dict((c, []) for c in CLASSES)
    kinds = dict((k, 0) for k in ("built", "vendor", "data", "text"))
    texts = []
    per_file = []
    vendor_hits = {}
    in_game = 0
    for rel in files:
        kind = kind_of(rel, built_names, default_set)
        kinds[kind] += 1
        if kind == "text":
            texts.append(rel)
            continue
        c, g = scan_file(os.path.join(tree, rel), kind, tokens, rel not in default_set)
        in_game += g
        if kind == "vendor":
            vendor_hits[rel] = c["vendor"]
        hit = [(k, v) for k, v in c.items() if v]
        if hit or g:
            per_file.append("  %s [%s]: %s" % (rel, kind, ", ".join("%s %d" % kv for kv in hit) +
                                               ("; tokens inside the game path %d" % g if g else "")))
        for k, v in c.items():
            if v:
                total[k] += v
                holders[k].append((rel, v))
    if a.print_vendor:
        for rel in sorted(vendor_hits):
            if vendor_hits[rel]:
                print("    %r: %d," % (rel, vendor_hits[rel]))
        return 0
    over = []
    for rel, n in sorted(vendor_hits.items()):
        if n > bound(VENDOR_BASELINE.get(rel, 0)):
            over.append((rel, n, VENDOR_BASELINE.get(rel)))
    bad = sum(total[c] for c in FAILING)
    verdict = "PASS (vendor files within their bounds; nothing in %s)" % ("files built here or data files" if a.all_files else "files built here")
    if bad or over:
        why = []
        if bad:
            why.append("%d hits in %s" % (bad, "files built here or data files" if a.all_files else "files built here"))
        if over:
            why.append("%d vendor files over their bound" % len(over))
        verdict = "FAIL (" + "; ".join(why) + ")"
    scanned = "%d files scanned (%d built here, %d prebuilt" % (kinds["built"] + kinds["vendor"] + kinds["data"], kinds["built"], kinds["vendor"])
    if a.all_files:
        scanned += ", %d data; %d package texts left to leak_check.py; run-time entries not read: %s; reparse points not entered: %s" % (
            kinds["data"], len(texts), ", ".join(skipped) or "none", ", ".join(links) or "none")
    scanned += ")"
    lines = ["SUMMARY: %s; %s; tokens %s; %s" % (
        ", ".join("%s %d" % (c, total[c]) for c in CLASSES), scanned,
        "none (no local token file)" if tokens is None else len(tokens), verdict)]
    if in_game:
        lines.append("private-token matches inside the game path (counted with game): %d" % in_game)
    for c in CLASSES:
        if holders[c]:
            lines.append("%s%s: %d hits in %d files: %s" % (c, " (bounded per file)" if c == "vendor" else "", total[c], len(holders[c]),
                                                       ", ".join("%s %d" % (r, v) for r, v in holders[c][:40]) + (" ..." if len(holders[c]) > 40 else "")))
    vend = sum(1 for r in vendor_hits if vendor_hits[r])
    lines.append("vendor bounds: %d vendor files with hits, %d over their bound%s" % (
        vend, len(over), (": " + ", ".join("%s %d > %s" % (r, n, "no baseline" if b is None else "%d (baseline %d)" % (bound(b), b)) for r, n, b in over)) if over else ""))
    if a.all_files and texts:
        lines.append("package texts (not read here; leak_check.py --tree checks them): " + ", ".join(texts))
    if per_file:
        lines.append("files with hits:")
        lines.extend(per_file)
    text = "\n".join(lines) + "\n"
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    sys.stdout.write(lines[0] + "\n")
    return 0 if not bad and not over else 1


if __name__ == "__main__":
    sys.exit(main())
