"""Generate C# stub classes for the MonoBehaviours found in HS2 AssetBundles, from the
bundles' own TypeTrees (clean-room: no decompiled code is used).

Usage:
  python -I gen_compat_stubs.py <out_assets_dir> [--merged PATH]
      Default. Reads merged.json written by typetree_scan.py (full scan, see
      README_typetree_scan.md); PATH defaults to <repo>/Cache/typetree/merged.json.
  python -I gen_compat_stubs.py <out_assets_dir> --sample <game_root> [--zip-sample N] [--rescan]
      Old path: scans a sample of bundles itself (cached in Tools/typetree_scan.pkl).
Writes <out>/Compat/... (.cs + .asmdef), <out>/Plugins/Compat/... (firstpass) and
<out>/Compat/compat_manifest.json.
"""
import sys, os, re, json, zipfile, random, collections, argparse

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_MERGED = os.path.normpath(os.path.join(HERE, "..", "Cache", "typetree", "merged.json"))
GAME = OUT = None
ZIP_N = 400

BASE_FIELDS = {"m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier"}
PRIM = {"int": "int", "SInt32": "int", "unsigned int": "uint", "UInt32": "uint", "SInt64": "long",
        "UInt64": "ulong", "SInt16": "short", "UInt16": "ushort", "SInt8": "sbyte", "UInt8": "byte",
        "float": "float", "double": "double", "bool": "bool", "char": "char", "string": "string",
        "long long": "long", "unsigned long long": "ulong", "short": "short", "unsigned short": "ushort"}
BUILTIN = {"Vector2f": "Vector2", "Vector3f": "Vector3", "Vector4f": "Vector4", "Quaternionf": "Quaternion",
           "Matrix4x4f": "Matrix4x4", "Rectf": "Rect", "AnimationCurve": "AnimationCurve",
           "Gradient": "Gradient", "BitField": "LayerMask", "AABB": "Bounds", "int2_storage": "Vector2Int",
           "int3_storage": "Vector3Int", "GUIStyle": "GUIStyle", "RectOffset": "RectOffset"}
UNITY_OBJ = {"GameObject", "Transform", "RectTransform", "Material", "Texture", "Texture2D", "Texture3D",
             "Cubemap", "RenderTexture", "Mesh", "SkinnedMeshRenderer", "MeshRenderer", "Renderer",
             "MeshFilter", "Animator", "Animation", "AnimationClip", "RuntimeAnimatorController",
             "AnimatorController", "AnimatorOverrideController", "Avatar", "Shader", "Object", "Component",
             "Behaviour", "MonoBehaviour", "Collider", "BoxCollider", "SphereCollider", "CapsuleCollider",
             "MeshCollider", "Rigidbody", "Light", "Camera", "AudioClip", "AudioSource", "Sprite", "Font",
             "TextAsset", "ParticleSystem", "ParticleSystemRenderer", "LineRenderer", "TrailRenderer",
             "Cloth", "ScriptableObject", "PhysicMaterial", "LightProbeGroup", "ReflectionProbe",
             "Terrain", "TerrainData", "ComputeShader", "Flare", "LensFlare", "Projector", "Canvas",
             "CanvasGroup", "SpriteRenderer", "AudioMixer", "AudioMixerGroup", "AvatarMask", "Motion",
             "BillboardRenderer", "LODGroup", "Joint", "CharacterJoint", "ConfigurableJoint", "HingeJoint"}
RENAME_OBJ = {"Object": "UnityEngine.Object", "PhysicMaterial": "PhysicsMaterial",
              "AnimatorController": "RuntimeAnimatorController", "AudioMixer": "UnityEngine.Object",
              "AudioMixerGroup": "UnityEngine.Object"}

# script key -> {"asm","ns","cls","fields": OrderedDict name->node, "so": bool, "count": int}
scripts = {}
# Real implementations come from Unity packages (ugui incl. TMP, post-processing v3).
PACKAGE_ASMS = {"UnityEngine.UI.dll", "Unity.TextMeshPro.dll", "Unity.Postprocessing.Runtime.dll"}
script_cache = {}


