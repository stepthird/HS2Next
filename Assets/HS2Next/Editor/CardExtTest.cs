// Batch test: decode the plugin extension data (HS2Next.Data.Ext) of every character card and coordinate card
// and compare the counts with the verified counts. Writes Spike/out/content/card_ext.txt.
// Run: -executeMethod CardExtTest.Run [-cards <dir>] [-coords <dir>] [-limit N]
//   -cards <dir>: one character-card folder instead of UserData/chara/female + male (labelled "cards");
//   -coords <dir>: coordinate folder (default UserData/coordinate); -limit N: at most N files per group.
// Folders are searched recursively. Paths come from Config/settings.local.json (see CardResolveTest.Settings).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class CardExtTest
{
    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    // Counter bag with sorted output.
    class Hist
    {
        public readonly Dictionary<string, int> D = new Dictionary<string, int>(StringComparer.Ordinal);
        public void Add(string k, int n = 1) { D.TryGetValue(k, out int v); D[k] = v + n; }
        public int this[string k] => D.TryGetValue(k, out int v) ? v : 0;
        public string Top(int n = 12) => string.Join(", ", D.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(n).Select(x => $"{x.Key} {x.Value}"));
    }

    class Group
    {
        public string Name;
        public bool Coord;
        public int Files, Parsed, WithKKEx, NoPngParsed;
        public long Bytes, ReadTicks, LoadTicks, DecodeTicks;
        public Hist Fail = new Hist(), Present = new Hist(), Nil = new Hist(), Versions = new Hist(), N = new Hist(), Diag = new Hist(), Nested = new Hist();
        public Hist MaXmlVersion = new Hist(), AbmxBones = new Hist(), BodyGuids = new Hist(), MeObjType = new Hist(), MeListFiles = new Hist(), MeListItems = new Hist();
        public Hist KsoxTypes = new Hist(), KcoxIds = new Hist(), PushKeys = new Hist(), Opaque = new Hist();
        public List<int> AbmxPerCard = new List<int>();
        public int MaMaxUsed, MaxSlot = -1;
        public List<string> Examples = new List<string>();
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        string game = CardResolveTest.GameRoot;
        string user = Path.Combine(game, "UserData");
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString()));
        string cardsArg = Arg("-cards", null);
        var groups = new List<(Group g, string dir)>();
        if (cardsArg != null) groups.Add((new Group { Name = "cards" }, cardsArg));
        else
        {
            groups.Add((new Group { Name = "female" }, Path.Combine(user, "chara", "female")));
            groups.Add((new Group { Name = "male" }, Path.Combine(user, "chara", "male")));
        }
        groups.Add((new Group { Name = "coord", Coord = true }, Arg("-coords", Path.Combine(user, "coordinate"))));

        CardExtensions.ProfileTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        var total = Stopwatch.StartNew();
        foreach (var (g, dir) in groups)
        {
            if (!Directory.Exists(dir)) { sb.AppendLine($"MISSING folder for {g.Name}: {dir}"); continue; }
            var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Take(limit).ToList();
            foreach (var f in files) One(g, f, dir);
        }
        total.Stop();
        s_profile = CardExtensions.ProfileTicks;
        CardExtensions.ProfileTicks = null;

        Report(sb, groups.Select(x => x.g).ToList(), total.Elapsed.TotalMilliseconds);
        SelfChecks(sb);
        var outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "content");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "card_ext.txt"), sb.ToString().Replace("\r\n", "\n"));
        Debug.Log("CARD EXT TEST DONE");
        if (Application.isBatchMode) TestExit.Exit(0);
    }

    static void One(Group g, string file, string root)
    {
        g.Files++;
        long t0 = Stopwatch.GetTimestamp();
        Card card = null; CoordCard coord = null;
        var bytes = File.ReadAllBytes(file);
        long tr = Stopwatch.GetTimestamp();
        g.ReadTicks += tr - t0;
        try
        {
            if (g.Coord) coord = CoordCard.Load(bytes, file); else card = Card.Load(bytes, file);
        }
        catch (Exception e) { g.Fail.Add(e.GetType().Name + ": " + Trunc(e.Message, 70)); return; }
        long t1 = Stopwatch.GetTimestamp();
        var x = g.Coord ? CardExtensions.Decode(coord) : CardExtensions.Decode(card);
        long t2 = Stopwatch.GetTimestamp();
        g.LoadTicks += t1 - tr; g.DecodeTicks += t2 - t1;
        g.Bytes += bytes.Length;
        if (!Card.HasPng(bytes))
        {
            // Maker autosaves carry no thumbnail. They parse, but the spec's census excluded them; keep them out of the counts.
            g.NoPngParsed++;
            return;
        }
        g.Parsed++;
        object kkex = g.Coord ? coord.KKEx : card.KKEx;
        if (kkex is Dictionary<object, object>) g.WithKKEx++;
        string rel = file.Substring(root.Length).TrimStart('\\', '/');
        SpotCheck(rel, x);

        foreach (var kv in x.Versions) { g.Present.Add(kv.Key); g.Versions.Add($"{kv.Key} v{kv.Value}"); }
        foreach (var id in x.NilIds) g.Nil.Add(id);
        foreach (var d in x.Diagnostics) g.Diag.Add(Trunc(System.Text.RegularExpressions.Regex.Replace(d, @"\d+", "#"), 110));
        foreach (var kv in x.Counts) if (kv.Key.StartsWith("nested.")) g.Nested.Add(kv.Key.Substring(7), kv.Value); else g.N.Add(kv.Key, kv.Value);
        foreach (var o in x.Opaque) g.Opaque.Add(o.Reason == null ? o.Guid : $"{o.Guid} ({o.Reason})");

        // Span check: every PluginData span re-parses, and opaque bytes equal their span.
        foreach (var kv in x.Spans)
        {
            var seg = x.RawPluginData(kv.Key);
            try { int p = seg.Offset; MsgPack.Read(seg.Array, ref p); if (p != seg.Offset + seg.Count) g.N.Add("span.lengthMismatch"); else g.N.Add("span.ok"); }
            catch { g.N.Add("span.unreadable"); }
        }
        foreach (var o in x.Opaque)
        {
            if (o.Raw == null) { g.N.Add("opaque.noRaw"); continue; }
            var back = MsgPack.Read(o.Raw);
            if (back is List<object> l && (l.Count == 0 || l[0].Int(-1) == o.Version)) g.N.Add("opaque.verbatimOk"); else g.N.Add("opaque.verbatimBad");
        }

        // ---- resolve
        g.N.Add("resolve.entries", x.Resolve.Count);
        foreach (var r in x.Resolve)
        {
            if (r.Author != null || r.Website != null || r.Name != null) g.N.Add("resolve.withManifestFields");
            if (r.LocalSlot < 100000000) g.N.Add("resolve.localSlotBelow1e8");
            int s = r.AccessorySlot;
            if (s >= 0)
            {
                Card.ResolveInfo.TryParseAccessorySlot(r.Property, out _, out bool prefixed);
                if (prefixed == g.Coord) g.N.Add("resolve.accessoryPrefixUnexpected");
                g.N.Add("resolve.accessory");
                if (s >= MoreAccessoriesData.FirstSlot) g.N.Add("resolve.accessoryExtraSlot");
                if (Card.ResolveInfo.AccessoryProperty(s, !g.Coord) != r.Property) g.N.Add("resolve.accessoryRoundTripBad");
            }
        }

        // ---- moreAccessories
        if (x.MoreAccessories != null)
        {
            var ma = x.MoreAccessories;
            g.N.Add("ma.present");
            g.MaXmlVersion.Add(ma.XmlVersion ?? "(none)");
            int used = ma.UsedCount;
            if (used > 0)
            {
                g.N.Add("ma.cardsWithUsed"); g.N.Add("ma.usedSlots", used);
                g.MaMaxUsed = Math.Max(g.MaMaxUsed, used);
                if (g.Examples.Count < 40) g.Examples.Add($"moreAccessories {rel}: {ma.Parts.Count} elements, {used} used, xml v{ma.XmlVersion}");
                foreach (var p in ma.Parts)
                {
                    if (p.IsEmpty) continue;
                    g.MaxSlot = Math.Max(g.MaxSlot, p.Slot);
                    var prop = Card.ResolveInfo.AccessoryProperty(p.Slot, !g.Coord);
                    var ri = x.Resolve.FirstOrDefault(r => r.Property == prop);
                    if (ri != null) { g.N.Add("ma.usedWithResolve"); if (ri.Category != p.Type) g.N.Add("ma.resolveCategoryMismatch"); }
                    if (p.MissingAttributes > 0) g.N.Add("ma.partsWithMissingAttributes");
                    if (p.Show.HasValue) g.N.Add("ma.showAttribute");
                }
            }
        }

        // ---- ABMX
        if (x.Abmx != null && x.Abmx.Modifiers.Count > 0)
        {
            g.N.Add("abmx.cards");
            g.AbmxPerCard.Add(x.Abmx.Modifiers.Count);
            foreach (var m in x.Abmx.Modifiers)
            {
                g.N.Add("abmx.modifiers");
                g.AbmxBones.Add(m.BoneName);
                if (!m.HasLocation) g.N.Add("abmx.locAbsent");
                else if (m.Location == 1) g.N.Add("abmx.locBodyTop");
                else if (m.AccessorySlot >= 0) g.N.Add("abmx.locAccessory");
                else g.N.Add("abmx.loc" + m.Location);
                if (m.CoordinateModifiers.Count != 1) g.N.Add("abmx.coordModsNot1");
                foreach (var d in m.CoordinateModifiers)
                {
                    if (d.FieldCount == 2) g.N.Add("abmx.data2Fields");
                    if (!Exact(d.Scale, Vector3.one)) g.N.Add("abmx.nonDefScale");
                    if (d.Length != 1) g.N.Add("abmx.nonDefLength");
                    if (!Exact(d.Rotation, Vector3.zero)) g.N.Add("abmx.nonDefRotation");
                    if (!Exact(d.Position, Vector3.zero)) g.N.Add("abmx.nonDefPosition");
                }
            }
        }

        // ---- uncensor
        if (x.Uncensor != null)
        {
            var u = x.Uncensor;
            g.N.Add("unc.present");
            if (u.BodyGUID != null) { g.N.Add("unc.body"); g.BodyGuids.Add(u.BodyGUID); }
            if (u.PenisGUID != null) g.N.Add("unc.penis");
            if (u.BallsGUID != null) g.N.Add("unc.balls");
            if (u.DisplayBalls == true) g.N.Add("unc.displayBalls");
            if (u.DisplayPenis == true) g.N.Add("unc.displayPenis");
        }

        // ---- MaterialEditor
        if (x.MaterialEditor != null)
        {
            var me = x.MaterialEditor;
            g.N.Add("me.present");
            if (me.HasEdits) g.N.Add("me.withEdits");
            if (me.Textures.Count > 0) { g.N.Add("me.withTextures"); g.N.Add("me.textures", me.Textures.Count); g.N.Add("me.textureBytesMB", (int)(me.Textures.Values.Sum(b => (long)b.Length) >> 20)); }
            var types = new HashSet<MaterialEditorData.ObjectType>();
            foreach (var it in me.AllItems())
            {
                types.Add(it.ObjectType);
                g.MeObjType.Add(it.ObjectType.ToString());
                if (it.ObjectType == MaterialEditorData.ObjectType.Accessory && it.Slot >= 20) g.N.Add("me.extraSlotItems");
                if (it.CoordinateIndex != 0) g.N.Add("me.coordIndexNot0");
            }
            foreach (var t in types) g.N.Add("me.filesWith" + t);
            void L(string name, int n) { if (n > 0) { g.MeListFiles.Add(name); g.MeListItems.Add(name, n); } }
            L("Renderer", me.RendererProperties.Count); L("Float", me.FloatProperties.Count); L("Color", me.ColorProperties.Count);
            L("Vector", me.VectorProperties.Count); L("Texture", me.TextureProperties.Count); L("Cubemap", me.CubemapProperties.Count);
            L("Shader", me.Shaders.Count); L("Copy", me.Copies.Count); L("Keyword", me.KeywordProperties.Count);
            L("Projector", me.ProjectorProperties.Count); L("Name", me.NameProperties.Count);
            int unref = me.Textures.Keys.Count(k => !me.TextureProperties.Any(t => t.TexID == k) && !me.CubemapProperties.Any(t => t.TexID == k));
            if (unref > 0) g.N.Add("me.unreferencedTextures", unref);
            g.N.Add("me.texItemsWithoutTexID", me.TextureProperties.Count(t => !t.TexID.HasValue));
        }

        // ---- KSOX
        if (x.SkinOverlays != null)
        {
            var k = x.SkinOverlays;
            g.N.Add("ksox.present");
            if (!k.HasTextures) g.N.Add($"ksox.v{k.Version}.empty");
            else
            {
                g.N.Add("ksox.withTextures"); g.N.Add("ksox.textures", k.Textures.Count);
                foreach (var t in k.Textures.Keys) g.KsoxTypes.Add(t.ToString());
            }
            if (!k.EnableInStudioSkin || !k.EnableInStudioIris) g.N.Add("ksox.studioSwitchOff");
        }

        // ---- KCOX
        if (x.ClothesOverlays != null)
        {
            var k = x.ClothesOverlays;
            g.N.Add("kcox.present");
            if (k.Overlays.Count > 0)
            {
                g.N.Add("kcox.withEntries"); g.N.Add("kcox.entries", k.Overlays.Count);
                foreach (var kv in k.Overlays) { g.KcoxIds.Add(kv.Key); if (kv.Value.Override) g.N.Add("kcox.override"); if (kv.Value.BlendingMode != 0) g.N.Add("kcox.blendingMode"); }
            }
            if (k.TextureCount > 0) { g.N.Add("kcox.withTexture"); g.N.Add("kcox.textures", k.TextureCount); }
            if (k.TextureSizeOverride.Count > 0) g.N.Add("kcox.sizeOverride");
        }

        // ---- pushUp
        if (x.PushUp != null)
        {
            g.N.Add("push.present");
            if (!x.PushUp.IsDefault) g.N.Add("push.nonDefault");
            g.PushKeys.Add($"v{x.PushUp.Version}/{x.PushUp.KnownKeyCount + x.PushUp.OtherKeys.Count}");
        }

        // ---- DBE
        if (x.DynamicBoneEditor != null)
        {
            g.N.Add("dbe.present");
            if (x.DynamicBoneEditor.Entries.Count > 0)
            {
                g.N.Add("dbe.withEntries"); g.N.Add("dbe.entries", x.DynamicBoneEditor.Entries.Count);
                g.Examples.Add($"DBE {rel}: slots {string.Join(",", x.DynamicBoneEditor.Entries.Select(e => e.Slot))}");
            }
        }

        // ---- InvisibleBody
        if (x.InvisibleBody != null) { g.N.Add("ib.present"); if (!x.InvisibleBody.Visible) g.N.Add("ib.hidden"); }

        // ---- AAC
        if (x.AdditionalAccessoryControls != null)
        {
            var a = x.AdditionalAccessoryControls;
            g.N.Add("aac.present");
            g.N.Add("aac.slots", a.Slots.Count);
            g.N.Add("aac.nonEmptySlots", a.Slots.Count(s => !s.IsEmpty));
            if (a.Slots.Count > 20) g.N.Add("aac.cardsOver20Slots");
            int rules = a.RuleCount;
            if (rules > 0) { g.N.Add("aac.cardsWithRules"); g.N.Add("aac.rules", rules); g.Examples.Add($"AAC {rel}: {string.Join("; ", a.Slots.Where(s => s.VisibilityRules.Count > 0).Select(s => s.SlotNumber + " " + string.Join("+", s.VisibilityRules)))}"); }
            if (a.Slots.Any(s => s.AdvancedParent != null)) g.N.Add("aac.advancedParent");
            if (a.Slots.Any(s => s.CharacterAccessory)) g.N.Add("aac.characterAccessory");
            if (a.SuppressedRules.Count + a.OverrideRules.Count > 0) g.N.Add("aac.overrideOrSuppress");
            if (a.OverrideDataRaw == null) g.N.Add("aac.noOverrideData");
        }
    }

    // ---- spot checks against the spec's verified examples (run when the file is in the input set)
    static readonly List<string> s_spot = new List<string>();
    static void Spot(string what, bool ok) => s_spot.Add($"{(ok ? "ok  " : "FAIL")} {what}");

    static void SpotCheck(string rel, CardExtData x)
    {
        string name = Path.GetFileName(rel);
        if (name == "AISChaF_20200220191234512.png" && !x.IsCoordinate)
        {
            var p = x.MoreAccessories?.Parts.ElementAtOrDefault(6);
            Spot("AISChaF_20200220191234512: 45 elements, 43 used", x.MoreAccessories?.Parts.Count == 45 && x.MoreAccessories.UsedCount == 43);
            Spot("element 6 = slot 26 type 351 id 1 N_Head", p != null && p.Slot == 26 && p.Type == 351 && p.Id == 1 && p.ParentKey == "N_Head");
            var r = x.Resolve.FirstOrDefault(e => e.AccessorySlot == 26);
            Spot("slot 26 resolve com.DeathWeasel.XIII#1 cat 351, outfit. prefix", r != null && r.GUID == "com.DeathWeasel.XIII" && r.Slot == 1 && r.Category == 351 && r.Property == "outfit.accessory26.ChaFileAccessory.PartsInfo.id");
            var k = x.ClothesOverlays;
            Spot("KCOX ct_gloves [PNG, true], ct_shoes [nil, true], ct_socks [PNG, true]", k != null && k.Overlays.Count == 3 &&
                k.Overlays.TryGetValue("ct_gloves", out var g1) && g1.Override && IsPng(g1.TextureBytes) &&
                k.Overlays.TryGetValue("ct_shoes", out var g2) && g2.Override && g2.TextureBytes == null &&
                k.Overlays.TryGetValue("ct_socks", out var g3) && g3.Override && IsPng(g3.TextureBytes));
        }
        if (name == "AISCoordeF_20200222032337708.png" && x.IsCoordinate)
            Spot("coordinate AISCoordeF_20200222032337708: slot 26 resolve unprefixed", x.Resolve.Any(e => e.Property == "accessory26.ChaFileAccessory.PartsInfo.id"));
        if (name == "HS2ChaF_20251109104122487_Eva MGS Delta.png")
        {
            var m = x.Abmx?.Modifiers;
            Spot("Eva MGS Delta: 24 ABMX modifiers, all BodyTop", m != null && m.Count == 24 && m.All(b => b.Location == 1));
            var h = m?.FirstOrDefault(b => b.BoneName == "cf_J_Hand_s_L");
            Spot("cf_J_Hand_s_L scale 1.1", h != null && Mathf.Approximately(h.Data.Scale.x, 1.1f) && Mathf.Approximately(h.Data.Scale.z, 1.1f));
            var e = m?.FirstOrDefault(b => b.BoneName == "cf_J_EyePos_rz_R");
            Spot("cf_J_EyePos_rz_R rotation (2, 6, 0)", e != null && (e.Data.Rotation - new Vector3(2, 6, 0)).sqrMagnitude < 1e-6f);
            Spot("Eva MGS Delta uncensor bp.sac_meaty_v2, penis/balls nil, displays false", x.Uncensor != null && x.Uncensor.BodyGUID == "bp.sac_meaty_v2" &&
                x.Uncensor.PenisGUID == null && x.Uncensor.BallsGUID == null && x.Uncensor.DisplayPenis == false && x.Uncensor.DisplayBalls == false);
        }
        if (name == "HS2ChaF_20250829211210545_Rowena Mod1.png")
            Spot("Rowena Mod1: cf_J_LegUp00_L/R scale 0.859", x.Abmx?.Modifiers.Count == 2 &&
                x.Abmx.Modifiers.All(b => b.BoneName.StartsWith("cf_J_LegUp00_") && Mathf.Abs(b.Data.Scale.x - 0.859f) < 1e-3f));
        if (name == "HS2ChaF_20230731152336501.png")
            Spot("KSOX v2 FaceOver id 5544, BodyOver id 2707", x.SkinOverlays?.Version == 2 && x.SkinOverlays.TextureIds.TryGetValue(SkinTexType.FaceOver, out int f) && f == 5544 &&
                x.SkinOverlays.TextureIds.TryGetValue(SkinTexType.BodyOver, out int b2) && b2 == 2707 && IsPng(x.SkinOverlays.Textures[SkinTexType.BodyOver]));
        if (name == "HS2ChaF_20240630115531448_Momiji V SSS.png")
        {
            var me = x.MaterialEditor;
            var sh = me?.Shaders.FirstOrDefault(z => z.ObjectType == MaterialEditorData.ObjectType.Hair && z.Slot == 1);
            Spot("Momiji: 43 float, 12 color, 7 shader items; hair slot 1 Next-Gen Hair Bayer queue 2510", me != null && me.FloatProperties.Count == 43 &&
                me.ColorProperties.Count == 12 && me.Shaders.Count == 7 && sh != null && sh.ShaderName == "Hanmen/Next-Gen Hair Bayer" && sh.RenderQueue == 2510 && sh.RenderQueueOriginal == 2450);
        }
        if (name == "HS2ChaF_20211214200718239.png")
            Spot("HS2ChaF_20211214200718239: 7 embedded textures, ids 1..7", x.MaterialEditor?.Textures.Count == 7 && Enumerable.Range(1, 7).All(i => x.MaterialEditor.Textures.ContainsKey(i)));
        if (name == "HS2CoordeF_RAPI.png")
            Spot("HS2CoordeF_RAPI: DBE 5 entries; slot 3 damping 0.2, radius 0, inertia original 0.8", x.DynamicBoneEditor?.Entries.Count == 5 &&
                x.DynamicBoneEditor.Entries.Any(e => e.Slot == 3 && e.Damping == 0.2f && e.Radius == 0f && e.InertiaOriginal == 0.8f && e.Inertia == null));
        if (name == "BR-Chan HS2 Rev2.png")
            Spot("BR-Chan: moreAccessories empty (xml 1.2.0), KSOX v1 empty, KCOX v1 empty, no pushUp", x.MoreAccessories?.Parts.Count == 0 &&
                x.MoreAccessories.XmlVersion == "1.2.0" && x.SkinOverlays?.Version == 1 && !x.SkinOverlays.HasTextures && x.ClothesOverlays?.Overlays.Count == 0 && x.PushUp == null);
    }

    static bool IsPng(byte[] b) => b != null && b.Length > 8 && b[0] == 0x89 && b[1] == (byte)'P';

    // ---- synthetic checks of the LZ4 / ext handling and the property helpers
    static void SelfChecks(StringBuilder sb)
    {
        var list = new List<string>();
        void C(string what, bool ok) => list.Add($"{(ok ? "ok  " : "FAIL")} {what}");
        // "abc", match offset 3 length 6, final literal "x" -> "abcabcabcx"
        var blk = new byte[] { 0x32, (byte)'a', (byte)'b', (byte)'c', 3, 0, 0x10, (byte)'x' };
        C("lz4 block with overlapping match", Encoding.ASCII.GetString(Lz4.DecodeBlock(blk, 0, blk.Length, 10)) == "abcabcabcx");
        // literal run of 20 (extended length) then a 20-byte match at offset 20, final literal
        var lit = Enumerable.Range(0, 20).Select(i => (byte)('A' + i)).ToArray();
        var blk2 = new List<byte> { 0xFF, 5 }; blk2.AddRange(lit); blk2.AddRange(new byte[] { 20, 0, 1 }); blk2.AddRange(new byte[] { 0x10, (byte)'!' });
        var out2 = Lz4.DecodeBlock(blk2.ToArray(), 0, blk2.Count, 41);
        C("lz4 block with extended literal and match lengths", out2.Take(20).SequenceEqual(lit) && out2.Skip(20).Take(20).SequenceEqual(lit) && out2[40] == (byte)'!');
        bool threw = false;
        try { Lz4.DecodeBlock(new byte[] { 0x00, 9, 0 }, 0, 3, 4); } catch (InvalidDataException) { threw = true; }
        C("lz4 rejects a bad match offset", threw);
        // document ["abcabcabcx"] stored as a literal-only LZ4 block
        var doc = new List<byte> { 0x91, 0xaa }; doc.AddRange(Encoding.ASCII.GetBytes("abcabcabcx"));
        var lz = new List<byte> { (byte)(doc.Count << 4) }; lz.AddRange(doc); // 12 literals, no match
        var ext99 = new List<byte> { 0xc7, (byte)(5 + lz.Count), 99, 0xd2, 0, 0, 0, (byte)doc.Count }; ext99.AddRange(lz);
        C("ext 99 (Lz4Block) document", NestedMsgPack.Read(ext99.ToArray(), out var e1) is List<object> l1 && e1 == NestedEncoding.Lz4Block && (string)l1[0] == "abcabcabcx");
        var ext98 = new List<byte> { 0x92, 0xd4, 98, (byte)doc.Count, 0xc4, (byte)lz.Count }; ext98.AddRange(lz);
        C("ext 98 (Lz4BlockArray) document", NestedMsgPack.Read(ext98.ToArray(), out var e2) is List<object> l2 && e2 == NestedEncoding.Lz4BlockArray && (string)l2[0] == "abcabcabcx");
        C("plain document passes through", NestedMsgPack.Read(doc.ToArray(), out var e3) is List<object> && e3 == NestedEncoding.Plain);
        C("msgpack ext value is MsgPackExt", MsgPack.Read(new byte[] { 0xd4, 5, 7 }) is MsgPackExt x1 && x1.Type == 5 && x1.Data.Length == 1);
        C("accessory property parse (outfit., slot 63)", Card.ResolveInfo.TryParseAccessorySlot("outfit.accessory63.ChaFileAccessory.PartsInfo.id", out int s1, out bool p1) && s1 == 63 && p1);
        C("accessory property parse (coordinate, slot 0)", Card.ResolveInfo.TryParseAccessorySlot("accessory0.ChaFileAccessory.PartsInfo.id", out int s2, out bool p2) && s2 == 0 && !p2);
        C("non-accessory properties are rejected", !Card.ResolveInfo.TryParseAccessorySlot("outfit.ChaFileClothes.ClothesTop", out _, out _) &&
            !Card.ResolveInfo.TryParseAccessorySlot("accessory.ChaFileAccessory.PartsInfo.id", out _, out _) && !Card.ResolveInfo.TryParseAccessorySlot("accessory3.ChaFileAccessory.PartsInfo.idx", out _, out _));
        C("AccessoryProperty round-trip", Card.ResolveInfo.AccessoryProperty(26, true) == "outfit.accessory26.ChaFileAccessory.PartsInfo.id" && Card.ResolveInfo.AccessoryProperty(26, false) == "accessory26.ChaFileAccessory.PartsInfo.id");
        sb.AppendLine();
        sb.AppendLine($"# self checks: {list.Count(l => l.StartsWith("ok"))} of {list.Count} ok; spot checks: {s_spot.Count(l => l.StartsWith("ok"))} of {s_spot.Count} ok");
        foreach (var l in list) sb.AppendLine(l);
        foreach (var l in s_spot) sb.AppendLine(l);
        s_spot.Clear();
    }

    // Exact component comparison (Unity's Vector3 == is approximate and hides tiny deviations).
    static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    static string Trunc(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static void Report(StringBuilder sb, List<Group> gs, double totalMs)
    {
        Group G(string name) => gs.FirstOrDefault(g => g.Name == name) ?? new Group { Name = name };
        var F = G("female"); var M = G("male"); var C = G("coord");
        if (gs.Any(g => g.Name == "cards")) F = G("cards");

        // ---- spec comparison
        var rows = new List<(string what, string spec, string got)>();
        void R(string what, object spec, object got) => rows.Add((what, Convert.ToString(spec, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(got, System.Globalization.CultureInfo.InvariantCulture)));
        R("files female / male / coord", "886 / 98 / 1125", $"{F.Files} / {M.Files} / {C.Files}");
        R("parsed female / male / coord", "849 / 96 / 1123", $"{F.Parsed} / {M.Parsed} / {C.Parsed}");
        R("coordinates with KKEx", 1051, C.WithKKEx);
        R("resolve entries (female+male)", 5095, F.N["resolve.entries"] + M.N["resolve.entries"]);
        R("resolve entries with manifest fields (all 8 keys)", 2406, F.N["resolve.withManifestFields"] + M.N["resolve.withManifestFields"]);
        R("resolve LocalSlot < 100000000", 0, F.N["resolve.localSlotBelow1e8"] + M.N["resolve.localSlotBelow1e8"] + C.N["resolve.localSlotBelow1e8"]);
        R("moreAccessories present female / male / coord", "623 / 39 / 1016", $"{F.N["ma.present"]} / {M.N["ma.present"]} / {C.N["ma.present"]}");
        R("moreAccessories with used extra slots female / male / coord", "3 / 0 / 6", $"{F.N["ma.cardsWithUsed"]} / {M.N["ma.cardsWithUsed"]} / {C.N["ma.cardsWithUsed"]}");
        R("used extra slots female / coord", "57 / 95", $"{F.N["ma.usedSlots"]} / {C.N["ma.usedSlots"]}");
        R("max used extra slots on one file female / coord", "43 / 43", $"{F.MaMaxUsed} / {C.MaMaxUsed}");
        R("highest used slot", 63, Math.Max(F.MaxSlot, Math.Max(M.MaxSlot, C.MaxSlot)));
        R("used extra slots with a resolve entry female / coord", "8 / 58", $"{F.N["ma.usedWithResolve"]} / {C.N["ma.usedWithResolve"]}");
        R("extra-slot resolve CategoryNo != type", 0, F.N["ma.resolveCategoryMismatch"] + C.N["ma.resolveCategoryMismatch"]);
        R("moreAccessories xml version 1.2.2 female / coord", "605 / 912", $"{F.MaXmlVersion["1.2.2"]} / {C.MaXmlVersion["1.2.2"]}");
        R("ABMX cards (female + male)", "474 (440 + 34)", $"{F.N["abmx.cards"] + M.N["abmx.cards"]} ({F.N["abmx.cards"]} + {M.N["abmx.cards"]})");
        var per = F.AbmxPerCard.Concat(M.AbmxPerCard).OrderBy(v => v).ToList();
        R("ABMX modifiers", 8987, per.Sum());
        R("ABMX per card min / median / max", "1 / 21 / 99", per.Count == 0 ? "-" : $"{per[0]} / {Median(per)} / {per[per.Count - 1]}");
        var bones = new HashSet<string>(F.AbmxBones.D.Keys.Concat(M.AbmxBones.D.Keys), StringComparer.Ordinal);
        R("ABMX distinct bones", 200, bones.Count);
        R("ABMX location absent / BodyTop / Accessory", "5607 / 3378 / 2", $"{F.N["abmx.locAbsent"] + M.N["abmx.locAbsent"]} / {F.N["abmx.locBodyTop"] + M.N["abmx.locBodyTop"]} / {F.N["abmx.locAccessory"] + M.N["abmx.locAccessory"]}");
        R("ABMX 2-field modifier data", 278, F.N["abmx.data2Fields"] + M.N["abmx.data2Fields"]);
        R("ABMX non-default scale / length / rotation / position", "8170 / 606 / 479 / 253",
            $"{F.N["abmx.nonDefScale"] + M.N["abmx.nonDefScale"]} / {F.N["abmx.nonDefLength"] + M.N["abmx.nonDefLength"]} / {F.N["abmx.nonDefRotation"] + M.N["abmx.nonDefRotation"]} / {F.N["abmx.nonDefPosition"] + M.N["abmx.nonDefPosition"]}");
        R("ABMX blobs LZ4 (ext 99) / plain", "474 / 0", $"{F.Nested[PluginIds.Abmx + ".Lz4Block"] + M.Nested[PluginIds.Abmx + ".Lz4Block"]} / {F.Nested[PluginIds.Abmx + ".Plain"] + M.Nested[PluginIds.Abmx + ".Plain"]}");
        R("ABMX nil entries on coordinates", 4, C.Nil[PluginIds.Abmx]);
        R("uncensor entries female / male", "519 / 4", $"{F.N["unc.present"]} / {M.N["unc.present"]}");
        R("uncensor BodyGUID set (female)", 286, F.N["unc.body"]);
        R("uncensor distinct body GUIDs", 31, new HashSet<string>(F.BodyGuids.D.Keys.Concat(M.BodyGuids.D.Keys)).Count);
        R("uncensor Penis/Balls GUID set", 0, F.N["unc.penis"] + F.N["unc.balls"] + M.N["unc.penis"] + M.N["unc.balls"]);
        R("uncensor DisplayBalls true (female)", 2, F.N["unc.displayBalls"]);
        R("ME present female / male / coord", "375 / 6 / 599", $"{F.N["me.present"]} / {M.N["me.present"]} / {C.N["me.present"]}");
        R("ME real edits female / male / coord", "158 / 6 / 304", $"{F.N["me.withEdits"]} / {M.N["me.withEdits"]} / {C.N["me.withEdits"]}");
        R("ME files with Character/Hair/Clothing/Accessory edits (female)", "121 / 21 / 29 / 31", $"{F.N["me.filesWithCharacter"]} / {F.N["me.filesWithHair"]} / {F.N["me.filesWithClothing"]} / {F.N["me.filesWithAccessory"]}");
        R("ME files with textures female / male / coord", "69 / 1 / 146", $"{F.N["me.withTextures"]} / {M.N["me.withTextures"]} / {C.N["me.withTextures"]}");
        R("ME textures female / male / coord (total)", "184 / 1 / 348 (533)", $"{F.N["me.textures"]} / {M.N["me.textures"]} / {C.N["me.textures"]} ({F.N["me.textures"] + M.N["me.textures"] + C.N["me.textures"]})");
        R("ME texture items without TexID", 9, F.N["me.texItemsWithoutTexID"] + M.N["me.texItemsWithoutTexID"] + C.N["me.texItemsWithoutTexID"]);
        R("ME accessory items on slots >= 20 (coord)", 39, C.N["me.extraSlotItems"]);
        R("ME LZ4 nested documents", 0, Sum(gs, g => g.Nested.D.Where(kv => kv.Key.StartsWith(PluginIds.MaterialEditor) && !kv.Key.EndsWith(".Plain")).Sum(kv => kv.Value)));
        R("KSOX non-nil / nil (female)", "182 / 100", $"{F.N["ksox.present"]} / {F.Nil[PluginIds.Ksox]}");
        R("KSOX v1 / v2 (female)", "173 / 9", $"{F.Versions[PluginIds.Ksox + " v1"]} / {F.Versions[PluginIds.Ksox + " v2"]}");
        R("KSOX v1 empty (female)", 150, F.N["ksox.v1.empty"]);
        R("KSOX cards with skin textures / textures", "32 / 35", $"{F.N["ksox.withTextures"] + M.N["ksox.withTextures"]} / {F.N["ksox.textures"] + M.N["ksox.textures"]}");
        R("KSOX BodyOver / FaceOver / BodyUnder", "21 / 13 / 1", $"{F.KsoxTypes["BodyOver"]} / {F.KsoxTypes["FaceOver"]} / {F.KsoxTypes["BodyUnder"]}");
        R("KCOX non-nil / nil female", "177 / 101", $"{F.N["kcox.present"]} / {F.Nil[PluginIds.Kcox]}");
        R("KCOX female cards with entries / with texture", "9 / 8", $"{F.N["kcox.withEntries"]} / {F.N["kcox.withTexture"]}");
        R("KCOX female entries / textures / override", "13 / 11 / 11", $"{F.N["kcox.entries"]} / {F.N["kcox.textures"]} / {F.N["kcox.override"]}");
        R("KCOX coord non-nil (v1 + v2) / nil", "48 (27 + 21) / 278", $"{C.N["kcox.present"]} ({C.Versions[PluginIds.Kcox + " v1"]} + {C.Versions[PluginIds.Kcox + " v2"]}) / {C.Nil[PluginIds.Kcox]}");
        R("KCOX coord with entries / with texture", "27 / 18", $"{C.N["kcox.withEntries"]} / {C.N["kcox.withTexture"]}");
        R("KCOX coord entries / textures / override", "29 / 19 / 18", $"{C.N["kcox.entries"]} / {C.N["kcox.textures"]} / {C.N["kcox.override"]}");
        R("pushUp female / male / coord", "132 / 2 / 219", $"{F.N["push.present"]} / {M.N["push.present"]} / {C.N["push.present"]}");
        R("pushUp non-default female / male / coord", "61 / 2 / 94", $"{F.N["push.nonDefault"]} / {M.N["push.nonDefault"]} / {C.N["push.nonDefault"]}");
        R("pushUp nil coord", 2, C.Nil[PluginIds.PushUp]);
        R("DBE present female / male / coord", "153 / 4 / 193", $"{F.N["dbe.present"]} / {M.N["dbe.present"]} / {C.N["dbe.present"]}");
        R("DBE files with entries (entries) female / coord", "1 (6) / 1 (5)", $"{F.N["dbe.withEntries"]} ({F.N["dbe.entries"]}) / {C.N["dbe.withEntries"]} ({C.N["dbe.entries"]})");
        R("InvisibleBody present female / male", "585 / 39", $"{F.N["ib.present"]} / {M.N["ib.present"]}");
        R("InvisibleBody hidden female / male", "5 / 2", $"{F.N["ib.hidden"]} / {M.N["ib.hidden"]}");
        R("AAC present female / male / coord", "483 / 39 / 813", $"{F.N["aac.present"]} / {M.N["aac.present"]} / {C.N["aac.present"]}");
        R("AAC female slots / non-empty", "9714 / 641", $"{F.N["aac.slots"]} / {F.N["aac.nonEmptySlots"]}");
        R("AAC coord slots", 16356, C.N["aac.slots"]);
        R("AAC files with rules female / male / coord", "0 / 1 / 2", $"{F.N["aac.cardsWithRules"]} / {M.N["aac.cardsWithRules"]} / {C.N["aac.cardsWithRules"]}");
        R("AAC AdvancedParent (coord)", 1, C.N["aac.advancedParent"]);
        R("AAC coord without coordinateOverrideData", 53, C.N["aac.noOverrideData"]);
        R("AAC override/suppress entries", 0, F.N["aac.overrideOrSuppress"] + M.N["aac.overrideOrSuppress"] + C.N["aac.overrideOrSuppress"]);
        R("eyecontrol / advik / outfitpainter (female, opaque)", "509 / 484 / 317", $"{F.Present["com.deathweasel.bepinex.eyecontrol"]} / {F.Present["orange.spork.advikplugin"]} / {F.Present["orange.spork.outfitpainter"]}");
        R("PregnancyPlus non-nil (nil) female", "395 (129)", $"{F.Present["KK_PregnancyPlus"]} ({F.Nil["KK_PregnancyPlus"]})");

        int match = rows.Count(r => r.spec == r.got);
        double read = gs.Sum(g => Ms(g.ReadTicks)), load = gs.Sum(g => Ms(g.LoadTicks)), dec = gs.Sum(g => Ms(g.DecodeTicks));
        long bytes = gs.Sum(g => g.Bytes);
        sb.AppendLine($"card ext test: {match} of {rows.Count} spec counts match; files {gs.Sum(g => g.Files)}, parsed {gs.Sum(g => g.Parsed)}");
        sb.AppendLine($"time: total {totalMs:F0} ms (incl. test bookkeeping); file read {read:F0} ms for {bytes / 1048576.0:F0} MB; container + MessagePack parse {load:F0} ms; plugin decode {dec:F0} ms (no PNG decoding)");
        sb.AppendLine();
        sb.AppendLine("# spec vs measured");
        foreach (var r in rows) sb.AppendLine($"{(r.spec == r.got ? "ok  " : "DIFF")} {r.what}: spec {r.spec} | measured {r.got}");
        sb.AppendLine("notes:");
        sb.AppendLine("  - uncensor distinct body GUIDs: an independent Python scan of the same cards also finds 31 distinct BodyGUIDs");
        sb.AppendLine("    (all on female cards); the spec's 41 appears to be wrong. Its other uncensor counts (286 set, top GUIDs) match.");
        sb.AppendLine("  - the 37 + 2 character files the spec calls non-HS2 are coordinate cards saved in the chara folders (marker AIS_Clothes)");
        sb.AppendLine("    and Maker autosaves (_autosave, card data without a PNG thumbnail). Card.Load reads the autosaves (as HS2 does); this test");
        sb.AppendLine("    parses them but leaves them out of the counts so the population equals the spec's.");

        foreach (var g in gs)
        {
            sb.AppendLine();
            sb.AppendLine($"# group {g.Name}: files {g.Files}, parsed {g.Parsed} (+ {g.NoPngParsed} PNG-less autosaves parsed, not counted), with KKEx {g.WithKKEx}, {g.Bytes / 1048576.0:F0} MB, read {Ms(g.ReadTicks):F0} ms, parse {Ms(g.LoadTicks):F0} ms, decode {Ms(g.DecodeTicks):F0} ms");
            if (g.Fail.D.Count > 0) sb.AppendLine("parse failures: " + g.Fail.Top(10));
            sb.AppendLine("ids (non-nil): " + g.Present.Top(60));
            if (g.Nil.D.Count > 0) sb.AppendLine("ids (nil PluginData): " + g.Nil.Top(30));
            sb.AppendLine("versions: " + g.Versions.Top(80));
            sb.AppendLine("nested documents: " + g.Nested.Top(40));
            sb.AppendLine("counters: " + string.Join(", ", g.N.D.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} {x.Value}")));
            if (g.MaXmlVersion.D.Count > 0) sb.AppendLine("moreAccessories xml versions: " + g.MaXmlVersion.Top());
            if (g.AbmxBones.D.Count > 0) sb.AppendLine($"ABMX bones ({g.AbmxBones.D.Count} distinct): " + g.AbmxBones.Top(16));
            if (g.BodyGuids.D.Count > 0) sb.AppendLine($"uncensor bodies ({g.BodyGuids.D.Count} distinct): " + g.BodyGuids.Top(8));
            if (g.MeObjType.D.Count > 0) sb.AppendLine("ME items by ObjectType: " + g.MeObjType.Top());
            if (g.MeListFiles.D.Count > 0) sb.AppendLine("ME lists (files / items): " + string.Join(", ", g.MeListFiles.D.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value} / {g.MeListItems[x.Key]}")));
            if (g.KsoxTypes.D.Count > 0) sb.AppendLine("KSOX types: " + g.KsoxTypes.Top());
            if (g.KcoxIds.D.Count > 0) sb.AppendLine("KCOX ids: " + g.KcoxIds.Top());
            if (g.PushKeys.D.Count > 0) sb.AppendLine("pushUp version/key count: " + g.PushKeys.Top());
            sb.AppendLine("opaque: " + g.Opaque.Top(40));
            if (g.Diag.D.Count > 0) { sb.AppendLine("diagnostics:"); foreach (var d in g.Diag.D.OrderByDescending(x => x.Value).Take(30)) sb.AppendLine($"  {d.Value,5} {d.Key}"); }
            foreach (var e in g.Examples.Take(20)) sb.AppendLine("  example: " + e);
        }

        sb.AppendLine();
        sb.AppendLine("# decode time per plugin id (ms, all groups)");
        foreach (var kv in s_profile.OrderByDescending(x => x.Value).Take(25)) sb.AppendLine($"  {Ms(kv.Value),8:F1} {kv.Key}");
    }

    static Dictionary<string, long> s_profile = new Dictionary<string, long>();
    static int Sum(List<Group> gs, Func<Group, int> f) => gs.Sum(f);
    static string Median(List<int> sorted)
    {
        int n = sorted.Count;
        if (n % 2 == 1) return sorted[n / 2].ToString();
        int a = sorted[n / 2 - 1], b = sorted[n / 2];
        return a == b ? a.ToString() : ((a + b) / 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
