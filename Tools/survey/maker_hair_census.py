# Maker hair census (Phase 4.3): data facts the hair pages need, read-only.
#   1. hair lists: vanilla rows (gate open as C9, Possess 99 hidden) and zipmod rows (one zipmod per GUID, the catalog's
#      de-duplication) of the hair categories 300 / 301 / 302 / 303 (back, front, side, extension), the colour presets 305
#      and the mesh patterns 306: counts, "none" rows (MainData p_dummy), the columns the hair unit reads (Kind, Weights,
#      MainData02, RingOff, TexAB / TexD / TexC), rows without a thumbnail, the default rows of both sexes;
#   2. hair colour presets (305): Base / Top / Under / Specular H, S, V as RGB (the colours a swatch writes), Metallic,
#      Smoothness, the swatch (Sample H / S / V);
#   3. mesh patterns (306): every row (MainAB / MainTex, ThumbAB / ThumbTex) and the Texture2D header of its MainTex (size,
#      format, mips, wrap U / V) -- headers only;
#   4. the hair shader materials (chara/hair_shader_mat.unity3d): their shaders and the mesh / ring properties;
#   5. hair prefabs (the CmpHair component of every listed row's MainData, and MainData02 for the male variants), read
#      with UnityPy type trees: the flags that decide which hair rows HS2's Maker shows (useTopColor, useUnderColor,
#      useSameSkinColorButton, useMesh, useAcsColor01..03), rendHair / rendAccessory counts and material slots, acsDefColor,
#      the bundles (boneInfo: count, trfCorrect present, limits, default rates, dynamic bones per bundle and where the bundle
#      correction transform sits relative to each chain root), the rendHair materials' shaders and mesh properties
#      (_Mesh_onoff, _MeshColorMask, _MeshColorMask_B);
#   6. the user's cards (UserData/chara/female, male): the hair block (items, shader type, the Maker switches, colours,
#      decoration colours, mesh patterns, bundles against the prefab's bundle count, out-of-range values, bundleId), colours
#      equal to a 305 preset or to the auto-colour rule, and the hair-related plugin blocks (MaterialEditor items of
#      ObjectType Hair, Additional Accessory Controls hair rules and AutoMatchBackHairColor, ABMX modifiers on hair bones,
#      accessories on hair-type rows);
#   7. Sideloader loose PNGs (GB10's rule) referenced by the hair rows: (TexAB, TexD / TexC)
#      of 300..303, (MainAB, MainTex) of 306, (ThumbAB, ThumbTex) of all.
#   8. --thumbs (as the face / body censuses' UF8 / UB8): the thumbnails of the hair lists whose Texture2D header has
#      m_MipCount < 1; --record-guard appends them to <cache>/texguard/bad_textures.tsv (then run Tools/texguard_repair.py).
# No image is decoded (list TextAssets, MonoBehaviour / Transform / Renderer / Material type trees, Texture2D headers,
# card data blocks only). Characters appear as data only.
#
#   python -I Tools/survey/maker_hair_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv> --out <dir>
#          [--no-prefabs | --prefab-limit N] [--thumbs [--record-guard]]
# (72 s on the development machine with --thumbs; --record-guard writes Cache/texguard records: run it only where the
#  spec asks for it, before the play-mode rows scroll the hair lists)
# Writes <out>/report.txt; the committed copy is Tools/survey/maker_hair_census.txt.
import sys, os, re, io, math, zipfile, struct, argparse, collections, colorsys

import UnityPy, msgpack

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
from maker_core_census import vanilla_lists            # noqa: E402
from loose_png_census import read_cache                 # noqa: E402
from hs2card import load_card, resolver_info             # noqa: E402

VANILLA = 'illusion.hs2'
HAIR_CATS = (300, 301, 302, 303)
PART_NAMES = ('back', 'front', 'side', 'option')
PART_PROPS = ('ChaFileHair.HairBack', 'ChaFileHair.HairFront', 'ChaFileHair.HairSide', 'ChaFileHair.HairOption')
MESH_PROPS = tuple(p + 'Mesh' for p in PART_PROPS)
DEFAULT_IDS = {1: (0, 1, 0, 0), 0: (0, 2, 0, 0)}   # sex 1 female, 0 male (ChangeHairAsync's table)
# ChaFileHair.PartsInfo.MemberInit
DOC = {'baseColor': (0.2, 0.2, 0.2, 1), 'topColor': (0.039, 0.039, 0.039, 1), 'underColor': (0.565, 0.565, 0.565, 1),
       'specular': (0.3, 0.3, 0.3, 1), 'metallic': 0.0, 'smoothness': 0.0, 'meshColor': (1, 1, 1, 1),
       'meshLayout': (1, 1, 0, 0)}
WRAP = {0: 'Repeat', 1: 'Clamp', 2: 'Mirror', 3: 'MirrorOnce', -1: '?'}
ME_KEY = 'com.deathweasel.bepinex.materialeditor'
AAC_KEY = 'orange.spork.additionalaccessorycontrolsplugin'
ABMX_KEY = 'KKABMPlugin.ABMData'
ME_LISTS = ('RendererPropertyList', 'MaterialFloatPropertyList', 'MaterialColorPropertyList', 'MaterialTexturePropertyList',
            'MaterialShaderList', 'MaterialCopyList', 'MaterialKeywordPropertyList', 'MaterialVectorPropertyList')
AAC_HAIR_MOD = {'30': 'HAIR_FRONT', '31': 'HAIR_BACK', '32': 'HAIR_SIDE', '33': 'HAIR_EXT', '34': 'HAIR_ACC'}


def utf8():
    try: sys.stdout.reconfigure(encoding='utf-8')
    except Exception: pass


def f(v):
    try: return float(v)
    except Exception: return float('nan')


def hsv(h, s, v):
    """Unity Color.HSVToRGB for h, s, v in 0..1 (colorsys is the same formula; h = 1 wraps to 0)."""
    h = f(h); h = 0.0 if h >= 1.0 else h
    r, g, b = colorsys.hsv_to_rgb(h, f(s), f(v))
    return (r, g, b)


def rgb_to_hsv(r, g, b):
    return colorsys.rgb_to_hsv(r, g, b)


def r4(t): return tuple(round(x, 4) for x in t)


def col(v):
    """A card colour ([r, g, b, a] or {r, g, b, a}) as a 4-tuple of floats."""
    if isinstance(v, dict): return (f(v.get('r')), f(v.get('g')), f(v.get('b')), f(v.get('a', 1)))
    if isinstance(v, (list, tuple)): return tuple(f(x) for x in v) + (1.0,) * (4 - len(v))
    return None


def vec(v, n):
    if isinstance(v, dict): return tuple(f(v.get(k)) for k in 'xyzw'[:n])
    if isinstance(v, (list, tuple)): return tuple(f(x) for x in v[:n])
    return None


