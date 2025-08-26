using System;
using UnityEngine;
using DG.Tweening;
using DG.Tweening.Core;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 2D 환경에 최적화된 플레이어 이동 및 공격 클래스입니다.
/// Rigidbody2D, DOTween, UniTask를 사용하여 효율적이고 부드러운 움직임을 구현합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerControl : MonoBehaviour
{
    #region 변수 및 속성

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
    [SerializeField] private float fireDuration = 1.5f;
    [Tooltip("포물선 운동의 속도 변화를 제어하는 커브. 정점에서 속도가 0에 가까워지도록 설정되었습니다.")]
    [SerializeField] private AnimationCurve fireEaseCurve = new AnimationCurve(new Keyframe(0, 0, 0, 2f), new Keyframe(0.5f, 0.5f, 0, 0), new Keyframe(1, 1, 2f, 0));
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

    #endregion

    #region Unity 메시지

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void OnDestroy()
    {
        // 모든 CancellationTokenSource와 DOTween 트윈을 안전하게 정리합니다.
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _movementTween?.Kill();
    }

    #endregion

    #region 공개 메서드

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

    #endregion

    #region 내부 로직

    private void StartRepeatingFire()
    {
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(_fireCts.Token).Forget();
    }

    private void FireAtNearestEnemy()
    {
        if (CanonBallPrefb == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            Vector3 startPos = transform.position;
            Vector3 endPos = nearestEnemy.transform.position;

            // 포물선의 정점과 베지어 제어점 계산
            Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
            Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

            GameObject cannonball = Instantiate(CanonBallPrefb, startPos, Quaternion.identity);
            if (cannonball == null) return;

            float t = 0f; // 0에서 1까지 보간될 값

            DOGetter<float> getter = () => t;
            DOSetter<float> setter = (x) =>
            {
                t = x;
                if (cannonball == null) return;
                float oneMinusT = 1f - t;
                // 2차 베지어 곡선 공식
                cannonball.transform.position = oneMinusT * oneMinusT * startPos +
                                                2f * oneMinusT * t * controlPoint +
                                                t * t * endPos;
            };

            // 단일 트윈과 AnimationCurve를 사용하여 부드러운 움직임 구현
            DOTween.To(getter, setter, 1f, fireDuration)
                    .SetEase(fireEaseCurve)
                    .OnComplete(() =>
                    {
                        if (cannonball != null) Destroy(cannonball);
                    });
        }
    }

    private GameObject FindNearestEnemy()
    {
        // [최적화 제안] 적이 많아질 경우, 매번 FindGameObjectsWithTag를 호출하는 것은 성능에 부담이 될 수 있습니다.
        // 더 나은 방법은 EnemyManager 클래스나 static 리스트를 만들어, 활성화된 적들의 목록을 직접 관리하는 것입니다.
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length == 0) return null;

        GameObject nearestEnemy = null;
        float minDistanceSqr = float.MaxValue;

        // [최적화] Vector2.Distance 대신 sqrMagnitude를 사용하여 불필요한 제곱근 연산을 피합니다.
        foreach (GameObject enemy in enemies)
        {
            float distanceSqr = (enemy.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr < minDistanceSqr)
            {
                minDistanceSqr = distanceSqr;
                nearestEnemy = enemy;
            }
        }
        return nearestEnemy;
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minXPosition, maxXPosition);
        _rigidbody2D.position = clampedPosition;
    }

    #endregion

    #region 코루틴 및 비동기 메서드

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
                    // [최적화] TimeSpan.FromSeconds 대신 밀리초 단위 정수 값을 사용하여 불필요한 메모리 할당을 방지합니다.
                    await UniTask.Delay((int)(timeUntilReady * 1000), cancellationToken: token);
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

    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        float targetVelocityX = direction * maxSpeed;

        _movementTween?.Kill();
        _movementTween = _rigidbody2D.DOVector(new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y), accelerationTime)
            .SetEase(accelerationEase)
            .SetUpdate(UpdateType.Fixed);

        try
        {
            // 가속 트윈이 끝날 때까지 기다립니다.
            while (_movementTween.IsActive() && !token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }

            // 트윈이 취소되었다면, 여기서 중단합니다.
            if (token.IsCancellationRequested) return;

            // 트윈이 끝난 후, 수동으로 속도를 유지합니다.
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

    #endregion
}

#region 확장 메서드

public static class Rigidbody2DExtensions
{
    public static Tween DOVector(this Rigidbody2D rb, Vector2 endValue, float duration)
    {
        return DOTween.To(() => rb.linearVelocity, x => rb.linearVelocity = x, endValue, duration);
    }
}

#endregion
