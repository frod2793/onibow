using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.Pool;
using UnityEngine.UI;

/// <summary>
/// 데미지 텍스트의 스타일 설정을 그룹화하는 구조체입니다.
/// </summary>
[System.Serializable]
public struct DamageTextStyleSettings
{
    [Tooltip("일반 데미지 텍스트의 스케일입니다.")]
    public float normalScale;
    [Tooltip("크리티컬 데미지 텍스트의 스케일입니다.")]
    public float criticalScale;
    [Tooltip("이 값 이상의 데미지를 크리티컬로 간주합니다.")]
    public int criticalThreshold;
    public Color normalColor;
    public Color criticalColor;
}

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
    [SerializeField] private GameObject HomingMissileExplosionEffectPrefab;
    [SerializeField] private GameObject akBulletHitEffectPrefab;
    public GameObject HealEffectPrefab;
    
    [Header("체력 경고 효과 (UI 이미지)")]
    [Tooltip("플레이어 체력이 낮을 때 표시될 화면 가장자리 효과 이미지")]
    [SerializeField] private Image lowHealthVignette;
    [Tooltip("체력 경고 효과가 발동될 체력 비율 (예: 0.3 = 30%)")]
    [Range(0, 1)]
    [SerializeField] private float lowHealthThreshold = 0.3f;
    [Tooltip("비네트 효과가 깜빡일 때의 최대 강도")]
    [Range(0, 1)]
    [SerializeField] private float vignetteMaxIntensity = 0.4f;
    [Tooltip("비네트 효과가 한 번 깜빡이는 데 걸리는 시간 (초)")]
    [SerializeField] private float vignettePulseDuration = 1.0f;
    
    [Header("오브젝트 풀 설정")]
    [SerializeField] private int damageTextPoolSize = 20;

    [Header("대미지 텍스트용 캔버스 ")] [SerializeField]
    private Canvas DamagedTextCanvas;

    [Header("데미지 텍스트 위치 오프셋")]
    [Tooltip("캐릭터 머리 위로 텍스트가 표시될 추가 높이입니다.")]
    [SerializeField] private float damageTextYOffset = 0.1f;
    [Tooltip("데미지 텍스트의 X축 위치에 적용될 랜덤 범위입니다. (예: 10이면 -10 ~ +10)")]
    [SerializeField] private float damageTextXRandomRange = 20f;

    [Header("데미지 텍스트 스타일 설정")]
    [SerializeField] private DamageTextStyleSettings damageTextStyle = new DamageTextStyleSettings {
        normalScale = 1f,
        criticalScale = 1.5f,
        criticalThreshold = 50,
        normalColor = Color.white,
        criticalColor = Color.yellow
    };

    #if UNITY_EDITOR
    private bool _isTestingLowHealthEffect = false;
    #endif

    private Tween _vignetteTween;
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

    private void Start()
    {
        if (lowHealthVignette != null)
        {
            lowHealthVignette.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("EffectManager: Low Health Vignette 이미지가 할당되지 않았습니다. 체력 경고 효과가 동작하지 않습니다.", this);
        }
    }

    private void InitializePool()
    {
        _damageTextPool = new ObjectPool<GameObject>(
            createFunc: () => {
                return Instantiate(damageTextPrefab);
            },
            actionOnGet: (obj) => {
                obj.transform.SetParent(DamagedTextCanvas.transform, false);
                obj.SetActive(true);
            },
            actionOnRelease: (obj) => {
                obj.transform.SetParent(transform, false);
                obj.SetActive(false);
            },
            actionOnDestroy: (obj) => {
                Destroy(obj);
            },
            collectionCheck: true, defaultCapacity: damageTextPoolSize, maxSize: damageTextPoolSize * 2
        );

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
        
        effectInstance.transform.localScale = Vector3.one * scale;

        if (sortingOrder.HasValue && effectInstance.TryGetComponent<SpriteRenderer>(out var renderer))
        {
            renderer.sortingOrder = sortingOrder.Value;
        }

        if (effectInstance.GetComponentInChildren<ParticleSystem>() is { } ps)
        {
            Destroy(effectInstance, ps.main.duration + ps.main.startLifetime.constantMax);
        }
        else if (effectInstance.GetComponentInChildren<Animator>() is { } animator)
        {
            if (animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.animationClips.Length > 0)
            {
                float clipLength = animator.runtimeAnimatorController.animationClips[0].length;
                Destroy(effectInstance, clipLength);
            }
            else
            {
                Destroy(effectInstance, 1.5f);
            }
        }
        else
        {
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
    /// 지정된 위치에 호밍 미사일 폭발 이펙트를 재생합니다.
    /// </summary>
    /// <param name="position">폭발이 일어날 월드 위치</param>
    public void PlayHomingMissileExplosion(Vector3 position)
    {
        PlayEffect(HomingMissileExplosionEffectPrefab, position, Quaternion.identity);
    }

    /// <summary>
    /// 지정된 위치에 AK 총알 착탄 이펙트를 재생합니다.
    /// </summary>
    /// <param name="position">이펙트가 생성될 월드 위치</param>
    public void PlayBulletHitEffect(Vector3 position)
    {
        PlayEffect(akBulletHitEffectPrefab, position, Quaternion.Euler(0, 0, -90));
    }

    /// <summary>
    /// 지정된 위치에 힐 이펙트를 재생합니다.
    /// </summary>
    /// <param name="position">이펙트가 생성될 월드 위치</param>
    public void PlayHealEffect(Vector3 position)
    {
        PlayEffect(HealEffectPrefab, position, Quaternion.identity);
    }

    /// <summary>
    /// 플레이어의 현재 체력에 따라 화면 가장자리 경고 효과를 업데이트합니다.
    /// </summary>
    public void UpdateLowHealthEffect(int currentHp, int maxHp)
    {
        if (lowHealthVignette == null) return;

        float healthRatio = (float)currentHp / maxHp;

        if (healthRatio <= lowHealthThreshold && currentHp > 0)
        {
            if (_vignetteTween == null || !_vignetteTween.IsActive())
            {
                lowHealthVignette.gameObject.SetActive(true);
             
                _vignetteTween = lowHealthVignette.DOFade(vignetteMaxIntensity, vignettePulseDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo);
            }
        }
        else
        {
            if (_vignetteTween != null)
            {
                _vignetteTween.Kill();
                _vignetteTween = null;
            }
            lowHealthVignette.DOFade(0, 0.5f).OnComplete(() => {
                if (lowHealthVignette != null) lowHealthVignette.gameObject.SetActive(false);
            });
        }
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

        Collider2D targetCollider = target.GetComponent<Collider2D>();
        if (targetCollider == null)
        {
            Debug.LogWarning($"데미지 텍스트를 표시할 대상 '{target.name}'에 Collider2D가 없습니다.");
            return;
        }
        Vector3 position = targetCollider.bounds.center + Vector3.up * (targetCollider.bounds.extents.y + damageTextYOffset);

        GameObject textInstance = _damageTextPool.Get();

        RectTransform textRectTransform = textInstance.GetComponent<RectTransform>();
        if (textRectTransform == null)
        {
            Debug.LogError($"데미지 텍스트 프리팹 '{damageTextPrefab.name}'에 RectTransform 컴포넌트가 없습니다. UI 요소여야 합니다.");
            Destroy(textInstance);
            return;
        }

        Vector2 screenPosition = Camera.main.WorldToScreenPoint(position);
        screenPosition.x += UnityEngine.Random.Range(-damageTextXRandomRange, damageTextXRandomRange);
        textRectTransform.position = screenPosition;

        var damageTextComponent = textInstance.GetComponent<DamageText>();
        if (damageTextComponent != null)
        {
            damageTextComponent.SetAppearance(damage, 
                damageTextStyle.normalScale, 
                damageTextStyle.criticalScale, 
                damageTextStyle.normalColor, 
                damageTextStyle.criticalColor, 
                damageTextStyle.criticalThreshold);
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
    
    #if UNITY_EDITOR
    /// <summary>
    /// [테스트용] 인스펙터의 버튼을 통해 체력 경고 효과를 토글합니다.
    /// </summary>
    public void ToggleTestLowHealthEffect()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("체력 경고 효과 테스트는 Play 모드에서만 가능합니다.");
            return;
        }

        _isTestingLowHealthEffect = !_isTestingLowHealthEffect;

        if (_isTestingLowHealthEffect)
        {
            Debug.Log("<color=cyan>[TEST]</color> 체력 경고 효과 시작");
            UpdateLowHealthEffect(1, 100);
        }
        else
        {
            Debug.Log("<color=cyan>[TEST]</color> 체력 경고 효과 중지");
            UpdateLowHealthEffect(100, 100);
        }
    }
    #endif
}