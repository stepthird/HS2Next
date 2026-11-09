// One character's stains on the built body: its runs, the slots that show them, the drops, the puddles and the floor. A rig exists while the node's block is not empty.
//   Sync      the block changed: the runs take the document's clock, the puddles its list, the maps are resolved and every slot is composed again
//   Resolve   each map against the built character: live (renderer by address, equal mesh signature), dormant (kept, acts when a matching part returns) or protected;
//             slots of renderers that went are released, a new part stays dry until the stain is applied again; no bake happens here
//   Tick      a running stain's clock moves by the scene clock's delta; a radius that changed recomposes that stain's slots; drops leave and fall in fixed steps
//   Bake      the maps of a stain on what is shown now, with the current pose; the picture is read back and stored once, at a commit
// Nothing here writes a material: drawing goes through WetCompose and WetFilm. The clock and the puddles are the runtime's; the capture writes them into the document.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Wet;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>A decoded or fresh map of a stain.</summary>
    public sealed class WetMapData
    {
        public RenderTexture Map;
        /// <summary>One byte per texel, rows from v = 0 upwards; null until the read-back of a fresh bake.</summary>
        public byte[] Bytes;
        public int Width, Height;
        public float Reach;

        public void Release()
        {
            WetBake.Free(Map);
            Map = null;
            Bytes = null;
        }
    }

    /// <summary>A vertex a droplet can leave from.</summary>
    public struct WetEmitter
    {
        public Transform Bone;
        public Vector3 Local;
        /// <summary>How far the stain must spread to reach it.</summary>
        public float Distance;
    }

    /// <summary>One stain as it runs.</summary>
    public sealed class WetStainRun
    {
        static int serials;

        public readonly int Serial = ++serials;
        public WetStain Doc;
        public WetShapeValues Shape;
        public WetLookValues Look;
        public WetDripValues Drips;
        public WetPuddleLook PuddleLook;
        public float T;
        public bool Running;
        /// <summary>The radius last drawn; below 0 before the first.</summary>
        public double Drawn = -1;
        public long DropStep;
        /// <summary>The maps that act, by target key.</summary>
        public readonly Dictionary<string, WetMapEntry> Live = new Dictionary<string, WetMapEntry>(StringComparer.Ordinal);
        /// <summary>The maps of a drag in progress, by target key: no picture yet.</summary>
        public readonly Dictionary<string, WetMapData> Preview = new Dictionary<string, WetMapData>(StringComparer.Ordinal);
        public int Dormant, Protected;
        public readonly List<WetEmitter> Emitters = new List<WetEmitter>();
        public bool EmittersStale = true;
        /// <summary>The default origin as last found, in the space of a bone.</summary>
        public Transform OriginBone;
        public Vector3 OriginLocal;

        public void Adopt(WetStain s)
        {
            Doc = s;
            Shape = s.Shape.Applied();
            Look = s.Look.Applied();
            Drips = s.Drips.Applied();
            PuddleLook = s.Puddle.Applied();
            T = s.AppliedT;
            Running = s.Running && !s.Kept;
            DropStep = WetMath.DropStepOf(T);
            EmittersStale = true;
        }

        public double Radius => WetMath.DrawnRadius(T, Shape.Spread, Shape.Radius);
        public void FreePreview()
        {
            foreach (var d in Preview.Values) d.Release();
            Preview.Clear();
        }
    }

    public sealed class WetRig : IDisposable
    {
        /// <summary>A target that shows at least one stain.</summary>
        public sealed class Slot
        {
            public WetTarget Target;
            public WetCompose Compose;
            public WetFilm Film;
            public bool Dirty, Filmed;
        }

        readonly SceneRuntime rt;
        readonly Transform root;
        readonly List<WetStainRun> runs = new List<WetStainRun>();
        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        readonly Dictionary<string, WetMapData> data = new Dictionary<string, WetMapData>(StringComparer.Ordinal);
        readonly List<WetLayer> layers = new List<WetLayer>();
        readonly List<Vector3> emitAt = new List<Vector3>();
        double[] emitWeight = new double[800];
        readonly WetDrops drops;
        readonly WetPuddles puddles;
        readonly WetFloor floor = new WetFloor();
        CharaWet doc;
        double fallClock;
        int puddleVersion = -1, unreadable, tiled, noFilm;
        bool puddleLookDirty = true, resolveWanted, suspended;

        public WetRig(SceneRuntime rt, object owner, string nodeId, Transform root)
        {
            this.rt = rt;
            this.root = root;
            NodeId = nodeId;
            drops = new WetDrops(root);
            puddles = new WetPuddles(owner, root);
        }

        public string NodeId { get; }
        public CharaRealization Real { get; private set; }
        public IReadOnlyList<WetStainRun> Runs => runs;
        public IReadOnlyDictionary<string, Slot> Slots => slots;
        public WetDrops Drops => drops;
        public WetPuddles Puddles => puddles;
        public WetFloor Floor => floor;
        public int Unreadable => unreadable;
        public int Tiled => tiled;
        public int NoFilm => noFilm;
        public CharaWet Doc => doc;
        /// <summary>Drop steps run and maps decoded so far (tests).</summary>
        public int Decoded { get; private set; }
        public int Composed { get; private set; }

        bool Built => Real != null && Real.State == RealizationState.Built && Real.Context != null;

        WetStainRun RunOf(string id)
        {
            foreach (var r in runs) if (r.Doc.Id == id) return r;
            return null;
        }

        // ------------------------------------------------------------ the document

        /// <summary>The block changed, or this rig is new: the runtime takes what the document says.</summary>
        public void Sync(CharaWet block)
        {
            doc = block;
            var next = new List<WetStainRun>();
            foreach (var s in block.AppliedStains())
            {
                var run = RunOf(s.Id) ?? new WetStainRun();
                bool started = !run.Running && s.Running && !s.Kept;
                run.FreePreview();
                run.Adopt(s);
                if (run.T <= 0f && !run.Running) drops.Clear(run.Serial);
                run.Drawn = -1;
                next.Add(run);
                if (started) floorDue = true;
            }
            foreach (var old in runs)
                if (!next.Contains(old))
                {
                    drops.Clear(old.Serial);
                    old.FreePreview();
                }
            runs.Clear();
            runs.AddRange(next);
            puddles.Sync(block);
            puddleVersion = -1;
            puddleLookDirty = true;
            floorDue = true;
            Resolve();
            var film = block.Film.Applied();
            foreach (var s in slots.Values) s.Film?.Set(film.Gloss, film.Smooth, film.Opacity);
            Compose();
        }

        bool floorDue;

        /// <summary>The character was built or rebuilt: what was on the old body is gone.</summary>
        public void Bind(CharaRealization r)
        {
            Real = r;
            foreach (var run in runs) { run.OriginBone = null; run.FreePreview(); }
            ReleaseSlots();
            if (doc == null) return;
            floorDue = true;
            Resolve();
            Compose();
        }

        // ------------------------------------------------------------ resolve

        /// <summary>Every map against the built character; the slots follow.</summary>
        public void Resolve()
        {
            resolveWanted = false;
            if (!Built || doc == null) return;
            var found = WetTargets.Collect(Real, doc.Targets);
            var byKey = new Dictionary<string, WetTarget>(found.Count, StringComparer.Ordinal);
            foreach (var t in found) byKey[t.Key] = t;
            var known = new Dictionary<Renderer, string>();
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            unreadable = tiled = noFilm = 0;
            foreach (var t in found)
            {
                if (t.State == WetTargetStates.Tiled) tiled++;
                var m = WetTargets.MeshOf(t.Renderer);
                if (m && m.isReadable) continue;
                WetTargets.ReadSignature(t, known);
                if (t.State == WetTargetStates.Unreadable) unreadable++;
            }
            foreach (var run in runs)
            {
                run.Live.Clear();
                run.Dormant = run.Protected = 0;
                run.EmittersStale = true;
                foreach (var e in run.Doc.AppliedMaps())
                {
                    string key = WetTarget.KeyOf(e.Renderer, e.Slot);
                    if (byKey.TryGetValue(key, out var t))
                    {
                        WetTargets.ReadSignature(t, known);
                        if (t.State == WetTargetStates.Ok && t.Signature == e.Mesh && !run.Live.ContainsKey(key))
                        {
                            var d = Data(e);
                            if (d != null)
                            {
                                d.Reach = e.Reach;
                                run.Live[key] = e;
                                wanted.Add(key);
                                used.Add(e.Res);
                                continue;
                            }
                        }
                        run.Dormant++;
                        continue;
                    }
                    var s = AdvancedPoseParts.Renderer(Real, e.Renderer);
                    if (s && MosaicSet.IsMember(s)) run.Protected++; else run.Dormant++;
                }
                foreach (var k in run.Preview.Keys) wanted.Add(k);
            }
            // what the slots show now
            var gone = new List<string>();
            foreach (var kv in slots)
            {
                var s = kv.Value;
                bool keep = wanted.Contains(kv.Key) && byKey.TryGetValue(kv.Key, out var t) && t.Renderer == s.Target.Renderer && s.Filmed == WantsFilm(t);
                if (keep) { s.Target = byKey[kv.Key]; s.Dirty = true; continue; }
                Release(s);
                gone.Add(kv.Key);
            }
            foreach (var k in gone) slots.Remove(k);
            foreach (var k in wanted)
                if (!slots.ContainsKey(k) && byKey.TryGetValue(k, out var t) && WetBake.Available) slots[k] = NewSlot(t);
            foreach (var s in slots.Values) SetFollowers(s, known);
            noFilm = 0;
            foreach (var s in slots.Values)
                if (s.Target.Glossy && doc.Film.On && s.Film == null) noFilm++;
            // pictures nothing uses now
            var free = new List<string>();
            foreach (var k in data.Keys) if (!used.Contains(k)) free.Add(k);
            foreach (var k in free) { data[k].Release(); data.Remove(k); }
        }

        WetMapData Data(WetMapEntry e)
        {
            if (data.TryGetValue(e.Res, out var d)) return d;
            var png = rt.Model.File.Res.Get(e.Res);
            if (png == null) return null;
            var map = WetBake.Decode(png, out var bytes);
            if (!map) return null;
            Decoded++;
            d = new WetMapData { Map = map, Bytes = bytes, Width = map.width, Height = map.height, Reach = e.Reach };
            data[e.Res] = d;
            return d;
        }

        bool WantsFilm(WetTarget t) => t.Glossy && doc.Film.On && WetFilm.FamilyOf(t.Material) != WetFilmFamily.None && t.Renderer is SkinnedMeshRenderer;

        Slot NewSlot(WetTarget t)
        {
            bool film = WantsFilm(t);
            var s = new Slot { Target = t, Filmed = film, Dirty = true };
            s.Compose = new WetCompose(t.Renderer, t.Slot, t.CompositeCap, film);
            if (film)
            {
                s.Film = WetFilm.Create(root, s.Compose);
                var v = doc.Film.Applied();
                s.Film?.Set(v.Gloss, v.Smooth, v.Opacity);
            }
            if (suspended) { s.Compose.Suspend(true); s.Film?.Suspend(true); }
            return s;
        }

        // the parts that draw the body's material and the cut bodies show the body's composite, when their UV0 is the body's
        void SetFollowers(Slot s, Dictionary<Renderer, string> known)
        {
            s.Compose.ClearFollowers();
            if (s.Target.Followers.Count == 0) return;
            WetTargets.ReadSignature(s.Target, known);
            string own = HashOf(s.Target.Signature);
            foreach (var f in s.Target.Followers)
            {
                if (!known.TryGetValue(f.Renderer, out string sig)) known[f.Renderer] = sig = WetTargets.SignatureOf(f.Renderer);
                if (own != null && HashOf(sig) == own) s.Compose.Follow(f.Renderer, f.Slot);
            }
        }

        static string HashOf(string signature)
        {
            if (signature == null) return null;
            int cut = signature.LastIndexOf('|');
            return cut >= 0 ? signature.Substring(cut + 1) : null;
        }

        static void Release(Slot s)
        {
            s.Film?.Dispose();
            s.Compose?.Dispose();
            s.Film = null;
            s.Compose = null;
        }

        void ReleaseSlots()
        {
            foreach (var s in slots.Values) Release(s);
            slots.Clear();
        }

        // ------------------------------------------------------------ drawing

        void MarkAll()
        {
            foreach (var s in slots.Values) s.Dirty = true;
        }

        void Mark(WetStainRun run)
        {
            foreach (var k in run.Live.Keys) if (slots.TryGetValue(k, out var s)) s.Dirty = true;
            foreach (var k in run.Preview.Keys) if (slots.TryGetValue(k, out var s)) s.Dirty = true;
        }

        /// <summary>Draws every slot that waits, with the radii of now.</summary>
        void Compose()
        {
            foreach (var run in runs) if (run.Drawn < 0) { run.Drawn = run.Radius; Mark(run); }
            foreach (var kv in slots)
            {
                var s = kv.Value;
                if (!s.Dirty) continue;
                s.Dirty = false;
                layers.Clear();
                foreach (var run in runs)
                {
                    Texture map = null;
                    float reach = 0f;
                    if (run.Preview.TryGetValue(kv.Key, out var pv)) { map = pv.Map; reach = pv.Reach; }
                    else if (run.Live.TryGetValue(kv.Key, out var e) && data.TryGetValue(e.Res, out var d)) { map = d.Map; reach = e.Reach; }
                    if (!map) continue;
                    layers.Add(new WetLayer
                    {
                        Map = map,
                        Radius = (float)run.Drawn,
                        Feather = run.Shape.Feather,
                        Reach = reach,
                        Saturate = run.Look.Saturate,
                        Amount = run.Look.Amount,
                        Tint = new Color(run.Look.Tint.X, run.Look.Tint.Y, run.Look.Tint.Z),
                        Stain = new Color(run.Look.Stain.X, run.Look.Stain.Y, run.Look.Stain.Z),
                        Skin = s.Target.SkinFactor ? run.Look.Skin : 1f,
                    });
                }
                s.Compose.Compose(layers);
                Composed++;
            }
        }

        /// <summary>After a flush of the character: a swap resolves, else the film copies the garment's values and every slot draws from the new image.</summary>
        public void AfterFlush(FlushReport r)
        {
            bool dead = false;
            foreach (var s in slots.Values) if (!s.Target.Renderer) dead = true;
            if (dead || r.AnySwap || r.HeadSwapped || r.NewObjects.Count > 0) { Resolve(); }
            else
            {
                foreach (var s in slots.Values) s.Film?.Refresh();
                MarkAll();
            }
            Compose();
        }

        /// <summary>A change of the character's state: the film copies the garment's values.</summary>
        public void StateChanged()
        {
            foreach (var s in slots.Values) s.Film?.Refresh();
        }

        /// <summary>Overrides and films off around a thumbnail of the character.</summary>
        public void Suspend(bool on)
        {
            suspended = on;
            foreach (var s in slots.Values)
            {
                s.Compose.Suspend(on);
                s.Film?.Suspend(on);
            }
        }

        // ------------------------------------------------------------ time

        public void Tick(float dt)
        {
            if (!(dt > 0f) || !Built || doc == null || Real.InGraveyard) return;
            foreach (var run in runs)
            {
                if (!run.Running) continue;
                run.T += dt;
                float end = run.Shape.Spread + run.Drips.Tail;
                if (run.T >= end)
                {
                    if (!run.Doc.Loop) run.Running = false;
                    else if (run.T >= end + run.Doc.AppliedHold) Again(run);
                }
                double r = run.Radius;
                if (r != run.Drawn)
                {
                    run.Drawn = r;
                    Mark(run);
                }
                long to = WetMath.DropStepOf(run.T);
                while (run.DropStep < to) Emit(run, ++run.DropStep);
            }
            Fall(dt);
            Compose();
        }

        // a loop dries and starts over; kept stains and their puddles stay
        void Again(WetStainRun run)
        {
            run.T = 0f;
            run.DropStep = 0;
            drops.Clear(run.Serial);
            puddles.Remove(run.Doc.Id);
        }

        void Emit(WetStainRun run, long step)
        {
            if (!run.Drips.On || !(run.Drips.Rate > 0f)) return;
            double t = step * WetMath.DropStep;
            int seed = run.Doc.Seed;
            int n = WetMath.DropsInStep(seed, step, run.Drips.Rate, WetMath.DripIntensity(t, run.Shape.Spread, run.Drips.Tail));
            if (n == 0) return;
            if (run.EmittersStale) BuildEmitters(run);
            double r = WetMath.Radius(t, run.Shape.Spread, run.Shape.Radius);
            var at = emitAt;
            at.Clear();
            float low = float.MaxValue;
            foreach (var e in run.Emitters)
            {
                if (e.Distance > r || !e.Bone) continue;
                var p = e.Bone.TransformPoint(e.Local);
                at.Add(p);
                if (p.y < low) low = p.y;
            }
            if (at.Count == 0) return;
            double total = 0;
            if (emitWeight.Length < at.Count) emitWeight = new double[Math.Max(at.Count, 800)];
            var weight = emitWeight;
            for (int i = 0; i < at.Count; i++) total += weight[i] = Math.Exp(-(at[i].y - low) / WetMath.LowFall);
            var c = run.Drips.Color;
            var colour = new Color(c.R, c.G, c.B, c.A);
            for (int j = 0; j < n && j < 2; j++)
            {
                double pick = WetMath.Rng(seed, step * 8 + 1 + j * 3) * total;
                int k = 0;
                while (k < at.Count - 1 && (pick -= weight[k]) > 0) k++;
                float jx = (float)(WetMath.Rng(seed, step * 8 + 2 + j * 3) - .5) * .04f, jz = (float)(WetMath.Rng(seed, step * 8 + 3 + j * 3) - .5) * .04f;
                drops.Emit(at[k] + new Vector3(jx, 0f, jz), run.Drips.Size, colour, run.Drips.Shine, run.Serial, seed, step, j);
            }
        }

        // the vertices of the reached parts a droplet may leave from: every max(1, V / 3000)-th, at most 800, read from the stain's map, so a load emits as before it
        void BuildEmitters(WetStainRun run)
        {
            run.EmittersStale = false;
            run.Emitters.Clear();
            if (!run.Drips.On) return;
            double max = run.Shape.Radius;
            foreach (var kv in run.Live)
            {
                if (!slots.TryGetValue(kv.Key, out var slot)) continue;
                var t = slot.Target;
                if (t.SkinFactor && !run.Drips.Skin) continue;
                if (!data.TryGetValue(kv.Value.Res, out var d) || d.Bytes == null) continue;
                var mesh = WetTargets.MeshOf(t.Renderer);
                if (!mesh || !mesh.isReadable) continue;
                var uv = mesh.uv;
                var verts = mesh.vertices;
                if (uv.Length != verts.Length || verts.Length == 0) continue;
                var s = t.Renderer as SkinnedMeshRenderer;
                var weights = s ? mesh.boneWeights : null;
                var binds = s ? mesh.bindposes : null;
                var bones = s ? s.bones : null;
                var sub = mesh.GetSubMesh(Mathf.Min(t.Slot, mesh.subMeshCount - 1));
                int first = sub.firstVertex, count = sub.vertexCount, stride = Math.Max(1, count / 3000);
                for (int v = first; v < first + count; v += stride)
                {
                    int value = Sample(d, uv[v]);
                    if (value >= WetMath.Unreached) continue;
                    double at = WetMath.MapDistance(value, kv.Value.Reach);
                    if (at > max) continue;
                    if (!s) { run.Emitters.Add(new WetEmitter { Bone = t.Renderer.transform, Local = verts[v], Distance = (float)at }); continue; }
                    if (weights == null || v >= weights.Length) continue;
                    int b = weights[v].boneIndex0;
                    if (b < 0 || b >= bones.Length || b >= binds.Length || !bones[b]) continue;
                    run.Emitters.Add(new WetEmitter { Bone = bones[b], Local = binds[b].MultiplyPoint3x4(verts[v]), Distance = (float)at });
                }
            }
            Thin(run.Emitters, 800);
        }

        static void Thin(List<WetEmitter> l, int cap)
        {
            if (l.Count <= cap) return;
            var kept = new List<WetEmitter>(cap);
            for (int i = 0; i < cap; i++) kept.Add(l[(int)((long)i * l.Count / cap)]);
            l.Clear();
            l.AddRange(kept);
        }

        static int Sample(WetMapData d, Vector2 uv)
        {
            int x = Mathf.Min(d.Width - 1, (int)(Wrapped(uv.x) * d.Width)), y = Mathf.Min(d.Height - 1, (int)(Wrapped(uv.y) * d.Height));
            return d.Bytes[y * d.Width + x];
        }

        // a tiled part wraps; a vertex on the map's far edge stays on that edge
        static float Wrapped(float c)
        {
            float f = c - Mathf.Floor(c);
            return f == 0f && c > 0f ? 1f : f;
        }

        void Fall(float dt)
        {
            bool flying = drops.Drops > 0 || drops.Splashes > 0;
            if (!flying) { fallClock = 0; return; }
            MeasureIfDue(dt);
            fallClock += dt;
            while (fallClock + 1e-6 >= WetMath.DropStep)
            {
                fallClock -= WetMath.DropStep;
                drops.Step(floor.Y, Landed);
            }
        }

        void Landed(int serial, Vector3 p, float size)
        {
            foreach (var run in runs)
            {
                if (run.Serial != serial) continue;
                puddles.Land(run.Doc.Id, p, size, run.Drips.Growth, run.PuddleLook.Scale, run.Doc.Seed);
                return;
            }
        }

        // ------------------------------------------------------------ per frame

        public void LateApply()
        {
            if (!Built || doc == null) return;
            bool hide = Real.InGraveyard || Real.Node == null || !Real.Node.EffectiveVisible;
            drops.Hidden = hide;
            puddles.Hidden = hide;
            foreach (var s in slots.Values)
                if (s.Film != null && !s.Film.LateApply()) resolveWanted = true;
            if (resolveWanted)
            {
                Resolve();
                Compose();
            }
            if (floorDue) MeasureNow();
            drops.Draw();
            if (puddleVersion != puddles.Version || puddleLookDirty)
            {
                puddleVersion = puddles.Version;
                puddleLookDirty = false;
                puddles.Draw(id => RunOf(id)?.PuddleLook, doc.Mirror.Applied());
            }
        }

        // ------------------------------------------------------------ floor

        void MeasureIfDue(float dt)
        {
            bool has = OriginNow(out var p, out _, out _);
            if (floorDue || (has && floor.Due(dt, p))) MeasureNow();
        }

        /// <summary>Measures the floor at the current stain's origin.</summary>
        public void MeasureNow()
        {
            floorDue = false;
            if (!Built || doc == null) return;
            OriginNow(out var p, out var f, out bool frame);
            floor.Measure(rt, Real.Context, doc.Floor, p, f, frame);
        }

        bool OriginNow(out Vector3 p, out WetFrame f, out bool frame)
        {
            p = default;
            frame = WetTargets.Frame(Real.Context, out f);
            var run = RunOf(doc.Current) ?? (runs.Count > 0 ? runs[runs.Count - 1] : null);
            if (run != null && OriginPoint(run.Doc, run, f, frame, null, out p)) return true;
            if (frame) p = WetTargets.V(f.Mid);
            return frame;
        }

        /// <summary>The floor's height of a block that differs from the present one, for an edit of the floor.</summary>
        public float FloorFor(WetFloorValues v, out string source)
        {
            var probe = new WetFloor();
            OriginNow(out var p, out var f, out bool frame);
            probe.Measure(rt, Real.Context, v, p, f, frame);
            source = probe.Source;
            return probe.Y;
        }

        /// <summary>The floor is measured again in this frame.</summary>
        public void MeasureSoon() => floorDue = true;

        /// <summary>A new map: puddles of the map mode take the surface at their point; where there is none they stay.</summary>
        public void MapChanged()
        {
            if (!Built || doc == null) return;
            MeasureNow();
            if (doc.Floor.AppliedMode != WetFloorValues.ModeMap || floor.Source != WetFloorSources.Map) return;
            var q = WetFloor.Query(rt);
            if (q == null) return;
            OriginNow(out var at, out _, out _);
            foreach (var p in puddles.List)
            {
                if (!q.Surface(new Vector3(p.Center.x, Mathf.Max(at.y, p.Center.y), p.Center.z), out var hit)) continue;
                float y = hit.Height + doc.Floor.AppliedOffset;
                if (Mathf.Abs(p.Center.y - y) > 1e-3f) { p.Center.y = y; puddles.Touch(); }
            }
        }

        // ------------------------------------------------------------ origin

        /// <summary>
        /// Where a stain's origin stands now. A bone's point follows the bone; the default origin was found by a ray at the last bake and follows the hips;
        /// <paramref name="found"/> given, the ray runs again.
        /// </summary>
        bool OriginPoint(WetStain s, WetStainRun run, in WetFrame f, bool frame, List<WetTarget> found, out Vector3 p)
        {
            p = default;
            var o = s.Origin;
            if (!o.IsActive || Real?.Context == null) return false;
            if (o.Mode == WetOriginValues.ModeBone)
            {
                var b = AdvancedPoseResolver.Resolve(Real, o.Bone);
                if (!b) return false;
                p = b.TransformPoint(new Vector3(o.Local.X, o.Local.Y, o.Local.Z));
                return true;
            }
            if (found == null && run?.OriginBone) { p = run.OriginBone.TransformPoint(run.OriginLocal); return true; }
            if (!frame) return false;
            p = DefaultOrigin(o, f, found ?? WetTargets.Collect(Real, true, false));
            var hips = Real.Context.Hips;
            if (run != null && hips)
            {
                run.OriginBone = hips;
                run.OriginLocal = hips.InverseTransformPoint(p);
            }
            return true;
        }

        // a level ray from 1.5 units in front of the bone below the hips, lowered by `down`, towards the back; the first surface of the body or clothes, moved out by `out`
        Vector3 DefaultOrigin(WetOriginValues o, in WetFrame f, List<WetTarget> found)
        {
            var bones = Real.Context.BodyBones;
            var forward = WetTargets.V(f.Forward);
            var up = WetTargets.V(f.Up);
            var surface = new List<WetTarget>(found.Count);
            foreach (var t in found) if (t.Kind == WetKinds.Skin || t.Kind == WetKinds.Clothes) surface.Add(t);
            if (bones != null && bones.TryGetValue("cf_J_Kokan", out var k) && k)
            {
                var ray = new Ray(k.position + forward * 1.5f - up * o.Down, -forward);
                if (WetTargets.Raycast(surface, ray, 4f, out var hit)) return hit.Point + forward * o.Out;
            }
            return WetTargets.V(f.Mid) - up * (.3f * (float)f.Scale) + forward * (1.1f * (float)f.Scale);
        }

        // ------------------------------------------------------------ bake

        /// <summary>
        /// Fresh maps of <paramref name="stain"/> (of a block not yet in the model) on what is shown now, with the current pose; with <paramref name="missing"/> only for
        /// the targets without a live map. Every map's picture goes into the file's store and <paramref name="stain"/>'s Maps are set. Null, or why it could not:
        /// `unsupported` without a graphics device, wet.noFrame for a body without the frame's bones, not_found for an origin it cannot find.
        /// </summary>
        public string Bake(CharaWet block, WetStain stain, bool missing)
        {
            string why = Draw(block, stain, missing, true, out var fresh);
            if (why != null) return why;
            var maps = new List<WetMapEntry>();
            if (missing) maps.AddRange(stain.Maps);
            foreach (var (t, d, reach) in fresh)
            {
                var bytes = maps.Count < WetLimits.MaxMaps ? WetBake.ReadBack(d.Map) : null;
                if (bytes == null) { d.Release(); continue; }
                d.Bytes = bytes;
                string res = rt.Model.File.Res.Add(WetBake.Encode(bytes, d.Width, d.Height));
                if (data.ContainsKey(res)) d.Release();
                else data[res] = d;
                maps.Add(new WetMapEntry { Renderer = t.Address, Slot = t.Slot, Res = res, Reach = reach, Mesh = t.Signature });
            }
            stain.Maps = maps;
            return null;
        }

        /// <summary>Maps of a drag in progress: drawn at once, no picture; the commit bakes the last value with its picture.</summary>
        public string Preview(CharaWet block, WetStain stain)
        {
            string why = Draw(block, stain, false, false, out var fresh);
            if (why != null) return why;
            var run = RunOf(stain.Id);
            if (run == null)
            {
                foreach (var (_, d, _) in fresh) d.Release();
                return null;
            }
            run.FreePreview();
            foreach (var (t, d, reach) in fresh) { d.Reach = reach; run.Preview[t.Key] = d; }
            run.Shape = stain.Shape.Applied();
            run.Drawn = -1;
            Resolve();
            Compose();
            return null;
        }

        string Draw(CharaWet block, WetStain stain, bool missing, bool picture, out List<(WetTarget t, WetMapData d, float reach)> fresh)
        {
            fresh = new List<(WetTarget, WetMapData, float)>();
            if (!WetBake.Available) return CommandCodes.Unsupported;
            if (!Built) return CommandCodes.NotFound;
            if (!WetTargets.Frame(Real.Context, out var frame)) return "wet.noFrame";
            var found = WetTargets.Collect(Real, block.Targets);
            var run = RunOf(stain.Id);
            if (!OriginPoint(stain, run, frame, true, found, out var origin)) return CommandCodes.NotFound;
            var shape = stain.Shape.Applied();
            double reach = WetMath.Reach(shape.Radius, shape.Feather), limit = shape.Radius + shape.Feather;
            var values = new WetDistanceValues { Up = shape.Up, Down = shape.Down, Front = shape.Front, Back = shape.Back, Side = shape.Side, Outer = shape.Outer, Noise = shape.Noise };
            var liveKeys = missing && run != null ? new HashSet<string>(run.Live.Keys) : null;
            var o = WetTargets.D(origin);
            var known = new Dictionary<Renderer, string>();
            var posed = new Mesh { name = "wet bake", hideFlags = HideFlags.HideAndDontSave };
            Renderer current = null;
            Vector2[] uv = null;
            float[] dist = null;
            bool readable = false;
            unreadable = 0;
            try
            {
                foreach (var t in found)
                {
                    if (!t.Shown || t.State != WetTargetStates.Ok || (liveKeys != null && liveKeys.Contains(t.Key))) continue;
                    if (t.Renderer != current)
                    {
                        current = t.Renderer;
                        readable = Pose(t.Renderer, posed, frame, values, o, out uv, out dist);
                    }
                    if (!readable) { t.State = WetTargetStates.Unreadable; unreadable++; continue; }
                    int sub = Mathf.Min(t.Slot, posed.subMeshCount - 1);
                    var tris = posed.GetTriangles(sub);
                    if (tris.Length == 0) continue;
                    float least = float.MaxValue;
                    foreach (int i in tris) if (dist[i] < least) least = dist[i];
                    if (least > limit) continue;
                    WetTargets.ReadSignature(t, known);
                    if (t.Signature == null) { unreadable++; continue; }
                    if (missing && Holds(stain, t)) continue;
                    var tex = WetCompose.BaseOf(t.Material);
                    var size = WetBake.MapSize(tex ? tex.width : 512, tex ? tex.height : 512);
                    var map = WetBake.Bake(uv, tris, dist, size.x, size.y, (float)reach);
                    if (map) fresh.Add((t, new WetMapData { Map = map, Width = size.x, Height = size.y, Reach = (float)reach }, (float)reach));
                }
            }
            finally { Object.DestroyImmediate(posed); }
            return null;
        }

        // a map this target can show as it is: its kind was switched off, and it comes back when the switch does
        bool Holds(WetStain stain, WetTarget t)
        {
            foreach (var e in stain.Maps)
                if (e.Renderer == t.Address && e.Slot == t.Slot && e.Mesh == t.Signature && Data(e) != null) return true;
            return false;
        }

        // the posed vertices of a part, and each one's distance from the origin
        static bool Pose(Renderer r, Mesh posed, in WetFrame frame, in WetDistanceValues values, Vec3d origin, out Vector2[] uv, out float[] dist)
        {
            uv = null;
            dist = null;
            Vector3[] v;
            Matrix4x4 toWorld;
            try
            {
                if (r is SkinnedMeshRenderer s)
                {
                    s.BakeMesh(posed, true);
                    toWorld = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                }
                else
                {
                    var m = WetTargets.MeshOf(r);
                    if (!m || !m.isReadable) return false;
                    posed.Clear();
                    posed.vertices = m.vertices;
                    posed.uv = m.uv;
                    posed.subMeshCount = m.subMeshCount;
                    for (int i = 0; i < m.subMeshCount; i++) posed.SetTriangles(m.GetTriangles(i), i, false);
                    toWorld = r.transform.localToWorldMatrix;
                }
                v = posed.vertices;
                uv = posed.uv;
            }
            catch (Exception) { return false; }
            if (v.Length == 0 || uv.Length != v.Length) return false;
            dist = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
                dist[i] = (float)WetMath.Distance(frame, values, origin, WetTargets.D(toWorld.MultiplyPoint3x4(v[i])));
            return true;
        }

        // ------------------------------------------------------------ the capture

        /// <summary>What the runtime holds that the document does not: the clock of each stain and the puddles.</summary>
        public void Collect(List<(string id, string path, object value, Action after)> writes)
        {
            if (doc == null) return;
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                int at = doc.Stains.IndexOf(run.Doc);
                if (at < 0) continue;
                if (run.T != run.Doc.T) writes.Add((NodeId, "chara.wet.stains[" + at + "].t", run.T, null));
                if (run.Running != run.Doc.Running) writes.Add((NodeId, "chara.wet.stains[" + at + "].running", run.Running, null));
            }
            if (!puddles.Same(doc.Puddles)) writes.Add((NodeId, "chara.wet.puddles", puddles.ToEntries(), null));
        }

        // ------------------------------------------------------------ end

        public void Dispose()
        {
            ReleaseSlots();
            foreach (var run in runs) run.FreePreview();
            foreach (var d in data.Values) d.Release();
            data.Clear();
            drops.Dispose();
            puddles.Dispose();
            runs.Clear();
        }
    }
}
