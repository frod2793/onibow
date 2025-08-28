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
    [SerializeField] private Button healSkillButton;
    [SerializeField] private Button rightMoveButton;
    [SerializeField] private Button leftMoveButton;

    private TextMeshProUGUI _skill1CooldownText;
    private TextMeshProUGUI _skill2CooldownText;
    private TextMeshProUGUI _skill3CooldownText;
    private TextMeshProUGUI _skill4CooldownText;
    private TextMeshProUGUI _healSkillCooldownText;

    private float _leftButtonClickTime = -1f;
    private float _rightButtonClickTime = -1f;
    private const float DOUBLE_CLICK_TIME = 0.3f;

    private void Start()
    {
        if (playerControl == null) Debug.LogError("PlayerControl 참조가 UIManager에 할당되지 않았습니다!", this);
        if (skillManager == null) Debug.LogError("SkillManager 참조가 UIManager에 할당되지 않았습니다!", this);
        if (enemy == null) Debug.LogError("Enemy 참조가 UIManager에 할당되지 않았습니다!", this);

        if (skill1Button != null) _skill1CooldownText = skill1Button.GetComponentInChildren<TextMeshProUGUI>();
        if (skill2Button != null) _skill2CooldownText = skill2Button.GetComponentInChildren<TextMeshProUGUI>();
        if (skill3Button != null) _skill3CooldownText = skill3Button.GetComponentInChildren<TextMeshProUGUI>();
        if (skill4Button != null) _skill4CooldownText = skill4Button.GetComponentInChildren<TextMeshProUGUI>();
        if (healSkillButton != null) _healSkillCooldownText = healSkillButton.GetComponentInChildren<TextMeshProUGUI>();

        BindButtonEvents();
        BindHealthBarEvents();
    }

    private void OnDestroy()
    {
        // 메모리 누수 방지를 위해 이벤트 구독 해지
        if (playerControl != null) {
            playerControl.OnHealthUpdated -= UpdatePlayerHpUI;
            playerControl.OnPlayerDied -= HandlePlayerDeath;
        }
        if (enemy != null) enemy.OnHpChanged -= UpdateEnemyHpUI;
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
            playerControl.OnPlayerDied += HandlePlayerDeath;
            playerControl.ForceUpdateHpUI(); // 초기값 설정
        }
        if (enemy != null)
        {
            enemy.OnHpChanged += UpdateEnemyHpUI;
            enemy.ForceUpdateHpUI(); // 초기값 설정
        }
    }

    private void HandlePlayerDeath()
    {
        // 모든 스킬 및 이동 버튼 비활성화
        skill1Button.interactable = false;
        skill2Button.interactable = false;
        skill3Button.interactable = false;
        skill4Button.interactable = false;
        healSkillButton.interactable = false;
        rightMoveButton.interactable = false;
        leftMoveButton.interactable = false;
    }

    private void UpdatePlayerHpUI(int currentHp, int tempHp, int maxHp)
    {
        if (PlayerHpbar != null)
        {
            PlayerHpbar.value = (float)currentHp / maxHp;
        }
        if (PlayerTempHpbar != null) PlayerTempHpbar.value = (float)tempHp / maxHp;
        if (PlayerHpText != null)
        {
            PlayerHpText.text = $"{currentHp}";
        }
    }

    private void UpdateEnemyHpUI(int currentHp, int tempHp, int maxHp)
    {
        if (EnemyHpBar != null)
        {
            EnemyHpBar.value = (float)currentHp / maxHp;
        }
        if (EnemyTempHpBar != null)
        {
            EnemyTempHpBar.value = (float)tempHp / maxHp;
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
            healSkillButton?.onClick.AddListener(skillManager.UseHealSkill);
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

        UpdateSingleSkillUI(_skill1CooldownText, skillManager.Skill1_RemainingCooldown);
        UpdateSingleSkillUI(_skill2CooldownText, skillManager.Skill2_RemainingCooldown);
        UpdateSingleSkillUI(_skill3CooldownText, skillManager.Skill3_RemainingCooldown);
        UpdateSingleSkillUI(_skill4CooldownText, skillManager.Skill4_RemainingCooldown);
        UpdateSingleSkillUI(_healSkillCooldownText, skillManager.HealSkill_RemainingCooldown);
    }

    private void UpdateSingleSkillUI(TextMeshProUGUI textElement, float remainingTime)
    {
        if (textElement == null) return;

        if (remainingTime > 0)
        {
            textElement.text = remainingTime.ToString("F1");
        }
        else
        {
            textElement.text = "";
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
