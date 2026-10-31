"""Full TypeTree scan of every HS2 asset bundle (abdata + zipmods), resumable and parallel.

Clean room: only bundle metadata is read (TypeTrees, MonoScript names, shader names,
object type counts). No game code is used.

Modes (see Tools/README_typetree_scan.md):
  scan    python -I typetree_scan.py scan <game_root> [--out DIR] [--workers N] ...
  merge   python -I typetree_scan.py merge [--out DIR] [--survey-dir DIR] [--file-list F]
  status  python -I typetree_scan.py status [--out DIR]

Cache layout (<out>, default <repo>/Cache/typetree):
  results/<sha1(relpath)[:16]>_<size>_<mtime_ns>.json.gz   one per source file
  scan.log        progress log (appended)
  progress.json   latest progress snapshot (read by `status`)
  scan.lock       pid of the running scan (prevents two scans on one cache)
  merged.json     output of `merge`
"""
import argparse
import collections
import concurrent.futures as cf
import ctypes
import datetime
import fnmatch
import gc
import gzip
import hashlib
import json
import os
import re
import sys
import time
import traceback
import zipfile

RESULT_VERSION = 1
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_OUT = os.path.normpath(os.path.join(HERE, "..", "Cache", "typetree"))
DEFAULT_SURVEY = os.path.join(HERE, "survey")

# Fields every MonoBehaviour has; they belong to the Unity base class, not to the script.
BASE_FIELDS = {"m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier"}
BUNDLE_SIGNATURES = (b"UnityFS", b"UnityWeb", b"UnityRaw", b"UnityArchive")


# --------------------------------------------------------------------------------------
# Discovery
# --------------------------------------------------------------------------------------

def rel(game_root, path):
    """Path relative to the game root with forward slashes (the stable source id)."""
    return os.path.relpath(path, game_root).replace(os.sep, "/")


def discover(game_root, includes=(), excludes=()):
    """All abdata/**/*.unity3d and mods/**/*.zipmod|*.zip, sorted by relpath."""
    found = []
    for top, exts in (("abdata", (".unity3d",)), ("mods", (".zipmod", ".zip"))):
        base = os.path.join(game_root, top)
        for root, dirs, files in os.walk(base):
            dirs.sort()
            for f in files:
                if f.lower().endswith(exts):
                    found.append(rel(game_root, os.path.join(root, f)))
    found.sort()

    def match(r, pats):
        rl = r.lower()
        return any(fnmatch.fnmatchcase(rl, p.lower()) for p in pats)

    if includes:
        found = [r for r in found if match(r, includes)]
    if excludes:
        found = [r for r in found if not match(r, excludes)]
    return found


def result_prefix(relpath):
    return hashlib.sha1(relpath.encode("utf-8")).hexdigest()[:16]


def result_name(relpath, size, mtime_ns):
    return f"{result_prefix(relpath)}_{size}_{mtime_ns}.json.gz"


def make_task(game_root, relpath):
    """Task tuple for one source. weight = size of the largest bundle (drives memory)."""
    path = os.path.join(game_root, relpath)
    st = os.stat(path)
    kind = "abdata" if relpath.lower().startswith("abdata/") else "zipmod"
    weight = st.st_size
    if kind == "zipmod":
        try:
            with zipfile.ZipFile(path) as zf:
                weight = max((zi.file_size for zi in zf.infolist()
                              if zi.filename.lower().endswith(".unity3d")), default=0)
        except Exception:
            weight = st.st_size  # broken zip: the worker will record the error
    return {"relpath": relpath, "path": path, "size": st.st_size, "mtime_ns": st.st_mtime_ns,
            "kind": kind, "weight": weight}


# --------------------------------------------------------------------------------------
# Worker side: scanning one source
# --------------------------------------------------------------------------------------

def clean_key(key):
    """Some mods carry NUL-padded names (e.g. 'IL.dll\\0\\0...'); strip them so they merge."""
    return tuple(x.replace("\x00", "").strip() for x in key)


ABS_PATH = re.compile(r"(?:[A-Za-z]:[\\/]|/(?:home|Users|mnt)/)[^\s'\"]*")


def clean_error(msg):
    """Drop absolute local paths (UnityPy puts the cwd into some messages)."""
    return ABS_PATH.sub("<path>", msg) if isinstance(msg, str) else msg


