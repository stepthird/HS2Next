// Item appearance commands of Phase 3.1 (package A): item.state (the item section, what
// the item offers and its default-button values, what the realization holds), item.set (item-section paths in their
// save-format JSON forms on one or several items, validated all-or-nothing, one history entry), item.materials (per
// renderer slot: flags, original and current shader, queue, recorded keywords, the Studio property values the current
// shader declares, texture names) and obj.visible (a node's own visibility, one history entry). The writes of item.set and
// obj.visible are StudioOps.SetItem / SetVisible (Phase 3.5 extraction); the handlers parse, call and reply.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class ItemCommands
    {
        /// <summary>The commands of this file.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "item.state", "item.set", "item.materials", "obj.visible" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("item.state", "An item's stored section, what it offers (HS2's inspector gates) with its default-button values, and what the realization holds.", "items")
                .Arg("key", ArgTypes.Node, "the item node", required: true)
                .Return("key", ArgTypes.String, "node id").Return(CommandKit.RefArg, ArgTypes.ContentId, "the stored item.ref")
                .Return("row", ArgTypes.Object, "{group, category, no, name, guid, fallback} of the loaded list row (null when not built)")
                .Return("state", ArgTypes.Object, "the item section as in scene.doc")
                .Return("features", ArgTypes.Object, "color[3], metallic[3], pattern[3], glass, alpha, emission, emissionColor, emissionPower, lightCancel, panel, particle, fk, dynamicBones, options, animePatterns, animated, icon, sound, lights, check, checkGlass, defaults")
                .Return("realized", ArgTypes.Object, "{state, renderers, slots, ownedMaterials, fallbackShaders, particles, fkBones, fkMissing, dynamicBones, animator, lights, icon, iconActive, sound, audioSuppressed, componentsDisabled, patterns, panel, buildMs}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), State);

            CommandKit.Register(r, app, CommandKit.Spec("item.set", "Sets item-section fields of one or several items (save-format JSON forms), validated all-or-nothing, as one undo entry.", "items")
                .Arg("key", ArgTypes.Node, "the item node")
                .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several item nodes (one entry for all)")
                .Arg("set", ArgTypes.Object, "{<item-section path>: <value>}: \"colors[0].main\": [r,g,b,a], \"alpha\": 0.5, \"emission.power\": 2, \"panel\": {\"hs2\": \"DefaultData/bg/x.png\"}, \"fk.bones[\\\"x\\\"]\": [x,y,z], \"options[0]\": false, ...", required: true)
                .Arg("label", ArgTypes.String, "history label (default by the first path: Item colour / pattern / alpha / emission / panel / FK / dynamic bones / options / animation)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "canonical paths that changed (on any key)")
                .Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "paths that already had the value (on every key)")
                .Return("keys", ArgTypes.Object, "with keys: {<id>: {changed, noop}}", false)
                .Example("{\"cmd\":\"item.set\",\"key\":\"<id>\",\"set\":{\"colors[0].main\":[1,0,0,1],\"alpha\":0.5}}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Set);

            CommandKit.Register(r, app, CommandKit.Spec("item.materials", "The materials of a built item per renderer slot: MaterialInfo flags, original and current shader, queue, recorded keywords, Studio property values, texture names.", "items")
                .Arg("key", ArgTypes.Node, "the item node", required: true)
                .Arg("props", ArgTypes.ArrayOf(ArgTypes.String), "extra property names to report")
                .Arg("slotsOnly", ArgTypes.Bool, "only slots with a MaterialInfo flag", defaultValue: false)
                .Arg("allProps", ArgTypes.Bool, "props holds every property the shader declares, in chara.edit.materials' forms", defaultValue: false)
                .Return("renderers", ArgTypes.ArrayOf(ArgTypes.Object), "{path, type, enabled, hidden, active, slots: [{index, flags (letters 123abcEAG, \"\" unflagged MaterialInfo, null none), material, shaderOriginal, shader, fallback, queue, keywords (recorded originals, sorted), keywordsCurrent, props, textures, textureSize}]}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Materials);

            CommandKit.Register(r, app, CommandKit.Spec("obj.visible", "Sets a node's own visibility (one undo entry \"Visibility\"); descendants follow as effective visibility.", "items")
                .Arg("key", ArgTypes.Node, "the node", required: true)
                .Arg("visible", ArgTypes.Bool, "the node's own flag", required: true)
                .Return("key", ArgTypes.String, "node id").Return("visible", ArgTypes.Bool, "own flag").Return("effective", ArgTypes.Bool, "effective visibility")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Visible);
        }

        // ------------------------------------------------------------ helpers

        static TreeNode Item(CoreContext c, string arg, TreeNode n) => CommandKit.KeyKind.Item.Check(n, arg);

        static double[] V4(Vec4 v) => new double[] { v.X, v.Y, v.Z, v.W };
        static List<object> L(bool[] a) { var l = new List<object>(); foreach (var x in a) l.Add(x); return l; }

        // ------------------------------------------------------------ item.state

        static object State(CoreContext c)
        {
            var n = Item(c, "key", CommandKit.Node(c, "key"));
            c.Runtime.ApplyNow();
            var st = n.State as ItemNodeState;
            var real = c.Runtime.FindItem(n.Id);
            var r = new BridgeReply().With("key", n.Id).With(CommandKit.RefArg, st?.Ref);
            bool built = real != null && real.State == RealizationState.Built && real.Instance;
            if (built && real.Row != null)
                r["row"] = new BridgeReply().With("group", real.Row.Group).With("category", real.Row.Category).With("no", real.Row.No)
                    .With("name", real.Row.Name).With("guid", real.Row.Key.SourceGuid).With("fallback", real.IsFallback);
            else r["row"] = null;
            r["state"] = new RawJson(FieldJson.ToText(st));
            r["features"] = built && real.Features != null ? Features(real.Features) : null;
            r["realized"] = Realized(real);
            return r;
        }

        static BridgeReply Features(ItemFeatures f)
        {
            var anime = new List<object>();
            foreach (var (name, state, valid) in f.AnimePatterns) anime.Add(new BridgeReply().With("name", name).With("state", state).With("valid", valid));
            var d = f.Default;
            List<object> Cols(Color4[] a) { var l = new List<object>(); foreach (var x in a) l.Add(CommandKit.C(x)); return l; }
            List<object> Fs(float[] a) { var l = new List<object>(); foreach (var x in a) l.Add((double)x); return l; }
            var uvs = new List<object>();
            foreach (var u in d.PatternUv) uvs.Add(V4(u));
            var defaults = new BridgeReply().With("color", Cols(d.Color)).With("metallic", Fs(d.Metallic)).With("glossiness", Fs(d.Glossiness))
                .With("patternColor", Cols(d.PatternColor)).With("uv", uvs).With("clamp", L(d.PatternClamp)).With("rot", Fs(d.PatternRot))
                .With("glass", CommandKit.C(d.Glass))
                .With("emission", new BridgeReply().With("color", CommandKit.C(d.Emission.Color)).With("intensity", (double)d.Emission.Intensity).With("power", (double)d.Emission.Power))
                .With("lightCancel", (double)d.LightCancel);
            if (d.Panel != null)
                defaults["panel"] = new BridgeReply().With("color", CommandKit.C(d.Panel.Color)).With("uv", V4(d.Panel.Uv)).With("clamp", d.Panel.Clamp).With("rot", (double)d.Panel.Rot);
            if (d.ParticleColor.HasValue) defaults["particleColor"] = CommandKit.C(d.ParticleColor.Value);
            object sound = null;
            if (f.Sound != null)
                sound = new BridgeReply().With("clip", f.Sound.Clip).With("loop", f.Sound.Loop).With("soundType", f.Sound.SoundType).With("rolloff", f.Sound.Rolloff)
                    .With("min", (double)f.Sound.Min).With("max", (double)f.Sound.Max).With("volume", (double)f.Sound.Volume);
            return new BridgeReply().With("color", L(f.Color)).With("metallic", L(f.Metallic)).With("pattern", L(f.Pattern))
                .With("glass", f.Glass).With("alpha", f.Alpha).With("emission", f.Emission).With("emissionColor", f.EmissionColor).With("emissionPower", f.EmissionPower)
                .With("lightCancel", f.LightCancel).With("panel", f.Panel).With("particle", f.Particle).With("fk", f.Fk).With("dynamicBones", f.DynamicBones > 0)
                .With("options", f.Options).With("animePatterns", anime).With("animePatternsShown", f.AnimePatternsShown).With("animated", f.Animated)
                .With("icon", f.Icon).With("sound", sound).With("lights", f.Lights).With("check", f.Check).With("checkGlass", f.CheckGlass)
                .With("defaults", defaults);
        }

        static BridgeReply Realized(ItemRealization real)
        {
            var r = new BridgeReply();
            r["state"] = real != null ? real.State.ToString() : "NotRealized";
            if (real == null || real.State != RealizationState.Built || !real.Instance) return r;
            int slots = 0;
            foreach (var e in real.Writer.Renderers) slots += e.Slots.Length;
            var fb = new BridgeReply();
            foreach (var kv in real.Writer.FallbackShaders) fb[kv.Key] = kv.Value;
            r["renderers"] = real.Writer.Renderers.Count;
            r["slots"] = slots;
            r["ownedMaterials"] = real.Writer.OwnedMaterials;
            r["fallbackShaders"] = fb;
            r["particles"] = real.Particles.Count;
            r["fkBones"] = real.Features != null ? new List<string>(real.Features.FkBones) : new List<string>();
            r["fkMissing"] = real.Features != null ? new List<string>(real.Features.FkMissing) : new List<string>();
            r["dynamicBones"] = real.DynamicBones.Count;
            var a = real.Animator;
            var an = new BridgeReply().With("present", (bool)a).With("enabled", a && a.enabled).With("driven", a && a.enabled && a.runtimeAnimatorController != null);
            an["state"] = null;
            an["time"] = null;
            if (a && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
            {
                var info = a.GetCurrentAnimatorStateInfo(0);
                an["state"] = StateName(real, info);
                an["stateHash"] = info.fullPathHash;
                an["time"] = (double)info.normalizedTime;
            }
            r["animator"] = an;
            r["lights"] = real.LightsPrepared.Lights;
            r["lightsFolded"] = real.LightsPrepared.Folded;
            r["icon"] = (bool)real.Icon;
            r["iconActive"] = real.Icon && real.Icon.gameObject.activeSelf;
            r["sound"] = real.SoundComponent != null;
            r["audioSuppressed"] = real.AudioSuppressed;
            r["componentsDisabled"] = real.ComponentsDisabled;
            r["patterns"] = new List<object> { real.PatternSources[0], real.PatternSources[1], real.PatternSources[2] };
            r["panel"] = real.PanelSource;
            r["buildMs"] = Math.Round(real.BuildMilliseconds, 3);
            if (ItemMotionHooks.Describe != null)
            {
                try { ItemMotionHooks.Describe(real, r); }
                catch (Exception e) { r["describeError"] = e.GetType().Name + ": " + e.Message; }
            }
            return r;
        }

        /// <summary>The animeInfos state name of a layer-0 state (short or "Base Layer." full-path hash), else null.</summary>
        public static string StateName(ItemRealization real, AnimatorStateInfo info)
        {
            var infos = real.Component?.animeInfos;
            if (infos == null) return null;
            foreach (var x in infos)
            {
                if (x == null || string.IsNullOrEmpty(x.state)) continue;
                int h = Animator.StringToHash(x.state);
                if (h == info.shortNameHash || h == info.fullPathHash || Animator.StringToHash("Base Layer." + x.state) == info.fullPathHash) return x.state;
            }
            return null;
        }

        // ------------------------------------------------------------ item.set

        static object Set(CoreContext c)
        {
            var nodes = CommandKit.Keys(c, CommandKit.KeyKind.Item, out bool many);
            if (c.Args.Kind("set") != ArgKind.Object) throw CommandKit.BadArgs("set", ArgTypes.Object);
            var set = c.Args.Nested("set");
            if (set == null || set.Names.Count == 0) throw CommandKit.BadArgs("set", ArgTypes.Object, "set names no field");

            // parse every path on every key before applying any (bad_args naming the path); StudioOps.SetItem writes
            var plan = new List<(string id, string path, object value)>();
            var model = c.Session.Model;
            foreach (var name in set.Names)
            {
                string full = StudioOps.ItemPath(name);
                if (full == null) throw CommandKit.BadArgs("set." + name, "an item-section path", "\"" + name + "\" is not a field path");
                if (StudioOps.IsItemMaterialPath(full)) throw CommandKit.BadArgs("set." + name, "an item-section path", StudioOps.ItemMaterialRefusal);
                string raw = set.RawJson(name);
                foreach (var n in nodes)
                {
                    object v;
                    try { v = FieldJson.FromText(raw, FieldAccessor.LeafType(model, n.Id, full)); }
                    catch (CommandException e) { throw new BridgeException(e.Code == CommandCodes.NotFound ? BridgeCodes.BadArgs : e.Code, "set." + name + ": " + e.Message, new BridgeErrorDetail { Arg = "set." + name }); }
                    catch (Exception e) { throw CommandKit.BadArgs("set." + name, "the save-format JSON form of " + full, e.Message); }
                    plan.Add((n.Id, full, v));
                }
            }
            var res = CommandKit.Check(c, CommandKit.Ops(c).SetItem(plan, CommandKit.Exec(c, c.Args.Get<string>("label"))));
            var r = new BridgeReply().With("changed", res.Changed).With("noop", res.NoOp);
            if (many)
            {
                var keys = new BridgeReply();
                foreach (var n in nodes)
                    if (res.PerTarget.TryGetValue(n.Id, out var pk)) keys[n.Id] = new BridgeReply().With("changed", pk.changed).With("noop", pk.noop);
                r["keys"] = keys;
            }
            return r;
        }

        // ------------------------------------------------------------ item.materials

        static object Materials(CoreContext c)
        {
            var n = Item(c, "key", CommandKit.Node(c, "key"));
            c.Runtime.ApplyNow();
            var real = c.Runtime.FindItem(n.Id);
            if (real == null || real.State != RealizationState.Built || real.InGraveyard || !real.Instance)
                throw CommandKit.NotRealized("item " + n.Id + " is not built (" + (real?.State.ToString() ?? "NotRealized") + ")", "key", real?.State.ToString() ?? "NotRealized");
            bool slotsOnly = c.Args.Get("slotsOnly", false);
            bool allProps = CommandKit.OptBool(c, "allProps") ?? false;
            var extra = c.Args.Has("props") ? c.Args.Get<string[]>("props") : Array.Empty<string>();
            var list = new List<object>();
            foreach (var e in real.Writer.Renderers)
            {
                if (!e.Renderer) continue;
                var slots = new List<object>();
                for (int i = 0; i < e.Slots.Length; i++)
                {
                    string flags = ItemMaterialWriter.FlagsOf(real.Component, e.Renderer, i);
                    if (slotsOnly && string.IsNullOrEmpty(flags)) continue;
                    var s = e.Slots[i];
                    var m = s?.Material;
                    var props = allProps && m ? CharaEditCommands.PropsOf(m, null) : new BridgeReply();
                    var texs = new BridgeReply();
                    var sizes = new BridgeReply();
                    if (m)
                    {
                        if (!allProps)
                            foreach (var p in ItemMaterialWriter.StudioProps)
                            {
                                var v = ItemMaterialWriter.PropValue(m, p, out bool declared);
                                if (declared) props[p] = v;
                            }
                        foreach (var p in extra)
                        {
                            if (string.IsNullOrEmpty(p) || props.ContainsKey(p)) continue;
                            props[p] = ItemMaterialWriter.PropValue(m, p, out _);
                        }
                        foreach (var p in ItemMaterialWriter.StudioTextures)
                        {
                            if (!m.shader || m.shader.FindPropertyIndex(p) < 0) continue;
                            var t = m.GetTexture(p);
                            texs[p] = t ? t.name : null;
                            if (t) sizes[p] = new[] { t.width, t.height };
                        }
                    }
                    slots.Add(new BridgeReply().With("index", i).With("flags", flags).With("material", s?.MaterialName)
                        .With("shaderOriginal", s?.ShaderOriginal).With("shader", m && m.shader ? m.shader.name : null).With("fallback", s?.Fallback ?? false)
                        .With("queue", m ? m.renderQueue : -1)
                        .With("keywords", Sorted(s?.KeywordsOriginal))
                        .With("keywordsCurrent", m ? Sorted(m.shaderKeywords) : null)
                        .With("props", props).With("textures", texs).With("textureSize", sizes));
                }
                if (slotsOnly && slots.Count == 0) continue;
                list.Add(new BridgeReply().With("path", e.Path).With("type", e.Renderer.GetType().Name).With("enabled", e.Renderer.enabled)
                    .With("hidden", e.Renderer.forceRenderingOff).With("active", e.Renderer.gameObject.activeInHierarchy).With("slots", slots));
            }
            return new BridgeReply().With("renderers", list);
        }

        static List<string> Sorted(string[] a)
        {
            if (a == null) return null;
            var l = new List<string>(a);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        // ------------------------------------------------------------ obj.visible

        static object Visible(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            if (c.Args.Kind("visible") != ArgKind.Bool) throw CommandKit.BadArgs("visible", ArgTypes.Bool);
            bool v = c.Args.Get<bool>("visible");
            CommandKit.Check(c, CommandKit.Ops(c).SetVisible(new[] { n.Id }, v, CommandKit.Exec(c, StudioOps.Labels.Visibility)));
            var cur = c.Session.Model.Find(n.Id) ?? n;
            return new BridgeReply().With("key", cur.Id).With("visible", cur.Doc.Visible).With("effective", cur.EffectiveVisible);
        }
    }
}