def near(a, b, eps=1e-4):
    if a is None or b is None: return False
    return all(abs(x - y) <= eps for x, y in zip(a, b))


def empty(v): return v is None or v == '' or v == '0'


def inv_lerp(a, b, v):
    """Unity Mathf.InverseLerp (clamped; a == b -> 0)."""
    if a == b: return 0.0
    return min(1.0, max(0.0, (v - a) / (b - a)))


# ---------------------------------------------------------------- rows
def hair_rows(a):
    """Vanilla rows {(cat, id): {col: val}} (first definition, gate open) and the mod rows of the loaded zipmods (one per GUID,
    the last file of the scan; inside a zipmod the first row of a key) {(cat, guid, id): (zip path, {col: val})}, plus the
    zipmods (path -> files) and the bundle -> first shipping zipmod map."""
    van = {}
    for fn, asset, cat, dist, keys, rows in vanilla_lists(a.hs2):
        for rid, vals in rows.items():
            try: rid = int(rid)
            except Exception: continue
            if (cat, rid) not in van: van[(cat, rid)] = {keys[i]: str(vals[i]) for i in range(min(len(keys), len(vals)))}
    zs = read_cache(a.modcache)
    by_guid = {}
    for z in zs:
        if z['guid']: by_guid[z['guid'].lower()] = z
    loaded = sorted(by_guid.values(), key=lambda z: z['guid'].lower())
    mod = {}
    for z in loaded:
        if z['guid'].lower() == VANILLA: continue
        seen = set()
        for c, i, kv in z['rows']:
            k = (c, z['guid'], i)
            if k in seen: continue
            seen.add(k)
            mod[k] = (z['path'], kv)
    ship = {}
    for z in loaded:
        for fl in z['files']: ship.setdefault(fl.replace('\\', '/').lower(), z['path'])
    return van, mod, loaded, ship, by_guid


# ---------------------------------------------------------------- bundles
class Bundles:
    """Bundle bytes from a zipmod (exact or case-insensitive entry), the first zipmod that ships the path, or abdata."""
    def __init__(self, hs2, ship):
        self.hs2, self.ship, self.names = hs2, ship, {}

    def _names(self, z):
        if z not in self.names:
            try:
                with zipfile.ZipFile(z) as zf: self.names[z] = {e.replace('\\', '/').lower(): e for e in zf.namelist()}
            except Exception:
                self.names[z] = {}
        return self.names[z]

    def get(self, zpath, ab):
        ab = (ab or '').replace('\\', '/')
        for z in ([zpath] if zpath else []) + ([self.ship.get(ab.lower())] if self.ship.get(ab.lower()) not in (None, zpath) else []):
            ent = self._names(z).get('abdata/' + ab.lower())
            if ent is not None:
                try:
                    with zipfile.ZipFile(z) as zf: return zf.read(ent), f'{os.path.basename(z)}:{ab}', z
                except Exception:
                    pass
        p = os.path.join(self.hs2, 'abdata', ab)
        if os.path.isfile(p):
            with open(p, 'rb') as fh: return fh.read(), f'abdata:{ab}', None
        return None, None, None


def tt(obj):
    try: return obj.read_typetree()
    except Exception: return None


def pid(p):
    """PPtr dict -> (file id, path id)."""
    if isinstance(p, dict): return p.get('m_FileID', 0), p.get('m_PathID', 0)
    return 0, 0


class BundleView:
    """Lazy type-tree reader over one loaded bundle (objects by path id, transforms' fathers, game object names)."""
    def __init__(self, data):
        self.env = UnityPy.load(data)
        self.objs = {}
        for o in self.env.objects: self.objs.setdefault(o.path_id, o)
        self.cache = {}
        self._fathers = None
        self.shader_names = {}

    def get(self, path_id):
        if path_id in self.cache: return self.cache[path_id]
        o = self.objs.get(path_id)
        v = tt(o) if o is not None else None
        self.cache[path_id] = v
        return v

    def type_of(self, path_id):
        o = self.objs.get(path_id)
        return o.type.name if o is not None else None

    def fathers(self):
        if self._fathers is None:
            self._fathers = {}
            for o in self.env.objects:
                if o.type.name != 'Transform' and o.type.name != 'RectTransform': continue
                t = self.get(o.path_id)
                if t: self._fathers[o.path_id] = pid(t.get('m_Father'))[1]
        return self._fathers

    def roots_named(self, name):
        """Root game objects (Transform without father) named `name`."""
        out = []
        for o in self.env.objects:
            if o.type.name != 'GameObject': continue
            try:
                nm = o.peek_name() if hasattr(o, 'peek_name') else None
            except Exception:
                nm = None
            if nm is not None and nm != name: continue
            g = self.get(o.path_id)
            if not g or g.get('m_Name') != name: continue
            tr = None
            for c in g.get('m_Component') or []:
                cp = pid(c.get('component') if isinstance(c, dict) and 'component' in c else c)[1]
                if self.type_of(cp) in ('Transform', 'RectTransform'): tr = cp; break
            if tr is None: continue
            t = self.get(tr)
            if t and pid(t.get('m_Father'))[1] == 0: out.append((o.path_id, g, tr))
        return out

    def components(self, g):
        for c in g.get('m_Component') or []:
            yield pid(c.get('component') if isinstance(c, dict) and 'component' in c else c)[1]

    def shader_name(self, sptr):
        fid, p = pid(sptr)
        if p == 0: return 'none'
        if fid != 0: return 'external'
        if p not in self.shader_names:
            nm = '?'
            o = self.objs.get(p)
            if o is not None:
                try:
                    s = o.read()
                    pf = getattr(s, 'm_ParsedForm', None)
                    nm = (pf.m_Name if pf is not None else None) or getattr(s, 'm_Name', None) or '?'
                except Exception:
                    nm = 'unreadable'
            self.shader_names[p] = nm
        return self.shader_names[p]

    def ancestors(self, t):
        fa = self.fathers()
        out, seen = [], set()
        while t and t not in seen:
            seen.add(t)
            t = fa.get(t, 0)
            if t: out.append(t)
        return out


def material_info(bv, mptr):
    fid, p = pid(mptr)
    if p == 0: return None
    if fid != 0: return {'external': True}
    m = bv.get(p)
    if not m: return None
    sp = m.get('m_SavedProperties') or {}
    tex = {}
    for e in sp.get('m_TexEnvs') or []:
        k, v = (e[0], e[1]) if isinstance(e, (list, tuple)) else (e.get('first'), e.get('second'))
        tex[k] = pid(v.get('m_Texture'))[1] != 0 if isinstance(v, dict) else False
    flo = {}
    for e in sp.get('m_Floats') or []:
        k, v = (e[0], e[1]) if isinstance(e, (list, tuple)) else (e.get('first'), e.get('second'))
        flo[k] = v
    return {'name': m.get('m_Name'), 'shader': bv.shader_name(m.get('m_Shader')), 'tex': tex, 'floats': flo,
            'keywords': (m.get('m_ShaderKeywords') or '')}


