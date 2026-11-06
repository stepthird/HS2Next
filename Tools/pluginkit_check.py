"""pluginkit_check - the plugin kit as a user gets it (row PluginKit.dist; Phase 6.1, KT2-KT5).

One run:
  1. ZIP  Tools/package_dist.ps1 -Zip -AllowDirty -Dist <out>/dist (never Builds/Dist) packages the tested app build; the
          zip's entries hold PluginKit/** (the required files) and both XML files, in HS2Next_Data/Managed/ and
          PluginKit/doc/.
  2. The zip is extracted into --extract-dir, a folder outside the repository (default <TEMP>/hs2next_pluginkit/extract;
     a folder inside the project is refused), after the previous extract is removed (only when it holds no reparse
     point). Entry names are checked (no absolute path, no "..").
  3. KT2  `dotnet build` in <extract>/HS2Next/PluginKit with no argument (the environment only: NUGET_PACKAGES=<the
          extract's parent>/nuget, outside the project like the extract, an empty folder before and after; no build server,
          no node reuse, English output, telemetry off): exit 0,
          0 warnings, 0 errors, both samples staged into PluginKit/out/<guid>/; the build time.
     KT3  each DLL references exactly netstandard 2.1.0.0 and HS2Next.Api 1.0.0.0 (read by Tools/plugins.ps1 -Verify).
     KT5  no build output names the repository: PluginKit/**/obj/** and the build log hold no path of the project
          folder (as written, with '/' or with escaped backslashes).
  4. KT4  out/<guid>/ copied into <extract>/HS2Next/Plugins/ (plain file copies); the extracted exe started
          `-batchmode -bridgePort <port> -bridgeAllowQuit -logFile <out>/player.log` with HS2NEXT_DATA_ROOT=<out>/root
          (holding only Config/settings.local.json: the project's hs2Root and cache, bridge.allowQuit), HS2NEXT_NO_PLUGINS
          removed, HS2NEXT_PRIVATE_OFF=1 and HS2NEXT_UPDATE_CHECK=off: endpoint.json under the root names the PID, ping
          with the token of <root>/UserData/Bridge/token is ready, both samples loaded (version 0.1.0), hello.greet
          {name: "kit"} answers "Hello, kit!", plugin.list counts HelloUi's 2 forms (a player without UI counts, not
          draws), app.quit exits 0. LOG: exception lines in player.log (WARN).
  The extract is removed after a passing run (--keep keeps it); <out> keeps the reports, the build log and player.log.

Usage:  python -I Tools/pluginkit_check.py --project <project> --out Spike/out/player/pluginkit [--port 17388]
        [--timeout-min 12] [--extract-dir <dir outside the project>] [--zip <a zip made before: no packaging>]
        [--app <app build folder for package_dist.ps1 -App>] [--keep] [--gpu-lock FILE] [--input-lock FILE]
Report <out>/pluginkit_report.txt: first and last line "SUMMARY: N PASS, M FAIL"; one "PASS|FAIL|WARN <id> <text>" line per
check; numbers and file names only. Exit code: 0 pass, 1 a check failed, 2 setup error / refused (port busy, no dotnet, no
project settings, an extract folder inside the project), 3 aborted by the game gate. Python 3, standard library only; run
with -I. Never touches Builds/Dist or Builds/Public; stops only the player it started, by PID.
"""
import argparse
import glob
import json
import os
import re
import shutil
import socket
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import zipfile

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GATE_FILE = os.path.join(ROOT, "Tools", "session_gate.json")
CREATE_NO_WINDOW = 0x08000000
KIT = [("stp3.sample.hello", "Hello", "Stp3.SampleHello.dll"), ("stp3.sample.helloui", "HelloUi", "Stp3.SampleHelloUi.dll")]
KIT_VERSION = "0.1.0"
WANT_REFS = {"netstandard": "2.1.0.0", "HS2Next.Api": "1.0.0.0"}
ZIP_REQUIRED = ["PluginKit/" + n for n in (
    "PLUGINS.md", "PLUGINS_ja.md", "PLUGINS_zh-Hans.md", "COMMANDS.md", "HS2NextPlugin.props", "PluginKit.sln", "NuGet.config",
    "Hello/Hello.csproj", "Hello/manifest.json", "HelloUi/HelloUi.csproj", "HelloUi/manifest.json", "HelloUi/strings.tsv",
    "doc/HS2Next.Api.xml", "doc/HS2Next.Api.Unity.xml")] + ["HS2Next_Data/Managed/HS2Next.Api.xml", "HS2Next_Data/Managed/HS2Next.Api.Unity.xml"]


