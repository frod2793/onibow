using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System;
using OniBow.Managers;
using OniBow.Data;
using OniBow.Logic;
using VContainer;

namespace OniBow.Presentation
{
    /// <summary>
    /// [설명]: 게임 시작 시의 타이틀 표시, 문 열림 연출, 카운트다운을 담당하는 뷰 클래스입니다.
    /// </summary>
    public class GameSetupView : MonoBehaviour
    {
        #region 에디터 설정
        [Header("UI 참조")]
        [SerializeField] private GameObject m_titleScreen;
        [SerializeField] private Button m_startButton;
        [SerializeField] private Image m_titleBackground;
        [SerializeField] private Image m_leftDoorImage;
        [SerializeField] private Image m_rightDoorImage;
        [SerializeField] private TMP_Text m_countdownText;
        #endregion

        #region 내부 필드
        private GameFlowViewModel m_viewModel;
        private GameSessionDTO m_sessionData;
        #endregion

        [Inject]
        public void Construct(GameFlowViewModel viewModel, GameSessionDTO sessionData)
        {
            m_viewModel = viewModel;
            m_sessionData = sessionData;
            
            Bind();
        }

        #region 초기화 및 바인딩 로직
        private void Bind()
        {
            if (m_startButton != null)
            {
                m_startButton.onClick.AddListener(m_viewModel.OnStartButtonClick);
                
                // 버튼 이미지 페이드 효과 (기존 로직 유지)
                var childImages = m_startButton.GetComponentsInChildren<Image>();
                foreach (var image in childImages)
                {
                    if (image.gameObject != m_startButton.gameObject)
                    {
                        image.DOFade(0.5f, 1.5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
                    }
                }
            }

            m_viewModel.OnRequestTransition += () => StartTransitionAsync().Forget();
            m_viewModel.OnRequestCountdown += () => RunCountdownAsync().Forget();
            
            m_viewModel.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.Playing)
            {
                // [설명]: 개발 모드 등에서 즉시 Playing 상태로 진입할 경우를 대비하여 모든 연출 UI를 즉시 끕니다.
                if (m_titleScreen != null) m_titleScreen.SetActive(false);
                if (m_leftDoorImage != null) m_leftDoorImage.gameObject.SetActive(false);
                if (m_rightDoorImage != null) m_rightDoorImage.gameObject.SetActive(false);
                if (m_countdownText != null) m_countdownText.gameObject.SetActive(false);
                if (m_startButton != null) m_startButton.gameObject.SetActive(false);
          
                
                // 혹시 모를 레이캐스트 방해 방지를 위해 레이캐스트 타겟을 비활성화하고 뷰 자체를 끕니다.
                if (m_titleBackground != null) m_titleBackground.raycastTarget = false;
                if (m_titleBackground != null) m_titleBackground.gameObject.SetActive(false);
                
                
                gameObject.SetActive(false);
            }
        }
        #endregion

        #region 내부 로직 (연출)
        private async UniTaskVoid StartTransitionAsync()
        {
            if (m_startButton != null) m_startButton.interactable = false;
            FadeOutButton(m_startButton, 0.2f);

            await UniTask.Delay(TimeSpan.FromSeconds(m_sessionData.InitialDelay));
            
            var doorTask = AnimateDoorsAsync();
            var backgroundTask = FadeOutTitleAsync();

            await UniTask.WhenAll(doorTask, backgroundTask);
            
            m_viewModel.OnTransitionAnimationComplete();
        }

        private void FadeOutButton(Button button, float duration)
        {
            if (button == null) return;
            Image[] allImages = button.GetComponentsInChildren<Image>();
            foreach (var image in allImages)
            {
                image.DOKill();
                image.DOFade(0f, duration);
            }
        }

        private async UniTask AnimateDoorsAsync()
        {
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.DoorOpenSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.DoorOpenSfx);
            }

            if (m_leftDoorImage != null)
            {
                m_leftDoorImage.rectTransform.DOAnchorPos(new Vector2(-m_leftDoorImage.rectTransform.rect.width, 0), m_sessionData.DoorOpenDuration)
                    .SetRelative(true).SetEase(Ease.OutQuad);
            }
            if (m_rightDoorImage != null)
            {
                m_rightDoorImage.rectTransform.DOAnchorPos(new Vector2(m_rightDoorImage.rectTransform.rect.width, 0), m_sessionData.DoorOpenDuration)
                    .SetRelative(true).SetEase(Ease.OutQuad);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(m_sessionData.DoorOpenDuration));
            
            if (m_leftDoorImage != null) m_leftDoorImage.gameObject.SetActive(false);
            if (m_rightDoorImage != null) m_rightDoorImage.gameObject.SetActive(false);
            m_startButton.gameObject.SetActive(false);
        }

        private async UniTask FadeOutTitleAsync()
        {
            if (m_titleBackground != null)
            {
                await m_titleBackground.DOFade(0, m_sessionData.BackgroundFadeDuration).SetEase(Ease.OutQuad);
            }
        }

        private async UniTaskVoid RunCountdownAsync()
        {
            if (m_countdownText == null) return;

            m_countdownText.gameObject.SetActive(true);
            for (int i = m_sessionData.CountdownStart; i > 0; i--)
            {
                if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.CountdownTickSfx))
                {
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.CountdownTickSfx);
                }

                m_countdownText.text = i.ToString();
                m_countdownText.transform.localScale = Vector3.one * 2f;
                m_countdownText.alpha = 1f;

                m_countdownText.transform.DOScale(1f, 1f).SetEase(Ease.OutCubic);
                m_countdownText.DOFade(0f, 1f).SetEase(Ease.InCubic);

                await UniTask.Delay(TimeSpan.FromSeconds(1.0f));
            }

            m_countdownText.text = "Fight!";
            m_countdownText.alpha = 1f;
            m_countdownText.transform.localScale = Vector3.one;
            m_countdownText.transform.DOPunchScale(new Vector3(0.5f, 0.5f, 0.5f), 0.5f);

            await UniTask.Delay(TimeSpan.FromSeconds(1.0f));
            m_countdownText.gameObject.SetActive(false);
            
            m_viewModel.OnCountdownComplete();
            
            // 모든 연출이 끝나고 게임이 시작되면, 뷰 전체를 비활성화하여 UI 터치 블로킹을 원천 차단합니다.
            gameObject.SetActive(false);
        }
        #endregion
    }
}
