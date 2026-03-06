using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using OniBow.Utils; // SoundNameAttribute

namespace OniBow.Managers
{
    [System.Serializable]
    public class Sound
    {
        public string name;
        public AudioClip clip;
        [Range(0f, 1f)]
        public float volume = 1f;
        [Range(0.1f, 3f)]
        public float pitch = 1f;
        public bool loop = false;
    }

    /// <summary>
    /// BGM과 SFX를 관리하는 중앙 사운드 매니저입니다.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        private class PooledSfxPlayer
        {
            public AudioSource audioSource;
            public long lastPlayedFrame;

            public PooledSfxPlayer(AudioSource source)
            {
                audioSource = source;
                lastPlayedFrame = 0;
            }
        }

        public static SoundManager Instance { get; private set; }

        [Header("사운드 목록")]
        [SerializeField] private Sound[] m_bgmSounds;
        [SerializeField] private Sound[] m_sfxSounds;

        [Header("오디오 소스")]
        [SerializeField] private AudioSource m_bgmPlayer;
        [SerializeField] private AudioSource m_sfxPlayer; // SFX 풀이 가득 찼을 때를 대비한 예비 플레이어

        [Header("SFX 플레이어 풀")]
        [Tooltip("동시에 재생 가능한 최대 효과음 수")]
        [SerializeField] private int m_sfxPoolSize = 15;
        private List<PooledSfxPlayer> m_sfxPlayerPool;

        private Dictionary<string, Sound> m_bgmDictionary;
        private Dictionary<string, Sound> m_sfxDictionary;

        [Header("게임 사운드 이름 정의")]
        [field: SerializeField, SoundName] public string TitleBgm { get; private set; } = "TitleBGM";
        [field: SerializeField, SoundName] public string GameplayBgm { get; private set; } = "GameplayBGM";
        [field: SerializeField, SoundName] public string DoorOpenSfx { get; private set; } = "Game_DoorOpen";
        [field: SerializeField, SoundName] public string CountdownTickSfx { get; private set; } = "Game_CountdownTick";

        [Header("플레이어 사운드")]
        [field: SerializeField, SoundName] public string PlayerFireSfx { get; private set; } = "Player_Fire";
        [field: SerializeField, SoundName] public string PlayerDashSfx { get; private set; } = "Player_Dash";
        [field: SerializeField, SoundName] public string PlayerDamagedSfx { get; private set; } = "Player_Damaged";
        [field: SerializeField, SoundName] public string PlayerDeathSfx { get; private set; } = "Player_Death";
        [field: SerializeField, SoundName] public string PlayerHealSfx { get; private set; } = "Player_Heal";

        [Header("UI 사운드")]
        [field: SerializeField, SoundName] public string GenericButtonClickSfx { get; private set; } = "UI_Button_Click";
        [field: SerializeField, SoundName] public string PopupOpenSfx { get; private set; } = "UI_Popup_Open";
        [field: SerializeField, SoundName] public string PopupCloseSfx { get; private set; } = "UI_Popup_Close";
        [field: SerializeField, SoundName] public string SliderChangedSfx { get; private set; } = "UI_Slider_Tick";
        [field: SerializeField, SoundName] public string ToggleChangedSfx { get; private set; } = "UI_Toggle_Switch";

        [Header("무기/스킬 사운드")]
        [field: SerializeField, SoundName] public string AKFireSfx { get; private set; } = "AK_Fire";
        [field: SerializeField, SoundName] public string AKHitSfx { get; private set; } = "AK_Hit";
        [field: SerializeField, SoundName] public string MissileLaunchSfx { get; private set; } = "Missile_Launch";
        [field: SerializeField, SoundName] public string MissileExplosionSfx { get; private set; } = "Missile_Explosion"; 
        [field: SerializeField, SoundName] public string RoketLaunchSfx { get; private set; } = "Roket_Launch";
        [field: SerializeField, SoundName] public string RoketExplosionSfx { get; private set; } = "Roket_Explosion";

        [Header("적 사운드")]
        [field: SerializeField, SoundName] public string EnemyAttackSfx { get; private set; } = "Enemy_Attack";
        [field: SerializeField, SoundName] public string EnemyDamagedSfx { get; private set; } = "Enemy_Damaged";
        [field: SerializeField, SoundName] public string EnemyDeathSfx { get; private set; } = "Enemy_Death";
        [field: SerializeField, SoundName] public string EnemyEvadeSfx { get; private set; } = "Enemy_Evade";
        [field: SerializeField, SoundName] public string EnemyHealSfx { get; private set; } = "Enemy_Heal";

        private Sound m_currentBgmSound;
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
            if (m_bgmPlayer == null)
            {
                GameObject bgmObject = new GameObject("BGM_Player");
                bgmObject.transform.SetParent(transform);
                m_bgmPlayer = bgmObject.AddComponent<AudioSource>();
                m_bgmPlayer.playOnAwake = false;
            }

