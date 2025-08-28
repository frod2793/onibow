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
            var spum = GetComponentInChildren<SPUM_Prefabs>();
            if (spum != null) _sourceVisualsRoot = spum.transform;
            else Debug.LogError("AfterimageEffect: _sourceVisualsRoot가 설정되지 않았고, 자식에서 SPUM_Prefabs를 찾을 수 없습니다.", this);
        }
    }

    /// <summary>
    /// 지정된 시간 동안 잔상 효과를 시작합니다.
    /// </summary>
    /// <param name="duration">효과 지속 시간</param>
    public void StartEffect(float duration)
    {
        if (_sourceVisualsRoot == null) return;
        
        // 최신 상태의 렌더러 목록을 가져옵니다.
        _sourceVisualsRoot.GetComponentsInChildren(true, _sourceRenderers);
        if (_sourceRenderers.Count == 0)
        {
            Debug.LogWarning("잔상 효과에 필요한 원본 렌더러가 없습니다.", this);
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
        if (AfterimagePool.Instance == null) return;

        GameObject snapshotContainer = AfterimagePool.Instance.Get();
        if (snapshotContainer == null) return;

        // [최적화] 기존 자식 오브젝트를 파괴하는 대신, 리스트를 가져와 재사용합니다.
        var existingPartRenderers = new List<SpriteRenderer>();
        snapshotContainer.GetComponentsInChildren<SpriteRenderer>(false, existingPartRenderers);
        int existingPartsCount = existingPartRenderers.Count;

        // 원본 렌더러들의 상태를 복제하여 스냅샷을 생성합니다.
        for (int i = 0; i < _sourceRenderers.Count; i++)
        {
            SpriteRenderer sourceRenderer = _sourceRenderers[i];

            // 비활성화되었거나 스프라이트가 없는 파츠는 건너뜁니다.
            if (!sourceRenderer.gameObject.activeInHierarchy || sourceRenderer.sprite == null)
            {
                // 재사용 중인 파츠가 있다면 비활성화합니다.
                if (i < existingPartsCount)
                {
                    existingPartRenderers[i].gameObject.SetActive(false);
                }
                continue;
            }

            SpriteRenderer partCopyRenderer;
            if (i < existingPartsCount)
            {
                // 기존 파츠를 재사용합니다.
                partCopyRenderer = existingPartRenderers[i];
                partCopyRenderer.gameObject.name = sourceRenderer.name; // 이름도 동기화
                partCopyRenderer.gameObject.SetActive(true);
            }
            else
            {
                // 파츠가 부족하면 새로 생성합니다.
                var partCopyObj = new GameObject(sourceRenderer.name);
                partCopyObj.transform.SetParent(snapshotContainer.transform, false);
                partCopyRenderer = partCopyObj.AddComponent<SpriteRenderer>();
            }

            // 속성을 복사합니다.
            partCopyRenderer.sprite = sourceRenderer.sprite;
            partCopyRenderer.color = _afterimageColor;
            partCopyRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            if (_overrideSortingOrder)
            {
                partCopyRenderer.sortingOrder = _sortingOrderOverrideValue;
            }
            else
            {
                partCopyRenderer.sortingOrder = sourceRenderer.sortingOrder - 1;
            }

            partCopyRenderer.transform.position = sourceRenderer.transform.position;
            partCopyRenderer.transform.rotation = sourceRenderer.transform.rotation;
            partCopyRenderer.transform.localScale = sourceRenderer.transform.lossyScale;
        }

        // 남는 기존 파츠들은 비활성화합니다.
        for (int i = _sourceRenderers.Count; i < existingPartsCount; i++)
        {
            existingPartRenderers[i].gameObject.SetActive(false);
        }

        snapshotContainer.GetComponent<AfterimageSnapshot>()?.Activate(_fadeDuration);
    }

    private void OnDestroy()
    {
        StopEffect();
    }
}