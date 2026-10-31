"""Synthetic bundles for the texture check's plain-Mono tests (TC1, Runtime/Content/TextureCheck.cs; Phase 5.2 package C).

Writes Assets/HS2Next/Editor/Fixtures/TextureCheck/*.unity3d and expect.tsv. Every bundle is built here from scratch: a UnityFS
archive (format 6, engine "2018.4.11f1", as most mod bundles), one serialized file of format 17 with a Texture2D type tree
(the 2018 layout: name, fallback fields, size, complete image size, format, mip count, readable / streaming flags, image
count, dimension, texture settings, lightmap format, colour space, inline image data, streamed range) and Texture2D objects
whose texel bytes are a counting pattern (no image of any content), plus a .resS resource node for streamed data. The
bundles are reader fixtures, not loadable by Unity; tc_mesh adds three Mesh objects of a small synthetic mesh layout (name,
sub-meshes, index buffer, vertex data, bounds): a whole one, one cut inside its vertex data and one whose last field (the
bounds) is missing whole, and an AssetBundle object whose container entries reach them through their preload ranges: the
entries that need the cut mesh are refused, the other mesh is completed with zeros by the patch. tc_longname's node table has
four bytes after the serialized file's name (as some mod bundles have): the next record is read past them, and the patched
copy keeps the table as the file holds it. tc_streamname's node table names other files than the ones its textures stream
from (tc_streamdir: only the serialized file's): the patch gives the nodes those names; tc_overlap's table also puts the
resource node at offset 0 over the serialized file: the patch moves it to the serialized file's end. tc_streamstray has one
texture whose path names another directory than the serialized file: the patch sets its path's directory.

expect.tsv lists per fixture the verdict and the actions the rules of Tools/texguard_repair.py give (computed here with its
functions on the same header values: diagnose, pad_block_size, grey), which the C# check must reproduce:
  fixture <tab> compression <tab> verdict <tab> textures <tab> name=action|... <tab> refused container paths (|)

Usage: python -I Tools/make_texcheck_fixtures.py [--out DIR]
Requires the lz4 package (UnityPy's dependency) for the LZ4 / LZ4HC fixtures; LZMA comes from the standard library.
"""
import argparse
import hashlib
import io
import lzma
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import texguard_repair as tr   # noqa: E402  (the reference rules)

ENGINE = '2018.4.11f1'
# Unity's shared type-tree strings used here (offsets into the engine's common string buffer)
COMMON = {'Array': 49, 'Base': 55, 'bool': 76, 'char': 81, 'data': 106, 'first': 155, 'float': 161, 'int': 222, 'map': 241,
          'm_Name': 427, 'pair': 543, 'PPtr<Object>': 633, 'second': 778, 'size': 795, 'SInt64': 814, 'string': 840,
          'Texture2D': 874, 'TypelessData': 894, 'UInt8': 928, 'unsigned int': 934, 'vector': 981, 'Vector3f': 997}
ALIGN = 0x4000


def string_node(level, name):
    return [(level, 'string', name, -1, 0x8000, 0), (level + 1, 'Array', 'Array', -1, ALIGN, 1),
            (level + 2, 'int', 'size', 4, 0, 0), (level + 2, 'char', 'data', 1, 0, 0)]


