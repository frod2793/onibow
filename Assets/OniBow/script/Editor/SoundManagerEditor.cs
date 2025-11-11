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
        // 기본 인스펙터 UI를 먼저 그립니다.
        DrawDefaultInspector();

        // 대상 SoundManager 인스턴스를 가져옵니다.
        SoundManager soundManager = (SoundManager)target;

        // 버튼을 추가하기 전에 공간을 만듭니다.
        EditorGUILayout.Space();

        // "리소스에서 사운드 자동 캐싱" 버튼을 인스펙터에 추가합니다.
        if (GUILayout.Button("리소스에서 사운드 자동 캐싱"))
        {
            // 사용자가 작업을 되돌릴 수 있도록 Undo 스택에 기록합니다.
            Undo.RecordObject(soundManager, "Cache Sounds from Resources");

            // BGM과 SFX를 캐싱하는 메서드를 호출합니다.
            CacheSounds(soundManager, BgmPath, true);
            CacheSounds(soundManager, SfxPath, false);

            // 변경 사항이 Unity 에디터에 저장되도록 표시합니다.
            EditorUtility.SetDirty(soundManager);

            Debug.Log("<color=green>[SoundManagerEditor]</color> 리소스 폴더의 사운드를 성공적으로 캐싱했습니다.");
        }
    }

    /// <summary>
    /// 지정된 경로에서 오디오 클립을 로드하여 SoundManager의 목록을 업데이트합니다.
    /// </summary>
    /// <param name="manager">업데이트할 SoundManager 인스턴스</param>
    /// <param name="path">Resources 폴더 내의 사운드 경로</param>
    /// <param name="isBgm">BGM 목록을 업데이트할지 여부</param>
    private void CacheSounds(SoundManager manager, string path, bool isBgm)
    {
        // 1. 리소스 폴더에서 모든 오디오 클립을 로드합니다.
        var audioClips = Resources.LoadAll<AudioClip>(path);
        if (audioClips == null || audioClips.Length == 0)
        {
            Debug.LogWarning($"[SoundManagerEditor] '{path}' 경로에서 오디오 클립을 찾을 수 없습니다.");
            return;
        }

        // 2. SoundManager에서 현재 사운드 목록을 가져옵니다.
        // SerializedObject와 SerializedProperty를 사용하여 배열을 안전하게 수정합니다.
        SerializedProperty soundArrayProperty = serializedObject.FindProperty(isBgm ? "m_bgmSounds" : "m_sfxSounds");

        // 3. 기존 목록을 List<Sound>로 변환하여 쉽게 조작할 수 있도록 합니다.
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

        // 4. 로드된 오디오 클립을 기반으로 목록을 업데이트합니다.
        var loadedClipNames = audioClips.Select(clip => clip.name).ToList();

        // 4-1. 삭제된 클립 제거: 기존 목록에는 있지만, 리소스 폴더에는 없는 항목을 제거합니다.
        existingSounds.RemoveAll(sound => sound.clip != null && !loadedClipNames.Contains(sound.clip.name));

        // 4-2. 새로운 클립 추가: 리소스 폴더에는 있지만, 기존 목록에는 없는 항목을 추가합니다.
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
                    loop = isBgm // BGM은 기본적으로 루프, SFX는 아님
                });
            }
        }

        // 5. 업데이트된 리스트를 다시 SerializedProperty 배열에 적용합니다.
        soundArrayProperty.ClearArray(); // 배열을 비웁니다.
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

        // 변경 사항을 적용합니다.
        serializedObject.ApplyModifiedProperties();
    }
}