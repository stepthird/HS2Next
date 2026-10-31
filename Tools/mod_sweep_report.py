# The mod-wide sweep's work list and failure report (Phase 5.2). Unity-free: reads the
# zipmod scan cache (Cache/modlists-<source>-<hash>.tsv; no zip, bundle or image is opened), the sweep's own outputs
# (Editor/ModSweepTest.cs), the item and map corpus rows files, and the texture check's verdict cache.
#
#   python -I Tools/mod_sweep_report.py worklist [--cache FILE] [--census FILE] [--dir DIR] [--crash-rows FILE]
#       writes <DIR>/worklist.tsv and zipmods.tsv (default DIR Spike/out/modsweep/<yyyymmdd-hhmm>) and prints SW1: the work
#       list's rows per kind against Tools/survey/zipmod_content_census.txt (exit 1 when they differ). --crash-rows writes a
#       work list of only the item rows of an item corpus crash list (lines "<guid>#<id>|<group>|<category>|<bundle>|...",
#       Cache/items/corpus_crashes.txt): each row alone in a fresh process with ModSweepTest's -oneRow (Step 0 L0e). --sw3
#       writes the crash fixture's work list: two part rows, a row that ends the process natively, two loose rows (SW3).
#       --graphics-from SWEEPDIR [--verdicts FILE] [--every 100] writes the graphics pass's work list (default DIR
#       <SWEEPDIR>_graphics): every swept row whose bundles the texture check patched, plus one in --every of the other rows
#       that loaded, per stratum (kind, abdata or zipmod copy, zipmod size class); ModSweepTest runs it with a graphics device.
#   python -I Tools/mod_sweep_report.py status --dir DIR
#       one line: "done", "more <rows left>" or "isolate <row>" (what the next sweep process does), and SW2 so far.
#   python -I Tools/mod_sweep_report.py player-rows --dir DIR --out FILE
#       the item rows of class crash as "<guid>#<no>|<group>|<category>" lines: the input of Tools/mod_sweep_player.py.
#   python -I Tools/mod_sweep_report.py header-rows [--dir DIR] --out FILE
#       the item rows of the bundles whose UnityFS header names a Unity release type other than f (the census of every
#       bundle's header in its comment lines): the input of a player pass over them (Tools/mod_sweep_player.py --rows).
#   python -I Tools/mod_sweep_report.py report --dir DIR [--items DIR] [--maps GLOB] [--verdicts FILE] [--baseline FILE]
#                                              [--summary FILE] [--graphics FILE] [--player FILE] [--graphics-dir DIR]
#                                              [--hs2 FILE] [--reset-maxcount CLASS,...] [--final] [--player-header FILE]
#       --graphics-dir: the graphics pass (SW5: its RESULT line, its rows against the headless rows); --hs2: HS2 evidence
#       (default Tools/survey/mod_sweep_hs2.tsv); --final: the item / map corpora read are the final ones (SW7 can pass);
#       --player-header: the report of the player pass over the header-rows rows (mod_sweep_player.py --one-player); its
#       deaths count in SW7 (the player is the official load path), split into first-use deaths (the crash quarantine
#       refuses the bundle afterwards) and deaths after the first.
#       writes <DIR>/report.tsv (one line per zipmod) and <DIR>/summary.txt (classes x zipmods, the threshold line, the top
#       classes, the files outside the zipmod set, SW2 / SW4 / SW6 / SW7 / SW8); --summary also writes the summary to that
#       file (the committed copy: zipmod file names and numbers only, no paths, no row names).
#
# Kinds (one work-list row each): part (a character-list entry: every asset column loaded, the prefab instantiated), item
# (a Studio item row: the item corpus is the input, the sweep loads only rows the corpus skipped), map (a map row: the map
# corpus is the input), anime (a Studio anime row: the controller loaded, its clips counted), loose (a loose PNG entry:
# decoded and freed), bundle (a bundle no list row names: opened only).
# Classes (one per row, the first that applies): crash, deviceLoss, exception, textureRefused, refused (the pre-load check
# stopped the load with a finding: an asset that needs a truncated mesh, a quarantined bundle), nullLoad (unexplained: they
# count in M), badPrefab, missingAsset, missingBundle, asHS2 (explained), and outside (a file the zipmod loader does not
# load). Metrics (not failures): shader fallback slots, slow loads (> 2 s).
import argparse
import collections
import glob
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CENSUS = os.path.join(HERE, 'survey', 'zipmod_content_census.txt')
SUMMARY = os.path.join(HERE, 'survey', 'mod_sweep_summary.txt')

KINDS = ('part', 'item', 'map', 'anime', 'loose', 'bundle')
SWEPT = ('part', 'anime', 'loose', 'bundle', 'fixture')   # kinds the sweep process loads itself
UNEXPLAINED = ('crash', 'deviceLoss', 'exception', 'textureRefused', 'refused', 'nullLoad')
EXPLAINED = ('badPrefab', 'missingAsset', 'missingBundle', 'asHS2')
CLASSES = UNEXPLAINED + EXPLAINED
THRESHOLD_M = 15


def cat_group(c):
    # character list categories (the census's grouping)
    c = int(c)
    if 240 <= c <= 247: return 'clothes'
    if 300 <= c <= 303: return 'hair'
    if 350 <= c <= 363: return 'accessories'
    if c == 348: return 'patterns'
    return 'face/body'


def studio_kind(name):
    n = os.path.basename(name)
    for k in ('ItemList', 'ItemGroup', 'ItemCategory', 'ItemBoneList', 'AnimeList', 'AnimeGroup', 'AnimeCategory', 'HAnimeList', 'Map',
              'VoiceList', 'VoiceGroup', 'VoiceCategory', 'BGM', 'Filter', 'Probe', 'Light', 'ColorGrading', 'Sky', 'Bone'):
        if n.startswith(k): return k
    if n.startswith('Anime_'): return 'AnimeList'
    if n.startswith('Voice_'): return 'VoiceList'
    return 'other:' + n.split('_')[0]


def norm_bundle(p):
    p = (p or '').replace('\\', '/').strip()
    if p.lower().startswith('abdata/'): p = p[7:]
    return p


def num(x):
    """A list cell as the list reader takes it: an integer without leading zeros (text left as is)."""
    x = (x or '').strip()
    return str(int(x)) if re.match(r'^[+-]?\d+$', x) else x


def lower_ordinal_key(s):
    return (s or '').lower()


def newest_cache():
    files = glob.glob(os.path.join(ROOT, 'Cache', 'modlists-*.tsv'))
    if not files: sys.exit('no scan cache under Cache/ (start the app or a catalog test once)')
    return max(files, key=os.path.getmtime)


def clean(s):
    return (s or '').replace('\t', ' ').replace('\n', ' ').replace('\r', ' ')


# ------------------------------------------------------------------ the scan cache

