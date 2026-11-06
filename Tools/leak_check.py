"""leak_check - the publication guard of HS2Next: scans what the export ships for text that must not be published.

What is scanned
  The tracked files of the repository (git ls-files) that Tools/export_manifest.json includes, or with --tree <dir>
  every file under an exported tree (the export dry run; the manifest, allow-list and token file still come from this
  repository). Text files are chosen by extension (and a few names without one), at most 4 MB each; a file with a NUL
  byte in its first 8 KB is binary and skipped. The save-format containers (ZIP: .hs2nscene, .hs2nchara, .hs2ncoord,
  .hs2npose) and .zip files are scanned entry by entry (their text entries; reported as 'file!entry'). File paths and
  entry names are checked for the private tokens too.

Hard classes (any hit fails the check)
  path.drive       absolute Windows paths: a drive letter at a word start, a colon, a slash or backslash
  path.profile     user-profile folders: the word "users" followed by a slash or backslash
  email            e-mail addresses
  token            the private tokens: one regex per line in the local token file Config/leak_tokens.local.txt
                   (git-ignored; matched case-insensitively; blank lines and '#' lines skipped). A hit names the
                   pattern's line number in that file, never the matched text, and every excerpt in the report masks
                   the tokens. Without the file the check runs the generic classes and says so.
  manifest.unclassified  a tracked path (or a file of the tree) that no include and no exclude pattern names
  manifest.excluded      (--tree) a file of the exported tree that the manifest excludes
  policy.uncensor  the release policy: an exported file outside the data allow-list
                   (leak_allow.json "policy_data": the card decoder and import of the inert uncensor data, the document
                   section, the field schema, the data tests) that names the moved uncensor code, its manifest
                   declaration, its overlay members, its FK node asset or the private folder (the tokens are built from
                   fragments below, so this file does not name them)
  policy.mosaicSwitch  an option, settings key, command argument, command-line flag, script parameter or switch field
                   whose name contains "mosaic" or "censor" (case-insensitive), other than the option render.mosaicWide:
                   .Arg("name") in C#; the string keys of OptionDefs; the keys of Config/settings.example.json and the
                   strings of SettingsKeys; "-flag" literals in runtime C#; [switch] / typed parameters of PowerShell
                   scripts; public static non-readonly fields and the public fields of *Options / *Settings / *Switches
                   classes in runtime C#
  The two policy classes check themselves first on generated fixtures (policy_self_test); a failing self-test is a setup
  error (exit 2).
Soft classes (counted only; the export rewrites or reviews them)
  cite.section     lines with a section sign in exported text files
  cite.spec        lines naming an internal document (SPEC_ / PREP_)
  method.comment   game or plugin method names in comments - a call on ChaControl, ChaFile*, CustomBase, Cvs* or
                   OCI* (the type, a dot, a name, an opening parenthesis) - in files changed since the tag v0.2
                   (--since; every file with --tree): the comment-rule spot check. Markdown and plain-text files
                   count as comments as a whole.

Allow-list Tools/leak_allow.json
  {"version": 1, "allow": [{"path": "<glob>", "class": "<hard class, optional>", "pattern": "<regex>",
  "reason": "<text>"}]}: a hit of path.drive, path.profile or email is allowed when its path matches the glob (the
  manifest's glob rules), the class matches (when given) and the regex is found in the hit's text (the path-like run
  or the address). Token hits are never allow-listed - the allow-list is committed; narrow the local pattern instead.
  Entries that allow nothing are listed in the report.

Report and exit
  --out (default Spike/out/leak/leak_report.txt): the first and the last line are
  "SUMMARY: N hard, M soft (...), K unclassified; F files scanned; tokens T"; then the hard hits (path:line, class,
  masked excerpt), the coverage (counts per pattern, unclassified paths), the soft counts per class with the files that
  hold the most, the skipped files, the allow entries that allowed nothing.
  Exit 0: no hard hit and no unclassified path; 1: hard hits or unclassified paths; 2: setup error (manifest,
  allow-list, token file, git).

Usage (Python 3, standard library only; run with -I)
  python -I Tools/leak_check.py                        # the tracked files of this repository
  python -I Tools/leak_check.py --tree <dir>           # an exported tree
  python -I Tools/leak_check.py --manifest-coverage    # the classification only; lists every unclassified path
  python -I Tools/leak_check.py --list-files           # print the exported paths (one per line) and exit
  python -I Tools/leak_check.py --self-test            # the policy classes' self-test alone
  python -I Tools/leak_check.py --public [...]         # with the public variants of the manifest and allow-list

Public variants
  The manifest may list 'devOnly' patterns (paths that exist only in the development tree) and give 'whyPublic' reasons
  and an 'aboutPublic' text; the allow-list may mark entries "devOnly": true. The public variant (public_manifest,
  public_allow) leaves those patterns and entries out and uses the public texts; every 'devOnly' / 'whyPublic' key must
  name a listed pattern (a setup error otherwise). A manifest without these keys is its own public variant.
"""
import argparse
import bisect
import json
import os
import re
import subprocess
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

