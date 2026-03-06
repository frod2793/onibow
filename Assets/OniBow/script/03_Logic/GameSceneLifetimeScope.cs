using VContainer;
using VContainer.Unity;
using UnityEngine;
using OniBow.Logic;
using OniBow.Presentation;
using OniBow.Data;
using OniBow.Managers;
using OniBow.UI.Views;
using OniBow.UI.ViewModels;

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
        [SerializeField] private CameraEffectView m_cameraEffectView;
        #endregion

        protected override void Awake()
        {
            // 씬 내에 이미 존재하는 모든 오브젝트(PlayerControl, Enemy 등)에 대해 자동 주입 활성화
            if (autoInjectGameObjects == null) autoInjectGameObjects = new System.Collections.Generic.List<GameObject>();
            
            var rootObjects = gameObject.scene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (!autoInjectGameObjects.Contains(root))
                    autoInjectGameObjects.Add(root);
            }

            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // 1. Data (DTO)
            if (m_sessionData == null) m_sessionData = new GameSessionDTO();
            builder.RegisterInstance(m_sessionData);

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
            
            // CameraEffectView 강제 검색 및 등록
            if (m_cameraEffectView == null) m_cameraEffectView = Object.FindAnyObjectByType<CameraEffectView>();
            if (m_cameraEffectView != null) builder.RegisterComponent(m_cameraEffectView);

            // 5. 씬 내 존재하는 주요 컴포넌트 명시적 등록 (주입 보장)
            builder.RegisterComponentInHierarchy<PlayerControl>();
            builder.RegisterComponentInHierarchy<Enemy>();
            builder.RegisterComponentInHierarchy<CameraEffectView>();
            builder.RegisterComponentInHierarchy<SkillHUDView>();
            builder.RegisterComponentInHierarchy<SkillConfiguration>();
            builder.RegisterComponentInHierarchy<PlayerHUDView>();
            builder.RegisterComponentInHierarchy<EnemyHUDView>();
            builder.RegisterComponentInHierarchy<SettingsPopupView>();

            // 6. Infrastructure (ObjectPoolManager 등)
            builder.RegisterComponentInHierarchy<ObjectPoolManager>();

            // 7. Entry Point (초기화 로직 실행)
            builder.RegisterEntryPoint<GameInitializer>();
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
