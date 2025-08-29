using UnityEngine;
using System;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// 사운드 클립의 속성을 정의하는 클래스입니다.
/// </summary>
[System.Serializable]
public class Sound
{
    public string name; // 사운드 이름 (키로 사용)
    public AudioClip clip; // 오디오 클립
    [Range(0f, 1f)]
    public float volume = 1f; // 기본 볼륨
    [Range(0.1f, 3f)]
    public float pitch = 1f; // 기본 피치
    public bool loop = false; // 반복 여부
}

/// <summary>
/// BGM과 SFX를 관리하는 중앙 사운드 매니저입니다.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("사운드 목록")]
    [SerializeField] private Sound[] bgmSounds; // BGM 목록
    [SerializeField] private Sound[] sfxSounds; // SFX 목록

    [Header("오디오 소스")]
    [SerializeField] private AudioSource bgmPlayer; // BGM 재생용
    [SerializeField] private AudioSource sfxPlayer; // SFX 재생용 (풀이 꽉 찼을 때의 예비용)
    
    [Header("SFX 플레이어 풀")]
    [Tooltip("동시에 재생 가능한 최대 효과음 수")]
    [SerializeField] private int sfxPoolSize = 15;
    private List<AudioSource> _sfxPlayerPool;

    // 빠른 접근을 위한 딕셔너리
    private Dictionary<string, Sound> _sfxDictionary;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        // SFX 딕셔너리 초기화
        _sfxDictionary = new Dictionary<string, Sound>();
        foreach (Sound sound in sfxSounds)
        {
            if (!_sfxDictionary.ContainsKey(sound.name))
            {
                _sfxDictionary.Add(sound.name, sound);
            }
        }

        // SFX 플레이어 풀 생성
        _sfxPlayerPool = new List<AudioSource>();
        for (int i = 0; i < sfxPoolSize; i++)
        {
            GameObject sfxPlayerObject = new GameObject($"SFXPlayer_{i}");
            sfxPlayerObject.transform.SetParent(transform);
            AudioSource source = sfxPlayerObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            _sfxPlayerPool.Add(source);
        }
    }

    /// <summary>
    /// 지정된 이름의 BGM을 재생합니다.
    /// </summary>
    /// <param name="name">재생할 BGM의 이름</param>
    /// <param name="fadeDuration">페이드 인에 걸리는 시간</param>
    public void PlayBGM(string name, float fadeDuration = 1.0f)
    {
        // 이름으로 BGM 사운드를 찾습니다.
        Sound sound = Array.Find(bgmSounds, s => s.name == name);
        if (sound != null)
        {
            bgmPlayer.DOKill(); // 기존 페이드 효과 중지
            bgmPlayer.volume = 0;
            bgmPlayer.clip = sound.clip;
            bgmPlayer.loop = sound.loop;
            bgmPlayer.pitch = sound.pitch;
            bgmPlayer.Play();
            bgmPlayer.DOFade(sound.volume, fadeDuration);
        }
        else
        {
            Debug.LogWarning($"BGM '{name}'을(를) 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 현재 재생 중인 BGM을 정지합니다.
    /// </summary>
    /// <param name="fadeDuration">페이드 아웃에 걸리는 시간</param>
    public void StopBGM(float fadeDuration = 1.0f)
    {
        bgmPlayer.DOKill();
        bgmPlayer.DOFade(0, fadeDuration).OnComplete(() => {
            bgmPlayer.Stop();
        });
    }

    /// <summary>
    /// 지정된 이름의 SFX를 재생합니다.
    /// </summary>
    /// <param name="name">재생할 SFX의 이름</param>
    public void PlaySFX(string name)
    {
        if (_sfxDictionary.TryGetValue(name, out Sound sound))
        {
            // 풀에서 사용 가능한 플레이어를 찾아 즉시 재생합니다.
            foreach (var player in _sfxPlayerPool)
            {
                if (!player.isPlaying)
                {
                    player.PlayOneShot(sound.clip, sound.volume);
                    return;
                }
            }
            // 모든 플레이어가 사용 중이면, 예비 플레이어를 사용합니다.
            sfxPlayer.PlayOneShot(sound.clip, sound.volume);
        }
        else
        {
            Debug.LogWarning($"SFX '{name}'을(를) 찾을 수 없습니다.");
        }
    }
}