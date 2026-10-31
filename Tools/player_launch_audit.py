"""
player_launch_audit - every place in Tools/ that starts the HS2Next player gives it its own UserData.

A player started without -userData keeps its files under the per-user data root (%USERPROFILE%\\AppData\\LocalLow\\stp3\\HS2Next)
or, with portable.txt, next to the exe: a test or tool must never write there. This static check reads Tools/**/*.py and
Tools/**/*.ps1 (text only, nothing is run) and lists every process start whose argument list is a player's:

  - an argument list that holds a player flag (-bridgePort, -bridgeAccept, -smoke, -noApp, -screen-width, -texturePrescanQuit,
    -bridgeAllowQuit; -batchmode / -logFile next to an exe) or names the player (HS2Next.exe, $AppExe, $SmokeExe); Unity Editor
    starts (-projectPath / -executeMethod), PowerShell (-NoProfile / -Command), browsers (--headless) and named system tools are
    not players;
  - a list that holds only the exe (a bare start).

Each start passes when its arguments hold -userData, when they hold -noApp or -smoke (no app starts, so no UserData), or when
HS2NEXT_DATA_ROOT is set in the same function (the bare-exe rows point it into their output). Otherwise it is listed as
MISSING. A start whose argument list is a function parameter is plumbing when the table PLUMBING names it (its callers are
checked instead), else UNRESOLVED (a failure: name it there or pass a literal list).

Usage:
    python -I Tools/player_launch_audit.py               # the audit of Tools/; exit code = the starts without their own UserData
    python -I Tools/player_launch_audit.py --self-test   # the detector on built-in fixtures; exit 0 when every verdict is right
    options: --root <project>, --verbose (every start, not only the failing ones), --scratch <dir> (self-test fixtures)
"""
import argparse
import os
import re
import sys

# functions whose start call receives its argument list as a parameter; their call sites are audited instead
PLUMBING = {
    ("run_tests.ps1", "Invoke-Process"): "the runner's process helper; its player calls pass $AppExe / $SmokeExe with a literal list",
}
# files that start a program that is not HS2Next by name (checked by hand when the file changes)
NOT_PLAYER_FILES = {
    "hs2_compare.py": "starts HS2 Studio (the game's own exe) for the reference records",
}
SYSTEM_TOOLS = {"tasklist", "netstat", "git", "nvidia-smi", "taskkill", "netsh", "icacls", "reg", "reg.exe", "cmd", "cmd.exe",
                "powershell", "pwsh", "python", "py", "where", "robocopy", "explorer", "msedge", "dotnet"}
PLAYER_FLAGS = ("-bridgePort", "-bridgeAccept", "-smoke", "-noApp", "-screen-width", "-texturePrescanQuit", "-bridgeAllowQuit")
EDITOR_FLAGS = ("-projectPath", "-executeMethod", "-createProject")
NO_APP_FLAGS = ("-noApp", "-smoke")
QUOTES = "\"'"


def balanced(text, start, open_ch, close_ch):
    """The text from text[start] (an opening bracket) to its matching close, skipping quoted strings; None when unbalanced."""
    depth = 0
    quote = None
    i = start
    while i < len(text):
        c = text[i]
        if quote:
            if c == quote:
                quote = None
        elif c in QUOTES:
            quote = c
        elif c == "#":
            # a comment runs to the end of its line (an apostrophe in it opens no string)
            nl = text.find("\n", i)
            i = len(text) if nl < 0 else nl
            continue
        elif c == open_ch:
            depth += 1
        elif c == close_ch:
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
        i += 1
    return None


def statement(text, offset, stop_comma=False):
    """The expression from offset to the end of its statement: brackets balanced, quoted strings skipped, a line ending in a
    backtick, a backslash or '+' continues it; with stop_comma also a top-level comma ends it (a call's first argument)."""
    depth = 0
    quote = None
    for i in range(offset, len(text)):
        c = text[i]
        if quote:
            if c == quote:
                quote = None
        elif c in QUOTES:
            quote = c
        elif c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth < 0:
                return text[offset:i]
        elif depth == 0 and c == "\n":
            if not text[offset:i].rstrip().endswith(("`", "\\", "+")):
                return text[offset:i]
        elif depth == 0 and (c in ";#" or (stop_comma and c == ",")):
            return text[offset:i]
    return text[offset:]


def has_flag(argv, flag):
    return re.search("[" + QUOTES + "]" + re.escape(flag) + "[" + QUOTES + "]", argv) is not None


