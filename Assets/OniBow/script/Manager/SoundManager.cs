using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    #region 싱글턴 패턴
    public static SoundManager Instance { get; private set; }
    #endregion

    [Header("사운드 목록")]
    [SerializeField] private Sound[] m_bgmSounds; // BGM 목록
    [SerializeField] private Sound[] m_sfxSounds; // SFX 목록

    [Header("오디오 소스")]
    [SerializeField] private AudioSource m_bgmPlayer; // BGM 재생용
    [SerializeField] private AudioSource m_sfxPlayer; // SFX 재생용 (풀이 꽉 찼을 때의 예비용)
    
    [Header("SFX 플레이어 풀")]
    [Tooltip("동시에 재생 가능한 최대 효과음 수")]
    [SerializeField] private int m_sfxPoolSize = 15;
    private ConcurrentQueue<AudioSource> m_sfxPlayerPool;

    // 빠른 접근을 위한 딕셔너리
    private Dictionary<string, Sound> m_sfxDictionary;

    // 볼륨 및 음소거 상태
    private float m_bgmVolume = 1f;
    private float m_sfxVolume = 1f;
    private bool m_isBgm_muted = false;
    private bool m_isSfx_muted = false;

    private void Awake()
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
        // BGM 플레이어가 인스펙터에서 할당되지 않았다면, 동적으로 생성합니다.
        if (m_bgmPlayer == null)
        {
            GameObject bgmObject = new GameObject("BGM_Player");
            bgmObject.transform.SetParent(transform);
            m_bgmPlayer = bgmObject.AddComponent<AudioSource>();
            m_bgmPlayer.playOnAwake = false;
        }

        // 예비 SFX 플레이어가 인스펙터에서 할당되지 않았다면, 동적으로 생성합니다.
        if (m_sfxPlayer == null)
        {
            GameObject sfxObject = new GameObject("SFX_Player_Reserve");
            sfxObject.transform.SetParent(transform);
            m_sfxPlayer = sfxObject.AddComponent<AudioSource>();
            m_sfxPlayer.playOnAwake = false;
        }

        // SFX 딕셔너리 초기화
        m_sfxDictionary = m_sfxSounds.ToDictionary(sound => sound.name, sound => sound);

        // SFX 플레이어 풀 생성
        m_sfxPlayerPool = new ConcurrentQueue<AudioSource>();
        for (int i = 0; i < m_sfxPoolSize; i++)
        {
            CreateSfxPlayer();
        }

        LoadSoundSettings();
    }

    private void CreateSfxPlayer()
    {
        GameObject sfxPlayerObject = new GameObject($"SFXPlayer_{m_sfxPlayerPool.Count}");
        sfxPlayerObject.transform.SetParent(transform);
        AudioSource source = sfxPlayerObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        m_sfxPlayerPool.Enqueue(source);
    }

    /// <summary>
    /// 지정된 이름의 BGM을 재생합니다.
    /// </summary>
    /// <param name="name">재생할 BGM의 이름</param>
    /// <param name="fadeDuration">페이드 인에 걸리는 시간</param>
    public void PlayBGM(string name, float fadeDuration = 1.0f)
    {
        // 이름으로 BGM 사운드를 찾습니다.
        Sound sound = Array.Find(m_bgmSounds, s => s.name == name);
        if (sound != null)
        {
            m_bgmPlayer.DOKill(); // 기존 페이드 효과 중지
            m_bgmPlayer.volume = 0;
            m_bgmPlayer.clip = sound.clip;
            m_bgmPlayer.loop = sound.loop;
            m_bgmPlayer.pitch = sound.pitch;
            m_bgmPlayer.Play();
            m_bgmPlayer.DOFade(sound.volume * m_bgmVolume, fadeDuration);
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
        m_bgmPlayer.DOKill(); // Kill the tween
        m_bgmPlayer.DOFade(0, fadeDuration).OnComplete(() => {
            m_bgmPlayer.Stop();
        });
    }

    /// <summary>
    /// 지정된 이름의 SFX를 재생합니다.
    /// </summary>
    /// <param name="name">재생할 SFX의 이름</param>
    public void PlaySFX(string name)
    {
        if (m_isSfx_muted) return;

        if (m_sfxDictionary != null && m_sfxDictionary.TryGetValue(name, out Sound sound))
        {
            if (m_sfxPlayerPool.TryDequeue(out AudioSource player))
            {
                player.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
                m_sfxPlayerPool.Enqueue(player);
                return;
            }

            // 모든 플레이어가 사용 중이면, 예비 플레이어를 사용합니다.
            m_sfxPlayer.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
        }
        else
        {
             Debug.LogWarning($"SFX '{name}'을(를) 찾을 수 없습니다.");
        }
    }

    #region 볼륨 및 음소거 제어 (UI 연동용)

    private void ApplyBGMVolume()
    {
         m_bgmPlayer.volume = m_bgmVolume;
    }

    public void SetBGMVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        if (Math.Abs(m_bgmVolume - volume) > 0.01f) // check the volume is changed
            PlayerPrefs.SetFloat("BGMVolume", volume);
        m_bgmVolume = volume;
        ApplyBGMVolume();
    }

    public void SetSFXVolume(float volume)
    {
        m_sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("SFXVolume", m_sfxVolume);
    }

    public void SetBGMMute(bool mute)
    {
        m_isBgm_muted = mute;
        PlayerPrefs.SetInt("BGMMuted", mute ? 1 : 0);
        ApplyBGMVolume();
    }

    public void SetSFXMute(bool mute)
    {
        m_isSfx_muted = mute;
        PlayerPrefs.SetInt("SFXMuted", mute ? 1 : 0);

    }

    /// <summary>
    /// 저장된 사운드 설정을 불러옵니다.
    /// </summary>
    private void LoadSoundSettings()
    {
        m_bgmVolume = PlayerPrefs.GetFloat("BGMVolume", 1f);
        m_sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
        m_isBgm_muted = PlayerPrefs.GetInt("BGMMuted", 0) == 1;
        m_isSfx_muted = PlayerPrefs.GetInt("SFXMuted", 0) == 1;

         ApplyBGMVolume();
    }

    // UI 초기화를 위한 Getter
    public float GetBGMVolume()
    {
        return m_bgmVolume;
    }

    public float GetSFXVolume()
    {
        return m_sfxVolume;
    }

    public bool IsBGMMuted()
    {
        return m_isBgm_muted;
    }

    public bool IsSFXMuted()
    {
        return m_isSfx_muted;
    }

    #endregion

}