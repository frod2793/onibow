using UnityEngine;

/// <summary>
/// 충돌 시 주변에 광역 피해를 입히는 폭발 화살 클래스입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Roket : MonoBehaviour
{
    [Header("폭발 설정")]
    [SerializeField] private float speed = 20f;
    [SerializeField] private int explosionDamage = 50;

    [Header("카메라 쉐이크")]
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private float shakeStrength = 0.5f;

    [Header("충돌 설정")]
    [Tooltip("로켓이 충돌했을 때 폭발을 일으킬 오브젝트의 태그 목록입니다.")]
    [SerializeField] private string[] collisionTags = { "Enemy"};

    private Rigidbody2D _rigidbody2D;
    private bool _hasExploded = false;

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 0;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hasExploded || other.CompareTag("Player")) return;

        foreach (string tag in collisionTags)
        {
            if (other.CompareTag(tag))
            {
                Explode(other);
                return;
            }
        }
    }

    /// <summary>
    /// 지정된 방향으로 로켓을 발사합니다.
    /// </summary>
    /// <param name="direction">발사 방향</param>
    public void Launch(Vector2 direction)
    {
        transform.right = direction;
        
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.RoketLaunchSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.RoketLaunchSfx);
        }
        _rigidbody2D.linearVelocity = direction.normalized * speed;
    }

    /// <summary>
    /// 폭발을 실행하여 이펙트를 재생하고, 직접 충돌한 대상에게 데미지를 줍니다.
    /// </summary>
    /// <param name="directHit">직접 충돌한 대상의 Collider2D</param>
    private void Explode(Collider2D directHit = null)
    {
        if (_hasExploded) return;
        _hasExploded = true;

        EffectManager.Instance.PlayExplosionEffect(transform.position);

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.RoketExplosionSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.RoketExplosionSfx);
        }

        GameManager.Instance.ShakeCamera(shakeDuration, shakeStrength);

        if (directHit != null && directHit.TryGetComponent<Enemy>(out var enemy))
        {
            enemy.TakeDamage(explosionDamage);
        }
       
        Destroy(gameObject);
    }
}