SIZE_CAP = 4 * 1024 * 1024
TEXT_EXT = {
    ".cs", ".py", ".ps1", ".psm1", ".psd1", ".md", ".txt", ".json", ".tsv", ".csv", ".shader", ".hlsl", ".cginc",
    ".compute", ".uss", ".uxml", ".tss", ".asmdef", ".asmref", ".asset", ".meta", ".unity", ".prefab", ".mat",
    ".yml", ".yaml", ".xml", ".ini", ".cfg", ".rsp", ".bat", ".cmd", ".sh", ".lighting", ".controller", ".anim",
    ".overridecontroller", ".mask", ".physicmaterial", ".preset", ".rendertexture", ".shadergraph",
    ".shadersubgraph", ".shadervariants", ".spriteatlas", ".terrainlayer", ".mixer", ".playable", ".signal",
    ".inputactions", ".fontsettings", ".guiskin", ".flare", ".brush", ".giparams", ".html", ".css", ".js", ".toml",
    ".sln", ".slnx", ".csproj", ".props", ".targets", ".config", ".editorconfig", ".gitignore", ".gitattributes",
}
TEXT_NAMES = {"license", "licence", "copying", "notice", ".gitignore", ".gitattributes", ".editorconfig"}

HARD = ("path.drive", "path.profile", "email", "token", "manifest.unclassified", "manifest.excluded", "policy.uncensor",
        "policy.mosaicSwitch")
SOFT = ("cite.section", "cite.spec", "method.comment")
ALLOWABLE = ("path.drive", "path.profile", "email")

RX = {
    "path.drive": re.compile(r"\b[A-Za-z]:[\\/]"),
    "path.profile": re.compile(r"[Uu][Ss][Ee][Rr][Ss][\\/]"),
    "email": re.compile(r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}"),
    "cite.section": re.compile(chr(0xA7)),          # the section sign
    "cite.spec": re.compile(r"\b(?:SPEC|PREP)_\w+"),
    "method.comment": re.compile(r"\b(?:ChaControl|ChaFile\w*|CustomBase|Cvs\w+|OCI[A-Z]\w*)\.\w+\("),
}
# the path-like run after a path hit (what an allow pattern is matched against)
PATH_RUN = re.compile(r"[^\s\"'<>|*?,;()\[\]{}`]*")


# ------------------------------------------------------------------------------------------------- the release policy
def _j(*parts):
    return "".join(parts)


# names of the uncensor path the official tree must not contain (fragments: this file must not hit itself)
POLICY_UNCENSOR_TOKENS = (
    _j("Uncensor", "Applier"), _j("KK", "_", "Uncensor", "Selector"), _j("uncensor", "Overlay"), _j("uncensor", "Underlay"),
    _j("Find", "Uncensor", "Body"), _j("Uncensor", "Bodies"), _j("additional", "_", "fknodes"), _j("HS2Next", "Private"),
)
RX["policy.uncensor"] = re.compile("|".join(re.escape(t) for t in POLICY_UNCENSOR_TOKENS))
SWITCH_WORD = re.compile(_j("mos", "aic") + "|" + _j("cen", "sor"), re.IGNORECASE)
SWITCH_ALLOWED = {_j("render.", "mos", "aicWide").lower(), _j("mos", "aicWide").lower()}
RX_ARG = re.compile(r'\.Arg\(\s*@?"([^"]+)"')
RX_KEYLIT = re.compile(r'"([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"')
RX_STRLIT = re.compile(r'"([^"\\\n]*)"')
RX_FLAG = re.compile(r'"(-[A-Za-z][\w\-]*)"')
RX_PSPARAM = re.compile(r'\[(?:switch|string|int|bool|double|string\[\])\]\s*\$(\w+)', re.IGNORECASE)
RX_CLASS = re.compile(r'\b(?:class|struct)\s+(\w+)')
RX_FIELD = re.compile(r'^\s*public\s+(static\s+)?(?!readonly\b|const\b|override\b|abstract\b|virtual\b)'
                      r'([A-Za-z_][\w<>,\[\]?. ]*?)\s+([A-Za-z_]\w*)\s*(=|;|\{\s*get)')


def switch_name(n):
    return SWITCH_WORD.search(n) is not None and n.lower() not in SWITCH_ALLOWED


def policy_switch_hits(path, text):
    """[(at, name, what)]: the switch-like names of one exported file (see the module docstring)."""
    hits = []
    low = path.lower()
    ext = os.path.splitext(low)[1]
    runtime = ext == ".cs" and "/editor/" not in "/" + low and "/tests/" not in "/" + low
    if ext == ".cs":
        for m in RX_ARG.finditer(text):
            if switch_name(m.group(1)):
                hits.append((m.start(), m.group(1), "command argument"))
        if re.search(r"\bclass\s+OptionDefs\b", text):
            for m in RX_KEYLIT.finditer(text):
                if switch_name(m.group(1)):
                    hits.append((m.start(), m.group(1), "option key"))
        if re.search(r"\bclass\s+SettingsKeys\b", text):
            for m in RX_STRLIT.finditer(text):
                if switch_name(m.group(1)):
                    hits.append((m.start(), m.group(1), "settings key"))
        if runtime:
            for m in RX_FLAG.finditer(text):
                if switch_name(m.group(1)):
                    hits.append((m.start(), m.group(1), "command-line flag"))
            # public fields: static non-readonly anywhere, any public field of an *Options / *Settings / *Switches class
            stack, depth, pos = [], 0, 0
            for line in text.splitlines(True):
                code = line.split("//", 1)[0]
                fm = RX_FIELD.match(code)
                if fm and "(" not in code.split("=", 1)[0]:
                    cls = stack[-1][0] if stack else ""
                    if (fm.group(1) or re.search(r"(Options|Settings|Switches)$", cls)) and switch_name(fm.group(3)):
                        hits.append((pos + fm.start(3), fm.group(3), "switch field" + (" of " + cls if cls else "")))
                cm = RX_CLASS.search(code)
                opens, closes = code.count("{"), code.count("}")
                if cm:
                    stack.append((cm.group(1), depth))
                depth += opens - closes
                while stack and depth <= stack[-1][1] and closes > 0:
                    stack.pop()
                pos += len(line)
    elif ext in (".ps1", ".psm1"):
        for m in RX_PSPARAM.finditer(text):
            if switch_name(m.group(1)):
                hits.append((m.start(), m.group(1), "script parameter"))
    elif low.endswith("config/settings.example.json"):
        try:
            j = json.loads(text)
        except ValueError:
            j = None

        def walk(o, prefix):
            if isinstance(o, dict):
                for k, v in o.items():
                    name = prefix + k
                    if switch_name(name) or switch_name(k):
                        at = text.find('"' + k + '"')
                        hits.append((max(at, 0), name, "settings key"))
                    walk(v, name + ".")
        walk(j, "")
    return hits


