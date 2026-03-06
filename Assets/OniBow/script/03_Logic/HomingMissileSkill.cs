using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using OniBow.Data;
using OniBow.Managers;
using OniBow.Projectiles;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 플레이어의 유도 미사일 스킬을 담당하는 클래스입니다.
    /// </summary>
    public class HomingMissileSkill : ISkill
    {
        private readonly SkillConfigData m_config;

        public HomingMissileSkill(SkillConfigData config)
        {
            m_config = config;
        }

        public async UniTask ExecuteAsync(SkillContext context, CancellationToken token)
        {
            if (context.Owner == null || context.Target == null || context.Hand == null || m_config.HomingMissilePrefab == null) return;

            var player = context.Owner.GetComponent<PlayerControl>();
            if (player == null) return;

            player.SetSkillUsageState(true, false);

            try
            {
                for (int i = 0; i < m_config.HomingMissileCount; i++)
                {
                    if (token.IsCancellationRequested || context.Target == null) break;

                    Vector3 spawnPosition = context.Hand.transform.position + (Vector3)UnityEngine.Random.insideUnitCircle * 0.05f;

                    GameObject missileObject = ObjectPoolManager.Instance.Get(m_config.HomingMissilePrefab);
                    if (missileObject == null) continue;

                    missileObject.transform.SetPositionAndRotation(spawnPosition, context.Hand.transform.rotation);
                    missileObject.GetComponent<HomingMissile>()?.Launch(context.Target, context.Hand.transform);

                    await UniTask.Delay(TimeSpan.FromSeconds(m_config.HomingMissileSpawnInterval), cancellationToken: token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                player.SetSkillUsageState(false, false);
            }
        }
    }
}
