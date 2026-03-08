using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using OniBow.Managers;
using OniBow.Projectiles;
using OniBow.Logic;
using VContainer;

namespace OniBow
{
    /// <summary>
    /// [설명]: 적의 공격, 스킬, 회피(대쉬) 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    public class EnemyCombat : MonoBehaviour
    {
        #region 에디터 설정
        [Header("공격 설정")]
        [SerializeField] private GameObject m_arrowPrefab;
        [SerializeField] private Transform m_firePoint;
        [SerializeField] private float m_fireDistance = 7f;      // 적의 표준 사거리 (이 값을 기준으로 AI가 이동 범위를 자동 계산)
        [SerializeField] private float m_fireArcHeight = 3f;
        [SerializeField] private float m_fireDuration = 1f;
        [SerializeField] private float m_attackCooldown = 2f;

        [Header("스킬 설정")]
        [SerializeField] private float m_skillCooldown = 10f;
        [SerializeField, Range(0, 1)] private float m_skillChance = 0.3f;
        [SerializeField] private Transform m_skillHandPoint;
        [SerializeField] private float m_healSkillCooldown = 20f;
        [SerializeField, Range(0, 1)] private float m_healHealthThreshold = 0.4f;

        [Header("회피 설정")]
        [SerializeField, Range(0, 1)] private float m_evadeChance = 0.3f;
        [SerializeField] private float m_evadeDashSpeed = 15f;
        [SerializeField] private float m_evadeDashDuration = 0.25f;
        [SerializeField] private float m_minEvadeDistance = 2f;
        #endregion

        #region 내부 필드
        private Rigidbody2D m_rigidbody2D;
        private Collider2D m_collider;
        private LayerMask m_groundLayer;
        private float m_lastSkillUseTime = -999f;
        private float m_lastHealTime = -999f;
        
        private ObjectPoolManager m_poolManager;
        private EnemySpraySkill m_multiShotSkill;
        #endregion

        # region 프로퍼티
        public float ArrowRange => m_fireDistance;
        public float AttackCooldown => m_attackCooldown;
        public float SkillCooldown => m_skillCooldown;
        public float SkillChance => m_skillChance;
        public float HealSkillCooldown => m_healSkillCooldown;
        public float HealHealthThreshold => m_healHealthThreshold;
        public float EvadeChance => m_evadeChance;
        public float LastSkillUseTime => m_lastSkillUseTime;
        public float LastHealTime => m_lastHealTime;
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            m_rigidbody2D = GetComponent<Rigidbody2D>();
            m_collider = GetComponent<Collider2D>();
        }
        #endregion

        #region 초기화
        [Inject]
        public void Construct(ObjectPoolManager poolManager, EnemySpraySkill multiShotSkill)
        {
            m_poolManager = poolManager;
            m_multiShotSkill = multiShotSkill;
        }

        public void Initialize(LayerMask groundLayer)
        {
            m_groundLayer = groundLayer;
        }
        #endregion