def policy_self_test():
    """Runs the two policy classes on generated fixture files through the scan (--tree) and checks the exact hits;
    returns a list of problems (empty: passed) and the number of cases."""
    import tempfile
    import shutil
    tok = POLICY_UNCENSOR_TOKENS
    mo = _j("Mos", "aic")
    fixtures = {
        "Assets/HS2Next/Runtime/A.cs": "class A { void F() { %s.Apply(null); } }\n" % tok[0],
        "Assets/HS2Next/Runtime/Data/Ext/UncensorData.cs": "// the <%s> data of a card\nclass D { }\n" % tok[1],
        "Assets/HS2Next/Runtime/B.cs": 'class B { void R() { S.Arg("no%s", 1).Arg("%s", 2).Arg("key", 3); } }\n' % (mo, _j("mos", "aicWide")),
        "Assets/HS2Next/Runtime/UI/Opts.cs": 'static class OptionDefs { const string A = "render.%sOff", B = "render.%sWide"; }\n' % (_j("mos", "aic"), _j("mos", "aic")),
        "Config/settings.example.json": '{"render": {"%sBlock": 4, "aa": "smaa"}}\n' % _j("mos", "aic"),
        "Assets/HS2Next/Runtime/C.cs": ("class C\n{\n    public static bool AllowUn%s;\n    public static readonly bool %sX = true;\n}\n"
                                         "class RenderOptions\n{\n    public bool %s;\n    public int %sBlock = 2;\n}\n"
                                         % (_j("cen", "sor"), mo, _j("mos", "aicWide"), _j("mos", "aic"))),
        "Assets/HS2Next/Runtime/D.cs": 'class D2 { bool F(string[] a) => System.Array.IndexOf(a, "-no%s") >= 0; }\n' % _j("mos", "aic"),
        "Tools/x.ps1": "param([switch]$No%s, [string]$Name)\n" % mo,
        "Assets/HS2Next/Runtime/E.cs": ('// the %s of the members\nclass E { void F() { Spec("chara.%s"); Find("me.%sProtected"); '
                                         'var r = new MemberInfo(); } }\nclass MemberInfo { public bool Restored; public string Mode; }\n'
                                         % (_j("mos", "aic"), _j("mos", "aic"), _j("mos", "aic"))),
        "Assets/HS2Next/Editor/T.cs": "class T { public static bool %sForTest; }\n" % mo,
    }
    expect = sorted([
        ("policy.uncensor", "Assets/HS2Next/Runtime/A.cs"),
        ("policy.mosaicSwitch", "Assets/HS2Next/Runtime/B.cs"),
        ("policy.mosaicSwitch", "Assets/HS2Next/Runtime/UI/Opts.cs"),
        ("policy.mosaicSwitch", "Config/settings.example.json"),
        ("policy.mosaicSwitch", "Assets/HS2Next/Runtime/C.cs"),
        ("policy.mosaicSwitch", "Assets/HS2Next/Runtime/C.cs"),
        ("policy.mosaicSwitch", "Assets/HS2Next/Runtime/D.cs"),
        ("policy.mosaicSwitch", "Tools/x.ps1"),
    ])
    top = tempfile.mkdtemp(prefix="leak_policy_selftest_")
    problems = []
    try:
        tree = os.path.join(top, "tree")
        for rel, text in fixtures.items():
            full = os.path.join(tree, *rel.split("/"))
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
        man = os.path.join(top, "manifest.json")
        inc = ["Assets/**", "Config/settings.example.json", "Tools/*.ps1"]
        with open(man, "w", encoding="utf-8") as f:
            json.dump({"version": 1, "include": inc, "exclude": [], "why": {p: "self-test" for p in inc}}, f)
        allow = os.path.join(top, "allow.json")
        with open(allow, "w", encoding="utf-8") as f:
            json.dump({"version": 1, "allow": [], "policy_data": [{"path": "Assets/HS2Next/Runtime/Data/Ext/UncensorData.cs",
                                                                  "reason": "self-test"}]}, f)

        class A:
            pass
        a = A()
        a.root, a.tree, a.manifest, a.allow, a.tokens = ROOT, tree, man, allow, os.path.join(top, "no_tokens.txt")
        a.since, a.manifest_coverage, a.list_files = "v0.2", False, False
        r = scan(a, self_test=False)
        got = sorted((h[0], h[1]) for h in r["hits"] if h[0].startswith("policy."))
        if got != expect:
            problems.append("hits %s, expected %s" % (got, expect))
    finally:
        shutil.rmtree(top, ignore_errors=True)
    return problems, len(fixtures)

