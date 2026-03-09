using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using OniBow.Logic;
using OniBow.Managers;

namespace OniBow.Presentation
{
    /// <summary>
    /// [설명]: 게임 상태 변화에 따라 BGM을 제어하는 POCO 프레젠터 클래스입니다.
    /// MonoBehaviour를 상속받지 않으며 VContainer 엔트리 포인트로 동작합니다.
    /// </summary>
    public class GameAudioPresenter : IStartable, IDisposable
    {
        #region 내부 필드
        private readonly GameFlowController m_gameFlow;
        private readonly SoundManager m_soundManager;
        #endregion

        #region 생성자
        public GameAudioPresenter(GameFlowController gameFlow, SoundManager soundManager)
        {
            m_gameFlow = gameFlow;
            m_soundManager = soundManager;
        }
        #endregion

        #region 인터페이스 구현
        public void Start()
        {
            if (m_gameFlow != null)
            {
                m_gameFlow.OnStateChanged += HandleStateChanged;
                // 초기 상태에 따른 BGM 재생
                HandleStateChanged(m_gameFlow.CurrentState);
            }
        }

        public void Dispose()
        {
            if (m_gameFlow != null)
            {
                m_gameFlow.OnStateChanged -= HandleStateChanged;
            }
        }
        #endregion

        #region 내부 로직
        private void HandleStateChanged(GameState state)
        {
            if (m_soundManager == null) return;

            switch (state)
            {
                case GameState.Title:
                case GameState.Transitioning:
                    m_soundManager.PlayBGM(m_soundManager.TitleBgm);
                    break;
                case GameState.Playing:
                    m_soundManager.PlayBGM(m_soundManager.GameplayBgm);
                    break;
                case GameState.GameOver:
                case GameState.GameClear:
                    m_soundManager.StopBGM(2.0f);
                    break;
            }
        }
        #endregion
    }
}
