// Applies the "geometry" group of card plugin data to a character that CharaAssembler is building, in the order
// vanilla assembly → textures/overlays → MaterialEditor → shapes →
// ABMX (+pushUp) → DBE. CharaAssembler.Build calls Apply at four points:
//   BodyMesh     right after the body prefab is spawned and rebound, before the body skin is composited: no module;
//                the BodyMesh build hooks run (CharaBuildHooks; none in the official build), then the skin layers plugins
//                set on the scene character outside builds are joined (none in the official build);
//   Accessories  no module: the accessory units load MoreAccessories slots ≥ 20 like slots 0..19, and a slot's
//                stored show = false is folded into the scene's accessory visibility at import, never toggled here;
//   Textures     after the whole base assembly, before the shapes: skin layers (card KSOX + the layers build hooks
//                registered) composited into the skin passes, KCOX clothes overlays, MaterialEditor
//                edits on top of both;
//   Final        after the shape sliders: pushUp stored (off), AdditionalAccessoryControls rules for the
//                static dressed state, ABMX bone modifiers incl. the rule bone effects,
//                DynamicBoneEditor overrides, InvisibleBody; then the Final build hooks.
// The card's uncensor data (body.uncensor) is kept as data only: no module reads it.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Data.Ext;

namespace HS2Next.Chara.Ext
{
    public enum ExtStage { BodyMesh, Accessories, Textures, Final }

    /// <summary>KSOX "Texture size limit": how far an overlay larger than its render texture grows it.</summary>
    public enum OverlaySizeLimit { Original, OriginalX2, Unlimited }

    /// <summary>Which extension modules run, and the few behaviour switches.</summary>
    public sealed class CharaExtOptions
    {
        /// <summary>Master switch (false = the card's plugin data is not decoded or applied).</summary>
        public bool Enabled = true;
        public bool MoreAccessories = true, Abmx = true, InvisibleBody = true, AccessoryControls = true, DynamicBoneEditor = true;
        /// <summary>Texture stage: skin layers (card KSOX + the layers build hooks registered), KCOX, MaterialEditor.</summary>
        public bool SkinOverlays = true, ClothesOverlays = true, MaterialEditor = true;
        /// <summary>Overlay render-texture growth (KSOX/KCOX config, default OriginalX2 as on the reference install).</summary>
        public OverlaySizeLimit OverlaySizeLimit = OverlaySizeLimit.OriginalX2;
        /// <summary>Honour the Studio switches stored on the card (KSOX EnableInStudioSkin/Iris, KCOX EnableInStudio).</summary>
        public bool OverlaysStudio = true;
        /// <summary>MaterialEditor: decode textures of Bump/Normal properties linear. false = sRGB like ME (normal maps as stored).</summary>
        public bool MaterialEditorLinearNormals = false;
        /// <summary>AAC in Studio semantics: startup and clothes rules only for slots with STUDIO_LOAD.
        /// false = main-game semantics (every slot's startup and clothes rules).</summary>
        public bool AccessoryControlsStudio = true;
        /// <summary>pushUp effect. Not implemented: the reference game does not run the plugin, the data is only stored.</summary>
        public bool PushUpEffect = false;
        /// <summary>Add the BoneModifierApplier component (the motion stage bones.modifiers applies it every step); the one-shot apply happens either way.</summary>
        public bool BoneModifierComponent = true;
    }

