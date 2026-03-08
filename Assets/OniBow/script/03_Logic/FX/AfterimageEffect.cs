using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Cysharp.Threading.Tasks;
using OniBow.Managers;

namespace OniBow.FX
{
    /// <summary>
    /// [설명]: 대쉬 등 빠른 움직임 시 잔상 효과를 비동기로 생성하고 관리합니다.
    /// 성능 확보를 위해 렌더러를 캐싱하여, 런타임 중 GetComponent 호출을 최대한 배제합니다.
    /// </summary>
    public class AfterimageEffect : MonoBehaviour
    {
        #region 에디터 설정
        [Header("참조")]
        [Tooltip("잔상 효과를 위해 풀링할 프리팹")]
        [FormerlySerializedAs("afterimagePrefab")]
        [SerializeField] private GameObject m_afterimagePrefab;

        [Tooltip("잔상이 복제할 원본 시각적 오브젝트의 루트 Transform입니다.")]
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

        [Tooltip("잔상의 Sorting Order를 덮어쓸지에 대한 여부입니다.")]
        [FormerlySerializedAs("_overrideSortingOrder")]
        [SerializeField] private bool m_overrideSortingOrder = false;

        [FormerlySerializedAs("_sortingOrderOverrideValue")]
        [SerializeField] private int m_sortingOrderOverrideValue = 20;
        #endregion

        #region 내부 필드
        private CancellationTokenSource m_effectCts;
        private readonly List<SpriteRenderer> m_cachedRenderers = new List<SpriteRenderer>(20);
        private bool m_isInitialized = false;
        #endregion

        #region 유니티 생명주기
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

        #region 공개 메서드
        /// <summary>
        /// [설명]: 지정된 시간 동안 비동기로 잔상 효과를 시작합니다.
        /// </summary>
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
                Debug.LogError("[AfterimageEffect] 잔상 프리팹이 할당되지 않았습니다.", this);
                return;
            }
            
            StopEffect();

            m_effectCts = new CancellationTokenSource();
            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(m_effectCts.Token, this.GetCancellationTokenOnDestroy()).Token;
            
            EffectLoopAsync(duration, linkedToken).Forget();
        }

        /// <summary>
        /// [설명]: 진행 중인 잔상 효과 코루틴을 강제 중지합니다.
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

        #region 내부 로직
        private void InitializeReferences()
        {
            if (m_sourceVisualsRoot == null)
            {
                var spumRoot = GetComponentInChildren<SPUM_Prefabs>(true);
                m_sourceVisualsRoot = spumRoot != null ? spumRoot.transform : transform;
            }
        }

        private async UniTaskVoid EffectLoopAsync(float duration, CancellationToken token)
        {
            float timer = 0f;
            
            try
            {
                while (timer < duration && !token.IsCancellationRequested)
                {
                    SpawnAfterimage();
                    
                    await UniTask.Delay(TimeSpan.FromSeconds(m_spawnInterval), cancellationToken: token);
                    timer += m_spawnInterval;
                }
            }
            catch (OperationCanceledException) { /* 무시 완료 */ }
        }

        private void SpawnAfterimage()
        {
            if (m_cachedRenderers.Count == 0 || ObjectPoolManager.Instance == null) return;

            GameObject snapshotGO = ObjectPoolManager.Instance.Get(m_afterimagePrefab);
            if (snapshotGO == null) return;

            // 좌표를 원점으로 완전히 맞춘 뒤 계산 시작
            snapshotGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            snapshotGO.transform.localScale = Vector3.one;

            var snapshot = snapshotGO.GetComponent<AfterimageSnapshot>();
            if (snapshot != null)
            {
                snapshot.ActivateAsync(
                    m_cachedRenderers, 
                    m_afterimageColor, 
                    m_fadeDuration, 
                    m_overrideSortingOrder, 
                    m_sortingOrderOverrideValue,
                    this.GetCancellationTokenOnDestroy()
                ).Forget();
            }
            else
            {
                ObjectPoolManager.Instance.Return(snapshotGO);
            }
        }
        #endregion
    }
}
