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
    public event Action<int, int, int> OnHpChanged; // (현재, 예비, 최대)

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
    [SerializeField] private float m_skillChance = 0.3f; // 30% 확률로 스킬 사용
    [SerializeField] private Transform m_skillHandPoint; // 스킬 무기가 장착될 손 위치
    private float m_lastSkillUseTime = -999f;
    [SerializeField] private float m_healSkillCooldown = 20f;
    [SerializeField, Range(0, 1)] private float m_healHealthThreshold = 0.4f; // 체력이 40% 이하일 때 회복 시도
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
    
    // 상수
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
    public async void TakeDamage(int damage)
    {
        // [회피 우선권] 회피, 피격, 사망 중에는 새로운 데미지나 회피를 처리하지 않음
        if (m_isDead || CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged) return;

        // 회피 판정: 특정 상태(이동, 공격, 대기)일 때만 확률적으로 발동
        bool canTryEvade = CurrentState == EnemyState.Moving || CurrentState == EnemyState.Attacking || CurrentState == EnemyState.Idle;
        if (canTryEvade && UnityEngine.Random.value < m_evadeChance)
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

        // 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDamagedSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDamagedSfx);
        }

        // 데미지를 받기 시작한 시점의 체력을 예비 체력으로 기록합니다.
        // 예비 체력 감소 딜레이 시간 내에 추가 타격을 받으면, 예비 체력은 갱신되지 않고 현재 체력만 감소합니다.
        if (Time.time > m_lastDamageTime + m_tempHpDecreaseDelay)
        {
            m_tempHp = m_currentHp;
        }

        m_currentHp -= damage;
        m_currentHp = Mathf.Max(0, m_currentHp);
        m_lastDamageTime = Time.time;

        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);

        // 데미지 텍스트 표시 (캐릭터 머리 위)
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
            m_tempHp = m_currentHp; // 회복 후에는 예비 체력과 현재 체력을 일치시킴
            OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
            Debug.Log("적이 체력을 회복했습니다!");
        }
    }
    #endregion

    #region AI 핵심 로직

    private void Die()
    {
        if (m_isDead) return;
        m_isDead = true;

        // 사운드 재생
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

    private async UniTask PlayAttackAndFireAsync(bool useSkill, CancellationToken token)
    {
        SetState(useSkill ? EnemyState.SkillAttacking : EnemyState.Attacking);

        var attackClip = m_enemyAnimation.ATTACK_List.Count > 0 ? m_enemyAnimation.ATTACK_List[0] : null;
        try
        {
            if (useSkill)
            {
                m_lastSkillUseTime = Time.time;
                Debug.Log("Enemy uses AK47 Skill!");
                // 스킬 실행을 기다립니다. 이 시간 동안 공격 애니메이션이 재생됩니다.
                await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
            }
            else
            {
                // 기존 일반 공격 로직
                if (attackClip != null)
                {
                    float fireDelay = attackClip.length * 0.5f;
                    await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

                    if (m_isDead) return;
                    PerformArrowLaunch();

                    await UniTask.Delay(TimeSpan.FromSeconds(attackClip.length - fireDelay), cancellationToken: token);
                }
                else // 애니메이션 클립이 없는 경우 기본 딜레이 후 발사
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                    if (m_isDead) return;
                    PerformArrowLaunch();
                    await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                }
            }
        }
        catch (OperationCanceledException) { /* 예외 발생 시에도 finally가 실행되도록 보장 */ }
    
    }

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
        
        // 피격 애니메이션이 끝난 후, 사망 상태가 아니라면 다시 AI 루프를 시작합니다.
        if (!m_isDead)
        {
            SetState(EnemyState.Idle);
            m_aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(m_aiTaskCts.Token).Forget();
        }
    }

    #endregion

    #region 보조 메소드

    private void CheckIfOffScreen()
    {
        Vector3 currentTransformPosition = transform.position;
        if (GameManager.Instance != null && GameManager.Instance.MainCamera != null) // GameManager와 MainCamera가 유효한지 확인
        {
            float cameraBottom = GameManager.Instance.MainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0)).y;
            float destroyThreshold = cameraBottom - 2f;

            if (currentTransformPosition.y < destroyThreshold)
            {
                Destroy(gameObject);
            }
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = m_rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, m_effectiveMinX, m_effectiveMaxX);
        m_rigidbody2D.position = clampedPosition;
    }

    private void PerformArrowLaunch()
    {
        // 사운드 재생
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

    private void DetectGroundAndCameraBoundaries()
    {
        Vector3 currentTransformPosition = transform.position;
        // 캐릭터 너비의 절반 (중심에서 가장자리까지의 거리)
        Bounds enemyBounds = m_collider.bounds;
        float enemyWidth = enemyBounds.extents.x;

        // 1. 지면 경계 감지 (Probing 방식)
        // 타일맵의 틈으로 인해 Raycast가 실패하는 문제를 방지하기 위해 BoxCast를 사용합니다.
        // 또한, 탐색 정밀도를 높여 더 정확한 경계를 찾습니다.

        float maxProbeDistance = 20f; // 양쪽으로 탐색할 최대 거리
        int probeSteps = 100; // 탐색 정밀도 (높을수록 정확하지만 비용 증가)
        float stepDistance = maxProbeDistance / probeSteps;
        
        // 캐릭터의 발 위치를 기준으로 탐색을 시작합니다.
        Vector2 characterFeet = (Vector2)currentTransformPosition - new Vector2(0, enemyBounds.extents.y);
        Vector2 boxCastSize = new Vector2(stepDistance, 0.1f); // 탐색 간격만큼의 너비를 가진 작은 상자

        // --- 오른쪽 경계(절벽) 찾기 ---
        float rightEdgeX = currentTransformPosition.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            // 현재 위치에서 오른쪽으로 한 스텝 이동한 지점
            Vector2 probeOrigin = new Vector2(currentTransformPosition.x + i * stepDistance, characterFeet.y);
            
            // 해당 지점 바로 아래에 땅이 있는지 확인합니다.
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
            
            if (hit.collider == null)
            {
                // 땅이 없으면, 바로 이전 지점이 절벽의 가장자리입니다.
                rightEdgeX = currentTransformPosition.x + (i - 1) * stepDistance;
                break;
            }
            
            // 탐색이 끝까지 도달했다면, 최대 탐색 거리를 경계로 간주합니다.
            if (i == probeSteps) rightEdgeX = currentTransformPosition.x + maxProbeDistance;
        }
        m_maxXPosition = rightEdgeX;

        // --- 왼쪽 경계(절벽) 찾기 ---
        float leftEdgeX = currentTransformPosition.x;
        for (int i = 1; i <= probeSteps; i++)
        {
            Vector2 probeOrigin = new Vector2(currentTransformPosition.x - i * stepDistance, characterFeet.y);
            RaycastHit2D hit = Physics2D.BoxCast(probeOrigin, boxCastSize, 0f, Vector2.down, 0.2f, m_groundLayer);
            
            if (hit.collider == null)
            {
                leftEdgeX = currentTransformPosition.x - (i - 1) * stepDistance;
                break;
            }
            if (i == probeSteps) leftEdgeX = currentTransformPosition.x - maxProbeDistance;
        }
        m_minXPosition = leftEdgeX; // 왼쪽 경계 설정

        // 2. 카메라 경계 감지
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

        // 3. 최종 유효 이동 범위 계산 (지면과 카메라 경계의 교집합)
        // 캐릭터의 중심이 이동할 수 있는 최종 범위를 계산합니다.
        // 캐릭터가 플랫폼과 카메라 뷰 밖으로 완전히 나가지 않도록 너비만큼 보정합니다.
        m_effectiveMinX = Mathf.Max(m_minXPosition, m_cameraMinX) + enemyWidth;
        m_effectiveMaxX = Mathf.Min(m_maxXPosition, m_cameraMaxX) - enemyWidth;
    }

    private void FlipCharacter(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) < 0.01f) return;
        if (m_enemyAnimation == null) return;

        // PlayerControl과 동일한 로직으로, SPUM_Prefabs의 자식 오브젝트를 회전시킵니다.
        // 오른쪽을 볼 때 Y축 회전값이 180, 왼쪽을 볼 때 0이 되도록 설정합니다.
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
            case EnemyState.SkillAttacking: // SkillAttacking도 ATTACK 애니메이션 사용
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
            default: // 기타 상태는 OTHER 애니메이션 사용
                animState = PlayerState.OTHER;
                break;
        }
        
        m_enemyAnimation.PlayAnimation(animState, 0);
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

        if (m_player == null || SkillManager.Instance == null)
        {
            Debug.LogError("스킬 테스트에 필요한 Player 또는 SkillManager 참조가 없습니다.");
            return;
        }

        Debug.Log("--- SKILL TEST: 적 다발 사격 스킬 실행 ---");
        // 테스트 시 자연스럽게 보이도록 플레이어 방향으로 몸을 돌립니다.
        float directionToPlayer = m_player.position.x - transform.position.x;
        FlipCharacter(directionToPlayer);

        // SkillManager를 통해 스킬을 직접 실행합니다.
        await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
        Debug.Log("--- SKILL TEST: 완료 ---");
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

        Debug.Log("--- EVADE TEST: 적 회피 동작 실행 ---");
        await EvadeAsync();
        Debug.Log("--- EVADE TEST: 완료 ---");
    }
    #endif

    #endregion

    #region AI 상태별 로직

    private async UniTask OnIdleStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        SetState(EnemyState.Idle);
        m_rigidbody2D.linearVelocity = Vector2.zero;

        if (m_player == null) return;

        // [우선순위 1: 회복]
        bool canHeal = Time.time >= m_lastHealTime + m_healSkillCooldown;
        bool shouldHeal = (float)m_currentHp / m_maxHp <= m_healHealthThreshold;
        if (canHeal && shouldHeal && (m_tempHp > m_currentHp))
        {
            SetState(EnemyState.Healing);
            return;
        }

        // [우선순위 2: 위치 조정 또는 공격]
        float horizontalDistanceToPlayer = Mathf.Abs(m_player.position.x - transform.position.x);
        if (Mathf.Abs(horizontalDistanceToPlayer - m_fireDistance) > m_distanceTolerance)
        {
            SetState(EnemyState.Moving);
        }
        else
        {
            // 공격 범위 내에 있을 경우, 스킬 사용 여부를 결정합니다.
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

    private UniTask OnMovingStateAsync(CancellationToken token)
    {
        if (m_isDead) return UniTask.CompletedTask;

        if (m_player == null)
        {
            SetState(EnemyState.Idle);
            return UniTask.CompletedTask;
        }

        Vector3 currentTransformPosition = transform.position;
        float horizontalDistanceToPlayer = Mathf.Abs(m_player.position.x - currentTransformPosition.x);
        if (Mathf.Abs(horizontalDistanceToPlayer - m_fireDistance) <= m_distanceTolerance)
        {
            SetState(EnemyState.Idle); // 목표 도달, 상태 재평가
            return UniTask.CompletedTask;
        }
        
        float xDirection = Mathf.Sign(m_player.position.x - currentTransformPosition.x);
        float moveDirection = (horizontalDistanceToPlayer > m_fireDistance) ? 1f : -1f;
        float targetXVelocity = xDirection * m_moveSpeed * moveDirection;

        // --- 절벽 감지 로직 추가 ---
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

        // --- 경계 감지 로직 (지면 + 카메라) ---
        if ((currentTransformPosition.x <= m_effectiveMinX && targetXVelocity < 0) || (currentTransformPosition.x >= m_effectiveMaxX && targetXVelocity > 0))
        {
            targetXVelocity = 0;
        }

        // --- 애니메이션 및 이동 처리 ---
        if (Mathf.Abs(targetXVelocity) > 0.01f)
        {
            SetState(EnemyState.Moving);
            FlipCharacter(targetXVelocity);
            m_rigidbody2D.linearVelocity = new Vector2(targetXVelocity, m_rigidbody2D.linearVelocity.y);
        }
        else
        {
            // [수정] 이동 불가 시, Idle 상태를 거치지 않고 즉시 공격 상태로 전환
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

    private async UniTask OnAttackingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        // 공격 방향으로 몸 돌리기
        Vector3 currentTransformPosition = transform.position;
        if (m_player != null) // 플레이어가 유효한지 확인
        {
            float directionToPlayer = m_player.position.x - currentTransformPosition.x;
            FlipCharacter(directionToPlayer);
        }

        try
        {
            // 공격 애니메이션 재생 및 발사 (이 작업이 끝나야 다음 상태로 감)
            await PlayAttackAndFireAsync(false, token);
            // 공격 후 쿨다운
            await UniTask.Delay(TimeSpan.FromSeconds(m_attackCooldown), cancellationToken: token);
        }
        catch (OperationCanceledException) { /* 쿨다운 중 취소될 수 있음 */ }
        finally
        {
            if (!m_isDead && CurrentState == EnemyState.Attacking)
                SetState(EnemyState.Idle); // 공격 완료 후 Idle로 돌아가 재평가
        }
    }

    private async UniTask OnSkillAttackingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        // 공격 방향으로 몸 돌리기
        Vector3 currentTransformPosition = transform.position;
        if (m_player != null) // 플레이어가 유효한지 확인
        {
            float directionToPlayer = m_player.position.x - currentTransformPosition.x;
            FlipCharacter(directionToPlayer);
        }

        try
        {
            // 스킬 사용
            await PlayAttackAndFireAsync(true, token);
        }
        catch (OperationCanceledException) { /* 스킬 사용 중 취소될 수 있음 */ }
        finally
        {
            if (!m_isDead && CurrentState == EnemyState.SkillAttacking)
                SetState(EnemyState.Idle); // 스킬 사용 후에는 즉시 Idle로 돌아가 다음 행동을 결정합니다.
        }
    }

    private async UniTask OnHealingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        // 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyHealSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyHealSfx);
        }

        Debug.Log("적이 회복을 시전합니다.");
        m_lastHealTime = Time.time;

        // TODO: 회복 애니메이션/이펙트 재생
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

        if (token.IsCancellationRequested || m_isDead) return;

        HealWithTempHp();
        SetState(EnemyState.Idle);
    }

    #endregion

    #region 회피 로직

    /// <summary>
    /// 공격 회피를 위한 비동기 대쉬 로직입니다.
    /// </summary>
    private async UniTask<bool> EvadeAsync()
    {
        if (m_isDead) return false;

        DetectGroundAndCameraBoundaries();

        Vector2 currentRigidbodyPosition = m_rigidbody2D.position;
        float currentX = currentRigidbodyPosition.x;
        float spaceToLeft = currentX - m_effectiveMinX;
        float spaceToRight = m_effectiveMaxX - currentX;
        float direction = (spaceToRight > spaceToLeft) ? 1f : -1f;
        string directionString = direction > 0 ? "오른쪽" : "왼쪽";
        Debug.Log($"<color=orange>[AI-Evade]</color> 회피 방향 결정. 왼쪽 공간: {spaceToLeft:F2}, 오른쪽 공간: {spaceToRight:F2}. 선택: {directionString}");

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
        
        Debug.Log($"<color=orange>[AI-Evade]</color> 계산된 실제 회피 가능 거리: {actualDashDistance:F2}");

        if (actualDashDistance < m_minEvadeDistance)
        {
            Debug.Log($"<color=orange>[AI-Evade]</color> 회피 실패: 이동 가능 거리({actualDashDistance:F2})가 최소 회피 거리({m_minEvadeDistance})보다 짧습니다.");
            return false;
        }

        Debug.Log($"<color=green>[AI-Evade]</color> 회피 성공! {actualDashDistance:F2}만큼 {directionString}으로 대쉬합니다.");
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
        catch (OperationCanceledException) { /* 회피 중 취소됨 */ }
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