def node_to_list(n):
    """TypeTreeNode -> [type, name, [children...]] (JSON-friendly, recursive)."""
    return [n.m_Type, n.m_Name, [node_to_list(c) for c in n.m_Children]]


class SourceResult:
    """Accumulates everything found in one source (one abdata bundle or one zipmod)."""

    def __init__(self):
        self.types = collections.Counter()
        self.shaders = collections.Counter()
        self.scripts = {}      # (asm, ns, cls) -> record
        self.unresolved = {}   # script_id hex -> record
        self.no_typetree = 0
        self.errors = []
        self.bundles = []

    @staticmethod
    def _new_record():
        return {"count": 0, "so": 0, "nonso": 0, "first_so": None,
                "variants": collections.OrderedDict(), "script_ids": set()}

    def add_mono(self, rec, fields_json, fields, so):
        rec["count"] += 1
        if so is not None:
            rec["so" if so else "nonso"] += 1
            if rec["first_so"] is None:
                rec["first_so"] = so
        v = rec["variants"].get(fields_json)
        if v is None:
            rec["variants"][fields_json] = [fields, 1]
        else:
            v[1] += 1

    def to_json(self):
        def rec_json(r):
            return {"count": r["count"], "so": r["so"], "nonso": r["nonso"], "first_so": r["first_so"],
                    "script_ids": sorted(r["script_ids"]),
                    "variants": [{"fields": f, "count": c} for f, c in r["variants"].values()]}
        scripts = []
        for (asm, ns, cls), r in self.scripts.items():
            d = rec_json(r)
            d.update({"assembly": asm, "namespace": ns, "class": cls})
            scripts.append(d)
        unresolved = []
        for sid, r in self.unresolved.items():
            d = rec_json(r)
            d.update({"script_id": sid, "error": r.get("error", "")})
            unresolved.append(d)
        return {"types": dict(self.types), "shaders": dict(self.shaders), "scripts": scripts,
                "unresolved": unresolved, "no_typetree": self.no_typetree,
                "errors": self.errors, "bundles": self.bundles}


def scan_bundle(UnityPy, src, name, size, res, mb_head_node):
    """Scan one bundle (file path or file-like object) into res."""
    binfo = {"name": name, "size": size, "objects": 0}
    res.bundles.append(binfo)
    try:
        if hasattr(src, "read"):
            head = src.read(16)
            src.seek(0)
        else:
            with open(src, "rb") as f:  # also turns a missing file into an error
                head = f.read(16)
        if not head.startswith(BUNDLE_SIGNATURES):
            # Not an AssetBundle (could be a raw serialized file; UnityPy still tries it).
            binfo["signature"] = head[:8].hex()
        env = UnityPy.load(src)
    except Exception as e:
        res.errors.append({"bundle": name, "stage": "load", "error": f"{type(e).__name__}: {e}"})
        binfo["error"] = "load"
        return
    try:
        objs = list(env.objects)
    except Exception as e:
        res.errors.append({"bundle": name, "stage": "objects", "error": f"{type(e).__name__}: {e}"})
        binfo["error"] = "objects"
        return
    binfo["objects"] = len(objs)
    if not objs:
        res.errors.append({"bundle": name, "stage": "load", "error": "no objects (not a Unity bundle?)"})
        binfo["error"] = "empty"
        return
    try:
        binfo["unity_version"] = str(objs[0].assets_file.unity_version)
    except Exception:
        pass
    # Script resolution cache, valid only within this bundle (keyed by stable names, not id()).
    script_cache = {}
    # Per SerializedType: serialized field list (computed once per type object).
    type_cache = {}
    for obj in objs:
        try:
            tname = obj.type.name
        except Exception:
            tname = f"ClassID_{getattr(obj, 'class_id', '?')}"
        res.types[tname] += 1
        if tname == "Shader":
            try:
                d = obj.read(check_read=False)
                pf = getattr(d, "m_ParsedForm", None)
                nm = getattr(pf, "m_Name", "") if pf is not None else ""
                res.shaders[nm or getattr(d, "m_Name", "") or "?"] += 1
            except Exception as e:
                res.errors.append({"bundle": name, "stage": "shader",
                                   "error": f"{type(e).__name__}: {e}"})
            continue
        if tname != "MonoBehaviour":
            continue
        st = obj.serialized_type
        if st is None or st.node is None:
            res.no_typetree += 1
            continue
        # Head = m_GameObject, m_Enabled, m_Script, m_Name only (cheap, no full parse).
        so = None
        sp = None
        try:
            h = obj.parse_monobehaviour_head(mb_head_node)
            so = h.m_GameObject.path_id == 0
            sp = h.m_Script
        except Exception as e:
            res.errors.append({"bundle": name, "stage": "mono_head", "error": f"{type(e).__name__}: {e}"})
        key = None
        err = ""
        if sp is not None:
            ck = (obj.assets_file.name, sp.file_id, sp.path_id)
            if ck in script_cache:
                key, err = script_cache[ck]
            elif sp.path_id == 0:
                err = "m_Script is null (missing script)"
                script_cache[ck] = (key, err)
            else:
                try:
                    sc = sp.deref_parse_as_object()
                    key = clean_key((sc.m_AssemblyName, sc.m_Namespace, sc.m_ClassName))
                except Exception as e:
                    err = clean_error(f"{type(e).__name__}: {e}")
                script_cache[ck] = (key, err)
        sid = st.script_id.hex() if st.script_id else ""
        tk = id(st)  # SerializedType objects live as long as env, so id() is safe here
        tc = type_cache.get(tk)
        if tc is None:
            fields = [node_to_list(ch) for ch in st.node.m_Children if ch.m_Name not in BASE_FIELDS]
            tc = (json.dumps(fields, separators=(",", ":")), fields)
            type_cache[tk] = tc
        if key is not None and key[2]:
            rec = res.scripts.get(key)
            if rec is None:
                rec = res.scripts[key] = SourceResult._new_record()
        else:
            uk = sid or "(none)"
            rec = res.unresolved.get(uk)
            if rec is None:
                rec = res.unresolved[uk] = SourceResult._new_record()
                rec["error"] = err or "empty class name"
        if sid:
            rec["script_ids"].add(sid)
        res.add_mono(rec, tc[0], tc[1], so)
    del objs, env, script_cache, type_cache