def texture_tree():
    """(level, type, name, byte size, meta flag, type flags) in pre-order: the 2018 Texture2D layout."""
    n = [(0, 'Texture2D', 'Base', -1, 0x8000, 0)]
    n += string_node(1, 'm_Name')
    n += [(1, 'int', 'm_ForcedFallbackFormat', 4, 0, 0), (1, 'bool', 'm_DownscaleFallback', 1, ALIGN, 0),
          (1, 'int', 'm_Width', 4, 0, 0), (1, 'int', 'm_Height', 4, 0, 0), (1, 'int', 'm_CompleteImageSize', 4, 0, 0),
          (1, 'int', 'm_TextureFormat', 4, 0, 0), (1, 'int', 'm_MipCount', 4, 0, 0), (1, 'bool', 'm_IsReadable', 1, 0, 0),
          (1, 'bool', 'm_StreamingMipmaps', 1, ALIGN, 0), (1, 'int', 'm_StreamingMipmapsPriority', 4, ALIGN, 0),
          (1, 'int', 'm_ImageCount', 4, 0, 0), (1, 'int', 'm_TextureDimension', 4, 0, 0),
          (1, 'GLTextureSettings', 'm_TextureSettings', 24, 0, 0),
          (2, 'int', 'm_FilterMode', 4, 0, 0), (2, 'int', 'm_Aniso', 4, 0, 0), (2, 'float', 'm_MipBias', 4, 0, 0),
          (2, 'int', 'm_WrapU', 4, 0, 0), (2, 'int', 'm_WrapV', 4, 0, 0), (2, 'int', 'm_WrapW', 4, 0, 0),
          (1, 'int', 'm_LightmapFormat', 4, 0, 0), (1, 'int', 'm_ColorSpace', 4, 0, 0),
          (1, 'TypelessData', 'image data', -1, ALIGN, 1), (2, 'int', 'size', 4, 0, 0), (2, 'UInt8', 'data', 1, 0, 0),
          (1, 'StreamingInfo', 'm_StreamData', -1, 0x8000, 0),
          (2, 'unsigned int', 'offset', 4, 0, 0), (2, 'unsigned int', 'size', 4, 0, 0)]
    n += string_node(2, 'path')
    return n


def mesh_tree():
    """A small synthetic mesh layout (the reader walks any tree by structure): name, sub-meshes, index buffer, vertex data, bounds."""
    n = [(0, 'Mesh', 'Base', -1, 0x8000, 0)]
    n += string_node(1, 'm_Name')
    n += [(1, 'vector', 'm_SubMeshes', -1, 0, 0), (2, 'Array', 'Array', -1, ALIGN, 1), (3, 'int', 'size', 4, 0, 0),
          (3, 'SubMesh', 'data', 8, 0, 0), (4, 'unsigned int', 'firstByte', 4, 0, 0), (4, 'unsigned int', 'indexCount', 4, 0, 0),
          (1, 'vector', 'm_IndexBuffer', -1, 0, 0), (2, 'Array', 'Array', -1, ALIGN, 1), (3, 'int', 'size', 4, 0, 0),
          (3, 'UInt8', 'data', 1, 0, 0),
          (1, 'VertexData', 'm_VertexData', -1, 0, 0), (2, 'unsigned int', 'm_VertexCount', 4, 0, 0),
          (2, 'TypelessData', 'm_DataSize', -1, ALIGN, 1), (3, 'int', 'size', 4, 0, 0), (3, 'UInt8', 'data', 1, 0, 0),
          (1, 'AABB', 'm_LocalAABB', 24, 0, 0), (2, 'Vector3f', 'm_Center', 12, 0, 0), (3, 'float', 'x', 4, 0, 0),
          (3, 'float', 'y', 4, 0, 0), (3, 'float', 'z', 4, 0, 0), (2, 'Vector3f', 'm_Extent', 12, 0, 0), (3, 'float', 'x', 4, 0, 0),
          (3, 'float', 'y', 4, 0, 0), (3, 'float', 'z', 4, 0, 0)]
    return n


def pptr(level, name):
    return [(level, 'PPtr<Object>', name, 12, 0, 0), (level + 1, 'int', 'm_FileID', 4, 0, 0), (level + 1, 'SInt64', 'm_PathID', 8, 0, 0)]


