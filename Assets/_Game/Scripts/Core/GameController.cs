using System;
using SmallTown.CameraControl;
using SmallTown.Commands;
using SmallTown.Simulation;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;
using SmallTown.UI;
using SmallTown.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SmallTown.Core
{
    /// <summary>
    /// Composition root: owns the session (simulation + parser + undo), steps the simulation on a
    /// fixed tick, drives the views and routes input. Views only read the simulation.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private GameAssets assets;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Light sun;
        [SerializeField] private Volume volume;

        public static GameController Instance { get; private set; }

        public TownSession Session { get; private set; }
        public TownSimulation Sim => Session.Sim;
        public GameAssets Assets => assets;
        public Camera MainCamera => mainCamera;
        public CameraRig Rig { get; private set; }
        public CityView City { get; private set; }
        public AgentRenderer Agents { get; private set; }
        public EnvironmentView Environment { get; private set; }
        public EventsView Events { get; private set; }
        public Hud Hud { get; private set; }
        public Showcase Showcase { get; private set; }
        public EntityRef Selected { get; private set; } = EntityRef.None;

        public bool Paused { get; private set; }
        public int Speed { get; private set; } = 1;
        public bool Cinema { get; private set; }
        public float Fps { get; private set; }
        public int TicksLastFrame { get; private set; }

        public event Action<ExecResult> CommandExecuted;

        private float _acc;
        private float _fpsAcc;
        private int _fpsFrames;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A controller placed in a freshly loaded scene replaces the bootstrap one.
                var old = Instance;
                Instance = null;
                if (old.Hud != null && old.Hud.Canvas != null) old.Hud.Canvas.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            Instance = this;
            Setup();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ setup

        private void Setup()
        {
            Application.targetFrameRate = 60;
            if (assets == null) assets = Resources.Load<GameAssets>(GameAssets.ResourcePath);
            if (assets == null) assets = ScriptableObject.CreateInstance<GameAssets>();
            if (assets.City == null || assets.Agents == null || assets.Water == null || assets.Glow == null || assets.ParticleAlpha == null)
                assets.FillMissing(MaterialSet.Create());

            string grammarText = assets.Grammar != null ? assets.Grammar.text : LoadGrammarFallback();
            var config = assets.Settings != null ? assets.Settings.ToConfig() : new SimConfig();
            Session = new TownSession(config, CommandGrammar.FromJson(grammarText));
            Session.SimulationReplaced += OnSimulationReplaced;

            SetupCameraAndLights();

            var cityGo = new GameObject("City");
            cityGo.transform.SetParent(transform, false);
            City = cityGo.AddComponent<CityView>();
            City.Build(Sim.City, assets);

            Agents = new AgentRenderer(assets.Agents, assets.GlowAlways, Sim.Residents.Count, Sim.Vehicles.Count);

            var envGo = new GameObject("Environment");
            envGo.transform.SetParent(transform, false);
            Environment = envGo.AddComponent<EnvironmentView>();
            Environment.Init(Sim.City, City, sun, mainCamera, assets);

            var evGo = new GameObject("Events");
            evGo.transform.SetParent(transform, false);
            Events = evGo.AddComponent<EventsView>();
            Events.Init(Sim.City, City, assets);

            var c = Sim.City;
            Rig = mainCamera.gameObject.GetComponent<CameraRig>();
            if (Rig == null) Rig = mainCamera.gameObject.AddComponent<CameraRig>();
            Rig.Init(mainCamera, new Rect(c.MinX, c.MinZ, c.MaxX - c.MinX, c.MaxZ - c.MinZ));
            Rig.Clicked += OnClicked;

            Showcase = new Showcase(this);
            Hud = gameObject.AddComponent<Hud>();
            Hud.Build(this);
            Environment.UpdateView(Sim, 0.016f, Rig.Focus, true);
        }

        private static string LoadGrammarFallback()
        {
#if UNITY_EDITOR
            string path = System.IO.Path.Combine(Application.dataPath, "_Game/Configs/CommandGrammar.json");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
#endif
            var ta = Resources.Load<TextAsset>("CommandGrammar");
            if (ta != null) return ta.text;
            Debug.LogError("[ST] CommandGrammar.json not found — run Tools/Small Town/Build Everything.");
            return "{\"connectors\":[],\"lexicon\":{}}";
        }

        private void SetupCameraAndLights()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                mainCamera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.fieldOfView = 30f;
            mainCamera.allowMSAA = true;
            var data = mainCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = true;

            if (sun == null) sun = RenderSettings.sun;
            if (sun == null)
            {
                var lightGo = new GameObject("Sun");
                sun = lightGo.AddComponent<Light>();
            }
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowNormalBias = 0.6f;
            sun.shadowBias = 0.2f;
            RenderSettings.sun = sun;

            if (volume == null)
            {
                var volGo = new GameObject("Global Volume");
                volGo.transform.SetParent(transform, false);
                volume = volGo.AddComponent<Volume>();
                volume.isGlobal = true;
            }
            if (volume.sharedProfile == null) volume.sharedProfile = assets.PostProfile != null ? assets.PostProfile : CreateRuntimeProfile();
        }

        private static VolumeProfile CreateRuntimeProfile()
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.65f);
            bloom.scatter.Override(0.65f);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.45f);
            var ca = p.Add<ColorAdjustments>(true);
            ca.contrast.Override(14f);
            ca.saturation.Override(16f);
            return p;
        }

        private void OnSimulationReplaced()
        {
            Select(EntityRef.None);
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fpsAcc += dt;
            _fpsFrames++;
            if (_fpsAcc >= 0.5f)
            {
                Fps = _fpsFrames / _fpsAcc;
                _fpsAcc = 0f;
                _fpsFrames = 0;
            }

            HandleHotkeys();
            Showcase.Update(dt);

            int ticks = 0;
            float tickDt = Sim.Dt;
            if (!Paused)
            {
                _acc += Mathf.Min(dt, 0.25f) * Speed;
                while (_acc >= tickDt && ticks < 16)
                {
                    Sim.Step();
                    _acc -= tickDt;
                    ticks++;
                }
                if (ticks >= 16) _acc = 0f;
            }
            TicksLastFrame = ticks;
            float alpha = Paused ? 1f : Mathf.Clamp01(_acc / tickDt);

            if (Sim.Notifications.Count > 0)
            {
                foreach (var n in Sim.Notifications) Hud.Toast(n);
                Sim.Notifications.Clear();
            }

            Environment.UpdateView(Sim, dt, Rig.Focus);
            Agents.Draw(Sim, alpha, Time.time, null);
            Events.UpdateView(Sim, dt, Environment.NightFactor, Agents, Selected);
        }

        /// <summary>Advances the simulation synchronously (used by tests and screenshot capture).</summary>
        public void AdvanceTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++) Sim.Step();
            Environment.UpdateView(Sim, 0.5f, Rig.Focus, true);
        }

        /// <summary>Renders the agents for a specific camera immediately (screenshots).</summary>
        public void DrawAgentsFor(Camera cam)
        {
            Agents.Draw(Sim, 1f, Time.time, cam);
            Events.UpdateView(Sim, 1f, Environment.NightFactor, Agents, Selected);
        }

        private void HandleHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool typing = Hud != null && Hud.InputFocused;
            Rig.TextInputFocused = typing;
            if (typing) return;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (Cinema) SetCinema(false);
                else if (Showcase.Active) Showcase.Stop();
                else if (!Hud.CloseTopPanel()) Select(EntityRef.None);
            }
            if (Cinema)
            {
                if (kb.cKey.wasPressedThisFrame) SetCinema(false);
                return;
            }
            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.zKey.wasPressedThisFrame) Undo();
            if (kb.cKey.wasPressedThisFrame) SetCinema(true);
            if (kb.slashKey.wasPressedThisFrame || kb.numpadDivideKey.wasPressedThisFrame)
            {
                if (shift) Hud.ToggleHelp();
                else Hud.FocusInput();
            }
            if (kb.f1Key.wasPressedThisFrame) Hud.ToggleHelp();
        }

        // ------------------------------------------------------------------ public API (UI, tests)

        public ExecResult Execute(string text)
        {
            var r = Session.Execute(text);
            foreach (var ui in r.UiActions) ApplyUiAction(ui);
            if (r.Outcomes.Count > 0)
            {
                var e = Session.Last;
                if (!e.IsNone && e.Kind == EntityKind.Place && !Showcase.Active && !Cinema) FocusPlace(e.Id, true);
            }
            CommandExecuted?.Invoke(r);
            if (Hud != null) Hud.ShowResult(r);
            return r;
        }

        public ExecResult ExecuteWorld(WorldCommand cmd)
        {
            var r = Session.ExecuteWorld(cmd);
            CommandExecuted?.Invoke(r);
            if (Hud != null) Hud.ShowResult(r);
            return r;
        }

        private void ApplyUiAction(CommandSpec s)
        {
            switch (s.Kind)
            {
                case SpecKind.Pause: SetPaused(true); break;
                case SpecKind.Resume: SetPaused(false); break;
                case SpecKind.Faster: SetSpeed(Speed >= 4 ? 4 : Speed * 2); break;
                case SpecKind.Slower: SetSpeed(Speed <= 1 ? 1 : Speed / 2); break;
                case SpecKind.SetSpeed: SetSpeed(s.Value >= 3f ? 4 : (s.Value >= 1.5f ? 2 : 1)); break;
                case SpecKind.Show: FocusPlace(s.Place, false); break;
                case SpecKind.Help: Hud?.ToggleHelp(); break;
            }
        }

        public void Undo()
        {
            bool ok = Session.Undo();
            Hud?.Toast(ok ? "Отменено: мир вернулся к моменту до последнего изменения" : "Отменять пока нечего");
        }

        public void ResetWorld()
        {
            if (Showcase.Active) Showcase.Stop();
            Session.ResetWorld();
            _acc = 0f;
            Rig.ResetView();
            Environment.UpdateView(Sim, 0.016f, Rig.Focus, true);
            Hud?.Toast("Мир начат заново");
        }

        public void TogglePause() => SetPaused(!Paused);

        public void SetPaused(bool p)
        {
            Paused = p;
            Hud?.RefreshTopBar();
        }

        public void SetSpeed(int s)
        {
            Speed = Mathf.Clamp(s, 1, 4);
            if (Speed == 3) Speed = 4;
            Paused = false;
            Hud?.RefreshTopBar();
        }

        public void SetCinema(bool on)
        {
            Cinema = on;
            Rig.CinemaMode = on;
            Hud?.SetCinema(on);
        }

        public void Select(EntityRef e)
        {
            Selected = e;
            Session.Selected = e.Kind == EntityKind.Place || e.Kind == EntityKind.Building ? e : (e.IsNone ? EntityRef.None : e);
            Hud?.ShowInspector(e);
        }

        public Vector3 PlacePosition(int placeId)
        {
            var p = Sim.City.Places[placeId];
            return new Vector3(p.X, CityData.StreetY, p.Z);
        }

        public void FocusPlace(int placeId, bool gentle)
        {
            if (placeId < 0 || placeId >= Sim.City.Places.Count) return;
            var p = Sim.City.Places[placeId];
            float dist;
            switch (p.Kind)
            {
                case PlaceKind.River: dist = 260f; break;
                case PlaceKind.Promenade: dist = 220f; break;
                case PlaceKind.Park:
                case PlaceKind.Square: dist = 125f; break;
                default: dist = 115f; break;
            }
            if (gentle) dist = Mathf.Max(dist, Mathf.Min(Rig.Distance, 200f));
            Rig.FocusOn(new Vector3(p.X, 0f, p.Z), dist);
        }

        private void OnClicked(Vector2 screen)
        {
            if (Cinema) return;
            if (Agents.Pick(Sim, mainCamera, screen, 18f, out bool isVehicle, out int id))
            {
                Select(new EntityRef(isVehicle ? EntityKind.Vehicle : EntityKind.Resident, id));
                return;
            }
            var ray = mainCamera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, 3000f))
            {
                var pt = hit.collider.GetComponent<PickTarget>();
                if (pt != null)
                {
                    Select(pt.Entity);
                    return;
                }
            }
            Select(EntityRef.None);
        }
    }
}
