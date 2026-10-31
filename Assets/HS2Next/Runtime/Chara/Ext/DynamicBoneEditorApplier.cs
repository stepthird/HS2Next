// DynamicBoneEditor: per (accessory slot, root bone name) overrides of
// accessory DynamicBone parameters. For each entry with CoordinateIndex 0, every DynamicBone under the slot object
// (slots ≥ 20 included) whose m_Root is named BoneName gets each non-nil field written:
// FreezeAxis → m_FreezeAxis, Weight → m_BlendWeight, Damping → m_Damping, Elasticity → m_Elasticity,
// Stiffness → m_Stiffness, Inertia → m_Inert, Radius → m_Radius. Fields only: there is no simulation yet, and the
// particle rebuild (SetupParticles/InitTransforms) is for the future physics step. Components on inactive objects are
// included (the plugin collects active ones a few frames after load; hidden slots would otherwise lose their edits).
using HS2Next.Data.Ext;

namespace HS2Next.Chara.Ext
{
    public static class DynamicBoneEditorApplier
    {
        public static void Apply(CharaContext ctx, DynamicBoneEditorData data)
        {
            if (data == null || data.Entries.Count == 0) return;
            var st = ctx.Stats;
            int noSlot = 0, noBone = 0;
            foreach (var e in data.Entries)
            {
                if (e.CoordinateIndex != 0) continue;
                st.DbeEntries++;
                var obj = ctx.AccessoryObject(e.Slot);
                if (!obj) { noSlot++; continue; }
                bool hit = false;
                foreach (var db in obj.GetComponentsInChildren<DynamicBone>(true))
                {
                    if (!db.m_Root || db.m_Root.name != e.BoneName) continue;
                    hit = true;
                    st.DbeComponents++;
                    if (e.FreezeAxis is int fa) { db.m_FreezeAxis = fa; st.DbeFields++; }
                    if (e.Weight is float w) { db.m_BlendWeight = w; st.DbeFields++; }
                    if (e.Damping is float d) { db.m_Damping = d; st.DbeFields++; }
                    if (e.Elasticity is float el) { db.m_Elasticity = el; st.DbeFields++; }
                    if (e.Stiffness is float s) { db.m_Stiffness = s; st.DbeFields++; }
                    if (e.Inertia is float i) { db.m_Inert = i; st.DbeFields++; }
                    if (e.Radius is float r) { db.m_Radius = r; st.DbeFields++; }
                }
                if (hit) { st.DbeEntriesMatched++; continue; }
                noBone++;
                var roots = new System.Collections.Generic.List<string>();
                foreach (var db in obj.GetComponentsInChildren<DynamicBone>(true))
                    if (db.m_Root && !roots.Contains(db.m_Root.name)) roots.Add(db.m_Root.name);
                ctx.Log.Add($"dbe: slot {e.Slot} ({obj.name}): no DynamicBone with m_Root {e.BoneName} (roots: {(roots.Count == 0 ? "none" : string.Join(", ", roots))})");
            }
            ctx.Log.Add($"dbe: {st.DbeEntriesMatched} of {st.DbeEntries} entries matched, {st.DbeComponents} DynamicBones, {st.DbeFields} fields written" +
                        (noSlot + noBone > 0 ? $" ({noSlot} without slot object, {noBone} without a matching m_Root)" : ""));
        }
    }
}
