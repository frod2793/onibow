using UnityEditor;
using UnityEngine;

/// <summary>
/// Enemy 컴포넌트의 Unity 인스펙터 UI를 커스터마이징하는 에디터 클래스입니다.
/// </summary>
[CustomEditor(typeof(Enemy))]
public class EnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 기본 인스펙터 UI를 먼저 그립니다.
        base.OnInspectorGUI();

        // 대상 Enemy 스크립트의 인스턴스를 가져옵니다.
        Enemy enemyScript = (Enemy)target;

        // 인스펙터에 여백을 추가합니다.
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("테스트 기능", EditorStyles.boldLabel);

        // "Test Multi Shot" 버튼을 추가합니다.
        // 이 버튼을 누르면 Play 모드에서 Enemy 스크립트의 TestMultiShotSkill 메서드가 호출됩니다.
        if (GUILayout.Button("Test Multi Shot Skill"))
        {
            if (Application.isPlaying)
            {
                enemyScript.TestMultiShotSkill();
            }
            else
            {
                Debug.LogWarning("스킬 테스트는 Play 모드에서만 가능합니다.");
            }
        }
        
        // "Test Evade" 버튼을 추가합니다.
        if (GUILayout.Button("Test Evade"))
        {
            if (Application.isPlaying)
            {
                enemyScript.TestEvade();
            }
            else
            {
                Debug.LogWarning("회피 테스트는 Play 모드에서만 가능합니다.");
            }
        }
    }
}
