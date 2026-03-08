using System;
using System.Threading;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using OniBow.FX;
using OniBow.Managers;

namespace OniBow
{
    /// <summary>
    /// [설명]: 플레이어의 이동, 대쉬, 지면 체크 및 카메라 경계 제한 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        #region 에디터 설정
        [Header("이동 설정")]
        [Tooltip("최대 이동 속도")]
        [SerializeField] private float m_maxSpeed = 5f;
        [Tooltip("가속 시간")]
        [SerializeField] private float m_accelerationTime = 0.2f;
        [Tooltip("감속 시간")]
        [SerializeField] private float m_decelerationTime = 0.1f;
        [Tooltip("가속 시 적용할 Ease 타입")]
        [SerializeField] private Ease m_accelerationEase = Ease.OutCubic;
        [Tooltip("감속 시 적용할 Ease 타입")]
        [SerializeField] private Ease m_decelerationEase = Ease.InCubic;

        [Header("대쉬 설정")]
        [Tooltip("대쉬 속도")]
        [SerializeField] private float m_dashSpeed = 20f;
        [Tooltip("대쉬 지속 시간 (초)")]
        [SerializeField] private float m_dashDuration = 0.2f;
        [Tooltip("대쉬 쿨다운 (초)")]
        [SerializeField] private float m_dashCooldown = 1f;

        [Header("충돌 설정")]
        [Tooltip("대쉬 시 충돌을 감지할 지면 및 벽 레이어")]
        [SerializeField] private LayerMask groundLayer;
        #endregion

        #region 내부 필드
        private Rigidbody2D m_rigidbody2D;
        private Collider2D m_collider;
        private AfterimageEffect m_afterimageEffect;

        private Tween m_movementTween;
        private float m_lastDashTime = -999f;
        private bool m_isDashing = false;

        #endregion

        #region 프로퍼티
        public bool IsDashing => m_isDashing;
        public float DashCooldown => m_dashCooldown;
        public float LastDashTime => m_lastDashTime;
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            m_rigidbody2D = GetComponent<Rigidbody2D>();
            m_collider = GetComponent<Collider2D>();
            m_afterimageEffect = GetComponent<AfterimageEffect>();

            m_rigidbody2D.gravityScale = 1;
            m_rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
        #endregion

        #region 공개 메서드
        public async UniTask MoveLoopAsync(float direction, CancellationToken token, Action<bool> onMoveStateChanged)
        {
            m_movementTween?.Kill();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    float targetVelocityX = direction * m_maxSpeed;
                    float currentVelocityX = m_rigidbody2D.linearVelocity.x;
                    
                    float accelRate = (Mathf.Abs(targetVelocityX) > 0.01f) ? (m_maxSpeed / m_accelerationTime) : (m_maxSpeed / m_decelerationTime);
                    float finalVelocityX = Mathf.MoveTowards(currentVelocityX, targetVelocityX, accelRate * Time.fixedDeltaTime);

                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        float minX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x + m_collider.bounds.extents.x;
                        float maxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x - m_collider.bounds.extents.x;

                        if ((m_rigidbody2D.position.x <= minX && finalVelocityX < 0) ||
                            (m_rigidbody2D.position.x >= maxX && finalVelocityX > 0))
                        {
                            finalVelocityX = 0;
                        }
                    }

                    if (Mathf.Abs(finalVelocityX) > 0.01f)
                    {
                        if (!IsGroundAhead(finalVelocityX))
                        {
                            finalVelocityX = 0;
                        }
                    }

                    m_rigidbody2D.linearVelocity = new Vector2(finalVelocityX, m_rigidbody2D.linearVelocity.y);
                    onMoveStateChanged?.Invoke(Mathf.Abs(finalVelocityX) > 0.01f);
                    
                    if (cam != null) ClampPositionDynamic(cam);

