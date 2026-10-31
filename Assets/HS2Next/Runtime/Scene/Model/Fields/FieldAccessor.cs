// Generic access to model state by field path. A name step selects the
// public instance field named like the segment with its first letter upper-cased (the 2.1 classes follow this
// for every schema member); hard-coded: the kind-section segment maps to SceneNode.State (and must match the
// node's kind), `targets.<Name>` inside `ik` maps to IkTargets.Targets[IkTargets.Names.IndexOf(Name)], the scene
// path `title` maps to SceneModel.Title. `[n]` indexes arrays and lists, `["k"]` sorted maps. The step chain
// (fields, static types, rules) is compiled once per (target kind, path text) and cached; resolution then
// walks the live objects. Not reachable: id, kind, parent, order, ext, anything in an opaque node, unknown
// members, and the inside of FileRef values (set as a whole).
// Phase 4.0: the node root `appearance` addresses the character documents of
// TreeNode.Appearance.Stored: `appearance.chara.<chara.json member path>`, `appearance.coord.<coord.json member path>`
// with the same reflection rule; hard-coded there: `hair.back/front/side/option` → HairData.Parts[k], the eight clothes
// kinds → ClothesSet.Parts[k] (save-format names), `accessories["<id>"]` → the element of CoordDocument.Accessories
// whose Id is the key (an index on that list is refused, so paths survive inserts and removals). Readable but not
// settable (unsupported): the documents as wholes, profile.sex, coord sex, profile.hs2 (Q26), accessories[*].id and
// .importSlot; not reachable: `appearance` alone, any ext member. A card-backed node (no Stored) is unsupported
// ("character not imported").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class FieldAccessor
    {
        /// <summary>The value at <paramref name="path"/> of node <paramref name="nodeId"/> (null = the scene): the live
        /// object (read-only for callers), or <see cref="FieldValue.Absent"/> for a missing map key. Throws
        /// <see cref="CommandException"/> (bad_args, not_found, wrong_kind, unsupported).</summary>
        public static object Get(SceneModel m, string nodeId, string path) => Resolve(m, nodeId, ParsePath(path)).Get();

        /// <summary>As <see cref="Get"/> without exceptions.</summary>
        public static bool TryGet(SceneModel m, string nodeId, string path, out object value, out string code)
        {
            try
            {
                value = Get(m, nodeId, path);
                code = null;
                return true;
            }
            catch (CommandException e)
            {
                value = null;
                code = e.Code;
                return false;
            }
        }

        /// <summary>The C# type of the value at a path (for <see cref="FieldJson.FromJson"/>); throws as <see cref="Get"/>.</summary>
        public static Type LeafType(SceneModel m, string nodeId, string path) => Resolve(m, nodeId, ParsePath(path)).Type;

        /// <summary>Canonical JSON text of the value at a path ("null" for null and for a missing map key).</summary>
        public static string GetJson(SceneModel m, string nodeId, string path) => FieldJson.ToText(Get(m, nodeId, path));

        /// <summary>View state (never recorded): node paths under `ui`, the scene path `camera.current`.</summary>
        public static bool IsViewPath(string nodeId, string path)
        {
            if (path == null) return false;
            return nodeId != null ? FieldPath.IsPrefixOf("ui", path) : FieldPath.IsPrefixOf("camera.current", path);
        }

        internal static FieldPath ParsePath(string path)
        {
            if (!FieldPath.TryParse(path, out var p, out string error))
                throw new CommandException(CommandCodes.BadArgs, "bad field path \"" + path + "\": " + error);
            return p;
        }

        // ---------------- compiled plans ----------------

        internal enum StepKind { Field, KindSection, IkTarget, Title, ArrayIndex, ListIndex, MapKey, AppearanceDoc, HairPart, ClothesPart, AccessoryById, OrderedIndex }

        internal sealed class PlanStep
        {
            public StepKind Kind;
            public FieldInfo Field;
            public int Index;
            public string Key;
            /// <summary>Static type of the value this step yields.</summary>
            public Type Type;
            public MemberRule Rule = MemberRule.Default;
            /// <summary>OrderedIndex: the list's write order (FieldSchema rule).</summary>
            public Func<IList, IList> Order;
        }

        internal sealed class Plan
        {
            public FieldPath Path;
            public PlanStep[] Steps;
            /// <summary>Kind section the path needs (node paths), else null.</summary>
            public string Kind;
            public string ErrorCode, Error;
            public bool CharaUi;
            /// <summary>An `appearance.` path (resolved on Appearance.Stored).</summary>
            public bool Appearance;
            /// <summary>Readable, but writes fail with this code and message (appearance paths).</summary>
            public string WriteCode, WriteError;
        }

        static readonly Dictionary<string, Plan> nodePlans = new Dictionary<string, Plan>(StringComparer.Ordinal);
        static readonly Dictionary<string, Plan> scenePlans = new Dictionary<string, Plan>(StringComparer.Ordinal);
        static readonly object plansLock = new object();

        static readonly Dictionary<string, Type> kindSections = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [SceneNode.KindChara] = typeof(CharaNodeState),
            [SceneNode.KindItem] = typeof(ItemNodeState),
            [SceneNode.KindLight] = typeof(LightNodeState),
            [SceneNode.KindFolder] = typeof(FolderNodeState),
            [SceneNode.KindRoute] = typeof(RouteNodeState),
            [SceneNode.KindCamera] = typeof(CameraNodeState),
        };

        static readonly HashSet<string> sceneRoots = new HashSet<string>(StringComparer.Ordinal)
            { "map", "effects", "camera", "charaLight", "sound", "background", "frame" };
        static readonly HashSet<string> nodeRoots = new HashSet<string>(StringComparer.Ordinal) { "name", "transform", "visible", "ui" };
        static readonly HashSet<string> envelopeOnly = new HashSet<string>(StringComparer.Ordinal) { "id", "kind", "parent", "order", "ext" };

        internal static Plan PlanOf(bool scene, FieldPath p)
        {
            var cache = scene ? scenePlans : nodePlans;
            lock (plansLock)
            {
                if (cache.TryGetValue(p.Text, out var hit)) return hit;
                var plan = Compile(scene, p);
                if (cache.Count > 8192) cache.Clear();
                cache[p.Text] = plan;
                return plan;
            }
        }

        static Plan Fail(Plan plan, string code, string error)
        {
            plan.ErrorCode = code;
            plan.Error = error;
            plan.Steps = null;
            return plan;
        }

        static Plan Compile(bool scene, FieldPath p)
        {
            var plan = new Plan { Path = p };
            var steps = new List<PlanStep>();
            Type cur;
            string root = p.Root;
            int k = 1;
            if (scene)
            {
                if (root == "title")
                {
                    if (p.Count != 1) return Fail(plan, CommandCodes.BadArgs, "title is a string");
                    steps.Add(new PlanStep { Kind = StepKind.Title, Type = typeof(string) });
                    plan.Steps = steps.ToArray();
                    return plan;
                }
                if (!sceneRoots.Contains(root)) return Fail(plan, CommandCodes.BadArgs, "\"" + root + "\" is not a scene member");
                var f = FieldOf(typeof(SceneSettings), root);
                steps.Add(new PlanStep { Kind = StepKind.Field, Field = f, Type = f.FieldType, Rule = FieldSchema.RuleOf(f) });
                cur = f.FieldType;
            }
            else
            {
                if (root == AppearancePaths.Root) return CompileAppearance(plan, p);
                if (envelopeOnly.Contains(root)) return Fail(plan, CommandCodes.BadArgs, "\"" + root + "\" is not addressable by a field path");
                if (kindSections.TryGetValue(root, out var st))
                {
                    if (p.Count == 1) return Fail(plan, CommandCodes.BadArgs, "a kind section is not set as a whole");
                    plan.Kind = root;
                    steps.Add(new PlanStep { Kind = StepKind.KindSection, Type = st });
                    cur = st;
                }
                else if (nodeRoots.Contains(root))
                {
                    var f = FieldOf(typeof(SceneNode), root);
                    steps.Add(new PlanStep { Kind = StepKind.Field, Field = f, Type = f.FieldType, Rule = FieldSchema.RuleOf(f) });
                    cur = f.FieldType;
                }
                else return Fail(plan, CommandCodes.BadArgs, "\"" + root + "\" is not a node member");
            }

            // p.Steps flattens segments: name steps after the first, index and key steps.
            for (; k < p.Count; k++)
            {
                var s = p[k];
                var prev = steps[steps.Count - 1];
                switch (s.Kind)
                {
                    case FieldStepKind.Name:
                    {
                        if (cur == typeof(IkTargets))
                        {
                            int i = Array.IndexOf(IkTargets.Names, s.Name);
                            if (i < 0) return Fail(plan, CommandCodes.BadArgs, "\"" + s.Name + "\" is not an IK target");
                            steps.Add(new PlanStep { Kind = StepKind.IkTarget, Index = i, Type = typeof(IkTarget), Rule = IkTargetRule });
                            cur = typeof(IkTarget);
                            break;
                        }
                        if (!typeof(DocObject).IsAssignableFrom(cur) || cur == typeof(FileRef) || cur == typeof(OpaqueNode))
                            return Fail(plan, CommandCodes.BadArgs, "no member \"" + s.Name + "\" inside a " + cur.Name);
                        var f = FieldOf(cur, s.Name);
                        if (f == null) return Fail(plan, CommandCodes.BadArgs, cur.Name + " has no member \"" + s.Name + "\"");
                        if (f.DeclaringType == typeof(NodeUi) && (f.Name == nameof(NodeUi.ExpandedGroups) || f.Name == nameof(NodeUi.ExpandedPoints))) plan.CharaUi = true;
                        steps.Add(new PlanStep { Kind = StepKind.Field, Field = f, Type = f.FieldType, Rule = FieldSchema.RuleOf(f) });
                        cur = f.FieldType;
                        break;
                    }
                    case FieldStepKind.Index:
                    {
                        Type et = cur.IsArray ? cur.GetElementType()
                            : cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(List<>) ? cur.GetGenericArguments()[0] : null;
                        if (et == null) return Fail(plan, CommandCodes.BadArgs, "[" + s.Index + "] needs an array or list");
                        steps.Add(new PlanStep { Kind = cur.IsArray ? StepKind.ArrayIndex : StepKind.ListIndex, Index = s.Index, Type = et, Rule = FieldSchema.ElementRule(prev.Rule) });
                        cur = et;
                        break;
                    }
                    default:
                    {
                        if (!(cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(SortedDictionary<,>)))
                            return Fail(plan, CommandCodes.BadArgs, "a key needs a map");
                        var vt = cur.GetGenericArguments()[1];
                        steps.Add(new PlanStep { Kind = StepKind.MapKey, Key = s.Name, Type = vt });
                        cur = vt;
                        break;
                    }
                }
            }
            plan.Steps = steps.ToArray();
            return plan;
        }

        static readonly MemberRule IkTargetRule = new MemberRule { Nullable = true };

        // ---------------- appearance paths ----------------

        // Members that are readable through paths but never written by a set: the sex of both documents (a
        // full rebuild), the imported game parameters (Q26), the accessory id (paths address it) and the preferred slot `importSlot`
        // (written only by the slot ops: add, copy and move-to-slot, and by the importer).
        static readonly Dictionary<FieldInfo, string> appearanceWriteBlocked = new Dictionary<FieldInfo, string>
        {
            [typeof(Profile).GetField(nameof(Profile.Sex))] = "the sex is changed by a full character change, not a set",
            [typeof(CoordDocument).GetField(nameof(CoordDocument.Sex))] = "the sex is changed by a full character change, not a set",
            [typeof(Profile).GetField(nameof(Profile.Hs2))] = "imported game parameters are read-only (Q26)",
            [typeof(AccessoryEntry).GetField(nameof(AccessoryEntry.Id))] = "an accessory id never changes (paths address it)",
            [typeof(AccessoryEntry).GetField(nameof(AccessoryEntry.ImportSlot))] = "the preferred slot is set by add / copy / move-to-slot",
        };

        static Plan CompileAppearance(Plan plan, FieldPath p)
        {
            plan.Appearance = true;
            plan.Kind = SceneNode.KindChara;
            if (p.Count == 1 || p[1].Kind != FieldStepKind.Name)
                return Fail(plan, CommandCodes.Unsupported, "address appearance.chara.<member> or appearance.coord.<member> (the appearance object is replaced with SetAppearanceCommand)");
            bool chara = p[1].Name == AppearancePaths.CharaDoc;
            if (!chara && p[1].Name != AppearancePaths.CoordDoc)
                return Fail(plan, CommandCodes.Unsupported, "\"" + p[1].Name + "\" is not an appearance document (chara or coord)");
            var steps = new List<PlanStep>();
            Type cur = chara ? typeof(CharaDocument) : typeof(CoordDocument);
            steps.Add(new PlanStep { Kind = StepKind.AppearanceDoc, Index = chara ? 0 : 1, Type = cur });
            if (p.Count == 2)
            {
                plan.WriteCode = CommandCodes.Unsupported;
                plan.WriteError = "a character document is not set as a whole (use SetAppearanceCommand)";
            }
            for (int k = 2; k < p.Count; k++)
            {
                var s = p[k];
                var prev = steps[steps.Count - 1];
                switch (s.Kind)
                {
                    case FieldStepKind.Name:
                    {
                        if (cur == typeof(HairData) && Array.IndexOf(HairData.PartNames, s.Name) >= 0)
                        {
                            steps.Add(new PlanStep { Kind = StepKind.HairPart, Index = Array.IndexOf(HairData.PartNames, s.Name), Type = typeof(HairPart) });
                            cur = typeof(HairPart);
                            break;
                        }
                        if (cur == typeof(ClothesSet) && Array.IndexOf(ClothesSet.Names, s.Name) >= 0)
                        {
                            int i = Array.IndexOf(ClothesSet.Names, s.Name);
                            steps.Add(new PlanStep { Kind = StepKind.ClothesPart, Index = i, Type = typeof(ClothesPart), Rule = FieldSchema.ClothesPartRule(i) });
                            cur = typeof(ClothesPart);
                            break;
                        }
                        if (s.Name == "ext") return Fail(plan, CommandCodes.Unsupported, "ext members are not settable through field paths");
                        if (!typeof(DocObject).IsAssignableFrom(cur) || cur == typeof(FileRef) || cur == typeof(OpaqueNode))
                            return Fail(plan, CommandCodes.BadArgs, "no member \"" + s.Name + "\" inside a " + cur.Name);
                        var f = AppearanceFieldOf(cur, s.Name);
                        if (f == null) return Fail(plan, CommandCodes.BadArgs, cur.Name + " has no member \"" + s.Name + "\"");
                        if (plan.WriteCode == null && appearanceWriteBlocked.TryGetValue(f, out string why))
                        {
                            plan.WriteCode = CommandCodes.Unsupported;
                            plan.WriteError = why;
                        }
                        steps.Add(new PlanStep { Kind = StepKind.Field, Field = f, Type = f.FieldType, Rule = FieldSchema.RuleOf(f) });
                        cur = f.FieldType;
                        break;
                    }
                    case FieldStepKind.Index:
                    {
                        if (cur == typeof(List<AccessoryEntry>))
                            return Fail(plan, CommandCodes.BadArgs, "accessories are addressed by id: accessories[\"<id>\"]");
                        if (prev.Rule.WriteOrder != null && cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            // a list the writer sorts: positions are those of the saved order
                            var oet = cur.GetGenericArguments()[0];
                            steps.Add(new PlanStep { Kind = StepKind.OrderedIndex, Index = s.Index, Type = oet, Rule = FieldSchema.ElementRule(prev.Rule), Order = prev.Rule.WriteOrder });
                            string key = prev.Rule.OrderKey;
                            bool last = k == p.Count - 1;
                            bool keyStep = !last && p[k + 1].Kind == FieldStepKind.Name && key != null &&
                                           string.Equals(p[k + 1].Name, char.ToLowerInvariant(key[0]) + key.Substring(1), StringComparison.Ordinal);
                            if ((last || keyStep) && plan.WriteCode == null)
                            {
                                plan.WriteCode = CommandCodes.BadArgs;
                                plan.WriteError = "this list is kept in its saved order (by " + key + "): set the element's other members or the whole list";
                            }
                            cur = oet;
                            break;
                        }
                        Type et = cur.IsArray ? cur.GetElementType()
                            : cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(List<>) ? cur.GetGenericArguments()[0] : null;
                        if (et == null) return Fail(plan, CommandCodes.BadArgs, "[" + s.Index + "] needs an array or list");
                        steps.Add(new PlanStep { Kind = cur.IsArray ? StepKind.ArrayIndex : StepKind.ListIndex, Index = s.Index, Type = et, Rule = FieldSchema.ElementRule(prev.Rule) });
                        cur = et;
                        break;
                    }
                    default:
                    {
                        if (cur == typeof(List<AccessoryEntry>))
                        {
                            steps.Add(new PlanStep { Kind = StepKind.AccessoryById, Key = s.Name, Type = typeof(AccessoryEntry) });
                            cur = typeof(AccessoryEntry);
                            break;
                        }
                        if (!(cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(SortedDictionary<,>)))
                            return Fail(plan, CommandCodes.BadArgs, "a key needs a map (or the accessory list)");
                        var vt = cur.GetGenericArguments()[1];
                        steps.Add(new PlanStep { Kind = StepKind.MapKey, Key = s.Name, Type = vt });
                        cur = vt;
                        break;
                    }
                }
            }
            plan.Steps = steps.ToArray();
            return plan;
        }

        /// <summary>The schema field of a document member: public instance fields that are written (not init-only
        /// shape flags such as ItemLayout.HasTilt, ClothesPart.Kind, the part arrays), not Unknown, not ext.</summary>
        static FieldInfo AppearanceFieldOf(Type t, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string fn = char.ToUpperInvariant(name[0]) + name.Substring(1);
            var f = t.GetField(fn, BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.IsInitOnly || f.IsLiteral) return null;
            if (f.DeclaringType == typeof(DocObject)) return null;   // Unknown
            if (f.FieldType == typeof(JsonLeftovers) || f.FieldType == typeof(ExtInline)) return null;
            return f;
        }

        /// <summary>As <see cref="Resolve"/> for a write: also refuses paths that are readable only (appearance).</summary>
        internal static Slot ResolveForWrite(SceneModel m, string nodeId, FieldPath p)
        {
            var plan = PlanOf(nodeId == null, p);
            if (plan.Steps != null && plan.WriteCode != null) throw Err(plan.WriteCode, p.Text + ": " + plan.WriteError);
            return Resolve(m, nodeId, p);
        }

        /// <summary>True when <paramref name="path"/> parses, resolves on node <paramref name="nodeId"/> and may be written.</summary>
        public static bool IsWritable(SceneModel m, string nodeId, string path, out string code)
        {
            try
            {
                ResolveForWrite(m, nodeId, ParsePath(path));
                code = null;
                return true;
            }
            catch (CommandException e)
            {
                code = e.Code;
                return false;
            }
        }

        static int AccessoryIndex(IList list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] is AccessoryEntry a && string.Equals(a.Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>The schema field for a JSON member name (first letter upper-cased); null for unknown members and
        /// public fields that are not schema members.</summary>
        static FieldInfo FieldOf(Type t, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string fn = char.ToUpperInvariant(name[0]) + name.Substring(1);
            var f = t.GetField(fn, BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.IsInitOnly && f.DeclaringType == typeof(IkTargets)) return null;
            if (f.DeclaringType == typeof(DocObject)) return null;   // Unknown
            if (f.FieldType == typeof(JsonLeftovers) || f.FieldType == typeof(ExtInline)) return null;
            if (t == typeof(SceneNode) && !nodeRoots.Contains(name)) return null;
            return f;
        }

        // ---------------- resolution ----------------

        internal sealed class Slot
        {
            public SceneModel Model;
            public TreeNode Node;
            public Plan Plan;
            public PlanStep Step;
            public object Owner;

            public Type Type => Step.Type;
            public MemberRule Rule => Step.Rule;
            public string PathText => Plan.Path.Text;
            public bool IsMapKey => Step.Kind == StepKind.MapKey;

            public bool Exists => Step.Kind != StepKind.MapKey || ((IDictionary)Owner).Contains(Step.Key);

            public object Get()
            {
                switch (Step.Kind)
                {
                    case StepKind.Field: return Step.Field.GetValue(Owner);
                    case StepKind.KindSection: return Node.Doc.State;
                    case StepKind.IkTarget: return ((IkTargets)Owner).Targets[Step.Index];
                    case StepKind.Title: return Model.Title;
                    case StepKind.ArrayIndex: return ((Array)Owner).GetValue(Step.Index);
                    case StepKind.ListIndex: return ((IList)Owner)[Step.Index];
                    case StepKind.AppearanceDoc: return Step.Index == 0 ? (object)((CharaSubtree)Owner).Chara : ((CharaSubtree)Owner).Coord;
                    case StepKind.HairPart: return ((HairData)Owner).Parts[Step.Index];
                    case StepKind.ClothesPart: return ((ClothesSet)Owner).Parts[Step.Index];
                    case StepKind.AccessoryById:
                    {
                        var l = (IList)Owner;
                        int i = AccessoryIndex(l, Step.Key);
                        return i >= 0 ? l[i] : null;
                    }
                    case StepKind.OrderedIndex: return Step.Order((IList)Owner)[Step.Index];
                    default:
                        var d = (IDictionary)Owner;
                        return d.Contains(Step.Key) ? d[Step.Key] : FieldValue.Absent;
                }
            }

            /// <summary>Writes a value the caller owns (no copy here); Absent removes a map key.</summary>
            public void Set(object v)
            {
                switch (Step.Kind)
                {
                    case StepKind.Field: Step.Field.SetValue(Owner, v); break;
                    case StepKind.IkTarget: ((IkTargets)Owner).Targets[Step.Index] = (IkTarget)v; break;
                    case StepKind.Title: Model.Title = (string)v; break;
                    case StepKind.ArrayIndex: ((Array)Owner).SetValue(v, Step.Index); break;
                    case StepKind.ListIndex: ((IList)Owner)[Step.Index] = v; break;
                    case StepKind.HairPart: ((HairData)Owner).Parts[Step.Index] = (HairPart)v; break;
                    case StepKind.ClothesPart: ((ClothesSet)Owner).Parts[Step.Index] = (ClothesPart)v; break;
                    case StepKind.AccessoryById:
                    {
                        var l = (IList)Owner;
                        int i = AccessoryIndex(l, Step.Key);
                        if (i < 0) throw new CommandException(CommandCodes.NotFound, PathText + ": no accessory " + Step.Key);
                        l[i] = v;
                        break;
                    }
                    case StepKind.MapKey:
                        if (FieldValue.IsAbsent(v)) ((IDictionary)Owner).Remove(Step.Key);
                        else ((IDictionary)Owner)[Step.Key] = v;
                        break;
                    default: throw new CommandException(CommandCodes.BadArgs, "not settable: " + PathText);
                }
            }
        }

        static CommandException Err(string code, string message) => new CommandException(code, message);

        /// <summary>Resolves a path against the live model (node id null = the scene).</summary>
        internal static Slot Resolve(SceneModel m, string nodeId, FieldPath p)
        {
            bool scene = nodeId == null;
            var plan = PlanOf(scene, p);
            if (plan.Steps == null) throw Err(plan.ErrorCode, p.Text + ": " + plan.Error);
            var slot = new Slot { Model = m, Plan = plan };
            object cur;
            if (scene) cur = m.Settings;
            else
            {
                var n = m.Find(nodeId);
                if (n == null) throw Err(CommandCodes.NotFound, "node " + nodeId + " is not in the scene");
                slot.Node = n;
                if (n.Kind == NodeKind.Opaque) throw Err(CommandCodes.Unsupported, "node " + nodeId + " is of an unknown kind (kept unchanged)");
                if (plan.Kind != null && plan.Kind != n.Doc.Kind) throw Err(CommandCodes.WrongKind, p.Text + ": node " + nodeId + " is a " + n.Doc.Kind);
                if (plan.CharaUi && n.Kind != NodeKind.Chara) throw Err(CommandCodes.WrongKind, p.Text + ": only character nodes have attach groups");
                if (plan.Appearance)
                {
                    cur = n.Appearance?.Stored;
                    if (cur == null)
                        throw Err(CommandCodes.Unsupported, p.Text + ": character not imported (node " + nodeId + " has no stored documents" +
                                                            (n.Appearance?.Card != null ? "; it is backed by an HS2 card" : "") + ")");
                }
                else cur = n.Doc;
            }
            var steps = plan.Steps;
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (i == steps.Length - 1)
                {
                    slot.Step = s;
                    slot.Owner = cur;
                    if ((s.Kind == StepKind.ArrayIndex && s.Index >= ((Array)cur).Length) || (s.Kind == StepKind.ListIndex && s.Index >= ((IList)cur).Count))
                        throw Err(CommandCodes.NotFound, p.Text + ": index out of range");
                    if (s.Kind == StepKind.AccessoryById && AccessoryIndex((IList)cur, s.Key) < 0)
                        throw Err(CommandCodes.NotFound, p.Text + ": no accessory with id " + s.Key);
                    if (s.Kind == StepKind.OrderedIndex && s.Index >= ((IList)cur).Count) throw Err(CommandCodes.NotFound, p.Text + ": index out of range");
                    return slot;
                }
                object next;
                switch (s.Kind)
                {
                    case StepKind.Field: next = s.Field.GetValue(cur); break;
                    case StepKind.KindSection: next = slot.Node.Doc.State; break;
                    case StepKind.IkTarget: next = ((IkTargets)cur).Targets[s.Index]; break;
                    case StepKind.ArrayIndex:
                        if (s.Index >= ((Array)cur).Length) throw Err(CommandCodes.NotFound, p.Text + ": index out of range");
                        next = ((Array)cur).GetValue(s.Index);
                        break;
                    case StepKind.ListIndex:
                        if (s.Index >= ((IList)cur).Count) throw Err(CommandCodes.NotFound, p.Text + ": index out of range");
                        next = ((IList)cur)[s.Index];
                        break;
                    case StepKind.MapKey:
                        var d = (IDictionary)cur;
                        if (!d.Contains(s.Key)) throw Err(CommandCodes.NotFound, p.Text + ": no key " + s.Key);
                        next = d[s.Key];
                        break;
                    case StepKind.AppearanceDoc: next = s.Index == 0 ? (object)((CharaSubtree)cur).Chara : ((CharaSubtree)cur).Coord; break;
                    case StepKind.OrderedIndex:
                        if (s.Index >= ((IList)cur).Count) throw Err(CommandCodes.NotFound, p.Text + ": index out of range");
                        next = s.Order((IList)cur)[s.Index];
                        break;
                    case StepKind.HairPart: next = ((HairData)cur).Parts[s.Index]; break;
                    case StepKind.ClothesPart: next = ((ClothesSet)cur).Parts[s.Index]; break;
                    case StepKind.AccessoryById:
                    {
                        var l = (IList)cur;
                        int ai = AccessoryIndex(l, s.Key);
                        if (ai < 0) throw Err(CommandCodes.NotFound, p.Text + ": no accessory with id " + s.Key);
                        next = l[ai];
                        break;
                    }
                    default: throw Err(CommandCodes.BadArgs, p.Text + ": bad path");
                }
                if (next == null) throw Err(CommandCodes.NotFound, p.Text + ": passes through a null object; set the whole object first");
                cur = next;
            }
            throw Err(CommandCodes.BadArgs, p.Text + ": empty path");
        }
    }
}
