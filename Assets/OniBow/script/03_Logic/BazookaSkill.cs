using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using OniBow.Data;
using OniBow.Projectiles;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 플레이어의 바주카 스킬을 담당하는 클래스입니다.
    /// </summary>
    public class BazookaSkill : ISkill
    {
        private readonly SkillConfigData m_config;

        public BazookaSkill(SkillConfigData config)
        {
            m_config = config;
        }

        public async UniTask ExecuteAsync(SkillContext context, CancellationToken token)
        {
            if (context.Owner == null || context.Target == null || context.Hand == null || m_config.BazookaPrefab == null) return;

            var player = context.Owner.GetComponent<PlayerControl>();
            if (player == null) return;

            player.SetSkillUsageState(true);

            GameObject bazookaInstance = UnityEngine.Object.Instantiate(m_config.BazookaPrefab, context.Hand.transform);
            bazookaInstance.transform.localRotation = Quaternion.Euler(0, 0, -90f);

            Animator bazookaAnimator = bazookaInstance.GetComponent<Animator>();
            if (bazookaAnimator == null)
            {
                UnityEngine.Object.Destroy(bazookaInstance);
                return;
            }

            Transform bazookaFirePoint = bazookaInstance.transform.Find("FirePoint");
            if (bazookaFirePoint == null)
            {
                UnityEngine.Object.Destroy(bazookaInstance);
                return;
            }

            float shoulderAnimDuration = 0.3f;
            float fireDelay = 0.2f;
            float totalFireAnimDuration = 1.2f;

            try
            {
                Vector2 directionToTarget = (context.Target.position - context.Hand.transform.position).normalized;
                Vector3 localDirection = context.Hand.transform.InverseTransformDirection(directionToTarget);
                float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

                float currentZ = -90f;
                DOTween.To(() => currentZ, z => { currentZ = z; bazookaInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, shoulderAnimDuration)
                    .SetEase(Ease.OutQuad);

                await UniTask.Delay(TimeSpan.FromSeconds(shoulderAnimDuration), cancellationToken: token);

                bazookaAnimator.enabled = true;
                bazookaAnimator.SetTrigger("Fire");

                await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

                if (m_config.ExplosiveArrowPrefab != null)
                {
                    Vector2 direction = (context.Target.position - bazookaFirePoint.position).normalized;
                    GameObject arrow = UnityEngine.Object.Instantiate(m_config.ExplosiveArrowPrefab, bazookaFirePoint.position, Quaternion.identity);
                    arrow.GetComponent<Roket>()?.Launch(direction);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(totalFireAnimDuration - fireDelay), cancellationToken: token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (bazookaInstance != null)
                {
                    UnityEngine.Object.Destroy(bazookaInstance);
                }
                player.SetSkillUsageState(false);
            }
        }
    }
}
