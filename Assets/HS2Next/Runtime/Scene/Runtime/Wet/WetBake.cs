// The distance picture of a stain, on the GPU. One map per drawn part and stain, in the part's UV space: per texel how far the stain
// must spread to reach it, as an 8-bit value (0-254 over the map's reach, 255 where nothing arrives). The map is what the runtime
// draws from the first moment and what the file stores, so a stain looks the same before and after a load.
// Bake: a temporary mesh with the UVs as positions (each triangle's UVs moved by the floor of their least values, as a tiled part wraps,
// so a triangle that ends exactly at 1 stays where it is) and the distance per vertex, drawn into an R16 target with BlendOp Min, two
// dilation passes into empty texels, then the map.
// Read-back and PNG: AsyncGPUReadback, rows from v = 0 upwards; a map is one grey channel and nothing else.
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public static class WetBake
    {
        public const string ShaderName = "HS2Next/Hidden/WetBake";
        /// <summary>The longest side of a map.</summary>
        public const int MapCap = 2048;
        /// <summary>The value of a texel nothing arrives at.</summary>
        public const byte Unreached = 255;
        const int PassRaster = 0, PassDilate = 1, PassFinish = 2, PassRed = 3;

        static readonly int TexelId = Shader.PropertyToID("_WetTexel");
        static readonly int ReachId = Shader.PropertyToID("_WetReach");

        static Material material;
        static Mesh mesh;
        static CommandBuffer cmd;
        static Vector3[] positions = new Vector3[0];
        static int[] sequence = new int[0];

        static Material Mat
        {
            get
            {
                if (material) return material;
                var s = Shader.Find(ShaderName);
                if (s) material = new Material(s) { name = "HS2Next wet bake", hideFlags = HideFlags.HideAndDontSave };
                return material;
            }
        }

        /// <summary>A graphics device and the shader are there: a stain can be baked.</summary>
        public static bool Available => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && Mat;

        /// <summary>The map of a texture of this size: its size, each side capped at <see cref="MapCap"/>, halved evenly.</summary>
        public static Vector2Int MapSize(int width, int height)
        {
            int w = Math.Max(1, width), h = Math.Max(1, height);
            while (w > MapCap || h > MapCap) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            return new Vector2Int(w, h);
        }

        /// <summary>An empty map of this size (the caller owns it, see <see cref="Free"/>).</summary>
        public static RenderTexture NewMap(int width, int height)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(width, height, GraphicsFormat.R8_UNorm, 0) { useMipMap = false, autoGenerateMips = false })
            {
                name = "HS2Next wet map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave,
            };
            rt.Create();
            return rt;
        }

        /// <summary>Releases and destroys a map.</summary>
        public static void Free(RenderTexture map)
        {
            if (!map) return;
            map.Release();
            if (Application.isPlaying) Object.Destroy(map); else Object.DestroyImmediate(map);
        }

        /// <summary>
        /// The map of one target: <paramref name="triangles"/> index <paramref name="uv"/> and <paramref name="distance"/> (one value per
        /// vertex), <paramref name="reach"/> is what value 254 stands for. Null when no bake can run. The caller owns the result.
        /// </summary>
        public static RenderTexture Bake(Vector2[] uv, int[] triangles, float[] distance, int width, int height, float reach)
        {
            if (!Available || uv == null || triangles == null || distance == null) return null;
            int n = triangles.Length / 3 * 3;
            if (positions.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                positions = new Vector3[size];
                sequence = new int[size];
                for (int i = 0; i < size; i++) sequence[i] = i;
            }
            for (int i = 0; i < n; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector2 ua = uv[a], ub = uv[b], uc = uv[c];
                float fx = Mathf.Floor(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x))), fy = Mathf.Floor(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y)));
                positions[i] = new Vector3(ua.x - fx, ua.y - fy, distance[a]);
                positions[i + 1] = new Vector3(ub.x - fx, ub.y - fy, distance[b]);
                positions[i + 2] = new Vector3(uc.x - fx, uc.y - fy, distance[c]);
            }
            if (!mesh) mesh = new Mesh { name = "HS2Next wet bake", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            mesh.Clear();
            mesh.SetVertices(positions, 0, n);
            mesh.SetIndices(sequence, 0, n, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

            var map = NewMap(width, height);
            var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R16_UNorm, 0) { useMipMap = false, autoGenerateMips = false };
            var near = RenderTexture.GetTemporary(desc);
            var other = RenderTexture.GetTemporary(desc);
            near.filterMode = other.filterMode = FilterMode.Point;
            cmd ??= new CommandBuffer { name = "HS2Next wet bake" };
            cmd.Clear();
            cmd.SetRenderTarget(near);
            cmd.ClearRenderTarget(false, true, Color.white);
            cmd.DrawMesh(mesh, Matrix4x4.identity, Mat, 0, PassRaster);
            cmd.SetGlobalVector(TexelId, new Vector4(1f / width, 1f / height, 0f, 0f));
            cmd.SetGlobalFloat(ReachId, reach);
            cmd.Blit(near, other, Mat, PassDilate);
            cmd.Blit(other, near, Mat, PassDilate);
            cmd.Blit(near, map, Mat, PassFinish);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            RenderTexture.ReleaseTemporary(near);
            RenderTexture.ReleaseTemporary(other);
            return map;
        }

        /// <summary>The map's bytes, one per texel, rows from v = 0 upwards; waits for the GPU. Null on a read-back error.</summary>
        public static byte[] ReadBack(RenderTexture map)
        {
            if (!map) return null;
            var req = AsyncGPUReadback.Request(map, 0, GraphicsFormat.R8_UNorm);
            req.WaitForCompletion();
            return req.hasError ? null : req.GetData<byte>().ToArray();
        }

        /// <summary>The picture stored in the file: 8-bit grey PNG of <see cref="ReadBack"/>'s bytes.</summary>
        public static byte[] Encode(byte[] r8, int width, int height) =>
            ImageConversion.EncodeArrayToPNG(r8, GraphicsFormat.R8_UNorm, (uint)width, (uint)height);

        /// <summary>A stored picture as a map and as bytes (<see cref="ReadBack"/>'s layout); null when it does not decode. The caller owns the map.</summary>
        public static RenderTexture Decode(byte[] png, out byte[] r8)
        {
            r8 = null;
            if (!Available || png == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "HS2Next wet picture", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!ImageConversion.LoadImage(tex, png, false)) return null;
                var px = tex.GetPixels32();
                r8 = new byte[px.Length];
                for (int i = 0; i < px.Length; i++) r8[i] = px[i].r;
                var map = NewMap(tex.width, tex.height);
                Graphics.Blit(tex, map, Mat, PassRed);
                return map;
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(tex); else Object.DestroyImmediate(tex);
            }
        }
    }
}
