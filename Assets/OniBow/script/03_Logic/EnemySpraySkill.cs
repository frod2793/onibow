using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using OniBow.Data;
using OniBow.Managers;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 적의 다발 사격 스킬을 담당하는 클래스입니다.
    /// </summary>
    public class EnemySpraySkill : ISkill
    {
        private readonly SkillConfigData m_config;

        public EnemySpraySkill(SkillConfigData config)
        {
            m_config = config;
        }

        public async UniTask ExecuteAsync(SkillContext context, CancellationToken token)
        {
            if (context.Owner == null || context.Target == null || context.FirePoint == null || m_config.AK47Prefab == null) return;

            GameObject akInstance = UnityEngine.Object.Instantiate(m_config.AK47Prefab, context.FirePoint);

            Transform akFirePoint = akInstance.transform.Find("FirePoint");
            if (akFirePoint == null)
            {
                UnityEngine.Object.Destroy(akInstance);
                return;
            }

            try
            {
                float aimDuration = 0.3f;
                Vector2 directionToTarget = (context.Target.position - context.FirePoint.position).normalized;
                Vector3 localDirection = context.FirePoint.InverseTransformDirection(directionToTarget);
                float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

                float currentAngle = 90f;

                if (context.FirePoint.lossyScale.x < 0)
                {
                    Vector3 akScale = akInstance.transform.localScale;
                    akScale.y *= -1;
                    akInstance.transform.localScale = akScale;
                    currentAngle *= -1;
                    finalLocalAngle *= -1;
                }

                akInstance.transform.localRotation = Quaternion.Euler(0, 0, currentAngle + 10f);

                DOTween.To(() => currentAngle, z => { currentAngle = z; akInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, aimDuration)
                    .SetEase(Ease.OutQuad);

                await UniTask.Delay(TimeSpan.FromSeconds(aimDuration), cancellationToken: token);

                for (int i = 0; i < m_config.EnemySpray_Count; i++)
                {
                    if (token.IsCancellationRequested || context.Target == null) break;

                    Vector2 direction = (context.Target.position - akFirePoint.position).normalized;

                    GameObject bullet = ObjectPoolManager.Instance.Get(m_config.AkBulletPrefab);
                    if (bullet == null) continue;

                    if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.AKFireSfx))
                    {
                        SoundManager.Instance.PlaySFX(SoundManager.Instance.AKFireSfx);
                    }

                    bullet.transform.SetPositionAndRotation(akFirePoint.position, Quaternion.identity);

                    Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
                    if (rb != null) rb.linearVelocity = direction * m_config.AkBulletSpeed;

                    await UniTask.Delay(TimeSpan.FromSeconds(m_config.EnemySpray_Interval), cancellationToken: token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (akInstance != null)
                {
                    UnityEngine.Object.Destroy(akInstance);
                }
            }
        }
    }
}
