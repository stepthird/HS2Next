// Face presets of a head row: the TextAsset named by the row's `Preset` column, loaded
// from the row's MainAB as the game does (a head whose bundle lacks it gets no preset: the game then loads nothing), decoded
// (a MessagePack ChaFileFace) and converted with the importer's face mapping (version migrations
// included) into a FaceData whose `ref` members are resolved by HeadPresets (the headPresetInfo GUIDs; other ids vanilla).
// The op that pushes it (StudioOps.Maker.ApplyFacePreset) sets every member of the `face` block in one composite. Main
// thread (bundle loads).
using System;
using System.Collections.Generic;
using HS2Next.Import;
using HS2Next.IO;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Content.Maker
{
    public sealed class FacePresetResult
    {
        /// <summary>The head row whose preset was loaded.</summary>
        public ListRow Head;
        /// <summary>The headPresetInfo entry used (null: none matched; every id stays vanilla).</summary>
        public HeadPresetEntry Entry;
        /// <summary>The row names no preset, or its MainAB has no such TextAsset (finding `edit.presetMissing`): <see cref="Face"/> is null.</summary>
        public bool Missing;
        /// <summary>The converted face block (refs resolved), or null.</summary>
        public FaceData Face;
        /// <summary>The preset's local ids by document member (after the importer's version migrations).</summary>
        public readonly Dictionary<string, int> LocalIds = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The references written into <see cref="Face"/> by member.</summary>
        public readonly Dictionary<string, ContentId> Refs = new Dictionary<string, ContentId>(StringComparer.Ordinal);
        /// <summary>`edit.presetUnresolved …`, `edit.presetMissing …` and the importer's findings on the preset data.</summary>
        public readonly List<string> Findings = new List<string>();
    }

    public static class FacePresetLoader
    {
        /// <summary>The preset TextAsset bytes of a head row (MainAB / Preset), or null.</summary>
        public static byte[] LoadBytes(ContentCatalog catalog, ListEntry head)
        {
            if (catalog == null || head == null || HeadPresets.PresetName(new ListRow(head.Category, head.Key, head.Values)) == null) return null;
            var ta = catalog.LoadAsset<TextAsset>(head, "MainAB", "Preset");
            return ta ? ta.bytes : null;
        }

        /// <summary>Loads, converts and resolves the face preset of <paramref name="head"/> for a character of <paramref name="sex"/>.</summary>
        public static FacePresetResult Load(ContentCatalog catalog, MakerLists lists, ContentId head, int sex)
        {
            var rows = lists?.Source ?? throw new ArgumentNullException(nameof(lists));
            var r = new FacePresetResult { Head = rows.Find(sex == 0 ? 110 : 210, head) };
            if (r.Head == null) { r.Missing = true; r.Findings.Add($"edit.presetMissing {head}: no head row for this sex"); return r; }
            r.Entry = HeadPresets.FindEntry(rows, r.Head);
            var bytes = LoadBytes(catalog, catalog.GetEntry(r.Head.Category, r.Head.Key));
            if (bytes == null) { r.Missing = true; r.Findings.Add($"edit.presetMissing {head}: preset {HeadPresets.PresetName(r.Head) ?? "(none)"} not in {r.Head["MainAB"]}"); return r; }
            object tree;
            try { tree = MsgPack.Read(bytes); }
            catch (Exception ex) { r.Missing = true; r.Findings.Add($"edit.presetMissing {head}: preset unreadable ({ex.Message})"); return r; }
            Convert(rows, r, tree, sex);
            return r;
        }

        /// <summary>Converts a decoded ChaFileFace tree with the importer's face mapping and resolves its refs (internal: tests).</summary>
        internal static void Convert(IListRows rows, FacePresetResult r, object tree, int sex)
        {
            var report = new ImportReport { Kind = "facePreset", SourcePath = r.Head?.ToString() };
            var x = new ImportContext { Report = report, Raw = new RawReader(report) };
            var face = new FaceData();
            VanillaImport.Face(x, tree, face, sex);
            foreach (var f in x.Refs)
            {
                var p = HeadPresets.ByMember(f.Path);
                ContentId id;
                if (p != null)
                {
                    id = HeadPresets.Resolve(rows, r.Entry, p, f.Raw, sex, out bool bad);
                    if (bad) r.Findings.Add($"edit.presetUnresolved {f.Path}: {HeadPresets.GuidOf(r.Entry, p)}#{f.Raw} is not installed in category {p.CategoryFor(sex)}; kept as vanilla id");
                }
                else id = ContentId.Vanilla(f.Raw);    // beard: no headPresetInfo element
                r.LocalIds[f.Path] = f.Raw;
                r.Refs[f.Path] = id;
                f.Result = id.ToString();
                f.Set?.Invoke(f.Result);
            }
            foreach (var fd in report.Findings) r.Findings.Add(fd.ToString());
            r.Face = face;
        }
    }
}
