# Zipmod-shadowed vanilla bundles (Phase 4.3, package H; a question / A's HT10 note):
# which asset bundles the game ships under abdata/<path>.unity3d are also shipped by a zipmod under the same path, which of
# their assets the zipmod copy overrides, and which vanilla list rows and which of the user's cards use them. Read-only.
#
# Behaviour this counts (Sideloader 21.1.4, read for behaviour only): when the game loads abdata/<path>.unity3d and the file
# exists, Sideloader loads the abdata file; every asset load from any bundle then asks Sideloader first, and when a zipmod
# ships a bundle with the same trimmed path (the zip entry without its first folder, exact case) that contains an asset of
# the requested name, the asset comes from that zipmod copy (the first such copy in the order the zipmods were added: one
# zipmod per manifest GUID, the highest version, GUIDs in sorted order). So a vanilla bundle is shadowed per asset, not per
# file: an asset the zipmod copy does not contain still comes from abdata.
#
#   python -I Tools/survey/shadowed_bundle_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv>
#          --studiocache <Cache/studiolists-*.tsv> --out <dir> [--no-assets]
# Writes <out>/report.txt (the committed copy is Tools/survey/shadowed_bundle_census.txt) and <out>/shadowed.tsv. Bundle
# containers are read with UnityPy (asset names only; no asset is decoded); cards with Tools/hs2card.py (data blocks only).
import sys, os, re, argparse, collections, zipfile, functools, locale

import UnityPy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
from maker_core_census import vanilla_lists            # noqa: E402
from hs2card import load_card, resolver_info             # noqa: E402

# CategoryNo of the character lists (AIChara.ChaListDefine) by card member
FACE = {'headId': (110, 210), 'skinId': (111, 211), 'detailId': (112, 212), 'eyebrowId': 314, 'hlId': 319, 'eyelashesId': 315,
        'moleId': 323, 'beardId': 121}
MAKEUP = {'eyeshadowId': 316, 'cheekId': 320, 'lipId': 322}
BODY = {'skinId': (131, 231), 'detailId': (132, 232), 'sunburnId': (133, 233), 'nipId': 334, 'underhairId': 335}
CLOTHES = {0: (140, 240), 1: (141, 241), 2: (None, 242), 3: (None, 243), 4: (144, 244), 5: (None, 245), 6: (None, 246), 7: (147, 247)}


def utf8():
    sys.stdout.reconfigure(encoding='utf-8')


def area(path):
    """The content area of an abdata-relative bundle path."""
    p = path.lower()
    b = p.rsplit('/', 1)[-1]
    if p.startswith('chara/'):
        if re.match(r'(so_hair_|st_hair)', b): return 'chara hair'
        if re.match(r'[fm]o_(top|bot|inner_t|inner_b|gloves|panst|socks|shoes)|[fm]t_\w*mask|st_pattern', b): return 'chara clothes'
        if b.startswith('ao_'): return 'chara accessories'
        if re.match(r'[fm]o_head|[fm]t_(skin|detail)_f|mt_beard|st_(eye|eyeblack|eye_hl|eyebrow|eyelash|eyeshadow|cheek|lip|mole)', b):
            return 'chara face'
        if re.match(r'[fm]t_(skin|detail)_b|[fm]t_sunburn|st_(nip|underhair|paint)|[fm]o_body|mt_body', b): return 'chara body'
        if '/thumb/' in p or b.startswith('thumb'): return 'chara thumbnails'
        return 'chara other'
    if p.startswith('studio/'): return 'studio items'
    if p.startswith('map/'): return 'maps'
    if p.startswith('list/'): return 'lists'
    return 'other'


def abdata_bundles(hs2):
    root = os.path.join(hs2, 'abdata')
    out = {}
    for d, _, fs in os.walk(root):
        for f in fs:
            if not f.lower().endswith('.unity3d'): continue
            rel = os.path.relpath(os.path.join(d, f), root).replace('\\', '/')
            out[rel.lower()] = rel
    return out


