using UnityEngine;
using UnityEngine.UI;
using OniBow.Logic;
using OniBow.Presentation;
using VContainer;
using UnityEngine.SceneManagement;

namespace OniBow.Presentation
{
    /// <summary>
    /// [설명]: 게임 종료 및 결과 UI(팝업)를 담당하는 뷰 클래스입니다.
    /// </summary>
    public class GameResultView : MonoBehaviour
    {
        #region 에디터 설정
        [SerializeField] private GameObject m_endGamePopup;
        [SerializeField] private GameObject m_gameOverTitle;
        [SerializeField] private GameObject m_gameClearTitle;
        [SerializeField] private Button m_restartButton;
        #endregion

        #region 내부 필드
        private GameFlowViewModel m_viewModel;
        #endregion

        [Inject]
        public void Construct(GameFlowViewModel viewModel)
        {
            m_viewModel = viewModel;
            Bind();
        }

        #region 초기화 및 바인딩 로직
        private void Bind()
        {
            if (m_restartButton != null)
            {
                m_restartButton.onClick.AddListener(RestartGame);
            }

            m_viewModel.OnStateChanged += HandleStateChanged;
            
            if (m_endGamePopup != null) m_endGamePopup.SetActive(false);
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.GameOver || state == GameState.GameClear)
            {
                ShowResult(state);
            }
        }
        #endregion

        #region 내부 로직
        private void ShowResult(GameState state)
        {
            if (m_endGamePopup != null)
            {
                m_endGamePopup.SetActive(true);
                if (m_gameOverTitle != null) m_gameOverTitle.SetActive(state == GameState.GameOver);
                if (m_gameClearTitle != null) m_gameClearTitle.SetActive(state == GameState.GameClear);
            }
        }

        private void RestartGame()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        #endregion
    }
}
