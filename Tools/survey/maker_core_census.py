# Maker core census (Phase 4.0): data facts the editing foundation needs, read-only.
#   1. gated vanilla character-list rows: rows of abdata/list/characustom whose list asset has distributionNo 2/3/4
#      (AI-Shoujo DLC flags) or >= 50 without abdata/add50 -- the rows the game's own reader drops;
#   2. face skins and heads: mod rows of 210/211 (female) and 110/111 (male) from HS2Next's zipmod cache, their HeadID
#      column, and the <faceSkinInfo> / <headPresetInfo> elements of the zipmod manifests (Sideloader semantics,
#      2);
#   3. compositing sources: width x height x format of the textures the Create/* passes read (skin MainTex, detail,
#      clothes MainTex / MainTex02 / MainTex03 and their colour masks) for every vanilla row and a fixed-seed sample of
#      mod rows, to size the render textures and the kept inputs;
#   4. slider values outside 0..1 on the user's character cards (UserData/chara/female, male): face and body shape
#      arrays and bust softness / weight (Q23). Only the custom blocks are read (Tools/hs2card.py).
# No image is decoded (Texture2D headers only).
#
#   python -I Tools/survey/maker_core_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv> --out <dir>
#          [--mod-sample 200] [--seed 4]
# Writes <out>/report.txt and <out>/textures.tsv; the committed copy of report.txt is Tools/survey/maker_core_census.txt
# (a run with --mod-sample 200 --seed 4).
import sys, os, io, re, json, random, zipfile, argparse, collections, xml.etree.ElementTree as ET

import UnityPy, msgpack

GATED = {2, 3, 4}
SKIN_CATS = {211: 'ft_skin_f', 231: 'ft_skin_b', 111: 'mt_skin_f', 131: 'mt_skin_b'}
DETAIL_CATS = {212: 'ft_detail_f', 232: 'ft_detail_b', 112: 'mt_detail_f', 132: 'mt_detail_b'}
CLOTHES_CATS = {240: 'fo_top', 241: 'fo_bot', 242: 'fo_inner_t', 243: 'fo_inner_b', 244: 'fo_gloves', 245: 'fo_panst',
                246: 'fo_socks', 247: 'fo_shoes', 140: 'mo_top', 141: 'mo_bot', 144: 'mo_gloves', 147: 'mo_shoes'}
TEX_COLS = {**{c: ['MainTex', 'OcclusionMapTex', 'NormalMapTex'] for c in SKIN_CATS},
            **{c: ['OcclusionMapTex', 'NormalMapTex', 'AddTex'] for c in DETAIL_CATS},
            **{c: ['MainTex', 'ColorMaskTex', 'MainTex02', 'ColorMask02Tex', 'MainTex03', 'ColorMask03Tex'] for c in CLOTHES_CATS}}
BPP = {'DXT1': 0.5, 'DXT1Crunched': 0.5, 'DXT5': 1, 'DXT5Crunched': 1, 'BC7': 1, 'BC5': 1, 'BC4': 0.5, 'RGBA32': 4,
       'ARGB32': 4, 'RGB24': 3, 'Alpha8': 1, 'RGBA4444': 2, 'RGB565': 2, 'R8': 1, 'RG16': 2, 'BC6H': 1}


def vanilla_lists(hs2):
    """[(bundle, asset, category, distributionNo, keys, {id: values})] of abdata/list/characustom."""
    root = os.path.join(hs2, 'abdata', 'list', 'characustom')
    out = []
    for fn in sorted(os.listdir(root)):
        if not fn.endswith('.unity3d'): continue
        env = UnityPy.load(os.path.join(root, fn))
        for obj in env.objects:
            if obj.type.name != 'TextAsset': continue
            d = obj.read()
            raw = d.m_Script
            if isinstance(raw, str): raw = raw.encode('utf-8', 'surrogateescape')
            try:
                m = msgpack.unpackb(bytes(raw), raw=False, strict_map_key=False)
            except Exception:
                continue
            if not isinstance(m, dict) or 'dictList' not in m: continue
            out.append((fn, d.m_Name, m.get('categoryNo'), m.get('distributionNo'), m.get('lstKey') or [], m.get('dictList') or {}))
    return out


def mod_rows(modcache):
    """[(zip path, guid, category, id, {column: value})] of the character-list rows (E records) in the zipmod cache."""
    rows, z, guid = [], None, None
    with open(modcache, encoding='utf-8') as f:
        for line in f:
            p = line.rstrip('\n').split('\t')
            if p[0] == 'Z': z, guid = p[1], (p[3] if len(p) > 3 else '')
            elif p[0] == 'E' and len(p) >= 3:
                kv = dict(zip(p[3::2], p[4::2]))
                rows.append((z, guid, int(p[1]), int(p[2]), kv))
    return rows


