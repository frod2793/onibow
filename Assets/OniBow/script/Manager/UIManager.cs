using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI 요소들의 이벤트와 상태를 관리합니다.
/// PlayerControl, SkillManager 등 다른 핵심 컴포넌트와 상호작용합니다.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("참조 컴포넌트")]
    [Tooltip("플레이어 컨트롤러 참조. 인스펙터에서 할당합니다.")]
    [SerializeField] private PlayerControl playerControl;
    [Tooltip("스킬 매니저 참조. 인스펙터에서 할당합니다.")]
    [SerializeField] private SkillManager skillManager;
    [Tooltip("UI가 추적할 적. 인스펙터에서 할당합니다.")]
    [SerializeField] private Enemy enemy;
    
    [Header("체력 바 애니메이션 설정")]
    [Tooltip("예비 체력이 감소를 시작하기까지의 대기 시간 (초)")]
    [SerializeField] private float tempHpDecreaseDelay = 1.0f;
    [Tooltip("예비 체력이 1초당 감소하는 속도 (체력 바 기준, 0~1)")]
    [SerializeField] private float tempHpDecreaseSpeed = 0.5f;
    
    [Header("UI 요소")]
    [SerializeField] private Slider PlayerHpbar;//본체력 
    [SerializeField] private Slider PlayerTempHpbar;// 예비 체력 
    [SerializeField] private TMP_Text PlayerHpText;
    [SerializeField] private Slider EnemyHpBar;
    [SerializeField] private Slider EnemyTempHpBar;
    [SerializeField] private TMP_Text EnemyHPText;
    [SerializeField] private Button skill1Button;
    [SerializeField] private Button skill2Button;
    [SerializeField] private Button skill3Button;
    [SerializeField] private Button skill4Button;
    [SerializeField] private Button rightMoveButton;
    [SerializeField] private Button leftMoveButton;

    private Tween _playerTempHpTween;
    private Tween _enemyTempHpTween;
    private TMP_Text _skill1CooldownText;
    private Image _skill1CooldownMask;
    private TMP_Text _skill2CooldownText;
    private Image _skill2CooldownMask;
    private TMP_Text _skill3CooldownText;
    private Image _skill3CooldownMask;
    private TMP_Text _skill4CooldownText;
    private Image _skill4CooldownMask;

    private float _leftButtonClickTime = -1f;
    private float _rightButtonClickTime = -1f;
    private const float DOUBLE_CLICK_TIME = 0.3f;

    private void Start()
    {
        if (playerControl == null) Debug.LogError("PlayerControl 참조가 UIManager에 할당되지 않았습니다!", this);
        if (skillManager == null) Debug.LogError("SkillManager 참조가 UIManager에 할당되지 않았습니다!", this);
        if (enemy == null) Debug.LogError("Enemy 참조가 UIManager에 할당되지 않았습니다!", this);

        if (skill1Button != null)
        {
            _skill1CooldownText = skill1Button.GetComponentInChildren<TMP_Text>();
            _skill1CooldownMask = skill1Button.transform.Find("CoolTimeMask")?.GetComponent<Image>();
        }
        if (skill2Button != null)
        {
            _skill2CooldownText = skill2Button.GetComponentInChildren<TMP_Text>();
            _skill2CooldownMask = skill2Button.transform.Find("CoolTimeMask")?.GetComponent<Image>();
        }
        if (skill3Button != null)
        {
            _skill3CooldownText = skill3Button.GetComponentInChildren<TMP_Text>();
            _skill3CooldownMask = skill3Button.transform.Find("CoolTimeMask")?.GetComponent<Image>();
        }
        if (skill4Button != null)
        {
            _skill4CooldownText = skill4Button.GetComponentInChildren<TMP_Text>();
            _skill4CooldownMask = skill4Button.transform.Find("CoolTimeMask")?.GetComponent<Image>();
        }

        BindButtonEvents();
        BindHealthBarEvents();
    }

    private void OnDestroy()
    {
        // 메모리 누수 방지를 위해 이벤트 구독 해지
        if (playerControl != null)
        {
            playerControl.OnHealthUpdated -= UpdatePlayerHpUI;
        }
        if (enemy != null) enemy.OnHpChanged -= UpdateEnemyHpUI;

        // GameManager는 DontDestroyOnLoad일 수 있으므로, 인스턴스가 살아있는지 확인
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver -= HandleGameOver;
            GameManager.Instance.OnGameClear -= HandleGameClear;
        }
    }

    private void Update()
    {
        UpdateCooldownUI();
    }

    /// <summary>
    /// 체력 바 이벤트를 구독합니다.
    /// </summary>
    private void BindHealthBarEvents()
    {
        if (playerControl != null)
        {
            playerControl.OnHealthUpdated += UpdatePlayerHpUI;
            playerControl.ForceUpdateHpUI(); // 초기값 설정
        }
        if (enemy != null)
        {
            enemy.OnHpChanged += UpdateEnemyHpUI;
            enemy.ForceUpdateHpUI(); // 초기값 설정
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver += HandleGameOver;
            GameManager.Instance.OnGameClear += HandleGameClear;
        }
    }

    private void HandleGameOver()
    {
        // 모든 스킬 및 이동 버튼 비활성화
        SetAllButtonsInteractable(false);
    }

    private void HandleGameClear()
    {
        // 게임 클리어 시에도 모든 버튼 비활성화
        SetAllButtonsInteractable(false);
    }

    private void SetAllButtonsInteractable(bool isInteractable)
    {
        skill1Button.interactable = isInteractable;
        skill2Button.interactable = isInteractable;
        skill3Button.interactable = isInteractable;
        skill4Button.interactable = isInteractable;
        rightMoveButton.interactable = isInteractable;
        leftMoveButton.interactable = isInteractable;
    }

    private void UpdatePlayerHpUI(int currentHp, int tempHp, int maxHp)
    {
        // 진행 중인 예비 체력 감소 애니메이션이 있다면 중단합니다.
        // 이는 플레이어가 짧은 간격으로 연속해서 피격당했을 때, 이전 애니메이션을 멈추고
        // 새로운 상태에서 애니메이션을 다시 시작하기 위함입니다.
        _playerTempHpTween?.Kill();

        float currentHpRatio = (float)currentHp / maxHp;
        float tempHpRatio = (float)tempHp / maxHp;

        if (PlayerHpbar != null)
        {
            // 메인 체력 바는 즉시 업데이트하여 피격감을 줍니다.
            PlayerHpbar.value = currentHpRatio;
        }

        if (PlayerTempHpbar != null)
        {
            // 예비 체력 바는 먼저 데미지 입기 전 체력으로 즉시 업데이트됩니다.
            PlayerTempHpbar.value = tempHpRatio;

            float diffRatio = tempHpRatio - currentHpRatio;
            if (diffRatio > 0.001f)
            {
                // 지속 시간 = 거리 / 속도. 일정한 속도로 체력 바가 줄어들도록 합니다.
                float duration = diffRatio / tempHpDecreaseSpeed;
                _playerTempHpTween = PlayerTempHpbar.DOValue(currentHpRatio, duration)
                    .SetDelay(tempHpDecreaseDelay)
                    .SetEase(Ease.Linear);
            }
        }
        if (PlayerHpText != null)
        {
            PlayerHpText.text = $"{currentHp}";
        }

        // 체력 상태에 따라 화면 효과를 업데이트하도록 EffectManager에 알립니다.
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.UpdateLowHealthEffect(currentHp, maxHp);
        }
    }

    private void UpdateEnemyHpUI(int currentHp, int tempHp, int maxHp)
    {
        _enemyTempHpTween?.Kill();

        float currentHpRatio = (float)currentHp / maxHp;
        float tempHpRatio = (float)tempHp / maxHp;

        if (EnemyHpBar != null)
        {
            EnemyHpBar.value = currentHpRatio;
        }
        if (EnemyTempHpBar != null)
        {
            EnemyTempHpBar.value = tempHpRatio;

            float diffRatio = tempHpRatio - currentHpRatio;
            if (diffRatio > 0.001f)
            {
                float duration = diffRatio / tempHpDecreaseSpeed;
                _enemyTempHpTween = EnemyTempHpBar.DOValue(currentHpRatio, duration)
                    .SetDelay(tempHpDecreaseDelay)
                    .SetEase(Ease.Linear);
            }
        }
        if (EnemyHPText != null)
        {
            EnemyHPText.text = $"{currentHp}";
        }
    }

    private void BindButtonEvents()
    {
        if (rightMoveButton != null && playerControl != null)
        {
            AddEventTrigger(rightMoveButton.gameObject, OnRightButtonDown, playerControl.StopMoving);
        }
        if (leftMoveButton != null && playerControl != null)
        {
            AddEventTrigger(leftMoveButton.gameObject, OnLeftButtonDown, playerControl.StopMoving);
        }

        if (skillManager != null)
        {
            skill1Button?.onClick.AddListener(skillManager.UseSkill1);
            skill2Button?.onClick.AddListener(skillManager.UseSkill2);
            skill3Button?.onClick.AddListener(skillManager.UseSkill3);
            skill4Button?.onClick.AddListener(skillManager.UseSkill4);
        }
    }

    private void OnLeftButtonDown()
    {
        if (Time.time - _leftButtonClickTime < DOUBLE_CLICK_TIME)
        {
            playerControl.Dash(-1f);
            _leftButtonClickTime = -1f; // 더블 클릭 후 타이머 초기화
        }
        else
        {
            playerControl.StartMoving(-1f);
            _leftButtonClickTime = Time.time;
        }
    }

    private void OnRightButtonDown()
    {
        if (Time.time - _rightButtonClickTime < DOUBLE_CLICK_TIME)
        {
            playerControl.Dash(1f);
            _rightButtonClickTime = -1f; // 더블 클릭 후 타이머 초기화
        }
        else
        {
            playerControl.StartMoving(1f);
            _rightButtonClickTime = Time.time;
        }
    }

    private void UpdateCooldownUI()
    {
        if (skillManager == null) return;

        UpdateSingleSkillUI(_skill1CooldownText, _skill1CooldownMask, skillManager.Skill1_RemainingCooldown, skillManager.PlayerSkill1_Cooldown);
        UpdateSingleSkillUI(_skill2CooldownText, _skill2CooldownMask, skillManager.Skill2_RemainingCooldown, skillManager.PlayerSkill2_Cooldown);
        UpdateSingleSkillUI(_skill3CooldownText, _skill3CooldownMask, skillManager.Skill3_RemainingCooldown, skillManager.PlayerSkill3_Cooldown);
        UpdateSingleSkillUI(_skill4CooldownText, _skill4CooldownMask, skillManager.Skill4_RemainingCooldown, skillManager.PlayerSkill4_Cooldown);
    }

    private void UpdateSingleSkillUI(TMP_Text textElement, Image maskImage, float remainingTime, float totalCooldown)
    {
        bool isOnCooldown = remainingTime > 0;

        // 쿨타임 텍스트 업데이트
        if (textElement != null)
        {
            textElement.gameObject.SetActive(isOnCooldown);
            if (isOnCooldown)
            {
                textElement.text = remainingTime.ToString("F1");
            }
        }

        // 쿨타임 마스크 이미지 업데이트
        if (maskImage != null)
        {
            maskImage.gameObject.SetActive(isOnCooldown);
            if (isOnCooldown && totalCooldown > 0)
            {
                maskImage.fillAmount = remainingTime / totalCooldown;
            }
        }

    }

    private void AddEventTrigger(GameObject target, System.Action onPointerDown, System.Action onPointerUp)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = target.AddComponent<EventTrigger>();
        }
        trigger.triggers.Clear();

        var pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDownEntry.callback.AddListener((data) => onPointerDown?.Invoke());
        trigger.triggers.Add(pointerDownEntry);

        var pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener((data) => onPointerUp?.Invoke());
        trigger.triggers.Add(pointerUpEntry);

        var pointerExitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        pointerExitEntry.callback.AddListener((data) => onPointerUp?.Invoke());
        trigger.triggers.Add(pointerExitEntry);
    }
}
