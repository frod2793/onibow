using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine.UI;
using System;
using System.Linq;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public Camera MainCamera => m_mainCamera;

    [Header("카메라 참조")]
    [SerializeField] private Camera m_mainCamera;
    [Header("게임 진행 UI")]
    [SerializeField] private GameObject m_titleScreen;
    [SerializeField] private Button m_startButton;
    [SerializeField] private Image m_titleBackground;
    [SerializeField] private Image m_leftDoorImage;
    [SerializeField] private Image m_rightDoorImage;
    [SerializeField] private TMP_Text m_countdownText;

    [Header("게임 종료 UI")]
    [SerializeField] private GameObject m_endGamePopup;
    [SerializeField] private GameObject m_gameOverTitle;
    [SerializeField] private GameObject m_gameClearTitle;
    [SerializeField] private Button m_restartButton;

    [Header("게임 오브젝트 참조")]
    [SerializeField] private GameObject m_playerObject;
    [SerializeField] private GameObject m_enemyObject;

    [Header("개발자 설정")]
    [SerializeField] private bool m_developerMode = false;

    [Header("전환 효과 설정")]
    [Tooltip("시작 버튼 클릭 후 문이 열리기까지의 대기 시간")]
    [SerializeField] private float m_initialDelay = 1f;
    [Tooltip("문이 열리는 데 걸리는 시간")]
    [SerializeField] private float m_doorOpenDuration = 1.0f;
    [Tooltip("타이틀 배경이 사라지는 데 걸리는 시간")]
    [SerializeField] private float m_backgroundFadeDuration = 1.0f;
    [Tooltip("게임 시작 전 카운트다운 시작 숫자")]
    [SerializeField] private int m_countdownStart = 5;

    private PlayerControl m_playerControl;
    private Vector3 m_initialCameraPosition;

    public enum GameState
    {
        Title,
        Transitioning,
        Playing,
        GameOver,
        GameClear
    }

    private GameState m_currentGameState = GameState.Title;

    public event Action OnGameOver;
    public event Action OnGameClear;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        if (m_mainCamera == null)
        {
            m_mainCamera = Camera.main;
        }

        if (m_mainCamera != null)
        {
            m_initialCameraPosition = m_mainCamera.transform.position;
        }

        m_restartButton?.onClick.AddListener(RestartGame);

        InitializeReferences();

        if (m_developerMode)
        {
            SetupForDeveloperMode();
        }
        else
        {
            SetupForNormalMode();
        }
    }

    private void OnEnable()
    {
        if (m_playerControl != null) m_playerControl.OnPlayerDied += HandlePlayerDeath;
        Enemy.OnEnemyDestroyed += HandleEnemyDeath;
    }

    private void OnDisable()
    {
        if (m_playerControl != null) m_playerControl.OnPlayerDied -= HandlePlayerDeath;
        Enemy.OnEnemyDestroyed -= HandleEnemyDeath;
    }

    /// <summary>
    /// 메인 카메라를 흔드는 효과를 재생합니다.
    /// </summary>
    /// <param name="duration">흔들림 지속 시간</param>
    /// <param name="strength">흔들림 강도</param>
    /// <param name="vibrato">진동 횟수</param>
    /// <param name="randomness">무작위성</param>
    public void ShakeCamera(float duration, float strength, int vibrato = 10, float randomness = 90)
    {
        if (MainCamera != null)
        {
            // 이전에 진행 중이던 카메라 트윈을 모두 중단합니다.
            MainCamera.transform.DOKill(true);

            MainCamera.transform.DOShakePosition(duration, strength, vibrato, randomness)
                .SetLoops(1, LoopType.Restart) // 쉐이크가 한 번 완료되면 OnComplete를 호출하도록 설정
                .OnComplete(() =>
                {
                    // 쉐이크 후 카메라 위치가 초기 위치와 다를 경우, 부드럽게 원래 위치로 이동시킵니다.
                    if (MainCamera.transform.position != m_initialCameraPosition)
                        MainCamera.transform.DOMove(m_initialCameraPosition, 0.2f).SetEase(Ease.OutQuad);
                });
        }
        else
        {
            Debug.LogWarning("메인 카메라가 할당되지 않아 카메라 쉐이크를 실행할 수 없습니다.");
        }
    }

    /// <summary>
    /// 게임 시작 버튼 클릭 시 호출됩니다.
    /// </summary>
    private void StartGame()
    {
        if (m_currentGameState != GameState.Title) return;

        m_startButton.interactable = false; // 중복 클릭 방지

        FadeOutButton(m_startButton, 0.2f);

        m_currentGameState = GameState.Transitioning;
        StartGameSequenceAsync().Forget();
    }

    private void FadeOutButton(Button button, float duration)
    {
        if (button == null) return;

        // 버튼 자신을 포함한 모든 자식 Image 컴포넌트를 가져옵니다.
        Image[] allImages = button.GetComponentsInChildren<Image>();
        foreach (var image in allImages)
        {
            image.DOKill();
            image.DOFade(0f, duration);
        }
    }

    private void InitializeReferences()
    {
        // 참조가 할당되었는지 확인 후 컴포넌트를 가져와 NullReferenceException을 방지합니다.
        if (m_playerObject != null) m_playerControl = m_playerObject.GetComponent<PlayerControl>();
    }

    private void SetupForDeveloperMode()
    {
        m_titleScreen?.SetActive(false);
        m_countdownText.gameObject.SetActive(false);
        SetGameActive(true);
        m_currentGameState = GameState.Playing;
        Debug.Log("개발자 모드로 게임을 시작합니다.");

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.GameplayBgm))
        {
            SoundManager.Instance.PlayBGM(SoundManager.Instance.GameplayBgm, 0.5f);
        }
    }

    private void SetupForNormalMode()
    {
        SetGameActive(false);
        m_countdownText.gameObject.SetActive(false);
        m_endGamePopup?.SetActive(false);
        m_restartButton?.onClick.AddListener(RestartGame);

        m_startButton?.onClick.AddListener(StartGame);

        if (m_startButton != null)
        {
            var childImages = m_startButton.GetComponentsInChildren<Image>()
                                         .Where(img => img.gameObject != m_startButton.gameObject);

            foreach (var image in childImages)
            {
                image.DOFade(0.5f, 1.5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
            }
        }

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.TitleBgm))
        {
            SoundManager.Instance.PlayBGM(SoundManager.Instance.TitleBgm, 1.0f);
        }
    }

    private async UniTaskVoid StartGameSequenceAsync()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(m_initialDelay));

        await TransitionToGameAsync();

        await RunCountdownAsync();

        SetGameActive(true);
        m_currentGameState = GameState.Playing;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.GameplayBgm))
        {
            SoundManager.Instance.PlayBGM(SoundManager.Instance.GameplayBgm, 1.5f);
        }
        Debug.Log("게임 시작!");
    }

    private async UniTask TransitionToGameAsync()
    {
        // 문 열림 애니메이션과 배경 페이드 아웃을 동시에 시작
        var doorTask = AnimateDoorsAsync();
        var backgroundTask = FadeOutTitleAsync();

        // 두 애니메이션이 모두 끝날 때까지 대기
        await UniTask.WhenAll(doorTask, backgroundTask);

        if (m_titleScreen != null) m_titleScreen.SetActive(false);
    }

    private async UniTask AnimateDoorsAsync()
    {
        // 문 열림 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.DoorOpenSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.DoorOpenSfx);
        }

        if (m_leftDoorImage != null)
        {
            m_leftDoorImage.rectTransform.DOAnchorPos(new Vector2(-m_leftDoorImage.rectTransform.rect.width, 0), m_doorOpenDuration)
                .SetRelative(true)
                .SetEase(Ease.OutQuad);
        }
        if (m_rightDoorImage != null)
        {
            m_rightDoorImage.rectTransform.DOAnchorPos(new Vector2(m_rightDoorImage.rectTransform.rect.width, 0), m_doorOpenDuration)
                .SetRelative(true)
                .SetEase(Ease.OutQuad);
        }

        await UniTask.Delay(TimeSpan.FromSeconds(m_doorOpenDuration));

        if (m_leftDoorImage != null) m_leftDoorImage.gameObject.SetActive(false);
        if (m_rightDoorImage != null) m_rightDoorImage.gameObject.SetActive(false);
    }

    private async UniTask FadeOutTitleAsync()
    {
        if (m_titleBackground != null)
        {
            await m_titleBackground.DOFade(0, m_backgroundFadeDuration).SetEase(Ease.OutQuad);
        }
    }

    private async UniTask RunCountdownAsync()
    {
        if (m_countdownText != null)
        {
            m_countdownText.gameObject.SetActive(true);
            for (int i = m_countdownStart; i > 0; i--)
            {
                // 카운트다운 틱 사운드 재생
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
            m_countdownText.transform.DOPunchScale(new Vector3(0.5f, 0.5f, 0.5f), 0.5f, 10, 1);

            await UniTask.Delay(TimeSpan.FromSeconds(1.0f));
            m_countdownText.gameObject.SetActive(false);
        }
    }

    private void SetGameActive(bool isActive)
    {
        if (m_playerObject != null) m_playerObject.SetActive(isActive);
        if (m_enemyObject != null) m_enemyObject.SetActive(isActive);
    }

    private void HandlePlayerDeath()
    {
        EndGame(GameState.GameOver, "게임 오버!", OnGameOver);
    }

    private void HandleEnemyDeath(Enemy enemy)
    {
        EndGame(GameState.GameClear, "게임 클리어!", OnGameClear);
    }

    /// <summary>
    /// 게임 종료(게임 오버 또는 클리어) 시 공통 로직을 처리합니다.
    /// </summary>
    private void EndGame(GameState endState, string logMessage, Action endEvent)
    {
        if (m_currentGameState != GameState.Playing) return;

        m_currentGameState = endState;
        Debug.Log(logMessage);
        endEvent?.Invoke();

        if (m_endGamePopup != null)
        {
            m_endGamePopup.SetActive(true);
            // 상태에 따라 적절한 타이틀 이미지를 활성화/비활성화합니다.
            m_gameOverTitle?.SetActive(endState == GameState.GameOver);
            m_gameClearTitle?.SetActive(endState == GameState.GameClear);
        }

        // 게임 종료 시 BGM을 정지합니다.
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM(1.0f);
        }
    }

    /// <summary>
    /// 현재 씬을 다시 로드하여 게임을 재시작합니다.
    /// </summary>
    private void RestartGame()
    {
        // 현재 활성화된 씬을 다시 로드합니다.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
