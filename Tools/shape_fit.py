"""Fit the shape-slider mapping tables (ShapeEngine map format) from black-box measurements.

Clean room: the tables are derived only from
  * measured bone dumps of the running game (Cache/shapemeasure/<set>_samples.csv, <set>_bones.csv), and
  * game DATA read with UnityPy: the per-bone shape key data (cf_anmShapeBody, the head's ShapeAnime)
    and the slider info lists (cf_custombody, cf_customhead / cm_customhead).
No existing map file is read.

Model (Runtime/Chara/ShapeEngine.cs): a slider value v in [0,1] picks a key at (25-1)*v with linear
interpolation (LerpAngle for rotations) and writes the flagged channels of its Src bones; every Dst
channel is an expression over Src channels. This tool replicates the Src stage, finds the Dst bones
(transforms whose local TRS varies over the samples) and fits each Dst channel, simplest form first:
  1. sparse sum of Src channels (orthogonal matching pursuit + backward elimination + swap search,
     coefficients snapped to simple values, constant term allowed), then a +-1 sum searched slider by
     slider on the sweeps;
  2. product / quotient of one Src channel per slider (matched on the sweep ratios), then products of
     per-slider (or per slider group) sparse factors by alternating least squares;
  3. sparse polynomial over cross-slider products.
Src channels the measurements cannot tell apart (affine-equivalent over all samples) are interchangeable;
the one giving coefficient +-1 and the matching side / channel / name is preferred. Every form is
validated through the engine path (raw LerpAngle Src values) on held-out samples (random / pair).
Channels no form explains are left out of the map and listed; a small linear approximation of them goes
into a separate *.approx.txt map only.

Usage:
  python -I Tools/shape_fit.py [--data DIR] [--out DIR] [--sets female_body,female_face,...]
                               [--card-female PNG] [--card-male PNG] [--game-root DIR]
Defaults: data <repo>/Cache/shapemeasure, out <repo>/Tools/survey/shape, game root from
Config/settings.local.json (hs2Root), cards as in the Unity tests (BR-Chan; first male card).
Outputs: shape_body_map.fitted.txt, shape_face_map.fitted.txt, [shape_face_map_male.fitted.txt],
[shape_body_map.approx.txt / shape_face_map.approx.txt: fitted rows + approximations of the unexplained
channels], shape_remaps.fitted.txt (per-set slider value remaps), fit_report.md.
"""
import argparse
import collections
import csv
import functools
import glob
import io
import itertools
import json
import math
import os
import struct
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
F32 = np.float32

CH = ["pos.x", "pos.y", "pos.z", "rot.x", "rot.y", "rot.z", "scl.x", "scl.y", "scl.z"]
GROUPS = {"pos": (0, 1, 2), "rot": (3, 4, 5), "scl": (6, 7, 8)}
NICE = [1.0, -1.0, 0.5, -0.5, 2.0, -2.0, 0.25, -0.25, 0.75, -0.75, 1.5, -1.5, 3.0, -3.0, 0.1, -0.1,
        0.2, -0.2, 0.3, -0.3, 0.4, -0.4, 0.6, -0.6, 0.7, -0.7, 0.8, -0.8, 0.9, -0.9, 4.0, -4.0, 5.0, -5.0,
        10.0, -10.0, 1.0 / 3, -1.0 / 3, 2.0 / 3, -2.0 / 3]
SETS = ["female_body", "female_face", "male_body", "male_face"]


# ======================================================================================
# Settings and game data
# ======================================================================================

def game_root_from_settings():
    for name in ("settings.local.json", "settings.json"):
        p = os.path.join(REPO, "Config", name)
        if os.path.exists(p):
            with open(p, encoding="utf-8") as f:
                s = json.load(f)
            r = s.get("hs2Root") or ""
            if r:
                return r if os.path.isabs(r) else os.path.normpath(os.path.join(REPO, r))
    raise SystemExit("no hs2Root in Config/settings.local.json (or give --game-root)")


class GameData:
    def __init__(self, root):
        self.root = root
        self.abdata = os.path.join(root, "abdata")
        self._ta = {}
        self._lists = None

    def text_assets(self, rel):
        if rel not in self._ta:
            import UnityPy
            env = UnityPy.load(os.path.join(self.abdata, rel))
            r = {}
            for o in env.objects:
                if o.type.name == "TextAsset":
                    d = o.read()
                    s = d.m_Script
                    r[d.m_Name] = s.encode("utf-8", "surrogateescape") if isinstance(s, str) else bytes(s)
            self._ta[rel] = r
        return self._ta[rel]

    def list_entry(self, cat, key):
        """Vanilla characustom list row (later lists win, as in ContentCatalog)."""
        if self._lists is None:
            import msgpack
            self._lists = {}
            for f in sorted(glob.glob(os.path.join(self.abdata, "list", "characustom", "*.unity3d"))):
                rel = os.path.relpath(f, self.abdata).replace(os.sep, "/")
                for b in self.text_assets(rel).values():
                    try:
                        m = msgpack.unpackb(b, raw=False, strict_map_key=False)
                    except Exception:
                        continue
                    if not isinstance(m, dict) or "categoryNo" not in m:
                        continue
                    keys = m.get("lstKey") or []
                    for k, v in (m.get("dictList") or {}).items():
                        self._lists[(m["categoryNo"], int(k))] = dict(zip(keys, v))
        return self._lists.get((cat, key))


def _rstr(f):
    n, sh = 0, 0
    while True:
        b = f.read(1)[0]
        n |= (b & 0x7F) << sh
        sh += 7
        if not b & 0x80:
            break
    return f.read(n).decode("utf-8")


def parse_keys(b):
    """Key data (ShapeEngine constructor format): {src: float32 array (K, 9)}."""
    f = io.BytesIO(b)
    n = struct.unpack("<i", f.read(4))[0]
    out = {}
    for _ in range(n):
        name = _rstr(f)
        kc = struct.unpack("<i", f.read(4))[0]
        arr = np.zeros((kc, 9), F32)
        for k in range(kc):
            f.read(4)
            arr[k] = struct.unpack("<9f", f.read(36))
        out[name] = arr
    return out


def parse_info(text):
    """Slider info list: [(slider, src, flags[9])] in file order (ShapeEngine constructor rules)."""
    rows = []
    for raw in text.split("\n"):
        c = raw.rstrip("\r").split("\t")
        if len(c) < 11:
            continue
        try:
            s = int(c[0])
        except ValueError:
            continue
        rows.append((s, c[1], [x != "0" for x in c[2:11]]))
    return rows


def card_head_id(path):
    sys.path.insert(0, HERE)
    import hs2card
    c = hs2card.load_card(path)
    return int(c["custom"]["face"].get("headId", 0))


def default_cards(game_root):
    female = os.path.join(game_root, "UserData", "chara", "female", "BR-Chan HS2 Rev2.png")
    mdir = os.path.join(game_root, "UserData", "chara", "male")
    males = []
    for r, _, fs in os.walk(mdir):
        for f in fs:
            if f.lower().endswith(".png"):
                p = os.path.join(r, f)
                males.append((p[len(mdir):].replace("\\", "/"), p))
    # Ordinal order of the relative path, as in the Unity tests.
    males.sort(key=lambda t: [ord(ch) for ch in t[0]])
    return female, (males[0][1] if males else None)


def load_shape_data(gd, set_name, card):
    """(keys, info rows, description) for a set, as CharaAssembler loads them."""
    cs = gd.text_assets("list/customshape.unity3d")
    sex, part = set_name.split("_")
    if part == "body":
        # The assembler uses the female body data for both sexes.
        return parse_keys(cs["cf_anmShapeBody"]), parse_info(cs["cf_custombody"].decode("utf-8")), \
            "keys cf_anmShapeBody, info cf_custombody"
    cat = 210 if sex == "female" else 110
    head = card_head_id(card) if card else (1 if sex == "female" else 0)
    e = gd.list_entry(cat, head)
    if e is None:
        raise SystemExit(f"{set_name}: head {head} (category {cat}) is not a vanilla list row")
    bundle, asset = e["MainAB"], e["ShapeAnime"]
    if sex == "female" and head == 0:
        bundle = "chara/35/fo_head_35.unity3d"   # the assembler's Studio override for female head 0
    info_name = "cf_customhead" if sex == "female" else "cm_customhead"
    keys = parse_keys(gd.text_assets(bundle)[asset])
    return keys, parse_info(cs[info_name].decode("utf-8")), f"head {head}: keys {bundle}:{asset}, info {info_name}"


# ======================================================================================
# Src stage (replicates ShapeEngine.SetValue)
# ======================================================================================

def repeat(t, length):
    return np.clip(t - np.floor(t / length) * length, F32(0), F32(length)).astype(F32)


def eval_keys(k, u):
    """Key interpolation for all samples: k (K, 9) float32, u (N,) -> (N, 9) float32."""
    u = np.clip(u.astype(F32), F32(0), F32(1))
    K = k.shape[0]
    f = F32(K - 1) * u
    i = np.floor(f).astype(np.int64)
    i = np.clip(i, 0, K - 1)
    t = (f - i.astype(F32)).astype(F32)[:, None]
    a = k[i]
    b = k[np.minimum(i + 1, K - 1)]
    out = (a + (b - a) * t).astype(F32)
    delta = repeat(b[:, 3:6] - a[:, 3:6], 360.0)
    delta = np.where(delta > 180, delta - F32(360), delta).astype(F32)
    out[:, 3:6] = a[:, 3:6] + delta * np.clip(t, 0, 1)
    out[u <= 0] = k[0]
    out[u >= 1] = k[K - 1]
    return out


