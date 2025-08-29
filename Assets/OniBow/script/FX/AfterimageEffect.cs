using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

/// <summary>
/// 대쉬 등 빠른 움직임 시 잔상 효과를 생성하고 관리합니다.
/// 캐릭터의 시각적 계층 구조를 복제하여 완전한 잔상을 만듭니다.
/// </summary>
public class AfterimageEffect : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("잔상 효과를 위해 풀링할 프리팹")]
    [SerializeField] private GameObject afterimagePrefab;
    [Tooltip("잔상이 복제할 원본 시각적 오브젝트의 루트 Transform입니다. (예: SPUM_Prefabs가 있는 오브젝트)")]
    [SerializeField] private Transform _sourceVisualsRoot;

    [Header("효과 설정")]
    [SerializeField] private Color _afterimageColor = new Color(0.5f, 0.8f, 1f, 1f);
    [SerializeField] private float _spawnInterval = 0.05f; // 잔상 생성 간격 (초)
    [SerializeField] private float _fadeDuration = 0.5f;  // 잔상이 사라지는 시간 (초)
    [Tooltip("이 값을 true로 설정하면, 잔상의 Sorting Order를 아래 값으로 덮어씁니다.")]
    [SerializeField] private bool _overrideSortingOrder = false;
    [SerializeField] private int _sortingOrderOverrideValue = 20;

    private CancellationTokenSource _effectCts;
    private readonly List<SpriteRenderer> _sourceRenderers = new List<SpriteRenderer>();

    private void Awake()
    {
        // 참조가 설정되지 않았다면 자동으로 찾기를 시도합니다.
        if (_sourceVisualsRoot == null)
        {
            var spum = GetComponentInChildren<SPUM_Prefabs>(true); // 비활성화된 오브젝트도 포함하여 검색
            if (spum != null)
            {
                // SPUM_Prefabs가 제어하는 Animator의 Transform을 시각적 루트로 사용하는 것이 더 안정적입니다.
                if (spum._anim != null)
                {
                    _sourceVisualsRoot = spum._anim.transform;
                }
                else
                {
                    // Animator가 없다면, SPUM_Prefabs가 있는 오브젝트를 루트로 사용합니다.
                    _sourceVisualsRoot = spum.transform;
                    Debug.LogWarning("AfterimageEffect: SPUM_Prefabs에 Animator가 연결되지 않았습니다. 잔상 효과가 올바르게 동작하지 않을 수 있습니다.", this);
                }
            }
            else
            {
                // SPUM_Prefabs가 없는 일반 캐릭터의 경우, 이 컴포넌트가 부착된 오브젝트의 Transform을 시각적 루트로 사용합니다.
                _sourceVisualsRoot = this.transform;
                Debug.Log("AfterimageEffect: _sourceVisualsRoot가 설정되지 않았습니다. 이 오브젝트의 Transform을 시각적 루트로 사용합니다.", this);
            }
        }
    }

    /// <summary>
    /// 지정된 시간 동안 잔상 효과를 시작합니다.
    /// </summary>
    /// <param name="duration">효과 지속 시간</param>
    public void StartEffect(float duration)
    {
        if (_sourceVisualsRoot == null) return;

        // 프리팹이 할당되지 않았으면 오류를 출력하고 중단합니다.
        if (afterimagePrefab == null)
        {
            Debug.LogError("AfterimageEffect: 'Afterimage Prefab'이(가) 할당되지 않았습니다. 잔상 효과를 생성할 수 없습니다.", this);
            return;
        }
        
        StopEffect();
        _effectCts = new CancellationTokenSource();
        EffectLoopAsync(duration, _effectCts.Token).Forget();
    }

    /// <summary>
    /// 현재 진행 중인 잔상 효과를 즉시 중지합니다.
    /// </summary>
    public void StopEffect()
    {
        _effectCts?.Cancel();
        _effectCts?.Dispose();
        _effectCts = null;
    }

    private async UniTaskVoid EffectLoopAsync(float duration, CancellationToken token)
    {
        float timer = 0f;
        while (timer < duration && !token.IsCancellationRequested)
        {
            SpawnAfterimage();
            await UniTask.Delay(System.TimeSpan.FromSeconds(_spawnInterval), cancellationToken: token);
            timer += _spawnInterval;
        }
    }

    private void SpawnAfterimage()
    {
        // 잔상을 생성하는 시점의 렌더러 목록을 매번 새로 가져옵니다.
        // 이렇게 하면 대쉬 중 애니메이션이 변경되어도 최신 상태의 잔상이 생성됩니다.
        _sourceVisualsRoot.GetComponentsInChildren(true, _sourceRenderers);
        if (_sourceRenderers.Count == 0)
        {
            return; // 복제할 렌더러가 없으면 생성하지 않음
        }

        // 런타임에 GameObject와 컴포넌트를 생성하는 대신,
        // 미리 구성된 AfterimageSnapshot 프리팹을 활성화하고 데이터를 전달하는 방식으로 최적화합니다.
        // 이 방식은 런타임 할당을 최소화합니다.
        if (ObjectPoolManager.Instance == null)
        {
            Debug.LogWarning("ObjectPoolManager가 없어 잔상을 생성할 수 없습니다.");
            return;
        }

        GameObject snapshotGO = ObjectPoolManager.Instance.Get(afterimagePrefab);
        if (snapshotGO == null) return;

        snapshotGO.transform.position = Vector3.zero;
        snapshotGO.transform.rotation = Quaternion.identity;
        snapshotGO.transform.localScale = Vector3.one;

        var snapshot = snapshotGO.GetComponent<AfterimageSnapshot>();
        if (snapshot == null)
        {
            Debug.LogError("Afterimage 프리팹에 AfterimageSnapshot 스크립트가 없습니다.", snapshotGO);
            ObjectPoolManager.Instance.Return(snapshotGO);
            return;
        }

        // AfterimageSnapshot에 필요한 모든 정보를 전달하여 활성화합니다.
        // 복제 및 페이드아웃 로직은 이제 AfterimageSnapshot이 담당합니다.
        snapshot.Activate(_sourceRenderers, _afterimageColor, _fadeDuration, _overrideSortingOrder, _sortingOrderOverrideValue);
    }

    private void OnDestroy()
    {
        StopEffect();
    }
}