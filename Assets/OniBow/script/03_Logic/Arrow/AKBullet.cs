using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using OniBow.Managers;
using OniBow;
using OniBow.Presentation;
using OniBow.UI.Interfaces;
using VContainer;

namespace OniBow.Projectiles
{
    /// <summary>
    /// AK 총알의 생명주기와 충돌을 관리합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class AKBullet : MonoBehaviour
    {
        [SerializeField] private int damage = 5;
        [Header("카메라 쉐이크")]
        [SerializeField] private float shakeDuration = 0.1f;
        [SerializeField] private float shakeStrength = 0.15f;
        
        private CancellationTokenSource _lifeTimeCts;
        private CameraEffectView m_cameraEffectView;
        
        [Inject]
        public void Construct(CameraEffectView cameraEffectView)
        {
            m_cameraEffectView = cameraEffectView;
        }

        private Vector2 m_lastPosition;
        private bool m_isFirstFrame;

        private void OnEnable()
        {
            _lifeTimeCts = new CancellationTokenSource();
            
            // 고속 투사체 충돌 신뢰성 확보
            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            // Z축 정규화 (플레이어 Z=1 기준 충돌 신뢰성 확보)
            Vector3 pos = transform.position;
            pos.z = 1f;
            transform.position = pos;
            
            m_isFirstFrame = true;

            ReturnAfterDelay(3f, _lifeTimeCts.Token).Forget();
        }

        private void OnDisable()
        {
            _lifeTimeCts?.Cancel();
            
            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.linearVelocity = Vector2.zero;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete; // 비활성 시 기본값 복구
            }
        }

        private void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy) return;

            Vector2 currentPos = transform.position;

            if (m_isFirstFrame)
            {
                // 오브젝트 풀에서 꺼낸 직후 텔레포트된 위치를 기준으로 잡음
                m_lastPosition = currentPos;
                m_isFirstFrame = false;
                return;
            }

            // 터널링 방지: 이전 프레임 위치에서 현재 위치까지 궤적 검사
            float distance = Vector2.Distance(m_lastPosition, currentPos);
            if (distance > 0.001f)
            {
                ContactFilter2D filter = new ContactFilter2D();
                filter.useLayerMask = true;
                filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer)); // 유니티 물리 매트릭스 준수
                filter.useTriggers = true;

                RaycastHit2D[] hits = new RaycastHit2D[10];
                int count = Physics2D.Linecast(m_lastPosition, currentPos, filter, hits);
                
                for (int i = 0; i < count; i++)
                {
                    Collider2D col = hits[i].collider;
                    if (col != null && col.gameObject != gameObject)
                    {
                        // 플레이어와 관련된 트리거가 아닌 단순 감지 트리거 무시 (허공 막힘 방지)
                        if (col.isTrigger && !col.CompareTag("Player") && col.GetComponentInParent<PlayerHealth>() == null)
                        {
                            continue;
                        }

                        HandleCollision(col);
                        if (!gameObject.activeInHierarchy) break;
                    }
                }
            }

            m_lastPosition = currentPos;
        }

        private void Update()
        {
            if (!gameObject.activeInHierarchy) return;

            // 카메라 화면 밖으로 나갔는지 체크
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            Vector3 viewportPos = mainCam.WorldToViewportPoint(transform.position);
            
            // 화면 밖(마진 0.5 부여)으로 나가면 반환하여 장거리 피격 신뢰성 확보
            if (viewportPos.x < -0.5f || viewportPos.x > 1.5f || viewportPos.y < -0.5f || viewportPos.y > 1.5f)
            {
                ReturnToPool();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.isTrigger && !other.CompareTag("Player") && other.GetComponentInParent<PlayerHealth>() == null)
            {
                return;
            }
            HandleCollision(other);
        }

        private void HandleCollision(Collider2D other)
        {
            if (!gameObject.activeInHierarchy) return;

            // 자신과 발사자(적), 기타 총알류는 관통 처리
            if (other.CompareTag("Enemy") || other.CompareTag("Arrow") || other.CompareTag("EnemyArrow")) return;

            // 1. 플레이어 피격 판정
            bool isPlayer = other.CompareTag("Player") || other.GetComponentInParent<PlayerHealth>() != null;
            
            if (isPlayer)
            {
                if (EffectManager.Instance != null)
                {
                    EffectManager.Instance.PlayBulletHitEffect(transform.position);
                }
                if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.AKHitSfx))
                {
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.AKHitSfx);
                }
                if (m_cameraEffectView != null)
                {
                    m_cameraEffectView.ShakeCamera(shakeDuration, shakeStrength);
                }
                else
                {
                    // Fallback
                    var effectView = FindFirstObjectByType<CameraEffectView>();
                    if (effectView != null) effectView.ShakeCamera(shakeDuration, shakeStrength);
                }

                // 인터페이스를 통한 데미지 처리 (부모 객체 포함 탐색)
                var damageable = other.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    damageable.TakeDamage(damage);
                }
                
                ReturnToPool();
                return;
            }

            // 2. 환경(벽, 지면) 충돌 처리
            if (other.CompareTag("Ground") || other.CompareTag("Wall") || other.gameObject.layer == LayerMask.NameToLayer("Ground") || other.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (EffectManager.Instance != null)
                {
                    EffectManager.Instance.PlayBulletHitEffect(transform.position);
                }
                ReturnToPool();
            }
        }

        /// <summary>
        /// 지정된 시간 후에 오브젝트를 풀로 반환합니다.
        /// </summary>
        private async UniTaskVoid ReturnAfterDelay(float delay, CancellationToken token)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token);
                ReturnToPool();
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// 이 오브젝트를 오브젝트 풀로 반환합니다.
        /// </summary>
        private void ReturnToPool()
        {
            _lifeTimeCts?.Cancel();
            if (ObjectPoolManager.Instance != null && gameObject.activeInHierarchy)
            {
                ObjectPoolManager.Instance.Return(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}