C_LIKE = {".cs", ".shader", ".hlsl", ".cginc", ".compute", ".uss", ".js", ".css"}
# the save-format containers are ZIP files: their text entries are scanned
CONTAINER_EXT = {".hs2nscene", ".hs2nchara", ".hs2ncoord", ".hs2npose", ".zip"}
HASH_LIKE = {".py", ".ps1", ".psm1", ".psd1", ".sh", ".toml", ".yml", ".yaml", ".ini", ".cfg"}


class SetupError(Exception):
    pass


# ---------------------------------------------------------------------------------------------- globs and the manifest
def glob_regex(g):
    """'**/' any folders (or none), '**' anything, '*' and '?' inside one folder; anchored, case-insensitive."""
    out, i = ["^"], 0
    while i < len(g):
        c = g[i]
        if c == "*" and i + 1 < len(g) and g[i + 1] == "*":
            if i + 2 < len(g) and g[i + 2] == "/":
                out.append("(?:.*/)?")
                i += 3
            else:
                out.append(".*")
                i += 2
            continue
        if c == "*":
            out.append("[^/]*")
        elif c == "?":
            out.append("[^/]")
        else:
            out.append(re.escape(c))
        i += 1
    out.append("$")
    return re.compile("".join(out), re.IGNORECASE)


def is_literal(g):
    return not any(ch in g for ch in "*?[")


class Manifest:
    def __init__(self, path):
        try:
            with open(path, encoding="utf-8") as f:
                m = json.load(f)
        except (OSError, ValueError) as e:
            raise SetupError("manifest %s: %s" % (path, e))
        if m.get("version") != 1:
            raise SetupError("manifest: version must be 1")
        inc, exc, why = m.get("include"), m.get("exclude"), m.get("why")
        if not isinstance(inc, list) or not isinstance(exc, list) or not isinstance(why, dict):
            raise SetupError("manifest: 'include' and 'exclude' must be lists and 'why' an object")
        pats = inc + exc
        dup = sorted({p for p in pats if pats.count(p) > 1})
        if dup:
            raise SetupError("manifest: patterns listed twice: %s" % ", ".join(dup))
        missing = [p for p in pats if not str(why.get(p, "")).strip()]
        if missing:
            raise SetupError("manifest: patterns without a reason in 'why': %s" % ", ".join(missing))
        extra = [k for k in why if k not in pats]
        if extra:
            raise SetupError("manifest: 'why' names patterns that are not listed: %s" % ", ".join(extra))
        dev, whyp = m.get("devOnly", []), m.get("whyPublic", {})
        if not isinstance(dev, list) or not isinstance(whyp, dict):
            raise SetupError("manifest: 'devOnly' must be a list and 'whyPublic' an object")
        dangling = [k for k in list(dev) + list(whyp) if k not in pats]
        if dangling:
            raise SetupError("manifest: 'devOnly' / 'whyPublic' name patterns that are not listed: %s" % ", ".join(dangling))
        both = [k for k in whyp if k in dev or not str(whyp[k]).strip()]
        if both:
            raise SetupError("manifest: 'whyPublic' for a devOnly pattern or without text: %s" % ", ".join(both))
        self.include = [(p, glob_regex(p), is_literal(p)) for p in inc]
        self.exclude = [(p, glob_regex(p)) for p in exc]
        self.counts = {p: 0 for p in pats}

    def classify(self, path):
        """'include', 'exclude' or None (named by neither side). Counts the deciding pattern."""
        inc = [(p, lit) for (p, rx, lit) in self.include if rx.match(path)]
        lit = [p for (p, l) in inc if l]
        if lit:
            self.counts[lit[0]] += 1
            return "include"
        exc = [p for (p, rx) in self.exclude if rx.match(path)]
        if exc:
            self.counts[exc[0]] += 1
            return "exclude"
        if inc:
            self.counts[inc[0][0]] += 1
            return "include"
        return None


def public_manifest(m):
    """The public variant of a manifest (a dict): the 'devOnly' patterns and their reasons left out, 'whyPublic' used
    where given, 'aboutPublic' as 'about'; the keys of the variant itself are dropped. A manifest without those keys
    comes back unchanged."""
    dev = set(m.get("devOnly", []))
    whyp = m.get("whyPublic", {})
    out = {}
    for k, v in m.items():
        if k in ("devOnly", "whyPublic", "aboutPublic"):
            continue
        if k == "about":
            out[k] = m.get("aboutPublic", v)
        elif k in ("include", "exclude"):
            out[k] = [p for p in v if p not in dev]
        elif k == "why":
            out[k] = {p: whyp.get(p, r) for p, r in v.items() if p not in dev}
        else:
            out[k] = v
    return out


def public_allow(a):
    """The public variant of an allow-list (a dict): entries marked "devOnly": true left out, 'aboutPublic' as 'about'."""
    out = {}
    for k, v in a.items():
        if k == "aboutPublic":
            continue
        if k == "about":
            out[k] = a.get("aboutPublic", v)
        elif k == "allow":
            out[k] = [{x: y for x, y in e.items() if x != "devOnly"} for e in v if not e.get("devOnly")]
        else:
            out[k] = v
    return out


