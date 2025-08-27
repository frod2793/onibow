using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// 플레이어를 공격하는 적 AI 클래스입니다.
/// 자신의 화살 궤적을 기준으로 최적의 공격 위치로 이동한 후 공격하는 패턴을 반복합니다.
/// UniTask와 DOTween을 사용하여 비동기적으로 동작합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    #region 변수
    public enum EnemyState
    {
        Idle,
        Moving,
        Attacking
    }

    [Header("AI 설정")]
    public EnemyState currentState = EnemyState.Idle;
    public Transform player;
    public float moveSpeed = 3f;
    [Tooltip("공격 위치로 간주할 거리의 허용 오차 범위입니다.")]
    [SerializeField] private float distanceTolerance = 0.5f;

    [Header("지형 설정")]
    [Tooltip("지면을 감지할 레이어 마스크")]
    [SerializeField] private LayerMask groundLayer;
    private float _minXPosition;
    private float _maxXPosition;

    [Header("공격 설정")]
    [SerializeField] private Transform firePoint;
    [Tooltip("화살이 날아가는 고정 거리. 적의 이동 및 공격 위치 선정의 기준이 됩니다.")]
    [SerializeField] private float fireDistance = 7f;
    [SerializeField] private float fireArcHeight = 3f;
    [SerializeField] private float fireDuration = 1f;
    [SerializeField] private AnimationCurve fireEaseCurve = new AnimationCurve(new Keyframe(0, 0, 0, 2f), new Keyframe(0.5f, 0.5f, 0, 0), new Keyframe(1, 1, 2f, 0));
    [SerializeField] private float attackCooldown = 2f;

    private SPUM_Prefabs _enemyAnimation;
    private Rigidbody2D _rigidbody2D;
    private CancellationTokenSource _aiTaskCts;
    private bool _isAction; // 액션 애니메이션 재생 중인지 확인하는 플래그
    #endregion

    #region MonoBehaviour 콜백
    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _enemyAnimation = GetComponent<SPUM_Prefabs>();
        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (_enemyAnimation != null)
        {
            if (!_enemyAnimation.allListsHaveItemsExist())
            {
                _enemyAnimation.PopulateAnimationLists();
            }
            _enemyAnimation.OverrideControllerInit();
            if (_enemyAnimation._anim == null)
            {
                Debug.LogError("적의 SPUM_Prefabs에 Animator 참조가 없습니다!");
            }
        }
        else
        {
            Debug.LogError("적 오브젝트에서 SPUM_Prefabs 컴포넌트를 찾을 수 없습니다. 애니메이션이 동작하지 않습니다.");
        }
    }

    void Start()
    {
        if (player == null)
        {
            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if(playerObject != null) player = playerObject.transform;
        }

        if (firePoint == null) firePoint = transform;

        DetectGroundBoundaries();

        _aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(_aiTaskCts.Token).Forget();
    }

    private void Update()
    {
        if (_isAction || _enemyAnimation == null) return;

        PlayerState animationState = currentState == EnemyState.Moving ? PlayerState.MOVE : PlayerState.IDLE;
        _enemyAnimation.PlayAnimation(animationState, 0);
    }

    private void OnDestroy()
    {
        _aiTaskCts?.Cancel();
        _aiTaskCts?.Dispose();
    }
    #endregion

    #region AI 핵심 로직

    /// <summary>
    /// 적의 행동을 결정하는 메인 AI 루프입니다.
    /// 1. 화살이 플레이어에게 닿을 수 있는지 궤적을 검사합니다 (수평 거리 비교).
    /// 2. 맞출 수 없다면, 맞출 수 있는 위치로 이동합니다.
    /// 3. 맞출 수 있다면, 공격을 수행합니다.
    /// 이 과정을 계속 반복합니다.
    /// </summary>
    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (player == null || _isAction)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                continue;
            }

            float horizontalDistanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);

            // 화살 발사 거리(fireDistance)를 기준으로 이동 여부 결정
            if (Mathf.Abs(horizontalDistanceToPlayer - fireDistance) > distanceTolerance)
            {
                await MoveToAttackPositionAsync(token);
            }
            else // 공격 가능 범위 안에 있으면 공격
            {
                await AttackAsync(token);
            }
        }
    }

    private async UniTask MoveToAttackPositionAsync(CancellationToken token)
    {
        currentState = EnemyState.Moving;

        while (!token.IsCancellationRequested && currentState == EnemyState.Moving)
        {
            if (player == null) break;

            float horizontalDistanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);
            if (Mathf.Abs(horizontalDistanceToPlayer - fireDistance) <= distanceTolerance)
            {
                break;
            }

            float xDirection = Mathf.Sign(player.position.x - transform.position.x);
            float moveDirection = (horizontalDistanceToPlayer > fireDistance) ? 1f : -1f;
            float targetXVelocity = xDirection * moveSpeed * moveDirection;

            FlipSprite(targetXVelocity); // 이동 방향으로 회전

            if ((transform.position.x <= _minXPosition && targetXVelocity < 0) || (transform.position.x >= _maxXPosition && targetXVelocity > 0))
            {
                targetXVelocity = 0;
            }

            _rigidbody2D.velocity = new Vector2(targetXVelocity, _rigidbody2D.velocity.y);
            ClampPosition();

            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
        }

        _rigidbody2D.velocity = new Vector2(0, _rigidbody2D.velocity.y);
        currentState = EnemyState.Idle;
    }

    private async UniTask AttackAsync(CancellationToken token)
    {
        currentState = EnemyState.Attacking;
        transform.rotation = Quaternion.Euler(0, 0, 0); // 공격 시에는 항상 왼쪽을 바라보도록 강제

        PlayAttackAnimationAsync(token).Forget();
        await UniTask.Delay((int)(attackCooldown * 1000), cancellationToken: token);
    }

    private async UniTaskVoid PlayAttackAnimationAsync(CancellationToken token)
    {
        _isAction = true;
        _enemyAnimation.PlayAnimation(PlayerState.ATTACK, 0);
        PerformArrowLaunch();

        var attackClip = _enemyAnimation.ATTACK_List.Count > 0 ? _enemyAnimation.ATTACK_List[0] : null;
        if (attackClip != null)
        {
            await UniTask.Delay((int)(attackClip.length * 1000), cancellationToken: token);
        }

        _isAction = false;
        currentState = EnemyState.Idle;
    }

    #endregion

    #region 보조 메소드

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, _minXPosition, _maxXPosition);
        _rigidbody2D.position = clampedPosition;
    }

    private void PerformArrowLaunch()
    {
        if (ArrowPool.Instance == null || player == null) return;

        Vector3 startPos = firePoint.position;
        // 공격 방향은 플레이어를 향하지만, 발사 거리(endPos)는 고정입니다.
        Vector2 direction = (player.position - startPos).normalized;
        Vector3 endPos = startPos + new Vector3(direction.x, direction.y, 0).normalized * fireDistance;

        Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
        Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

        GameObject arrowObject = ArrowPool.Instance.Get();
        if (arrowObject == null) return;

        arrowObject.transform.SetPositionAndRotation(startPos, Quaternion.identity);
        var arrowController = arrowObject.GetComponent<ArrowController>();
        if (arrowController != null)
        {
            arrowController.Launch(startPos, controlPoint, endPos, fireDuration, fireEaseCurve);
        }
        else
        {
            ArrowPool.Instance.Return(arrowObject);
        }
    }

    private void DetectGroundBoundaries()
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 5f, groundLayer);

        if (hit.collider != null)
        {
            Collider2D groundCollider = hit.collider;
            _minXPosition = groundCollider.bounds.min.x;
            _maxXPosition = groundCollider.bounds.max.x;
        }
        else
        {
            Debug.LogWarning("적 아래에서 지면을 찾을 수 없습니다. 이동 범위가 제한되지 않습니다.", this);
            _minXPosition = -Mathf.Infinity;
            _maxXPosition = Mathf.Infinity;
        }
    }

    /// <summary>
    /// 이동 방향에 따라 적의 스프라이트 방향을 전환합니다. (기본 스프라이트는 왼쪽을 바라봄)
    /// </summary>
    /// <param name="horizontalDirection">수평 이동 방향 (양수: 오른쪽, 음수: 왼쪽)</param>
    private void FlipSprite(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) > 0.01f)
        {
            // 오른쪽으로 이동 시 Y축 180도 회전, 왼쪽 이동 시 0도
            transform.rotation = Quaternion.Euler(0, horizontalDirection > 0 ? 180f : 0f, 0);
        }
    }

    #endregion
}
