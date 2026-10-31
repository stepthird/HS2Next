# Maker clothes census (Phase 4.4): data facts the clothes pages and Sideloader's per-asset
# redirect need, read-only.
#   1. zipmods as Sideloader 21.1.4 loads them (behaviour only): one zipmod per manifest GUID (case-sensitive GUID; the
#      highest manifest version, then the longer file name; without versions the newest file), GUIDs in the catalog's load
#      order (lower-case ordinal, StudioCsv.LoadOrder), zipmods whose manifest names games without HS2 skipped
#      (hs2 / honeyselect2 / honey select 2); compared with HS2Next's catalog (the last file of the scan per GUID, case-insensitive,
#      no game filter);
#   2. the redirect table: every bundle path (zip entry minus its first folder, exact case) that a loaded zipmod ships and
#      either the game's abdata also has or a second loaded zipmod ships; per asset name of the copies the copy Sideloader
#      serves (the first zipmod copy whose AssetBundle container holds the name; else the abdata file; else nothing), the
#      internal CAB file names (Unity refuses two bundles with the same CAB file loaded at once; HS2's resource redirector
#      randomizes a clashing CAB) and, for the overridden prefabs of abdata paths, what the copies change (type trees:
#      GameObject activity, renderers, materials, MonoBehaviour fields without references, mesh vertex counts);
#   3. the list rows (character lists, Studio items / maps) whose assets HS2Next's catalog reads from another copy than
#      Sideloader serves (vanilla rows: abdata; mod rows: the row's own zipmod, else the first shipping zipmod), per category
#      and column, and the user's cards that reference such rows;
#   4. clothes lists: vanilla rows (gate open as C9, Possess 99 hidden) and zipmod rows of 240..247 (female), 140 / 141 / 144 /
#      147 (male) and the patterns 348: counts, "none" rows, the columns the clothes unit and the masks read (Coordinate,
#      NotBra, StateType, BreakDisableMask, KokanHide, mask columns, MainTex02 / 03, MainData02), rows without a thumbnail;
#   5. clothes prefabs: the CmpClothes component of every listed row's MainData (MainData02 for male rows that set it), read
#      from the copy Sideloader serves: useColorN0x / useColorA0x, useBreak, option parts, rendNormal groups and their material
#      slots / shaders / `_AlphaEx`, def / half objects, the prefab defaults (defMainColor0x alpha, defPtnIndex0x,
#      defLayout0x, defRotation0x, uvScalePattern, the fourth colour), DynamicBones (skirt roots) and DynamicBoneColliders
#      inside the prefab, the mask reference renderers (o_bra_a, o_shorts_a, o_panst_a), the MainTex sizes (compositing RTs);
#   6. patterns (348): MainTexAB / MainTex texture headers (size, format, mips, wrap U / V), loose PNGs;
#   7. the user's cards (UserData/chara/female, male) and coordinates (UserData/coordinate/female, male): per kind items
#      vanilla / mod / missing, the colour sets (alpha < 1 with and without the prefab's useColorA0x, patterns, out-of-range
#      layout / rotation / gloss / metallic, sets equal to the prefab defaults), breakRate, hideOpt, one-piece tops with a
#      stored bottom, male kinds 2 / 3 / 5 / 6, clothes plugin data (KCOX, MaterialEditor Clothing items, pushUp,
#      OutfitPainter), the coordinates' authored sex;
#   8. accessories worn on the user's cards: DynamicBoneColliders inside their prefabs (the collider snapshot rule of an
#      item swap);
#   9. --thumbs: thumbnails of the clothes and pattern lists whose Texture2D header has m_MipCount < 1 (--record-guard
#      appends them to <cache>/texguard/bad_textures.tsv; then run Tools/texguard_repair.py).
# No image is decoded (list TextAssets, MonoBehaviour / Transform / Renderer / Material / Mesh type trees, Texture2D headers,
# AssetBundle containers, card data blocks only). Clothes include underwear: names and numbers only.
#
#   python -I Tools/survey/maker_clothes_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv>
#          --studiocache <Cache/studiolists-*.tsv> --out <dir> [--no-prefabs | --prefab-limit N] [--no-games]
#          [--thumbs [--record-guard]]
# Writes <out>/report.txt; the committed copy is Tools/survey/maker_clothes_census.txt.
# Section 10 of the committed text (Step 0 of 4.4: the CAB table of every redirectable path with zipmod-zipmod copies,
# m_Externals, the uncensor bodies' cm_o_dan_f, the game-filtered zipmods' references) is the output of the sibling
# Tools/survey/redirect_step0_census.py, appended (it reuses this file's readers). Since Step 0, cab_names counts the
# serialized files only: a bundle's .resS can carry another CAB-* name (the HairMod copies keep the abdata file's), and
# the 7036f9a run's "15 of 18" counted those resource-file names.
import sys, os, re, io, gc, zipfile, struct, argparse, collections, functools, time

import UnityPy, msgpack

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
from maker_core_census import vanilla_lists                                  # noqa: E402
from maker_hair_census import (BundleView, pid, tt, material_info, renderer_materials, tex_header, col, vec,  # noqa: E402
                               unpack_any, plugin_data, empty, f)
from shadowed_bundle_census import studio_rows, container_names, area        # noqa: E402
from hs2card import load_card, resolver_info, _str                            # noqa: E402

VANILLA = 'illusion.hs2'
KINDS = ('top', 'bottom', 'innerTop', 'innerBottom', 'gloves', 'pantyhose', 'socks', 'shoes')
FEMALE = (240, 241, 242, 243, 244, 245, 246, 247)
MALE = (140, 141, 242, 243, 144, 245, 246, 147)      # males use the female inner / pantyhose / socks lists
MALE_PAGE = (0, 1, 4, 7)                              # the male Maker shows these four kinds
GAME_NAMES = ('hs2', 'honeyselect2', 'honey select 2')
ME_KEY = 'com.deathweasel.bepinex.materialeditor'
ME_LISTS = ('RendererPropertyList', 'MaterialFloatPropertyList', 'MaterialColorPropertyList', 'MaterialTexturePropertyList',
            'MaterialShaderList', 'MaterialCopyList', 'MaterialKeywordPropertyList', 'MaterialVectorPropertyList')
PLUGINS = {'KCOX': 'KCOX', 'pushUp': 'mikke.pushUpAI', 'OutfitPainter': 'orange.spork.outfitpainter',
           'DynamicBoneEditor': 'com.deathweasel.bepinex.dynamicboneeditor', 'AAC': 'orange.spork.additionalaccessorycontrolsplugin'}
# bundle column -> asset columns read from it (character lists; the clothes / masks / hair / face / body / accessory units)
PAIRS = {
    'MainAB': ('MainData', 'MainData02', 'MainTex', 'MainTex02', 'MainTex03', 'ColorMaskTex', 'ColorMask02Tex', 'ColorMask03Tex',
               'AddTex', 'GlossTex', 'NormalMapTex', 'OcclusionMapTex', 'MatData', 'ShapeAnime', 'Preset'),
    'MainTexAB': ('MainTex',), 'ThumbAB': ('ThumbTex',), 'TexAB': ('TexD', 'TexC'),
    'OverBodyMaskAB': ('OverBodyMask',), 'OverBraMaskAB': ('OverBraMask',), 'OverBodyBMaskAB': ('OverBodyBMask',),
    'OverInnerTBMaskAB': ('OverInnerTBMask',), 'OverInnerBMaskAB': ('OverInnerBMask',), 'OverPanstMaskAB': ('OverPanstMask',),
}
MASK_COLS = ('OverBodyMask', 'OverBraMask', 'OverBodyBMask', 'OverInnerTBMask', 'OverInnerBMask', 'OverPanstMask')
REF_RENDERERS = {2: ('o_bra_a', 'o_shorts_a'), 3: ('o_shorts_a',), 5: ('o_panst_a',)}   # the masks unit's reference objects


def utf8():
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')


def low_ord(s): return (s or '').lower()   # StudioCsv.LoadOrder: string.CompareOrdinal of the lower-invariant strings


def version_tokens(v):
    """Sideloader's manifest version rule (behaviour): leading v / V / r / R / blanks trimmed, empty = "0"; parts split on
    . blank - , _ ; each part split into runs of digits and non-digits; digit runs are numbers."""
    v = (v or '').strip().lstrip('vVrR ')
    if not v: v = '0'
    out = []
    for part in re.split(r'[. \-,_]', v.strip()):
        if not part: out.append(0); continue
        for run in re.findall(r'\d+|\D+', part):
            out.append(int(run) if run.isdigit() else run)
    return v, out