class SrcModel:
    def __init__(self, keys, info):
        self.keys = keys
        self.info = info
        self.links = collections.defaultdict(list)
        for s, src, fl in info:
            self.links[s].append((src, fl))
        self.sliders = sorted(self.links)
        self.src_names = set(src for _, src, _ in info)   # key entries no slider uses can be real bones (N_Megane)
        cols = []
        for s, src, fl in info:
            for c in range(9):
                if fl[c] and (src, c) not in cols:
                    cols.append((src, c))
        self.cols = cols                                 # every Src channel some slider writes
        self.col_index = {c: i for i, c in enumerate(cols)}
        self.writers = collections.defaultdict(list)     # column -> sliders writing it
        for s, src, fl in info:
            for c in range(9):
                if fl[c] and s not in self.writers[(src, c)]:
                    self.writers[(src, c)].append(s)

    def slider_cols(self, s):
        return sorted(set(self.col_index[(src, c)] for src, fl in self.links[s] for c in range(9) if fl[c]))

    def compute(self, V, remaps):
        """V (N, n) slider values -> (N, len(cols)) Src channel values. Sliders are set in ascending order
        (ApplyShapes loop), the links of a slider in file order; later writes win."""
        N, n = V.shape
        X = np.zeros((N, len(self.cols)), F32)
        default = np.array([0, 0, 0, 0, 0, 0, 1, 1, 1], F32)
        for j, (src, c) in enumerate(self.cols):
            X[:, j] = default[c]
        for s in self.sliders:
            if s >= n:
                continue
            u = remaps.get(s, REMAP_ID).apply(V[:, s])
            for src, fl in self.links[s]:
                k = self.keys.get(src)
                if k is None or len(k) == 0:
                    continue
                val = eval_keys(k, u)
                for c in range(9):
                    if fl[c]:
                        X[:, self.col_index[(src, c)]] = val[:, c]
        return X

    def col_name(self, j):
        src, c = self.cols[j]
        return f"{src}.{CH[c]}"


class Remap:
    """Slider value remap applied before SetValue: u = a + b*v, or a constant."""

    def __init__(self, a=0.0, b=1.0, const=None):
        self.a, self.b, self.const = a, b, const

    def apply(self, v):
        if self.const is not None:
            return np.full_like(v, self.const, dtype=F32)
        return (F32(self.a) + F32(self.b) * v.astype(F32)).astype(F32)

    @property
    def is_id(self):
        return self.const is None and self.a == 0 and self.b == 1

    def __str__(self):
        if self.const is not None:
            return f"constant {self.const:g}"
        if self.is_id:
            return "identity"
        if self.a == 0:
            return f"Lerp(0, {self.b:g}, v)"
        return f"Lerp({self.a:g}, {self.a + self.b:g}, v)"

    def key(self):
        return ("c", self.const) if self.const is not None else ("l", round(self.a, 6), round(self.b, 6))


REMAP_ID = Remap()


# ======================================================================================
# Rotations (Unity convention: Quaternion.Euler(x, y, z) = Ry * Rx * Rz)
# ======================================================================================

def quat_from_euler(e):
    e = np.radians(np.asarray(e, np.float64)) * 0.5
    x, y, z = e[..., 0], e[..., 1], e[..., 2]
    cx, sx, cy, sy, cz, sz = np.cos(x), np.sin(x), np.cos(y), np.sin(y), np.cos(z), np.sin(z)
    # q = qy * qx * qz (xyzw)
    qw = cy * cx * cz + sy * sx * sz
    qx = cy * sx * cz + sy * cx * sz
    qy = sy * cx * cz - cy * sx * sz
    qz = cy * cx * sz - sy * sx * cz
    return np.stack([qx, qy, qz, qw], -1)


def euler_from_quat(q):
    q = np.asarray(q, np.float64)
    q = q / np.linalg.norm(q, axis=-1, keepdims=True)
    x, y, z, w = q[..., 0], q[..., 1], q[..., 2], q[..., 3]
    r02 = 2 * (x * z + w * y)
    r12 = 2 * (y * z - w * x)
    r22 = 1 - 2 * (x * x + y * y)
    r10 = 2 * (x * y + w * z)
    r11 = 1 - 2 * (x * x + z * z)
    ex = np.degrees(np.arcsin(np.clip(-r12, -1, 1)))
    ey = np.degrees(np.arctan2(r02, r22))
    ez = np.degrees(np.arctan2(r10, r11))
    return np.mod(np.stack([ex, ey, ez], -1), 360.0)


def quat_angle(a, b):
    d = np.abs(np.sum(np.asarray(a, np.float64) * np.asarray(b, np.float64), -1))
    d /= np.linalg.norm(a, axis=-1) * np.linalg.norm(b, axis=-1)
    return np.degrees(2 * np.arccos(np.clip(d, -1, 1)))


def wrap180(a):
    return (np.asarray(a, np.float64) + 180.0) % 360.0 - 180.0


def unwrap_series(a):
    """Angles (deg) -> continuous values around their circular mean, in (-180, 180] + mean."""
    a = np.asarray(a, np.float64)
    m = math.degrees(math.atan2(np.mean(np.sin(np.radians(a))), np.mean(np.cos(np.radians(a)))))
    m = float(wrap180(m))
    return m + wrap180(a - m)


# ======================================================================================
# Measurements
# ======================================================================================

class Measured:
    def __init__(self, data_dir, set_name):
        sp = os.path.join(data_dir, f"{set_name}_samples.csv")
        bp = os.path.join(data_dir, f"{set_name}_bones.csv")
        self.ok = os.path.exists(sp) and os.path.exists(bp)
        if not self.ok:
            return
        self.ids, self.kind, self.slider, rows = [], [], [], []
        with open(sp, newline="", encoding="utf-8-sig") as f:
            r = csv.reader(f)
            head = next(r)
            for row in r:
                if not row:
                    continue
                n = int(row[4])
                self.ids.append(row[0])
                self.kind.append(row[1].strip().lower())
                self.slider.append(row[2])
                rows.append([float(x) for x in row[5:5 + n]])
        n = max(len(r) for r in rows)
        self.V = np.full((len(rows), n), 0.5, F32)
        for i, r in enumerate(rows):
            self.V[i, :len(r)] = r
        self.N = len(rows)
        idx = {s: i for i, s in enumerate(self.ids)}
        data = collections.defaultdict(lambda: np.full((self.N, 13), np.nan))
        self.paths = {}
        dup = collections.defaultdict(set)
        with open(bp, newline="", encoding="utf-8-sig") as f:
            r = csv.reader(f)
            head = [h.strip() for h in next(r)]
            ci = {h: i for i, h in enumerate(head)}
            cols = [ci[c] for c in ("px", "py", "pz", "qx", "qy", "qz", "qw", "ex", "ey", "ez", "sx", "sy", "sz")]
            for row in r:
                if not row:
                    continue
                si = idx.get(row[ci["sample"]])
                if si is None:
                    continue
                name, path = row[ci["bone"]], row[ci["path"]]
                key = path or name
                if key not in self.paths:
                    self.paths[key] = name
                    dup[name].add(key)
                data[key][si] = [float(row[c]) for c in cols]
        self.data = dict(data)
        self.dup_names = {n: sorted(p) for n, p in dup.items() if len(p) > 1}
        # First match by name in depth-first order is what ShapeEngine / CharaAssembler.Find uses;
        # the CSV order of the paths is assumed to be the dump (depth-first) order.
        self.by_name = {}
        for key, name in self.paths.items():
            self.by_name.setdefault(name, key)
        kinds = [i for i, k in enumerate(self.kind) if k == "rest"]
        self.ref = kinds[0] if kinds else 0
        self.missing = {k: int(np.isnan(v[:, 0]).sum()) for k, v in self.data.items() if np.isnan(v[:, 0]).any()}

    def kinds(self):
        return collections.Counter(self.kind)


# ======================================================================================
# Sparse fitting
# ======================================================================================

def lsq(A, y):
    if A.shape[1] == 0:
        return np.zeros(0)
    c, *_ = np.linalg.lstsq(A, y, rcond=1e-12)
    return c


def design(X, terms):
    """terms: list of tuples of column indices (product terms)."""
    if not terms:
        return np.zeros((X.shape[0], 0))
    return np.stack([np.prod(X[:, list(t)], axis=1) for t in terms], 1)


def fit_coef(X, y, terms, const=True):
    A = design(X, terms)
    if const:
        A = np.hstack([A, np.ones((len(y), 1))])
    c = lsq(A, y)
    r = y - A @ c
    return c, r


def nice(v, tol):
    for n in NICE:
        if abs(v - n) <= tol:
            return n
    return None


def round_const(c, ok):
    """Shortest decimal rounding of c accepted by ok(value)."""
    for d in range(0, 8):
        v = round(c, d)
        if ok(v):
            return v
    return c


class Model:
    """y = const + sum(coef * prod(columns)) ; mult: y = const * prod(columns)."""

    def __init__(self, terms, coefs, const, mult=False):
        self.terms, self.coefs, self.const, self.mult = list(terms), list(coefs), float(const), mult

    def predict(self, X):
        if self.mult:
            return self.const * (np.prod(X[:, [t[0] for t in self.terms]], axis=1) if self.terms else 1.0)
        y = np.full(X.shape[0], self.const)
        for t, c in zip(self.terms, self.coefs):
            y = y + c * np.prod(X[:, list(t)], axis=1)
        return y

    @property
    def n_terms(self):
        return len(self.terms)