# ------------------------------------------------------------------------------------------ allow-list and token file
class AllowList:
    def __init__(self, path):
        self.entries = []
        self.policy_data = []
        if not os.path.exists(path):
            return
        try:
            with open(path, encoding="utf-8") as f:
                a = json.load(f)
        except (OSError, ValueError) as e:
            raise SetupError("allow-list %s: %s" % (path, e))
        if a.get("version") != 1 or not isinstance(a.get("allow"), list):
            raise SetupError("allow-list: version 1 with an 'allow' list expected")
        for n, e in enumerate(a["allow"], 1):
            if not isinstance(e, dict) or not e.get("path") or not e.get("pattern") or not str(e.get("reason", "")).strip():
                raise SetupError("allow-list entry %d: 'path', 'pattern' and 'reason' are required" % n)
            cls = e.get("class")
            if cls is not None and cls not in ALLOWABLE:
                raise SetupError("allow-list entry %d: class must be one of %s" % (n, ", ".join(ALLOWABLE)))
            try:
                rx = re.compile(e["pattern"])
            except re.error as x:
                raise SetupError("allow-list entry %d: pattern does not compile (%s)" % (n, x))
            self.entries.append({"n": n, "path": e["path"], "prx": glob_regex(e["path"]), "class": cls, "rx": rx,
                                 "used": 0})
        for n, e in enumerate(a.get("policy_data", []), 1):
            if not isinstance(e, dict) or not e.get("path") or not str(e.get("reason", "")).strip():
                raise SetupError("allow-list policy_data entry %d: 'path' and 'reason' are required" % n)
            self.policy_data.append({"n": n, "path": e["path"], "prx": glob_regex(e["path"]), "used": 0})

    def policy_data_file(self, path):
        """True for a file of the data allow-list of policy.uncensor (the inert uncensor data and its tests)."""
        for e in self.policy_data:
            if e["prx"].match(path):
                e["used"] += 1
                return True
        return False

    def allows(self, path, cls, text):
        if cls not in ALLOWABLE:
            return False
        for e in self.entries:
            if e["prx"].match(path) and (e["class"] is None or e["class"] == cls) and e["rx"].search(text):
                e["used"] += 1
                return True
        return False


def read_tokens(path):
    """[(line number, compiled regex)]; None when the file does not exist."""
    if not os.path.exists(path):
        return None
    toks = []
    with open(path, encoding="utf-8-sig") as f:
        for n, line in enumerate(f, 1):
            s = line.strip()
            if not s or s.startswith("#"):
                continue
            try:
                toks.append((n, re.compile(s, re.IGNORECASE)))
            except re.error:
                raise SetupError("token file line %d: the regex does not compile" % n)
    return toks


# ---------------------------------------------------------------------------------------------------------- git, files
def git(root, *args):
    try:
        r = subprocess.run(["git", "-C", root] + list(args), capture_output=True)
    except OSError as e:
        raise SetupError("git: %s" % e)
    return r.returncode, r.stdout


def tracked_files(root):
    code, out = git(root, "ls-files", "-z")
    if code != 0:
        raise SetupError("git ls-files failed in %s" % root)
    return [p for p in out.decode("utf-8", "replace").split("\0") if p]


def tree_files(top):
    out = []
    for dirpath, dirnames, filenames in os.walk(top, followlinks=False):
        dirnames[:] = [d for d in dirnames if d != ".git" and not os.path.islink(os.path.join(dirpath, d))
                       and not is_reparse(os.path.join(dirpath, d))]
        for fn in filenames:
            out.append(os.path.relpath(os.path.join(dirpath, fn), top).replace("\\", "/"))
    return sorted(out)


def is_reparse(p):
    try:
        return bool(os.lstat(p).st_file_attributes & 0x400)   # FILE_ATTRIBUTE_REPARSE_POINT (Windows)
    except (AttributeError, OSError):
        return False


def changed_since(root, rev):
    """Paths changed between rev and the working tree; None when rev does not exist."""
    code, _ = git(root, "rev-parse", "--verify", "-q", rev + "^{commit}")
    if code != 0:
        return None
    code, out = git(root, "diff", "--name-only", "--no-renames", "-z", rev, "--")
    if code != 0:
        return None
    return {p for p in out.decode("utf-8", "replace").split("\0") if p}


def rel_or_abs(path, root):
    """path relative to root, '/' separated; the path itself when it is on another drive."""
    try:
        return os.path.relpath(path, root).replace("\\", "/")
    except ValueError:
        return path.replace("\\", "/")


def is_text(path):
    base = os.path.basename(path).lower()
    ext = os.path.splitext(base)[1]
    return ext in TEXT_EXT or base in TEXT_NAMES


def read_text(full):
    with open(full, "rb") as f:
        b = f.read()
    if b.startswith(b"\xff\xfe") or b.startswith(b"\xfe\xff"):
        return b.decode("utf-16", "replace")
    if b"\0" in b[:8192]:
        return None
    return b.decode("utf-8-sig", "replace")


# -------------------------------------------------------------------------------------------------------- the scan
class Masker:
    def __init__(self, tokens):
        self.tokens = tokens or []

    def __call__(self, s):
        for n, rx in self.tokens:
            s = rx.sub("<token %d>" % n, s)
        return s


