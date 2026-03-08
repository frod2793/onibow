using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using OniBow.Managers;
using OniBow.Projectiles;
using System.Collections.Generic;
using VContainer;

namespace OniBow
{
    /// <summary>
    /// [설명]: 플레이어의 자동 공격 루프 및 타겟팅 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        #region 에디터 설정
        [Header("공격 설정")]
        [Tooltip("발사할 화살 프리팹")]
        [SerializeField] private GameObject m_arrowPrefab;
        [Tooltip("화살이 발사될 위치")]
        [SerializeField] private Transform m_firePoint;
        [Tooltip("화살이 날아가는 고정 거리")]
        [SerializeField] private float m_fireDistance = 7f;
        [Tooltip("포물선 발사의 최고 높이")]
        [SerializeField] private float m_fireArcHeight = 3f;
        [Tooltip("화살이 목표 지점까지 도달하는 시간")]
        [SerializeField] private float m_fireDuration = 1f;
        [Tooltip("정지 후 반복 발사 간격")]
        [SerializeField] private float m_fireInterval = 2f;
        [Tooltip("최적화된 탐색용 최대 사거리")]
        [SerializeField] private float m_targetingRange = 15f;
        #endregion

        #region 내부 필드
        private float m_lastFireTime = -999f;
        private CancellationTokenSource m_fireCts;
        
        // 의존성 주입 필드
        private ObjectPoolManager m_poolManager;
        private SoundManager m_soundManager;

        // 최적화된 적 탐색을 위한 캐싱 (Zero Allocation)
        private static readonly Collider2D[] s_enemyOverlapResults = new Collider2D[10];
        private const string k_EnemyTag = "Enemy";
        #endregion

        #region 의존성 주입
        [Inject]
        public void Construct(ObjectPoolManager poolManager, SoundManager soundManager)
        {
            m_poolManager = poolManager;
            m_soundManager = soundManager;
        }
        #endregion

        #region 공개 메서드
        public void Initialize()
        {
            StopRepeatingFire();
        }

        public void StartRepeatingFire(Func<bool> canAction, Action onFire)
        {
            StopRepeatingFire();
            m_fireCts = new CancellationTokenSource();
            RepeatingFireLoopAsync(canAction, onFire, m_fireCts.Token).Forget();
        }

        public void StopRepeatingFire()
        {
            if (m_fireCts != null)
            {
                m_fireCts.Cancel();
                m_fireCts.Dispose();
                m_fireCts = null;
            }
        }

        public void FireAtNearestEnemy(Action onFireComplete)
        {
            GameObject nearestEnemy = FindNearestEnemyOptimized();
            if (nearestEnemy != null)
            {
                if (m_soundManager != null && !string.IsNullOrEmpty(m_soundManager.PlayerFireSfx))
                {
                    m_soundManager.PlaySFX(m_soundManager.PlayerFireSfx);
                }

                // 회전 및 애니메이션 처리는 Facade에서 처리하거나 이벤트를 통해 전달
                onFireComplete?.Invoke();

                Vector3 startPos = m_firePoint != null ? m_firePoint.position : transform.position;
                Vector3 enemyPos = nearestEnemy.transform.position;
                Vector2 direction = (enemyPos - startPos).normalized;
                Vector3 endPos = startPos + (Vector3)direction * m_fireDistance;

                Vector3 apex = (startPos + endPos) / 2f + Vector3.up * m_fireArcHeight;
                Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

                if (m_poolManager != null)
                {
                    GameObject arrowObject = m_poolManager.Get(m_arrowPrefab);
                    if (arrowObject != null)
                    {
                        var arrowController = arrowObject.GetComponent<ArrowController>();
                        if (arrowController != null)
                        {
                            arrowController.Owner = ArrowController.ArrowOwner.Player;
                            arrowController.Launch(startPos, controlPoint, endPos, m_fireDuration);
                        }
                        else
                        {
                            m_poolManager.Return(arrowObject);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// [설명]: 적 관리 시스템이 없는 경우를 대비한 최적화된 탐색 로직 (sqrMagnitude 기반)
        /// </summary>
        public GameObject FindNearestEnemyOptimized()
        {
            GameObject nearest = null;
            float minDistanceSqr = Mathf.Infinity;
            Vector3 currentPos = transform.position;

            // 1. OverlapCircleNonAlloc을 사용하여 가비지 없이 주변 콜라이더 탐색
            int count = Physics2D.OverlapCircleNonAlloc(currentPos, m_targetingRange, s_enemyOverlapResults);
            
            for (int i = 0; i < count; i++)
            {
                Collider2D col = s_enemyOverlapResults[i];
                if (col == null) continue;

                // 2. 태그 및 적 컴포넌트 유무 확인 (캐싱 고려 가능하나 현재 로직 유지)
                if (col.CompareTag(k_EnemyTag))
                {
                    var enemy = col.GetComponent<Enemy>();
                    if (enemy != null && !enemy.IsDead)
                    {
                        float distSqr = (col.transform.position - currentPos).sqrMagnitude;
                        if (distSqr < minDistanceSqr)
                        {
                            minDistanceSqr = distSqr;
                            nearest = col.gameObject;
                        }
                    }
                }
            }

            return nearest;
        }

        public void FireStraightArrow(Action onFireComplete)
        {
            GameObject nearestEnemy = FindNearestEnemyOptimized();
            if (nearestEnemy != null)
            {
                onFireComplete?.Invoke();

                Vector3 startPos = m_firePoint != null ? m_firePoint.position : transform.position;
                Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;

                if (m_poolManager != null)
                {
                    GameObject arrowObject = m_poolManager.Get(m_arrowPrefab);
                    if (arrowObject != null)
                    {
                        var arrowController = arrowObject.GetComponent<ArrowController>();
                        if (arrowController != null)
                        {
                            arrowController.Owner = ArrowController.ArrowOwner.Player;
                            arrowController.LaunchStraight(startPos, direction, 100f, 1.5f);
                        }
                        else
                        {
                            m_poolManager.Return(arrowObject);
                        }
                    }
                }
            }
        }
        #endregion

        #region 내부 로직
        private async UniTaskVoid RepeatingFireLoopAsync(Func<bool> canAction, Action onFire, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    float timeUntilReady = (m_lastFireTime + m_fireInterval) - Time.time;
                    if (timeUntilReady > 0)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(timeUntilReady), cancellationToken: token);
                    }
                    if (token.IsCancellationRequested) break;

                    if (canAction != null && !canAction())
                    {
                        await UniTask.Yield(token);
                        continue;
                    }

                    if (FindNearestEnemyOptimized() == null)
                    {
                        await UniTask.Yield(token);
                        continue;
                    }

                    FireAtNearestEnemy(onFire);
                    m_lastFireTime = Time.time;
                }
            }
            catch (OperationCanceledException){}
        }
        #endregion
    }
}
