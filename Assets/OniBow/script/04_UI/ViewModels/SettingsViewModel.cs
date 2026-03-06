using System;
using OniBow.Managers;

namespace OniBow.UI.ViewModels
{
    public class SettingsViewModel
    {
        private SoundManager m_SoundManager;
        public event Action<float, bool> OnBgmStateChanged;
        public event Action<float, bool> OnSfxStateChanged;

        public void Initialize(SoundManager soundManager)
        {
            m_SoundManager = soundManager;
            
            // 초기 상태 전파
            if (m_SoundManager != null)
            {
                OnBgmStateChanged?.Invoke(m_SoundManager.GetBGMVolume(), m_SoundManager.IsBGMMuted());
                OnSfxStateChanged?.Invoke(m_SoundManager.GetSFXVolume(), m_SoundManager.IsSFXMuted());
            }
        }

        public void SetBgmVolume(float value)
        {
            if (m_SoundManager != null)
            {
                m_SoundManager.SetBGMVolume(value);
                OnBgmStateChanged?.Invoke(value, m_SoundManager.IsBGMMuted());
            }
        }

        public void SetSfxVolume(float value)
        {
            if (m_SoundManager != null)
            {
                m_SoundManager.SetSFXVolume(value);
                OnSfxStateChanged?.Invoke(value, m_SoundManager.IsSFXMuted());
            }
        }

        public void SetBgmMute(bool mute)
        {
            if (m_SoundManager != null)
            {
                m_SoundManager.SetBGMMute(mute);
                OnBgmStateChanged?.Invoke(m_SoundManager.GetBGMVolume(), mute);
            }
        }

        public void SetSfxMute(bool mute)
        {
            if (m_SoundManager != null)
            {
                m_SoundManager.SetSFXMute(mute);
                OnSfxStateChanged?.Invoke(m_SoundManager.GetSFXVolume(), mute);
            }
        }
    }
}

