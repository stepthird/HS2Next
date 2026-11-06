// MosaicTest, the rule of constraints (Phase 6.5 package O). Numbers only: no image is made, opened or written.
//   CT2  a list entry on a guarded transform, by the path that asks nothing (what a file gives): kept, not applied, state protected, finding once
//   CT3  a member arrives under a driven transform: the entry becomes protected at the resolve; it goes: active again. The transform stands over frames
//   CT4  a slot of any owner on a guarded transform: no write, not effective
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using static MakerTestKit;
using Object = UnityEngine.Object;

public static partial class MosaicTest
{
    static void ConstraintGroups()
    {
        var faceSeed = HS2Next.Motion.Face.FaceSetup.Seed;
        try
        {
            if (Want("CT2")) Guard("CT2 a list entry on a guarded transform", Ct2);
            if (Want("CT3")) Guard("CT3 a member arrives under a driven transform", Ct3);
            if (Want("CT4")) Guard("CT4 a slot on a guarded transform", Ct4);
            ConstraintCommandGroups();
        }
        finally { HS2Next.Motion.Face.FaceSetup.Seed = faceSeed; }
    }

    /// <summary>The commands' group (package C's file), when present.</summary>
    static partial void ConstraintCommandGroups();

    // the angle in degrees, in double: Quaternion.Angle answers 0 under about 0.16 degrees
    static double CtDeg(Quaternion a, Quaternion b) => ConstraintTest.Deg(a, b);

    sealed class CtFixture
    {
        public AmHost H;
        public string Key, Id, Source;
        public CharaRealization R;
        public Transform Top;
        public string Address(string name) => "acc:" + Id + "/" + name;
        public ConstraintPart Part => H.Rt.GetPart<ConstraintPart>();

        public void Put(params ConstraintEntry[] entries)
        {
            var r = H.Session.Commands.Execute(ConstraintEditCommand.ReplaceList(new ConstraintList(entries), ConstraintEditCommand.LabelAdd));
            Check("CT the list", r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
            H.Rt.ApplyNow();
        }

        public int Findings(string code) { int n = 0; foreach (var f in H.Rt.Findings) if (f.Code == code && f.NodeId == Key) n += Math.Max(1, f.Count); return n; }

        public void Frames(int n) { for (int i = 0; i < n; i++) H.App.Frame(1f / 60f); }
    }

    static CtFixture NewCt(AmHost h)
    {
        var f = new CtFixture { H = h };
        f.Key = AmLoad(h, male: false);
        h.Ok(new { cmd = "chara.motion", key = f.Key, autoStep = true }, "chara.motion back on its frames");
        f.R = h.Rt.FindChara(f.Key);
        var slot = f.R.Context.Accessories.FirstOrDefault(x => x != null && x.Object && f.R.AccessoryIdOf(x) != null);
        if (!Check("CT an accessory on the target", slot != null)) return null;
        f.Id = f.R.AccessoryIdOf(slot);
        f.Top = slot.Object.transform;
        var added = h.Ok(new { cmd = "item.add", no = 399 }, "CT a node as the source");
        f.Source = (string)added["key"] ?? (string)added["added"]?[0]?["key"];
        return f;
    }

    static ConstraintEntry CtEntry(CtFixture f, string bone, int n) => new ConstraintEntry
    {
        Id = (0xc7000000L + n).ToString("x32"),
        Source = new ConstraintEnd(f.Source),
        Target = new ConstraintEnd(f.Key, bone),
        Components = new List<string> { ConstraintComponents.Rotation },
    };

    static void Ct2()
    {
        using (var h = NewAmHost())
        {
            var f = NewCt(h);
            if (f == null) return;
            var part = AmChild(f.Top, "am_part");
            AmChild(part, "o_gomu", renderer: true);
            string address = f.Address("am_part");
            h.Session.Set(f.Source, "transform.rot", new Vec3(10f, 20f, 30f));
            var e = CtEntry(f, address, 1);
            f.Put(e);
            var row = f.Part.RowOf(e.Id);
            Check("CT2 state protected", row?.State == ConstraintStates.Protected, row?.State);
            Check("CT2 the entry is kept", h.Session.Model.Constraints?.Find(e.Id) != null);
            Check("CT2 not active, no pass for it alone", f.Part.Active == 0 && h.Rt.Pass == null, f.Part.Active.ToString());
            var q0 = part.localRotation;
            f.Frames(5);
            Check("CT2 the transform stands", CtDeg(part.localRotation, q0) == 0);
            Check("CT2 one finding", f.Findings(ConstraintFindings.MosaicProtected) == 1, f.Findings(ConstraintFindings.MosaicProtected).ToString());
            h.Rt.ApplyNow();
            f.Frames(3);
            Check("CT2 still one finding after more frames", f.Findings(ConstraintFindings.MosaicProtected) == 1, f.Findings(ConstraintFindings.MosaicProtected).ToString());
            Object.DestroyImmediate(part.gameObject);
        }
    }

