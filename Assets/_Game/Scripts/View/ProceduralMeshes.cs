using SmallTown.Simulation.World;
using SmallTown.Utils;
using UnityEngine;

namespace SmallTown.View
{
    /// <summary>Procedural low-poly meshes for agents and props.</summary>
    public static class ProceduralMeshes
    {
        private static readonly Vector4 TintA = new Vector4(0f, 1f, 0f, 0f);
        private static readonly Vector4 TintB = new Vector4(0f, 0f, 1f, 0f);
        private static readonly Vector4 NoSnow = MeshBuilder.NoSnow;
        private static readonly Vector4 Lamp = new Vector4(1f, 0f, 0f, 0f);

        public static Mesh Person()
        {
            var mb = new MeshBuilder();
            mb.BoxMinMax(new Vector3(-0.26f, 0f, -0.16f), new Vector3(-0.03f, 0.85f, 0.14f), Palette.Pants, NoSnow);
            mb.BoxMinMax(new Vector3(0.03f, 0f, -0.16f), new Vector3(0.26f, 0.85f, 0.14f), Palette.Pants, NoSnow);
            mb.Cylinder(new Vector3(0f, 0.8f, 0f), 0.36f, 0.82f, 8, Palette.White, TintA, true, true, 0.3f);
            mb.BoxMinMax(new Vector3(-0.46f, 0.95f, -0.1f), new Vector3(-0.34f, 1.55f, 0.1f), Palette.White, TintA);
            mb.BoxMinMax(new Vector3(0.34f, 0.95f, -0.1f), new Vector3(0.46f, 1.55f, 0.1f), Palette.White, TintA);
            mb.Blob(new Vector3(0f, 1.9f, 0f), new Vector3(0.3f, 0.32f, 0.3f), Palette.White, TintB);
            mb.Blob(new Vector3(0f, 2.02f, -0.04f), new Vector3(0.31f, 0.2f, 0.31f), Palette.Hair, NoSnow);
            // nose to show heading
            mb.BoxMinMax(new Vector3(-0.05f, 1.86f, 0.26f), new Vector3(0.05f, 1.94f, 0.34f), Palette.White, TintB);
            return mb.ToMesh("Person");
        }

        public static Mesh Umbrella()
        {
            var mb = new MeshBuilder();
            mb.Cylinder(new Vector3(0.28f, 1.3f, 0.1f), 0.03f, 1.15f, 4, Palette.Metal, NoSnow);
            mb.Cone(new Vector3(0.28f, 2.35f, 0.1f), 0.95f, 0.42f, 8, Palette.White, TintA);
            mb.Cylinder(new Vector3(0.28f, 2.3f, 0.1f), 0.95f, 0.05f, 8, Palette.White, TintA, false, true);
            return mb.ToMesh("Umbrella");
        }

        public static Mesh Car(bool police)
        {
            var mb = new MeshBuilder();
            Color32 body = Palette.White;
            Vector4 tint = police ? NoSnow : TintA;
            mb.BoxMinMax(new Vector3(-0.95f, 0.3f, -2.2f), new Vector3(0.95f, 0.95f, 2.2f), body, tint);
            if (police) mb.BoxMinMax(new Vector3(-0.97f, 0.45f, -0.9f), new Vector3(0.97f, 0.8f, 0.9f), Palette.PoliceBlue, NoSnow);
            mb.BoxMinMax(new Vector3(-0.82f, 0.95f, -1.15f), new Vector3(0.82f, 1.5f, 0.95f), Palette.GlassDark, NoSnow);
            mb.BoxMinMax(new Vector3(-0.84f, 1.5f, -1.1f), new Vector3(0.84f, 1.6f, 0.9f), body, tint);
            // wheels
            float[] zs = { -1.35f, 1.35f };
            foreach (float z in zs)
            {
                mb.BoxMinMax(new Vector3(-1.0f, 0f, z - 0.36f), new Vector3(-0.72f, 0.62f, z + 0.36f), Palette.Tire, NoSnow);
                mb.BoxMinMax(new Vector3(0.72f, 0f, z - 0.36f), new Vector3(1.0f, 0.62f, z + 0.36f), Palette.Tire, NoSnow);
            }
            // lights
            mb.BoxMinMax(new Vector3(-0.8f, 0.6f, 2.18f), new Vector3(-0.45f, 0.8f, 2.26f), Palette.Headlight, Lamp);
            mb.BoxMinMax(new Vector3(0.45f, 0.6f, 2.18f), new Vector3(0.8f, 0.8f, 2.26f), Palette.Headlight, Lamp);
            mb.BoxMinMax(new Vector3(-0.8f, 0.6f, -2.26f), new Vector3(-0.5f, 0.78f, -2.18f), Palette.Red, new Vector4(0.5f, 0, 0, 0));
            mb.BoxMinMax(new Vector3(0.5f, 0.6f, -2.26f), new Vector3(0.8f, 0.78f, -2.18f), Palette.Red, new Vector4(0.5f, 0, 0, 0));
            return mb.ToMesh(police ? "PoliceCar" : "Car");
        }

