using System.Collections.Generic;
using SmallTown.Core;
using SmallTown.Simulation;
using SmallTown.Simulation.Agents;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.Traffic;
using SmallTown.Simulation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmallTown.View
{
    /// <summary>World-space visuals of events: festival, bridge barriers, riverside lot/park, fire, selection and route line.</summary>
    public sealed class EventsView : MonoBehaviour
    {
        private CityData _c;
        private CityView _city;
        private GameAssets _a;
        private Transform _festival;
        private Material _bulbMat;
        private float _festivalScale;
        private readonly Transform[] _barrierArms = new Transform[4];
        private readonly Transform[] _barrierRoots = new Transform[4];
        private float _lotBlend;
        private ParticleSystem _flames, _smoke, _spray;
        private float _smokeLinger;
        private Transform _ring;
        private Material _ringMat;
        private LineRenderer _route;
        private readonly List<Vector3> _routePts = new List<Vector3>(256);

        public void Init(CityData city, CityView cityView, GameAssets assets)
        {
            _c = city;
            _city = cityView;
            _a = assets;
            BuildFestival();
            BuildBarriers();
            BuildFire();
            BuildSelection();
        }

        // ------------------------------------------------------------------ construction

        private GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        private void BuildFestival()
        {
            var root = new GameObject("Festival").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(_c.StageX, CityData.CurbY + 0.07f, _c.StageZ);
            var mb = new MeshBuilder();
            // stage facing south (-Z)
            mb.BoxMinMax(new Vector3(-5.5f, 0f, -2.2f), new Vector3(5.5f, 1.0f, 2.6f), Palette.Wood, MeshBuilder.Plain);
            mb.BoxMinMax(new Vector3(-5.5f, 1.0f, 2.2f), new Vector3(5.5f, 6.0f, 2.6f), Palette.C(0x3D4A5C), MeshBuilder.NoSnow);
            mb.BoxMinMax(new Vector3(-5.8f, 6.0f, -2.6f), new Vector3(5.8f, 6.4f, 2.8f), Palette.C(0x8A7FD1), MeshBuilder.Plain);
            mb.BoxMinMax(new Vector3(-5.6f, 1.0f, -2.4f), new Vector3(-5.2f, 6.0f, -2.0f), Palette.Metal, MeshBuilder.NoSnow);
            mb.BoxMinMax(new Vector3(5.2f, 1.0f, -2.4f), new Vector3(5.6f, 6.0f, -2.0f), Palette.Metal, MeshBuilder.NoSnow);
            mb.BoxMinMax(new Vector3(-7.2f, 0f, -1.0f), new Vector3(-5.8f, 2.6f, 0.4f), Palette.C(0x2F3238), MeshBuilder.Plain);
            mb.BoxMinMax(new Vector3(5.8f, 0f, -1.0f), new Vector3(7.2f, 2.6f, 0.4f), Palette.C(0x2F3238), MeshBuilder.Plain);
            // banner
            mb.Quad(new Vector3(-4f, 4.2f, 2.18f), new Vector3(-4f, 5.6f, 2.18f), new Vector3(4f, 5.6f, 2.18f), new Vector3(4f, 4.2f, 2.18f), Palette.Awning4, MeshBuilder.NoSnow, Vector3.back);
            // garland poles around the crowd area
            var poles = new[] { new Vector3(-9f, 0, -1f), new Vector3(9f, 0, -1f), new Vector3(-9f, 0, -12f), new Vector3(9f, 0, -12f) };
            foreach (var p in poles) mb.Cylinder(p, 0.12f, 4.6f, 6, Palette.Wood, MeshBuilder.NoSnow);
            // food stalls
            mb.SetTransform(new Vector3(-10.5f, 0f, -6.5f), 90f);
            StallInto(mb, Palette.Awning1);
            mb.SetTransform(new Vector3(10.5f, 0f, -6.5f), 270f);
            StallInto(mb, Palette.Awning3);
            mb.Identity();
            MeshObject("Stage", mb.ToMesh("Stage"), _a.City, root, true);

            // bulbs strung between the poles
            var bulbs = new MeshBuilder();
            Color32[] cols = { Palette.C(0xFFD27A), Palette.C(0xFF8A7A), Palette.C(0x8AD0FF), Palette.C(0xB2F29A) };
            int ci = 0;
            void String(Vector3 a, Vector3 b)
            {
                for (int i = 0; i <= 14; i++)
                {
                    float t = i / 14f;
                    var p = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.9f);
                    bulbs.Box(p, new Vector3(0.22f, 0.22f, 0.22f), cols[ci++ % cols.Length], MeshBuilder.NoSnow);
                }
            }
            var top = Vector3.up * 4.5f;
            String(poles[0] + top, poles[1] + top);
            String(poles[0] + top, poles[2] + top);
            String(poles[1] + top, poles[3] + top);
            String(poles[2] + top, poles[3] + top);
            String(poles[0] + top, poles[3] + top);
            String(poles[1] + top, poles[2] + top);
            _bulbMat = new Material(_a.GlowAlways) { name = "M_Bulbs" };
            _bulbMat.SetFloat("_Intensity", 1.6f);
            MeshObject("Bulbs", bulbs.ToMesh("Bulbs"), _bulbMat, root, false);
            _festival = root;
            _festival.localScale = new Vector3(1f, 0.001f, 1f);
            _festival.gameObject.SetActive(false);
        }

        private static void StallInto(MeshBuilder mb, Color32 awning)
        {
            mb.BoxMinMax(new Vector3(-1.4f, 0f, -0.8f), new Vector3(1.4f, 1.1f, 0.8f), Palette.Wood, MeshBuilder.Plain);
            mb.BoxMinMax(new Vector3(-1.4f, 1.1f, 0.7f), new Vector3(1.4f, 2.4f, 0.8f), Palette.Wood, MeshBuilder.NoSnow);
            mb.GableRoof(2.8f, 1.6f, 2.4f, 0.6f, 0.25f, true, awning, Palette.White, MeshBuilder.Plain);
        }

        private void BuildBarriers()
        {
            float R = _c.Config.RoadWidth;
            int k = 0;
            for (int br = 0; br < 2; br++)
            {
                float z = _c.BridgeZ(br);
                for (int end = 0; end < 2; end++)
                {
                    float x = end == 0 ? _c.WestWallX - 0.6f : _c.EastWallX + 0.6f;
                    var root = new GameObject("Barrier_" + br + "_" + end).transform;
                    root.SetParent(transform, false);
                    root.position = new Vector3(x, CityData.StreetY, z - R * 0.5f - 0.2f);
                    var post = new MeshBuilder();
                    post.BoxMinMax(new Vector3(-0.25f, 0f, -0.25f), new Vector3(0.25f, 1.3f, 0.25f), Palette.Red, MeshBuilder.NoSnow);
                    post.BoxMinMax(new Vector3(-0.3f, 0f, R + 0.1f), new Vector3(0.3f, 1.0f, R + 0.5f), Palette.White, MeshBuilder.NoSnow);
                    MeshObject("Post", post.ToMesh("BarrierPost"), _a.City, root, true);
                    var armRoot = new GameObject("Arm").transform;
                    armRoot.SetParent(root, false);
                    armRoot.localPosition = new Vector3(0f, 1.1f, 0f);
                    var arm = new MeshBuilder();
                    int stripes = 8;
                    for (int i = 0; i < stripes; i++)
                    {
                        float z0 = i * (R + 0.3f) / stripes, z1 = (i + 1) * (R + 0.3f) / stripes;
                        arm.BoxMinMax(new Vector3(-0.1f, -0.12f, z0), new Vector3(0.1f, 0.12f, z1), i % 2 == 0 ? Palette.Red : Palette.White, MeshBuilder.NoSnow);
                    }
                    MeshObject("ArmMesh", arm.ToMesh("BarrierArm"), _a.City, armRoot, true);
                    armRoot.localRotation = Quaternion.Euler(-80f, 0f, 0f);
                    _barrierArms[k] = armRoot;
                    _barrierRoots[k] = root;
                    root.gameObject.SetActive(false);
                    k++;
                }
            }
        }

        private void BuildFire()
        {
            _flames = MakeFireSystem("Flames", _a.ParticleAdditive, new Color(1f, 0.55f, 0.18f, 0.9f), 1.2f, new Vector2(1.6f, 3.4f), 5f, 6.5f);
            _smoke = MakeFireSystem("Smoke", _a.ParticleAlpha, new Color(0.32f, 0.32f, 0.34f, 0.55f), 5.5f, new Vector2(3f, 6.5f), 3.5f, 1.3f);
            _spray = MakeFireSystem("Spray", _a.ParticleAlpha, new Color(0.8f, 0.9f, 1f, 0.7f), 1.1f, new Vector2(0.35f, 0.7f), 16f, 0.8f);
            var sprayShape = _spray.shape;
            sprayShape.angle = 6f;
            var main = _spray.main;
            main.gravityModifier = 0.9f;
        }

        private ParticleSystem MakeFireSystem(string name, Material mat, Color color, float life, Vector2 size, float speed, float gravityUp)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startColor = color;
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -gravityUp * 0.05f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 2.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            ps.Play();
            return ps;
        }

        private void BuildSelection()
        {
            var ringGo = new GameObject("SelectionRing");
            ringGo.transform.SetParent(transform, false);
            ringGo.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Ring(1.4f, 0.35f, 24);
            var mr = ringGo.AddComponent<MeshRenderer>();
            _ringMat = new Material(_a.GlowAlways) { name = "M_Ring" };
            _ringMat.SetColor("_Color", new Color(0.3f, 0.6f, 1f, 1f));
            _ringMat.SetFloat("_Intensity", 1.4f);
            mr.sharedMaterial = _ringMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            _ring = ringGo.transform;
            ringGo.SetActive(false);

            var lineGo = new GameObject("RouteLine");
            lineGo.transform.SetParent(transform, false);
            _route = lineGo.AddComponent<LineRenderer>();
            _route.sharedMaterial = _ringMat;
            _route.widthMultiplier = 0.45f;
            _route.numCornerVertices = 2;
            _route.startColor = new Color(0.35f, 0.65f, 1f, 1f);
            _route.endColor = new Color(0.35f, 0.65f, 1f, 0.6f);
            _route.enabled = false;
        }

        // ------------------------------------------------------------------ per frame

        public void UpdateView(TownSimulation sim, float dt, float nightFactor, AgentRenderer agents, EntityRef selected)
        {
            var w = sim.World;
            float time = Time.time;

            // Festival pop-in
            float fTarget = w.Festival ? 1f : 0f;
            _festivalScale = Mathf.MoveTowards(_festivalScale, fTarget, dt * 1.5f);
            bool fOn = _festivalScale > 0.001f;
            if (_festival.gameObject.activeSelf != fOn) _festival.gameObject.SetActive(fOn);
            if (fOn)
            {
                float s = Mathf.SmoothStep(0f, 1f, _festivalScale);
                _festival.localScale = new Vector3(1f, Mathf.Max(0.001f, s), 1f);
                float pulse = 0.8f + 0.4f * Mathf.Sin(time * 3f);
                _bulbMat.SetFloat("_Intensity", Mathf.Lerp(0.5f, 2.2f, nightFactor) * pulse);
            }

            // Barriers: arm lowers when the bridge is closed or flooded
            for (int i = 0; i < 4; i++)
            {
                int br = i / 2;
                bool closed = w.BridgeClosed(br) || w.BridgesFlooded;
                var root = _barrierRoots[i];
                var arm = _barrierArms[i];
                float cur = arm.localEulerAngles.x;
                if (cur > 180f) cur -= 360f;
                float target = closed ? 0f : -80f;
                float next = Mathf.MoveTowards(cur, target, dt * 110f);
                arm.localRotation = Quaternion.Euler(next, 0f, 0f);
                bool show = closed || next > -79f;
                if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            }

            // Riverside lot <-> park
            _lotBlend = Mathf.MoveTowards(_lotBlend, w.LotIsPark ? 1f : 0f, dt * 1.2f);
            SetGrow(_city.LotParkRoot, _lotBlend);
            SetGrow(_city.LotParkingRoot, 1f - _lotBlend);

            // Fire, smoke and the fire hose
            if (w.FireActive)
            {
                var b = _c.Buildings[w.FireBuilding];
                float top = CityData.CurbY + b.Height + 1f;
                var p = new Vector3(b.X, top, b.Z);
                _flames.transform.position = p;
                _smoke.transform.position = p + Vector3.up * 2f;
                var fs = _flames.shape;
                fs.radius = Mathf.Min(b.SizeX, b.SizeZ) * 0.35f;
                SetRate(_flames, 70f * w.FireIntensity);
                SetRate(_smoke, 22f * Mathf.Max(0.3f, w.FireIntensity));
                _smokeLinger = 6f;
                bool fighting = w.FireStage == FireStage.Fighting;
                if (fighting)
                {
                    Vehicle truck = null;
                    foreach (var v in sim.Vehicles)
                        if (v.Kind == VehicleKind.FireTruck && v.State == VehicleState.OnScene) { truck = v; break; }
                    if (truck != null)
                    {
                        var from = new Vector3(truck.X, CityData.StreetY + 2.4f, truck.Z);
                        var to = new Vector3(b.X, CityData.CurbY + b.Height * 0.8f, b.Z);
                        _spray.transform.position = from;
                        _spray.transform.rotation = Quaternion.LookRotation((to - from).normalized + Vector3.up * 0.25f);
                        SetRate(_spray, 90f);
                    }
                }
                else SetRate(_spray, 0f);
            }
            else
            {
                SetRate(_flames, 0f);
                SetRate(_spray, 0f);
                _smokeLinger -= dt;
                SetRate(_smoke, _smokeLinger > 0f ? 8f * (_smokeLinger / 6f) : 0f);
            }

            // Selection ring and route line
            UpdateSelection(sim, agents, selected, time);
        }

        private static void SetGrow(GameObject go, float t)
        {
            if (go == null) return;
            bool on = t > 0.001f;
            if (go.activeSelf != on) go.SetActive(on);
            if (on) go.transform.localScale = new Vector3(1f, Mathf.SmoothStep(0.001f, 1f, t), 1f);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        private void UpdateSelection(TownSimulation sim, AgentRenderer agents, EntityRef sel, float time)
        {
            bool ring = false, route = false;
            _routePts.Clear();
            var g = _c.Walk;
            if (sel.Kind == EntityKind.Resident && sel.Id >= 0 && sel.Id < sim.Residents.Count)
            {
                var r = sim.Residents[sel.Id];
                if (agents.PeopleVisible[sel.Id])
                {
                    ring = true;
                    _ring.position = agents.PeoplePos[sel.Id] + Vector3.up * 0.08f;
                    if (r.State == ResidentState.Walking && r.Path != null)
                    {
                        _routePts.Add(agents.PeoplePos[sel.Id] + Vector3.up * 0.3f);
                        for (int i = r.PathIdx + 1; i < r.Path.Length; i++)
                            _routePts.Add(new Vector3(g.X[r.Path[i]], g.Y[r.Path[i]] + 0.3f, g.Z[r.Path[i]]));
                    }
                }
            }
            else if (sel.Kind == EntityKind.Vehicle && sel.Id >= 0 && sel.Id < sim.Vehicles.Count)
            {
                var v = sim.Vehicles[sel.Id];
                ring = true;
                _ring.position = agents.CarPos[sel.Id] + Vector3.up * 0.08f;
                if (v.OnRoad && v.Route.Count > 0)
                {
                    var rn = _c.Roads;
                    _routePts.Add(agents.CarPos[sel.Id] + Vector3.up * 0.4f);
                    for (int i = v.RouteIdx; i < v.Route.Count; i++)
                    {
                        int l = v.Route[i];
                        float endS = i == v.Route.Count - 1 ? v.DestS : rn.LLen[l];
                        var e = rn.LanePoint(l, endS);
                        _routePts.Add(new Vector3(e.X, CityData.StreetY + 0.4f, e.Z));
                    }
                }
            }
            if (ring)
            {
                float s = 1f + Mathf.Sin(time * 5f) * 0.08f;
                _ring.localScale = new Vector3(s, 1f, s);
            }
            if (_ring.gameObject.activeSelf != ring) _ring.gameObject.SetActive(ring);
            route = _routePts.Count > 1;
            _route.enabled = route;
            if (route)
            {
                _route.positionCount = _routePts.Count;
                for (int i = 0; i < _routePts.Count; i++) _route.SetPosition(i, _routePts[i]);
            }
        }
    }
}
