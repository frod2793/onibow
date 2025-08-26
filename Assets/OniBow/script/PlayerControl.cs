using System;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Linq;

/// <summary>
/// 2D 환경에 최적화된 플레이어 이동 클래스입니다.
/// Rigidbody2D, DOTween, UniTask를 사용하여 효율적이고 부드러운 움직임을 구현합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerControl : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("최대 이동 속도")]
    [SerializeField] private float maxSpeed = 5f;
    [Tooltip("가속 시간")]
    [SerializeField] private float accelerationTime = 0.2f;
    [Tooltip("감속 시간")]
    [SerializeField] private float decelerationTime = 0.1f;
    [Tooltip("가속 시 적용할 Ease 타입")]
    [SerializeField] private Ease accelerationEase = Ease.OutCubic;
    [Tooltip("감속 시 적용할 Ease 타입")]
    [SerializeField] private Ease decelerationEase = Ease.InCubic;

    [Header("Attack Settings")]
    [Tooltip("발사할 포탄 프리팹")]
    [SerializeField] private GameObject CanonBallPrefb;
    [Tooltip("포물선 발사의 최고 높이")]
    [SerializeField] private float fireArcHeight = 3f;
    [Tooltip("포탄이 목표에 도달하는 시간")]
    [SerializeField] private float fireDuration = 1f;
    [Tooltip("정지 후 반복 발사 간격")]
    [SerializeField] private float fireInterval = 2f;

    [Header("Movement Boundaries")]
    [Tooltip("이동 가능한 최소 X 좌표")]
    [SerializeField] private float minXPosition = -4.0f;
    [Tooltip("이동 가능한 최대 X 좌표")]
    [SerializeField] private float maxXPosition = 4.0f;

    private Rigidbody2D _rigidbody2D;
    private Tween _movementTween;
    private CancellationTokenSource _moveCts;
    private CancellationTokenSource _fireCts;
    private float _lastFireTime = -999f; // 마지막 발사 시간을 기록

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void StartMoving(float direction)
    {
        // 반복 발사 루프를 안전하게 중단하고 정리합니다.
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = null;

        // 이동 루프를 시작합니다.
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _moveCts = new CancellationTokenSource();
        MoveLoopAsync(direction, _moveCts.Token).Forget();
    }

    public void StopMoving()
    {
        // 이동 루프를 중단하고 감속을 시작합니다.
        _moveCts?.Cancel();
        _movementTween?.Kill();

        _movementTween = _rigidbody2D.DOVector(new Vector2(0, _rigidbody2D.linearVelocity.y), decelerationTime)
            .SetEase(decelerationEase)
            .SetUpdate(UpdateType.Fixed)
            .OnComplete(StartRepeatingFire); // 감속 완료 후 반복 발사 시작
    }

    private void StartRepeatingFire()
    {
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(_fireCts.Token).Forget();
    }

    private async UniTaskVoid RepeatingFireLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // 다음 발사까지 남은 시간을 계산합니다.
                float timeUntilReady = (_lastFireTime + fireInterval) - Time.time;

                // 만약 기다려야 한다면, 해당 시간만큼 대기합니다.
                if (timeUntilReady > 0)
                {
                    await UniTask.Delay(System.TimeSpan.FromSeconds(timeUntilReady), cancellationToken: token);
                }

                // 대기 중에 이동이 시작되었다면 루프를 종료합니다.
                if (token.IsCancellationRequested) break;

                // 포탄을 발사하고 마지막 발사 시간을 갱신합니다.
                FireAtNearestEnemy();
                _lastFireTime = Time.time;
            }
        }
        catch (OperationCanceledException)
        {
            // Player starts moving, loop is correctly canceled.
        }
    }

    private void FireAtNearestEnemy()
    {
        if (CanonBallPrefb == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            Vector3 startPos = transform.position;
            Vector3 endPos = nearestEnemy.transform.position;
            Vector3 controlPoint = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
            Vector3[] path = { controlPoint, endPos };

            GameObject cannonball = Instantiate(CanonBallPrefb, startPos, Quaternion.identity);
            cannonball.transform.DOPath(path, fireDuration, PathType.CatmullRom)
                .SetEase(Ease.Linear)
                .OnComplete(() => Destroy(cannonball));
        }
    }

    private GameObject FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length == 0) return null;

        return enemies.OrderBy(enemy =>
            Vector2.Distance(transform.position, enemy.transform.position)
        ).FirstOrDefault();
    }

    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        float targetVelocityX = direction * maxSpeed;

        _movementTween?.Kill();
        _movementTween = _rigidbody2D.DOVector(new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y), accelerationTime)
            .SetEase(accelerationEase)
            .SetUpdate(UpdateType.Fixed);

        try
        {
            while (_movementTween.IsActive() && !token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }

            if (token.IsCancellationRequested) return;

            while (!token.IsCancellationRequested)
            {
                _rigidbody2D.linearVelocity = new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y);
                ClampPosition();
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Canceled correctly.
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minXPosition, maxXPosition);
        _rigidbody2D.position = clampedPosition;
    }

    private void OnDestroy()
    {
        // 모든 CancellationTokenSource를 안전하게 정리합니다.
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _movementTween?.Kill();
    }
}

public static class Rigidbody2DExtensions
{
    public static Tween DOVector(this Rigidbody2D rb, Vector2 endValue, float duration)
    {
        return DOTween.To(() => rb.linearVelocity, x => rb.linearVelocity = x, endValue, duration);
    }
}
