using System.Collections.Generic;
using UnityEngine;

namespace SmallTown.Core
{
    /// <summary>
    /// Scripted tour: runs a sequence of real text commands with camera moves, then restores
    /// the world exactly as it was before the tour.
    /// </summary>
    public sealed class Showcase
    {
        private struct Step
        {
            public string Command;
            public string Place;
            public float Distance;
            public float Wait;
            public string Caption;
            public float Yaw;
            public float Pitch;
        }

        private readonly GameController _gc;
        private readonly List<Step> _steps = new List<Step>();
        private int _index = -1;
        private float _timer;
        private byte[] _startSnapshot;
        private int _startUndo;
        private int _prevSpeed;

        public bool Active { get; private set; }
        public int Index => _index;
        public int Count => _steps.Count;
        public string Caption => Active && _index >= 0 && _index < _steps.Count ? _steps[_index].Caption : "";

        public Showcase(GameController gc)
        {
            _gc = gc;
            Add("сделай утро", null, 400f, 4.5f, "Утро: город просыпается, люди идут на работу и в школу", 0f, 48f);
            Add("закрой северный мост", "north_bridge", 120f, 7f, "Закрываем Северный мост — машины ищут объезд", -25f, 45f);
            Add("пусть пойдёт дождь", null, 250f, 6.5f, "Дождь: люди прячутся под крыши и зонты, машины медленнее", 20f, 44f);
            Add("подними реку на 1 метр", "riverside_parking", 115f, 7.5f, "Река поднимается: затоплены парковка и набережные", 30f, 42f);
            Add("верни реку в норму и открой северный мост", "river", 230f, 5.5f, "Вода уходит, мост снова открыт", 0f, 50f);
            Add("пусть будет ясно и начни фестиваль", "central_park", 105f, 8f, "Фестиваль в Центральном парке — собирается толпа", -15f, 40f);
            Add("сделай закат", null, 360f, 5.5f, "Закат: длинные тени и тёплый свет", 35f, 36f);
            Add("подожги школу", "school", 120f, 11f, "Пожар в школе: эвакуация и пожарная машина", 10f, 44f);
            Add("ограбь банк", "bank", 120f, 11f, "Ограбление банка: полиция мчится с мигалками", -20f, 44f);
            Add("сделай ночь", null, 380f, 6.5f, "Ночь: светятся окна и фонари, почти все дома", 0f, 50f);
            Add("наступает зима и пусть пойдёт снег", null, 330f, 7.5f, "Зима: снег на крышах, голые деревья", -30f, 46f);
            Add("преврати парковку в парк", "riverside_parking", 110f, 6.5f, "Парковка у реки превращается в парк", 20f, 44f);
        }

        private void Add(string cmd, string place, float dist, float wait, string caption, float yaw, float pitch)
        {
            _steps.Add(new Step { Command = cmd, Place = place, Distance = dist, Wait = wait, Caption = caption, Yaw = yaw, Pitch = pitch });
        }

        public void Start()
        {
            if (Active) return;
            if (_gc.Cinema) _gc.SetCinema(false);
            _startSnapshot = _gc.Sim.SaveSnapshot();
            _startUndo = _gc.Session.UndoDepth;
            _prevSpeed = _gc.Speed;
            _gc.SetSpeed(2);
            _gc.Select(Simulation.Events.EntityRef.None);
            Active = true;
            _index = -1;
            _timer = 0f;
        }

        public void Update(float dt)
        {
            if (!Active) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _index++;
            if (_index >= _steps.Count)
            {
                Finish(true);
                return;
            }
            var s = _steps[_index];
            var city = _gc.Sim.City;
            var place = s.Place != null ? city.PlaceByKey(s.Place) : null;
            var target = place != null ? new Vector3(place.X, 0f, place.Z) : new Vector3((city.MinX + city.MaxX) * 0.5f, 0f, (city.MinZ + city.MaxZ) * 0.5f - 6f);
            _gc.Rig.FocusOn(target, s.Distance);
            _gc.Rig.SetAngles(s.Yaw, s.Pitch);
            _gc.Execute(s.Command);
            _timer = s.Wait;
        }

        public void Stop() => Finish(false);

        private void Finish(bool completed)
        {
            if (!Active) return;
            Active = false;
            _gc.Sim.LoadSnapshot(_startSnapshot);
            _gc.Session.TrimUndo(_startUndo);
            _gc.SetSpeed(_prevSpeed);
            _gc.Rig.ResetView();
            _gc.Environment.UpdateView(_gc.Sim, 0.016f, _gc.Rig.Focus, true);
            _gc.Hud?.Toast(completed ? "Показ завершён — мир вернулся в исходное состояние" : "Показ остановлен — мир восстановлен");
        }
    }
}