def omp_fit(X, y, cand, pref, tol, max_terms=24, prods=False, refine=True, terms_all=None):
    """Sparse linear model over columns `cand` (+ constant); returns (Model or None, max residual)."""
    yc = y - y.mean()
    if np.max(np.abs(yc)) <= tol:
        return Model([], [], float(y.mean())), float(np.max(np.abs(yc)))
    if terms_all is None:
        terms_all = [(j,) for j in cand]
        if prods:
            terms_all += [(a, b) for i, a in enumerate(cand) for b in cand[i:]]
    A = design(X, terms_all)
    A = A - A.mean(0)
    norm = np.linalg.norm(A, axis=0)
    keep = norm > 1e-9
    terms_all = [t for t, k in zip(terms_all, keep) if k]
    A, norm = A[:, keep], norm[keep]
    if not terms_all:
        return None, float(np.max(np.abs(yc)))
    An = A / norm
    tp = np.array([sum(pref.get(j, 0) for j in t) - (2 if len(t) > 1 else 0) for t in terms_all], float)
    # collapse perfectly correlated terms (affine-equivalent columns) to the most preferred one: they are
    # indistinguishable and make the least-squares systems singular
    order = np.argsort(-tp, kind="stable")
    kept = []
    for i in order:
        if kept and np.max(np.abs(An[:, kept].T @ An[:, i])) > 1 - 1e-9:
            continue
        kept.append(int(i))
    kept.sort()
    terms_all = [terms_all[i] for i in kept]
    A, An, tp = A[:, kept], An[:, kept], tp[kept]
    sel = []
    r = yc.copy()
    best = None
    for _ in range(min(max_terms, len(terms_all))):
        corr = np.abs(An.T @ r)
        corr[sel] = -1
        top = corr.max()
        if top <= 1e-12:
            break
        tie = np.where(corr >= top * (1 - 1e-6))[0]
        k = int(tie[np.argmax(tp[tie])])
        sel.append(k)
        c = lsq(A[:, sel], yc)
        r = yc - A[:, sel] @ c
        if np.max(np.abs(r)) <= tol:
            best = list(sel)
            break
    res = float(np.max(np.abs(r)))
    if best is None:
        return None, res
    terms = [terms_all[k] for k in best]
    if not refine:
        c, rr = fit_coef(X, y, terms)
        return Model(terms, c[:-1], c[-1]), float(np.max(np.abs(rr)))

    def err(ts):
        _, rr = fit_coef(X, y, ts)
        return float(np.max(np.abs(rr)))

    # backward elimination (least preferred first)
    changed = True
    while changed and len(terms) > 1:
        changed = False
        for t in sorted(terms, key=lambda t: sum(pref.get(j, 0) for j in t)):
            ts = [u for u in terms if u != t]
            if err(ts) <= tol:
                terms = ts
                changed = True
                break
    # swap search: replace a term by a more preferred candidate that explains equally well
    for i in range(len(terms)):
        cur = sum(pref.get(j, 0) for j in terms[i])
        for t in sorted(terms_all, key=lambda t: -sum(pref.get(j, 0) for j in t)):
            if sum(pref.get(j, 0) for j in t) <= cur:
                break
            if t in terms:
                continue
            ts = terms[:i] + [t] + terms[i + 1:]
            if err(ts) <= tol:
                terms = ts
                cur = sum(pref.get(j, 0) for j in t)
                break
    c, rr = fit_coef(X, y, terms)
    return Model(terms, c[:-1], c[-1]), float(np.max(np.abs(rr)))


class ProdModel:
    """y = k * prod(factor_i ** exp_i), exp_i = +1 or -1, each factor an additive Model over the columns
    of one slider (or one group of sliders)."""

    mult = False

    def __init__(self, k, factors, exps=None):
        self.k, self.factors = float(k), list(factors)
        self.exps = list(exps) if exps is not None else [1] * len(self.factors)

    def predict(self, X):
        p = np.full(X.shape[0], self.k)
        for f, e in zip(self.factors, self.exps):
            p = p * f.predict(X) if e > 0 else p / f.predict(X)
        return p

    @property
    def terms(self):
        return [t for f in self.factors for t in f.terms]

    @property
    def coefs(self):
        return [c for f in self.factors for c in f.coefs]

    @property
    def const(self):
        return self.k

    @property
    def n_terms(self):
        return sum(f.n_terms for f in self.factors)


def als_fit(X, y, groups, tol, iters=200):
    """Separable product y = prod_g (A_g c_g) with A_g = [columns of group g, 1] (alternating least squares).
    Returns the per-group factor values (N, G) or None."""
    N = len(y)
    F = [np.ones(N) for _ in groups]
    A = [np.hstack([X[:, g], np.ones((N, 1))]) for g in groups]
    res = np.inf
    for _ in range(iters):
        for i in range(len(groups)):
            other = np.ones(N)
            for j in range(len(groups)):
                if j != i:
                    other = other * F[j]
            c = lsq(other[:, None] * A[i], y)
            F[i] = A[i] @ c
        p = np.prod(np.stack(F, 1), 1)
        r = float(np.max(np.abs(p - y)))
        if r <= tol * 0.05 or r > res * 0.999999:
            res = min(res, r)
            break
        res = r
    if res > tol:
        return None, res
    return np.stack(F, 1), res


def complexity(m):
    """(coefficients that are not simple values, terms): lower is simpler."""
    odd = sum(1 for c in m.coefs if nice(c, 1e-9) is None)
    if isinstance(m, ProdModel):
        odd += nice(m.k, 1e-9) is None
    return (odd, m.n_terms)


def partitions(items):
    """All set partitions of a list (lists of lists)."""
    if not items:
        yield []
        return
    first, rest = items[0], items[1:]
    for p in partitions(rest):
        for i in range(len(p)):
            yield p[:i] + [[first] + p[i]] + p[i + 1:]
        yield [[first]] + p


def lin_snap_params(model, ok):
    """Snap the coefficients and the constant of an additive Model, keeping ok(model) true."""
    for i, c in enumerate(list(model.coefs)):
        cand = [n for n in [nice(c, max(1e-4, 0.002 * abs(c)))] if n is not None] + [round(c, d) for d in range(1, 8)]
        for v in cand:
            old = model.coefs[i]
            model.coefs[i] = v
            if ok():
                break
            model.coefs[i] = old
    keep = [i for i, c in enumerate(model.coefs) if c != 0]
    if len(keep) < len(model.coefs):
        t, c = model.terms, model.coefs
        model.terms, model.coefs = [t[i] for i in keep], [c[i] for i in keep]
    old = model.const
    for d in range(0, 8):
        model.const = round(old, d)
        if ok():
            return
    model.const = old


def snap(model, X, y, tol):
    """Snap coefficients to simple values and round the constant, keeping the residual within tol."""
    coefs = list(model.coefs)
    snapped = [nice(c, max(1e-4, 0.002 * abs(c))) for c in coefs]

    def const_for(cs):
        return float(np.mean(y - sum(c * np.prod(X[:, list(t)], axis=1) for t, c in zip(model.terms, cs)) if cs else y))

    def maxres(cs, k):
        m = Model(model.terms, cs, k)
        return float(np.max(np.abs(m.predict(X) - y)))

    cs = [s if s is not None else c for s, c in zip(snapped, coefs)]
    if maxres(cs, const_for(cs)) > tol:
        cs = list(coefs)
        for i, s in enumerate(snapped):
            if s is None:
                continue
            trial = cs[:i] + [s] + cs[i + 1:]
            if maxres(trial, const_for(trial)) <= tol:
                cs = trial
    # unsnapped coefficients: shortest rounding that stays within tol
    for i, c in enumerate(cs):
        if c in NICE:
            continue
        for d in range(1, 8):
            trial = cs[:i] + [round(c, d)] + cs[i + 1:]
            if maxres(trial, const_for(trial)) <= tol:
                cs = trial
                break
    terms = [t for t, c in zip(model.terms, cs) if c != 0]
    cs = [c for c in cs if c != 0]
    model = Model(terms, cs, 0.0)
    k = const_for(cs)
    base = maxres(cs, k)
    k = round_const(k, lambda v: maxres(cs, v) <= max(tol * 0.5, base + tol * 0.1))
    return Model(model.terms, cs, k)


# ======================================================================================
# Expressions and map files
# ======================================================================================

def fmt_num(v):
    v = float(v)
    if abs(v - round(v)) < 1e-9:
        return str(int(round(v)))
    s = np.format_float_positional(F32(v), trim="-", unique=True)
    s2 = np.format_float_positional(v, trim="-", precision=8, unique=False).rstrip("0").rstrip(".")
    return s if len(s) <= len(s2) else s2


def expr_of(model, names):
    """Expression text (ShapeEngine.Expr grammar: numbers, refs, + - * /, unary minus, parentheses)."""
    if isinstance(model, ProdModel):
        num, den = [], []
        for f, ex in zip(model.factors, model.exps):
            e = expr_of(f, names)
            e = e if (len(f.terms) == 1 and f.const == 0 and abs(f.coefs[0] - 1) < 1e-12) else "(" + e + ")"
            (num if ex > 0 else den).append(e)
        k = model.k
        if abs(abs(k) - 1) > 1e-12 or not num:
            num.insert(0, fmt_num(abs(k)))
        out = " * ".join(num) + "".join(" / " + d for d in den)
        return out if k > 0 else "-" + out
    items = []
    for t, c in sorted(zip(model.terms, model.coefs), key=lambda tc: [names[j] for j in tc[0]]):
        ref = " * ".join(names[j] for j in t)
        if abs(c - 1) < 1e-12:
            items.append((1, ref))
        elif abs(c + 1) < 1e-12:
            items.append((-1, ref))
        else:
            items.append((1 if c > 0 else -1, fmt_num(abs(c)) + " * " + ref))
    k = model.const
    if abs(k) > 1e-12 or not items:
        items.append((1 if k >= 0 else -1, fmt_num(abs(k))))
    out = ""
    for i, (sg, s) in enumerate(items):
        if i == 0:
            out = s if sg > 0 else "-" + s
        else:
            out += (" + " if sg > 0 else " - ") + s
    return out


