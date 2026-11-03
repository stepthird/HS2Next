// Character builders: which code turns a node's
// CharaAppearance into a built character under its holder. The registry is ordered; the first builder that CanBuild the
// appearance wins: SubtreeCharaBuilder (stored character documents, 2.3) before CardCharaBuilder (an HS2 card source: since
// Phase 4.6 the card is imported with the runtime's importer, cached per card source while it lives, and the imported
// documents are built by the document path; the node stays card-backed in the model until an edit or a save imports it
// into the scene file). No builder → the realization is Unbuildable (`chara.unbuildable`). A builder owns
// CharaAssembler.Build and the one ShaderMap.ApplyUrp on the fresh character root (in place since 4.0, C2); later the edit
// units map only the subtrees they create. Both use RuntimeServices.ExtOptions (a copy per build).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Data;
using HS2Next.Save;

using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class BuildContext
    {
        public SceneRuntime Runtime;
        public RuntimeServices Services;
        public TreeNode Node;
        /// <summary>The node's holder: the built root goes under it at local identity.</summary>
        public Transform Holder;
    }

    public sealed class CharaBuildResult
    {
        /// <summary>The character root, already under the holder at local identity, URP-mapped; null on failure.</summary>
        public GameObject Root;
        public CharaContext Context;
        public CharaAssembler Assembler;
        /// <summary>The card the character was built from (card builders), else null.</summary>
        public CardAppearanceSource Card;
        /// <summary>The stored subtree the character was built from (document builder), else null.</summary>
        public CharaSubtree Stored;
        /// <summary>Build slot → accessory id when the builder knows them (documents); null = the card formula.</summary>
        public IReadOnlyDictionary<int, string> AccessoryIds;
        /// <summary>Findings of the build (document builder: build.*, part.*); null when none were collected.</summary>
        public FindingList Findings;
        /// <summary>Failure text (the exception); null on success.</summary>
        public string Error;
        /// <summary>GameObjects the build left at a scene root besides the character root (moved under it).</summary>
        public int StrayRoots;
        /// <summary>Partial objects destroyed after a failed build.</summary>
        public int Destroyed;
        public double Milliseconds;
        public bool Ok => Root != null && Error == null;
        /// <summary>What the character was built from (the stored subtree or the card source).</summary>
        public object BuiltFrom => (object)Stored ?? Card;
    }

    public interface ICharaBuilder
    {
        bool CanBuild(CharaAppearance a);
        CharaBuildResult Build(CharaAppearance a, BuildContext ctx);
    }

    public static class CharaBuilders
    {
        /// <summary>C2: the URP mapping of a new character works in place (default). false: the pre-4.0 copies (U4 measurement).</summary>
        public static bool UrpInPlace = true;

        static readonly List<ICharaBuilder> builders = new List<ICharaBuilder> { SubtreeCharaBuilder.Instance, CardCharaBuilder.Instance };

        public static IReadOnlyList<ICharaBuilder> All => builders;

        /// <summary>Registers a builder; <paramref name="first"/> puts it in front of the existing ones.</summary>
        public static void Register(ICharaBuilder b, bool first = true)
        {
            if (b == null || builders.Contains(b)) return;
            if (first) builders.Insert(0, b); else builders.Add(b);
        }

        public static bool Unregister(ICharaBuilder b) => builders.Remove(b);

        /// <summary>The first builder that can build <paramref name="a"/>, or null.</summary>
        public static ICharaBuilder For(CharaAppearance a)
        {
            if (a == null) return null;
            foreach (var b in builders) if (b.CanBuild(a)) return b;
            return null;
        }

        /// <summary>The object <paramref name="b"/> builds <paramref name="a"/> from (rebuild detection).</summary>
        public static object SourceOf(ICharaBuilder b, CharaAppearance a) =>
            a == null ? null : b is SubtreeCharaBuilder ? a.Stored : b is CardCharaBuilder ? a.Card : (object)a;

        /// <summary>
        /// After 1.1.0 (default on): the physics chains set up where the assembler built the character are carried rigidly to where
        /// the root stands at the character's first step (CharaMotion.CarryChainsAtFirstStep), so a character that appears away from
        /// the origin does not swing in from there (its breasts and hips used to whip for about a second). False = the behaviour
        /// before (tests). A root at the assembly's place at that step (the origin) touches nothing.
        /// </summary>
        public static bool CarryChainsToPlacement = true;

        /// <summary>A copy of the services' extension options for one build (2.2 used the assembler default; equal values).</summary>
        public static CharaExtOptions CopyOf(CharaExtOptions o)
        {
            if (o == null) return null;
            var c = new CharaExtOptions();
            foreach (var f in typeof(CharaExtOptions).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (!f.IsInitOnly) f.SetValue(c, f.GetValue(o));
            return c;
        }

        /// <summary>
        /// The steps every builder shares: the assembler build, the root under the holder at local
        /// identity, partial objects of a failed build destroyed, stray roots moved under the character root, one
        /// ShaderMap.ApplyUrp on the fresh root. <paramref name="res"/> receives the outcome.
        /// </summary>
        internal static void Run(CharaBuildResult res, BuildContext ctx, Func<CharaAssembler, GameObject> build, Stopwatch sw)
        {
            var scene = SceneManager.GetActiveScene();
            var before = new HashSet<GameObject>(scene.IsValid() ? scene.GetRootGameObjects() : Array.Empty<GameObject>());
            var asm = new CharaAssembler(ctx.Services.Catalog) { ExtOptions = CopyOf(ctx.Services.ExtOptions) };
            GameObject root = null;
            try
            {
                root = build(asm);
                var assembled = root.transform.localToWorldMatrix;
                root.transform.SetParent(ctx.Holder, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                if (CarryChainsToPlacement) asm.Context?.Motion?.CarryChainsAtFirstStep(root.transform, assembled);
            }
            catch (Exception e)
            {
                res.Error = e.GetType().Name + ": " + e.Message;
                if (scene.IsValid())
                    foreach (var go in scene.GetRootGameObjects())
                        if (go && !before.Contains(go)) { Object.DestroyImmediate(go); res.Destroyed++; }
                if (root) { Object.DestroyImmediate(root); res.Destroyed++; }
                res.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return;
            }
            // Anything else the build left at a scene root (e.g. an accessory whose anchor was not found) goes under the
            // character root, so the realization owns it and a map unload cannot destroy it.
            if (scene.IsValid())
                foreach (var go in scene.GetRootGameObjects())
                    if (go && go != root && !before.Contains(go) && go.transform.parent == null)
                    {
                        go.transform.SetParent(root.transform, true);
                        res.StrayRoots++;
                        asm.Log.Add("scene: stray root " + go.name + " moved under the character root");
                    }
            // C2: after OwnAll every slot holds a material of this character, so the
            // URP mapping works in place: Context.BodyDraw / FaceDraw / OwnedMaterials are the drawn materials and the edit
            // units write them. With OwnAllMaterials off (measurements) the mapping keeps making copies.
            bool own = CharaAssembler.OwnAllMaterials && UrpInPlace;
            ShaderMap.ApplyUrp(root, copy: !own);
            if (own && asm.Context?.Edit != null) asm.Context.Edit.UrpMapped = true;
            res.Root = root;
            res.Context = asm.Context;
            res.Assembler = asm;
            res.Milliseconds = sw.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>Builds a character from its stored documents: the document adapter in front of the assembler.</summary>
    public sealed class SubtreeCharaBuilder : ICharaBuilder
    {
        public static readonly SubtreeCharaBuilder Instance = new SubtreeCharaBuilder();

        public bool CanBuild(CharaAppearance a) => a?.Stored != null;

        public CharaBuildResult Build(CharaAppearance a, BuildContext ctx)
        {
            var res = new CharaBuildResult { Stored = a.Stored, Findings = new FindingList() };
            var sw = Stopwatch.StartNew();
            CharaBuildInput input;
            try { input = CharaDocumentInput.From(a.Stored, ctx.Runtime?.Model?.File?.Res, ctx.Services.Catalog, res.Findings); }
            catch (Exception e)
            {
                res.Error = "character documents not buildable: " + e.GetType().Name + ": " + e.Message;
                res.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return res;
            }
            res.AccessoryIds = input.AccessoryIds;
            CharaBuilders.Run(res, ctx, asm => asm.Build(input), sw);
            if (res.Ok) CharaDocumentInput.AfterBuild(res.Context);
            return res;
        }
    }

    /// <summary>
    /// Builds a character from its HS2 card source: the card imported with the runtime's importer
    /// (ImportSetup.ImporterOf; cached per card source and SHA-256 while the source lives), then the imported documents
    /// through the document path (as SubtreeCharaBuilder). The import's warnings and errors are reported as build findings;
    /// a failed import fails the build.
    /// </summary>
    public sealed class CardCharaBuilder : ICharaBuilder
    {
        public static readonly CardCharaBuilder Instance = new CardCharaBuilder();

        sealed class Imported
        {
            public string Sha256;
            public CharaContainer Container;
            public readonly FindingList Findings = new FindingList();
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardAppearanceSource, Imported> imports =
            new System.Runtime.CompilerServices.ConditionalWeakTable<CardAppearanceSource, Imported>();

        /// <summary>Imports done by the builder since the start (tests: a rebuild of the same source does not import again).</summary>
        public static int ImportCount { get; private set; }

        public bool CanBuild(CharaAppearance a) => a?.Card != null;

        public CharaBuildResult Build(CharaAppearance a, BuildContext ctx)
        {
            var res = new CharaBuildResult { Card = a.Card, Findings = new FindingList() };
            var sw = Stopwatch.StartNew();
            var imp = ImportOf(a.Card, ctx.Services, out string error);
            if (imp == null)
            {
                res.Error = error;
                res.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return res;
            }
            foreach (var f in imp.Findings)
                if (f.Severity != FindingSeverity.Info) res.Findings.Add(f.Severity, f.Code, f.Path, f.Message);
            CharaBuildInput input;
            try { input = CharaDocumentInput.From(imp.Container.Subtree, imp.Container.Res, ctx.Services.Catalog, res.Findings); }
            catch (Exception e)
            {
                res.Error = "imported documents not buildable: " + e.GetType().Name + ": " + e.Message;
                res.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return res;
            }
            res.AccessoryIds = input.AccessoryIds;
            CharaBuilders.Run(res, ctx, asm => asm.Build(input), sw);
            if (res.Ok) CharaDocumentInput.AfterBuild(res.Context);
            return res;
        }

        static Imported ImportOf(CardAppearanceSource src, RuntimeServices services, out string error)
        {
            error = null;
            if (imports.TryGetValue(src, out var hit) && hit.Sha256 == src.Sha256 && hit.Container != null) return hit;
            imports.Remove(src);
            var importer = ImportSetup.ImporterOf(services);
            if (importer == null) { error = "no card importer is available (no content catalog)"; return null; }
            var imp = new Imported { Sha256 = src.Sha256 };
            try { imp.Container = importer.Import(src, imp.Findings); }
            catch (Exception e) { error = "import of the card failed: " + e.GetType().Name + ": " + e.Message; return null; }
            ImportCount++;
            if (imp.Container == null)
            {
                string first = null;
                foreach (var f in imp.Findings) if (f.Severity == FindingSeverity.Error) { first = f.Code + ": " + f.Message; break; }
                error = "import of the card failed" + (first != null ? " (" + first + ")" : "");
                return null;
            }
            imports.Add(src, imp);
            return imp;
        }
    }
}