def renderer_materials(bv, rptr):
    p = pid(rptr)[1]
    if p == 0: return None
    r = bv.get(p)
    if not r: return None
    return r.get('m_Materials') or []


def prefab_info(bv, name):
    """CmpHair facts of the root game object `name` in a loaded bundle, or a dict with 'status'."""
    roots = bv.roots_named(name)
    if not roots: return {'status': 'no prefab'}
    gpid, g, tr = roots[0]
    cmp, dbs = None, []
    for cp in bv.components(g):
        if bv.type_of(cp) != 'MonoBehaviour': continue
        t = bv.get(cp)
        if not t: continue
        if 'boneInfo' in t and 'rendHair' in t: cmp = t
        elif 'm_Root' in t and 'm_Damping' in t: dbs.append(cp)
    if cmp is None: return {'status': 'no CmpHair'}
    info = {'status': 'ok'}
    for k in ('useTopColor', 'useUnderColor', 'useSameSkinColorButton', 'useMesh', 'useAcsColor01', 'useAcsColor02', 'useAcsColor03'):
        info[k] = bool(cmp.get(k))
    rh = [x for x in (cmp.get('rendHair') or [])]
    ra = [x for x in (cmp.get('rendAccessory') or [])]
    info['rendHair'] = sum(1 for x in rh if pid(x)[1] != 0)
    info['rendHairNull'] = sum(1 for x in rh if pid(x)[1] == 0)
    info['rendAccessory'] = sum(1 for x in ra if pid(x)[1] != 0)
    adc = cmp.get('acsDefColor') or []
    info['acsDefColor'] = [col(c) for c in adc]
    # rendAccessory material slots (HS2 writes every slot; one renderer may hold several)
    slots = []
    for x in ra:
        ms = renderer_materials(bv, x)
        if ms is not None: slots.append(len(ms))
    info['acsSlots'] = slots
    # rendHair materials
    mats = []
    for x in rh:
        ms = renderer_materials(bv, x)
        if not ms: continue
        for mp in ms:
            mi = material_info(bv, mp)
            if mi: mats.append(mi)
    info['mats'] = mats
    # bundles
    bundles = []
    db_in_bundles = set()
    for b in cmp.get('boneInfo') or []:
        tc = pid(b.get('trfCorrect'))[1]
        rec = {'trf': tc != 0, 'dbs': 0, 'rel': collections.Counter()}
        for k in ('posMin', 'posMax', 'rotMin', 'rotMax', 'basePos', 'baseRot'): rec[k] = vec(b.get(k), 3)
        if rec['posMin'] and rec['posMax'] and rec['basePos']:
            rec['defMove'] = tuple(inv_lerp(rec['posMin'][i], rec['posMax'][i], rec['basePos'][i]) for i in range(3))
            rec['defRot'] = tuple(inv_lerp(rec['rotMin'][i], rec['rotMax'][i], rec['baseRot'][i]) for i in range(3))
        anc_tc = set(bv.ancestors(tc)) if tc else set()
        for d in b.get('dynamicBone') or []:
            dp = pid(d)[1]
            if dp == 0: continue
            rec['dbs'] += 1
            db_in_bundles.add(dp)
            dt = bv.get(dp)
            root = pid(dt.get('m_Root'))[1] if dt else 0
            if not tc or not root: rec['rel']['unknown'] += 1
            elif root == tc: rec['rel']['root = trfCorrect'] += 1
            elif tc in set(bv.ancestors(root)): rec['rel']['trfCorrect above the root'] += 1
            elif root in anc_tc: rec['rel']['trfCorrect inside the chain'] += 1
            else: rec['rel']['unrelated'] += 1
        bundles.append(rec)
    info['bundles'] = bundles
    info['dbsRoot'] = len(dbs)
    info['dbsNotInBundles'] = sum(1 for d in dbs if d not in db_in_bundles)
    return info


def tex_header(bv_env, names):
    r = {}
    for obj in bv_env.objects:
        if obj.type.name != 'Texture2D': continue
        try:
            nm = obj.peek_name() if hasattr(obj, 'peek_name') else None
            if nm is not None and nm not in names: continue
            t = obj.read()
            if t.m_Name not in names or t.m_Name in r: continue
            ts = t.m_TextureSettings
            wu = getattr(ts, 'm_WrapU', getattr(ts, 'm_WrapMode', -1))
            wv = getattr(ts, 'm_WrapV', getattr(ts, 'm_WrapMode', -1))
            fmt = getattr(t.m_TextureFormat, 'name', None)
            if fmt is None:
                try: fmt = UnityPy.enums.TextureFormat(int(t.m_TextureFormat)).name
                except Exception: fmt = str(t.m_TextureFormat)
            r[t.m_Name] = (t.m_Width, t.m_Height, fmt, t.m_MipCount, int(wu), int(wv))
        except Exception:
            pass
    return r


# ---------------------------------------------------------------- cards
def unpack_any(b):
    def hook(code, data):
        if code == 99:
            try:
                import lz4.block
                n = struct.unpack('>i', data[1:5])[0] if data[:1] == b'\xd2' else None
                raw = lz4.block.decompress(data[5:], uncompressed_size=n)
                return msgpack.unpackb(raw, raw=False, strict_map_key=False, ext_hook=hook)
            except Exception:
                return None
        return msgpack.ExtType(code, data)
    try: return msgpack.unpackb(bytes(b), raw=False, strict_map_key=False, ext_hook=hook)
    except Exception: return None


def plugin_data(ext, key):
    v = ext.get(key)
    if isinstance(v, (list, tuple)) and len(v) > 1 and isinstance(v[1], dict): return v[1]
    if isinstance(v, dict): return v.get('data') if isinstance(v.get('data'), dict) else v
    return None


def derive(base):
    h, s, v = rgb_to_hsv(*base[:3])
    top = hsv(h, s, max(v - 0.15, 0.0))
    under = hsv(h, max(s - 0.1, 0.0), min(v + 0.44, 1.0))
    spec = hsv(h, s, min(v + 0.17, 1.0))
    return top, under, spec