        #region 공개 메서드
        public void PerformArrowLaunch(Transform target)
        {
            if (target == null) return;
            
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyAttackSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyAttackSfx);
            }

            Vector3 startPos = m_firePoint != null ? m_firePoint.position : transform.position;
            Vector2 direction = (target.position - startPos).normalized;
            
            // [개선]: m_fireDistance를 화살의 엄격한 최대 물리 사거리로 사용합니다.
            // 타겟 방향으로 설정된 사거리만큼 정확히 날아가도록 설정합니다.
            Vector3 endPos = startPos + (Vector3)direction * m_fireDistance;

            Vector3 apex = (startPos + endPos) / 2f + Vector3.up * m_fireArcHeight;
            Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

            GameObject arrowObject = m_poolManager.Get(m_arrowPrefab);
            if (arrowObject == null) return;

            arrowObject.transform.SetPositionAndRotation(startPos, Quaternion.identity);
            var arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Owner = ArrowController.ArrowOwner.Enemy;
                arrowController.Launch(startPos, controlPoint, endPos, m_fireDuration);
            }
            else
            {
                m_poolManager.Return(arrowObject);
            }
        }

        public async UniTask ExecuteSkillAsync(Transform target, CancellationToken token)
        {
            m_lastSkillUseTime = Time.time;
            if (m_multiShotSkill != null)
            {
                var context = new SkillContext(transform, target, m_skillHandPoint);
                await m_multiShotSkill.ExecuteAsync(context, token);
            }
        }

        public void ResetHealCooldown()
        {
            m_lastHealTime = Time.time;
        }

        public async UniTask<bool> EvadeAsync(EnemyMovement movement, Action<float> onFlip, Action<Enemy.EnemyState> onStateChange, Func<CancellationTokenSource> getAITaskCts, Action<CancellationTokenSource> setAITaskCts, Action startAfterimage, CancellationToken token)
        {
            movement.DetectBoundaries();

            float currentX = m_rigidbody2D.position.x;
            float spaceToLeft = currentX - movement.EffectiveMinX;
            float spaceToRight = movement.EffectiveMaxX - currentX;
            float direction = (spaceToRight > spaceToLeft) ? 1f : -1f;

            float maxDashDistance = m_evadeDashSpeed * m_evadeDashDuration;
            Bounds enemyBounds = m_collider.bounds;
            
            RaycastHit2D wallHit = Physics2D.BoxCast(
                (Vector2)transform.position + m_collider.offset,
                new Vector2(enemyBounds.size.x, enemyBounds.size.y * 0.9f),
                0f, new Vector2(direction, 0), maxDashDistance, m_groundLayer);
            
            float wallLimitedDistance = wallHit.collider != null ? wallHit.distance : maxDashDistance;

            float finalDashDistance = wallLimitedDistance;
            int steps = 10;
            float stepDistance = wallLimitedDistance / steps;

            for (int i = 1; i <= steps; i++)
            {
                float checkDistance = i * stepDistance;
                Vector2 checkPos = new Vector2(currentX + direction * checkDistance, enemyBounds.center.y);
                if (!movement.IsGroundAhead(direction)) // This is a bit simplified, but movement component has the logic
                {
                    // More precise ground check for dashing
                     RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                        checkPos, new Vector2(enemyBounds.size.x * 0.9f, 0.1f),
                        0f, Vector2.down, enemyBounds.extents.y + 0.5f, m_groundLayer);
                    if (groundUnderneath.collider == null)
                    {
                        finalDashDistance = (i - 1) * stepDistance;
                        break;
                    }
                }
            }

            finalDashDistance = Mathf.Max(0, finalDashDistance - enemyBounds.extents.x);
            float finalTargetX = Mathf.Clamp(currentX + direction * finalDashDistance, movement.EffectiveMinX, movement.EffectiveMaxX);
            float actualDashDistance = Mathf.Abs(finalTargetX - currentX);
            float actualDuration = actualDashDistance / m_evadeDashSpeed;
            
            if (actualDashDistance < m_minEvadeDistance) return false;

            getAITaskCts()?.Cancel();
            onStateChange?.Invoke(Enemy.EnemyState.Evading);
            onFlip?.Invoke(direction);

            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyEvadeSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyEvadeSfx);
            }
            
            startAfterimage?.Invoke();

            try
            {
                m_rigidbody2D.linearVelocity = new Vector2(direction * m_evadeDashSpeed, m_rigidbody2D.linearVelocity.y);
                await UniTask.Delay(TimeSpan.FromSeconds(actualDuration), cancellationToken: token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (m_rigidbody2D != null)
                {
                    m_rigidbody2D.linearVelocity = new Vector2(0, m_rigidbody2D.linearVelocity.y);
                }
            }

            return true;
        }
        #endregion
    }
}