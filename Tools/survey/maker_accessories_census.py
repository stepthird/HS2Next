# Maker accessories census (Phase 4.5): data facts the accessory pages need, read-only.
#   1. zipmods as Sideloader loads them (the 4.4 rule: game filter, one file per GUID, GUID load order) — only the counts;
#   2. accessory lists 350..363: vanilla rows (gate open as C9, Possess 99 hidden) and zipmod rows per category, the
#      `Parent` column (one of the 54 attach-point keys, "0" = cannot be re-parented, "null" = head-weighted rows that are
#      rebound to the head bones, empty, other text), the hair-type texture columns (TexAB / TexD / TexC), rows without a
#      thumbnail, rows whose MainData another copy serves (Sideloader's per-asset rule);
#   3. accessory prefabs: the CmpAccessory component of every listed row's MainData (the copy Sideloader serves): typeHair,
#      useColor01..03, rendNormal / rendAlpha counts and their material slots (multi-slot renderers: HS2 colours the first
#      slot only), the hair-type rows' material shaders, trfMove01 / trfMove02 (the two transform sets), the prefab defaults
#      (defColor01..04 alpha, defGlossPower / defMetallicPower), DynamicBones (chains) and DynamicBoneColliders inside the
#      prefab, ParticleSystems, transforms named like an attach point inside the prefab (the anchor search must not pick
#      them), a second CmpAccessory;
#   4. the 54 attach points: per key the transforms of that name in the skeleton prefab p_cf_anim (studio/base/00) and the
#      head skeleton p_cf_head_bone (chara/oo_base): exactly one each, the head group (keys 1..14) under the head skeleton;
#   5. the user's cards (UserData/chara/female, male) and coordinates (UserData/coordinate/female, male): used slots
#      (0..19 and MoreAccessories' extra slots), gaps below the highest used slot, types 120 / 350 / other, items vanilla /
#      mod / not installed, the parent key (empty, the row's default, another of the 54, unknown), the stored transform
#      values against the Maker's input rules (position −100..100 on a 0.1 grid, rotation integer 0..359, scale
#      0.01..100 on a 0.01 grid, scale 0), colours (alpha < 1 on colours 1..3, colour 4 alpha 0 on prefabs with alpha
#      renderers, gloss / metallic / smoothness outside 0..1, smoothness ≠ 0.5), hair-type rows worn (their colours equal
#      to the back hair's), noShake, hideCategory / hideTiming, the Studio state (Status showAccessory), plugin data
#      (MoreAccessories extra slots and `show`, Additional Accessory Controls rules / CharacterAccessory /
#      AutoMatchBackHairColor / AdvancedParent / override data, MaterialEditor items of ObjectType Accessory by slot,
#      DynamicBoneEditor accessory entries, ABMX modifiers on accessory locations 10..29);
#   6. --thumbs: thumbnails of the accessory lists whose Texture2D header has m_MipCount < 1 (--record-guard appends them to
#      <cache>/texguard/bad_textures.tsv; then run Tools/texguard_repair.py).
# No image is decoded (list TextAssets, MonoBehaviour / Transform / Renderer / Material type trees, Texture2D headers,
# AssetBundle containers, card data blocks only). Characters and items appear as data names and numbers only.
#
#   python -I Tools/survey/maker_accessories_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv> --out <dir>
#          [--no-prefabs | --prefab-limit N] [--no-cards] [--no-games] [--thumbs [--record-guard]]
# Writes <out>/report.txt; the committed copy is Tools/survey/maker_accessories_census.txt. Memory: bundle views are kept
# two at a time (LRU) with a garbage collection every 20 evictions; the run stays near 1 GB (watch `rss` in the progress
# lines; above 3.5 GB the views are dropped and collected).
import sys, os, re, io, gc, collections, time, argparse
import xml.etree.ElementTree as ET

import UnityPy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
from maker_core_census import vanilla_lists                                                    # noqa: E402
from maker_hair_census import pid, material_info, renderer_materials, tex_header, col, vec, unpack_any, plugin_data, empty  # noqa: E402
from maker_clothes_census import (read_modcache, select_zipmods, Source, roots_of, go_tree, load_coordinate,   # noqa: E402
                                  resolve_entries, low_ord, norm, rss_mb, utf8)
from hs2card import load_card, resolver_info                                                    # noqa: E402

VANILLA = 'illusion.hs2'
CATS = tuple(range(351, 364))
CAT_NAMES = {350: 'none', 351: 'head', 352: 'ear', 353: 'glasses', 354: 'face', 355: 'neck', 356: 'shoulder', 357: 'chest',
             358: 'waist', 359: 'back', 360: 'arm', 361: 'hand', 362: 'leg', 363: 'kokan'}
