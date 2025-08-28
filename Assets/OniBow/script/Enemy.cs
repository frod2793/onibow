using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;

/// <summary>
/// 플레이어를 공격하는 적 AI 클래스입니다.
/// 자신의 화살 궤적을 기준으로 최적의 공격 위치로 이동한 후 공격하는 패턴을 반복합니다.
/// UniTask를 사용하여 비동기적으로 동작합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    #region 변수
    public static event Action<Enemy> OnEnemyDestroyed;
 
    public enum EnemyState
    {
        Idle,
        Moving,
        Attacking,
        Healing,
        Evading,
        Damaged,
        Dead
    }

    [Header("체력 설정")]
    [SerializeField] private int maxHp = 150;
    [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
    [SerializeField] private float tempHpDecreaseDelay = 3f;
    [Tooltip("예비 체력이 현재 체력을 따라잡는 속도 (초당 체력)")]
    [SerializeField] private float tempHpCatchUpSpeed = 50f;
    private int _currentHp;
    private int _tempHp;
    private float _lastDamageTime;
    public event Action<int, int, int> OnHpChanged; // (현재, 예비, 최대)

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

    [Header("스킬 설정")]
    [SerializeField] private float skillCooldown = 10f;
    [Range(0, 1)]
    [SerializeField] private float skillChance = 0.3f; // 30% 확률로 스킬 사용
    private float _lastSkillUseTime = -999f;
    [SerializeField] private float healSkillCooldown = 20f;
    [SerializeField, Range(0, 1)] private float healHealthThreshold = 0.4f; // 체력이 40% 이하일 때 회복 시도
    private float _lastHealTime = -999f;

    [Header("회피 설정")]
    [Tooltip("플레이어의 공격을 회피할 확률 (0.0 ~ 1.0)")]
    [Range(0, 1)]
    [SerializeField] private float evadeChance = 0.3f;
    [Tooltip("회피 대쉬 속도")]
    [SerializeField] private float evadeDashSpeed = 15f;
    [Tooltip("회피 대쉬 최대 지속 시간")]
    [SerializeField] private float evadeDashDuration = 0.25f;

    private SPUM_Prefabs _enemyAnimation;
    private Rigidbody2D _rigidbody2D;
    private Collider2D _collider;
    private AfterimageEffect _afterimageEffect;
    private CancellationTokenSource _aiTaskCts;
    private bool _isDead;
    public bool IsDead => _isDead;
    #endregion

    #region MonoBehaviour 콜백
    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        _afterimageEffect = GetComponent<AfterimageEffect>();
        if (_afterimageEffect == null)
        {
            Debug.LogWarning("Enemy에 AfterimageEffect 컴포넌트가 없습니다. 회피 잔상 효과가 동작하지 않습니다.");
        }
        _enemyAnimation = GetComponent<SPUM_Prefabs>();
        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
        _currentHp = maxHp;
        _tempHp = maxHp;

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
            if (playerObject != null) player = playerObject.transform;
        }

        if (firePoint == null) firePoint = transform;

        DetectGroundBoundaries();
        ForceUpdateHpUI();

        _aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(_aiTaskCts.Token).Forget();
    }

    private void Update()
    {
        if (_isDead) return;

        UpdateTempHp();
        CheckIfOffScreen();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isDead && (other.CompareTag("Arrow") || other.CompareTag("PlayerArrow")))
        {
            TakeDamage(10);
        }
    }

    private void OnDestroy()
    {
        _aiTaskCts?.Cancel();
        _aiTaskCts?.Dispose();
        OnEnemyDestroyed?.Invoke(this);
    }
    #endregion

    #region 공개 메서드
    public void TakeDamage(int damage)
    {
        // [회피 우선권] 회피, 피격, 사망 중에는 새로운 데미지나 회피를 처리하지 않음
        if (_isDead || currentState == EnemyState.Evading || currentState == EnemyState.Damaged) return;

        // 회피 판정: 특정 상태(이동, 공격, 대기)일 때만 확률적으로 발동
        bool canTryEvade = currentState == EnemyState.Moving || currentState == EnemyState.Attacking || currentState == EnemyState.Idle;
        if (canTryEvade && UnityEngine.Random.value < evadeChance)
        {
            // 공격을 회피했으므로 데미지를 입지 않고 함수 종료
            EvadeAsync().Forget();
            return;
        }

        int oldHp = _currentHp;
        _currentHp -= damage;
        _currentHp = Mathf.Max(0, _currentHp);

        if (_tempHp == oldHp)
        {
            _tempHp = oldHp;
        }
        else
        {
            _tempHp -= damage;
        }
        _tempHp = Mathf.Max(_tempHp, _currentHp);
        _lastDamageTime = Time.time;

        OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);

        if (_currentHp <= 0)
        {
            // _currentHp is already clamped to 0
            Die();
        }
        else
        {
            PlayDamagedAnimationAsync().Forget();
        }
    }

    public void ForceUpdateHpUI()
    {
        OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);
    }

    /// <summary>
    /// 예비 체력만큼 현재 체력을 회복합니다.
    /// </summary>
    public void HealWithTempHp()
    {
        if (_isDead) return;
        int recoveryAmount = _tempHp - _currentHp;
        if (recoveryAmount > 0)
        {
            _currentHp += recoveryAmount;
            _tempHp = _currentHp; // 회복 후에는 예비 체력과 현재 체력을 일치시킴
            OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);
            Debug.Log("적이 체력을 회복했습니다!");
        }
    }
    #endregion

    #region AI 핵심 로직

    private void Die()
    {
        _isDead = true;
        _aiTaskCts?.Cancel();
        _rigidbody2D.linearVelocity = Vector2.zero;
        GetComponent<Collider2D>().enabled = false;
        currentState = EnemyState.Dead;
 
        // 체력을 0으로 설정하고 UI를 업데이트합니다.
        _currentHp = 0;
        _tempHp = 0;
        OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);

        if (_enemyAnimation != null) _enemyAnimation.PlayAnimation(PlayerState.DEATH, 0);
        
        Destroy(gameObject, 3f);
    }

    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_isDead)
        {
            switch (currentState)
            {
                case EnemyState.Idle:
                    await OnIdleStateAsync(token);
                    break;
                case EnemyState.Moving:
                    await OnMovingStateAsync(token);
                    break;
                case EnemyState.Attacking:
                    await OnAttackingStateAsync(token);
                    break;
                case EnemyState.Healing:
                    await OnHealingStateAsync(token);
                    break;
            }
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }

    private async UniTask PlayAttackAndFireAsync(bool useSkill, CancellationToken token)
    {
        _enemyAnimation.PlayAnimation(PlayerState.ATTACK, 0);

        var attackClip = _enemyAnimation.ATTACK_List.Count > 0 ? _enemyAnimation.ATTACK_List[0] : null;
        if (attackClip != null)
        {
            try
            {
                float fireDelay = attackClip.length * 0.5f;
                await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

                if (_isDead) return;

                if (useSkill)
                {
                    _lastSkillUseTime = Time.time;
                    Debug.Log("Enemy uses Multi-Shot Skill!");
                    SkillManager.Instance.ExecuteEnemyMultiShot(firePoint, player);
                }
                else
                {
                    PerformArrowLaunch();
                }

                await UniTask.Delay(TimeSpan.FromSeconds(attackClip.length - fireDelay), cancellationToken: token);
            }
            catch (OperationCanceledException) { /* 예외 발생 시에도 finally가 실행되도록 보장 */ }
            finally
            {
                if (!_isDead) currentState = EnemyState.Idle;
            }
        }
        else // 애니메이션 클립이 없는 경우
        {
            if (!_isDead)
            {
                if (useSkill)
                {
                    _lastSkillUseTime = Time.time;
                    SkillManager.Instance.ExecuteEnemyMultiShot(firePoint, player);
                }
                else
                {
                    PerformArrowLaunch();
                }
            }
            if (!_isDead) currentState = EnemyState.Idle;
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (_isDead || _enemyAnimation == null) return;

        _aiTaskCts?.Cancel();
        currentState = EnemyState.Damaged;
        _rigidbody2D.linearVelocity = Vector2.zero;

        _enemyAnimation.PlayAnimation(PlayerState.DAMAGED, 0);

        var damagedClip = _enemyAnimation.DAMAGED_List.Count > 0 ? _enemyAnimation.DAMAGED_List[0] : null;
        if (damagedClip != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClip.length), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!_isDead)
        {
            currentState = EnemyState.Idle;
            _aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(_aiTaskCts.Token).Forget();
        }
    }

    #endregion

    #region 보조 메소드

    private void UpdateTempHp()
    {
        // 예비 체력(Temp HP)이 현재 체력보다 높고, 마지막 피격 후 일정 시간이 지났다면
        // 예비 체력을 서서히 감소시켜 현재 체력과 맞춥니다.
        if (!_isDead && _tempHp > _currentHp && Time.time >= _lastDamageTime + tempHpDecreaseDelay)
        {
            int decreaseAmount = (int)Mathf.Ceil(tempHpCatchUpSpeed * Time.deltaTime);
            _tempHp = Mathf.Max(_currentHp, _tempHp - decreaseAmount);
            OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);
        }
    }

    private void CheckIfOffScreen()
    {
        if (GameManager.Instance != null && GameManager.Instance.mainCamera != null)
        {
            float cameraBottom = GameManager.Instance.mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0)).y;
            float destroyThreshold = cameraBottom - 2f;

            if (transform.position.y < destroyThreshold)
            {
                Destroy(gameObject);
            }
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, _minXPosition, _maxXPosition);
        _rigidbody2D.position = clampedPosition;
    }

    private void PerformArrowLaunch()
    {
        if (_isDead || EnemyArrowPool.Instance == null || player == null) return;

        Vector3 startPos = firePoint.position;
        Vector2 direction = (player.position - startPos).normalized;
        Vector3 endPos = startPos + new Vector3(direction.x, direction.y, 0).normalized * fireDistance;

        Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
        Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

        GameObject arrowObject = EnemyArrowPool.Instance.Get();
        if (arrowObject == null) return;

        arrowObject.transform.SetPositionAndRotation(startPos, Quaternion.identity);
        var arrowController = arrowObject.GetComponent<ArrowController>();
        if (arrowController != null)
        {
            arrowController.Launch(startPos, controlPoint, endPos, fireDuration, fireEaseCurve);
        }
        else
        {
            EnemyArrowPool.Instance.Return(arrowObject);
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

    private void FlipSprite(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) < 0.01f) return;

        // 로컬 스케일의 x값을 조절하여 스프라이트의 방향을 전환합니다.
        // 기본적으로 왼쪽을 보고 있다고 가정합니다 (localScale.x > 0).
        // horizontalDirection이 0보다 크면(오른쪽으로 이동) 오른쪽을 보도록 localScale.x를 음수로 만듭니다.
        Vector3 localScale = transform.localScale;
        if (horizontalDirection > 0)
        {
            localScale.x = -Mathf.Abs(localScale.x);
        }
        else
        {
            localScale.x = Mathf.Abs(localScale.x);
        }
        transform.localScale = localScale;
    }

    #endregion

    #region AI 상태별 로직

    private async UniTask OnIdleStateAsync(CancellationToken token)
    {
        if (_isDead) return;

        _enemyAnimation.PlayAnimation(PlayerState.IDLE, 0);
        _rigidbody2D.linearVelocity = Vector2.zero;

        if (player == null) return;

        // [우선순위 1: 회복]
        bool canHeal = Time.time >= _lastHealTime + healSkillCooldown;
        bool shouldHeal = (float)_currentHp / maxHp <= healHealthThreshold;
        if (canHeal && shouldHeal && (_tempHp > _currentHp))
        {
            currentState = EnemyState.Healing;
            return;
        }

        // [우선순위 2: 위치 조정 또는 공격]
        float horizontalDistanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);
        if (Mathf.Abs(horizontalDistanceToPlayer - fireDistance) > distanceTolerance)
        {
            currentState = EnemyState.Moving;
        }
        else
        {
            currentState = EnemyState.Attacking;
        }
        await UniTask.Yield(token);
    }

    private async UniTask OnMovingStateAsync(CancellationToken token)
    {
        if (_isDead) return;

        _enemyAnimation.PlayAnimation(PlayerState.MOVE, 0);

        while (!token.IsCancellationRequested && !_isDead)
        {
            if (player == null)
            {
                currentState = EnemyState.Idle;
                return;
            }

            float horizontalDistanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);
            if (Mathf.Abs(horizontalDistanceToPlayer - fireDistance) <= distanceTolerance)
            {
                currentState = EnemyState.Idle; // 목표 도달, 상태 재평가
                return;
            }

            float xDirection = Mathf.Sign(player.position.x - transform.position.x);
            float moveDirection = (horizontalDistanceToPlayer > fireDistance) ? 1f : -1f;
            float targetXVelocity = xDirection * moveSpeed * moveDirection;

            FlipSprite(targetXVelocity);

            if ((transform.position.x <= _minXPosition && targetXVelocity < 0) || (transform.position.x >= _maxXPosition && targetXVelocity > 0))
            {
                targetXVelocity = 0;
            }

            _rigidbody2D.linearVelocity = new Vector2(targetXVelocity, _rigidbody2D.linearVelocity.y);
            ClampPosition();
            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
        }
    }

    private async UniTask OnAttackingStateAsync(CancellationToken token)
    {
        if (_isDead) return;

        // 공격 방향으로 몸 돌리기
        if (player != null)
        {
            float directionToPlayer = player.position.x - transform.position.x;
            FlipSprite(directionToPlayer);
        }

        // 스킬 사용 결정
        bool canUseSkill = Time.time >= _lastSkillUseTime + skillCooldown;
        bool willUseSkill = canUseSkill && UnityEngine.Random.value < skillChance;

        // 공격 애니메이션 재생 및 발사 (이 작업이 끝나야 다음 상태로 감)
        await PlayAttackAndFireAsync(willUseSkill, token);

        // 공격 후 쿨다운
        await UniTask.Delay(TimeSpan.FromSeconds(attackCooldown), cancellationToken: token);

        currentState = EnemyState.Idle; // 공격 완료 후 Idle로 돌아가 재평가
    }

    private async UniTask OnHealingStateAsync(CancellationToken token)
    {
        if (_isDead) return;

        Debug.Log("적이 회복을 시전합니다.");
        _lastHealTime = Time.time;

        // TODO: 회복 애니메이션/이펙트 재생
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

        if (token.IsCancellationRequested || _isDead) return;

        HealWithTempHp();
        currentState = EnemyState.Idle;
    }

    #endregion

    #region 회피 로직

    /// <summary>
    /// 공격 회피를 위한 비동기 대쉬 로직입니다.
    /// </summary>
    private async UniTaskVoid EvadeAsync()
    {
        if (_isDead) return;

        _aiTaskCts?.Cancel(); // 현재 AI 행동 중지
        currentState = EnemyState.Evading;
        _rigidbody2D.linearVelocity = Vector2.zero;

        // 1. 더 넓은 공간이 있는 방향으로 회피 방향 결정
        float currentX = _rigidbody2D.position.x;
        float spaceToLeft = currentX - _minXPosition;
        float spaceToRight = _maxXPosition - currentX;
        float direction = (spaceToRight > spaceToLeft) ? 1f : -1f;

        // --- 안전한 대쉬 거리 계산 (PlayerControl의 Dash 로직 차용) ---
        float maxDashDistance = evadeDashSpeed * evadeDashDuration;

        // 2. 벽에 의해 제한되는 최대 대쉬 거리를 계산합니다.
        float wallLimitedDistance = maxDashDistance;
        RaycastHit2D wallHit = Physics2D.BoxCast(
            (Vector2)transform.position + _collider.offset,
            new Vector2(_collider.bounds.size.x, _collider.bounds.size.y * 0.9f),
            0f,
            new Vector2(direction, 0),
            maxDashDistance,
            groundLayer
        );
        if (wallHit.collider != null)
        {
            wallLimitedDistance = wallHit.distance;
        }

        // 3. 절벽을 감지하여 실제 이동 가능한 거리를 찾습니다.
        float finalDashDistance = 0f;
        int steps = 10;
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, _rigidbody2D.position.y);

            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(_collider.bounds.size.x * 0.5f, 0.1f),
                0f, Vector2.down, _collider.bounds.extents.y + 0.5f, groundLayer
            );

            if (groundUnderneath.collider == null)
            {
                finalDashDistance = (i - 1) * stepDistance;
                goto FoundSafeDistance;
            }
        }
        finalDashDistance = wallLimitedDistance;

    FoundSafeDistance:
        finalDashDistance = Mathf.Max(0, finalDashDistance - 0.1f); // 여유 공간

        float finalTargetX = currentX + direction * finalDashDistance;
        float actualDuration = finalDashDistance / evadeDashSpeed;

        if (actualDuration < Time.fixedDeltaTime)
        {
            // 대쉬할 공간이 없으면 회피 취소하고 AI 재시작
            currentState = EnemyState.Idle;
            if (!_isDead)
            {
                _aiTaskCts = new CancellationTokenSource();
                AI_LoopAsync(_aiTaskCts.Token).Forget();
            }
            return;
        }

        // --- 대쉬 실행 ---
        FlipSprite(direction);
        _afterimageEffect?.StartEffect(actualDuration);

        float startY = _rigidbody2D.position.y;
        float originalGravity = _rigidbody2D.gravityScale;
        _rigidbody2D.gravityScale = 0;
        _rigidbody2D.linearVelocity = new Vector2(direction * evadeDashSpeed, 0);

        await UniTask.Delay(TimeSpan.FromSeconds(actualDuration));

        _rigidbody2D.gravityScale = originalGravity;
        _rigidbody2D.linearVelocity = Vector2.zero;
        _rigidbody2D.position = new Vector2(finalTargetX, startY);
        currentState = EnemyState.Idle;

        // AI 루프 재시작
        if (!_isDead)
        {
            _aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(_aiTaskCts.Token).Forget();
        }
    }
    #endregion
}
