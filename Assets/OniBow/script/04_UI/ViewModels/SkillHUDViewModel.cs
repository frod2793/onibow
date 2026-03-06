using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OniBow.Managers;

namespace OniBow.UI.ViewModels
{
    public class SkillHUDViewModel
    {
        private SkillManager m_SkillManager;
        public event Action<int, float> OnCooldownChanged;

        public void Initialize(SkillManager skillManager)
        {
            m_SkillManager = skillManager;
        }

        public void UseSkill(int slot)
        {
            if (m_SkillManager == null) return;

            switch (slot)
            {
                case 1: m_SkillManager.UseSkill1(); break;
                case 2: m_SkillManager.UseSkill2(); break;
                case 3: m_SkillManager.UseSkill3(); break;
                case 4: m_SkillManager.UseSkill4(); break;
            }
        }

        public async UniTaskVoid MonitorCooldowns(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (m_SkillManager != null)
                {
                    OnCooldownChanged?.Invoke(1, m_SkillManager.Skill1_RemainingCooldown / m_SkillManager.PlayerSkill1_Cooldown);
                    OnCooldownChanged?.Invoke(2, m_SkillManager.Skill2_RemainingCooldown / m_SkillManager.PlayerSkill2_Cooldown);
                    OnCooldownChanged?.Invoke(3, m_SkillManager.Skill3_RemainingCooldown / m_SkillManager.PlayerSkill3_Cooldown);
                    OnCooldownChanged?.Invoke(4, m_SkillManager.Skill4_RemainingCooldown / m_SkillManager.PlayerSkill4_Cooldown);
                }
                await UniTask.Yield(ct);
            }
        }
    }
}

