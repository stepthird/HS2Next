"""Repaired copies of mod bundles whose textures Unity 6 cannot upload (companion of Runtime/Chara/TextureGuard.cs).

Some zipmod bundles (built with Unity 2018.x) serialize Texture2D objects with m_MipCount = 0, or with no image data at
all. Unity 6 asserts 'mipLevel < mipCount' when it loads them; with a graphics device the asynchronous upload inside
AssetBundle.LoadAsset removes the D3D12 device (D3D11: access violation), so an in-process fix after the load is too
late. TextureGuard records every such texture of a headless run in Cache/texguard/bad_textures.tsv; this tool reads
that list, opens each listed bundle (from its zipmod, or from <game>/abdata), fixes EVERY texture of the bundle that
is broken by the rules below, and writes

  Cache/texguard/bundles/<sha1(cache key)[:16]>.unity3d   the repaired bundle (LZ4)
  Cache/texguard/repaired.tsv                            cache key, file, source label, "name=action|..." per bundle

ContentCatalog opens a listed bundle from its repaired copy (IBundleCache.GetOrLoad -> TextureGuard.RepairedPath), and
TextureGuard reports the repaired textures a build uses as CharaContext.Missing "texture.badmip".

Rules per Texture2D (metadata only; pixels are never decoded or viewed):
  * data present and m_CompleteImageSize matches a mip chain of n levels for the size and format, but m_MipCount != n
    (typically 0): m_MipCount := n ("mips 0->1"); the texture is kept as it is;
  * no data (image data and stream data empty), data shorter than m_CompleteImageSize, or no chain length matches:
    replaced by a 4x4 RGBA32 texture with one mip, mid-grey (normal maps: flat normal (128, 128, 255, 128)) ("grey 4x4").
  v0.3 trial 2, for the textures the list names for a block size or a CPU-decoded format (Tools/survey/thumb_guard_census.py,
  the thumbnails' header check; textures listed for another reason keep the rules above):
  * a block-compressed texture (DXT1 / DXT5 / BC4-BC7) whose width or height is not a multiple of four (Unity 6 falls back
    to RGBA32 and the upload of the streamed data crashed the player): the header is padded to the next multiple of four
    ("pad 512x511->512x512"). The data is unchanged: a block-compressed level already stores whole 4x4 blocks, so the
    padded size describes the same bytes; the image keeps its pixels and gains at most three edge rows / columns of the
    encoder's block padding. Mip levels whose block counts differ after padding are dropped (m_MipCount, the complete
    image size and the stream size shortened to the levels kept: "pad 493x388->496x388 mips 9->3"); crunched textures and
    textures whose data is short become the grey 4x4;
  * a format desktop GPUs cannot sample (ETC / EAC / ASTC / PVRTC / ATC; decoded on the CPU on the same fallback path):
    the grey 4x4.
  A bundle repaired before is written again (from the original, with every rule) when the list names a texture of it that
  the index's actions do not name.
  A bundle listed only as a malformed bundle (its node table points outside its data) gets no repaired copy.
  Not broken (left as they are; before, the strict chain test made them the grey 4x4): a complete size a few bytes above
  the mip chain, and a block-compressed texture whose data is the chain of its size padded to a multiple of four.
Not repaired here: readable textures for which Unity 6 logs "image data ... has an unexpected size (expected N, got 0)"
although the stream holds the bytes (seen in one mod; moving the bytes inline did not help). TextureGuard swaps those
for its in-process fallback after the load (no crash: they do not assert).
The repaired copies derive from installed mods; they live in the git-ignored Cache only.

Cross-check mode (Phase 5.2): the app checks and patches every bundle it opens itself (Runtime/Content/TextureCheck.cs), so
this tool is a diagnostic and the reference of that check. --cross-check KEYS applies the rules to EVERY defective texture
of each bundle named in KEYS (one cache key per line, optionally followed by a tab and a local bundle file to read instead),
as the app does - no list, no index: the defect classes of the texture censuses (mip < 1, block size, mips > chain, data
short, cpu format, image size, empty size) plus "stream outside" (the streamed range is not inside the bundle) and
"readable streamed" (a readable texture whose data are streamed: Unity 6 loads its CPU copy as empty, "got 0"; it is made
not readable - "readable off", added to another action of the texture unless that is the grey); a texture without a class
is left as it is (the list mode's mip rule also raised the mip count of healthy textures of a listed bundle); a CPU-decoded format or data outside the bundle -> grey
4x4; a block size -> padded, else the mip rule, else grey; an empty size (a dynamic font's texture) -> left as it is; any
other class -> the mip rule, else grey. It
writes <out>/<sha1(key)[:16]>.unity3d uncompressed and <out>/crosscheck.tsv (key, file, name=action|...); the repair
cache and the list are not touched. TextureCheckTest.parity (TC2) compares the app's patched bundles with these copies.

Usage: python -I Tools/texguard_repair.py [--list FILE] [--out DIR] [--settings FILE] [--force]
       python -I Tools/texguard_repair.py --cross-check KEYS --out DIR [--settings FILE]
Requires UnityPy (pip install UnityPy).
"""
import argparse
import hashlib
import json
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# TextureFormat -> (block width, bytes per block); block width 1 = uncompressed bytes per pixel.
FORMATS = {
    1: (1, 1), 2: (1, 2), 3: (1, 3), 4: (1, 4), 5: (1, 4), 7: (1, 2), 9: (1, 2), 13: (1, 2), 14: (1, 4),
    15: (1, 2), 16: (1, 4), 17: (1, 8), 18: (1, 4), 19: (1, 8), 20: (1, 16), 62: (1, 2), 63: (1, 1),
    10: (4, 8), 12: (4, 16), 24: (4, 16), 25: (4, 16), 26: (4, 8), 27: (4, 16), 28: (4, 8), 29: (4, 16),
}


