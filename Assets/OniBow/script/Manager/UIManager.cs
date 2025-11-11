using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.EventSystems;

/// <summary>
/// 스킬 UI 요소들을 그룹화하는 구조체입니다.
/// </summary>
[System.Serializable]
public struct SkillUIElements
{
    [Tooltip("스킬 버튼 참조")]
    public Button Button;
    [Tooltip("쿨타임 텍스트 (TMP_Text) 참조")]
    [SerializeField] public TMP_Text CooldownText;
    [Tooltip("쿨타임 마스크 이미지 참조")]
    [SerializeField] public Image CooldownMask;
}

/// <summary>
/// UI 요소들의 이벤트와 상태를 관리합니다.
/// PlayerControl, SkillManager 등 다른 핵심 컴포넌트와 상호작용합니다.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("참조 컴포넌트")]
    [Tooltip("플레이어 컨트롤러 참조. 인스펙터에서 할당합니다.")]
    [SerializeField] private PlayerControl m_playerControl;
    [Tooltip("스킬 매니저 참조. 인스펙터에서 할당합니다.")]
    [SerializeField] private SkillManager m_skillManager;
    [Tooltip("UI가 추적할 적. 인스펙터에서 할당합니다.")]
    [SerializeField] private Enemy m_enemy;
    
    [Header("체력 바 애니메이션 설정")]
    [Tooltip("예비 체력이 감소를 시작하기까지의 대기 시간 (초)")]
    [SerializeField] private float m_tempHpDecreaseDelay = 1.0f;
    [Tooltip("예비 체력이 1초당 감소하는 속도 (체력 바 기준, 0~1)")]
    [SerializeField] private float m_tempHpDecreaseSpeed = 0.5f;
    
    [Header("플레이어 체력 UI")]
    [SerializeField] private Slider m_playerHpBar;
    [SerializeField] private Slider m_playerTempHpBar;
    [SerializeField] private TMP_Text m_playerHpText;

    [Header("적 체력 UI")]
    [SerializeField] private Slider m_enemyHpBar;
    [SerializeField] private Slider m_enemyTempHpBar;
    [SerializeField] private TMP_Text m_enemyHpText;

    [Header("스킬 UI")]
    [SerializeField] private SkillUIElements[] m_skillUIElements = new SkillUIElements[4];

    [Header("이동 및 대쉬 UI")]
    [SerializeField] private Button m_rightMoveButton;
    [SerializeField] private Button m_leftMoveButton;

    [Header("설정 팝업 UI")]
    [SerializeField] private GameObject m_settingsPopup;
    [SerializeField] private Button m_openSettingsButton;
    [SerializeField] private Button m_closeSettingsButton;
    [SerializeField] private Slider m_bgmVolumeSlider;
    [SerializeField] private Slider m_sfxVolumeSlider;
    [SerializeField] private Toggle m_bgmMuteToggle;
    [SerializeField] private Toggle m_sfxMuteToggle;

    private Tween m_playerTempHpTween;
    private Tween m_enemyTempHpTween;

    private float m_leftButtonClickTime = -1f;
    private float m_rightButtonClickTime = -1f;
    private const float k_DoubleClickTime = 0.3f;

    private Coroutine m_cooldownUICoroutine;

    private void Start()
    {
        InitializeUIComponents();
        BindButtonEvents();
        InitializeSettingsPopup();
    }

    private void OnEnable()
    {
        BindEvents();
    }

    private void OnDisable()
    {
        // 메모리 누수 방지를 위해 이벤트 구독 해지
        if (m_playerControl != null)
        {
            m_playerControl.OnHealthUpdated -= UpdatePlayerHpUI;
        }
        if (m_enemy != null) m_enemy.OnHpChanged -= UpdateEnemyHpUI;

        // GameManager는 DontDestroyOnLoad일 수 있으므로, 인스턴스가 살아있는지 확인
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver -= HandleGameOver;
            GameManager.Instance.OnGameClear -= HandleGameClear;
        }

        if (m_cooldownUICoroutine != null)
        {
            StopCoroutine(m_cooldownUICoroutine);
        }
    }

    private void InitializeUIComponents()
    {
        if (m_playerControl == null) Debug.LogError("PlayerControl 참조가 UIManager에 할당되지 않았습니다!", this);
        if (m_skillManager == null) Debug.LogError("SkillManager 참조가 UIManager에 할당되지 않았습니다!", this);
        if (m_enemy == null) Debug.LogError("Enemy 참조가 UIManager에 할당되지 않았습니다!", this);

        // 인스펙터에서 할당된 스킬 UI 요소들의 유효성을 검사합니다.
        for (int i = 0; i < m_skillUIElements.Length; i++)
        {
            if (m_skillUIElements[i].Button == null)
                Debug.LogWarning($"Skill UI Element {i}: Button이 할당되지 않았습니다.", this);
            if (m_skillUIElements[i].CooldownText == null)
                Debug.LogWarning($"Skill UI Element {i}: CooldownText가 할당되지 않았습니다.", this);
            if (m_skillUIElements[i].CooldownMask == null)
                Debug.LogWarning($"Skill UI Element {i}: CooldownMask가 할당되지 않았습니다.", this);
        }
    }

    /// <summary>
    /// 각종 이벤트들을 구독합니다.
    /// </summary>
    private void BindEvents()
    {
        if (m_playerControl != null)
        {
            m_playerControl.OnHealthUpdated += UpdatePlayerHpUI;
            m_playerControl.ForceUpdateHpUI(); // 초기값 설정
        }
        if (m_enemy != null)
        {
            m_enemy.OnHpChanged += UpdateEnemyHpUI;
            m_enemy.ForceUpdateHpUI(); // 초기값 설정
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver += HandleGameOver;
            GameManager.Instance.OnGameClear += HandleGameClear;
        }

        if (m_cooldownUICoroutine == null)
        {
            m_cooldownUICoroutine = StartCoroutine(CooldownUICoroutine());
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
        foreach (var ui in m_skillUIElements)
        {
            if (ui.Button != null) ui.Button.interactable = isInteractable;
        }
        if (m_rightMoveButton != null) m_rightMoveButton.interactable = isInteractable;
        if (m_leftMoveButton != null) m_leftMoveButton.interactable = isInteractable;
        if (m_openSettingsButton != null) m_openSettingsButton.interactable = isInteractable;
    }

    private void UpdatePlayerHpUI(int currentHp, int tempHp, int maxHp)
    {
        // 진행 중인 예비 체력 감소 애니메이션이 있다면 중단합니다.
        m_playerTempHpTween?.Kill();

        float currentHpRatio = (float)currentHp / maxHp;
        float tempHpRatio = (float)tempHp / maxHp;

        if (m_playerHpBar != null)
        {
            // 메인 체력 바는 즉시 업데이트하여 피격감을 줍니다.
            m_playerHpBar.value = currentHpRatio;
        }

        if (m_playerTempHpBar != null)
        {
            // 예비 체력 바는 먼저 데미지 입기 전 체력으로 즉시 업데이트됩니다.
            m_playerTempHpBar.value = tempHpRatio;

            float diffRatio = tempHpRatio - currentHpRatio;
            if (diffRatio > 0.001f)
            {
                // 지속 시간 = 거리 / 속도. 일정한 속도로 체력 바가 줄어들도록 합니다.
                float duration = diffRatio / m_tempHpDecreaseSpeed;
                m_playerTempHpTween = m_playerTempHpBar.DOValue(currentHpRatio, duration)
                    .SetDelay(m_tempHpDecreaseDelay)
                    .SetEase(Ease.Linear);
            }
        }
        if (m_playerHpText != null)
        {
            m_playerHpText.text = $"{currentHp}";
        }

        // 체력 상태에 따라 화면 효과를 업데이트하도록 EffectManager에 알립니다.
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.UpdateLowHealthEffect(currentHp, maxHp);
        }
    }

    private void UpdateEnemyHpUI(int currentHp, int tempHp, int maxHp)
    {
        m_enemyTempHpTween?.Kill();

        float currentHpRatio = (float)currentHp / maxHp;
        float tempHpRatio = (float)tempHp / maxHp;

        if (m_enemyHpBar != null)
        {
            m_enemyHpBar.value = currentHpRatio;
        }
        if (m_enemyTempHpBar != null)
        {
            m_enemyTempHpBar.value = tempHpRatio;

            float diffRatio = tempHpRatio - currentHpRatio;
            if (diffRatio > 0.001f)
            {
                float duration = diffRatio / m_tempHpDecreaseSpeed;
                m_enemyTempHpTween = m_enemyTempHpBar.DOValue(currentHpRatio, duration)
                    .SetDelay(m_tempHpDecreaseDelay)
                    .SetEase(Ease.Linear);
            }
        }
        if (m_enemyHpText != null)
        {
            m_enemyHpText.text = $"{currentHp}";
        }
    }

    private void BindButtonEvents()
    {
        if (m_rightMoveButton != null && m_playerControl != null)
        {
            AddEventTrigger(m_rightMoveButton.gameObject, OnRightButtonDown, m_playerControl.StopMoving);
        }
        if (m_leftMoveButton != null && m_playerControl != null)
        {
            AddEventTrigger(m_leftMoveButton.gameObject, OnLeftButtonDown, m_playerControl.StopMoving);
        }

        if (m_skillManager != null)
        {
            m_skillUIElements[0].Button?.onClick.AddListener(() => { PlaySfx(SoundManager.Instance.GenericButtonClickSfx); m_skillManager.UseSkill1(); });
            m_skillUIElements[1].Button?.onClick.AddListener(() => { PlaySfx(SoundManager.Instance.GenericButtonClickSfx); m_skillManager.UseSkill2(); });
            m_skillUIElements[2].Button?.onClick.AddListener(() => { PlaySfx(SoundManager.Instance.GenericButtonClickSfx); m_skillManager.UseSkill3(); });
            m_skillUIElements[3].Button?.onClick.AddListener(() => { PlaySfx(SoundManager.Instance.GenericButtonClickSfx); m_skillManager.UseSkill4(); });
        }
    }

    private void OnLeftButtonDown()
    {
        PlaySfx(SoundManager.Instance.GenericButtonClickSfx);

        if (Time.time - m_leftButtonClickTime < k_DoubleClickTime)
        {
            m_playerControl.Dash(-1f);
            m_leftButtonClickTime = -1f; // 더블 클릭 후 타이머 초기화
        }
        else
        {
            m_playerControl.StartMoving(-1f);
            m_leftButtonClickTime = Time.time;
        }
    }

    private void OnRightButtonDown()
    {
        PlaySfx(SoundManager.Instance.GenericButtonClickSfx);

        if (Time.time - m_rightButtonClickTime < k_DoubleClickTime)
        {
            m_playerControl.Dash(1f);
            m_rightButtonClickTime = -1f; // 더블 클릭 후 타이머 초기화
        }
        else
        {
            m_playerControl.StartMoving(1f);
            m_rightButtonClickTime = Time.time;
        }
    }

    private IEnumerator CooldownUICoroutine()
    {
        while (true)
        {
            if (m_skillManager != null)
            {
                UpdateSingleSkillUI(m_skillUIElements[0], m_skillManager.Skill1_RemainingCooldown, m_skillManager.PlayerSkill1_Cooldown);
                UpdateSingleSkillUI(m_skillUIElements[1], m_skillManager.Skill2_RemainingCooldown, m_skillManager.PlayerSkill2_Cooldown);
                UpdateSingleSkillUI(m_skillUIElements[2], m_skillManager.Skill3_RemainingCooldown, m_skillManager.PlayerSkill3_Cooldown);
                UpdateSingleSkillUI(m_skillUIElements[3], m_skillManager.Skill4_RemainingCooldown, m_skillManager.PlayerSkill4_Cooldown);
            }
            yield return null;
        }
    }

    private void UpdateSingleSkillUI(SkillUIElements ui, float remainingTime, float totalCooldown)
    {
        bool isOnCooldown = remainingTime > 0;

        if (ui.CooldownText != null)
        {
            // UI 요소를 먼저 활성화/비활성화하고 값을 설정합니다.
            ui.CooldownText.gameObject.SetActive(isOnCooldown);
            if (isOnCooldown)
            {
                // 쿨타임 텍스트를 소수점 첫째 자리까지 표시합니다. (예: "5.3")
                ui.CooldownText.text = remainingTime.ToString("F1");
            }
        }

        if (ui.CooldownMask != null)
        {
            // UI 요소를 먼저 활성화/비활성화하고 값을 설정합니다.
            ui.CooldownMask.gameObject.SetActive(isOnCooldown);
            if (isOnCooldown && totalCooldown > 0)
            {
                ui.CooldownMask.fillAmount = remainingTime / totalCooldown;
            }
        }
    }

    #region 설정 팝업 관련 로직

    private void InitializeSettingsPopup()
    {
        if (m_settingsPopup != null) m_settingsPopup.SetActive(false);

        m_openSettingsButton?.onClick.AddListener(OpenSettingsPopup);
        m_closeSettingsButton?.onClick.AddListener(CloseSettingsPopup);

        if (SoundManager.Instance != null)
        {
            m_bgmVolumeSlider?.onValueChanged.AddListener((v) => { PlaySfx(SoundManager.Instance.SliderChangedSfx); SoundManager.Instance.SetBGMVolume(v); });
            m_sfxVolumeSlider?.onValueChanged.AddListener((v) => { PlaySfx(SoundManager.Instance.SliderChangedSfx); SoundManager.Instance.SetSFXVolume(v); });
            m_bgmMuteToggle?.onValueChanged.AddListener((v) => { PlaySfx(SoundManager.Instance.ToggleChangedSfx); SoundManager.Instance.SetBGMMute(v); });
            m_sfxMuteToggle?.onValueChanged.AddListener((v) => { PlaySfx(SoundManager.Instance.ToggleChangedSfx); SoundManager.Instance.SetSFXMute(v); });
        }
    }

    public void OpenSettingsPopup()
    {
        if (m_settingsPopup == null || SoundManager.Instance == null) return;

        m_settingsPopup.SetActive(true);
        Time.timeScale = 0f; // 게임 일시 정지
        PlaySfx(SoundManager.Instance.PopupOpenSfx);

        // 현재 설정 값으로 UI 초기화
        m_bgmVolumeSlider.value = SoundManager.Instance.GetBGMVolume();
        m_sfxVolumeSlider.value = SoundManager.Instance.GetSFXVolume();
        m_bgmMuteToggle.isOn = SoundManager.Instance.IsBGMMuted();
        m_sfxMuteToggle.isOn = SoundManager.Instance.IsSFXMuted();
    }

    public void CloseSettingsPopup()
    {
        if (m_settingsPopup == null) return;

        m_settingsPopup.SetActive(false);
        Time.timeScale = 1f; // 게임 재개
        PlaySfx(SoundManager.Instance.PopupCloseSfx);
    }

    #endregion

    #region 유틸리티 메서드
    private void AddEventTrigger(GameObject target, System.Action onPointerDown, System.Action onPointerUp)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
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

    #endregion

    private void PlaySfx(string sfxName)
    {
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(sfxName))
        {
            SoundManager.Instance.PlaySFX(sfxName);
        }
    }
}
