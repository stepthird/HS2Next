// Shared pieces of the save documents: the DocObject base (one JSON object of a
// document: known members, then the unknown members kept, then `ext` where the schema has it), the
// reference value types AnimeRef and FileRef, and the constants the schema tables use as
// "absent" values. All JSON goes through CanonicalJsonWriter and DocReader; no reflection serialisation.
using System;
using System.Collections;
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>
    /// Base of every document object. A fresh object holds the "absent" value of every member (for chara and coord:
    /// HS2's MemberInit defaults); <see cref="ReadMembers"/> overwrites the members present in the JSON. Members this
    /// reader does not know are kept in <see cref="Unknown"/> and written back after the known ones, in read order.
    /// </summary>
    public abstract class DocObject
    {
        /// <summary>Unknown members; null when there are none.</summary>
        public JsonLeftovers Unknown;

        /// <summary>Reads the known members (the object's absent values stay for missing ones).</summary>
        internal abstract void ReadMembers(DocReader r);

        /// <summary>Writes the known members in schema order.</summary>
        internal abstract void WriteMembers(CanonicalJsonWriter w);

        /// <summary>The inline `ext` of objects whose schema has one (written last), else null.</summary>
        internal virtual ExtInline InlineExt => null;

        internal void ReadFrom(DocReader r)
        {
            ReadMembers(r);
            Unknown = r.Leftovers();
        }

        internal void WriteTo(CanonicalJsonWriter w)
        {
            w.StartObject();
            WriteMembers(w);
            w.Leftovers(Unknown);
            w.Ext(InlineExt);
            w.EndObject();
        }

        internal void WriteTo(CanonicalJsonWriter w, string name)
        {
            w.Name(name);
            WriteTo(w);
        }

        // ---------- helpers for the document classes ----------

        /// <summary>A new <typeparamref name="T"/> read from <paramref name="r"/>; null for a null reader (`object?` absent or null).</summary>
        internal static T ReadNew<T>(DocReader r) where T : DocObject, new()
        {
            if (r == null) return null;
            var x = new T();
            x.ReadFrom(r);
            return x;
        }

        /// <summary>An array of objects (any length); absent → empty.</summary>
        internal static List<T> ReadList<T>(DocReader r, string name) where T : DocObject, new()
        {
            var list = new List<T>();
            foreach (var e in r.ObjArray(name)) list.Add(ReadNew<T>(e));
            return list;
        }

        /// <summary>
        /// A `T[n]` member read into <paramref name="target"/> (fresh elements from <paramref name="make"/>); absent (or a
        /// lenient type error) keeps the defaults already in <paramref name="target"/>; another length is a type error.
        /// </summary>
        internal static void ReadFixed<T>(DocReader r, string name, T[] target, Func<int, T> make) where T : DocObject
        {
            var items = r.ObjArray(name, target.Length);
            if (items.Count != target.Length) return;
            for (int i = 0; i < target.Length; i++)
            {
                var x = make(i);
                x.ReadFrom(items[i]);
                target[i] = x;
            }
        }

        /// <summary>A `T[]` of objects; a null list writes `[]`, a null element fails with BadDocument and its path.</summary>
        internal static void WriteList<T>(CanonicalJsonWriter w, string name, IEnumerable<T> items) where T : DocObject
        {
            w.Name(name);
            string at = w.Path;
            w.StartArray();
            if (items != null)
            {
                int i = 0;
                foreach (var x in items)
                {
                    if (x == null) throw NullElement(w, at, i);
                    x.WriteTo(w);
                    i++;
                }
            }
            w.EndArray();
        }

        /// <summary>BadDocument for a null element at index <paramref name="i"/> of the array at <paramref name="arrayPath"/>.</summary>
        internal static SaveFormatException NullElement(CanonicalJsonWriter w, string arrayPath, int i) =>
            new SaveFormatException(SaveErrorCode.BadDocument, "array element is null", w.Entry, arrayPath + "[" + i + "]");

        /// <summary>Writes an `object?` member (null writes null).</summary>
        internal static void WriteN(CanonicalJsonWriter w, string name, DocObject x)
        {
            if (x == null) w.Null(name);
            else x.WriteTo(w, name);
        }

        // Non-nullable members: a null (or a fixed-length array of another length) in the model would give a file that
        // the reader rejects, so the writer fails here with BadDocument and the member's path.

        static SaveFormatException Invalid(CanonicalJsonWriter w, string detail) =>
            new SaveFormatException(SaveErrorCode.BadDocument, detail, w.Entry, w.Path);

        internal static void ReqStr(CanonicalJsonWriter w, string name, string v)
        {
            w.Name(name);
            if (v == null) throw Invalid(w, "required string is null");
            w.Str(v);
        }

        internal static void ReqRef(CanonicalJsonWriter w, string name, string v)
        {
            w.Name(name);
            if (v == null) throw Invalid(w, "required ref is null");
            w.Ref(v);
        }

        internal static void ReqId(CanonicalJsonWriter w, string name, string v)
        {
            w.Name(name);
            if (v == null) throw Invalid(w, "required id is null");
            w.Id(v);
        }

        internal static void ReqRes(CanonicalJsonWriter w, string name, string v)
        {
            w.Name(name);
            if (v == null) throw Invalid(w, "required res is null");
            w.Res(v);
        }

        internal static void CheckLength(CanonicalJsonWriter w, ICollection a, int length)
        {
            if (a == null) throw Invalid(w, "array is null");
            if (length >= 0 && a.Count != length) throw Invalid(w, "array has " + a.Count + " elements, the schema needs " + length);
        }

        /// <summary>A non-null f32[] (exactly <paramref name="length"/> elements unless -1).</summary>
        internal static void F32s(CanonicalJsonWriter w, string name, float[] a, int length = -1)
        {
            w.Name(name);
            CheckLength(w, a, length);
            w.F32Array(a);
        }

        internal static void Ints(CanonicalJsonWriter w, string name, int[] a, int length)
        {
            w.Name(name);
            CheckLength(w, a, length);
            w.IntArray(a);
        }

        internal static void Bools(CanonicalJsonWriter w, string name, bool[] a, int length)
        {
            w.Name(name);
            CheckLength(w, a, length);
            w.BoolArray(a);
        }

        /// <summary>A non-null `bool[]` of any length held as a list.</summary>
        internal static void Bools(CanonicalJsonWriter w, string name, List<bool> a)
        {
            w.Name(name);
            CheckLength(w, a, -1);
            w.BoolArray(a);
        }

        /// <summary>A `T[n]` of objects (no null elements).</summary>
        internal static void WriteFixed<T>(CanonicalJsonWriter w, string name, T[] items, int length) where T : DocObject
        {
            w.Name(name);
            string at = w.Path;
            CheckLength(w, items, length);
            w.StartArray();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) throw NullElement(w, at, i);
                items[i].WriteTo(w);
            }
            w.EndArray();
        }

        /// <summary>A `col[n]`.</summary>
        internal static void WriteColors(CanonicalJsonWriter w, string name, Color4[] items, int length)
        {
            w.Name(name);
            CheckLength(w, items, length);
            w.StartArray();
            foreach (var c in items) w.Col(c);
            w.EndArray();
        }
    }

    /// <summary>Constants of the schema tables.</summary>
    public static class DocDefaults
    {
        /// <summary>The "none" row of a base-game list (`ref` absent value; HS2 id 0 is a real row).</summary>
        public const string NoneRef = "illusion.hs2#0";
        /// <summary>`source` of base-game Anime/Voice rows.</summary>
        public const string VanillaSource = "illusion.hs2";

        /// <summary>A byte colour of HS2 as float32(c/255) with alpha 1.</summary>
        public static Color4 Byte(int r, int g, int b) => new Color4(r / 255f, g / 255f, b / 255f, 1f);

        public static float[] Fill(int n, float v)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }
    }

    /// <summary>`animeRef`: Anime/HAnime/Voice row identity (source GUID, global group, category, no).</summary>
    public sealed class AnimeRef : DocObject
    {
        public string Source = DocDefaults.VanillaSource;
        public int Group, Category, No;

        public AnimeRef() { }
        public AnimeRef(string source, int group, int category, int no) { Source = source; Group = group; Category = category; No = no; }

        internal override void ReadMembers(DocReader r)
        {
            Source = r.Str("source", Source);
            Group = r.Int("group", Group);
            Category = r.Int("category", Category);
            No = r.Int("no", No);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.RefGuid(Source);
            ReqStr(w, "source", Source);
            w.Int("group", Group);
            w.Int("category", Category);
            w.Int("no", No);
        }
    }

    /// <summary>`file`: exactly one of `res` (embedded copy), `user` (path relative to HS2Next's UserData) or
    /// `hs2` (path relative to the HS2 install root).</summary>
    public sealed class FileRef : DocObject
    {
        public enum Kinds { Res, User, Hs2, None }

        /// <summary>Which member is set; <see cref="Kinds.None"/> only for a file object without a known member (a newer
        /// writer's kind, kept in <see cref="DocObject.Unknown"/>).</summary>
        public Kinds Kind;
        /// <summary>The `res/&lt;sha256&gt;.&lt;ext&gt;` entry name or the relative path.</summary>
        public string Value;

        public static FileRef FromRes(string res) => new FileRef { Kind = Kinds.Res, Value = res };
        public static FileRef FromUser(string path) => new FileRef { Kind = Kinds.User, Value = path };
        public static FileRef FromHs2(string path) => new FileRef { Kind = Kinds.Hs2, Value = path };

        internal override void ReadMembers(DocReader r)
        {
            int n = 0;
            Kind = Kinds.None;
            Value = null;
            if (r.Has("res")) { Kind = Kinds.Res; Value = r.Res("res"); n++; }
            if (r.Has("user")) { Kind = Kinds.User; Value = r.Str("user"); n++; }
            if (r.Has("hs2")) { Kind = Kinds.Hs2; Value = r.Str("hs2"); n++; }
            if (n > 1) throw new DocTypeException(r.Context, r.Object, r.Path, "a file object holds more than one of res, user, hs2");
            if (n == 0) r.Report(FindingSeverity.Warning, "file.unknownKind", null, "file object without res, user or hs2 kept as read");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            switch (Kind)
            {
                case Kinds.Res: ReqRes(w, "res", Value); break;
                case Kinds.User: ReqStr(w, "user", Value); break;
                case Kinds.Hs2: ReqStr(w, "hs2", Value); break;
            }
        }
    }
}