def read_cache(path):
    """Zipmods in file order: dicts with path, stamp, guid, version, entries [(cat, id, {col: val})], studio rows, B paths."""
    Z, cur, lastS = [], None, None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            t = line.rstrip('\n').split('\t')
            k = t[0]
            if k == 'Z':
                cur = {'path': t[1], 'stamp': t[2] if len(t) > 2 else '', 'guid': t[3] if len(t) > 3 else '', 'version': '', 'name': '',
                       'entries': [], 'studio': [], 'B': [], 'maps': 0}
                Z.append(cur); lastS = None
            elif cur is None:
                continue
            elif k == 'N':
                cur['name'] = t[1] if len(t) > 1 else ''
                cur['version'] = t[4] if len(t) > 4 else ''   # N: name, author, website, version
            elif k == 'E' and len(t) > 2:
                cols = {}
                for i in range(3, len(t) - 1, 2): cols[t[i]] = t[i + 1]
                cur['entries'].append((int(t[1]), t[2], cols))
            elif k == 'S' and len(t) > 1:
                lastS = (t[1], studio_kind(t[1]))
            elif k == 'C' and lastS and len(t) > 1 and re.match(r'-?\d+$', t[1] or ''):
                cur['studio'].append((lastS[0], lastS[1], t[1:]))
            elif k == 'B' and len(t) > 1:
                cur['B'].append(t[1])
            elif k == 'M':
                cur['maps'] += 1
    return Z


_refs = {}


def refs_of(Z):
    """Every bundle path a row of any zipmod names (lower case): the *AB columns of character-list entries, the bundle
    column of item, map and anime rows."""
    if id(Z) in _refs: return _refs[id(Z)]
    refs = set()
    for z in Z:
        for cat, cid, cols in z['entries']:
            for c, v in cols.items():
                if c.endswith('AB') and v and v != '0': refs.add(norm_bundle(v).lower())
        for src, kind, v in z['studio']:
            if kind == 'ItemList' and len(v) > 5: refs.add(norm_bundle(v[5]).lower())
            elif kind == 'Map' and len(v) > 2: refs.add(norm_bundle(v[2]).lower())
            elif kind in ('AnimeList', 'HAnimeList') and len(v) > 5: refs.add(norm_bundle(v[5]).lower())
    _refs[id(Z)] = refs
    return refs


def build_worklist(Z):
    """Rows (kind, zi, key, bundle, asset, extra) in GUID load order (lower-case ordinal, then file order)."""
    order = sorted(range(len(Z)), key=lambda i: (lower_ordinal_key(Z[i]['guid']), i))
    refs = refs_of(Z)
    rows = []
    for zi in order:
        z = Z[zi]
        for ei, (cat, cid, cols) in enumerate(z['entries']):
            rows.append(('part', zi, '%d/%s' % (cat, num(cid)), '', '', str(ei)))
        ords = collections.Counter()
        for src, kind, v in z['studio']:
            if kind == 'ItemList':
                key = '%s/%s/%s' % (num(v[1]) if len(v) > 1 else '', num(v[2]) if len(v) > 2 else '', num(v[0]))
                ords[(kind, key)] += 1
                rows.append(('item', zi, key, norm_bundle(v[5]) if len(v) > 5 else '', v[6] if len(v) > 6 else '', '%d' % ords[(kind, key)]))
        for src, kind, v in z['studio']:
            if kind == 'Map':
                key = num(v[0])
                ords[(kind, key)] += 1
                rows.append(('map', zi, key, norm_bundle(v[2]) if len(v) > 2 else '', v[3] if len(v) > 3 else '', '%d' % ords[(kind, key)]))
        for src, kind, v in z['studio']:
            if kind in ('AnimeList', 'HAnimeList'):
                key = '%s/%s/%s' % (num(v[2]) if len(v) > 2 else '', num(v[3]) if len(v) > 3 else '', num(v[1]) if len(v) > 1 else '')
                ords[(kind, key)] += 1
                rows.append(('anime', zi, key, norm_bundle(v[5]) if len(v) > 5 else '', v[6] if len(v) > 6 else '', '%d' % ords[(kind, key)]))
        for p in z['B']:
            if p.lower().endswith('.png'):
                folder, _, fname = p.rpartition('/')
                rows.append(('loose', zi, p, (folder + '.unity3d') if folder else '', fname[:-4], ''))
        for p in z['B']:
            if p.lower().endswith('.unity3d') and norm_bundle(p).lower() not in refs:
                rows.append(('bundle', zi, p, p, '', ''))
    return rows, order


def census_numbers(path):
    """The per-kind numbers of zipmod_content_census.txt."""
    txt = open(path, encoding='utf-8').read()
    n = {}
    m = re.search(r'character-list rows: (\d+) \(([^)]*)\)', txt)
    n['part'] = int(m.group(1))
    for g, c in re.findall(r'([a-z/]+) (\d+)', m.group(2)): n['part:' + g] = int(c)
    m = re.search(r'Studio list data rows: (.*)', txt)
    sr = dict((k, int(v)) for k, v in re.findall(r'(\w+) (\d+)', m.group(1)))
    n['item'] = sr.get('ItemList', 0)
    n['map'] = sr.get('Map', 0)
    n['anime'] = sr.get('AnimeList', 0) + sr.get('HAnimeList', 0)
    m = re.search(r'bundles \(\.unity3d entries\) (\d+); loose PNG entries (\d+)', txt)
    n['bundles'] = int(m.group(1)); n['loose'] = int(m.group(2))
    m = re.search(r'no list rows \(bundles only\)\s+(\d+)', txt)
    n['bundlesOnlyZipmods'] = int(m.group(1)) if m else 0
    m = re.search(r'zipmod files in the cache: (\d+)', txt)
    n['zipmods'] = int(m.group(1))
    return n


def crash_rows(Z, rows, path):
    """The item rows of an item corpus crash list, in the list's order (the zipmod file that ships the row's bundle)."""
    want = []
    for line in open(path, encoding='utf-8', errors='replace'):
        t = line.strip().split('|')
        m = re.match(r'(.+)#(-?\d+)$', t[0]) if t and t[0] else None
        if m and len(t) >= 4: want.append((m.group(1).lower(), num(m.group(2)), num(t[1]), num(t[2]), norm_bundle(t[3]).lower()))
    out = []
    for guid, no, g, c, b in want:
        hit = [r for r in rows if r[0] == 'item' and Z[r[1]]['guid'].lower() == guid and r[2] == '%s/%s/%s' % (g, c, no)]
        hit.sort(key=lambda r: 0 if r[3].lower() == b else 1)
        if hit: out.append(hit[0])
        else: print('WARN crash row not in the scan cache: %s#%s %s/%s' % (guid, no, g, c))
    return out