class Report:
    def __init__(self):
        self.lines, self.passed, self.failed = [], 0, 0

    def check(self, cid, ok, text, detail=""):
        if ok:
            self.passed += 1
        else:
            self.failed += 1
        line = ("PASS " if ok else "FAIL ") + cid + " " + text + (("  " + str(detail)) if detail and not ok else "")
        self.lines.append(line)
        print(line, flush=True)
        return ok

    def warn(self, cid, text):
        line = "WARN " + cid + " " + text
        self.lines.append(line)
        print(line, flush=True)

    def info(self, text):
        self.lines.append("   " + text)
        print("   " + text, flush=True)

    def write(self, path, seconds):
        s = "SUMMARY: %d PASS, %d FAIL" % (self.passed, self.failed)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(s + " (%.0f s)\n" % seconds)
            for l in self.lines:
                f.write(l + "\n")
            f.write(s + "\n")


class GameGate(Exception):
    pass


def is_reparse(path):
    try:
        return bool(os.lstat(path).st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)
    except OSError:
        return False


def remove_own_tree(path):
    """Removes a folder this script made, after proving it holds no reparse point (never follows a link)."""
    if not os.path.exists(path):
        return
    if is_reparse(path):
        raise RuntimeError("refusing to delete %s: it is a reparse point" % path)
    for d, dirs, files in os.walk(path):
        for n in dirs + files:
            if is_reparse(os.path.join(d, n)):
                raise RuntimeError("refusing to delete %s: it holds a reparse point (%s)" % (path, os.path.join(d, n)))
    shutil.rmtree(path)


def inside(path, folder):
    a, b = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(folder))
    return a == b or a.startswith(b.rstrip("\\/") + os.sep)


def port_busy(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def game_names():
    try:
        with open(GATE_FILE, encoding="utf-8") as f:
            return [str(x).lower() for x in json.load(f).get("processes") or [] if str(x).strip()]
    except (OSError, ValueError, TypeError, AttributeError):
        return []


def game_running():
    names = game_names()
    if not names:
        return False
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW).stdout
    except Exception:
        return False
    for line in out.decode("oem", "replace").splitlines():
        n = line.split(",")[0].strip('"').lower()
        if any(n.startswith(g) for g in names):
            return True
    return False


def ps_file(script, args, timeout=900):
    exe = shutil.which("pwsh") or shutil.which("powershell") or "powershell.exe"
    p = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script] + args,
                       capture_output=True, timeout=timeout, creationflags=CREATE_NO_WINDOW)
    return p.returncode, p.stdout.decode("utf-8", "replace") + p.stderr.decode("utf-8", "replace")


def http(port, body, token=None, timeout=30):
    """(status, json or text) of one request."""
    req = urllib.request.Request("http://127.0.0.1:%d/" % port, data=json.dumps(body).encode("utf-8"), method="POST",
                                 headers={"Content-Type": "application/json"})
    if token:
        req.add_header("X-HS2Next-Token", token)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            text = r.read().decode("utf-8", "replace")
            try:
                return r.status, json.loads(text)
            except ValueError:
                return r.status, text
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:
        return 0, str(e)


def wait_exit(p, seconds):
    try:
        return p.wait(seconds)
    except subprocess.TimeoutExpired:
        return None


def stop(p):
    if p is not None and p.poll() is None:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True, creationflags=CREATE_NO_WINDOW)
        wait_exit(p, 20)


def references(project, dll):
    """{name: version} of the DLL's assembly references, read by Tools/plugins.ps1 -Verify (exit code ignored here)."""
    code, out = ps_file(os.path.join(project, "Tools", "plugins.ps1"), ["-Verify", dll], timeout=120)
    refs = {}
    for line in out.splitlines():
        m = re.match(r"^\s{2}(\S+) (\d+\.\d+\.\d+\.\d+)\s*$", line)
        if m:
            refs[m.group(1)] = m.group(2)
    return refs


def path_forms(path):
    """The forms a folder's path takes in build outputs: as written, with '/' and with JSON-escaped backslashes."""
    p = os.path.abspath(path).rstrip("\\/")
    return sorted({p.lower(), p.replace("\\", "/").lower(), p.replace("\\", "\\\\").lower()}, key=len, reverse=True)


