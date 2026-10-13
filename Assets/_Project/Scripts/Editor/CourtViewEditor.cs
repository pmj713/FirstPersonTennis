using UnityEditor;
using UnityEngine;

namespace FirstPersonTennis.EditorTools
{
    // Court 오브젝트를 선택하면 코트·공 크기를 바로 조절할 수 있게 한다.
    // 값은 TennisConfig 에셋에 저장되고, 에디터와 플레이 중 모두 즉시 반영된다.
    [CustomEditor(typeof(CourtView))]
    public class CourtViewEditor : Editor
    {
        SerializedObject configObject;

        public override void OnInspectorGUI()
        {
            TennisConfig config = ((CourtView)target).Config;
            if (config != null)
            {
                if (configObject == null || configObject.targetObject != config) configObject = new SerializedObject(config);
                configObject.Update();

                EditorGUILayout.LabelField("코트·공 크기", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(configObject.FindProperty("courtWidthScale"), new GUIContent("코트 너비 배율", "1 = ITF 규격 (단식 8.23m)"));
                EditorGUILayout.PropertyField(configObject.FindProperty("courtLengthScale"), new GUIContent("코트 길이 배율", "1 = ITF 규격 (23.77m)"));
                EditorGUILayout.PropertyField(configObject.FindProperty("ballDiameter"), new GUIContent("공 지름 (m)", "실제 공 0.067m"));
                if (GUILayout.Button("코트·공 크기를 ITF 규격으로 되돌리기"))
                {
                    configObject.FindProperty("courtWidthScale").floatValue = 1f;
                    configObject.FindProperty("courtLengthScale").floatValue = 1f;
                    configObject.FindProperty("ballDiameter").floatValue = 0.067f;
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("템포", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(configObject.FindProperty("ballTimeScale"),
                    new GUIContent("공 템포", "공의 시간 배율. 1 = 실제 속도, 낮을수록 느리게 날아온다. 플레이 중 - = 키로도 조절"));
                EditorGUILayout.PropertyField(configObject.FindProperty("smashTempoBoost"),
                    new GUIContent("스매시 템포 배율", "스매시로 친 공은 공 템포에 이 값을 곱한 템포로 날아간다"));
                configObject.ApplyModifiedProperties();

                EditorGUILayout.HelpBox(
                    $"단식 {Court.SinglesHalfWidth * 2f:0.00}m × {Court.HalfLength * 2f:0.00}m · 공 템포 {config.ballTimeScale:0.00}\n" +
                    "바꾸는 즉시 반영돼요. 플레이 중에 바꾼 값도 TennisConfig에 그대로 남아요.",
                    MessageType.Info);
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();
        }
    }
}
