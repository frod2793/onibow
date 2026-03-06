
using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using OniBow.UI.ViewModels;
using TMPro;

// Unity-specific 뷰
namespace OniBow.UI.Views
{
    public class PlayerHUDView : MonoBehaviour
    {
        [SerializeField] private Slider m_playerHpBar;
        [SerializeField] private Slider m_playerTempHpBar;
        [SerializeField] private TMP_Text m_playerHpText;

        [Header("이동 제어")]
        [SerializeField] private Button m_leftMoveButton;
        [SerializeField] private Button m_rightMoveButton;

        private PlayerHUDViewModel m_ViewModel;

        // 들어오는 ViewModel 바인딩
        public void Initialize(PlayerHUDViewModel viewModel)
        {
            m_ViewModel = viewModel;
            if (m_ViewModel != null)
            {
                m_ViewModel.OnHpRatioChanged += OnHpRatioChanged;
                m_ViewModel.OnHpTextChanged += OnHpTextChanged;
            }

            BindMovementButtons();
        }

        private void BindMovementButtons()
        {
            AddMoveEvent(m_leftMoveButton, -1f);
            AddMoveEvent(m_rightMoveButton, 1f);
        }

        private void AddMoveEvent(Button btn, float dir)
        {
            if (btn == null) return;

            var trigger = btn.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            // Pointer Down
            var downEntry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
            downEntry.callback.AddListener((data) => m_ViewModel?.MoveBegin(dir));
            trigger.triggers.Add(downEntry);

            // Pointer Up
            var upEntry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
            upEntry.callback.AddListener((data) => m_ViewModel?.MoveEnd());
            trigger.triggers.Add(upEntry);
        }

        private void OnDestroy()
        {
            if (m_ViewModel != null)
            {
                m_ViewModel.OnHpRatioChanged -= OnHpRatioChanged;
                m_ViewModel.OnHpTextChanged -= OnHpTextChanged;
            }
            // 안전하게 모든 트윈 제거
            if (m_playerHpBar != null) m_playerHpBar.DOKill();
            if (m_playerTempHpBar != null) m_playerTempHpBar.DOKill();
        }

        private void OnHpRatioChanged(float ratioA, float ratioB)
        {
            SetSliderSafe(m_playerHpBar, ratioA);
            SetSliderSafe(m_playerTempHpBar, ratioB);
        }

        private void OnHpTextChanged(string text)
        {
            if (m_playerHpText != null)
            {
                m_playerHpText.text = text;
            }
        }

        private void SetSliderSafe(Slider slider, float value)
        {
#if DOTWEEN || UNITY_EDITOR
            if (slider != null) slider.DOValue(Mathf.Clamp01(value), 0.25f);
#else
            if (slider != null)
            {
                slider.value = Mathf.Clamp01(value);
            }
#endif
        }
    }
}