def classify(argv, lang):
    """'player', 'bare', or None (not a player start)."""
    if argv is None:
        return None
    a = argv.strip()
    first = re.match(r"[\[@(]*\s*[\"']([^\"']+)[\"']", a)
    if first and os.path.basename(first.group(1)).lower() in SYSTEM_TOOLS:
        return None
    if any(has_flag(a, f) for f in EDITOR_FLAGS) or has_flag(a, "-NoProfile") or has_flag(a, "-Command") or "--headless" in a:
        return None
    names_player = re.search(r"HS2Next\.exe|\$AppExe|\$SmokeExe", a) is not None
    exe_ref = re.search(r"\bexe\b|\.exe\b|\$exe\b", a, re.I) is not None
    if any(has_flag(a, f) for f in PLAYER_FLAGS):
        return "player"
    if (has_flag(a, "-batchmode") or has_flag(a, "-logFile")) and (exe_ref or names_player or lang == "ps1"):
        return "player"
    if names_player:
        return "player"
    # a list holding only an exe variable: [exe] / [a.exe] / @($exe)
    if re.fullmatch(r"[\[@(]\(?\s*\$?[\w.]*exe\s*\)?[\])]", a, re.I):
        return "bare"
    return None


def enclosing_function(lines, idx, lang):
    """(name, first line, last line) of the function holding line idx (0-based), or (None, 0, last line) at the top level."""
    if lang == "py":
        indent = len(lines[idx]) - len(lines[idx].lstrip())
        for j in range(idx - 1, -1, -1):
            m = re.match(r"^(\s*)def\s+(\w+)", lines[j])
            if not m or len(m.group(1)) >= indent:
                continue
            ind = len(m.group(1))
            end = len(lines) - 1
            for q in range(j + 1, len(lines)):
                t = lines[q]
                if t.strip() and (len(t) - len(t.lstrip())) <= ind and not t.lstrip().startswith(("#", ")", "]", "}")):
                    end = q - 1
                    break
            if end >= idx:
                return m.group(2), j, end
            indent = ind
        return None, 0, len(lines) - 1
    text = "\n".join(lines)
    for j in range(idx, -1, -1):
        m = re.match(r"^\s*function\s+([\w-]+)", lines[j], re.I)
        if not m:
            continue
        off = sum(len(l) + 1 for l in lines[:j])
        brace = text.find("{", off)
        body = balanced(text, brace, "{", "}") if brace >= 0 else None
        end = text.count("\n", 0, brace + len(body)) if body else len(lines) - 1
        if end >= idx:
            return m.group(1), j, end
    return None, 0, len(lines) - 1


def resolve_var(text, lines, call_line, var, lang):
    """The assignment of var nearest above call_line (0-based) and the later additions to it, as one text; None if absent."""
    name = re.escape("$" + var) if lang == "ps1" else re.escape(var)
    assign = re.compile(r"^\s*" + name + r"\s*=(?!=)\s*")
    add = re.compile(name + r"\s*(\+=|\.append|\.extend|\.insert)")
    for j in range(call_line, max(-1, call_line - 60), -1):
        m = assign.match(lines[j])
        if not m:
            continue
        offset = sum(len(l) + 1 for l in lines[:j]) + m.end()
        body = statement(text, offset)
        extra = [lines[q] for q in range(j + 1, call_line + 1) if add.search(lines[q])]
        return body + "\n" + "\n".join(extra)
    return None