PARENTS = ('N_Hair_pony', 'N_Hair_twin_L', 'N_Hair_twin_R', 'N_Hair_pin_L', 'N_Hair_pin_R', 'N_Head_top', 'N_Hitai', 'N_Head',
           'N_Face', 'N_Earring_L', 'N_Earring_R', 'N_Megane', 'N_Nose', 'N_Mouth', 'N_Neck', 'N_Chest_f', 'N_Chest',
           'N_Tikubi_L', 'N_Tikubi_R', 'N_Back', 'N_Back_L', 'N_Back_R', 'N_Waist', 'N_Waist_f', 'N_Waist_b', 'N_Waist_L',
           'N_Waist_R', 'N_Leg_L', 'N_Knee_L', 'N_Ankle_L', 'N_Foot_L', 'N_Leg_R', 'N_Knee_R', 'N_Ankle_R', 'N_Foot_R',
           'N_Shoulder_L', 'N_Elbo_L', 'N_Arm_L', 'N_Wrist_L', 'N_Shoulder_R', 'N_Elbo_R', 'N_Arm_R', 'N_Wrist_R', 'N_Hand_L',
           'N_Index_L', 'N_Middle_L', 'N_Ring_L', 'N_Hand_R', 'N_Index_R', 'N_Middle_R', 'N_Ring_R', 'N_Dan', 'N_Kokan', 'N_Ana')
HEAD_KEYS = set(PARENTS[:14])
PARENT_SET = set(PARENTS)
ME_KEY = 'com.deathweasel.bepinex.materialeditor'
ME_LISTS = ('RendererPropertyList', 'MaterialFloatPropertyList', 'MaterialColorPropertyList', 'MaterialTexturePropertyList',
            'MaterialShaderList', 'MaterialCopyList', 'MaterialKeywordPropertyList', 'MaterialVectorPropertyList')
AAC_KEY = 'orange.spork.additionalaccessorycontrolsplugin'
DBE_KEY = 'com.deathweasel.bepinex.dynamicboneeditor'
ABMX_KEY = 'KKABMPlugin.ABMData'
MORE_KEY = 'moreAccessories'
AAC_RULES = {0: 'NONE', 1: 'STARTUP', 2: 'H_START', 3: 'H_END', 4: 'STUDIO_LOAD', 10: 'TOP', 11: 'BOT', 12: 'INNER_TOP',
             13: 'INNER_BOT', 14: 'PANTYHOSE', 15: 'GLOVE', 16: 'SOCK', 17: 'SHOE', 20: 'ACCESSORY_LINK',
             21: 'ACCESSORY_INVERSE_LINK', 30: 'HAIR', 31: 'NOSE', 32: 'EAR', 33: 'HAND', 34: 'FOOT', 35: 'EYELASH'}
ACC_PROP = re.compile(r'(?:outfit\.)?accessory(\d+)\.ChaFileAccessory\.PartsInfo\.id$')


def num(v, d=0.0):
    try: return float(v)
    except Exception: return d


def on_grid(v, step):
    return abs(v / step - round(v / step)) < 1e-3


def cmp_accessory(bv, name):
    """CmpAccessory facts of the root `name`, or {'status': …}."""
    roots = roots_of(bv, name)
    if not roots: return {'status': 'no prefab'}
    gpid, g, tr = roots[0]
    cmps = []
    tree = go_tree(bv, name) or []
    for path, go, t in tree:
        for cp in bv.components(go):
            if bv.type_of(cp) != 'MonoBehaviour': continue
            d = bv.get(cp)
            if d and 'rendNormal' in d and 'typeHair' in d: cmps.append((path, d))
    if not cmps: return {'status': 'no CmpAccessory'}
    path0, c = cmps[0]
    info = {'status': 'ok', 'cmpCount': len(cmps), 'cmpOnRoot': path0 == g.get('m_Name')}
    info['typeHair'] = bool(c.get('typeHair'))
    for k in ('useColor01', 'useColor02', 'useColor03'): info[k] = bool(c.get(k))
    for k in ('trfMove01', 'trfMove02'): info[k] = pid(c.get(k))[1] != 0
    for i in (1, 2, 3, 4):
        info[f'defColor0{i}'] = col(c.get(f'defColor0{i}'))
        info[f'defGlossPower0{i}'] = c.get(f'defGlossPower0{i}')
        info[f'defMetallicPower0{i}'] = c.get(f'defMetallicPower0{i}')
    for k in ('rendNormal', 'rendAlpha', 'rendHair'):
        rs = [x for x in (c.get(k) or []) if pid(x)[1] != 0] if k in c else None
        if rs is None: info[k] = None; continue
        slots, multi, shaders, ext_ = 0, 0, collections.Counter(), 0
        for x in rs:
            ms = renderer_materials(bv, x) or []
            if len(ms) > 1: multi += 1
            for mp in ms:
                mi = material_info(bv, mp)
                if not mi: continue
                if mi.get('external'): ext_ += 1; continue
                slots += 1
                shaders[mi['shader']] += 1
        info[k] = {'renderers': len(rs), 'slots': slots, 'multiSlot': multi, 'shaders': shaders, 'external': ext_}
    dbs, cols, parts, anchors = 0, 0, 0, collections.Counter()
    for path, go, t in tree:
        nm = go.get('m_Name')
        if nm in PARENT_SET and path != g.get('m_Name'): anchors[nm] += 1
        for cp in bv.components(go):
            ty = bv.type_of(cp)
            if ty == 'ParticleSystem': parts += 1
            if ty != 'MonoBehaviour': continue
            d = bv.get(cp) or {}
            if 'm_Root' in d and 'm_Damping' in d: dbs += 1
            elif 'm_Direction' in d and 'm_Radius' in d and 'm_Height' in d: cols += 1
    info['dynamicBones'] = dbs
    info['colliders'] = cols
    info['particleSystems'] = parts
    info['anchorNames'] = anchors
    info['transforms'] = len(tree)
    return info


