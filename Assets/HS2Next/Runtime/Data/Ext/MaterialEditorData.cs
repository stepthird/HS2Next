// MaterialEditor (`com.deathweasel.bepinex.materialeditor`): every key holds a bin with
// a nested (plain) MessagePack document, or nil. Lists of maps keyed by field name; TextureDictionary = map
// int -> image file bytes (PNG/JPEG, not decoded here). Coordinate cards carry Clothing/Accessory items only.
using System;
using System.Collections.Generic;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Data.Ext
{
    public class MaterialEditorData
    {
        /// <summary>ObjectType.</summary>
        public enum ObjectType { Unknown = 0, Clothing = 1, Accessory = 2, Hair = 3, Character = 4 }
        /// <summary>RendererProperty.Property.</summary>
        public enum RendererPropertyType { Enabled = 0, ShadowCastingMode = 1, ReceiveShadows = 2, RecalculateNormals = 3, UpdateWhenOffscreen = 4 }

        public int Version;
        /// <summary>Texture id -> raw image file bytes (as imported by the user; sRGB PNG/JPEG).</summary>
        public Dictionary<int, byte[]> Textures = new Dictionary<int, byte[]>();
        public List<RendererProperty> RendererProperties = new List<RendererProperty>();
        public List<FloatProperty> FloatProperties = new List<FloatProperty>();
        public List<ColorProperty> ColorProperties = new List<ColorProperty>();
        public List<VectorProperty> VectorProperties = new List<VectorProperty>();
        public List<TextureProperty> TextureProperties = new List<TextureProperty>();
        public List<CubemapProperty> CubemapProperties = new List<CubemapProperty>();
        public List<ShaderEdit> Shaders = new List<ShaderEdit>();
        public List<MaterialCopy> Copies = new List<MaterialCopy>();
        public List<KeywordProperty> KeywordProperties = new List<KeywordProperty>();
        public List<ProjectorProperty> ProjectorProperties = new List<ProjectorProperty>();
        public List<NameProperty> NameProperties = new List<NameProperty>();
        /// <summary>Load-only compatibility keys (DEDUPED_/LOCAL_ texture dictionaries), decoded but not resolved.</summary>
        public Dictionary<string, object> CompatKeys = new Dictionary<string, object>(StringComparer.Ordinal);

        public const string KeyTextures = "TextureDictionary";

        /// <summary>Total list items over all lists.</summary>
        public int ItemCount => RendererProperties.Count + FloatProperties.Count + ColorProperties.Count + VectorProperties.Count +
            TextureProperties.Count + CubemapProperties.Count + Shaders.Count + Copies.Count + KeywordProperties.Count +
            ProjectorProperties.Count + NameProperties.Count;
        /// <summary>False for the common "every list nil" entries of older builds (treat as no edits).</summary>
        public bool HasEdits => ItemCount > 0;

        /// <summary>The items of every list, in the apply order (copy, name, shader, renderer, float, keyword, color, vector, texture, cubemap, projector).</summary>
        public IEnumerable<Item> AllItems()
        {
            foreach (var x in Copies) yield return x;
            foreach (var x in NameProperties) yield return x;
            foreach (var x in Shaders) yield return x;
            foreach (var x in RendererProperties) yield return x;
            foreach (var x in FloatProperties) yield return x;
            foreach (var x in KeywordProperties) yield return x;
            foreach (var x in ColorProperties) yield return x;
            foreach (var x in VectorProperties) yield return x;
            foreach (var x in TextureProperties) yield return x;
            foreach (var x in CubemapProperties) yield return x;
            foreach (var x in ProjectorProperties) yield return x;
        }

        /// <summary>Common fields: target object (ObjectType + Slot; Slot = clothes kind / absolute accessory slot / hair kind / 0).</summary>
        public abstract class Item
        {
            public ObjectType ObjectType;
            public int CoordinateIndex, Slot;
            internal void ReadCommon(object o)
            {
                ObjectType = (ObjectType)V.I(o.Get("ObjectType"), 0);
                CoordinateIndex = V.I(o.Get("CoordinateIndex"), 0);
                Slot = V.I(o.Get("Slot"), 0);
            }
        }

        public class RendererProperty : Item
        {
            public string RendererName;
            public RendererPropertyType Property;
            /// <summary>Integer as text: bools "0"/"1"; ShadowCastingMode "0" Off, "1" On, "2" TwoSided, "3" ShadowsOnly.</summary>
            public string Value, ValueOriginal;
        }

        public abstract class MaterialItem : Item
        {
            /// <summary>Material name with "(Instance)"/" Instance" removed.</summary>
            public string MaterialName;
        }

        public abstract class MaterialPropertyItem : MaterialItem
        {
            /// <summary>Shader property name without the leading underscore (e.g. "Metallic" = _Metallic).</summary>
            public string Property;
        }

        public class FloatProperty : MaterialPropertyItem
        {
            /// <summary>Float as invariant text, as stored.</summary>
            public string Value, ValueOriginal;
            public float ParsedValue => V.ParseFloat(Value, 0);
        }

        public class ColorProperty : MaterialPropertyItem
        {
            public Color Value;
            public Color? ValueOriginal;
        }

        public class VectorProperty : MaterialPropertyItem
        {
            public Vector4 Value;
            public Vector4? ValueOriginal;
        }

        public class TextureProperty : MaterialPropertyItem
        {
            /// <summary>Key into <see cref="Textures"/>; null = only offset/scale edited.</summary>
            public int? TexID;
            public Vector2? Offset, OffsetOriginal, Scale, ScaleOriginal;
            /// <summary>Texture animation (ME 5.0 only; raw map), or null.</summary>
            public object TexAnimationDef;
        }

        public class CubemapProperty : MaterialPropertyItem
        {
            public int? TexID;
        }

        public class ShaderEdit : MaterialItem
        {
            /// <summary>Unity shader name; null = render-queue-only edit.</summary>
            public string ShaderName, ShaderNameOriginal;
            /// <summary>Null = queue not edited.</summary>
            public int? RenderQueue, RenderQueueOriginal;
        }

        public class MaterialCopy : MaterialItem
        {
            /// <summary>Clone name, "&lt;MaterialName&gt;.MECopy&lt;n&gt;".</summary>
            public string MaterialCopyName;
        }

        public class KeywordProperty : MaterialPropertyItem
        {
            /// <summary>Keyword = "_" + Property enabled.</summary>
            public bool Value, ValueOriginal;
        }

        public class ProjectorProperty : Item
        {
            public string ProjectorName;
            /// <summary>0 Enabled, 1 NearClipPlane, 2 FarClipPlane, 3 FieldOfView, 4 AspectRatio, 5 Orthographic, 6 OrthographicSize, 7 IgnoreCharaLayer, 8 IgnoreMapLayer.</summary>
            public int Property;
            public string Value, ValueOriginal;
        }

        public class NameProperty : Item
        {
            public string Renderer;
            /// <summary>Original material name; <see cref="Value"/> is the new name.</summary>
            public string MaterialName, Value, ValueOriginal;
        }

        static readonly HashSet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            KeyTextures, "RendererPropertyList", "MaterialFloatPropertyList", "MaterialColorPropertyList", "MaterialVectorPropertyList",
            "MaterialTexturePropertyList", "MaterialCubemapPropertyList", "MaterialShaderList", "MaterialCopyList",
            "MaterialKeywordPropertyList", "ProjectorPropertyList", "MaterialNamePropertyList",
        };

        public static MaterialEditorData Decode(int version, object data, ExtContext ctx)
        {
            const string P = PluginIds.MaterialEditor;
            var r = new MaterialEditorData { Version = version };
            if (version < 0 || version > 2) ctx.Warn(P, $"version {version}: textures skipped (as the plugin does); edit lists read");
            if (!(data is Dictionary<object, object> map)) return r;
            foreach (var kv in map)
            {
                if (!(kv.Key is string key) || KnownKeys.Contains(key)) continue;
                if (key.StartsWith("DEDUPED_", StringComparison.Ordinal) || key.StartsWith("LOCAL_", StringComparison.Ordinal))
                {
                    r.CompatKeys[key] = ctx.Nested(kv.Value, P, key);
                    ctx.Warn(P, $"{key}: non-bundled textures are not resolved");
                }
                else ctx.Warn(P, $"unknown key {key}");
            }

            if (version >= 0 && version <= 2 && ctx.Nested(map.Get(KeyTextures), P, KeyTextures) is Dictionary<object, object> tex)
                foreach (var kv in tex)
                {
                    if (kv.Value is byte[] b && (kv.Key is long || kv.Key is ulong)) r.Textures[kv.Key.Int()] = b;
                    else ctx.Warn(P, "TextureDictionary entry is not (int, bin)");
                }

            foreach (var o in List(map, "RendererPropertyList", ctx))
            {
                var x = new RendererProperty { RendererName = o.Get("RendererName").Str(), Property = (RendererPropertyType)V.I(o.Get("Property"), 0),
                    Value = Text(o.Get("Value")), ValueOriginal = Text(o.Get("ValueOriginal")) };
                x.ReadCommon(o); r.RendererProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialFloatPropertyList", ctx))
            {
                var x = new FloatProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(),
                    Value = Text(o.Get("Value")), ValueOriginal = Text(o.Get("ValueOriginal")) };
                x.ReadCommon(o); r.FloatProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialColorPropertyList", ctx))
            {
                var x = new ColorProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(),
                    Value = V.ColN(o.Get("Value")) ?? Color.white, ValueOriginal = V.ColN(o.Get("ValueOriginal")) };
                x.ReadCommon(o); r.ColorProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialVectorPropertyList", ctx))
            {
                var x = new VectorProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(),
                    Value = V.V4N(o.Get("Value")) ?? Vector4.zero, ValueOriginal = V.V4N(o.Get("ValueOriginal")) };
                x.ReadCommon(o); r.VectorProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialTexturePropertyList", ctx))
            {
                var x = new TextureProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(),
                    TexID = V.IN(o.Get("TexID")), Offset = V.V2N(o.Get("Offset")), OffsetOriginal = V.V2N(o.Get("OffsetOriginal")),
                    Scale = V.V2N(o.Get("Scale")), ScaleOriginal = V.V2N(o.Get("ScaleOriginal")), TexAnimationDef = o.Get("TexAnimationDef") };
                x.ReadCommon(o); r.TextureProperties.Add(x);
                if (x.TexID.HasValue && !r.Textures.ContainsKey(x.TexID.Value)) ctx.Warn(P, $"TexID {x.TexID} of {x.MaterialName}.{x.Property} not in TextureDictionary");
            }
            foreach (var o in List(map, "MaterialCubemapPropertyList", ctx))
            {
                var x = new CubemapProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(), TexID = V.IN(o.Get("TexID")) };
                x.ReadCommon(o); r.CubemapProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialShaderList", ctx))
            {
                var x = new ShaderEdit { MaterialName = o.Get("MaterialName").Str(), ShaderName = V.SN(o.Get("ShaderName")),
                    ShaderNameOriginal = V.SN(o.Get("ShaderNameOriginal")), RenderQueue = V.IN(o.Get("RenderQueue")),
                    RenderQueueOriginal = V.IN(o.Get("RenderQueueOriginal")) };
                x.ReadCommon(o); r.Shaders.Add(x);
            }
            foreach (var o in List(map, "MaterialCopyList", ctx))
            {
                var x = new MaterialCopy { MaterialName = o.Get("MaterialName").Str(), MaterialCopyName = o.Get("MaterialCopyName").Str() };
                x.ReadCommon(o); r.Copies.Add(x);
            }
            foreach (var o in List(map, "MaterialKeywordPropertyList", ctx))
            {
                var x = new KeywordProperty { MaterialName = o.Get("MaterialName").Str(), Property = o.Get("Property").Str(),
                    Value = V.B(o.Get("Value"), false), ValueOriginal = V.B(o.Get("ValueOriginal"), false) };
                x.ReadCommon(o); r.KeywordProperties.Add(x);
            }
            foreach (var o in List(map, "ProjectorPropertyList", ctx))
            {
                var x = new ProjectorProperty { ProjectorName = o.Get("ProjectorName").Str(), Property = V.I(o.Get("Property"), 0),
                    Value = Text(o.Get("Value")), ValueOriginal = Text(o.Get("ValueOriginal")) };
                x.ReadCommon(o); r.ProjectorProperties.Add(x);
            }
            foreach (var o in List(map, "MaterialNamePropertyList", ctx))
            {
                var x = new NameProperty { Renderer = o.Get("Renderer").Str(), MaterialName = o.Get("MaterialName").Str(),
                    Value = o.Get("Value").Str(), ValueOriginal = o.Get("ValueOriginal").Str() };
                x.ReadCommon(o); r.NameProperties.Add(x);
            }
            return r;
        }

        static IEnumerable<object> List(Dictionary<object, object> map, string key, ExtContext ctx)
        {
            var v = ctx.Nested(map.Get(key), PluginIds.MaterialEditor, key);
            if (v == null) yield break;
            if (!(v is List<object> l)) { ctx.Warn(PluginIds.MaterialEditor, key + " is not an array"); yield break; }
            foreach (var o in l) if (o is Dictionary<object, object>) yield return o;
        }

        // Value fields are strings in this format; tolerate numbers written by other builds.
        static string Text(object o) => o is string s ? s : o is float f ? f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : o is double d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : o is long l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : o is bool b ? (b ? "1" : "0") : null;
    }
}
