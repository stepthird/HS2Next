// The test plugin of the generated item rows (CC0-1.0). Load adds item group 731497 with three rows (1 and 2 in category 0, 3 in
// category 1; row 1 has the FK bone rp1), so a scene loaded before the plugin has its items built again once the plugin is there.
// Each row's object is a small mesh with a chain rp1 / rp2 under its root. rowprobe.mode makes the function build (ok), throw or
// return null; rowprobe.rename {row, name} changes a row's name, rowprobe.refresh {row} builds a row's items again,
// rowprobe.dispose removes the group and rowprobe.add puts it back; rowprobe.state reports the counts, kept across a disable and
// an enable.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using UnityEngine;

namespace Stp3.Test.RowProbe
{
    public sealed class RowProbePlugin : IPlugin
    {
        public const int Group = 731497;

        // static, so that a disable and an enable keep them
        static int makes, made, loads, unloads;
        static string mode = "ok", lastNode;
        static readonly List<string> calls = new List<string>();

        IPluginContext ctx;
        IItemRowGroup group;
        readonly Dictionary<int, string> names = new Dictionary<int, string> { [1] = "Probe one", [2] = "Probe two", [3] = "Probe three" };
        string addError;

        public void Load(IPluginContext context)
        {
            ctx = context;
            loads++;
            AddGroup();
            context.Commands.Register(new CommandSpec("rowprobe.state", "The counts of the row functions.").WithFlags(CommandFlags.ReadOnly), _ => new Dictionary<string, object>
            {
                ["unity"] = ctx.Unity() != null, ["group"] = group != null ? group.Group : 0, ["makes"] = makes, ["made"] = made, ["loads"] = loads, ["unloads"] = unloads,
                ["mode"] = mode, ["lastNode"] = lastNode, ["calls"] = calls.ToArray(), ["addError"] = addError,
            });
            context.Commands.Register(new CommandSpec("rowprobe.mode", "How the row function answers.")
                .Arg("mode", ArgTypes.String, "ok, throw or null", required: true), c =>
            {
                mode = c.Args.Require<string>("mode");
                return new Dictionary<string, object> { ["mode"] = mode };
            });
            context.Commands.Register(new CommandSpec("rowprobe.rename", "Renames a row (the rows are set again).")
                .Arg("row", ArgTypes.Int, "the row's id", required: true).Arg("name", ArgTypes.String, "the new name", required: true), c =>
            {
                names[c.Args.Require<int>("row")] = c.Args.Require<string>("name");
                group.SetRows(Rows());
                return new Dictionary<string, object> { ["rows"] = names.Count };
            });
            context.Commands.Register(new CommandSpec("rowprobe.refresh", "Builds a row's items again.")
                .Arg("row", ArgTypes.Int, "the row's id", required: true), c =>
            {
                group.Refresh(c.Args.Require<int>("row"));
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("rowprobe.dispose", "Removes the group."), _ =>
            {
                group?.Dispose();
                group = null;
                return new Dictionary<string, object>();
            });
            context.Commands.Register(new CommandSpec("rowprobe.add", "Adds the group again."), _ =>
            {
                AddGroup();
                return new Dictionary<string, object> { ["group"] = group != null ? group.Group : 0, ["error"] = addError };
            });
            context.Commands.Register(new CommandSpec("rowprobe.clear", "Forgets the counts."), _ =>
            {
                makes = made = 0;
                calls.Clear();
                mode = "ok";
                return new Dictionary<string, object>();
            });
        }

        void AddGroup()
        {
            addError = null;
            try
            {
                group = ctx.Unity().ItemRows.AddGroup(Group, "Row probe", Make);
                group.SetCategory(0, "Probe rows");
                group.SetCategory(1, "More probe rows");
                group.SetRows(Rows());
            }
            catch (Exception e)
            {
                group = null;
                addError = e.GetType().Name + ": " + e.Message;
                ctx.Log.Warning("rowprobe: " + addError);
            }
        }

        List<ItemRow> Rows()
        {
            var l = new List<ItemRow>();
            foreach (var kv in names)
                l.Add(new ItemRow { Category = kv.Key == 3 ? 1 : 0, Id = kv.Key, Name = kv.Value, FkBones = kv.Key == 1 ? new[] { "rp1" } : null });
            return l;
        }

        GameObject Make(ItemRowRequest r)
        {
            makes++;
            lastNode = r.NodeId;
            calls.Add(r.Guid + "#" + r.Id + " " + r.Group + "/" + r.Category + " " + mode);
            if (mode == "throw") throw new InvalidOperationException("thrown by the row probe");
            if (mode == "null") return null;
            made++;
            var go = new GameObject("rowprobe_" + r.Id);
            var mesh = new Mesh { name = "rowprobe_mesh" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shader = Shader.Find("HS2Next/Standard");
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material(shader) { name = "rowprobe_mat" };
            var b1 = new GameObject("rp1").transform;
            b1.SetParent(go.transform, false);
            var b2 = new GameObject("rp2").transform;
            b2.SetParent(b1, false);
            b2.localPosition = new Vector3(0, 1, 0);
            return go;
        }

        public void Unload() => unloads++;
    }
}