def manifest_infos(zpath):
    """(faceSkinInfo list, headPresetInfo list) of one zipmod manifest."""
    try:
        with zipfile.ZipFile(zpath) as zf:
            name = next((n for n in zf.namelist() if n.lower() == 'manifest.xml'), None)
            if not name: return [], []
            root = ET.fromstring(zf.read(name).decode('utf-8-sig', 'replace'))
    except Exception:
        return [], []
    fs = [dict(e.attrib) for e in root.iter('faceSkinInfo')]
    hp = [{'preset': e.attrib.get('preset'), **{c.tag: (c.text or '').strip() for c in e}} for e in root.iter('headPresetInfo')]
    return fs, hp


def tex_headers(env):
    """name (lower) -> (w, h, format, mips) of every Texture2D of a loaded bundle."""
    r = {}
    for obj in env.objects:
        if obj.type.name != 'Texture2D': continue
        try:
            t = obj.read()
            fmt = getattr(t.m_TextureFormat, 'name', str(t.m_TextureFormat))
            r[t.m_Name.lower()] = (t.m_Width, t.m_Height, fmt, getattr(t, 'm_MipCount', 1))
        except Exception:
            pass
    return r


def mib(w, h, fmt, mips):
    b = w * h * BPP.get(fmt, 4)
    return b * (4 / 3 if mips and mips > 1 else 1) / 2**20


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--mod-sample', type=int, default=200)
    ap.add_argument('--seed', type=int, default=4)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = []
    w = lines.append
    # GameSystem.isAdd50: the file abdata/add50 exists and names the append-disc scenario bundle
    p50 = os.path.join(a.hs2, 'abdata', 'add50')
    add50 = os.path.isfile(p50) and 'adv/scenario/c10/50/08.unity3d' in open(p50, encoding='utf-8', errors='replace').read()

    # 1. gate
    lists = vanilla_lists(a.hs2)
    kept, gated = {}, []
    first = {}
    for fn, asset, cat, dist, keys, rows in lists:
        drop = dist in GATED or (dist is not None and dist >= 50 and not add50)
        for rid, vals in rows.items():
            name = vals[keys.index('Name')] if 'Name' in keys and keys.index('Name') < len(vals) else ''
            if drop: gated.append((cat, rid, dist, fn, asset, name))
            elif (cat, rid) not in first: first[(cat, rid)] = dict(zip(keys, vals))
    w(f'# gated vanilla rows (distributionNo in {sorted(GATED)}; add50 present: {add50}): {len(gated)}')
    for g in sorted(gated): w('gated\t' + '\t'.join(str(x) for x in g))
    w(f'# vanilla rows kept (first definition wins): {len(first)}')

    # 2. heads and face skins
    mods = mod_rows(a.modcache)
    w(f'# mod character-list rows in the cache: {len(mods)}')
    vheads = {s: sorted(i for (c, i) in first if c == hc) for s, hc in (('f', 210), ('m', 110))}
    w(f'vanilla heads female {vheads["f"]} male {vheads["m"]}')
    for s, hc, sc in (('female', 210, 211), ('male', 110, 111)):
        vs = collections.Counter(int(v.get('HeadID', '0')) for (c, i), v in first.items() if c == sc)
        w(f'vanilla {s} face skins per HeadID: {dict(sorted(vs.items()))}')
        mh = [r for r in mods if r[2] == hc]
        ms = [r for r in mods if r[2] == sc]
        w(f'mod {s} heads {len(mh)} (zipmods {len(set(r[0] for r in mh))}), mod face skins {len(ms)} (zipmods {len(set(r[0] for r in ms))})')
        hid = collections.Counter()
        for r in ms:
            h = r[4].get('HeadID')
            hid['vanilla head ' + h if h is not None and h.isdigit() and int(h) in (vheads['f'] if hc == 210 else vheads['m']) else 'other ' + str(h)] += 1
        w(f'  mod face skin HeadID column: {dict(hid.most_common(12))}')
    zips = sorted(set(r[0] for r in mods if r[2] in (210, 211, 110, 111)))
    fs_all, hp_all = [], []
    for z in zips:
        fs, hp = manifest_infos(z)
        fs_all += [(os.path.basename(z), x) for x in fs]
        hp_all += [(os.path.basename(z), x) for x in hp]
    w(f'# manifests of the {len(zips)} zipmods with head / face-skin rows: faceSkinInfo {len(fs_all)} in '
      f'{len(set(z for z, _ in fs_all))} zipmods, headPresetInfo {len(hp_all)} in {len(set(z for z, _ in hp_all))} zipmods')
    for z, x in fs_all[:40]: w('faceSkinInfo\t' + z + '\t' + json.dumps(x, ensure_ascii=False))
    for z, x in hp_all[:20]: w('headPresetInfo\t' + z + '\t' + json.dumps(x, ensure_ascii=False))
    # skins whose HeadID names a head of the same zipmod (resolvable only through faceSkinInfo)
    own = 0
    fsk = {(z, x.get('skinID')) for z, x in fs_all}
    by_zip_heads = collections.defaultdict(set)
    for r in mods:
        if r[2] in (210, 110): by_zip_heads[r[0]].add(str(r[3]))
    covered = 0
    for r in mods:
        if r[2] in (211, 111) and r[4].get('HeadID') in by_zip_heads.get(r[0], ()):
            own += 1
            if (os.path.basename(r[0]), str(r[3])) in fsk: covered += 1
    w(f'mod face skins whose HeadID is a head id of their own zipmod: {own}; of those with a faceSkinInfo entry: {covered}')

    # 3. compositing sources
    tex_rows = []
    by_bundle = collections.defaultdict(list)
    for (c, i), v in first.items():
        if c in TEX_COLS and v.get('MainAB') not in (None, '', '0'): by_bundle[('abdata', v['MainAB'])].append((c, i, v))
    for (src, ab), rows in sorted(by_bundle.items()):
        p = os.path.join(a.hs2, 'abdata', ab)
        if not os.path.isfile(p): continue
        try: heads = tex_headers(UnityPy.load(p))
        except Exception: continue
        for c, i, v in rows:
            for col in TEX_COLS[c]:
                n = v.get(col)
                if n and n != '0' and n.lower() in heads: tex_rows.append(('vanilla', c, i, col) + heads[n.lower()])
    rnd = random.Random(a.seed)
    cand = [r for r in mods if r[2] in TEX_COLS and r[4].get('MainAB') not in (None, '', '0')]
    for z, guid, c, i, v in rnd.sample(cand, min(a.mod_sample, len(cand))):
        try:
            with zipfile.ZipFile(z) as zf:
                ent = next((n for n in zf.namelist() if n.replace('\\', '/').lower() == ('abdata/' + v['MainAB']).lower()), None)
                if not ent: continue
                heads = tex_headers(UnityPy.load(zf.read(ent)))
        except Exception:
            continue
        for col in TEX_COLS[c]:
            n = v.get(col)
            if n and n != '0' and n.lower() in heads: tex_rows.append(('mod', c, i, col) + heads[n.lower()])
    with open(os.path.join(a.out, 'textures.tsv'), 'w', encoding='utf-8') as f:
        f.write('src\tcategory\tid\tcolumn\tw\th\tformat\tmips\n')
        for r in tex_rows: f.write('\t'.join(str(x) for x in r) + '\n')
    summ = collections.defaultdict(lambda: collections.Counter())
    for src, c, i, col, wd, ht, fmt, mips in tex_rows:
        grp = 'skin' if c in SKIN_CATS else 'detail' if c in DETAIL_CATS else 'clothes'
        summ[(src, grp, col)][f'{wd}x{ht}'] += 1
    w('# compositing source sizes (rows per size)')
    for k in sorted(summ): w('size\t' + '\t'.join(k) + '\t' + ', '.join(f'{s} {n}' for s, n in summ[k].most_common()))
    # clothes render textures per row: both creators (colour + gloss) at the MainTex size, ARGB32 with mips
    rt = collections.Counter()
    for src, c, i, col, wd, ht, fmt, mips in tex_rows:
        if c in CLOTHES_CATS and col in ('MainTex', 'MainTex02', 'MainTex03'):
            rt[(src, round(2 * wd * ht * 4 * 4 / 3 / 2**20, 1))] += 1
    w('# clothes compositing RTs per colour group (2 creators, ARGB32 + mips, MiB): ' +
      ', '.join(f'{s} {m} MiB x{n}' for (s, m), n in sorted(rt.items())))
    src_mib = collections.defaultdict(float)
    for src, c, i, col, wd, ht, fmt, mips in tex_rows: src_mib[(src, c in CLOTHES_CATS)] += mib(wd, ht, fmt, mips)
    w('# texture bytes over the measured rows (MiB): ' + ', '.join(f'{k} {v:.0f}' for k, v in sorted(src_mib.items())))

    # 4. card slider ranges
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
    from hs2card import load_card   # noqa: E402
    for sex in ('female', 'male'):
        folder = os.path.join(a.hs2, 'UserData', 'chara', sex)
        n = bad = 0
        vals, cards_out = collections.Counter(), collections.Counter()
        lo, hi = 0.0, 1.0
        for root_, _, files in os.walk(folder):
            for fn in files:
                if not fn.lower().endswith('.png'): continue
                try:
                    c = load_card(os.path.join(root_, fn))
                    face, body = c['custom']['face'], c['custom']['body']
                except Exception:
                    bad += 1
                    continue
                n += 1
                arrays = {'face.shape': face.get('shapeValueFace') or [], 'body.shape': body.get('shapeValueBody') or [],
                          'body.bust': [body.get('bustSoftness', 0.5), body.get('bustWeight', 0.5)]}
                hit = set()
                for k, arr in arrays.items():
                    for v in arr:
                        if isinstance(v, (int, float)) and (v < 0 or v > 1):
                            vals[k] += 1
                            hit.add(k)
                            lo, hi = min(lo, v), max(hi, v)
                for k in hit: cards_out[k] += 1
                if hit: cards_out['any'] += 1
        w(f'# {sex} cards read {n} (not readable as character cards {bad}): cards with a value outside 0..1 '
          f'{dict(cards_out)}; values outside {dict(vals)}; range seen [{lo:.4g}, {hi:.4g}]')
    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8') as f: f.write('\n'.join(lines) + '\n')
    print('\n'.join(l for l in lines if not l.startswith(('faceSkinInfo', 'headPresetInfo'))))


if __name__ == '__main__':
    main()