class JNode:
    """TypeTree node rebuilt from merged.json ([type, name, [children...]]); same attributes
    as N / UnityPy's TypeTreeNode, so the emitter does not care where a node came from."""
    __slots__ = ("m_Type", "m_Name", "m_Children")

    def __init__(self, lst):
        self.m_Type, self.m_Name = lst[0], lst[1]
        self.m_Children = [JNode(c) for c in lst[2]]


def load_merged(path):
    """Fill `scripts` from typetree_scan merged.json with the same filters as the old scan."""
    with open(path, encoding="utf-8") as f:
        merged = json.load(f)
    for s in merged["scripts"]:
        key = (s["assembly"], s["namespace"], s["class"])
        if not key[2] or key[0] in PACKAGE_ASMS:
            continue
        fields = collections.OrderedDict((n[1], JNode(n)) for n in s["fields"])
        scripts[key] = {"fields": fields, "so": s["scriptableObject"], "count": s["count"],
                        "conflicts": list(s["conflicts"])}
    print(f"merged.json: {merged['totals'].get('sources')} sources, {len(scripts)} classes")


class N:
    """Picklable copy of a TypeTreeNode (type, name, children)."""
    __slots__ = ("m_Type", "m_Name", "m_Children")

    def __init__(self, n):
        self.m_Type, self.m_Name = n.m_Type, n.m_Name
        self.m_Children = [N(c) for c in n.m_Children]

    def __getstate__(self):
        return (self.m_Type, self.m_Name, self.m_Children)

    def __setstate__(self, st):
        self.m_Type, self.m_Name, self.m_Children = st


def scan_bundle(data, label):
    import UnityPy
    try:
        env = UnityPy.load(data)
    except Exception as e:
        print("skip", label, e)
        return
    script_cache.clear()  # keys use id(), which is reused across bundles
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        st = obj.serialized_type
        if st is None or st.node is None:
            continue
        ck = (id(obj.assets_file), st.script_type_index)
        key = script_cache.get(ck)
        if key is None:
            try:
                sc = obj.read(check_read=False).m_Script.read()
                key = (sc.m_AssemblyName, sc.m_Namespace, sc.m_ClassName)
            except Exception:
                key = ("?", "", "")
            script_cache[ck] = key
        if not key[2] or key[0] in PACKAGE_ASMS:
            continue
        s = scripts.setdefault(key, {"fields": collections.OrderedDict(), "so": None, "count": 0, "conflicts": []})
        s["count"] += 1
        for ch in st.node.m_Children:
            if ch.m_Name in BASE_FIELDS:
                continue
            old = s["fields"].get(ch.m_Name)
            if old is None:
                s["fields"][ch.m_Name] = N(ch)
            elif old.m_Type != ch.m_Type:
                s["conflicts"].append(f"{ch.m_Name}: {old.m_Type} vs {ch.m_Type}")
        if s["so"] is None:
            try:
                tt = obj.read_typetree()
                s["so"] = tt.get("m_GameObject", {}).get("m_PathID", 0) == 0
            except Exception:
                pass


def collect():
    ab = os.path.join(GAME, "abdata")
    files = []
    for sub in ["chara", "studio", "list/characustom", "studio/info", "animator", "custom"]:
        d = os.path.join(ab, sub)
        if not os.path.isdir(d):
            continue
        for root, _, fs in os.walk(d):
            if sub == "studio" and os.path.relpath(root, d).count(os.sep) > 0 and "info" not in root:
                pass
            files += [os.path.join(root, f) for f in fs if f.endswith(".unity3d")]
    random.seed(1)
    # chara/ is big; keep all *_00 and base bundles, sample the rest
    keep = [f for f in files if re.search(r"(_00|base|info|characustom|animator)", f.replace("\\", "/"))]
    rest = [f for f in files if f not in keep]
    files = keep + random.sample(rest, min(150, len(rest)))
    print(f"abdata bundles: {len(files)}")
    for i, f in enumerate(files):
        with open(f, "rb") as fh:
            scan_bundle(fh.read(), f)
    zips = []
    for root, _, fs in os.walk(os.path.join(GAME, "mods")):
        zips += [os.path.join(root, f) for f in fs if f.lower().endswith(".zipmod")]
    random.seed(0)
    zs = random.sample(zips, min(ZIP_N, len(zips)))
    print(f"zipmods: {len(zs)}")
    for z in zs:
        try:
            with zipfile.ZipFile(z) as zf:
                for n in zf.namelist():
                    if n.lower().endswith(".unity3d"):
                        scan_bundle(zf.read(n), z + "!" + n)
        except Exception as e:
            print("zip skip", z, e)


