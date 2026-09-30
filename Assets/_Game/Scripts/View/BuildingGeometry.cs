using SmallTown.Simulation.World;
using SmallTown.Utils;
using UnityEngine;

namespace SmallTown.View
{
    /// <summary>Generates the low-poly model of every building type into a shared mesh builder.</summary>
    public static class BuildingGeometry
    {
        private static readonly Vector4 Plain = MeshBuilder.Plain;
        private static readonly Vector4 NoSnow = MeshBuilder.NoSnow;

        private static Vector3 FaceNormal(Side s)
        {
            switch (s)
            {
                case Side.South: return Vector3.back;
                case Side.North: return Vector3.forward;
                case Side.East: return Vector3.right;
                default: return Vector3.left;
            }
        }

        /// <summary>Vertical quad centred on a wall.</summary>
        private static void WallQuad(MeshBuilder mb, Vector3 center, Vector3 normal, float w, float h, Color32 col, Vector4 uv1)
        {
            Vector3 r = Vector3.Cross(Vector3.up, normal).normalized * (w * 0.5f);
            Vector3 u = Vector3.up * (h * 0.5f);
            mb.Quad(center - r - u, center - r + u, center + r + u, center + r - u, col, uv1, normal);
        }

        private static Vector4 WindowUv(DetRandom rnd)
        {
            float lit = rnd.Chance(0.58f) ? rnd.Range(0.55f, 1f) : 0f;
            return new Vector4(lit, 0f, 0f, 0f);
        }

        /// <summary>Rows of framed windows on the four walls of a box [-hx,hx]x[0,h]x[-hz,hz].</summary>
        private static void Windows(MeshBuilder mb, DetRandom rnd, float hx, float hz, int floors, float floorH, float firstY, float winW, float winH, float spacing, Side door, bool skipDoorColumn)
        {
            for (int s = 0; s < 4; s++)
            {
                var side = (Side)s;
                Vector3 n = FaceNormal(side);
                float half = (side == Side.South || side == Side.North) ? hx : hz;
                float offset = (side == Side.South || side == Side.North) ? hz : hx;
                int count = Mathf.Max(1, Mathf.FloorToInt((half * 2f - 0.8f) / spacing));
                float step = (half * 2f) / count;
                for (int f = 0; f < floors; f++)
                {
                    float y = firstY + f * floorH;
                    for (int k = 0; k < count; k++)
                    {
                        float t = -half + step * (k + 0.5f);
                        if (f == 0 && side == door && skipDoorColumn && Mathf.Abs(t) < step * 0.6f) continue;
                        Vector3 along = (side == Side.South || side == Side.North) ? new Vector3(t, 0, 0) : new Vector3(0, 0, t);
                        Vector3 c = n * (offset + 0.03f) + along + Vector3.up * y;
                        WallQuad(mb, c, n, winW + 0.24f, winH + 0.24f, Palette.White, NoSnow);
                        WallQuad(mb, c + n * 0.02f, n, winW, winH, Palette.Glass, WindowUv(rnd));
                    }
                }
            }
        }

        private static void Door(MeshBuilder mb, float hx, float hz, Side door, float width = 1.1f, float height = 2.1f)
        {
            Vector3 n = FaceNormal(door);
            float offset = (door == Side.South || door == Side.North) ? hz : hx;
            WallQuad(mb, n * (offset + 0.05f) + Vector3.up * (height * 0.5f), n, width, height, Palette.Door, NoSnow);
            WallQuad(mb, n * (offset + 0.06f) + Vector3.up * (height + 0.12f), n, width + 0.4f, 0.2f, Palette.White, NoSnow);
            var stepC = n * (offset + 0.45f) + Vector3.up * 0.06f;
            Vector3 size = (door == Side.South || door == Side.North) ? new Vector3(width + 0.6f, 0.12f, 0.8f) : new Vector3(0.8f, 0.12f, width + 0.6f);
            mb.Box(stepC, size, Palette.StoneDark, Plain);
        }