def comment_mask(text, ext):
    """Character spans of comments (start, end) for the method-name check; Markdown is a comment as a whole."""
    if ext == ".md" or ext == ".txt":
        return [(0, len(text))]
    spans = []
    if ext in C_LIKE:
        i, n = 0, len(text)
        in_str = None
        while i < n:
            c = text[i]
            if in_str:
                if c == "\\":
                    i += 2
                    continue
                if c == in_str or c == "\n":
                    in_str = None
                i += 1
                continue
            if c == '"' or (c == "'" and ext == ".cs"):
                in_str = c
                i += 1
                continue
            if text.startswith("//", i):
                j = text.find("\n", i)
                j = n if j < 0 else j
                spans.append((i, j))
                i = j
                continue
            if text.startswith("/*", i):
                j = text.find("*/", i + 2)
                j = n if j < 0 else j + 2
                spans.append((i, j))
                i = j
                continue
            i += 1
        return spans
    if ext in HASH_LIKE:
        pos = 0
        in_block = None
        for line in text.splitlines(True):
            s = line.lstrip()
            if ext == ".py":
                q = line.count('"""') + line.count("'''")
                if in_block or q:
                    spans.append((pos, pos + len(line)))
                    if q % 2 == 1:
                        in_block = not in_block
                    pos += len(line)
                    continue
            if ext in (".ps1", ".psm1", ".psd1"):
                if in_block or "<#" in line:
                    spans.append((pos, pos + len(line)))
                    if "<#" in line and "#>" not in line[line.find("<#"):]:
                        in_block = True
                    elif "#>" in line:
                        in_block = False
                    pos += len(line)
                    continue
            k = line.find("#")
            if k >= 0 and not s.startswith(("'", '"')):
                spans.append((pos + k, pos + len(line)))
            pos += len(line)
        return spans
    return []


def in_spans(spans, at):
    for a, b in spans:
        if a <= at < b:
            return True
    return False


def scan(args, self_test=True):
    st_problems, st_cases = (policy_self_test() if self_test else ([], 0))
    if st_problems:
        raise SetupError("the policy self-test failed: " + "; ".join(st_problems))
    root = os.path.abspath(args.root)
    manifest = Manifest(args.manifest)
    allow = AllowList(args.allow)
    token_file = os.path.join(root, "Config", "leak_tokens.local.txt") if args.tokens is None else args.tokens
    tree = os.path.abspath(args.tree) if args.tree else None

    # the token file must never be tracked and must be ignored by git
    if not tree and os.path.exists(token_file):
        rel = os.path.relpath(token_file, root).replace("\\", "/")
        if not rel.startswith(".."):
            code, out = git(root, "ls-files", "--", rel)
            if out.strip():
                raise SetupError("the local token file is tracked by git: remove it from the index")
            code, _ = git(root, "check-ignore", "-q", "--", rel)
            if code != 0:
                raise SetupError("the local token file is not ignored by git (.gitignore)")
    tokens = read_tokens(token_file)
    mask = Masker(tokens)

    files = tree_files(tree) if tree else tracked_files(root)
    base = tree or root
    since = None if tree else changed_since(root, args.since)

    hits = []                      # (class, path, line, excerpt, text)
    soft = {c: [] for c in SOFT}   # (path, line)
    unclassified, excluded_present, exported = [], [], []
    for p in files:
        c = manifest.classify(p)
        if c is None:
            unclassified.append(p)
        elif c == "exclude":
            if tree:
                excluded_present.append(p)
        else:
            exported.append(p)

    if args.list_files:
        for p in exported:
            print(p)
        return None

    for p in unclassified:
        hits.append(("manifest.unclassified", p, 0, "", p))
    for p in excluded_present:
        hits.append(("manifest.excluded", p, 0, "", p))

    allowed = [0]

    def check_text(shown, p, text, ext, methods):
        """Every class over one text; 'shown' names it in the report (a container entry: 'file!entry'), 'p' is the
        exported path the allow-list globs see."""
        starts = [0] + [m.end() for m in re.finditer("\n", text)]

        def line_of(at):
            return bisect.bisect_right(starts, at)

        def excerpt(at):
            ln = line_of(at)
            a = starts[ln - 1]
            b = text.find("\n", a)
            b = len(text) if b < 0 else b
            s = text[a:b].strip()
            if len(s) > 160:
                k = max(0, at - a - 60)
                s = ("..." if k else "") + text[a + k:a + k + 150].strip() + "..."
            return mask(s)

        for cls in ("path.drive", "path.profile", "email"):
            for m in RX[cls].finditer(text):
                t = m.group(0) if cls == "email" else m.group(0) + PATH_RUN.match(text, m.end()).group(0)
                if allow.allows(p, cls, t):
                    allowed[0] += 1
                    continue
                hits.append((cls, shown, line_of(m.start()), excerpt(m.start()), t))
        for n, rx in (tokens or []):
            for m in rx.finditer(text):
                hits.append(("token", shown, line_of(m.start()), "token line %d: %s" % (n, excerpt(m.start())), ""))
        # the release policy
        if not allow.policy_data_file(p):
            for m in RX["policy.uncensor"].finditer(text):
                hits.append(("policy.uncensor", shown, line_of(m.start()), excerpt(m.start()), m.group(0)))
        for at, name, what in policy_switch_hits(p, text):
            hits.append(("policy.mosaicSwitch", shown, line_of(at), "%s '%s': %s" % (what, name, excerpt(at)), name))
        for cls in ("cite.section", "cite.spec"):
            seen = set()
            for m in RX[cls].finditer(text):
                ln = line_of(m.start())
                if ln not in seen:
                    seen.add(ln)
                    soft[cls].append((shown, ln))
        if methods:
            ms = list(RX["method.comment"].finditer(text))
            if ms:
                spans = comment_mask(text, ext)
                seen = set()
                for m in ms:
                    if in_spans(spans, m.start()):
                        ln = line_of(m.start())
                        if ln not in seen:
                            seen.add(ln)
                            soft["method.comment"].append((shown, ln))

    skipped_size, skipped_binary, skipped_type, scanned = [], [], 0, 0
    if not args.manifest_coverage:
        for p in exported:
            for n, rx in (tokens or []):
                if rx.search(p):
                    hits.append(("token", p, 0, "token line %d in the path" % n, ""))
            ext = os.path.splitext(p)[1].lower()
            container = ext in CONTAINER_EXT
            if not container and not is_text(p):
                skipped_type += 1
                continue
            full = os.path.join(base, p)
            try:
                size = os.path.getsize(full)
            except OSError:
                continue                       # tracked but deleted in the working tree
            if size > SIZE_CAP:
                skipped_size.append((p, size))
                continue
            methods = since is None or p in since
            if container:
                # save-format containers are ZIP files: their text entries (JSON) are scanned one by one
                try:
                    with zipfile.ZipFile(full) as z:
                        for info in z.infolist():
                            name = info.filename
                            for n, rx in (tokens or []):
                                if rx.search(name):
                                    hits.append(("token", p + "!" + name, 0, "token line %d in the entry name" % n, ""))
                            if not is_text(name) or info.file_size > SIZE_CAP:
                                continue
                            b = z.read(info)
                            if b"\0" in b[:8192]:
                                continue
                            check_text(p + "!" + name, p, b.decode("utf-8-sig", "replace"),
                                       os.path.splitext(name)[1].lower(), methods)
                    scanned += 1
                except (zipfile.BadZipFile, OSError, RuntimeError, NotImplementedError):
                    skipped_binary.append(p)
                continue
            text = read_text(full)
            if text is None:
                skipped_binary.append(p)
                continue
            scanned += 1
            check_text(p, p, text, ext, methods)
    allowed = allowed[0]

    # the export must not ship a path of the private folder either (its name is a policy token)
    for p in exported:
        if RX["policy.uncensor"].search(p) and not allow.policy_data_file(p):
            hits.append(("policy.uncensor", p, 0, "in the path", ""))
    return {"root": root, "tree": tree, "files": files, "exported": exported, "hits": hits, "allowed": allowed,
            "self_test": st_cases,
            "soft": soft, "unclassified": unclassified, "excluded_present": excluded_present, "scanned": scanned,
            "skipped_size": skipped_size, "skipped_binary": skipped_binary, "skipped_type": skipped_type,
            "tokens": tokens, "token_file_rel": rel_or_abs(token_file, root),
            "manifest": manifest, "allow": allow, "since": since, "mask": mask}