    static void Ct3()
    {
        using (var h = NewAmHost())
        {
            var f = NewCt(h);
            if (f == null) return;
            var side = AmChild(f.Top, "am_side");
            string address = f.Address("am_side");
            h.Session.Set(f.Source, "transform.rot", new Vec3(10f, 20f, 30f));
            var q0 = side.localRotation;
            var e = CtEntry(f, address, 2);
            f.Put(e);
            Check("CT3 active first", f.Part.RowOf(e.Id)?.State == ConstraintStates.Active, f.Part.RowOf(e.Id)?.State);
            f.Frames(3);
            Check("CT3 the transform is driven", CtDeg(side.localRotation, q0) > 1f, CtDeg(side.localRotation, q0).ToString("G3"));
            var reresolved = typeof(SceneRuntime).GetMethod("NoteReresolved", BindingFlags.Instance | BindingFlags.NonPublic);
            var member = AmChild(side, "o_gomu", renderer: true);
            reresolved.Invoke(h.Rt, new object[] { f.R });
            h.Rt.ApplyNow();
            Check("CT3 a member under it: protected at the resolve", f.Part.RowOf(e.Id)?.State == ConstraintStates.Protected, f.Part.RowOf(e.Id)?.State);
            f.Frames(1);
            Check("CT3 the next step takes the value off", CtDeg(side.localRotation, q0) <= 1e-3f, CtDeg(side.localRotation, q0).ToString("G3"));
            f.Frames(30);
            Check("CT3 the transform stands over frames", CtDeg(side.localRotation, q0) <= 1e-3f);
            Object.DestroyImmediate(member.gameObject);
            reresolved.Invoke(h.Rt, new object[] { f.R });
            h.Rt.ApplyNow();
            Check("CT3 the member gone: active again", f.Part.RowOf(e.Id)?.State == ConstraintStates.Active, f.Part.RowOf(e.Id)?.State);
            f.Frames(3);
            Check("CT3 driven again", CtDeg(side.localRotation, q0) > 1f);
            Object.DestroyImmediate(side.gameObject);
        }
    }

    static void Ct4()
    {
        using (var h = NewAmHost())
        {
            var f = NewCt(h);
            if (f == null) return;
            var part = AmChild(f.Top, "am_part");
            AmChild(part, "o_gomu", renderer: true);
            var q0 = part.localRotation; var p0 = part.localPosition; var s0 = part.localScale;
            string address = f.Address("am_part");
            var ov = h.Rt.Overrides;
            var slots = new[]
            {
                ov.Acquire("test.owner", f.Key, OverridePath.BoneText(address, "rot"), 0),
                ov.Acquire(OverrideLayer.ConstraintsOwner, f.Key, OverridePath.BoneText(address, "pos"), OverrideLayer.ConstraintsPriority),
                ov.Acquire(OverrideLayer.TimelineOwner, f.Key, OverridePath.BoneText(address, "scale"), 0),
            };
            slots[0].Set(0.5, 0.5, 0.5, 0.5);
            slots[1].Set(1, 2, 3);
            slots[2].Set(2, 2, 2);
            f.Frames(5);
            Check("CT4 no write on any channel, by any owner", CtDeg(part.localRotation, q0) == 0 && part.localPosition.Equals(p0) && part.localScale.Equals(s0));
            Check("CT4 not effective", slots.All(s => !s.IsEffective));
            Check("CT4 winning all the same: the slot's owner sees it", slots.All(s => s.IsWinning));
            Check("CT4 no stage for it", !f.R.Motion.Stages.Any(s => s.Name.StartsWith("bones.driven", StringComparison.Ordinal)));
            foreach (var s in slots) s.Release();
            Object.DestroyImmediate(part.gameObject);
        }
    }
}