def bundle_object_tree():
    """The AssetBundle object's first fields: name, preload table, container (path -> preload index, size, asset)."""
    n = [(0, 'AssetBundle', 'Base', -1, 0x8000, 0)]
    n += string_node(1, 'm_Name')
    n += [(1, 'vector', 'm_PreloadTable', -1, 0, 0), (2, 'Array', 'Array', -1, ALIGN, 1), (3, 'int', 'size', 4, 0, 0)]
    n += pptr(3, 'data')
    n += [(1, 'map', 'm_Container', -1, 0, 0), (2, 'Array', 'Array', -1, ALIGN, 1), (3, 'int', 'size', 4, 0, 0),
          (3, 'pair', 'data', -1, 0, 0)]
    n += string_node(4, 'first')
    n += [(4, 'AssetInfo', 'second', 20, 0, 0), (5, 'int', 'preloadIndex', 4, 0, 0), (5, 'int', 'preloadSize', 4, 0, 0)]
    n += pptr(5, 'asset')
    return n


def tree_blob(nodes):
    local = bytearray()
    offs = {}

    def off(s):
        if s in COMMON:
            return 0x80000000 | COMMON[s]
        if s not in offs:
            offs[s] = len(local)
            local.extend(s.encode('utf-8') + b'\0')
        return offs[s]
    body = bytearray()
    for i, (level, typ, name, size, meta, tflags) in enumerate(nodes):
        body += struct.pack('<HBBIIiii', 1, level, tflags, off(typ), off(name), size, i, meta)
    return struct.pack('<ii', len(nodes), len(local)) + bytes(body) + bytes(local)


def align4(b):
    while len(b) % 4:
        b.append(0)


def texture_object(t):
    """The bytes of one Texture2D object; t: dict of the fields (name, w, h, size, fmt, mips, image, stream (offset, size, path), lightmap)."""
    b = bytearray()
    nm = t['name'].encode('utf-8')
    b += struct.pack('<i', len(nm)) + nm
    align4(b)
    b += struct.pack('<iB', 0, 0)
    align4(b)
    b += struct.pack('<iiiii', t['w'], t['h'], t['size'], t['fmt'], t['mips'])
    b += struct.pack('<BB', 1 if t.get('readable') else 0, 0)
    align4(b)
    b += struct.pack('<iii', 0, t.get('images', 1), 2)
    b += struct.pack('<iifiii', 1, 1, 0.0, 0, 0, 0)
    b += struct.pack('<ii', t.get('lightmap', 0), 1)
    img = t.get('image', b'')
    b += struct.pack('<i', len(img)) + img
    align4(b)
    so, ss, sp = t.get('stream', (0, 0, ''))
    spb = sp.encode('utf-8')
    b += struct.pack('<II', so, ss) + struct.pack('<i', len(spb)) + spb
    align4(b)
    return bytes(b)


TYPES = [(28, texture_tree), (43, mesh_tree), (142, bundle_object_tree)]   # Texture2D, Mesh, AssetBundle


def serialized_file(objects):
    """Format 17 serialized file with the types of TYPES (each with its tree); objects: [(path id, bytes)] (Texture2D) or
    [(path id, bytes, type index)]."""
    used = sorted({o[2] if len(o) > 2 else 0 for o in objects})
    meta = bytearray(ENGINE.encode() + b'\0')
    meta += struct.pack('<iB', 19, 1)                       # target platform, type trees on
    meta += struct.pack('<i', len(used))
    for t in used:
        meta += struct.pack('<iBh', TYPES[t][0], 0, -1) + b'\0' * 16   # class id, not stripped, no script, old type hash
        meta += tree_blob(TYPES[t][1]())
    objects = [(o[0], o[1], used.index(o[2] if len(o) > 2 else 0)) for o in objects]
    meta += struct.pack('<i', len(objects))
    header_len = 20
    # object table: align 4, path id, start, size, type index; the starts need the data offset -> two passes
    table_at = len(meta)
    starts, data = [], bytearray()
    for pid, ob, ti in objects:
        while len(data) % 8:
            data.append(0)
        starts.append(len(data))
        data += ob
    for (pid, ob, ti), st in zip(objects, starts):
        while (header_len + len(meta)) % 4:
            meta.append(0)
        meta += struct.pack('<qIIi', pid, st, len(ob), ti)
    meta += struct.pack('<i', 0)        # scripts
    meta += struct.pack('<i', 0)        # externals
    meta += b'\0'                       # user information
    data_offset = header_len + len(meta)
    data_offset = (data_offset + 15) & ~15
    total = data_offset + len(data)
    head = struct.pack('>IIII', len(meta), total, 17, data_offset) + b'\0\0\0\0'
    out = bytearray(head + meta)
    out += b'\0' * (data_offset - len(out))
    out += data
    assert len(out) == total
    return bytes(out)


