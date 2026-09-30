using SmallTown.Core;
using SmallTown.Simulation;
using SmallTown.Simulation.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SmallTown.View
{
    /// <summary>
    /// Everything atmospheric: sun/moon, sky and background colours, fog, weather particles,
    /// lightning, seasonal tints, snow/wet shader globals and the animated river surface.
    /// Pure presentation of simulation state (visual randomness here is not part of the simulation).
    /// </summary>
    public sealed class EnvironmentView : MonoBehaviour
    {
        private static readonly int IdSnow = Shader.PropertyToID("_ST_Snow");
        private static readonly int IdWet = Shader.PropertyToID("_ST_Wet");
        private static readonly int IdNight = Shader.PropertyToID("_ST_Night");
        private static readonly int IdWindow = Shader.PropertyToID("_ST_WindowColor");
        private static readonly int IdSky = Shader.PropertyToID("_ST_AmbientSky");
        private static readonly int IdGround = Shader.PropertyToID("_ST_AmbientGround");
        private static readonly int IdTime = Shader.PropertyToID("_ST_Time");
        private static readonly int IdBase = Shader.PropertyToID("_BaseColor");

        private CityData _c;
        private Light _sun;
        private Camera _cam;
        private CityView _city;
        private Transform _water;
        private Transform _waterFronts;
        private Material _waterMat;
        private ParticleSystem _rain, _snow, _leaves;
        private LineRenderer _bolt;
        private System.Random _vr = new System.Random(12345);

        private float _displayHour = -1f;
        private float _rainF, _stormF, _snowF, _snowCover, _wet, _winterF, _autumnF, _springF;
        private float _flash, _nextLightning = 4f, _boltFade;
        private float _waterLevel;
        private Color _foliage, _grass;

        public float DisplayHour => _displayHour;
        public float NightFactor { get; private set; }
        public Color Background { get; private set; }

        public void Init(CityData city, CityView cityView, Light sun, Camera cam, GameAssets assets)
        {
            _c = city;
            _city = cityView;
            _sun = sun;
            _cam = cam;
            BuildWater(assets.Water);
            _rain = MakeParticles("Rain", assets.ParticleAlpha, new Color(0.75f, 0.82f, 0.92f, 0.55f));
            ConfigureRain(_rain);
            _snow = MakeParticles("Snow", assets.ParticleAlpha, new Color(1f, 1f, 1f, 0.95f));
            ConfigureSnow(_snow);
            _leaves = MakeParticles("Leaves", assets.ParticleAlpha, Color.white);
            ConfigureLeaves(_leaves);
            var boltGo = new GameObject("LightningBolt");
            boltGo.transform.SetParent(transform, false);
            _bolt = boltGo.AddComponent<LineRenderer>();
            _bolt.sharedMaterial = assets.GlowAlways;
            _bolt.widthMultiplier = 0.9f;
            _bolt.positionCount = 10;
            _bolt.startColor = _bolt.endColor = new Color(0.85f, 0.9f, 1f, 1f);
            _bolt.enabled = false;
            _foliage = new Color(0.40f, 0.66f, 0.34f);
            _grass = new Color(0.52f, 0.74f, 0.42f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Flat;
            Shader.SetGlobalColor(IdWindow, new Color(1f, 0.78f, 0.46f, 1f));
        }

        // ------------------------------------------------------------------ construction

        private void BuildWater(Material mat)
        {
            _waterMat = new Material(mat) { name = "M_Water (runtime)" };
            float x0 = _c.RoadX[_c.RiverGap] + _c.Config.RoadWidth * 0.5f;
            float x1 = _c.RoadX[_c.RiverGap + 1] - _c.Config.RoadWidth * 0.5f;
            var mb = new MeshBuilder();
            int strips = 24;
            float dz = (_c.MaxZ - _c.MinZ) / strips;
            for (int i = 0; i < strips; i++)
                mb.Rect(x0, _c.MinZ + i * dz, x1, _c.MinZ + (i + 1) * dz, 0f, Palette.White, MeshBuilder.NoSnow);
            _water = MakeMeshObject("RiverSurface", mb.ToMesh("Water"), _waterMat).transform;

            // Cut-away water faces at the slab ends: a unit-height quad scaled with the level.
            var fb = new MeshBuilder();
            float wx0 = _c.RiverWestBank, wx1 = _c.RiverEastBank;
            fb.Quad(new Vector3(wx0, 0f, _c.MinZ - 0.02f), new Vector3(wx0, 1f, _c.MinZ - 0.02f), new Vector3(wx1, 1f, _c.MinZ - 0.02f), new Vector3(wx1, 0f, _c.MinZ - 0.02f), Palette.White, MeshBuilder.NoSnow, Vector3.back);
            fb.Quad(new Vector3(wx0, 0f, _c.MaxZ + 0.02f), new Vector3(wx0, 1f, _c.MaxZ + 0.02f), new Vector3(wx1, 1f, _c.MaxZ + 0.02f), new Vector3(wx1, 0f, _c.MaxZ + 0.02f), Palette.White, MeshBuilder.NoSnow, Vector3.forward);
            _waterFronts = MakeMeshObject("RiverCutaway", fb.ToMesh("WaterFront"), _waterMat).transform;
        }

        private GameObject MakeMeshObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private ParticleSystem MakeParticles(string name, Material mat, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private void ConfigureRain(ParticleSystem ps)
        {
            var main = ps.main;
            main.maxParticles = 9000;
            main.startLifetime = 1.6f;
            main.startSpeed = 0f;
            main.startSize = 0.16f;
            main.gravityModifier = 0f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-2f, -1f);
            vel.y = new ParticleSystem.MinMaxCurve(-42f, -36f);
            vel.z = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(260f, 1f, 220f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2f;
            r.velocityScale = 0.06f;
            ps.transform.position = new Vector3(0f, 58f, 0f);
            ps.Play();
        }

        private void ConfigureSnow(ParticleSystem ps)
        {
            var main = ps.main;
            main.maxParticles = 6000;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.9f);
            vel.y = new ParticleSystem.MinMaxCurve(-6.5f, -5.0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.3f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(260f, 1f, 220f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            ps.transform.position = new Vector3(0f, 55f, 0f);
            ps.Play();
        }

        private void ConfigureLeaves(ParticleSystem ps)
        {
            var main = ps.main;
            main.maxParticles = 800;
            main.startLifetime = 14f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.55f, 0.18f), new Color(0.85f, 0.3f, 0.15f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.8f, -1.1f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.2f;
            noise.frequency = 0.4f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(260f, 1f, 200f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            ps.transform.position = new Vector3(0f, 20f, 0f);
            ps.Play();
        }

        // ------------------------------------------------------------------ per frame

        private static Color Key(float h, float[] hours, Color[] cols)
        {
            if (h <= hours[0]) return cols[0];
            for (int i = 0; i + 1 < hours.Length; i++)
            {
                if (h <= hours[i + 1])
                {
                    float t = Mathf.InverseLerp(hours[i], hours[i + 1], h);
                    return Color.Lerp(cols[i], cols[i + 1], Mathf.SmoothStep(0f, 1f, t));
                }
            }
            return cols[cols.Length - 1];
        }

        private static readonly float[] Hours = { 0f, 4.8f, 6.2f, 7.6f, 12f, 17f, 19.3f, 20.6f, 22f, 24f };

        private static readonly Color[] Bg =
        {
            new Color(0.10f, 0.12f, 0.21f), new Color(0.13f, 0.15f, 0.26f), new Color(0.96f, 0.78f, 0.70f), new Color(0.93f, 0.93f, 0.92f),
            new Color(0.92f, 0.94f, 0.95f), new Color(0.95f, 0.92f, 0.87f), new Color(0.98f, 0.72f, 0.56f), new Color(0.42f, 0.34f, 0.46f),
            new Color(0.14f, 0.15f, 0.26f), new Color(0.10f, 0.12f, 0.21f)
        };

        private static readonly Color[] SkyAmb =
        {
            new Color(0.15f, 0.18f, 0.30f), new Color(0.16f, 0.18f, 0.30f), new Color(0.46f, 0.38f, 0.40f), new Color(0.44f, 0.47f, 0.53f),
            new Color(0.46f, 0.49f, 0.55f), new Color(0.46f, 0.46f, 0.49f), new Color(0.48f, 0.38f, 0.36f), new Color(0.28f, 0.25f, 0.36f),
            new Color(0.16f, 0.18f, 0.30f), new Color(0.15f, 0.18f, 0.30f)
        };

        private static readonly Color[] GroundAmb =
        {
            new Color(0.06f, 0.06f, 0.10f), new Color(0.07f, 0.07f, 0.11f), new Color(0.28f, 0.23f, 0.21f), new Color(0.29f, 0.28f, 0.26f),
            new Color(0.30f, 0.29f, 0.27f), new Color(0.30f, 0.28f, 0.25f), new Color(0.30f, 0.21f, 0.18f), new Color(0.14f, 0.12f, 0.16f),
            new Color(0.07f, 0.07f, 0.11f), new Color(0.06f, 0.06f, 0.10f)
        };

        public void UpdateView(TownSimulation sim, float dt, Vector3 focus, bool jumpInstantly = false)
        {
            var w = sim.World;
            float simHour = sim.Hour;
            if (_displayHour < 0f || jumpInstantly) _displayHour = simHour;
            else
            {
                float diff = Mathf.Repeat(simHour - _displayHour, 24f);
                if (diff > 0.35f && diff < 23.9f) _displayHour = Mathf.Repeat(_displayHour + Mathf.Max(diff * dt * 1.6f, dt * 1.5f), 24f);
                else _displayHour = simHour;
            }
            float h = _displayHour;
            float k = 1f - Mathf.Exp(-dt * 1.2f);
            float kSlow = 1f - Mathf.Exp(-dt * 0.35f);
            if (jumpInstantly) { k = 1f; kSlow = 1f; }
            _rainF = Mathf.Lerp(_rainF, w.Weather == Weather.Rain || w.Weather == Weather.Storm ? 1f : 0f, k);
            _stormF = Mathf.Lerp(_stormF, w.Weather == Weather.Storm ? 1f : 0f, k);
            _snowF = Mathf.Lerp(_snowF, w.Weather == Weather.Snow ? 1f : 0f, k);
            float coverTarget = w.Weather == Weather.Snow ? 1f : (w.Season == Season.Winter ? 0.85f : 0f);
            _snowCover = Mathf.MoveTowards(_snowCover, coverTarget, dt * (coverTarget > _snowCover ? 0.12f : 0.18f) * (jumpInstantly ? 100f : 1f));
            float wetTarget = _rainF > 0.5f ? 1f : 0f;
            _wet = Mathf.MoveTowards(_wet, wetTarget, dt * (wetTarget > _wet ? 0.25f : 0.08f) * (jumpInstantly ? 100f : 1f));
            _winterF = Mathf.Lerp(_winterF, w.Season == Season.Winter ? 1f : 0f, k);
            _autumnF = Mathf.Lerp(_autumnF, w.Season == Season.Autumn ? 1f : 0f, k);
            _springF = Mathf.Lerp(_springF, w.Season == Season.Spring ? 1f : 0f, k);

            // Sun & moon
            float dayT = Mathf.InverseLerp(5.8f, 20.2f, h);
            bool day = h > 5.8f && h < 20.2f;
            float elev = day ? Mathf.Sin(dayT * Mathf.PI) * 58f : -10f;
            float az = 90f + dayT * 180f;
            float sunI = day ? Mathf.Clamp01(elev / 12f) : 0f;
            float moonI = Mathf.Clamp01(1f - sunI * 3f);
            NightFactor = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 12f, day ? elev : -10f));
            Color sunCol = Color.Lerp(new Color(1f, 0.62f, 0.42f), new Color(1f, 0.97f, 0.92f), Mathf.Clamp01(elev / 35f));
            float overcast = Mathf.Max(_rainF * 0.55f, _snowF * 0.4f) + _stormF * 0.25f;
            if (sunI > 0.02f)
            {
                _sun.transform.rotation = Quaternion.Euler(Mathf.Max(6f, elev), az + 180f, 0f);
                _sun.color = sunCol;
                _sun.intensity = Mathf.Lerp(0.1f, 1.02f, sunI) * (1f - overcast * 0.7f);
                _sun.shadowStrength = Mathf.Lerp(0.9f, 0.45f, overcast);
            }
            else
            {
                _sun.transform.rotation = Quaternion.Euler(48f, 200f, 0f);
                _sun.color = new Color(0.58f, 0.68f, 0.98f);
                _sun.intensity = 0.32f * moonI * (1f - overcast * 0.5f);
                _sun.shadowStrength = 0.55f;
            }

            // Lightning
            if (_stormF > 0.5f)
            {
                _nextLightning -= dt;
                if (_nextLightning <= 0f)
                {
                    _nextLightning = 2.5f + (float)_vr.NextDouble() * 6f;
                    _flash = 1f;
                    ShowBolt(focus);
                }
            }
            if (_flash > 0f)
            {
                _sun.intensity += _flash * 2.2f;
                _flash = Mathf.Max(0f, _flash - dt * 5f);
            }
            if (_boltFade > 0f)
            {
                _boltFade -= dt * 3f;
                var c = new Color(0.85f, 0.9f, 1f, Mathf.Clamp01(_boltFade));
                _bolt.startColor = _bolt.endColor = c;
                if (_boltFade <= 0f) _bolt.enabled = false;
            }

            // Colours
            Color bg = Key(h, Hours, Bg);
            Color sky = Key(h, Hours, SkyAmb);
            Color gnd = Key(h, Hours, GroundAmb);
            float lum = bg.grayscale;
            Color rainBg = new Color(0.52f, 0.56f, 0.62f) * Mathf.Lerp(0.35f, 1f, lum);
            Color stormBg = new Color(0.34f, 0.37f, 0.44f) * Mathf.Lerp(0.3f, 1f, lum);
            Color snowBg = new Color(0.84f, 0.86f, 0.90f) * Mathf.Lerp(0.3f, 1f, lum);
            bg = Color.Lerp(bg, rainBg, _rainF * 0.75f);
            bg = Color.Lerp(bg, stormBg, _stormF * 0.7f);
            bg = Color.Lerp(bg, snowBg, _snowF * 0.7f);
            sky = Color.Lerp(sky, sky * 0.7f + new Color(0.1f, 0.1f, 0.12f), overcast * 0.6f);
            bg += Color.white * (_flash * 0.35f);
            Background = bg;
            _cam.backgroundColor = bg;
            RenderSettings.fogColor = bg;
            RenderSettings.fogStartDistance = Mathf.Lerp(520f, 200f, Mathf.Max(_rainF, _snowF * 0.8f));
            RenderSettings.fogEndDistance = Mathf.Lerp(1400f, 720f, Mathf.Max(_rainF, _snowF * 0.8f));
            RenderSettings.ambientLight = sky;

            float night = Mathf.Clamp01(NightFactor + _stormF * 0.25f + _rainF * 0.1f);
            Shader.SetGlobalFloat(IdNight, night);
            Shader.SetGlobalFloat(IdSnow, _snowCover);
            Shader.SetGlobalFloat(IdWet, _wet);
            Shader.SetGlobalColor(IdSky, sky * (1f + _flash));
            Shader.SetGlobalColor(IdGround, gnd);
            Shader.SetGlobalFloat(IdTime, Time.time);

            // Seasons
            Color summerLeaf = new Color(0.40f, 0.66f, 0.34f), springLeaf = new Color(0.62f, 0.84f, 0.50f), autumnLeaf = new Color(0.96f, 0.56f, 0.22f), winterLeaf = new Color(0.55f, 0.45f, 0.35f);
            Color leaf = summerLeaf;
            leaf = Color.Lerp(leaf, springLeaf, _springF);
            leaf = Color.Lerp(leaf, autumnLeaf, _autumnF);
            leaf = Color.Lerp(leaf, winterLeaf, _winterF);
            _foliage = Color.Lerp(_foliage, leaf, k);
            Color grassCol = new Color(0.52f, 0.74f, 0.42f);
            grassCol = Color.Lerp(grassCol, new Color(0.60f, 0.80f, 0.46f), _springF);
            grassCol = Color.Lerp(grassCol, new Color(0.74f, 0.70f, 0.42f), _autumnF);
            grassCol = Color.Lerp(grassCol, new Color(0.70f, 0.74f, 0.66f), _winterF);
            _grass = Color.Lerp(_grass, grassCol, k);
            if (_city != null)
            {
                _city.FoliageMaterial.SetColor(IdBase, _foliage);
                _city.GrassMaterial.SetColor(IdBase, _grass);
                bool leaves = _winterF < 0.6f;
                if (_city.FoliageRenderer != null && _city.FoliageRenderer.enabled != leaves) _city.FoliageRenderer.enabled = leaves;
                var parkFoliage = _city.LotParkRoot != null ? _city.LotParkRoot.transform.Find("ParkFoliage") : null;
                if (parkFoliage != null) parkFoliage.gameObject.SetActive(leaves);
            }

            // Particles follow the camera focus
            var fpos = new Vector3(focus.x, 0f, focus.z);
            SetEmission(_rain, (_rainF > 0.05f ? _rainF : 0f) * (2600f + _stormF * 1800f), fpos + Vector3.up * 58f);
            SetEmission(_snow, _snowF > 0.05f ? _snowF * 700f : 0f, fpos + Vector3.up * 55f);
            SetEmission(_leaves, _autumnF > 0.5f && w.Weather == Weather.Clear ? 18f : 0f, fpos + Vector3.up * 20f);

            // River
            _waterLevel = w.RiverLevel;
            _water.position = new Vector3(0f, _waterLevel, 0f);
            float depth = _waterLevel - CityData.ChannelBottomY;
            _waterFronts.position = new Vector3(0f, CityData.ChannelBottomY, 0f);
            _waterFronts.localScale = new Vector3(1f, Mathf.Max(0.01f, depth), 1f);
            Color shallow = Color.Lerp(new Color(0.46f, 0.78f, 0.84f), new Color(0.16f, 0.24f, 0.36f), NightFactor);
            Color deep = Color.Lerp(new Color(0.20f, 0.48f, 0.62f), new Color(0.07f, 0.12f, 0.22f), NightFactor);
            shallow = Color.Lerp(shallow, new Color(0.45f, 0.52f, 0.58f), overcast * 0.5f);
            _waterMat.SetColor("_ShallowColor", shallow);
            _waterMat.SetColor("_DeepColor", deep);
            _waterMat.SetFloat("_WaveScale", 0.35f + _stormF * 0.6f);
        }

        private static void SetEmission(ParticleSystem ps, float rate, Vector3 pos)
        {
            ps.transform.position = pos;
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        private void ShowBolt(Vector3 focus)
        {
            var target = focus + new Vector3((float)_vr.NextDouble() * 160f - 80f, 0f, (float)_vr.NextDouble() * 120f - 60f);
            target.y = CityData.StreetY + 6f;
            var start = target + new Vector3((float)_vr.NextDouble() * 30f - 15f, 90f, (float)_vr.NextDouble() * 20f - 10f);
            for (int i = 0; i < _bolt.positionCount; i++)
            {
                float t = i / (float)(_bolt.positionCount - 1);
                var p = Vector3.Lerp(start, target, t);
                if (i > 0 && i < _bolt.positionCount - 1) p += new Vector3((float)_vr.NextDouble() * 8f - 4f, 0f, (float)_vr.NextDouble() * 8f - 4f);
                _bolt.SetPosition(i, p);
            }
            _bolt.enabled = true;
            _boltFade = 1f;
        }
    }
}
