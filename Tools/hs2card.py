"""Read an HS2 (AIS) character card into Python objects. Format notes

    card = load_card(path) -> dict(header=..., blocks={name: obj}, custom=dict(face, body, hair),
                                   coordinate=dict(clothes, accessory), ext=KKEx dict)
"""
import io, struct
import msgpack


def _str(f):
    n, shift = 0, 0
    while True:
        b = f.read(1)[0]
        n |= (b & 0x7F) << shift
        shift += 7
        if not b & 0x80:
            break
    return f.read(n).decode("utf-8")


def _unpack(b):
    return msgpack.unpackb(b, raw=False, strict_map_key=False)


def _chunks(raw):
    g = io.BytesIO(raw)
    out = []
    while g.tell() < len(raw):
        n = struct.unpack("<i", g.read(4))[0]
        out.append(_unpack(g.read(n)))
    return out


def load_card(path):
    data = open(path, "rb").read()
    f = io.BytesIO(data[data.index(b"IEND") + 8:])
    hdr = {"product": struct.unpack("<i", f.read(4))[0], "marker": _str(f), "version": _str(f),
           "language": struct.unpack("<i", f.read(4))[0], "userID": _str(f), "dataID": _str(f)}
    n = struct.unpack("<i", f.read(4))[0]
    info = _unpack(f.read(n))
    struct.unpack("<q", f.read(8))
    base = f.tell()
    raw = {}
    for b in info["lstInfo"]:
        f.seek(base + b["pos"])
        raw[b["name"]] = f.read(b["size"])
    card = {"header": hdr, "blocks": {}}
    for k, v in raw.items():
        if k == "Custom":
            face, body, hair = _chunks(v)
            card["custom"] = {"face": face, "body": body, "hair": hair}
        elif k == "Coordinate":
            clothes, acc = _chunks(v)
            card["coordinate"] = {"clothes": clothes, "accessory": acc}
        else:
            card["blocks"][k] = _unpack(v)
    ext = card["blocks"].get("KKEx") or {}
    card["ext"] = ext
    return card


def resolver_info(card):
    """Sideloader UniversalAutoResolver entries: list of dicts (GUID, Slot, LocalSlot, Property, ...)."""
    r = card["ext"].get("com.bepis.sideloader.universalautoresolver")
    if not r or not r[1] or not r[1].get("info"):
        return []
    return [_unpack(x) if isinstance(x, (bytes, bytearray)) else x for x in r[1]["info"]]
