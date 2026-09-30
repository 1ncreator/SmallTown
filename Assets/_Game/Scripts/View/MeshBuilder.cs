using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmallTown.View
{
    /// <summary>
    /// Flat-shaded low-poly mesh builder. Every primitive gets per-face normals, a vertex colour and
    /// the TEXCOORD1 channel used by the toy shaders (x emission, y/z tint masks, w snow mask).
    /// Faces are oriented automatically towards an outward hint vector.
    /// </summary>
    public sealed class MeshBuilder
    {
        public static readonly Vector4 Plain = new Vector4(0f, 0f, 0f, 1f);
        public static readonly Vector4 NoSnow = new Vector4(0f, 0f, 0f, 0f);

        private readonly List<Vector3> _v = new List<Vector3>(4096);
        private readonly List<Vector3> _n = new List<Vector3>(4096);
        private readonly List<Color32> _c = new List<Color32>(4096);
        private readonly List<Vector2> _uv = new List<Vector2>(4096);
        private readonly List<Vector4> _uv1 = new List<Vector4>(4096);
        private readonly List<int> _t = new List<int>(8192);
        public Matrix4x4 M = Matrix4x4.identity;

        public int VertexCount => _v.Count;

        public void Clear()
        {
            _v.Clear(); _n.Clear(); _c.Clear(); _uv.Clear(); _uv1.Clear(); _t.Clear();
            M = Matrix4x4.identity;
        }

        public void SetTransform(Vector3 pos, float yawDeg, Vector3 scale)
        {
            M = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), scale);
        }

        public void SetTransform(Vector3 pos, float yawDeg) => SetTransform(pos, yawDeg, Vector3.one);

        public void Identity() => M = Matrix4x4.identity;

        // ------------------------------------------------------------------ primitives

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col, Vector4 uv1, Vector3 hint)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); c = M.MultiplyPoint3x4(c);
            Vector3 h = M.MultiplyVector(hint);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            if (h.sqrMagnitude > 1e-8f && Vector3.Dot(n, h) < 0f)
            {
                var t = b; b = c; c = t;
                n = -n;
            }
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            for (int k = 0; k < 3; k++) { _n.Add(n); _c.Add(col); _uv1.Add(uv1); _uv.Add(new Vector2(0.5f, 0.5f)); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, Vector4 uv1, Vector3 hint, bool radialUv = false)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); c = M.MultiplyPoint3x4(c); d = M.MultiplyPoint3x4(d);
            Vector3 h = M.MultiplyVector(hint);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            bool flip = h.sqrMagnitude > 1e-8f && Vector3.Dot(n, h) < 0f;
            if (flip) n = -n;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); _uv1.Add(uv1); }
            if (radialUv)
            {
                _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(0, 1)); _uv.Add(new Vector2(1, 1)); _uv.Add(new Vector2(1, 0));
            }
            else
            {
                for (int k = 0; k < 4; k++) _uv.Add(new Vector2(0.5f, 0.5f));
            }
            if (!flip)
            {
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }
            else
            {
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
                _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
            }
        }

        /// <summary>Horizontal rectangle (top face only) at height y.</summary>
        public void Rect(float x0, float z0, float x1, float z1, float y, Color32 col, Vector4 uv1, bool radialUv = false)
        {
            Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), col, uv1, Vector3.up, radialUv);
        }

        /// <summary>Axis-aligned box (in local space of M), given min and max corners.</summary>
        public void BoxMinMax(Vector3 min, Vector3 max, Color32 col, Vector4 uv1, bool bottom = false, Color32? topCol = null)
        {
            Vector3 c = (min + max) * 0.5f;
            var p000 = new Vector3(min.x, min.y, min.z); var p100 = new Vector3(max.x, min.y, min.z);
            var p010 = new Vector3(min.x, max.y, min.z); var p110 = new Vector3(max.x, max.y, min.z);
            var p001 = new Vector3(min.x, min.y, max.z); var p101 = new Vector3(max.x, min.y, max.z);
            var p011 = new Vector3(min.x, max.y, max.z); var p111 = new Vector3(max.x, max.y, max.z);
            Quad(p010, p011, p111, p110, topCol ?? col, uv1, Vector3.up);
            Vector4 side = new Vector4(uv1.x, uv1.y, uv1.z, 0f);
            Quad(p000, p010, p110, p100, col, side, Vector3.back);
            Quad(p101, p111, p011, p001, col, side, Vector3.forward);
            Quad(p001, p011, p010, p000, col, side, Vector3.left);
            Quad(p100, p110, p111, p101, col, side, Vector3.right);
            if (bottom) Quad(p000, p100, p101, p001, col, side, Vector3.down);
        }

        public void Box(Vector3 center, Vector3 size, Color32 col, Vector4 uv1, bool bottom = false)
        {
            BoxMinMax(center - size * 0.5f, center + size * 0.5f, col, uv1, bottom);
        }

        /// <summary>Gable roof over footprint [-w/2,w/2]x[-d/2,d/2] at base y with ridge along X (or Z).</summary>
        public void GableRoof(float w, float d, float baseY, float height, float overhang, bool ridgeAlongX, Color32 roof, Color32 gable, Vector4 uv1)
        {
            float hw = w * 0.5f + overhang, hd = d * 0.5f + overhang;
            Vector4 gableUv = new Vector4(uv1.x, uv1.y, uv1.z, 0f);
            if (ridgeAlongX)
            {
                var r0 = new Vector3(-hw, baseY + height, 0f);
                var r1 = new Vector3(hw, baseY + height, 0f);
                Quad(new Vector3(-hw, baseY, -hd), r0, r1, new Vector3(hw, baseY, -hd), roof, uv1, new Vector3(0, 1, -1));
                Quad(new Vector3(hw, baseY, hd), r1, r0, new Vector3(-hw, baseY, hd), roof, uv1, new Vector3(0, 1, 1));
                float gw = w * 0.5f, gd = d * 0.5f;
                Tri(new Vector3(-gw, baseY, -gd), new Vector3(-gw, baseY + height * (gd / hd), 0f), new Vector3(-gw, baseY, gd), gable, gableUv, Vector3.left);
                Tri(new Vector3(gw, baseY, -gd), new Vector3(gw, baseY + height * (gd / hd), 0f), new Vector3(gw, baseY, gd), gable, gableUv, Vector3.right);
                // underside of the overhang
                Quad(new Vector3(-hw, baseY, -hd), new Vector3(-hw, baseY, hd), new Vector3(hw, baseY, hd), new Vector3(hw, baseY, -hd), gable, gableUv, Vector3.down);
            }
            else
            {
                var r0 = new Vector3(0f, baseY + height, -hd);
                var r1 = new Vector3(0f, baseY + height, hd);
                Quad(new Vector3(-hw, baseY, -hd), new Vector3(-hw, baseY, hd), r1, r0, roof, uv1, new Vector3(-1, 1, 0));
                Quad(new Vector3(hw, baseY, hd), new Vector3(hw, baseY, -hd), r0, r1, roof, uv1, new Vector3(1, 1, 0));
                float gw = w * 0.5f, gd = d * 0.5f;
                Tri(new Vector3(-gw, baseY, -gd), new Vector3(0f, baseY + height * (gw / hw), -gd), new Vector3(gw, baseY, -gd), gable, gableUv, Vector3.back);
                Tri(new Vector3(-gw, baseY, gd), new Vector3(0f, baseY + height * (gw / hw), gd), new Vector3(gw, baseY, gd), gable, gableUv, Vector3.forward);
                Quad(new Vector3(-hw, baseY, -hd), new Vector3(-hw, baseY, hd), new Vector3(hw, baseY, hd), new Vector3(hw, baseY, -hd), gable, gableUv, Vector3.down);
            }
        }

        /// <summary>Four-sided pyramid (spires, tower roofs).</summary>
        public void Pyramid(Vector3 baseCenter, float w, float d, float height, Color32 col, Vector4 uv1)
        {
            float hw = w * 0.5f, hd = d * 0.5f;
            var apex = baseCenter + new Vector3(0, height, 0);
            var a = baseCenter + new Vector3(-hw, 0, -hd);
            var b = baseCenter + new Vector3(hw, 0, -hd);
            var c = baseCenter + new Vector3(hw, 0, hd);
            var e = baseCenter + new Vector3(-hw, 0, hd);
            Tri(a, apex, b, col, uv1, new Vector3(0, 0.5f, -1));
            Tri(b, apex, c, col, uv1, new Vector3(1, 0.5f, 0));
            Tri(c, apex, e, col, uv1, new Vector3(0, 0.5f, 1));
            Tri(e, apex, a, col, uv1, new Vector3(-1, 0.5f, 0));
        }

        public void Cylinder(Vector3 baseCenter, float radius, float height, int segments, Color32 col, Vector4 uv1, bool top = true, bool bottom = false, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            Vector4 side = new Vector4(uv1.x, uv1.y, uv1.z, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var b0 = baseCenter + d0 * radius; var b1 = baseCenter + d1 * radius;
                var t0 = baseCenter + d0 * topRadius + Vector3.up * height; var t1 = baseCenter + d1 * topRadius + Vector3.up * height;
                Quad(b0, t0, t1, b1, col, side, (d0 + d1));
                if (top) Tri(baseCenter + Vector3.up * height, t0, t1, col, uv1, Vector3.up);
                if (bottom) Tri(baseCenter, b0, b1, col, side, Vector3.down);
            }
        }

        public void Cone(Vector3 baseCenter, float radius, float height, int segments, Color32 col, Vector4 uv1)
        {
            var apex = baseCenter + Vector3.up * height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Tri(baseCenter + d0 * radius, apex, baseCenter + d1 * radius, col, uv1, (d0 + d1) + Vector3.up * (radius / Mathf.Max(0.01f, height)));
            }
        }

        private static readonly Vector3[] IcoVerts = BuildIco(out IcoTris);
        private static int[] IcoTris;

        private static Vector3[] BuildIco(out int[] tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            tris = new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            return v.ToArray();
        }

        /// <summary>Low-poly ellipsoid (icosahedron) — foliage, heads, bushes.</summary>
        public void Blob(Vector3 center, Vector3 radii, Color32 col, Vector4 uv1, float jitterSeed = 0f)
        {
            for (int i = 0; i < IcoTris.Length; i += 3)
            {
                var a = Scale(IcoVerts[IcoTris[i]], radii, jitterSeed, IcoTris[i]) + center;
                var b = Scale(IcoVerts[IcoTris[i + 1]], radii, jitterSeed, IcoTris[i + 1]) + center;
                var c = Scale(IcoVerts[IcoTris[i + 2]], radii, jitterSeed, IcoTris[i + 2]) + center;
                var hint = (a + b + c) / 3f - center;
                Tri(a, b, c, col, uv1, hint);
            }
        }

        private static Vector3 Scale(Vector3 v, Vector3 r, float seed, int idx)
        {
            float j = seed == 0f ? 1f : 1f + 0.12f * Mathf.Sin(seed * 12.9898f + idx * 78.233f);
            return new Vector3(v.x * r.x * j, v.y * r.y * j, v.z * r.z * j);
        }

        // ------------------------------------------------------------------ output

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetUVs(0, _uv);
            m.SetUVs(1, _uv1);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