def compress(raw, kind):
    if kind == 0:
        return raw
    if kind in (2, 3):
        import lz4.block
        return lz4.block.compress(raw, mode='high_compression' if kind == 3 else 'default', store_size=False)
    if kind == 1:
        f = [{'id': lzma.FILTER_LZMA1, 'dict_size': 1 << 16, 'lc': 3, 'lp': 0, 'pb': 2}]
        props = struct.pack('<BI', (2 * 5 + 0) * 9 + 3, 1 << 16)
        return props + lzma.compress(raw, format=lzma.FORMAT_RAW, filters=f)
    raise ValueError(kind)


def bundle(nodes, kind, malformed=False):
    """UnityFS format 6; nodes: [(path, flags, bytes)]; data blocks of 128 KiB (one block for LZMA) compressed with kind.
    malformed: True = the resource node runs past the data; 'long name' = the serialized file's name is followed by four
    bytes after its zero byte (a writer that left part of a longer name: the next record starts after them)."""
    data = bytearray()
    table = []
    for path, flags, b in nodes:
        table.append((len(data), len(b), flags, path))
        data += b
    chunk = len(data) if kind == 1 else 1 << 17
    blocks, payload = [], bytearray()
    for i in range(0, max(1, len(data)), chunk):
        raw = bytes(data[i:i + chunk])
        c = compress(raw, kind)
        blocks.append((len(raw), len(c), 0x40 | kind))
        payload += c
    info = bytearray(b'\0' * 16) + struct.pack('>I', len(blocks))
    for us, cs, fl in blocks:
        info += struct.pack('>IIH', us, cs, fl)
    info += struct.pack('>I', len(table))
    for off, size, flags, path in table:
        if malformed == 'overlap' and not flags & 4:
            off = 0               # the record overlaps the serialized file; the bytes follow it
        if malformed is True and path.endswith('.resS'):
            size += 1 << 20       # the node table points past the data its blocks hold
        tail = b'\0\x02\0\0' if malformed == 'long name' and flags & 4 else b''
        info += struct.pack('>qqI', off, size, flags) + path.encode('utf-8') + b'\0' + tail
    head = b'UnityFS\0' + struct.pack('>I', 6) + b'5.x.x\0' + ENGINE.encode() + b'\0'
    total = len(head) + 8 + 12 + len(info) + len(payload)
    head += struct.pack('>qIII', total, len(info), len(info), 0x40)
    return bytes(head + info + payload)


def pattern(n, seed):
    return bytes((i * 37 + seed) & 0xFF for i in range(n))


def string_bytes(b, s):
    e = s.encode('utf-8')
    b += struct.pack('<i', len(e)) + e
    align4(b)


def mesh_object(name, data_size, present, bounds=True):
    """A synthetic mesh: 2 sub-meshes, 12 index bytes, vertex data of data_size bytes of which only `present` are written
    (fewer = the object ends inside its vertex data); bounds=False leaves the last field out whole (completed by the patch)."""
    b = bytearray()
    string_bytes(b, name)
    b += struct.pack('<i', 2) + struct.pack('<IIII', 0, 3, 6, 3)
    b += struct.pack('<i', 12) + bytes(range(12))
    align4(b)
    b += struct.pack('<I', 4) + struct.pack('<i', data_size) + pattern(min(present, data_size), 7)
    if present < data_size:
        return bytes(b)       # cut here: the vertex data and the bounds are missing
    align4(b)
    if not bounds:
        return bytes(b)       # the bounds are missing whole
    b += struct.pack('<6f', 0, 0, 0, 1, 1, 1)
    return bytes(b)


