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
        SkillAttacking,
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
    private float _cameraMinX;
    private float _cameraMaxX;
    private float _effectiveMinX;
    private float _effectiveMaxX;

    [Header("공격 설정")]
    [Tooltip("발사할 화살 프리팹")]
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private Transform firePoint;
    [Tooltip("화살이 날아가는 고정 거리. 적의 이동 및 공격 위치 선정의 기준이 됩니다.")]
    [SerializeField] private float fireDistance = 7f;
    [SerializeField] private float fireArcHeight = 3f;
    [SerializeField] private float fireDuration = 1f;
    [SerializeField] private float attackCooldown = 2f;

    [Header("스킬 설정")]
    [SerializeField] private float skillCooldown = 10f;
    [Range(0, 1)]
    [SerializeField] private float skillChance = 0.3f; // 30% 확률로 스킬 사용
    [SerializeField] private Transform skillHandPoint; // 스킬 무기가 장착될 손 위치
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
    [Tooltip("회피가 발동되기 위한 최소 대쉬 거리")]
    [SerializeField] private float minEvadeDistance = 2f;

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

        DetectGroundAndCameraBoundaries();
        ForceUpdateHpUI();

        _aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(_aiTaskCts.Token).Forget();
    }

    private void Update()
    {
        if (_isDead) return;

        CheckIfOffScreen();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isDead && (other.CompareTag("Arrow")))
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
    public async void TakeDamage(int damage)
    {
        // [회피 우선권] 회피, 피격, 사망 중에는 새로운 데미지나 회피를 처리하지 않음
        if (_isDead || currentState == EnemyState.Evading || currentState == EnemyState.Damaged) return;

        // 회피 판정: 특정 상태(이동, 공격, 대기)일 때만 확률적으로 발동
        bool canTryEvade = currentState == EnemyState.Moving || currentState == EnemyState.Attacking || currentState == EnemyState.Idle;
        if (canTryEvade && UnityEngine.Random.value < evadeChance)
        {
            Debug.Log("<color=orange>[AI-Evade]</color> 회피 시도!");
            // 회피를 시도하고, 성공 여부를 기다립니다.
            bool evaded = await EvadeAsync();
            if (evaded)
            {
                // 회피에 성공했다면, 데미지를 입지 않고 함수를 종료합니다.
                return;
            }
            // 회피에 실패했다면(예: 공간 부족), 아래의 데미지 로직을 계속 진행합니다.
        }

        // 데미지를 받기 시작한 시점의 체력을 예비 체력으로 기록합니다.
        // 예비 체력 감소 딜레이 시간 내에 추가 타격을 받으면, 예비 체력은 갱신되지 않고 현재 체력만 감소합니다.
        if (Time.time > _lastDamageTime + tempHpDecreaseDelay)
        {
            _tempHp = _currentHp;
        }

        _currentHp -= damage;
        _currentHp = Mathf.Max(0, _currentHp);
        _lastDamageTime = Time.time;

        OnHpChanged?.Invoke(_currentHp, _tempHp, maxHp);

        // 데미지 텍스트 표시 (캐릭터 머리 위)
        EffectManager.Instance.ShowDamageText(gameObject, damage);

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
                case EnemyState.SkillAttacking:
                    await OnSkillAttackingStateAsync(token);
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
        try
        {
            if (useSkill)
            {
                _lastSkillUseTime = Time.time;
                Debug.Log("Enemy uses AK47 Skill!");
                // 스킬 실행을 기다립니다. 이 시간 동안 공격 애니메이션이 재생됩니다.
                await SkillManager.Instance.ExecuteEnemyMultiShot(skillHandPoint, player);
            }
            else
            {
                // 기존 일반 공격 로직
                if (attackClip != null)
                {
                    float fireDelay = attackClip.length * 0.5f;
                    await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

                    if (_isDead) return;
                    PerformArrowLaunch();

                    await UniTask.Delay(TimeSpan.FromSeconds(attackClip.length - fireDelay), cancellationToken: token);
                }
                else // 애니메이션 클립이 없는 경우 기본 딜레이 후 발사
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                    if (_isDead) return;
                    PerformArrowLaunch();
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                }
            }
        }
        catch (OperationCanceledException) { /* 예외 발생 시에도 finally가 실행되도록 보장 */ }
        finally
        {
            // 상태 전환은 각 상태 핸들러(OnAttackingStateAsync 등)에서 책임지도록 변경합니다.
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
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, _effectiveMinX, _effectiveMaxX);
        _rigidbody2D.position = clampedPosition;
    }

    private void PerformArrowLaunch()
    {
        if (_isDead || ObjectPoolManager.Instance == null || player == null) return;

        Vector3 startPos = firePoint.position;
        Vector2 direction = (player.position - startPos).normalized;
        Vector3 endPos = startPos + new Vector3(direction.x, direction.y, 0).normalized * fireDistance;

        Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
        Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

        GameObject arrowObject = ObjectPoolManager.Instance.Get(arrowPrefab);
        if (arrowObject == null) return;

        arrowObject.transform.SetPositionAndRotation(startPos, Quaternion.identity);
        var arrowController = arrowObject.GetComponent<ArrowController>();
        if (arrowController != null)
        {
            arrowController.Owner = ArrowController.ArrowOwner.Enemy;
            arrowController.Launch(startPos, controlPoint, endPos, fireDuration);
        }
        else
        {
            ObjectPoolManager.Instance.Return(arrowObject);
        }
    }

    private void DetectGroundAndCameraBoundaries()
    {
        // 캐릭터 너비의 절반 (중심에서 가장자리까지의 거리)
        float enemyWidth = _collider.bounds.extents.x;

        // 1. 지면 경계 감지 (Probing 방식)
        // 타일맵의 틈으로 인해 Raycast가 실패하는 문제를 방지하기 위해 BoxCast를 사용합니다.
        // 또한, 탐색 정밀도를 높여 더 정확한 경계를 찾습니다.

        float maxProbeDistance = 20f; // 양쪽으로 탐색할 최대 거리
        int probeSteps = 100;         // 탐색 정밀도 (높을수록 정확하지만 비용 증가)
        float stepDistance = maxProbeDistance / probeSteps;
        
        // 캐릭터의 발 위치를 기준으로 탐색을 시작합니다.
        Vector2 characterFeet = (Vector2)transform.position - new Vector2(0, _collider.bounds.extents.y);
        Vector2 boxCastSize = new Vector2(stepDistance, 0.1f); // 탐색 간격만큼의 너비를 가진 작은 상자

        // --- 오른쪽 경계(절벽) 찾기 ---
        float rightEdgeX = transform.position.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            // 현재 위치에서 오른쪽으로 한 스텝 이동한 지점
            Vector2 probeOrigin = new Vector2(transform.position.x + i * stepDistance, characterFeet.y);
            
            // 해당 지점 바로 아래에 땅이 있는지 확인합니다.
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, groundLayer);
            
            if (hit.collider == null)
            {
                // 땅이 없으면, 바로 이전 지점이 절벽의 가장자리입니다.
                rightEdgeX = transform.position.x + (i - 1) * stepDistance;
                break;
            }
            
            // 탐색이 끝까지 도달했다면, 최대 탐색 거리를 경계로 간주합니다.
            if (i == probeSteps) rightEdgeX = transform.position.x + maxProbeDistance;
        }
        _maxXPosition = rightEdgeX;

        // --- 왼쪽 경계(절벽) 찾기 ---
        float leftEdgeX = transform.position.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            Vector2 probeOrigin = new Vector2(transform.position.x - i * stepDistance, characterFeet.y);
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, groundLayer);
            
            if (hit.collider == null)
            {
                leftEdgeX = transform.position.x - (i - 1) * stepDistance;
                break;
            }

            if (i == probeSteps) leftEdgeX = transform.position.x - maxProbeDistance;
        }
        _minXPosition = leftEdgeX;

        // 2. 카메라 경계 감지
        if (GameManager.Instance != null && GameManager.Instance.mainCamera != null)
        {
            Camera cam = GameManager.Instance.mainCamera;
            _cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
            _cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;
        }
        else
        {
            _cameraMinX = -Mathf.Infinity;
            _cameraMaxX = Mathf.Infinity;
        }

        // 3. 최종 유효 이동 범위 계산 (지면과 카메라 경계의 교집합)
        // 캐릭터의 중심이 이동할 수 있는 최종 범위를 계산합니다.
        // 캐릭터가 플랫폼과 카메라 뷰 밖으로 완전히 나가지 않도록 너비만큼 보정합니다.
        _effectiveMinX = Mathf.Max(_minXPosition, _cameraMinX) + enemyWidth;
        _effectiveMaxX = Mathf.Min(_maxXPosition, _cameraMaxX) - enemyWidth;
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

    #if UNITY_EDITOR
    /// <summary>
    /// [테스트용] 인스펙터의 컨텍스트 메뉴에서 적의 다발 사격 스킬을 즉시 실행합니다.
    /// 이 기능은 Play 모드에서만 동작합니다.
    /// </summary>
    public async void TestMultiShotSkill() // Custom Editor에서 접근할 수 있도록 public으로 변경
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("스킬 테스트는 Play 모드에서만 가능합니다.");
            return;
        }

        if (player == null || SkillManager.Instance == null)
        {
            Debug.LogError("스킬 테스트에 필요한 Player 또는 SkillManager 참조가 없습니다.");
            return;
        }

        Debug.Log("--- SKILL TEST: 적 다발 사격 스킬 실행 ---");
        // 테스트 시 자연스럽게 보이도록 플레이어 방향으로 몸을 돌립니다.
        float directionToPlayer = player.position.x - transform.position.x;
        FlipSprite(directionToPlayer);

        // SkillManager를 통해 스킬을 직접 실행합니다.
        await SkillManager.Instance.ExecuteEnemyMultiShot(skillHandPoint, player);
        Debug.Log("--- SKILL TEST: 완료 ---");
    }
    #endif

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
            // 공격 범위 내에 있을 경우, 스킬 사용 여부를 결정합니다.
            bool canUseSkill = Time.time >= _lastSkillUseTime + skillCooldown;
            bool willUseSkill = canUseSkill && UnityEngine.Random.value < skillChance;
            if (willUseSkill)
            {
                currentState = EnemyState.SkillAttacking;
            }
            else
            {
                currentState = EnemyState.Attacking;
            }
        }
        await UniTask.Yield(token);
    }

    private async UniTask OnMovingStateAsync(CancellationToken token)
    {
        if (_isDead) return;

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

            // --- 절벽 감지 로직 추가 ---
            float moveSign = Mathf.Sign(targetXVelocity);
            if (moveSign != 0)
            {
                // 캐릭터의 앞쪽, 발밑을 확인하기 위한 위치. 캐릭터의 전방 가장자리 바로 아래를 확인합니다.
                Vector2 groundCheckOrigin = (Vector2)_collider.bounds.center + new Vector2(moveSign * _collider.bounds.extents.x, -_collider.bounds.extents.y - 0.05f);
                
                // 아래 방향으로 짧은 Raycast를 실행하여 발밑에 땅이 있는지 확인
                RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, groundLayer);

                if (groundHit.collider == null)
                {
                    // 앞에 땅이 없으면 멈춤
                    targetXVelocity = 0;
                }
            }

            // --- 경계 감지 로직 (지면 + 카메라) ---
            if ((transform.position.x <= _effectiveMinX && targetXVelocity < 0) || (transform.position.x >= _effectiveMaxX && targetXVelocity > 0))
            {
                targetXVelocity = 0;
            }

            // --- 애니메이션 및 이동 처리 ---
            if (Mathf.Abs(targetXVelocity) > 0.01f)
            {
                _enemyAnimation.PlayAnimation(PlayerState.MOVE, 0);
                FlipSprite(targetXVelocity);
                _rigidbody2D.linearVelocity = new Vector2(targetXVelocity, _rigidbody2D.linearVelocity.y);
            }
            else
            {
                // 이동할 수 없으면 IDLE 애니메이션 재생 및 정지
                _enemyAnimation.PlayAnimation(PlayerState.IDLE, 0);
                _rigidbody2D.linearVelocity = new Vector2(0, _rigidbody2D.linearVelocity.y);
            }

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

        // 공격 애니메이션 재생 및 발사 (이 작업이 끝나야 다음 상태로 감)
        await PlayAttackAndFireAsync(false, token);

        // 공격 후 쿨다운
        await UniTask.Delay(TimeSpan.FromSeconds(attackCooldown), cancellationToken: token);

        currentState = EnemyState.Idle; // 공격 완료 후 Idle로 돌아가 재평가
    }

    private async UniTask OnSkillAttackingStateAsync(CancellationToken token)
    {
        if (_isDead) return;

        // 공격 방향으로 몸 돌리기
        if (player != null)
        {
            float directionToPlayer = player.position.x - transform.position.x;
            FlipSprite(directionToPlayer);
        }

        // 스킬 사용
        await PlayAttackAndFireAsync(true, token);

        // 스킬 사용 후에는 즉시 Idle로 돌아가 다음 행동을 결정합니다.
        currentState = EnemyState.Idle;
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
    private async UniTask<bool> EvadeAsync()
    {
        if (_isDead) return false;

        // 회피를 시도하는 시점에서 현재 서 있는 땅과 카메라 경계를 다시 계산하여
        // 항상 정확한 정보를 바탕으로 판단하도록 합니다.
        DetectGroundAndCameraBoundaries();

        // 1. 더 넓은 공간이 있는 방향으로 회피 방향 결정
        float currentX = _rigidbody2D.position.x;
        float spaceToLeft = currentX - _effectiveMinX;
        float spaceToRight = _effectiveMaxX - currentX;
        float direction = (spaceToRight > spaceToLeft) ? 1f : -1f;
        string directionString = direction > 0 ? "오른쪽" : "왼쪽";
        Debug.Log($"<color=orange>[AI-Evade]</color> 회피 방향 결정. 왼쪽 공간: {spaceToLeft:F2}, 오른쪽 공간: {spaceToRight:F2}. 선택: {directionString}");

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
        float finalDashDistance = wallLimitedDistance; // 벽이 없다면 최대 대쉬 거리로 초기화
        int steps = 10;
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            // 캐릭터의 중심이 이동할 위치를 기준으로 BoxCast를 수행합니다.
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, _collider.bounds.center.y);

            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(_collider.bounds.size.x * 0.9f, 0.1f), // 약간의 여유를 둔 너비로 체크
                0f, Vector2.down, _collider.bounds.extents.y + 0.5f, groundLayer // 발밑으로 충분한 거리 체크
            );

            if (groundUnderneath.collider == null)
            {
                finalDashDistance = (i - 1) * stepDistance; // 땅이 없는 지점 직전까지의 거리
                break; // 안전한 거리를 찾았으므로 루프 종료
            }
        }

        finalDashDistance = Mathf.Max(0, finalDashDistance - _collider.bounds.extents.x); // 여유 공간 (캐릭터 너비의 절반만큼)

        // 최종 목표 지점을 유효 경계 내로 제한합니다.
        float finalTargetX = Mathf.Clamp(currentX + direction * finalDashDistance, _effectiveMinX, _effectiveMaxX);
        float actualDashDistance = Mathf.Abs(finalTargetX - currentX);
        float actualDuration = actualDashDistance / evadeDashSpeed;
        
        Debug.Log($"<color=orange>[AI-Evade]</color> 계산된 실제 회피 가능 거리: {actualDashDistance:F2}");

        if (actualDashDistance < minEvadeDistance)
        {
            Debug.Log($"<color=orange>[AI-Evade]</color> 회피 실패: 이동 가능 거리({actualDashDistance:F2})가 최소 회피 거리({minEvadeDistance})보다 짧습니다.");
            return false;
        }

        // --- 대쉬 실행 ---
        Debug.Log($"<color=green>[AI-Evade]</color> 회피 성공! {actualDashDistance:F2}만큼 {directionString}으로 대쉬합니다.");
        _aiTaskCts?.Cancel(); // 현재 AI 행동 중지
        currentState = EnemyState.Evading;
        _rigidbody2D.linearVelocity = Vector2.zero;

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

        // 회피 후 잠시 대기하여 다음 행동까지 딜레이를 줍니다.
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f));

        currentState = EnemyState.Idle;

        // AI 루프 재시작
        if (!_isDead)
        {
            _aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(_aiTaskCts.Token).Forget();
        }

        return true; // 회피 성공
    }
    #endregion
}