def cmp_versions(a, b):
    a, ta = version_tokens(a)
    b, tb = version_tokens(b)
    if a == b: return 0
    for i in range(max(len(ta), len(tb))):
        x = ta[i] if i < len(ta) else 0
        y = tb[i] if i < len(tb) else 0
        if type(x) is type(y):
            if x != y: return -1 if x < y else 1
            continue
        sx, sy = str(x), str(y)
        if sx == '0' and sy != '0': return -1
        if sy == '0' and sx != '0': return 1
        if sx != sy: return -1 if sx < sy else 1
    return 0


def norm(p): return (p or '').replace('\\', '/')


def rss_mb():
    """Resident memory of this process in MB (psutil when present; the census watches it on large bundles)."""
    try:
        import psutil
        return int(psutil.Process().memory_info().rss / 2**20)
    except Exception:
        return -1


# ---------------------------------------------------------------- zipmods
def read_modcache(modcache):
    """Zipmods in scan order: {path, stamp ticks, guid, version, files (abdata-relative), rows [(cat, id, {col: val})],
    items [(no, bundle, asset)] (the Studio item-list CSV rows: ID, BigCategory, MidCategory, Name, Manifest, Bundle, Object)}."""
    zs, cur = [], None
    csv_item, skip = False, 0
    with open(modcache, encoding='utf-8') as fh:
        head = fh.readline()
        if not head.startswith('#hs2next-modlists'): raise SystemExit('not a zipmod scan cache: ' + modcache)
        for line in fh:
            p = line.rstrip('\n').split('\t')
            if p[0] == 'Z':
                st = p[2] if len(p) > 2 else ''
                ticks = int(st.split(':')[1]) if ':' in st and st.split(':')[1].isdigit() else 0
                cur = {'path': p[1], 'ticks': ticks, 'guid': p[3] if len(p) > 3 and p[3] else None, 'version': '', 'files': [],
                       'rows': [], 'items': []}
                zs.append(cur)
                csv_item = False
            elif cur is None: continue
            elif p[0] == 'N':
                v = p[4] if len(p) > 4 else ''
                cur['version'] = '' if v == '\x01' else v
            elif p[0] == 'B': cur['files'].append(norm(p[1]))
            elif p[0] == 'E' and len(p) >= 3:
                cur['rows'].append((int(p[1]), int(p[2]), dict(zip(p[3::2], p[4::2]))))
            elif p[0] == 'S':
                csv_item = 'itemlist' in os.path.basename(p[1]).lower()
                skip = int(p[2]) if len(p) > 2 and p[2].isdigit() else 1
            elif p[0] == 'C' and csv_item:
                if skip > 0: skip -= 1; continue
                c = [x.replace('\x1f', '\t') for x in p[1:]]
                if len(c) >= 7 and c[5] and c[6]: cur['items'].append((c[0], norm(c[5]), c[6]))
    return zs


def manifest_games(zpath, cache={}):
    if zpath not in cache:
        games = []
        try:
            with zipfile.ZipFile(zpath) as zf:
                m = zf.read('manifest.xml').decode('utf-8', 'replace')
            games = [g.strip().lower() for g in re.findall(r'<game>(.*?)</game>', m, re.S) if g.strip()]   # empty values dropped (Sideloader)
        except Exception:
            games = None
        cache[zpath] = games
    return cache[zpath]


def loads_on_hs2(zpath):
    g = manifest_games(zpath)
    return not g or any(x in GAME_NAMES for x in g)


def select_zipmods(zs, games=True):
    """(Sideloader's loaded zipmods in load order, HS2Next's ZipmodsByGuid choice per lower GUID, skipped by the game filter)."""
    groups = collections.OrderedDict()
    skipped = []
    for z in zs:
        if not z['guid']: continue
        if games and not loads_on_hs2(z['path']):
            skipped.append(z)
            continue
        groups.setdefault(z['guid'], []).append(z)
    chosen = []
    for g, lst in groups.items():
        if len(lst) > 1:
            if all(z['version'] for z in lst):
                lst = sorted(lst, key=functools.cmp_to_key(lambda x, y: cmp_versions(x['version'], y['version']) or (len(os.path.basename(x['path'])) - len(os.path.basename(y['path'])))), reverse=True)
            else:
                lst = sorted(lst, key=lambda z: z['ticks'], reverse=True)
        chosen.append(lst[0])
    chosen.sort(key=lambda z: low_ord(z['guid']))
    hs2next = {}
    for z in zs:                                   # ZipmodsByGuid: last file of the scan, case-insensitive GUID, no game filter
        if z['guid']: hs2next[z['guid'].lower()] = z
    return chosen, hs2next, skipped, groups


# ---------------------------------------------------------------- bundle sources
class Source:
    """Bundle bytes and AssetBundle container names per (zip path or None for abdata, trimmed path); cached."""
    def __init__(self, hs2):
        self.hs2 = hs2
        self.names_cache = {}
        self.entries = {}
        self.views = collections.OrderedDict()

    def zip_entry(self, zpath, trimmed):
        if zpath not in self.entries:
            d, dl = {}, {}
            try:
                with zipfile.ZipFile(zpath) as zf:
                    for e in zf.namelist():
                        t = e.replace('\\', '/')
                        if '/' in t and t.lower().endswith('.unity3d'):
                            d.setdefault(t[t.index('/') + 1:], e)
                            dl.setdefault(t[t.index('/') + 1:].lower(), e)
            except Exception:
                pass
            self.entries[zpath] = (d, dl)
        d, dl = self.entries[zpath]
        return d.get(trimmed) or dl.get(trimmed.lower())   # exact (Sideloader) first, else as HS2Next's catalog (any case)

    def data(self, zpath, trimmed):
        if zpath is None:
            p = os.path.join(self.hs2, 'abdata', trimmed)
            if not os.path.isfile(p): return None
            with open(p, 'rb') as fh: return fh.read()
        e = self.zip_entry(zpath, trimmed)
        if e is None: return None
        try:
            with zipfile.ZipFile(zpath) as zf: return zf.read(e)
        except Exception:
            return None

    def names(self, zpath, trimmed):
        k = (zpath, trimmed)
        if k not in self.names_cache:
            d = self.data(zpath, trimmed)
            n = None
            if d is not None:
                try: n = container_names(UnityPy.load(d))
                except Exception: n = None
            self.names_cache[k] = n
        return self.names_cache[k]

    def view(self, zpath, trimmed):
        if trimmed in getattr(self, 'skip', ()): return None
        k = (zpath, trimmed)
        if k in self.views:
            self.views.move_to_end(k)
            return self.views[k]
        d = self.data(zpath, trimmed)
        v = None
        if d is not None:
            try: v = BundleView(d)
            except Exception: v = None
        self.views[k] = v
        evicted = False
        while len(self.views) > 2:   # rows are visited grouped by bundle: a small cache keeps the memory low (UnityPy objects)
            self.views.popitem(last=False)
            evicted = True
        if evicted:
            self.evictions = getattr(self, 'evictions', 0) + 1
            if self.evictions % 20 == 0 or rss_mb() > 3000: gc.collect()
        return v


COMPRESSION = {0: 'none', 1: 'LZMA', 2: 'LZ4', 3: 'LZ4HC'}


def bundle_compression(data):
    """UnityFS block-info compression from the header flags (none / LZMA / LZ4 / LZ4HC), or the signature for other formats.
    A managed reader (Hs2BundleFile) reads none / LZ4 / LZ4HC without Unity; LZMA needs Unity's loader."""
    if not data or len(data) < 64: return '?'
    if not data.startswith(b'UnityFS\x00'): return data[:8].split(b'\x00')[0].decode('ascii', 'replace')
    p = 8 + 4
    for _ in range(2):
        e = data.index(b'\x00', p)
        p = e + 1
    p += 8 + 4 + 4
    flags = struct.unpack('>I', data[p:p + 4])[0]
    return COMPRESSION.get(flags & 0x3F, str(flags & 0x3F))


def cab_names(data):
    """CAB-* names of a bundle's serialized files (what Unity's 'another AssetBundle with the same files' check is
    documented for); resource files (.resS / .resource) are left out: their CAB-* name can differ from the serialized
    file's (Step 0, redirect_step0_census.py)."""
    try:
        env = UnityPy.load(data)
    except Exception:
        return None
    out = set()
    for bf in env.files.values():
        for n, sf in (getattr(bf, 'files', {}) or {}).items():
            if str(n).startswith('CAB-') and hasattr(sf, 'objects'): out.add(str(n))
    return out


