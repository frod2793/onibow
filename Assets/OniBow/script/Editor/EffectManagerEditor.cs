using UnityEditor;
using UnityEngine;

/// <summary>
/// EffectManager 스크립트의 인스펙터를 커스터마이징하여 테스트 버튼을 추가합니다.
/// </summary>
[CustomEditor(typeof(EffectManager))]
public class EffectManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 기존 인스펙터 필드를 그대로 그립니다.
        DrawDefaultInspector();

        EffectManager effectManager = (EffectManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("에디터 테스트 도구", EditorStyles.boldLabel);

        // "체력 경고 효과 토글" 버튼을 추가합니다.
        if (GUILayout.Button("테스트: 체력 경고 효과 토글"))
        {
            // 버튼이 클릭되면 EffectManager 스크립트의 테스트 메서드를 호출합니다.
            // 이 메서드는 Play 모드에서만 동작하도록 내부에 로직이 구현되어 있습니다.
            effectManager.ToggleTestLowHealthEffect();
        }
    }
}