def parse_more(ext):
    """MoreAccessories' extra slots: [(slot, attributes)] (element i = slot 20 + i), or None without the data."""
    d = plugin_data(ext or {}, MORE_KEY)
    xml = d.get('additionalAccessories') if isinstance(d, dict) else None
    if not isinstance(xml, str): return None
    try: root = ET.fromstring(xml)
    except Exception: return []
    return [(20 + i, e.attrib) for i, e in enumerate(x for x in root if x.tag == 'accessory')]


def more_part(attrs):
    """A MoreAccessories element as a card part dict (the card's PartsInfo member names)."""
    p = {'type': int(num(attrs.get('type'), 350)), 'id': int(num(attrs.get('id'), 0)), 'parentKey': attrs.get('parentKey', ''),
         'noShake': attrs.get('noShake') == 'true', 'hideCategory': int(num(attrs.get('hideCategory'), 0)),
         'hideTiming': int(num(attrs.get('hideTiming'), 1))}
    mv = []
    for j in range(2):
        for k in range(3):
            mv.append(tuple(num(attrs.get(f'addMove{j}{k}{a}'), 1.0 if k == 2 else 0.0) for a in 'xyz'))
    p['addMove'] = mv
    p['colorInfo'] = [{'color': tuple(num(attrs.get(f'color{l}{c}'), 1.0) for c in 'rgba'),
                       'glossPower': num(attrs.get(f'glossPower{l}'), 0.5), 'metallicPower': num(attrs.get(f'metallicPower{l}'), 0.5),
                       'smoothnessPower': num(attrs.get(f'smoothnessPower{l}'), 0.5)} for l in range(4)]
    p['show'] = attrs.get('show')
    return p


def card_moves(p):
    """[(position, rotation, scale)] × 2 of a card part (the MessagePack 2-D array [2, 3, [six Vector3]])."""
    am = p.get('addMove')
    if isinstance(am, list) and len(am) == 3 and isinstance(am[2], list): flat = am[2]
    elif isinstance(am, list) and len(am) == 6 and not isinstance(am[0], (int, float)): flat = am
    else: flat = None
    if flat is None or len(flat) < 6: return None
    out = []
    for j in range(2):
        out.append(tuple(tuple(vec(flat[j * 3 + k], 3) or ((1, 1, 1) if k == 2 else (0, 0, 0))) for k in range(3)))
    return out


