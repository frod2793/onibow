using UnityEditor;
using UnityEngine;

/// <summary>
/// Enemy 스크립트의 인스펙터를 커스터마이징하여 테스트 버튼을 추가합니다.
/// 이 스크립트는 반드시 'Editor' 폴더 내에 위치해야 합니다.
/// </summary>
[CustomEditor(typeof(Enemy))]
public class EnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 기존 인스펙터 필드를 그대로 그립니다.
        DrawDefaultInspector();

        // 대상 Enemy 스크립트의 인스턴스를 가져옵니다.
        Enemy enemyScript = (Enemy)target;

        // 에디터 UI에 여백을 추가하여 섹션을 구분합니다.
        EditorGUILayout.Space(10);

        // 테스트용 버튼을 위한 제목을 추가합니다.
        EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

        // "스킬 테스트" 버튼을 추가합니다.
        if (GUILayout.Button("Test: Multi-Shot Skill (AK47)"))
        {
            // 버튼이 클릭되면 Enemy 스크립트의 테스트 메서드를 호출합니다.
            enemyScript.TestMultiShotSkill();
        }
    }
}