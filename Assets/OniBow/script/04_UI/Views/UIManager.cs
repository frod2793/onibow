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
            // VContainer (GameSceneLifetimeScope)를 통한 자동 주입으로 전환되었습니다.
            // 이 클래스는 하위 호환성 또는 컴포넌트 참조 유지용으로만 남겨둡니다.
            /*
            // 1. Player HUD 초기화
            ...
            */
        }
        #endregion
    }
}