# ---------------------------------------------------------------- prefab summaries (type trees, names and numbers)
def roots_of(bv, name):
    """Root game objects named `name` (exact; else case-insensitive), from an index built once per bundle view (a bundle
    holding many prefabs is asked once per row)."""
    idx = getattr(bv, '_roots_idx', None)
    if idx is None:
        idx = collections.defaultdict(list)
        fathers = bv.fathers()
        for t, fa in fathers.items():
            if fa != 0 or pid((bv.get(t) or {}).get('m_Father'))[0] != 0: continue
            tt_ = bv.get(t) or {}
            gid = pid(tt_.get('m_GameObject'))[1]
            g = bv.get(gid) if gid else None
            if g and g.get('m_Name') is not None: idx[g['m_Name']].append((gid, g, t))
        bv._roots_idx = idx
        bv._roots_lower = {k.lower(): v for k, v in idx.items()}
        bv._children = None
    return idx.get(name) or bv._roots_lower.get((name or '').lower()) or []


def children_of(bv):
    """Children per transform from the transforms' m_Father, same serialized file only (a father in another file of the
    bundle would otherwise alias a local path id and can make a cycle)."""
    if getattr(bv, '_children', None) is None:
        ch = collections.defaultdict(list)
        for o in bv.env.objects:
            if o.type.name not in ('Transform', 'RectTransform'): continue
            t = bv.get(o.path_id)
            if not t: continue
            fid, fa = pid(t.get('m_Father'))
            if fid == 0: ch[fa].append(o.path_id)
        bv._children = ch
    return bv._children


def go_tree(bv, name):
    """[(path, game object dict, transform id)] under the root `name` (depth first)."""
    roots = roots_of(bv, name)
    if not roots: return None
    gpid, g, tr = roots[0]
    ch = children_of(bv)
    out = []
    stack = [(tr, g.get('m_Name'))]
    seen = set()
    while stack:
        t, path = stack.pop()
        if t in seen or len(out) > 100000: continue   # a malformed hierarchy (cycle) must not run away
        seen.add(t)
        tt_ = bv.get(t) or {}
        gid = pid(tt_.get('m_GameObject'))[1]
        go = bv.get(gid) or {}
        out.append((path, go, t))
        for c in ch.get(t, []):
            ct = bv.get(c) or {}
            cg = bv.get(pid(ct.get('m_GameObject'))[1]) or {}
            stack.append((c, path + '/' + str(cg.get('m_Name'))))
    return out


def strip_refs(x):
    if isinstance(x, dict):
        if set(x.keys()) <= {'m_FileID', 'm_PathID'}: return 'ref' if x.get('m_PathID') else 'null'
        return {k: strip_refs(v) for k, v in x.items()}
    if isinstance(x, list): return [strip_refs(v) for v in x]
    if isinstance(x, float): return round(x, 6)
    return x


def mesh_vertices(bv, mptr):
    fid, p = pid(mptr)
    if p == 0 or fid != 0: return None
    m = bv.get(p)
    if not m: return None
    vd = m.get('m_VertexData') or {}
    return vd.get('m_VertexCount')


def prefab_summary(bv, name):
    """{path: facts} of a prefab: GameObject activity, renderers (type, enabled, mesh vertices, materials with shader /
    texture slots present / floats), MonoBehaviour fields (references as ref / null). For copy diffs."""
    tree = go_tree(bv, name)
    if tree is None: return None
    out = {}
    for path, go, t in tree:
        out[path + ' :active'] = bool(go.get('m_IsActive', 1))
        mb = 0
        for cp in bv.components(go):
            ty = bv.type_of(cp)
            d = bv.get(cp) or {}
            if ty in ('SkinnedMeshRenderer', 'MeshRenderer'):
                rec = {'type': ty, 'enabled': bool(d.get('m_Enabled', 1)), 'vertices': mesh_vertices(bv, d.get('m_Mesh')) if ty == 'SkinnedMeshRenderer' else None,
                       'bones': len(d.get('m_Bones') or [])}
                mats = []
                for mp in d.get('m_Materials') or []:
                    mi = material_info(bv, mp)
                    mats.append(None if mi is None else ('external' if mi.get('external') else
                                (mi['name'], mi['shader'], tuple(sorted(k for k, v in mi['tex'].items() if v)),
                                 tuple(sorted(k for k, v in mi['tex'].items() if not v)))))
                rec['materials'] = mats
                out[path + ' :' + ty] = rec
            elif ty == 'MonoBehaviour':
                out[path + ' :MB%d' % mb] = strip_refs({k: v for k, v in d.items() if k not in ('m_GameObject', 'm_Script')})
                mb += 1
            elif ty == 'MeshFilter':
                out[path + ' :MeshFilter'] = mesh_vertices(bv, d.get('m_Mesh'))
    return out


def diff_summaries(a, b):
    lines = []
    for k in sorted(set(a) | set(b)):
        if k not in a: lines.append(('only in the zipmod copy', k, b[k])); continue
        if k not in b: lines.append(('only in abdata', k, a[k])); continue
        if a[k] != b[k]:
            if isinstance(a[k], dict) and isinstance(b[k], dict):
                keys = [x for x in sorted(set(a[k]) | set(b[k])) if a[k].get(x) != b[k].get(x)]
                lines.append(('differs', k, {x: (a[k].get(x), b[k].get(x)) for x in keys}))
            else:
                lines.append(('differs', k, (a[k], b[k])))
    return lines


def short(v, n=220):
    s = repr(v)
    return s if len(s) <= n else s[:n] + '…'


def cmp_clothes(bv, name):
    """CmpClothes facts of the root `name`, or {'status': …}."""
    roots = roots_of(bv, name)
    if not roots: return {'status': 'no prefab'}
    gpid, g, tr = roots[0]
    cmp = None
    for cp in bv.components(g):
        if bv.type_of(cp) != 'MonoBehaviour': continue
        t = bv.get(cp)
        if t and 'rendNormal01' in t and 'useBreak' in t: cmp = t; break
    if cmp is None: return {'status': 'no CmpClothes'}
    info = {'status': 'ok'}
    for k in ('useBreak', 'useColorN01', 'useColorN02', 'useColorN03', 'useColorA01', 'useColorA02', 'useColorA03'):
        info[k] = bool(cmp.get(k))
    for k in ('objTopDef', 'objTopHalf', 'objBotDef', 'objBotHalf'):
        info[k] = pid(cmp.get(k))[1] != 0
    for k in ('objOpt01', 'objOpt02'):
        info[k] = sum(1 for x in (cmp.get(k) or []) if pid(x)[1] != 0)
    groups = []
    for k in ('rendNormal01', 'rendNormal02', 'rendNormal03'):
        rs = [x for x in (cmp.get(k) or []) if pid(x)[1] != 0]
        slots, shaders, alphaex = 0, collections.Counter(), 0
        for x in rs:
            for mp in renderer_materials(bv, x) or []:
                mi = material_info(bv, mp)
                if not mi or mi.get('external'): continue
                slots += 1
                shaders[mi['shader']] += 1
                if '_AlphaEx' in mi['floats']: alphaex += 1
        groups.append({'renderers': len(rs), 'slots': slots, 'shaders': shaders, 'alphaEx': alphaex})
    info['groups'] = groups
    info['uvScalePattern'] = vec(cmp.get('uvScalePattern'), 4)
    for i in (1, 2, 3, 4):
        info[f'defMainColor0{i}'] = col(cmp.get(f'defMainColor0{i}'))
    for i in (1, 2, 3):
        info[f'defPtnIndex0{i}'] = cmp.get(f'defPtnIndex0{i}')
        info[f'defPatternColor0{i}'] = col(cmp.get(f'defPatternColor0{i}'))
        info[f'defGloss0{i}'] = cmp.get(f'defGloss0{i}')
        info[f'defMetallic0{i}'] = cmp.get(f'defMetallic0{i}')
        info[f'defLayout0{i}'] = vec(cmp.get(f'defLayout0{i}'), 4)
        info[f'defRotation0{i}'] = cmp.get(f'defRotation0{i}')
    # dynamic bones and colliders in the prefab, mask reference renderers, the prefab's own cf_J_Root copy
    tree = go_tree(bv, name) or []
    dbs, cols, cols_in_root, refs, has_root = [], 0, 0, set(), False
    for path, go, t in tree:
        nm = go.get('m_Name')
        if nm == 'cf_J_Root': has_root = True
        if nm in ('o_bra_a', 'o_shorts_a', 'o_panst_a'): refs.add(nm)
        for cp in bv.components(go):
            if bv.type_of(cp) != 'MonoBehaviour': continue
            d = bv.get(cp) or {}
            if 'm_Root' in d and 'm_Damping' in d:
                rt = bv.get(pid(d.get('m_Root'))[1]) if pid(d.get('m_Root'))[0] == 0 else None
                rg = bv.get(pid(rt.get('m_GameObject'))[1]) if rt else None
                dbs.append(rg.get('m_Name') if rg else '?')
            elif 'm_Direction' in d and 'm_Radius' in d and 'm_Height' in d:
                cols += 1
                if '/cf_J_Root' in path: cols_in_root += 1
    info['dynamicBones'] = dbs
    info['colliders'] = cols
    info['collidersInOwnRoot'] = cols_in_root
    info['refRenderers'] = refs
    info['ownRoot'] = has_root
    return info


