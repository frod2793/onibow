using System;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Threading;

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

    [Header("Movement Boundaries")]
    [Tooltip("이동 가능한 최소 X 좌표")]
    [SerializeField] private float minXPosition = -4.0f;
    [Tooltip("이동 가능한 최대 X 좌표")]
    [SerializeField] private float maxXPosition = 4.0f;

    private Rigidbody2D _rigidbody2D;
    private Tween _movementTween;
    private CancellationTokenSource _moveCts;

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    public void StartMoving(float direction)
    {
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _moveCts = new CancellationTokenSource();

        MoveLoopAsync(direction, _moveCts.Token).Forget();
    }

    public void StopMoving()
    {
        _moveCts?.Cancel();
        _movementTween?.Kill();

        _movementTween = _rigidbody2D.DOVector(new Vector2(0, _rigidbody2D.linearVelocity.y), decelerationTime)
            .SetEase(decelerationEase)
            .SetUpdate(UpdateType.Fixed);
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
        _moveCts?.Cancel();
        _moveCts?.Dispose();
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
