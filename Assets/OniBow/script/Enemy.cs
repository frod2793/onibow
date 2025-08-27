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

    private SPUM_Prefabs _enemyAnimation;
    private Rigidbody2D _rigidbody2D;
    private CancellationTokenSource _aiTaskCts;
    private bool _isAction;
    private bool _isDead;
    public bool IsDead => _isDead;
    #endregion

    #region MonoBehaviour 콜백
    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
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

        CheckIfOffScreen();

        // 예비 체력(Temp HP)이 현재 체력보다 높고, 마지막 피격 후 일정 시간이 지났다면
        // 예비 체력을 서서히 감소시켜 현재 체력과 맞춥니다.
        if (!_isDead && _tempHp > _currentHp && Time.time >= _lastDamageTime + tempHpDecreaseDelay)
        {
            int decreaseAmount = (int)Mathf.Ceil(tempHpCatchUpSpeed * Time.deltaTime);
            _tempHp = Mathf.Max(_currentHp, _tempHp - decreaseAmount);
            OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);
        }

        if (_isAction || _enemyAnimation == null) return;

        PlayerState animationState = currentState == EnemyState.Moving ? PlayerState.MOVE : PlayerState.IDLE;
        _enemyAnimation.PlayAnimation(animationState, 0);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isDead && (other.CompareTag("Arrow") || other.CompareTag("PlayerArrow")))
        {
            TakeDamage(10);
            Destroy(other.gameObject);
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
        if (_isDead) return;

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
        currentState = EnemyState.Dead;
        _aiTaskCts?.Cancel();
        _rigidbody2D.linearVelocity = Vector2.zero;
        GetComponent<Collider2D>().enabled = false;

        // 체력을 0으로 설정하고 UI를 업데이트합니다.
        _currentHp = 0;
        _tempHp = 0;
        OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);

        if (_enemyAnimation != null) _enemyAnimation.PlayAnimation(PlayerState.DEATH, 0);
        
        Destroy(gameObject, 3f);
    }

    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (player == null || _isAction || _isDead)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                continue;
            }

            // [AI 우선순위 1: 회복] 체력이 낮고 쿨타임이 돌았으면 회복
            bool canHeal = Time.time >= _lastHealTime + healSkillCooldown;
            bool shouldHeal = (float)_currentHp / maxHp <= healHealthThreshold;
            if (canHeal && shouldHeal && (_tempHp > _currentHp)) // 회복할 체력이 있을 때만
            {
                await HealAsync(token);
                continue; // 회복 후에는 다시 생각
            }

            // [AI 우선순위 2: 위치 조정 및 공격]
            float horizontalDistanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);

            if (Mathf.Abs(horizontalDistanceToPlayer - fireDistance) > distanceTolerance)
            {
                await MoveToAttackPositionAsync(token);
            }
            else
            {
                await AttackAsync(token);
            }
        }
    }

    private async UniTask HealAsync(CancellationToken token)
    {
        _isAction = true;
        currentState = EnemyState.Idle; // 또는 새로운 "Healing" 상태
        _rigidbody2D.linearVelocity = Vector2.zero;

        Debug.Log("적이 회복을 시전합니다.");
        _lastHealTime = Time.time;

        // 여기에 회복 애니메이션이나 이펙트를 추가할 수 있습니다.
        // 예: _enemyAnimation.PlayAnimation(PlayerState.OTHER, healAnimationIndex);
        // 잠시 대기하여 회복 모션을 보여줍니다.
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

        if (token.IsCancellationRequested || _isDead)
        {
            _isAction = false;
            return;
        }

        HealWithTempHp();

        _isAction = false;
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

            FlipSprite(targetXVelocity);

            if ((transform.position.x <= _minXPosition && targetXVelocity < 0) || (transform.position.x >= _maxXPosition && targetXVelocity > 0))
            {
                targetXVelocity = 0;
            }

            _rigidbody2D.linearVelocity = new Vector2(targetXVelocity, _rigidbody2D.linearVelocity.y);
            ClampPosition();

            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
        }

        _rigidbody2D.linearVelocity = new Vector2(0, _rigidbody2D.linearVelocity.y);
        currentState = EnemyState.Idle;
    }

    private async UniTask AttackAsync(CancellationToken token)
    {
        currentState = EnemyState.Attacking;
        // 공격 시에는 항상 왼쪽을 바라보도록 설정합니다.
        // 기본 스프라이트가 왼쪽을 향한다고 가정 (localScale.x > 0 이 왼쪽)
        Vector3 localScale = transform.localScale;
        localScale.x = Mathf.Abs(localScale.x);
        transform.localScale = localScale;

        // 스킬 사용 결정
        bool canUseSkill = Time.time >= _lastSkillUseTime + skillCooldown;
        bool willUseSkill = canUseSkill && UnityEngine.Random.value < skillChance;

        // 공격 애니메이션 재생 및 발사/스킬 사용 (백그라운드 실행)
        PlayAttackAndFireAsync(willUseSkill, token).Forget();

        // 공격 후 쿨다운 (애니메이션 재생과 동시에 진행)
        await UniTask.Delay(TimeSpan.FromSeconds(attackCooldown), cancellationToken: token);
    }

    private async UniTaskVoid PlayAttackAndFireAsync(bool useSkill, CancellationToken token)
    {
        _isAction = true;
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
                _isAction = false;
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
            _isAction = false;
            if (!_isDead) currentState = EnemyState.Idle;
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (_isDead || _enemyAnimation == null) return;

        _isAction = true;
        _aiTaskCts?.Cancel();
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

        _isAction = false;

        if (!_isDead)
        {
            _aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(_aiTaskCts.Token).Forget();
        }
    }

    #endregion

    #region 보조 메소드

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
}