def main():
    utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--no-prefabs', action='store_true', help='skip section 3 (the accessory prefabs; minutes)')
    ap.add_argument('--prefab-limit', type=int, default=0, help='read at most N prefab bundles (a quick check)')
    ap.add_argument('--no-cards', action='store_true', help='skip section 5 (the user\'s cards and coordinates)')
    ap.add_argument('--no-games', action='store_true', help='do not open the manifests (no game filter)')
    ap.add_argument('--skip-bundle', action='append', default=None, help='bundle path not read in section 3 (repeatable)')
    ap.add_argument('--thumbs', action='store_true', help='section 6: thumbnail headers of the accessory lists (m_MipCount < 1)')
    ap.add_argument('--record-guard', action='store_true', help='with --thumbs: append the found textures to <cache>/texguard/bad_textures.tsv')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    t0 = time.time()
    lines = []
    w = lines.append
    src = Source(a.hs2)
    src.skip = set(a.skip_bundle or [])

    def progress(msg):
        print(f'... {time.time() - t0:.0f} s, {rss_mb()} MB: {msg}', file=sys.stderr, flush=True)

    # ------------------------------------------------------------ 1. zipmods
    zs = read_modcache(a.modcache)
    loaded, hs2next, skipped, groups = select_zipmods(zs, games=not a.no_games)
    w('# 1. zipmods as Sideloader loads them (4.4: game filter, one file per GUID, GUID load order)')
    w(f'zipmod files with a GUID: {sum(1 for z in zs if z["guid"])}; loaded: {len(loaded)}; skipped by the game filter: {len(skipped)}')
    abd = set()
    root = os.path.join(a.hs2, 'abdata')
    for d, _, fs in os.walk(root):
        for fn in fs:
            if fn.lower().endswith('.unity3d'): abd.add(norm(os.path.relpath(os.path.join(d, fn), root)))
    ship = collections.defaultdict(list)
    for z in loaded:
        z['files_l'] = {fl.lower() for fl in z['files']}
        for fl in z['files']:
            if fl.lower().endswith('.unity3d'): ship[fl].append(z)

    def served(path, asset):
        """The zip path of the copy Sideloader serves `asset` of `path` from (None = abdata or missing)."""
        copies = ship.get(path)
        if not copies: return None
        if len(copies) == 1 and path not in abd: return copies[0]['path']
        for z in copies:
            nm = src.names(z['path'], path)
            if nm and asset in nm: return z['path']
        return None if path in abd else copies[0]['path']

    # ------------------------------------------------------------ 2. lists
    progress('2. accessory lists')
    van = {}
    for fn, asset, cat, dist, keys, rows in vanilla_lists(a.hs2):
        if cat not in CATS and cat != 350: continue
        for rid, vals in rows.items():
            try: rid = int(rid)
            except Exception: continue
            if (cat, rid) not in van: van[(cat, rid)] = {keys[i]: str(vals[i]) for i in range(min(len(keys), len(vals)))}
    mod = {}
    for z in loaded:
        seen = set()
        for c, i, kv in z['rows']:
            if c not in CATS or (c, i) in seen: continue
            seen.add((c, i))
            mod[(c, z['guid'], i)] = (z, kv)

    def rows_of(c):
        r = [(f'{VANILLA}#{i}', None, v) for (cc, i), v in sorted(van.items()) if cc == c and str(v.get('Possess', '0')) != '99']
        ms = sorted(((g, i, z, kv) for (cc, g, i), (z, kv) in mod.items() if cc == c), key=lambda t: (low_ord(t[0]), t[1]))
        return r + [(f'{g}#{i}', z, kv) for g, i, z, kv in ms]

    w('')
    w('# 2. accessory lists 350..363 (vanilla: gate open as C9, Possess 99 hidden; mod: Sideloader\'s zipmod set, first row of a key)')
    w('category\tname\tvanilla\tmod\tvanilla hidden (Possess 99)\tParent: one of the 54 / "0" / "null" / empty / other\tTexAB+TexD/TexC set\tno thumbnail (vanilla / mod)\tMainData served by another copy than the row\'s own')
    allrows = {}
    parent_other = collections.Counter()
    for c in (350,) + CATS:
        rs = rows_of(c)
        allrows[c] = rs
        v = [x for x in rs if x[1] is None]
        m = [x for x in rs if x[1] is not None]
        hid = sum(1 for (cc, i), kv in van.items() if cc == c and str(kv.get('Possess', '0')) == '99')
        pc = collections.Counter()
        tex = 0
        moved = 0
        for ref, z, kv in rs:
            p = kv.get('Parent')
            if p in PARENT_SET: pc['key'] += 1
            elif p == '0': pc['0'] += 1
            elif p == 'null': pc['null'] += 1
            elif empty(p) or p is None: pc['empty'] += 1
            else: pc['other'] += 1; parent_other[p] += 1
            if not empty(kv.get('TexAB')) and (not empty(kv.get('TexD')) or not empty(kv.get('TexC'))): tex += 1
            b = norm(kv.get('MainAB'))
            if not empty(b) and not empty(kv.get('MainData')) and b in ship:
                own = z['path'] if z is not None and b.lower() in z['files_l'] else None
                if served(b, kv['MainData']) != own: moved += 1
        nth = lambda xs: sum(1 for _, _, kv in xs if empty(kv.get('ThumbAB')) or empty(kv.get('ThumbTex')))
        w(f'{c}\t{CAT_NAMES[c]}\t{len(v)}\t{len(m)}\t{hid}\t{pc["key"]} / {pc["0"]} / {pc["null"]} / {pc["empty"]} / {pc["other"]}\t{tex}\t{nth(v)} / {nth(m)}\t{moved}')
    if parent_other: w('Parent values outside the 54 keys: ' + ', '.join(f'{k} {v}' for k, v in parent_other.most_common(20)))
    w('row 0 per category (the item a type change picks, AccessoryDefaultIndex 0): ' +
      ', '.join(f'{c} {van.get((c, 0), {}).get("Name", "-")} (Parent {van.get((c, 0), {}).get("Parent", "-")})' for c in CATS))

    # ------------------------------------------------------------ 3. prefabs
    prefab = {}
    if not a.no_prefabs:
        progress('3. accessory prefabs')
        w('')
        w('# 3. accessory prefabs (CmpAccessory of MainData from the copy Sideloader serves)')
        want = []
        for c in CATS:
            for ref, z, kv in allrows[c]:
                if empty(kv.get('MainAB')) or empty(kv.get('MainData')): continue
                b = norm(kv['MainAB'])
                zp = served(b, kv['MainData']) if b in ship else None
                want.append((c, ref, b, kv['MainData'], zp, kv))
        want.sort(key=lambda t: (t[4] or '', t[2]))
        nb, last = 0, None
        stat = collections.defaultdict(collections.Counter)
        examples = collections.defaultdict(list)
        hair_shaders = collections.Counter()
        normal_shaders = collections.Counter()
        anchor_names = collections.Counter()
        for c, ref, b, nm, zp, kv in want:
            key = (zp, b)
            if key != last:
                nb += 1
                last = key
                if a.prefab_limit and nb > a.prefab_limit: break
                if nb % 100 == 0:
                    progress(f'prefabs: {nb} bundles')
                    if rss_mb() > 3500:
                        src.views.clear()
                        gc.collect()
            bv = src.view(zp, b)
            s = stat[c]
            if bv is None: s['bundle unreadable or missing'] += 1; continue
            try:
                info = cmp_accessory(bv, nm)
            except Exception as e:
                s['read error ' + type(e).__name__] += 1
                continue
            if info.get('status') != 'ok': s[info['status']] += 1; continue
            prefab[(c, ref)] = info
            s['ok'] += 1
            if ref.startswith(VANILLA): s['ok vanilla'] += 1
            if info['cmpCount'] > 1: s['more than one CmpAccessory'] += 1
            if not info['cmpOnRoot']: s['CmpAccessory not on the root'] += 1
            th = info['typeHair']
            if th:
                s['typeHair'] += 1
                if len(examples[(c, 'typeHair')]) < 4 and ref.startswith(VANILLA): examples[(c, 'typeHair')].append(f'{ref} {kv.get("Name")}')
                if empty(kv.get('TexAB')) or (empty(kv.get('TexD')) and empty(kv.get('TexC'))): s['typeHair without TexAB / TexD / TexC'] += 1
                for k in ('useColor01', 'useColor02', 'useColor03'):
                    if info[k]: s['typeHair ' + k] += 1
                if info['rendNormal']: hair_shaders.update(info['rendNormal']['shaders'])
            else:
                if not (info['useColor01'] or info['useColor02'] or info['useColor03'] or (info['rendAlpha'] or {}).get('renderers')): s['no colour area and no alpha renderer (colour tab hidden)'] += 1
                for j, k in enumerate(('useColor01', 'useColor02', 'useColor03')):
                    if info[k]: s[f'colour {j + 1} used'] += 1
                if info['rendNormal']: normal_shaders.update(info['rendNormal']['shaders'])
            n_al = (info['rendAlpha'] or {}).get('renderers', 0)
            if n_al:
                s['alpha renderers (colour 4)'] += 1
                dc4 = info.get('defColor04')
                if dc4 and dc4[3] == 0: s['defColor04 alpha 0 (alpha renderers hidden by default)'] += 1
                if len(examples[(c, 'alpha')]) < 3 and ref.startswith(VANILLA): examples[(c, 'alpha')].append(f'{ref} {kv.get("Name")}')
            for j in (1, 2, 3):
                dc = info.get(f'defColor0{j}')
                if dc and dc[3] < 1 and info.get(f'useColor0{j}'): s['defColor0n alpha < 1 on a used colour 1..3'] += 1
            rn = info['rendNormal'] or {}
            if rn.get('multiSlot'): s['rendNormal renderers with several material slots (prefabs)'] += 1
            if not rn.get('renderers'): s['rendNormal empty'] += 1
            if rn.get('external'): s['rendNormal material slots in another bundle'] += 1
            if info['trfMove01']: s['trfMove01'] += 1
            if info['trfMove02']: s['trfMove02'] += 1
            if not info['trfMove01'] and not info['trfMove02']: s['no transform set'] += 1
            if info['trfMove02'] and not info['trfMove01']: s['trfMove02 without trfMove01'] += 1
            if info['dynamicBones']: s['with DynamicBones'] += 1; s['DynamicBones (chains)'] += info['dynamicBones']
            if info['colliders']: s['with DynamicBoneColliders'] += 1
            if info['particleSystems']: s['with ParticleSystems'] += 1
            if info['anchorNames']:
                s['transforms named like an attach point inside the prefab'] += 1
                anchor_names.update(info['anchorNames'])
            if kv.get('Parent') == 'null': s['Parent "null" (head-weighted)'] += 1
            if kv.get('Parent') == '0': s['Parent "0" (fixed)'] += 1
            for g_ in ('defGlossPower', 'defMetallicPower'):
                for j in (1, 2, 3, 4):
                    v = info.get(f'{g_}0{j}')
                    if isinstance(v, (int, float)) and (v < 0 or v > 1): s[f'{g_} outside 0..1'] += 1
        w(f'bundles read: {nb}')
        keys = sorted({k for s in stat.values() for k in s})
        w('fact\t' + '\t'.join(str(c) for c in CATS) + '\tall')
        for k in keys:
            w(k + '\t' + '\t'.join(str(stat[c][k]) for c in CATS) + '\t' + str(sum(stat[c][k] for c in CATS)))
        for (c, kind), ex in sorted(examples.items()): w(f'  {c} {kind} (vanilla): ' + '; '.join(ex))
        w('hair-type rendNormal shaders: ' + ', '.join(f'{k} {v}' for k, v in hair_shaders.most_common(8)))
        w('normal rendNormal shaders (top 12): ' + ', '.join(f'{k} {v}' for k, v in normal_shaders.most_common(12)))
        if anchor_names: w('attach-point names inside prefabs (transform count): ' + ', '.join(f'{k} {v}' for k, v in anchor_names.most_common(16)))

    # ------------------------------------------------------------ 4. the 54 attach points
    progress('4. attach points')
    w('')
    w('# 4. the 54 attach points in the skeleton prefabs (transforms of the key name)')
    rigs = (('studio/base/00.unity3d', 'p_cf_anim'), ('chara/oo_base.unity3d', 'p_cf_head_bone'))
    counts = {}
    for b, nm in rigs:
        bv = src.view(None, b)
        tree = go_tree(bv, nm) if bv is not None else None
        if not tree:
            w(f'{b} {nm}: not read'); continue
        cnt = collections.Counter(go.get('m_Name') for _, go, _ in tree)
        counts[nm] = cnt
        w(f'{b} {nm}: {len(tree)} transforms; keys present {sum(1 for k in PARENTS if cnt[k])}; keys more than once {[k for k in PARENTS if cnt[k] > 1]}')
    if len(counts) == 2:
        miss = [k for k in PARENTS if not counts['p_cf_anim'][k] and not counts['p_cf_head_bone'][k]]
        head_in_anim = [k for k in HEAD_KEYS if counts['p_cf_anim'][k]]
        body_in_head = [k for k in PARENT_SET - HEAD_KEYS if counts['p_cf_head_bone'][k]]
        w(f'keys in neither: {miss}; head keys (1..14) also in p_cf_anim: {head_in_anim}; body keys in p_cf_head_bone: {body_in_head}')

    # ------------------------------------------------------------ 5. cards and coordinates
    if not a.no_cards:
        progress('5. cards and coordinates')
        w('')
        w('# 5. the user\'s cards and coordinates: the accessory block, MoreAccessories, plugin data')
        modidx = {(c, g.lower(), i): (g, i) for (c, g, i) in mod}

        def part_ref(cat, rid, slot, res):
            for ri in res:
                mm = ACC_PROP.match(ri.get('Property') or '')
                g = ri.get('ModID') or ri.get('GUID')
                if mm and int(mm.group(1)) == slot and g and ri.get('CategoryNo', cat) == cat and rid in (ri.get('Slot'), ri.get('LocalSlot')):
                    return (f"{g}#{ri.get('Slot')}", 'mod' if (cat, g.lower(), ri.get('Slot')) in modidx else 'mod not installed')
            if (cat, rid) in van: return f'{VANILLA}#{rid}', 'vanilla'
            return f'{VANILLA}#{rid}', 'vanilla id not listed'

        def row_of(cat, ref, how):
            if how == 'vanilla': return van.get((cat, int(ref.rsplit('#', 1)[1])))
            if how == 'mod':
                g, i = ref.rsplit('#', 1)
                g0, i0 = modidx[(cat, g.lower(), int(i))]
                return mod[(cat, g0, i0)][1]
            return None

        def canon(cat, ref, how):
            if how != 'mod': return ref
            g, i = ref.rsplit('#', 1)
            g0, i0 = modidx[(cat, g.lower(), int(i))]
            return f'{g0}#{i0}'

        def scan(label, items):
            st = collections.Counter()
            types = collections.Counter()
            parents = collections.Counter()
            aac = collections.Counter()
            me = collections.Counter()
            used_hist = collections.Counter()
            n = 0
            for name, accp, ext, status, hair in items:
                n += 1
                res = resolve_entries(ext) if ext else []
                parts = [(s, p) for s, p in enumerate(accp or []) if isinstance(p, dict)]
                more = parse_more(ext)
                if more is not None:
                    st['cards with MoreAccessories data'] += 1
                    used_more = [(s, more_part(at)) for s, at in more if 351 <= int(num(at.get('type'), 350)) <= 363]
                    if used_more:
                        st['cards with used extra slots (>= 20)'] += 1
                        st['used extra slots'] += len(used_more)
                        st[f'max extra slot used ({"<= 29" if max(s for s, _ in used_more) <= 29 else ">= 30"})'] += 1
                    st['MoreAccessories elements (incl. empty)'] += len(more)
                    for s, p in [(s, more_part(at)) for s, at in more]:
                        if p.get('show') == 'false': st['extra slots with show=false'] += 1
                    parts += used_more
                if len(accp or []) != 20: st[f'cards whose accessory parts count is {len(accp or [])} (not 20)'] += 1
                used = []
                flags = set()
                for s, p in parts:
                    t = p.get('type', 120)
                    types[t if t in (120, 350) else ('351..363' if 351 <= t <= 363 else 'other')] += 1
                    if not (351 <= t <= 363): continue
                    used.append(s)
                    rid = p.get('id', 0)
                    ref, how = part_ref(t, rid, s, res)
                    st['items ' + how] += 1
                    row = row_of(t, ref, how)
                    key = canon(t, ref, how)
                    pf = prefab.get((t, key))
                    pk = p.get('parentKey') or ''
                    dflt = (row or {}).get('Parent')
                    if pk == '': parents['empty (the row default)'] += 1
                    elif pk in PARENT_SET:
                        parents['= the row default' if pk == dflt else ('another key (row "0")' if dflt == '0' else
                                ('another key (row "null")' if dflt == 'null' else 'another of the 54 keys'))] += 1
                    elif pk == 'none': parents['"none"'] += 1
                    else: parents['unknown key'] += 1
                    if dflt == 'null': st['worn rows with Parent "null" (head-weighted)'] += 1
                    if dflt == '0': st['worn rows with Parent "0"'] += 1
                    if 'show' in p: mv = [tuple(p['addMove'][j * 3 + k] for k in range(3)) for j in range(2)]   # a MoreAccessories element
                    else: mv = card_moves(p)
                    if mv is None: st['transform values unreadable'] += 1
                    else:
                        for j, (pos, rot, scl) in enumerate(mv):
                            nondef = any(abs(x) > 1e-6 for x in pos + rot) or any(abs(x - 1) > 1e-6 for x in scl)
                            if nondef:
                                st[f'set {j + 1} not identity'] += 1
                                if pf is not None and not pf.get(f'trfMove0{j + 1}'): st[f'set {j + 1} not identity on a prefab without trfMove0{j + 1} (no effect)'] += 1
                            if any(abs(x) > 100 + 1e-4 for x in pos): st['position outside -100..100'] += 1
                            elif any(not on_grid(x, 0.1) for x in pos): st['position off the 0.1 grid'] += 1
                            if any(x < -1e-4 or x >= 360 - 1e-4 for x in rot): st['rotation outside 0..360'] += 1
                            elif any(not on_grid(x, 1.0) for x in rot): st['rotation not an integer'] += 1
                            if any(x == 0 for x in scl): st['scale component 0'] += 1
                            elif any(x < 0.01 - 1e-6 or x > 100 + 1e-4 for x in scl): st['scale outside 0.01..100'] += 1
                            elif any(not on_grid(x, 0.01) for x in scl): st['scale off the 0.01 grid'] += 1
                    cis = p.get('colorInfo') or []
                    for l, ci in enumerate(cis[:4]):
                        if not isinstance(ci, dict): continue
                        cc = col(ci.get('color')) if not isinstance(ci.get('color'), tuple) else ci.get('color')
                        if cc and l < 3 and cc[3] < 1: st['colours 1..3 with alpha < 1'] += 1
                        if cc and l == 3 and cc[3] == 0 and pf and (pf.get('rendAlpha') or {}).get('renderers'): st['colour 4 alpha 0 on a prefab with alpha renderers (hidden)'] += 1
                        for kk in ('glossPower', 'metallicPower', 'smoothnessPower'):
                            v = ci.get(kk)
                            if isinstance(v, (int, float)) and (v < 0 or v > 1): st[f'{kk} outside 0..1'] += 1
                    if pf is not None and pf.get('trfMove02'): st['worn rows whose prefab has trfMove02'] += 1
                    if pf is not None and (pf.get('rendAlpha') or {}).get('renderers'): st['worn rows whose prefab has alpha renderers'] += 1
                    if pf is not None and pf.get('typeHair'):
                        flags.add('cards with a worn hair-type accessory')
                        st['worn hair-type accessories'] += 1
                        c0 = cis[0] if cis and isinstance(cis[0], dict) else {}
                        sm = c0.get('smoothnessPower')
                        if isinstance(sm, (int, float)) and abs(sm - 0.5) > 1e-4: st['hair-type: smoothness != 0.5'] += 1
                        if hair:
                            bp = (hair.get('parts') or [{}])[0] if isinstance(hair.get('parts'), list) else {}
                            bc = col(bp.get('baseColor')) if bp else None
                            c0c = col(c0.get('color')) if c0 and not isinstance(c0.get('color'), tuple) else c0.get('color')
                            if bc and c0c and all(abs(bc[i] - c0c[i]) < 2e-3 for i in range(3)): st['hair-type: colour 1 = the back hair base colour'] += 1
                    elif pf is None: st['worn rows whose prefab was not read'] += 1
                    if p.get('noShake'): st['noShake true'] += 1
                    hc, ht = p.get('hideCategory', 0), p.get('hideTiming', 1)
                    if hc: st[f'hideCategory {hc}'] += 1
                    if ht != 1: st[f'hideTiming {ht}'] += 1
                for fl in flags: st[fl] += 1
                if used:
                    st['cards with accessories'] += 1
                    used_hist[min(len(used), 21)] += 1
                    hi = max(used)
                    gaps = sum(1 for x in range(hi) if x not in set(used))
                    if gaps: st['cards with an empty slot below the highest used slot'] += 1; st['empty slots below the highest used slot'] += gaps
                    base = [x for x in used if x < 20]
                    if len(base) == 20: st['cards with all 20 base slots used'] += 1
                if status is not None:
                    sa = status.get('showAccessory') if isinstance(status, dict) else None
                    if isinstance(sa, list) and any(b is False for b in sa[:20]):
                        st['cards whose Status hides an accessory slot'] += 1
                        st['hidden slots in Status (used ones)'] += sum(1 for s in used if s < 20 and s < len(sa) and sa[s] is False)
                # plugin data
                d = plugin_data(ext or {}, AAC_KEY)
                if d:
                    blob = d.get('accessoryData') or d.get('coordinateAccessoryData')
                    slots = unpack_any(blob) if isinstance(blob, (bytes, bytearray)) else None
                    if isinstance(slots, list):
                        aac['cards with slot data'] += 1
                        for sd in slots:
                            if not isinstance(sd, list) or not sd: continue
                            if sd[0]:
                                if len(sd) > 6 and sd[6]: aac['empty slots with rules'] += 1
                                continue
                            aac['used slot records'] += 1
                            if len(sd) > 3 and sd[3]: aac['CharacterAccessory'] += 1
                            if len(sd) > 5 and sd[5]: aac['AutoMatchBackHairColor'] += 1
                            if len(sd) > 7 and sd[7]: aac['AdvancedParent'] += 1
                            if len(sd) > 1 and isinstance(sd[1], int) and sd[1] >= 20: aac['records of slots >= 20'] += 1
                            rules = sd[6] if len(sd) > 6 and isinstance(sd[6], list) else []
                            if rules: aac['slots with rules'] += 1
                            for r_ in rules:
                                if isinstance(r_, list) and r_: aac['rule ' + AAC_RULES.get(r_[0], str(r_[0]))] += 1
                    ob = d.get('overrideData') or d.get('coordinateOverrideData')
                    od = unpack_any(ob) if isinstance(ob, (bytes, bytearray)) else None
                    if isinstance(od, list) and len(od) >= 2 and (od[0] or od[1]): aac['cards with override / suppress entries'] += 1
                d = plugin_data(ext or {}, ME_KEY)
                if d:
                    hit = False
                    for lk in ME_LISTS:
                        blob = d.get(lk)
                        lst = unpack_any(blob) if isinstance(blob, (bytes, bytearray)) else None
                        for it in lst if isinstance(lst, list) else []:
                            if isinstance(it, dict) and it.get('ObjectType') == 2:
                                hit = True
                                me['items'] += 1
                                me['items on slots >= 20' if (it.get('Slot') or 0) >= 20 else 'items on slots 0..19'] += 1
                                if lk == 'MaterialShaderList': me['shader swaps'] += 1
                    if hit: me['cards'] += 1
                d = plugin_data(ext or {}, DBE_KEY)
                if d:
                    blob = d.get('AccessoryDynamicBoneData') or d.get('DynamicBoneData')
                    lst = unpack_any(blob) if isinstance(blob, (bytes, bytearray)) else None
                    if isinstance(lst, list) and lst: st['cards with DynamicBoneEditor entries'] += 1; st['DynamicBoneEditor entries'] += len(lst)
                d = plugin_data(ext or {}, ABMX_KEY)
                if d and d.get('boneData') is not None:
                    bd = d['boneData']
                    mods_ = unpack_any(bd) if isinstance(bd, (bytes, bytearray)) else bd
                    if isinstance(mods_, list):
                        na = 0
                        for m_ in mods_:
                            if not isinstance(m_, list) or len(m_) < 3: continue
                            loc = m_[2] if isinstance(m_[2], int) else None
                            if loc is not None and 10 <= loc <= 29: na += 1
                        if na: st['cards with ABMX modifiers on accessory locations 10..29'] += 1; st['ABMX accessory modifiers'] += na
            w(f'{label}: {n} read')
            w('  slot types (slots read): ' + ', '.join(f'{k} {v}' for k, v in sorted(types.items(), key=lambda t: str(t[0]))))
            w('  used slots per card: ' + ', '.join(f'{k}{"+" if k == 21 else ""} {v}' for k, v in sorted(used_hist.items())))
            w('  parent keys of used slots: ' + ', '.join(f'{k} {v}' for k, v in parents.most_common()))
            for k, v in sorted(st.items()): w(f'  {k}: {v}')
            w('  Additional Accessory Controls: ' + (', '.join(f'{k} {v}' for k, v in sorted(aac.items())) or 'none'))
            w('  MaterialEditor items of ObjectType Accessory: ' + (', '.join(f'{k} {v}' for k, v in sorted(me.items())) or 'none'))

        def card_items(sex):
            folder = os.path.join(a.hs2, 'UserData', 'chara', sex)
            for root_, _, files in os.walk(folder):
                for fn in sorted(files):
                    if not fn.lower().endswith('.png'): continue
                    try:
                        c = load_card(os.path.join(root_, fn))
                        acc = c['coordinate']['accessory'].get('parts') or []
                    except Exception:
                        continue
                    ext = c.get('ext') or {}
                    # the card resolver entries in the clothes census form (resolve_entries reads the same KKEx)
                    yield fn, acc, ext, c['blocks'].get('Status'), (c.get('custom') or {}).get('hair')

        def coord_items(sex):
            folder = os.path.join(a.hs2, 'UserData', 'coordinate', sex)
            for root_, _, files in os.walk(folder):
                for fn in sorted(files):
                    if not fn.lower().endswith('.png'): continue
                    r = load_coordinate(os.path.join(root_, fn))
                    if r: yield r[0], (r[2] or {}).get('parts') or [], r[3], None, None

        scan('female cards', card_items('female'))
        scan('male cards', card_items('male'))
        scan('female coordinates', coord_items('female'))
        scan('male coordinates', coord_items('male'))

    # ------------------------------------------------------------ 6. thumbnails
    if a.thumbs:
        progress('6. thumbnails')
        w('')
        w('# 6. thumbnails of the accessory lists (--thumbs): texture headers only (m_MipCount), no image decoded')
        pngs = set()
        for z in loaded:
            for fl in z['files']:
                if fl.lower().endswith('.png'): pngs.add(fl.lower())
        want = collections.defaultdict(list)
        for c in CATS:
            for ref, z, kv in allrows[c]:
                ab, tex = norm(kv.get('ThumbAB')), kv.get('ThumbTex')
                if empty(ab) or empty(tex): continue
                zp = served(ab, tex) if ab in ship else None
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
            if n % 200 == 0:
                progress(f'thumbs: {n} of {len(want)} bundles')
                gc.collect()
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
                    fh.write('\t'.join([source, tex, tex, f'{x[0]}x{x[1]}', x[2], str(x[3]), 'thumbnail: m_MipCount < 1 (maker_accessories_census --thumbs)', key]) + '\n')
                    added += 1
            w(f'recorded in Cache/texguard/bad_textures.tsv: {added} texture(s)')

    w('')
    w(f'(census time {time.time() - t0:.0f} s)')
    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8', newline='\n') as fh: fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