# ------------------------------------------------------------------------------------------------------------- report
def summary_line(r, coverage_only):
    hard = [h for h in r["hits"] if h[0] != "manifest.unclassified"]
    nsoft = sum(len(v) for v in r["soft"].values())
    soft_txt = ", ".join("%s %d" % (c, len(r["soft"][c])) for c in SOFT)
    tok = ("%d patterns" % len(r["tokens"])) if r["tokens"] is not None else "none (no local token file: generic classes only)"
    if coverage_only:
        return "SUMMARY: %d unclassified of %d paths (coverage only)" % (len(r["unclassified"]), len(r["files"]))
    return "SUMMARY: %d hard, %d soft (%s), %d unclassified; %d files scanned; tokens %s" % (
        len(hard), nsoft, soft_txt, len(r["unclassified"]), r["scanned"], tok)


def write_report(r, out, coverage_only):
    mask = r["mask"]
    L = [summary_line(r, coverage_only)]
    L.append("source: %s; %d paths, %d exported; manifest Tools/export_manifest.json; allow-list Tools/leak_allow.json" % (
        "tree " + mask(r["tree"]) if r["tree"] else "tracked files (git ls-files)", len(r["files"]), len(r["exported"])))
    if r["tokens"] is None:
        L.append("tokens: no local token file (%s); the private-token class did not run" % r["token_file_rel"])
    else:
        L.append("tokens: %d patterns from the local token file" % len(r["tokens"]))
    if r["since"] is None and not coverage_only:
        L.append("method.comment: every exported file (no history: %s)" % ("an exported tree" if r["tree"] else "no tag"))
    L.append("policy self-test: %d fixture files, passed" % r.get("self_test", 0))
    L.append("")

    counts = {c: 0 for c in HARD}
    for h in r["hits"]:
        counts[h[0]] += 1
    L.append("## Hard classes")
    L.append("  " + ", ".join("%s %d" % (c, counts[c]) for c in HARD) + "; allowed by the allow-list %d" % r["allowed"])
    for cls in HARD:
        hs = [h for h in r["hits"] if h[0] == cls]
        if not hs or cls == "manifest.unclassified":
            continue
        L.append("")
        L.append("### %s (%d)" % (cls, len(hs)))
        for (_, p, ln, ex, _) in hs:
            L.append("  %s:%d  %s" % (mask(p), ln, ex) if ln else "  %s  %s" % (mask(p), ex))
    L.append("")

    L.append("## Coverage (every path named by the manifest)")
    m = r["manifest"]
    L.append("  unclassified: %d" % len(r["unclassified"]))
    for p in r["unclassified"]:
        L.append("    %s" % mask(p))
    L.append("  paths decided per pattern (include):")
    for (p, _, _) in m.include:
        L.append("    %6d  %s" % (m.counts[p], p))
    L.append("  paths decided per pattern (exclude):")
    for (p, _) in m.exclude:
        L.append("    %6d  %s" % (m.counts[p], p))
    L.append("")

    if not coverage_only:
        L.append("## Soft classes (counted; the export rewrites or reviews them)")
        for cls in SOFT:
            v = r["soft"][cls]
            per = {}
            for (p, _) in v:
                per[p] = per.get(p, 0) + 1
            L.append("  %s: %d lines in %d files" % (cls, len(v), len(per)))
            top = sorted(per.items(), key=lambda kv: (-kv[1], kv[0]))
            show = top if cls == "method.comment" else top[:15]
            for p, n in show:
                L.append("    %5d  %s" % (n, mask(p)))
            if cls == "method.comment":
                for (p, ln) in v:
                    L.append("      %s:%d" % (mask(p), ln))
        L.append("")
        L.append("## Not scanned")
        L.append("  not text by extension: %d; binary (NUL byte): %d; over 4 MB: %d" % (
            r["skipped_type"], len(r["skipped_binary"]), len(r["skipped_size"])))
        for p, size in r["skipped_size"]:
            L.append("    %s (%d bytes)" % (mask(p), size))
        for p in r["skipped_binary"]:
            L.append("    binary: %s" % mask(p))
        unused = [e for e in r["allow"].entries if e["used"] == 0]
        L.append("")
        L.append("## Allow-list entries that allowed nothing: %d" % len(unused))
        for e in unused:
            L.append("  entry %d: %s" % (e["n"], e["path"]))
        L.append("  policy_data entries (files read as inert data): %d, matching a scanned file: %d" % (
            len(r["allow"].policy_data), sum(1 for e in r["allow"].policy_data if e["used"] > 0)))
        L.append("")
    L.append(L[0])
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L) + "\n")
    return L