    /// <summary>What the extension stage did to one character (for tests and reports).</summary>
    public sealed class ExtStats
    {
        public int ExtraSlotsUsed, ExtraSlotsLoaded, ExtraSlotsHidden;
        public int AbmxModifiers, AbmxApplied, AbmxIdentity, AbmxOnAccessories;
        public readonly List<string> AbmxUnknownBones = new List<string>();
        public int AacRules, AacRulesEvaluated, AacSlotsChanged, AacHairObjectsHidden, AacBoneEffects;
        public int DbeEntries, DbeEntriesMatched, DbeComponents, DbeFields;
        public bool InvisibleBody;
        public int InvisibleRenderers;
        public bool PushUpStored;
        // texture stage
        /// <summary>Skin layers composited, by "TexType source" (e.g. "BodyOver card"); without a graphics
        /// device: decoded and counted, not drawn.</summary>
        public readonly SortedDictionary<string, int> SkinLayersComposited = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SkinLayersSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> SkinResized = new List<string>();
        public int SkinPassesRebuilt;
        public int KcoxEntries, KcoxApplied, KcoxOverrides, KcoxOverlays, KcoxCleared, KcoxResized;
        public readonly SortedDictionary<string, int> KcoxSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int MeItems, MeApplied, MeShaderSwaps, MeShaderSwapsToFitted, MeTexturesDecoded, MeTextureAssignments;
        public long MeTextureBytes;
        public readonly SortedDictionary<string, int> MeSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Shader names requested by ME without a fitted URP replacement (items).</summary>
        public readonly SortedDictionary<string, int> MeUnknownShaders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>"fitted shader _Property": written on the original but absent from its URP replacement (materials).</summary>
        public readonly SortedDictionary<string, int> MeNotOnFitted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeNotes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>"shader _Property" of items skipped because the material's shader lacks the property (items).</summary>
        public readonly SortedDictionary<string, int> MeAbsentProperties = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>"ObjectType MaterialName" of items skipped because no material of the object has that name (items).</summary>
        public readonly SortedDictionary<string, int> MeMissingMaterials = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> Errors = new List<string>();
        /// <summary>The generic build hooks (CharaBuildHooks) that ran on this character, by "&lt;id&gt;@&lt;stage&gt;": the last
        /// run of each (ran, renderers changed, errors). Empty when no hook is registered (the official build).</summary>
        public readonly SortedDictionary<string, HookRun> Hooks = new SortedDictionary<string, HookRun>(StringComparer.Ordinal);

        public static void Inc(SortedDictionary<string, int> d, string key, int n = 1) => d[key] = d.TryGetValue(key, out int v) ? v + n : n;
        static string Join(SortedDictionary<string, int> d) => string.Join(", ", d.Select(kv => $"{kv.Key} {kv.Value}"));

        /// <summary>The hooks part of <see cref="Summary"/>: "hooks -" or "hooks [id@Stage ran renderers n, ...]".</summary>
        public string HooksSummary() => Hooks.Count == 0 ? "hooks -" : "hooks [" + string.Join(", ", Hooks.Select(kv => kv.Key + " " + kv.Value)) + "]";

        public bool TexturesActive => SkinLayersComposited.Count > 0 || SkinLayersSkipped.Count > 0 || KcoxEntries > 0 || MeItems > 0;

        /// <summary>The texture stage in one line (empty when it did nothing).</summary>
        public string TexturesSummary()
        {
            var p = new List<string>();
            if (SkinLayersComposited.Count > 0 || SkinLayersSkipped.Count > 0)
                p.Add($"skin layers [{Join(SkinLayersComposited)}]{(SkinLayersSkipped.Count > 0 ? $" skipped [{Join(SkinLayersSkipped)}]" : "")}" +
                      $"{(SkinResized.Count > 0 ? " resized " + string.Join(", ", SkinResized) : "")}");
            if (KcoxEntries > 0)
                p.Add($"kcox {KcoxApplied}/{KcoxEntries} (overrides {KcoxOverrides}, images {KcoxOverlays}, cleared {KcoxCleared}, resized {KcoxResized})" +
                      $"{(KcoxSkipped.Count > 0 ? $" skipped [{Join(KcoxSkipped)}]" : "")}");
            if (MeItems > 0)
                p.Add($"me {MeApplied}/{MeItems} (shader swaps {MeShaderSwaps}{(MeShaderSwapsToFitted > 0 ? $", {MeShaderSwapsToFitted} to the URP shader" : "")}, textures {MeTexturesDecoded})" +
                      $"{(MeSkipped.Count > 0 ? $" skipped [{Join(MeSkipped)}]" : "")}{(MeUnknownShaders.Count > 0 ? $" unknown shaders [{Join(MeUnknownShaders)}]" : "")}" +
                      $"{(MeNotOnFitted.Count > 0 ? $" not on fitted [{Join(MeNotOnFitted)}]" : "")}{(MeNotes.Count > 0 ? $" notes [{Join(MeNotes)}]" : "")}");
            return string.Join("; ", p);
        }