def graphics_rows(src, verdicts_path, every):
    """The graphics pass's rows of a finished sweep: every swept row whose bundles the texture check patched, plus every
    <every>-th other row that loaded (default 100: 1 %) per stratum (kind, abdata or zipmod copy, zipmod size class)."""
    rows = read_worklist(src)
    Z = read_zipmods(src)
    res = read_results(src)
    verdicts = read_verdicts(verdicts_path)
    cb = corpus_crash_bundles()
    # the patched bundles: a patch verdict, an entry of the repair index (a patched bundle is served from its repaired copy), or
    # a row flagged patched / repaired by the sweep
    repaired = set()
    rp = os.path.join(ROOT, 'Cache', 'texguard', 'repaired.tsv')
    if os.path.isfile(rp):
        for line in open(rp, encoding='utf-8', errors='replace'):
            if not line.startswith('#'): repaired.add(line.split('\t')[0].lower())
    patched, rest = [], collections.defaultdict(list)
    for r in rows:
        own = res.get(r['idx'])
        if not swept(r, cb) or r['kind'] == 'fixture' or not own: continue
        x = own[-1]
        if x['cls'] not in ('ok', ''): continue
        keys = [k for k in (x['keys'] or '').split('|') if k]
        fl = flags_of(x)
        if 'patched' in fl or 'repaired' in fl or any(k.lower() in repaired or verdicts.get(k.lower(), ('',))[0].startswith('patch') for k in keys):
            patched.append(r); continue
        size = int((Z[r['zi']]['stamp'] or '0:').split(':')[0] or 0)
        sc = 'small' if size < 10e6 else 'medium' if size < 100e6 else 'large'
        rest[(r['kind'], 'abdata' if keys and all(k.startswith('abdata:') for k in keys) else 'zipmod', sc)].append(r)
    sample = []
    for st in sorted(rest):
        sample.extend(rest[st][::every])
    pick = sorted(patched + sample, key=lambda r: r['idx'])
    return pick, Z, len(patched), len(sample), len(rest)


