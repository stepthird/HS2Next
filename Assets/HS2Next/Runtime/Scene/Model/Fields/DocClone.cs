// Deep copies of document objects: a canonical write → read round
// trip, the technique of CharaSubtree.CloneChara. The copy is exactly what a save and reload would give
// (unknown members kept, −0 written as 0.0, refs and ids validated by the writer), so commands that hold old
// and new values never share objects with the model. The canonical byte count doubles as the history size of
// the value.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class DocClone
    {
        /// <summary>A deep copy of <paramref name="value"/> (null for null). Throws when the writer rejects the object
        /// (<see cref="SaveFormatException"/>: a null required member, a non-finite float, a bad ref or id).</summary>
        public static T Clone<T>(T value) where T : DocObject => (T)Clone(value, out _, null);

        /// <summary>The canonical JSON bytes of one object (the bytes a clone is read from).</summary>
        public static byte[] Bytes(DocObject value)
        {
            if (value == null) return null;
            if (value is SceneNode n) return CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, w => n.Write(w, n.Order));
            if (value is NodeUi ui)
            {
                // The character members are written only when the flag says so; a copy keeps them all.
                bool flag = ui.IsChara;
                ui.IsChara = true;
                try { return CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, ui.WriteTo); }
                finally { ui.IsChara = flag; }
            }
            if (value is OpaqueNode) throw new ArgumentException("an opaque node section cannot be copied");
            return CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, value.WriteTo);
        }

        /// <summary>The copy, its canonical byte count, and reader findings (e.g. <c>json.unknownEnum</c>) in <paramref name="findings"/>.</summary>
        internal static DocObject Clone(DocObject value, out int bytes, FindingList findings)
        {
            bytes = 0;
            if (value == null) return null;
            var b = Bytes(value);
            bytes = b.Length;
            var r = DocReader.ForDocument(b, new DocReadContext(SceneDocument.EntryName, false, findings ?? new FindingList()));
            if (value is SceneNode) return SceneNode.Read(r);
            DocObject c;
            if (value is NodeUi ui)
            {
                var u = new NodeUi { IsChara = true };
                u.ReadFrom(r);
                u.IsChara = ui.IsChara;
                return u;
            }
            if (value is MaterialOverride) return MaterialOverride.Read(r);   // dispatch on `type` (unknown types kept)
            c = BlankOf(value);
            c.ReadFrom(r);
            return c;
        }

        /// <summary>A copy of <paramref name="value"/> read into <paramref name="blank"/> (a fresh object of the shape the
        /// destination needs: ItemLayout with or without tilt, the clothes part of a kind).</summary>
        internal static DocObject CloneInto(DocObject value, DocObject blank, FindingList findings)
        {
            if (value == null) return null;
            var r = DocReader.ForDocument(Bytes(value), new DocReadContext(SceneDocument.EntryName, false, findings ?? new FindingList()));
            blank.ReadFrom(r);
            return blank;
        }

        // A fresh object of the same class and schema shape. The character document classes without a parameterless
        // constructor carry their shape in constructor arguments (ItemLayout.HasTilt, ClothesPart.Kind); the other values
        // they take are overwritten by the read.
        static DocObject BlankOf(DocObject value)
        {
            switch (value)
            {
                case ItemPower p: return new ItemPower(p.Power);
                case ItemColor c: return new ItemColor(c.Color);
                case ItemLayout l: return new ItemLayout(l.Color, l.Layout, l.HasTilt);
                case ClothesPart k: return new ClothesPart(k.Kind);
                default: return (DocObject)Activator.CreateInstance(value.GetType());
            }
        }
    }
}
