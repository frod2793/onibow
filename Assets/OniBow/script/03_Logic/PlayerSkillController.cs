using System;
using System.Collections.Generic;
using UnityEngine;
using OniBow.Data;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 플레이어의 스킬 사용 및 쿨타임을 관리하는 컨트롤러 클래스입니다.
    /// </summary>
    public class PlayerSkillController : IDisposable
    {
        private readonly SkillConfigData m_config;
        private readonly ISkill m_skill1;
        private readonly ISkill m_skill2;
        private readonly ISkill m_skill3;
        private readonly ISkill m_skill4;

        private float m_lastSkill1_Time = -999f;
        private float m_lastSkill2_Time = -999f;
        private float m_lastSkill3_Time = -999f;
        private float m_lastSkill4_Time = -999f;

        #region 프로퍼티 (쿨타임 정보)
        public float Skill1_RemainingCooldown => Mathf.Max(0f, m_lastSkill1_Time + m_config.PlayerSkill1_Cooldown - Time.time);
        public float Skill2_RemainingCooldown => Mathf.Max(0f, m_lastSkill2_Time + m_config.PlayerSkill2_Cooldown - Time.time);
        public float Skill3_RemainingCooldown => Mathf.Max(0f, m_lastSkill3_Time + m_config.PlayerSkill3_Cooldown - Time.time);
        public float Skill4_RemainingCooldown => Mathf.Max(0f, m_lastSkill4_Time + m_config.PlayerSkill4_Cooldown - Time.time);

        public float Skill1_TotalCooldown => m_config.PlayerSkill1_Cooldown;
        public float Skill2_TotalCooldown => m_config.PlayerSkill2_Cooldown;
        public float Skill3_TotalCooldown => m_config.PlayerSkill3_Cooldown;
        public float Skill4_TotalCooldown => m_config.PlayerSkill4_Cooldown;
        #endregion

        public PlayerSkillController(
            SkillConfigData config,
            BarrierSkill skill1,
            HealSkill skill2,
            HomingMissileSkill skill3,
            BazookaSkill skill4)
        {
            m_config = config;
            m_skill1 = skill1;
            m_skill2 = skill2;
            m_skill3 = skill3;
            m_skill4 = skill4;
        }

        public void UseSkill(int slot, SkillContext context, System.Threading.CancellationToken token)
        {
            switch (slot)
            {
                case 1:
                    if (Skill1_RemainingCooldown > 0) return;
                    m_lastSkill1_Time = Time.time;
                    m_skill1.ExecuteAsync(context, token).Forget();
                    break;
                case 2:
                    if (Skill2_RemainingCooldown > 0) return;
                    m_lastSkill2_Time = Time.time;
                    m_skill2.ExecuteAsync(context, token).Forget();
                    break;
                case 3:
                    if (Skill3_RemainingCooldown > 0) return;
                    m_lastSkill3_Time = Time.time;
                    m_skill3.ExecuteAsync(context, token).Forget();
                    break;
                case 4:
                    if (Skill4_RemainingCooldown > 0) return;
                    m_lastSkill4_Time = Time.time;
                    m_skill4.ExecuteAsync(context, token).Forget();
                    break;
            }
        }

        public void Dispose()
        {
            // 필요한 경우 정리 로직 추가
        }
    }
}
