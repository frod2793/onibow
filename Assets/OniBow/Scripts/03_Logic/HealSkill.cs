using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using OniBow.Managers;

namespace OniBow.Logic
{
    /// <summary>
    /// [설명]: 플레이어의 체력 회복 스킬을 담당하는 클래스입니다.
    /// </summary>
    public class HealSkill : ISkill
    {
        public async UniTask ExecuteAsync(SkillContext context, CancellationToken token)
        {
            if (context.Owner == null) return;

            var player = context.Owner.GetComponent<PlayerControl>();
            if (player == null) return;

            player.SetSkillUsageState(true, false);
            GameObject healEffectInstance = null;

            try
            {
                player.HealWithTempHp();

                if (EffectManager.Instance != null && EffectManager.Instance.HealEffectPrefab != null)
                {
                    healEffectInstance = UnityEngine.Object.Instantiate(EffectManager.Instance.HealEffectPrefab, context.Owner.position, Quaternion.identity, context.Owner);
                    if (healEffectInstance != null)
                    {
                        healEffectInstance.SetActive(true);
                        var spum = context.Owner.GetComponentInChildren<SPUM_Prefabs>();
                        if (spum != null && spum._anim != null)
                        {
                            int maxPlayerSortingOrder = spum._anim.GetComponentsInChildren<SpriteRenderer>().Max(r => r.sortingOrder);
                            foreach (var effectRenderer in healEffectInstance.GetComponentsInChildren<Renderer>())
                            {
                                effectRenderer.sortingOrder = maxPlayerSortingOrder + 1;
                            }
                        }
                    }
                }

                ApplyPlayerTintAsync(player, new Color(0.7f, 1f, 0.7f, 1f), 3f, token).Forget();

                float healAmount = player.GetMaxHp() * 0.3f;
                await player.GradualHeal(healAmount, 3f, token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (healEffectInstance != null) UnityEngine.Object.Destroy(healEffectInstance);
                player.SetSkillUsageState(false, false);
            }
        }

        private async UniTaskVoid ApplyPlayerTintAsync(PlayerControl player, Color tintColor, float duration, CancellationToken token)
        {
            if (player == null) return;
            var spum = player.GetComponentInChildren<SPUM_Prefabs>();
            if (spum == null || spum._anim == null) return;

            var renderers = spum._anim.GetComponentsInChildren<SpriteRenderer>();
            var originalColors = new Dictionary<SpriteRenderer, Color>();

            try
            {
                foreach (var renderer in renderers)
                {
                    if (renderer != null)
                    {
                        originalColors[renderer] = renderer.color;
                        renderer.color = tintColor;
                    }
                }
                await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                foreach (var kvp in originalColors)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.color = kvp.Value;
                    }
                }
            }
        }
    }
}
