// FK state of one built scene character: the discovered bone table, the three
// stages on its CharaMotion (FkHipsStage and FkNeckGate at IkPrepare, registered in that order; FkStage at Fk), the
// applied groups and their side effects, suspend. Created by FkDriver.Apply at build and found again
// through CharaMotion.Get<FkStage>().Owner, so it lives and dies with the character's root.
using System;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Look;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class FkCharacter
    {
        public CharaRealization Realization { get; }
        public GameObject Root { get; }
        public FkBoneTable Table { get; }
        public FkStage Stage { get; }
        public FkNeckGate Gate { get; }
        public FkHipsStage Hips { get; }
        public bool Suspended { get; private set; }
        /// <summary>fk.enabled as last read.</summary>
        public bool Enabled { get; private set; }
        public float HipsOffset { get; private set; }
        /// <summary>fk.hipsPosition as last read (3.4), or null.</summary>
        public Vector3? HipsPosition { get; private set; }
        public readonly List<string> Log = new List<string>();

        readonly CharaMotion motion;
        readonly DynamicBoneStage dyn;
        readonly BustStage bust;
        readonly CharaLook look;

        internal FkCharacter(CharaRealization r, FkBoneTable table)
        {
            Realization = r;
            Root = r.Root;
            Table = table;
            motion = r.Motion;
            dyn = motion ? motion.Get<DynamicBoneStage>() : null;
            bust = motion ? motion.Get<BustStage>() : null;
            look = r.Look;
            Transform hips = null;
            if (r.Context?.BodyBones != null) r.Context.BodyBones.TryGetValue("cf_J_Hips", out hips);
            Hips = new FkHipsStage(hips);
            var ctx = r.Context;
            Hips.ExternalWrite = w => ModifierWrite(ctx?.Bones, Hips.Hips, w);
            Gate = new FkNeckGate(look);
            Stage = new FkStage(table) { Owner = this };
            if (motion)
            {
                motion.Add(Hips);    // IkPrepare, before the gate (equal phases keep registration order)
                motion.Add(Gate);
                motion.Add(Stage);   // Fk
            }
        }

        public bool Applied(FkGroup g) => Stage.Applied[(int)g];

        /// <summary>The value the card's bone modifiers write on <paramref name="t"/> from base <paramref name="w"/> (as
        /// BoneModifierApplier combines them: base · length + position, else base + position), or null without one.</summary>
        static Vector3? ModifierWrite(HS2Next.Chara.Ext.BoneModifierApplier m, Transform t, Vector3 w)
        {
            if (!m || !t) return null;
            float len = 1f; var pos = Vector3.zero;
            bool hasLen = false, hasPos = false;
            foreach (var x in m.Modifiers)
            {
                if (x == null || x.Target != t) continue;
                if (x.Length != 1) { len *= x.Length; hasLen = true; }
                if (x.Position.x != 0 || x.Position.y != 0 || x.Position.z != 0) { pos += x.Position; hasPos = true; }
            }
            if (hasLen) return w * len + pos;
            if (hasPos) return w + pos;
            return null;
        }

        /// <summary>The discovery input of a built character.</summary>
        internal static FkDiscovery DiscoveryOf(CharaRealization r)
        {
            var ctx = r.Context;
            var holders = new HashSet<Transform>();
            foreach (var h in r.Root.GetComponentsInChildren<SceneNodeHolder>(true)) holders.Add(h.transform);
            return new FkDiscovery
            {
                Root = r.Root.transform,
                BodyBones = ctx.BodyBones,
                HeadParent = ctx.HeadBone ? ctx.HeadBone.transform.parent : null,
                Female = !ctx.IsMale,
                Rows = FkRows.From(r.Runtime?.Services?.Db),
                SkirtRenderers = SkirtRenderers(ctx),
                Stop = holders.Count > 0 ? (Func<Transform, bool>)(t => holders.Contains(t)) : null,
            };
        }

        static List<Renderer> SkirtRenderers(HS2Next.Chara.CharaContext ctx)
        {
            var l = new List<Renderer>();
            if (ctx?.Clothes == null) return l;
            for (int k = 0; k < 2 && k < ctx.Clothes.Length; k++)
            {
                var go = ctx.Clothes[k];
                var cmp = go ? go.GetComponent<CmpClothes>() : null;
                if (cmp == null) continue;
                foreach (var g in new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 })
                    if (g != null) foreach (var rr in g) if (rr) l.Add(rr);
            }
            return l;
        }

        // ------------------------------------------------------------ model → state

        static CharaFkState FkOf(CharaRealization r) => (r.Node?.State as CharaNodeState)?.Fk;

        /// <summary>Re-reads the whole block (build, `chara.fk` replaced).</summary>
        internal void ReadAll(bool build)
        {
            var fk = FkOf(Realization);
            if (fk == null) return;
            ReadValues(fk);
            UpdateApplied(fk, build);
            UpdateHips(fk);
            WriteNow();
        }

        /// <summary>Re-reads every value (`chara.fk.bones` replaced); bones that lost their value while applied are de-applied alone.</summary>
        internal void ReadValues(CharaFkState fk)
        {
            foreach (var b in Table.Bones) ReadValue(b, fk);
        }

        /// <summary>Re-reads one bone; returns true when it changed.</summary>
        internal bool ReadValue(FkBone b, CharaFkState fk)
        {
            bool had = b.HasValue;
            var old = b.Value;
            if (fk.Bones.TryGetValue(b.Key, out var v) || (b.PartnerKey != null && fk.Bones.TryGetValue(b.PartnerKey, out v)))
            {
                var nv = new Vector3(v.X, v.Y, v.Z);
                if (had && Same(nv, old)) return false;
                b.Set(nv);
                return true;
            }
            if (!had) return false;
            b.Clear();
            // removing a key while its group is applied de-applies that bone alone
            if (!Suspended && Stage.Applied[(int)b.Group] && !FkOptions.MissingBonesZero && (FkGroupInfo.ResetsOnDeapply(b.Group) || b.Additional))
                b.Reset(FkOptions.ResetToIdentity);
            return true;
        }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        static bool Flag(FkGroups g, int i)
        {
            switch (i)
            {
                case 0: return g.Hair;
                case 1: return g.Neck;
                case 2: return g.Breast;
                case 3: return g.Body;
                case 4: return g.RightHand;
                case 5: return g.LeftHand;
                default: return g.Skirt;
            }
        }

        /// <summary>Applied(g) = fk.enabled AND fk.groups.g; side effects of every change at once.</summary>
        internal void UpdateApplied(CharaFkState fk, bool build)
        {
            Enabled = fk.Enabled;
            bool any = false;
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                bool now = fk.Enabled && fk.Groups != null && Flag(fk.Groups, g);
                any |= now;
                if (now != Stage.Applied[g])
                {
                    Stage.Applied[g] = now;
                    if (now) OnApply((FkGroup)g); else OnDeapply((FkGroup)g);
                }
                else if (build && !now && FkOptions.ResetToIdentity)
                {
                    // vanilla initialisation of a new character: every group de-applied (Hair, Body, Skirt → identity)
                    foreach (var b in Table.Of((FkGroup)g))
                        if (FkGroupInfo.ResetsOnDeapply(b.Group) || b.Additional) b.Reset(true);
                }
            }
            Stage.Enabled = any;
            UpdateHips(fk);
        }

        /// <summary>
        /// The hips stage from fk.hipsOffset and fk.hipsPosition (3.4): active while FK and
        /// its Body group are on and either a hips position is set or the offset applies (FkOptions.HipsOffset, ≠ 0); the
        /// written value is (hipsPosition or the clip's value) + (0, offset, 0).
        /// </summary>
        internal void UpdateHips(CharaFkState fk)
        {
            HipsOffset = fk.HipsOffset;
            Vector3? pos = fk.HipsPosition.HasValue ? new Vector3(fk.HipsPosition.Value.X, fk.HipsPosition.Value.Y, fk.HipsPosition.Value.Z) : (Vector3?)null;
            HipsPosition = pos;
            bool offset = FkOptions.HipsOffset && fk.HipsOffset != 0f;
            bool active = fk.Enabled && fk.Groups != null && fk.Groups.Body && (pos.HasValue || offset);
            Hips.SetActive(active, FkOptions.HipsOffset ? fk.HipsOffset : 0f, pos);
        }

        void OnApply(FkGroup g)
        {
            switch (g)
            {
                case FkGroup.Hair:
                    if (dyn != null) { dyn.SetHairFk(true); dyn.ApplySwitches(); }   // chains restore their start pose before FK writes
                    break;
                case FkGroup.Neck:
                    Gate.Active = true;
                    if (!Suspended) Gate.Hold();
                    break;
                case FkGroup.Breast:
                    if (bust != null) { bust.FkBreastActive = true; bust.ApplySwitches(); }
                    break;
                case FkGroup.Skirt:
                    if (dyn != null) { dyn.SetSkirtFk(true); dyn.ApplySwitches(); }
                    break;
            }
        }

        void OnDeapply(FkGroup g)
        {
            if (!Suspended)
                foreach (var b in Table.Of(g))
                    if (FkGroupInfo.ResetsOnDeapply(g) || b.Additional) b.Reset(FkOptions.ResetToIdentity);
            switch (g)
            {
                case FkGroup.Hair:
                    if (dyn != null) { dyn.SetHairFk(false); dyn.ApplySwitches(); }
                    break;
                case FkGroup.Neck:
                    Gate.Active = false;
                    var st = Realization.Node?.State as CharaNodeState;
                    if (look != null && st?.Look != null) look.SetNeckPattern(st.Look.Neck);
                    break;
                case FkGroup.Breast:
                    if (bust != null) { bust.FkBreastActive = false; bust.ApplySwitches(); }
                    break;
                case FkGroup.Skirt:
                    if (dyn != null) { dyn.SetSkirtFk(false); dyn.ApplySwitches(); }
                    break;
            }
        }

        /// <summary>Writes the applied bones once and applies the gate and the hips offset (outside a step).</summary>
        public void WriteNow()
        {
            if (Suspended) return;
            Stage.WriteApplied();
            if (Gate.Active) Gate.Hold();
            Hips.ApplyNow();
        }

        // ------------------------------------------------------------ change handling

        internal void OnChanged(string path)
        {
            var fk = FkOf(Realization);
            if (fk == null) return;
            if (path.StartsWith("chara.fk.bones[", StringComparison.Ordinal))
            {
                string key = KeyOf(path);
                var b = key != null ? Table.FindAny(key) : null;
                if (b != null && ReadValue(b, fk)) Stage.WriteIfApplied(b);
                return;
            }
            if (path == "chara.fk.bones")
            {
                ReadValues(fk);
                if (!Suspended) Stage.WriteApplied();
                return;
            }
            if (path == "chara.fk.enabled" || path.StartsWith("chara.fk.groups", StringComparison.Ordinal))
            {
                UpdateApplied(fk, false);
                WriteNow();
                return;
            }
            if (path == "chara.fk.hipsOffset" || path == "chara.fk.hipsPosition")
            {
                UpdateHips(fk);
                if (!Suspended) Hips.ApplyNow();
                return;
            }
            // chara.fk (replaced) or a shorter overlapping path
            ReadAll(false);
        }

        static string KeyOf(string path)
        {
            if (!FieldPath.TryParse(path, out var p, out _) || p.Count != 4) return null;
            var s = p[3];
            return s.Kind == FieldStepKind.Key ? s.Name : null;
        }

        // ------------------------------------------------------------ suspend

        public IDisposable Suspend()
        {
            if (Suspended) return null;
            Suspended = true;
            Stage.Suspended = true;
            Gate.Suspended = true;
            Hips.SetSuspended(true);
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                if (!Stage.Applied[g]) continue;
                foreach (var b in Table.Of((FkGroup)g))
                    if (FkGroupInfo.ResetsOnDeapply(b.Group) || b.Additional) b.Reset(false);
            }
            return new Resume(this);
        }

        sealed class Resume : IDisposable
        {
            FkCharacter c;
            public Resume(FkCharacter c) { this.c = c; }
            public void Dispose()
            {
                var x = c;
                c = null;
                if (x == null || !x.Root) return;
                x.Suspended = false;
                x.Stage.Suspended = false;
                x.Gate.Suspended = false;
                x.Hips.SetSuspended(false);
                x.Stage.WriteApplied();
            }
        }

        // ------------------------------------------------------------ additional nodes

        /// <summary>Joins FK node rows (the build hooks' providers, HS2Next.Chara.Ext.CharaBuildHooks.FkNodes);
        /// returns the number of nodes found on the character. Null: nothing.</summary>
        public int JoinNodes(HS2Next.Chara.Ext.FkNodeSet nodes)
        {
            if (nodes == null || Table.NodesJoined) return 0;
            int before = Table.Additional.Count;
            Table.JoinNodes(nodes.Rows, Log);
            var fk = FkOf(Realization);
            for (int i = before; i < Table.Additional.Count; i++)
            {
                var b = Table.Additional[i];
                if (fk != null) ReadValue(b, fk);
                Stage.WriteIfApplied(b);
            }
            foreach (var f in nodes.Findings) Realization.Runtime?.ReportOnce(f.Severity, f.Code, Realization.NodeId, f.Message);
            return Table.Additional.Count - before;
        }

        public override string ToString() =>
            $"FK {(Enabled ? "on" : "off")} applied [{string.Join(",", Enumerable.Range(0, FkGroupInfo.Count).Where(g => Stage.Applied[g]).Select(g => ((FkGroup)g).ToString()))}] {Table.Bones.Count} bones{(Suspended ? " (suspended)" : "")}";
    }
}