def main():
    ap = argparse.ArgumentParser(description="the publication guard of HS2Next (see the module docstring)")
    ap.add_argument("--root", default=ROOT, help="the repository (default: the parent of Tools)")
    ap.add_argument("--tree", help="scan an exported tree instead of the tracked files")
    ap.add_argument("--manifest", default=os.path.join(ROOT, "Tools", "export_manifest.json"))
    ap.add_argument("--allow", default=os.path.join(ROOT, "Tools", "leak_allow.json"))
    ap.add_argument("--tokens", default=None, help="the local token file (default <root>/Config/leak_tokens.local.txt)")
    ap.add_argument("--since", default="v0.2", help="the revision of the method-name check (default v0.2)")
    ap.add_argument("--out", default=os.path.join(ROOT, "Spike", "out", "leak", "leak_report.txt"))
    ap.add_argument("--manifest-coverage", action="store_true", help="the classification only")
    ap.add_argument("--list-files", action="store_true", help="print the exported paths and exit")
    ap.add_argument("--self-test", action="store_true", help="run the policy classes' self-test alone and exit")
    ap.add_argument("--public", action="store_true",
                    help="check with the public variants of the manifest and the allow-list (written to a temp folder)")
    args = ap.parse_args()
    if args.public and not args.tree:
        print("leak_check: --public checks an exported tree (--tree <dir>); the development tree has development-only paths")
        return 2
    if args.public:
        import atexit
        import shutil
        import tempfile
        tmp = tempfile.mkdtemp(prefix="leak_public_")
        atexit.register(shutil.rmtree, tmp, True)
        try:
            with open(args.manifest, encoding="utf-8") as f:
                m = public_manifest(json.load(f))
            a = {"version": 1, "allow": []}
            if os.path.exists(args.allow):
                with open(args.allow, encoding="utf-8") as f:
                    a = public_allow(json.load(f))
        except (OSError, ValueError) as e:
            print("leak_check: setup error: %s" % e)
            return 2
        args.manifest, args.allow = os.path.join(tmp, "export_manifest.json"), os.path.join(tmp, "leak_allow.json")
        for p, d in ((args.manifest, m), (args.allow, a)):
            with open(p, "w", encoding="utf-8") as f:
                json.dump(d, f)
    if args.self_test:
        problems, cases = policy_self_test()
        print("policy self-test: %d fixture files, %s" % (cases, "passed" if not problems else "FAILED: " + "; ".join(problems)))
        return 0 if not problems else 1
    if args.tree and not os.path.isdir(args.tree):
        print("leak_check: --tree is not a folder")
        return 2
    try:
        r = scan(args)
    except SetupError as e:
        print("leak_check: setup error: %s" % e)
        return 2
    if r is None:
        return 0
    L = write_report(r, args.out, args.manifest_coverage)
    mask = r["mask"]
    print(L[0])
    shown = 0
    for h in r["hits"]:
        if shown >= 20:
            print("  ... (%d more; see the report)" % (len(r["hits"]) - shown))
            break
        cls, p, ln, ex, _ = h
        print("  [%s] %s%s  %s" % (cls, mask(p), (":%d" % ln) if ln else "", ex))
        shown += 1
    print("report: %s" % mask(os.path.abspath(args.out)))
    return 0 if not r["hits"] else 1


if __name__ == "__main__":
    sys.exit(main())
