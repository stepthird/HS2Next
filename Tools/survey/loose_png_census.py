# Loose-PNG census (Phase 4.2 package L, gap GB10): the list rows whose
# texture HS2 gets from a Sideloader "loose PNG" — a zipmod file abdata/<bundle path without .unity3d>/<asset>.png that
# Sideloader's asset-load hook hands out for a texture request, whether or not the bundle exists. Read-only; zip directory
# listings and the zipmod scan cache only, plus the 24-byte PNG header (width, height, bit depth, colour type) of the
# referenced files. No image is decoded.
#
# The rule (Sideloader, behaviour only; see the spec section):
#   - every zip entry of a loaded zipmod (one per GUID) whose name starts with "abdata/" and ends with ".png" is a loose PNG,
#     keyed by its exact name (case-sensitive); the first zipmod in GUID load order (lower-cased ordinal) keeps a duplicate;
#   - a texture request for (bundle B, asset A) looks up "abdata/" + B with every ".unity3d" removed (any case) + "/" + A +
#     ".png" exactly; a hit wins over the bundle's own asset (also when the bundle exists);
#   - a bundle path that no source ships but whose folder holds a loose PNG "exists" (an empty bundle) for the game's checks.
# Rows: the vanilla character lists (abdata/list/characustom, every row) and the zipmod rows of the scan cache with the
# catalog's de-duplication (a later zipmod of the same (category, GUID, id) wins, inside a zipmod the first row). The
# (bundle column, asset column) pairs tried are the texture requests HS2 makes from list rows (TEXTURE_PAIRS: thumbnails,
# the MainAB textures, patterns, hair textures, clothes masks); a PNG that only matches another pair (a prefab or text
# column, or a thumbnail name under the main bundle) is counted apart: it is never requested as a texture.
#
#   python -I Tools/survey/loose_png_census.py --hs2 <HS2 root> --modcache <Cache/modlists-*.tsv> --out <dir>
# Writes <out>/report.txt (the committed copy: Tools/survey/loose_png_census.txt) and <out>/refs.tsv (one line per loose
# texture reference, read by Editor/LooseTextureTest.cs):
#   category  ref(guid#id)  bundleColumn  assetColumn  bundle  asset  pngEntry  pngGuid  method  size  crc32  width  height
#   bitDepth  colourType  clamp  bundleExists  thumb
import sys, os, re, argparse, collections, zipfile, struct, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

VANILLA = 'illusion.hs2'
NOT_ASSET = {'ID', 'Kind', 'Name', 'Possess', 'EN_US', 'ZH_CN', 'ZH_TW', 'StateType', 'HeadID', 'KokanHide', 'Parent',
             'Coordinate', 'BreakDisableMask', 'SetHair', 'NotBra', 'RingOff', 'OverBraType', 'CenterX', 'MoveX', 'CenterY',
             'MoveY', 'CenterScale', 'AddScale'}
UNITY3D = re.compile(re.escape('.unity3d'), re.IGNORECASE)
CATS = {111: 'face skin M', 211: 'face skin F', 112: 'face detail M', 212: 'face detail F', 131: 'body skin M',
        231: 'body skin F', 132: 'body detail M', 232: 'body detail F', 133: 'sunburn M', 233: 'sunburn F', 313: 'paint',
        314: 'eyebrow', 315: 'eyelashes', 316: 'eyeshadow', 317: 'iris', 318: 'pupil', 319: 'eye highlight', 320: 'cheek',
        322: 'lip', 323: 'mole', 334: 'nipple', 335: 'underhair', 121: 'beard', 7: 'face paint region', 8: 'body paint region'}


def utf8():
    try: sys.stdout.reconfigure(encoding='utf-8')
    except Exception: pass


# The texture requests of HS2 from list rows (bundle column -> asset columns; CommonLib.LoadAsset<Texture2D>).
TEXTURE_PAIRS = {
    'ThumbAB': ['ThumbTex', 'Preset'],   # Preset: the face preset grid's picture (head rows)
    'MainAB': ['MainTex', 'MainTex02', 'MainTex03', 'ColorMaskTex', 'ColorMask02Tex', 'ColorMask03Tex', 'AddTex', 'GlossTex',
               'OcclusionMapTex', 'NormalMapTex'],
    'MainTexAB': ['MainTex'],
    'TexAB': ['TexC', 'TexD'],
    'OverBodyMaskAB': ['OverBodyMask'], 'OverBraMaskAB': ['OverBraMask'], 'OverBodyBMaskAB': ['OverBodyBMask'],
    'OverInnerTBMaskAB': ['OverInnerTBMask'], 'OverInnerBMaskAB': ['OverInnerBMask'], 'OverPanstMaskAB': ['OverPanstMask'],
}