def scan_source(task, out_dir):
    """Worker entry point: scan one source and write its result file atomically."""
    import io
    import UnityPy
    t0 = time.time()
    res = SourceResult()
    mb_head_node = None  # None = UnityPy's built-in MonoBehaviour head node for the file version
    nbytes = 0
    try:
        if task["kind"] == "abdata":
            nbytes = task["size"]
            scan_bundle(UnityPy, task["path"], "", task["size"], res, mb_head_node)
        else:
            try:
                zf = zipfile.ZipFile(task["path"])
            except Exception as e:
                res.errors.append({"bundle": "", "stage": "zip", "error": f"{type(e).__name__}: {e}"})
                zf = None
            if zf is not None:
                with zf:
                    for zi in zf.infolist():
                        if not zi.filename.lower().endswith(".unity3d") or zi.is_dir():
                            continue
                        nbytes += zi.file_size
                        try:
                            # Stored entries are streamed straight from the zip (no copy).
                            if zi.compress_type == zipfile.ZIP_STORED:
                                src = zf.open(zi)
                            else:
                                src = io.BytesIO(zf.read(zi))
                        except Exception as e:
                            res.errors.append({"bundle": zi.filename, "stage": "zip_entry",
                                               "error": f"{type(e).__name__}: {e}"})
                            continue
                        with src:
                            scan_bundle(UnityPy, src, zi.filename, zi.file_size, res, mb_head_node)
                        gc.collect()
    except MemoryError as e:
        res.errors.append({"bundle": "", "stage": "memory", "error": f"MemoryError: {e}"})
    except Exception as e:
        res.errors.append({"bundle": "", "stage": "fatal",
                           "error": f"{type(e).__name__}: {e}", "trace": traceback.format_exc()[-2000:]})
    doc = {"version": RESULT_VERSION, "source": task["relpath"], "kind": task["kind"],
           "size": task["size"], "mtime_ns": task["mtime_ns"], "bytes": nbytes,
           "seconds": round(time.time() - t0, 2),
           "scanned_at": datetime.datetime.now().isoformat(timespec="seconds"),
           "unitypy": UnityPy.__version__}
    doc.update(res.to_json())
    write_result(out_dir, task, doc)
    gc.collect()
    return {"relpath": task["relpath"], "bytes": nbytes, "seconds": doc["seconds"],
            "errors": len(doc["errors"]), "scripts": len(doc["scripts"])}


