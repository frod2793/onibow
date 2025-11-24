using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine.Serialization; // 인스펙터 맵핑 유지를 위해 필수

/// <summary>
/// 대쉬 등 빠른 움직임 시 잔상 효과를 생성하고 관리합니다.
/// 성능을 위해 렌더러 정보를 캐싱하고, UniTask를 사용하여 비동기 루프를 제어합니다.
/// </summary>
public class AfterimageEffect : MonoBehaviour
{
    #region Inspector Fields (FormerlySerializedAs 적용)
    
    [Header("참조")]
    [Tooltip("잔상 효과를 위해 풀링할 프리팹")]
    [FormerlySerializedAs("afterimagePrefab")] // 기존 이름과 맵핑
    [SerializeField] private GameObject m_afterimagePrefab;

    [Tooltip("잔상이 복제할 원본 시각적 오브젝트의 루트 Transform입니다. (예: SPUM_Prefabs가 있는 오브젝트)")]
    [FormerlySerializedAs("_sourceVisualsRoot")]
    [SerializeField] private Transform m_sourceVisualsRoot;

    [Header("효과 설정")]
    [FormerlySerializedAs("_afterimageColor")]
    [SerializeField] private Color m_afterimageColor = new Color(0.5f, 0.8f, 1f, 1f);

    [FormerlySerializedAs("_spawnInterval")]
    [SerializeField] private float m_spawnInterval = 0.05f; // 잔상 생성 간격 (초)

    [FormerlySerializedAs("_fadeDuration")]
    [SerializeField] private float m_fadeDuration = 0.5f;   // 잔상이 사라지는 시간 (초)

    [Tooltip("이 값을 true로 설정하면, 잔상의 Sorting Order를 아래 값으로 덮어씁니다.")]
    [FormerlySerializedAs("_overrideSortingOrder")]
    [SerializeField] private bool m_overrideSortingOrder = false;

    [FormerlySerializedAs("_sortingOrderOverrideValue")]
    [SerializeField] private int m_sortingOrderOverrideValue = 20;

    #endregion

    #region Private Fields
    
    private CancellationTokenSource m_effectCts;
    
    // 런타임 메모리 할당 방지를 위해 미리 캐싱된 렌더러 리스트
    private readonly List<SpriteRenderer> m_cachedRenderers = new List<SpriteRenderer>();
    private bool m_isInitialized = false;

    #endregion

    #region MonoBehaviour Flow

    private void Awake()
    {
        InitializeReferences();
    }

    private void Start()
    {
        // 렌더러 캐싱은 Start에서 한 번만 수행하여 런타임 비용(Garbage Collection)을 최소화합니다.
        // SPUM과 같은 캐릭터는 뼈대 구조가 바뀌지 않고 스프라이트만 교체되므로 캐싱이 안전합니다.
        if (m_sourceVisualsRoot != null)
        {
            // 비활성화된 오브젝트(꺼진 장비 등)의 렌더러도 포함하여 캐싱합니다.
            m_sourceVisualsRoot.GetComponentsInChildren(true, m_cachedRenderers);
            m_isInitialized = true;
        }
    }

    private void OnDestroy()
    {
        StopEffect();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 시간 동안 잔상 효과를 시작합니다.
    /// </summary>
    /// <param name="duration">효과 지속 시간</param>
    public void StartEffect(float duration)
    {
        if (!m_isInitialized || m_sourceVisualsRoot == null)
        {
            // Start에서 초기화가 안 되었을 경우(비동기 로딩 등), 안전하게 다시 시도
            if (m_sourceVisualsRoot != null)
            {
                m_sourceVisualsRoot.GetComponentsInChildren(true, m_cachedRenderers);
                m_isInitialized = true;
            }
            else
            {
                return;
            }
        }

        if (m_afterimagePrefab == null)
        {
            Debug.LogError("[AfterimageEffect] 프리팹이 할당되지 않았습니다.");
            return;
        }
        
        // 기존 효과가 실행 중이라면 중지
        StopEffect();

        m_effectCts = new CancellationTokenSource();
        
        // 이 컴포넌트가 파괴될 때(OnDestroy) 자동으로 취소되도록 토큰을 연결합니다.
        var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(m_effectCts.Token, this.GetCancellationTokenOnDestroy()).Token;
        
        EffectLoopAsync(duration, linkedToken).Forget();
    }

    /// <summary>
    /// 현재 진행 중인 잔상 효과를 즉시 중지합니다.
    /// </summary>
    public void StopEffect()
    {
        if (m_effectCts != null)
        {
            m_effectCts.Cancel();
            m_effectCts.Dispose();
            m_effectCts = null;
        }
    }

    #endregion

    #region Internal Logic

    private void InitializeReferences()
    {
        if (m_sourceVisualsRoot == null)
        {
            var spum = GetComponentInChildren<SPUM_Prefabs>(true);
            if (spum != null)
            {
                // Animator가 있다면 해당 Transform이 시각적 루트일 가능성이 높음 (회전/스케일 반영)
                m_sourceVisualsRoot = spum._anim != null ? spum._anim.transform : spum.transform;
            }
            else
            {
                // SPUM이 없으면 현재 Transform 사용
                m_sourceVisualsRoot = transform;
            }
        }
    }

    private async UniTaskVoid EffectLoopAsync(float duration, CancellationToken token)
    {
        float timer = 0f;
        
        // 반복 루프
        while (timer < duration && !token.IsCancellationRequested)
        {
            SpawnAfterimage();
            
            // Interval 만큼 대기 (UniTask는 Frame 기반이 아닌 시간 기반 대기를 효율적으로 처리)
            await UniTask.Delay(System.TimeSpan.FromSeconds(m_spawnInterval), cancellationToken: token);
            timer += m_spawnInterval;
        }
    }

    private void SpawnAfterimage()
    {
        // 캐싱된 렌더러가 없으면 중단
        if (m_cachedRenderers.Count == 0) return;

        if (ObjectPoolManager.Instance == null) return;

        // 풀에서 잔상 오브젝트 가져오기
        GameObject snapshotGO = ObjectPoolManager.Instance.Get(m_afterimagePrefab);
        if (snapshotGO == null) return;

        // 위치 초기화: 원본 루트의 월드 변환을 따르도록 설정하거나
        // AfterimageSnapshot 내부 로직에 따라 0점으로 설정 (여기서는 원본 코드의 의도대로 0점/Identity)
        snapshotGO.transform.position = Vector3.zero;
        snapshotGO.transform.rotation = Quaternion.identity;
        snapshotGO.transform.localScale = Vector3.one;

        var snapshot = snapshotGO.GetComponent<AfterimageSnapshot>();
        if (snapshot != null)
        {
            // 매번 GetComponent를 하지 않고, 미리 캐싱해둔 m_cachedRenderers를 전달합니다.
            snapshot.Activate(
                m_cachedRenderers, 
                m_afterimageColor, 
                m_fadeDuration, 
                m_overrideSortingOrder, 
                m_sortingOrderOverrideValue
            );
        }
        else
        {
            // 컴포넌트가 없으면 오류 방지를 위해 즉시 반환
            ObjectPoolManager.Instance.Return(snapshotGO);
        }
    }

    #endregion
}