def ident(n):
    n = re.sub(r"[^0-9A-Za-z_]", "_", n)
    if n[0].isdigit():
        n = "_" + n
    return "@" + n if n in CS_KEYWORDS else n


CS_KEYWORDS = set("""abstract as base bool break byte case catch char checked class const continue decimal default
delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int
interface internal is lock long namespace new null object operator out override params private protected public
readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint
ulong unchecked unsafe ushort using virtual void volatile while""".split())


class Emitter:
    def __init__(self, asm, known):
        self.asm = asm          # assembly being emitted
        self.known = known      # (asm) -> set of "ns.cls" in that assembly
        self.nested = {}        # nested type name -> lines

    def ctype(self, node):
        t = node.m_Type
        if t in PRIM:
            return PRIM[t]
        if t in BUILTIN:
            return "global::UnityEngine." + BUILTIN[t]
        if t == "ColorRGBA":
            return "global::UnityEngine." + ("Color" if all(c.m_Type == "float" for c in node.m_Children) else "Color32")
        m = re.match(r"PPtr<(\$?)(.+)>", t)
        if m:
            name = m.group(2)  # script TypeTrees prefix every PPtr target with '$'
            if name in UNITY_OBJ:
                return "global::UnityEngine." + RENAME_OBJ.get(name, name).replace("UnityEngine.", "")
            if m.group(1) == "$":
                return self.find_script(name) or "global::UnityEngine.MonoBehaviour"
            return "global::UnityEngine.Object"
        if t in ("vector", "staticvector", "set") or (len(node.m_Children) == 1 and node.m_Children[0].m_Type == "Array"):
            arr = node.m_Children[0]
            elem = arr.m_Children[1]
            et = self.ctype(elem)
            return None if et is None else f"global::System.Collections.Generic.List<{et}>"
        if t == "Array":
            et = self.ctype(node.m_Children[1])
            return None if et is None else f"global::System.Collections.Generic.List<{et}>"
        if t in ("map", "pair", "TypelessData") or t.startswith("managedReference"):
            return None
        if node.m_Children:
            name = ident(t) + "_S"
            if name not in self.nested:
                self.nested[name] = None  # reserve against recursion
                lines = [f"[global::System.Serializable] public class {name}", "{"]
                for ch in node.m_Children:
                    ct = self.ctype(ch)
                    lines.append(f"    public {ct} {ident(ch.m_Name)};" if ct else f"    // skipped {ch.m_Type} {ch.m_Name}")
                lines.append("}")
                self.nested[name] = lines
            return name
        return None

    def find_script(self, cls):
        # Referencable only within the same assembly (asmdefs cannot see Assembly-CSharp).
        for full in self.known.get(self.asm, ()):
            if full.split(".")[-1] == cls:
                return "global::" + full if "." in full else "global::" + full
        return None


