using System;
using UnityEngine;

namespace OniBow.Data
{
    /// <summary>
    /// [설명]: 스킬 시스템에서 사용하는 설정 데이터를 담는 ScriptableObject입니다.
    /// 구체적인 스킬 클래스들이 이 데이터를 참조하여 작동합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "OniBow/SkillConfig")]
    public class SkillConfigData : ScriptableObject
    {
        #region 플레이어 스킬 설정
        [Header("플레이어 스킬 쿨타임")]
        public float PlayerSkill1_Cooldown = 10f;
        public float PlayerSkill2_Cooldown = 15f;
        public float PlayerSkill3_Cooldown = 15f;
        public float PlayerSkill4_Cooldown = 20f;

        [Header("플레이어 스킬 프리팹")]
        public GameObject BarrierEffectPrefab;
        public float BarrierDuration = 5f;
        public GameObject HomingMissilePrefab;
        public GameObject ExplosiveArrowPrefab;
        public int HomingMissileCount = 5;
        public float HomingMissileSpawnInterval = 0.1f;

        [Header("플레이어 스킬 무기 프리팹")]
        public GameObject BazookaPrefab;
        public GameObject AK47Prefab;
        #endregion

        #region 적 스킬 설정
        [Header("적 스킬 설정")]
        public GameObject AkBulletPrefab;
        public int EnemySpray_Count = 5;
        public float EnemySpray_Interval = 0.15f;
        public float AkBulletSpeed = 30f;
        #endregion
    }
}
