using System;
using UnityEngine;
using OniBow.Data;

namespace OniBow.Logic
{
    #region 게임 상태 정의
    public enum GameState
    {
        Title,
        Transitioning,
        Playing,
        GameOver,
        GameClear
    }
    #endregion

    /// <summary>
    /// [설명]: 게임의 전반적인 상태 흐름을 관리하는 POCO 클래스입니다.
    /// MonoBehaviour에 의존하지 않으며 VContainer를 통해 주입됩니다.
    /// </summary>
    public class GameFlowController : IDisposable
    {
        #region 내부 필드
        private readonly GameSessionDTO m_sessionData;
        private GameState m_currentGameState = GameState.Title;
        #endregion

        #region 프로퍼티
        public GameState CurrentState => m_currentGameState;
        #endregion

        #region 이벤트
        public event Action<GameState> OnStateChanged;
        public event Action OnGameOver;
        public event Action OnGameClear;
        #endregion

        public GameFlowController(GameSessionDTO sessionData)
        {
            m_sessionData = sessionData;
        }

        #region 공개 메서드
        public void Initialize()
        {
            if (m_sessionData.DeveloperMode)
            {
                ChangeState(GameState.Playing);
            }
            else
            {
                ChangeState(GameState.Title);
            }
        }

        public void StartGameTransition()
        {
            if (m_currentGameState != GameState.Title) return;
            ChangeState(GameState.Transitioning);
        }

        public void EnterPlayingState()
        {
            ChangeState(GameState.Playing);
        }

        public void HandlePlayerDeath()
        {
            if (m_currentGameState != GameState.Playing) return;
            ChangeState(GameState.GameOver);
            OnGameOver?.Invoke();
        }

        public void HandleEnemyDeath()
        {
            if (m_currentGameState != GameState.Playing) return;
            ChangeState(GameState.GameClear);
            OnGameClear?.Invoke();
        }

        public void Dispose()
        {
            // 이벤트 해제 등 필요한 정리 작업 수행
        }
        #endregion

        #region 내부 로직
        private void ChangeState(GameState newState)
        {
            if (m_currentGameState == newState) return;
            m_currentGameState = newState;
            OnStateChanged?.Invoke(m_currentGameState);
            Debug.Log($"[GameFlowController] State Changed: {newState}");
        }
        #endregion
    }
}