def zipmods(modcache):
    """Zipmods as Sideloader loads them: one per manifest GUID (the highest version; else the last scanned), in GUID order
    (the default string comparer); per zipmod its .unity3d entries (trimmed path as cached)."""
    zs, cur = [], None
    with open(modcache, encoding='utf-8') as f:
        f.readline()
        for line in f:
            p = line.rstrip('\n').split('\t')
            if p[0] == 'Z':
                cur = {'path': p[1], 'guid': p[3] if len(p) > 3 else '', 'version': '', 'name': '', 'bundles': []}
                zs.append(cur)
            elif cur is None: continue
            elif p[0] == 'N':
                cur['name'] = p[1] if len(p) > 1 else ''
                cur['version'] = p[-1] if len(p) > 2 else ''
            elif p[0] == 'B' and p[1].lower().endswith('.unity3d'):
                cur['bundles'].append(p[1])
    by = collections.OrderedDict()
    for z in zs:
        if not z['guid']: continue
        by.setdefault(z['guid'], []).append(z)

    def vkey(v):
        return [int(x) if x.isdigit() else x for x in re.split(r'[.\-_ ]', v or '')]
    chosen = []
    for g, lst in by.items():
        if len(lst) > 1 and all(z['version'] for z in lst):
            try:
                lst = sorted(lst, key=lambda z: (vkey(z['version']), len(z['path'])), reverse=True)
            except TypeError:
                lst = lst[::-1]
        elif len(lst) > 1:
            lst = lst[::-1]
        chosen.append(lst[0])
    # Sideloader orders by GUID with the default comparer (culture-aware in Mono); Python's locale collation approximates it
    try:
        locale.setlocale(locale.LC_COLLATE, '')
        chosen.sort(key=functools.cmp_to_key(lambda a, b: locale.strcoll(a['guid'], b['guid'])))
    except Exception:
        chosen.sort(key=lambda z: z['guid'])
    return chosen


GAME_NAMES = ('hs2', 'honeyselect2', 'honey select 2')   # Sideloader's GameNameList for HS2


def loads_on_hs2(zpath, cache={}):
    """Sideloader skips a zipmod whose manifest names games and none of them is HS2 ("meant for AI Girl")."""
    if zpath not in cache:
        ok = True
        try:
            with zipfile.ZipFile(zpath) as zf:
                m = zf.read('manifest.xml').decode('utf-8', 'replace')
            games = [g.strip().lower() for g in re.findall(r'<game>(.*?)</game>', m, re.S)]
            ok = not games or any(g in GAME_NAMES for g in games)
        except Exception:
            pass
        cache[zpath] = ok
    return cache[zpath]


def container_names(env):
    """Short asset names (file name without extension, lower case) of a bundle's AssetBundle container: what
    AssetBundle.Contains / LoadAsset(name) match (read from the AssetBundle object; UnityPy's container index can fail on
    in-memory bundles)."""
    out = set()
    for o in env.objects:
        if o.type.name != 'AssetBundle': continue
        d = o.read_typetree()
        for c in d.get('m_Container') or []:
            k = c[0] if isinstance(c, (list, tuple)) else c.get('first')
            out.add(k.rsplit('/', 1)[-1].rsplit('.', 1)[0].lower())
    return out


def asset_names_file(path):
    return container_names(UnityPy.load(path))


def asset_names_zip(zpath, trimmed):
    with zipfile.ZipFile(zpath) as zf:
        for e in zf.namelist():
            t = e.replace('\\', '/')
            t = t[t.index('/') + 1:] if '/' in t else t
            if t == trimmed:
                return container_names(UnityPy.load(zf.read(e)))
    return None


def studio_rows(studiocache):
    """(list, no, bundle, asset) of the vanilla Studio item and map lists."""
    out, cur = [], None
    with open(studiocache, encoding='utf-8') as f:
        for line in f:
            p = line.rstrip('\n').split('\t')
            if p[0] == 'L': cur = p[1].lower() if len(p) > 1 else None
            elif p[0] == 'R' and cur:
                c = p[1:]
                if cur.startswith('itemlist_') and len(c) >= 7: out.append(('item', cur, c[0], c[5], c[6]))
                elif cur.startswith('map_') and len(c) >= 4: out.append(('map', cur, c[0], c[2], c[3]))
    return out