def cmd_worklist(a):
    if a.graphics_from:
        pick, Z, npatched, nsample, nstrata = graphics_rows(a.graphics_from, a.verdicts or os.path.join(ROOT, 'Cache', 'texcheck', 'verdicts-v1.tsv'), a.every)
        d = a.dir or os.path.join(os.path.normpath(a.graphics_from) + '_graphics')
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, 'zipmods.tsv'), 'w', encoding='utf-8', newline='\n') as f:
            f.write('zi\tpath\tstamp\tguid\tversion\n')
            for z in Z: f.write('%d\t%s\t%s\t%s\t%s\n' % (z['zi'], z['path'], z['stamp'], z['guid'], z['version']))
        with open(os.path.join(d, 'worklist.tsv'), 'w', encoding='utf-8', newline='\n') as f:
            f.write('#hs2next-modsweep-worklist\t1\tgraphics pass of %s\n' % os.path.basename(os.path.normpath(a.graphics_from)))
            f.write('idx\tkind\tzi\tkey\tbundle\tasset\textra\n')
            for r in pick:
                f.write('%d\t%s\t%d\t%s\t%s\t%s\t%s\n' % (r['idx'], r['kind'], r['zi'], r['key'], r['bundle'], r['asset'], r['extra']))
        print('graphics pass work list: %d rows (%d with patched bundles, %d sampled 1 in %d over %d strata) -> %s'
              % (len(pick), npatched, nsample, a.every, nstrata, os.path.join(d, 'worklist.tsv')))
        return 0
    cache = a.cache or newest_cache()
    Z = read_cache(cache)
    rows, order = build_worklist(Z)
    if a.crash_rows:
        rows = crash_rows(Z, rows, a.crash_rows)
    elif a.sw3:
        parts = [r for r in rows if r[0] == 'part'][:2]
        loose = [r for r in rows if r[0] == 'loose'][:2]
        rows = parts + [('fixture', -1, 'crash', '', '', '')] + loose
    d = a.dir or os.path.join(ROOT, 'Spike', 'out', 'modsweep', time.strftime('%Y%m%d-%H%M'))
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, 'zipmods.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('zi\tpath\tstamp\tguid\tversion\n')
        for zi, z in enumerate(Z):
            f.write('%d\t%s\t%s\t%s\t%s\n' % (zi, z['path'], z['stamp'], clean(z['guid']), clean(z['version'])))
    with open(os.path.join(d, 'worklist.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('#hs2next-modsweep-worklist\t1\t%s\n' % os.path.basename(cache))
        f.write('idx\tkind\tzi\tkey\tbundle\tasset\textra\n')
        for i, (kind, zi, key, b, asset, extra) in enumerate(rows):
            f.write('%d\t%s\t%d\t%s\t%s\t%s\t%s\n' % (i, kind, zi, clean(key), clean(b), clean(asset), clean(extra)))
    if a.crash_rows or a.sw3:
        print('%s work list: %d rows -> %s' % ('crash-list' if a.crash_rows else 'SW3 fixture', len(rows), os.path.join(d, 'worklist.tsv')))
        return 0
    # SW1
    cn = census_numbers(a.census or CENSUS)
    have = collections.Counter(r[0] for r in rows)
    parts = collections.Counter(cat_group(Z[r[1]]['entries'][int(r[5])][0]) for r in rows if r[0] == 'part')
    # the census's "no list rows (bundles only)": no character-list entry, no item / anime / map row, no loose PNG, a bundle;
    # each of them is covered when every bundle it ships is a bundle row or is named by a row of another zipmod
    def listless(z):
        kinds = set(k for _, k, _ in z['studio'])
        return (not z['entries'] and not kinds & {'ItemList', 'AnimeList', 'HAnimeList', 'Map'} and not z.get('maps')
                and not any(p.lower().endswith('.png') for p in z['B']) and any(p.lower().endswith('.unity3d') for p in z['B']))
    zip_bundle_rows = set(r[1] for r in rows if r[0] == 'bundle')
    bundle_row_paths = set((r[1], r[2]) for r in rows if r[0] == 'bundle')
    listless_z = [zi for zi, z in enumerate(Z) if listless(z)]
    covered = sum(1 for zi in listless_z if all((zi, p) in bundle_row_paths or norm_bundle(p).lower() in refs_of(Z)
                                                for p in Z[zi]['B'] if p.lower().endswith('.unity3d')))
    lines, ok = [], True

    def chk(name, got, want):
        nonlocal ok
        good = got == want
        ok &= good
        lines.append('%s SW1 %-34s work list %7d  census %7d' % ('PASS' if good else 'FAIL', name, got, want))

    chk('zipmod files', len(Z), cn['zipmods'])
    chk('character-list rows', have['part'], cn['part'])
    for g in ('face/body', 'clothes', 'accessories', 'hair', 'patterns'):
        chk('  ' + g, parts[g], cn.get('part:' + g, 0))
    chk('Studio item rows', have['item'], cn['item'])
    chk('anime rows', have['anime'], cn['anime'])
    chk('map rows', have['map'], cn['map'])
    chk('loose PNG entries', have['loose'], cn['loose'])
    chk('bundles-only zipmods', len(listless_z), cn['bundlesOnlyZipmods'])
    chk('  ... whose bundles are all covered', covered, cn['bundlesOnlyZipmods'])
    lines.append('     bundle rows (bundles no list row of any zipmod names): %d in %d zipmods (of %d bundles in all)'
                 % (have['bundle'], len(zip_bundle_rows), cn['bundles']))
    lines.append('     work list: %d rows -> %s' % (len(rows), os.path.join(d, 'worklist.tsv')))
    text = '\n'.join(lines) + '\n'
    with open(os.path.join(d, 'sw1.txt'), 'w', encoding='utf-8', newline='\n') as f: f.write(text)
    sys.stdout.write(text)
    return 0 if ok else 1


# ------------------------------------------------------------------ the sweep's outputs

def read_worklist(d):
    rows = []
    with open(os.path.join(d, 'worklist.tsv'), encoding='utf-8') as f:
        for line in f:
            if line.startswith('#') or line.startswith('idx\t'): continue
            t = line.rstrip('\n').split('\t')
            rows.append({'idx': int(t[0]), 'kind': t[1], 'zi': int(t[2]), 'key': t[3], 'bundle': t[4], 'asset': t[5], 'extra': t[6] if len(t) > 6 else ''})
    return rows


def read_zipmods(d):
    Z = []
    with open(os.path.join(d, 'zipmods.tsv'), encoding='utf-8') as f:
        next(f)
        for line in f:
            t = line.rstrip('\n').split('\t')
            Z.append({'zi': int(t[0]), 'path': t[1], 'stamp': t[2], 'guid': t[3], 'version': t[4] if len(t) > 4 else ''})
    return Z


def read_results(d):
    """idx -> list of result dicts (results.tsv: idx kind class ms loads flags keys detail)."""
    res = collections.defaultdict(list)
    p = os.path.join(d, 'results.tsv')
    if not os.path.isfile(p): return res
    with open(p, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('idx\t') or not line.strip(): continue
            t = line.rstrip('\n').split('\t')
            if len(t) < 8 or not t[0].isdigit(): continue
            res[int(t[0])].append({'kind': t[1], 'cls': t[2], 'ms': float(t[3] or 0), 'loads': int(t[4] or 0), 'flags': t[5], 'keys': t[6], 'detail': t[7]})
    return res


def read_zipset(d):
    """zip path (lower) -> SkippedBy reason ('' = loaded), from the sweep's zipset.tsv."""
    s = {}
    p = os.path.join(d, 'zipset.tsv')
    if not os.path.isfile(p): return None
    with open(p, encoding='utf-8') as f:
        for line in f:
            if line.startswith('path\t'): continue
            t = line.rstrip('\n').split('\t')
            if len(t) >= 3: s[t[0].lower()] = t[2]
    return s


def flags_of(r):
    out = {}
    for x in (r.get('flags') or '').split(','):
        if not x: continue
        k, _, v = x.partition('=')
        out[k] = v
    return out


def corpus_crash_bundles():
    """The bundles of the item corpus crash list (Cache/items/corpus_crashes.txt): the sweep loads the item rows of these."""
    s = set()
    p = os.path.join(ROOT, 'Cache', 'items', 'corpus_crashes.txt')
    if os.path.isfile(p):
        for line in open(p, encoding='utf-8', errors='replace'):
            t = line.strip().split('|')
            if len(t) >= 4: s.add(norm_bundle(t[3]).lower())
    return s


def swept(r, crash_bundles):
    """True for a row the sweep process loads itself (ModSweepTest's rule)."""
    return r['kind'] in SWEPT or (r['kind'] == 'item' and r['bundle'].lower() in crash_bundles)


def cmd_player_rows(a):
    """The player pass's input: the item rows of a sweep whose class is crash, as "<guid>#<no>|<group>|<category>" lines
    (rows of other kinds cannot be added with item.add and are counted only)."""
    rows = read_worklist(a.dir)
    Z = read_zipmods(a.dir)
    res = read_results(a.dir)
    items, other = [], 0
    for r in rows:
        own = res.get(r['idx'])
        if not own or own[-1]['cls'] != 'crash': continue
        if r['kind'] != 'item': other += 1; continue
        g, c, no = r['key'].split('/')
        items.append('%s#%s|%s|%s' % (Z[r['zi']]['guid'], no, g, c))
    with open(a.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(''.join(x + '\n' for x in items))
    print('player rows: %d item crash rows -> %s; crash rows of other kinds (not addable): %d' % (len(items), a.out, other))
    return 0


def unity_release(head):
    """The Unity version a UnityFS header names ("2018.4.11c1": signature, format number, player version, Unity version)
    and its release type letter (f final, p patch, c China, a / b alpha / beta); (None, None) for another header."""
    if not head.startswith(b'UnityFS\x00') or len(head) < 13: return None, None
    parts = head[12:].split(b'\x00')
    ver = parts[1].decode('ascii', 'replace') if len(parts) > 1 else ''
    m = re.match(r'\d+\.\d+\.\d+([a-z])', ver)
    return ver, (m.group(1) if m else None)


def cmd_header_rows(a):
    """The input of a player pass over unusual bundles: the item rows of the sweep whose bundle's UnityFS header names a Unity
    release type other than f (Unity 6 logs "Invalid version provided" for a China release "c" and loads it; the player died
    on one such bundle, [ljlsdq] ljlsdq_special). Reads the first 64 bytes of every .unity3d entry of the scan cache's
    zipmods; no object is parsed. The rows are those of the sweep in --dir, else those the scan cache lists (the sweep's
    work-list rule, so no sweep run is needed)."""
    import zipfile
    letters, odd = collections.Counter(), {}
    cache = read_cache(newest_cache())
    for z in cache:
        bs = [p for p in z['B'] if p.lower().endswith('.unity3d')]
        if not bs: continue
        try:
            zf = zipfile.ZipFile(z['path'])
        except Exception:
            letters['unreadable zip'] += 1; continue
        with zf:
            for p in bs:
                try:
                    with zf.open('abdata/' + p) as f: head = f.read(64)
                except Exception:
                    letters['unreadable'] += 1; continue
                ver, t = unity_release(head)
                letters[t or 'other header'] += 1
                if t != 'f': odd[(z['path'].lower(), norm_bundle(p).lower())] = ver or 'other header'
    if a.dir:
        rows, Z = read_worklist(a.dir), read_zipmods(a.dir)
    else:
        Z = cache
        rows = [{'kind': t[0], 'zi': t[1], 'key': t[2], 'bundle': t[3], 'asset': t[4]} for t in build_worklist(Z)[0]]
    items, hit, other = [], set(), collections.Counter()
    for r in rows:
        k = (Z[r['zi']]['path'].lower(), norm_bundle(r['bundle']).lower())
        if k not in odd: continue
        hit.add(k)
        if r['kind'] != 'item': other[r['kind']] += 1; continue
        g, c, no = r['key'].split('/')
        items.append('%s#%s|%s|%s|%s|%s' % (Z[r['zi']]['guid'], no, g, c, r['bundle'], r['asset']))
    vers = collections.Counter(odd.values())
    head = ['# item rows of the bundles whose header names a Unity release type other than f (mod_sweep_report.py header-rows)',
            '# bundles by release type: ' + ', '.join('%s %d' % kv for kv in sorted(letters.items())),
            '# other than f: %d bundles (%s); %d of them with sweep rows: item rows %d%s'
            % (len(odd), ', '.join('%s %d' % kv for kv in sorted(vers.items())), len(hit), len(items),
               ''.join('; %s rows %d (not addable)' % kv for kv in sorted(other.items())))]
    with open(a.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(''.join(x + '\n' for x in head + items))
    for x in head[1:]: print(x[2:])
    print('-> %s' % a.out)
    return 0


def cmd_status(a):
    d = a.dir
    rows = read_worklist(d)
    res = read_results(d)
    cb = corpus_crash_bundles()
    owned = [r for r in rows if swept(r, cb)]
    left = sum(1 for r in owned if r['idx'] not in res)
    dup = sum(1 for v in res.values() if len(v) > 1)
    cur = os.path.join(d, 'current.txt')
    if os.path.isfile(cur): st = 'isolate ' + open(cur, encoding='utf-8').read().split('\t')[0]
    elif left == 0: st = 'done'
    else: st = 'more %d' % left
    print('%s; SW2 so far: %d of %d rows with a result line, %d rows with more than one' % (st, len(owned) - left, len(owned), dup))
    return 0


# ------------------------------------------------------------------ corpora and verdicts

def latest_item_rows(items_dir):
    """(ref, group, category) -> problem lines, and whether all six chunk reports exist, from item_corpus_rows_<0-5>.tsv."""
    probs = collections.defaultdict(list)
    found = 0
    for tag in range(6):
        p = os.path.join(items_dir, 'item_corpus_rows_%d.tsv' % tag)
        if not os.path.isfile(p): continue
        found += 1
        with open(p, encoding='utf-8', errors='replace') as f:
            for line in f:
                if line.startswith('ref\t'): continue
                t = line.rstrip('\n').split('\t')
                if len(t) >= 5: probs[(t[0], t[1], t[2])].append(t[4])
    return probs, found


def item_class(problems):
    """The sweep class of an item corpus row's problem lines (None = no failure the sweep counts)."""
    cls = None
    for p in problems:
        c = None
        if p.startswith('skipped:'): c = 'crash'
        elif p.startswith('exception:') or p.startswith('partFailed:') or p.startswith('build:'): c = 'exception'
        elif p.startswith('prefab not loadable'): c = 'nullLoad' if 'not a known missing prefab' in p else 'missingAsset'
        if c and (cls is None or CLASSES.index(c) < CLASSES.index(cls)): cls = c
    return cls


def latest_map_rows(pattern):
    files = sorted(glob.glob(pattern))
    if not files: return {}, []
    stamps = sorted(set(re.sub(r'_\d+\.tsv$', '', os.path.basename(f)) for f in files if re.search(r'_\d+\.tsv$', f)))
    last = stamps[-1] if stamps else None
    use = [f for f in files if last and os.path.basename(f).startswith(last + '_') and re.search(r'_\d+\.tsv$', f)]
    rows = {}
    for p in use:
        with open(p, encoding='utf-8', errors='replace') as f:
            head = None
            for line in f:
                t = line.rstrip('\n').split('\t')
                if head is None: head = t; continue
                d = dict(zip(head, t))
                rows[d.get('ref', '')] = d
    return rows, use


def map_refused(maps_glob):
    """Map refs the map corpus reports name as rows whose scene neither Unity 6 nor HS2 loads (repacked bundles)."""
    refs = set()
    d = os.path.join(ROOT, 'Spike', 'out', 'maps')
    for p in glob.glob(os.path.join(d, 'map_corpus_report_*.txt')):
        for line in open(p, encoding='utf-8', errors='replace'):
            m = re.search(r'rows whose scene Unity 6 does not load from its bundle \(repacked bundles; HS2 does not load them either[^)]*\): (.*)', line)
            if m: refs.update(x.strip() for x in m.group(1).split(','))
    return refs


def map_class(d, refused):
    """The sweep class of a map corpus row (None = loaded)."""
    probs = (d.get('problems') or '')
    finds = (d.get('findings') or '').split(',')
    if 'exception:' in probs or 'stepFailed' in probs: return 'exception'
    if 'map.loadFailed' in finds or probs.startswith('state ') or '; state ' in probs:
        return 'asHS2' if d.get('ref') in refused else 'nullLoad'
    return None


def read_verdicts(path):
    """bundle key (lower case) -> (verdict, defects) from the texture check's verdict cache (key, stamp, verdict, textures,
    texture bytes, ms, defects, error; the last line of a key wins). An abdata key ("<source id>:<path>") is also filed under
    "abdata:<path>", the form the sweep's result lines use."""
    v = {}
    if not path or not os.path.isfile(path): return v
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('#'): continue
            t = line.rstrip('\n').split('\t')
            if len(t) < 3: continue
            val = (t[2], t[6] if len(t) > 6 else '')
            k = t[0].lower()
            v[k] = val
            if '!' not in k and ':' in k[2:]:
                v['abdata:' + k.split(':', 1)[1]] = val
    return v


HS2_EVIDENCE = os.path.join(HERE, 'survey', 'mod_sweep_hs2.tsv')


def read_hs2(path):
    """(kind, ref lower, key) -> (hs2, what) from the HS2 evidence file (key: a part's category, a Studio row's group/category)."""
    ev = {}
    if not path or not os.path.isfile(path): return ev
    for line in open(path, encoding='utf-8', errors='replace'):
        if line.startswith('#') or line.startswith('kind\t'): continue
        t = line.rstrip('\n').split('\t')
        if len(t) >= 6 and t[4] in ('shows', 'nothing'): ev[(t[0], t[1].lower(), t[2])] = (t[4], t[5])
    return ev


def read_first_m(path):
    """The M of the first full run, from a previous summary: its "first full run" line, else its SW7 line."""
    if not path or not os.path.isfile(path): return None
    first = sw7 = None
    for line in open(path, encoding='utf-8'):
        m = re.match(r'first full run before the fixes: M = (\d+)', line)
        if m: first = int(m.group(1))
        m = re.match(r'(PASS|FAIL|OPEN) SW7 M = (\d+)', line)
        if m: sw7 = int(m.group(2))
    return first if first is not None else sw7


def read_baseline(path):
    mc = {}
    if path and os.path.isfile(path):
        for line in open(path, encoding='utf-8'):
            m = re.match(r'maxCount\s+(\w+)\s+(\d+)', line)
            if m: mc[m.group(1)] = int(m.group(2))
    return mc


# ------------------------------------------------------------------ the report

def cmd_report(a):
    d = a.dir
    rows = read_worklist(d)
    Z = read_zipmods(d)
    res = read_results(d)
    zipset = read_zipset(d)
    items, item_chunks = latest_item_rows(a.items or os.path.join(ROOT, 'Spike', 'out', 'items'))
    maps, map_files = latest_map_rows(a.maps or os.path.join(ROOT, 'Cache', 'maps', 'corpus_*_*.tsv'))
    refused = map_refused(a.maps)
    cb = corpus_crash_bundles()
    verdicts = read_verdicts(a.verdicts or os.path.join(ROOT, 'Cache', 'texcheck', 'verdicts-v1.tsv'))
    baseline = read_baseline(a.baseline or SUMMARY)
    base = lambda zi: os.path.basename(Z[zi]['path'])

    final = {}            # idx -> (class or None, detail, flags dict, ms)
    sw2_missing, sw2_dup = 0, 0
    fixture_rows = [r for r in rows if r['kind'] == 'fixture']
    if fixture_rows:
        return sw3(rows, res)
    for r in rows:
        i, k = r['idx'], r['kind']
        z = Z[r['zi']]
        skipped = zipset.get(z['path'].lower(), '') if zipset is not None else ''
        own = res.get(i, [])
        if len(own) > 1: sw2_dup += 1
        if k in SWEPT:
            if not own: sw2_missing += 1; final[i] = ('unrun', '', {}, 0.0); continue
            x = own[-1]
            cls = None if x['cls'] in ('ok', '') else x['cls']
            if cls == 'outside' or skipped: final[i] = ('outside', skipped or x['detail'], flags_of(x), x['ms']); continue
            if cls in ('nullLoad', 'missingAsset', 'ok', None) and verdicts:
                for key in (x['keys'] or '').split('|'):
                    vk = verdicts.get(key.lower())
                    if vk and vk[0] == 'malformed': cls = 'textureRefused'; x['detail'] = 'malformed bundle ' + os.path.basename(key.split('!')[0])
            final[i] = (cls, x['detail'], flags_of(x), x['ms'])
        elif k == 'item':
            if skipped: final[i] = ('outside', skipped, {}, 0.0); continue
            g, c, no = r['key'].split('/')
            probs = items.get(('%s#%s' % (z['guid'], no), g, c), [])
            cls = item_class(probs)
            if swept(r, cb) and not own: sw2_missing += 1
            if own:   # a row the corpus skipped, loaded by the sweep (crash isolation)
                x = own[-1]
                cls = None if x['cls'] in ('ok', '') else x['cls']
                final[i] = (cls, x['detail'], flags_of(x), x['ms']); continue
            final[i] = (cls, (probs[0] if probs else '') if cls else '', {}, 0.0)
        elif k == 'map':
            if skipped: final[i] = ('outside', skipped, {}, 0.0); continue
            m = maps.get('%s#%s' % (z['guid'], r['key']))
            cls = map_class(m, refused) if m else None
            det = ('scene refused by Unity 6 and by HS2 (repacked bundle)' if cls == 'asHS2' else (m.get('problems') or m.get('findings') or '')) if cls else ''
            final[i] = (cls, det, {}, float(m.get('loadMs') or 0) if m else 0.0)

    # HS2 evidence: an unexplained row HS2 does not show either becomes asHS2; "shows" keeps the row where it is
    hs2 = read_hs2(a.hs2 or HS2_EVIDENCE)
    ev = collections.Counter()
    for r in rows:
        z = Z[r['zi']]
        parts = r['key'].split('/')
        e = hs2.get((r['kind'], ('%s#%s' % (z['guid'], parts[-1])).lower(), '/'.join(parts[:-1])))
        if not e: continue
        ev[e[0]] += 1
        cls, det, fl, ms = final.get(r['idx'], (None, '', {}, 0.0))
        if e[0] == 'nothing' and cls in UNEXPLAINED:
            final[r['idx']] = ('asHS2', 'HS2 shows nothing either: ' + e[1], fl, ms)
            ev['reclassified'] += 1

    # per zipmod
    per = collections.OrderedDict()
    for r in rows:
        zi = r['zi']
        p = per.setdefault(zi, {'kinds': collections.Counter(), 'cls': collections.Counter(), 'first': {}, 'slow': 0, 'fb': 0, 'sw8': 0,
                                'sw6': collections.Counter(), 'png16': 0, 'outside': ''})
        p['kinds'][r['kind']] += 1
        cls, det, fl, ms = final.get(r['idx'], (None, '', {}, 0.0))
        if cls == 'outside': p['outside'] = det; continue
        if cls and cls != 'unrun':
            p['cls'][cls] += 1
            p['first'].setdefault(cls, det)
        if 'slow' in fl: p['slow'] += 1
        if fl.get('fb'): p['fb'] += int(fl['fb'])
        if 'sw8' in fl: p['sw8'] += 1
        if 'sw6' in fl: p['sw6'][fl['sw6']] += 1
        if 'png16' in fl: p['png16'] += 1

    with open(os.path.join(d, 'report.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('guid\tfile\tversion\t' + '\t'.join(KINDS) + '\t' + '\t'.join(CLASSES) + '\toutside\tfirst messages\n')
        for zi, p in per.items():
            z = Z[zi]
            f.write('%s\t%s\t%s\t%s\t%s\t%s\t%s\n' % (z['guid'], base(zi), z['version'], '\t'.join(str(p['kinds'][k]) for k in KINDS),
                                                     '\t'.join(str(p['cls'][c]) for c in CLASSES), clean(p['outside']),
                                                     clean(' | '.join('%s: %s' % (c, m[:200]) for c, m in p['first'].items()))))

    # summary
    L = []
    w = L.append
    inset = [zi for zi, p in per.items() if not p['outside']]
    outside = [zi for zi, p in per.items() if p['outside']]
    by_cls_z = collections.Counter()
    by_cls_rows = collections.Counter()
    for zi in inset:
        for c, n in per[zi]['cls'].items():
            by_cls_z[c] += 1; by_cls_rows[c] += n
    M = [zi for zi in inset if any(per[zi]['cls'][c] for c in UNEXPLAINED)]
    deaths_editor = by_cls_rows['crash']
    graphics = read_kv(a.graphics or (os.path.join(a.graphics_dir, 'runner_graphics.log') if a.graphics_dir else None))
    sw5 = graphics_check(a.graphics_dir, res) if a.graphics_dir else None
    player = read_kv(a.player)
    w('# mod sweep summary (Tools/mod_sweep_report.py); zipmod file names and numbers only')
    w('run: %s; work list %d rows from %d zipmod files with rows (%d in the zipmod set, %d outside)' % (os.path.basename(os.path.normpath(d)), len(rows), len(per), len(inset), len(outside)))
    kc = collections.Counter(r['kind'] for r in rows)
    w('rows by kind: ' + ', '.join('%s %d' % (k, kc[k]) for k in KINDS))
    w('inputs: item corpus chunks %d of 6; map corpus files %d (%s); texture verdicts %d%s' % (
        item_chunks, len(map_files), os.path.basename(map_files[0]).rsplit('_', 1)[0] if map_files else '-', len(verdicts),
        '' if verdicts else ' (no verdict cache: textureRefused from the in-process guard only)'))
    if hs2:
        w('HS2 evidence (Tools/survey/mod_sweep_hs2.tsv): %d rows matched; HS2 shows %d (they stay), shows nothing %d (unexplained -> asHS2: %d)'
          % (ev['shows'] + ev['nothing'], ev['shows'], ev['nothing'], ev['reclassified']))
    w('')
    w('== classes (zipmods / rows; first that applies per row)')
    # asHS2 may grow by the rows the HS2 evidence moves there (the first run's count stays the written maxCount)
    # a class whose rule changed gets this run's count as its new maxCount (--reset-maxcount, said in the summary)
    reset = [c for c in (a.reset_maxcount or '').split(',') if c in EXPLAINED]
    for c in reset: baseline[c] = by_cls_rows[c]
    allowed = dict((c, baseline[c] + (ev['reclassified'] if c == 'asHS2' else 0)) for c in baseline)
    for c in CLASSES:
        w('  %-15s %-11s %5d zipmods %7d rows%s' % (c, 'unexplained' if c in UNEXPLAINED else 'explained', by_cls_z[c], by_cls_rows[c],
                                                   ('   maxCount %d%s%s' % (baseline[c], (' + %d by HS2 evidence' % (allowed[c] - baseline[c])) if allowed[c] > baseline[c] else '',
                                                                          '' if by_cls_rows[c] <= allowed[c] else ' EXCEEDED')) if c in baseline else ''))
    slow = sum(p['slow'] for p in per.values()); fb = sum(p['fb'] for p in per.values())
    w('  metrics: slow loads (> 2 s) %d rows; shader fallback slots %d' % (slow, fb))
    w('  not covered: failures Unity reports only in its native log (a streamed texture whose file the bundle does not carry')
    w('  loads without its data: "could not open file"); the managed log never sees them - the texture check\'s scan counts them')
    w('')
    w('== threshold (SW7; LQ1: M <= %d, 0 process deaths in the official load path, explained classes within maxCount)' % THRESHOLD_M)
    exceeded = [c for c in EXPLAINED if c in allowed and by_cls_rows[c] > allowed[c]]
    # the second player pass (header-rows rows, mod_sweep_player.py --one-player): the player is the official load path, so
    # its deaths count; the crash quarantine refuses a bundle after its first death in a user folder (a first-use death)
    ph = read_kv(a.player_header)
    ph_first, ph_after, ph_zips, ph_quar = player_header_deaths(a.player_header, Z) if ph else (0, 0, [], set())
    deaths = deaths_editor + int(graphics.get('deviceLoss', 0) or 0) + int(player.get('deaths', 0) or 0) + int(ph.get('deaths', 0) or 0)
    verdict = 'PASS' if len(M) <= THRESHOLD_M and deaths == 0 and not exceeded else 'FAIL'
    # open while a pass has not run or the item / map corpora read here are not the final ones (--final: T's whole-suite run)
    still_open = [x for x, ok in (('graphics pass', bool(graphics)), ('player pass', bool(player)), ('player pass over unusual headers', bool(ph)),
                                  ('final item / map corpora', bool(a.final))) if not ok]
    if verdict == 'PASS' and still_open: verdict = 'OPEN'
    w('%s SW7 M = %d zipmods with an unexplained failure; process deaths: editor sweep %d, graphics pass %s, player pass %s, player pass over unusual headers %s; explained classes over maxCount: %s'
      % (verdict, len(M), deaths_editor, graphics.get('deviceLoss', 'not run'), player.get('deaths', 'not run'), ph.get('deaths', 'not run'),
         ', '.join(exceeded) or 'none' + ('' if baseline else ' (no maxCount yet: this run sets it)')))
    if ph:
        w('player pass over the Studio item rows of the bundles whose header names a Unity release type other than f: rows %s, added %s, refused %s, deaths %s'
          % (ph.get('rows', '?'), ph.get('added', '?'), ph.get('refused', '?'), ph.get('deaths', '?')))
        if ph_first or ph_after:
            w('player deaths: %d first-use death%s (%s; a known open fault), %d death%s after the first'
              % (ph_first, '' if ph_first == 1 else 's',
                 ', '.join('%s, %s' % (z, 'quarantined afterwards' if z in ph_quar else 'NOT seen quarantined') for z in ph_zips),
                 ph_after, '' if ph_after == 1 else 's'))
    if still_open: w('SW7 still open for: ' + ', '.join(still_open))
    first_m = read_first_m(a.baseline or SUMMARY)
    w('first full run before the fixes: M = %s' % (first_m if first_m is not None else len(M)))
    # a row the check refused counts in M until HS2 evidence shows HS2 fails too (then it becomes asHS2)
    m_wo_refused = [zi for zi in inset if any(per[zi]['cls'][c] for c in UNEXPLAINED if c != 'refused')]
    w('M without the rows the pre-load check refuses (they count until HS2 evidence makes them asHS2): %d' % len(m_wo_refused))
    w('')
    w('== zipmods in M (file, version, unexplained rows by class)')
    for zi in sorted(M, key=lambda zi: (-sum(per[zi]['cls'][c] for c in UNEXPLAINED), base(zi).lower())):
        w('  %s\t%s\t%s' % (base(zi), Z[zi]['version'], ', '.join('%s %d' % (c, per[zi]['cls'][c]) for c in UNEXPLAINED if per[zi]['cls'][c])))
    w('')
    w('== top classes by zipmods affected (the order of the fixes)')
    for c, n in sorted(((c, by_cls_z[c]) for c in CLASSES if by_cls_z[c]), key=lambda x: -x[1])[:8]:
        top = sorted((zi for zi in inset if per[zi]['cls'][c]), key=lambda zi: -per[zi]['cls'][c])[:5]
        w('  %-15s %5d zipmods: %s' % (c, n, ', '.join('%s (%d)' % (base(zi), per[zi]['cls'][c]) for zi in top)))
    w('')
    w('== files outside the zipmod set (%d)' % len(outside))
    for zi in sorted(outside, key=lambda zi: base(zi).lower()):
        w('  %s\t%s' % (base(zi), per[zi]['outside']))
    w('')
    owned = sum(1 for r in rows if swept(r, cb))
    w('%s SW2 every row the sweep loads has exactly one result line: %d rows, %d without, %d with more than one'
      % ('PASS' if sw2_missing == 0 and sw2_dup == 0 else 'FAIL', owned, sw2_missing, sw2_dup))
    tot = collections.Counter()
    for p in per.values(): tot.update(p['kinds'])
    w('%s SW4 the per-zipmod counts sum to the per-kind totals: %s' % ('PASS' if all(tot[k] == kc[k] for k in KINDS) else 'FAIL',
                                                                      ', '.join('%s %d/%d' % (k, tot[k], kc[k]) for k in KINDS)))
    sw6 = collections.Counter()
    for p in per.values(): sw6.update(p['sw6'])
    w('%s SW6 Studio-side loose PNGs: %d item / map texture names a loose PNG overrides load the PNG, %d do not%s'
      % ('PASS' if sw6['fail'] == 0 else 'FAIL', sw6['ok'], sw6['fail'],
         ' (no loose PNG answers a Studio item / map bundle path in this work list)' if not sw6 else ''))
    if sw5:
        ok5 = graphics.get('deviceLoss') == '0' and sw5['missing'] == 0 and sw5['patchedBad'] == 0 and sw5['differ'] == 0
        w('%s SW5 graphics pass: %d rows (%d whose bundles the check patched), device losses %s, nvlddmkm +%s; patched rows not '
          'loading %d; rows whose class differs from the headless sweep %d; rows without a result %d'
          % ('PASS' if ok5 else 'FAIL', sw5['rows'], sw5['patched'], graphics.get('deviceLoss', '?'), graphics.get('nvlddmkm', '?'),
             sw5['patchedBad'], sw5['differ'], sw5['missing']))
    w('INFO SW8 accessory prefabs whose move transforms are also chain bones: %d (zero closes the 5.7 row)'
      % sum(p['sw8'] for p in per.values()))
    w('INFO loose PNG entries with 16 bits per channel: %d' % sum(p['png16'] for p in per.values()))
    w('')
    w('== maxCount of the explained classes (set by the first full run; a later run fails a class above it)')
    for c in EXPLAINED:
        w('maxCount %s %d%s' % (c, baseline.get(c, by_cls_rows[c]), '   (reset by this run: a class rule changed)' if c in reset else ''))
    text = '\n'.join(L) + '\n'
    # the shader fallback metric per original shader (local only: shader names stay out of the committed summary)
    sh = collections.Counter()
    shp = os.path.join(d, 'shaders.tsv')
    if os.path.isfile(shp):
        for line in open(shp, encoding='utf-8', errors='replace'):
            t = line.rstrip('\n').split('\t')
            if len(t) >= 3 and t[2].isdigit(): sh[t[1]] += int(t[2])
    with open(os.path.join(d, 'shaders_summary.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('# part prefab slots on the URP/Lit fallback by original shader (metric)\n')
        for name, n in sh.most_common(): f.write('%7d  %s\n' % (n, name))
    with open(os.path.join(d, 'summary.txt'), 'w', encoding='utf-8', newline='\n') as f: f.write(text)
    if a.summary:
        with open(a.summary, 'w', encoding='utf-8', newline='\n') as f: f.write(text)
    sys.stdout.write(text)
    return 0 if verdict == 'PASS' else 1


def sw3(rows, res):
    """SW3: the fixture row ended its process and is class crash; every other row has exactly one result line."""
    bad = []
    for r in rows:
        own = res.get(r['idx'], [])
        if len(own) != 1: bad.append('row %d: %d result lines' % (r['idx'], len(own))); continue
        x = own[0]
        if r['kind'] == 'fixture' and r['key'] == 'crash' and x['cls'] != 'crash': bad.append('fixture row %d: class %s' % (r['idx'], x['cls']))
        if r['kind'] != 'fixture' and x['cls'] == 'crash': bad.append('row %d: crash' % r['idx'])
    line = '%s SW3 the forced crash is isolated (class crash) and the chunk resumes: %d rows%s' % ('PASS' if not bad else 'FAIL', len(rows), ('; ' + '; '.join(bad)) if bad else '')
    print(line)
    return 0 if not bad else 1


def graphics_check(gdir, headless):
    """SW5's numbers: the graphics pass's rows against the headless sweep's (same row indexes)."""
    gres = read_results(gdir)
    rows = read_worklist(gdir)
    out = {'rows': len(rows), 'patched': 0, 'patchedBad': 0, 'differ': 0, 'missing': 0}
    repaired = set()
    rp = os.path.join(ROOT, 'Cache', 'texguard', 'repaired.tsv')
    if os.path.isfile(rp):
        for line in open(rp, encoding='utf-8', errors='replace'):
            if not line.startswith('#'): repaired.add(line.split('\t')[0].lower())
    for r in rows:
        g = gres.get(r['idx'])
        if not g: out['missing'] += 1; continue
        x = g[-1]
        h = headless.get(r['idx'])
        hc = h[-1]['cls'] if h else None
        hf = flags_of(h[-1]) if h else {}
        keys = [k.lower() for k in ((h[-1]['keys'] if h else '') or '').split('|') if k]
        if 'patched' in hf or 'repaired' in hf or any(k in repaired for k in keys):
            out['patched'] += 1
            if x['cls'] not in ('ok', ''): out['patchedBad'] += 1
        if hc is not None and x['cls'] != hc: out['differ'] += 1
    return out


def read_kv(path):
    """key=value pairs from a pass's result line (the graphics pass, the player pass)."""
    if not path or not os.path.isfile(path): return {}
    kv = {}
    for line in open(path, encoding='utf-8', errors='replace'):
        for k, v in re.findall(r'(\w+)=(\S+)', line): kv[k] = v
    return kv


def player_header_deaths(path, Z):
    """From a Tools/mod_sweep_player.py --one-player report: (first-use deaths, deaths after the first (a bundle that died
    again, or a nonzero exit at app.quit), the zipmod files of the first-use deaths, those of them a later player logged as
    content.quarantined)."""
    kv = read_kv(path)
    first = int(kv.get('firstDeaths', 0) or 0)
    after = int(kv.get('repeatDeaths', 0) or 0) + int(kv.get('quitDeaths', 0) or 0)
    byguid = {}
    for z in Z: byguid.setdefault(z['guid'].lower(), os.path.basename(z['path']))
    zips, quar = [], set()
    for line in open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'^(.+?)#-?\d+ -?\d+/-?\d+: (?:died|hung) - .*; bundle (.+?); log:', line)
        if m:
            zn = byguid.get(m.group(1).lower(), m.group(1))
            if zn not in zips: zips.append(zn)
        m = re.search(r'content\.quarantined: (.+?):', line)
        if m: quar.add(byguid.get(m.group(1).lower(), m.group(1)))
    return first, after, zips, quar


def main():
    try: sys.stdout.reconfigure(encoding='utf-8')
    except Exception: pass
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('worklist'); p.add_argument('--cache'); p.add_argument('--census'); p.add_argument('--dir'); p.add_argument('--crash-rows')
    p.add_argument('--sw3', action='store_true'); p.add_argument('--graphics-from'); p.add_argument('--verdicts')
    p.add_argument('--every', type=int, default=100)
    p = sub.add_parser('status'); p.add_argument('--dir', required=True)
    p = sub.add_parser('player-rows'); p.add_argument('--dir', required=True); p.add_argument('--out', required=True)
    p = sub.add_parser('report'); p.add_argument('--dir', required=True); p.add_argument('--items'); p.add_argument('--maps')
    p.add_argument('--verdicts'); p.add_argument('--baseline'); p.add_argument('--summary'); p.add_argument('--graphics'); p.add_argument('--player')
    p.add_argument('--hs2'); p.add_argument('--reset-maxcount'); p.add_argument('--graphics-dir')
    p.add_argument('--final', action='store_true'); p.add_argument('--player-header')
    p = sub.add_parser('header-rows'); p.add_argument('--dir'); p.add_argument('--out', required=True)
    a = ap.parse_args()
    return {'worklist': cmd_worklist, 'status': cmd_status, 'player-rows': cmd_player_rows, 'header-rows': cmd_header_rows, 'report': cmd_report}[a.cmd](a)


if __name__ == '__main__':
    sys.exit(main())