def find_starts(path, text, rel):
    lang = "py" if path.endswith(".py") else "ps1"
    lines = text.split("\n")
    starts = []
    if lang == "py":
        call = re.compile(r"subprocess\.(Popen|run|call|check_call|check_output)\s*\(|os\.startfile\s*\(")
    else:
        call = re.compile(r"Start-Process\b|Invoke-Process\s+(?=['\"$])", re.I)
    for m in call.finditer(text):
        line = text.count("\n", 0, m.start())
        if lines[line].lstrip().startswith("#"):
            continue
        func, f0, f1 = enclosing_function(lines, line, lang)
        argv = None
        if lang == "py":
            args = balanced(text, m.end() - 1, "(", ")") or ""
            inner = args[1:].lstrip()
            if inner.startswith("["):
                argv = statement(inner, 0, stop_comma=True)
            else:
                vm = re.match(r"([A-Za-z_]\w*)", inner)
                if vm:
                    argv = resolve_var(text, lines, line, vm.group(1), lang)
                    if argv is None:
                        argv = "<unresolved " + vm.group(1) + ">"
        elif m.group(0).lower().startswith("invoke-process"):
            # Invoke-Process '<id>' <exe> @( ... ) <minutes>
            em = re.match(r"\s*(?:'[^']*'|\"[^\"]*\"|\$\w+)\s+(\S+)\s+@\(", text[m.end():])
            if not em:
                continue
            exe = em.group(1)
            if exe.strip(QUOTES).lower() in SYSTEM_TOOLS:
                continue
            argv = exe + " " + (balanced(text, m.end() + em.end() - 1, "(", ")") or "")
        else:
            seg = lines[line][lines[line].lower().find("start-process"):]
            j = line
            while seg.rstrip().endswith("`") and j + 1 < len(lines):
                j += 1
                seg += "\n" + lines[j]
            fp = re.search(r"-FilePath\s+(\S+)", seg, re.I)
            al = re.search(r"-ArgumentList\s+(\$\w+|\(|@\()", seg, re.I)
            exe = fp.group(1) if fp else ""
            if exe.strip(QUOTES).lower() in SYSTEM_TOOLS:
                continue
            if al and al.group(1).startswith("$"):
                r = resolve_var(text, lines, line, al.group(1)[1:], lang)
                argv = "<unresolved " + al.group(1) + ">" if r is None else exe + " " + r
            elif al:
                expr = balanced(seg, al.end() - 1, "(", ")") or ""
                if re.search("[" + QUOTES + "]-", expr) is None:
                    # an expression over variables only: the first one assigned in this file, else a parameter (plumbing)
                    for v in re.findall(r"\$(\w+)", expr):
                        if v.lower() in ("_", "null", "true", "false"):
                            continue
                        r = resolve_var(text, lines, line, v, lang)
                        if r is not None:
                            argv = exe + " " + r
                            break
                    if argv is None:
                        argv = "<unresolved " + expr.strip()[:40] + ">"
                else:
                    argv = exe + " " + expr
            else:
                argv = "@(" + exe + ")"
        if argv is None:
            continue
        unresolved = argv.startswith("<unresolved")
        kind = "unresolved" if unresolved else classify(argv, lang)
        if kind is None:
            continue
        body = "\n".join(lines[f0:f1 + 1])
        if unresolved:
            verdict = "plumbing" if (os.path.basename(rel), func or "") in PLUMBING else "UNRESOLVED"
        elif has_flag(argv, "-userData"):
            verdict = "userData"
        elif any(has_flag(argv, f) for f in NO_APP_FLAGS):
            verdict = "noApp"
        elif "HS2NEXT_DATA_ROOT" in body:
            verdict = "dataRoot"
        else:
            verdict = "MISSING"
        starts.append({"file": rel, "line": line + 1, "function": func or "(top level)", "kind": kind, "verdict": verdict})
    return starts


def audit(root, files=None):
    tools = os.path.join(root, "Tools")
    out = []
    if files is None:
        files = []
        for d, dirs, names in os.walk(tools):
            dirs[:] = [x for x in dirs if x != "__pycache__"]
            for n in names:
                if n.endswith((".py", ".ps1")) and n != os.path.basename(__file__):
                    files.append(os.path.join(d, n))
    for p in sorted(files):
        rel = os.path.relpath(p, root).replace("\\", "/")
        if os.path.basename(p) in NOT_PLAYER_FILES:
            out.append({"file": rel, "line": 0, "function": "-", "kind": "-", "verdict": "notPlayer"})
            continue
        with open(p, encoding="utf-8", errors="replace") as f:
            text = f.read().replace("\r\n", "\n")
        out.extend(find_starts(p, text, rel))
    return out