def bundle_object(preload, container):
    """preload: [(file id, path id)]; container: [(path, preload index, preload size, (file id, path id))]."""
    b = bytearray()
    string_bytes(b, 'tc_mesh')
    b += struct.pack('<i', len(preload))
    for f, pid in preload:
        b += struct.pack('<iq', f, pid)
    b += struct.pack('<i', len(container))
    for path, pi, ps, (f, pid) in container:
        string_bytes(b, path)
        b += struct.pack('<iiiq', pi, ps, f, pid)
    return bytes(b)


def make_mesh(name, kind=3):
    """tc_mesh: one texture, a whole mesh (path id 2), a cut one (3) and one without its last field (5); container entries
    reach them by preload ranges."""
    cab = 'CAB-' + hashlib.md5(name.encode()).hexdigest()
    tex = T('mesh_tex', 16, 16, 4, 5)
    tt = dict(tex); tt['image'] = pattern(tex['data'], 0)
    objs = [(1, texture_object(tt), 0), (2, mesh_object('whole', 96, 96), 1), (3, mesh_object('cut', 960, 40), 1),
            (5, mesh_object('tailless', 96, 96, bounds=False), 1)]
    preload = [(0, 1), (0, 2), (0, 1), (0, 3), (0, 5)]
    container = [('assets/fx/fine.prefab', 0, 2, (0, 10)), ('assets/fx/broken.prefab', 2, 2, (0, 11)),
                 ('assets/fx/cut.asset', 3, 0, (0, 3)), ('assets/fx/mesh_tex.png', 0, 1, (0, 1)), ('assets/fx/tailless.prefab', 4, 1, (0, 12))]
    objs.append((4, bundle_object(preload, container), 2))
    return bundle([(cab, 4, serialized_file(objs))], kind)


SER_SIZE = {}


def make(name, textures, kind=3, malformed=False):
    """textures: list of dicts (w, h, fmt, mips, size, data length, streamed?, name, lightmap)."""
    cab = 'CAB-' + hashlib.md5(name.encode()).hexdigest()
    res = bytearray()
    objs = []
    for i, t in enumerate(textures):
        n = t['data']
        body = pattern(n, i)
        tt = dict(t)
        if t.get('streamed'):
            while len(res) % 16:
                res.append(0)
            # 'stray': the path's directory names another CAB of the same length (a rename missed this path)
            d = 'CAB-' + hashlib.md5((name + ' stray').encode()).hexdigest() if t.get('stray') else cab
            tt['stream'] = (len(res) + t.get('stream_shift', 0), n, f'archive:/{d}/{cab}.resS')
            res += body
            tt['image'] = b''
        else:
            tt['image'] = body
        objs.append((i + 1, texture_object(tt)))
    # 'stream name': the node table names other files than the ones the textures stream from (a writer broke the names);
    # 'stream dir': only the serialized file's name differs from the directory the textures name; 'overlap': the names
    # differ and the resource node's record says offset 0 (it overlaps the serialized file)
    table = 'CAB-' + hashlib.md5((name + ' table').encode()).hexdigest() if malformed in ('stream name', 'stream dir', 'overlap') else cab
    nodes = [(table, 4, serialized_file(objs))]
    SER_SIZE[name] = len(nodes[0][2])
    if res:
        nodes.append(((cab if malformed == 'stream dir' else table) + '.resS', 0, bytes(res)))
    return bundle(nodes, kind, malformed)


