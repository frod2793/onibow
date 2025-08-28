using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine.Pool;

/// <summary>
/// 게임 내 모든 시각 효과(VFX)를 중앙에서 관리하는 싱글턴 클래스입니다.
/// 이펙트의 생성과 자동 파괴를 담당합니다.
/// </summary>
public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("이펙트 프리팹")]
    [SerializeField] private GameObject rocketExplosionEffectPrefab;
    [SerializeField] private GameObject damageTextPrefab;

    [Header("오브젝트 풀 설정")]
    [SerializeField] private int damageTextPoolSize = 20;

    [Header("대미지 텍스트용 캔버스 ")] [SerializeField]
    private Canvas DamagedTextCanvas;

    [Header("데미지 텍스트 위치 오프셋")]
    [Tooltip("캐릭터 머리 위로 텍스트가 표시될 추가 높이입니다.")]
    [SerializeField] private float damageTextYOffset = 0.1f;
    [Tooltip("데미지 텍스트의 X축 위치에 적용될 랜덤 범위입니다. (예: 10이면 -10 ~ +10)")]
    [SerializeField] private float damageTextXRandomRange = 20f;

    [Header("데미지 텍스트 스타일")]
    [Tooltip("일반 데미지 텍스트의 스케일입니다.")]
    [SerializeField] private float normalDamageScale = 1f;
    [Tooltip("크리티컬 데미지 텍스트의 스케일입니다.")]
    [SerializeField] private float criticalDamageScale = 1.5f;
    [Tooltip("이 값 이상의 데미지를 크리티컬로 간주합니다.")]
    [SerializeField] private int criticalDamageThreshold = 50;
    [SerializeField] private Color normalDamageColor = Color.white;
    [SerializeField] private Color criticalDamageColor = Color.yellow;
    private IObjectPool<GameObject> _damageTextPool;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializePool();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializePool()
    {
        _damageTextPool = new ObjectPool<GameObject>(
            createFunc: () => {
                // 새로 생성될 때의 로직
                return Instantiate(damageTextPrefab);
            },
            actionOnGet: (obj) => {
                // 풀에서 가져올 때의 로직
                obj.transform.SetParent(DamagedTextCanvas.transform, false);
                obj.SetActive(true);
            },
            actionOnRelease: (obj) => {
                // 풀로 반환할 때의 로직
                obj.transform.SetParent(transform, false);
                obj.SetActive(false);
            },
            actionOnDestroy: (obj) => {
                // 풀이 파괴될 때의 로직
                Destroy(obj);
            },
            collectionCheck: true,  // 이미 풀에 반환된 오브젝트를 다시 반환하려 할 때 오류 발생
            defaultCapacity: damageTextPoolSize,
            maxSize: damageTextPoolSize * 2 // 풀의 최대 크기
        );

        // 오브젝트 풀을 미리 채워둡니다 (Pre-warming).
        var prewarmList = new List<GameObject>();
        for(int i = 0; i < damageTextPoolSize; i++)
        {
            prewarmList.Add(_damageTextPool.Get());
        }
        foreach(var item in prewarmList) _damageTextPool.Release(item);
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

    /// <summary>
    /// 지정된 위치에 데미지 텍스트를 표시합니다.
    /// </summary>
    /// <param name="target">데미지 텍스트가 표시될 대상 게임 오브젝트</param>
    /// <param name="damage">표시할 데미지 수치</param>
    public void ShowDamageText(GameObject target, int damage)
    {
        if (damageTextPrefab == null)
        {
            Debug.LogWarning("데미지 텍스트 프리팹이 EffectManager에 할당되지 않았습니다.");
            return;
        }
        if (DamagedTextCanvas == null)
        {
            Debug.LogWarning("데미지 텍스트용 캔버스가 EffectManager에 할당되지 않았습니다.");
            return;
        }

        if (target == null)
        {
            Debug.LogWarning("데미지 텍스트를 표시할 대상(target)이 null입니다.");
            return;
        }

        // 대상의 머리 위 위치를 계산합니다.
        Collider2D targetCollider = target.GetComponent<Collider2D>();
        if (targetCollider == null)
        {
            Debug.LogWarning($"데미지 텍스트를 표시할 대상 '{target.name}'에 Collider2D가 없습니다.");
            return;
        }
        Vector3 position = targetCollider.bounds.center + Vector3.up * (targetCollider.bounds.extents.y + damageTextYOffset);

        // Instantiate the damage text prefab as a child of the Canvas
        GameObject textInstance = _damageTextPool.Get();

        // Get the RectTransform of the instantiated text
        RectTransform textRectTransform = textInstance.GetComponent<RectTransform>();
        if (textRectTransform == null)
        {
            Debug.LogError($"데미지 텍스트 프리팹 '{damageTextPrefab.name}'에 RectTransform 컴포넌트가 없습니다. UI 요소여야 합니다.");
            Destroy(textInstance);
            return;
        }

        // Screen Space - Overlay 캔버스에서는 UI 요소의 position을 스크린 좌표로 직접 설정할 수 있습니다.
        // 이 방식이 RectTransformUtility를 사용하는 것보다 더 간단하고 직관적입니다.
        Vector2 screenPosition = Camera.main.WorldToScreenPoint(position);
        screenPosition.x += UnityEngine.Random.Range(-damageTextXRandomRange, damageTextXRandomRange);
        textRectTransform.position = screenPosition;

        // DamageText 스크립트를 찾아 데미지 값을 설정합니다.
        var damageTextComponent = textInstance.GetComponent<DamageText>();
        if (damageTextComponent != null)
        {
            damageTextComponent.SetAppearance(damage, normalDamageScale, criticalDamageScale, normalDamageColor, criticalDamageColor, criticalDamageThreshold);
            // 위치와 텍스트가 설정된 후 애니메이션을 재생합니다.
            damageTextComponent.PlayAnimation();
        }
    }

    /// <summary>
    /// 사용이 끝난 데미지 텍스트 오브젝트를 풀로 반환합니다.
    /// </summary>
    /// <param name="textObject">반환할 게임 오브젝트</param>
    public void ReturnDamageTextToPool(GameObject textObject)
    {
        _damageTextPool.Release(textObject);
    }
}