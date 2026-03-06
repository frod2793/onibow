using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using OniBow.UI.ViewModels;
using TMPro;

namespace OniBow.UI.Views
{
    public class EnemyHUDView : MonoBehaviour
    {
        [SerializeField] private Slider m_enemyHpBar;
        [SerializeField] private Slider m_enemyTempHpBar;
        [SerializeField] private TMP_Text m_enemyHpText;

        private EnemyHUDViewModel m_ViewModel;

        public void Initialize(EnemyHUDViewModel viewModel)
        {
            m_ViewModel = viewModel;
            if (m_ViewModel != null)
            {
                m_ViewModel.OnHpRatioChanged += OnHpRatioChanged;
                m_ViewModel.OnHpTextChanged += OnHpTextChanged;
            }
        }

        private void OnDestroy()
        {
            if (m_ViewModel != null)
            {
                m_ViewModel.OnHpRatioChanged -= OnHpRatioChanged;
                m_ViewModel.OnHpTextChanged -= OnHpTextChanged;
            }

            // 안전하게 모든 트윈 제거
            if (m_enemyHpBar != null) m_enemyHpBar.DOKill();
            if (m_enemyTempHpBar != null) m_enemyTempHpBar.DOKill();
        }

        private void OnHpRatioChanged(float ratioA, float ratioB)
        {
            SetSliderSafe(m_enemyHpBar, ratioA);
            SetSliderSafe(m_enemyTempHpBar, ratioB);
        }

        private void OnHpTextChanged(string text)
        {
            if (m_enemyHpText != null)
            {
                m_enemyHpText.text = text;
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
