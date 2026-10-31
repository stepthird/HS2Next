// layout.json (U32): <UserData>/config/layout.json, versioned:
//   { "version": 2,
//     "regions": { "left":   { "size": 360, "collapsed": false, "panels": ["workspace", "inspector"], "active": "workspace" },
//                  "right":  { … }, "bottom": { … } },
//     "hidden": ["history"] }
// Unknown panel ids are kept (a panel of a later build or a plugin keeps its place); registered panels missing from the
// file go to their default region (Normalize); "Reset layout" = Default(). Unknown members (top level and per region)
// are kept for the round trip. Version 2 is the sidebar layout: left = workspace and inspector as tabs, right = the add
// browsers, the scene panel and the history as tabs, bottom empty (collapsed until a panel is moved there). A file of an
// older version (1, or 0 = no "version" member, an early draft shape `{ "panels": { id: region } }`) is read and then
// replaced once by the version 2 default (MigrateToCurrent): its hidden panels and unknown top-level members are kept,
// its regions are not; the shell tells the user once and writes the new file. Package A writes it on change (debounced
// 1 s) and at quit. Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core
{
    public static class LayoutRegions
    {
        public const string Left = "left", Right = "right", Bottom = "bottom";
        public static readonly IReadOnlyList<string> All = new[] { Left, Right, Bottom };
        public static bool IsRegion(string r) => r == Left || r == Right || r == Bottom;

        /// <summary>Default size (logical px) and minimum (the sidebar layout of version 2: left 360, right 380).</summary>
        public static double DefaultSize(string r) => r == Left ? 360 : r == Right ? 380 : 300;
        public static double MinSize(string r) => r == Left ? 300 : r == Right ? 300 : 160;
    }

    /// <summary>A panel known to the shell and its default place.</summary>
    public readonly struct PanelDefault
    {
        public readonly string Id;
        public readonly string Region;

        public PanelDefault(string id, string region)
        {
            Id = id;
            Region = LayoutRegions.IsRegion(region) ? region : LayoutRegions.Bottom;
        }

        public override string ToString() => Id + "@" + Region;
    }

    public sealed class LayoutRegion
    {
        public double Size;
        public bool Collapsed;
        public readonly List<string> Panels = new List<string>();
        public string Active;
        internal JObject Extra = new JObject();

        public LayoutRegion Clone()
        {
            var r = new LayoutRegion { Size = Size, Collapsed = Collapsed, Active = Active, Extra = (JObject)Extra.DeepClone() };
            r.Panels.AddRange(Panels);
            return r;
        }
    }

    public sealed class LayoutDocument
    {
        public const int CurrentVersion = 2;
        public const string FileName = "layout.json";

        /// <summary>The default panel assignment (version 2, the sidebar layout): left = workspace and inspector as tabs,
        /// right = the add browsers, the scene panel and the history as tabs, bottom = none.</summary>
        public static readonly IReadOnlyList<PanelDefault> DefaultPanels = new[]
        {
            new PanelDefault("workspace", LayoutRegions.Left),
            new PanelDefault("inspector", LayoutRegions.Left),
            new PanelDefault("add.chara", LayoutRegions.Right),
            new PanelDefault("add.item", LayoutRegions.Right),
            new PanelDefault("add.anime", LayoutRegions.Right),
            new PanelDefault("add.map", LayoutRegions.Right),
            new PanelDefault("add.light", LayoutRegions.Right),
            new PanelDefault("add.pose", LayoutRegions.Right),
            new PanelDefault("scene", LayoutRegions.Right),
            new PanelDefault("history", LayoutRegions.Right),
        };

        /// <summary>The version read (0 = no version member); always written as <see cref="CurrentVersion"/>.</summary>
        public int Version = CurrentVersion;

        /// <summary>True for a document read from a file older than <see cref="CurrentVersion"/>: the shell replaces it once
        /// by the current default (<see cref="MigrateToCurrent"/>).</summary>
        public bool IsOutdated => Version < CurrentVersion;

        /// <summary>
        /// The current default layout for the registered panels, keeping this (older) document's hidden panels and unknown
        /// top-level members; its regions, sizes and active tabs are dropped (the region assignment of version 2 replaces
        /// them). The result has version <see cref="CurrentVersion"/>.
        /// </summary>
        public LayoutDocument MigrateToCurrent(IEnumerable<PanelDefault> registered = null)
        {
            var d = Default(registered);
            foreach (var h in Hidden) if (!string.IsNullOrEmpty(h) && !d.Hidden.Contains(h)) d.Hidden.Add(h);
            d.extra = (JObject)extra.DeepClone();
            d.Normalize(registered);
            return d;
        }
        public readonly Dictionary<string, LayoutRegion> Regions = new Dictionary<string, LayoutRegion>(StringComparer.Ordinal);
        public readonly List<string> Hidden = new List<string>();
        JObject extra = new JObject();

        public LayoutRegion Region(string name) => Regions.TryGetValue(name, out var r) ? r : null;

        public static LayoutDocument Default(IEnumerable<PanelDefault> panels = null)
        {
            var d = new LayoutDocument();
            foreach (var r in LayoutRegions.All) d.Regions[r] = new LayoutRegion { Size = LayoutRegions.DefaultSize(r) };
            foreach (var p in panels ?? DefaultPanels) d.Regions[p.Region].Panels.Add(p.Id);
            foreach (var r in d.Regions.Values) r.Active = r.Panels.FirstOrDefault();
            return d;
        }

        /// <summary>The region holding a panel (null when the layout does not list it).</summary>
        public string RegionOf(string panelId)
        {
            foreach (var r in LayoutRegions.All)
                if (Regions.TryGetValue(r, out var reg) && reg.Panels.Contains(panelId)) return r;
            return null;
        }

        /// <summary>Moves a panel to the end of a region's tabs (the "Move to …" menu) and makes it active there.</summary>
        public void Move(string panelId, string region)
        {
            if (!LayoutRegions.IsRegion(region)) throw new ArgumentException("not a region: " + region);
            foreach (var r in Regions.Values)
                if (r.Panels.Remove(panelId) && r.Active == panelId) r.Active = r.Panels.FirstOrDefault();
            Regions[region].Panels.Add(panelId);
            Regions[region].Active = panelId;
        }

        /// <summary>Makes the layout valid for the registered panels: every region present with a size ≥ its minimum, every
        /// registered panel listed once (missing ones appended to their default region), duplicates removed (first place
        /// wins), unknown ids kept, active = a listed panel. Returns what was fixed.</summary>
        public List<string> Normalize(IEnumerable<PanelDefault> registered = null)
        {
            var fixes = new List<string>();
            foreach (var r in LayoutRegions.All)
            {
                if (!Regions.TryGetValue(r, out var reg)) { Regions[r] = reg = new LayoutRegion { Size = LayoutRegions.DefaultSize(r) }; fixes.Add("region " + r + " added"); }
                if (double.IsNaN(reg.Size) || reg.Size <= 0) { reg.Size = LayoutRegions.DefaultSize(r); fixes.Add(r + ".size defaulted"); }
                else if (reg.Size < LayoutRegions.MinSize(r)) { reg.Size = LayoutRegions.MinSize(r); fixes.Add(r + ".size raised to the minimum"); }
            }
            foreach (var k in Regions.Keys.Where(k => !LayoutRegions.IsRegion(k)).ToList())
            {
                fixes.Add("region " + k + " is not a region; its panels go to bottom");
                foreach (var p in Regions[k].Panels) Regions[LayoutRegions.Bottom].Panels.Add(p);
                Regions.Remove(k);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in LayoutRegions.All)
            {
                var reg = Regions[r];
                for (int i = 0; i < reg.Panels.Count; i++)
                {
                    string p = reg.Panels[i];
                    if (string.IsNullOrEmpty(p) || !seen.Add(p)) { fixes.Add("duplicate or empty panel id " + p + " removed"); reg.Panels.RemoveAt(i--); }
                }
            }
            foreach (var p in registered ?? DefaultPanels)
                if (!seen.Contains(p.Id)) { Regions[p.Region].Panels.Add(p.Id); seen.Add(p.Id); fixes.Add(p.Id + " added to " + p.Region); }
            foreach (var r in Regions.Values)
                if (r.Active == null || !r.Panels.Contains(r.Active)) r.Active = r.Panels.FirstOrDefault();
            for (int i = 0; i < Hidden.Count; i++)
                if (string.IsNullOrEmpty(Hidden[i]) || Hidden.IndexOf(Hidden[i]) != i) Hidden.RemoveAt(i--);
            return fixes;
        }

        // ---------------- JSON ----------------

        /// <summary>Reads layout.json; unreadable text gives null and a problem ("ui.layoutInvalid": defaults used).</summary>
        public static LayoutDocument Parse(string json, List<string> problems = null)
        {
            JObject o;
            try { o = JObject.Parse(json ?? ""); }
            catch (JsonException e) { problems?.Add("layout.json unreadable: " + e.Message); return null; }
            var d = new LayoutDocument { Version = (int?)Num(o["version"]) ?? 0 };
            if (d.Version > CurrentVersion) problems?.Add("layout.json version " + d.Version + " is newer than " + CurrentVersion + "; read as " + CurrentVersion);
            if (o["regions"] is JObject regions)
            {
                foreach (var p in regions.Properties())
                {
                    if (!(p.Value is JObject ro)) { problems?.Add("region " + p.Name + " is not an object"); continue; }
                    var r = new LayoutRegion
                    {
                        Size = Num(ro["size"]) ?? (LayoutRegions.IsRegion(p.Name) ? LayoutRegions.DefaultSize(p.Name) : 300),
                        Collapsed = ro["collapsed"]?.Type == JTokenType.Boolean && (bool)ro["collapsed"],
                        Active = ro["active"]?.Type == JTokenType.String ? (string)ro["active"] : null,
                    };
                    if (ro["panels"] is JArray pa)
                        foreach (var e in pa) if (e.Type == JTokenType.String) r.Panels.Add((string)e);
                    foreach (var m in ro.Properties())
                        if (m.Name != "size" && m.Name != "collapsed" && m.Name != "active" && m.Name != "panels") r.Extra[m.Name] = m.Value.DeepClone();
                    d.Regions[p.Name] = r;
                }
            }
            else if (d.Version == 0 && o["panels"] is JObject flat)
            {
                // version 0 draft: { "panels": { "<id>": "<region>" }, "sizes": { "<region>": n } }
                foreach (var r in LayoutRegions.All) d.Regions[r] = new LayoutRegion { Size = Num(o["sizes"]?[r]) ?? LayoutRegions.DefaultSize(r) };
                foreach (var p in flat.Properties())
                {
                    string region = p.Value.Type == JTokenType.String && LayoutRegions.IsRegion((string)p.Value) ? (string)p.Value : LayoutRegions.Bottom;
                    d.Regions[region].Panels.Add(p.Name);
                }
                problems?.Add("layout.json version 0 migrated");
            }
            if (o["hidden"] is JArray h)
                foreach (var e in h) if (e.Type == JTokenType.String) d.Hidden.Add((string)e);
            foreach (var m in o.Properties())
                if (m.Name != "version" && m.Name != "regions" && m.Name != "hidden" && !(d.Version == 0 && (m.Name == "panels" || m.Name == "sizes")))
                    d.extra[m.Name] = m.Value.DeepClone();
            return d;
        }

        static double? Num(JToken t) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : (double?)null;

        public string ToJson()
        {
            var regions = new JObject();
            foreach (var r in LayoutRegions.All.Concat(Regions.Keys.Where(k => !LayoutRegions.IsRegion(k)).OrderBy(k => k, StringComparer.Ordinal)))
            {
                if (!Regions.TryGetValue(r, out var reg)) continue;
                var ro = new JObject
                {
                    ["size"] = Math.Round(reg.Size, 1),
                    ["collapsed"] = reg.Collapsed,
                    ["panels"] = new JArray(reg.Panels.Cast<object>().ToArray()),
                    ["active"] = reg.Active,
                };
                foreach (var m in reg.Extra.Properties()) ro[m.Name] = m.Value.DeepClone();
                regions[r] = ro;
            }
            var o = new JObject { ["version"] = CurrentVersion, ["regions"] = regions, ["hidden"] = new JArray(Hidden.Cast<object>().ToArray()) };
            foreach (var m in extra.Properties()) o[m.Name] = m.Value.DeepClone();
            return o.ToString(Formatting.Indented);
        }

        public LayoutDocument Clone()
        {
            var d = new LayoutDocument { Version = Version, extra = (JObject)extra.DeepClone() };
            foreach (var kv in Regions) d.Regions[kv.Key] = kv.Value.Clone();
            d.Hidden.AddRange(Hidden);
            return d;
        }
    }
}