def card_refs(card, sex):
    """[(member, category, id)] of the list rows a card references (vanilla or mod ids as stored)."""
    out = []
    cu = card['custom']
    f = cu.get('face') or {}
    for m, cat in FACE.items():
        if m in f: out.append(('face.' + m, cat[sex] if isinstance(cat, tuple) else cat, f[m]))
    for e, p in enumerate(f.get('pupil') or []):
        out.append(('face.pupil%d.pupilId' % e, 317, p.get('pupilId')))
        out.append(('face.pupil%d.blackId' % e, 318, p.get('blackId')))
    mk = f.get('makeup') or {}
    for m, cat in MAKEUP.items():
        if m in mk: out.append(('face.makeup.' + m, cat, mk[m]))
    for k, pi in enumerate(mk.get('paintInfo') or []):
        out.append(('face.paint%d' % k, 313, pi.get('id')))
    b = cu.get('body') or {}
    for m, cat in BODY.items():
        if m in b: out.append(('body.' + m, cat[sex] if isinstance(cat, tuple) else cat, b[m]))
    for k, pi in enumerate(b.get('paintInfo') or []):
        out.append(('body.paint%d' % k, 313, pi.get('id')))
    for k, p in enumerate((cu.get('hair') or {}).get('parts') or []):
        out.append(('hair.%d' % k, 300 + k, p.get('id')))
        out.append(('hair.%d.mesh' % k, 306, p.get('meshType')))
    co = card.get('coordinate') or {}
    for k, p in enumerate((co.get('clothes') or {}).get('parts') or []):
        cat = CLOTHES.get(k, (None, None))[sex]
        if cat is not None:
            out.append(('clothes.%d' % k, cat, p.get('id')))
        for j, ci in enumerate(p.get('colorInfo') or []):
            out.append(('clothes.%d.pattern%d' % (k, j), 348, ci.get('pattern')))
    for i, p in enumerate((co.get('accessory') or {}).get('parts') or []):
        if p.get('type') not in (None, 350): out.append(('accessory.%d' % i, p.get('type'), p.get('id')))
    return [x for x in out if isinstance(x[2], int)]


