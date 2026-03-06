using UnityEditor;
using UnityEngine;
using OniBow.Managers; 

namespace OniBow.Editor
{
    /// <summary>
    /// EffectManager 스크립트의 인스펙터를 커스터마이징하여 테스트 버튼을 추가합니다.
    /// </summary>
    [CustomEditor(typeof(EffectManager))]
    public class EffectManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EffectManager effectManager = (EffectManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("에디터 테스트 도구", EditorStyles.boldLabel);

            if (GUILayout.Button("테스트: 체력 경고 효과 토글"))
            {
                effectManager.ToggleTestLowHealthEffect();
            }
        }
    }
}