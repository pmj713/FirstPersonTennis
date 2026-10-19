using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis.EditorTools
{
    // VARCO 3D 파츠 캐릭터(TennisPlayer)의 Unity 설정과 정리 전·후 비교.
    // 정리 후 TennisPlayer.fbx: 손본 최종 .blend를 ArtSource/Blender/finalize_character.py로 내보낸 것 (몸·헤어·상의·하의·신발 + 리그).
    // 정리 전 Before/: character_parts_pipeline.py가 원본 파츠를 한 줄로 늘어놓은 것.
    //   1·2 텍스처·머티리얼·FBX 설정은 AthleteModel (경기 씬과 같이 쓴다)
    //   3 애니메이터 (Speed로 Idle → Walk → Run)   4 빈 씬에 정리 전·후를 나란히 놓고 Docs/Portfolio에 캡처
    // 메뉴: FirstPersonTennis > Build Character Compare Scene
    // unity run <project> -- -executeMethod FirstPersonTennis.EditorTools.CharacterCompareBuilder.Build
    public static class CharacterCompareBuilder
    {
        const string Dir = AthleteModel.Dir;
        const string FbxPath = AthleteModel.FbxPath;
        const string BeforePath = Dir + "/Before/TennisPlayer_Before.fbx";
        const string ControllerPath = Dir + "/TennisPlayer.controller";
        public const string ScenePath = "Assets/_Project/Scenes/CharacterCompare.unity";
        const string CaptureDir = "Docs/Portfolio";
        static readonly string[] ClipNames = { "Idle", "Walk", "Run" };

        // 블렌드 트리 문턱 속도 (m/s): 걷기 1.4, 달리기(조깅) 3.5
        const float WalkSpeed = 1.4f, RunSpeed = 3.5f;

        [MenuItem("FirstPersonTennis/Build Character Compare Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            AthleteModel.Configure();
            AnimatorController controller = SetupController();
            BuildScene(controller);
        }

        // ---------- 3 애니메이터 ----------

        static AnimatorController SetupController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            Dictionary<string, AnimationClip> clips = AthleteModel.LoadClips();
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], WalkSpeed);
            tree.AddChild(clips["Run"], RunSpeed);
            controller.layers[0].stateMachine.defaultState = state;
            AssetDatabase.SaveAssets();
            return controller;
        }

        // ---------- 4 비교 씬·캡처 ----------

        static void BuildScene(AnimatorController controller)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 클립 자세 캡처(x = 40)까지 덮는다

            Dictionary<string, AnimationClip> clips = AthleteModel.LoadClips();
            // 정리 전: 원본 파츠 (각자 1m 정규화, 원점이 가운데라 절반이 바닥 아래) — 줄의 오른쪽 끝을 x = -0.7에
            GameObject before = Place(BeforePath, "Before (VARCO 원본 파츠)", Vector3.zero);
            Stats b = Measure(before);
            before.transform.position = new Vector3(-0.7f - b.Bounds.max.x, 0f, -b.Bounds.center.z);
            float beforeX = -0.7f - b.Bounds.extents.x;
            GameObject after = Place(FbxPath, "After (조립·정리 후)", new Vector3(1.4f, 0f, 0f));
            var animator = after.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            clips["Idle"].SampleAnimation(after, 1f);
            Stats a = Measure(after);

            // 실제 키 1.8m 기준 막대
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Height 1.8m";
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.position = new Vector3(2.4f, 0.9f, 0f);
            bar.transform.localScale = new Vector3(0.03f, 1.8f, 0.03f);
            Label("1.8 m", new Vector3(2.4f, 1.86f, 0f), 0.04f);

            Label($"BEFORE  VARCO raw parts\n{b.Meshes} meshes · {b.Tris:N0} tris · each ~{b.Height:0.0} m, origin at center\nroot rot {b.RootRotation}",
                  new Vector3(beforeX, -0.02f, 1.2f), 0.04f, flat: true);
            Label($"AFTER  assembled · game-ready\n{a.Meshes} meshes · {a.Tris:N0} tris · {a.Height:0.00} m\nroot rot {a.RootRotation} · Idle",
                  new Vector3(1.4f, -0.02f, 1.2f), 0.04f, flat: true);
            Debug.Log($"[CharacterCompare] 정리 전 메시 {b.Meshes}개 {b.Tris} tris, 높이 {b.Height:0.000}m, 루트 회전 {b.RootRotation} / " +
                      $"정리 후 메시 {a.Meshes}개 {a.Tris} tris, 키 {a.Height:0.000}m, 루트 회전 {a.RootRotation}, 클립 {string.Join(", ", clips.Keys)}");

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = 32f;
            float centerX = (beforeX - b.Bounds.extents.x + 2.6f) / 2f;
            cam.transform.position = new Vector3(centerX, 1.6f, 10.5f);
            cam.transform.LookAt(new Vector3(centerX, 0.75f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.85f, 0.88f);

            Directory.CreateDirectory(CaptureDir);
            Capture(cam, $"{CaptureDir}/character_before_after.png");
            CaptureClips(cam, clips);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterCompare] 씬 저장 {ScenePath}, 캡처 {CaptureDir}/");
        }

        // 클립마다 한 장면씩: Idle·Walk·Run 자세를 나란히 (캡처한 뒤 지운다)
        static void CaptureClips(Camera cam, Dictionary<string, AnimationClip> clips)
        {
            var group = new GameObject("ClipPoses");
            group.transform.position = new Vector3(40f, 0f, 0f);
            for (int i = 0; i < ClipNames.Length; i++)
            {
                AnimationClip clip = clips[ClipNames[i]];
                GameObject go = Place(FbxPath, ClipNames[i], group.transform.position + new Vector3((i - 1) * 1.3f, 0f, 0f));
                go.transform.SetParent(group.transform, true);
                clip.SampleAnimation(go, clip.length * 0.3f);
                Label($"{ClipNames[i]}\n{clip.length:0.00}s · loop", go.transform.position + new Vector3(0f, -0.02f, 0.9f), 0.03f, flat: true)
                    .transform.SetParent(group.transform, true);
            }
            Vector3 old = cam.transform.position;
            Quaternion oldRot = cam.transform.rotation;
            cam.transform.position = group.transform.position + new Vector3(0f, 1.25f, 6.2f);
            cam.transform.LookAt(group.transform.position + new Vector3(0f, 0.85f, 0f));
            Capture(cam, $"{CaptureDir}/character_clips.png");
            cam.transform.SetPositionAndRotation(old, oldRot);
            Object.DestroyImmediate(group);
        }

        static GameObject Place(string path, string name, Vector3 position)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.name = name;
            go.transform.position = position;
            return go;
        }

        struct Stats
        {
            public int Tris, Meshes;
            public float Height;
            public Bounds Bounds;
            public string RootRotation;
        }

        // 삼각형 수, 지금 자세의 실제 높이·범위 (스킨 메시는 자세를 구워서, 일반 메시는 그대로)
        static Stats Measure(GameObject go)
        {
            var stats = new Stats { RootRotation = FormatEuler(go.transform.GetChild(0).localEulerAngles) };
            var bounds = new Bounds();
            bool first = true;
            void Add(Transform t, Mesh mesh)
            {
                stats.Tris += mesh.triangles.Length / 3;
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 p = t.TransformPoint(v);
                    if (first) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    first = false;
                }
            }
            var baked = new Mesh();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(baked, true);
                Add(smr.transform, baked);
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                Add(mf.transform, mf.sharedMesh);
            Object.DestroyImmediate(baked);
            stats.Bounds = bounds;
            stats.Height = bounds.size.y;
            stats.Meshes = go.GetComponentsInChildren<Renderer>().Length;
            return stats;
        }

        static string FormatEuler(Vector3 e)
        {
            static float Wrap(float a) => Mathf.Round(Mathf.DeltaAngle(0f, a) * 10f) / 10f;
            return $"({Wrap(e.x)}, {Wrap(e.y)}, {Wrap(e.z)})";
        }

        static GameObject Label(string text, Vector3 position, float size, bool flat = false)
        {
            var go = new GameObject("Label");
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.UpperCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.12f, 0.14f, 0.18f);
            go.transform.position = position;
            // 카메라(+Z 쪽)에서 읽히게 돌린다. 바닥 글씨는 눕힌다.
            go.transform.rotation = flat ? Quaternion.Euler(70f, 180f, 0f) : Quaternion.Euler(0f, 180f, 0f);
            return go;
        }

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
            sun.transform.rotation = Quaternion.Euler(40f, 160f, 0f); // 카메라 쪽 앞에서 비춘다
        }

        static void Capture(Camera cam, string file, int width = 1600, int height = 900)
        {
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
