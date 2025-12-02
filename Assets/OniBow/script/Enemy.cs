using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;
using UnityEngine.Serialization;

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
    [SerializeField] private int m_maxHp = 150;
    [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
    [SerializeField] private float m_tempHpDecreaseDelay = 3f;
    private int m_currentHp;
    private int m_tempHp;
    private float m_lastDamageTime;
    public event Action<int, int, int> OnHpChanged;

    [Header("AI 설정")]
    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;
    [SerializeField] private Transform m_player;
    [SerializeField] private float m_moveSpeed = 3f;
    [Tooltip("공격 위치로 간주할 거리의 허용 오차 범위입니다.")]
    [SerializeField] private float m_distanceTolerance = 0.5f;

    [Header("지형 설정")]
    [Tooltip("지면을 감지할 레이어 마스크")]
    [SerializeField] private LayerMask m_groundLayer;
    private float m_minXPosition;
    private float m_maxXPosition;
    private float m_cameraMinX;
    private float m_cameraMaxX;
    private float m_effectiveMinX;
    private float m_effectiveMaxX;

    [Header("공격 설정")]
    [Tooltip("발사할 화살 프리팹")]
    [SerializeField] private GameObject m_arrowPrefab;
    [SerializeField] private Transform m_firePoint;
    [Tooltip("화살이 날아가는 고정 거리. 적의 이동 및 공격 위치 선정의 기준이 됩니다.")]
    [SerializeField] private float m_fireDistance = 7f;
    [SerializeField] private float m_fireArcHeight = 3f;
    [SerializeField] private float m_fireDuration = 1f;
    [SerializeField] private float m_attackCooldown = 2f;

    [Header("스킬 설정")]
    [SerializeField] private float m_skillCooldown = 10f;
    [Range(0, 1)]
    [SerializeField] private float m_skillChance = 0.3f;
    [SerializeField] private Transform m_skillHandPoint;
    private float m_lastSkillUseTime = -999f;
    [SerializeField] private float m_healSkillCooldown = 20f;
    [SerializeField, Range(0, 1)] private float m_healHealthThreshold = 0.4f;
    private float m_lastHealTime = -999f;

    [Header("회피 설정")]
    [Tooltip("플레이어의 공격을 회피할 확률 (0.0 ~ 1.0)")]
    [Range(0, 1)]
    [SerializeField] private float m_evadeChance = 0.3f;
    [Tooltip("회피 대쉬 속도")]
    [SerializeField] private float m_evadeDashSpeed = 15f;
    [Tooltip("회피 대쉬 최대 지속 시간")]
    [SerializeField] private float m_evadeDashDuration = 0.25f;
    [Tooltip("회피가 발동되기 위한 최소 대쉬 거리")]
    [SerializeField] private float m_minEvadeDistance = 2f;

    private SPUM_Prefabs m_enemyAnimation;
    private Rigidbody2D m_rigidbody2D;
    private Collider2D m_collider;
    private AfterimageEffect m_afterimageEffect;
    private CancellationTokenSource m_aiTaskCts;
    private bool m_isDead;
    public bool IsDead => m_isDead;
    
    private const string k_PlayerTag = "Player";
    private const string k_ArrowTag = "Arrow";

    #endregion

    #region MonoBehaviour 콜백
    private void Awake()
    {
        m_rigidbody2D = GetComponent<Rigidbody2D>();
        m_collider = GetComponent<Collider2D>();
        m_afterimageEffect = GetComponent<AfterimageEffect>();
        if (m_afterimageEffect == null)
        {
            Debug.LogWarning("Enemy에 AfterimageEffect 컴포넌트가 없습니다. 회피 잔상 효과가 동작하지 않습니다.");
        }
        m_enemyAnimation = GetComponentInChildren<SPUM_Prefabs>();
        m_rigidbody2D.gravityScale = 1;
        m_rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
        
        m_currentHp = m_maxHp;
        m_tempHp = m_maxHp;

        if (m_enemyAnimation != null)
        {
            if (!m_enemyAnimation.allListsHaveItemsExist())
            {
                m_enemyAnimation.PopulateAnimationLists();
            }
            m_enemyAnimation.OverrideControllerInit();
            if (m_enemyAnimation._anim == null)
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
        if (m_player == null)
        {
            var playerObject = GameObject.FindGameObjectWithTag(k_PlayerTag);
            if (playerObject != null) m_player = playerObject.transform;
        }

        if (m_firePoint == null) m_firePoint = transform;

        DetectGroundAndCameraBoundaries();
        ForceUpdateHpUI();

        m_aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(m_aiTaskCts.Token).Forget();
    }

    private void Update()
    {
        if (m_isDead) return;

        CheckIfOffScreen();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!m_isDead && (other.CompareTag(k_ArrowTag)))
        {
            TakeDamage(10);
        }
    }

    private void OnDestroy()
    {
        m_aiTaskCts?.Cancel();
        m_aiTaskCts?.Dispose();
    }
    #endregion

    #region 공개 메서드
    /// <summary>
    /// 적에게 데미지를 적용하고, 확률적으로 회피를 시도합니다.
    /// </summary>
    /// <param name="damage">적용할 데미지 양</param>
    public async void TakeDamage(int damage)
    {
        if (m_isDead || CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged) return;

        bool canTryEvade = CurrentState == EnemyState.Moving || CurrentState == EnemyState.Attacking || CurrentState == EnemyState.Idle;
        if (canTryEvade && UnityEngine.Random.value < m_evadeChance)
        {
            bool evaded = await EvadeAsync();
            if (evaded) return;
        }

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDamagedSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDamagedSfx);
        }

        if (Time.time > m_lastDamageTime + m_tempHpDecreaseDelay)
        {
            m_tempHp = m_currentHp;
        }

        m_currentHp = Mathf.Max(0, m_currentHp - damage);
        m_lastDamageTime = Time.time;

        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        EffectManager.Instance.ShowDamageText(gameObject, damage);

        if (m_currentHp <= 0)
        {
            Die();
        }
        else
        {
            PlayDamagedAnimationAsync().Forget();
        }
    }

    /// <summary>
    /// 현재 체력 정보를 기반으로 UI를 강제로 업데이트합니다.
    /// </summary>
    public void ForceUpdateHpUI()
    {
        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
    }

    /// <summary>
    /// 예비 체력만큼 현재 체력을 회복합니다.
    /// </summary>
    public void HealWithTempHp()
    {
        if (m_isDead) return;
        int recoveryAmount = m_tempHp - m_currentHp;
        if (recoveryAmount > 0)
        {
            m_currentHp += recoveryAmount;
            m_tempHp = m_currentHp;
            OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        }
    }
    #endregion

    #region AI 핵심 로직

    /// <summary>
    /// 적의 사망 처리를 담당합니다.
    /// </summary>
    private void Die()
    {
        if (m_isDead) return;
        m_isDead = true;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDeathSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDeathSfx);
        }
        
        m_aiTaskCts?.Cancel();
        m_rigidbody2D.linearVelocity = Vector2.zero;
        GetComponent<Collider2D>().enabled = false;
        SetState(EnemyState.Dead);
 
        m_currentHp = 0;
        m_tempHp = 0;
        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        
        OnEnemyDestroyed?.Invoke(this);
        
        Destroy(gameObject, 3f);
    }

    /// <summary>
    /// 적의 주 AI 루프를 비동기적으로 실행합니다.
    /// </summary>
    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !m_isDead)
        {
            switch (CurrentState)
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
            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token).SuppressCancellationThrow();
        }
    }

    /// <summary>
    /// 공격 애니메이션을 재생하고, 지정된 딜레이 후 화살을 발사합니다.
    /// </summary>
    private async UniTask PlayAttackAndFireAsync(bool useSkill, CancellationToken token)
    {
        SetState(useSkill ? EnemyState.SkillAttacking : EnemyState.Attacking);

        var attackClip = m_enemyAnimation.ATTACK_List.Count > 0 ? m_enemyAnimation.ATTACK_List[0] : null;
        try
        {
            if (useSkill)
            {
                m_lastSkillUseTime = Time.time;
                await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
            }
            else
            {
                if (attackClip != null)
                {
                    float fireDelay = attackClip.length * 0.5f;
                    await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

                    if (m_isDead) return;
                    PerformArrowLaunch();

                    await UniTask.Delay(TimeSpan.FromSeconds(attackClip.length - fireDelay), cancellationToken: token);
                }
                else
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                    if (m_isDead) return;
                    PerformArrowLaunch();
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 피격 애니메이션을 재생하고, 애니메이션이 끝나면 AI 루프를 다시 시작합니다.
    /// </summary>
    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (m_isDead || m_enemyAnimation == null) return;

        m_aiTaskCts?.Cancel();
        SetState(EnemyState.Damaged);
        m_rigidbody2D.linearVelocity = Vector2.zero;

        var damagedClip = m_enemyAnimation.DAMAGED_List.Count > 0 ? m_enemyAnimation.DAMAGED_List[0] : null;
        if (damagedClip != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClip.length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        
        if (!m_isDead)
        {
            SetState(EnemyState.Idle);
            m_aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(m_aiTaskCts.Token).Forget();
        }
    }

    #endregion

    #region 보조 메소드

    /// <summary>
    /// 적이 화면 밖으로 떨어졌는지 확인하고, 그렇다면 오브젝트를 파괴합니다.
    /// </summary>
    private void CheckIfOffScreen()
    {
        if (GameManager.Instance != null && GameManager.Instance.MainCamera != null)
        {
            float cameraBottom = GameManager.Instance.MainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0)).y;
            float destroyThreshold = cameraBottom - 2f;

            if (transform.position.y < destroyThreshold)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// 적의 위치를 유효한 이동 범위 내로 제한합니다.
    /// </summary>
    private void ClampPosition()
    {
        Vector2 clampedPosition = m_rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, m_effectiveMinX, m_effectiveMaxX);
        m_rigidbody2D.position = clampedPosition;
    }

    /// <summary>
    /// 플레이어를 향해 포물선 궤적의 화살을 발사합니다.
    /// </summary>
    private void PerformArrowLaunch()
    {
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyAttackSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyAttackSfx);
        }

        if (m_isDead || ObjectPoolManager.Instance == null || m_player == null) return;
        
        Vector3 startPos = m_firePoint.position;
        Vector2 direction = (m_player.position - startPos).normalized;
        Vector3 endPos = startPos + new Vector3(direction.x, direction.y, 0).normalized * m_fireDistance;

        Vector3 apex = (startPos + endPos) / 2f + Vector3.up * m_fireArcHeight;
        Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

        GameObject arrowObject = ObjectPoolManager.Instance.Get(m_arrowPrefab);
        if (arrowObject == null) return;

        arrowObject.transform.SetPositionAndRotation(startPos, Quaternion.identity);
        var arrowController = arrowObject.GetComponent<ArrowController>();
        if (arrowController != null)
        {
            arrowController.Owner = ArrowController.ArrowOwner.Enemy;
            arrowController.Launch(startPos, controlPoint, endPos, m_fireDuration);
        }
        else
        {
            ObjectPoolManager.Instance.Return(arrowObject);
        }
    }

    /// <summary>
    /// 주변 지형과 카메라 경계를 감지하여 유효한 이동 범위를 계산합니다.
    /// </summary>
    private void DetectGroundAndCameraBoundaries()
    {
        Bounds enemyBounds = m_collider.bounds;
        float enemyWidth = enemyBounds.extents.x;

        float maxProbeDistance = 20f;
        int probeSteps = 100;
        float stepDistance = maxProbeDistance / probeSteps;
        
        Vector2 characterFeet = (Vector2)transform.position - new Vector2(0, enemyBounds.extents.y);
        Vector2 boxCastSize = new Vector2(stepDistance, 0.1f);

        float rightEdgeX = transform.position.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            Vector2 probeOrigin = new Vector2(transform.position.x + i * stepDistance, characterFeet.y);
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
            if (hit.collider == null)
            {
                rightEdgeX = transform.position.x + (i - 1) * stepDistance;
                break;
            }
            if (i == probeSteps) rightEdgeX = transform.position.x + maxProbeDistance;
        }
        m_maxXPosition = rightEdgeX;

        float leftEdgeX = transform.position.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            Vector2 probeOrigin = new Vector2(transform.position.x - i * stepDistance, characterFeet.y);
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
            if (hit.collider == null)
            {
                leftEdgeX = transform.position.x - (i - 1) * stepDistance;
                break;
            }
            if (i == probeSteps) leftEdgeX = transform.position.x - maxProbeDistance;
        }
        m_minXPosition = leftEdgeX;

        if (GameManager.Instance != null && GameManager.Instance.MainCamera != null)
        {
            Camera cam = GameManager.Instance.MainCamera;
            m_cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
            m_cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;
        }
        else
        {
            m_cameraMinX = -Mathf.Infinity;
            m_cameraMaxX = Mathf.Infinity;
        }

        m_effectiveMinX = Mathf.Max(m_minXPosition, m_cameraMinX) + enemyWidth;
        m_effectiveMaxX = Mathf.Min(m_maxXPosition, m_cameraMaxX) - enemyWidth;
    }

    /// <summary>
    /// 이동 방향에 따라 캐릭터의 좌우를 뒤집습니다.
    /// </summary>
    private void FlipCharacter(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) < 0.01f) return;
        if (m_enemyAnimation == null) return;
        
        m_enemyAnimation.transform.rotation = Quaternion.Euler(0f, horizontalDirection > 0 ? 180f : 0f, 0f);
    }

    /// <summary>
    /// 적의 상태를 변경하고, 상태에 맞는 애니메이션을 재생합니다.
    /// </summary>
    private void SetState(EnemyState newState)
    {
        if (CurrentState == newState) return;
        
        CurrentState = newState;

        if (m_enemyAnimation == null) return;

        PlayerState animState;
        switch (newState)
        {
            case EnemyState.Idle:
                animState = PlayerState.IDLE;
                break;
            case EnemyState.Moving:
                animState = PlayerState.MOVE;
                break;
            case EnemyState.Attacking:
            case EnemyState.SkillAttacking:
                animState = PlayerState.ATTACK;
                break;
            case EnemyState.Damaged:
                animState = PlayerState.DAMAGED;
                break;
            case EnemyState.Dead:
                animState = PlayerState.DEATH;
                break;
            case EnemyState.Healing:
            case EnemyState.Evading:
            default:
                animState = PlayerState.OTHER;
                break;
        }
        
        m_enemyAnimation.PlayAnimation(animState, 0);
    }

    #if UNITY_EDITOR
    /// <summary>
    /// [테스트용] 인스펙터에서 적의 다발 사격 스킬을 즉시 실행합니다.
    /// </summary>
    public async void TestMultiShotSkill()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("스킬 테스트는 Play 모드에서만 가능합니다.");
            return;
        }

        if (m_player == null || SkillManager.Instance == null)
        {
            Debug.LogError("스킬 테스트에 필요한 Player 또는 SkillManager 참조가 없습니다.");
            return;
        }

        float directionToPlayer = m_player.position.x - transform.position.x;
        FlipCharacter(directionToPlayer);

        await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
    }
    
    /// <summary>
    /// [테스트용] 인스펙터에서 적의 회피 동작을 즉시 실행합니다.
    /// </summary>
    public async void TestEvade()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("회피 테스트는 Play 모드에서만 가능합니다.");
            return;
        }
        
        if (CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged || m_isDead)
        {
            Debug.LogWarning($"현재 상태({CurrentState})에서는 회피할 수 없습니다.");
            return;
        }

        await EvadeAsync();
    }
    #endif

    #endregion

    #region AI 상태별 로직

    /// <summary>
    /// AI가 'Idle' 상태일 때의 행동을 정의합니다. 다음 행동을 결정합니다.
    /// </summary>
    private async UniTask OnIdleStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        SetState(EnemyState.Idle);
        m_rigidbody2D.linearVelocity = Vector2.zero;

        if (m_player == null) return;

        bool canHeal = Time.time >= m_lastHealTime + m_healSkillCooldown;
        bool shouldHeal = (float)m_currentHp / m_maxHp <= m_healHealthThreshold;
        if (canHeal && shouldHeal && (m_tempHp > m_currentHp))
        {
            SetState(EnemyState.Healing);
            return;
        }

        float horizontalDistanceToPlayer = Mathf.Abs(m_player.position.x - transform.position.x);
        if (Mathf.Abs(horizontalDistanceToPlayer - m_fireDistance) > m_distanceTolerance)
        {
            SetState(EnemyState.Moving);
        }
        else
        {
            bool canUseSkill = Time.time >= m_lastSkillUseTime + m_skillCooldown;
            bool willUseSkill = canUseSkill && UnityEngine.Random.value < m_skillChance;
            if (willUseSkill)
            {
                SetState(EnemyState.SkillAttacking);
            }
            else
            {
                SetState(EnemyState.Attacking);
            }
        }
        await UniTask.Yield(token).SuppressCancellationThrow();
    }

    /// <summary>
    /// AI가 'Moving' 상태일 때의 행동을 정의합니다. 최적의 공격 위치로 이동합니다.
    /// </summary>
    private UniTask OnMovingStateAsync(CancellationToken token)
    {
        if (m_isDead) return UniTask.CompletedTask;

        if (m_player == null)
        {
            SetState(EnemyState.Idle);
            return UniTask.CompletedTask;
        }

        float horizontalDistanceToPlayer = Mathf.Abs(m_player.position.x - transform.position.x);
        if (Mathf.Abs(horizontalDistanceToPlayer - m_fireDistance) <= m_distanceTolerance)
        {
            SetState(EnemyState.Idle);
            return UniTask.CompletedTask;
        }
        
        float xDirection = Mathf.Sign(m_player.position.x - transform.position.x);
        float moveDirection = (horizontalDistanceToPlayer > m_fireDistance) ? 1f : -1f;
        float targetXVelocity = xDirection * m_moveSpeed * moveDirection;

        float moveSign = Mathf.Sign(targetXVelocity);
        if (moveSign != 0)
        {
            Bounds enemyBounds = m_collider.bounds;
            Vector2 groundCheckOrigin = (Vector2)enemyBounds.center + new Vector2(moveSign * enemyBounds.extents.x, -enemyBounds.extents.y - 0.05f);
            RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, m_groundLayer);

            if (groundHit.collider == null)
            {
                targetXVelocity = 0;
            }
        }

        if ((transform.position.x <= m_effectiveMinX && targetXVelocity < 0) || (transform.position.x >= m_effectiveMaxX && targetXVelocity > 0))
        {
            targetXVelocity = 0;
        }

        if (Mathf.Abs(targetXVelocity) > 0.01f)
        {
            SetState(EnemyState.Moving);
            FlipCharacter(targetXVelocity);
            m_rigidbody2D.linearVelocity = new Vector2(targetXVelocity, m_rigidbody2D.linearVelocity.y);
        }
        else
        {
            m_rigidbody2D.linearVelocity = new Vector2(0, m_rigidbody2D.linearVelocity.y);
            
            bool canUseSkill = Time.time >= m_lastSkillUseTime + m_skillCooldown;
            bool willUseSkill = canUseSkill && UnityEngine.Random.value < m_skillChance;
            if (willUseSkill)
            {
                SetState(EnemyState.SkillAttacking);
            }
            else
            {
                SetState(EnemyState.Attacking);
            }
        }

        ClampPosition();
        return UniTask.CompletedTask;
    }

    /// <summary>
    /// AI가 'Attacking' 상태일 때의 행동을 정의합니다. 일반 공격을 수행합니다.
    /// </summary>
    private async UniTask OnAttackingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        if (m_player != null)
        {
            float directionToPlayer = m_player.position.x - transform.position.x;
            FlipCharacter(directionToPlayer);
        }

        try
        {
            await PlayAttackAndFireAsync(false, token);
            await UniTask.Delay(TimeSpan.FromSeconds(m_attackCooldown), cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!m_isDead && CurrentState == EnemyState.Attacking)
                SetState(EnemyState.Idle);
        }
    }

    /// <summary>
    /// AI가 'SkillAttacking' 상태일 때의 행동을 정의합니다. 스킬 공격을 수행합니다.
    /// </summary>
    private async UniTask OnSkillAttackingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        if (m_player != null)
        {
            float directionToPlayer = m_player.position.x - transform.position.x;
            FlipCharacter(directionToPlayer);
        }

        try
        {
            await PlayAttackAndFireAsync(true, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!m_isDead && CurrentState == EnemyState.SkillAttacking)
                SetState(EnemyState.Idle);
        }
    }

    /// <summary>
    /// AI가 'Healing' 상태일 때의 행동을 정의합니다. 체력을 회복합니다.
    /// </summary>
    private async UniTask OnHealingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyHealSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyHealSfx);
        }

        m_lastHealTime = Time.time;
        
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

        if (token.IsCancellationRequested || m_isDead) return;

        HealWithTempHp();
        SetState(EnemyState.Idle);
    }

    #endregion

    #region 회피 로직

    /// <summary>
    /// 공격 회피를 위한 비동기 대쉬 로직을 실행합니다.
    /// </summary>
    /// <returns>회피에 성공하면 true, 실패하면 false를 반환합니다.</returns>
    private async UniTask<bool> EvadeAsync()
    {
        if (m_isDead) return false;

        DetectGroundAndCameraBoundaries();

        float currentX = m_rigidbody2D.position.x;
        float spaceToLeft = currentX - m_effectiveMinX;
        float spaceToRight = m_effectiveMaxX - currentX;
        float direction = (spaceToRight > spaceToLeft) ? 1f : -1f;

        float maxDashDistance = m_evadeDashSpeed * m_evadeDashDuration;
        Bounds enemyBounds = m_collider.bounds;
        float wallLimitedDistance = maxDashDistance;
        RaycastHit2D wallHit = Physics2D.BoxCast(
            (Vector2)transform.position + m_collider.offset,
            new Vector2(enemyBounds.size.x, enemyBounds.size.y * 0.9f),
            0f, new Vector2(direction, 0), maxDashDistance, m_groundLayer);
        if (wallHit.collider != null)
        {
            wallLimitedDistance = wallHit.distance;
        }

        float finalDashDistance = wallLimitedDistance;
        int steps = 10;
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, enemyBounds.center.y);
            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos, new Vector2(enemyBounds.size.x * 0.9f, 0.1f),
                0f, Vector2.down, enemyBounds.extents.y + 0.5f, m_groundLayer);
            if (groundUnderneath.collider == null)
            {
                finalDashDistance = (i - 1) * stepDistance;
                break;
            }
        }

        finalDashDistance = Mathf.Max(0, finalDashDistance - enemyBounds.extents.x);
        float finalTargetX = Mathf.Clamp(currentX + direction * finalDashDistance, m_effectiveMinX, m_effectiveMaxX);
        float actualDashDistance = Mathf.Abs(finalTargetX - currentX);
        float actualDuration = actualDashDistance / m_evadeDashSpeed;
        
        if (actualDashDistance < m_minEvadeDistance)
        {
            return false;
        }

        m_aiTaskCts?.Cancel();
        SetState(EnemyState.Evading);
        FlipCharacter(direction);

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyEvadeSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyEvadeSfx);
        }
        if (m_afterimageEffect != null)
            m_afterimageEffect.StartEffect(actualDuration);

        var evadeCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(evadeCts.Token, this.GetCancellationTokenOnDestroy());

        try
        {
            m_rigidbody2D.linearVelocity = new Vector2(direction * m_evadeDashSpeed, m_rigidbody2D.linearVelocity.y);
            await UniTask.Delay(TimeSpan.FromSeconds(actualDuration), cancellationToken: linkedCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (m_rigidbody2D != null)
            {
                m_rigidbody2D.linearVelocity = new Vector2(0, m_rigidbody2D.linearVelocity.y);
            }

            if (!m_isDead)
            {
                SetState(EnemyState.Idle);
                m_aiTaskCts = new CancellationTokenSource();
                AI_LoopAsync(m_aiTaskCts.Token).Forget();
            }
        }

        return true;
    }
    #endregion
}