def reference(textures):
    """The verdict and actions of texguard_repair's rules on the fixture's header values (every defective texture)."""
    acts, grey = [], False
    for t in textures:
        tt = {'m_Name': t['name'], 'm_Width': t['w'], 'm_Height': t['h'], 'm_TextureFormat': t['fmt'], 'm_MipCount': t['mips'],
              'm_CompleteImageSize': t['size'], 'm_ImageCount': 1, 'm_LightmapFormat': t.get('lightmap', 0), 'm_IsReadable': t.get('readable', False)}
        if t.get('streamed'):
            tt['image data'] = b''
            tt['m_StreamData'] = {'offset': 0, 'size': t['data'], 'path': 'x'}
        else:
            tt['image data'] = b'\0' * t['data']
            tt['m_StreamData'] = {'offset': 0, 'size': 0, 'path': ''}
        cls = t.get('classes', [])
        if not cls:
            continue
        tt['m_IsReadable'] = t.get('readable', False)
        d = tr.plan(tt, cls)
        acts.append(f"{t['name']}={d[0]}")
        grey |= d[0].endswith('4x4')
    return ('patch-grey' if grey else 'patch' if acts else 'ok'), acts


def T(name, w, h, fmt, mips, size=None, data=None, streamed=False, classes=(), lightmap=0, stream_shift=0, readable=False, stray=False):
    size = tr.chain_size(w, h, fmt, max(1, mips)) if size is None else size
    return {'name': name, 'w': w, 'h': h, 'fmt': fmt, 'mips': mips, 'size': size, 'data': size if data is None else data,
            'streamed': streamed, 'classes': list(classes), 'lightmap': lightmap, 'stream_shift': stream_shift, 'readable': readable,
            'stray': stray}


