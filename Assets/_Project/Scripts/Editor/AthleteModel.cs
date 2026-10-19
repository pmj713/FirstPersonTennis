using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FirstPersonTennis.EditorTools
{
    // 선수 캐릭터(TennisPlayer.fbx)의 Unity 설정. 경기 씬(MatchSceneBuilder)과 캐릭터 비교 씬이 같이 쓴다.
    // FBX: ArtSource/Blender/tennis_animations.py + finalize_character.py가 내보낸 것 (몸·헤어·상의·하의·신발 + 리그 + 라켓 표식)
    //   1 텍스처 파츠(얼굴·헤어·신발) URP Lit 머티리얼   2 FBX 설정: 축 변환 굽기, 머티리얼 연결, 반복 클립
    //   3 경기용 애니메이터: 이동(준비 자세 → 걷기 → 달리기) + 스윙 두 층 (온몸 / 상체만, AthleteAnimator가 시간을 맞춘다)
    public static class AthleteModel
    {
        public const string Dir = "Assets/_Project/Art/Characters/TennisPlayer";
        public const string FbxPath = Dir + "/TennisPlayer.fbx";
        const string MatchControllerPath = Dir + "/TennisPlayer_Match.controller";
        const string UpperBodyMaskPath = Dir + "/TennisPlayer_UpperBody.mask";

        // 텍스처 맵을 연결하는 파츠. 최종본에서 피부·상의·하의는 단색 재질이라 FBX에 들어 있는 재질 색을 그대로 쓴다
        static readonly string[] TexturedParts = { "Body", "Hair", "Shoes" };
        // 반복하는 클립 (나머지 스윙 클립은 한 번)
        static readonly string[] LoopClips = { "Idle", "Walk", "Run", "Ready" };
        public static readonly string[] SwingClips =
            { "Forehand", "Backhand", "VolleyForehand", "VolleyBackhand", "Smash", "Serve", "SliceForehand", "SliceBackhand", "LobForehand", "LobBackhand", "ServeSlice", "ServeKick" };

        // 이동 블렌드 트리 문턱 속도 (m/s). 게임 속도(걷기 4, 달리기 7)에 맞춰 발이 덜 미끄러지게 잡았다
        const float WalkSpeed = 2.5f, RunSpeed = 6f;

        // ---------- 1 텍스처·머티리얼 ----------

        static string TexturePath(string part, string map) => $"{Dir}/Textures/TennisPlayer_{part}_{map}.png";

        public static void Configure()
        {
            var materials = new Dictionary<string, Material>();
            foreach (string part in TexturedParts)
            {
                if (!File.Exists(TexturePath(part, "BaseColor"))) continue; // 아직 만들지 않은 파츠
                SetupTextures(part);
                materials[part] = SetupMaterial(part);
            }
            SetupModel(materials);
        }

        static void SetupTextures(string part)
        {
            ConfigureTexture(TexturePath(part, "BaseColor"), TextureImporterType.Default, true);
            ConfigureTexture(TexturePath(part, "Normal"), TextureImporterType.NormalMap, false);
            ConfigureTexture(TexturePath(part, "MetallicSmoothness"), TextureImporterType.Default, false); // R 금속성, A 매끈함
            ConfigureTexture(TexturePath(part, "Occlusion"), TextureImporterType.Default, false);
        }

        static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Blender 파이프라인을 먼저 돌려야 한다", path);
            if (importer.textureType == type && importer.sRGBTexture == srgb) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.SaveAndReimport();
        }

        static Material SetupMaterial(string part)
        {
            string path = $"{Dir}/M_TennisPlayer_{part}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            Texture2D Map(string map) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(part, map));
            mat.SetTexture("_BaseMap", Map("BaseColor"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", Map("Normal"));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", Map("MetallicSmoothness"));
            mat.SetFloat("_Smoothness", 1f); // 매끈함은 맵의 알파 그대로 (톤 보정에서 거칠기 하한을 이미 넣음)
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_OcclusionMap", Map("Occlusion"));
            mat.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ---------- 2 FBX 설정 ----------

        static void SetupModel(Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            importer.bakeAxisConversion = true; // Blender(Z 위)→Unity(Y 위) 변환을 메시에 구워 루트 회전이 0이 되게
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_TennisPlayer_" + pair.Key), pair.Value);
            importer.SaveAndReimport(); // 축 설정이 바뀐 뒤의 테이크로 클립을 만든다

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = clip.takeName; // Blender NLA 스트립 이름
                clip.loopTime = LoopClips.Contains(clip.name);
                clip.loopPose = false;     // 처음·끝 자세는 Blender에서 이미 맞췄다
            }
            string[] missing = LoopClips.Concat(SwingClips).Where(n => clips.All(c => c.name != n)).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("FBX에 없는 클립: " + string.Join(", ", missing));
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        public static Dictionary<string, AnimationClip> LoadClips() =>
            AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);

        // ---------- 3 경기용 애니메이터 ----------

        // 층 0 이동, 층 1 스윙(온몸), 층 2 스윙(상체만). 스윙 상태는 속도 0: AthleteAnimator가 매 프레임 시간을 정한다
        public static AnimatorController MatchController()
        {
            Dictionary<string, AnimationClip> clips = LoadClips();
            AssetDatabase.DeleteAsset(MatchControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(MatchControllerPath);
            controller.AddParameter(AthleteAnimator.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(AthleteAnimator.MoveRateParameter, AnimatorControllerParameterType.Float);

            AnimatorState move = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendParameter = AthleteAnimator.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Ready"], 0f);
            tree.AddChild(clips["Walk"], WalkSpeed);
            tree.AddChild(clips["Run"], RunSpeed);
            move.speedParameterActive = true;
            move.speedParameter = AthleteAnimator.MoveRateParameter;
            controller.layers[0].stateMachine.defaultState = move;

            AddSwingLayer(controller, AthleteAnimator.SwingBodyLayer, null, clips);
            AddSwingLayer(controller, AthleteAnimator.SwingUpperLayer, UpperBodyMask(), clips);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // 경기 씬을 다시 만들지 않고 클립만 갱신한다 (컨트롤러 GUID 유지). 동작만 고쳤을 때 쓴다
        [MenuItem("FirstPersonTennis/Refresh Tennis Animations")]
        public static void RefreshAnimations()
        {
            Configure();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MatchControllerPath);
            if (controller == null) { MatchController(); return; }
            var clips = LoadClips();
            foreach (var layer in controller.layers)
            {
                if (layer.name != AthleteAnimator.SwingBodyLayer && layer.name != AthleteAnimator.SwingUpperLayer) continue;
                foreach (string name in SwingClips)
                {
                    var state = layer.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                                ?? layer.stateMachine.AddState(name);
                    state.motion = clips[name];
                    state.speed = 0f;
                    EditorUtility.SetDirty(state);
                }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("TENNIS_ANIMATIONS_REFRESHED " + string.Join(", ", SwingClips));
        }

        static void AddSwingLayer(AnimatorController controller, string name, AvatarMask mask, Dictionary<string, AnimationClip> clips)
        {
            controller.AddLayer(name);
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.defaultWeight = 0f;
            layer.avatarMask = mask;
            AnimatorStateMachine machine = layer.stateMachine;
            machine.defaultState = machine.AddState("None");
            foreach (string clip in SwingClips)
            {
                AnimatorState state = machine.AddState(clip);
                state.motion = clips[clip];
                state.speed = 0f;
            }
            controller.layers = layers;
        }

        // 허리(Spine)부터 위: 이동 중에 치면 다리는 이동 클립, 상체는 스윙 클립
        static AvatarMask UpperBodyMask()
        {
            AssetDatabase.DeleteAsset(UpperBodyMaskPath);
            var mask = new AvatarMask();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            Transform spine = model.GetComponentsInChildren<Transform>().First(t => t.name == "Spine");
            Transform[] all = model.GetComponentsInChildren<Transform>();
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(all[i], model.transform));
                mask.SetTransformActive(i, all[i] == spine || all[i].IsChildOf(spine));
            }
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            return mask;
        }
    }
}