def chain_size(w, h, fmt, n):
    bw, bpb = FORMATS[fmt]
    total = 0
    for i in range(n):
        mw, mh = max(1, w >> i), max(1, h >> i)
        if bw == 1:
            total += mw * mh * bpb
        else:
            total += ((mw + 3) // 4) * ((mh + 3) // 4) * bpb
    return total


def full_chain(w, h):
    m, n = max(1, w, h), 1
    while m > 1:
        m >>= 1
        n += 1
    return n


BLOCK = {10, 12, 24, 25, 26, 27, 28, 29}   # block-compressed (and crunched DXT)
CRUNCHED = {28, 29, 64, 65}
CPU = set(range(30, 37)) | set(range(41, 62)) | {64, 65} | set(range(66, 72))   # PVRTC, ATC, EAC, ETC, ASTC, ETC 3DS


def blocks(n):
    return (n + 3) // 4


def pad_block_size(tt):
    """(action, new fields) for a listed block-compressed texture whose size is not a multiple of four, or None (grey)."""
    w, h, fmt = tt.get('m_Width', 0), tt.get('m_Height', 0), tt.get('m_TextureFormat', 0)
    mips, size = tt.get('m_MipCount', 1), tt.get('m_CompleteImageSize', 0)
    images = max(1, tt.get('m_ImageCount', 1))
    inline = tt.get('image data', b'') or b''
    sd = tt.get('m_StreamData') or {}
    data = len(inline) or sd.get('size', 0)
    pw, ph = (w + 3) & ~3, (h + 3) & ~3
    if fmt in CRUNCHED or images != 1 or data < size:
        return None
    if mips < 1:
        # also no mip level (m_MipCount 0): the chain length the data holds at the padded size, then both fixes
        for n in range(1, full_chain(pw, ph) + 1):
            if chain_size(pw, ph, fmt, n) == size:
                new = {'m_Width': pw, 'm_Height': ph, 'm_MipCount': n}
                if 'm_MipMap' in tt:
                    new['m_MipMap'] = n > 1
                return f'pad {w}x{h}->{pw}x{ph} mips {mips}->{n}', new
        return None
    if mips > full_chain(pw, ph):
        return None
    if size == chain_size(pw, ph, fmt, mips) and mips <= full_chain(pw, ph):
        # the data is the padded size's chain (most encoders pad the top level and halve from there): the header only
        return f'pad {w}x{h}->{pw}x{ph}', {'m_Width': pw, 'm_Height': ph}
    if mips > full_chain(w, h) or size < chain_size(w, h, fmt, mips):
        return None
    keep = 0
    while keep < mips and blocks(max(1, w >> keep)) == blocks(max(1, pw >> keep)) and blocks(max(1, h >> keep)) == blocks(max(1, ph >> keep)):
        keep += 1
    if keep < 1:
        return None
    new = {'m_Width': pw, 'm_Height': ph}
    action = f'pad {w}x{h}->{pw}x{ph}'
    if keep < mips:
        kept = chain_size(pw, ph, fmt, keep)
        new['m_MipCount'] = keep
        new['m_CompleteImageSize'] = kept
        if len(inline):
            new['image data'] = bytes(inline[:kept])
        else:
            nsd = dict(sd)
            nsd['size'] = kept
            new['m_StreamData'] = nsd
        if 'm_MipMap' in tt:
            new['m_MipMap'] = keep > 1
        action += f' mips {mips}->{keep}'
    return action, new


def grey(tt):
    normal = tt.get('m_LightmapFormat', 0) in (3, 4) or tt.get('m_Name', '').lower().endswith(('_n', 'norm', 'normal', '_nm'))
    px = bytes([128, 128, 255, 128]) if normal else bytes([128, 128, 128, 255])
    new = {'m_Width': 4, 'm_Height': 4, 'm_TextureFormat': 4, 'm_MipCount': 1, 'm_CompleteImageSize': 64,
           'm_IsReadable': False, 'm_StreamingMipmaps': False, 'image data': px * 16,
           'm_StreamData': {'offset': 0, 'size': 0, 'path': ''}}
    if 'm_MipMap' in tt:
        new['m_MipMap'] = False
    if 'm_ImageCount' in tt:
        new['m_ImageCount'] = 1
    return ('flat normal 4x4' if normal else 'grey 4x4'), new


def diagnose_listed(tt):
    """The v0.3 trial 2 rules for a texture the list names: (action, new fields) or None."""
    w, h, fmt = tt.get('m_Width', 0), tt.get('m_Height', 0), tt.get('m_TextureFormat', 0)
    if fmt in CPU:
        return grey(tt)
    if fmt in BLOCK and w >= 1 and h >= 1 and (w % 4 or h % 4):
        # when the size cannot be padded, the mip rule below may still keep the texture (else the grey 4x4)
        return pad_block_size(tt) or diagnose(tt)
    return None


def diagnose(tt):
    """(action, new fields) for a broken texture, or None when it is fine."""
    w, h, fmt = tt.get('m_Width', 0), tt.get('m_Height', 0), tt.get('m_TextureFormat', 0)
    mips, size = tt.get('m_MipCount', 1), tt.get('m_CompleteImageSize', 0)
    images = max(1, tt.get('m_ImageCount', 1))
    data = len(tt.get('image data', b'') or b'')
    sd = tt.get('m_StreamData') or {}
    data = data or sd.get('size', 0)
    if w < 1 or h < 1:
        return None
    ok_chain = fmt in FORMATS and 1 <= mips <= full_chain(w, h) and chain_size(w, h, fmt, mips) * images == size
    if ok_chain and data >= size > 0:
        return None
    if data > 0 and size > 0 and data >= size and fmt in FORMATS:
        for n in range(1, full_chain(w, h) + 1):
            if chain_size(w, h, fmt, n) * images == size:
                if n != mips:
                    return f'mips {mips}->{n}', {'m_MipCount': n}
                break
    if ok_chain is False and mips >= 1 and data >= size > 0 and fmt not in FORMATS:
        return None   # unknown format with a plausible header: leave it
    # v0.3 trial 2: valid textures the strict chain test above took for broken (they became the grey 4x4 before): data
    # covering a complete size a few bytes above the chain (common: Unity reads the chain), and a block-compressed texture
    # whose data is the chain of its size padded to a multiple of four (Unity converts it to RGBA32 when a part loads it)
    if fmt in FORMATS and mips >= 1 and data >= size > 0:
        pw, ph = (w + 3) & ~3, (h + 3) & ~3
        if mips <= full_chain(w, h) and chain_size(w, h, fmt, mips) * images <= size:
            return None
        if fmt in BLOCK and mips <= full_chain(pw, ph) and chain_size(pw, ph, fmt, mips) * images == size:
            return None
    return grey(tt)


def classes(tt, bundle_files=None):
    """The defect classes of a texture (the texture censuses' classes, plus "stream outside")."""
    w, h, fmt = tt.get('m_Width', 0), tt.get('m_Height', 0), tt.get('m_TextureFormat', 0)
    mips = tt.get('m_MipCount', 1)
    size = tt.get('m_CompleteImageSize', 0)
    images = max(1, tt.get('m_ImageCount', 1))
    inline = len(tt.get('image data', b'') or b'')
    sd = tt.get('m_StreamData') or {}
    data = inline or sd.get('size', 0)
    if w < 1 or h < 1:
        return ['empty size']
    out = []
    if mips < 1: out.append('mip < 1')
    if fmt in BLOCK and (w % 4 or h % 4): out.append('block size')
    if mips > full_chain(w, h): out.append('mips > chain')
    known = fmt in FORMATS and fmt not in CRUNCHED and 1 <= mips <= full_chain(w, h)
    if known and data < chain_size(w, h, fmt, mips) * images: out.append('data short')
    if fmt in CPU: out.append('cpu format')
    if known and size < chain_size(w, h, fmt, mips) * images: out.append('image size')
    if not inline and sd.get('size', 0) > 0 and bundle_files is not None:
        name = (sd.get('path') or '').rsplit('/', 1)[-1]
        n = bundle_files.get(name)
        if n is None or sd.get('offset', 0) + sd['size'] > n:
            out.append('stream outside')
    if 'stream outside' not in out and tt.get('m_IsReadable') and not inline and sd.get('size', 0) > 0:
        out.append('readable streamed')
    return out


def plan(tt, cls):
    """The app's action for a texture with the given classes (see the cross-check mode), or None."""
    if not cls or 'empty size' in cls:   # an empty size is a dynamic font's texture: left as it is
        return None
    if 'cpu format' in cls or 'stream outside' in cls:
        return grey(tt)
    if cls == ['readable streamed']:
        d = ('', {})
    elif 'block size' in cls:
        d = pad_block_size(tt) or diagnose(tt) or grey(tt)
    else:
        d = diagnose(tt) or grey(tt)
    if 'readable streamed' in cls and not d[0].endswith('4x4'):
        new = dict(d[1])
        new['m_IsReadable'] = False
        d = ((d[0] + ' readable off') if d[0] else 'readable off', new)
    return d


def cross_check(a, game_root):
    """The cross-check mode (see the module text): the app's rules on every defective texture of the named bundles."""
    import UnityPy
    os.makedirs(a.out, exist_ok=True)
    rows = []
    with open(a.cross_check, encoding='utf-8-sig') as f:
        for line in f:
            p = line.rstrip('\n').split('\t')
            if p and p[0] and not p[0].startswith('#'):
                rows.append((p[0], p[1] if len(p) > 1 and p[1] else None))
    lines, failed = [], 0
    for key, local in rows:
        try:
            if local:
                with open(local, 'rb') as fh:
                    data = fh.read()
            else:
                data = read_bundle(key, game_root)
            env = UnityPy.load(data)
            bf = next(iter(env.files.values()))
            sizes = {}
            for name, fobj in bf.files.items():
                ln = getattr(fobj, 'Length', None)
                if ln is not None:
                    sizes[name] = ln
            actions = []
            for obj in env.objects:
                if obj.type.name != 'Texture2D':
                    continue
                tt = obj.read_typetree()
                d = plan(tt, classes(tt, sizes))
                if d is None:
                    continue
                action, new = d
                tt.update(new)
                obj.save_typetree(tt)
                actions.append(f"{tt['m_Name']}={action}")
            rel = hashlib.sha1(key.encode('utf-8')).hexdigest()[:16] + '.unity3d'
            if actions:
                with open(os.path.join(a.out, rel), 'wb') as fo:
                    fo.write(bf.save(packer='none'))
            lines.append('\t'.join([key, rel if actions else '', '|'.join(actions)]))
            print(f'  {key}: {", ".join(actions) or "nothing to repair"}')
        except Exception as e:   # one broken bundle must not stop the others
            failed += 1
            lines.append('\t'.join([key, '', 'FAILED ' + type(e).__name__ + ': ' + str(e).replace('\t', ' ')]))
            print(f'  {key}: FAILED {type(e).__name__}: {e}')
    with open(os.path.join(a.out, 'crosscheck.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('# cache key\tcopy\ttextures (name=action)\n')
        f.write('\n'.join(lines) + '\n')
    print(f'texguard_repair --cross-check: {len(rows)} bundles, {failed} failed ({a.out})')
    return 1 if failed else 0


def read_bundle(key, game_root):
    if '!' in key:
        zp, inner = key.split('!', 1)
        with zipfile.ZipFile(zp) as z:
            return z.read('abdata/' + inner)
    src, rel = key.split(':', 1)
    with open(os.path.join(game_root, 'abdata', rel), 'rb') as f:
        return f.read()


def main():
    # Texture and mod names can be CJK outside the console code page (cp932 on the dev machine); `python -I` ignores
    # PYTHONUTF8, so set it here, otherwise a successful repair is reported as failed when its line is printed.
    sys.stdout.reconfigure(encoding='utf-8', errors='backslashreplace')
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--list', default=os.path.join(ROOT, 'Cache', 'texguard', 'bad_textures.tsv'))
    ap.add_argument('--out', default=os.path.join(ROOT, 'Cache', 'texguard'))
    ap.add_argument('--settings', default=os.path.join(ROOT, 'Config', 'settings.local.json'))
    ap.add_argument('--force', action='store_true', help='rewrite bundles already in the index')
    ap.add_argument('--cross-check', metavar='KEYS', help="the app's rules on every defective texture of these bundles (see above)")
    a = ap.parse_args()
    import UnityPy

    game_root = ''
    if os.path.exists(a.settings):
        with open(a.settings, encoding='utf-8-sig') as f:
            game_root = json.load(f).get('hs2Root', '')
    if a.cross_check:
        return cross_check(a, game_root)
    if not os.path.exists(a.list):
        print(f'texguard_repair: no list at {a.list} (run a headless CardImportTest first)')
        return 0
    bundles = {}   # cache key -> source label
    listed = {}    # cache key -> the texture names the list gives (the v0.3 trial 2 rules apply to these only)
    reasons = {}   # cache key -> the list's reasons
    padded = {}    # cache key -> the names listed for a block size / CPU format (the thumbnail census, the header check)
    with open(a.list, encoding='utf-8-sig') as f:
        for line in f:
            p = line.rstrip('\n').split('\t')
            if len(p) >= 8 and p[7]:
                bundles.setdefault(p[7], p[0])
                listed.setdefault(p[7], set()).add(p[2])
                if len(p) > 6 and ('block size' in p[6] or 'cpu format' in p[6]):
                    padded.setdefault(p[7], set()).add(p[2])
                reasons.setdefault(p[7], set()).add(p[6] if len(p) > 6 else '')
    index_path = os.path.join(a.out, 'repaired.tsv')
    index = {}
    if os.path.exists(index_path) and not a.force:
        with open(index_path, encoding='utf-8-sig') as f:
            for line in f:
                p = line.rstrip('\n').split('\t')
                if len(p) >= 4 and not line.startswith('#') and os.path.exists(os.path.join(a.out, p[1])):
                    index[p[0]] = p
    os.makedirs(os.path.join(a.out, 'bundles'), exist_ok=True)
    done = failed = 0
    for key, source in sorted(bundles.items()):
        acted = None
        if reasons.get(key) and all('malformed bundle' in r for r in reasons[key]):
            # its node table is broken (thumb_guard_census.py): no repaired copy; the lists show the error tile
            print(f'  {source}: malformed bundle, not repaired (the lists show the error tile)')
            continue
        if key in index:
            # a bundle repaired before is read again only when the list names a texture its repair did not touch; it is
            # written again (from the original, every rule) only when that adds an action, under a new file name, so a
            # process that has the old copy open is not disturbed (it takes the new one with the index)
            acted = {t.rsplit('=', 1)[0] for t in index[key][3].split('|') if '=' in t}
            if not (listed.get(key, set()) - acted):
                continue
        try:
            env = UnityPy.load(read_bundle(key, game_root))
            actions = []
            names = padded.get(key, set())
            for obj in env.objects:
                if obj.type.name != 'Texture2D':
                    continue
                tt = obj.read_typetree()
                # a listed block-size / CPU-format texture takes the v0.3 trial 2 rules first (its padded chain would
                # look like a size mismatch to the mip rule)
                d = diagnose_listed(tt) if tt.get('m_Name') in names else None
                if d is None:
                    d = diagnose(tt)
                if d is None:
                    continue
                action, new = d
                tt.update(new)
                obj.save_typetree(tt)
                actions.append(f"{tt['m_Name']}={action}")
            if not actions:
                print(f'  {source}: nothing to repair')
                continue
            if acted is not None and {x.rsplit('=', 1)[0] for x in actions} <= acted:
                continue   # nothing new for this copy
            stem = hashlib.sha1(key.encode('utf-8')).hexdigest()[:16]
            rel = os.path.join('bundles', stem + '.unity3d')
            if acted is not None or (os.path.exists(os.path.join(a.out, rel)) and not a.force):
                n = 2
                while os.path.exists(os.path.join(a.out, 'bundles', f'{stem}_{n}.unity3d')):
                    n += 1
                rel = os.path.join('bundles', f'{stem}_{n}.unity3d')
            bundle_file = next(iter(env.files.values()))
            with open(os.path.join(a.out, rel), 'wb') as f:
                f.write(bundle_file.save(packer='lz4'))
            index[key] = [key, rel.replace('\\', '/'), source, '|'.join(actions)]
            done += 1
            print(f'  {source}: {", ".join(actions)}')
        except Exception as e:   # one broken bundle must not stop the others
            failed += 1
            print(f'  {source}: FAILED {type(e).__name__}: {e}')
    with open(index_path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# cache key\trepaired file\tsource label\ttextures (name=action)\n')
        for p in sorted(index.values()):
            f.write('\t'.join(p[:4]) + '\n')
    print(f'texguard_repair: {done} bundles repaired, {failed} failed, {len(index)} in the index ({os.path.relpath(index_path, ROOT)})')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
