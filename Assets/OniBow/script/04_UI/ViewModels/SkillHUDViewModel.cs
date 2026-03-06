using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OniBow.Managers;

using OniBow.Logic;

namespace OniBow.UI.ViewModels
{
    public class SkillHUDViewModel
    {
        private readonly PlayerSkillController m_skillController;
        private readonly SkillConfiguration m_skillConfig;
        private readonly GameFlowController m_gameFlowController;
        public event Action<int, float> OnCooldownChanged;

        public SkillHUDViewModel(
            PlayerSkillController skillController, 
            SkillConfiguration skillConfig,
            GameFlowController gameFlowController)
        {
            m_skillController = skillController;
            m_skillConfig = skillConfig;
            m_gameFlowController = gameFlowController;
        }

        public void UseSkill(int slot)
        {
            if (m_gameFlowController != null && m_gameFlowController.CurrentState != GameState.Playing) return;
            if (m_skillController == null || m_skillConfig == null) return;

            var context = new SkillContext(
                m_skillConfig.Player.transform, 
                m_skillConfig.Player.FindNearestEnemy()?.transform,
                m_skillConfig.PlayerFirePoint,
                m_skillConfig.PlayerHand);

            m_skillController.UseSkill(slot, context, CancellationToken.None); // 실제로는 개별 CTS 관리 가능
        }

        public async UniTaskVoid MonitorCooldowns(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (m_skillController != null)
                {
                    OnCooldownChanged?.Invoke(1, m_skillController.Skill1_RemainingCooldown / m_skillController.Skill1_TotalCooldown);
                    OnCooldownChanged?.Invoke(2, m_skillController.Skill2_RemainingCooldown / m_skillController.Skill2_TotalCooldown);
                    OnCooldownChanged?.Invoke(3, m_skillController.Skill3_RemainingCooldown / m_skillController.Skill3_TotalCooldown);
                    OnCooldownChanged?.Invoke(4, m_skillController.Skill4_RemainingCooldown / m_skillController.Skill4_TotalCooldown);
                }
                await UniTask.Yield(ct);
            }
        }
    }
}