def is_bundle_col(c): return c.endswith('AB')
def is_manifest_col(c): return 'manifest' in c.lower() or 'mainfest' in c.lower()
def empty(v): return v is None or v == '' or v == '0'


def bundle_assets(abdata, by_guid, bundle):
    """Lower-cased asset names of a bundle (container file names without extension and texture / sprite object names),
    read with UnityPy from the abdata file or the first zipmod that ships it; None when unreadable."""
    import UnityPy
    rel = bundle.replace('\\', '/')
    data = None
    f = os.path.join(abdata, rel)
    if os.path.isfile(f):
        data = f
    else:
        for z in sorted(by_guid.values(), key=lambda z: z['guid'].lower()):
            if any(x.lower() == rel.lower() for x in z['files']):
                with zipfile.ZipFile(z['path']) as zf:
                    n = next(n for n in zf.namelist() if n.lower() == ('abdata/' + rel).lower())
                    data = zf.read(n)
                break
    if data is None: return None
    try:
        env = UnityPy.load(data)
    except Exception:
        return None
    names = set()
    for path in env.container.keys():
        names.add(os.path.splitext(path.rsplit('/', 1)[-1])[0].lower())
    for obj in env.objects:
        if obj.type.name in ('Texture2D', 'Sprite'):
            try: names.add(obj.read().m_Name.lower())
            except Exception: pass
    return names


def read_cache(modcache):
    """Zipmods in scan order: dict(path, guid, files (abdata-relative, as cached), rows [(cat, id, {col: value})])."""
    zs, cur = [], None
    with open(modcache, encoding='utf-8') as f:
        head = f.readline().rstrip('\n')
        if not head.startswith('#hs2next-modlists'): raise SystemExit('not a zipmod scan cache: ' + modcache)
        for line in f:
            p = line.rstrip('\n').split('\t')
            if p[0] == 'Z':
                cur = {'path': p[1], 'guid': p[3] if len(p) > 3 and p[3] else None, 'files': [], 'rows': []}
                zs.append(cur)
            elif cur is None: continue
            elif p[0] == 'B': cur['files'].append(p[1])
            elif p[0] == 'E' and len(p) >= 3:
                cur['rows'].append((int(p[1]), int(p[2]), dict(zip(p[3::2], p[4::2]))))
    return zs


def vanilla_rows(hs2):
    from maker_core_census import vanilla_lists
    rows = {}
    for fn, asset, cat, dist, keys, dl in vanilla_lists(hs2):
        for rid, vals in dl.items():
            try: rid = int(rid)
            except Exception: continue
            k = (cat, VANILLA, rid)
            if k in rows: continue   # the first definition of an id wins
            rows[k] = {keys[i]: str(vals[i]) for i in range(min(len(keys), len(vals)))}
    return rows


def png_header(zf, info):
    """(width, height, bit depth, colour type) from the IHDR chunk; (0, 0, 0, -1) for JPEG data (FF D8 FF: LoadImage reads it
    as a JPEG); None otherwise."""
    try:
        with zf.open(info) as f: b = f.read(33)
    except Exception:
        return None
    if b[:3] == b'\xff\xd8\xff': return 0, 0, 0, -1
    if len(b) < 26 or b[:8] != b'\x89PNG\r\n\x1a\n' or b[12:16] != b'IHDR': return None
    w, h = struct.unpack('>II', b[16:24])
    return w, h, b[24], b[25]