                    await UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.FixedUpdate, token);
                }
            }
            catch (OperationCanceledException){}
        }

        public void StopMoving(Action onComplete)
        {
            m_movementTween?.Kill();
            m_movementTween = DOTween.To(() => m_rigidbody2D.linearVelocity.x, 
                x => m_rigidbody2D.linearVelocity = new Vector2(x, m_rigidbody2D.linearVelocity.y), 
                0f, m_decelerationTime)
                .SetEase(m_decelerationEase)
                .SetUpdate(UpdateType.Fixed)
                .OnComplete(() => onComplete?.Invoke());
        }

        public bool CanDash()
        {
            if (m_isDashing || Time.time < m_lastDashTime + m_dashCooldown) return false;



            // 지면에 있는지 체크
            RaycastHit2D groundCheck = Physics2D.Raycast(
                (Vector2)transform.position + m_collider.offset,
                Vector2.down,
                m_collider.bounds.extents.y + 0.5f,
                groundLayer
            );
            return groundCheck.collider != null;
        }

        public async UniTaskVoid DashAsync(float direction, CancellationToken token, Action onStart, Action onComplete)
        {
            m_isDashing = true;
            m_lastDashTime = Time.time;
            onStart?.Invoke();

            float finalDashDistance = CalculateSafeDashDistance(direction);
            
            Camera cam = Camera.main;
            float minX = float.MinValue;
            float maxX = float.MaxValue;
            if (cam != null)
            {
                minX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x + m_collider.bounds.extents.x;
                maxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x - m_collider.bounds.extents.x;
            }

            float targetX = Mathf.Clamp(m_rigidbody2D.position.x + direction * finalDashDistance, minX, maxX);
            float actualDashDistance = Mathf.Abs(targetX - m_rigidbody2D.position.x);
            float duration = actualDashDistance / m_dashSpeed;

            if (duration < Time.fixedDeltaTime)
            {
                m_isDashing = false;
                onComplete?.Invoke();
                return;
            }

            if (m_afterimageEffect != null) m_afterimageEffect.StartEffect(duration);
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDashSfx))
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDashSfx);

            float originalGravity = m_rigidbody2D.gravityScale;
            m_rigidbody2D.gravityScale = 0;
            m_rigidbody2D.linearVelocity = Vector2.zero;

            float startY = m_rigidbody2D.position.y;

            try
            {
                float elapsedTime = 0f;
                while (elapsedTime < duration && !token.IsCancellationRequested)
                {
                    await UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.FixedUpdate, token);
                    elapsedTime += Time.fixedDeltaTime;

                    float newX = Mathf.MoveTowards(m_rigidbody2D.position.x, targetX, m_dashSpeed * Time.fixedDeltaTime);
                    m_rigidbody2D.position = new Vector2(newX, startY);

                    if (Mathf.Approximately(m_rigidbody2D.position.x, targetX)) break;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                m_rigidbody2D.gravityScale = originalGravity;
                m_rigidbody2D.linearVelocity = Vector2.zero;
                if (!token.IsCancellationRequested)
                {
                    m_rigidbody2D.position = new Vector2(targetX, startY);
                }
                m_isDashing = false;
                onComplete?.Invoke();
            }
        }

        public void CancelMovement()
        {
            m_movementTween?.Kill();
            m_rigidbody2D.linearVelocity = Vector2.zero;
        }
        #endregion

        #region 내부 로직
        private void ClampPositionDynamic(Camera cam)
        {
            float minX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x + m_collider.bounds.extents.x;
            float maxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x - m_collider.bounds.extents.x;
            Vector2 clampedPosition = m_rigidbody2D.position;
            clampedPosition.x = Mathf.Clamp(clampedPosition.x, minX, maxX);
            m_rigidbody2D.position = clampedPosition;
        }

        private bool IsGroundAhead(float velocityX)
        {


            Bounds playerBounds = m_collider.bounds;
            float moveSign = Mathf.Sign(velocityX);
            Vector2 groundCheckOrigin = (Vector2)playerBounds.center + new Vector2(moveSign * playerBounds.extents.x, -playerBounds.extents.y - 0.05f);
            RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, groundLayer);
            return groundHit.collider != null;
        }

        private float CalculateSafeDashDistance(float direction)
        {
            Bounds playerBounds = m_collider.bounds;
            float currentX = m_rigidbody2D.position.x;
            float maxDashDistance = m_dashSpeed * m_dashDuration;



            float wallLimitedDistance = maxDashDistance;
            RaycastHit2D wallHit = Physics2D.BoxCast(
                (Vector2)transform.position + m_collider.offset,
                new Vector2(playerBounds.size.x, playerBounds.size.y * 0.9f),
                0f,
                new Vector2(direction, 0),
                maxDashDistance,
                groundLayer
            );
            if (wallHit.collider != null) wallLimitedDistance = wallHit.distance;

            float finalDashDistance = wallLimitedDistance;
            int steps = 15;
            float stepDistance = wallLimitedDistance / steps;

            for (int i = 1; i <= steps; i++)
            {
                float checkDistance = i * stepDistance;
                Vector2 checkPos = new Vector2(currentX + direction * checkDistance, m_rigidbody2D.position.y);

                RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                    checkPos,
                    new Vector2(playerBounds.size.x * 0.9f, 0.1f),
                    0f, Vector2.down, playerBounds.extents.y + 0.5f, groundLayer
                );

                if (groundUnderneath.collider == null)
                {
                    finalDashDistance = (i - 1) * stepDistance;
                    break;
                }
            }

            return Mathf.Max(0, finalDashDistance - playerBounds.extents.x);
        }
        #endregion
    }
}