        private static void Parapet(MeshBuilder mb, float hx, float hz, float y, float height, Color32 col)
        {
            float t = 0.22f;
            mb.BoxMinMax(new Vector3(-hx, y, -hz), new Vector3(hx, y + height, -hz + t), col, Plain);
            mb.BoxMinMax(new Vector3(-hx, y, hz - t), new Vector3(hx, y + height, hz), col, Plain);
            mb.BoxMinMax(new Vector3(-hx, y, -hz + t), new Vector3(-hx + t, y + height, hz - t), col, Plain);
            mb.BoxMinMax(new Vector3(hx - t, y, -hz + t), new Vector3(hx, y + height, hz - t), col, Plain);
            mb.Rect(-hx + t, -hz + t, hx - t, hz - t, y + 0.02f, Palette.C(0xBDB7AE), Plain);
        }

        public static void Add(MeshBuilder mb, Building b, CityData city)
        {
            var rnd = new DetRandom(b.Style * 2654435761UL + 97UL);
            float baseY = b.Type == BuildingType.Lighthouse ? CityData.TerraceY : CityData.CurbY;
            mb.SetTransform(new Vector3(b.X, baseY, b.Z), 0f);
            float hx = b.SizeX * 0.5f, hz = b.SizeZ * 0.5f, h = b.Height;
            Color32 facade = Palette.Facades[rnd.Next(Palette.Facades.Length)];
            Color32 roof = Palette.Roofs[rnd.Next(Palette.Roofs.Length)];
            switch (b.Type)
            {
                case BuildingType.House: House(mb, rnd, b, hx, hz, h, facade, roof); break;
                case BuildingType.Apartment: Apartment(mb, rnd, b, hx, hz, h, facade); break;
                case BuildingType.Office: Office(mb, rnd, b, hx, hz, h); break;
                case BuildingType.Shop: Shop(mb, rnd, b, hx, hz, h, facade, roof); break;
                case BuildingType.School: School(mb, rnd, b, hx, hz, h); break;
                case BuildingType.Bank: Bank(mb, rnd, b, hx, hz, h); break;
                case BuildingType.FireStation: FireStation(mb, rnd, b, hx, hz, h); break;
                case BuildingType.Police: Police(mb, rnd, b, hx, hz, h); break;
                case BuildingType.Church: Church(mb, rnd, b, hx, hz, h); break;
                case BuildingType.MarketHall: MarketHall(mb, rnd, b, hx, hz, h); break;
                case BuildingType.WaterTower: WaterTower(mb, hx, hz); break;
                case BuildingType.Lighthouse: Lighthouse(mb); break;
            }
            mb.Identity();
        }

        private static void House(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h, Color32 facade, Color32 roof)
        {
            mb.BoxMinMax(new Vector3(-hx - 0.1f, 0f, -hz - 0.1f), new Vector3(hx + 0.1f, 0.35f, hz + 0.1f), Palette.StoneDark, Plain);
            mb.BoxMinMax(new Vector3(-hx, 0.35f, -hz), new Vector3(hx, h, hz), facade, Plain);
            bool ridgeX = b.SizeX >= b.SizeZ;
            float rh = Mathf.Min(b.SizeX, b.SizeZ) * 0.42f;
            mb.GableRoof(b.SizeX, b.SizeZ, h, rh, 0.45f, ridgeX, roof, facade, Plain);
            Windows(mb, rnd, hx, hz, b.Floors, 3.1f, 1.7f, 1.0f, 1.15f, 2.4f, b.Facing, true);
            Door(mb, hx, hz, b.Facing);
            if (rnd.Chance(0.45f))
            {
                float cx = ridgeX ? hx * 0.45f : hx * 0.35f, cz = ridgeX ? hz * 0.35f : hz * 0.45f;
                mb.BoxMinMax(new Vector3(cx - 0.3f, h + rh * 0.3f, cz - 0.3f), new Vector3(cx + 0.3f, h + rh + 0.6f, cz + 0.3f), Palette.C(0xA9786A), Plain);
            }
            if (rnd.Chance(0.4f))
            {
                // Solar panels on the south (or west) slope.
                float hd = (ridgeX ? hz : hx) + 0.45f;
                float span = (ridgeX ? hx : hz) * 0.62f;
                float t0 = 0.22f, t1 = 0.78f;
                Vector3 P(float along, float t)
                {
                    float y = h + rh * t + 0.07f;
                    float across = -hd + hd * t;
                    return ridgeX ? new Vector3(along, y, across) : new Vector3(across, y, along);
                }
                Vector3 hint = ridgeX ? new Vector3(0, 1, -1) : new Vector3(-1, 1, 0);
                int panels = 3;
                float w = span * 2f / panels;
                for (int i = 0; i < panels; i++)
                {
                    float a0 = -span + i * w + 0.08f, a1 = -span + (i + 1) * w - 0.08f;
                    mb.Quad(P(a0, t0), P(a0, t1), P(a1, t1), P(a1, t0), Palette.Solar, new Vector4(0, 0, 0, 0.4f), hint);
                }
            }
        }