            if (m_sfxPlayer == null)
            {
                GameObject sfxObject = new GameObject("SFX_Player_Reserve");
                sfxObject.transform.SetParent(transform);
                m_sfxPlayer = sfxObject.AddComponent<AudioSource>();
                m_sfxPlayer.playOnAwake = false;
            }

            m_bgmDictionary = m_bgmSounds.ToDictionary(sound => sound.name, sound => sound);
            m_sfxDictionary = m_sfxSounds.ToDictionary(sound => sound.name, sound => sound);

            m_sfxPlayerPool = new List<PooledSfxPlayer>(m_sfxPoolSize);
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
            m_sfxPlayerPool.Add(new PooledSfxPlayer(source));
        }

        /// <summary>
        /// 지정된 이름의 BGM을 페이드 인 효과와 함께 재생합니다.
        /// </summary>
        /// <param name="name">재생할 BGM의 이름</param>
        /// <param name="fadeDuration">페이드 인에 걸리는 시간</param>
        public void PlayBGM(string name, float fadeDuration = 1.0f)
        {
            if (!m_bgmDictionary.TryGetValue(name, out Sound sound))
            {
                Debug.LogWarning($"BGM '{name}'을(를) 찾을 수 없습니다.");
                return;
            }

            m_currentBgmSound = sound;
            m_bgmPlayer.DOKill();

            m_bgmPlayer.clip = sound.clip;
            m_bgmPlayer.loop = sound.loop;
            m_bgmPlayer.pitch = sound.pitch;
            m_bgmPlayer.volume = 0f; 
            m_bgmPlayer.Play();

            float targetVolume = m_isBgmMuted ? 0f : m_currentBgmSound.volume * m_bgmVolume;
            m_bgmPlayer.DOFade(targetVolume, fadeDuration);
        }

        /// <summary>
        /// 현재 재생 중인 BGM을 페이드 아웃 효과와 함께 정지합니다.
        /// </summary>
        /// <param name="fadeDuration">페이드 아웃에 걸리는 시간</param>
        public void StopBGM(float fadeDuration = 1.0f)
        {
            m_bgmPlayer.DOKill();
            m_currentBgmSound = null;
            m_bgmPlayer.DOFade(0, fadeDuration).OnComplete(() => {
                m_bgmPlayer.Stop();
            });
        }

        /// <summary>
        /// 지정된 이름의 SFX를 재생합니다. 사용 가능한 오디오 소스를 풀에서 찾아 사용합니다.
        /// </summary>
        /// <param name="name">재생할 SFX의 이름</param>
        public void PlaySFX(string name)
        {
            if (m_isSfxMuted) return;

            if (m_sfxDictionary.TryGetValue(name, out Sound sound))
            {
                PooledSfxPlayer sfxPlayer = m_sfxPlayerPool.FirstOrDefault(p => !p.audioSource.isPlaying);

                // 모든 플레이어가 사용 중이라면, 가장 오래된 플레이어를 재사용합니다.
                if (sfxPlayer == null)
                {
                    sfxPlayer = m_sfxPlayerPool.OrderBy(p => p.lastPlayedFrame).FirstOrDefault();
                }

                if (sfxPlayer == null)
                {
                    // 예비 플레이어를 사용합니다.
                    if (m_sfxPlayer != null)
                        m_sfxPlayer.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
                    return;
                }
                
                sfxPlayer.audioSource.PlayOneShot(sound.clip, sound.volume * m_sfxVolume);
                sfxPlayer.lastPlayedFrame = Time.frameCount;
            }
            else
            {
                 Debug.LogWarning($"SFX '{name}'을(를) 찾을 수 없습니다.");
            }
        }

        #region 볼륨 및 음소거 제어
        
        private void ApplyBgmVolume()
        {
            if (m_bgmPlayer == null || !m_bgmPlayer.isPlaying || m_currentBgmSound == null) return;

            float targetVolume = m_isBgmMuted ? 0f : m_currentBgmSound.volume * m_bgmVolume;
            m_bgmPlayer.DOKill();
            m_bgmPlayer.volume = targetVolume;
        }

        public void SetBGMVolume(float volume)
        {
            volume = Mathf.Clamp01(volume);
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

        private void LoadSoundSettings()
        {
            m_bgmVolume = PlayerPrefs.GetFloat("BGMVolume", 1f);
            m_sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
            m_isBgmMuted = PlayerPrefs.GetInt("BGMMuted", 0) == 1;
            m_isSfxMuted = PlayerPrefs.GetInt("SFXMuted", 0) == 1;

             ApplyBgmVolume();
        }

        public float GetBGMVolume() => m_bgmVolume;
        public float GetSFXVolume() => m_sfxVolume;
        public bool IsBGMMuted() => m_isBgmMuted;
        public bool IsSFXMuted() => m_isSfxMuted;

        #endregion
    }
}