def main():
    utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--studiocache', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--no-assets', action='store_true', help='paths only (no bundle containers read)')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    L = []
    w = L.append
    ab = abdata_bundles(a.hs2)
    zs = zipmods(a.modcache)
    ship = collections.defaultdict(list)          # lower trimmed path -> [(zipmod, exact path)] in Sideloader's order
    for z in zs:
        for t in z['bundles']:
            ship[t.lower()].append((z, t))
    skipped = collections.Counter()
    for p in [p for p in ab if p in ship]:
        keep = [(z, t) for z, t in ship[p] if loads_on_hs2(z['path'])]
        for z, t in ship[p]:
            if (z, t) not in keep: skipped[z['name'] or os.path.basename(z['path'])] += 1
        ship[p] = keep
        if not keep: del ship[p]
    shadowed = sorted(p for p in ab if p in ship)
    w('# 1. abdata bundles shipped by a zipmod under the same path (Sideloader serves the zipmod copy per asset name)')
    w('abdata bundles: %d; zipmods (one per GUID): %d; zipmod bundle paths: %d; shadowed abdata paths: %d (exact case %d)' % (
        len(ab), len(zs), len(ship), len(shadowed), sum(1 for p in shadowed if any(t == ab[p] for _, t in ship[p]))))
    per = collections.Counter(area(ab[p]) for p in shadowed)
    tot = collections.Counter(area(v) for v in ab.values())
    for k in sorted(tot):
        w('  %-18s %4d of %4d abdata bundles' % (k, per.get(k, 0), tot[k]))
    zc = collections.Counter(z['name'] or os.path.basename(z['path']) for p in shadowed for z, _ in ship[p][:1])
    w('shadowing zipmods (the first copy of each path): ' + '; '.join('%s %d' % kv for kv in zc.most_common()))
    multi = [p for p in shadowed if len(ship[p]) > 1]
    w('paths shipped by more than one zipmod: %d' % len(multi))
    w('zipmods shipping an abdata path but skipped by Sideloader (manifest <game> not HS2): ' + (
        '; '.join('%s %d' % kv for kv in skipped.most_common()) or 'none'))
    # 2. assets overridden
    names = {}
    over = {}
    if not a.no_assets:
        for p in shadowed:
            try:
                va = asset_names_file(os.path.join(a.hs2, 'abdata', ab[p]))
            except Exception as e:  # noqa: BLE001
                va = None
            zn = []
            for z, t in ship[p]:
                try:
                    zn.append((z, t, asset_names_zip(z['path'], t)))
                except Exception:
                    zn.append((z, t, None))
            names[p] = (va, zn)
            # per vanilla asset: the first zipmod copy that contains it (exact case of the trimmed path)
            o = {}
            for n in (va or set()):
                for z, t, s in zn:
                    if t == ab[p] and s and n in s:
                        o[n] = z
                        break
            over[p] = o
        w('')
        w('# 2. assets of the shadowed bundles: overridden = the vanilla asset name is in a zipmod copy of the exact path')
        cnt = collections.Counter()
        for p in shadowed:
            va, zn = names[p]
            cnt['bundles'] += 1
            cnt['vanilla assets'] += len(va or ())
            cnt['overridden assets'] += len(over[p])
            cnt['bundles with every asset overridden'] += 1 if va and len(over[p]) == len(va) else 0
            cnt['bundles with no asset overridden'] += 1 if not over[p] else 0
            cnt['zipmod-only assets (added to the path)'] += len(set().union(*[s for _, _, s in zn if s]) - (va or set()))
        for k, v in cnt.items():
            w('  %s: %d' % (k, v))
        per_o = collections.Counter()
        for p in shadowed:
            per_o[area(ab[p])] += len(over[p])
        w('  overridden assets per area: ' + ', '.join('%s %d' % kv for kv in sorted(per_o.items())))
    # 3. vanilla list rows
    w('')
    w('# 3. vanilla list rows that load from a shadowed bundle (any column naming a .unity3d path; "overridden" = one of the')
    w('#    row\'s asset columns names an asset the zipmod copy overrides)')
    rows = collections.Counter()
    rows_o = collections.Counter()
    van = {}
    seen = set()
    for fn, asset, cat, dist, keys, dl in vanilla_lists(a.hs2):
        for rid, vals in dl.items():
            try: rid = int(rid)
            except Exception: continue
            if (cat, rid) in seen: continue
            seen.add((cat, rid))
            kv = {keys[i]: str(vals[i]) for i in range(min(len(keys), len(vals)))}
            van[(cat, rid)] = kv
            bcols = [v for v in kv.values() if v.lower().endswith('.unity3d') and v.lower() in ab and v.lower() in ship]
            if not bcols: continue
            rows[cat] += 1
            vals_l = {v.lower() for v in kv.values()}
            if any(over.get(b.lower()) and (vals_l & set(over[b.lower()])) for b in bcols):
                rows_o[cat] += 1
    w('  character lists: %d rows (overridden %d); per category: %s' % (sum(rows.values()), sum(rows_o.values()),
      ', '.join('%d %d/%d' % (c, rows_o[c], rows[c]) for c in sorted(rows))))
    st = studio_rows(a.studiocache)
    sc = collections.Counter()
    so = collections.Counter()
    for kind, lst, no, bundle, asset in st:
        b = bundle.lower()
        if b in ab and b in ship:
            sc[kind] += 1
            if asset.lower() in (over.get(b) or {}):
                so[kind] += 1
    w('  studio lists: items %d rows (overridden %d), maps %d rows (overridden %d)' % (sc['item'], so['item'], sc['map'], so['map']))
    # 4. the user's cards
    w('')
    w('# 4. the user\'s cards (UserData/chara/female, male): references to vanilla rows that load from a shadowed bundle')
    shadow_rows = {}
    for (cat, rid), kv in van.items():
        bcols = [v.lower() for v in kv.values() if v.lower().endswith('.unity3d') and v.lower() in ab and v.lower() in ship]
        if not bcols: continue
        vals_l = {v.lower() for v in kv.values()}
        shadow_rows[(cat, rid)] = any(over.get(b) and (vals_l & set(over[b])) for b in bcols)
    for sex, folder in ((1, 'female'), (0, 'male')):
        d = os.path.join(a.hs2, 'UserData', 'chara', folder)
        cards = cards_o = n = 0
        members = collections.Counter()
        for root, _, fs in os.walk(d):
            for fname in fs:
                if not fname.lower().endswith('.png'): continue
                try:
                    c = load_card(os.path.join(root, fname))
                except Exception:
                    continue
                if not c or 'custom' not in c: continue
                n += 1
                mods = {(ri.get('CategoryNo'), ri.get('Slot')) for ri in resolver_info(c)}
                hit = hit_o = False
                for m, cat, rid in card_refs(c, sex):
                    if (cat, rid) in mods or (cat, rid) not in shadow_rows: continue
                    hit = True
                    if shadow_rows[(cat, rid)]:
                        hit_o = True
                        members[re.sub(r'\d+', 'N', m)] += 1
                cards += hit
                cards_o += hit_o
        w('  %s: %d cards read; %d reference a vanilla row on a shadowed bundle; %d an overridden asset; members: %s' % (
            folder, n, cards, cards_o, ', '.join('%s %d' % kv for kv in members.most_common(12))))
    # tsv
    with open(os.path.join(a.out, 'shadowed.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        o.write('path\tarea\tzipmods\tvanilla assets\toverridden\tzipmod-only\n')
        for p in shadowed:
            va, zn = names.get(p, (None, []))
            extra = len(set().union(*[s for _, _, s in zn if s]) - (va or set())) if zn else ''
            o.write('%s\t%s\t%s\t%s\t%s\t%s\n' % (ab[p], area(ab[p]), ' | '.join(z['name'] or os.path.basename(z['path']) for z, _ in ship[p]),
                                                  len(va) if va is not None else '', len(over.get(p) or ()) if not a.no_assets else '', extra))
    text = '\n'.join(L) + '\n'
    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8', newline='\n') as o:
        o.write(text)
    print(text)


if __name__ == '__main__':
    main()
