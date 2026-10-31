// The face / body rule of plugin data on a partial character load (Phase 4.6; HeadNames): the vanilla head names (the head
// bone object's transforms, the vanilla heads' renderers and materials without the `_create` suffix) against body names; the
// head-skin material family `c[fm]_m_skin_head…`; skin overlays by target group; bone modifiers and MaterialEditor character
// items on names of neither set go with the body (the census's hair / accessory adjust bones, "New Material").
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class HeadNamesTests
    {
        public static void Run(TestRunner t)
        {
            var v = HeadNames.Vanilla.Of(1, "illusion.hs2#0");
            t.Equal(126, v.Count, "vanilla: 126 names (110 transforms, 16 materials)");
            t.Check(new[] { "cf_J_FaceBase", "cf_J_Mayu_L", "cf_J_pupil_s_R", "N_Earring_L", "p_cf_head_bone", "o_head", "o_eyebase_L", "o_tang", "c_m_eye", "cf_m_skin_head_00", "cm_m_skin_head_00" }.All(v.Contains),
                "vanilla: head bones, attach points, head renderers and materials are head names");
            t.Check(!new[] { "cf_J_Spine01", "cf_J_Neck", "cf_J_Hips", "o_body_cf", "o_mnpa", "cf_m_skin_body_00", "cf_m_skin_head_00_create", "neck_adjust" }.Any(v.Contains),
                "vanilla: body bones, body renderers, body materials and the _create names are not");
            t.Check(ReferenceEquals(HeadNames.Vanilla.Of(0, "x#1"), v) && ReferenceEquals(HeadNames.Current, MakerServices.HeadNames ?? HeadNames.Vanilla), "vanilla: one set for every head; Current = the registered names, else vanilla");
            t.Equal("cf_m_skin_head_01", HeadNames.StripCreate("cf_m_skin_head_01_create"), "StripCreate removes the suffix");
            t.Check(new HeadNames(new[] { "a_create", "b" }).Of(1, null).SequenceEqual(new[] { "a", "b" }), "a name set strips _create");

            t.Check(HeadNames.IsHeadSkinMaterial("cf_m_skin_head_02") && HeadNames.IsHeadSkinMaterial("cm_m_skin_head_00") && HeadNames.IsHeadSkinMaterial("cf_m_skin_head_00_create"),
                "head-skin family: cf / cm _m_skin_head… (a head not read, census cf_m_skin_head_02)");
            t.Check(!HeadNames.IsHeadSkinMaterial("cf_m_skin_body_00") && !HeadNames.IsHeadSkinMaterial("New Material") && !HeadNames.IsHeadSkinMaterial("c_m_skin_head") &&
                    !HeadNames.IsHeadSkinMaterial("cx_m_skin_head") && !HeadNames.IsHeadSkinMaterial(null), "head-skin family: not the body skin, not other names");

            foreach (var face in new[] { "FaceOver", "FaceUnder", "FaceDetailOver", "FaceDetailUnder", "EyeUnderL", "EyeUnderR", "EyeOverL", "EyeOverR", "EyebrowUnder", "EyelineUnder" })
                t.Check(HeadNames.IsFace(new SkinOverlay { Target = face }), "overlay " + face + " is of the face");
            foreach (var body in new[] { "BodyOver", "BodyUnder", "BodyDetailOver", "BodyDetailUnder", "FutureOverlayTarget" })
                t.Check(!HeadNames.IsFace(new SkinOverlay { Target = body }), "overlay " + body + " is of the body");
            t.Check(!HeadNames.IsFace((SkinOverlay)null), "a null overlay is not of the face");

            t.Check(HeadNames.IsFace(new BodyBoneMod { Bone = "cf_J_FaceBase" }, v) && HeadNames.IsFace(new BodyBoneMod { Bone = "cf_J_EarUp_L" }, v), "head-bone modifiers are of the face");
            t.Check(!HeadNames.IsFace(new BodyBoneMod { Bone = "cf_J_Spine02_s" }, v) && !HeadNames.IsFace(new BodyBoneMod { Bone = "neck_adjust" }, v) && !HeadNames.IsFace(new BodyBoneMod { Bone = "ct_hairB" }, v),
                "body bones and names of neither set (hair / accessory adjust bones) go with the body");

            t.Check(HeadNames.IsFace(new MeRenderer { Renderer = "o_head" }, v) && HeadNames.IsFace(new MeRenderer { Renderer = "o_tang" }, v), "an item on a head renderer is of the face (o_tang is in both sets: head)");
            t.Check(HeadNames.IsFace(new MeName { Renderer = "o_body_cf", Material = "cf_m_skin_head_00" }, v), "a name item with a head material is of the face");
            t.Check(HeadNames.IsFace(new MeFloat { Material = "cf_m_skin_head_02" }, v) && HeadNames.IsFace(new MeShader { Material = "c_m_eye_01" }, v), "material items of the head-skin family or a head material are of the face");
            t.Check(!HeadNames.IsFace(new MeRenderer { Renderer = "o_body_cf" }, v) && !HeadNames.IsFace(new MeFloat { Material = "cf_m_skin_body_00" }, v) &&
                    !HeadNames.IsFace(new MeFloat { Material = "New Material" }, v) && !HeadNames.IsFace(new MeProjector(), v), "body items, unknown materials and projector items go with the body");
        }
    }
}
