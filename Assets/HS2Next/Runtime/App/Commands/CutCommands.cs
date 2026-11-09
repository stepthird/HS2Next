// The commands of the Cutter (group "cut", owner core, Since 1.3.0, experimental): markers, cuts of characters and items into pieces, edits of a piece's cut.
//   cut.marker, cut.make, cut.set, cut.status
// Every cut cuts a bake: a character or a plain item is baked as drawn first (that needs a graphics device), a bake item is cut at its FK pose, a piece is cut
// again from its own source with the new step added. A cut adds its pieces and hides the target in one history entry; an edit of a piece's cut is one entry.
// A shape is given by a marker (an item of the app's cut tools) or by values in world space. A node of the wrong kind answers bad_args; over a limit bad_args
// with the limit's name in the text; a step that cannot run bad_args with cut.badStep.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class CutCommands
    {
        /// <summary>The commands of this file, in the order of the spec's table.</summary>
        public static readonly string[] Names = { "cut.marker", "cut.make", "cut.set", "cut.status" };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since68 = "1.3.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "cut";

        const string ShapeDoc = "{kind, pos, rot, size} in world space, in place of marker: kind plane, box, tear or hole; rot [x, y, z] in degrees or [x, y, z, w]; size the full extent per axis in units, absent: the shape's own";
        const string ModeDoc = "plane both, front or back; box both, inside or outside; tear and hole open. Absent: both, or open";
        const string RaggedDoc = "{amp, tooth, random, seed}: a ragged edge, in units (1 = 10 cm): amp 0-0.5, tooth 0.05-2 (absent 0.2), random 0-1 (absent 0.5), seed 1-9999 (absent 1); amp 0 or absent: a clean cut";
        const string SectionDoc = "{color, ownInside}: the cut faces' colour, [r, g, b] or darkRed, flesh, bone, black, grey (absent dark red), and whether garments and hair show their own fabric inside (absent true)";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since68).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("cut.marker", "Adds a marker of a cut, at the centre of a target's drawn objects or at the camera's pivot, in one history entry, and selects it. Markers are drawn in the view and left out of captures.", CommandFlags.Mutates)
                    .Arg("kind", ArgTypes.Enum(CutShapeInfo.Plane, CutShapeInfo.Box, CutShapeInfo.Tear, CutShapeInfo.Hole), "the shape", true)
                    .Arg("target", ArgTypes.Node, "a node the marker is put on")
                    .Return("key", ArgTypes.String, "the marker's key")
                    .Example("{\"cmd\":\"cut.marker\",\"kind\":\"plane\",\"target\":\"<id>\"}"),
                Marker);
            CommandKit.Register(r, app, Spec("cut.make", "Cuts a character or an item by a marker or a shape into pieces, in one history entry; the target is hidden, not deleted. A character or a plain item is baked as drawn first (needs a graphics device); a bake item is cut at its FK pose; a piece is cut again from its own source.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "the target: a character or an item", true)
                    .Arg("marker", ArgTypes.Node, "a marker")
                    .Arg("shape", ArgTypes.Object, ShapeDoc)
                    .Arg("mode", ArgTypes.String, ModeDoc)
                    .Arg("ragged", ArgTypes.Object, RaggedDoc)
                    .Arg("clothesOnly", ArgTypes.Bool, "cut garments only; every other part goes whole to the side its centre is on", false, false)
                    .Arg("section", ArgTypes.Object, SectionDoc)
                    .Arg("hide", ArgTypes.Bool, "hide the target", false, true)
                    .Arg("name", ArgTypes.String, "the pieces' name, with the side when there are two; empty: the target, the side and the time", false, "")
                    .Return("pieces", ArgTypes.ArrayOf(ArgTypes.String), "the pieces' keys")
                    .Return("ms", ArgTypes.Float, "milliseconds the cut took")
                    .Example("{\"cmd\":\"cut.make\",\"key\":\"<id>\",\"marker\":\"<marker>\",\"mode\":\"both\",\"ragged\":{\"amp\":0.1}}"),
                Make);
            CommandKit.Register(r, app, Spec("cut.set", "Changes a piece's cut and computes the piece again from its source, in one history entry: the last step's shape (by a marker or values), mode, ragged edge and clothes only, and the section. recut computes it again with no change.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a piece", true)
                    .Arg("marker", ArgTypes.Node, "a marker the last step takes")
                    .Arg("shape", ArgTypes.Object, ShapeDoc)
                    .Arg("mode", ArgTypes.String, ModeDoc + "; a piece of both keeps its own side")
                    .Arg("ragged", ArgTypes.Any, RaggedDoc + "; null: a clean cut")
                    .Arg("clothesOnly", ArgTypes.Bool, "cut garments only")
                    .Arg("section", ArgTypes.Object, SectionDoc)
                    .Arg("recut", ArgTypes.Bool, "compute the piece again from its source with no change", false, false)
                    .Return("changed", ArgTypes.Bool, "the piece's cut or its result changed")
                    .Return("ms", ArgTypes.Float, "milliseconds it took")
                    .Example("{\"cmd\":\"cut.set\",\"key\":\"<piece>\",\"section\":{\"color\":\"bone\"}}"),
                Set);
            CommandKit.Register(r, app, Spec("cut.status", "A piece's cut as stored.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a piece", true)
                    .Return("source", ArgTypes.Object, "{bake, main, data}: the bake that was cut")
                    .Return("pose", ArgTypes.Object, "{bone: [x, y, z]}: the source's FK values at the cut, or null for its baked pose")
                    .Return("steps", ArgTypes.ArrayOf(ArgTypes.Object), "{shape, pos, rot, size, keep, ragged, clothesOnly, pose} in the source's root space, from the first source; pose: the source's FK values that step was decided at, null when it takes the previous step's (the first step the block's pose)")
                    .Return("section", ArgTypes.Object, "{color, ownInside}")
                    .Return("recomputed", ArgTypes.Bool, "the stored result was missing and was computed again when the piece was built")
                    .Example("{\"cmd\":\"cut.status\",\"key\":\"<piece>\"}"),
                Status);
        }

        // ------------------------------------------------------------ arguments

        static float Num(JToken t, string arg)
        {
            if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw CommandKit.BadArgs(arg, ArgTypes.Float);
            return CommandKit.Finite((float)(double)t, arg);
        }

        static JObject Obj(CoreContext c, string arg, string expected)
        {
            var t = WetCommands.Raw(c, arg);
            if (t == null) return null;
            if (!(t is JObject o)) throw CommandKit.BadArgs(arg, expected);
            return o;
        }

        static CutShapeGiven ShapeOf(CoreContext c)
        {
            var o = Obj(c, "shape", "{kind, pos, rot, size}");
            if (o == null) return null;
            foreach (var m in o.Properties())
                if (m.Name != "kind" && m.Name != "pos" && m.Name != "rot" && m.Name != "size") WetCommands.UnknownMember(c, "shape", m.Name, "kind, pos, rot or size");
            if (o["kind"]?.Type != JTokenType.String) throw new BridgeException(BridgeError.Missing("shape.kind", ArgTypes.String));
            var s = new CutShapeGiven { Kind = (string)o["kind"] };
            var pos = AdvancedPoseCommands.JsonVec3(o["pos"], "shape.pos");
            if (pos != null) s.Pos = WetCommands.V(pos.Value);
            var rot = o["rot"];
            if (rot != null && rot.Type != JTokenType.Null)
            {
                if (!(rot is JArray a) || (a.Count != 3 && a.Count != 4)) throw CommandKit.BadArgs("shape.rot", "[x, y, z] in degrees or [x, y, z, w]");
                if (a.Count == 3) s.Rot = Quaternion.Euler(Num(a[0], "shape.rot[0]"), Num(a[1], "shape.rot[1]"), Num(a[2], "shape.rot[2]"));
                else
                {
                    var q = new Quaternion(Num(a[0], "shape.rot[0]"), Num(a[1], "shape.rot[1]"), Num(a[2], "shape.rot[2]"), Num(a[3], "shape.rot[3]"));
                    float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                    if (!(len > 1e-6f)) throw CommandKit.BadArgs("shape.rot", "a rotation", "shape.rot has no length");
                    s.Rot = new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
                }
            }
            var size = AdvancedPoseCommands.JsonVec3(o["size"], "shape.size");
            if (size != null)
            {
                var v = WetCommands.V(size.Value);
                if (!(v.x > 0f) || !(v.y >= 0f) || !(v.z > 0f)) throw CommandKit.BadArgs("shape.size", "sizes above 0", "shape.size must be above 0 (y may be 0 for a plane)");
                s.Size = v;
            }
            return s;
        }

        /// <summary>A ragged edge; null when absent. <paramref name="cleared"/> is true for a JSON null (a clean cut).</summary>
        static CutRagParams RaggedOf(CoreContext c, out bool cleared)
        {
            cleared = false;
            var k = c.Args.Kind("ragged");
            if (k == ArgKind.Missing) return null;
            if (k == ArgKind.Null) { cleared = true; return null; }
            var o = Obj(c, "ragged", "{amp, tooth, random, seed}");
            var g = new CutRagParams();
            foreach (var m in o.Properties())
            {
                switch (m.Name)
                {
                    case "amp": g.Amp = Num(m.Value, "ragged.amp"); break;
                    case "tooth": g.Tooth = Num(m.Value, "ragged.tooth"); break;
                    case "random": g.Random = Num(m.Value, "ragged.random"); break;
                    case "seed":
                        if (m.Value.Type != JTokenType.Integer) throw CommandKit.BadArgs("ragged.seed", ArgTypes.Int);
                        g.Seed = (int)m.Value;
                        break;
                    default: WetCommands.UnknownMember(c, "ragged", m.Name, "amp, tooth, random or seed"); break;
                }
            }
            g.Applied(out bool outside);
            if (outside) c.Warn(BakeFindings.CutRange + ": a ragged value is out of its range; it is kept and clamped where it acts");
            if (g.Amp <= 0) cleared = true;
            return g;
        }

        static CutSection SectionOf(CoreContext c)
        {
            var o = Obj(c, "section", "{color, ownInside}");
            if (o == null) return null;
            var s = new CutSection();
            foreach (var m in o.Properties())
            {
                switch (m.Name)
                {
                    case "color":
                        if (m.Value.Type == JTokenType.String)
                        {
                            var named = CutSectionColors.Of((string)m.Value);
                            if (named == null) throw new BridgeException(BridgeCodes.BadArgs, "unknown colour \"" + (string)m.Value + "\"",
                                new BridgeErrorDetail { Arg = "section.color", Expected = "[r, g, b] or a preset", Candidates = new List<string>(CutSectionColors.Names) });
                            s.Color = named.Value;
                        }
                        else
                        {
                            var v = AdvancedPoseCommands.JsonVec3(m.Value, "section.color");
                            if (v == null) throw CommandKit.BadArgs("section.color", "[r, g, b] or a preset");
                            s.Color = v.Value;
                        }
                        break;
                    case "ownInside":
                        if (m.Value.Type != JTokenType.Boolean) throw CommandKit.BadArgs("section.ownInside", ArgTypes.Bool);
                        s.OwnInside = (bool)m.Value;
                        break;
                    default: WetCommands.UnknownMember(c, "section", m.Name, "color or ownInside"); break;
                }
            }
            return s;
        }

        static string MarkerOf(CoreContext c) => CommandKit.OptionalNode(c, "marker")?.Id;

        // ------------------------------------------------------------ handlers

        static object Marker(CoreContext c)
        {
            string kind = CommandKit.OptString(c, "kind");
            if (kind == null) throw new BridgeException(BridgeError.Missing("kind", ArgTypes.String));
            var target = CommandKit.OptionalNode(c, "target");
            var r = CommandKit.Check(c, CommandKit.Ops(c).CutMarker(kind, target?.Id, CommandKit.Exec(c, StudioOps.Labels.AddItem)));
            return new BridgeReply().With("key", r.Value as string);
        }

        static object Make(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var req = new CutMakeRequest
            {
                Target = n.Id,
                Marker = MarkerOf(c),
                Shape = ShapeOf(c),
                Mode = CommandKit.OptString(c, "mode"),
                ClothesOnly = CommandKit.OptBool(c, "clothesOnly") ?? false,
                Section = SectionOf(c),
                Hide = CommandKit.OptBool(c, "hide") ?? true,
                Name = CommandKit.OptString(c, "name"),
            };
            var g = RaggedOf(c, out bool clean);
            req.Ragged = clean ? null : g;
            var r = CommandKit.Check(c, CommandKit.Ops(c).CutMake(req, CommandKit.Exec(c, StudioOps.Labels.CutMake)));
            var made = (CutMakeResult)r.Value;
            return new BridgeReply().With("pieces", new List<string>(made.Pieces)).With("ms", Math.Round(made.Ms, 1));
        }

        static object Set(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var req = new CutSetRequest
            {
                Marker = MarkerOf(c),
                Shape = ShapeOf(c),
                Mode = CommandKit.OptString(c, "mode"),
                ClothesOnly = CommandKit.OptBool(c, "clothesOnly"),
                Section = SectionOf(c),
                Recut = CommandKit.OptBool(c, "recut") ?? false,
            };
            var g = RaggedOf(c, out bool clean);
            if (clean && c.Args.Kind("ragged") != ArgKind.Missing) req.ClearRagged = true;
            else req.Ragged = g;
            var r = CommandKit.Check(c, CommandKit.Ops(c).CutSet(n.Id, req, CommandKit.Exec(c, StudioOps.Labels.CutEdit)));
            var res = (CutSetResult)r.Value;
            return new BridgeReply().With("changed", res.Changed).With("ms", Math.Round(res.Ms, 1));
        }

        internal static object StepRow(CutStep s)
        {
            return new BridgeReply
            {
                ["shape"] = s.Shape,
                ["pos"] = CommandKit.D3(s.Pos),
                ["rot"] = new double[] { s.Rot.X, s.Rot.Y, s.Rot.Z, s.Rot.W },
                ["size"] = CommandKit.D3(s.Size),
                ["keep"] = s.Keep,
                ["ragged"] = s.Ragged == null ? null : new BridgeReply { ["amp"] = (double)s.Ragged.Amp, ["tooth"] = (double)s.Ragged.Tooth, ["random"] = (double)s.Ragged.Random, ["seed"] = s.Ragged.Seed },
                ["clothesOnly"] = s.ClothesOnly,
                ["pose"] = PoseRow(s.Pose),
            };
        }

        /// <summary>A step's own FK pose of the source ({bone: [x, y, z]}), or null when the step has none.</summary>
        static BridgeReply PoseRow(IDictionary<string, Vec3> pose)
        {
            if (pose == null) return null;
            var r = new BridgeReply();
            foreach (var kv in pose) r[kv.Key] = CommandKit.D3(kv.Value);
            return r;
        }

        static object Status(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var r = CommandKit.Check(c, CommandKit.Ops(c).CutStatus(n.Id));
            var s = (CutStatusInfo)r.Value;
            BridgeReply pose = null;
            if (s.Pose != null)
            {
                pose = new BridgeReply();
                foreach (var kv in s.Pose) pose[kv.Key] = CommandKit.D3(kv.Value);
            }
            var steps = new List<object>();
            foreach (var st in s.Steps) steps.Add(StepRow(st));
            return new BridgeReply
            {
                ["source"] = s.Source == null ? null : new BridgeReply { ["bake"] = s.Source.Id, ["main"] = s.Source.Main, ["data"] = new List<string>(s.Source.Data) },
                ["pose"] = pose,
                ["steps"] = steps,
                ["section"] = s.Section == null ? null : new BridgeReply { ["color"] = CommandKit.D3(s.Section.Color), ["ownInside"] = s.Section.OwnInside },
                ["recomputed"] = s.Recomputed,
            };
        }
    }
}
