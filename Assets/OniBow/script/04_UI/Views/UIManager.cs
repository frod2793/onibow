using UnityEngine;
using OniBow.UI.ViewModels;
using OniBow.UI.Views;
using OniBow.Managers;

namespace OniBow.UI.Views
{
    /// <summary>
    /// [설명]: UI 시스템의 중앙 엔트리 포인트입니다.
    /// 모든 View와 ViewModel을 생성하고 필요한 도메인 모델(Manager)을 주입하는 DI 컨테이너 역할을 합니다.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region 에디터 설정
        [Header("UI Views")]
        [SerializeField] private PlayerHUDView m_playerHUDView;
        [SerializeField] private EnemyHUDView m_enemyHUDView;
        [SerializeField] private SkillHUDView m_skillHUDView;
        [SerializeField] private SettingsPopupView m_settingsPopupView;
        #endregion

        #region 내부 필드
        private PlayerHUDViewModel m_playerHUDViewModel;
        private EnemyHUDViewModel m_enemyHUDViewModel;
        private SkillHUDViewModel m_skillHUDViewModel;
        private SettingsViewModel m_settingsViewModel;
        #endregion

        #region 유니티 생명주기
        private void Start()
        {
            InitializeAllUI();
        }
        #endregion

        #region 초기화 로직
        /// <summary>
        /// [설명]: 모든 UI 컴포넌트를 초기화하고 의존성을 주입합니다.
        /// </summary>
        private void InitializeAllUI()
        {
            // 1. Player HUD 초기화
            var player = GameObject.FindGameObjectWithTag("Player")?.GetComponent<PlayerControl>();
            if (player != null)
            {
                m_playerHUDViewModel = new PlayerHUDViewModel();
                m_playerHUDViewModel.Initialize(player);
                if (m_playerHUDView != null) m_playerHUDView.Initialize(m_playerHUDViewModel);
            }

            // 2. Enemy HUD 초기화 (필요 시 타겟팅 시스템과 연동 가능)
            // 현재는 씬의 첫 번째 적을 예시로 바인딩하거나, 비워둘 수 있습니다.
            var enemy = GameObject.FindObjectOfType<Enemy>();
            if (enemy != null)
            {
                m_enemyHUDViewModel = new EnemyHUDViewModel();
                m_enemyHUDViewModel.Initialize(enemy);
                if (m_enemyHUDView != null) m_enemyHUDView.Initialize(m_enemyHUDViewModel);
            }

            // 3. Skill HUD 초기화
            if (SkillManager.Instance != null)
            {
                m_skillHUDViewModel = new SkillHUDViewModel();
                m_skillHUDViewModel.Initialize(SkillManager.Instance);
                if (m_skillHUDView != null) m_skillHUDView.Initialize(m_skillHUDViewModel);
            }

            // 4. Settings Popup 초기화
            if (SoundManager.Instance != null)
            {
                m_settingsViewModel = new SettingsViewModel();
                m_settingsViewModel.Initialize(SoundManager.Instance);
                if (m_settingsPopupView != null) m_settingsPopupView.Initialize(m_settingsViewModel);
            }
        }
        #endregion
    }
}