SELF_TEST = [
    # (name, language, source, expected verdicts in order)
    ("py with -userData", "py", '''
def start(a, ud):
    args = [a.exe, "-batchmode", "-bridgePort", "17601", "-settings", s, "-userData", ud, "-logFile", lf]
    proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe))
''', ["userData"]),
    ("py without -userData", "py", '''
def start(a):
    args = [a.exe, "-batchmode", "-bridgePort", "17601", "-settings", s, "-logFile", lf]
    args += ["-cacheDir", c]
    proc = subprocess.Popen(args)
''', ["MISSING"]),
    ("py inline list without -userData", "py", '''
def start(exe):
    subprocess.Popen([exe, "-batchmode", "-logFile", "x.log"])
''', ["MISSING"]),
    ("py list joined over lines", "py", '''
def start(a, ud, mode):
    args = [a.exe] + mode + ["-bridgePort", str(a.port), "-settings", settings,
            "-userData", ud, "-logFile", logf]
    proc = subprocess.Popen(args)
''', ["userData"]),
    ("py bare exe with HS2NEXT_DATA_ROOT", "py", '''
def bare(exe, root):
    env = dict(os.environ)
    env["HS2NEXT_DATA_ROOT"] = root
    p = subprocess.Popen([exe], env=env)
''', ["dataRoot"]),
    ("py bare exe without the variable", "py", '''
def bare(exe):
    p = subprocess.Popen([exe])
''', ["MISSING"]),
    ("py -noApp spike player", "py", '''
def launch(a):
    args = [a.exe, "-noApp", "-screen-width", "100", "-logFile", "p.log"]
    subprocess.Popen(args)
''', ["noApp"]),
    ("py system tools are not players", "py", '''
def tools():
    subprocess.run(["tasklist", "/FO", "CSV"])
    subprocess.run([exe, "-NoProfile", "-Command", script])
    subprocess.run(["git", "-C", root, "status"])
''', []),
    ("py userData added later", "py", '''
def start(a, ud):
    args = [a.exe, "-batchmode", "-bridgePort", "17601"]
    args += ["-userData", ud]
    subprocess.Popen(args)
''', ["userData"]),
    ("ps1 without -userData", "ps1", '''
$exe = (Resolve-Path $Player).Path
$args2 = @('-batchmode', '-nographics', '-texturePrescanQuit')
if ($Settings) { $args2 += @('-settings', $Settings) }
$p = Start-Process -FilePath $exe -ArgumentList $args2 -PassThru
''', ["MISSING"]),
    ("ps1 with -userData", "ps1", '''
$args2 = @('-batchmode', '-nographics', '-texturePrescanQuit')
$args2 += @('-userData', $UserData)
$p = Start-Process -FilePath $exe -ArgumentList $args2 -PassThru
''', ["userData"]),
    ("ps1 runner rows", "ps1", '''
function Invoke-Process([string]$name, [string]$exe, [string[]]$argv, [double]$timeoutMin) {
    $p = Start-Process -FilePath $exe -ArgumentList ((@($argv | ForEach-Object { Quote-Arg $_ })) -join ' ') -PassThru
}
$p = Invoke-Process 'PlayerSmoke' $SmokeExe @('-batchmode', '-smoke', $out, '-settings', $settingsFile) 15
$p = Invoke-Process $id $AppExe @('-batchmode', '-bridgeAccept', $out, '-userData', (Join-Path $out 'userdata')) 15
$p = Invoke-Process $id $AppExe @('-batchmode', '-bridgeAccept', $out, '-settings', $s) 15
$p = Invoke-Process $id 'python' @('-I', $script, '--exe', $AppExe) 25
''', ["plumbing", "noApp", "userData", "MISSING"]),
    ("ps1 Unity Editor is not a player", "ps1", '''
$p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-nographics', '-projectPath', $Project, '-logFile', $log) -PassThru
''', []),
]


def self_test(scratch):
    os.makedirs(scratch, exist_ok=True)
    fails = 0
    for i, (name, lang, src, expect) in enumerate(SELF_TEST):
        fname = "run_tests.ps1" if "runner" in name else ("fixture_%02d.%s" % (i, lang))
        sub = os.path.join(scratch, "case%02d" % i, "Tools")
        os.makedirs(sub, exist_ok=True)
        p = os.path.join(sub, fname)
        with open(p, "w", encoding="utf-8") as f:
            f.write(src)
        got = [s["verdict"] for s in audit(os.path.dirname(sub), [p])]
        ok = got == expect
        fails += 0 if ok else 1
        print("%s %s: %s%s" % ("PASS" if ok else "FAIL", name, got, "" if ok else " (expected %s)" % expect))
    print("SUMMARY: %d PASS, %d FAIL (player_launch_audit self-test)" % (len(SELF_TEST) - fails, fails))
    return fails


def main():
    ap = argparse.ArgumentParser(description="every player start in Tools/ gives the player its own UserData")
    ap.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--scratch", help="self-test: the folder for the fixture files (default: the system temp folder)")
    ap.add_argument("--verbose", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        import tempfile
        scratch = a.scratch or os.path.join(tempfile.gettempdir(), "hs2next_player_launch_audit")
        sys.exit(min(self_test(scratch), 100))
    rows = audit(os.path.abspath(a.root))
    bad = [r for r in rows if r["verdict"] in ("MISSING", "UNRESOLVED")]
    for r in rows:
        if a.verbose or r in bad:
            print("%-10s %s:%d %s (%s)" % (r["verdict"], r["file"], r["line"], r["function"], r["kind"]))
    counts = {}
    for r in rows:
        counts[r["verdict"]] = counts.get(r["verdict"], 0) + 1
    print("SUMMARY: %d player starts in Tools/ (%s); %d without their own UserData" %
          (len([r for r in rows if r["verdict"] != "notPlayer"]), ", ".join("%s %d" % kv for kv in sorted(counts.items())), len(bad)))
    sys.exit(min(len(bad), 100))


if __name__ == "__main__":
    main()
