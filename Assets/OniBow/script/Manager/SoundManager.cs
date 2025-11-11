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
    private List<AudioSource> m_sfxPlayerPool;

    // 빠른 접근을 위한 딕셔너리
    private Dictionary<string, Sound> m_bgmDictionary;
    private Dictionary<string, Sound> m_sfxDictionary;

    [Header("게임 사운드 이름 정의")]
    [Header("GameManager Sounds")]
    [field: SerializeField, SoundName] public string TitleBgm { get; private set; } = "TitleBGM";
    [field: SerializeField, SoundName] public string GameplayBgm { get; private set; } = "GameplayBGM";
    [field: SerializeField, SoundName] public string DoorOpenSfx { get; private set; } = "Game_DoorOpen";
    [field: SerializeField, SoundName] public string CountdownTickSfx { get; private set; } = "Game_CountdownTick";

    [Header("Player Sounds")]
    [field: SerializeField, SoundName] public string PlayerFireSfx { get; private set; } = "Player_Fire";
    [field: SerializeField, SoundName] public string PlayerDashSfx { get; private set; } = "Player_Dash";
    [field: SerializeField, SoundName] public string PlayerDamagedSfx { get; private set; } = "Player_Damaged";
    [field: SerializeField, SoundName] public string PlayerDeathSfx { get; private set; } = "Player_Death";
    [field: SerializeField, SoundName] public string PlayerHealSfx { get; private set; } = "Player_Heal";

    [Header("UI Sounds")]
    [field: SerializeField, SoundName] public string GenericButtonClickSfx { get; private set; } = "UI_Button_Click";
    [field: SerializeField, SoundName] public string PopupOpenSfx { get; private set; } = "UI_Popup_Open";
    [field: SerializeField, SoundName] public string PopupCloseSfx { get; private set; } = "UI_Popup_Close";
    [field: SerializeField, SoundName] public string SliderChangedSfx { get; private set; } = "UI_Slider_Tick";
    [field: SerializeField, SoundName] public string ToggleChangedSfx { get; private set; } = "UI_Toggle_Switch";

    [Header("Weapon/Skill Sounds")]
    [field: SerializeField, SoundName] public string AKFireSfx { get; private set; } = "AK_Fire";
    [field: SerializeField, SoundName] public string AKHitSfx { get; private set; } = "AK_Hit";
    [field: SerializeField, SoundName] public string MissileLaunchSfx { get; private set; } = "Missile_Launch";
    [field: SerializeField, SoundName] public string MissileExplosionSfx { get; private set; } = "Missile_Explosion"; 
    [field: SerializeField, SoundName] public string RoketLaunchSfx { get; private set; } = "Roket_Launch";
    [field: SerializeField, SoundName] public string RoketExplosionSfx { get; private set; } = "Roket_Explosion";

    // --- 상태 변수 ---
    private Sound m_currentBgmSound; // 현재 재생 중인 BGM
    private float m_bgmVolume = 1f;
    private float m_sfxVolume = 1f;
    private bool m_isBgmMuted = false;
    private bool m_isSfxMuted = false;

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
        m_bgmDictionary = m_bgmSounds.ToDictionary(sound => sound.name, sound => sound);
        m_sfxDictionary = m_sfxSounds.ToDictionary(sound => sound.name, sound => sound);

        // SFX 플레이어 풀 생성
        m_sfxPlayerPool = new List<AudioSource>(m_sfxPoolSize);
        for (int i = 0; i < m_sfxPoolSize; i++)
        {
            CreateSfxPlayer();
        }

        LoadSoundSettings();
    }

    private void CreateSfxPlayer()
    {
        GameObject sfxPlayerObject = new GameObject($"SFX_Player_{m_sfxPlayerPool.Count}");
        sfxPlayerObject.transform.SetParent(transform);
        AudioSource source = sfxPlayerObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        m_sfxPlayerPool.Add(source);
    }

    /// <summary>
    /// 지정된 이름의 BGM을 재생합니다.
    /// </summary>
    /// <param name="name">재생할 BGM의 이름</param>
    /// <param name="fadeDuration">페이드 인에 걸리는 시간</param>
    public void PlayBGM(string name, float fadeDuration = 1.0f)
    {
        // Dictionary를 사용하여 BGM을 O(1) 시간 복잡도로 찾습니다.
        if (!m_bgmDictionary.TryGetValue(name, out Sound sound))
        {
            Debug.LogWarning($"BGM '{name}'을(를) 찾을 수 없습니다.");
            return;
        }

        m_currentBgmSound = sound; // 현재 BGM 정보 저장
        m_bgmPlayer.DOKill(); // 기존 페이드 효과 중지

        // 즉시 재생을 시작하되, 볼륨은 0에서 시작하여 페이드 인 효과를 줍니다.
        m_bgmPlayer.clip = sound.clip;
        m_bgmPlayer.loop = sound.loop;
        m_bgmPlayer.pitch = sound.pitch;
        m_bgmPlayer.volume = 0f; // 페이드 인을 위해 볼륨을 0으로 설정
        m_bgmPlayer.Play();

        // 음소거 상태와 개별 사운드 볼륨을 모두 고려한 최종 타겟 볼륨 계산
        float targetVolume = m_isBgmMuted ? 0f : m_currentBgmSound.volume * m_bgmVolume;
        m_bgmPlayer.DOFade(targetVolume, fadeDuration);
    }

    /// <summary>
    /// 현재 재생 중인 BGM을 정지합니다.
    /// </summary>
    /// <param name="fadeDuration">페이드 아웃에 걸리는 시간</param>
    public void StopBGM(float fadeDuration = 1.0f)
    {
        m_bgmPlayer.DOKill();
        m_currentBgmSound = null; // BGM 정지 시 현재 BGM 정보 초기화
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
        if (m_isSfxMuted) return;

        if (m_sfxDictionary != null && m_sfxDictionary.TryGetValue(name, out Sound sound))
        {
            // 현재 재생 중이 아닌 플레이어를 우선적으로 찾습니다.
            AudioSource sfxPlayer = m_sfxPlayerPool.FirstOrDefault(p => !p.isPlaying);

            // 모든 플레이어가 사용 중이라면, 가장 먼저 생성된(오래된) 플레이어를 재사용합니다.
            if (sfxPlayer == null)
            {
                sfxPlayer = m_sfxPlayerPool.FirstOrDefault();
                if (sfxPlayer == null) // 풀이 비어있는 극단적인 경우
                {
                    // 예비 플레이어를 사용합니다.
                    if (m_sfxPlayer != null)
                        m_sfxPlayer.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
                    return;
                }
            }
            sfxPlayer.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
        }
        else
        {
             Debug.LogWarning($"SFX '{name}'을(를) 찾을 수 없습니다.");
        }
    }

    #region 볼륨 및 음소거 제어 (UI 연동용)

    /// <summary>
    /// 현재 BGM 플레이어의 볼륨을 마스터 볼륨, 개별 볼륨, 음소거 상태에 맞춰 적용합니다.
    /// </summary>
    private void ApplyBgmVolume()
    {
        if (m_bgmPlayer == null || !m_bgmPlayer.isPlaying || m_currentBgmSound == null) return;

        float targetVolume = m_isBgmMuted ? 0f : m_currentBgmSound.volume * m_bgmVolume;
        m_bgmPlayer.DOKill(); // 볼륨 변경 중 페이드 효과가 있다면 중지
        m_bgmPlayer.volume = targetVolume;
    }

    public void SetBGMVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        // 부동 소수점 비교 오차를 고려하여, 값이 실제로 변경되었을 때만 PlayerPrefs에 저장합니다.
        if (!Mathf.Approximately(m_bgmVolume, volume))
        {
            PlayerPrefs.SetFloat("BGMVolume", volume);
        }
        m_bgmVolume = volume;
        ApplyBgmVolume();
    }

    public void SetSFXVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        if (!Mathf.Approximately(m_sfxVolume, volume))
        {
            PlayerPrefs.SetFloat("SFXVolume", volume);
        }
        m_sfxVolume = volume;
    }

    public void SetBGMMute(bool mute)
    {
        m_isBgmMuted = mute;
        PlayerPrefs.SetInt("BGMMuted", mute ? 1 : 0);
        ApplyBgmVolume();
    }

    public void SetSFXMute(bool mute)
    {
        m_isSfxMuted = mute;
        PlayerPrefs.SetInt("SFXMuted", mute ? 1 : 0);
    }

    /// <summary>
    /// 저장된 사운드 설정을 불러옵니다.
    /// </summary>
    private void LoadSoundSettings()
    {
        m_bgmVolume = PlayerPrefs.GetFloat("BGMVolume", 1f);
        m_sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
        m_isBgmMuted = PlayerPrefs.GetInt("BGMMuted", 0) == 1;
        m_isSfxMuted = PlayerPrefs.GetInt("SFXMuted", 0) == 1;

         ApplyBgmVolume();
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
        return m_isBgmMuted;
    }

    public bool IsSFXMuted()
    {
        return m_isSfxMuted;
    }

    #endregion

}