using UnityEngine;

namespace OniBow
{
    /// <summary>
    /// [설명]: 적의 이동, 지면 감지, 카메라 경계 제한 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    public class EnemyMovement : MonoBehaviour
    {
        #region 에디터 설정
        [Header("이동 설정")]
        [SerializeField] private float m_moveSpeed = 3f;
        [SerializeField] private float m_distanceTolerance = 0.5f;
        
        [Header("지형 및 카메라 설정")]
        [SerializeField] private LayerMask m_groundLayer;
        #endregion

        #region 내부 필드
        private Rigidbody2D m_rigidbody2D;
        private Collider2D m_collider;
        private float m_minXPosition;
        private float m_maxXPosition;
        private float m_cameraMinX;
        private float m_cameraMaxX;
        private float m_effectiveMinX;
        private float m_effectiveMaxX;
        private float m_actualSpeed;
        private Vector2 m_previousPos;
        #endregion

        #region 프로퍼티
        public float MoveSpeed => m_moveSpeed;
        public float DistanceTolerance => m_distanceTolerance;
        public float EffectiveMinX => m_effectiveMinX;
        public float EffectiveMaxX => m_effectiveMaxX;
        public float ActualSpeed => m_actualSpeed;
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            m_rigidbody2D = GetComponent<Rigidbody2D>();
            m_collider = GetComponent<Collider2D>();
        }

        private void FixedUpdate()
        {
            if (m_rigidbody2D != null)
            {
                m_actualSpeed = Mathf.Abs(m_rigidbody2D.position.x - m_previousPos.x) / Time.fixedDeltaTime;
                m_previousPos = m_rigidbody2D.position;
            }
        }
        #endregion

        #region 공개 메서드
        public void Initialize(float moveSpeed, float tolerance, LayerMask groundLayer)
        {
            m_moveSpeed = moveSpeed;
            m_distanceTolerance = tolerance;
            m_groundLayer = groundLayer;
            DetectBoundaries();
        }

        private void UpdateEffectiveBounds()
        {
            float enemyWidth = m_collider.bounds.extents.x;
            Camera cam = Camera.main;
            if (cam != null)
            {
                m_cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
                m_cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;
            }

            // 지형 끝이나 벽에 반쯤 걸쳐도 자연스럽게 서 있게 조절 (50% 폭만 적용)
            m_effectiveMinX = Mathf.Max(m_minXPosition, m_cameraMinX) + enemyWidth * 0.5f;
            m_effectiveMaxX = Mathf.Min(m_maxXPosition, m_cameraMaxX) - enemyWidth * 0.5f;
        }

        public float Move(float targetXVelocity)
        {
            UpdateEffectiveBounds();

            float moveSign = Mathf.Sign(targetXVelocity);
            if (moveSign != 0 && !IsGroundAhead(moveSign))
            {
                targetXVelocity = 0;
            }

            if ((transform.position.x <= m_effectiveMinX && targetXVelocity < 0) || 
                (transform.position.x >= m_effectiveMaxX && targetXVelocity > 0))
            {
                targetXVelocity = 0;
            }

            m_rigidbody2D.linearVelocity = new Vector2(targetXVelocity, m_rigidbody2D.linearVelocity.y);
            ClampPosition();
            
            return targetXVelocity;
        }

        public void Stop()
        {
            m_rigidbody2D.linearVelocity = new Vector2(0, m_rigidbody2D.linearVelocity.y);
        }

        public void DetectBoundaries()
        {
            Bounds enemyBounds = m_collider.bounds;
            float enemyWidth = enemyBounds.extents.x;

            float maxProbeDistance = 20f;
            int probeSteps = 100;
            float stepDistance = maxProbeDistance / probeSteps;
            
            Vector2 characterFeet = (Vector2)transform.position - new Vector2(0, enemyBounds.extents.y);
            Vector2 boxCastSize = new Vector2(stepDistance, 0.1f);

            // Right edge
            float rightEdgeX = transform.position.x;
            for (int i = 1; i <= probeSteps; i++)
            {
                Vector2 probeOrigin = new Vector2(transform.position.x + i * stepDistance, characterFeet.y);
                RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
                if (hit.collider == null)
                {
                    rightEdgeX = transform.position.x + (i - 1) * stepDistance;
                    break;
                }
                if (i == probeSteps) rightEdgeX = transform.position.x + maxProbeDistance;
            }
            m_maxXPosition = rightEdgeX;

            // Left edge
            float leftEdgeX = transform.position.x;
            for (int i = 1; i <= probeSteps; i++)
            {
                Vector2 probeOrigin = new Vector2(transform.position.x - i * stepDistance, characterFeet.y);
                RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
                if (hit.collider == null)
                {
                    leftEdgeX = transform.position.x - (i - 1) * stepDistance;
                    break;
                }
                if (i == probeSteps) leftEdgeX = transform.position.x - maxProbeDistance;
            }
            m_minXPosition = leftEdgeX;

            Camera cam = Camera.main;
            if (cam != null)
            {
                m_cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
                m_cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;
            }
            else
            {
                m_cameraMinX = -Mathf.Infinity;
                m_cameraMaxX = Mathf.Infinity;
            }

            m_effectiveMinX = Mathf.Max(m_minXPosition, m_cameraMinX) + enemyWidth;
            m_effectiveMaxX = Mathf.Min(m_maxXPosition, m_cameraMaxX) - enemyWidth;
        }

        public bool IsGroundAhead(float direction)
        {
            Bounds enemyBounds = m_collider.bounds;
            // [개선]: 캐릭터 중심이 아닌, 이동 방향의 레이어 끝부분(약 80%) 지점에서 체크하여 
            // 낭떠러지에 도달하기 전에 미리 감지할 수 있도록 합니다.
            float checkOffsetX = direction * enemyBounds.extents.x * 0.8f;
            
            // [개선]: 발 밑보다 약간 위(0.1f)에서부터 아래로 넉넉하게(0.5f) 레이를 쏩니다.
            Vector2 rayOrigin = new Vector2(enemyBounds.center.x + checkOffsetX, enemyBounds.min.y + 0.1f);
            RaycastHit2D groundHit = Physics2D.Raycast(rayOrigin, Vector2.down, 0.5f, m_groundLayer);
            
            // 디버그 레이 (에디터에서 확인용)
            Debug.DrawRay(rayOrigin, Vector2.down * 0.5f, groundHit.collider != null ? Color.green : Color.red);
            
            return groundHit.collider != null;
        }

        public void ClampPosition()
        {
            Vector2 clampedPosition = m_rigidbody2D.position;
            clampedPosition.x = Mathf.Clamp(clampedPosition.x, m_effectiveMinX, m_effectiveMaxX);
            m_rigidbody2D.position = clampedPosition;
        }
        #endregion
    }
}