def write_result(out_dir, task, doc):
    rdir = os.path.join(out_dir, "results")
    os.makedirs(rdir, exist_ok=True)
    name = result_name(task["relpath"], task["size"], task["mtime_ns"])
    tmp = os.path.join(rdir, name + f".{os.getpid()}.tmp")
    with gzip.open(tmp, "wt", encoding="utf-8", compresslevel=6) as f:
        json.dump(doc, f, separators=(",", ":"))
    os.replace(tmp, os.path.join(rdir, name))
    # Drop results of older versions of the same source (size/mtime changed).
    pre = result_prefix(task["relpath"]) + "_"
    for fn in os.listdir(rdir):
        if fn.startswith(pre) and fn != name and fn.endswith(".json.gz"):
            try:
                os.remove(os.path.join(rdir, fn))
            except OSError:
                pass


# --------------------------------------------------------------------------------------
# Parent side: scheduling, logging, resume
# --------------------------------------------------------------------------------------

def pid_alive(pid):
    """True if a process with this pid is running (Windows-safe: never sends a signal)."""
    if not pid:
        return False
    if os.name == "nt":
        k32 = ctypes.windll.kernel32
        h = k32.OpenProcess(0x1000, False, int(pid))  # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return False
        code = ctypes.c_ulong()
        ok = k32.GetExitCodeProcess(h, ctypes.byref(code))
        k32.CloseHandle(h)
        return bool(ok) and code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


class Logger:
    def __init__(self, path):
        self.f = open(path, "a", encoding="utf-8")

    def __call__(self, msg):
        line = f"{datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')} {msg}"
        print(line, flush=True)
        self.f.write(line + "\n")
        self.f.flush()


def fmt_dur(s):
    s = int(max(0, s))
    return f"{s // 3600}h{s % 3600 // 60:02d}m" if s >= 3600 else f"{s // 60}m{s % 60:02d}s"


def write_json_atomic(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=1)
    os.replace(tmp, path)


def cmd_scan(a):
    game = os.path.abspath(a.game_root)
    out = os.path.abspath(a.out)
    os.makedirs(os.path.join(out, "results"), exist_ok=True)
    lock = os.path.join(out, "scan.lock")
    if os.path.exists(lock):
        try:
            other = int(open(lock).read().strip() or 0)
        except ValueError:
            other = 0
        if other != os.getpid() and pid_alive(other):
            sys.exit(f"another scan (pid {other}) is running on {out}")
    with open(lock, "w") as f:
        f.write(str(os.getpid()))
    log = Logger(os.path.join(out, "scan.log"))
    try:
        _scan(a, game, out, log)
    finally:
        try:
            os.remove(lock)
        except OSError:
            pass


