using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System.Threading;
using OniBow; // PlayerControl, Enemy
using OniBow.Projectiles; // HomingMissile, Roket

namespace OniBow.Managers
{
    /// <summary>
    /// [주의]: 이 클래스는 더 이상 사용되지 않습니다 (Legacy).
    /// 모든 스킬 관련 로직은 ISkill 인터페이스와 PlayerSkillController로 이전되었습니다.
    /// VContainer (GameSceneLifetimeScope)를 통해 의존성이 관리됩니다.
    /// </summary>
    public class SkillManager : MonoBehaviour
    {
        public static SkillManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
            
            Debug.Log("[Legacy] SkillManager가 감지되었습니다. 신규 스킬 시스템(PlayerSkillController)을 사용하세요.");
        }

        // 구 버전 UI/코드와의 호환성을 위한 비어있는 메서드들 (필요 시 유지)
        public float Skill1_RemainingCooldown => 0;
        public float Skill2_RemainingCooldown => 0;
        public float Skill3_RemainingCooldown => 0;
        public float Skill4_RemainingCooldown => 0;
        public float PlayerSkill1_Cooldown => 1;
        public float PlayerSkill2_Cooldown => 1;
        public float PlayerSkill3_Cooldown => 1;
        public float PlayerSkill4_Cooldown => 1;

        public void UseSkill1() { }
        public void UseSkill2() { }
        public void UseSkill3() { }
        public void UseSkill4() { }
        public async UniTask ExecuteEnemyMultiShot(Transform handPoint, Transform target) { await UniTask.CompletedTask; }
    }
}