FIXTURES = [
    # name, compression (0 none, 1 LZMA, 2 LZ4, 3 LZ4HC), textures, node table (True: malformed; 'long name': read past)
    ('tc_ok', 3, [T('ok_rgba', 64, 64, 4, 7), T('ok_dxt1_streamed', 128, 128, 10, 8, streamed=True),
                  T('ok_crunched', 16, 16, 29, 1, size=40, data=40), T('ok_rgb24_extra', 32, 32, 3, 1, size=3072 + 26, data=3072 + 26)], False),
    ('tc_mip0', 2, [T('mip0_dxt5_streamed', 256, 256, 12, 0, size=65536, streamed=True, classes=['mip < 1']),
                    T('mip0_rgba_chain', 32, 32, 4, 0, size=tr.chain_size(32, 32, 4, 6), classes=['mip < 1'])], False),
    ('tc_block', 3, [T('block_512x511', 512, 511, 12, 10, size=tr.chain_size(512, 512, 12, 10), streamed=True, classes=['block size']),
                     T('block_493x388', 493, 388, 10, 9, size=tr.chain_size(493, 388, 10, 9), classes=['block size']),
                     T('block_150_mip0', 150, 150, 12, 0, size=tr.chain_size(152, 152, 12, 1), classes=['mip < 1', 'block size'])], False),
    ('tc_grey', 3, [T('short_rgba', 128, 128, 4, 1, data=1000, classes=['data short']),
                    T('short_bump_n', 64, 64, 12, 1, data=100, streamed=True, classes=['data short']),
                    T('etc_rgb4', 64, 64, 34, 1, size=2048, data=2048, classes=['cpu format']),
                    T('above_dxt1', 64, 64, 10, 6, size=tr.chain_size(64, 64, 10, 7), readable=True),
                    T('above_dxt1_not_readable', 64, 64, 10, 6, size=tr.chain_size(64, 64, 10, 7)),
                    T('outside_dxt1', 64, 64, 10, 1, streamed=True, stream_shift=1 << 16, classes=['stream outside'])], False),
    ('tc_lzma', 1, [T('lzma_mip0', 64, 64, 4, 0, size=tr.chain_size(64, 64, 4, 1), classes=['mip < 1']), T('lzma_ok', 16, 16, 10, 5)], False),
    ('tc_none', 0, [T('none_ok', 16, 16, 4, 5), T('none_mip0', 16, 16, 4, 0, size=1024, classes=['mip < 1'])], False),
    ('tc_malformed', 3, [T('malformed_streamed', 64, 64, 10, 1, streamed=True)], True),
    ('tc_streamname', 3, [T('streamname_dxt1', 64, 64, 10, 7, streamed=True), T('streamname_rgba', 32, 32, 4, 6, streamed=True)],
     'stream name'),
    ('tc_streamdir', 2, [T('streamdir_dxt5', 64, 64, 12, 7, streamed=True)], 'stream dir'),
    ('tc_streamstray', 3, [T('stray_ok', 64, 64, 10, 7, streamed=True), T('stray_dir', 32, 32, 12, 6, streamed=True, stray=True)],
     False),
    ('tc_overlap', 2, [T('overlap_dxt1', 64, 64, 10, 7, streamed=True), T('overlap_rgba', 16, 16, 4, 5, streamed=True)], 'overlap'),
    ('tc_longname', 3, [T('longname_streamed', 64, 64, 10, 7, streamed=True),
                        T('longname_mip0', 32, 32, 4, 0, size=tr.chain_size(32, 32, 4, 6), classes=['mip < 1'])], 'long name'),
    ('tc_readable', 3, [T('readable_streamed', 64, 64, 10, 7, streamed=True, readable=True, classes=['readable streamed']),
                        T('readable_streamed_mip0', 32, 32, 4, 0, size=tr.chain_size(32, 32, 4, 1), streamed=True, readable=True,
                          classes=['mip < 1', 'readable streamed']),
                        T('readable_inline', 32, 32, 4, 6, readable=True)], False),
]
COMP = {0: 'none', 1: 'LZMA', 2: 'LZ4', 3: 'LZ4HC'}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--out', default=os.path.join(ROOT, 'Assets', 'HS2Next', 'Editor', 'Fixtures', 'TextureCheck'))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = ['# fixture\tcompression\tverdict\ttextures\tactions (name=action|...)\trefused container paths (|)']
    for name, kind, texs, malformed in FIXTURES:
        b = make(name, texs, kind, malformed)
        with open(os.path.join(a.out, name + '.unity3d'), 'wb') as f:
            f.write(b)
        verdict, acts = ('malformed', []) if malformed is True else reference(texs)
        if malformed in ('stream name', 'stream dir', 'overlap'):   # the patch gives the nodes the names the textures stream from
            cab = 'CAB-' + hashlib.md5(name.encode()).hexdigest()
            moved = f' moved {SER_SIZE[name]}' if malformed == 'overlap' else ''
            verdict, acts = ('patch' if verdict == 'ok' else verdict), [f'node names=names {cab} {cab}.resS{moved}'] + acts
        strays = [f"{t['name']}=stream path" for t in texs if t.get('stray')]
        if strays:   # the path's directory is set to the serialized file's name
            verdict, acts = ('patch' if verdict == 'ok' else verdict), acts + strays
        lines.append('\t'.join([name + '.unity3d', COMP[kind], verdict, str(len(texs)), '|'.join(acts), '']))
        print(f'{name}: {len(b)} bytes, {COMP[kind]}, {verdict} {"|".join(acts)}')
    b = make_mesh('tc_mesh')
    with open(os.path.join(a.out, 'tc_mesh.unity3d'), 'wb') as f:
        f.write(b)
    lines.append('\t'.join(['tc_mesh.unity3d', 'LZ4HC', 'patch', '1', 'tailless=mesh tail +24', 'assets/fx/broken.prefab|assets/fx/cut.asset']))
    print(f'tc_mesh: {len(b)} bytes, LZ4HC, patch tailless=mesh tail +24, refused assets/fx/broken.prefab|assets/fx/cut.asset')
    with open(os.path.join(a.out, 'expect.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    return 0


if __name__ == '__main__':
    sys.exit(main())
