using System.Collections;
using System.IO;
using NUnit.Framework;
using SmallTown.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SmallTown.Tests.PlayMode
{
    /// <summary>
    /// Renders the main camera into a RenderTexture for a set of world states and writes PNGs to
    /// &lt;project&gt;/Screenshots. Explicit: run with -testFilter SmallTown.Tests.PlayMode.ScreenshotCapture.
    /// </summary>
    [Explicit]
    public sealed class ScreenshotCapture
    {
        private const int W = 1920, H = 1080;

        [UnityTest]
        public IEnumerator CaptureAll()
        {
            SceneManager.LoadScene("SmallTown", LoadSceneMode.Single);
            for (int i = 0; i < 5; i++) yield return null;
            var gc = GameController.Instance;
            Assert.NotNull(gc);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            gc.SetPaused(true);
            gc.AdvanceTicks(900);

            yield return Shot(gc, dir, "01_day_overview", "сделай день", null, 405f);
            yield return Shot(gc, dir, "02_day_closeup", null, "market_square", 120f);
            yield return Shot(gc, dir, "03_night", "сделай ночь", null, 405f);
            yield return Shot(gc, dir, "04_rain", "сделай день и пусть пойдёт дождь", null, 260f);
            yield return Shot(gc, dir, "05_snow_winter", "наступает зима и пусть пойдёт снег", null, 280f);
            yield return Shot(gc, dir, "06_fire_school", "наступает лето и пусть будет ясно и подожги школу", "school", 120f, 400);
            yield return Shot(gc, dir, "07_festival", "начни фестиваль", "central_park", 110f, 900);
            yield return Shot(gc, dir, "08_flood", "подними реку на 1 метр и закрой северный мост", "riverside_parking", 130f, 200);
            yield return Shot(gc, dir, "09_sunset", "сделай закат", null, 380f);
            yield return Shot(gc, dir, "10_ui", null, null, 405f, 0, true);
        }

        private static IEnumerator Shot(GameController gc, string dir, string name, string command, string place, float dist, int ticks = 60, bool withUi = false)
        {
            if (command != null) gc.Execute(command);
            var city = gc.Sim.City;
            var p = place != null ? city.PlaceByKey(place) : null;
            var target = p != null ? new Vector3(p.X, 0f, p.Z) : new Vector3((city.MinX + city.MaxX) * 0.5f, 0f, (city.MinZ + city.MaxZ) * 0.5f - 6f);
            gc.Rig.FocusOn(target, dist, true);
            if (ticks > 0) gc.AdvanceTicks(ticks);
            // let particles, smoothing and layout settle in real time
            float t = 0f;
            while (t < 2.5f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            gc.Environment.UpdateView(gc.Sim, 0.5f, gc.Rig.Focus, true);
            gc.Rig.Snap();

            var cam = gc.MainCamera;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            // Warm-up render: in batchmode nothing is drawn to a display, so the first render of a frame
            // is the one that uploads material data; the second one is the real capture.
            for (int pass = 0; pass < 2; pass++)
            {
                gc.DrawAgentsFor(cam);
                Render(cam, rt);
                yield return null;
            }

            cam.targetTexture = prevTarget;
            var tex = ReadBack(rt);

            if (withUi)
            {
                // Overlay UI is not part of camera output: render it with a separate camera into a
                // transparent texture (no post-processing) and composite it over the frame on the CPU.
                var canvas = gc.Hud.Canvas;
                var prevMode = canvas.renderMode;
                SetLayer(canvas.gameObject, 5);
                var uiCamGo = new GameObject("UICaptureCam");
                var uiCam = uiCamGo.AddComponent<Camera>();
                uiCam.clearFlags = CameraClearFlags.SolidColor;
                uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                uiCam.cullingMask = 1 << 5;
                uiCam.orthographic = true;
                var data = uiCamGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                var uiRt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                uiRt.Create();
                uiCam.targetTexture = uiRt;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCam;
                canvas.planeDistance = 10f;
                for (int pass = 0; pass < 2; pass++)
                {
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Render(uiCam, uiRt);
                }
                var ui = ReadBack(uiRt, true);
                var a = tex.GetPixels32();
                var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    float ua = Mathf.Sqrt(b[i].a / 255f);
                    a[i] = new Color32(
                        (byte)Mathf.Clamp(b[i].r + a[i].r * (1f - ua), 0f, 255f),
                        (byte)Mathf.Clamp(b[i].g + a[i].g * (1f - ua), 0f, 255f),
                        (byte)Mathf.Clamp(b[i].b + a[i].b * (1f - ua), 0f, 255f), 255);
                }
                tex.SetPixels32(a);
                tex.Apply();
                canvas.renderMode = prevMode;
                canvas.worldCamera = null;
                Object.Destroy(uiCamGo);
                Object.Destroy(ui);
                uiRt.Release();
            }
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Debug.Log("[ST] screenshot " + name + " — " + gc.Sim.StatusLine());
        }

        private static Texture2D ReadBack(RenderTexture rt, bool alpha = false)
        {
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            return tex;
        }

        private static void Render(Camera cam, RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
        }
    }
}