def main():
    global GAME, OUT, ZIP_N
    ap = argparse.ArgumentParser(description="Generate Compat stubs from bundle TypeTrees")
    ap.add_argument("out_assets_dir", help="Assets dir (or a temp dir) to write Compat/ and Plugins/Compat/ into")
    ap.add_argument("--merged", default=DEFAULT_MERGED, help="typetree_scan.py merged.json (default: %(default)s)")
    ap.add_argument("--sample", metavar="GAME_ROOT", help="old path: scan a sample of bundles instead")
    ap.add_argument("--zip-sample", type=int, default=400, help="(--sample) zipmods to sample")
    ap.add_argument("--rescan", action="store_true", help="(--sample) ignore Tools/typetree_scan.pkl")
    a = ap.parse_args()
    OUT, ZIP_N = a.out_assets_dir, a.zip_sample
    if a.sample:
        import pickle
        GAME = a.sample
        cache = os.path.join(HERE, "typetree_scan.pkl")
        if os.path.exists(cache) and not a.rescan:
            with open(cache, "rb") as f:
                scripts.update(pickle.load(f))
        else:
            collect()
            with open(cache, "wb") as f:
                pickle.dump(scripts, f)
    else:
        load_merged(a.merged)
    known = collections.defaultdict(set)
    for (asm, ns, cls) in scripts:
        known[asm].add(f"{ns}.{cls}" if ns else cls)
    root = os.path.join(OUT, "Compat")
    os.makedirs(root, exist_ok=True)
    manifest = []
    by_asm = collections.defaultdict(list)
    used_files = {}  # dir -> lower-case file stems written
    for key, s in scripts.items():
        by_asm[key[0]].append((key, s))
    for asm, items in sorted(by_asm.items()):
        asm_name = asm[:-4] if asm.endswith(".dll") else asm
        if asm_name == "Assembly-CSharp":
            d = os.path.join(root, "AssemblyCSharp")
        elif asm_name == "Assembly-CSharp-firstpass":
            d = os.path.join(OUT, "Plugins", "Compat")
        else:
            d = os.path.join(root, asm_name)
        os.makedirs(d, exist_ok=True)
        if asm_name not in ("Assembly-CSharp", "Assembly-CSharp-firstpass"):
            with open(os.path.join(d, asm_name + ".asmdef"), "w", encoding="utf-8", newline="\n") as f:
                json.dump({"name": asm_name, "references": [], "autoReferenced": True,
                           "noEngineReferences": False}, f, indent=2)
        for (a, ns, cls), s in sorted(items, key=lambda kv: kv[0]):
            em = Emitter(a, known)
            body = []
            for fname, node in s["fields"].items():
                ct = em.ctype(node)
                body.append(f"    public {ct} {ident(fname)};" if ct else f"    // skipped {node.m_Type} {fname}")
            base = "global::UnityEngine." + ("ScriptableObject" if s["so"] else "MonoBehaviour")
            lines = ["// Generated by Tools/gen_compat_stubs.py from AssetBundle TypeTrees. Do not edit.",
                     "using System;", "using System.Collections.Generic;", "using UnityEngine;", ""]
            ind = ""
            if ns:
                lines += [f"namespace {ns}", "{"]
                ind = "    "
            lines.append(f"{ind}public class {ident(cls)} : {base}")
            lines.append(f"{ind}{{")
            for nl in em.nested.values():
                lines += [ind + "    " + x for x in nl]
            lines += [ind + x for x in body]
            lines.append(f"{ind}}}")
            if ns:
                lines.append("}")
            # File names must be unique case-insensitively (Windows; e.g. UVScroll vs UvScroll).
            fn = ident((ns + "." if ns else "") + cls)
            used = used_files.setdefault(d, set())
            stem, k = fn, 2
            while stem.lower() in used:
                stem, k = f"{fn}_{k}", k + 1
            used.add(stem.lower())
            with open(os.path.join(d, stem + ".cs"), "w", encoding="utf-8", newline="\n") as f:
                f.write("\n".join(lines) + "\n")
            manifest.append({"assembly": a, "namespace": ns, "class": cls, "count": s["count"],
                             "scriptableObject": s["so"], "fields": len(s["fields"]),
                             "conflicts": s["conflicts"][:10]})
    with open(os.path.join(root, "compat_manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(sorted(manifest, key=lambda m: -m["count"]), f, indent=1)
    print(f"classes: {len(manifest)}  assemblies: {sorted(by_asm)}")


if __name__ == "__main__":
    main()
