using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// 게임 내 모든 시각 효과(VFX)를 중앙에서 관리하는 싱글턴 클래스입니다.
/// 이펙트의 생성과 자동 파괴를 담당합니다.
/// </summary>
public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("이펙트 프리팹")]
    [SerializeField] private GameObject rocketExplosionEffectPrefab;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 지정된 위치에 이펙트를 재생하고, 재생이 끝나면 자동으로 파괴합니다.
    /// </summary>
    /// <param name="effectPrefab">재생할 이펙트 프리팹</param>
    /// <param name="position">이펙트가 생성될 월드 위치</param>
    /// <param name="rotation">이펙트의 초기 회전값</param>
    public void PlayEffect(GameObject effectPrefab, Vector3 position, Quaternion rotation, float scale = 1f, int? sortingOrder = null)
    {
        if (effectPrefab == null)
        {
            Debug.LogWarning("재생할 이펙트 프리팹이 null입니다.");
            return;
        }

        GameObject effectInstance = Instantiate(effectPrefab, position, rotation);
        
        // 스케일 설정
        effectInstance.transform.localScale = Vector3.one * scale;

        // 스프라이트 렌더러의 Sorting Order 설정
        if (sortingOrder.HasValue && effectInstance.TryGetComponent<SpriteRenderer>(out var renderer))
        {
            renderer.sortingOrder = sortingOrder.Value;
        }

        // 1. 파티클 시스템을 기반으로 자동 파괴 시간을 계산합니다.
        if (effectInstance.TryGetComponent<ParticleSystem>(out var ps))
        {
            // 파티클 시스템의 총 지속 시간이 지난 후에 게임 오브젝트를 파괴합니다.
            Destroy(effectInstance, ps.main.duration + ps.main.startLifetime.constantMax);
        }
        // 2. 파티클 시스템이 없다면, Animator를 확인하여 스프라이트 애니메이션 길이를 가져옵니다.
        else if (effectInstance.TryGetComponent<Animator>(out var animator))
        {
            // 애니메이터에 연결된 첫 번째 애니메이션 클립의 길이를 가져옵니다.
            // 이 방법은 이펙트가 단일 애니메이션 클립으로 구성되어 있다고 가정합니다.
            if (animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.animationClips.Length > 0)
            {
                float clipLength = animator.runtimeAnimatorController.animationClips[0].length;
                Destroy(effectInstance, clipLength);
            }
            else
            {
                // 클립을 찾을 수 없는 경우, 기본 시간으로 파괴합니다.
                Destroy(effectInstance, 1.5f);
            }
        }
        else
        {
            // 3. 둘 다 없는 경우, 기본 시간(1.5초) 후에 파괴합니다.
            Debug.LogWarning($"'{effectPrefab.name}'에 ParticleSystem 또는 Animator 컴포넌트가 없어 기본 시간 후 파괴됩니다.");
            Destroy(effectInstance, 1.5f);
        }
    }

    /// <summary>
    /// 지정된 위치에 로켓 폭발 이펙트를 재생합니다.
    /// </summary>
    /// <param name="position">폭발이 일어날 월드 위치</param>
    public void PlayExplosionEffect(Vector3 position)
    {
        PlayEffect(rocketExplosionEffectPrefab, position, Quaternion.identity, 1.5f, 30);
    }
}