def _scan(a, game, out, log):
    if a.file_list:
        with open(a.file_list, encoding="utf-8") as f:
            rels = [l.strip().replace("\\", "/") for l in f if l.strip() and not l.startswith("#")]
    else:
        rels = discover(game, a.include, a.exclude)
    rels = list(dict.fromkeys(rels))  # drop duplicates, keep order
    if a.limit:
        rels = rels[:a.limit]
    t0 = time.time()
    tasks = []
    for r in rels:
        try:
            tasks.append(make_task(game, r))
        except OSError as e:
            log(f"MISSING {r}: {e}")
    existing = set(os.listdir(os.path.join(out, "results")))
    todo = [t for t in tasks if result_name(t["relpath"], t["size"], t["mtime_ns"]) not in existing]
    total_bytes = sum(t["size"] for t in tasks)
    todo_bytes = sum(t["size"] for t in todo)
    log(f"START pid={os.getpid()} sources={len(tasks)} ({total_bytes / 1e9:.1f} GB) already_done="
        f"{len(tasks) - len(todo)} todo={len(todo)} ({todo_bytes / 1e9:.1f} GB) workers={a.workers} "
        f"big_workers={a.big_workers} big_threshold={a.big_mb}MB discovery={time.time() - t0:.1f}s")
    big_limit = a.big_mb * 1024 * 1024
    # Sources whose largest bundle is >= --big-mb go to a separate pool with few workers so
    # that several multi-GB bundles are never decompressed at the same time. Biggest first.
    queues = {"small": [t for t in todo if t["weight"] < big_limit],
              "big": sorted([t for t in todo if t["weight"] >= big_limit], key=lambda t: -t["weight"])}
    queues["small"].reverse()  # pop() from the end keeps the sorted order
    queues["big"].reverse()
    nworkers = {"small": a.workers, "big": a.big_workers}
    attempts = collections.Counter()
    pools = {}

    def new_pool(kind):
        return cf.ProcessPoolExecutor(max_workers=nworkers[kind], max_tasks_per_child=a.tasks_per_child)

    if nworkers["big"] <= 0:  # no dedicated big pool: big sources go through the small pool
        queues["small"] = queues["small"] + queues["big"]
        queues["big"] = []
    for k in ("small", "big"):
        if nworkers[k] > 0:
            pools[k] = new_pool(k)
    inflight = {}  # future -> (kind, task)
    done_n = done_bytes = err_n = 0
    started = time.time()
    last_report = 0.0
    solo = []  # tasks that killed a worker twice: run alone at the end

    def submit(kind):
        while queues[kind] and sum(1 for k, _ in inflight.values() if k == kind) < nworkers[kind]:
            t = queues[kind].pop()
            inflight[pools[kind].submit(scan_source, t, out)] = (kind, t)

    def report(final=False):
        el = time.time() - started
        rate = done_bytes / el if el > 0 else 0
        left = todo_bytes - done_bytes
        eta = left / rate if rate > 0 else 0
        snap = {"pid": os.getpid(), "game_root_name": os.path.basename(game), "started": started,
                "updated": time.time(), "sources_total": len(tasks), "already_done": len(tasks) - len(todo),
                "todo": len(todo), "done": done_n, "errors_sources": err_n,
                "bytes_total": total_bytes, "bytes_todo": todo_bytes, "bytes_done": done_bytes,
                "rate_mb_s": round(rate / 1e6, 2), "eta_s": round(eta), "final": final,
                "inflight": [t["relpath"] for _, t in inflight.values()]}
        write_json_atomic(os.path.join(out, "progress.json"), snap)
        log(f"PROGRESS {done_n}/{len(todo)} sources, {done_bytes / 1e9:.1f}/{todo_bytes / 1e9:.1f} GB, "
            f"{rate / 1e6:.1f} MB/s, elapsed {fmt_dur(el)}, ETA {fmt_dur(eta)}, sources with errors {err_n}")

    for k in pools:
        submit(k)
    while inflight:
        finished, _ = cf.wait(list(inflight), timeout=a.report_every, return_when=cf.FIRST_COMPLETED)
        broken = set()
        for fut in finished:
            kind, t = inflight.pop(fut)
            try:
                r = fut.result()
            except cf.process.BrokenProcessPool:
                broken.add(kind)
                attempts[t["relpath"]] += 1
                (solo if attempts[t["relpath"]] >= 2 else queues[kind]).append(t)
                continue
            except Exception as e:  # unexpected (e.g. pickling); record and move on
                log(f"FAIL {t['relpath']}: {type(e).__name__}: {e}")
                err_n += 1
                continue
            done_n += 1
            done_bytes += t["size"]
            if r["errors"]:
                err_n += 1
            log(f"DONE {done_n}/{len(todo)} {t['relpath']} {t['size'] / 1e6:.0f}MB {r['seconds']:.1f}s "
                f"scripts={r['scripts']} errors={r['errors']}")
        for kind in broken:
            # A worker died (e.g. out of memory). Every task still running in that pool is
            # suspect: requeue them (twice-failed ones go to the solo list) and restart the pool.
            log(f"POOL BROKEN ({kind}); restarting")
            for fut, (k, t) in list(inflight.items()):
                if k == kind:
                    inflight.pop(fut)
                    attempts[t["relpath"]] += 1
                    (solo if attempts[t["relpath"]] >= 2 else queues[kind]).append(t)
            pools[kind].shutdown(wait=False, cancel_futures=True)
            pools[kind] = new_pool(kind)
        for k in pools:
            submit(k)
        if time.time() - last_report >= a.report_every and inflight:
            last_report = time.time()
            report()
    for p in pools.values():
        p.shutdown()
    # Tasks that crashed a worker twice: run each alone in a fresh single-worker pool.
    for t in solo:
        log(f"SOLO {t['relpath']}")
        with cf.ProcessPoolExecutor(max_workers=1) as p:
            try:
                r = p.submit(scan_source, t, out).result()
                done_n += 1
                done_bytes += t["size"]
                err_n += bool(r["errors"])
                log(f"DONE(solo) {t['relpath']} errors={r['errors']}")
            except Exception as e:
                err_n += 1
                write_result(out, t, {"version": RESULT_VERSION, "source": t["relpath"], "kind": t["kind"],
                                      "size": t["size"], "mtime_ns": t["mtime_ns"], "bytes": 0,
                                      "seconds": 0, "types": {}, "shaders": {}, "scripts": [],
                                      "unresolved": [], "no_typetree": 0, "bundles": [],
                                      "errors": [{"bundle": "", "stage": "crash",
                                                  "error": f"worker died: {type(e).__name__}: {e}"}]})
                log(f"CRASH {t['relpath']}: worker died twice alone; recorded as error")
    report(final=True)
    log(f"END scanned {done_n} sources in {fmt_dur(time.time() - started)}")
    if not a.no_merge:
        merge(out, a.survey_dir, None, log)


