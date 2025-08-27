using UnityEngine;

/// <summary>
/// 지정된 목표물을 향해 날아가는 추적 화살 클래스입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class HomingArrow : MonoBehaviour
{
    [Header("추적 설정")]
    [SerializeField] private float speed = 15f;         // 이동 속도
    [SerializeField] private float rotateSpeed = 200f;  // 회전 속도
    [SerializeField] private float lifeTime = 10f;      // 최대 생존 시간

    private Transform _target;
    private Rigidbody2D _rigidbody2D;

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        // Rigidbody 설정: 중력 영향 받지 않음, 물리적 충돌은 감지
        _rigidbody2D.gravityScale = 0;
        _rigidbody2D.isKinematic = false;

        Destroy(gameObject, lifeTime); // 일정 시간 후 자동 파괴
    }

    /// <summary>
    /// 추적할 목표를 설정하고 발사를 시작합니다.
    /// </summary>
    /// <param name="target">추적할 대상의 Transform</param>
    public void Launch(Transform target)
    {
        _target = target;
    }

    private void FixedUpdate()
    {
        // 목표가 없거나 비활성화되면 직진
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            _rigidbody2D.linearVelocity = transform.right * speed;
            return;
        }

        // 목표를 향한 방향 계산
        Vector2 direction = (Vector2)_target.position - _rigidbody2D.position;
        direction.Normalize();

        // 목표 방향으로 점진적 회전
        float rotateAmount = Vector3.Cross(direction, transform.right).z;
        _rigidbody2D.angularVelocity = -rotateAmount * rotateSpeed;

        // 앞으로 이동
        _rigidbody2D.linearVelocity = transform.right * speed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 적과 충돌 시 파괴 (다른 트리거와 충돌 방지)
        if (other.CompareTag("Enemy"))
        {
            // TODO: 여기에 데미지 처리 로직 추가 (예: other.GetComponent<Enemy>().TakeDamage(damage);)
            Destroy(gameObject);
        }
    }
}