        public string Summary() =>
            $"extra slots {ExtraSlotsLoaded}/{ExtraSlotsUsed}; abmx {AbmxApplied}/{AbmxModifiers} (identity {AbmxIdentity}, unknown {AbmxUnknownBones.Count}); " +
            HooksSummary() + "; " +
            $"aac rules {AacRulesEvaluated}/{AacRules} (slots changed {AacSlotsChanged}, hair objects {AacHairObjectsHidden}, bone effects {AacBoneEffects}); " +
            $"dbe {DbeEntriesMatched}/{DbeEntries} (components {DbeComponents}, fields {DbeFields}); invisible {(InvisibleBody ? InvisibleRenderers + " renderers" : "no")}; pushUp {(PushUpStored ? "stored" : "-")}" +
            (TexturesActive ? "; " + TexturesSummary() : "");

        // ---- arithmetic for the per-object re-application of the edit units

        static readonly System.Reflection.FieldInfo[] fields = typeof(ExtStats).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        static bool Selected(System.Reflection.FieldInfo f, string[] prefixes)
        {
            if (prefixes == null) return f.Name != nameof(Errors);
            foreach (var p in prefixes) if (f.Name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>A deep copy.</summary>
        public ExtStats Copy()
        {
            var c = new ExtStats();
            c.Set(this, null);
            c.Errors.AddRange(Errors);
            return c;
        }

        /// <summary>Adds (<paramref name="sign"/> 1) or subtracts (-1) the counters of <paramref name="o"/> whose field names
        /// start with one of <paramref name="prefixes"/> (null: all but Errors): ints and longs, the per-key counts, the lists
        /// (items added / removed). Flags and texts are not counters and are left.</summary>
        public void Add(ExtStats o, int sign, string[] prefixes)
        {
            if (o == null) return;
            foreach (var f in fields)
            {
                if (!Selected(f, prefixes)) continue;
                var t = f.FieldType;
                if (t == typeof(int)) f.SetValue(this, (int)f.GetValue(this) + sign * (int)f.GetValue(o));
                else if (t == typeof(long)) f.SetValue(this, (long)f.GetValue(this) + sign * (long)f.GetValue(o));
                else if (t == typeof(SortedDictionary<string, int>))
                {
                    var d = (SortedDictionary<string, int>)f.GetValue(this);
                    foreach (var kv in (SortedDictionary<string, int>)f.GetValue(o))
                    {
                        int v = (d.TryGetValue(kv.Key, out int x) ? x : 0) + sign * kv.Value;
                        if (v == 0) d.Remove(kv.Key); else d[kv.Key] = v;
                    }
                }
                else if (t == typeof(List<string>))
                {
                    var l = (List<string>)f.GetValue(this);
                    foreach (var s in (List<string>)f.GetValue(o)) { if (sign > 0) l.Add(s); else l.Remove(s); }
                }
            }
        }

        /// <summary>Sets the fields named by <paramref name="prefixes"/> (null: all but Errors) to <paramref name="o"/>'s values (collections copied).</summary>
        public void Set(ExtStats o, string[] prefixes)
        {
            if (o == null) return;
            foreach (var f in fields)
            {
                if (!Selected(f, prefixes)) continue;
                var t = f.FieldType;
                if (t == typeof(SortedDictionary<string, int>))
                {
                    var d = (SortedDictionary<string, int>)f.GetValue(this);
                    d.Clear();
                    foreach (var kv in (SortedDictionary<string, int>)f.GetValue(o)) d[kv.Key] = kv.Value;
                }
                else if (t == typeof(SortedDictionary<string, HookRun>))
                {
                    var d = (SortedDictionary<string, HookRun>)f.GetValue(this);
                    d.Clear();
                    foreach (var kv in (SortedDictionary<string, HookRun>)f.GetValue(o)) d[kv.Key] = kv.Value?.Clone();
                }
                else if (t == typeof(List<string>))
                {
                    var l = (List<string>)f.GetValue(this);
                    l.Clear();
                    l.AddRange((List<string>)f.GetValue(o));
                }
                else if (!f.IsInitOnly) f.SetValue(this, f.GetValue(o));
            }
        }
    }

    public static class CharaExtApplier
    {
        /// <summary>Runs the modules of one stage (when the card has plugin data and the modules are on), then the build hooks
        /// registered for it (BodyMesh, Final; CharaBuildHooks). Exceptions of a module are logged and recorded in
        /// <see cref="ExtStats.Errors"/>, those of a hook in <see cref="ExtStats.Hooks"/>; the build goes on.</summary>
        public static void Apply(CharaContext ctx, CardExtData ext, CharaExtOptions options, ExtStage stage)
        {
            if (ctx == null) return;
            if (ext != null && options != null && options.Enabled) ApplyModules(ctx, ext, options, stage);
            // the generic build hooks of the stage (CharaBuildHooks; none in the official build)
            if (stage == ExtStage.BodyMesh)
            {
                CharaBuildHooks.Run(HookStage.BodyMesh, ctx, true, null);
                CharaBuildHooks.JoinRecord(ctx);
            }
            else if (stage == ExtStage.Final) CharaBuildHooks.Run(HookStage.Final, ctx, ctx.Edit == null || ctx.Edit.InBuild, ctx.Edit?.Report);
        }

        static void ApplyModules(CharaContext ctx, CardExtData ext, CharaExtOptions options, ExtStage stage)
        {
            switch (stage)
            {
                case ExtStage.Textures:
                    if (options.SkinOverlays) Run(ctx, "ksox", () => SkinOverlayApplier.Apply(ctx, ext.SkinOverlays, options));
                    if (options.ClothesOverlays) Run(ctx, "kcox", () => ClothesOverlayApplier.Apply(ctx, ext.ClothesOverlays, options));
                    if (options.MaterialEditor) Run(ctx, "materialEditor", () => MaterialEditorApplier.Apply(ctx, ext.MaterialEditor, options));
                    break;
                case ExtStage.Final:
                    if (ext.PushUp != null)
                    {
                        ctx.PushUp = ext.PushUp;
                        ctx.Stats.PushUpStored = true;
                        ctx.Log.Add(options.PushUpEffect ? "pushUp: stored; effect requested but not implemented (off)" : "pushUp: stored, off");
                    }
                    var effects = new List<BoneModifierApplier.Modifier>();
                    if (options.AccessoryControls) Run(ctx, "aac", () => AccessoryControlsApplier.Apply(ctx, ext, options, effects));
                    if (options.Abmx || effects.Count > 0) Run(ctx, "abmx", () => AbmxApplier.Apply(ctx, options.Abmx ? ext.Abmx : null, effects, options));
                    if (options.DynamicBoneEditor) Run(ctx, "dbe", () => DynamicBoneEditorApplier.Apply(ctx, ext.DynamicBoneEditor));
                    if (options.InvisibleBody) Run(ctx, "invisibleBody", () => InvisibleBodyApplier.Apply(ctx, ext.InvisibleBody));
                    break;
            }
        }

        /// <summary>Runs one module like a stage does (exceptions logged and recorded in ExtStats.Errors); the edit units'
        /// per-object re-application uses it.</summary>
        public static void RunModule(CharaContext ctx, string module, Action a) => Run(ctx, module, a);

        static void Run(CharaContext ctx, string module, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                string msg = $"{module}: {e.GetType().Name}: {e.Message}";
                ctx.Stats.Errors.Add(msg);
                ctx.Log.Add("ext error " + msg);
            }
        }
    }
}
