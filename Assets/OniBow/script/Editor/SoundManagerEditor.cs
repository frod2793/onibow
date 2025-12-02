using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SoundManager의 인스펙터 UI를 커스터마이징하고,
/// 리소스 폴더의 사운드를 자동으로 캐싱하는 기능을 제공합니다.
/// </summary>
[CustomEditor(typeof(SoundManager))]
public class SoundManagerEditor : Editor
{
    private const string BgmPath = "Sounds/BGM";
    private const string SfxPath = "Sounds/SFX";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SoundManager soundManager = (SoundManager)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("리소스에서 사운드 자동 캐싱"))
        {
            Undo.RecordObject(soundManager, "Cache Sounds from Resources");

            CacheSounds(soundManager, BgmPath, true);
            CacheSounds(soundManager, SfxPath, false);

            EditorUtility.SetDirty(soundManager);
            Debug.Log("<color=green>[SoundManagerEditor]</color> 리소스 폴더의 사운드를 성공적으로 캐싱했습니다.");
        }
    }

    /// <summary>
    /// 지정된 경로에서 오디오 클립을 로드하여 SoundManager의 목록을 업데이트합니다.
    /// </summary>
    private void CacheSounds(SoundManager manager, string path, bool isBgm)
    {
        var audioClips = Resources.LoadAll<AudioClip>(path);
        if (audioClips == null || audioClips.Length == 0)
        {
            Debug.LogWarning($"[SoundManagerEditor] '{path}' 경로에서 오디오 클립을 찾을 수 없습니다.");
            return;
        }

        SerializedProperty soundArrayProperty = serializedObject.FindProperty(isBgm ? "m_bgmSounds" : "m_sfxSounds");

        var existingSounds = new List<Sound>();
        for (int i = 0; i < soundArrayProperty.arraySize; i++)
        {
            SerializedProperty soundProperty = soundArrayProperty.GetArrayElementAtIndex(i);
            existingSounds.Add(new Sound
            {
                name = soundProperty.FindPropertyRelative("name").stringValue,
                clip = soundProperty.FindPropertyRelative("clip").objectReferenceValue as AudioClip,
                volume = soundProperty.FindPropertyRelative("volume").floatValue,
                pitch = soundProperty.FindPropertyRelative("pitch").floatValue,
                loop = soundProperty.FindPropertyRelative("loop").boolValue
            });
        }

        var loadedClipNames = audioClips.Select(clip => clip.name).ToList();

        // 리소스 폴더에 없는 클립 제거
        existingSounds.RemoveAll(sound => sound.clip != null && !loadedClipNames.Contains(sound.clip.name));

        // 새로운 클립 추가
        foreach (var clip in audioClips)
        {
            if (!existingSounds.Any(s => s.clip != null && s.clip.name == clip.name))
            {
                existingSounds.Add(new Sound
                {
                    name = clip.name,
                    clip = clip,
                    volume = 1f,
                    pitch = 1f,
                    loop = isBgm
                });
            }
        }

        soundArrayProperty.ClearArray();
        for (int i = 0; i < existingSounds.Count; i++)
        {
            soundArrayProperty.InsertArrayElementAtIndex(i);
            SerializedProperty soundProperty = soundArrayProperty.GetArrayElementAtIndex(i);
            soundProperty.FindPropertyRelative("name").stringValue = existingSounds[i].name;
            soundProperty.FindPropertyRelative("clip").objectReferenceValue = existingSounds[i].clip;
            soundProperty.FindPropertyRelative("volume").floatValue = existingSounds[i].volume;
            soundProperty.FindPropertyRelative("pitch").floatValue = existingSounds[i].pitch;
            soundProperty.FindPropertyRelative("loop").boolValue = existingSounds[i].loop;
        }

        serializedObject.ApplyModifiedProperties();
    }
}