class ExprEval:
    """Python twin of ShapeEngine.Expr (vectorised over samples)."""

    def __init__(self, text, ref):
        self.s, self.p, self.ref = text, 0, ref

    def run(self):
        self.p = 0
        v = self.sum()
        return v

    def ws(self):
        while self.p < len(self.s) and self.s[self.p] == " ":
            self.p += 1

    def sum(self):
        a = self.prod()
        while True:
            self.ws()
            if self.p < len(self.s) and self.s[self.p] in "+-":
                op = self.s[self.p]
                self.p += 1
                b = self.prod()
                a = a + b if op == "+" else a - b
            else:
                return a

    def prod(self):
        a = self.unary()
        while True:
            self.ws()
            if self.p < len(self.s) and self.s[self.p] in "*/":
                op = self.s[self.p]
                self.p += 1
                b = self.unary()
                a = a * b if op == "*" else a / b
            else:
                return a

    def unary(self):
        self.ws()
        if self.s[self.p] == "-":
            self.p += 1
            return -self.unary()
        if self.s[self.p] == "(":
            self.p += 1
            a = self.sum()
            self.ws()
            self.p += 1
            return a
        st = self.p
        while self.p < len(self.s) and (self.s[self.p].isalnum() or self.s[self.p] in "_."):
            self.p += 1
        tok = self.s[st:self.p]
        try:
            return F32(float(tok))
        except ValueError:
            return self.ref(tok)


def split_top(s):
    r, depth, start = [], 0, 0
    for i, ch in enumerate(s):
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == "," and depth == 0:
            r.append(s[start:i])
            start = i + 1
    r.append(s[start:])
    return r


def parse_map(text):
    rows = []
    for raw in text.split("\n"):
        if not raw.strip():
            continue
        c = raw.rstrip("\r").split("\t")
        rows.append((c[1], c[2], split_top(c[3])))
    return rows


def eval_map(rows, srcm, X, meas):
    """Apply map rows to the rest values of the measured bones; returns {key: (N, 13) predicted}
    (pos, quat, euler, scale), only for the bones the rows write."""
    colmap = {f"{src}.{CH[c]}": j for j, (src, c) in enumerate(srcm.cols)}
    default = {"pos": 0.0, "rot": 0.0, "scl": 1.0}

    def ref(name):
        j = colmap.get(name)
        if j is not None:
            return X[:, j].astype(F32)
        ch = name.rsplit(".", 2)[-2]
        return F32(default.get(ch, 1.0))

    out = {}
    for dst, op, args in rows:
        key = meas.by_name.get(dst)
        if key is None:
            continue
        if key not in out:
            # channels no row writes keep the reference (rest) sample's values
            out[key] = np.repeat(meas.data[key][meas.ref:meas.ref + 1], meas.N, 0).astype(np.float64)
        v = [np.broadcast_to(ExprEval(a, ref).run(), (meas.N,)).astype(np.float64) for a in args]
        o = out[key]
        if op == "PositionX":
            o[:, 0] = v[0]
        elif op == "PositionY":
            o[:, 1] = v[0]
        elif op == "PositionZ":
            o[:, 2] = v[0]
        elif op == "Position":
            o[:, 0], o[:, 1], o[:, 2] = v
        elif op == "Rotation":
            e = np.stack(v, 1)
            o[:, 3:7] = quat_from_euler(e)
            o[:, 7:10] = np.mod(e, 360)
        elif op == "Scale":
            o[:, 10], o[:, 11], o[:, 12] = v
    return out


def compare(pred, meas, idx):
    """Errors of predicted vs measured bones over sample indices idx."""
    res = {}
    for key, p in pred.items():
        m = meas.data[key][idx]
        p = p[idx]
        dp = float(np.nanmax(np.abs(p[:, 0:3] - m[:, 0:3])))
        da = float(np.nanmax(quat_angle(p[:, 3:7], m[:, 3:7])))
        ds = float(np.nanmax(np.abs(p[:, 10:13] - m[:, 10:13])))
        res[key] = (dp, da, ds)
    return res


# ======================================================================================
# Per-set fitting
# ======================================================================================

def side(name):
    for s in ("_L", "_R"):
        if name.endswith(s) or (s + "_") in name or name.endswith(s + "_00") or name.endswith(s + "_01"):
            return s
    return None


@functools.lru_cache(maxsize=None)
def lcs_len(a, b):
    a, b = a.lower(), b.lower()
    best = 0
    prev = [0] * (len(b) + 1)
    for i in range(1, len(a) + 1):
        cur = [0] * (len(b) + 1)
        for j in range(1, len(b) + 1):
            if a[i - 1] == b[j - 1]:
                cur[j] = prev[j - 1] + 1
                best = max(best, cur[j])
        prev = cur
    return best


def preference(dst, ch, srcm, cols):
    """Tie-break score of candidate Src columns for Dst channel ch (indistinguishable columns)."""
    p = {}
    ds = side(dst)
    for j in cols:
        src, c = srcm.cols[j]
        s = 0.0
        ss = side(src)
        if ds and ss:
            s += 10 if ds == ss else -10
        if c // 3 == ch // 3:
            s += 3
        if c % 3 == ch % 3:
            s += 1
        s += 0.05 * lcs_len(dst, src)
        p[j] = s
    return p


