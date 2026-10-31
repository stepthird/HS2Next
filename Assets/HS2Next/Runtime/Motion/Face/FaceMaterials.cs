// Material-driven expression values: tears `_NamidaScale` on the
// `o_namida` material, cheek flush `_Texture4Scale` on the face draw material (`o_head` slot 0), eye
// highlight `_Smoothness` (0 hidden / 1 shown) on the `o_eyebase_L` / `o_eyebase_R` materials.
//
// Only per-character material instances are written: the face draw material and the eye materials are
// already instances made by CharaAssembler.BuildHead, and the assembler copies every other material of the
// character after its extension stage (CharaContext.OwnedMaterials); an `o_namida` material that is not
// owned (straight from the head bundle, shared by every character using that head) is copied once here. The material
// is looked up on the renderer at every write, so a later material swap (ShaderMap.ApplyUrp copies
// all materials) is followed; Refresh() rewrites all three values after such a swap. Never
// renderer.material (it logs errors and leaks in edit mode).
using UnityEngine;

namespace HS2Next.Motion.Face
{
    public sealed class FaceMaterials
    {
        public const string TearsProperty = "_NamidaScale", CheekProperty = "_Texture4Scale", HighlightProperty = "_Smoothness";

        readonly Renderer tears, face;
        readonly Renderer[] eyes;
        readonly Material faceFallback;

        /// <summary>Current values (status `tearsRate`, `hohoAkaRate`, `hideEyesHighlight`).</summary>
        public float Tears { get; private set; }
        public float Cheek { get; private set; }
        public bool HighlightHidden { get; private set; }

        /// <param name="owned">The character's own materials (CharaContext.OwnedMaterials); a copy made here is added.</param>
        public FaceMaterials(GameObject head, Material faceDraw, System.Collections.Generic.ISet<Material> owned = null)
        {
            faceFallback = faceDraw;
            if (!head) { eyes = new Renderer[0]; return; }
            tears = Find(head.transform, "o_namida")?.GetComponent<Renderer>();
            face = Find(head.transform, "o_head")?.GetComponent<Renderer>();
            var l = Find(head.transform, "o_eyebase_L")?.GetComponent<Renderer>();
            var r = Find(head.transform, "o_eyebase_R")?.GetComponent<Renderer>();
            eyes = new[] { l, r };
            if (tears)
            {
                var mats = tears.sharedMaterials;
                if (mats.Length > 0 && mats[0] && (owned == null || !owned.Contains(mats[0])))
                {
                    mats[0] = MaterialCopies.Copy(mats[0]);   // keeps the name and the loaded keywords
                    owned?.Add(mats[0]);
                    tears.sharedMaterials = mats;
                }
            }
        }

        /// <summary>The materials currently written (for tests and tools).</summary>
        public Material TearsMaterial => tears ? tears.sharedMaterial : null;
        public Material FaceMaterial => face && face.sharedMaterial ? face.sharedMaterial : faceFallback;
        public Material EyeMaterial(int i) => i >= 0 && i < eyes.Length && eyes[i] ? eyes[i].sharedMaterial : null;
        public bool HasTears => tears;
        public int EyeCount => (eyes.Length > 0 && eyes[0] ? 1 : 0) + (eyes.Length > 1 && eyes[1] ? 1 : 0);

        /// <summary>`ChangeTearsRate`: clamped to 0..1.</summary>
        public void SetTears(float v)
        {
            Tears = Mathf.Clamp01(v);
            var m = TearsMaterial;
            if (m) m.SetFloat(TearsProperty, Tears);
        }

        /// <summary>`ChangeHohoAkaRate`: clamped to 0..1.</summary>
        public void SetCheek(float v)
        {
            Cheek = Mathf.Clamp01(v);
            var m = FaceMaterial;
            if (m) m.SetFloat(CheekProperty, Cheek);
        }

        /// <summary>`HideEyeHighlight`: `_Smoothness` 0 when hidden, 1 when shown.</summary>
        public void SetHighlightHidden(bool hide)
        {
            HighlightHidden = hide;
            for (int i = 0; i < eyes.Length; i++)
            {
                var m = EyeMaterial(i);
                if (m) m.SetFloat(HighlightProperty, hide ? 0f : 1f);
            }
        }

        /// <summary>Rewrites the three values (after a material swap such as ShaderMap.ApplyUrp).</summary>
        public void Refresh()
        {
            SetTears(Tears); SetCheek(Cheek); SetHighlightHidden(HighlightHidden);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = Find(t.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }
    }
}
