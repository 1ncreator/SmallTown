using System.Collections.Generic;
using SmallTown.Commands;
using SmallTown.Core;
using SmallTown.Simulation;
using SmallTown.Simulation.Agents;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;
using SmallTown.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SmallTown.UI
{
    /// <summary>The whole Russian-language interface, built procedurally with uGUI.</summary>
    public sealed class Hud : MonoBehaviour
    {
        private GameController _gc;
        private RectTransform _root;
        private CanvasGroup _group;
        private WorldLabels _labels;
        private AmbientAudio _audio;

        private Text _clock, _envText, _statOutside, _statWaiting, _statUnavailable, _statCars, _perf;
        private Button _pauseBtn, _undoBtn, _soundBtn;
        private readonly Button[] _speedBtns = new Button[3];

        private InputField _input;
        private readonly Button[] _chips = new Button[3];
        private readonly string[] _chipTexts = new string[3];
        private float _chipTimer;

        private RectTransform _card;
        private Text _cardTitle, _cardBody, _cardNote;
        private RectTransform _cardOptions;
        private float _cardTimer;

        private RectTransform _toasts;
        private readonly List<CanvasGroup> _toastItems = new List<CanvasGroup>();
        private readonly List<float> _toastTimes = new List<float>();

        private RectTransform _inspector;
        private Text _insTitle, _insSub, _insBody;
        private RectTransform _insActions;
        private EntityRef _insEntity = EntityRef.None;
        private float _insTimer;
        private string _insActionsKey = "";
        private readonly List<string> _lines = new List<string>();

        private RectTransform _settings;
        private Slider _riverSlider, _timeSlider;
        private Text _riverLabel, _timeLabel;
        private float _riverDebounce = -1f, _timeDebounce = -1f;
        private readonly Button[] _weatherBtns = new Button[4];
        private readonly Button[] _seasonBtns = new Button[4];

        private RectTransform _help;
        private RectTransform _showcaseBar;
        private Text _showcaseText;
        private Text _cinemaHint;
        private float _cinemaHintTimer;
        private float _statsTimer;
        private int _historyIdx = -1;
        private bool _focusNextFrame;

        public bool InputFocused => _input != null && _input.isFocused;
        public RectTransform Root => _root;
        public Canvas Canvas { get; private set; }

        public void Build(GameController gc)
        {
            _gc = gc;
            UIKit.Init();
            EnsureEventSystem();

            var canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)canvasGo.transform;
            _group = canvasGo.GetComponent<CanvasGroup>();

            var labelsLayer = UIKit.Rect("WorldLabels", _root);
            UIKit.Stretch(labelsLayer);
            _labels = labelsLayer.gameObject.AddComponent<WorldLabels>();
            _labels.Build(gc, labelsLayer);

            BuildStats();
            BuildTopBar();
            BuildToasts();
            BuildInspector();
            BuildSettings();
            BuildCommandArea();
            BuildShowcaseBar();
            BuildHelp();
            BuildCinemaHint();

            var audioGo = new GameObject("AmbientAudio");
            audioGo.transform.SetParent(transform, false);
            _audio = audioGo.AddComponent<AmbientAudio>();

            RefreshTopBar();
            RefreshSuggestions();
            HideCard();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ------------------------------------------------------------------ builders

        private RectTransform Column(Image panel, int pad, float spacing)
        {
            UIKit.VLayout(panel.gameObject, spacing, new RectOffset(pad, pad, pad, pad));
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return panel.rectTransform;
        }

        private void BuildStats()
        {
            var p = UIKit.Panel(_root, "Stats");
            UIKit.Place(p.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(330f, 0f));
            var col = Column(p, 20, 6f);
            var title = UIKit.Label(col, "Small Town", 24, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(title, -1f, 30f);
            _clock = UIKit.Label(col, "", 17, UIKit.Ink);
            UIKit.Size(_clock, -1f, 22f);
            _envText = UIKit.Label(col, "", 15, UIKit.Muted);
            UIKit.Size(_envText, -1f, 20f);
            UIKit.Divider(col);
            _statOutside = StatRow(col, "человек на улице");
            _statWaiting = StatRow(col, "машин ждёт");
            _statUnavailable = StatRow(col, "маршрутов недоступно");
            _statCars = UIKit.Label(col, "", 14, UIKit.Muted);
            UIKit.Size(_statCars, -1f, 18f);
            _perf = UIKit.Label(col, "", 12, new Color(0.62f, 0.65f, 0.7f));
            UIKit.Size(_perf, -1f, 16f);
        }

        private Text StatRow(Transform parent, string label)
        {
            var row = UIKit.Rect("Stat", parent);
            UIKit.HLayout(row.gameObject, 10f, new RectOffset(0, 0, 0, 0));
            UIKit.Size(row, -1f, 32f);
            var num = UIKit.Label(row, "0", 26, UIKit.Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            UIKit.Size(num, 64f, 32f);
            var l = UIKit.Label(row, label, 16, UIKit.Muted);
            UIKit.Size(l, 220f, 32f);
            return num;
        }

        private void BuildTopBar()
        {
            var p = UIKit.Panel(_root, "TopBar");
            UIKit.Place(p.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(0f, 0f));
            UIKit.HLayout(p.gameObject, 6f, new RectOffset(10, 10, 8, 8), TextAnchor.MiddleCenter);
            var fit = p.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = p.transform;
            _pauseBtn = UIKit.Button(t, "Пауза", () => _gc.TogglePause(), 16, null, null, 84f);
            for (int i = 0; i < 3; i++)
            {
                int s = i == 0 ? 1 : (i == 1 ? 2 : 4);
                _speedBtns[i] = UIKit.Button(t, s + "×", () => _gc.SetSpeed(s), 16, null, null, 48f);
            }
            Spacer(t, 8f);
            _undoBtn = UIKit.Button(t, "Отменить", () => _gc.Undo(), 16);
            UIKit.Button(t, "Сброс мира", () => _gc.ResetWorld(), 16);
            Spacer(t, 8f);
            UIKit.Button(t, "Настройки мира", ToggleSettings, 16);
            UIKit.Button(t, "Показ", () => { if (_gc.Showcase.Active) _gc.Showcase.Stop(); else _gc.Showcase.Start(); }, 16);
            UIKit.Button(t, "Кино", () => _gc.SetCinema(true), 16);
            _soundBtn = UIKit.Button(t, "Звук: выкл", ToggleSound, 16);
            UIKit.Button(t, "?", ToggleHelp, 16, UIKit.AccentSoft, UIKit.Accent, 40f);
        }

        private static void Spacer(Transform t, float w)
        {
            var s = UIKit.Rect("Spacer", t);
            UIKit.Size(s, w, 10f);
        }

        private void BuildToasts()
        {
            _toasts = UIKit.Rect("Toasts", _root);
            UIKit.Place(_toasts, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(620f, 0f));
            var v = UIKit.VLayout(_toasts.gameObject, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter);
            v.childForceExpandWidth = false;
            v.childControlWidth = true;
            var fit = _toasts.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildInspector()
        {
            var p = UIKit.Panel(_root, "Inspector");
            UIKit.Place(p.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(370f, 0f));
            var col = Column(p, 20, 6f);
            var header = UIKit.Rect("Header", col);
            UIKit.HLayout(header.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            UIKit.Size(header, -1f, 30f);
            _insTitle = UIKit.Label(header, "", 20, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(_insTitle, 282f, 30f);
            UIKit.Button(header, "×", () => _gc.Select(EntityRef.None), 18, UIKit.Chip, UIKit.Muted, 32f, 30f);
            _insSub = UIKit.Label(col, "", 14, UIKit.Muted);
            UIKit.Divider(col);
            _insBody = UIKit.Label(col, "", 16, UIKit.Ink);
            _insBody.lineSpacing = 1.15f;
            _insActions = UIKit.Rect("Actions", col);
            UIKit.VLayout(_insActions.gameObject, 6f, new RectOffset(0, 0, 6, 0));
            _inspector = p.rectTransform;
            _inspector.gameObject.SetActive(false);
        }

        private void BuildSettings()
        {
            var p = UIKit.Panel(_root, "Settings");
            UIKit.Place(p.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(390f, 0f));
            var col = Column(p, 22, 8f);
            var header = UIKit.Rect("Header", col);
            UIKit.HLayout(header.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            UIKit.Size(header, -1f, 30f);
            var t = UIKit.Label(header, "Настройки мира", 20, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(t, 298f, 30f);
            UIKit.Button(header, "×", ToggleSettings, 18, UIKit.Chip, UIKit.Muted, 32f, 30f);

            Section(col, "Погода");
            var wrow = Row(col);
            string[] wn = { "Ясно", "Дождь", "Гроза", "Снег" };
            for (int i = 0; i < 4; i++)
            {
                int w = i;
                _weatherBtns[i] = UIKit.Button(wrow, wn[i], () => _gc.ExecuteWorld(new WorldCommand(CommandKind.SetWeather, w)), 15, null, null, 76f, 36f);
            }

            _riverLabel = Section(col, "Уровень реки");
            _riverSlider = UIKit.Slider(col, CityData.MinRiverLevel, CityData.MaxRiverLevel, 0f, v => { _riverDebounce = 0.45f; UpdateSliderLabels(); });
            var rrow = Row(col);
            UIKit.Button(rrow, "+50 см", () => _gc.Execute("подними реку на 50 см"), 14, null, null, 0f, 32f);
            UIKit.Button(rrow, "+1 м", () => _gc.Execute("подними реку на 1 метр"), 14, null, null, 0f, 32f);
            UIKit.Button(rrow, "Норма", () => _gc.Execute("верни реку в норму"), 14, null, null, 0f, 32f);

            _timeLabel = Section(col, "Время суток");
            _timeSlider = UIKit.Slider(col, 0f, 23.99f, 8f, v => { _timeDebounce = 0.45f; UpdateSliderLabels(); });
            var trow = Row(col);
            string[] tn = { "Рассвет", "Утро", "День", "Закат", "Ночь" };
            float[] th = { 6f, 8f, 13f, 19.5f, 23f };
            for (int i = 0; i < 5; i++)
            {
                float hh = th[i];
                UIKit.Button(trow, tn[i], () => _gc.ExecuteWorld(new WorldCommand(CommandKind.SetTime, 0, hh)), 13, null, null, 0f, 32f);
            }

            Section(col, "Время года");
            var srow = Row(col);
            string[] sn = { "Весна", "Лето", "Осень", "Зима" };
            for (int i = 0; i < 4; i++)
            {
                int s = i;
                _seasonBtns[i] = UIKit.Button(srow, sn[i], () => _gc.ExecuteWorld(new WorldCommand(CommandKind.SetSeason, s)), 15, null, null, 76f, 36f);
            }
            var hint = UIKit.Label(col, "Каждое изменение можно отменить (Z).", 13, UIKit.Muted);
            UIKit.Size(hint, -1f, 20f);
            _settings = p.rectTransform;
            _settings.gameObject.SetActive(false);
        }

        private Text Section(Transform parent, string title)
        {
            var l = UIKit.Label(parent, title, 15, UIKit.Muted, FontStyle.Bold);
            UIKit.Size(l, -1f, 26f);
            return l;
        }

        private static RectTransform Row(Transform parent)
        {
            var r = UIKit.Rect("Row", parent);
            UIKit.HLayout(r.gameObject, 6f, new RectOffset(0, 0, 0, 0));
            UIKit.Size(r, -1f, 36f);
            return r;
        }

        private void BuildCommandArea()
        {
            var area = UIKit.Rect("CommandArea", _root);
            UIKit.Place(area, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(880f, 0f));
            var v = UIKit.VLayout(area.gameObject, 10f, new RectOffset(0, 0, 0, 0), TextAnchor.LowerCenter);
            v.childForceExpandWidth = true;
            var fit = area.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Consequence / question card
            var card = UIKit.Panel(area, "Card");
            var cc = Column(card, 20, 6f);
            var header = UIKit.Rect("Header", cc);
            UIKit.HLayout(header.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            UIKit.Size(header, -1f, 30f);
            _cardTitle = UIKit.Label(header, "", 21, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(_cardTitle, 780f, 30f);
            UIKit.Button(header, "×", HideCard, 18, UIKit.Chip, UIKit.Muted, 32f, 30f);
            _cardBody = UIKit.Label(cc, "", 18, UIKit.Ink);
            _cardBody.lineSpacing = 1.2f;
            _cardNote = UIKit.Label(cc, "", 16, UIKit.Muted, FontStyle.Italic);
            _cardOptions = UIKit.Rect("Options", cc);
            UIKit.HLayout(_cardOptions.gameObject, 8f, new RectOffset(0, 0, 4, 0));
            UIKit.Size(_cardOptions, -1f, 42f);
            _card = card.rectTransform;

            // Input line
            var bar = UIKit.Panel(area, "InputBar");
            UIKit.HLayout(bar.gameObject, 10f, new RectOffset(10, 10, 10, 10));
            UIKit.Size(bar, -1f, 70f);
            _input = UIKit.Input(bar.transform, "Изменить мир...  (например: «закрой северный мост»)", 19);
            var le = _input.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 50f;
            _input.onEndEdit.AddListener(OnEndEdit);
            UIKit.Button(bar.transform, "Изменить", Submit, 17, UIKit.Accent, Color.white, 130f, 50f);

            // Suggestions
            var chips = UIKit.Rect("Suggestions", area);
            UIKit.HLayout(chips.gameObject, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
            UIKit.Size(chips, -1f, 36f);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _chips[i] = UIKit.Button(chips, "…", () => RunChip(k), 15, new Color(1f, 1f, 1f, 0.92f), UIKit.Accent, 0f, 34f);
            }
        }

        private void BuildShowcaseBar()
        {
            var p = UIKit.Panel(_root, "ShowcaseBar");
            UIKit.Place(p.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(760f, 56f));
            UIKit.HLayout(p.gameObject, 10f, new RectOffset(18, 10, 8, 8));
            _showcaseText = UIKit.Label(p.transform, "", 16, UIKit.Ink);
            var le = _showcaseText.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            UIKit.Button(p.transform, "Остановить", () => _gc.Showcase.Stop(), 15, UIKit.Chip, UIKit.Danger, 120f, 38f);
            _showcaseBar = p.rectTransform;
            _showcaseBar.gameObject.SetActive(false);
        }

        private void BuildHelp()
        {
            var p = UIKit.Panel(_root, "Help");
            UIKit.Place(p.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(980f, 700f));
            var header = UIKit.Rect("Header", p.transform);
            UIKit.Place(header, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -20f), new Vector2(924f, 34f));
            UIKit.HLayout(header.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            var t = UIKit.Label(header, "Как изменить город — нажмите на пример", 22, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(t, 880f, 34f);
            UIKit.Button(header, "×", ToggleHelp, 18, UIKit.Chip, UIKit.Muted, 34f, 32f);

            // Scrollable examples (left)
            var view = UIKit.Rect("Viewport", p.transform);
            UIKit.Place(view, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -66f), new Vector2(600f, 610f));
            view.gameObject.AddComponent<RectMask2D>();
            var bgi = view.gameObject.AddComponent<Image>();
            bgi.color = new Color(1f, 1f, 1f, 0.001f);
            var content = UIKit.Rect("Content", view);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            UIKit.VLayout(content.gameObject, 6f, new RectOffset(0, 12, 0, 12));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 28f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            HelpGroup(content, "Мосты", "закрой северный мост", "перекрой мост на юге", "открой оба моста", "close the north bridge");
            HelpGroup(content, "Река", "подними реку на 50 см", "подними воду на полметра", "raise the river by 1 meter", "верни реку в норму");
            HelpGroup(content, "Погода", "пусть пойдёт дождь", "начни грозу", "пусть идёт снег", "make it sunny");
            HelpGroup(content, "Время суток", "сделай рассвет", "сделай ночь", "сделай 18:30", "верни утро");
            HelpGroup(content, "Времена года", "наступает осень", "наступает зима", "spring");
            HelpGroup(content, "События", "начни фестиваль в парке", "закончи фестиваль", "преврати парковку в парк", "верни парковку", "ограбь банк", "подожги школу", "потуши пожар");
            HelpGroup(content, "Сложные фразы", "закрой северный мост и включи дождь", "подожги это (после клика по зданию)", "открой его", "закрой мост", "покажи маяк");

            // Controls (right)
            var right = UIKit.Rect("Controls", p.transform);
            UIKit.Place(right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(652f, -66f), new Vector2(300f, 610f));
            UIKit.VLayout(right.gameObject, 4f, new RectOffset(0, 0, 0, 0));
            var ct = UIKit.Label(right, "Управление", 17, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(ct, -1f, 26f);
            string controls =
                "Перетаскивание — сдвиг карты\nКолесо / щипок, + и − — масштаб\nПравая кнопка — поворот\nСтрелки — движение камеры\n0 — стандартный вид\n\n" +
                "Пробел — пауза\nZ — отменить изменение\nC — кинорежим\n/ — строка команд\n? — эта справка\nEsc — закрыть или очистить\n↑ ↓ в строке — история команд\n\n" +
                "Клик по жителю или машине — кто это и куда идёт. Клик по зданию, мосту или подписи — действия для этого места.\n\n" +
                "Команды понимают русский и английский. Если непонятно, какой объект, — я переспрошу.";
            var cl = UIKit.Label(right, controls, 14, UIKit.Muted);
            cl.lineSpacing = 1.15f;
            _help = p.rectTransform;
            _help.gameObject.SetActive(false);
        }

        private void HelpGroup(Transform parent, string title, params string[] examples)
        {
            var h = UIKit.Label(parent, title, 16, UIKit.Ink, FontStyle.Bold);
            UIKit.Size(h, -1f, 26f);
            var grid = UIKit.Rect("Grid", parent);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(290f, 34f);
            g.spacing = new Vector2(8f, 6f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            int rows = (examples.Length + 1) / 2;
            UIKit.Size(grid, -1f, rows * 40f);
            foreach (var e in examples)
            {
                string cmd = e.Contains("(") ? e.Substring(0, e.IndexOf('(')).Trim() : e;
                var b = UIKit.Button(grid, e, () => { ToggleHelp(); _gc.Execute(cmd); }, 14, UIKit.Chip, UIKit.Ink, 0f, 34f);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
        }

        private void BuildCinemaHint()
        {
            // lives on its own canvas so it stays visible while the HUD is hidden
            var go = new GameObject("CinemaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 20;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            _cinemaHint = UIKit.Label(go.transform, "Кинорежим · C или Esc — выйти", 18, new Color(1f, 1f, 1f, 0.9f), FontStyle.Normal, TextAnchor.MiddleCenter);
            UIKit.Place(_cinemaHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(600f, 40f));
            var sh = _cinemaHint.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
            _cinemaHint.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_gc == null) return;
            float dt = Time.unscaledDeltaTime;
            _statsTimer -= dt;
            if (_statsTimer <= 0f)
            {
                _statsTimer = 0.25f;
                RefreshStats();
            }
            _chipTimer -= dt;
            if (_chipTimer <= 0f)
            {
                _chipTimer = 2f;
                RefreshSuggestions();
            }
            if (_cardTimer > 0f)
            {
                _cardTimer -= dt;
                if (_cardTimer <= 0f) HideCard();
            }
            UpdateToasts(dt);
            if (_inspector.gameObject.activeSelf)
            {
                _insTimer -= dt;
                if (_insTimer <= 0f)
                {
                    _insTimer = 0.4f;
                    RefreshInspector();
                }
            }
            UpdateSettings(dt);
            bool sc = _gc.Showcase.Active;
            if (_showcaseBar.gameObject.activeSelf != sc) _showcaseBar.gameObject.SetActive(sc);
            if (sc) _showcaseText.text = "Показ · " + (_gc.Showcase.Index + 1) + "/" + _gc.Showcase.Count + " · " + _gc.Showcase.Caption;
            if (_cinemaHintTimer > 0f)
            {
                _cinemaHintTimer -= dt;
                var c = _cinemaHint.color;
                c.a = Mathf.Clamp01(_cinemaHintTimer / 1.2f) * 0.9f;
                _cinemaHint.color = c;
            }
            if (_focusNextFrame)
            {
                _focusNextFrame = false;
                _input.ActivateInputField();
                _input.Select();
            }
            HandleInputKeys();
            _audio.SetState(_gc.Sim, _gc.Environment.NightFactor);
        }

        private void HandleInputKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || !_input.isFocused) return;
            var hist = _gc.Session.History;
            if (kb.upArrowKey.wasPressedThisFrame && hist.Count > 0)
            {
                _historyIdx = _historyIdx < 0 ? hist.Count - 1 : Mathf.Max(0, _historyIdx - 1);
                SetInputText(hist[_historyIdx]);
            }
            else if (kb.downArrowKey.wasPressedThisFrame && hist.Count > 0)
            {
                if (_historyIdx < 0) return;
                _historyIdx++;
                if (_historyIdx >= hist.Count)
                {
                    _historyIdx = -1;
                    SetInputText("");
                }
                else SetInputText(hist[_historyIdx]);
            }
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_input.text.Length > 0) SetInputText("");
                else _input.DeactivateInputField();
                HideCard();
            }
        }

        private void SetInputText(string s)
        {
            _input.text = s;
            _input.caretPosition = s.Length;
            _input.selectionAnchorPosition = s.Length;
            _input.selectionFocusPosition = s.Length;
        }

        private void RefreshStats()
        {
            var sim = _gc.Sim;
            var w = sim.World;
            _clock.text = "День " + (sim.Day + 1) + " · " + MathUtil.FormatHour(sim.Hour) + " · " + TownSimulation.PhaseName(sim.Hour) + (_gc.Paused ? " · пауза" : "");
            string river = Mathf.Abs(w.RiverTarget) < 0.01f ? "река в норме" : "река " + RuText.SignedMetres(w.RiverTarget);
            string extra = "";
            if (w.Festival) extra += " · фестиваль";
            if (w.FireActive) extra += " · пожар!";
            if (w.RobberyActive) extra += " · ограбление!";
            if (w.NorthClosed || w.SouthClosed) extra += " · мост закрыт";
            _envText.text = Cap(TownSimulation.WeatherName(w.Weather)) + " · " + TownSimulation.SeasonName(w.Season) + " · " + river + extra;
            var s = sim.Stats;
            _statOutside.text = s.PeopleOutside.ToString();
            _statWaiting.text = s.CarsWaiting.ToString();
            _statWaiting.color = s.CarsWaiting > 0 ? UIKit.Ink : UIKit.Ink;
            _statUnavailable.text = s.RoutesUnavailable.ToString();
            _statUnavailable.color = s.RoutesUnavailable > 0 ? UIKit.Danger : UIKit.Ink;
            _statCars.text = RuText.Cars(s.CarsOnRoad) + " на дорогах · " + RuText.People(s.PeopleWalking) + " в пути";
            _perf.text = Mathf.RoundToInt(_gc.Fps) + " FPS · скорость " + _gc.Speed + "×";
            if (_undoBtn != null) _undoBtn.interactable = _gc.Session.CanUndo;
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        public void RefreshTopBar()
        {
            if (_pauseBtn == null) return;
            UIKit.SetButtonText(_pauseBtn, _gc.Paused ? "Пуск" : "Пауза");
            int[] sp = { 1, 2, 4 };
            for (int i = 0; i < 3; i++)
            {
                bool on = _gc.Speed == sp[i] && !_gc.Paused;
                _speedBtns[i].GetComponent<Image>().color = on ? UIKit.Accent : UIKit.Chip;
                _speedBtns[i].GetComponentInChildren<Text>().color = on ? Color.white : UIKit.Ink;
            }
            _pauseBtn.GetComponent<Image>().color = _gc.Paused ? UIKit.Accent : UIKit.Chip;
            _pauseBtn.GetComponentInChildren<Text>().color = _gc.Paused ? Color.white : UIKit.Ink;
        }

        private void RefreshSuggestions()
        {
            var list = SuggestionProvider.Get(_gc.Sim, _gc.Session.CommandsExecuted);
            for (int i = 0; i < 3; i++)
            {
                bool on = i < list.Count;
                _chips[i].gameObject.SetActive(on);
                if (!on) continue;
                _chipTexts[i] = list[i];
                UIKit.SetButtonText(_chips[i], list[i]);
            }
        }

        private void RunChip(int i)
        {
            if (string.IsNullOrEmpty(_chipTexts[i])) return;
            _gc.Execute(_chipTexts[i]);
        }

        // ------------------------------------------------------------------ commands & results

        private void OnEndEdit(string text)
        {
            var kb = Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.enterKey.isPressed || kb.numpadEnterKey.isPressed);
            if (enter) Submit();
        }

        private void Submit()
        {
            string text = _input.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _historyIdx = -1;
            _input.text = "";
            _gc.Execute(text);
            _focusNextFrame = true;
        }

        public void FocusInput() => _focusNextFrame = true;

        public void ShowResult(ExecResult r)
        {
            ClearOptions();
            _cardNote.text = "";
            switch (r.Status)
            {
                case ParseStatus.Ambiguous:
                    _cardTitle.text = r.Question;
                    _cardBody.text = "";
                    foreach (var o in r.Options)
                    {
                        string text = o.Text;
                        UIKit.Button(_cardOptions, o.Label, () => _gc.Execute(text), 16, UIKit.AccentSoft, UIKit.Accent, 0f, 38f);
                        _optionCount++;
                    }
                    ShowCard(0f);
                    break;
                case ParseStatus.Ok:
                    if (r.Outcomes.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(r.Title)) Toast(r.Title);
                        else if (r.UiActions.Count > 0) Toast("Готово");
                        return;
                    }
                    _cardTitle.text = r.Title;
                    _cardBody.text = Bullets(r.Lines);
                    _cardNote.text = r.Note;
                    ShowCard(12f);
                    break;
                default:
                    _cardTitle.text = string.IsNullOrEmpty(r.Title) ? "Не понял" : r.Title;
                    _cardBody.text = "";
                    _cardNote.text = r.Note;
                    for (int i = 0; i < 3; i++)
                    {
                        if (string.IsNullOrEmpty(_chipTexts[i])) continue;
                        string text = _chipTexts[i];
                        UIKit.Button(_cardOptions, text, () => _gc.Execute(text), 15, UIKit.Chip, UIKit.Accent, 0f, 36f);
                        _optionCount++;
                    }
                    ShowCard(12f);
                    break;
            }
            _cardNote.gameObject.SetActive(!string.IsNullOrEmpty(_cardNote.text));
            _cardBody.gameObject.SetActive(!string.IsNullOrEmpty(_cardBody.text));
            RefreshSuggestions();
            RefreshTopBar();
            if (_insEntity.Kind != EntityKind.None) _insTimer = 0f;
        }

        private static string Bullets(List<string> lines)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("•  ").Append(lines[i]);
            }
            return sb.ToString();
        }

        private int _optionCount;

        private void ClearOptions()
        {
            for (int i = _cardOptions.childCount - 1; i >= 0; i--) Destroy(_cardOptions.GetChild(i).gameObject);
            _optionCount = 0;
        }

        private void ShowCard(float seconds)
        {
            _card.gameObject.SetActive(true);
            _cardOptions.gameObject.SetActive(_optionCount > 0);
            _cardTimer = seconds;
            LayoutRebuilder.MarkLayoutForRebuild(_card);
        }

        private void HideCard()
        {
            if (_card != null) _card.gameObject.SetActive(false);
            _cardTimer = 0f;
        }

        public string CardTitle => _card != null && _card.gameObject.activeSelf ? _cardTitle.text : "";

        public void Toast(string message)
        {
            if (_toasts == null) return;
            var p = UIKit.Panel(_toasts, "Toast", true, 2.4f);
            UIKit.HLayout(p.gameObject, 0f, new RectOffset(18, 18, 10, 10), TextAnchor.MiddleCenter);
            var t = UIKit.Label(p.transform, message, 15, UIKit.Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var cg = p.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            _toastItems.Add(cg);
            _toastTimes.Add(6f);
            while (_toastItems.Count > 4)
            {
                Destroy(_toastItems[0].gameObject);
                _toastItems.RemoveAt(0);
                _toastTimes.RemoveAt(0);
            }
        }

        private void UpdateToasts(float dt)
        {
            for (int i = _toastItems.Count - 1; i >= 0; i--)
            {
                _toastTimes[i] -= dt;
                _toastItems[i].alpha = Mathf.Clamp01(_toastTimes[i] / 0.8f);
                if (_toastTimes[i] <= 0f)
                {
                    Destroy(_toastItems[i].gameObject);
                    _toastItems.RemoveAt(i);
                    _toastTimes.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ inspector

        public void ShowInspector(EntityRef e)
        {
            _insEntity = e;
            _insActionsKey = "";
            if (e.IsNone)
            {
                _inspector.gameObject.SetActive(false);
                return;
            }
            if (_settings.gameObject.activeSelf) _settings.gameObject.SetActive(false);
            _inspector.gameObject.SetActive(true);
            _insTimer = 0f;
            RefreshInspector();
        }

        private void RefreshInspector()
        {
            var sim = _gc.Sim;
            var e = _insEntity;
            string title, sub;
            switch (e.Kind)
            {
                case EntityKind.Resident:
                    if (e.Id < 0 || e.Id >= sim.Residents.Count) return;
                    Describer.DescribeResident(sim, sim.Residents[e.Id], out title, out sub, _lines);
                    break;
                case EntityKind.Vehicle:
                    if (e.Id < 0 || e.Id >= sim.Vehicles.Count) return;
                    Describer.DescribeVehicle(sim, sim.Vehicles[e.Id], out title, out sub, _lines);
                    break;
                case EntityKind.Building:
                    Describer.DescribeBuilding(sim, sim.City.Buildings[e.Id], out title, out sub, _lines);
                    break;
                case EntityKind.Place:
                    Describer.DescribePlace(sim, sim.City.Places[e.Id], out title, out sub, _lines);
                    break;
                default:
                    return;
            }
            _insTitle.text = title;
            _insSub.text = sub;
            _insBody.text = string.Join("\n", _lines);
            var actions = ActionsFor(e);
            string key = string.Join("|", actions.ConvertAll(a => a.Key + a.Value));
            if (key != _insActionsKey)
            {
                _insActionsKey = key;
                for (int i = _insActions.childCount - 1; i >= 0; i--) Destroy(_insActions.GetChild(i).gameObject);
                foreach (var a in actions)
                {
                    string cmd = a.Value;
                    var b = UIKit.Button(_insActions, a.Key, () => RunAction(cmd), 15, UIKit.AccentSoft, UIKit.Accent, 0f, 38f);
                }
            }
        }

        private void RunAction(string cmd)
        {
            if (cmd == "@focus")
            {
                var e = _insEntity;
                Vector3 p = Vector3.zero;
                if (e.Kind == EntityKind.Resident) p = _gc.Agents.PeoplePos[e.Id];
                else if (e.Kind == EntityKind.Vehicle) p = _gc.Agents.CarPos[e.Id];
                _gc.Rig.FocusOn(p, 60f);
                return;
            }
            _gc.Execute(cmd);
        }

        private List<KeyValuePair<string, string>> ActionsFor(EntityRef e)
        {
            var list = new List<KeyValuePair<string, string>>();
            var sim = _gc.Sim;
            var w = sim.World;
            void Add(string label, string cmd) => list.Add(new KeyValuePair<string, string>(label, cmd));
            void Fire(int building)
            {
                if (w.FireActive && w.FireBuilding == building) Add("Потушить пожар", "потуши пожар");
                else if (!w.FireActive) Add("Поджечь", "подожги это");
            }
            switch (e.Kind)
            {
                case EntityKind.Resident:
                case EntityKind.Vehicle:
                    Add("Показать крупно", "@focus");
                    break;
                case EntityKind.Building:
                    Fire(e.Id);
                    break;
                case EntityKind.Place:
                    var p = sim.City.Places[e.Id];
                    switch (p.Key)
                    {
                        case "north_bridge":
                            Add(w.NorthClosed ? "Открыть мост" : "Закрыть мост", w.NorthClosed ? "открой северный мост" : "закрой северный мост");
                            break;
                        case "south_bridge":
                            Add(w.SouthClosed ? "Открыть мост" : "Закрыть мост", w.SouthClosed ? "открой южный мост" : "закрой южный мост");
                            break;
                        case "central_park":
                            Add(w.Festival ? "Закончить фестиваль" : "Начать фестиваль", w.Festival ? "закончи фестиваль" : "начни фестиваль");
                            break;
                        case "riverside_parking":
                            Add(w.LotIsPark ? "Вернуть парковку" : "Превратить в парк", w.LotIsPark ? "верни парковку" : "преврати парковку в парк");
                            Add("Поднять реку на 50 см", "подними реку на 50 см");
                            break;
                        case "river":
                            Add("Поднять на 50 см", "подними реку на 50 см");
                            Add("Поднять на 1 м", "подними реку на 1 метр");
                            if (Mathf.Abs(w.RiverTarget) > 0.01f) Add("Вернуть в норму", "верни реку в норму");
                            break;
                        case "bank":
                            if (!w.RobberyActive) Add("Ограбить банк", "ограбь банк");
                            Fire(p.Building);
                            break;
                        default:
                            if (p.Building >= 0) Fire(p.Building);
                            break;
                    }
                    break;
            }
            return list;
        }

        // ------------------------------------------------------------------ settings, help, cinema

        public void ToggleSettings()
        {
            bool on = !_settings.gameObject.activeSelf;
            _settings.gameObject.SetActive(on);
            if (on)
            {
                _inspector.gameObject.SetActive(false);
                _riverDebounce = -1f;
                _timeDebounce = -1f;
                SyncSliders();
            }
            else if (!_insEntity.IsNone) _inspector.gameObject.SetActive(true);
        }

        private void SyncSliders()
        {
            var sim = _gc.Sim;
            _riverSlider.SetValueWithoutNotify(sim.World.RiverTarget);
            _timeSlider.SetValueWithoutNotify(sim.Hour);
            UpdateSliderLabels();
        }

        private void UpdateSliderLabels()
        {
            float r = _riverSlider.value;
            _riverLabel.text = "Уровень реки: " + (Mathf.Abs(r) < 0.01f ? "норма" : RuText.SignedMetres(r));
            _timeLabel.text = "Время суток: " + MathUtil.FormatHour(_timeSlider.value);
        }

        private void UpdateSettings(float dt)
        {
            if (!_settings.gameObject.activeSelf) return;
            bool dragging = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (_riverDebounce > 0f && !dragging)
            {
                _riverDebounce -= dt;
                if (_riverDebounce <= 0f)
                {
                    _riverDebounce = -1f;
                    float v = Mathf.Round(_riverSlider.value * 10f) / 10f;
                    _gc.ExecuteWorld(new WorldCommand(CommandKind.RiverSet, 0, v));
                }
            }
            if (_timeDebounce > 0f && !dragging)
            {
                _timeDebounce -= dt;
                if (_timeDebounce <= 0f)
                {
                    _timeDebounce = -1f;
                    float v = Mathf.Round(_timeSlider.value * 4f) / 4f;
                    _gc.ExecuteWorld(new WorldCommand(CommandKind.SetTime, 0, v));
                }
            }
            if (_riverDebounce < 0f && _timeDebounce < 0f && !dragging) SyncSliders();
            var w = _gc.Sim.World;
            for (int i = 0; i < 4; i++)
            {
                _weatherBtns[i].GetComponent<Image>().color = (int)w.Weather == i ? UIKit.AccentSoft : UIKit.Chip;
                _seasonBtns[i].GetComponent<Image>().color = (int)w.Season == i ? UIKit.AccentSoft : UIKit.Chip;
            }
        }

        public void ToggleHelp()
        {
            _help.gameObject.SetActive(!_help.gameObject.activeSelf);
            _help.SetAsLastSibling();
        }

        private void ToggleSound()
        {
            _audio.Enabled = !_audio.Enabled;
            UIKit.SetButtonText(_soundBtn, _audio.Enabled ? "Звук: вкл" : "Звук: выкл");
        }

        /// <summary>Closes the topmost panel. Returns false if nothing was open.</summary>
        public bool CloseTopPanel()
        {
            if (_help.gameObject.activeSelf) { _help.gameObject.SetActive(false); return true; }
            if (_settings.gameObject.activeSelf) { ToggleSettings(); return true; }
            if (_card.gameObject.activeSelf) { HideCard(); return true; }
            return false;
        }

        public void SetCinema(bool on)
        {
            _group.alpha = on ? 0f : 1f;
            _group.blocksRaycasts = !on;
            _group.interactable = !on;
            _cinemaHint.gameObject.SetActive(on);
            _cinemaHintTimer = on ? 4f : 0f;
            if (on) _input.DeactivateInputField();
        }
    }
}