class SetFit:
    def __init__(self, name, meas, srcm, desc, tol, log):
        self.name, self.meas, self.srcm, self.desc, self.tol, self.log = name, meas, srcm, desc, tol, log
        self.remaps = {}
        self.notes = []

    # ---------------------------------------------------------------- samples
    def split(self):
        k = np.array(self.meas.kind)
        rnd = np.where(k == "random")[0]
        train = np.concatenate([np.where(np.isin(k, ["rest", "sweep"]))[0], rnd[0::2]])
        val = np.concatenate([rnd[1::2], np.where(k == "pair")[0]])
        if len(val) == 0:
            val = train
            self.notes.append("no random / pair samples: validation uses the training samples")
        other = np.where(~np.isin(k, ["rest", "sweep", "random", "pair"]))[0]
        if len(other):
            train = np.concatenate([train, other])
        self.train, self.val = np.sort(train), np.sort(val)
        self.all = np.arange(self.meas.N)

    def sweep_samples(self, s):
        out = []
        for i, (k, sl) in enumerate(zip(self.meas.kind, self.meas.slider)):
            if k == "sweep":
                try:
                    if int(float(sl)) == s:
                        out.append(i)
                except ValueError:
                    pass
        return np.array(out, int)

    # ---------------------------------------------------------------- measured channels
    def channels(self):
        """Measured Dst channel series: {(key, ch): y (N,)} with Euler unwrapped, plus moving groups."""
        m = self.meas
        self.ser = {}
        self.dst = {}
        src_names = self.srcm.src_names
        self.helpers = []
        for key, d in m.data.items():
            name = m.paths[key]
            if np.isnan(d[:, 0]).any():
                continue
            if name in src_names:
                self.helpers.append(key)
                continue
            q = d[:, 3:7] * np.sign(d[:, 6:7] + 1e-300)
            moving = {
                "pos": [c for c in range(3) if np.ptp(d[:, c]) > 1e-5],
                "rot": [],
                "scl": [c for c in range(3) if np.ptp(d[:, 10 + c]) > 1e-5],
            }
            rot_moves = np.max(np.ptp(q, 0)) > 1e-5
            eul = euler_from_quat(d[:, 3:7])
            if rot_moves:
                for c in range(3):
                    e = unwrap_series(eul[:, c])
                    if np.ptp(e) > 1e-3:
                        moving["rot"].append(c)
                if not moving["rot"]:
                    moving["rot"] = [0]   # quaternion moves below the Euler tolerance: fit anyway
            if not any(moving.values()):
                continue
            self.dst[key] = moving
            for c in range(3):
                self.ser[(key, c)] = d[:, c].astype(np.float64)
                self.ser[(key, 3 + c)] = unwrap_series(eul[:, c])
                self.ser[(key, 6 + c)] = d[:, 10 + c].astype(np.float64)
        # Euler check against what Unity reported
        worst = 0.0
        for key, d in m.data.items():
            if np.isnan(d[:, 0]).any():
                continue
            e = euler_from_quat(d[:, 3:7])
            worst = max(worst, float(np.max(np.abs(wrap180(e - d[:, 7:10])))))
        self.euler_check = worst

    # ---------------------------------------------------------------- Src helper check
    def helper_check(self, X):
        out = []
        for key in self.helpers:
            name = self.meas.paths[key]
            d = self.meas.data[key]
            meas9 = np.hstack([d[:, 0:3], euler_from_quat(d[:, 3:7]), d[:, 10:13]])
            err, nch = 0.0, 0
            for c in range(9):
                j = self.srcm.col_index.get((name, c))
                if j is None:
                    continue
                nch += 1
                e = np.abs(wrap180(meas9[:, c] - X[:, j])) if 3 <= c < 6 else np.abs(meas9[:, c] - X[:, j])
                err = max(err, float(np.max(e)))
            out.append((name, nch, err))
        return out

    # ---------------------------------------------------------------- sliders moving a channel
    def movers(self):
        self.moved_by = collections.defaultdict(set)
        self.slider_effect = {}
        n = self.meas.V.shape[1]
        for s in range(n):
            idx = self.sweep_samples(s)
            self.slider_effect[s] = 0
            if len(idx) < 2:
                continue
            for k, y in self.ser.items():
                tol = 1e-3 if 3 <= k[1] < 6 else 1e-5
                if np.ptp(y[idx]) > tol:
                    self.moved_by[k].add(s)
                    self.slider_effect[s] += 1

    def candidates(self, key, ch):
        s = self.moved_by.get((key, ch))
        if not s:
            s = set(self.srcm.sliders)   # no sweep information: all sliders
        cols = set()
        for x in s:
            cols.update(self.srcm.slider_cols(x))
        return sorted(cols)

    # ---------------------------------------------------------------- fitting
    def slider_classes(self, X):
        """Per slider: groups of affine-equivalent Src columns (on all samples). Members of a group are
        interchangeable in a fit (the measurements cannot tell them apart)."""
        if getattr(self, "_cls_for", None) is X:
            return self._cls
        N = X.shape[0]
        cls = {}
        for s in self.srcm.sliders:
            groups = []
            for j in self.srcm.slider_cols(s):
                x = X[:, j]
                if np.ptp(x) < 1e-9:
                    continue
                for g in groups:
                    A = np.c_[X[:, g[0]], np.ones(N)]
                    if np.max(np.abs(A @ lsq(A, x) - x)) < 1e-5 * max(1.0, float(np.ptp(x))):
                        g.append(j)
                        break
                else:
                    groups.append([j])
            cls[s] = groups
        self._cls_for, self._cls = X, cls
        return cls

    def fit_channel(self, key, ch, X, quick=False):
        """Fit one Dst channel. Forms tried in order: sparse sum of Src channels; product of per-slider
        sparse factors (alternating least squares, then a sparse fit per factor); sparse polynomial over
        cross-slider products. A form is accepted when the engine-side evaluation (raw Src values) stays
        within tol on the training samples and within 3 tol on the validation samples."""
        y = self.ser[(key, ch)]
        rot = 3 <= ch < 6
        tol = self.tol["rot"] if rot else self.tol["pos"] if ch < 3 else self.tol["scl"]
        S = sorted(self.moved_by.get((key, ch)) or self.srcm.sliders)
        cand = [j for j in self.candidates(key, ch) if np.ptp(X[self.train, j]) > 1e-9 and (rot or j not in self.jumpy)]
        name = self.meas.paths[key]
        pref = preference(name, ch, self.srcm, cand)
        Xt, yt = X[self.train], y[self.train]
        m, res = omp_fit(Xt, yt, cand, pref, tol, refine=not quick)
        if quick:
            return m, res, "linear", tol

        def errors(model):
            p = model.predict(self.Xraw)
            r = wrap180(y - p) if rot else y - p
            return float(np.max(np.abs(r[self.train]))), float(np.max(np.abs(r[self.val])))

        def accept(model):
            e_tr, e_va = errors(model)
            return e_tr <= tol * 1.5 and e_va <= tol * 3

        best_res = res
        cls = self.slider_classes(X)
        found = []   # accepted (complexity, model, form)
        if m is not None:
            m = snap(m, Xt, yt, tol)
            m = self.unitize(m, Xt, yt, tol, cls, pref)
            if rot:
                m.const = float(wrap180(m.const))
            if accept(m):
                found.append((complexity(m), m, "linear"))
                if complexity(m)[0] == 0:
                    return m, errors(m)[0], "linear", tol
            best_res = min(best_res, errors(m)[1])

        # sums of Src channels with coefficients +-1, searched slider by slider on the sweeps
        um = self.unit_sum_search(key, ch, X, y, S, pref, tol, rot)
        if um is not None and accept(um):
            found.append((complexity(um), um, "linear"))
            if complexity(um)[0] == 0:
                return um, errors(um)[0], "linear", tol

        # products / quotients of single Src channels, searched slider by slider on the sweeps
        if len(S) >= 1:
            up = self.unit_product_search(X, y, S, pref, tol)
            if up is not None and accept(up):
                found.append((complexity(up), up, "product"))
                if complexity(up)[0] == 0:
                    return up, errors(up)[0], "product", tol

        # products of per-slider (or per slider group) factors
        pref = preference(name, ch, self.srcm, sorted(set(j for s_ in S for j in self.srcm.slider_cols(s_))))
        if len(S) >= 2:
            parts = list(partitions(S)) if len(S) <= 6 else [[[x] for x in S]]
            parts = [p for p in parts if len(p) >= 2]
            parts.sort(key=lambda p: (-len(p), sorted(len(g) for g in p)))
            for part in parts:
                pm, r = self.product_fit(X, y, part, cls, pref, tol)
                best_res = min(best_res, r)
                if pm is not None and accept(pm):
                    found.append((complexity(pm), pm, "product"))
                    if complexity(pm)[0] == 0:
                        break
        if found:
            found.sort(key=lambda t: t[0])
            return found[0][1], errors(found[0][1])[0], found[0][2], tol

        # sparse polynomial over cross-slider products
        reps = {s: [max(g, key=lambda j: pref.get(j, 0)) for g in self.nonjumpy(cls[s])] for s in S}
        singles = [(j,) for s in S for j in reps[s]]
        pairs = [(a, b) for i, s in enumerate(S) for t in S[i + 1:] for a in reps[s] for b in reps[t]]
        triples = []
        if len(S) >= 3:
            n3 = sum(len(reps[s]) * len(reps[t]) * len(reps[u]) for i, s in enumerate(S) for j, t in enumerate(S[i + 1:], i + 1) for u in S[j + 1:])
            if n3 <= 4000:
                triples = [(a, b, c) for i, s in enumerate(S) for j, t in enumerate(S[i + 1:], i + 1) for u in S[j + 1:]
                           for a in reps[s] for b in reps[t] for c in reps[u]]
        if pairs:
            prf = {j: pref.get(j, 0) for s in S for j in reps[s]}
            pm, r = omp_fit(Xt, yt, None, prf, tol, max_terms=60, terms_all=singles + pairs + triples)
            if pm is not None:
                pm = snap(pm, Xt, yt, tol)
                if accept(pm):
                    return pm, errors(pm)[0], "polynomial", tol
                best_res = min(best_res, errors(pm)[1])
        return None, best_res, "none", tol

    def unit_sum_search(self, key, ch, X, y, S, pref, tol, rot, max_k=3, keep=4):
        """y = c + sum of up to max_k Src channels per slider with coefficients +-1. Each slider's part is
        matched on its sweep (the other sliders sit at 0.5, so the sweep isolates that slider's terms)."""
        per = []
        for s in S:
            idx = self.sweep_samples(s)
            if len(idx) < 3:
                return None
            v = self.meas.V[idx, s]
            mid = idx[np.argmin(np.abs(v - 0.5))]
            d = y[idx] - y[mid]
            cols = [j for j in self.srcm.slider_cols(s) if (rot or j not in self.jumpy)]
            D = {j: X[idx, j] - X[mid, j] for j in cols}
            # drop columns without a sweep response and exact duplicates (keep the most preferred)
            uniq = {}
            for j in sorted(cols, key=lambda j: -pref.get(j, 0)):
                if np.max(np.abs(D[j])) < 1e-9:
                    continue
                kk = tuple(np.round(D[j], 7))
                uniq.setdefault(kk, j)
            cols = list(uniq.values())
            sols = []
            if np.max(np.abs(d)) <= tol:
                sols.append(((), ()))
            for k in range(1, max_k + 1):
                if len(sols) >= keep:
                    break
                for comb in itertools.combinations(cols, k):
                    M = np.stack([D[j] for j in comb], 1)
                    for signs in itertools.product((1, -1), repeat=k):
                        if np.max(np.abs(M @ np.array(signs) - d)) <= tol:
                            sols.append((comb, signs))
                    if len(sols) >= keep * 4 or (k == 3 and len(cols) > 40):
                        break
            if not sols:
                return None
            sols.sort(key=lambda t: -sum(pref.get(j, 0) for j in t[0]) / max(1, len(t[0])) + 0.01 * len(t[0]))
            per.append(sols[:keep])
        Xt, yt = X[self.train], y[self.train]
        best = None
        for combo in itertools.islice(itertools.product(*per), 256):
            terms = [(j,) for comb, _ in combo for j in comb]
            coefs = [float(sg) for _, signs in combo for sg in signs]
            if len(set(terms)) < len(terms):
                continue
            base = sum(c * Xt[:, t[0]] for t, c in zip(terms, coefs)) if terms else np.zeros(len(yt))
            c0 = float(np.mean(yt - base))
            r = float(np.max(np.abs(yt - base - c0)))
            if r <= tol and (best is None or r < best[0]):
                best = (r, terms, coefs, c0)
                break
        if best is None:
            return None
        _, terms, coefs, c0 = best
        m = Model(terms, coefs, c0)
        k0 = c0
        resid = yt - (m.predict(Xt) - c0)
        m.const = round_const(k0, lambda v: np.max(np.abs(resid - v)) <= tol)
        if rot:
            m.const = float(wrap180(m.const))
        return m

    def approximate(self, key, ch, X):
        """Best small linear approximation of a channel no exact form explains (for the report and the
        separate *.approx.txt map; never part of the fitted map)."""
        y = self.ser[(key, ch)]
        rot = 3 <= ch < 6
        S = sorted(self.moved_by.get((key, ch)) or self.srcm.sliders)
        cols = sorted(set(j for s in S for j in self.srcm.slider_cols(s) if np.ptp(X[self.train, j]) > 1e-9 and j not in self.jumpy))
        Xt, yt = X[self.train], y[self.train]
        _, r = fit_coef(Xt, yt, [(j,) for j in cols])
        e0 = float(np.max(np.abs(r)))
        pref = preference(self.meas.paths[key], ch, self.srcm, cols)
        m, _ = omp_fit(Xt, yt, cols, pref, e0 * 1.5 + 1e-12, max_terms=40)
        if m is None:
            return None
        m = snap(m, Xt, yt, e0 * 1.6 + 1e-12)
        p = m.predict(self.Xraw)
        rr = wrap180(y - p) if rot else y - p
        return m, float(np.max(np.abs(rr[self.train]))), float(np.max(np.abs(rr[self.val])))

    def unit_product_search(self, X, y, S, pref, tol, keep=4):
        """y = k * prod over the sliders of one Src channel each, multiplied or divided. The sweep ratio
        y(v) / y(0.5) of each slider must equal x(v) / x(0.5) (or its inverse) for the chosen channel."""
        per = []
        scale = float(np.max(np.abs(y))) or 1.0
        for s in S:
            idx = self.sweep_samples(s)
            if len(idx) < 3:
                return None
            v = self.meas.V[idx, s]
            mid = idx[np.argmin(np.abs(v - 0.5))]
            if abs(y[mid]) < 1e-9:
                return None
            r = y[idx] / y[mid]
            sols = []
            for j in sorted(self.srcm.slider_cols(s), key=lambda j: -pref.get(j, 0)):
                if j in self.jumpy:
                    continue
                x = X[idx, j]
                if np.min(np.abs(x)) < 1e-9 or np.ptp(x) < 1e-9:
                    continue
                q = x / X[mid, j]
                for e in (1, -1):
                    if np.max(np.abs((q if e > 0 else 1 / q) - r)) * scale <= tol:
                        sols.append((j, e))
                if len(sols) >= keep:
                    break
            if not sols:
                return None
            per.append(sols)
        Xt, yt = X[self.train], y[self.train]
        for combo in itertools.islice(itertools.product(*per), 64):
            p = np.ones(len(yt))
            for j, e in combo:
                p = p * Xt[:, j] if e > 0 else p / Xt[:, j]
            k = float(p @ yt / (p @ p))
            if np.max(np.abs(k * p - yt)) > tol:
                continue
            pm = ProdModel(k, [Model([(j,)], [1.0], 0.0) for j, _ in combo], [e for _, e in combo])
            k2 = nice(k, 1e-6)
            if k2 is None:
                k2 = round_const(k, lambda v: np.max(np.abs(v * p - yt)) <= tol)
            pm.k = k2
            return pm
        return None

    def nonjumpy(self, groups):
        out = [[j for j in g if j not in self.jumpy] for g in groups]
        return [g for g in out if g]

    def unitize(self, m, Xt, yt, tol, cls, pref):
        """Replace a term with a non-unit coefficient by an indistinguishable Src channel (same class) whose
        coefficient is +-1, if there is one (the readable form; the engine output is the same)."""
        member = {}
        for s, groups in cls.items():
            for g in groups:
                for j in g:
                    member[j] = g
        terms, coefs = list(m.terms), list(m.coefs)
        changed = False
        for i, (t, c) in enumerate(zip(terms, coefs)):
            if len(t) != 1 or abs(abs(c) - 1) < 1e-9 or t[0] not in member:
                continue
            for u in sorted(member[t[0]], key=lambda j: -pref.get(j, 0)):
                if u == t[0]:
                    continue
                trial = terms[:i] + [(u,)] + terms[i + 1:]
                cc, rr = fit_coef(Xt, yt, trial)
                if abs(abs(cc[i]) - 1) < 1e-3 and np.max(np.abs(rr)) <= tol:
                    terms = trial
                    changed = True
                    break
        if not changed:
            return m
        cc, _ = fit_coef(Xt, yt, terms)
        return snap(Model(terms, cc[:-1], cc[-1]), Xt, yt, tol)

    def product_fit(self, X, y, part, cls, pref, tol):
        """y = k * prod over the groups of `part` (lists of sliders) of a sparse sum of the group's Src channels."""
        tr = self.train
        Xt, yt = X[tr], y[tr]
        groups = [[max(g, key=lambda j: pref.get(j, 0)) for s in grp for g in self.nonjumpy(cls[s])] for grp in part]
        F, res = als_fit(Xt, yt, groups, tol)
        if F is None:
            return None, res
        factors = []
        for i, grp in enumerate(part):
            other = np.prod(np.delete(F, i, axis=1), axis=1)
            if np.min(np.abs(other)) < 1e-6:
                return None, res
            t = yt / other
            cols = sorted(set(j for s in grp for j in self.srcm.slider_cols(s) if np.ptp(Xt[:, j]) > 1e-9 and j not in self.jumpy))
            ftol = tol / float(np.max(np.abs(other)))
            mi = None
            if len(grp) == 1:
              # a single Src channel times a constant (the constant goes into k), most preferred first
              for j in sorted(cols, key=lambda j: -pref.get(j, 0)):
                x = Xt[:, j]
                if np.min(np.abs(x)) < 1e-9:
                    continue
                kj = float(t @ x / (x @ x))
                if np.max(np.abs(kj * x - t)) <= ftol:
                    mi = Model([(j,)], [kj], 0.0)
                    break
            if mi is None:
                mi, _ = omp_fit(Xt, t, cols, pref, ftol)
                if mi is not None:
                    mi = self.unitize(mi, Xt, t, ftol, cls, pref)
            if mi is None:
                return None, res
            F[:, i] = mi.predict(Xt)
            factors.append(mi)
        k = 1.0
        for f in factors:
            if not f.terms:
                k *= f.const
                f.const = 1.0
                continue
            lead = max(range(len(f.terms)), key=lambda i: sum(pref.get(j, 0) for j in f.terms[i]))
            c = f.coefs[lead]
            f.coefs = [x / c for x in f.coefs]
            f.const = f.const / c
            k *= c
        pm = ProdModel(k, factors)

        def refit_k():
            pm.k = 1.0
            p = pm.predict(Xt)
            pm.k = float(p @ yt / (p @ p))
            return float(np.max(np.abs(pm.k * p - yt)))

        base = refit_k()

        def ok():
            return refit_k() <= max(tol, base * 1.05)

        for f in factors:
            lin_snap_params(f, ok)
        refit_k()
        k0 = pm.k
        for v in [nice(k0, 1e-6)] + [round(k0, d) for d in range(0, 8)]:
            if v is None:
                continue
            pm.k = v
            if float(np.max(np.abs(pm.predict(Xt) - yt))) <= max(tol, base * 1.05):
                break
        else:
            pm.k = k0
        return pm, float(np.max(np.abs(pm.predict(Xt) - yt)))

    def fit_all(self, X):
        fits = {}
        for (key, ch) in self.ser:
            g = "pos" if ch < 3 else "rot" if ch < 6 else "scl"
            if (ch % 3) not in self.dst[key][g] and g == "pos":
                continue
            if not self.dst[key][g]:
                continue
            if (ch % 3) not in self.dst[key][g]:
                # non-moving component of a Rotation / Scale row: the measured constant
                y = self.ser[(key, ch)]
                k = float(np.mean(y))
                k = round_const(k, lambda v: np.max(np.abs(y - v)) <= 1e-6) if g != "rot" else \
                    round_const(float(wrap180(k)), lambda v: np.max(np.abs(wrap180(y - v))) <= 1e-4)
                fits[(key, ch)] = (Model([], [], k), float(np.max(np.abs(y - k))), "constant", 0)
                continue
            fits[(key, ch)] = self.fit_channel(key, ch, X)
            if fits[(key, ch)][0] is None:
                self.approx[(key, ch)] = self.approximate(key, ch, X)
        return fits

    def fit_x(self, remaps):
        """Src columns for fitting: float64, rotation columns unwrapped around their circular mean (the
        engine output is periodic in each Euler component, the per-column 360 shifts go into the constant)."""
        X = self.srcm.compute(self.meas.V, remaps).astype(np.float64)
        self.jumpy = set()
        for j, (src, c) in enumerate(self.srcm.cols):
            if 3 <= c < 6:
                u = unwrap_series(X[:, j])
                if np.ptp(X[:, j] - u) > 1e-6:
                    # the raw LerpAngle values jump by 360 inside the slider range (keys stored in 0..360):
                    # only usable with coefficient +-1 inside a rotation channel
                    X[:, j] = u
                    self.jumpy.add(j)
        return X

    def slider_channels(self, s, cap=40):
        """Channels that only slider s moves (a remap of s shows there without cross-slider products)."""
        ch = [k for k in self.ser if self.moved_by.get(k) == {s} and (k[1] % 3) in self.dst[k[0]][("pos", "rot", "scl")[k[1] // 3]]]
        return sorted(ch)[:cap]

    def score_slider(self, s, X):
        """Quick fit quality of the channels slider s moves: failures + 0.001 * terms (lower is better)."""
        score = 0.0
        for k in self.slider_channels(s):
            m, res, how, tol = self.fit_channel(k[0], k[1], X, quick=True)
            # non-unit coefficients cost more than an extra term: a remap that makes them 1 is preferred
            score += 1 if m is None else 0.001 * m.n_terms + 0.01 * sum(abs(abs(c) - 1) > 1e-3 for c in m.coefs)
        return score

    def search_remaps(self):
        """Per slider: identity unless another remap explains the slider's channels better. Alternatives are
        tried when at least half of the slider's channels fail with the identity (and always for body slider 32,
        which the assembler remaps as Lerp(0, 0.8, v))."""
        X = self.fit_x({})
        for s in self.srcm.sliders:
            if s >= self.meas.V.shape[1] or not self.slider_effect.get(s):
                continue
            chans = self.slider_channels(s)
            if not chans:
                continue
            sc_id = self.score_slider(s, X)
            tried = [(sc_id, REMAP_ID)]
            special = self.name.endswith("_body") and s == 32
            if sc_id >= 0.5 * len(chans) or special:
                for h in (Remap(0, 0.8),):
                    tried.append((self.score_slider(s, self.fit_x({**self.remaps, s: h})), h))
            if min(t[0] for t in tried) >= 0.5 * len(chans):
                for a in np.arange(-0.2, 0.51, 0.05):
                    for b in np.arange(0.2, 1.21, 0.05):
                        h = Remap(round(float(a), 4), round(float(b), 4))
                        tried.append((self.score_slider(s, self.fit_x({**self.remaps, s: h})), h))
            seen, uniq = set(), []
            for t in sorted(tried, key=lambda t: (t[0], not t[1].is_id)):
                if t[1].key() not in seen:
                    seen.add(t[1].key())
                    uniq.append(t)
            tried = uniq
            best = tried[0][1]
            if not best.is_id:
                self.remaps[s] = best
            if len(tried) > 1:
                self.remap_trials[s] = [(float(sc), str(h)) for sc, h in tried[:6]] + [(-1, f"of {len(chans)} channels")]
            self.log(f"  slider {s}: {len(chans)} channels, identity failures {sc_id:.0f}, chosen {best}", flush=True) if not best.is_id or special else None

    def run(self):
        t0 = time.time()
        self.split()
        self.channels()
        self.movers()
        self.remap_trials = {}
        self.search_remaps()
        X = self.srcm.compute(self.meas.V, self.remaps)
        self.X = X
        self.helper_result = self.helper_check(X)
        self.Xraw = X.astype(np.float64)
        self.approx = {}
        self.fits = self.fit_all(self.fit_x(self.remaps))
        self.build_rows()
        self.validate()
        self.secs = time.time() - t0

    # ---------------------------------------------------------------- rows
    def build_rows(self):
        names = [self.srcm.col_name(j) for j in range(len(self.srcm.cols))]
        self.rows = []
        self.unexplained = []
        self.approx_rows = []
        for key, mv in self.dst.items():
            dst = self.meas.paths[key]
            if self.meas.by_name.get(dst) != key:
                self.unexplained.append((dst, "all", f"duplicate name, not the first match ({key})"))
                continue
            for c in mv["pos"]:
                f = self.fits.get((key, c))
                if f is None or f[0] is None:
                    self.unexplained.append((dst, "Position" + "XYZ"[c], f"max residual {f[1]:.3g}" if f else "?"))
                    ap = (self.approx.get((key, c)) or (None,))[0]
                    if ap is not None:
                        self.approx_rows.append((dst, "Position" + "XYZ"[c], [expr_of(ap, names)], key, [c]))
            pos_ok = [c for c in mv["pos"] if self.fits.get((key, c)) and self.fits[(key, c)][0] is not None]
            if len(pos_ok) == 3:
                self.rows.append((dst, "Position", [expr_of(self.fits[(key, c)][0], names) for c in range(3)], key, [0, 1, 2]))
            else:
                for c in pos_ok:
                    self.rows.append((dst, "Position" + "XYZ"[c], [expr_of(self.fits[(key, c)][0], names)], key, [c]))
            for g, op, base in (("rot", "Rotation", 3), ("scl", "Scale", 6)):
                if not mv[g]:
                    continue
                fs = [self.fits.get((key, base + c)) for c in range(3)]
                bad = [c for c in range(3) if fs[c] is None or fs[c][0] is None]
                if bad:
                    self.unexplained.append((dst, op, ", ".join(f"{'xyz'[c]}: max residual {fs[c][1]:.3g}" for c in bad)))
                    ap = [fs[c][0] if c not in bad else (self.approx.get((key, base + c)) or (None,))[0] for c in range(3)]
                    if all(m is not None for m in ap):
                        self.approx_rows.append((dst, op, [expr_of(m, names) for m in ap], key, [base, base + 1, base + 2]))
                    continue
                self.rows.append((dst, op, [expr_of(fs[c][0], names) for c in range(3)], key, [base, base + 1, base + 2]))
        order = {"Position": 0, "PositionX": 1, "PositionY": 2, "PositionZ": 3, "Rotation": 4, "Scale": 5}
        self.rows.sort(key=lambda r: (r[0], order[r[1]]))

    def map_text(self, approx=False):
        order = {"Position": 0, "PositionX": 1, "PositionY": 2, "PositionZ": 3, "Rotation": 4, "Scale": 5}
        rows = sorted(self.rows + (self.approx_rows if approx else []), key=lambda r: (r[0], order[r[1]]))
        return "".join(f"always\t{d}\t{op}\t{','.join(a)}\n" for d, op, a, _, _ in rows)

    def validate(self):
        rows = parse_map(self.map_text())
        pred = eval_map(rows, self.srcm, self.X, self.meas)
        self.err_val = compare(pred, self.meas, self.val)
        self.err_all = compare(pred, self.meas, self.all)


# ======================================================================================
# Cross-set checks
# ======================================================================================

def cross_eval(map_text, sf, extra_remaps=None):
    """Evaluate a map on another set's measurements; for sliders without measurable effect, search a
    constant value (the game may ignore the slider and use a fixed value)."""
    rows = parse_map(map_text)
    remaps = dict(sf.remaps)
    found = {}
    if extra_remaps:
        remaps.update(extra_remaps)
    dead = [s for s in sf.srcm.sliders if s < sf.meas.V.shape[1] and not sf.slider_effect.get(s)
            and sf.srcm.slider_cols(s)]

    def total(rm):
        X = sf.srcm.compute(sf.meas.V, rm)
        err = compare(eval_map(rows, sf.srcm, X, sf.meas), sf.meas, sf.all)
        return sum(min(e[0] / 1e-5, 1e4) + min(e[1] / 1e-2, 1e4) + min(e[2] / 1e-5, 1e4) for e in err.values()), err

    base, err = total(remaps)
    for s in dead:
        best = (base, None)
        for c in np.round(np.arange(0, 1.0001, 0.05), 4):
            sc, _ = total({**remaps, s: Remap(const=float(c))})
            if sc < best[0] - 1e-9:
                best = (sc, float(c))
        if best[1] is not None:
            c0 = best[1]
            for c in np.round(np.arange(max(0, c0 - 0.05), min(1, c0 + 0.05) + 1e-9, 0.0025), 5):
                sc, _ = total({**remaps, s: Remap(const=float(c))})
                if sc < best[0] - 1e-9:
                    best = (sc, float(c))
            remaps[s] = Remap(const=best[1])
            found[s] = best[1]
            base = best[0]
    _, err = total(remaps)
    return err, found


# ======================================================================================
# Report
# ======================================================================================

def stats(err):
    if not err:
        return "n/a"
    p = [e[0] for e in err.values()]
    a = [e[1] for e in err.values()]
    s = [e[2] for e in err.values()]
    return (f"position max {max(p):.3g} mean {np.mean(p):.3g}; angle max {max(a):.3g} deg mean {np.mean(a):.3g}; "
            f"scale max {max(s):.3g} mean {np.mean(s):.3g}")


def worst(err, meas, n=5):
    items = sorted(err.items(), key=lambda kv: -max(kv[1][0] / 1e-4, kv[1][1] / 0.05, kv[1][2] / 1e-4))[:n]
    return ", ".join(f"{meas.paths[k]} (p {e[0]:.2g}, a {e[1]:.2g}, s {e[2]:.2g})" for k, e in items)


def set_report(sf):
    L = []
    m = sf.meas
    L.append(f"## {sf.name}\n")
    L.append(f"- data: {sf.desc}; {len(sf.srcm.sliders)} sliders, {len(sf.srcm.cols)} written Src channels")
    L.append(f"- samples: {m.N} ({', '.join(f'{k} {v}' for k, v in sorted(m.kinds().items()))}); "
             f"train {len(sf.train)}, validation {len(sf.val)}; bones dumped {len(m.data)}")
    if m.missing:
        L.append(f"- bones missing in some samples (ignored): {len(m.missing)}")
    if m.dup_names:
        L.append(f"- duplicate bone names in the dump: {', '.join(list(m.dup_names)[:10])}")
    L.append(f"- Euler recovery check (own ZXY decomposition vs Unity's localEulerAngles): max {sf.euler_check:.3g} deg")
    if sf.helper_result:
        mx = max(e for _, _, e in sf.helper_result)
        ok = sum(1 for _, n, e in sf.helper_result if n and e < 1e-4)
        L.append(f"- Src helpers in the dump: {len(sf.helper_result)} transforms named like Src bones; "
                 f"{ok} match the computed Src channels within 1e-4 (max error {mx:.3g})")
        bad = [(n, e) for n, c, e in sf.helper_result if c and e >= 1e-4]
        if bad:
            L.append("  - not matching: " + ", ".join(f"{n} ({e:.3g})" for n, e in bad[:15]))
    else:
        L.append("- Src helpers in the dump: none (no transform carries a Src bone name)")
    dead = [s for s in sf.srcm.sliders if s < m.V.shape[1] and not sf.slider_effect.get(s)]
    L.append(f"- sliders without measurable effect in the sweeps: {dead if dead else 'none'}")
    L.append("- remaps (slider value before SetValue): "
             + (", ".join(f"slider {s}: {r}" for s, r in sorted(sf.remaps.items())) or "none (identity everywhere)"))
    for s, tr in sorted(sf.remap_trials.items()):
        L.append(f"  - slider {s} trials (failed channels + 0.001 per term; lower is better): "
                 + "; ".join(f"{h} {sc:.3g}" if sc >= 0 else h for sc, h in tr))
        ident = [sc for sc, h in tr if h == "identity"]
        if s in sf.remaps and ident and ident[0] < 1:
            L.append(f"  - note: the identity also explains every channel of slider {s}, only with coefficients other than +-1 "
                     f"(the keys are linear over the range): the remap is not strictly identifiable from the measurements; "
                     f"{sf.remaps[s]} is the form under which all of them become plain sums / products (unit coefficients)")
    terms = [f[0] for f in sf.fits.values() if f[0] is not None]
    nterm = sum(t.n_terms for t in terms)
    odd = [(k, f) for k, f in sf.fits.items() if f[0] is not None and (isinstance(f[0], ProdModel) or any(abs(abs(c) - 1) > 1e-9 for c in f[0].coefs)
                                                                      or any(len(t) > 1 for t in f[0].terms))]
    hows = collections.Counter(f[2] for f in sf.fits.values() if f[0] is not None)
    L.append(f"- Dst bones {len(sf.dst)}; rows {len(sf.rows)} "
             f"({', '.join(f'{k} {v}' for k, v in sorted(collections.Counter(r[1] for r in sf.rows).items()))}); "
             f"channel fits {len(terms)} with {nterm} terms ({', '.join(f'{k} {v}' for k, v in sorted(hows.items()))})")
    L.append(f"- channels with coefficients other than +-1, products or multiplicative form: {len(odd)}")
    for (k, ch), f in odd[:25]:
        L.append(f"  - {m.paths[k]} {CH[ch]}: {expr_of(f[0], [sf.srcm.col_name(j) for j in range(len(sf.srcm.cols))])}")
    tr = [f[1] for f in sf.fits.values() if f[0] is not None and f[2] != "constant"]
    if tr:
        L.append(f"- training max residual per channel (fit space, rotations in Euler degrees): max {max(tr):.3g}, mean {np.mean(tr):.3g}")
    L.append(f"- validation ({len(sf.val)} samples, map text re-parsed and evaluated, rotations as quaternion angle): {stats(sf.err_val)}")
    L.append(f"- all samples: {stats(sf.err_all)}")
    L.append(f"- worst bones (all samples): {worst(sf.err_all, m)}")
    if sf.unexplained:
        L.append(f"- unexplained (left out of the map): {len(sf.unexplained)}")
        for d, op, why in sf.unexplained[:40]:
            L.append(f"  - {d} {op}: {why}")
        names = [sf.srcm.col_name(j) for j in range(len(sf.srcm.cols))]
        L.append("- best small linear approximations of the unexplained channels (only in the *.approx.txt map):")
        for (k, ch), ap in sorted(sf.approx.items(), key=lambda kv: (sf.meas.paths[kv[0][0]], kv[0][1])):
            if ap is None:
                L.append(f"  - {sf.meas.paths[k]} {CH[ch]}: none")
                continue
            L.append(f"  - {sf.meas.paths[k]} {CH[ch]}: `{expr_of(ap[0], names)}` (max error train {ap[1]:.3g}, validation {ap[2]:.3g}; "
                     f"channel range {np.ptp(sf.ser[(k, ch)]):.3g})")
    else:
        L.append("- unexplained: none")
    for n in sf.notes:
        L.append(f"- note: {n}")
    L.append(f"- fit time {sf.secs:.1f} s")
    return "\n".join(L) + "\n"


# ======================================================================================

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default=os.path.join(REPO, "Cache", "shapemeasure"))
    ap.add_argument("--out", default=os.path.join(REPO, "Tools", "survey", "shape"))
    ap.add_argument("--sets", default=",".join(SETS))
    ap.add_argument("--game-root")
    ap.add_argument("--card-female")
    ap.add_argument("--card-male")
    ap.add_argument("--tol-pos", type=float, default=5e-6)
    ap.add_argument("--tol-scl", type=float, default=5e-6)
    ap.add_argument("--tol-rot", type=float, default=5e-4, help="degrees (Euler components)")
    a = ap.parse_args()

    root = a.game_root or game_root_from_settings()
    gd = GameData(root)
    cf, cm = default_cards(root)
    cards = {"female": a.card_female or cf, "male": a.card_male or cm}
    tol = {"pos": a.tol_pos, "scl": a.tol_scl, "rot": a.tol_rot}
    os.makedirs(a.out, exist_ok=True)
    fits = {}
    def log(*x, **k):
        print(*x, flush=True)
    for name in [s.strip() for s in a.sets.split(",") if s.strip()]:
        meas = Measured(a.data, name)
        if not meas.ok:
            log(f"{name}: no measurement files, skipped")
            continue
        keys, info, desc = load_shape_data(gd, name, cards[name.split("_")[0]])
        srcm = SrcModel(keys, info)
        sf = SetFit(name, meas, srcm, desc, tol, log)
        log(f"{name}: {meas.N} samples, {len(meas.data)} bones; fitting")
        sf.run()
        log(f"{name}: {len(sf.rows)} rows, {len(sf.unexplained)} unexplained, validation {stats(sf.err_val)}")
        fits[name] = sf

    out_files = {}
    rep = ["# Shape map fit report\n",
           "Generated by Tools/shape_fit.py from the measured bone dumps in Cache/shapemeasure and the game's shape "
           "key data / slider lists (no existing map file was read).\n",
           f"Tolerances (training fit): position {a.tol_pos:g}, scale {a.tol_scl:g}, Euler {a.tol_rot:g} deg.\n"]
    readme = os.path.join(a.data, "README.md")
    if os.path.exists(readme):
        rep.append(f"Measurement README: Cache/shapemeasure/README.md ({os.path.getsize(readme)} bytes).\n")
    for name, sf in fits.items():
        rep.append(set_report(sf))

    def write(fname, text):
        with open(os.path.join(a.out, fname), "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        out_files[fname] = text.count("\n")

    if "female_body" in fits:
        write("shape_body_map.fitted.txt", fits["female_body"].map_text())
    elif "male_body" in fits:
        write("shape_body_map.fitted.txt", fits["male_body"].map_text())
    if "female_face" in fits:
        write("shape_face_map.fitted.txt", fits["female_face"].map_text())
    for name, fname in (("female_body", "shape_body_map.approx.txt"), ("female_face", "shape_face_map.approx.txt")):
        if name in fits and fits[name].approx_rows:
            write(fname, fits[name].map_text(approx=True))

    rep.append("## Cross-set checks\n")
    for fem, mal, label in (("female_body", "male_body", "body"), ("female_face", "male_face", "face")):
        if fem in fits and mal in fits:
            err, found = cross_eval(fits[fem].map_text(), fits[mal])
            rep.append(f"- female {label} map on the male {label} data: {stats(err)}"
                       + (f"; constant slider values found for sliders without effect: {found}" if found else ""))
            rep.append(f"  - worst: {worst(err, fits[mal].meas)}")
            # same as on the female data (rows left unexplained there fail the same way on both)
            own = fits[fem].err_all

            def ref(k):
                return own.get(fits[fem].meas.by_name.get(fits[mal].meas.paths[k]), (0, 0, 0))
            same = all(e[0] <= max(5e-6, ref(k)[0] * 1.5) and e[1] <= max(5e-4, ref(k)[1] * 1.5)
                       and e[2] <= max(5e-6, ref(k)[2] * 1.5) for k, e in err.items()) and                 set(r[0] for r in fits[fem].rows) >= set(r[0] for r in fits[mal].rows)
            if label == "face":
                if same:
                    rep.append("  - the male face follows the female face map: no separate male map written")
                else:
                    write("shape_face_map_male.fitted.txt", fits[mal].map_text())
                    a_rows = set((r[0], r[1]) for r in fits[fem].rows)
                    b_rows = set((r[0], r[1]) for r in fits[mal].rows)
                    rep.append(f"  - the male face differs: shape_face_map_male.fitted.txt written "
                               f"({len(b_rows - a_rows)} rows only in male, {len(a_rows - b_rows)} only in female)")
            else:
                rep.append("  - " + ("the female body map explains the male body data as well as the female data (same residuals)" if same else
                                     "the female body map does NOT fully explain the male body data (see worst bones)"))
            if found:
                for s, c in found.items():
                    fits[mal].remaps.setdefault(s, Remap(const=c))
        elif fem in fits or mal in fits:
            rep.append(f"- {label}: only one sex measured, no cross check")
    rem = []
    for name, sf in fits.items():
        for s, r in sorted(sf.remaps.items()):
            rem.append(f"{name}\t{s}\t{r}")
    write("shape_remaps.fitted.txt", "# set\tslider\tremap applied to the card value before SetValue\n" + "\n".join(rem) + ("\n" if rem else ""))
    rep.append("\n## Files\n")
    for f, n in out_files.items():
        rep.append(f"- Tools/survey/shape/{f}: {n} lines")
    with open(os.path.join(a.out, "fit_report.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(rep) + "\n")
    log("written: " + ", ".join(out_files) + ", fit_report.md")


if __name__ == "__main__":
    main()
