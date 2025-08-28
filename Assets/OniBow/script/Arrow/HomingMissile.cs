using UnityEngine;
using DG.Tweening;

/// <summary>
/// 지정된 목표물을 향해 S자 곡선을 그리며 날아가는 추적 미사일 클래스입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class HomingMissile : MonoBehaviour
{
    [Header("추적 설정")]
    [SerializeField] private int damage = 10;           // 미사일 데미지
    [SerializeField] private float speed = 4f;          // 이동 속도
    [SerializeField] private float rotateSpeed = 200f;  // 회전 속도
    [SerializeField] private float lifeTime = 10f;      // 최대 생존 시간

    [Header("S-커브 비행 설정")]
    [SerializeField] private float waveFrequency = 2f;  // S자 곡선의 빈도 (클수록 더 자주 굽이침)
    [SerializeField] private float waveAmplitude = 1.5f; // S자 곡선의 폭 (클수록 더 넓게 굽이침)

    [Header("초기 발사 설정")]
    [SerializeField] private float initialLaunchDistance = 1.5f; // 초기 위로 발사되는 거리
    [SerializeField] private float initialLaunchDuration = 0.3f; // 초기 위로 발사되는 시간

    private Transform _target;
    private Rigidbody2D _rigidbody2D;
    private Collider2D _collider;
    private float _randomStartTime; // 각 미사일의 비행 패턴을 다르게 하기 위한 오프셋
    private AfterimageEffect _afterimageEffect; // 잔상 효과 참조
    private bool _isHoming = false; // 추적 시작 플래그

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        // DOTween으로 Transform을 직접 제어하므로, 물리 엔진과의 충돌을 피하기 위해 Kinematic으로 설정합니다.
        _rigidbody2D.isKinematic = true;
        _afterimageEffect = GetComponent<AfterimageEffect>();
        _rigidbody2D.gravityScale = 0;

        // 각 미사일이 다른 패턴으로 움직이도록 랜덤 값을 부여합니다.
        _randomStartTime = Random.Range(0f, 10f);

        // 발사 시 의도치 않은 즉시 충돌을 방지하기 위해, 추적이 시작될 때까지 충돌체를 비활성화합니다.
        _collider.enabled = false;

        Destroy(gameObject, lifeTime); // 일정 시간 후 자동 파괴
    }

    /// <summary>
    /// 추적할 목표를 설정하고 발사를 시작합니다. 이 메서드는 SkillManager에서 호출됩니다.
    /// </summary>
    /// <param name="target">추적할 대상의 Transform</param>
    public void Launch(Transform target)
    {
        // [수정] 오브젝트 풀에서 재사용될 때 발생할 수 있는 문제를 방지하기 위해 상태를 초기화합니다.
        // 1. 부모로부터 분리하여 스케일(크기) 왜곡을 방지합니다.
        transform.SetParent(null);
        // 2. 회전 상태를 초기화하여 발사 애니메이션이 항상 동일한 조건에서 시작하도록 합니다.
        transform.rotation = Quaternion.identity;
        // 3. 매번 새로운 비행 패턴을 갖도록 랜덤 시드를 리셋합니다.
        _randomStartTime = Random.Range(0f, 10f);

        _target = target;

        // 초기 발사 시 잔상 효과를 시작합니다.
        _afterimageEffect?.StartEffect(lifeTime);

        // DOTween 시퀀스를 사용하여 초기 발사 애니메이션을 만듭니다.
        Sequence launchSequence = DOTween.Sequence();

        // 1. 즉시 위를 보도록 회전합니다.
        launchSequence.Append(transform.DORotate(new Vector3(0, 0, 90), 0.1f));
        
        // 2. 설정된 거리만큼 위로 이동합니다.
        launchSequence.Append(transform.DOMoveY(transform.position.y + initialLaunchDistance, initialLaunchDuration).SetEase(Ease.OutSine));

        // 3. 애니메이션이 끝나면 추적을 시작하도록 플래그를 설정합니다.
        launchSequence.OnComplete(() => {
            // 추적을 시작하기 직전에 충돌체를 활성화합니다.
            _collider.enabled = true;
            _isHoming = true;
        });
    }

    private void FixedUpdate()
    {
        // 초기 발사 애니메이션(DOTween)이 끝나기 전까지는 추적 로직을 실행하지 않습니다.
        if (!_isHoming)
        {
            return;
        }

        // 목표가 없거나 비활성화되면 회전을 멈추고 직진합니다.
        if (_target == null || !_target.gameObject.activeInHierarchy)
        {
            transform.Translate(transform.right * speed * Time.fixedDeltaTime, Space.World);
            return;
        }

        // --- 추적 로직 (비-물리 기반) ---
        Vector2 directionToTarget = (Vector2)_target.position - (Vector2)transform.position;

        Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;
        float sineOffset = Mathf.Sin((Time.time + _randomStartTime) * waveFrequency) * waveAmplitude;

        Vector2 aimPoint = (Vector2)_target.position + perpendicular * sineOffset;
        Vector2 finalDirection = (aimPoint - (Vector2)transform.position).normalized;

        // 목표 방향으로 점진적 회전 (DOTween 대신 표준적인 부드러운 회전 방식 사용)
        float targetAngle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);

        // 항상 미사일의 앞 방향으로 이동
        transform.Translate(transform.right * speed * Time.fixedDeltaTime, Space.World);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 적과 충돌 시 파괴 (다른 트리거와 충돌 방지)
        if (other.CompareTag("Enemy"))
        {
            // 이펙트 매니저를 통해 폭발 이펙트 재생
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayHomingMissileExplosion(transform.position);
            }

            // 적에게 데미지 처리
            if (other.TryGetComponent<Enemy>(out var enemy))
            {
                enemy.TakeDamage(damage);
            }

            // 이 오브젝트에서 실행 중인 모든 DOTween 애니메이션을 정리합니다.
            DOTween.Kill(transform);
            Destroy(gameObject); // 자기 자신을 파괴
        }
    }
}
