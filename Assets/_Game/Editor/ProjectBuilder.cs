using System;
using System.IO;
using SmallTown.Core;
using SmallTown.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SmallTown.Editor
{
    /// <summary>
    /// Generates everything the project needs from code: URP pipeline + renderer (soft shadows, SSAO,
    /// MSAA 4x), post-processing profile, materials, meshes, prefabs, configs, the scene and build settings.
    /// Idempotent: running it again updates existing assets in place.
    /// </summary>
    public static class ProjectBuilder
    {
        private const string Root = "Assets/_Game";
        private const string SettingsDir = Root + "/Settings";
        private const string MaterialsDir = Root + "/Materials";
        private const string ConfigsDir = Root + "/Configs";
        private const string ScenesDir = Root + "/Scenes";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string MeshesDir = Root + "/Generated/Meshes";
        private const string ResourcesDir = Root + "/Resources";
        public const string ScenePath = ScenesDir + "/SmallTown.unity";

        [MenuItem("Tools/Small Town/Build Everything")]
        public static void BuildEverythingMenu() => Run(false);

        /// <summary>Entry point for -executeMethod SmallTown.Editor.ProjectBuilder.BuildEverything.</summary>
        public static void BuildEverything() => Run(Application.isBatchMode);

        private static void Run(bool exitWhenDone)
        {
            try
            {
                Debug.Log("[ST] Build Everything: start");
                EnsureFolders();
                ConfigurePlayer();
                var pipeline = SetupUrp();
                var profile = SetupPostProfile();
                var materials = SetupMaterials();
                var settings = SetupSimSettings();
                var grammar = AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigsDir + "/CommandGrammar.json");
                if (grammar == null) throw new Exception("Missing " + ConfigsDir + "/CommandGrammar.json");
                var gameAssets = SetupGameAssets(materials, settings, grammar, profile);
                SetupMeshesAndPrefabs(materials, gameAssets);
                SetupScene(gameAssets, profile);
                SetupBuildSettings();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[ST] Build Everything: OK (pipeline " + pipeline.name + ")");
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[ST] Build Everything FAILED: " + e);
                if (exitWhenDone) EditorApplication.Exit(1);
            }
        }

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { SettingsDir, MaterialsDir, ConfigsDir, ScenesDir, PrefabsDir, MeshesDir, ResourcesDir })
            {
                if (AssetDatabase.IsValidFolder(dir)) continue;
                string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                string leaf = Path.GetFileName(dir);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    string gp = Path.GetDirectoryName(parent).Replace('\\', '/');
                    AssetDatabase.CreateFolder(gp, Path.GetFileName(parent));
                }
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        // ------------------------------------------------------------------ player & input

        private static void ConfigurePlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "SmallTown";
            PlayerSettings.productName = "Small Town";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            // Input System package (new). "Both" (2) keeps legacy uGUI InputField IME support safe;
            // all game code uses UnityEngine.InputSystem only.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 2)
                {
                    p.intValue = 2;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // ------------------------------------------------------------------ URP

        private static UniversalRenderPipelineAsset SetupUrp()
        {
            string rendererPath = SettingsDir + "/SmallTown_Renderer.asset";
            string pipelinePath = SettingsDir + "/SmallTown_URP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            if (renderer.postProcessData == null)
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            renderer.renderingMode = RenderingMode.Forward;

            // SSAO renderer feature
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (var f in renderer.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion s) ssao = s;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
            }
            var sso = new SerializedObject(ssao);
            SetF(sso, "m_Settings.Intensity", 1.1f);
            SetF(sso, "m_Settings.Radius", 0.55f);
            SetF(sso, "m_Settings.DirectLightingStrength", 0.3f);
            SetF(sso, "m_Settings.Falloff", 120f);
            sso.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            // keep the feature map in sync (local file ids)
            var rso = new SerializedObject(renderer);
            var map = rso.FindProperty("m_RendererFeatureMap");
            map.arraySize = renderer.rendererFeatures.Count;
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long localId);
                map.GetArrayElementAtIndex(i).longValue = localId;
            }
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = true;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 650f;
            pipeline.shadowCascadeCount = 2;
            pipeline.cascade2Split = 0.3f;
            pipeline.mainLightShadowmapResolution = 4096;
            pipeline.shadowDepthBias = 1.2f;
            pipeline.shadowNormalBias = 1.0f;
            var pso = new SerializedObject(pipeline);
            SetB(pso, "m_MainLightShadowsSupported", true);
            SetB(pso, "m_SoftShadowsSupported", true);
            SetI(pso, "m_SoftShadowQuality", 3);
            SetI(pso, "m_ShadowType", 2);
            SetB(pso, "m_SupportsHDR", true);
            SetI(pso, "m_ColorGradingMode", 1);
            SetB(pso, "m_RequireDepthTexture", false);
            SetB(pso, "m_UseSRPBatcher", true);
            SetI(pso, "m_AdditionalLightsRenderingMode", 1);
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(Mathf.Max(current, QualitySettings.names.Length - 1), false);
            return pipeline;
        }

        private static void SetF(SerializedObject so, string path, float v)
        {
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = v;
            else Debug.LogWarning("[ST] property not found: " + path);
        }

        private static void SetB(SerializedObject so, string path, bool v)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = v;
            else Debug.LogWarning("[ST] property not found: " + path);
        }

        private static void SetI(SerializedObject so, string path, int v)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogWarning("[ST] property not found: " + path); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v;
            else p.intValue = v;
        }

        private static VolumeProfile SetupPostProfile()
        {
            string path = SettingsDir + "/SmallTown_PostProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (profile.TryGet<T>(out var c)) return c;
                c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.65f);
            bloom.scatter.Override(0.65f);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.45f);
            var ca = Get<ColorAdjustments>();
            ca.contrast.Override(14f);
            ca.saturation.Override(16f);
            foreach (var c in profile.components) EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------ materials & data

        private static MaterialSet SetupMaterials()
        {
            var fresh = MaterialSet.Create();
            var set = new MaterialSet
            {
                City = SaveMat(fresh.City, "M_City"),
                Grass = SaveMat(fresh.Grass, "M_Grass"),
                Foliage = SaveMat(fresh.Foliage, "M_Foliage"),
                Conifer = SaveMat(fresh.Conifer, "M_Conifer"),
                Agents = SaveMat(fresh.Agents, "M_Agents"),
                Water = SaveMat(fresh.Water, "M_Water"),
                Glow = SaveMat(fresh.Glow, "M_Glow"),
                GlowAlways = SaveMat(fresh.GlowAlways, "M_GlowAlways"),
                ParticleAlpha = SaveMat(fresh.ParticleAlpha, "M_ParticleAlpha"),
                ParticleAdditive = SaveMat(fresh.ParticleAdditive, "M_ParticleAdditive")
            };
            return set;
        }

        private static Material SaveMat(Material fresh, string name)
        {
            if (fresh.shader == null || fresh.shader.name == "Hidden/InternalErrorShader")
                throw new Exception("Shader missing for material " + name);
            string path = MaterialsDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                fresh.name = name;
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            existing.shader = fresh.shader;
            existing.CopyPropertiesFromMaterial(fresh);
            existing.renderQueue = fresh.renderQueue;
            existing.enableInstancing = fresh.enableInstancing;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static SimSettingsAsset SetupSimSettings()
        {
            string path = ConfigsDir + "/SimSettings.asset";
            var s = AssetDatabase.LoadAssetAtPath<SimSettingsAsset>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<SimSettingsAsset>();
                AssetDatabase.CreateAsset(s, path);
            }
            return s;
        }

        private static GameAssets SetupGameAssets(MaterialSet m, SimSettingsAsset settings, TextAsset grammar, VolumeProfile profile)
        {
            string path = ResourcesDir + "/" + GameAssets.ResourcePath + ".asset";
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>(path);
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, path);
            }
            var so = new SerializedObject(ga);
            so.FindProperty("city").objectReferenceValue = m.City;
            so.FindProperty("grass").objectReferenceValue = m.Grass;
            so.FindProperty("foliage").objectReferenceValue = m.Foliage;
            so.FindProperty("conifer").objectReferenceValue = m.Conifer;
            so.FindProperty("agents").objectReferenceValue = m.Agents;
            so.FindProperty("water").objectReferenceValue = m.Water;
            so.FindProperty("glow").objectReferenceValue = m.Glow;
            so.FindProperty("glowAlways").objectReferenceValue = m.GlowAlways;
            so.FindProperty("particleAlpha").objectReferenceValue = m.ParticleAlpha;
            so.FindProperty("particleAdditive").objectReferenceValue = m.ParticleAdditive;
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("grammar").objectReferenceValue = grammar;
            so.FindProperty("postProfile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ga);
            return ga;
        }

        // ------------------------------------------------------------------ meshes, prefabs, scene

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MeshesDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void SetupMeshesAndPrefabs(MaterialSet m, GameAssets ga)
        {
            var person = SaveMesh(ProceduralMeshes.Person(), "Person");
            var car = SaveMesh(ProceduralMeshes.Car(false), "Car");
            var police = SaveMesh(ProceduralMeshes.Car(true), "PoliceCar");
            var fire = SaveMesh(ProceduralMeshes.FireTruck(), "FireTruck");
            var umbrella = SaveMesh(ProceduralMeshes.Umbrella(), "Umbrella");
            PreviewPrefab("Person", person, m.Agents);
            PreviewPrefab("Car", car, m.Agents);
            PreviewPrefab("PoliceCar", police, m.Agents);
            PreviewPrefab("FireTruck", fire, m.Agents);
            PreviewPrefab("Umbrella", umbrella, m.Agents);

            var root = new GameObject("GameRoot");
            var gc = root.AddComponent<GameController>();
            var so = new SerializedObject(gc);
            so.FindProperty("assets").objectReferenceValue = ga;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabsDir + "/GameRoot.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void PreviewPrefab(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabsDir + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void SetupScene(GameAssets ga, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.92f, 0.94f, 0.95f);
            cam.fieldOfView = 30f;
            cam.transform.position = new Vector3(-160f, 240f, -230f);
            cam.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sunGo.transform.rotation = Quaternion.Euler(50f, 210f, 0f);
            sunGo.AddComponent<UniversalAdditionalLightData>();

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/GameRoot.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var gc = root.GetComponent<GameController>();
            var so = new SerializedObject(gc);
            so.FindProperty("assets").objectReferenceValue = ga;
            so.FindProperty("mainCamera").objectReferenceValue = cam;
            so.FindProperty("sun").objectReferenceValue = sun;
            so.FindProperty("volume").objectReferenceValue = vol;
            so.ApplyModifiedPropertiesWithoutUndo();

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 420f;
            RenderSettings.fogEndDistance = 1100f;
            RenderSettings.fogColor = cam.backgroundColor;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void SetupBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
