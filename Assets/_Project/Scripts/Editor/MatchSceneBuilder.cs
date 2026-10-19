using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FirstPersonTennis.EditorTools
{
    // 경기 씬(하드코트, 플레이어, AI 상대, 공, 심판, 보조 표시, HUD)을 만들고 컴포넌트를 연결한다.
    // 메뉴: FirstPersonTennis > Build Match Scene. 다시 실행하면 씬을 새로 만든다 (설정 에셋은 유지).
    public static class MatchSceneBuilder
    {
        const string Root = "Assets/_Project";
        public const string ScenePath = Root + "/Scenes/Match.unity";
        const string ConfigPath = Root + "/Settings/TennisConfig.asset";
        const string MaterialDir = Root + "/Materials";
        const string BallModelPath = Root + "/Art/Props/TennisBall/TennisBall.fbx";
        const string RacketModelPath = Root + "/Art/Props/TennisRacket/TennisRacket.fbx";

        [MenuItem("FirstPersonTennis/Build Match Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        // unity run <project> -- -executeMethod FirstPersonTennis.EditorTools.MatchSceneBuilder.Build
        public static void Build()
        {
            foreach (string folder in new[] { Root, Root + "/Scenes", Root + "/Settings", MaterialDir })
                EnsureFolder(folder);

            // 새 씬을 만들면 쓰이지 않는 에셋이 메모리에서 내려가므로, 에셋은 씬을 만든 뒤에 불러온다.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TennisConfig config = LoadOrCreateConfig();
            var m = new Materials();
            AthleteModel.Configure();
            AnimatorController athleteController = AthleteModel.MatchController();
            SetupLighting();

            // 코트는 Play를 누를 때 설정 크기로 다시 만들어진다. 여기서는 에디터에서 보이도록 한 번 만들어 둔다.
            var court = new GameObject("Court").AddComponent<CourtView>();
            Set(court, "config", config);
            Set(court, "surround", m.Surround);
            Set(court, "courtSurface", m.CourtSurface);
            Set(court, "line", m.Line);
            Set(court, "net", m.Net);
            Set(court, "post", m.Post);
            court.Rebuild();

            TennisBall ball = CreateBall(m, config, out GameObject halo);

            // 플레이어 (근거리, side -1)
            Transform playerRoot = CreateAthlete("Player", new Vector3(0f, 0f, -(Court.HalfLength + 0.8f)), 0f, m.Player,
                athleteController, out Renderer[] playerBody, out AthleteAnimator playerRacket);
            var movement = playerRoot.gameObject.AddComponent<PlayerMovement>();
            var gesture = playerRoot.gameObject.AddComponent<ShotGestureInput>();
            var playerSwing = playerRoot.gameObject.AddComponent<AutoSwing>();
            var playerServe = playerRoot.gameObject.AddComponent<PlayerServe>(); // 같은 오브젝트에 있어야 제스처가 서브로 넘긴다
            Set(movement, "config", config);
            SetInt(movement, "side", -1);
            Set(gesture, "ball", ball);
            Set(gesture, "swing", playerSwing);
            WireSwing(playerSwing, config, ball, -1, playerRacket, gesture, 1f, 11);
            Set(playerServe, "config", config);
            Set(playerServe, "ball", ball);
            Set(playerServe, "swing", playerSwing);
            Set(playerServe, "movement", movement);

            // AI 상대 (원거리, side +1). 오차 배율은 경기 시작 때 난이도로 다시 정한다.
            Transform aiRoot = CreateAthlete("Opponent", new Vector3(0f, 0f, Court.HalfLength + 0.8f), 180f, m.Opponent,
                athleteController, out _, out AthleteAnimator aiRacket);
            var ai = aiRoot.gameObject.AddComponent<AiOpponent>();
            var aiSwing = aiRoot.gameObject.AddComponent<AutoSwing>();
            Set(ai, "config", config);
            Set(ai, "ball", ball);
            Set(ai, "swing", aiSwing);
            Set(ai, "opponent", playerRoot);
            WireSwing(aiSwing, config, ball, 1, aiRacket, ai, config.AiProfileFor(config.defaultDifficulty).errorScale, 23);

            // 관전용 AI (가까운 쪽, side -1). AI 대결 관전과 선수 밸런스 측정 때만 켠다.
            Transform nearRoot = CreateAthlete("NearOpponent", new Vector3(0f, 0f, -(Court.HalfLength + 0.8f)), 0f,
                m.NearOpponent, athleteController, out _, out AthleteAnimator nearRacket);
            var nearAi = nearRoot.gameObject.AddComponent<AiOpponent>();
            var nearSwing = nearRoot.gameObject.AddComponent<AutoSwing>();
            Set(nearAi, "config", config);
            Set(nearAi, "ball", ball);
            Set(nearAi, "swing", nearSwing);
            Set(nearAi, "opponent", aiRoot);
            SetInt(nearAi, "randomSeed", 13);
            WireSwing(nearSwing, config, ball, -1, nearRacket, nearAi, config.AiProfileFor(config.defaultDifficulty).errorScale, 37);
            nearRoot.gameObject.SetActive(false);

            // 진행
            var systems = new GameObject("Systems");
            var referee = systems.AddComponent<MatchReferee>();
            Set(referee, "config", config);
            Set(referee, "ball", ball);
            Set(referee, "player", movement);
            Set(referee, "playerInput", gesture);
            Set(referee, "playerServe", playerServe);
            Set(referee, "playerSwing", playerSwing);
            Set(referee, "ai", ai);
            Set(referee, "aiSwing", aiSwing);
            Set(referee, "nearAi", nearAi);
            Set(referee, "nearAiSwing", nearSwing);

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
            SetArray(cameraRig, "hideInFirstPerson", playerBody.Cast<Object>().ToArray());
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

            var marks = systems.AddComponent<BallMarks>();
            Set(marks, "ball", ball);
            Set(marks, "material", m.BallMark);

            var hud = systems.AddComponent<TennisHud>();
            Set(hud, "config", config);
            Set(hud, "referee", referee);
            Set(hud, "player", movement);
            Set(hud, "input", gesture);
            Set(hud, "playerSwing", playerSwing);
            Set(hud, "playerServe", playerServe);
            Set(hud, "cameraRig", cameraRig);

            ValidateReferences(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FirstPersonTennis] 경기 씬 생성 완료: {ScenePath}");
        }

        static void WireSwing(AutoSwing swing, TennisConfig config, TennisBall ball, int side,
            AthleteAnimator racket, MonoBehaviour source, float errorScale, int seed)
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

        static TennisBall CreateBall(Materials m, TennisConfig config, out GameObject halo)
        {
            // 공 오브젝트의 스케일 = 설정의 공 지름 (TennisBall이 맞춘다). 모델은 지름 1로 맞춰 자식으로 단다
            var ball = new GameObject("Ball").transform;
            ball.position = new Vector3(0f, 1f, Court.HalfLength);
            ball.localScale = Vector3.one * (BallPhysics.Radius * 2f);

            GameObject visual = InstantiateModel(BallModelPath, out Bounds bounds);
            visual.name = "Visual";
            visual.transform.SetParent(ball, false);
            float size = bounds.size.x; // 모델 지름 (실제 공 0.067m)
            visual.transform.localScale /= size; // 모델 루트의 임포트 스케일은 살린다
            visual.transform.localPosition = -bounds.center / size;
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off; // 그림자는 BallShadow가 따로 그린다

            Transform haloT = Primitive(PrimitiveType.Sphere, "Halo", ball, m.Halo);
            haloT.localScale = Vector3.one * 1.9f;
            haloT.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            halo = haloT.gameObject;
            halo.SetActive(false);

            var component = ball.gameObject.AddComponent<TennisBall>();
            Set(component, "config", config);
            Set(visual.AddComponent<BallSpinVisual>(), "ball", component);
            return component;
        }

        // FBX 모델을 씬에 놓는다: Blender 축(Z 위)을 메시에 구워 루트 회전 없이, 크기 1로. bounds = 모델 자체 크기
        static GameObject InstantiateModel(string path, out Bounds bounds)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException($"모델이 없음: {path}");
            if (!importer.bakeAxisConversion || importer.importAnimation)
            {
                importer.bakeAxisConversion = true;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return go;
        }

        // 선수: 루트(네트를 봄, 이동·타격 컴포넌트) 아래 캐릭터 모델. 상의 색으로 선수를 구분한다.
        // body: 1인칭에서 숨길 몸 메시 (라켓은 1인칭에서도 보인다)
        static Transform CreateAthlete(string name, Vector3 position, float yaw, Material shirt,
            RuntimeAnimatorController controller, out Renderer[] body, out AthleteAnimator animation)
        {
            var root = new GameObject(name).transform;
            root.position = position;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AthleteModel.FbxPath));
            model.name = "Model";
            model.transform.SetParent(root, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // FBX 축 변환 뒤 모델은 -Z를 본다 → 루트 앞(+Z)으로
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            body = model.GetComponentsInChildren<SkinnedMeshRenderer>().Cast<Renderer>().ToArray();
            foreach (Renderer r in body)
                if (r.name.EndsWith("_Top")) r.sharedMaterials = Enumerable.Repeat(shirt, r.sharedMaterials.Length).ToArray();

            Transform racket = AttachRacket(model.transform);

            animation = root.gameObject.AddComponent<AthleteAnimator>();
            Set(animation, "animator", animator);
            Set(animation, "model", model.transform);
            Set(animation, "hands", body.First(r => r.name.EndsWith("_Body")));
            Set(animation, "racket", racket);
            return root;
        }

        // 라켓을 오른손에: FBX의 표식 세 점(손잡이 끝, 샤프트 쪽, 타구면 쪽)으로 방향을 잡는다.
        // 라켓 모델은 루트 기준 긴 축 +Y, 타구면 ±Z, 원점 = 손잡이 끝
        static Transform AttachRacket(Transform model)
        {
            Transform Marker(string marker) => model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == marker)
                ?? throw new InvalidOperationException($"선수 모델에 {marker}가 없음 (tennis_animations.py로 다시 내보내야 한다)");
            Transform socket = Marker("RacketSocket");
            Vector3 shaft = Marker("RacketTip").position - socket.position;
            Vector3 face = Marker("RacketFace").position - socket.position;

            var holder = new GameObject("Racket").transform;
            holder.SetPositionAndRotation(socket.position, Quaternion.LookRotation(face, shaft));
            GameObject racket = InstantiateModel(RacketModelPath, out _);
            racket.transform.SetParent(holder, false); // 루트의 축 변환 회전은 그대로
            holder.SetParent(socket, true);
            return holder;
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

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        // ── 에셋 ─────────────────────────────────────────────────

        sealed class Materials
        {
            public readonly Material Surround = Lit("Surround", new Color(0.16f, 0.36f, 0.27f));
            public readonly Material CourtSurface = Lit("HardCourt", new Color(0.13f, 0.32f, 0.58f));
            public readonly Material Line = Lit("Line", new Color(0.95f, 0.95f, 0.95f));
            public readonly Material Post = Lit("NetPost", new Color(0.2f, 0.22f, 0.2f));
            public readonly Material Player = Lit("Player", new Color(0.2f, 0.45f, 0.9f));
            public readonly Material Opponent = Lit("Opponent", new Color(0.85f, 0.25f, 0.2f));
            public readonly Material NearOpponent = Lit("NearOpponent", new Color(0.95f, 0.75f, 0.2f));
            public readonly Material Net = Transparent("Net", new Color(0.05f, 0.05f, 0.05f, 0.55f), true);
            public readonly Material Shadow = Transparent("BallShadow", new Color(0f, 0f, 0f, 0.55f));
            public readonly Material Marker = Transparent("LandingMarker", new Color(1f, 0.85f, 0.1f, 0.85f));
            public readonly Material Range = Transparent("HitRange", new Color(1f, 1f, 1f, 0.35f));
            public readonly Material Halo = Transparent("BallHalo", new Color(0.3f, 0.9f, 1f, 0.35f));
            public readonly Material BallMark = Transparent("BallMark", new Color(0.38f, 0.16f, 0.08f, 0.6f));
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
            // 지워진 씬(1단계 Phase1Court 등)은 목록에서 뺀다
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.path != path && AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null).ToList();
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
