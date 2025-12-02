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
        base.OnInspectorGUI();

        Enemy enemyScript = (Enemy)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("테스트 기능", EditorStyles.boldLabel);

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