def main():
    utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--hs2', required=True)
    ap.add_argument('--modcache', required=True)
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    zs = read_cache(a.modcache)
    rep = []
    w = rep.append

    # ---- the zipmods HS2Next uses: one per GUID (the last file of the scan, as ContentCatalog.ZipmodsByGuid)
    by_guid = {}
    for z in zs:
        if z['guid']: by_guid[z['guid'].lower()] = z
    load_order = sorted(by_guid.values(), key=lambda z: z['guid'].lower())
    abdata = os.path.join(a.hs2, 'abdata')

    # ---- loose PNGs: exact zip entry names (zip directory listings of the zipmods that hold any .png under abdata/)
    pngs = {}                  # exact abdata-relative name -> (zipmod, ZipInfo)
    dup_same = dup_diff = 0
    prefix_case = ext_case = 0
    listed = 0
    zinfo = {}
    for z in load_order:
        if not any(f.lower().endswith('.png') for f in z['files']): continue
        try:
            zf = zipfile.ZipFile(z['path'])
        except Exception:
            continue
        listed += 1
        zinfo[z['path']] = zf
        for info in zf.infolist():
            n = info.filename.replace('\\', '/')
            if not n.lower().startswith('abdata/') or not n.lower().endswith('.png') or n.endswith('/'): continue
            if not n.startswith('abdata/'): prefix_case += 1; continue   # Sideloader looks up "abdata/" in lower case
            if not n.endswith('.png'): ext_case += 1; continue           # ... and ".png" in lower case
            rel = n[7:]
            if rel in pngs:
                if pngs[rel][1].CRC == info.CRC and pngs[rel][1].file_size == info.file_size: dup_same += 1
                else: dup_diff += 1
                continue
            pngs[rel] = (z, info)
    folders = collections.Counter(rel.rsplit('/', 1)[0] for rel in pngs)

    # ---- bundle existence as HS2Next sees it (case-insensitive: a real abdata file or any zipmod's entry)
    shipped = set()
    for z in by_guid.values():
        for f in z['files']:
            if f.lower().endswith('.unity3d'): shipped.add(f.lower())
    exists_memo = {}

    def bundle_exists(b):
        k = b.replace('\\', '/').lower()
        if k not in exists_memo: exists_memo[k] = k in shipped or os.path.isfile(os.path.join(abdata, b.replace('\\', '/')))
        return exists_memo[k]

    # ---- rows (catalog de-duplication)
    rows = vanilla_rows(a.hs2)
    nvan = len(rows)
    mod = {}
    for zi, z in enumerate(zs):
        if not z['guid'] or z['guid'].lower() == VANILLA: continue
        seen = set()
        for cat, rid, kv in z['rows']:
            k = (cat, z['guid'], rid)
            if k in seen: continue      # inside a zipmod the first row of an id wins
            seen.add(k)
            mod[k] = (zi, kv)           # a later zipmod wins
    for k, (zi, kv) in mod.items(): rows[k] = kv
    per_cat_rows = collections.Counter(k[0] for k in rows if k[1] != VANILLA)

    refs = []
    case_only = 0
    backslash = 0
    other_pairs = collections.Counter()
    lower_index = {rel.lower(): rel for rel in pngs}
    for (cat, guid, rid), kv in rows.items():
        for bc, bv in kv.items():
            if not is_bundle_col(bc) or empty(bv): continue
            stem = UNITY3D.sub('', bv)
            for ac, av in kv.items():
                if ac == bc or is_bundle_col(ac) or is_manifest_col(ac) or ac in NOT_ASSET or empty(av): continue
                key = stem + '/' + av + '.png'
                hit = pngs.get(key)
                if ac not in TEXTURE_PAIRS.get(bc, ()):
                    if hit is not None: other_pairs['%s/%s' % (bc, ac)] += 1
                    continue
                if hit is None:
                    if key.lower() in lower_index: case_only += 1
                    elif '\\' in key and key.replace('\\', '/') in pngs: backslash += 1
                    continue
                z, info = hit
                hdr = png_header(zinfo[z['path']], info) or (0, 0, 0, 0)
                refs.append((cat, '%s#%d' % (guid, rid), bc, ac, bv, av, 'abdata/' + key, z['guid'], info.compress_type,
                             info.file_size, info.CRC, hdr[0], hdr[1], hdr[2], hdr[3], int('clamp' in 'abdata/' + key),
                             int(bundle_exists(bv)), int(bc == 'ThumbAB')))
    for zf in zinfo.values(): zf.close()

    # does a bundle that exists also hold the asset the PNG replaces? (UnityPy, asset names only)
    held = {}
    for b in sorted({r[4] for r in refs if r[16]}):
        held[b] = bundle_assets(abdata, by_guid, b)
    overrides = [r for r in refs if r[16] and held.get(r[4]) is not None and r[5].lower() in held[r[4]]]
    unreadable = sorted(b for b, n in held.items() if n is None)

    refs.sort(key=lambda r: (r[0], r[1], r[2], r[3]))
    with open(os.path.join(a.out, 'refs.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('category\tref\tbundleColumn\tassetColumn\tbundle\tasset\tpngEntry\tpngGuid\tmethod\tsize\tcrc32\twidth\theight\t'
                'bitDepth\tcolourType\tclamp\tbundleExists\tthumb\n')
        for r in refs: f.write('\t'.join(str(x) for x in r) + '\n')

    # ---- report
    w('Loose-PNG census (Phase 4.2 package L, GB10). Zip directory listings + the zipmod scan cache; PNG headers only, nothing decoded.')
    w('zipmods in the scan cache %d, one per GUID %d; zipmods with a .png under abdata/ listed %d' % (len(zs), len(by_guid), listed))
    w('loose PNG entries (exact "abdata/...png", first in GUID load order): %d in %d folders; duplicates in later zipmods %d '
      '(same CRC and size %d, different %d)' % (len(pngs), len(folders), dup_same + dup_diff, dup_same, dup_diff))
    w('entries never matched by Sideloader (lookup is "abdata/" + ... + ".png" in lower case): prefix not "abdata/" %d, '
      'extension not ".png" %d' % (prefix_case, ext_case))
    only = sum(1 for f in folders if not bundle_exists(f + '.unity3d'))
    w('PNG folders without a bundle "<folder>.unity3d" anywhere (Sideloader\'s empty-bundle folders): %d of %d' % (only, len(folders)))
    w('rows: vanilla %d, zipmod (distinct keys) %d' % (nvan, len(rows) - nvan))
    w('references that match only case-insensitively (no texture in HS2): %d; only after "\\" -> "/": %d' % (case_only, backslash))
    w('')
    w('loose texture references: %d (distinct rows %d)' % (len(refs), len({(r[0], r[1]) for r in refs})))
    w('  bundle also exists (the PNG wins): %d references in %d bundles (%s); of them the bundle also holds the asset: %d%s'
      % (sum(r[16] for r in refs), len(held), ', '.join(sorted(held)), len(overrides), (' (unreadable: %s)' % unreadable) if unreadable else ''))
    w('  thumbnails (ThumbAB): %d; clamp in the path: %d; vanilla rows: %d'
      % (sum(r[17] for r in refs), sum(r[15] for r in refs), sum(1 for r in refs if r[1].startswith(VANILLA + '#'))))
    w('  PNGs that match only a pair HS2 never requests as a texture (not counted): ' + (', '.join('%s %d' % kv for kv in sorted(other_pairs.items())) or 'none'))
    w('')
    w('per category: distinct rows with a loose texture of the category\'s mod rows; references by column pair')
    by_cat = collections.defaultdict(list)
    for r in refs: by_cat[r[0]].append(r)
    for cat in sorted(by_cat):
        rs = by_cat[cat]
        pairs = collections.Counter('%s/%s' % (r[2], r[3]) for r in rs)
        w('  %4d %-16s rows %5d of %5d mod rows; refs %5d: %s; bundle exists %d, thumbs %d, clamp %d' % (
            cat, CATS.get(cat, ''), len({r[1] for r in rs}), per_cat_rows.get(cat, 0), len(rs),
            ', '.join('%s %d' % kv for kv in sorted(pairs.items())), sum(r[16] for r in rs), sum(r[17] for r in rs), sum(r[15] for r in rs)))
    w('')
    sizes = collections.Counter('%dx%d' % (r[11], r[12]) for r in refs)
    w('PNG sizes: ' + ', '.join('%s %d' % kv for kv in sizes.most_common(12)))
    ct = collections.Counter('depth %d type %d' % (r[13], r[14]) for r in refs)
    w('PNG bit depth / colour type (2 RGB, 6 RGBA, 3 palette, 0 grey, 4 grey+alpha; type -1 = JPEG data named .png, type 0 depth 0 = '
      'neither): ' + ', '.join('%s %d' % kv for kv in sorted(ct.items())))
    w('PNG entries stored %d, deflated %d' % (sum(1 for r in refs if r[8] == 0), sum(1 for r in refs if r[8] == 8)))
    text = '\n'.join(rep) + '\n'
    with open(os.path.join(a.out, 'report.txt'), 'w', encoding='utf-8', newline='\n') as f: f.write(text)
    print(text)


if __name__ == '__main__':
    main()
