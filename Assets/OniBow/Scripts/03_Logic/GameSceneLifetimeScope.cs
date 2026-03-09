using VContainer;
using VContainer.Unity;
using UnityEngine;
using OniBow.Logic;
using OniBow.Presentation;
using OniBow.Data;
using OniBow.Managers;
using OniBow.UI.Views;
using OniBow.UI.ViewModels;
using OniBow.UI.Interfaces;

namespace OniBow
{
    /// <summary>
    /// [설명]: 게임 씬의 모든 의존성을 조립하고 관리하는 수명 주기 범위 클래스입니다.
    /// </summary>
    public class GameSceneLifetimeScope : LifetimeScope
    {
        #region 에디터 설정
        [Header("데이터 설정")]
        [SerializeField] private GameSessionDTO m_sessionData;

        [Header("뷰 컴포넌트 (Scene)")]
        [SerializeField] private GameSetupView m_setupView;
        [SerializeField] private GameResultView m_resultView;
        [SerializeField] private PlayerHUDView m_playerHUDView;
        [SerializeField] private EnemyHUDView m_enemyHUDView;
        [SerializeField] private SkillHUDView m_skillHUDView;
        [SerializeField] private SettingsPopupView m_settingsPopupView;
        #endregion

        protected override void Awake()
        {
            // [최적화]: 씬 내 모든 루트 오브젝트에 대해 자동 주입을 활성화하여 의존성 주입 누락을 방지합니다.
            // 리스트 할당 및 중복 체크 로직을 효율적으로 구성합니다.
            var rootObjects = gameObject.scene.GetRootGameObjects();
            
            if (autoInjectGameObjects == null)
            {
                autoInjectGameObjects = new System.Collections.Generic.List<GameObject>(rootObjects);
            }
            else
            {
                foreach (var root in rootObjects)
                {
                    if (!autoInjectGameObjects.Contains(root))
                        autoInjectGameObjects.Add(root);
                }
            }

            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // 1. Data (DTO)
            if (m_sessionData == null) m_sessionData = new GameSessionDTO();
            builder.RegisterInstance(m_sessionData);

            // 1-1. SkillConfigData (ScriptableObject) 등록
            var skillConfig = Object.FindAnyObjectByType<SkillConfiguration>();
            if (skillConfig != null && skillConfig.ConfigData != null)
            {
                builder.RegisterInstance(skillConfig.ConfigData);
            }

            // 2. Logic (POCO)
            builder.Register<GameFlowController>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<BarrierSkill>(Lifetime.Singleton);
            builder.Register<HealSkill>(Lifetime.Singleton);
            builder.Register<HomingMissileSkill>(Lifetime.Singleton);
            builder.Register<BazookaSkill>(Lifetime.Singleton);
            builder.Register<EnemySpraySkill>(Lifetime.Singleton);
            builder.Register<PlayerSkillController>(Lifetime.Singleton);

            // 3. ViewModel
            builder.Register<GameFlowViewModel>(Lifetime.Singleton);
            builder.Register<SkillHUDViewModel>(Lifetime.Singleton);
            builder.Register<PlayerHUDViewModel>(Lifetime.Singleton);
            builder.Register<EnemyHUDViewModel>(Lifetime.Singleton);
            builder.Register<SettingsViewModel>(Lifetime.Singleton);

            // 4. Views (인스펙터에서 할당된 컴포넌트 등록)
            if (m_setupView != null) builder.RegisterComponent(m_setupView);
            if (m_resultView != null) builder.RegisterComponent(m_resultView);
            if (m_playerHUDView != null) builder.RegisterComponent(m_playerHUDView);
            if (m_enemyHUDView != null) builder.RegisterComponent(m_enemyHUDView);
            if (m_skillHUDView != null) builder.RegisterComponent(m_skillHUDView);
            if (m_settingsPopupView != null) builder.RegisterComponent(m_settingsPopupView);
            
            // CameraEffectView 등록 (기존의 수동 할당 방식 대신 Hierarchy에서 자동 검색 및 등록)
            builder.RegisterComponentInHierarchy<CameraEffectView>();

            // 5. 씬 내 존재하는 주요 컴포넌트 명시적 등록 (주입 보장)
            builder.RegisterComponentInHierarchy<PlayerControl>();
            builder.RegisterComponentInHierarchy<PlayerHealth>().As<IHealthProvider>().AsSelf();
            builder.RegisterComponentInHierarchy<PlayerMovement>();
            builder.RegisterComponentInHierarchy<PlayerCombat>();
            builder.RegisterComponentInHierarchy<Enemy>();
            builder.RegisterComponentInHierarchy<EnemyHealth>().As<IHealthProvider>().AsSelf();
            builder.RegisterComponentInHierarchy<EnemyMovement>();
            builder.RegisterComponentInHierarchy<EnemyCombat>();
            builder.RegisterComponentInHierarchy<SkillConfiguration>();

            // 6. Infrastructure (ObjectPoolManager 등)
            builder.RegisterComponentInHierarchy<ObjectPoolManager>();
            builder.RegisterComponentInHierarchy<SoundManager>();

            // 7. Entry Point (초기화 로직 실행)
            builder.RegisterEntryPoint<GameInitializer>();
            builder.RegisterEntryPoint<GameAudioPresenter>();
        }
    }

    /// <summary>
    /// [설명]: 게임 시작 시 초기화 로직을 트리거하는 VContainer 엔트리 포인트입니다.
    /// </summary>
    public class GameInitializer : IStartable
    {
        private readonly GameFlowController m_controller;

        public GameInitializer(GameFlowController controller)
        {
            m_controller = controller;
        }

        public void Start()
        {
            m_controller.Initialize();
        }
    }
}
