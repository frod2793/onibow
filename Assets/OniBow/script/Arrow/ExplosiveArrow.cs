using UnityEngine;

/// <summary>
/// 충돌 시 주변에 광역 피해를 입히는 폭발 화살 클래스입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ExplosiveArrow : MonoBehaviour
{
    [Header("폭발 설정")]
    [SerializeField] private float speed = 20f;         // 이동 속도
    [SerializeField] private float explosionRadius = 5f;  // 폭발 반경
    [SerializeField] private float lifeTime = 5f;       // 최대 생존 시간 (이 시간이 지나면 자동 폭발)
    [SerializeField] private GameObject explosionEffectPrefab; // 폭발 이펙트 프리팹

    private Rigidbody2D _rigidbody2D;
    private bool _hasExploded = false; // 중복 폭발 방지 플래그

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 0; // 직선으로 날아가도록 중력 무시
        
        // lifeTime 이후에 Explode 메서드를 호출하여 자동 폭발 및 파괴
        Invoke(nameof(Explode), lifeTime);
    }

    /// <summary>
    /// 지정된 방향으로 화살을 발사합니다.
    /// </summary>
    public void Launch(Vector2 direction)
    {
        transform.right = direction; // 화살이 날아가는 방향을 바라보도록 설정
        _rigidbody2D.linearVelocity = direction.normalized * speed;
    }

    // 물리적 충돌이 일어났을 때 호출됩니다.
    private void OnCollisionEnter2D(Collision2D other)
    {
        // 지형 또는 적과 충돌 시 폭발
        Explode();
    }

    /// <summary>
    /// 폭발을 실행하여 주변의 적에게 피해를 줍니다.
    /// </summary>
    private void Explode()
    {
        // 이미 폭발했다면 중복 실행 방지
        if (_hasExploded) return;
        _hasExploded = true;

        // 폭발 이펙트 생성
        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        // 폭발 반경 내의 모든 적을 찾음
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
        foreach (var hitCollider in colliders)
        {
            if (hitCollider.CompareTag("Enemy"))
            {
                Debug.Log(hitCollider.name + "에게 폭발 데미지를 입혔습니다!");
                // TODO: 여기에 실제 데미지를 입히는 로직 추가
                // 예: hitCollider.GetComponent<Enemy>().TakeDamage(explosionDamage);
            }
        }

        // 모든 처리가 끝난 후 오브젝트 파괴
        Destroy(gameObject);
    }
}