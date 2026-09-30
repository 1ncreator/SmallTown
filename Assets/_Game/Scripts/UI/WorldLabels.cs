using System.Collections.Generic;
using SmallTown.Core;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;
using SmallTown.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace SmallTown.UI
{
    /// <summary>Clickable name tags floating over key places, with live status badges.</summary>
    public sealed class WorldLabels : MonoBehaviour
    {
        private sealed class Tag
        {
            public int Place;
            public RectTransform Rect;
            public Text Name;
            public RectTransform Badge;
            public Text BadgeText;
            public Image BadgeImage;
            public CanvasGroup Group;
        }

        private GameController _gc;
        private RectTransform _layer;
        private readonly List<Tag> _tags = new List<Tag>();

        public void Build(GameController gc, RectTransform layer)
        {
            _gc = gc;
            _layer = layer;
            var c = gc.Sim.City;
            foreach (var p in c.Places)
            {
                if (!p.ShowLabel) continue;
                if (p.Kind == PlaceKind.Building && p.Key != "school" && p.Key != "bank" && p.Key != "fire_station" && p.Key != "police" && p.Key != "church" && p.Key != "lighthouse" && p.Key != "water_tower") continue;
                _tags.Add(MakeTag(p));
            }
        }

        private Tag MakeTag(Place p)
        {
            var t = new Tag { Place = p.Id };
            var rt = UIKit.Rect("Label_" + p.Key, _layer);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIKit.Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 3f;
            img.color = new Color(1f, 1f, 1f, 0.9f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            int id = p.Id;
            btn.onClick.AddListener(() => _gc.Select(new EntityRef(EntityKind.Place, id)));
            UIKit.HLayout(rt.gameObject, 6f, new RectOffset(12, 12, 4, 4), TextAnchor.MiddleCenter);
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            t.Name = UIKit.Label(rt, p.NameRu, 14, UIKit.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(t.Name, -1f, 20f);
            var badge = UIKit.Rect("Badge", rt);
            t.BadgeImage = badge.gameObject.AddComponent<Image>();
            t.BadgeImage.sprite = UIKit.Rounded;
            t.BadgeImage.type = Image.Type.Sliced;
            t.BadgeImage.pixelsPerUnitMultiplier = 4f;
            t.BadgeImage.color = UIKit.Danger;
            t.BadgeImage.raycastTarget = false;
            UIKit.HLayout(badge.gameObject, 0f, new RectOffset(8, 8, 1, 1), TextAnchor.MiddleCenter);
            t.BadgeText = UIKit.Label(badge, "", 12, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.BadgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Size(t.BadgeText, -1f, 18f);
            t.Badge = badge;
            badge.gameObject.SetActive(false);
            t.Group = rt.gameObject.AddComponent<CanvasGroup>();
            t.Rect = rt;
            return t;
        }

        private void LateUpdate()
        {
            if (_gc == null) return;
            var cam = _gc.MainCamera;
            var sim = _gc.Sim;
            var c = sim.City;
            var w = sim.World;
            float scale = _layer.rect.width / Mathf.Max(1f, cam.pixelWidth);
            int sw = cam.pixelWidth, sh = cam.pixelHeight;
            float dist = _gc.Rig.Distance;
            foreach (var t in _tags)
            {
                var p = c.Places[t.Place];
                var world = new Vector3(p.X, p.Y + (p.Kind == PlaceKind.River ? w.RiverLevel : 0f), p.Z);
                var sp = cam.WorldToScreenPoint(world);
                bool visible = sp.z > 0f && sp.x > -100f && sp.x < sw + 100f && sp.y > -60f && sp.y < sh + 60f;
                if (t.Rect.gameObject.activeSelf != visible) t.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                t.Rect.anchoredPosition = new Vector2(sp.x * scale, sp.y * scale + 6f);
                bool minor = p.Kind == PlaceKind.Building && p.Key != "school" && p.Key != "bank";
                t.Group.alpha = minor ? Mathf.Clamp01((380f - dist) / 140f) : 1f;
                t.Group.blocksRaycasts = t.Group.alpha > 0.3f;

                string name = p.NameRu;
                if (p.Kind == PlaceKind.Parking && w.LotIsPark) name = "Парк у реки";
                if (t.Name.text != name) t.Name.text = name;
                string badge = null;
                Color bc = UIKit.Danger;
                switch (p.Kind)
                {
                    case PlaceKind.Bridge:
                        if (w.BridgesFlooded) badge = "затоплен";
                        else if (w.BridgeClosed(p.Bridge)) badge = "закрыт";
                        break;
                    case PlaceKind.Parking:
                        if (sim.Traffic.LotFlooded) badge = "затоплено";
                        break;
                    case PlaceKind.Park:
                        if (w.Festival) { badge = "фестиваль"; bc = new Color(0.55f, 0.4f, 0.9f); }
                        break;
                    case PlaceKind.River:
                        if (Mathf.Abs(w.RiverTarget) > 0.01f) { badge = RuText.SignedMetres(w.RiverTarget); bc = UIKit.Accent; }
                        break;
                    default:
                        if (p.Building >= 0 && w.FireActive && w.FireBuilding == p.Building) badge = "пожар!";
                        else if (p.Building >= 0 && w.RobberyActive && w.RobberyBuilding == p.Building) badge = "ограбление!";
                        break;
                }
                bool hasBadge = badge != null;
                if (t.Badge.gameObject.activeSelf != hasBadge) t.Badge.gameObject.SetActive(hasBadge);
                if (hasBadge)
                {
                    if (t.BadgeText.text != badge) t.BadgeText.text = badge;
                    t.BadgeImage.color = bc;
                }
            }
        }
    }
}
