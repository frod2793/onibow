using System;
using OniBow.Logic;
using OniBow.Data;
using Cysharp.Threading.Tasks;

namespace OniBow.Presentation
{
    /// <summary>
    /// [설명]: 게임 흐름 상태를 View에 매핑하고 명령을 처리하는 뷰모델입니다.
    /// </summary>
    public class GameFlowViewModel
    {
        #region 내부 필드
        private readonly GameFlowController m_controller;
        private readonly GameSessionDTO m_sessionData;
        #endregion

        #region 이벤트 (View 바인딩용)
        public event Action<GameState> OnStateChanged;
        public event Action OnRequestTransition;
        public event Action OnRequestCountdown;
        public event Action OnRequestGameStart;
        #endregion

        public GameFlowViewModel(GameFlowController controller, GameSessionDTO sessionData)
        {
            m_controller = controller;
            m_sessionData = sessionData;

            m_controller.OnStateChanged += HandleStateChanged;
        }

        #region 공개 메서드 (View -> ViewModel)
        public void OnStartButtonClick()
        {
            m_controller.StartGameTransition();
        }

        public void OnTransitionAnimationComplete()
        {
            // 애니메이션 완료 후 다음 단계 이동 요청
            OnRequestCountdown?.Invoke();
        }

        public void OnCountdownComplete()
        {
            m_controller.EnterPlayingState();
            OnRequestGameStart?.Invoke();
        }
        #endregion

        #region 내부 로직
        private void HandleStateChanged(GameState state)
        {
            OnStateChanged?.Invoke(state);

            if (state == GameState.Transitioning)
            {
                OnRequestTransition?.Invoke();
            }
        }
        #endregion
    }
}