# --------------------------------------------------------------------------------------
# Merge
# --------------------------------------------------------------------------------------

def latest_results(out):
    """relpath-prefix -> newest result file name (tolerates partial / interrupted scans)."""
    rdir = os.path.join(out, "results")
    best = {}
    for fn in os.listdir(rdir) if os.path.isdir(rdir) else ():
        if not fn.endswith(".json.gz"):
            continue
        pre = fn.split("_", 1)[0]
        full = os.path.join(rdir, fn)
        if pre not in best or os.path.getmtime(full) > os.path.getmtime(best[pre]):
            best[pre] = full
    return best


def peek_source(path):
    """The "source" value of a result file, read from its first bytes only."""
    with gzip.open(path, "rt", encoding="utf-8") as f:
        head = f.read(4096)
    m = re.search(r'"source":("(?:[^"\\]|\\.)*")', head)
    if not m:
        raise ValueError("no source field")
    return json.loads(m.group(1))


def merge_fields(fields, conflicts, variant):
    """Old gen_compat_stubs semantics: first-seen node per field name wins (in order);
    a later node with the same name but another top-level type is a conflict."""
    for node in variant:
        old = fields.get(node[1])
        if old is None:
            fields[node[1]] = node
        elif old[0] != node[0]:
            c = f"{node[1]}: {old[0]} vs {node[0]}"
            if c not in conflicts:
                conflicts.append(c)


