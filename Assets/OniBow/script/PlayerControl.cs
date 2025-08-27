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
    [Tooltip("발사할 화살 프리팹")]
    [SerializeField] private Transform firePoint;
    [Tooltip("화살이 날아가는 고정 거리")]
    [SerializeField] private float fireDistance = 7f;
    [Tooltip("포물선 발사의 최고 높이")]
    [SerializeField] private float fireArcHeight = 3f;
    [Tooltip("화살이 목표 지점까지 도달하는 시간")]
    [SerializeField] private float fireDuration = 1f;
    [Tooltip("포물선 운동의 속도 변화를 제어하는 커브")]
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
    private SPUM_Prefabs _spumPrefabs; // 애니메이션 제어를 위한 참조
    private PlayerState _currentState; // 현재 플레이어 상태
    private bool _isAction; // 액션 애니메이션 재생 중인지 확인하는 플래그

    #endregion

    #region Unity 메시지

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _spumPrefabs = GetComponentInChildren<SPUM_Prefabs>();

        // SPUM 애니메이션 시스템 초기화
        if (_spumPrefabs != null)
        {
            if(!_spumPrefabs.allListsHaveItemsExist()){
                _spumPrefabs.PopulateAnimationLists();
            }
            _spumPrefabs.OverrideControllerInit();
            if (_spumPrefabs._anim == null)
            {
                Debug.LogError("SPUM_Prefabs has a null Animator reference!");
            }
        }
        else
        {
            Debug.LogError("SPUM_Prefabs component not found in children. Animations will not work.");
        }

        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
        _currentState = PlayerState.IDLE; // 초기 상태를 IDLE로 설정
    }

    private void Update()
    {
        if (_isAction) return; // 액션 중에는 상태 애니메이션 갱신 안함

        // IDLE, MOVE 같은 지속적인 상태는 Update에서 계속 애니메이션을 갱신해줍니다.
        if (_spumPrefabs != null)
        {
            _spumPrefabs.PlayAnimation(_currentState, 0);
        }
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
        _isAction = false;
        _currentState = PlayerState.MOVE;

        // 캐릭터 방향 전환
        if (_spumPrefabs != null)
        {
            _spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

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
        _isAction = false;
        _currentState = PlayerState.IDLE;

        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(_fireCts.Token).Forget();
    }

    private void FireAtNearestEnemy()
    {
        // [최적화] 오브젝트 풀이 없으면 공격을 실행하지 않습니다.
        if (ArrowPool.Instance == null)
        {
            Debug.LogError("ArrowPool.Instance가 씬에 존재하지 않습니다.");
            return;
        }

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            // 공격 전, 가장 가까운 적의 방향으로 캐릭터를 회전시킵니다.
            if (_spumPrefabs != null)
            {
                float directionToEnemyX = nearestEnemy.transform.position.x - transform.position.x;
                // directionToEnemyX > 0 이면 오른쪽(180도), 아니면 왼쪽(0도)을 보도록 설정합니다.
                _spumPrefabs.transform.rotation = Quaternion.Euler(0f, directionToEnemyX > 0 ? 180f : 0f, 0f);
            }

            PlayAttackAnimationAsync().Forget();

            // 화살의 출발점을 firePoint로 설정합니다. firePoint가 없으면 플레이어의 위치를 사용합니다.
            Vector3 startPos = firePoint != null ? firePoint.position : transform.position;
            
            // 적의 위치는 방향을 결정하는 데만 사용합니다.
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;
            // 최종 목표 지점은 출발점에서 고정된 거리만큼 떨어진 곳입니다.
            Vector3 endPos = startPos + (Vector3)direction * fireDistance;

            // 포물선의 정점과 베지어 제어점 계산
            Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
            Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

            // [최적화] Instantiate 대신 오브젝트 풀에서 화살을 가져옵니다.
            GameObject arrowObject = ArrowPool.Instance.Get();
            if (arrowObject == null) return;
            arrowObject.transform.position = startPos;
            arrowObject.transform.rotation = Quaternion.identity;

            // ArrowController를 통해 발사 로직을 위임하여 메모리 최적화
            ArrowController arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Launch(startPos, controlPoint, endPos, fireDuration, fireEaseCurve);
            }
            else
            {
                Debug.LogError("Arrow Prefab에 ArrowController 컴포넌트가 없습니다! 오브젝트를 풀에 즉시 반환합니다.");
                ArrowPool.Instance.Return(arrowObject); // 컨트롤러가 없으면 즉시 반환
            }
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

    private async UniTaskVoid PlayAttackAnimationAsync()
    {
        if (_spumPrefabs == null) return;

        _isAction = true;
        // [최적화] Rebind()는 매우 무거운 연산이며, 매 공격마다 호출할 필요가 없습니다.
        // 애니메이션 상태 전환은 Animator Controller가 담당하므로 이 코드를 제거하여 성능을 향상시킵니다.
        // _spumPrefabs._anim.Rebind();
        _spumPrefabs.PlayAnimation(PlayerState.ATTACK, 0);

        var attackClips = _spumPrefabs.StateAnimationPairs[PlayerState.ATTACK.ToString()];
        if (attackClips != null && attackClips.Count > 0)
        {
            var clip = attackClips[0];
            if (clip != null)
            {
                await UniTask.Delay((int)(clip.length * 1000), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
        }

        _isAction = false;
        _currentState = PlayerState.IDLE; // 공격 후 IDLE 상태로 복귀
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
