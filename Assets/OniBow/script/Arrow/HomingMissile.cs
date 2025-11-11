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
    [SerializeField] private int damage = 10;
    [SerializeField] private float speed = 4f;
    [SerializeField] private float rotateSpeed = 200f;
    [SerializeField] private float lifeTime = 10f;

    [Header("S-커브 비행 설정")]
    [SerializeField] private float waveFrequency = 2f;
    [SerializeField] private float waveAmplitude = 1.5f;

    [Header("초기 발사 설정")]
    [SerializeField] private float initialLaunchDistance = 1.5f;
    [SerializeField] private float initialLaunchDuration = 0.3f;

    private Transform _target;
    private Rigidbody2D _rigidbody2D;
    private Collider2D _collider;
    private CancellationTokenSource _lifeTimeCts;
    private float _randomStartTime;
    private AfterimageEffect _afterimageEffect;
    private bool _isHoming = false;
    private bool _hasExploded = false;

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        _rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
        _afterimageEffect = GetComponent<AfterimageEffect>();
        _rigidbody2D.gravityScale = 0;
    }

    private void OnEnable()
    {
        _isHoming = false;
        _hasExploded = false;
        _randomStartTime = Random.Range(0f, 10f);
        _collider.enabled = false;
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
        if (transform.parent != null) transform.SetParent(null);
        Quaternion identity = Quaternion.identity;
        transform.position = firePoint.position;
        transform.rotation = identity;
        _randomStartTime = Random.Range(0f, 10f);

        _target = target;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.MissileLaunchSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.MissileLaunchSfx);
        }

        if (_afterimageEffect != null)
            _afterimageEffect.StartEffect(lifeTime);

        Sequence launchSequence = DOTween.Sequence();

        launchSequence.Append(transform.DORotate(new Vector3(0, 0, 90), 0.1f));
        launchSequence.Append(transform.DOMoveY(transform.position.y + initialLaunchDistance, initialLaunchDuration).SetEase(Ease.OutSine));
        launchSequence.OnComplete(() => {
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
        Vector2 currentRigidbodyPosition = _rigidbody2D.position;

        if (_target != null && _target.gameObject.activeInHierarchy)
        {
            Vector2 targetPosition = _target.position;
            Vector2 directionToTarget = targetPosition - currentRigidbodyPosition;
            Vector2 perpendicular = Vector2.Perpendicular(directionToTarget).normalized;
            float sineOffset = Mathf.Sin((Time.time + _randomStartTime) * waveFrequency) * waveAmplitude;

            Vector2 aimPoint = targetPosition + perpendicular * sineOffset;
            Vector2 finalDirection = (aimPoint - currentRigidbodyPosition).normalized;

            float targetAngle = Mathf.Atan2(finalDirection.y, finalDirection.x) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0, 0, targetAngle);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.fixedDeltaTime);
        }

        moveDirection = transform.right;
        _rigidbody2D.MovePosition(currentRigidbodyPosition + moveDirection * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hasExploded) return;

        if (other.CompareTag("Enemy"))
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
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayHomingMissileExplosion(transform.position);
        }

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.MissileExplosionSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.MissileExplosionSfx);
        }

        if (hitTarget != null && hitTarget.TryGetComponent<Enemy>(out var enemy))
        {
            enemy.TakeDamage(damage);
        }

        DOTween.Kill(transform);
        _lifeTimeCts?.Cancel();
        ObjectPoolManager.Instance.Return(gameObject);
    }
}