def merge(out, survey_dir, file_list, log=print):
    """Merge every finished result into merged.json + the two census TSVs."""
    t0 = time.time()
    best = latest_results(out)
    if file_list:  # explicit source order (used to reproduce an older sample scan)
        with open(file_list, encoding="utf-8") as f:
            order = [l.strip().replace("\\", "/") for l in f if l.strip() and not l.startswith("#")]
        paths = [best[result_prefix(r)] for r in order if result_prefix(r) in best]
    else:
        # Default order: by relpath, so abdata/... (vanilla) comes before mods/... and wins
        # "first seen" for field nodes. Only the head of each file is read for sorting.
        keyed = []
        for p in best.values():
            try:
                keyed.append((peek_source(p), p))
            except Exception as e:
                log(f"merge: unreadable {p}: {e}")
        paths = [p for _, p in sorted(keyed)]

    def docs():
        # Stream the results one at a time (the full set does not need to fit in memory).
        for p in paths:
            try:
                with gzip.open(p, "rt", encoding="utf-8") as f:
                    yield json.load(f)
            except Exception as e:
                log(f"merge: unreadable {p}: {e}")

    scripts = collections.OrderedDict()
    shaders = collections.Counter()
    shader_src = collections.Counter()
    types = collections.Counter()
    errors = []
    unresolved = []
    sid_to_keys = collections.defaultdict(set)
    totals = collections.Counter()
    for d in docs():
        src = d["source"]
        totals["sources"] += 1
        totals[d["kind"]] += 1
        totals["bytes"] += d.get("bytes", 0)
        totals["bundles"] += len(d.get("bundles", []))
        totals["no_typetree"] += d.get("no_typetree", 0)
        types.update(d.get("types", {}))
        for nm, c in d.get("shaders", {}).items():
            shaders[nm] += c
            shader_src[nm] += 1
        for e in d.get("errors", []):
            errors.append({"source": src, **{k: clean_error(v) for k, v in e.items() if k != "trace"}})
        for r in d.get("scripts", []):
            key = clean_key((r["assembly"], r["namespace"], r["class"]))
            s = scripts.get(key)
            if s is None:
                s = scripts[key] = {"assembly": key[0], "namespace": key[1], "class": key[2], "count": 0,
                                    "so": 0, "nonso": 0, "first_so": None, "fields": collections.OrderedDict(),
                                    "conflicts": [], "variants": set(), "script_ids": set(), "sources": []}
            s["count"] += r["count"]
            s["so"] += r["so"]
            s["nonso"] += r["nonso"]
            if s["first_so"] is None:
                s["first_so"] = r["first_so"]
            s["script_ids"].update(r["script_ids"])
            s["sources"].append([src, r["count"]])
            for sid in r["script_ids"]:
                sid_to_keys[sid].add(key)
            for v in r["variants"]:
                s["variants"].add(json.dumps(v["fields"], separators=(",", ":")))
                merge_fields(s["fields"], s["conflicts"], v["fields"])
        for u in d.get("unresolved", []):
            unresolved.append((src, u))
    # Attribute unresolved MonoBehaviours to a class through their script hash, when that
    # hash maps to exactly one resolved class elsewhere. They are counted separately.
    left = collections.OrderedDict()
    for src, u in unresolved:
        keys = sid_to_keys.get(u["script_id"], set())
        if len(keys) == 1:
            s = scripts[next(iter(keys))]
            s.setdefault("unresolved_count", 0)
            s["unresolved_count"] += u["count"]
            s.setdefault("unresolved_sources", []).append([src, u["count"]])
        else:
            lu = left.setdefault(u["script_id"], {"script_id": u["script_id"], "count": 0, "error": clean_error(u["error"]),
                                                  "ambiguous": len(keys) > 1, "sources": [],
                                                  "fields": collections.OrderedDict(), "conflicts": []})
            lu["count"] += u["count"]
            lu["sources"].append([src, u["count"]])
            for v in u["variants"]:
                merge_fields(lu["fields"], lu["conflicts"], v["fields"])
    out_scripts = []
    for s in scripts.values():
        s = dict(s)
        s["variant_count"] = len(s.pop("variants"))
        s["script_ids"] = sorted(s["script_ids"])
        s["fields"] = list(s["fields"].values())
        s["scriptableObject"] = s["first_so"]
        s["so_mixed"] = s["so"] > 0 and s["nonso"] > 0
        out_scripts.append(s)
    for lu in left.values():
        lu["fields"] = list(lu["fields"].values())
    progress = {}
    try:
        with open(os.path.join(out, "progress.json"), encoding="utf-8") as f:
            progress = json.load(f)
    except Exception:
        pass
    merged = {"version": RESULT_VERSION, "generated": datetime.datetime.now().isoformat(timespec="seconds"),
              "partial_hint": {"sources_merged": totals["sources"],
                               "sources_total_last_scan": progress.get("sources_total")},
              "totals": dict(totals), "base_fields_excluded": sorted(BASE_FIELDS),
              "node_format": "[type, name, [children...]]",
              "scripts": out_scripts, "unresolved": list(left.values()),
              "shaders": [{"name": n, "count": c, "sources": shader_src[n]} for n, c in shaders.most_common()],
              "types": dict(types.most_common()), "errors": errors}
    write_json_atomic(os.path.join(out, "merged.json"), merged)
    if survey_dir:
        os.makedirs(survey_dir, exist_ok=True)
        with open(os.path.join(survey_dir, "class_census_full.tsv"), "w", encoding="utf-8", newline="\n") as f:
            f.write("assembly\tclass\tobjects\tsources\tscriptable_object\n")
            for s in sorted(out_scripts, key=lambda s: (-s["count"], s["assembly"], s["namespace"], s["class"])):
                fn = (s["namespace"] + "." if s["namespace"] else "") + s["class"]
                f.write(f"{s['assembly']}\t{fn}\t{s['count']}\t{len(s['sources'])}\t{int(bool(s['first_so']))}\n")
        with open(os.path.join(survey_dir, "shader_census_full.tsv"), "w", encoding="utf-8", newline="\n") as f:
            f.write("shader\tcount\tsources\n")
            for n, c in sorted(shaders.items(), key=lambda kv: (-kv[1], kv[0])):
                f.write(f"{n}\t{c}\t{shader_src[n]}\n")
    log(f"MERGE {totals['sources']} sources ({totals['abdata']} abdata, {totals['zipmod']} zipmods), "
        f"{len(out_scripts)} script classes, {len(left)} unresolved script ids, {len(shaders)} shaders, "
        f"{len(errors)} errors, {time.time() - t0:.1f}s -> {os.path.join(out, 'merged.json')}")
    return merged


