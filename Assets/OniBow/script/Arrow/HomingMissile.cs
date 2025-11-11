using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using Random = UnityEngine.Random;

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
    private CancellationTokenSource _lifeTimeCts;
    private float _randomStartTime; // 각 미사일의 비행 패턴을 다르게 하기 위한 오프셋
    private AfterimageEffect _afterimageEffect; // 잔상 효과 참조
    private bool _isHoming = false; // 추적 시작 플래그
    private bool _hasExploded = false; // 중복 폭발 방지 플래그

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        // DOTween으로 Transform을 직접 제어하므로, 물리 엔진과의 충돌을 피하기 위해 BodyType을 Kinematic으로 설정합니다.
        _rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
        _afterimageEffect = GetComponent<AfterimageEffect>();
        _rigidbody2D.gravityScale = 0;

    }

    private void OnEnable()
    {
        // 상태 초기화
        _isHoming = false;
        _hasExploded = false;

        // 각 미사일이 다른 패턴으로 움직이도록 랜덤 값을 부여합니다.
        _randomStartTime = Random.Range(0f, 10f);

        // 발사 시 의도치 않은 즉시 충돌을 방지하기 위해, 추적이 시작될 때까지 충돌체를 비활성화합니다.
        _collider.enabled = false;

        // 일정 시간 후 자동 비활성화 (풀링을 위해 Destroy 대신 사용)
        _lifeTimeCts?.Cancel();
        _lifeTimeCts = new CancellationTokenSource();
        DisableAfterDelay(_lifeTimeCts.Token).Forget();
    }

    /// <summary>
    /// 추적할 목표를 설정하고 발사를 시작합니다. 이 메서드는 SkillManager에서 호출됩니다.
    /// </summary>
    /// <param name="target">추적할 대상의 Transform</param>
    public void Launch(Transform target, Transform firePoint)
    {
        // 1. 부모로부터 분리하여 스케일(크기) 왜곡을 방지합니다.
        if (transform.parent != null) transform.SetParent(null);
        Quaternion identity = Quaternion.identity;
        transform.position = firePoint.position;
        // 2. 회전 상태를 초기화하여 발사 애니메이션이 항상 동일한 조건에서 시작하도록 합니다.
        transform.rotation = identity;
        // 3. 매번 새로운 비행 패턴을 갖도록 랜덤 시드를 리셋합니다.
        _randomStartTime = Random.Range(0f, 10f);

        _target = target;

        // 발사 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.MissileLaunchSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.MissileLaunchSfx);
        }

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

    private async UniTaskVoid DisableAfterDelay(CancellationToken token)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(lifeTime), cancellationToken: token);
            if (gameObject.activeSelf)
            {
                ObjectPoolManager.Instance.Return(gameObject);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void FixedUpdate()
    {
        // 초기 발사 애니메이션(DOTween)이 끝나기 전까지는 추적 로직을 실행하지 않습니다.
        if (!_isHoming)
        {
            return;
        }
        
        HandleHoming();
    }

    /// <summary>
    /// 목표물을 향한 추적 및 이동 로직을 처리합니다.
    /// </summary>
    private void HandleHoming()
    {
        Vector2 moveDirection;
        Vector2 currentRigidbodyPosition = _rigidbody2D.position; // Rigidbody 위치 캐싱

        // 목표가 유효한 경우, S-커브를 그리며 목표를 향해 회전합니다.
        if (_target != null && _target.gameObject.activeInHierarchy)
        {
            Vector2 targetPosition = _target.position; // 목표 위치 캐싱
            Vector2 directionToTarget = targetPosition - currentRigidbodyPosition;
            Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;
            float sineOffset = Mathf.Sin((Time.time + _randomStartTime) * waveFrequency) * waveAmplitude;

            Vector2 aimPoint = targetPosition + perpendicular * sineOffset;
            Vector2 finalDirection = (aimPoint - currentRigidbodyPosition).normalized;

            // 목표 방향으로 점진적 회전
            float targetAngle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);
        }

        // 항상 미사일의 앞 방향(transform.right)으로 이동 방향을 설정합니다.
        moveDirection = transform.right;

        // Rigidbody의 위치를 물리적으로 안전하게 업데이트합니다.
        _rigidbody2D.MovePosition(currentRigidbodyPosition + moveDirection * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hasExploded) return;

        // 적과 충돌 시 파괴 (다른 트리거와 충돌 방지)
        if (other.CompareTag("Enemy")) // TODO: 향후 확장성을 위해 태그 목록을 사용하는 것을 고려
        {
            Explode(other);
        }
    }

    /// <summary>
    /// 미사일 폭발 효과를 처리하고 오브젝트를 풀에 반환합니다.
    /// </summary>
    private void Explode(Collider2D hitTarget)
    {
        _hasExploded = true;
            // 이펙트 매니저를 통해 폭발 이펙트 재생
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayHomingMissileExplosion(transform.position);
            }

            // 폭발 사운드 재생
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.MissileExplosionSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.MissileExplosionSfx);
            }

            // 적에게 데미지 처리
        if (hitTarget != null && hitTarget.TryGetComponent<Enemy>(out var enemy))
            {
                enemy.TakeDamage(damage);
            }

            // 이 오브젝트에서 실행 중인 모든 DOTween 애니메이션을 정리합니다.
            DOTween.Kill(transform);
        _lifeTimeCts?.Cancel(); // 자동 비활성화 코루틴 중단

            // 오브젝트 풀에 반환
            ObjectPoolManager.Instance.Return(gameObject);
    }
}
