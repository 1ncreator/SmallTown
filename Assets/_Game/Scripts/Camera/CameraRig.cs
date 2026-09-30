using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SmallTown.CameraControl
{
    /// <summary>
    /// Isometric-style diorama camera: drag to pan, wheel/pinch to zoom, right-drag for a limited
    /// rotation, arrows and +/- on the keyboard, 0 to reset. Everything is smoothed and clamped
    /// so the diorama never leaves the screen. Cinema mode slowly orbits the town.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private float defaultYaw = 35f;
        [SerializeField] private float defaultPitch = 48f;
        [SerializeField] private float defaultDistance = 405f;
        [SerializeField] private float minDistance = 35f;
        [SerializeField] private float maxDistance = 540f;
        [SerializeField] private float yawRange = 55f;
        [SerializeField] private float minPitch = 28f;
        [SerializeField] private float maxPitch = 72f;
        [SerializeField] private float smoothing = 9f;

        private Camera _cam;
        private Rect _bounds;
        private Vector3 _focus, _focusT;
        private float _yaw, _yawT, _pitch, _pitchT, _dist, _distT;
        private bool _dragging, _rotating, _dragMoved;
        private Vector3 _dragWorld;
        private Vector2 _pressPos;
        private float _lastPinch = -1f;
        private float _cinemaTime;

        public bool CinemaMode { get; set; }
        public bool InputEnabled { get; set; } = true;
        public Vector3 Focus => _focus;
        public float Distance => _dist;

        /// <summary>Raised with the screen position when the user clicks (press + release without dragging).</summary>
        public event System.Action<Vector2> Clicked;

        public void Init(Camera cam, Rect worldBounds)
        {
            _cam = cam;
            _bounds = worldBounds;
            ResetView(true);
        }

        public void ResetView(bool instant = false)
        {
            _focusT = new Vector3(_bounds.center.x, 2f, _bounds.center.y - 6f);
            _yawT = defaultYaw;
            _pitchT = defaultPitch;
            _distT = defaultDistance;
            if (instant) Snap();
        }

        public void Snap()
        {
            _focus = _focusT;
            _yaw = _yawT;
            _pitch = _pitchT;
            _dist = _distT;
            Apply();
        }

        public void FocusOn(Vector3 point, float distance, bool instant = false)
        {
            _focusT = new Vector3(point.x, 2f, point.z);
            _distT = Mathf.Clamp(distance, minDistance, maxDistance);
            ClampFocus();
            if (instant) Snap();
        }

        public void SetAngles(float yawOffset, float pitch)
        {
            _yawT = defaultYaw + Mathf.Clamp(yawOffset, -yawRange, yawRange);
            _pitchT = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (CinemaMode) UpdateCinema(dt);
            else if (InputEnabled) HandleInput(dt);
            float k = 1f - Mathf.Exp(-smoothing * dt);
            _focus = Vector3.Lerp(_focus, _focusT, k);
            _yaw = Mathf.Lerp(_yaw, _yawT, k);
            _pitch = Mathf.Lerp(_pitch, _pitchT, k);
            _dist = Mathf.Lerp(_dist, _distT, k);
            Apply();
        }

        private void Apply()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.transform.position = _focus - rot * Vector3.forward * _dist;
            _cam.transform.rotation = rot;
            _cam.nearClipPlane = Mathf.Max(0.3f, _dist * 0.02f);
            _cam.farClipPlane = _dist + 900f;
        }

        private void UpdateCinema(float dt)
        {
            _cinemaTime += dt;
            _yawT = defaultYaw + Mathf.Sin(_cinemaTime * 0.06f) * yawRange * 0.95f;
            _pitchT = 38f + Mathf.Sin(_cinemaTime * 0.11f) * 8f;
            _distT = 190f + Mathf.Sin(_cinemaTime * 0.08f) * 60f;
            _focusT = new Vector3(_bounds.center.x + Mathf.Sin(_cinemaTime * 0.05f) * _bounds.width * 0.25f, 2f,
                _bounds.center.y + Mathf.Cos(_cinemaTime * 0.07f) * _bounds.height * 0.2f);
        }

        private bool PointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void HandleInput(float dt)
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            var touch = Touchscreen.current;

            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && !PointerOverUi())
                {
                    _dragging = true;
                    _dragMoved = false;
                    _pressPos = mp;
                    _dragWorld = GroundPoint(mp);
                }
                if (_dragging && mouse.leftButton.isPressed)
                {
                    if ((mp - _pressPos).sqrMagnitude > 36f) _dragMoved = true;
                    if (_dragMoved)
                    {
                        var now = GroundPoint(mp);
                        var delta = _dragWorld - now;
                        _focusT += new Vector3(delta.x, 0f, delta.z);
                        _focus += new Vector3(delta.x, 0f, delta.z);
                        ClampFocus();
                        Apply();
                        _dragWorld = GroundPoint(mp);
                    }
                }
                if (_dragging && mouse.leftButton.wasReleasedThisFrame)
                {
                    _dragging = false;
                    if (!_dragMoved) Clicked?.Invoke(mp);
                }
                if (mouse.rightButton.wasPressedThisFrame && !PointerOverUi()) _rotating = true;
                if (mouse.rightButton.wasReleasedThisFrame) _rotating = false;
                if (_rotating)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _yawT = Mathf.Clamp(_yawT + d.x * 0.18f, defaultYaw - yawRange, defaultYaw + yawRange);
                    _pitchT = Mathf.Clamp(_pitchT - d.y * 0.12f, minPitch, maxPitch);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi())
                {
                    float step = Mathf.Sign(scroll) * Mathf.Min(1f, Mathf.Abs(scroll) / 120f + 0.4f);
                    _distT = Mathf.Clamp(_distT * (1f - step * 0.12f), minDistance, maxDistance);
                }
            }

            if (touch != null)
            {
                int active = 0;
                Vector2 p0 = Vector2.zero, p1 = Vector2.zero;
                foreach (var t in touch.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (active == 0) p0 = t.position.ReadValue();
                    else if (active == 1) p1 = t.position.ReadValue();
                    active++;
                }
                if (active >= 2)
                {
                    float d = Vector2.Distance(p0, p1);
                    if (_lastPinch > 0f) _distT = Mathf.Clamp(_distT * (_lastPinch / Mathf.Max(1f, d)), minDistance, maxDistance);
                    _lastPinch = d;
                    _dragging = false;
                }
                else _lastPinch = -1f;
            }

            if (kb != null && !TextInputFocused)
            {
                Vector3 move = Vector3.zero;
                if (kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.upArrowKey.isPressed) move.z += 1f;
                if (kb.downArrowKey.isPressed) move.z -= 1f;
                if (move.sqrMagnitude > 0f)
                {
                    var rot = Quaternion.Euler(0f, _yaw, 0f);
                    _focusT += rot * move.normalized * (_dist * 0.9f * dt);
                    ClampFocus();
                }
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) _distT = Mathf.Clamp(_distT * (1f - dt * 1.4f), minDistance, maxDistance);
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) _distT = Mathf.Clamp(_distT * (1f + dt * 1.4f), minDistance, maxDistance);
                if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) ResetView();
            }
        }

        /// <summary>Set by the UI while the command line has focus so typing does not move the camera.</summary>
        public bool TextInputFocused { get; set; }

        private Vector3 GroundPoint(Vector2 screen)
        {
            var ray = _cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, 2f, 0f));
            if (plane.Raycast(ray, out float enter)) return ray.GetPoint(enter);
            return _focus;
        }

        private void ClampFocus()
        {
            float margin = 10f;
            _focusT.x = Mathf.Clamp(_focusT.x, _bounds.xMin + margin, _bounds.xMax - margin);
            _focusT.z = Mathf.Clamp(_focusT.z, _bounds.yMin + margin, _bounds.yMax - margin);
        }
    }
}
