using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

/// <summary>
/// 플레이어를 공격하는 적 AI 클래스입니다.
/// UniTask를 활용한 비동기 FSM(유한 상태 머신) 구조로 최적화되었습니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    #region Events & Enums
    public static event Action<Enemy> OnEnemyDestroyed;
    public event Action<int, int, int> OnHpChanged; // (Current, Temp, Max)

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
    #endregion

    #region Inspector Fields
    [Header("체력 설정")]
    [SerializeField] private int m_maxHp = 150;
    [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
    [SerializeField] private float m_tempHpDecreaseDelay = 3f;

    [Header("AI 설정")]
    [SerializeField] private Transform m_player;
    [SerializeField] private float m_moveSpeed = 3f;
    [Tooltip("공격 위치로 간주할 거리의 허용 오차 범위입니다.")]
    [SerializeField] private float m_distanceTolerance = 0.5f;

    [Header("지형 설정")]
    [Tooltip("지면을 감지할 레이어 마스크")]
    [SerializeField] private LayerMask m_groundLayer;

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
    [SerializeField] private float m_healSkillCooldown = 20f;
    [SerializeField, Range(0, 1)] private float m_healHealthThreshold = 0.4f; // 체력이 40% 이하일 때 회복 시도

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
    #endregion

    #region Private Fields
    // Status
    private int m_currentHp;
    private int m_tempHp;
    private bool m_isDead;
    private float m_lastDamageTime;
    private float m_lastSkillUseTime = -999f;
    private float m_lastHealTime = -999f;
    
    // Components
    private Rigidbody2D m_rigidbody2D;
    private Collider2D m_collider;
    private AfterimageEffect m_afterimageEffect;
    private SPUM_Prefabs m_enemyAnimation;
    private Camera m_mainCamera;

    // Async Control
    private CancellationTokenSource m_aiTaskCts;

    // Boundaries (Optimized)
    private float m_cameraMinX;
    private float m_cameraMaxX;
    private float m_enemyWidthHalf;

    // Constants
    private const string k_PlayerTag = "Player";
    private const string k_ArrowTag = "Arrow";
    #endregion

    #region Properties
    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;
    public bool IsDead => m_isDead;
    #endregion

    #region MonoBehaviour Flow
    private void Awake()
    {
        InitializeComponents();
        InitializeStatus();
    }

    private void Start()
    {
        InitializeDependencies();
        ForceUpdateHpUI();

        // AI 루프 시작
        StartAILoop();
    }

    private void Update()
    {
        if (m_isDead) return;
        CheckIfOffScreen();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!m_isDead && other.CompareTag(k_ArrowTag))
        {
            // 화살 데미지 하드코딩 대신 컴포넌트나 인자를 통해 가져오는 것이 좋으나, 
            // 원본 로직 유지를 위해 10으로 설정합니다.
            TakeDamage(10);
        }
    }

    private void OnDestroy()
    {
        CancelAITask();
    }
    #endregion

    #region Initialization
    private void InitializeComponents()
    {
        m_rigidbody2D = GetComponent<Rigidbody2D>();
        m_collider = GetComponent<Collider2D>();
        
        if (!TryGetComponent(out m_afterimageEffect))
        {
            Debug.LogWarning("[Enemy] AfterimageEffect 컴포넌트가 없습니다. 회피 잔상 효과가 동작하지 않습니다.");
        }

        m_enemyAnimation = GetComponentInChildren<SPUM_Prefabs>();
        
        m_rigidbody2D.gravityScale = 1;
        m_rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (m_enemyAnimation != null)
        {
            if (!m_enemyAnimation.allListsHaveItemsExist())
            {
                m_enemyAnimation.PopulateAnimationLists();
            }
            m_enemyAnimation.OverrideControllerInit();
            if (m_enemyAnimation._anim == null)
            {
                Debug.LogError("[Enemy] SPUM_Prefabs에 Animator 참조가 없습니다!");
            }
        }
        else
        {
            Debug.LogError("[Enemy] SPUM_Prefabs 컴포넌트를 찾을 수 없습니다.");
        }

        if (m_collider != null)
        {
            m_enemyWidthHalf = m_collider.bounds.extents.x;
        }
    }

    private void InitializeStatus()
    {
        m_currentHp = m_maxHp;
        m_tempHp = m_maxHp;
    }

    private void InitializeDependencies()
    {
        if (m_player == null)
        {
            var playerObject = GameObject.FindGameObjectWithTag(k_PlayerTag);
            if (playerObject != null) m_player = playerObject.transform;
        }

        if (m_firePoint == null) m_firePoint = transform;

        // 메인 카메라 캐싱 (성능 최적화)
        if (GameManager.Instance != null)
        {
            m_mainCamera = GameManager.Instance.MainCamera;
        }
        else
        {
            m_mainCamera = Camera.main;
        }
    }
    #endregion

    #region Public Methods
    public async void TakeDamage(int damage)
    {
        // [회피 우선권] 회피, 이미 피격 중, 사망 중에는 처리하지 않음
        if (m_isDead || CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged) return;

        // 회피 판정
        bool canTryEvade = CurrentState == EnemyState.Moving || CurrentState == EnemyState.Attacking || CurrentState == EnemyState.Idle;
        if (canTryEvade && UnityEngine.Random.value < m_evadeChance)
        {
            Debug.Log("<color=orange>[AI-Evade]</color> 회피 시도!");
            bool evaded = await EvadeAsync();
            if (evaded) return; // 회피 성공 시 데미지 무효화
        }

        // 데미지 처리
        ApplyDamageLogic(damage);

        // 사망 처리 또는 피격 애니메이션
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

    public void HealWithTempHp()
    {
        if (m_isDead) return;
        
        int recoveryAmount = m_tempHp - m_currentHp;
        if (recoveryAmount > 0)
        {
            m_currentHp += recoveryAmount;
            m_tempHp = m_currentHp;
            OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
            Debug.Log("적이 체력을 회복했습니다!");
        }
    }
    #endregion

    #region AI Core Logic
    private void StartAILoop()
    {
        CancelAITask(); // 기존 작업 정리
        m_aiTaskCts = new CancellationTokenSource();
        AI_LoopAsync(m_aiTaskCts.Token).Forget();
    }

    private void CancelAITask()
    {
        if (m_aiTaskCts != null)
        {
            m_aiTaskCts.Cancel();
            m_aiTaskCts.Dispose();
            m_aiTaskCts = null;
        }
    }

    private async UniTaskVoid AI_LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !m_isDead)
        {
            // 카메라 경계 업데이트 (매 프레임 X, 루프 시작시 체크)
            UpdateCameraBoundaries();

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
            
            // FixedUpdate 타이밍보다는 AI 결정 루프는 Update 타이밍이 적절
            await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
        }
    }

    private void Die()
    {
        if (m_isDead) return;
        m_isDead = true;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDeathSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDeathSfx);
        }
        
        CancelAITask();
        
        // 정지 (Unity 6 호환: linearVelocity, 구버전: velocity)
        m_rigidbody2D.linearVelocity = Vector2.zero; 
        m_collider.enabled = false;
        
        SetState(EnemyState.Dead);
        
        m_currentHp = 0;
        m_tempHp = 0;
        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        
        OnEnemyDestroyed?.Invoke(this);
        
        Destroy(gameObject, 3f);
    }

    private void ApplyDamageLogic(int damage)
    {
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDamagedSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDamagedSfx);
        }

        // 예비 체력 로직
        if (Time.time > m_lastDamageTime + m_tempHpDecreaseDelay)
        {
            m_tempHp = m_currentHp;
        }

        m_currentHp = Mathf.Max(0, m_currentHp - damage);
        m_lastDamageTime = Time.time;

        OnHpChanged?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        EffectManager.Instance.ShowDamageText(gameObject, damage);
    }
    #endregion

    #region AI States
    private async UniTask OnIdleStateAsync(CancellationToken token)
    {
        SetState(EnemyState.Idle);
        m_rigidbody2D.linearVelocity = Vector2.zero;

        if (m_player == null)
        {
            await UniTask.Yield(token);
            return;
        }

        // 1. 회복 판단
        bool canHeal = Time.time >= m_lastHealTime + m_healSkillCooldown;
        bool shouldHeal = (float)m_currentHp / m_maxHp <= m_healHealthThreshold;
        if (canHeal && shouldHeal && (m_tempHp > m_currentHp))
        {
            SetState(EnemyState.Healing);
            return;
        }

        // 2. 이동 및 공격 판단
        float distanceToPlayer = Mathf.Abs(m_player.position.x - transform.position.x);
        if (Mathf.Abs(distanceToPlayer - m_fireDistance) > m_distanceTolerance)
        {
            SetState(EnemyState.Moving);
        }
        else
        {
            bool canUseSkill = Time.time >= m_lastSkillUseTime + m_skillCooldown;
            bool willUseSkill = canUseSkill && UnityEngine.Random.value < m_skillChance;
            SetState(willUseSkill ? EnemyState.SkillAttacking : EnemyState.Attacking);
        }
    }

    private async UniTask OnMovingStateAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !m_isDead)
        {
            if (m_player == null)
            {
                SetState(EnemyState.Idle);
                return;
            }

            float currentX = transform.position.x;
            float playerX = m_player.position.x;
            float dist = Mathf.Abs(playerX - currentX);

            // 목표 거리 도달 확인
            if (Mathf.Abs(dist - m_fireDistance) <= m_distanceTolerance)
            {
                SetState(EnemyState.Idle);
                return;
            }

            // 이동 방향 결정 (거리를 유지하려고 함)
            float moveDirectionSign = (dist > m_fireDistance) ? Mathf.Sign(playerX - currentX) : -Mathf.Sign(playerX - currentX);
            
            // 안전한 이동인지 확인 (절벽/카메라 체크)
            if (!IsSafeToMove(moveDirectionSign))
            {
                m_rigidbody2D.linearVelocity = new Vector2(0, m_rigidbody2D.linearVelocity.y);
                SetState(EnemyState.Idle);
                // 이동 불가 시 잠시 대기
                await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                return;
            }

            // 이동 실행
            SetState(EnemyState.Moving);
            FlipCharacter(moveDirectionSign);
            m_rigidbody2D.linearVelocity = new Vector2(moveDirectionSign * m_moveSpeed, m_rigidbody2D.linearVelocity.y);

            await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
        }
    }

    private async UniTask OnAttackingStateAsync(CancellationToken token)
    {
        await PerformAttackRoutine(false, token);
    }

    private async UniTask OnSkillAttackingStateAsync(CancellationToken token)
    {
        await PerformAttackRoutine(true, token);
    }

    private async UniTask OnHealingStateAsync(CancellationToken token)
    {
        if (m_isDead) return;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyHealSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyHealSfx);
        }

        m_lastHealTime = Time.time;
        // 회복 애니메이션 대기 (여기서는 임의 시간 설정)
        await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

        if (!token.IsCancellationRequested && !m_isDead)
        {
            HealWithTempHp();
            SetState(EnemyState.Idle);
        }
    }
    #endregion

    #region Helper Logic (Movement & Physics)

    private async UniTask PerformAttackRoutine(bool useSkill, CancellationToken token)
    {
        if (m_isDead) return;

        EnemyState state = useSkill ? EnemyState.SkillAttacking : EnemyState.Attacking;
        SetState(state);

        // 플레이어 방향 보기
        if (m_player != null)
        {
            FlipCharacter(m_player.position.x - transform.position.x);
        }

        try
        {
            if (useSkill)
            {
                m_lastSkillUseTime = Time.time;
                Debug.Log("[Enemy] 스킬 사용!");
                await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
            }
            else
            {
                // 일반 공격
                var attackClip = (m_enemyAnimation.ATTACK_List.Count > 0) ? m_enemyAnimation.ATTACK_List[0] : null;
                float animLength = attackClip != null ? attackClip.length : 1f;
                float fireDelay = animLength * 0.5f;

                await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);
                
                if (!m_isDead) PerformArrowLaunch();
                
                await UniTask.Delay(TimeSpan.FromSeconds(animLength - fireDelay), cancellationToken: token);
            }

            // 후딜레이 (쿨다운)
            if (state == EnemyState.Attacking) // 일반 공격만 쿨다운 적용 (스킬은 자체 쿨타임)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(m_attackCooldown), cancellationToken: token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!m_isDead && CurrentState == state)
            {
                SetState(EnemyState.Idle);
            }
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (m_isDead || m_enemyAnimation == null) return;

        // 기존 AI 작업 취소
        CancelAITask();
        
        SetState(EnemyState.Damaged);
        m_rigidbody2D.linearVelocity = Vector2.zero;

        var damagedClip = (m_enemyAnimation.DAMAGED_List.Count > 0) ? m_enemyAnimation.DAMAGED_List[0] : null;
        float duration = damagedClip != null ? damagedClip.length : 0.5f;

        // 피격 애니메이션 대기 (파괴 시 토큰 사용)
        await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: this.GetCancellationTokenOnDestroy())
                     .SuppressCancellationThrow();

        if (!m_isDead)
        {
            StartAILoop(); // AI 재시작
        }
    }

    /// <summary>
    /// 이동하려는 방향이 안전한지(땅이 있고, 카메라 밖이 아닌지) 확인합니다.
    /// 기존의 무거운 전체 스캔 로직을 대체합니다.
    /// </summary>
    private bool IsSafeToMove(float directionSign)
    {
        if (Mathf.Abs(directionSign) < 0.01f) return true;

        Vector3 pos = transform.position;
        
        // 1. 카메라 경계 체크
        float nextX = pos.x + (directionSign * 0.5f); // 0.5f 정도 앞을 미리 봄
        if (nextX < m_cameraMinX + m_enemyWidthHalf || nextX > m_cameraMaxX - m_enemyWidthHalf)
        {
            return false;
        }

        // 2. 절벽 체크 (레이캐스트)
        // 캐릭터의 진행 방향 앞쪽, 발 밑을 확인
        Vector2 origin = (Vector2)pos + new Vector2(directionSign * m_enemyWidthHalf, -m_collider.bounds.extents.y);
        // 약간 더 앞으로 나가서 찍어봄
        origin.x += directionSign * 0.2f; 
        
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 1.0f, m_groundLayer);
        
        // 디버그용 (필요시 주석 해제)
        // Debug.DrawRay(origin, Vector2.down, hit.collider != null ? Color.green : Color.red);

        return hit.collider != null;
    }

    private void UpdateCameraBoundaries()
    {
        if (m_mainCamera == null) return;
        
        // 뷰포트 좌표를 월드 좌표로 변환 (매 프레임 할 필요 없음, 필요할 때만 호출)
        Vector3 bottomLeft = m_mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 topRight = m_mainCamera.ViewportToWorldPoint(new Vector3(1, 0, 0));
        
        m_cameraMinX = bottomLeft.x;
        m_cameraMaxX = topRight.x;
    }

    private void CheckIfOffScreen()
    {
        if (m_mainCamera == null) return;

        float cameraBottom = m_mainCamera.ViewportToWorldPoint(Vector3.zero).y;
        if (transform.position.y < cameraBottom - 2f)
        {
            Destroy(gameObject);
        }
    }

    private void FlipCharacter(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) < 0.01f) return;
        if (m_enemyAnimation == null) return;

        // 오른쪽(양수) -> 180도 회전, 왼쪽(음수) -> 0도 회전 (SPUM 기준)
        float yRotation = horizontalDirection > 0 ? 180f : 0f;
        m_enemyAnimation.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    private void SetState(EnemyState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;

        if (m_enemyAnimation == null) return;

        PlayerState animState = newState switch
        {
            EnemyState.Idle => PlayerState.IDLE,
            EnemyState.Moving => PlayerState.MOVE,
            EnemyState.Attacking or EnemyState.SkillAttacking => PlayerState.ATTACK,
            EnemyState.Damaged => PlayerState.DAMAGED,
            EnemyState.Dead => PlayerState.DEATH,
            _ => PlayerState.OTHER
        };
        
        m_enemyAnimation.PlayAnimation(animState, 0);
    }
    
    private void PerformArrowLaunch()
    {
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyAttackSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyAttackSfx);
        }

        if (m_isDead || ObjectPoolManager.Instance == null || m_player == null) return;

        Vector3 startPos = m_firePoint.position;
        Vector2 dir = (m_player.position - startPos).normalized;
        Vector3 endPos = startPos + (Vector3)dir * m_fireDistance;

        // 베지어 곡선 제어점 계산
        Vector3 apex = (startPos + endPos) * 0.5f + Vector3.up * m_fireArcHeight;
        Vector3 controlPoint = 2 * apex - (startPos + endPos) * 0.5f;

        GameObject arrowObj = ObjectPoolManager.Instance.Get(m_arrowPrefab);
        if (arrowObj == null) return;

        arrowObj.transform.SetPositionAndRotation(startPos, Quaternion.identity);
        var arrowController = arrowObj.GetComponent<ArrowController>();
        if (arrowController != null)
        {
            arrowController.Owner = ArrowController.ArrowOwner.Enemy;
            arrowController.Launch(startPos, controlPoint, endPos, m_fireDuration);
        }
        else
        {
            ObjectPoolManager.Instance.Return(arrowObj);
        }
    }
    #endregion

    #region Evade Logic
    private async UniTask<bool> EvadeAsync()
    {
        if (m_isDead) return false;

        UpdateCameraBoundaries();
        float currentX = transform.position.x;
        
        // 1. 넓은 공간 방향 판단
        float spaceLeft = currentX - m_cameraMinX;
        float spaceRight = m_cameraMaxX - currentX;
        float dirSign = (spaceRight > spaceLeft) ? 1f : -1f;

        // 2. 목표 지점 계산
        float dashDistance = m_evadeDashSpeed * m_evadeDashDuration;
        float targetX = currentX + (dirSign * dashDistance);

        // 3. 목표 지점이 안전한지 검사 (벽, 절벽)
        // 전체 경로를 스캔하는 대신, 최종 위치와 중간 지점을 샘플링하여 검사
        Vector2 startPos = m_rigidbody2D.position;
        Vector2 targetPos = new Vector2(Mathf.Clamp(targetX, m_cameraMinX + m_enemyWidthHalf, m_cameraMaxX - m_enemyWidthHalf), startPos.y);
        
        float actualDistance = Mathf.Abs(targetPos.x - startPos.x);
        
        if (actualDistance < m_minEvadeDistance) 
        {
            return false; // 이동 거리가 너무 짧음
        }

        // 벽 검사 (BoxCast)
        RaycastHit2D wallHit = Physics2D.BoxCast(startPos, m_collider.bounds.size, 0f, Vector2.right * dirSign, actualDistance, m_groundLayer);
        if (wallHit.collider != null)
        {
            // 벽에 막히면 벽 바로 앞까지만 이동
            actualDistance = wallHit.distance - m_enemyWidthHalf;
            targetPos.x = startPos.x + (dirSign * actualDistance);
            
            if (actualDistance < m_minEvadeDistance) return false;
        }

        // 절벽 검사 (목표 지점 발 밑 확인)
        Vector2 groundCheckPos = targetPos + new Vector2(0, -m_collider.bounds.extents.y - 0.1f);
        RaycastHit2D groundHit = Physics2D.Raycast(groundCheckPos, Vector2.down, 1.0f, m_groundLayer);
        if (groundHit.collider == null)
        {
            return false; // 목표 지점에 땅이 없음 (회피 포기)
        }

        // --- 회피 실행 ---
        CancelAITask(); // AI 일시 중지
        SetState(EnemyState.Evading);
        m_rigidbody2D.linearVelocity = Vector2.zero;
        FlipCharacter(dirSign);

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyEvadeSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyEvadeSfx);
        }
        if (m_afterimageEffect != null) m_afterimageEffect.StartEffect(m_evadeDashDuration);

        // 회피 이동 (CancellationToken 연결)
        var evadeCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(evadeCts.Token, this.GetCancellationTokenOnDestroy());

        try
        {
            float elapsed = 0f;
            while (elapsed < m_evadeDashDuration)
            {
                // FixedUpdate 타이밍에 이동
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, linkedCts.Token);
                elapsed += Time.fixedDeltaTime;

                float newX = Mathf.MoveTowards(m_rigidbody2D.position.x, targetPos.x, m_evadeDashSpeed * Time.fixedDeltaTime);
                m_rigidbody2D.position = new Vector2(newX, startPos.y);
            }
            
            // 회피 후 딜레이
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: linkedCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            linkedCts.Dispose();
            evadeCts.Dispose();

            if (!m_isDead)
            {
                StartAILoop(); // AI 복귀
            }
        }

        return true;
    }
    #endregion

    #region Editor Test
#if UNITY_EDITOR
    public async void TestMultiShotSkill()
    {
        if (!Application.isPlaying) return;
        if (m_player == null || SkillManager.Instance == null) return;

        Debug.Log("--- SKILL TEST: 적 다발 사격 실행 ---");
        FlipCharacter(m_player.position.x - transform.position.x);
        await SkillManager.Instance.ExecuteEnemyMultiShot(m_skillHandPoint, m_player);
    }
#endif
    #endregion
}