// What an item of a materialOverrides array is addressed by: an edit replaces the item with its key, a removal removes every item
// with it. Strings compare ordinally, as stored. `material` is the formatted name (Format), `property` the stored text, which is the
// shader property without its first underscore; `renderer` is a renderer's name in a character's arrays and a path in an item's or
// the map's. An item of an unknown type has no key: no edit ever matches it. Unity-free.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public readonly struct MeKey : IEquatable<MeKey>
    {
        /// <summary>The `property` of a renderer's visibility: the one renderer property that can be edited.</summary>
        public const string EnabledProperty = "Enabled";

        /// <summary>The item's `type`.</summary>
        public readonly string Type;
        /// <summary>The material, or the renderer (`renderer`, `name`), or the projector.</summary>
        public readonly string Target;
        /// <summary>The property, or the copy's name (`copy`), or the material (`name`); null for `shader`.</summary>
        public readonly string Member;

        public MeKey(string type, string target, string member)
        {
            Type = type;
            Target = target;
            Member = member;
        }

        /// <summary>The key of a `float`, `keyword`, `color`, `vector`, `texture` or `cubemap` item.</summary>
        public static MeKey Property(string type, string material, string property) => new MeKey(type, material, property);
        /// <summary>The key of a `shader` item.</summary>
        public static MeKey Shader(string material) => new MeKey("shader", material, null);
        /// <summary>The key of a `renderer` item.</summary>
        public static MeKey Renderer(string renderer, string property) => new MeKey("renderer", renderer, property);
        /// <summary>The key of a renderer's `Enabled` item.</summary>
        public static MeKey Enabled(string renderer) => Renderer(renderer, EnabledProperty);

        /// <summary>A material's name as the arrays store it: the engine's instance marks removed, trimmed.</summary>
        public static string Format(string name) => name == null ? "" : name.Replace("(Instance)", "").Replace(" Instance", "").Trim();

        /// <summary>The key of an item; false for an item of an unknown type.</summary>
        public static bool TryOf(MaterialOverride item, out MeKey key)
        {
            switch (item)
            {
                case MeCopy c: key = new MeKey(c.Type, c.Material, c.CopyName); return true;
                case MeName n: key = new MeKey(n.Type, n.Renderer, n.Material); return true;
                case MeShader s: key = Shader(s.Material); return true;
                case MeRenderer r: key = Renderer(r.Renderer, r.Property); return true;
                case MeProjector p: key = new MeKey(p.Type, p.Projector, p.Property); return true;
                case MeMaterialProperty m: key = Property(m.Type, m.Material, m.Property); return true;
                default: key = default; return false;
            }
        }

        /// <summary>True when <paramref name="item"/> has this key.</summary>
        public bool Matches(MaterialOverride item) => TryOf(item, out var k) && Equals(k);

        public bool Equals(MeKey o) =>
            string.Equals(Type, o.Type, StringComparison.Ordinal) && string.Equals(Target, o.Target, StringComparison.Ordinal) && string.Equals(Member, o.Member, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is MeKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Type == null ? 0 : Type.GetHashCode();
                h = h * 397 ^ (Target == null ? 0 : Target.GetHashCode());
                return h * 397 ^ (Member == null ? 0 : Member.GetHashCode());
            }
        }

        public static bool operator ==(MeKey a, MeKey b) => a.Equals(b);
        public static bool operator !=(MeKey a, MeKey b) => !a.Equals(b);

        /// <summary>The key as one text, for messages and merge keys.</summary>
        public override string ToString() => Member == null ? Type + "/" + Target : Type + "/" + Target + "/" + Member;
    }
}
