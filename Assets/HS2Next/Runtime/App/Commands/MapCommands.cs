// Map commands of Phase 3.2 (package A): map.state (the loaded map: roots, pose, flags,
// MapComponent features and blocks, sea, RenderSettings, lightmaps, probes, counts, the main light, optionally every light
// and the step timing), map.set (pose and the option / light flags, validated before any is applied, one history entry)
// and map.renderers (every renderer in pre-order with its lightmap data, rendering layers and per-slot materials: the
// bundle's original shader and keywords next to the current shader). Paths and members as the HS2 side builds them (package
// C's StudioBridgeMaps, phase32_C_shapes): root-relative names joined by '/', "[k]" on every one of several same-named
// siblings.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HS2Next.App.Commands
{
    public static class MapCommands
    {
        /// <summary>The commands of this file.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "map.state", "map.set", "map.renderers" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("map.state", "The loaded map: roots, pose, flags, MapComponent features and blocks, sea, RenderSettings, lightmaps, probes, counts, the main light; optionally every map light and the post-load step timing.", "maps")
                .Arg("lights", ArgTypes.Bool, "every Light under the roots (pre-order, inactive included)", defaultValue: false)
                .Arg("blocks", ArgTypes.Bool, "the MapComponent's option / light block objects", defaultValue: true)
                .Arg("timing", ArgTypes.Bool, "bundle / load / per-step milliseconds", defaultValue: false)
                .Return("no", ArgTypes.Int, "local id (−1 none)").Return(CommandKit.RefArg, ArgTypes.ContentId, "\"guid#id\" or null").Return("guid", ArgTypes.String, "")
                .Return("name", ArgTypes.String, "").Return("state", ArgTypes.String, "MapState").Return("loading", ArgTypes.Bool, "")
                .Return("scenePath", ArgTypes.String, "").Return("roots", ArgTypes.ArrayOf(ArgTypes.Object), "{name, path, pos, rot (localEulerAngles), active (activeSelf), children}")
                .Return("pose", ArgTypes.Object, "{pos, rot} of scene.map").Return("flags", ArgTypes.Object, "{option, light} of scene.map")
                .Return("features", ArgTypes.Object, "{option, light, componentPath, componentRoot}")
                .Return("blocks", ArgTypes.Object, "{options: [{on: [{path, active}], off}], lights}", false)
                .Return("sea", ArgTypes.Object, "{renderers, keywordOff}").Return("renderSettings", ArgTypes.Object, "the active scene's RenderSettings")
                .Return("lightmaps", ArgTypes.Object, "{count, mode, textures: [{index, color, dir, mask}]}").Return("probes", ArgTypes.Object, "{count, tetrahedralized}")
                .Return("counts", ArgTypes.Object, "{renderers, active, lightmapped, staticBatched, lights, lightsActive, shadowed, reflectionProbes, terrains, cameras, audioSources, audioPlaying, mirrors, scripts}")
                .Return("mainLight", ArgTypes.Object, "{name, path, reason}").Return("mapScripts", ArgTypes.Object, "{lightCurves, uvScrolls, fountainUVs, time, running}")
                .Return("audio", ArgTypes.Object, "{sources, wasPlaying, disabled, unavailable, link}")
                .Return("lights", ArgTypes.ArrayOf(ArgTypes.Object), "with lights: {path, type, enabled, active, color, intensity, original {color, intensity}, range, spot, shadows, shadowStrength, cull, renderingLayers, cookie, bakeType, isBaked, renderMode}", false)
                .Return("timing", ArgTypes.Object, "with timing: {bundleMs, loadMs, steps: {name: ms}}", false)
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "map.* findings of the current map's load")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(120), State);

            CommandKit.Register(r, app, CommandKit.Spec("map.set", "Sets the map pose and the option / light flags (validated before any is applied; one undo entry).", "maps")
                .Arg("pos", ArgTypes.Vec3, "scene.map.pos").Arg("rot", ArgTypes.Euler, "scene.map.rot (stored as given)")
                .Arg("option", ArgTypes.Bool, "scene.map.options (the MapComponent's option blocks)").Arg("light", ArgTypes.Bool, "scene.map.lights (the light blocks)")
                .Arg("label", ArgTypes.String, "history label (default: Map pose / Map options / Map lights)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "of pos, rot, option, light").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "the names that already had the value")
                .Example("{\"cmd\":\"map.set\",\"pos\":[1,2,3],\"rot\":[0,90,0],\"light\":false}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Set);

            CommandKit.Register(r, app, CommandKit.Spec("map.renderers", "The loaded map's renderers in pre-order with lightmap data, rendering layers and per-slot materials (original and current shader, keywords as loaded, queue).", "maps")
                .Arg("filter", ArgTypes.String, "path substring (ordinal)", defaultValue: "")
                .Arg("offset", ArgTypes.Int, "skip this many matches", defaultValue: 0)
                .Arg("limit", ArgTypes.Int, "at most this many", defaultValue: 2000)
                .Arg("materials", ArgTypes.Bool, "per-slot materials", defaultValue: true)
                .Return("count", ArgTypes.Int, "returned").Return("total", ArgTypes.Int, "matching the filter")
                .Return("renderers", ArgTypes.ArrayOf(ArgTypes.Object), "{path, type, layer, enabled, active, lightmapIndex, lightmapScaleOffset, staticBatched, renderingLayerMask, materials: [{name, shaderOriginal, shader, keywords, queue} | null]}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(120), Renderers);
        }

        static MapRealization Part(CoreContext c) => c.Runtime.GetPart<MapRealization>();

        static double[] D3(Vector3 v) => CommandKit.D3(v);
        static double[] V4(Vector4 v) => new double[] { v.x, v.y, v.z, v.w };

        // ------------------------------------------------------------ map.state

        static object State(CoreContext c)
        {
            c.Runtime.ApplyNow();
            bool withLights = c.Args.Get("lights", false), withBlocks = c.Args.Get("blocks", true), withTiming = c.Args.Get("timing", false);
            var part = Part(c);
            var ms = c.Session.Model.Settings.Map;
            var r = new BridgeReply();
            ContentId.TryParse(ms.Ref, out var id);
            string name = "";
            if (!id.IsEmpty && c.Db != null && c.Db.TryGetMap(id, out var row) && row != null) name = row.Name ?? "";
            r["no"] = id.IsEmpty ? -1 : id.Id;
            r[CommandKit.RefArg] = id.IsEmpty ? null : id.ToString();
            r["guid"] = id.IsEmpty ? null : id.SourceGuid;
            r["name"] = name;
            var state = part?.State ?? MapState.None;
            r["state"] = state.ToString();
            r["loading"] = part != null && (state == MapState.Loading || part.IsBusy);
            var s = state == MapState.Loaded ? part.Structure : null;
            r["scenePath"] = s != null ? part.ScenePath : null;

            var roots = new List<object>();
            if (s != null)
            {
                var rootPaths = new Dictionary<Transform, string>();
                MapStructure.Walk(s.Roots, (t, p) => { if (!t.parent) rootPaths[t] = p; });
                foreach (var g in s.Roots)
                {
                    if (!g) continue;
                    var t = g.transform;
                    roots.Add(new BridgeReply().With("name", g.name).With("path", rootPaths.TryGetValue(t, out var p) ? p : g.name)
                        .With("pos", D3(t.localPosition)).With("rot", D3(t.localEulerAngles)).With("active", g.activeSelf).With("children", t.childCount));
                }
            }
            r["roots"] = roots;
            r["pose"] = new BridgeReply().With("pos", CommandKit.D3(ms.Pos)).With("rot", CommandKit.D3(ms.Rot));
            r["flags"] = new BridgeReply().With("option", ms.Options).With("light", ms.Lights);

            var comp = s?.Component;
            r["features"] = new BridgeReply().With("option", s != null && part.Behaviour.FeatureOption).With("light", s != null && part.Behaviour.FeatureLight)
                .With("componentPath", comp ? s.PathOf(comp.transform) : null).With("componentRoot", comp ? (object)s.ComponentRoot : null);
            if (withBlocks)
                r["blocks"] = new BridgeReply().With("options", Blocks(s, comp ? comp.optionInfos : null)).With("lights", Blocks(s, comp ? comp.lightInfos : null));

            int seaOff = 0;
            if (s != null)
                foreach (var sr in s.SeaRenderers)
                {
                    bool all = true;
                    foreach (var m in sr.sharedMaterials) if (m && MapBehaviour.HasKeyword(m, MapBehaviour.SeaKeyword)) all = false;
                    if (all && part.Behaviour.SeaDone) seaOff++;
                }
            r["sea"] = new BridgeReply().With("renderers", s?.SeaRenderers.Count ?? 0).With("keywordOff", seaOff);
            r["renderSettings"] = RenderSettingsReply(part);
            r["lightmaps"] = LightmapsReply();
            var lp = LightmapSettings.lightProbes;
            r["probes"] = new BridgeReply().With("count", lp != null ? lp.count : 0).With("tetrahedralized", part?.ProbesTetrahedralized ?? true)
                .With("outsideHull", part != null && part.ProbeFallback.Active ? part.ProbeFallback.Overridden : 0);
            r["counts"] = Counts(s);

            var main = MainLight.Current;
            r["mainLight"] = new BridgeReply().With("name", main ? main.name : null).With("path", main ? (s != null ? s.PathOf(main.transform) : PathOf(main.transform)) : null)
                .With("reason", MainLight.Reason);
            var sc = part?.Scripts;
            r["mapScripts"] = new BridgeReply().With("lightCurves", sc?.LightCurves ?? 0).With("uvScrolls", sc?.UVScrolls ?? 0).With("fountainUVs", sc?.FountainUVs ?? 0)
                .With("time", (double)(sc?.Time ?? 0f)).With("running", sc?.Running ?? false);
            var au = part?.Audio ?? default;
            r["audio"] = new BridgeReply().With("sources", s != null ? au.Sources : 0).With("wasPlaying", s != null ? au.WerePlaying : 0)
                .With("disabled", s != null ? au.Disabled : 0).With("unavailable", s != null ? au.ClipsUnavailable : 0).With("link", MapAudio.LinkState);

            if (withLights)
            {
                var ls = new List<object>();
                if (s != null)
                    s.Walk((t, p) =>
                    {
                        var l = t.GetComponent<Light>();
                        if (l) ls.Add(LightReply(l, p));
                    });
                r["lights"] = ls;
            }
            if (withTiming)
            {
                var steps = new BridgeReply();
                if (part != null) foreach (var kv in part.StepMs) steps[kv.Key] = kv.Value;
                r["timing"] = new BridgeReply().With("bundleMs", part?.BundleMs ?? 0).With("loadMs", part?.LoadMs ?? 0).With("steps", steps);
            }
            r["findings"] = CommandKit.Findings(part != null ? (IEnumerable<SceneFinding>)part.MapFindingsOfLoad : new List<SceneFinding>());
            return r;
        }

        static List<object> Blocks(MapStructure s, List<Studio.MapComponent.OptionInfo_S> infos)
        {
            var l = new List<object>();
            if (s == null || infos == null) return l;
            List<object> Objs(List<GameObject> gs)
            {
                var o = new List<object>();
                if (gs == null) return o;
                foreach (var g in gs)
                    o.Add(g ? new BridgeReply().With("path", s.PathOf(g.transform)).With("active", g.activeSelf) : new BridgeReply().With("path", null).With("active", null));
                return o;
            }
            foreach (var i in infos)
                l.Add(new BridgeReply().With("on", Objs(i?.objectsOn)).With("off", Objs(i?.objectsOff)));
            return l;
        }

        static BridgeReply RenderSettingsReply(MapRealization part)
        {
            var probe = RenderSettings.ambientProbe;
            var sh = new double[27];
            for (int ch = 0; ch < 3; ch++)
                for (int k = 0; k < 9; k++) sh[ch * 9 + k] = probe[ch, k];
            var sk = RenderSettings.skybox;
            var cr = RenderSettings.customReflectionTexture;
            var mats = part?.Materials;
            string skOrig = mats != null && sk ? mats.OriginOf(sk)?.ShaderOriginal : null;
            return new BridgeReply()
                .With("ambientMode", RenderSettings.ambientMode.ToString()).With("ambientIntensity", (double)RenderSettings.ambientIntensity)
                .With("ambientSky", CommandKit.C(RenderSettings.ambientSkyColor)).With("ambientEquator", CommandKit.C(RenderSettings.ambientEquatorColor))
                .With("ambientGround", CommandKit.C(RenderSettings.ambientGroundColor)).With("ambientProbe", sh)
                .With("fog", RenderSettings.fog).With("fogMode", RenderSettings.fogMode.ToString()).With("fogColor", CommandKit.C(RenderSettings.fogColor))
                .With("fogDensity", (double)RenderSettings.fogDensity).With("fogStart", (double)RenderSettings.fogStartDistance).With("fogEnd", (double)RenderSettings.fogEndDistance)
                .With("reflectionMode", RenderSettings.defaultReflectionMode.ToString()).With("reflectionIntensity", (double)RenderSettings.reflectionIntensity)
                .With("customReflection", cr ? cr.name : null)
                .With("skybox", sk ? new BridgeReply().With("material", sk.name).With("shader", sk.shader ? sk.shader.name : null).With("shaderOriginal", skOrig ?? (sk.shader ? sk.shader.name : null)) : null)
                .With("haloStrength", (double)RenderSettings.haloStrength).With("flareStrength", (double)RenderSettings.flareStrength)
                .With("sun", RenderSettings.sun ? RenderSettings.sun.name : null);
        }

        static BridgeReply Tex(Texture t) => t ? new BridgeReply().With("name", t.name).With("w", t.width).With("h", t.height) : null;

        static BridgeReply LightmapsReply()
        {
            var lms = LightmapSettings.lightmaps;
            var tex = new List<object>();
            for (int i = 0; i < lms.Length; i++)
            {
                var d = lms[i];
                tex.Add(new BridgeReply().With("index", i).With("color", Tex(d?.lightmapColor)).With("dir", Tex(d?.lightmapDir)).With("mask", Tex(d?.shadowMask)));
            }
            // the canonical name: Unity 6's LightmapsMode.ToString() prints CombinedDirectional as "Single, Dual"
            string mode = LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional ? "CombinedDirectional" : "NonDirectional";
            return new BridgeReply().With("count", lms.Length).With("mode", mode).With("textures", tex);
        }

        static BridgeReply Counts(MapStructure s)
        {
            int renderers = 0, active = 0, lightmapped = 0, batched = 0, lights = 0, lightsActive = 0, shadowed = 0, playing = 0;
            var scripts = new BridgeReply();
            if (s != null)
            {
                foreach (var r in s.Renderers)
                {
                    if (!r) continue;
                    renderers++;
                    if (r.enabled && r.gameObject.activeInHierarchy) active++;
                    if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) lightmapped++;
                    if (r.isPartOfStaticBatch) batched++;
                }
                foreach (var l in s.Lights)
                {
                    if (!l) continue;
                    lights++;
                    if (l.enabled && l.gameObject.activeInHierarchy) { lightsActive++; if (l.shadows != LightShadows.None) shadowed++; }
                }
                foreach (var a in s.AudioSources) if (a && a.isPlaying) playing++;
                foreach (var kv in s.Scripts) scripts[kv.Key] = kv.Value;
            }
            return new BridgeReply().With("renderers", renderers).With("active", active).With("lightmapped", lightmapped).With("staticBatched", batched)
                .With("lights", lights).With("lightsActive", lightsActive).With("shadowed", shadowed)
                .With("reflectionProbes", s?.ReflectionProbes.Count ?? 0).With("terrains", s?.Terrains.Count ?? 0).With("cameras", s?.Cameras.Count ?? 0)
                .With("audioSources", s?.AudioSources.Count ?? 0).With("audioPlaying", playing).With("mirrors", s?.Mirrors.Count ?? 0).With("scripts", scripts);
        }

        /// <summary>One light as map.state / scene.lights report it (C's shape: original = the bundle's values, HS2's colour / intensity).</summary>
        public static BridgeReply LightReply(Light l, string path)
        {
            var o = BundleLights.OriginalOf(l);
            object layers = l.TryGetComponent<UniversalAdditionalLightData>(out var d) ? (object)unchecked((uint)d.renderingLayers) : null;
            return new BridgeReply().With("path", path).With("type", l.type.ToString()).With("enabled", l.enabled).With("active", l.gameObject.activeInHierarchy)
                .With("color", CommandKit.C(l.color)).With("intensity", (double)l.intensity)
                .With("original", new BridgeReply().With("color", CommandKit.C(o ? o.Color : l.color)).With("intensity", (double)(o ? o.Intensity : l.intensity)))
                .With("range", (double)l.range).With("spot", (double)l.spotAngle).With("shadows", l.shadows.ToString()).With("shadowStrength", (double)l.shadowStrength)
                .With("cull", l.cullingMask).With("renderingLayers", layers).With("cookie", l.cookie ? l.cookie.name : null)
                .With("bakeType", l.bakingOutput.lightmapBakeType.ToString()).With("isBaked", l.bakingOutput.isBaked).With("renderMode", l.renderMode.ToString());
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------ map.set

        static object Set(CoreContext c)
        {
            // every argument is read (and type-checked) before anything is applied
            bool hasPos = c.Args.TryGet<Vector3>("pos", out var pos);
            bool hasRot = c.Args.TryGet<Vector3>("rot", out var rot);
            bool hasOpt = c.Args.Has("option"), hasLight = c.Args.Has("light");
            bool opt = hasOpt && c.Args.Get("option", true), light = hasLight && c.Args.Get("light", true);
            if (!hasPos && !hasRot && !hasOpt && !hasLight) throw new BridgeException(BridgeError.Missing("pos", "one of pos, rot, option, light"));
            if (hasPos && !Finite(pos)) throw CommandKit.BadArgs("pos", ArgTypes.Vec3, "finite numbers");
            if (hasRot && !Finite(rot)) throw CommandKit.BadArgs("rot", ArgTypes.Euler, "finite numbers");
            var r = CommandKit.Check(c, CommandKit.Ops(c).SetMap(hasPos ? pos : (Vector3?)null, hasRot ? rot : (Vector3?)null,
                hasOpt ? opt : (bool?)null, hasLight ? light : (bool?)null, CommandKit.Exec(c, c.Args.Get<string>("label"))));
            return new BridgeReply().With("changed", r.Changed).With("noop", r.NoOp);
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // ------------------------------------------------------------ map.renderers

        static object Renderers(CoreContext c)
        {
            c.Runtime.ApplyNow();
            string filter = c.Args.Get("filter", "") ?? "";
            int offset = c.Args.Get("offset", 0), limit = c.Args.Get("limit", 2000);
            bool withMats = c.Args.Get("materials", true);
            if (offset < 0) throw CommandKit.BadArgs("offset", ">= 0");
            if (limit < 0) throw CommandKit.BadArgs("limit", ">= 0");
            var part = Part(c);
            var s = part != null && part.State == MapState.Loaded ? part.Structure : null;
            var mats = part?.Materials;
            var list = new List<object>();
            int total = 0;
            if (s != null)
                s.Walk((t, p) =>
                {
                    var r = t.GetComponent<Renderer>();
                    if (!r) return;
                    if (filter.Length > 0 && p.IndexOf(filter, StringComparison.Ordinal) < 0) return;
                    total++;
                    if (total <= offset || list.Count >= limit) return;
                    var e = new BridgeReply().With("path", p).With("type", r.GetType().Name).With("layer", r.gameObject.layer).With("enabled", r.enabled)
                        .With("active", r.gameObject.activeInHierarchy).With("lightmapIndex", r.lightmapIndex).With("lightmapScaleOffset", V4(r.lightmapScaleOffset))
                        .With("staticBatched", r.isPartOfStaticBatch).With("renderingLayerMask", r.renderingLayerMask);
                    if (withMats)
                    {
                        var ml = new List<object>();
                        foreach (var m in r.sharedMaterials)
                        {
                            if (!m) { ml.Add(null); continue; }
                            var o = mats?.OriginOf(m);
                            string cur = m.shader ? m.shader.name : null;
                            string[] kw = o != null ? o.Keywords : Sorted(m.shaderKeywords);
                            ml.Add(new BridgeReply().With("name", m.name).With("shaderOriginal", o?.ShaderOriginal ?? cur).With("shader", cur)
                                .With("keywords", new List<string>(kw)).With("queue", m.renderQueue));
                        }
                        e["materials"] = ml;
                    }
                    list.Add(e);
                });
            return new BridgeReply().With("count", list.Count).With("total", total).With("renderers", list);
        }

        static string[] Sorted(string[] k)
        {
            var s = k != null ? (string[])k.Clone() : Array.Empty<string>();
            Array.Sort(s, StringComparer.Ordinal);
            return s;
        }
    }
}
