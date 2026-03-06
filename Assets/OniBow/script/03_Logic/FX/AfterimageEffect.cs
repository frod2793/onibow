using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine.Serialization; // 인스펙터 맵핑 유지를 위해 필수
using OniBow.Managers;

namespace OniBow.FX
{
    /// <summary>
    /// 대쉬 등 빠른 움직임 시 잔상 효과를 생성하고 관리합니다.
    /// 성능을 위해 렌더러 정보를 캐싱하고, UniTask를 사용하여 비동기 루프를 제어합니다.
    /// </summary>
    public class AfterimageEffect : MonoBehaviour
    {
        #region Inspector Fields (FormerlySerializedAs 적용)
        
        [Header("참조")]
        [Tooltip("잔상 효과를 위해 풀링할 프리팹")]
        [FormerlySerializedAs("afterimagePrefab")]
        [SerializeField] private GameObject m_afterimagePrefab;

        [Tooltip("잔상이 복제할 원본 시각적 오브젝트의 루트 Transform입니다. (예: SPUM_Prefabs가 있는 오브젝트)")]
        [FormerlySerializedAs("_sourceVisualsRoot")]
        [SerializeField] private Transform m_sourceVisualsRoot;

        [Header("효과 설정")]
        [Tooltip("잔상에 적용할 색상입니다.")]
        [FormerlySerializedAs("_afterimageColor")]
        [SerializeField] private Color m_afterimageColor = new Color(0.5f, 0.8f, 1f, 1f);

        [Tooltip("잔상이 생성되는 간격 (초)입니다.")]
        [FormerlySerializedAs("_spawnInterval")]
        [SerializeField] private float m_spawnInterval = 0.05f;
        [Tooltip("잔상이 완전히 사라지는 데 걸리는 시간 (초)입니다.")]
        [FormerlySerializedAs("_fadeDuration")]
        [SerializeField] private float m_fadeDuration = 0.5f;

        [Tooltip("이 값을 true로 설정하면, 잔상의 Sorting Order를 아래 값으로 덮어씁니다.")]
        [FormerlySerializedAs("_overrideSortingOrder")]
        [SerializeField] private bool m_overrideSortingOrder = false;

        [FormerlySerializedAs("_sortingOrderOverrideValue")]
        [SerializeField] private int m_sortingOrderOverrideValue = 20;

        #endregion

        #region Private Fields
        
        private CancellationTokenSource m_effectCts;
        
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
            if (m_sourceVisualsRoot != null)
            {
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
            
            StopEffect();

            m_effectCts = new CancellationTokenSource();
            
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
                    m_sourceVisualsRoot = spum.transform;
                }
                else
                {
                    Debug.LogWarning("자식 오브젝트에서 SPUM_Prefabs를 찾을 수 없어, 현재 오브젝트를 시각적 루트로 사용합니다.", this);
                    m_sourceVisualsRoot = transform;
                }
            }
        }

        private async UniTaskVoid EffectLoopAsync(float duration, CancellationToken token)
        {
            float timer = 0f;
            
            while (timer < duration && !token.IsCancellationRequested)
            {
                SpawnAfterimage();
                
                await UniTask.Delay(TimeSpan.FromSeconds(m_spawnInterval), cancellationToken: token);
                timer += m_spawnInterval;
            }
        }

        private void SpawnAfterimage()
        {
            if (m_cachedRenderers.Count == 0) return;

            if (ObjectPoolManager.Instance == null) return;

            GameObject snapshotGO = ObjectPoolManager.Instance.Get(m_afterimagePrefab);
            if (snapshotGO == null) return;

            // AfterimageSnapshot 내부에서 Matrix 연산을 통해 정확한 위치를 계산하므로,
            // 스냅샷 컨테이너 자체의 Transform은 초기화합니다.
            snapshotGO.transform.position = Vector3.zero;
            snapshotGO.transform.rotation = Quaternion.identity;
            snapshotGO.transform.localScale = Vector3.one;

            var snapshot = snapshotGO.GetComponent<AfterimageSnapshot>();
            if (snapshot != null)
            {
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
                ObjectPoolManager.Instance.Return(snapshotGO);
            }
        }

        #endregion
    }
}