def package(a, rep, out):
    """Runs package_dist.ps1 -Zip -AllowDirty into <out>/dist and returns the zip's path (None on failure)."""
    dist = os.path.join(out, "dist")
    args = ["-Zip", "-AllowDirty", "-Dist", dist] + (["-App", os.path.abspath(a.app)] if a.app else [])
    t0 = time.time()
    code, text = ps_file(os.path.join(a.project, "Tools", "package_dist.ps1"), args, timeout=600)
    with open(os.path.join(out, "package_console.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    first = ""
    try:
        with open(os.path.join(dist, "package_report.txt"), encoding="utf-8") as f:
            first = f.readline().strip()
    except OSError:
        pass
    zips = sorted(glob.glob(os.path.join(dist, "HS2Next-*-win64.zip")), key=os.path.getmtime)
    zip_path = zips[-1] if zips and os.path.getmtime(zips[-1]) >= t0 - 5 else None
    rep.check("ZIP", code == 0 and first == "RESULT: PASS" and zip_path is not None,
              "package_dist.ps1 -Zip -AllowDirty into %s: exit %s, %s, %s (%.0f s)" % (
                  dist, code, first or "no report", os.path.basename(zip_path) if zip_path else "no zip", time.time() - t0),
              "see %s" % os.path.join(dist, "package_report.txt"))
    return zip_path


def extract(zip_path, dest, rep):
    """Extracts the zip into dest (a fresh folder); checks the entry names and the kit's entries. Returns the HS2Next folder."""
    remove_own_tree(dest)
    os.makedirs(dest)
    with zipfile.ZipFile(zip_path) as z:
        names = z.namelist()
        bad = [n for n in names if n.startswith(("/", "\\")) or ".." in n.replace("\\", "/").split("/") or ":" in n]
        if bad:
            raise RuntimeError("unsafe zip entry names: %s" % bad[:5])
        z.extractall(dest)
    inner = set(n[len("HS2Next/"):] for n in names if n.startswith("HS2Next/"))
    missing = [n for n in ZIP_REQUIRED if n not in inner]
    kit = [n for n in inner if n.startswith("PluginKit/")]
    build_out = [n for n in kit if any(part in ("bin", "obj", "out") for part in n.split("/")[1:-1])]
    rep.check("ZIP", not missing and not build_out and len(names) == len(inner),
              "%s: %d entries under HS2Next/, PluginKit/ %d files (required %d / %d), both XML files in HS2Next_Data/Managed "
              "and PluginKit/doc, build output 0" % (os.path.basename(zip_path), len(names), len(kit), len(ZIP_REQUIRED) - len(missing),
                                                   len(ZIP_REQUIRED)),
              "missing %s; build output %s" % (missing, build_out[:5]))
    return os.path.join(dest, "HS2Next")


def build_kit(a, rep, app, out):
    """KT2, KT3, KT5. Returns {guid: staged folder} of the plugins built."""
    kit = os.path.join(app, "PluginKit")
    # the empty package folder lies next to the extract (outside the project, like the extract): NuGet writes its path into
    # obj/, so a folder under <out> (inside the repository when <out> is Spike/out) would be a repository path there (KT5)
    nuget = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(app))), "nuget")
    remove_own_tree(nuget)
    os.makedirs(nuget)
    env = dict(os.environ)
    env.update({"NUGET_PACKAGES": nuget, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
                "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_CLI_UI_LANGUAGE": "en", "MSBUILDDISABLENODEREUSE": "1",
                "DOTNET_CLI_USE_MSBUILD_SERVER": "0", "UseSharedCompilation": "false"})
    for k in ("HS2NextDir", "HS2NextManaged", "HS2NextUnityManaged", "HS2NextJson", "HS2NextKitOut"):
        env.pop(k, None)   # environment variables would become MSBuild properties: the kit must find the app by itself
    dotnet = shutil.which("dotnet") or os.path.join(os.environ.get("ProgramFiles", ""), "dotnet", "dotnet.exe")
    t0 = time.time()
    p = subprocess.run([dotnet, "build"], cwd=kit, env=env, capture_output=True, timeout=a.build_timeout_s,
                       creationflags=CREATE_NO_WINDOW)
    secs = time.time() - t0
    log = p.stdout.decode("utf-8", "replace") + p.stderr.decode("utf-8", "replace")
    with open(os.path.join(out, "dotnet_build.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write(log)
    w = re.search(r"^\s*(\d+) Warning\(s\)", log, re.M)
    e = re.search(r"^\s*(\d+) Error\(s\)", log, re.M)
    warnings, errors = (int(w.group(1)) if w else None), (int(e.group(1)) if e else None)
    staged = {}
    for guid, folder, dll in KIT:
        d = os.path.join(kit, "out", guid)
        if os.path.isfile(os.path.join(d, dll)) and os.path.isfile(os.path.join(d, "manifest.json")):
            staged[guid] = d
    left = os.listdir(nuget) if os.path.isdir(nuget) else ["(folder gone)"]
    rep.check("KT2", p.returncode == 0 and warnings == 0 and errors == 0 and len(staged) == len(KIT) and not left,
              "dotnet build in the extracted PluginKit, no argument: exit %s, %s warnings, %s errors in %.1f s; staged %s; "
              "NUGET_PACKAGES empty before and after (%d entries after)" % (p.returncode, warnings, errors, secs,
                                                                          ", ".join("out/" + g for g in staged) or "nothing", len(left)),
              "see %s" % os.path.join(out, "dotnet_build.txt"))
    rep.info("dotnet build %.1f s; DLL sizes %s" % (secs, ", ".join("%s %d B" % (dll, os.path.getsize(os.path.join(staged[g], dll)))
                                                                    for g, _, dll in KIT if g in staged)))
    for guid, folder, dll in KIT:
        if guid not in staged:
            rep.check("KT3", False, "%s: not built" % dll)
            continue
        refs = references(a.project, os.path.join(staged[guid], dll))
        rep.check("KT3", refs == WANT_REFS, "%s references %s (exactly %s)" % (
            dll, ", ".join("%s %s" % kv for kv in sorted(refs.items())) or "nothing read",
            ", ".join("%s %s" % kv for kv in sorted(WANT_REFS.items()))))
    # KT5: the build outputs of the kit never name the repository
    forms = path_forms(a.project)
    hits = []
    files = [os.path.join(out, "dotnet_build.txt")]
    for d, dirs, fs in os.walk(kit):
        if "obj" in os.path.relpath(d, kit).split(os.sep):
            files += [os.path.join(d, n) for n in fs]
    for f in files:
        try:
            with open(f, "rb") as fh:
                text = fh.read().decode("utf-8", "replace").lower()
        except OSError:
            continue
        if any(x in text for x in forms):
            hits.append(os.path.relpath(f, app) if inside(f, app) else os.path.basename(f))
    rep.check("KT5", not hits and len(files) > 1, "%d build outputs read (the build log and PluginKit/**/obj/**): %d name the project folder"
              % (len(files), len(hits)), hits[:5])
    return staged


def run_player(a, rep, app, out, staged):
    """KT4: the kit's plugins in the extracted exe."""
    plugins = os.path.join(app, "Plugins")
    for guid, src in staged.items():
        dst = os.path.join(plugins, guid)
        os.makedirs(dst, exist_ok=True)
        for n in os.listdir(src):
            shutil.copyfile(os.path.join(src, n), os.path.join(dst, n))
    rep.info("copied %s into %s" % (", ".join("out/" + g for g in staged) or "nothing", plugins))
    with open(os.path.join(a.project, "Config", "settings.local.json"), encoding="utf-8") as f:
        proj = json.load(f)
    hs2 = proj.get("hs2Root")
    hs2 = os.path.abspath(os.path.join(a.project, hs2)) if hs2 else None
    root = os.path.join(out, "root")
    remove_own_tree(root)
    os.makedirs(os.path.join(root, "Config"))
    with open(os.path.join(root, "Config", "settings.local.json"), "w", encoding="utf-8") as f:
        json.dump({"hs2Root": hs2, "cacheDir": os.path.join(os.path.abspath(a.project), "Cache"), "bridge": {"allowQuit": True}},
                  f, ensure_ascii=False, indent=1)
    exe = os.path.join(app, "HS2Next.exe")
    logf = os.path.join(out, "player.log")
    env = dict(os.environ)
    env.pop("HS2NEXT_NO_PLUGINS", None)
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    env["HS2NEXT_UPDATE_CHECK"] = "off"
    env["HS2NEXT_DATA_ROOT"] = root   # the extracted exe keeps its data under the run's own root, never the per-user folder
    args = [exe, "-batchmode", "-bridgePort", str(a.port), "-bridgeAllowQuit", "-logFile", logf]
    rep.info("start: %s (HS2NEXT_DATA_ROOT=%s)" % (subprocess.list2cmdline(args), root))
    t0 = time.time()
    with open(os.path.join(out, "player_stdout.txt"), "w", encoding="utf-8") as so:
        p = subprocess.Popen(args, cwd=app, env=env, stdin=subprocess.DEVNULL, stdout=so, stderr=subprocess.STDOUT,
                             creationflags=CREATE_NO_WINDOW)
        try:
            ep = os.path.join(root, "UserData", "Bridge", "endpoint.json")
            info = None
            while time.time() - t0 < a.ready_timeout_s and p.poll() is None:
                if game_running():
                    raise GameGate()
                try:
                    with open(ep, encoding="utf-8") as f:
                        j = json.load(f)
                    if j.get("pid") == p.pid:
                        info = j
                        break
                except (OSError, ValueError):
                    pass
                time.sleep(0.25)
            rep.check("KT4", isinstance(info, dict), "endpoint.json under the data root names the started PID %s (%.1f s)"
                      % (p.pid, time.time() - t0), "exit code %s" % p.poll())
            token = None
            try:
                with open(os.path.join(root, "UserData", "Bridge", "token"), encoding="utf-8") as f:
                    token = f.read().strip()
            except OSError:
                pass
            ready, last = None, None
            while info and time.time() - t0 < a.ready_timeout_s and p.poll() is None:
                st, j = http(a.port, {"cmd": "ping"}, token, timeout=5)
                last = j
                if st == 200 and isinstance(j, dict) and j.get("appReady"):
                    ready = time.time() - t0
                    break
                time.sleep(0.5)
            rep.check("KT4", ready is not None, "ping.appReady with the token of the data root (%s; tokenRequired %s) after %.1f s"
                      % ("token read" if token else "no token file", (info or {}).get("tokenRequired"), ready or -1), str(last)[:200])
            if ready is not None:
                st, v = http(a.port, {"cmd": "bridge.version"}, token)
                by = {str(x.get("guid", "")).lower(): x for x in (v.get("plugins") if isinstance(v, dict) else None) or []}
                states = {g: (by.get(g) or {}).get("state") for g, _, _ in KIT}
                vers = {g: (by.get(g) or {}).get("version") for g, _, _ in KIT}
                rep.check("KT4", all(s == "loaded" for s in states.values()) and all(x == KIT_VERSION for x in vers.values()),
                          "bridge.version.plugins: %s" % ", ".join("%s %s %s" % (g, vers[g], states[g]) for g, _, _ in KIT),
                          json.dumps(v)[:300] if not isinstance(v, dict) else "")
                st, g = http(a.port, {"cmd": "hello.greet", "name": "kit"}, token)
                rep.check("KT4", isinstance(g, dict) and g.get("ok") and g.get("text") == "Hello, kit!" and isinstance(g.get("objects"), int),
                          "hello.greet {name: kit} -> %s, objects %s" % (json.dumps(g.get("text") if isinstance(g, dict) else g),
                                                                       g.get("objects") if isinstance(g, dict) else None), str(g)[:200])
                st, pl = http(a.port, {"cmd": "plugin.list"}, token)
                e = next((x for x in (pl.get("plugins") if isinstance(pl, dict) else None) or [] if str(x.get("guid", "")).lower() == "stp3.sample.helloui"), {})
                ui = e.get("ui")
                forms = ui.get("forms") if isinstance(ui, dict) else e.get("ui.forms", e.get("forms"))
                forms = len(forms) if isinstance(forms, list) else forms
                rep.check("KT4", forms == 2, "plugin.list stp3.sample.helloui: state %s, forms %s (2: the panel and the item section)"
                          % (e.get("state"), forms if forms is not None else "not reported"))
                st, q = http(a.port, {"cmd": "app.quit"}, token)
                code = wait_exit(p, 30)
                rep.check("KT4", isinstance(q, dict) and q.get("ok") and code == 0, "app.quit -> exit code %s" % code, str(q)[:200])
        finally:
            stop(p)
    try:
        with open(logf, encoding="utf-8", errors="replace") as f:
            lines = f.read().splitlines()
        exc = [l for l in lines if "Exception" in l]
        line = "player.log %d lines, %d with 'Exception'%s" % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else "")
        if exc:
            rep.warn("LOG", line)
        else:
            rep.info(line)
    except OSError:
        rep.warn("LOG", "no player.log")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=17388)
    ap.add_argument("--timeout-min", type=float, default=12)
    ap.add_argument("--extract-dir", help="where the zip is extracted (outside the project; default <TEMP>/hs2next_pluginkit/extract)")
    ap.add_argument("--zip", help="a zip made before (no packaging)")
    ap.add_argument("--app", help="the app build package_dist.ps1 packages (its -App; default Builds/App)")
    ap.add_argument("--keep", action="store_true", help="keep the extract after a passing run")
    ap.add_argument("--gpu-lock")
    ap.add_argument("--input-lock")
    a = ap.parse_args()
    a.project = os.path.abspath(a.project)
    a.build_timeout_s = 600
    a.ready_timeout_s = min(600, a.timeout_min * 60 / 2)
    t_all = time.time()
    rep = Report()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    report_path = os.path.join(out, "pluginkit_report.txt")
    locks = []
    extract_dir = os.path.abspath(a.extract_dir or os.path.join(tempfile.gettempdir(), "hs2next_pluginkit", "extract"))

    def finish(code):
        for l in locks:
            try:
                os.remove(l)
            except OSError:
                pass
        rep.write(report_path, time.time() - t_all)
        sys.exit(code)

    # setup
    if inside(extract_dir, a.project):
        rep.check("setup", False, "the extract folder lies outside the project", extract_dir)
        finish(2)
    if port_busy(a.port):
        rep.check("setup", False, "port %d is free" % a.port)
        finish(2)
    if not os.path.isfile(os.path.join(a.project, "Config", "settings.local.json")):
        rep.check("setup", False, "the project's Config/settings.local.json exists")
        finish(2)
    try:
        sdks = subprocess.run([shutil.which("dotnet") or "dotnet", "--list-sdks"], capture_output=True, timeout=60,
                              creationflags=CREATE_NO_WINDOW).stdout.decode("utf-8", "replace")
    except Exception:
        sdks = ""
    majors = [int(m.group(1)) for m in re.finditer(r"^(\d+)\.\d+\.\d+", sdks, re.M)]
    if not majors or max(majors) < 8:
        rep.check("setup", False, "a .NET SDK 8 or later is installed", sdks.strip()[:200] or "dotnet not found")
        finish(2)
    rep.info("dotnet SDKs: %s; extract folder %s" % (", ".join(l.split()[0] for l in sdks.strip().splitlines()), extract_dir))
    if game_running():
        rep.info("aborted by the game gate (a process of Tools/session_gate.json runs)")
        finish(3)
    for lk in (a.gpu_lock, a.input_lock):
        if lk:
            try:
                fd = os.open(lk, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except OSError:
                rep.check("setup", False, "the lock %s is free" % lk)
                finish(2)
            os.write(fd, ("pluginkit_check PID %d start %s expected end %s\n" % (
                os.getpid(), time.strftime("%Y-%m-%d %H:%M:%S"),
                time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(time.time() + a.timeout_min * 60)))).encode())
            os.close(fd)
            locks.append(lk)
    passed = False
    try:
        zip_path = os.path.abspath(a.zip) if a.zip else package(a, rep, out)
        if zip_path and os.path.isfile(zip_path):
            app = extract(zip_path, extract_dir, rep)
            staged = build_kit(a, rep, app, out)
            if not staged:
                rep.check("KT4", False, "no kit plugin was built (the exe is started without them: the start, the token and the quit are still checked)")
            run_player(a, rep, app, out, staged)
        elif a.zip:
            rep.check("ZIP", False, "the zip %s exists" % zip_path)
        passed = rep.failed == 0
    except GameGate:
        rep.info("aborted by the game gate (a process of Tools/session_gate.json started)")
        finish(3)
    except Exception as e:
        rep.check("run", False, "the check ran to its end", "%s: %s" % (type(e).__name__, e))
    if passed and not a.keep:
        try:
            remove_own_tree(extract_dir)
        except Exception as e:
            rep.warn("CLEAN", "the extract was not removed: %s" % e)
    finish(0 if rep.failed == 0 else 1)


if __name__ == "__main__":
    main()
