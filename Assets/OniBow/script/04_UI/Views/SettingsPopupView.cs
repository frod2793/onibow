using UnityEngine;
using UnityEngine.UI;
using OniBow.UI.ViewModels;

namespace OniBow.UI.Views
{
    /// <summary>
    /// [설명]: 설정 팝업 UI 뷰입니다. BGM/SFX 볼륨 및 음소거 상태를 시각화합니다.
    /// </summary>
    public class SettingsPopupView : MonoBehaviour
    {
        #region 에디터 설정
        [Header("BGM 설정")]
        [SerializeField] private Slider m_bgmSlider;
        [SerializeField] private Toggle m_bgmToggle;

        [Header("SFX 설정")]
        [SerializeField] private Slider m_sfxSlider;
        [SerializeField] private Toggle m_sfxToggle;

        [Header("팝업 제어")]
        [SerializeField, Tooltip("팝업을 활성화할 외부 버튼 (HUD 등)")]
        private Button m_settingsButton;
        [SerializeField, Tooltip("팝업의 실제 루트 오브젝트")]
        private GameObject m_popupRoot;
        [SerializeField, Tooltip("팝업 내부의 닫기 버튼")]
        private Button m_closeButton;
        #endregion

        #region 내부 변수
        private SettingsViewModel m_viewModel;
        #endregion

        #region 초기화
        public void Initialize(SettingsViewModel viewModel)
        {
            m_viewModel = viewModel;
            if (m_viewModel != null)
            {
                m_viewModel.OnBgmStateChanged += UpdateBgmUI;
                m_viewModel.OnSfxStateChanged += UpdateSfxUI;
            }

            BindUIEvents();
        }

        private void BindUIEvents()
        {
            if (m_bgmSlider != null) m_bgmSlider.onValueChanged.AddListener(OnBgmSliderChanged);
            if (m_bgmToggle != null) m_bgmToggle.onValueChanged.AddListener(OnBgmMuteChanged);
            if (m_sfxSlider != null) m_sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
            if (m_sfxToggle != null) m_sfxToggle.onValueChanged.AddListener(OnSfxMuteChanged);

            if (m_settingsButton != null) m_settingsButton.onClick.AddListener(TogglePopup);
            if (m_closeButton != null) m_closeButton.onClick.AddListener(ClosePopup);
        }
        #endregion

        #region 공개 메서드
        /// <summary>
        /// [설명]: 팝업의 활성 상태를 토글합니다.
        /// </summary>
        public void TogglePopup()
        {
            if (m_popupRoot == null) return;

            bool isActive = !m_popupRoot.activeSelf;
            m_popupRoot.SetActive(isActive);

            // 팝업이 열릴 때 게임 일시 정지 (선택 사항)
            Time.timeScale = isActive ? 0f : 1f;
        }

        /// <summary>
        /// [설명]: 팝업을 닫습니다.
        /// </summary>
        public void ClosePopup()
        {
            if (m_popupRoot != null)
            {
                m_popupRoot.SetActive(false);
                Time.timeScale = 1f;
            }
        }
        #endregion

        #region UI 이벤트 핸들러
        private void OnBgmSliderChanged(float val) => m_viewModel?.SetBgmVolume(val);
        private void OnBgmMuteChanged(bool mute) => m_viewModel?.SetBgmMute(mute);
        private void OnSfxSliderChanged(float val) => m_viewModel?.SetSfxVolume(val);
        private void OnSfxMuteChanged(bool mute) => m_viewModel?.SetSfxMute(mute);
        #endregion

        #region 내부 로직
        private void UpdateBgmUI(float vol, bool mute)
        {
            if (m_bgmSlider != null) m_bgmSlider.SetValueWithoutNotify(vol);
            if (m_bgmToggle != null) m_bgmToggle.SetIsOnWithoutNotify(mute);
        }

        private void UpdateSfxUI(float vol, bool mute)
        {
            if (m_sfxSlider != null) m_sfxSlider.SetValueWithoutNotify(vol);
            if (m_sfxToggle != null) m_sfxToggle.SetIsOnWithoutNotify(mute);
        }
        #endregion

        #region 유니티 생명주기
        private void OnDestroy()
        {
            if (m_viewModel != null)
            {
                m_viewModel.OnBgmStateChanged -= UpdateBgmUI;
                m_viewModel.OnSfxStateChanged -= UpdateSfxUI;
            }
        }
        #endregion
    }
}