# --------------------------------------------------------------------------------------
# Status
# --------------------------------------------------------------------------------------

def cmd_status(a):
    out = os.path.abspath(a.out)
    try:
        with open(os.path.join(out, "progress.json"), encoding="utf-8") as f:
            p = json.load(f)
    except FileNotFoundError:
        print(f"no progress.json in {out}")
        p = {}
    nres = len(latest_results(out))
    lock_pid = None
    try:
        lock_pid = int(open(os.path.join(out, "scan.lock")).read().strip())
    except Exception:
        pass
    alive = pid_alive(lock_pid)
    print(f"cache: {out}")
    print(f"result files: {nres}")
    if p:
        age = time.time() - p["updated"]
        print(f"scan pid {p['pid']}: {'RUNNING' if alive else 'not running'}"
              f"{' (final)' if p.get('final') else ''}; snapshot {fmt_dur(age)} old")
        print(f"this run: {p['done']}/{p['todo']} sources, {p['bytes_done'] / 1e9:.1f}/{p['bytes_todo'] / 1e9:.1f} GB,"
              f" {p['rate_mb_s']} MB/s, ETA {fmt_dur(p['eta_s'] - (age if alive else 0))}"
              f" (sources total {p['sources_total']}, done before this run {p['already_done']},"
              f" sources with errors {p['errors_sources']})")
        if p.get("inflight"):
            print("in flight: " + ", ".join(p["inflight"]))
    logp = os.path.join(out, "scan.log")
    if os.path.exists(logp):
        with open(logp, encoding="utf-8") as f:
            tail = collections.deque(f, maxlen=a.tail)
        print("--- scan.log tail ---")
        print("".join(tail), end="")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("scan", help="scan bundles (resumable)")
    s.add_argument("game_root", help="HS2 install folder (contains abdata/ and mods/)")
    s.add_argument("--out", default=DEFAULT_OUT, help="cache dir (default: %(default)s)")
    s.add_argument("--workers", type=int, default=5, help="workers for normal sources")
    s.add_argument("--big-workers", type=int, default=1, help="workers for sources with a big bundle")
    s.add_argument("--big-mb", type=int, default=300, help="bundle size (MB) that routes to the big pool")
    s.add_argument("--tasks-per-child", type=int, default=40, help="recycle worker processes (memory)")
    s.add_argument("--include", action="append", default=[], help="glob on relpath, e.g. 'mods/*Tunnel*'")
    s.add_argument("--exclude", action="append", default=[], help="glob on relpath to skip")
    s.add_argument("--file-list", help="text file of relpaths to scan (instead of discovery)")
    s.add_argument("--limit", type=int, default=0, help="only the first N sources (tests)")
    s.add_argument("--report-every", type=float, default=30.0, help="seconds between progress lines")
    s.add_argument("--survey-dir", default=DEFAULT_SURVEY, help="where merge writes the census TSVs")
    s.add_argument("--no-merge", action="store_true", help="do not merge at the end")
    m = sub.add_parser("merge", help="merge finished results (works on partial scans)")
    m.add_argument("--out", default=DEFAULT_OUT)
    m.add_argument("--survey-dir", default=DEFAULT_SURVEY, help="'' to skip the TSVs")
    m.add_argument("--file-list", help="merge only these relpaths, in this order")
    st = sub.add_parser("status", help="print progress of a running or finished scan")
    st.add_argument("--out", default=DEFAULT_OUT)
    st.add_argument("--tail", type=int, default=8)
    a = ap.parse_args()
    # `python -I` ignores PYTHONUTF8; make console/redirected output safe for any file name.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    if a.cmd == "scan":
        cmd_scan(a)
    elif a.cmd == "merge":
        merge(os.path.abspath(a.out), a.survey_dir or None, a.file_list)
    else:
        cmd_status(a)


if __name__ == "__main__":
    main()
