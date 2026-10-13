using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FirstPersonTennis.EditorTools
{
    // 1단계 연습 씬(하드코트, 플레이어, 허수아비, 공, 보조 표시, HUD)을 만들고 컴포넌트를 연결한다.
    // 메뉴: FirstPersonTennis > Build Phase 1 Scene. 다시 실행하면 씬을 새로 만든다 (설정 에셋은 유지).
    public static class Phase1SceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Phase1Court.unity";
        const string ConfigPath = Root + "/Settings/TennisConfig.asset";
        const string MaterialDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";

        [MenuItem("FirstPersonTennis/Build Phase 1 Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        // unity run <project> -- -executeMethod FirstPersonTennis.EditorTools.Phase1SceneBuilder.Build
        public static void Build()
        {
            foreach (string folder in new[] { Root, Root + "/Scenes", Root + "/Settings", MaterialDir, MeshDir })
                EnsureFolder(folder);

            // 새 씬을 만들면 쓰이지 않는 에셋이 메모리에서 내려가므로, 에셋은 씬을 만든 뒤에 불러온다.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TennisConfig config = LoadOrCreateConfig();
            var m = new Materials();
            SetupLighting();

            BuildCourt(m);
            TennisBall ball = CreateBall(m, config, out GameObject halo);

            // 플레이어 (근거리, side -1)
            Transform playerRoot = CreateAthlete("Player", new Vector3(0f, 0f, -(Court.HalfLength + 0.8f)), 0f, m.Player, m,
                out Renderer playerBody, out Transform playerPivot);
            var movement = playerRoot.gameObject.AddComponent<PlayerMovement>();
            var playerRacket = playerRoot.gameObject.AddComponent<RacketSwingVisual>();
            var gesture = playerRoot.gameObject.AddComponent<ShotGestureInput>();
            var playerSwing = playerRoot.gameObject.AddComponent<AutoSwing>();
            Set(movement, "config", config);
            SetInt(movement, "side", -1);
            Set(playerRacket, "pivot", playerPivot);
            Set(gesture, "ball", ball);
            Set(gesture, "swing", playerSwing);
            WireSwing(playerSwing, config, ball, -1, playerRacket, gesture, 1f, 11);

            // 허수아비 (원거리, side +1)
            Transform dummyRoot = CreateAthlete("Dummy", new Vector3(0f, 0f, Court.HalfLength + 0.8f), 180f, m.Dummy, m,
                out _, out Transform dummyPivot);
            var dummyRacket = dummyRoot.gameObject.AddComponent<RacketSwingVisual>();
            var dummy = dummyRoot.gameObject.AddComponent<DummyOpponent>();
            var dummySwing = dummyRoot.gameObject.AddComponent<AutoSwing>();
            Set(dummyRacket, "pivot", dummyPivot);
            Set(dummy, "config", config);
            Set(dummy, "ball", ball);
            Set(dummy, "swing", dummySwing);
            WireSwing(dummySwing, config, ball, 1, dummyRacket, dummy, config.dummyErrorScale, 23);

            // 진행
            var systems = new GameObject("Systems");
            var referee = systems.AddComponent<RallyReferee>();
            Set(referee, "config", config);
            Set(referee, "ball", ball);
            Set(referee, "player", movement);
            Set(referee, "dummy", dummy);

            // 카메라 + 소리
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = playerRoot.position + Vector3.up * config.eyeHeight;
            var cameraRig = camGo.AddComponent<FirstPersonBallCamera>();
            Set(cameraRig, "config", config);
            Set(cameraRig, "player", movement);
            Set(cameraRig, "ball", ball);
            SetArray(cameraRig, "hideInFirstPerson", new Object[] { playerBody });
            var audioSource = camGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            var audio = camGo.AddComponent<AudioFeedback>();
            Set(audio, "ball", ball);
            Set(audio, "referee", referee);

            // 거리감 보조
            Transform shadow = CreateRing("BallShadow", 0f, 0.5f, m.Shadow);
            Transform landing = CreateRing("LandingMarker", 0.35f, 0.5f, m.Marker);
            landing.localScale = Vector3.one * 0.7f;
            Transform range = CreateRing("HitRangeRing", 0.95f, 1f, m.Range);
            var assists = systems.AddComponent<AssistVisuals>();
            Set(assists, "config", config);
            Set(assists, "ball", ball);
            Set(assists, "playerSwing", playerSwing);
            Set(assists, "player", playerRoot);
            Set(assists, "ballShadow", shadow);
            Set(assists, "landingMarker", landing);
            Set(assists, "hitRangeRing", range);
            Set(assists, "ballHalo", halo);
            Set(assists, "audioFeedback", audio);

            var hud = systems.AddComponent<TennisHud>();
            Set(hud, "config", config);
            Set(hud, "referee", referee);
            Set(hud, "player", movement);
            Set(hud, "input", gesture);
            Set(hud, "playerSwing", playerSwing);
            Set(hud, "cameraRig", cameraRig);

            ValidateReferences(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FirstPersonTennis] 1단계 씬 생성 완료: {ScenePath}");
        }

        static void WireSwing(AutoSwing swing, TennisConfig config, TennisBall ball, int side,
            RacketSwingVisual racket, MonoBehaviour source, float errorScale, int seed)
        {
            Set(swing, "config", config);
            Set(swing, "ball", ball);
            SetInt(swing, "side", side);
            Set(swing, "racket", racket);
            Set(swing, "intentSource", source);
            SetFloat(swing, "errorScale", errorScale);
            SetInt(swing, "randomSeed", seed);
        }

        // ── 씬 구성 ───────────────────────────────────────────────

        static void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.32f, 0.3f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }

        static void BuildCourt(Materials m)
        {
            var court = new GameObject("Court").transform;

            Transform ground = Primitive(PrimitiveType.Plane, "Ground", court, m.Surround);
            ground.localScale = new Vector3(6f, 1f, 8f);

            Transform surface = Flat("HardCourt", court, m.CourtSurface,
                new Vector3(0f, 0.005f, 0f), new Vector2(Court.DoublesHalfWidth * 2f, Court.HalfLength * 2f));
            surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lines = new GameObject("Lines").transform;
            lines.SetParent(court, false);
            const float Y = 0.01f, W = 0.05f, Baseline = 0.1f;
            float length = Court.HalfLength * 2f;
            foreach (float s in new[] { -1f, 1f })
            {
                Flat("Baseline", lines, m.Line, new Vector3(0f, Y, s * (Court.HalfLength - Baseline / 2f)),
                    new Vector2(Court.DoublesHalfWidth * 2f, Baseline));
                Flat("DoublesSideline", lines, m.Line, new Vector3(s * (Court.DoublesHalfWidth - W / 2f), Y, 0f),
                    new Vector2(W, length));
                Flat("SinglesSideline", lines, m.Line, new Vector3(s * (Court.SinglesHalfWidth - W / 2f), Y, 0f),
                    new Vector2(W, length));
                Flat("ServiceLine", lines, m.Line, new Vector3(0f, Y, s * (Court.ServiceLine - W / 2f)),
                    new Vector2(Court.SinglesHalfWidth * 2f, W));
                Flat("CenterMark", lines, m.Line, new Vector3(0f, Y, s * (Court.HalfLength - 0.15f)),
                    new Vector2(W, 0.1f));
            }
            Flat("CenterServiceLine", lines, m.Line, new Vector3(0f, Y, 0f), new Vector2(W, Court.ServiceLine * 2f));

            // 네트: 가운데 0.914m, 기둥 1.07m
            var net = new GameObject("Net");
            net.transform.SetParent(court, false);
            net.AddComponent<MeshFilter>().sharedMesh = NetMesh();
            var netRenderer = net.AddComponent<MeshRenderer>();
            netRenderer.sharedMaterial = m.Net;
            netRenderer.shadowCastingMode = ShadowCastingMode.Off;

            float slope = Mathf.Atan2(Court.NetPostHeight - Court.NetCenterHeight, Court.NetPostX) * Mathf.Rad2Deg;
            float tapeLength = new Vector2(Court.NetPostX, Court.NetPostHeight - Court.NetCenterHeight).magnitude;
            float tapeY = (Court.NetPostHeight + Court.NetCenterHeight) / 2f - 0.025f;
            foreach (float s in new[] { -1f, 1f })
            {
                Transform tape = Primitive(PrimitiveType.Cube, "NetTape", net.transform, m.Line);
                tape.localPosition = new Vector3(s * Court.NetPostX / 2f, tapeY, 0f);
                tape.localRotation = Quaternion.Euler(0f, 0f, s * slope);
                tape.localScale = new Vector3(tapeLength, 0.05f, 0.03f);

                Transform post = Primitive(PrimitiveType.Cylinder, "NetPost", net.transform, m.Post);
                post.localPosition = new Vector3(s * Court.NetPostX, Court.NetPostHeight / 2f, 0f);
                post.localScale = new Vector3(0.08f, Court.NetPostHeight / 2f, 0.08f);
            }
        }

        static TennisBall CreateBall(Materials m, TennisConfig config, out GameObject halo)
        {
            Transform ball = Primitive(PrimitiveType.Sphere, "Ball", null, m.Ball);
            ball.position = new Vector3(0f, 1f, Court.HalfLength);
            ball.localScale = Vector3.one * (BallPhysics.Radius * 2f);
            ball.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Transform haloT = Primitive(PrimitiveType.Sphere, "Halo", ball, m.Halo);
            haloT.localScale = Vector3.one * 1.9f;
            haloT.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            halo = haloT.gameObject;
            halo.SetActive(false);

            var component = ball.gameObject.AddComponent<TennisBall>();
            Set(component, "config", config);
            return component;
        }

        static Transform CreateAthlete(string name, Vector3 position, float yaw, Material bodyMaterial, Materials m,
            out Renderer body, out Transform racketPivot)
        {
            var root = new GameObject(name).transform;
            root.position = position;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);

            Transform capsule = Primitive(PrimitiveType.Capsule, "Body", root, bodyMaterial);
            capsule.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body = capsule.GetComponent<Renderer>();

            racketPivot = new GameObject("RacketPivot").transform;
            racketPivot.SetParent(root, false);
            racketPivot.localPosition = new Vector3(0.22f, 1.25f, 0.1f);

            Transform handle = Primitive(PrimitiveType.Cylinder, "Handle", racketPivot, m.Racket);
            handle.localPosition = new Vector3(0.15f, 0f, 0f);
            handle.localRotation = Quaternion.Euler(0f, 0f, 90f);
            handle.localScale = new Vector3(0.03f, 0.13f, 0.03f);

            Transform head = Primitive(PrimitiveType.Cylinder, "Head", racketPivot, m.RacketHead);
            head.localPosition = new Vector3(0.44f, 0f, 0f);
            head.localRotation = Quaternion.Euler(90f, 0f, 0f);
            head.localScale = new Vector3(0.32f, 0.006f, 0.26f);

            return root;
        }

        static Transform CreateRing(string name, float inner, float outer, Material material)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var ring = go.AddComponent<ProceduralRing>();
            SetFloat(ring, "innerRadius", inner);
            SetFloat(ring, "outerRadius", outer);
            return go.transform;
        }

        static Mesh NetMesh()
        {
            string path = MeshDir + "/Net.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Net" };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            float x = Court.NetPostX;
            mesh.vertices = new[]
            {
                new Vector3(-x, 0f, 0f), new Vector3(x, 0f, 0f), new Vector3(x, Court.NetPostHeight, 0f),
                new Vector3(0f, Court.NetCenterHeight, 0f), new Vector3(-x, Court.NetPostHeight, 0f),
            };
            mesh.triangles = new[] { 0, 4, 3, 0, 3, 1, 1, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        // 바닥에 눕힌 사각형 (size.x = 너비, size.y = z 방향 길이)
        static Transform Flat(string name, Transform parent, Material material, Vector3 center, Vector2 size)
        {
            Transform t = Primitive(PrimitiveType.Quad, name, parent, material);
            t.localPosition = center;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(size.x, size.y, 1f);
            t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        // ── 에셋 ─────────────────────────────────────────────────

        sealed class Materials
        {
            public readonly Material Surround = Lit("Surround", new Color(0.16f, 0.36f, 0.27f));
            public readonly Material CourtSurface = Lit("HardCourt", new Color(0.13f, 0.32f, 0.58f));
            public readonly Material Line = Lit("Line", new Color(0.95f, 0.95f, 0.95f));
            public readonly Material Post = Lit("NetPost", new Color(0.2f, 0.22f, 0.2f));
            public readonly Material Ball = Lit("Ball", new Color(0.85f, 0.95f, 0.15f), 0.25f);
            public readonly Material Player = Lit("Player", new Color(0.2f, 0.45f, 0.9f));
            public readonly Material Dummy = Lit("Dummy", new Color(0.85f, 0.25f, 0.2f));
            public readonly Material Racket = Lit("Racket", new Color(0.12f, 0.12f, 0.14f));
            public readonly Material RacketHead = Lit("RacketHead", new Color(0.85f, 0.85f, 0.8f));
            public readonly Material Net = Transparent("Net", new Color(0.05f, 0.05f, 0.05f, 0.55f), true);
            public readonly Material Shadow = Transparent("BallShadow", new Color(0f, 0f, 0f, 0.55f));
            public readonly Material Marker = Transparent("LandingMarker", new Color(1f, 0.85f, 0.1f, 0.85f));
            public readonly Material Range = Transparent("HitRange", new Color(1f, 1f, 1f, 0.35f));
            public readonly Material Halo = Transparent("BallHalo", new Color(0.3f, 0.9f, 1f, 0.35f));
        }

        static Material Lit(string name, Color color, float smoothness = 0.1f)
        {
            Material mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Transparent(string name, Color color, bool doubleSided = false)
        {
            Material mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"셰이더를 찾을 수 없음: {shaderName}");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static TennisConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<TennisConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<TennisConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ── 직렬화 필드 연결 ──────────────────────────────────────

        // 우리 컴포넌트의 참조 필드가 하나라도 비어 있으면 실패시킨다.
        static void ValidateReferences(Scene scene)
        {
            var missing = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Namespace != nameof(FirstPersonTennis)) continue;
                var so = new SerializedObject(component);
                SerializedProperty p = so.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script" &&
                        p.objectReferenceValue == null)
                        missing.Add($"{component.name}.{component.GetType().Name}.{p.propertyPath}");
                }
            }
            if (missing.Count > 0)
                throw new InvalidOperationException("연결되지 않은 참조: " + string.Join(", ", missing));
        }

        static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException($"{so.targetObject.GetType().Name}.{field} 필드가 없음");
            return p;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            Find(so, field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            Find(so, field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            Find(so, field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = Find(so, field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