# ---------------------------------------------------------------- coordinate cards
def load_coordinate(path):
    """(name, clothes, accessory, ext) of an HS2 coordinate card, or None (AIS_Clothes marker; ESF trailer KKEx)."""
    data = open(path, 'rb').read()
    i = data.find(b'IEND')
    fh = io.BytesIO(data[i + 8:] if i >= 0 else data)
    try:
        struct.unpack('<i', fh.read(4))
        marker = _str(fh)
        if 'AIS_Clothes' not in marker: return None
        _str(fh)
        struct.unpack('<i', fh.read(4))
        name = _str(fh)
        n = struct.unpack('<i', fh.read(4))[0]
        blob = io.BytesIO(fh.read(n))
        cl = msgpack.unpackb(blob.read(struct.unpack('<i', blob.read(4))[0]), raw=False, strict_map_key=False)
        ac = msgpack.unpackb(blob.read(struct.unpack('<i', blob.read(4))[0]), raw=False, strict_map_key=False)
    except Exception:
        return None
    ext = {}
    try:
        if _str(fh) == 'KKEx':
            struct.unpack('<i', fh.read(4))
            ln = struct.unpack('<i', fh.read(4))[0]
            ext = msgpack.unpackb(fh.read(ln), raw=False, strict_map_key=False) or {}
    except Exception:
        ext = {}
    return name, cl, ac, ext


def resolve_entries(ext):
    r = (ext or {}).get('com.bepis.sideloader.universalautoresolver')
    if not r or not isinstance(r, (list, tuple)) or len(r) < 2 or not isinstance(r[1], dict) or not r[1].get('info'): return []
    out = []
    for x in r[1]['info']:
        try: out.append(msgpack.unpackb(x, raw=False, strict_map_key=False) if isinstance(x, (bytes, bytearray)) else x)
        except Exception: pass
    return out


