using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// 충돌 시 주변에 광역 피해를 입히는 폭발 화살 클래스입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Roket : MonoBehaviour
{
    [Header("폭발 설정")]
    [SerializeField] private float speed = 20f;         // 이동 속도
    [SerializeField] private float explosionRadius = 5f;  // 폭발 반경
    [SerializeField] private int explosionDamage = 50;    // 폭발 데미지

    [Header("카메라 쉐이크")]
    [SerializeField] private float shakeDuration = 0.3f;  // 카메라 쉐이크 지속 시간
    [SerializeField] private float shakeStrength = 0.5f;  // 카메라 쉐이크 강도

    [Header("충돌 설정")]
    [Tooltip("로켓이 충돌했을 때 폭발을 일으킬 오브젝트의 태그 목록입니다.")]
    [SerializeField] private string[] collisionTags = { "Enemy"}; // 충돌 시 폭발할 태그 목록

    private Rigidbody2D _rigidbody2D;
    private bool _hasExploded = false; // 중복 폭발 방지 플래그

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 0; // 직선으로 날아가도록 중력 무시
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
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 이미 폭발 과정이 시작되었다면, 추가적인 충돌 처리를 무시합니다.
        if (_hasExploded) return;

        // 발사한 주체(플레이어)와 충돌하는 것을 방지합니다.
        if (other.CompareTag("Player"))
        {
            return;
        }

        // 지정된 태그 중 하나와 일치하는지 확인
        foreach (string tag in collisionTags)
        {
            if (other.CompareTag(tag))
            {
                Explode(other); // 충돌한 대상을 Explode 메서드로 전달
                return; // 중복 폭발 방지를 위해 즉시 반환
            }
        }
    }

    /// <summary>
    /// 폭발을 실행하여 주변의 적에게 피해를 줍니다.
    /// </summary>
    private void Explode(Collider2D directHit = null)
    {
        // 이미 폭발했다면 중복 실행 방지
        if (_hasExploded) return;
        _hasExploded = true;

        // 중앙 EffectManager를 통해 폭발 이펙트를 재생합니다.
        EffectManager.Instance.PlayExplosionEffect(transform.position);

        // GameManager를 통해 카메라를 흔듭니다.
        GameManager.Instance.ShakeCamera(shakeDuration, shakeStrength);

        // 우선, 직접 충돌한 대상이 적인지 확인하여 데미지를 줍니다.
        if (directHit != null && directHit.TryGetComponent<Enemy>(out var enemy))
        {
            Debug.Log(enemy.name + "에게 직접 타격 데미지를 입혔습니다!");
            enemy.TakeDamage(explosionDamage);
        }
       
        // 모든 처리가 끝난 후 오브젝트 파괴
        Destroy(gameObject);
    }
}