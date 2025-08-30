using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine.UI;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public Camera mainCamera;

    [Header("게임 진행 UI")]
    [SerializeField] private GameObject titleScreen; // 타이틀 화면 전체
    [SerializeField] private Button startButton; // 시작 버튼
    [SerializeField] private Image titleBackground; // 타이틀 배경 이미지 (페이드 아웃용)
    [SerializeField] private Image leftDoorImage; // 왼쪽 문 이미지
    [SerializeField] private Image rightDoorImage; // 오른쪽 문 이미지
    [SerializeField] private TMP_Text countdownText; // 카운트다운 텍스트
    [SerializeField] private GameObject gameOverScreen; // 게임 오버 화면
    [SerializeField] private GameObject gameClearScreen; // 게임 클리어 화면

    [Header("게임 오브젝트 참조")]
    [SerializeField] private GameObject playerObject; // 플레이어 오브젝트
    [SerializeField] private GameObject enemyObject; // 적 오브젝트

    [Header("사운드 설정")]
    [SerializeField] private string inGameBgmName = "InGameTheme"; // 게임 플레이 중 재생할 BGM 이름

    [Header("개발자 설정")]
    [SerializeField] private bool developerMode = false; // 개발자 모드 활성화 토글

    private PlayerControl _playerControl;
    private Enemy _enemy;

    public enum GameState
    {
        Title,
        Transitioning,
        Playing,
        GameOver,
        GameClear
    }

    private GameState _currentGameState = GameState.Title;

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

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        _playerControl = playerObject.GetComponent<PlayerControl>();
        _enemy = enemyObject.GetComponent<Enemy>();

        if (developerMode)
        {
            // 개발자 모드: 타이틀 시퀀스를 건너뛰고 바로 게임을 시작합니다.
            titleScreen?.SetActive(false);
            countdownText.gameObject.SetActive(false);
            SetGameActive(true);
            _currentGameState = GameState.Playing;
            Debug.Log("개발자 모드로 게임을 시작합니다.");
            SoundManager.Instance?.PlayBGM(inGameBgmName);
        }
        else
        {
            // 일반 모드: 타이틀 화면부터 시작합니다.
            SetGameActive(false);
            countdownText.gameObject.SetActive(false);
           // gameOverScreen?.SetActive(false);
          //  gameClearScreen?.SetActive(false);

            // 시작 버튼 이벤트 연결
            startButton?.onClick.AddListener(StartGame);

            // 시작 버튼이 부드럽게 페이드 인/아웃 되는 효과 추가
            Image startButtonImage = startButton?.GetComponent<Image>();
            if (startButtonImage != null)
            {
                // 반투명 상태까지 갔다가 다시 원래대로 돌아오는 것을 반복합니다.
                startButtonImage.DOFade(0.5f, 1.5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
            }
        }
    }

    private void OnEnable()
    {
        if (_playerControl != null)
        {
            _playerControl.OnPlayerDied += HandlePlayerDeath;
        }
        // Enemy.OnEnemyDestroyed는 static 이벤트이므로 OnEnable/OnDisable에서 구독/해지
        Enemy.OnEnemyDestroyed += HandleEnemyDeath;
    }

    private void OnDisable()
    {
        if (_playerControl != null)
        {
            _playerControl.OnPlayerDied -= HandlePlayerDeath;
        }
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
        if (mainCamera != null)
        {
            mainCamera.transform.DOShakePosition(duration, strength, vibrato, randomness);
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
        if (_currentGameState != GameState.Title) return;

        startButton.interactable = false; // 중복 클릭 방지

        // 버튼 클릭 시 페이드 효과를 멈추고, 버튼을 완전히 투명하게 만들어 사라지게 합니다.
        Image startButtonImage = startButton?.GetComponent<Image>();
        if (startButtonImage != null)
        {
            startButtonImage.DOKill(); // 진행 중인 페이드 루프 중지
            // 0.2초 동안 완전히 투명해지면서 사라집니다.
            startButtonImage.DOFade(0f, 0.2f);
        }

        _currentGameState = GameState.Transitioning;
        StartGameSequenceAsync().Forget();
    }

    private async UniTaskVoid StartGameSequenceAsync()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(1f));

        // --- 1. 문 열림 애니메이션 ---
        float doorOpenDuration = 1.0f; // 문 열리는 시간

        if (leftDoorImage != null)
        {
            // 현재 위치에서 자신의 너비만큼 왼쪽으로 상대적으로 이동하여 화면 밖으로 완전히 사라지게 합니다.
            // 이 방식은 앵커나 피봇 위치에 관계없이 안정적으로 동작합니다.
            leftDoorImage.rectTransform.DOAnchorPos(new Vector2(-leftDoorImage.rectTransform.rect.width, 0), doorOpenDuration)
                .SetRelative(true)
                .SetEase(Ease.OutQuad);
        }
        if (rightDoorImage != null)
        {
            // 현재 위치에서 자신의 너비만큼 오른쪽으로 상대적으로 이동합니다.
            rightDoorImage.rectTransform.DOAnchorPos(new Vector2(rightDoorImage.rectTransform.rect.width, 0), doorOpenDuration)
                .SetRelative(true)
                .SetEase(Ease.OutQuad);
        }

        // 문이 열릴 때까지 대기
        await UniTask.Delay(TimeSpan.FromSeconds(doorOpenDuration));

        // --- 2. 문 제거 ---
        if (leftDoorImage != null) leftDoorImage.gameObject.SetActive(false);
        if (rightDoorImage != null) rightDoorImage.gameObject.SetActive(false);

        // --- 3. 타이틀 배경 페이드 아웃 ---
        float backgroundFadeDuration = 1.0f;
        if (titleBackground != null)
        {
            titleBackground.DOFade(0, backgroundFadeDuration).SetEase(Ease.OutQuad);
        }
        
        // 배경 페이드 아웃 완료 대기
        await UniTask.Delay(TimeSpan.FromSeconds(backgroundFadeDuration));

        // 페이드 아웃 후 타이틀 화면 전체 비활성화
        if (titleScreen != null) titleScreen.SetActive(false);

        // --- 4. 카운트다운 시작 ---
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            for (int i = 5; i > 0; i--)
            {
                countdownText.text = i.ToString();

                // 카운트다운 텍스트 애니메이션: 커졌다가 작아지며 사라지는 효과
                countdownText.transform.localScale = Vector3.one * 2f; // 시작 시 크기를 키움
                countdownText.alpha = 1f; // 시작 시 완전히 보이게 함

                // 1초 동안 원래 크기로 돌아오면서 사라지는 애니메이션을 동시에 실행
                countdownText.transform.DOScale(1f, 1f).SetEase(Ease.OutCubic);
                countdownText.DOFade(0f, 1f).SetEase(Ease.InCubic);

                await UniTask.Delay(TimeSpan.FromSeconds(1.0f));
            }

            // "GO!" 텍스트 애니메이션
            countdownText.text = "Fight!";
            countdownText.alpha = 1f;
            countdownText.transform.localScale = Vector3.one;
            countdownText.transform.DOPunchScale(new Vector3(0.5f, 0.5f, 0.5f), 0.5f, 10, 1);

            await UniTask.Delay(TimeSpan.FromSeconds(1.0f));
            countdownText.gameObject.SetActive(false);
        }

        // --- 5. 플레이어와 적 활성화, 게임 상태 변경 ---
        SetGameActive(true);
        _currentGameState = GameState.Playing;
        Debug.Log("게임 시작!");
        SoundManager.Instance?.PlayBGM(inGameBgmName);
    }

    private void SetGameActive(bool isActive)
    {
        if (playerObject != null) playerObject.SetActive(isActive);
        if (enemyObject != null) enemyObject.SetActive(isActive);
    }

    private void HandlePlayerDeath()
    {
        if (_currentGameState != GameState.Playing) return;

        _currentGameState = GameState.GameOver;
        Debug.Log("게임 오버!");
        SoundManager.Instance?.StopBGM();
        OnGameOver?.Invoke();
     //   gameOverScreen?.SetActive(true);
    }

    private void HandleEnemyDeath(Enemy enemy)
    {
        if (_currentGameState != GameState.Playing) return;

        _currentGameState = GameState.GameClear;
        Debug.Log("게임 클리어!");
        SoundManager.Instance?.StopBGM();
        OnGameClear?.Invoke();
       // gameClearScreen?.SetActive(true);
    }
}