        public static Mesh FireTruck()
        {
            var mb = new MeshBuilder();
            mb.BoxMinMax(new Vector3(-1.1f, 0.4f, -3.3f), new Vector3(1.1f, 2.1f, 1.7f), Palette.FireRed, NoSnow);
            mb.BoxMinMax(new Vector3(-1.1f, 0.4f, 1.7f), new Vector3(1.1f, 1.9f, 3.3f), Palette.FireRed, NoSnow);
            mb.BoxMinMax(new Vector3(-1.0f, 1.2f, 2.3f), new Vector3(1.0f, 1.8f, 3.32f), Palette.GlassDark, NoSnow);
            mb.BoxMinMax(new Vector3(-1.12f, 0.9f, -3.3f), new Vector3(1.12f, 1.1f, 3.3f), Palette.White, NoSnow);
            // ladder
            mb.BoxMinMax(new Vector3(-0.55f, 2.1f, -3.0f), new Vector3(-0.45f, 2.3f, 1.4f), Palette.MetalLight, NoSnow);
            mb.BoxMinMax(new Vector3(0.45f, 2.1f, -3.0f), new Vector3(0.55f, 2.3f, 1.4f), Palette.MetalLight, NoSnow);
            for (float z = -2.8f; z < 1.4f; z += 0.6f)
                mb.BoxMinMax(new Vector3(-0.5f, 2.15f, z), new Vector3(0.5f, 2.25f, z + 0.08f), Palette.MetalLight, NoSnow);
            float[] zs = { -2.3f, -0.9f, 2.3f };
            foreach (float z in zs)
            {
                mb.BoxMinMax(new Vector3(-1.15f, 0f, z - 0.4f), new Vector3(-0.85f, 0.75f, z + 0.4f), Palette.Tire, NoSnow);
                mb.BoxMinMax(new Vector3(0.85f, 0f, z - 0.4f), new Vector3(1.15f, 0.75f, z + 0.4f), Palette.Tire, NoSnow);
            }
            mb.BoxMinMax(new Vector3(-0.9f, 0.7f, 3.28f), new Vector3(-0.55f, 0.9f, 3.36f), Palette.Headlight, Lamp);
            mb.BoxMinMax(new Vector3(0.55f, 0.7f, 3.28f), new Vector3(0.9f, 0.9f, 3.36f), Palette.Headlight, Lamp);
            return mb.ToMesh("FireTruck");
        }

        /// <summary>One half of an emergency light bar (drawn additively with per-instance colour).</summary>
        public static Mesh SirenHalf(bool left, float y, float z)
        {
            var mb = new MeshBuilder();
            float x0 = left ? -0.62f : 0.02f, x1 = left ? -0.02f : 0.62f;
            mb.BoxMinMax(new Vector3(x0, y, z - 0.16f), new Vector3(x1, y + 0.22f, z + 0.16f), Palette.White, NoSnow, true);
            // halo quad
            mb.Rect(x0 - 0.6f, z - 0.9f, x1 + 0.6f, z + 0.9f, y + 0.12f, Palette.White, NoSnow, true);
            return mb.ToMesh(left ? "SirenL" : "SirenR");
        }

