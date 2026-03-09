using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using OniBow.Data;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 플레이어의 배리어(무적) 스킬을 담당하는 클래스입니다.
    /// </summary>
    public class BarrierSkill : ISkill
    {
        private readonly SkillConfigData m_config;

        public BarrierSkill(SkillConfigData config)
        {
            m_config = config;
        }

        public async UniTask ExecuteAsync(SkillContext context, CancellationToken token)
        {
            if (context.Owner == null || m_config.BarrierEffectPrefab == null) return;

            var player = context.Owner.GetComponent<PlayerControl>();
            if (player == null) return;

            player.SetSkillUsageState(true, false);
            player.SetInvulnerable(true);

            GameObject barrierInstance = null;
            Animator barrierAnimator = null;

            try
            {
                barrierInstance = UnityEngine.Object.Instantiate(m_config.BarrierEffectPrefab, context.Owner.position, Quaternion.identity, context.Owner);
                if (barrierInstance == null) return;

                barrierInstance.SetActive(true);
                barrierAnimator = barrierInstance.GetComponentInChildren<Animator>();

                if (barrierAnimator != null)
                {
                    barrierAnimator.SetTrigger("Spawn");
                    barrierAnimator.SetTrigger("Stay");
                }

                await UniTask.Delay(TimeSpan.FromSeconds(m_config.BarrierDuration), cancellationToken: token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (barrierInstance != null)
                {
                    await PopAndDestroyBarrierAsync(barrierInstance, barrierAnimator);
                }

                player.SetInvulnerable(false);
                player.SetSkillUsageState(false, false);
            }
        }

        private async UniTask PopAndDestroyBarrierAsync(GameObject barrierInstance, Animator animator)
        {
            if (animator == null || barrierInstance == null)
            {
                if (barrierInstance != null) UnityEngine.Object.Destroy(barrierInstance);
                return;
            }

            var token = barrierInstance.GetCancellationTokenOnDestroy();
            animator.SetTrigger("Pop");

            try
            {
                await UniTask.WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Pop"), cancellationToken: token);
                await UniTask.WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f, cancellationToken: token);
            }
            catch (OperationCanceledException) { return; }

            if (barrierInstance != null)
            {
                UnityEngine.Object.Destroy(barrierInstance);
            }
        }
    }
}
