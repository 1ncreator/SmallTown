using SmallTown.Simulation;
using SmallTown.Simulation.Agents;
using SmallTown.Simulation.Traffic;
using SmallTown.Simulation.World;
using SmallTown.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmallTown.View
{
    /// <summary>
    /// Draws all residents and vehicles with Graphics.RenderMeshInstanced, interpolating between
    /// simulation ticks. All buffers are preallocated: no allocations per frame.
    /// </summary>
    public sealed class AgentRenderer
    {
        private const int MaxBatch = 1023;

        private readonly Mesh _person, _umbrella, _car, _police, _fire, _sirenL, _sirenR;
        private readonly Material _mat, _glow;
        private readonly MaterialPropertyBlock _mpbPeople = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbUmbrella = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbCars = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbPolice = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbFire = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbSirenL = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbSirenR = new MaterialPropertyBlock();

        private readonly Matrix4x4[] _peopleM = new Matrix4x4[MaxBatch];
        private readonly Vector4[] _peopleA = new Vector4[MaxBatch];
        private readonly Vector4[] _peopleB = new Vector4[MaxBatch];
        private readonly Matrix4x4[] _umbM = new Matrix4x4[MaxBatch];
        private readonly Vector4[] _umbA = new Vector4[MaxBatch];
        private readonly Vector4[] _umbB = new Vector4[MaxBatch];
        private readonly Matrix4x4[] _carM = new Matrix4x4[MaxBatch];
        private readonly Vector4[] _carA = new Vector4[MaxBatch];
        private readonly Vector4[] _carB = new Vector4[MaxBatch];
        private readonly Matrix4x4[] _polM = new Matrix4x4[64];
        private readonly Vector4[] _polA = new Vector4[64];
        private readonly Vector4[] _polB = new Vector4[64];
        private readonly Matrix4x4[] _fireM = new Matrix4x4[32];
        private readonly Vector4[] _fireA = new Vector4[32];
        private readonly Vector4[] _fireB = new Vector4[32];
        private readonly Matrix4x4[] _sirenLM = new Matrix4x4[96];
        private readonly Matrix4x4[] _sirenRM = new Matrix4x4[96];
        private readonly Vector4[] _sirenLC = new Vector4[96];
        private readonly Vector4[] _sirenRC = new Vector4[96];

        // Interpolated world positions (for picking and labels)
        public readonly Vector3[] PeoplePos;
        public readonly bool[] PeopleVisible;
        public readonly Vector3[] CarPos;

        public int DrawnPeople { get; private set; }
        public int DrawnCars { get; private set; }

        public AgentRenderer(Material agents, Material glowAlways, int residents, int vehicles)
        {
            _mat = agents;
            _glow = glowAlways;
            _person = ProceduralMeshes.Person();
            _umbrella = ProceduralMeshes.Umbrella();
            _car = ProceduralMeshes.Car(false);
            _police = ProceduralMeshes.Car(true);
            _fire = ProceduralMeshes.FireTruck();
            _sirenL = ProceduralMeshes.SirenHalf(true, 1.62f, -0.1f);
            _sirenR = ProceduralMeshes.SirenHalf(false, 1.62f, -0.1f);
            PeoplePos = new Vector3[residents];
            PeopleVisible = new bool[residents];
            CarPos = new Vector3[vehicles];
            _mpbPeople.SetVectorArray("_InstColorA", _peopleA);
            _mpbPeople.SetVectorArray("_InstColorB", _peopleB);
            _mpbUmbrella.SetVectorArray("_InstColorA", _umbA);
            _mpbUmbrella.SetVectorArray("_InstColorB", _umbB);
            _mpbCars.SetVectorArray("_InstColorA", _carA);
            _mpbCars.SetVectorArray("_InstColorB", _carB);
            _mpbPolice.SetVectorArray("_InstColorA", _polA);
            _mpbPolice.SetVectorArray("_InstColorB", _polB);
            _mpbFire.SetVectorArray("_InstColorA", _fireA);
            _mpbFire.SetVectorArray("_InstColorB", _fireB);
            _mpbSirenL.SetVectorArray("_InstColorA", _sirenLC);
            _mpbSirenR.SetVectorArray("_InstColorA", _sirenRC);
        }

        private static Vector4 V(Color32 c) => new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f);

        public void Draw(TownSimulation sim, float alpha, float time, Camera camera)
        {
            bool rain = sim.World.Weather == Weather.Rain || sim.World.Weather == Weather.Storm;
            int np = 0, nu = 0;
            var residents = sim.Residents;
            for (int i = 0; i < residents.Count && i < PeoplePos.Length; i++)
            {
                var r = residents[i];
                if (!r.IsOutside)
                {
                    PeopleVisible[i] = false;
                    continue;
                }
                float x = Mathf.Lerp(r.PX, r.X, alpha), y = Mathf.Lerp(r.PY, r.Y, alpha), z = Mathf.Lerp(r.PZ, r.Z, alpha);
                // standing agents jump between spots rarely; avoid long interpolation streaks after teleports
                if ((r.X - r.PX) * (r.X - r.PX) + (r.Z - r.PZ) * (r.Z - r.PZ) > 25f) { x = r.X; y = r.Y; z = r.Z; }
                float yaw = Mathf.LerpAngle(r.PHeading, r.Heading, alpha);
                float bob = r.Moving ? Mathf.Abs(Mathf.Sin(time * 9f + r.Id * 1.7f)) * 0.12f : 0f;
                float sway = r.State == ResidentState.Standing && r.Act == Activity.Festival ? Mathf.Sin(time * 4f + r.Id) * 0.1f : 0f;
                float s = r.Role == Role.Student ? 0.78f : 1f;
                var pos = new Vector3(x, y + bob + sway, z);
                PeoplePos[i] = pos;
                PeopleVisible[i] = true;
                if (np >= MaxBatch) continue;
                _peopleM[np] = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(s, s, s));
                _peopleA[np] = V(Palette.Shirts[r.ColorSeed % Palette.Shirts.Length]);
                _peopleB[np] = V(Palette.Skins[(r.ColorSeed >> 5) % Palette.Skins.Length]);
                np++;
                if (rain && r.Umbrella && nu < MaxBatch)
                {
                    _umbM[nu] = _peopleM[np - 1];
                    _umbA[nu] = V(Palette.CarColors[(r.ColorSeed >> 9) % Palette.CarColors.Length]);
                    _umbB[nu] = _umbA[nu];
                    nu++;
                }
            }
            DrawnPeople = np;

            int nc = 0, npol = 0, nf = 0, nsl = 0;
            bool flash = Mathf.Repeat(time * 3.2f, 1f) < 0.5f;
            var red = new Vector4(1f, 0.15f, 0.12f, 1f);
            var blue = new Vector4(0.15f, 0.35f, 1f, 1f);
            var dim = new Vector4(0.25f, 0.25f, 0.3f, 0.35f);
            var vehicles = sim.Vehicles;
            for (int i = 0; i < vehicles.Count && i < CarPos.Length; i++)
            {
                var v = vehicles[i];
                float x = Mathf.Lerp(v.PX, v.X, alpha), y = Mathf.Lerp(v.PY, v.Y, alpha), z = Mathf.Lerp(v.PZ, v.Z, alpha);
                if ((v.X - v.PX) * (v.X - v.PX) + (v.Z - v.PZ) * (v.Z - v.PZ) > 36f) { x = v.X; y = v.Y; z = v.Z; }
                float yaw = Mathf.LerpAngle(v.PHeading, v.Heading, alpha);
                var pos = new Vector3(x, y, z);
                CarPos[i] = pos;
                var m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                switch (v.Kind)
                {
                    case VehicleKind.Car:
                        if (nc >= MaxBatch) break;
                        _carM[nc] = m;
                        _carA[nc] = V(Palette.CarColors[v.ColorIndex % Palette.CarColors.Length]);
                        _carB[nc] = _carA[nc];
                        nc++;
                        break;
                    case VehicleKind.Police:
                        if (npol >= _polM.Length) break;
                        _polM[npol] = m;
                        _polA[npol] = Vector4.one;
                        _polB[npol] = Vector4.one;
                        npol++;
                        if (nsl < _sirenLM.Length)
                        {
                            _sirenLM[nsl] = m;
                            _sirenRM[nsl] = m;
                            _sirenLC[nsl] = v.Siren ? (flash ? red : dim) : dim;
                            _sirenRC[nsl] = v.Siren ? (flash ? dim : blue) : dim;
                            nsl++;
                        }
                        break;
                    case VehicleKind.FireTruck:
                        if (nf >= _fireM.Length) break;
                        _fireM[nf] = m;
                        _fireA[nf] = Vector4.one;
                        _fireB[nf] = Vector4.one;
                        nf++;
                        if (nsl < _sirenLM.Length)
                        {
                            var raised = m * Matrix4x4.Translate(new Vector3(0f, 0.55f, 2.3f));
                            _sirenLM[nsl] = raised;
                            _sirenRM[nsl] = raised;
                            _sirenLC[nsl] = v.Siren ? (flash ? red : dim) : dim;
                            _sirenRC[nsl] = v.Siren ? (flash ? dim : red) : dim;
                            nsl++;
                        }
                        break;
                }
            }
            DrawnCars = nc + npol + nf;

            Submit(_person, _mat, _mpbPeople, _peopleM, _peopleA, _peopleB, np, camera, true);
            Submit(_umbrella, _mat, _mpbUmbrella, _umbM, _umbA, _umbB, nu, camera, true);
            Submit(_car, _mat, _mpbCars, _carM, _carA, _carB, nc, camera, true);
            Submit(_police, _mat, _mpbPolice, _polM, _polA, _polB, npol, camera, true);
            Submit(_fire, _mat, _mpbFire, _fireM, _fireA, _fireB, nf, camera, true);
            if (nsl > 0)
            {
                _mpbSirenL.SetVectorArray("_InstColorA", _sirenLC);
                _mpbSirenR.SetVectorArray("_InstColorA", _sirenRC);
                var rp = new RenderParams(_glow) { matProps = _mpbSirenL, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera, worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000f) };
                Graphics.RenderMeshInstanced(rp, _sirenL, 0, _sirenLM, nsl);
                rp.matProps = _mpbSirenR;
                Graphics.RenderMeshInstanced(rp, _sirenR, 0, _sirenRM, nsl);
            }
        }

        private static void Submit(Mesh mesh, Material mat, MaterialPropertyBlock mpb, Matrix4x4[] m, Vector4[] a, Vector4[] b, int count, Camera camera, bool shadows)
        {
            if (count <= 0) return;
            mpb.SetVectorArray("_InstColorA", a);
            mpb.SetVectorArray("_InstColorB", b);
            var rp = new RenderParams(mat)
            {
                matProps = mpb,
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                camera = camera,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000f)
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, m, count);
        }

        /// <summary>Finds the resident or vehicle nearest to a screen point (within maxPixels).</summary>
        public bool Pick(TownSimulation sim, Camera cam, Vector2 screen, float maxPixels, out bool isVehicle, out int id)
        {
            isVehicle = false;
            id = -1;
            float best = maxPixels * maxPixels;
            for (int i = 0; i < sim.Residents.Count && i < PeoplePos.Length; i++)
            {
                if (!PeopleVisible[i]) continue;
                var sp = cam.WorldToScreenPoint(PeoplePos[i] + Vector3.up * 1.1f);
                if (sp.z <= 0f) continue;
                float d = ((Vector2)sp - screen).sqrMagnitude;
                if (d < best) { best = d; id = i; isVehicle = false; }
            }
            for (int i = 0; i < sim.Vehicles.Count && i < CarPos.Length; i++)
            {
                var sp = cam.WorldToScreenPoint(CarPos[i] + Vector3.up * 0.9f);
                if (sp.z <= 0f) continue;
                float d = ((Vector2)sp - screen).sqrMagnitude * 0.7f;
                if (d < best) { best = d; id = i; isVehicle = true; }
            }
            return id >= 0;
        }
    }
}
