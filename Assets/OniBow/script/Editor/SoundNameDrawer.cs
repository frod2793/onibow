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
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.HelpBox(position, "SoundName 속성은 string 타입에만 사용할 수 있습니다.", MessageType.Error);
            return;
        }

        if (!_isInitialized || _soundNames == null)
        {
            InitializeSoundNames();
            _isInitialized = true;
        }

        string currentSoundName = property.stringValue;
        int currentIndex = 0;

        if (!string.IsNullOrEmpty(currentSoundName))
        {
            int foundIndex = _soundNames.IndexOf(currentSoundName);
            if (foundIndex != -1)
            {
                currentIndex = foundIndex;
            }
            else
            {
                label.text += " (Missing!)";
                GUI.color = Color.yellow;
            }
        }

        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, _soundNames.ToArray());
        GUI.color = Color.white;

        if (newIndex != currentIndex)
        {
            property.stringValue = (newIndex == 0) ? "" : _soundNames[newIndex];
        }
    }

    private void InitializeSoundNames()
    {
        _soundNames = new List<string> { "None" };

        SoundManager soundManager = Object.FindFirstObjectByType<SoundManager>();
        if (soundManager == null)
        {
            Debug.LogWarning("[SoundNameDrawer] 씬에서 SoundManager를 찾을 수 없습니다.");
            return;
        }

        SerializedObject so = new SerializedObject(soundManager);
        
        SerializedProperty bgmSoundsProp = so.FindProperty("m_bgmSounds");
        for (int i = 0; i < bgmSoundsProp.arraySize; i++)
        {
            _soundNames.Add(bgmSoundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
        }

        SerializedProperty sfxSoundsProp = so.FindProperty("m_sfxSounds");
        for (int i = 0; i < sfxSoundsProp.arraySize; i++)
        {
            _soundNames.Add(sfxSoundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
        }

        _soundNames = _soundNames.Distinct().OrderBy(s => s).ToList();
    }
}