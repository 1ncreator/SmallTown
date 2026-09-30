using System.Collections.Generic;
using SmallTown.Core;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmallTown.View
{
    /// <summary>
    /// Builds the static diorama from <see cref="CityData"/>: layered slab, roads and markings,
    /// blocks, embankments, bridges, buildings, props and pick colliders.
    /// </summary>
    public sealed class CityView : MonoBehaviour
    {
        private CityData _c;
        private GameAssets _a;
        private readonly List<GameObject> _meshObjects = new List<GameObject>();

        public GameObject LotParkingRoot { get; private set; }
        public GameObject LotParkRoot { get; private set; }
        public Renderer FoliageRenderer { get; private set; }
        public Material FoliageMaterial { get; private set; }
        public Material GrassMaterial { get; private set; }
        public Material ConiferMaterial { get; private set; }
        public Mesh StatsMesh { get; private set; }
        public int VertexCount { get; private set; }

        private float R => _c.Config.RoadWidth;
        private float SW => _c.Config.SidewalkWidth;
        private float RoadEdgeW => _c.RoadX[_c.RiverGap] + R * 0.5f;
        private float RoadEdgeE => _c.RoadX[_c.RiverGap + 1] - R * 0.5f;

        public void Build(CityData city, GameAssets assets)
        {
            _c = city;
            _a = assets;
            GrassMaterial = new Material(assets.Grass) { name = "M_Grass (runtime)" };
            FoliageMaterial = new Material(assets.Foliage) { name = "M_Foliage (runtime)" };
            ConiferMaterial = new Material(assets.Conifer) { name = "M_Conifer (runtime)" };

            var city3 = new MeshBuilder();
            var grass = new MeshBuilder();
            var foliage = new MeshBuilder();
            var conifer = new MeshBuilder();
            var glow = new MeshBuilder();

            Plinth(city3);
            Ground(grass);
            Roads(city3);
            Blocks(city3, grass);
            RiverBanks(city3);
            Bridges(city3);
            foreach (var b in _c.Buildings) BuildingGeometry.Add(city3, b, _c);
            foreach (var p in _c.Props)
                if (p.Group == 0 || p.Group == 3) ProceduralMeshes.AddProp(p, city3, foliage, conifer, glow);
            SlabSides(city3);

            AddMeshObject("City", city3.ToMesh("City"), assets.City, true);
            AddMeshObject("Grass", grass.ToMesh("Grass"), GrassMaterial, true);
            FoliageRenderer = AddMeshObject("Foliage", foliage.ToMesh("Foliage"), FoliageMaterial, true);
            AddMeshObject("Conifers", conifer.ToMesh("Conifers"), ConiferMaterial, true);
            AddMeshObject("LampGlow", glow.ToMesh("LampGlow"), assets.Glow, false);
            VertexCount = city3.VertexCount + grass.VertexCount + foliage.VertexCount + conifer.VertexCount;

            BuildLotVariants();
            BuildColliders();
        }

        private MeshRenderer AddMeshObject(string name, Mesh mesh, Material mat, bool shadows, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            _meshObjects.Add(go);
            return mr;
        }

        // ------------------------------------------------------------------ ground

        private void Plinth(MeshBuilder mb)
        {
            mb.BoxMinMax(new Vector3(_c.MinX - 1.6f, CityData.SlabBottomY - 1.0f, _c.MinZ - 1.6f),
                new Vector3(_c.MaxX + 1.6f, CityData.SlabBottomY, _c.MaxZ + 1.6f), Palette.Plinth, MeshBuilder.NoSnow, true);
        }

        private void Ground(MeshBuilder grass)
        {
            grass.Rect(_c.MinX, _c.MinZ, RoadEdgeW, _c.MaxZ, CityData.StreetY, Palette.GrassWhite, MeshBuilder.Plain);
            grass.Rect(RoadEdgeE, _c.MinZ, _c.MaxX, _c.MaxZ, CityData.StreetY, Palette.GrassWhite, MeshBuilder.Plain);
        }

        private void Roads(MeshBuilder mb)
        {
            var rn = _c.Roads;
            float y = CityData.StreetY + 0.01f;
            float z0 = _c.RoadZ[0] - R * 0.5f, z1 = _c.RoadZ[_c.RoadZ.Length - 1] + R * 0.5f;
            for (int i = 0; i < _c.RoadX.Length; i++)
            {
                float x = _c.RoadX[i];
                mb.Rect(x - R * 0.5f, z0, x + R * 0.5f, z1, y, Palette.Asphalt, MeshBuilder.Plain);
            }
            foreach (var seg in rn.Segments)
            {
                if (!seg.Horizontal || seg.Bridge >= 0) continue;
                float xa = rn.IX[seg.A], xb = rn.IX[seg.B], z = rn.IZ[seg.A];
                mb.Rect(xa, z - R * 0.5f, xb, z + R * 0.5f, y + 0.002f, Palette.Asphalt, MeshBuilder.Plain);
            }
            // Centre dashes and stop lines
            float my = CityData.StreetY + 0.02f;
            foreach (var seg in rn.Segments)
            {
                Vector3 a = new Vector3(rn.IX[seg.A], 0, rn.IZ[seg.A]), b = new Vector3(rn.IX[seg.B], 0, rn.IZ[seg.B]);
                Vector3 d = (b - a).normalized;
                float len = Vector3.Distance(a, b);
                for (float s = R * 0.5f + SW + 1.5f; s < len - R * 0.5f - SW - 2.5f; s += 4.5f)
                {
                    Vector3 p0 = a + d * s, p1 = a + d * (s + 2.2f);
                    Vector3 w = new Vector3(d.z, 0, -d.x) * 0.09f;
                    float yy = seg.Bridge >= 0 ? CityData.StreetY + 0.03f : my;
                    mb.Quad(p0 - w + Vector3.up * yy, p1 - w + Vector3.up * yy, p1 + w + Vector3.up * yy, p0 + w + Vector3.up * yy, Palette.MarkingYellow, MeshBuilder.Plain, Vector3.up);
                }
            }
            for (int l = 0; l < rn.LaneCount; l++)
            {
                float s = rn.LLen[l] - (SW + 0.6f);
                var p = rn.LanePoint(l, s);
                var d = rn.LDir[l];
                Vector3 c = new Vector3(p.X, my, p.Z);
                Vector3 across = new Vector3(d.Z, 0, -d.X) * (R * 0.25f - 0.2f);
                Vector3 along = new Vector3(d.X, 0, d.Z) * 0.18f;
                mb.Quad(c - across - along, c - across + along, c + across + along, c + across - along, Palette.Marking, MeshBuilder.Plain, Vector3.up);
            }
            // Zebra crossings
            foreach (var cw in rn.Crosswalks)
            {
                for (int k = -2; k <= 2; k++)
                {
                    float off = k * 0.5f;
                    float half = R * 0.5f - 0.35f;
                    if (cw.AlongX)
                        mb.Rect(cw.X - half, cw.Z + off - 0.2f, cw.X + half, cw.Z + off + 0.2f, my + 0.002f, Palette.Marking, MeshBuilder.Plain);
                    else
                        mb.Rect(cw.X + off - 0.2f, cw.Z - half, cw.X + off + 0.2f, cw.Z + half, my + 0.002f, Palette.Marking, MeshBuilder.Plain);
                }
            }
        }

        private void Blocks(MeshBuilder mb, MeshBuilder grass)
        {
            float top = CityData.CurbY;
            foreach (var cell in _c.Cells)
            {
                if (cell.Kind == CellKind.River) continue;
                mb.BoxMinMax(new Vector3(cell.MinX, CityData.StreetY - 0.3f, cell.MinZ), new Vector3(cell.MaxX, top, cell.MaxZ), Palette.Sidewalk, MeshBuilder.Plain);
                float ix0 = cell.MinX + SW, ix1 = cell.MaxX - SW, iz0 = cell.MinZ + SW, iz1 = cell.MaxZ - SW;
                float y = top + 0.012f;
                // kerb line between sidewalk and lawn
                mb.BoxMinMax(new Vector3(ix0 - 0.15f, top, iz0 - 0.15f), new Vector3(ix1 + 0.15f, top + 0.05f, iz1 + 0.15f), Palette.Curb, MeshBuilder.Plain);
                switch (cell.Kind)
                {
                    case CellKind.Commercial:
                        mb.Rect(ix0, iz0, ix1, iz1, y + 0.05f, Palette.Plaza, MeshBuilder.Plain);
                        break;
                    case CellKind.Market:
                        for (float x = ix0; x < ix1 - 0.01f; x += 2.2f)
                            for (float z = iz0; z < iz1 - 0.01f; z += 2.2f)
                            {
                                bool dark = (((int)((x - ix0) / 2.2f) + (int)((z - iz0) / 2.2f)) & 1) == 0;
                                mb.Rect(x, z, Mathf.Min(ix1, x + 2.2f), Mathf.Min(iz1, z + 2.2f), y + 0.05f, dark ? Palette.PlazaDark : Palette.Plaza, MeshBuilder.Plain);
                            }
                        break;
                    case CellKind.Park:
                        grass.Rect(ix0, iz0, ix1, iz1, y + 0.05f, Palette.GrassWhite, MeshBuilder.Plain);
                        float cx = cell.CenterX, cz = cell.CenterZ;
                        mb.Rect(cx - 1.2f, iz0, cx + 1.2f, cz + 3f, y + 0.07f, Palette.Plaza, MeshBuilder.Plain);
                        mb.Rect(ix0, cz - 1.2f, ix1, cz + 1.2f, y + 0.07f, Palette.Plaza, MeshBuilder.Plain);
                        mb.Rect(cx - 7.6f, cz - 5.5f, cx + 7.6f, cz + 4.4f, y + 0.06f, Palette.Plaza, MeshBuilder.Plain);
                        mb.Rect(cx - 1.2f, cz + 3f, cx + 1.2f, iz1, y + 0.065f, Palette.Plaza, MeshBuilder.Plain);
                        break;
                    default:
                        grass.Rect(ix0, iz0, ix1, iz1, y + 0.05f, Palette.GrassWhite, MeshBuilder.Plain);
                        break;
                }
            }
            // Walkways from doors to the sidewalk
            foreach (var b in _c.Buildings)
            {
                var cell = _c.Cells[b.Cell];
                if (cell.Kind == CellKind.River || cell.Kind == CellKind.Market || cell.Kind == CellKind.Commercial) continue;
                float y = top + 0.075f;
                float w = b.Type == BuildingType.House ? 0.7f : 1.3f;
                switch (b.Facing)
                {
                    case Side.South: mb.Rect(b.DoorX - w, cell.MinZ + SW - 0.1f, b.DoorX + w, b.DoorZ, y, Palette.Plaza, MeshBuilder.Plain); break;
                    case Side.North: mb.Rect(b.DoorX - w, b.DoorZ, b.DoorX + w, cell.MaxZ - SW + 0.1f, y, Palette.Plaza, MeshBuilder.Plain); break;
                    case Side.West: mb.Rect(cell.MinX + SW - 0.1f, b.DoorZ - w, b.DoorX, b.DoorZ + w, y, Palette.Plaza, MeshBuilder.Plain); break;
                    default: mb.Rect(b.DoorX, b.DoorZ - w, cell.MaxX - SW + 0.1f, b.DoorZ + w, y, Palette.Plaza, MeshBuilder.Plain); break;
                }
            }
        }

        // ------------------------------------------------------------------ river column

        private List<Vector2> BridgeSpans(float pad)
        {
            var list = new List<Vector2>();
            for (int b = 0; b < 2; b++)
            {
                float z = _c.BridgeZ(b);
                list.Add(new Vector2(z - pad, z + pad));
            }
            list.Sort((a, b) => a.x.CompareTo(b.x));
            return list;
        }

        /// <summary>Splits [z0,z1] around the given spans.</summary>
        private static List<Vector2> Subtract(float z0, float z1, List<Vector2> spans)
        {
            var result = new List<Vector2>();
            float cur = z0;
            foreach (var s in spans)
            {
                if (s.y <= cur || s.x >= z1) continue;
                if (s.x > cur) result.Add(new Vector2(cur, s.x));
                cur = Mathf.Max(cur, s.y);
            }
            if (cur < z1) result.Add(new Vector2(cur, z1));
            return result;
        }

        private void RiverBanks(MeshBuilder mb)
        {
            float bottom = CityData.SlabBottomY;
            var roadSpans = BridgeSpans(R * 0.5f);
            // street-level strips along both riverside roads, interrupted by the bridge roads
            foreach (var span in Subtract(_c.MinZ, _c.MaxZ, roadSpans))
            {
                mb.BoxMinMax(new Vector3(RoadEdgeW, bottom, span.x), new Vector3(_c.WestWallX, CityData.CurbY, span.y), Palette.Wall, MeshBuilder.Plain, false, Palette.Sidewalk);
                mb.BoxMinMax(new Vector3(_c.EastWallX, bottom, span.x), new Vector3(RoadEdgeE, CityData.CurbY, span.y), Palette.Wall, MeshBuilder.Plain, false, Palette.Sidewalk);
            }
            foreach (var span in roadSpans)
            {
                mb.BoxMinMax(new Vector3(RoadEdgeW, bottom, span.x), new Vector3(_c.WestWallX, CityData.StreetY + 0.01f, span.y), Palette.Wall, MeshBuilder.Plain, false, Palette.Asphalt);
                mb.BoxMinMax(new Vector3(_c.EastWallX, bottom, span.x), new Vector3(RoadEdgeE, CityData.StreetY + 0.01f, span.y), Palette.Wall, MeshBuilder.Plain, false, Palette.Asphalt);
            }

            // West terrace with the sunken lot and the ramp
            float rampX1 = _c.WestWallX + 3.6f;
            void Terrace(float x0, float x1, float z0, float z1, float y)
            {
                if (z1 - z0 < 0.01f) return;
                mb.BoxMinMax(new Vector3(x0, bottom, z0), new Vector3(x1, y, z1), Palette.Wall, MeshBuilder.Plain, false, Palette.Stone);
            }
            Terrace(_c.WestWallX, _c.RiverWestBank, _c.MinZ, _c.LotMinZ, CityData.TerraceY);
            Terrace(_c.WestWallX, _c.RiverWestBank, _c.LotMinZ, _c.LotMaxZ, CityData.LotY);
            Terrace(rampX1, _c.RiverWestBank, _c.LotMaxZ, _c.RampTopZ, CityData.TerraceY);
            Terrace(_c.WestWallX, rampX1, _c.LotMaxZ, _c.RampTopZ, CityData.LotY);
            Terrace(_c.WestWallX, _c.RiverWestBank, _c.RampTopZ, _c.MaxZ, CityData.TerraceY);
            // ramp surface and its side wall
            mb.Quad(new Vector3(_c.WestWallX, CityData.LotY + 0.01f, _c.LotMaxZ), new Vector3(_c.WestWallX, CityData.StreetY, _c.RampTopZ),
                new Vector3(rampX1, CityData.StreetY, _c.RampTopZ), new Vector3(rampX1, CityData.LotY + 0.01f, _c.LotMaxZ), Palette.AsphaltDark, MeshBuilder.Plain, Vector3.up);
            mb.Tri(new Vector3(rampX1, CityData.LotY, _c.LotMaxZ), new Vector3(rampX1, CityData.StreetY, _c.RampTopZ), new Vector3(rampX1, CityData.LotY, _c.RampTopZ), Palette.Wall, MeshBuilder.NoSnow, Vector3.right);
            mb.Quad(new Vector3(rampX1, CityData.StreetY, _c.RampTopZ), new Vector3(rampX1, CityData.StreetY + 0.9f, _c.RampTopZ),
                new Vector3(rampX1, CityData.LotY + 0.9f, _c.LotMaxZ), new Vector3(rampX1, CityData.LotY, _c.LotMaxZ), Palette.MetalLight, MeshBuilder.NoSnow, Vector3.right);
            // East terrace
            Terrace(_c.RiverEastBank, _c.EastWallX, _c.MinZ, _c.MaxZ, CityData.TerraceY);
            // Channel bed
            mb.Rect(_c.RiverWestBank, _c.MinZ, _c.RiverEastBank, _c.MaxZ, CityData.ChannelBottomY, Palette.Sand, MeshBuilder.NoSnow);

            // Railings along the water
            RailingZ(mb, _c.RiverWestBank - 0.15f, CityData.TerraceY, _c.MinZ + 1f, _c.LotMinZ);
            RailingZ(mb, _c.RiverWestBank - 0.15f, CityData.LotY, _c.LotMinZ, _c.LotMaxZ);
            RailingZ(mb, _c.RiverWestBank - 0.15f, CityData.TerraceY, _c.LotMaxZ, _c.MaxZ - 1f);
            RailingZ(mb, _c.RiverEastBank + 0.15f, CityData.TerraceY, _c.MinZ + 1f, _c.MaxZ - 1f);
            // Railings along the street-level wall top
            foreach (var span in Subtract(_c.MinZ, _c.MaxZ, BridgeSpans(R * 0.5f + SW)))
            {
                foreach (var part in Subtract(span.x, span.y, new List<Vector2> { new Vector2(_c.LotMaxZ, _c.RampTopZ + 0.5f) }))
                    RailingZ(mb, _c.WestWallX - 0.12f, CityData.CurbY, part.x, part.y);
                RailingZ(mb, _c.EastWallX + 0.12f, CityData.CurbY, span.x, span.y);
            }

            // Stairs from the street down to the terrace
            var g = _c.Walk;
            for (int e = 0; e < g.EdgeCount; e++)
            {
                if (g.EKind[e] != WalkEdgeKind.Stairs) continue;
                int top = g.Y[g.EA[e]] > g.Y[g.EB[e]] ? g.EA[e] : g.EB[e];
                float z = g.Z[top];
                bool west = g.X[top] < _c.RiverCenterX;
                float wall = west ? _c.WestWallX : _c.EastWallX;
                float dir = west ? 1f : -1f;
                int steps = 6;
                for (int k = 0; k < steps; k++)
                {
                    float x0 = wall + dir * k * 0.45f, x1 = wall + dir * (k + 1) * 0.45f;
                    float yTop = Mathf.Lerp(CityData.CurbY, CityData.TerraceY, (k + 1f) / (steps + 1f));
                    mb.BoxMinMax(new Vector3(Mathf.Min(x0, x1), CityData.TerraceY - 0.05f, z - 1.2f), new Vector3(Mathf.Max(x0, x1), yTop, z + 1.2f), Palette.Stone, MeshBuilder.Plain);
                }
            }
        }

        private static void RailingZ(MeshBuilder mb, float x, float y, float z0, float z1)
        {
            if (z1 - z0 < 1f) return;
            mb.BoxMinMax(new Vector3(x - 0.05f, y + 0.85f, z0), new Vector3(x + 0.05f, y + 0.95f, z1), Palette.MetalLight, MeshBuilder.NoSnow);
            for (float z = z0; z <= z1; z += 2.2f)
                mb.BoxMinMax(new Vector3(x - 0.05f, y, z - 0.05f), new Vector3(x + 0.05f, y + 0.9f, z + 0.05f), Palette.Metal, MeshBuilder.NoSnow);
        }

        private void Bridges(MeshBuilder mb)
        {
            float x0 = _c.WestWallX, x1 = _c.EastWallX;
            for (int br = 0; br < 2; br++)
            {
                float z = _c.BridgeZ(br);
                float hr = R * 0.5f;
                mb.BoxMinMax(new Vector3(x0, CityData.BridgeFloodY, z - hr), new Vector3(x1, CityData.StreetY, z + hr), Palette.Stone, MeshBuilder.Plain, true, Palette.Asphalt);
                mb.BoxMinMax(new Vector3(x0, CityData.BridgeFloodY, z - hr - SW), new Vector3(x1, CityData.CurbY, z - hr), Palette.Stone, MeshBuilder.Plain, true, Palette.Sidewalk);
                mb.BoxMinMax(new Vector3(x0, CityData.BridgeFloodY, z + hr), new Vector3(x1, CityData.CurbY, z + hr + SW), Palette.Stone, MeshBuilder.Plain, true, Palette.Sidewalk);
                // decorative fascia arches
                for (int side = -1; side <= 1; side += 2)
                {
                    float fz = z + side * (hr + SW + 0.05f);
                    mb.Quad(new Vector3(x0, CityData.BridgeFloodY - 0.35f, fz), new Vector3(x0, CityData.CurbY, fz), new Vector3(x1, CityData.CurbY, fz), new Vector3(x1, CityData.BridgeFloodY - 0.35f, fz),
                        Palette.StoneDark, MeshBuilder.NoSnow, new Vector3(0, 0, side));
                }
                // railings
                for (int side = -1; side <= 1; side += 2)
                {
                    float rz = z + side * (hr + SW - 0.12f);
                    mb.BoxMinMax(new Vector3(x0, CityData.CurbY + 0.95f, rz - 0.07f), new Vector3(x1, CityData.CurbY + 1.07f, rz + 0.07f), Palette.White, MeshBuilder.NoSnow);
                    for (float x = x0; x <= x1 + 0.01f; x += 1.6f)
                        mb.BoxMinMax(new Vector3(x - 0.06f, CityData.CurbY, rz - 0.06f), new Vector3(x + 0.06f, CityData.CurbY + 1.0f, rz + 0.06f), Palette.White, MeshBuilder.NoSnow);
                }
                // piers in the river
                float[] px = { _c.RiverWestBank + 5f, _c.RiverEastBank - 5f };
                foreach (float x in px)
                {
                    mb.Cylinder(new Vector3(x, CityData.ChannelBottomY, z), 1.3f, CityData.BridgeFloodY - CityData.ChannelBottomY, 10, Palette.StoneDark, MeshBuilder.NoSnow, false);
                    mb.Cylinder(new Vector3(x, CityData.ChannelBottomY, z - hr - SW + 1.3f), 1.3f, CityData.BridgeFloodY - CityData.ChannelBottomY, 10, Palette.StoneDark, MeshBuilder.NoSnow, false);
                    mb.Cylinder(new Vector3(x, CityData.ChannelBottomY, z + hr + SW - 1.3f), 1.3f, CityData.BridgeFloodY - CityData.ChannelBottomY, 10, Palette.StoneDark, MeshBuilder.NoSnow, false);
                }
            }
        }

        // ------------------------------------------------------------------ slab sides

        private struct ProfileSeg
        {
            public float A, B, Top;
            public Color32 Cap;
        }

        private List<ProfileSeg> SouthProfile()
        {
            var p = new List<ProfileSeg>();
            void Add(float a, float b, float top, Color32 cap)
            {
                if (b - a > 0.001f) p.Add(new ProfileSeg { A = a, B = b, Top = top, Cap = cap });
            }
            Add(_c.MinX, RoadEdgeW, CityData.StreetY, Palette.GrassEdge);
            Add(RoadEdgeW, _c.WestWallX, CityData.CurbY, Palette.Sidewalk);
            Add(_c.WestWallX, _c.RiverWestBank, CityData.TerraceY, Palette.Stone);
            Add(_c.RiverWestBank, _c.RiverEastBank, CityData.ChannelBottomY, Palette.Sand);
            Add(_c.RiverEastBank, _c.EastWallX, CityData.TerraceY, Palette.Stone);
            Add(_c.EastWallX, RoadEdgeE, CityData.CurbY, Palette.Sidewalk);
            Add(RoadEdgeE, _c.MaxX, CityData.StreetY, Palette.GrassEdge);
            return p;
        }

        private static void Bands(MeshBuilder mb, float top, Color32 cap, System.Action<float, float, Color32> quad)
        {
            float bottom = CityData.SlabBottomY;
            float[] ys = { bottom, -2.6f, -0.8f, top - 0.35f, top };
            Color32[] cols = { Palette.RockDark, Palette.SoilMid, Palette.SoilTop, cap };
            for (int i = 0; i < 4; i++)
            {
                float y0 = Mathf.Max(ys[i], bottom), y1 = Mathf.Min(ys[i + 1], top);
                if (i == 3) y0 = Mathf.Max(bottom, top - 0.35f);
                if (y1 - y0 < 0.01f) continue;
                quad(y0, y1, cols[i]);
            }
        }

        private void SlabSides(MeshBuilder mb)
        {
            foreach (var seg in SouthProfile())
            {
                var s = seg;
                Bands(mb, s.Top, s.Cap, (y0, y1, col) =>
                {
                    mb.Quad(new Vector3(s.A, y0, _c.MinZ), new Vector3(s.A, y1, _c.MinZ), new Vector3(s.B, y1, _c.MinZ), new Vector3(s.B, y0, _c.MinZ), col, MeshBuilder.NoSnow, Vector3.back);
                    mb.Quad(new Vector3(s.A, y0, _c.MaxZ), new Vector3(s.A, y1, _c.MaxZ), new Vector3(s.B, y1, _c.MaxZ), new Vector3(s.B, y0, _c.MaxZ), col, MeshBuilder.NoSnow, Vector3.forward);
                });
            }
            Bands(mb, CityData.StreetY, Palette.GrassEdge, (y0, y1, col) =>
            {
                mb.Quad(new Vector3(_c.MinX, y0, _c.MinZ), new Vector3(_c.MinX, y1, _c.MinZ), new Vector3(_c.MinX, y1, _c.MaxZ), new Vector3(_c.MinX, y0, _c.MaxZ), col, MeshBuilder.NoSnow, Vector3.left);
                mb.Quad(new Vector3(_c.MaxX, y0, _c.MinZ), new Vector3(_c.MaxX, y1, _c.MinZ), new Vector3(_c.MaxX, y1, _c.MaxZ), new Vector3(_c.MaxX, y0, _c.MaxZ), col, MeshBuilder.NoSnow, Vector3.right);
            });
        }

        // ------------------------------------------------------------------ riverside lot variants

        private void BuildLotVariants()
        {
            LotParkingRoot = new GameObject("RiversideParking");
            LotParkingRoot.transform.SetParent(transform, false);
            var lot = new MeshBuilder();
            float y = CityData.LotY + 0.015f;
            lot.Rect(_c.LotMinX + 0.05f, _c.LotMinZ, _c.LotMaxX - 0.05f, _c.LotMaxZ, y, Palette.AsphaltDark, MeshBuilder.Plain);
            float lineX0 = _c.RiverWestBank - 5.4f, lineX1 = _c.RiverWestBank - 0.4f;
            for (int i = 0; i <= _c.LotSlots.Count; i++)
            {
                float z = (i < _c.LotSlots.Count ? _c.LotSlots[i].Z : _c.LotSlots[_c.LotSlots.Count - 1].Z + 2.9f) - 1.45f;
                lot.Rect(lineX0, z - 0.07f, lineX1, z + 0.07f, y + 0.005f, Palette.Marking, MeshBuilder.Plain);
            }
            // "P" sign
            lot.Cylinder(new Vector3(_c.WestWallX + 3.9f, CityData.LotY, _c.LotMaxZ - 0.8f), 0.07f, 2.6f, 5, Palette.Metal, MeshBuilder.NoSnow);
            lot.BoxMinMax(new Vector3(_c.WestWallX + 3.4f, CityData.LotY + 2.2f, _c.LotMaxZ - 0.9f), new Vector3(_c.WestWallX + 4.4f, CityData.LotY + 3.1f, _c.LotMaxZ - 0.75f), Palette.PoliceBlue, MeshBuilder.NoSnow);
            AddMeshObject("LotSurface", lot.ToMesh("Lot"), _a.City, true, LotParkingRoot.transform);

            LotParkRoot = new GameObject("RiversidePark");
            LotParkRoot.transform.SetParent(transform, false);
            var pc = new MeshBuilder();
            var pg = new MeshBuilder();
            var pf = new MeshBuilder();
            var pcon = new MeshBuilder();
            var pglow = new MeshBuilder();
            pg.Rect(_c.LotMinX + 0.05f, _c.LotMinZ, _c.LotMaxX - 0.05f, _c.LotMaxZ, y, Palette.GrassWhite, MeshBuilder.Plain);
            pc.Rect(_c.WestWallX + 1.3f, _c.LotMinZ, _c.WestWallX + 3.4f, _c.LotMaxZ, y + 0.01f, Palette.Plaza, MeshBuilder.Plain);
            foreach (var p in _c.Props)
                if (p.Group == 1) ProceduralMeshes.AddProp(p, pc, pf, pcon, pglow);
            AddMeshObject("ParkHard", pc.ToMesh("RiverParkHard"), _a.City, true, LotParkRoot.transform);
            AddMeshObject("ParkGrass", pg.ToMesh("RiverParkGrass"), GrassMaterial, true, LotParkRoot.transform);
            AddMeshObject("ParkFoliage", pf.ToMesh("RiverParkFoliage"), FoliageMaterial, true, LotParkRoot.transform);
            AddMeshObject("ParkConifer", pcon.ToMesh("RiverParkConifer"), ConiferMaterial, true, LotParkRoot.transform);
            LotParkRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ colliders

        private void AddCollider(string name, Vector3 center, Vector3 size, EntityRef e)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            go.AddComponent<PickTarget>().Set(e);
        }

        private void BuildColliders()
        {
            foreach (var b in _c.Buildings)
            {
                float extra = b.Type == BuildingType.Church ? 18f : b.Type == BuildingType.WaterTower ? 1f : b.Type == BuildingType.Lighthouse ? 3.5f : 3f;
                float baseY = b.Type == BuildingType.Lighthouse ? CityData.TerraceY : CityData.CurbY;
                float h = b.Height + extra;
                var e = b.Place >= 0 && _c.Places[b.Place].Kind == PlaceKind.Building ? new EntityRef(EntityKind.Place, b.Place) : new EntityRef(EntityKind.Building, b.Id);
                AddCollider("Pick_" + b.Name, new Vector3(b.X, baseY + h * 0.5f, b.Z), new Vector3(b.SizeX + 0.6f, h, b.SizeZ + 0.6f), e);
            }
            foreach (var p in _c.Places)
            {
                switch (p.Kind)
                {
                    case PlaceKind.Park:
                    case PlaceKind.Square:
                        AddCollider("Pick_" + p.Key, new Vector3((p.MinX + p.MaxX) * 0.5f, CityData.CurbY + 0.25f, (p.MinZ + p.MaxZ) * 0.5f), new Vector3(p.MaxX - p.MinX, 0.5f, p.MaxZ - p.MinZ), new EntityRef(EntityKind.Place, p.Id));
                        break;
                    case PlaceKind.Parking:
                        AddCollider("Pick_" + p.Key, new Vector3((p.MinX + p.MaxX) * 0.5f, CityData.LotY + 0.5f, (p.MinZ + p.MaxZ) * 0.5f), new Vector3(p.MaxX - p.MinX, 1f, p.MaxZ - p.MinZ), new EntityRef(EntityKind.Place, p.Id));
                        break;
                    case PlaceKind.Bridge:
                        AddCollider("Pick_" + p.Key, new Vector3((p.MinX + p.MaxX) * 0.5f, CityData.StreetY, (p.MinZ + p.MaxZ) * 0.5f), new Vector3(p.MaxX - p.MinX, 1.4f, p.MaxZ - p.MinZ), new EntityRef(EntityKind.Place, p.Id));
                        break;
                    case PlaceKind.River:
                        AddCollider("Pick_" + p.Key, new Vector3((p.MinX + p.MaxX) * 0.5f, -0.4f, (p.MinZ + p.MaxZ) * 0.5f), new Vector3(p.MaxX - p.MinX, 2.4f, p.MaxZ - p.MinZ), new EntityRef(EntityKind.Place, p.Id));
                        break;
                }
            }
        }
    }
}
