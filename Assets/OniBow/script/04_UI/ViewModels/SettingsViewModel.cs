using System;
using OniBow.Managers;

namespace OniBow.UI.ViewModels
{
    public class SettingsViewModel
    {
        private readonly SoundManager m_soundManager;

        public event Action<float, bool> OnBgmStateChanged;
        public event Action<float, bool> OnSfxStateChanged;

        public SettingsViewModel(SoundManager soundManager)
        {
            m_soundManager = soundManager;
        }

        public void Initialize(SoundManager soundManager)
        {
            // 호환성 유지용 (VContainer 사용 시 생성자 주입됨)
        }

        public void RequestInitialState()
        {
            if (m_soundManager != null)
            {
                OnBgmStateChanged?.Invoke(m_soundManager.GetBGMVolume(), m_soundManager.IsBGMMuted());
                OnSfxStateChanged?.Invoke(m_soundManager.GetSFXVolume(), m_soundManager.IsSFXMuted());
            }
        }

        public void SetBgmVolume(float value)
        {
            if (m_soundManager != null)
            {
                m_soundManager.SetBGMVolume(value);
                OnBgmStateChanged?.Invoke(value, m_soundManager.IsBGMMuted());
            }
        }

        public void SetSfxVolume(float value)
        {
            if (m_soundManager != null)
            {
                m_soundManager.SetSFXVolume(value);
                OnSfxStateChanged?.Invoke(value, m_soundManager.IsSFXMuted());
            }
        }

        public void SetBgmMute(bool mute)
        {
            if (m_soundManager != null)
            {
                m_soundManager.SetBGMMute(mute);
                OnBgmStateChanged?.Invoke(m_soundManager.GetBGMVolume(), mute);
            }
        }

        public void SetSfxMute(bool mute)
        {
            if (m_soundManager != null)
            {
                m_soundManager.SetSFXMute(mute);
                OnSfxStateChanged?.Invoke(m_soundManager.GetSFXVolume(), mute);
            }
        }
    }
}

