using UnityEngine;
using UnityEngine.UI;
using OniBow.UI.ViewModels;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OniBow.UI.Views
{
    /// <summary>
    /// [설명]: 플레이어의 스킬 슬롯과 쿨타임을 표시하는 뷰입니다.
    /// </summary>
    public class SkillHUDView : MonoBehaviour
    {
        #region 에디터 설정
        [SerializeField] private Button[] m_skillButtons;
        [SerializeField] private Image[] m_cooldownImages;
        #endregion

        #region 내부 변수
        private SkillHUDViewModel m_viewModel;
        private CancellationTokenSource m_cts;
        #endregion

        #region 초기화
        public void Initialize(SkillHUDViewModel viewModel)
        {
            m_viewModel = viewModel;
            if (m_viewModel != null)
            {
                m_viewModel.OnCooldownChanged += UpdateCooldown;

                for (int i = 0; i < m_skillButtons.Length; i++)
                {
                    int index = i + 1; // Slot 1~4
                    m_skillButtons[i].onClick.AddListener(() => OnSkillButtonClicked(index));
                }

                m_cts = new CancellationTokenSource();
                m_viewModel.MonitorCooldowns(m_cts.Token).Forget();
            }
        }
        #endregion

        #region UI 이벤트 핸들러
        private void OnSkillButtonClicked(int slot)
        {
            m_viewModel?.UseSkill(slot);
        }
        #endregion

        #region 내부 로직
        private void UpdateCooldown(int slot, float ratio)
        {
            int index = slot - 1;
            if (index >= 0 && index < m_cooldownImages.Length)
            {
                if (m_cooldownImages[index] != null)
                {
                    m_cooldownImages[index].fillAmount = ratio;
                }
            }
        }
        #endregion

        #region 유니티 생명주기
        private void OnDestroy()
        {
            if (m_viewModel != null)
            {
                m_viewModel.OnCooldownChanged -= UpdateCooldown;
            }
            m_cts?.Cancel();
            m_cts?.Dispose();
        }
        #endregion
    }
}

