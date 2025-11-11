using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// [SoundName] 속성이 지정된 string 필드를 위한 커스텀 프로퍼티 드로어입니다.
/// 인스펙터에 SoundManager의 사운드 목록을 드롭다운으로 표시합니다.
/// </summary>
[CustomPropertyDrawer(typeof(SoundNameAttribute))]
public class SoundNameDrawer : PropertyDrawer
{
    private static List<string> _soundNames;
    private static bool _isInitialized = false;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 이 드로어는 string 타입의 필드에만 작동합니다.
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.HelpBox(position, "SoundName 속성은 string 타입에만 사용할 수 있습니다.", MessageType.Error);
            return;
        }

        // 에디터가 처음 로드되거나 플레이 모드가 변경될 때 사운드 목록을 다시 불러옵니다.
        if (!_isInitialized || _soundNames == null)
        {
            InitializeSoundNames();
            _isInitialized = true;
        }

        string currentSoundName = property.stringValue;
        int currentIndex = 0; // 기본값은 "None"

        if (!string.IsNullOrEmpty(currentSoundName))
        {
            int foundIndex = _soundNames.IndexOf(currentSoundName);
            if (foundIndex != -1)
            {
                currentIndex = foundIndex;
            }
            else
            {
                // 목록에 없는 사운드 이름이 설정되어 있을 경우, 경고를 표시합니다.
                label.text += " (Missing!)";
                GUI.color = Color.yellow;
            }
        }

        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, _soundNames.ToArray());
        GUI.color = Color.white; // 색상 원상 복구

        if (newIndex != currentIndex)
        {
            property.stringValue = (newIndex == 0) ? "" : _soundNames[newIndex];
        }
    }

    /// <summary>
    /// SoundManager에서 모든 BGM 및 SFX 이름을 가져와 목록을 초기화합니다.
    /// </summary>
    private void InitializeSoundNames()
    {
        _soundNames = new List<string> { "None" }; // 첫 항목은 선택 안 함 옵션

        SoundManager soundManager = Object.FindFirstObjectByType<SoundManager>();
        if (soundManager == null)
        {
            Debug.LogWarning("[SoundNameDrawer] 씬에서 SoundManager를 찾을 수 없습니다.");
            return;
        }

        SerializedObject so = new SerializedObject(soundManager);
        
        // BGM 목록 추가
        SerializedProperty bgmSoundsProp = so.FindProperty("m_bgmSounds");
        for (int i = 0; i < bgmSoundsProp.arraySize; i++)
        {
            _soundNames.Add(bgmSoundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
        }

        // SFX 목록 추가
        SerializedProperty sfxSoundsProp = so.FindProperty("m_sfxSounds");
        for (int i = 0; i < sfxSoundsProp.arraySize; i++)
        {
            _soundNames.Add(sfxSoundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
        }

        // 중복 제거 및 정렬
        _soundNames = _soundNames.Distinct().OrderBy(s => s).ToList();
    }
}