        public static Mesh Ring(float radius, float width, int segments)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                mb.Quad(d0 * radius, d0 * (radius + width), d1 * (radius + width), d1 * radius, Palette.White, NoSnow, Vector3.up);
            }
            return mb.ToMesh("Ring");
        }

        // ------------------------------------------------------------------ props

        /// <summary>Adds a prop. Trunks and hard parts go to <paramref name="city"/>, leaves to foliage/conifer builders.</summary>
        public static void AddProp(Prop p, MeshBuilder city, MeshBuilder foliage, MeshBuilder conifer, MeshBuilder glow)
        {
            var rnd = new DetRandom((ulong)(p.X * 1000f) * 31UL + (ulong)(p.Z * 1000f + 100000f) + (ulong)p.Variant);
            var pos = new Vector3(p.X, p.Y, p.Z);
            float s = p.Scale <= 0f ? 1f : p.Scale;
            city.SetTransform(pos, p.Yaw, Vector3.one * s);
            foliage.SetTransform(pos, p.Yaw, Vector3.one * s);
            conifer.SetTransform(pos, p.Yaw, Vector3.one * s);
            switch (p.Kind)
            {
                case PropKind.Tree:
                {
                    city.Cylinder(Vector3.zero, 0.28f, 2.4f, 6, Palette.Trunk, MeshBuilder.NoSnow);
                    float g = rnd.Range(0.82f, 1f);
                    var col = new Color32((byte)Mathf.Clamp(255f * g, 0f, 255f), (byte)Mathf.Clamp(255f * g, 0f, 255f), (byte)Mathf.Clamp(255f * rnd.Range(0.8f, 0.95f), 0f, 255f), 255);
                    foliage.Blob(new Vector3(0f, 3.4f, 0f), new Vector3(1.8f, 1.6f, 1.8f), col, MeshBuilder.Plain, rnd.Range(1f, 9f));
                    foliage.Blob(new Vector3(rnd.Range(-0.6f, 0.6f), 4.4f, rnd.Range(-0.6f, 0.6f)), new Vector3(1.2f, 1.1f, 1.2f), col, MeshBuilder.Plain, rnd.Range(1f, 9f));
                    // bare branches visible in winter
                    city.BoxMinMax(new Vector3(-0.08f, 2.2f, -0.08f), new Vector3(0.08f, 3.8f, 0.08f), Palette.Trunk, MeshBuilder.Plain);
                    city.SetTransform(pos, p.Yaw + 90f, Vector3.one * s);
                    city.BoxMinMax(new Vector3(-0.06f, 2.6f, -0.9f), new Vector3(0.06f, 2.72f, 0.9f), Palette.Trunk, MeshBuilder.Plain);
                    break;
                }
                case PropKind.Conifer:
                    city.Cylinder(Vector3.zero, 0.25f, 1.2f, 6, Palette.Trunk, MeshBuilder.NoSnow);
                    conifer.Cone(new Vector3(0, 1.0f, 0), 1.7f, 2.2f, 7, Palette.White, MeshBuilder.Plain);
                    conifer.Cone(new Vector3(0, 2.4f, 0), 1.35f, 2.0f, 7, Palette.White, MeshBuilder.Plain);
                    conifer.Cone(new Vector3(0, 3.7f, 0), 0.95f, 1.9f, 7, Palette.White, MeshBuilder.Plain);
                    break;
                case PropKind.Bush:
                    foliage.Blob(new Vector3(0, 0.6f, 0), new Vector3(1.0f, 0.75f, 1.0f), Palette.White, MeshBuilder.Plain, rnd.Range(1f, 9f));
                    break;
                case PropKind.Lamp:
                    city.Cylinder(Vector3.zero, 0.09f, 4.2f, 6, Palette.Metal, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-0.06f, 4.1f, 0f), new Vector3(0.06f, 4.2f, 0.7f), Palette.Metal, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-0.22f, 3.85f, 0.5f), new Vector3(0.22f, 4.1f, 0.95f), Palette.Headlight, new Vector4(1f, 0, 0, 0));
                    glow.SetTransform(pos, p.Yaw, Vector3.one);
                    glow.Rect(-2.6f, -1.9f, 2.6f, 3.3f, 0.06f, new Color32(255, 214, 150, 255), MeshBuilder.NoSnow, true);
                    glow.Rect(-0.7f, 0.0f, 0.7f, 1.4f, 3.8f, new Color32(255, 230, 180, 255), MeshBuilder.NoSnow, true);
                    break;
                case PropKind.Bench:
                    city.BoxMinMax(new Vector3(-0.9f, 0.45f, -0.25f), new Vector3(0.9f, 0.55f, 0.25f), Palette.Wood, MeshBuilder.Plain);
                    city.BoxMinMax(new Vector3(-0.9f, 0.55f, -0.3f), new Vector3(0.9f, 1.0f, -0.22f), Palette.Wood, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-0.8f, 0f, -0.2f), new Vector3(-0.7f, 0.45f, 0.2f), Palette.Metal, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(0.7f, 0f, -0.2f), new Vector3(0.8f, 0.45f, 0.2f), Palette.Metal, MeshBuilder.NoSnow);
                    break;
                case PropKind.Stall:
                {
                    Color32 aw = (p.Variant % 4) switch { 0 => Palette.Awning1, 1 => Palette.Awning2, 2 => Palette.Awning3, _ => Palette.Awning4 };
                    city.BoxMinMax(new Vector3(-1.3f, 0f, -0.7f), new Vector3(1.3f, 1.0f, 0.7f), Palette.Wood, MeshBuilder.Plain);
                    city.BoxMinMax(new Vector3(-1.2f, 1.0f, -0.5f), new Vector3(1.2f, 1.2f, 0.4f), Palette.Awning4, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-1.3f, 1.0f, -0.7f), new Vector3(-1.2f, 2.3f, -0.6f), Palette.Wood, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(1.2f, 1.0f, -0.7f), new Vector3(1.3f, 2.3f, -0.6f), Palette.Wood, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-1.3f, 1.0f, 0.6f), new Vector3(-1.2f, 2.3f, 0.7f), Palette.Wood, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(1.2f, 1.0f, 0.6f), new Vector3(1.3f, 2.3f, 0.7f), Palette.Wood, MeshBuilder.NoSnow);
                    city.GableRoof(2.6f, 1.4f, 2.3f, 0.5f, 0.2f, true, aw, Palette.White, MeshBuilder.Plain);
                    break;
                }
                case PropKind.Flowerbed:
                {
                    city.Cylinder(Vector3.zero, 1.2f, 0.3f, 10, Palette.StoneDark, MeshBuilder.NoSnow, false);
                    city.Cylinder(Vector3.zero, 1.05f, 0.34f, 10, Palette.SoilTop, MeshBuilder.Plain);
                    Color32[] fl = { Palette.Awning1, Palette.Awning4, Palette.C(0xE98BB0), Palette.White, Palette.C(0x8A7FD1) };
                    for (int i = 0; i < 9; i++)
                    {
                        float a = i * 0.7f, r = 0.25f + (i % 3) * 0.28f;
                        city.Blob(new Vector3(Mathf.Cos(a) * r, 0.45f, Mathf.Sin(a) * r), new Vector3(0.16f, 0.14f, 0.16f), fl[i % fl.Length], MeshBuilder.NoSnow);
                    }
                    break;
                }
                case PropKind.Playground:
                    city.BoxMinMax(new Vector3(-3.5f, 0f, -3f), new Vector3(3.5f, 0.04f, 3f), Palette.Court, MeshBuilder.Plain);
                    // swing
                    city.BoxMinMax(new Vector3(-2.8f, 0f, -0.1f), new Vector3(-2.6f, 2.4f, 0.1f), Palette.Awning2, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-0.2f, 0f, -0.1f), new Vector3(0f, 2.4f, 0.1f), Palette.Awning2, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-2.8f, 2.3f, -0.1f), new Vector3(0f, 2.5f, 0.1f), Palette.Awning2, MeshBuilder.NoSnow);
                    city.BoxMinMax(new Vector3(-1.9f, 0.6f, -0.25f), new Vector3(-0.9f, 0.7f, 0.25f), Palette.Awning1, MeshBuilder.NoSnow);
                    // slide
                    city.BoxMinMax(new Vector3(1.2f, 0f, -1.8f), new Vector3(2.4f, 1.8f, -0.8f), Palette.Awning4, MeshBuilder.Plain);
                    city.Quad(new Vector3(1.3f, 1.8f, -0.8f), new Vector3(1.3f, 0.1f, 1.8f), new Vector3(2.3f, 0.1f, 1.8f), new Vector3(2.3f, 1.8f, -0.8f), Palette.Awning1, MeshBuilder.Plain, Vector3.up);
                    break;
                case PropKind.Fountain:
                    city.Cylinder(Vector3.zero, 2f, 0.6f, 12, Palette.Stone, MeshBuilder.Plain);
                    break;
            }
            city.Identity();
            foliage.Identity();
            conifer.Identity();
            glow.Identity();
        }
    }
}