        private static void Apartment(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h, Color32 facade)
        {
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0.6f, hz), Palette.StoneDark, Plain);
            mb.BoxMinMax(new Vector3(-hx, 0.6f, -hz), new Vector3(hx, h, hz), facade, Plain);
            Parapet(mb, hx, hz, h, 0.55f, Palette.Stone);
            Windows(mb, rnd, hx, hz, b.Floors, 3.0f, 1.9f, 1.1f, 1.3f, 2.3f, b.Facing, true);
            Door(mb, hx, hz, b.Facing, 1.6f, 2.3f);
            // canopy over the entrance
            Vector3 n = FaceNormal(b.Facing);
            float off = (b.Facing == Side.South || b.Facing == Side.North) ? hz : hx;
            Vector3 cc = n * (off + 0.8f) + Vector3.up * 2.7f;
            Vector3 cs = (b.Facing == Side.South || b.Facing == Side.North) ? new Vector3(3f, 0.15f, 1.6f) : new Vector3(1.6f, 0.15f, 3f);
            mb.Box(cc, cs, Palette.Metal, Plain);
            if (rnd.Chance(0.55f))
            {
                // balconies on the facing side
                float half = (b.Facing == Side.South || b.Facing == Side.North) ? hx : hz;
                for (int f = 1; f < b.Floors; f++)
                    for (float t = -half + 2.5f; t < half - 1.5f; t += 4.6f)
                    {
                        Vector3 along = (b.Facing == Side.South || b.Facing == Side.North) ? new Vector3(t, 0, 0) : new Vector3(0, 0, t);
                        Vector3 c = n * (off + 0.45f) + along + Vector3.up * (f * 3.0f + 0.9f);
                        Vector3 size = (b.Facing == Side.South || b.Facing == Side.North) ? new Vector3(1.9f, 0.9f, 0.9f) : new Vector3(0.9f, 0.9f, 1.9f);
                        mb.Box(c, size, Palette.White, Plain);
                    }
            }
            if (rnd.Chance(0.6f))
            {
                // rooftop solar array
                for (float z = -hz + 1.3f; z < hz - 1.6f; z += 2.2f)
                    mb.Quad(new Vector3(-hx + 1.2f, h + 0.3f, z), new Vector3(-hx + 1.2f, h + 1.0f, z + 1.3f), new Vector3(hx - 1.2f, h + 1.0f, z + 1.3f), new Vector3(hx - 1.2f, h + 0.3f, z), Palette.Solar, new Vector4(0, 0, 0, 0.4f), new Vector3(0, 1, -1));
            }
            else
            {
                mb.Cylinder(new Vector3(hx * 0.4f, h, hz * 0.2f), 1.2f, 2.2f, 10, Palette.MetalLight, Plain);
                mb.BoxMinMax(new Vector3(-hx * 0.6f, h, -1.2f), new Vector3(-hx * 0.6f + 2.4f, h + 2.4f, 1.2f), Palette.Stone, Plain);
            }
        }

        private static void Office(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 body = rnd.Chance(0.5f) ? Palette.C(0xE9EEF2) : Palette.C(0xF1E8DA);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), body, Plain);
            float fh = 3.4f;
            for (int f = 0; f < b.Floors; f++)
            {
                float y = f * fh + 1.9f;
                for (int s = 0; s < 4; s++)
                {
                    var side = (Side)s;
                    Vector3 n = FaceNormal(side);
                    float half = (side == Side.South || side == Side.North) ? hx : hz;
                    float off = (side == Side.South || side == Side.North) ? hz : hx;
                    int segs = Mathf.Max(1, Mathf.FloorToInt(half * 2f / 3f));
                    float seg = (half * 2f - 1.2f) / segs;
                    for (int k = 0; k < segs; k++)
                    {
                        float t = -half + 0.6f + seg * (k + 0.5f);
                        if (f == 0 && side == b.Facing && Mathf.Abs(t) < seg) continue;
                        Vector3 along = (side == Side.South || side == Side.North) ? new Vector3(t, 0, 0) : new Vector3(0, 0, t);
                        WallQuad(mb, n * (off + 0.03f) + along + Vector3.up * y, n, seg - 0.25f, 1.7f, Palette.Glass, WindowUv(rnd));
                    }
                }
            }
            Door(mb, hx, hz, b.Facing, 2.2f, 2.5f);
            Parapet(mb, hx, hz, h, 0.5f, body);
            mb.BoxMinMax(new Vector3(-1.6f, h, -1.4f), new Vector3(1.6f, h + 2.2f, 1.4f), Palette.MetalLight, Plain);
            mb.Cylinder(new Vector3(hx * 0.5f, h, 0f), 0.06f, 4f, 4, Palette.Metal, NoSnow);
        }

        private static void Shop(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h, Color32 facade, Color32 roof)
        {
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), facade, Plain);
            Vector3 n = FaceNormal(b.Facing);
            bool sn = b.Facing == Side.South || b.Facing == Side.North;
            float half = sn ? hx : hz, off = sn ? hz : hx;
            WallQuad(mb, n * (off + 0.04f) + Vector3.up * 1.45f, n, half * 1.4f, 1.9f, Palette.Glass, new Vector4(0.9f, 0, 0, 0));
            WallQuad(mb, n * (off + 0.05f) + Vector3.up * 1.2f + (sn ? Vector3.right : Vector3.forward) * (half * 0.78f), n, 1.0f, 2.3f, Palette.Door, NoSnow);
            // striped awning
            Color32[] aw = { Palette.Awning1, Palette.Awning2, Palette.Awning3, Palette.Awning4 };
            Color32 a1 = aw[rnd.Next(aw.Length)];
            int stripes = 8;
            for (int i = 0; i < stripes; i++)
            {
                float t0 = -half * 0.85f + i * (half * 1.7f / stripes), t1 = t0 + half * 1.7f / stripes;
                Color32 c = i % 2 == 0 ? a1 : Palette.White;
                Vector3 a = n * off + Vector3.up * 3.0f, bb = n * (off + 1.3f) + Vector3.up * 2.5f;
                Vector3 d0 = sn ? new Vector3(t0, 0, 0) : new Vector3(0, 0, t0), d1 = sn ? new Vector3(t1, 0, 0) : new Vector3(0, 0, t1);
                mb.Quad(a + d0, bb + d0, bb + d1, a + d1, c, Plain, Vector3.up + n);
            }
            // sign board
            Vector3 signC = n * (off + 0.12f) + Vector3.up * 3.45f;
            mb.Box(signC, sn ? new Vector3(half * 1.2f, 0.55f, 0.2f) : new Vector3(0.2f, 0.55f, half * 1.2f), a1, NoSnow);
            if (b.Floors > 1)
                Windows(mb, rnd, hx, hz, b.Floors - 1, 3.3f, 5.0f, 1.0f, 1.2f, 2.4f, b.Facing, false);
            if (rnd.Chance(0.5f))
                mb.GableRoof(b.SizeX, b.SizeZ, h, Mathf.Min(b.SizeX, b.SizeZ) * 0.35f, 0.3f, b.SizeX >= b.SizeZ, roof, facade, Plain);
            else
                Parapet(mb, hx, hz, h, 0.45f, facade);
        }

        private static void School(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 body = Palette.C(0xF3DDA6);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0.5f, hz), Palette.StoneDark, Plain);
            mb.BoxMinMax(new Vector3(-hx, 0.5f, -hz), new Vector3(hx, h, hz), body, Plain);
            mb.GableRoof(b.SizeX, b.SizeZ, h, 3.0f, 0.45f, b.SizeX >= b.SizeZ, Palette.C(0xB35B4F), body, Plain);
            Windows(mb, rnd, hx, hz, 2, 3.4f, 1.9f, 1.4f, 1.5f, 2.3f, b.Facing, true);
            Door(mb, hx, hz, b.Facing, 2.2f, 2.5f);
            Vector3 n = FaceNormal(b.Facing);
            bool sn = b.Facing == Side.South || b.Facing == Side.North;
            float off = sn ? hz : hx;
            Vector3 porch = n * (off + 1.2f);
            mb.Box(porch + Vector3.up * 3.0f, sn ? new Vector3(4.2f, 0.25f, 2.4f) : new Vector3(2.4f, 0.25f, 4.2f), Palette.White, Plain);
            Vector3 side = sn ? Vector3.right : Vector3.forward;
            mb.Cylinder(porch + n * 0.9f + side * 1.8f, 0.16f, 3.0f, 6, Palette.White, NoSnow);
            mb.Cylinder(porch + n * 0.9f - side * 1.8f, 0.16f, 3.0f, 6, Palette.White, NoSnow);
            // clock tower
            float ty = h + 2.2f;
            mb.BoxMinMax(new Vector3(-1.5f, h, -1.5f), new Vector3(1.5f, ty + 2.4f, 1.5f), body, Plain);
            mb.Pyramid(new Vector3(0, ty + 2.4f, 0), 3.6f, 3.6f, 2.6f, Palette.C(0xB35B4F), Plain);
            WallQuad(mb, n * 1.53f + Vector3.up * (ty + 1.2f), n, 1.5f, 1.5f, Palette.White, new Vector4(0.6f, 0, 0, 0));
            WallQuad(mb, n * 1.55f + Vector3.up * (ty + 1.3f), n, 0.12f, 0.6f, Palette.Metal, NoSnow);
        }

        private static void Bank(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 body = Palette.C(0xF1EBDD);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), body, Plain);
            mb.BoxMinMax(new Vector3(-hx - 0.3f, h, -hz - 0.3f), new Vector3(hx + 0.3f, h + 0.5f, hz + 0.3f), Palette.Stone, Plain);
            Windows(mb, rnd, hx, hz, 2, 3.6f, 2.0f, 1.1f, 1.8f, 2.3f, b.Facing, true);
            Door(mb, hx, hz, b.Facing, 1.8f, 2.8f);
            Vector3 n = FaceNormal(b.Facing);
            bool sn = b.Facing == Side.South || b.Facing == Side.North;
            float off = sn ? hz : hx, half = sn ? hx : hz;
            Vector3 side = sn ? Vector3.right : Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                float t = -half * 0.75f + i * (half * 1.5f / 3f);
                mb.Cylinder(n * (off + 1.0f) + side * t + Vector3.up * 0.5f, 0.3f, h - 0.5f, 8, Palette.White, NoSnow);
            }
            // steps and portico roof
            mb.Box(n * (off + 1.3f) + Vector3.up * 0.25f, sn ? new Vector3(half * 1.9f, 0.5f, 2.6f) : new Vector3(2.6f, 0.5f, half * 1.9f), Palette.Stone, Plain);
            mb.Box(n * (off + 1.1f) + Vector3.up * (h + 0.1f), sn ? new Vector3(half * 1.9f, 0.4f, 2.4f) : new Vector3(2.4f, 0.4f, half * 1.9f), Palette.Stone, Plain);
            // pediment
            Vector3 pc = n * (off + 1.1f) + Vector3.up * (h + 0.3f);
            Vector3 w = side * (half * 0.95f);
            mb.Tri(pc - w, pc + Vector3.up * 1.6f, pc + w, Palette.White, NoSnow, n);
            mb.Quad(pc - w, pc - w - n * 2.2f, pc - n * 2.2f + Vector3.up * 1.6f, pc + Vector3.up * 1.6f, Palette.Stone, Plain, Vector3.up - side);
            mb.Quad(pc + w, pc + Vector3.up * 1.6f, pc - n * 2.2f + Vector3.up * 1.6f, pc + w - n * 2.2f, Palette.Stone, Plain, Vector3.up + side);
            WallQuad(mb, n * (off + 0.06f) + Vector3.up * 3.5f, n, 2.4f, 0.5f, Palette.C(0xD9B45A), NoSnow);
        }

        private static void FireStation(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 brick = Palette.C(0xC8574A);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), brick, Plain);
            Parapet(mb, hx, hz, h, 0.5f, Palette.C(0xE9DCCB));
            Vector3 n = FaceNormal(b.Facing);
            bool sn = b.Facing == Side.South || b.Facing == Side.North;
            float off = sn ? hz : hx, half = sn ? hx : hz;
            Vector3 side = sn ? Vector3.right : Vector3.forward;
            for (int i = 0; i < 2; i++)
            {
                float t = (i == 0 ? -1f : 1f) * half * 0.45f;
                WallQuad(mb, n * (off + 0.04f) + side * t + Vector3.up * 1.8f, n, 3.4f, 3.6f, Palette.White, NoSnow);
                for (int k = 0; k < 4; k++)
                    WallQuad(mb, n * (off + 0.06f) + side * t + Vector3.up * (0.6f + k * 0.85f), n, 3.3f, 0.12f, Palette.MetalLight, NoSnow);
            }
            for (int s = 0; s < 4; s++)
            {
                var sd = (Side)s;
                if (sd == b.Facing) continue;
                var nn = FaceNormal(sd);
                float o2 = (sd == Side.South || sd == Side.North) ? hz : hx;
                WallQuad(mb, nn * (o2 + 0.03f) + Vector3.up * 5.2f, nn, 1.3f, 1.3f, Palette.Glass, WindowUv(rnd));
            }
            WallQuad(mb, n * (off + 0.05f) + Vector3.up * 5.3f, n, half * 1.3f, 0.8f, Palette.White, NoSnow);
            // hose tower
            Vector3 tc = -n * (off - 1.4f) + side * (half - 1.4f);
            mb.BoxMinMax(tc + new Vector3(-1.3f, 0f, -1.3f), tc + new Vector3(1.3f, h + 5.5f, 1.3f), brick, Plain);
            mb.Pyramid(tc + Vector3.up * (h + 5.5f), 3.0f, 3.0f, 2.0f, Palette.C(0x5E5A66), Plain);
        }

        private static void Police(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 body = Palette.C(0xEEF1F5);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), body, Plain);
            mb.BoxMinMax(new Vector3(-hx - 0.05f, 3.0f, -hz - 0.05f), new Vector3(hx + 0.05f, 3.5f, hz + 0.05f), Palette.PoliceBlue, NoSnow);
            Parapet(mb, hx, hz, h, 0.45f, body);
            Windows(mb, rnd, hx, hz, 2, 3.6f, 1.7f, 1.2f, 1.2f, 2.4f, b.Facing, true);
            Door(mb, hx, hz, b.Facing, 1.8f, 2.4f);
            Vector3 n = FaceNormal(b.Facing);
            bool sn = b.Facing == Side.South || b.Facing == Side.North;
            float off = sn ? hz : hx, half = sn ? hx : hz;
            Vector3 side = sn ? Vector3.right : Vector3.forward;
            mb.Box(n * (off + 0.9f) + Vector3.up * 2.75f, sn ? new Vector3(3.4f, 0.2f, 1.8f) : new Vector3(1.8f, 0.2f, 3.4f), Palette.PoliceBlue, Plain);
            Vector3 pole = n * (off + 2.2f) + side * (half - 0.6f);
            mb.Cylinder(pole, 0.07f, 7.5f, 5, Palette.MetalLight, NoSnow);
            mb.Quad(pole + Vector3.up * 7.3f, pole + Vector3.up * 6.2f, pole + Vector3.up * 6.2f - side * 1.8f, pole + Vector3.up * 7.3f - side * 1.8f, Palette.PoliceBlue, NoSnow, n);
            mb.Quad(pole + Vector3.up * 7.3f, pole + Vector3.up * 6.2f, pole + Vector3.up * 6.2f - side * 1.8f, pole + Vector3.up * 7.3f - side * 1.8f, Palette.PoliceBlue, NoSnow, -n);
            mb.Box(new Vector3(0, h + 0.6f, 0), new Vector3(1.2f, 0.5f, 1.2f), Palette.PoliceBlue, new Vector4(0.8f, 0, 0, 0));
        }

        private static void Church(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            Color32 body = Palette.C(0xF6F1E7);
            Color32 roof = Palette.C(0x5E6B7A);
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h, hz), body, Plain);
            mb.GableRoof(b.SizeX, b.SizeZ, h, 4.0f, 0.35f, false, roof, body, Plain);
            // tall windows along the nave
            for (float z = -hz + 3.5f; z < hz - 1f; z += 2.8f)
            {
                WallQuad(mb, new Vector3(-hx - 0.03f, 4.0f, z), Vector3.left, 1.0f, 3.2f, Palette.C(0xE7C77F), new Vector4(0.8f, 0, 0, 0));
                WallQuad(mb, new Vector3(hx + 0.03f, 4.0f, z), Vector3.right, 1.0f, 3.2f, Palette.C(0xE7C77F), new Vector4(0.8f, 0, 0, 0));
            }
            // tower at the front (south)
            float tz = -hz + 1.9f;
            float th = h + 8.5f;
            mb.BoxMinMax(new Vector3(-1.9f, 0f, tz - 1.9f - 0.8f), new Vector3(1.9f, th, tz + 1.9f - 0.8f), body, Plain);
            mb.Pyramid(new Vector3(0f, th, tz - 0.8f), 4.2f, 4.2f, 9.5f, roof, Plain);
            WallQuad(mb, new Vector3(0f, th - 2.4f, tz - 2.73f), Vector3.back, 1.2f, 2.0f, Palette.GlassDark, NoSnow);
            WallQuad(mb, new Vector3(0f, 6.2f, tz - 2.73f), Vector3.back, 1.8f, 1.8f, Palette.C(0xE7A76F), new Vector4(0.9f, 0, 0, 0));
            WallQuad(mb, new Vector3(0f, 1.4f, tz - 2.73f), Vector3.back, 1.6f, 2.8f, Palette.Door, NoSnow);
            float cy = th + 9.5f;
            mb.BoxMinMax(new Vector3(-0.09f, cy - 0.2f, tz - 0.9f), new Vector3(0.09f, cy + 1.6f, tz - 0.7f), Palette.C(0xD9B45A), NoSnow);
            mb.BoxMinMax(new Vector3(-0.5f, cy + 0.8f, tz - 0.9f), new Vector3(0.5f, cy + 1.0f, tz - 0.7f), Palette.C(0xD9B45A), NoSnow);
        }

        private static void MarketHall(MeshBuilder mb, DetRandom rnd, Building b, float hx, float hz, float h)
        {
            mb.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0.3f, hz), Palette.Stone, Plain);
            for (int i = 0; i < 4; i++)
            {
                float x = -hx + 0.6f + i * ((hx * 2f - 1.2f) / 3f);
                mb.Cylinder(new Vector3(x, 0.3f, -hz + 0.6f), 0.28f, h - 0.3f, 8, Palette.White, NoSnow);
                mb.Cylinder(new Vector3(x, 0.3f, hz - 0.6f), 0.28f, h - 0.3f, 8, Palette.White, NoSnow);
            }
            mb.BoxMinMax(new Vector3(-hx, h - 0.4f, -hz), new Vector3(hx, h, hz), Palette.Wood, Plain);
            mb.GableRoof(b.SizeX, b.SizeZ, h, 3.2f, 1.2f, true, Palette.C(0xC96F5B), Palette.Wood, Plain);
            Color32[] crates = { Palette.Awning1, Palette.Awning4, Palette.Awning3, Palette.C(0xE98BB0) };
            for (int i = 0; i < 5; i++)
            {
                float x = -hx + 2f + i * ((hx * 2f - 4f) / 4f);
                mb.BoxMinMax(new Vector3(x - 0.9f, 0.3f, -0.8f), new Vector3(x + 0.9f, 1.2f, 0.8f), Palette.Wood, Plain);
                mb.BoxMinMax(new Vector3(x - 0.7f, 1.2f, -0.6f), new Vector3(x + 0.7f, 1.45f, 0.6f), crates[i % crates.Length], NoSnow);
            }
        }

        private static void WaterTower(MeshBuilder mb, float hx, float hz)
        {
            float legH = 10f, s = 1.9f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * s, z = (i < 2 ? -1 : 1) * s;
                mb.BoxMinMax(new Vector3(x - 0.18f, 0f, z - 0.18f), new Vector3(x + 0.18f, legH, z + 0.18f), Palette.Metal, NoSnow);
            }
            for (int k = 1; k <= 2; k++)
            {
                float y = legH * k / 3f;
                mb.BoxMinMax(new Vector3(-s, y, -s - 0.08f), new Vector3(s, y + 0.15f, -s + 0.08f), Palette.Metal, NoSnow);
                mb.BoxMinMax(new Vector3(-s, y, s - 0.08f), new Vector3(s, y + 0.15f, s + 0.08f), Palette.Metal, NoSnow);
                mb.BoxMinMax(new Vector3(-s - 0.08f, y, -s), new Vector3(-s + 0.08f, y + 0.15f, s), Palette.Metal, NoSnow);
                mb.BoxMinMax(new Vector3(s - 0.08f, y, -s), new Vector3(s + 0.08f, y + 0.15f, s), Palette.Metal, NoSnow);
            }
            mb.Cylinder(new Vector3(0, legH, 0), 2.7f, 4.2f, 14, Palette.C(0xCFE3EE), MeshBuilder.Plain, false, true);
            mb.Cylinder(new Vector3(0, legH + 1.9f, 0), 2.75f, 0.35f, 14, Palette.C(0x4E8CD9), MeshBuilder.NoSnow, false);
            mb.Cone(new Vector3(0, legH + 4.2f, 0), 2.95f, 1.7f, 14, Palette.C(0x7A8FA6), MeshBuilder.Plain);
        }

        private static void Lighthouse(MeshBuilder mb)
        {
            mb.Cylinder(Vector3.zero, 2.6f, 1.1f, 12, Palette.Stone, Plain);
            float y = 1.1f;
            float r = 2.0f;
            for (int i = 0; i < 4; i++)
            {
                float nr = r - 0.18f;
                mb.Cylinder(new Vector3(0, y, 0), r, 2.5f, 12, i % 2 == 0 ? Palette.White : Palette.Red, NoSnow, false, false, nr);
                y += 2.5f;
                r = nr;
            }
            mb.Cylinder(new Vector3(0, y, 0), 1.85f, 0.3f, 12, Palette.Metal, Plain);
            mb.Cylinder(new Vector3(0, y + 0.3f, 0), 1.05f, 1.7f, 10, Palette.Headlight, new Vector4(1f, 0, 0, 0), false);
            mb.Cone(new Vector3(0, y + 2.0f, 0), 1.45f, 1.3f, 10, Palette.Red, Plain);
        }
    }
}