# ---------------------------------------------------------------- main
def main():
    utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--studiocache', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--no-prefabs', action='store_true', help='skip section 5 (the clothes prefabs; minutes)')
    ap.add_argument('--prefab-limit', type=int, default=0, help='read at most N prefab bundles (a quick check)')
    ap.add_argument('--no-games', action='store_true', help='do not open the manifests (no game filter; seconds instead of a minute)')
    ap.add_argument('--skip-bundle', action='append', default=None,
                    help='bundle path not read in section 5 (repeatable); default: chara/GDCLets/Minamotobra.unity3d, a 38 MB '
                         'mod bundle UnityPy expands to more than 10 GB on the development machine')
    ap.add_argument('--thumbs', action='store_true', help='section 9: thumbnail headers of the clothes / pattern lists (m_MipCount < 1)')
    ap.add_argument('--record-guard', action='store_true', help='with --thumbs: append the found textures to <cache>/texguard/bad_textures.tsv')
    a = ap.parse_args()
    skip_bundles = set(a.skip_bundle if a.skip_bundle is not None else ['chara/GDCLets/Minamotobra.unity3d'])
    os.makedirs(a.out, exist_ok=True)
    t0 = time.time()
    lines = []
    w = lines.append
    src = Source(a.hs2)
    src.skip = skip_bundles

    # ------------------------------------------------------------ 1. zipmods
    zs = read_modcache(a.modcache)
    loaded, hs2next, skipped, groups = select_zipmods(zs, games=not a.no_games)
    w('# 1. zipmods as Sideloader loads them (one per GUID, highest version, game filter) vs HS2Next\'s catalog')
    multi = {g: l for g, l in groups.items() if len(l) > 1}
    differ = 0
    for g, l in multi.items():
        sl = [z for z in loaded if z['guid'] == g]
        hn = hs2next.get(g.lower())
        if sl and hn is not sl[0]: differ += 1
    ci = collections.Counter(g.lower() for g in groups)
    w(f'zipmod files with a GUID: {sum(1 for z in zs if z["guid"])}; GUIDs: {len(groups)}; loaded by Sideloader: {len(loaded)}; '
      f'GUIDs with several files: {len(multi)} (HS2Next takes another file than Sideloader for {differ}); GUIDs differing only '
      f'in case: {sum(1 for v in ci.values() if v > 1)}')
    if a.no_games:
        w('game filter: not evaluated (--no-games)')
    else:
        rows_sk = sum(len(z['rows']) for z in skipped)
        paths_sk = sum(len(z['files']) for z in skipped)
        w(f'game filter: {len(skipped)} zipmod(s) name games without HS2 (skipped by Sideloader, loaded by HS2Next\'s catalog): '
          f'{rows_sk} character-list rows, {paths_sk} files; ' + '; '.join(f'{os.path.basename(z["path"])} {manifest_games(z["path"])} rows {len(z["rows"])}' for z in skipped[:12]))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 2. the redirect table', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 2. the redirect table
    abd = {}
    root = os.path.join(a.hs2, 'abdata')
    for d, _, fs in os.walk(root):
        for fn in fs:
            if fn.lower().endswith('.unity3d'):
                rel = norm(os.path.relpath(os.path.join(d, fn), root))
                abd[rel] = True
    abd_lower = {k.lower(): k for k in abd}
    ship = collections.defaultdict(list)                  # exact trimmed path -> zipmods in load order
    for z in loaded:
        for fl in z['files']:
            if fl.lower().endswith('.unity3d'): ship[fl].append(z)
    shadowed = sorted(p for p in ship if p in abd)
    shadow_ci = sorted(p for p in ship if p not in abd and p.lower() in abd_lower)
    multipath = sorted(p for p in ship if len(ship[p]) > 1)
    w('')
    w('# 2. the redirect table (Sideloader: per asset name, the first zipmod copy of the exact trimmed path that holds it; else abdata)')
    w(f'abdata bundles: {len(abd)}; zipmod bundle paths: {len(ship)}; abdata paths a zipmod ships (exact case): {len(shadowed)}; '
      f'same path in another case only (not redirected by Sideloader): {len(shadow_ci)}; paths shipped by more than one loaded zipmod: {len(multipath)}')
    per_area = collections.Counter(area(p) for p in shadowed)
    w('shadowed abdata paths per area: ' + ', '.join(f'{k} {v}' for k, v in sorted(per_area.items())))
    per_area_m = collections.Counter(area(p) for p in multipath)
    w('multi-zipmod paths per area: ' + ', '.join(f'{k} {v}' for k, v in sorted(per_area_m.items())))
    serve = {}                                            # (path, asset lower) -> ('zip', z) | ('abdata', None) | ('none', None)
    path_names = {}
    w('path\tarea\tabdata\tcopies (load order)\tasset names: abdata / served by a zipmod copy / by a later copy than the first / not in the first copy\tCAB clash abdata-zipmod\tcompression of the copies')
    cab_clash = 0
    comp = collections.Counter()
    for p in sorted(set(shadowed) | set(multipath)):
        copies = ship[p]
        an = src.names(None, p) if p in abd else None
        cn = [(z, src.names(z['path'], p)) for z in copies]
        allnames = set(an or ())
        for _, s in cn: allnames |= set(s or ())
        by_zip = by_later = 0
        for n in allnames:
            hit = None
            for i, (z, s) in enumerate(cn):
                if s and n in s: hit = (i, z); break
            if hit:
                serve[(p, n)] = ('zip', hit[1])
                by_zip += 1
                if hit[0] > 0: by_later += 1
            elif an and n in an: serve[(p, n)] = ('abdata', None)
            else: serve[(p, n)] = ('none', None)
        path_names[p] = (an, cn)
        clash = ''
        if p in abd:
            ca = cab_names(src.data(None, p) or b'')
            cz = cab_names(src.data(copies[0]['path'], p) or b'')
            if ca and cz and ca & cz:
                clash = 'yes'
                cab_clash += 1
            else:
                clash = 'no'
        nfirst = sum(1 for n in allnames if not (cn[0][1] and n in cn[0][1]))
        cs = [bundle_compression(src.data(z['path'], p)) for z in copies]
        comp.update(cs)
        w(f'{p}\t{area(p)}\t{len(an) if an is not None else "-"}\t{" | ".join(os.path.basename(z["path"]) for z in copies)}\t'
          f'{len(an or ())} / {by_zip} / {by_later} / {nfirst}\t{clash}\t{" | ".join(cs)}')
    w(f'abdata paths whose first zipmod copy has the same CAB file name as the abdata file: {cab_clash} of {len(shadowed)}')
    w('compression of the zipmod copies above (UnityFS block info): ' + ', '.join(f'{k} {v}' for k, v in comp.most_common()))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: what the copies change', file=sys.stderr, flush=True)
    # what the copies change (abdata paths: every overridden asset that is a root GameObject; Texture2D headers)
    w('')
    w('## 2.1 overridden assets of abdata paths: what the zipmod copy changes (type trees; names and numbers)')
    for p in shadowed:
        an, cn = path_names[p]
        over = sorted(n for n in (an or ()) if serve.get((p, n), ('',))[0] == 'zip')
        if not over: continue
        z0 = serve[(p, over[0])][1]
        va = src.view(None, p)
        vz = src.view(z0['path'], p)
        if va is None or vz is None:
            w(f'{p}: unreadable copy'); continue
        same = 0
        for n in over:
            z = serve[(p, n)][1]
            vz = src.view(z['path'], p)
            sa = prefab_summary(va, n_case(va, n))
            sb = prefab_summary(vz, n_case(vz, n))
            if sa is None or sb is None:
                ha = tex_header(va.env, {n_tex(va, n)}) if n_tex(va, n) else {}
                hb = tex_header(vz.env, {n_tex(vz, n)}) if n_tex(vz, n) else {}
                if ha or hb:
                    if list(ha.values()) == list(hb.values()): same += 1
                    else: w(f'  {p} {n}: Texture2D {list(ha.values())} -> {list(hb.values())}')
                else:
                    w(f'  {p} {n}: not a prefab / texture (kept by name)')
                continue
            d = diff_summaries(sa, sb)
            if not d: same += 1; continue
            w(f'  {p} {n} ({os.path.basename(z["path"])}): {len(d)} difference(s)')
            for kind, k, v in d[:14]: w(f'    {kind}: {k} {short(v)}')
            if len(d) > 14: w(f'    … {len(d) - 14} more')
        if same: w(f'  {p}: {same} overridden asset(s) identical in the compared facts')

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 3. rows whose assets HS2Next reads from another copy', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 3. rows whose assets HS2Next reads from another copy
    w('')
    w('# 3. list rows whose assets the catalog reads from another copy than Sideloader serves')
    van = {}
    for fn, asset, cat, dist, keys, rows in vanilla_lists(a.hs2):
        for rid, vals in rows.items():
            try: rid = int(rid)
            except Exception: continue
            if (cat, rid) not in van: van[(cat, rid)] = {keys[i]: str(vals[i]) for i in range(min(len(keys), len(vals)))}
    # ContentCatalog.FindZipmodShipping walks ZipmodsByGuid.Values: dictionary order = the first scan position of each GUID
    first_ship_hn = {}
    order_hn = []
    seen_g = set()
    for z in zs:
        if z['guid'] and z['guid'].lower() not in seen_g:
            seen_g.add(z['guid'].lower())
            order_hn.append(hs2next[z['guid'].lower()])
    for z in order_hn:
        z['files_l'] = {fl.lower() for fl in z['files']}
        for fl in z['files']: first_ship_hn.setdefault(fl.lower(), z)
    for z in zs: z.setdefault('files_l', {fl.lower() for fl in z['files']})

    def hs2next_copy(z, path):
        """The copy HS2Next's catalog opens for a row (ContentCatalog.LoadBundle): the row's zipmod if it ships the path
        (case-insensitive), else for mod rows the first zipmod that ships it, else abdata (None)."""
        if z is not None:
            if path.lower() in z['files_l']: return z['path']
            o = first_ship_hn.get(path.lower())
            if o: return o['path']
        return None

    def sideloader_copy(path, asset):
        k = (path, (asset or '').lower())
        s = serve.get(k)
        if s is None:
            if path in ship and path not in abd:
                return ship[path][0]['path']   # single zipmod copy (or the first: the bundle value[0])
            return None                        # abdata (or missing)
        return s[1]['path'] if s[0] == 'zip' else None

    diffrows = collections.Counter()
    diffcols = collections.Counter()
    diffkeys = {}
    def check_row(cat, key, zrec, kv):
        hit = False
        for bc, acs in PAIRS.items():
            b = norm(kv.get(bc))
            if empty(b): continue
            if b not in ship and (zrec is None or b.lower() not in zrec['files_l']): continue
            for ac in acs:
                asset = kv.get(ac)
                if empty(asset): continue
                hn = hs2next_copy(zrec, b)
                sl = sideloader_copy(b, asset)
                if (hn or None) != (sl or None):
                    diffcols[(cat, ac)] += 1
                    hit = True
        if hit:
            diffrows[cat] += 1
            diffkeys[(cat, key)] = True
    for (cat, rid), kv in van.items():
        if str(kv.get('Possess', '0')) == '99': continue
        check_row(cat, f'{VANILLA}#{rid}', None, kv)
    loaded_ids = {id(z) for z in loaded}
    absent = collections.Counter()
    for z in hs2next.values():
        if z['guid'].lower() == VANILLA: continue
        if id(z) not in loaded_ids:
            absent['zipmods'] += 1
            absent['rows'] += len(z['rows'])
            continue
        seen = set()
        for c, i, kv in z['rows']:
            if (c, i) in seen: continue
            seen.add((c, i))
            check_row(c, f'{z["guid"]}#{i}', z, kv)
    w(f'HS2Next zipmod choices Sideloader does not load (game filter or another file of the GUID): {absent["zipmods"]} zipmods, '
      f'{absent["rows"]} character-list rows (rows HS2 does not list; not compared below)')
    w('rows per category (vanilla + mod): ' + ', '.join(f'{c} {n}' for c, n in sorted(diffrows.items())))
    w('columns: ' + ', '.join(f'{c}.{col_} {n}' for (c, col_), n in sorted(diffcols.items())))
    st_rows = studio_rows(a.studiocache)
    sc = collections.Counter()
    for kind, lst, no, bundle, asset in st_rows:
        b = norm(bundle)
        if b in abd and b in ship and sideloader_copy(b, asset) is not None: sc['vanilla ' + kind + ' rows whose asset a zipmod copy serves'] += 1
    # zipmod Studio item rows: HS2Next opens the real abdata file first, then the owner's copy, then the first shipping zipmod
    # (ContentCatalog.OpenAnywhere); Sideloader serves per asset name
    for z in loaded:
        for no, b, asset in z['items']:
            if b not in ship and b not in abd: continue
            if b in abd: hn = None
            elif b.lower() in z['files_l']: hn = z['path']
            else: hn = (first_ship_hn.get(b.lower()) or {}).get('path')
            sl = sideloader_copy(b, asset)
            sc['mod item rows on a redirectable path'] += 1 if (b in multipath or b in abd) else 0
            if (hn or None) != (sl or None): sc['mod item rows whose asset another copy serves'] += 1
    w('studio item rows: ' + (', '.join(f'{k} {v}' for k, v in sc.items() if v) or 'none on redirectable paths'))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 4. clothes lists', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 4. clothes lists
    w('')
    w('# 4. clothes lists (vanilla after C9 = gate open, Possess 99 hidden; mod rows = HS2Next\'s zipmod per GUID, first row of a key)')
    mod = {}
    for z in hs2next.values():
        if z['guid'].lower() == VANILLA: continue
        seen = set()
        for c, i, kv in z['rows']:
            if (c, i) in seen: continue
            seen.add((c, i))
            mod[(c, z['guid'], i)] = (z, kv)

    def rows_of(c):
        r = [(f'{VANILLA}#{i}', None, v) for (cc, i), v in sorted(van.items()) if cc == c and str(v.get('Possess', '0')) != '99']
        ms = sorted(((g, i, z, kv) for (cc, g, i), (z, kv) in mod.items() if cc == c), key=lambda t: (low_ord(t[0]), t[1]))
        return r + [(f'{g}#{i}', z, kv) for g, i, z, kv in ms]

    cats = sorted(set(FEMALE) | set(MALE)) + [348]
    w('category\tvanilla\tmod\tnone rows (MainData p_dummy)\tno thumbnail (vanilla / mod)\trow 0 name')
    for c in cats:
        rs = rows_of(c)
        v = [x for x in rs if x[1] is None]
        m = [x for x in rs if x[1] is not None]
        none = sum(1 for _, _, kv in rs if kv.get('MainData') == 'p_dummy')
        nth = lambda xs: sum(1 for _, _, kv in xs if empty(kv.get('ThumbAB')) or empty(kv.get('ThumbTex')))
        w(f'{c}\t{len(v)}\t{len(m)}\t{none}\t{nth(v)} / {nth(m)}\t{van.get((c, 0), {}).get("Name")}')
    for c in sorted(set(FEMALE) | set(MALE)):
        rs = rows_of(c)
        cnt = collections.Counter()
        for ref, z, kv in rs:
            if kv.get('MainData') == 'p_dummy': continue
            for k in ('Coordinate', 'NotBra', 'StateType', 'BreakDisableMask', 'KokanHide', 'OverBraType'):
                if k in kv: cnt[f'{k}={kv[k]}'] += 1
            for k in MASK_COLS:
                if not empty(kv.get(k)): cnt[f'{k} set'] += 1
            for k in ('MainTex02', 'MainTex03'):
                if not empty(kv.get(k)): cnt[f'{k} set'] += 1
            if empty(kv.get('MainTex')) or empty(kv.get('ColorMaskTex')): cnt['no MainTex / ColorMaskTex (no compositing)'] += 1
            if not empty(kv.get('MainData02')): cnt['MainData02 set'] += 1
        w(f'  {c}: ' + ', '.join(f'{k} {v}' for k, v in sorted(cnt.items())))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 5. clothes prefabs', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 5. clothes prefabs
    prefab = {}                                           # (cat, ref, sex variant) -> CmpClothes facts
    if not a.no_prefabs:
        w('')
        w('# 5. clothes prefabs (CmpClothes of MainData, MainData02 for male rows that set it; the copy Sideloader serves)')
        want = []
        for c in sorted(set(FEMALE) | set(MALE)):
            for ref, z, kv in rows_of(c):
                if kv.get('MainData') == 'p_dummy' or empty(kv.get('MainAB')): continue
                names = [('', kv.get('MainData'))]
                if c in (140, 141, 144, 147) and not empty(kv.get('MainData02')): names.append(('02', kv.get('MainData02')))
                for var, nm in names:
                    if empty(nm): continue
                    b = norm(kv['MainAB'])
                    # the copy Sideloader serves; a row of a zipmod Sideloader does not load reads its own copy
                    sl = sideloader_copy(b, nm) if b in ship else (z['path'] if z is not None and b.lower() in z['files_l'] else None)
                    if sl is None and b not in abd and z is not None and b.lower() in z['files_l']: sl = z['path']
                    want.append((c, ref, var, b, nm, sl, kv))
        want.sort(key=lambda t: (t[5] or '', t[3]))
        nb = 0
        last = None
        t_b = time.time()
        stat = collections.defaultdict(collections.Counter)
        for c, ref, var, b, nm, sl, kv in want:
            key = (sl, b)
            if key != last:
                if last is not None and time.time() - t_b > 15:
                    print(f'... slow bundle {time.time() - t_b:.0f} s, {rss_mb()} MB: {last[0] and os.path.basename(last[0])} {last[1]}', file=sys.stderr, flush=True)
                t_b = time.time()
                nb += 1
                last = key
                if os.environ.get('CENSUS_TRACE') and nb >= int(os.environ['CENSUS_TRACE']):
                    print(f'... bundle {nb}: {key[0] and os.path.basename(key[0])} {key[1]} {rss_mb()} MB', file=sys.stderr, flush=True)
                if a.prefab_limit and nb > a.prefab_limit: break
                if nb % 100 == 0:
                    print(f'... prefabs: {nb} bundles, {rss_mb()} MB, {key[0] and os.path.basename(key[0])} {key[1]}', file=sys.stderr, flush=True)
                    if rss_mb() > 4000:
                        src.views.clear()
                        gc.collect()
            if b in skip_bundles: stat[c]['bundle not read (--skip-bundle)'] += 1; continue
            bv = src.view(sl, b)
            if bv is None: stat[c]['bundle not found'] += 1; continue
            info = cmp_clothes(bv, nm)
            prefab[(c, ref, var)] = info
            stat[c][info['status']] += 1
            if info['status'] != 'ok': continue
            kind = FEMALE.index(c) if c in FEMALE else MALE.index(c)
            s = stat[c]
            s['vanilla' if ref.startswith(VANILLA) else 'mod'] += 1
            for i in (1, 2, 3):
                n_, a_ = info[f'useColorN0{i}'], info[f'useColorA0{i}']
                if n_ or a_: s[f'colour set {i} shown'] += 1
                if a_: s[f'useColorA0{i}'] += 1
                g = info['groups'][i - 1]
                if g['renderers']: s[f'rendNormal0{i} used'] += 1
                if g['slots'] > g['renderers']: s[f'rendNormal0{i} with more slots than renderers'] += 1
                if not (n_ or a_) and g['renderers']: s[f'rendNormal0{i} used but colour set {i} hidden'] += 1
                if (n_ or a_) and not g['renderers'] and not info['groups'][0]['renderers']: s[f'colour set {i} shown without any rendNormal'] += 1
                if info[f'defPtnIndex0{i}']: s[f'defPtnIndex0{i} != 0'] += 1
                dc = info[f'defMainColor0{i}']
                if dc and dc[3] < 1: s[f'defMainColor0{i} alpha < 1' + (' (useColorA)' if a_ else ' (no useColorA)')] += 1
                lay = info[f'defLayout0{i}']
                if lay and tuple(round(x, 4) for x in lay) != (10.0, 10.0, 0.0, 0.0): s[f'defLayout0{i} not (10, 10, 0, 0)'] += 1
                if info[f'defRotation0{i}']: s[f'defRotation0{i} != 0'] += 1
            if info['useBreak']:
                s['useBreak'] += 1
                if not any(g['alphaEx'] for g in info['groups']): s['useBreak without an _AlphaEx material'] += 1
            for k in ('objOpt01', 'objOpt02'):
                if info[k]: s[f'{k} present'] += 1
            for k in ('objTopDef', 'objTopHalf', 'objBotDef', 'objBotHalf'):
                if info[k]: s[k] += 1
            if info['uvScalePattern'] and tuple(round(x, 4) for x in info['uvScalePattern']) != (1.0, 1.0, 0.0, 0.0): s['uvScalePattern not (1, 1, 0, 0)'] += 1
            if info['dynamicBones']: s['with DynamicBones'] += 1; s['DynamicBones'] += len(info['dynamicBones'])
            if info['colliders']: s['with DynamicBoneColliders'] += 1; s['colliders'] += info['colliders']; s['colliders in the prefab\'s own cf_J_Root'] += info['collidersInOwnRoot']
            if kind in REF_RENDERERS:
                for rn in REF_RENDERERS[kind]:
                    s[f'reference {rn} present' if rn in info['refRenderers'] else f'reference {rn} missing'] += 1
            for g in info['groups']:
                for sh, n in g['shaders'].items(): stat[c]['shader ' + sh] += n
        for c in sorted(stat):
            s = stat[c]
            main = ', '.join(f'{k} {v}' for k, v in sorted(s.items()) if not k.startswith('shader '))
            shaders = ', '.join(f'{k[7:]} {v}' for k, v in s.most_common() if k.startswith('shader '))[:600]
            w(f'{c}: {main}')
            w(f'{c} shaders (rendNormal slots): {shaders}')
        # vanilla examples per fact (test targets of the spec: row ids and list names only)
        w('vanilla examples (row: name):')
        facts = {
            'useColorA (any set)': lambda i: any(i[f'useColorA0{n}'] for n in (1, 2, 3)),
            'three colour sets shown': lambda i: all(i[f'useColorN0{n}'] or i[f'useColorA0{n}'] for n in (1, 2, 3)),
            'useBreak': lambda i: i['useBreak'],
            'useBreak with a multi-slot renderer': lambda i: i['useBreak'] and any(g['slots'] > g['renderers'] for g in i['groups']),
            'option parts': lambda i: bool(i['objOpt01'] or i['objOpt02']),
            'DynamicBones': lambda i: bool(i['dynamicBones']),
            'defPtnIndex != 0': lambda i: any(i[f'defPtnIndex0{n}'] for n in (1, 2, 3)),
        }
        for c in sorted(set(FEMALE) | set(MALE)):
            parts_ = []
            for label, fn in facts.items():
                ex = [ref.split('#')[1] for (cc, ref, var), i in prefab.items()
                      if cc == c and var == '' and ref.startswith(VANILLA + '#') and i.get('status') == 'ok' and fn(i)]
                if ex:
                    ex = sorted(ex, key=int)
                    parts_.append(f'{label} {len(ex)}: ' + ', '.join(f'#{x} {van.get((c, int(x)), {}).get("Name")}' for x in ex[:4]))
            one = [f'#{i} {kv.get("Name")}' for (cc, i), kv in sorted(van.items()) if cc == c and kv.get('Coordinate') not in (None, '', '0')]
            if one: parts_.append(f'Coordinate != 0 {len(one)}: ' + ', '.join(one[:4]))
            if parts_: w(f'  {c}: ' + ' | '.join(parts_))
        # the compositing RT budget: MainTex sizes of the vanilla rows of every kind
        w('MainTex sizes of vanilla rows (compositing RTs = colour + detail per group at the MainTex size):')
        for c in FEMALE + (140, 141, 144, 147):
            sizes = collections.Counter()
            for ref, z, kv in rows_of(c):
                if z is not None or kv.get('MainData') == 'p_dummy' or empty(kv.get('MainTex')): continue
                b = norm(kv['MainAB'])
                bv = src.view(None if b in abd else (ship[b][0]['path'] if b in ship else None), b)
                if bv is None: continue
                h = tex_header(bv.env, {kv['MainTex']})
                for x in h.values(): sizes[f'{x[0]}x{x[1]}'] += 1
            w(f'  {c}: ' + ', '.join(f'{k} {v}' for k, v in sizes.most_common(8)))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 6. patterns 348', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 6. patterns 348
    w('')
    w('# 6. patterns (348): texture headers (size, format, mips, wrap U / V) of MainTexAB / MainTex; loose PNGs')
    pngs = set()
    for z in hs2next.values():
        for fl in z['files']:
            if fl.lower().endswith('.png'): pngs.add(fl.lower())
    pst = collections.Counter()
    fmts = collections.Counter()
    for ref, z, kv in rows_of(348):
        ab, tex = norm(kv.get('MainTexAB')), kv.get('MainTex')
        if empty(ab) or empty(tex): pst['row without a texture (none)'] += 1; continue
        if (re.sub(r'\.unity3d$', '', ab, flags=re.I) + '/' + tex + '.png').lower() in pngs: pst['loose PNG'] += 1; continue
        zp = z['path'] if z is not None and ab in z['files'] else sideloader_copy(ab, tex) if ab in ship else None
        bv = src.view(zp, ab)
        if bv is None: pst['bundle not found'] += 1; continue
        h = tex_header(bv.env, {tex})
        if not h: pst['texture not found'] += 1; continue
        x = list(h.values())[0]
        pst['vanilla' if z is None else 'mod'] += 1
        fmts[f'{x[0]}x{x[1]} {x[2]} mips {x[3]} wrap {x[4]}/{x[5]}'] += 1
    w('rows: ' + ', '.join(f'{k} {v}' for k, v in pst.items()))
    w('headers: ' + ', '.join(f'{k} ×{v}' for k, v in fmts.most_common(16)))

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 7. cards and coordinates', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 7. cards and coordinates
    w('')
    w('# 7. the user\'s cards and coordinates: the clothes block')
    modidx = {}
    for (c, g, i), (z, kv) in mod.items(): modidx[(c, g.lower(), i)] = (g, i)
    def part_ref(kind, cat, rid, res):
        for ri in res:   # character cards store the mod's own id (Slot), coordinate cards the runtime id of the save (LocalSlot)
            g = ri.get('ModID') or ri.get('GUID')   # Sideloader's ResolveInfo names the GUID ModID
            if ri.get('CategoryNo') == cat and rid in (ri.get('Slot'), ri.get('LocalSlot')) and g:
                return f"{g}#{ri.get('Slot')}", ('mod' if (cat, g.lower(), ri.get('Slot')) in modidx else 'mod not installed')
        if (cat, rid) in van: return f'{VANILLA}#{rid}', 'vanilla'
        return f'{VANILLA}#{rid}', 'vanilla id not listed'
    def scan(label, items, sex_of):
        st = collections.Counter()
        kinds = collections.defaultdict(collections.Counter)
        plug = collections.Counter()
        refs = collections.Counter()
        n = 0
        for name, clothes, ext in items:
            n += 1
            sx = sex_of(ext, clothes)
            cats_ = FEMALE if sx == 1 else MALE
            res = resolve_entries(ext)
            parts = (clothes or {}).get('parts') or []
            card = collections.Counter()
            top_one = False
            for k, p in enumerate(parts[:8]):
                if not isinstance(p, dict): continue
                cat = cats_[k]
                rid = p.get('id', 0)
                ref, how = part_ref(k, cat, rid, res)
                if rid != 0 or how != 'vanilla': kinds[k][how] += 1
                refs[(cat, ref)] += 1
                row = van.get((cat, rid)) if how.startswith('vanilla') else None
                if how == 'mod':
                    g, i = ref.rsplit('#', 1)
                    g0, i0 = modidx[(cat, g.lower(), int(i))]
                    ref = f'{g0}#{i0}'
                    row = mod[(cat, g0, i0)][1]
                if (cat, ref) in diffkeys: card['wears a row whose asset the catalog reads from another copy'] = 1
                pf = prefab.get((cat, ref, ''))
                cis = p.get('colorInfo') or []
                if k == 0 and row and row.get('Coordinate') not in (None, '0'): top_one = True
                if k == 1 and top_one and rid != 0: card['one-piece top with a stored bottom'] = 1
                if sx == 0 and k in (2, 3, 5, 6) and rid != 0: card[f'male kind {k} id != 0'] = 1
                br = p.get('breakRate') or 0
                if br:
                    card['breakRate != 0'] = 1
                    st[f'parts with breakRate != 0 ({"useBreak" if pf and pf.get("useBreak") else "no useBreak" if pf else "prefab not read"})'] += 1
                ho = p.get('hideOpt') or []
                if any(ho):
                    st[f'parts with hideOpt set ({"option parts" if pf and (pf.get("objOpt01") or pf.get("objOpt02")) else "no option parts" if pf else "prefab not read"})'] += 1
                for j, ciw in enumerate(cis[:3]):
                    if not isinstance(ciw, dict): continue
                    bc = col(ciw.get('baseColor'))
                    ua = pf.get(f'useColorA0{j + 1}') if pf and pf.get('status') == 'ok' else None
                    if bc and bc[3] < 1 and rid != 0:
                        st[f'colour sets with alpha < 1 ({"useColorA" if ua else "no useColorA: HS2 writes alpha 1 back" if ua is False else "prefab not read"})'] += 1
                    pt = ciw.get('pattern', 0)
                    if pt:
                        pr, ph = part_ref(k, 348, pt, res)
                        st[f'patterns {ph}'] += 1
                        if pt == 58947543: st['patterns = the KCOX pattern id 58947543'] += 1
                    lay = vec(ciw.get('layout'), 4) or (1, 1, 0, 0)
                    if any(x < 0 or x > 1 for x in lay): st['colour sets with a layout outside 0..1'] += 1
                    for kk in ('rotation', 'glossPower', 'metallicPower'):
                        v = ciw.get(kk)
                        if isinstance(v, (int, float)) and (v < 0 or v > 1): st[f'colour sets with {kk} outside 0..1'] += 1
                    if pf and pf.get('status') == 'ok' and rid != 0:
                        dc = pf.get(f'defMainColor0{j + 1}')
                        if dc and bc and all(abs(dc[i_] - bc[i_]) < 2e-3 for i_ in range(3)): st['colour sets whose base colour = the prefab default (RGB)'] += 1
                        elif dc and bc: st['colour sets whose base colour differs from the prefab default'] += 1
            for k_, v_ in card.items(): st['cards: ' + k_] += v_
            for pn, key in PLUGINS.items():
                if (ext or {}).get(key) is not None: plug[pn] += 1
            d = plugin_data(ext or {}, ME_KEY)
            mec = 0
            if d:
                for lk in ME_LISTS:
                    blob = d.get(lk)
                    if not isinstance(blob, (bytes, bytearray)): continue
                    lst = unpack_any(blob) or []
                    for it in lst if isinstance(lst, list) else []:
                        if isinstance(it, dict) and it.get('ObjectType') == 1: mec += 1
            if mec: plug['MaterialEditor Clothing items (cards)'] += 1; plug['MaterialEditor Clothing items'] += mec
        w(f'{label}: {n} read')
        for k in range(8):
            if kinds[k]: w(f'  kind {k} {KINDS[k]}: ' + ', '.join(f'{h} {v}' for h, v in sorted(kinds[k].items())))
        for k, v in sorted(st.items()): w(f'  {k}: {v}')
        w('  plugin data: ' + (', '.join(f'{k} {v}' for k, v in sorted(plug.items())) or 'none'))
        return refs
    def card_items(sex):
        folder = os.path.join(a.hs2, 'UserData', 'chara', sex)
        for root_, _, files in os.walk(folder):
            for fn in sorted(files):
                if not fn.lower().endswith('.png'): continue
                try:
                    c = load_card(os.path.join(root_, fn))
                    yield fn, c['coordinate']['clothes'], c.get('ext') or {}
                except Exception:
                    continue
    def coord_items(sex):
        folder = os.path.join(a.hs2, 'UserData', 'coordinate', sex)
        for root_, _, files in os.walk(folder):
            for fn in sorted(files):
                if not fn.lower().endswith('.png'): continue
                r = load_coordinate(os.path.join(root_, fn))
                if r: yield r[0], r[1], r[3]
    def coord_sex(ext, clothes):
        try:
            esd = clothes.get('ExtendedSaveData') or {}
        except Exception:
            esd = {}
        for k, v in (esd.items() if isinstance(esd, dict) else []):
            if 'sideloader' in str(k):
                try:
                    d = v[1] if isinstance(v, (list, tuple)) and len(v) > 1 else v
                    if isinstance(d, dict) and isinstance(d.get('data'), dict): d = d['data']
                    if isinstance(d, dict) and 'CoordinateSex' in d: return int(d['CoordinateSex'])
                except Exception: pass
        for ri in resolve_entries(ext):
            if (ri.get('Property') or '').endswith(('ClothesTopM', 'ClothesBotM', 'ClothesGlovesM', 'ClothesShoesM')): return 0
        return 1
    scan('female cards', card_items('female'), lambda e, c: 1)
    scan('male cards', card_items('male'), lambda e, c: 0)
    scan('female coordinates', coord_items('female'), coord_sex)
    scan('male coordinates', coord_items('male'), coord_sex)

    print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: 8. accessory colliders', file=sys.stderr, flush=True)
    # ------------------------------------------------------------ 8. accessory colliders (worn accessories)
    w('')
    w('# 8. accessories worn on the user\'s cards: DynamicBoneColliders inside their prefabs (the collider snapshot of a swap)')
    worn = collections.Counter()
    acc_cards = {}
    for sex in ('female', 'male'):
        folder = os.path.join(a.hs2, 'UserData', 'chara', sex)
        for root_, _, files in os.walk(folder):
            for fn in sorted(files):
                if not fn.lower().endswith('.png'): continue
                try:
                    c = load_card(os.path.join(root_, fn))
                    accp = c['coordinate']['accessory'].get('parts') or []
                except Exception:
                    continue
                res = resolver_info(c)
                keys = set()
                for s, p in enumerate(accp):
                    t, i = p.get('type'), p.get('id')
                    if t in (None, 120, 350) or i is None: continue
                    ref = f'{VANILLA}#{i}'
                    for ri in res:
                        mm = re.match(r'(?:outfit\.)?accessory(\d+)\.ChaFileAccessory\.PartsInfo\.id$', ri.get('Property') or '')
                        g_ = ri.get('ModID') or ri.get('GUID')
                        if mm and int(mm.group(1)) == s and g_: ref = f"{g_}#{ri.get('Slot')}"
                    keys.add((t, ref))
                for k in keys: worn[k] += 1
                acc_cards[(sex, fn)] = keys
    colliders = {}
    nb = 0
    for (t, ref) in sorted(worn):
        g, i = ref.rsplit('#', 1)
        i = int(i)
        if g == VANILLA: kv, zr = van.get((t, i)), None
        else:
            zr, kv = mod.get((t, g, i), (None, None))
            if kv is None:
                hit = next(((zz, kk) for (cc, gg, ii), (zz, kk) in mod.items() if cc == t and gg.lower() == g.lower() and ii == i), (None, None))
                zr, kv = hit
        if not kv or empty(kv.get('MainAB')) or empty(kv.get('MainData')): continue
        b = norm(kv['MainAB'])
        zp = zr['path'] if zr is not None and b in zr['files'] else (sideloader_copy(b, kv['MainData']) if b in ship else None)
        bv = src.view(zp, b)
        nb += 1
        if bv is None: continue
        tree = go_tree(bv, kv['MainData']) or []
        n = 0
        for path, go, tr in tree:
            for cp in bv.components(go):
                if bv.type_of(cp) != 'MonoBehaviour': continue
                d = bv.get(cp) or {}
                if 'm_Direction' in d and 'm_Radius' in d and 'm_Height' in d: n += 1
        colliders[(t, ref)] = n
    with_c = {k for k, v in colliders.items() if v}
    cards_c = sum(1 for keys in acc_cards.values() if keys & with_c)
    w(f'worn accessory rows: {len(worn)} (read {nb}); with DynamicBoneColliders: {len(with_c)} ({sum(colliders[k] for k in with_c)} colliders); '
      f'cards wearing one: {cards_c} of {len(acc_cards)}')
    for k in sorted(with_c)[:12]: w(f'  {k[0]} {k[1]}: {colliders[k]} collider(s), worn on {worn[k]} card(s)')

    # ------------------------------------------------------------ 9. thumbnails
    if a.thumbs:
        w('')
        w('# 9. thumbnails of the clothes and pattern lists (--thumbs): texture headers only (m_MipCount), no image decoded')
        want = collections.defaultdict(list)
        for c in cats:
            for ref, z, kv in rows_of(c):
                ab, tex = norm(kv.get('ThumbAB')), kv.get('ThumbTex')
                if empty(ab) or empty(tex): continue
                zp = z['path'] if z is not None and ab in z['files'] else (sideloader_copy(ab, tex) if ab in ship else None)
                want[(zp, ab)].append((c, ref, tex))
        gl = os.path.join(os.path.dirname(os.path.abspath(a.modcache)), 'texguard', 'bad_textures.tsv')
        guard = set()
        if os.path.isfile(gl):
            for line in open(gl, encoding='utf-8', errors='replace'):
                p = line.rstrip('\n').split('\t')
                if len(p) >= 3: guard.add((p[0].lstrip('﻿').lower(), p[2]))
        stat = collections.defaultdict(collections.Counter)
        bad = []
        n = 0
        for (zp, ab), refs_ in sorted(want.items(), key=lambda t: (t[0][0] or '', t[0][1])):
            n += 1
            d = src.data(zp, ab)
            if d is None:
                for c, ref, tex in refs_:
                    stat[c]['no bundle (loose PNG)' if (re.sub(r'\.unity3d$', '', ab, flags=re.I) + '/' + tex + '.png').lower() in pngs else 'no bundle'] += 1
                continue
            try:
                h = tex_header(UnityPy.load(d), {t for _, _, t in refs_})
            except Exception:
                for c, ref, tex in refs_: stat[c]['bundle unreadable'] += 1
                continue
            for c, ref, tex in refs_:
                stat[c]['rows'] += 1
                x = h.get(tex)
                if x is None: stat[c]['no texture'] += 1; continue
                if x[3] < 1:
                    stat[c]['mip < 1'] += 1
                    zg = next((z['guid'] for z in loaded if z['path'] == zp), None) if zp else None
                    source = f'{zg}:{ab}' if zp else f'abdata:{ab}'
                    if (source.lower(), tex) in guard: stat[c]['mip < 1, already in the guard list'] += 1
                    bad.append((c, ref, source, tex, x, f'{zp}!{ab}' if zp else None))
            if n % 200 == 0: print(f'... thumbs: {n} of {len(want)} bundles', file=sys.stderr, flush=True)
        w('category\trows\tmip < 1\t(already in the guard list)\tno texture\tno bundle\tno bundle (loose PNG)\tbundle unreadable')
        for c in sorted(stat):
            s = stat[c]
            w(f"{c}\t{s['rows']}\t{s['mip < 1']}\t{s['mip < 1, already in the guard list']}\t{s['no texture']}\t{s['no bundle']}\t{s['no bundle (loose PNG)']}\t{s['bundle unreadable']}")
        w(f'mip < 1: {len(bad)} row uses of {len({(b_[2], b_[3]) for b_ in bad})} textures (bundles read {n})')
        for c, ref, source, tex, x, key in bad[:40]:
            w(f'  mip<1\t{c}\t{ref}\t{source}\t{tex}\t{x[0]}x{x[1]} {x[2]} mips {x[3]}')
        if a.record_guard:
            added = 0
            with open(gl, 'a', encoding='utf-8', newline='\n') as fh:
                for c, ref, source, tex, x, key in bad:
                    if key is None or (source.lower(), tex) in guard: continue
                    guard.add((source.lower(), tex))
                    fh.write('\t'.join([source, tex, tex, f'{x[0]}x{x[1]}', x[2], str(x[3]), 'thumbnail: m_MipCount < 1 (maker_clothes_census --thumbs)', key]) + '\n')
                    added += 1
            w(f'recorded in Cache/texguard/bad_textures.tsv: {added} texture(s)')

    w('')
    w(f'(census time {time.time() - t0:.0f} s)')
    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8', newline='\n') as fh: fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


def serve_paths(serve, cache={}):
    k = id(serve)
    if k not in cache: cache[k] = {p for p, _ in serve}
    return cache[k]


def n_case(bv, n):
    """The exact name of the root GameObject whose lower-case name is `n` (container names are lower case)."""
    r = roots_of(bv, n)
    return r[0][1].get('m_Name') if r else n


def n_tex(bv, n):
    for o in bv.env.objects:
        if o.type.name != 'Texture2D': continue
        try:
            nm = o.peek_name() if hasattr(o, 'peek_name') else None
        except Exception:
            nm = None
        if nm is not None and nm.lower() == n: return nm
    return None


if __name__ == '__main__':
    main()