def main():
    utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--no-prefabs', action='store_true', help='skip section 5 (the hair prefabs; ~minutes)')
    ap.add_argument('--prefab-limit', type=int, default=0, help='read at most N prefab bundles (a quick check)')
    ap.add_argument('--thumbs', action='store_true', help='section 8: thumbnail texture headers of the hair lists (m_MipCount < 1)')
    ap.add_argument('--record-guard', action='store_true', help='with --thumbs: append the found textures to <cache>/texguard/bad_textures.tsv')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = []
    w = lines.append
    van, mod, loaded, ship, by_guid = hair_rows(a)
    bundles = Bundles(a.hs2, ship)

    def rows_of(c):
        """[(ref, zip or None, {col: val})] listed rows (Possess 99 hidden) of category c, list order (vanilla, then mods by GUID)."""
        r = [(f'{VANILLA}#{i}', None, v) for (cc, i), v in sorted(van.items()) if cc == c and str(v.get('Possess', '0')) != '99']
        mods = [(g, i, zp, kv) for (cc, g, i), (zp, kv) in mod.items() if cc == c]
        mods.sort(key=lambda t: (t[0].lower(), t[1]))
        r += [(f'{g}#{i}', zp, kv) for g, i, zp, kv in mods]
        return r

    # ---------------------------------------------------------------- 1. lists
    w('# 1. hair lists (vanilla after C9 = gate open, Possess 99 hidden; mod rows = one zipmod per GUID, first row of a key)')
    w('category\tvanilla\tmod\t"none" rows (MainData p_dummy)\tno thumbnail (vanilla / mod)\trow 0 name\tdefault female / male')
    for c in HAIR_CATS + (305, 306):
        rs = rows_of(c)
        v = [x for x in rs if x[1] is None]
        m = [x for x in rs if x[1] is not None]
        none = sum(1 for _, _, kv in rs if kv.get('MainData') == 'p_dummy')
        nth = lambda xs: sum(1 for _, _, kv in xs if empty(kv.get('ThumbAB')) or empty(kv.get('ThumbTex')))
        r0 = van.get((c, 0), {}).get('Name')
        d = ''
        if c in HAIR_CATS:
            k = c - 300
            d = f"{van.get((c, DEFAULT_IDS[1][k]), {}).get('Name')} (id {DEFAULT_IDS[1][k]}) / {van.get((c, DEFAULT_IDS[0][k]), {}).get('Name')} (id {DEFAULT_IDS[0][k]})"
        w(f'{c}\t{len(v)}\t{len(m)}\t{none}\t{nth(v)} / {nth(m)}\t{r0}\t{d}')
    for c in HAIR_CATS:
        rs = rows_of(c)
        kinds = collections.Counter(kv.get('Kind') for _, _, kv in rs)
        weights = collections.Counter(kv.get('Weights') for _, _, kv in rs if 'Weights' in kv)
        md2 = [(ref, kv.get('MainData'), kv.get('MainData02')) for ref, _, kv in rs if not empty(kv.get('MainData02'))]
        ring = sum(1 for _, _, kv in rs if kv.get('RingOff') == '1')
        notex = sum(1 for _, _, kv in rs if empty(kv.get('TexAB')) and kv.get('MainData') != 'p_dummy')
        dc = sum(1 for _, _, kv in rs if not empty(kv.get('TexD')) and kv.get('TexD') == kv.get('TexC'))
        w(f'  {c}: Kind {dict(sorted(kinds.items(), key=lambda t: str(t[0])))}; Weights {dict(weights)}; RingOff 1: {ring}; '
          f'no TexAB (not none rows): {notex}; TexD = TexC: {dc}; MainData02 rows: {len(md2)} {md2[:6]}')
        nn = [(ref, kv.get('Name')) for ref, zp, kv in rs if zp is None and kv.get('MainData') == 'p_dummy']
        if nn: w(f'  {c}: vanilla "none" rows {nn}')

    # ---------------------------------------------------------------- 2. colour presets 305
    w('# 2. hair colour presets (305): id, Name, Base / Top / Under / Specular = HSVToRGB(<X>H, <X>S, <X>V), Metallic, Smoothness, swatch = Sample')
    for (c, i), v in sorted((k, v) for k, v in van.items() if k[0] == 305):
        parts = []
        for X in ('Base', 'Top', 'Under', 'Specular'):
            parts.append(f"{X} ({v.get(X + 'H')}, {v.get(X + 'S')}, {v.get(X + 'V')}) -> {r4(hsv(v.get(X + 'H'), v.get(X + 'S'), v.get(X + 'V')))}")
        w(f"preset\t{i}\t{v.get('Name')}\t" + '; '.join(parts) + f"; Metallic {v.get('Metallic')}; Smoothness {v.get('Smoothness')}; "
          f"swatch {r4(hsv(v.get('SampleH'), v.get('SampleS'), v.get('SampleV')))}")
    w(f'mod rows of 305: {sum(1 for k in mod if k[0] == 305)}; vanilla ids {sorted(i for (c, i) in van if c == 305)}')

    # ---------------------------------------------------------------- 3. mesh patterns 306
    w('# 3. hair mesh patterns (306): id, Name, MainAB / MainTex, ThumbAB / ThumbTex, MainTex header (size, format, mips, wrap U / V)')
    for ref, zp, kv in rows_of(306):
        hdr = ''
        if not empty(kv.get('MainAB')) and not empty(kv.get('MainTex')):
            data, src, _ = bundles.get(zp, kv['MainAB'])
            if data is None: hdr = 'bundle not found'
            else:
                h = tex_header(UnityPy.load(data), {kv['MainTex']}).get(kv['MainTex'])
                hdr = f'{h[0]}x{h[1]} {h[2]} mips {h[3]} wrap {WRAP.get(h[4], h[4])}/{WRAP.get(h[5], h[5])}' if h else 'texture not in the bundle'
        else:
            hdr = 'no texture (MainAB / MainTex "0": the game sets null)'
        w(f"mesh\t{ref}\t{kv.get('Name')}\t{kv.get('MainAB')} / {kv.get('MainTex')}\t{kv.get('ThumbAB')} / {kv.get('ThumbTex')}\t{hdr}")
    w(f'mod rows of 306: {sum(1 for k in mod if k[0] == 306)}')

    # ---------------------------------------------------------------- 4. hair shader materials
    w('# 4. hair shader materials (chara/hair_shader_mat.unity3d)')
    try:
        bv = BundleView(os.path.join(a.hs2, 'abdata', 'chara', 'hair_shader_mat.unity3d'))
        for o in bv.env.objects:
            if o.type.name != 'Material': continue
            mi = material_info(bv, {'m_FileID': 0, 'm_PathID': o.path_id})
            fl = mi['floats']
            w(f"material\t{mi['name']}\tshader {mi['shader']}\t_Mesh_onoff {fl.get('_Mesh_onoff')}\t_Ringoff {fl.get('_Ringoff')}\t"
              f"_Ringcontrolr_onoff {fl.get('_Ringcontrolr_onoff')}\t_MeshColorMask set {mi['tex'].get('_MeshColorMask')}\t"
              f"_MeshColorMask_B set {mi['tex'].get('_MeshColorMask_B')}\tkeywords '{mi['keywords']}'")
    except Exception as ex:
        w(f'hair shader materials: failed ({type(ex).__name__}: {ex})')

    # ---------------------------------------------------------------- 5. prefabs
    PREFAB = {}   # (cat, ref, male variant) -> info
    if not a.no_prefabs:
        w('# 5. hair prefabs (CmpHair of each listed row\'s MainData; male variants = MainData02): flags, renderers, bundles, chains, materials')
        jobs = collections.defaultdict(list)   # (zip or None, bundle lower) -> [(cat, ref, prefab name, male)]
        for c in HAIR_CATS:
            for ref, zp, kv in rows_of(c):
                if kv.get('MainData') in (None, '', '0', 'p_dummy') or empty(kv.get('MainAB')): continue
                jobs[(zp, kv['MainAB'].replace('\\', '/').lower())].append((c, ref, kv['MainData'], False, kv['MainAB']))
                if not empty(kv.get('MainData02')):
                    jobs[(zp, kv['MainAB'].replace('\\', '/').lower())].append((c, ref, kv['MainData02'], True, kv['MainAB']))
        n = 0
        status = collections.Counter()
        for (zp, abl), lst in sorted(jobs.items(), key=lambda t: (t[0][0] or '', t[0][1])):
            n += 1
            if a.prefab_limit and n > a.prefab_limit: break
            data, src, _ = bundles.get(zp, lst[0][4])
            if data is None:
                for c, ref, nm, male, _ in lst: PREFAB[(c, ref, male)] = {'status': 'bundle not found'}; status['bundle not found'] += 1
                continue
            try:
                bv = BundleView(data)
            except Exception:
                for c, ref, nm, male, _ in lst: PREFAB[(c, ref, male)] = {'status': 'bundle unreadable'}; status['bundle unreadable'] += 1
                continue
            for c, ref, nm, male, _ in lst:
                try: info = prefab_info(bv, nm)
                except Exception as ex: info = {'status': f'error {type(ex).__name__}'}
                info['src'] = src
                PREFAB[(c, ref, male)] = info
                status[info['status']] += 1
            if n % 100 == 0: print(f'... prefabs: {n} of {len(jobs)} bundles', file=sys.stderr, flush=True)
        w(f'prefab rows read: {dict(status)} (bundles {len(jobs)})')
        for c in HAIR_CATS:
            for side in ('vanilla', 'mod'):
                infos = [(k, v) for k, v in PREFAB.items() if k[0] == c and not k[2] and v.get('status') == 'ok' and (k[1].startswith(VANILLA + '#') == (side == 'vanilla'))]
                if not infos: continue
                fl = collections.Counter()
                bc = collections.Counter()
                rel = collections.Counter()
                trfnull = nb = dbs = dbs_not = acs_multi = acs_short = rh0 = 0
                onoff = collections.Counter(); meshmask = collections.Counter(); shaders = collections.Counter()
                rotw, posw = [], []
                def_out = 0
                for k, v in infos:
                    for f_ in ('useTopColor', 'useUnderColor', 'useSameSkinColorButton', 'useMesh', 'useAcsColor01', 'useAcsColor02', 'useAcsColor03'):
                        if v[f_]: fl[f_] += 1
                    if not v['useTopColor']: fl['useTopColor false'] += 1
                    if not v['useUnderColor']: fl['useUnderColor false'] += 1
                    if v['rendHair'] == 0: rh0 += 1
                    used = [v['useAcsColor01'], v['useAcsColor02'], v['useAcsColor03']]
                    if any(used[j] and j >= len(v['acsDefColor']) for j in range(3)): acs_short += 1
                    if any(s > 1 for s in v['acsSlots']): acs_multi += 1
                    bs = v['bundles']
                    shown = sum(1 for b in bs if b['trf'])
                    bc[len(bs)] += 1
                    nb += len(bs)
                    trfnull += len(bs) - shown
                    for b in bs:
                        dbs += b['dbs']
                        rel.update(b['rel'])
                        if b.get('rotMin') and b.get('rotMax'): rotw.append(max(abs(b['rotMax'][i] - b['rotMin'][i]) for i in range(3)))
                        if b.get('posMin') and b.get('posMax'): posw.append(max(abs(b['posMax'][i] - b['posMin'][i]) for i in range(3)))
                        if b.get('basePos') and b.get('posMin'):
                            for i in range(3):
                                lo, hi = sorted((b['posMin'][i], b['posMax'][i]))
                                if not (lo - 1e-6 <= b['basePos'][i] <= hi + 1e-6): def_out += 1
                    dbs_not += v['dbsNotInBundles']
                    for m in v['mats']:
                        shaders[m.get('shader', 'external' if m.get('external') else '?')] += 1
                        if v['useMesh'] and not m.get('external'):
                            onoff[round(m['floats'].get('_Mesh_onoff', float('nan')), 3) if '_Mesh_onoff' in m['floats'] else 'absent'] += 1
                            meshmask[('mask' if m['tex'].get('_MeshColorMask') else 'no mask') + ', ' + ('B set' if m['tex'].get('_MeshColorMask_B') else 'B empty')] += 1
                rotw.sort(); posw.sort()
                med = lambda xs: xs[len(xs) // 2] if xs else float('nan')
                w(f'{c} {side}: prefabs {len(infos)}; flags {dict(fl)}; rendHair empty {rh0}; useAcsColor0N beyond acsDefColor {acs_short}; '
                  f'rendAccessory with > 1 material slot {acs_multi}')
                w(f'  bundles: {nb} on {len(infos)} prefabs, per prefab {dict(sorted(bc.items()))}; trfCorrect missing {trfnull}; '
                  f'chains in bundles {dbs}; chains on the root not in any bundle {dbs_not}')
                w(f'  bundle vs chain root: {dict(rel)}; base pose outside [posMin, posMax] (axes) {def_out}; '
                  f'widest rotation range per bundle: median {med(rotw):.1f} deg, max {(rotw[-1] if rotw else float("nan")):.1f}; '
                  f'widest position range: median {med(posw):.3f}, max {(posw[-1] if posw else float("nan")):.3f}')
                w(f'  rendHair material shaders {dict(shaders.most_common(6))}')
                if onoff: w(f'  useMesh prefabs: _Mesh_onoff values {dict(onoff.most_common(6))}; masks {dict(meshmask)}')
        males = [(k, v) for k, v in PREFAB.items() if k[2]]
        for k, v in males:
            if v.get('status') == 'ok':
                w(f"male variant {k[0]} {k[1]}: bundles {len(v['bundles'])}, useMesh {v['useMesh']}, useAcsColor {[v['useAcsColor01'], v['useAcsColor02'], v['useAcsColor03']]}")
            else:
                w(f"male variant {k[0]} {k[1]}: {v.get('status')}")
        # examples of bundles whose correction transform is a chain transform
        ex = []
        for k, v in PREFAB.items():
            if v.get('status') != 'ok': continue
            for bi, b in enumerate(v['bundles']):
                if b['rel'].get('root = trfCorrect') or b['rel'].get('trfCorrect inside the chain'):
                    ex.append(f'{k[0]} {k[1]} bundle {bi}: {dict(b["rel"])}')
        w(f'bundles whose trfCorrect is a chain transform (root or inside): {len(ex)}')
        for e in ex[:12]: w('  ' + e)
        # vanilla examples: bundle limits and default rates
        for (c, ref, male), v in sorted(PREFAB.items()):
            if male or not ref.startswith(VANILLA + '#') or v.get('status') != 'ok' or not v['bundles']: continue
            if ref not in (f'{VANILLA}#0', f'{VANILLA}#1', f'{VANILLA}#2'): continue
            for bi, b in enumerate(v['bundles'][:3]):
                w(f"  example {c} {ref} bundle {bi}: posMin {r4(b['posMin'])} posMax {r4(b['posMax'])} rotMin {r4(b['rotMin'])} rotMax {r4(b['rotMax'])} "
                  f"basePos {r4(b['basePos'])} baseRot {r4(b['baseRot'])} -> default move {r4(b.get('defMove', ()))} rotate {r4(b.get('defRot', ()))}; chains {b['dbs']}")

    # ---------------------------------------------------------------- 6. cards
    w('# 6. installed cards: the hair block and hair-related plugin data')
    presets305 = {}
    for (c, i), v in van.items():
        if c != 305: continue
        presets305[i] = tuple(hsv(v.get(X + 'H'), v.get(X + 'S'), v.get(X + 'V')) for X in ('Base', 'Top', 'Under', 'Specular')) + (f(v.get('Metallic')), f(v.get('Smoothness')))
    # hair-type accessory candidates: accessory rows (351..363) with a TexAB (the hair shader's texture columns)
    hairtype = set()
    for (c, i), v in van.items():
        if 351 <= c <= 363 and not empty(v.get('TexAB')): hairtype.add((c, VANILLA, i))
    for (c, g, i), (zp, kv) in mod.items():
        if 351 <= c <= 363 and not empty(kv.get('TexAB')): hairtype.add((c, g.lower(), i))
    w(f'accessory rows with TexAB (hair-type candidates): {len(hairtype)}')
    modidx = {(c, g.lower(), i): (c, g, i) for (c, g, i) in mod}
    for sex in ('female', 'male'):
        sx = 1 if sex == 'female' else 0
        folder = os.path.join(a.hs2, 'UserData', 'chara', sex)
        n = bad = 0
        st = collections.Counter()
        items = collections.Counter()
        me = collections.Counter(); me_cards = 0; me_props = collections.Counter(); me_shaders = collections.Counter()
        aac = collections.Counter(); abmx = collections.Counter(); abmx_bones = collections.Counter()
        mesh_ids = collections.Counter()
        mesh_vals = collections.Counter(); mesh_eff = collections.Counter()
        oor = collections.Counter()
        for root_, _, files in os.walk(folder):
            for fn in sorted(files):
                if not fn.lower().endswith('.png'): continue
                try:
                    c = load_card(os.path.join(root_, fn))
                    hair = c['custom']['hair']
                except Exception:
                    bad += 1
                    continue
                n += 1
                res = {}
                for ri in resolver_info(c):
                    pr = ri.get('Property') or ''
                    if pr in PART_PROPS or pr in MESH_PROPS: res[pr] = ri
                st[f"shaderType {hair.get('shaderType')}"] += 1
                for k in ('sameSetting', 'autoSetting', 'ctrlTogether'):
                    st[f'{k} {bool(hair.get(k))}'] += 1
                parts = hair.get('parts') or []
                cols_same = True
                any_bundle_oor = False
                card_mesh = card_chainb = card_usemesh = False
                for k, p in enumerate(parts[:4]):
                    c_ = 300 + k
                    pid_ = p.get('id', 0)
                    ri = res.get(PART_PROPS[k])
                    if ri and ri.get('ModID'):
                        g = ri['ModID'].strip()
                        key = modidx.get((c_, g.lower(), pid_))
                        if key is None: st['part: mod not installed'] += 1; items['mod missing'] += 1
                        else: items['mod'] += 1
                        ref = f'{key[1]}#{key[2]}' if key else f'{g}#{pid_}'
                    else:
                        ref = f'{VANILLA}#{pid_}'
                        if (c_, pid_) in van: items['vanilla'] += 1
                        else: items['vanilla id not listed'] += 1; st['part: vanilla id not listed'] += 1
                    male_variant = sx == 0 and not empty((van.get((c_, pid_)) or {}).get('MainData02')) and ref.startswith(VANILLA)
                    info = PREFAB.get((c_, ref, male_variant)) or PREFAB.get((c_, ref, False))
                    ok = info is not None and info.get('status') == 'ok'
                    # colours
                    cs = {m: col(p.get(m)) for m in ('baseColor', 'topColor', 'underColor', 'specular')}
                    for m, cv in cs.items():
                        if cv and cv[3] < 1 - 1e-4: st[f'{m} alpha < 1'] += 1
                    for m in ('metallic', 'smoothness'):
                        x = f(p.get(m, 0))
                        if x < 0 or x > 1: oor[m] += 1
                        if abs(x - DOC[m]) > 1e-4: st[f'{m} != 0'] += 1
                    if all(cs.values()):
                        t, u, s_ = derive(cs['baseColor'])
                        if near(cs['topColor'][:3], t, 2e-3) and near(cs['underColor'][:3], u, 2e-3) and near(cs['specular'][:3], s_, 2e-3): st['part colours = the auto rule of its base'] += 1
                        for pi, pv in presets305.items():
                            if all(near(cs[m][:3], pv[j], 2e-3) for j, m in enumerate(('baseColor', 'topColor', 'underColor', 'specular'))):
                                st[f'part colours = preset 305#{pi}'] += 1
                                break
                    if k > 0 and not all(near(col(parts[0].get(m)), col(p.get(m)), 1e-4) for m in ('baseColor', 'topColor', 'underColor', 'specular')):
                        cols_same = False
                    # decoration colours
                    acs = [col(x.get('color')) if isinstance(x, dict) else None for x in (p.get('acsColorInfo') or [])]
                    if ok:
                        use = [info['useAcsColor01'], info['useAcsColor02'], info['useAcsColor03']]
                        if any(use): st['part on a prefab with decoration colours'] += 1
                        for j in range(3):
                            if use[j] and j < len(acs) and acs[j]:
                                d = info['acsDefColor'][j] if j < len(info['acsDefColor']) else None
                                st[f'deco colour {j + 1} used by the prefab: ' + ('= acsDefColor' if d and near(acs[j], d, 2e-3) else 'differs from acsDefColor')] += 1
                        if not info['useTopColor']: st['part on a prefab without top colour'] += 1
                        if not info['useUnderColor']: st['part on a prefab without under colour'] += 1
                        if info['useSameSkinColorButton']: st['part on a prefab with the skin-match button'] += 1
                    # mesh
                    if 'meshType' not in p: st['meshType key absent'] += 1
                    mt = p.get('meshType', 0)
                    mri = res.get(MESH_PROPS[k])
                    if mt:
                        mesh_ids[(f"{mri['ModID']}#{mt}" if mri and mri.get('ModID') else f'{VANILLA}#{mt}')] += 1
                        st['meshType != 0'] += 1
                        if ok: st['meshType != 0 on a useMesh prefab' if info['useMesh'] else 'meshType != 0 on a prefab without useMesh (no effect in HS2)'] += 1
                        else: st['meshType != 0, prefab not read'] += 1
                    if ok and info['useMesh']: st['part on a useMesh prefab'] += 1; card_usemesh = True
                    mc = col(p.get('meshColor')) if 'meshColor' in p else None
                    ml = vec(p.get('meshLayout'), 4) if 'meshLayout' in p else None
                    if mc: mesh_vals['meshColor ' + str(r4(mc))] += 1
                    if ml: mesh_vals['meshLayout ' + str(r4(ml))] += 1
                    if ok and any(b['rel'].get('root = trfCorrect') or b['rel'].get('trfCorrect inside the chain') for b in info['bundles']):
                        st['parts on a prefab with a bundle whose trfCorrect is a chain transform'] += 1; card_chainb = True
                    if mt and ok and info['useMesh']:
                        card_mesh = True
                        if ml: mesh_eff['layout ' + str(r4(ml))] += 1
                        if mc: mesh_eff['colour alpha ' + ('1' if mc[3] >= 1 - 1e-4 else '< 1')] += 1
                    if mc and not near(mc, DOC['meshColor']): st['meshColor != white'] += 1
                    if mc and mc[3] < 1 - 1e-4: st['meshColor alpha < 1'] += 1
                    if ml and not near(ml, DOC['meshLayout']): st['meshLayout != (1, 1, 0, 0)'] += 1
                    if ml and any(x < 0 or x > 1 for x in ml): oor['meshLayout'] += 1
                    # bundles
                    db = p.get('dictBundle') or {}
                    bid = p.get('bundleId', -1)
                    if db:
                        st['parts with bundles'] += 1
                        if ref.startswith(VANILLA) and bid != pid_: st['vanilla part with bundles, bundleId != id (HS2 drops them)'] += 1
                        if ok:
                            nbp = len(info['bundles'])
                            st['bundles: count = prefab' if len(db) == nbp else 'bundles: count != prefab (HS2 resets to the defaults)'] += 1
                            if len(db) != nbp: st[f'  count mismatch {len(db)} vs {nbp}'] += 0
                        else:
                            st['bundles: prefab not read'] += 1
                        for bk, bv_ in db.items():
                            if not isinstance(bv_, dict): continue
                            mv, rr = vec(bv_.get('moveRate'), 3), vec(bv_.get('rotRate'), 3)
                            if bv_.get('noShake'): st['bundle noShake true'] += 1
                            for nm, vv in (('moveRate', mv), ('rotRate', rr)):
                                if vv and any(x < -1e-6 or x > 1 + 1e-6 for x in vv): oor['bundle ' + nm] += 1; any_bundle_oor = True
                            if ok and isinstance(bk, int) and bk < len(info['bundles']) and info['bundles'][bk].get('defMove'):
                                b = info['bundles'][bk]
                                if near(mv, b['defMove'], 1e-3) and near(rr, b['defRot'], 1e-3): st['bundle = prefab default rates'] += 1
                                else: st['bundle != prefab default rates'] += 1
                    elif ok and info['bundles']:
                        st['parts without stored bundles on a prefab with bundles (defaults)'] += 1
                if not cols_same: st['cards whose four parts differ in colour'] += 1
                if card_mesh: st['cards with a drawn mesh pattern (meshType != 0 on a useMesh prefab)'] += 1
                if card_chainb: st['cards with a part whose prefab has a chain-transform bundle'] += 1
                if card_usemesh: st['cards with a part on a useMesh prefab (HS2 writes its mesh values)'] += 1
                if any_bundle_oor: st['cards with a bundle rate outside 0..1'] += 1
                # plugin data
                ext = c.get('ext') or {}
                d = plugin_data(ext, ME_KEY)
                hit = False
                if d:
                    for lk in ME_LISTS:
                        blob = d.get(lk)
                        if not isinstance(blob, (bytes, bytearray)): continue
                        lst = unpack_any(blob) or []
                        for it in lst if isinstance(lst, list) else []:
                            if isinstance(it, dict) and it.get('ObjectType') == 3:
                                hit = True
                                me[f"{lk} slot {it.get('Slot')}"] += 1
                                if lk == 'MaterialShaderList': me_shaders[str(it.get('ShaderName'))] += 1
                                elif lk == 'RendererPropertyList': me_props[f"Renderer.{it.get('Property')}"] += 1
                                elif 'Property' in it: me_props[f"{lk[8:].replace('PropertyList', '')}.{it.get('Property')}"] += 1
                if hit: me_cards += 1
                d = plugin_data(ext, AAC_KEY)
                if d and isinstance(d.get('accessoryData'), (bytes, bytearray)):
                    slots = unpack_any(d['accessoryData']) or []
                    for s in slots if isinstance(slots, list) else []:
                        if not isinstance(s, list) or not s or s[0]: continue
                        if len(s) > 5 and s[5]: aac['slots with AutoMatchBackHairColor'] += 1
                        for rule in (s[6] if len(s) > 6 and isinstance(s[6], list) else []):
                            if isinstance(rule, list) and len(rule) >= 2 and rule[0] == 30:
                                aac['HAIR rule ' + AAC_HAIR_MOD.get(str(rule[1]), str(rule[1]))] += 1
                d = plugin_data(ext, ABMX_KEY)
                if d and d.get('boneData') is not None:
                    bd = d['boneData']
                    mods_ = unpack_any(bd) if isinstance(bd, (bytes, bytearray)) else bd
                    if isinstance(mods_, list):
                        hb = [m_[0] for m_ in mods_ if isinstance(m_, list) and m_ and isinstance(m_[0], str) and 'hair' in m_[0].lower()]
                        if hb: abmx['cards with an ABMX modifier on a hair bone'] += 1; abmx['modifiers'] += len(hb); abmx_bones.update(hb)
                # accessories on hair-type rows (the 20 base slots)
                try:
                    accp = c['coordinate']['accessory'].get('parts') or []
                except Exception:
                    accp = []
                ares = {}
                for ri in resolver_info(c):
                    mm = re.match(r'(?:outfit\.)?accessory(\d+)\.ChaFileAccessory\.PartsInfo\.id$', ri.get('Property') or '')
                    if mm: ares[int(mm.group(1))] = ri
                cnt = 0
                for si, ap_ in enumerate(accp):
                    t_ = ap_.get('type', 350) if isinstance(ap_, dict) else 350
                    if not (351 <= t_ <= 363): continue
                    ri = ares.get(si)
                    g = (ri.get('ModID') or '').strip().lower() if ri else VANILLA
                    if (t_, g or VANILLA, ap_.get('id', 0)) in hairtype: cnt += 1
                if cnt: st['cards with an accessory on a hair-type row (base 20 slots)'] += 1; st['accessories on hair-type rows'] += cnt
        w(f'cards {sex}: read {n} (not character cards {bad})')
        w(f'  hair part items: {dict(items)}')
        w('  ' + ', '.join(f'{k} {v}' for k, v in sorted(st.items()) if v))
        w(f'  values outside 0..1 (parts / bundles): {dict(oor)}')
        w(f'  mesh patterns used (parts): {dict(mesh_ids.most_common(12))}')
        w(f'  stored mesh colour / layout (all parts, top): {dict(mesh_vals.most_common(8))}')
        w(f'  parts with a pattern on a useMesh prefab (HS2 draws it): {dict(mesh_eff.most_common(10))}')
        w(f'  MaterialEditor items of ObjectType Hair: cards {me_cards}; {dict(me.most_common(16))}')
        if me_props: w(f'    properties: {dict(me_props.most_common(16))}')
        if me_shaders: w(f'    shader swaps: {dict(me_shaders.most_common(8))}')
        w(f'  Additional Accessory Controls: {dict(aac)}')
        w(f'  ABMX on hair bones: {dict(abmx)}; bones {dict(abmx_bones.most_common(8))}')

    # ---------------------------------------------------------------- 7. loose PNGs
    w('# 7. Sideloader loose PNGs (GB10 rule) referenced by the hair rows: (bundle column, asset column) -> zipmod abdata/<bundle without .unity3d>/<asset>.png')
    pngs = {}
    unity3d = re.compile(re.escape('.unity3d'), re.IGNORECASE)
    for z in loaded:
        if not any(fl.lower().endswith('.png') for fl in z['files']): continue
        try:
            with zipfile.ZipFile(z['path']) as zf:
                for info in zf.infolist():
                    nm = info.filename.replace('\\', '/')
                    if nm.startswith('abdata/') and nm.endswith('.png') and nm[7:] not in pngs: pngs[nm[7:]] = z['guid']
        except Exception:
            pass
    shipped = set(ship.keys())
    pairs = {300: [('TexAB', 'TexD'), ('TexAB', 'TexC'), ('ThumbAB', 'ThumbTex')], 306: [('MainAB', 'MainTex'), ('ThumbAB', 'ThumbTex')]}
    for c in HAIR_CATS + (306,):
        cnt = collections.Counter()
        rows_hit = set()
        for ref, zp, kv in rows_of(c):
            for bc_, ac_ in pairs[306 if c == 306 else 300]:
                b, asset = kv.get(bc_), kv.get(ac_)
                if empty(b) or empty(asset): continue
                key = unity3d.sub('', b.replace('\\', '/')) + '/' + asset + '.png'
                if key in pngs:
                    cnt[f'{bc_}/{ac_}'] += 1
                    rows_hit.add(ref)
                    ex = b.replace('\\', '/').lower() in shipped or os.path.isfile(os.path.join(a.hs2, 'abdata', b))
                    cnt['bundle also exists' if ex else 'bundle absent (empty-bundle folder)'] += 1
                    if 'clamp' in ('abdata/' + key): cnt['clamp in the path'] += 1
        w(f'{c}: rows with a loose texture {len(rows_hit)}; references {dict(cnt)}')

    # ---------------------------------------------------------------- 8. thumbnails
    if a.thumbs:
        w('# 8. thumbnails of the hair lists (--thumbs): texture headers only (m_MipCount), no image decoded')
        want = collections.defaultdict(list)
        for c in HAIR_CATS + (306,):
            for ref, zp, kv in rows_of(c):
                ab, tex = kv.get('ThumbAB'), kv.get('ThumbTex')
                if empty(ab) or empty(tex): continue
                want[(zp, ab.replace('\\', '/'))].append((c, ref, tex))
        gl = os.path.join(os.path.dirname(os.path.abspath(a.modcache)), 'texguard', 'bad_textures.tsv')
        guard = set()
        if os.path.isfile(gl):
            for line in open(gl, encoding='utf-8', errors='replace'):
                p = line.rstrip('\n').split('\t')
                if len(p) >= 3: guard.add((p[0].lstrip('﻿').lower(), p[2]))
        stat = collections.defaultdict(collections.Counter)
        bad = []
        n = 0
        for (zp, ab), refs in sorted(want.items(), key=lambda t: (t[0][0] or '', t[0][1])):
            n += 1
            data, src, zsrc = bundles.get(zp, ab)
            if data is None:
                for c, ref, tex in refs:
                    stat[c]['no bundle (loose PNG)' if (unity3d.sub('', ab) + '/' + tex + '.png') in pngs else 'no bundle'] += 1
                continue
            try:
                h = tex_header(UnityPy.load(data), {t for _, _, t in refs})
            except Exception:
                for c, ref, tex in refs: stat[c]['bundle unreadable'] += 1
                continue
            for c, ref, tex in refs:
                stat[c]['rows'] += 1
                x = h.get(tex)
                if x is None: stat[c]['no texture'] += 1; continue
                if x[3] < 1:
                    stat[c]['mip < 1'] += 1
                    source = f"{ref.split('#')[0]}:{ab}" if zsrc else f'abdata:{ab}'
                    if (source.lower(), tex) in guard: stat[c]['mip < 1, already in the guard list'] += 1
                    bad.append((c, ref, source, tex, x, f'{zsrc}!{ab}' if zsrc else None))
            if n % 200 == 0: print(f'... thumbs: {n} of {len(want)} bundles', file=sys.stderr, flush=True)
        w('category\trows\tmip < 1\t(already in Cache/texguard/bad_textures.tsv)\tno texture\tno bundle\tno bundle (loose PNG)\tbundle unreadable')
        for c in sorted(stat):
            s = stat[c]
            w(f"{c}\t{s['rows']}\t{s['mip < 1']}\t{s['mip < 1, already in the guard list']}\t{s['no texture']}\t{s['no bundle']}\t{s['no bundle (loose PNG)']}\t{s['bundle unreadable']}")
        w(f'mip < 1: {len(bad)} row uses of {len({(b[2], b[3]) for b in bad})} textures (bundles read {n})')
        for c, ref, source, tex, x, key in bad[:40]:
            w(f'  mip<1\t{c}\t{ref}\t{source}\t{tex}\t{x[0]}x{x[1]} {x[2]} mips {x[3]}')
        if a.record_guard:
            added = 0
            with open(gl, 'a', encoding='utf-8', newline='\n') as fh:
                for c, ref, source, tex, x, key in bad:
                    if key is None or (source.lower(), tex) in guard: continue
                    guard.add((source.lower(), tex))
                    fh.write('\t'.join([source, tex, tex, f'{x[0]}x{x[1]}', x[2], str(x[3]), 'thumbnail: m_MipCount < 1 (maker_hair_census --thumbs)', key]) + '\n')
                    added += 1
            w(f'recorded in Cache/texguard/bad_textures.tsv: {added} texture(s)')

    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8') as fh: fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
