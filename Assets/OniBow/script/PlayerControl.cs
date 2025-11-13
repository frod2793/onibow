using System;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Linq;
using System.Threading;
using UnityEngine.Serialization;

/// <summary>
/// 2D 환경에 최적화된 플레이어 이동 및 공격 클래스입니다.
/// Rigidbody2D, DOTween, UniTask를 사용하여 효율적이고 부드러운 움직임을 구현합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerControl : MonoBehaviour
{
    #region 변수 및 속성

    [Header("Health Settings")]
    [Tooltip("최대 체력")]
    [SerializeField] private int m_maxHp = 100;
    [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
    [SerializeField] private float m_tempHpDecreaseDelay = 3f;
    private int m_currentHp;
    private int m_tempHp; // 예비 체력 (UI의 상단 바에 해당)
    private float m_lastDamageTime; // 마지막 피격 시간
    public event Action OnPlayerDied; // 플레이어 사망 시 발생하는 이벤트
    public event Action<int, int, int> OnHealthUpdated; // 체력 변경 시 발생하는 이벤트 (현재, 예비, 최대)

    [Header("Movement Settings")]
    [Tooltip("최대 이동 속도")]
    [SerializeField] private float m_maxSpeed = 5f;
    [Tooltip("가속 시간")]
    [SerializeField] private float m_accelerationTime = 0.2f;
    [Tooltip("감속 시간")]
    [SerializeField] private float m_decelerationTime = 0.1f;
    [Tooltip("가속 시 적용할 Ease 타입")]
    [SerializeField] private Ease m_accelerationEase = Ease.OutCubic;
    [Tooltip("감속 시 적용할 Ease 타입")]
    [SerializeField] private Ease m_decelerationEase = Ease.InCubic;

    [Header("Attack Settings")]
    [Tooltip("발사할 화살 프리팹")]
    [SerializeField] private GameObject m_arrowPrefab;
    [Tooltip("화살이 발사될 위치")]
    [SerializeField] private Transform m_firePoint;
    [Tooltip("화살이 날아가는 고정 거리")]
    [SerializeField] private float m_fireDistance = 7f;
    [Tooltip("포물선 발사의 최고 높이")]
    [SerializeField] private float m_fireArcHeight = 3f;
    [Tooltip("화살이 목표 지점까지 도달하는 시간")]
    [SerializeField] private float m_fireDuration = 1f;
    [Tooltip("정지 후 반복 발사 간격")]
    [SerializeField] private float m_fireInterval = 2f;

    [Header("Collision Settings")]
    [Tooltip("대쉬 시 충돌을 감지할 지면 및 벽 레이어")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Dash Settings")]
    [Tooltip("대쉬 속도")]
    [SerializeField] private float m_dashSpeed = 20f;
    [Tooltip("대쉬 지속 시간 (초)")]
    [SerializeField] private float m_dashDuration = 0.2f;
    [Tooltip("대쉬 쿨다운 (초)")]
    [SerializeField] private float m_dashCooldown = 1f;


    private Rigidbody2D m_rigidbody2D;
    private Collider2D m_collider;
    private Tween m_movementTween;
    private CancellationTokenSource m_moveCts;
    private CancellationTokenSource m_fireCts;
    private CancellationTokenSource m_dashCts;
    private float m_lastFireTime = -999f; // 마지막 발사 시간을 기록
    private float m_lastDashTime = -999f; // 마지막 대쉬 시간을 기록
    private SPUM_Prefabs m_spumPrefabs; // 애니메이션 제어를 위한 참조
    private AfterimageEffect m_afterimageEffect; // 잔상 효과 참조
    private PlayerState m_currentState = PlayerState.IDLE; // 현재 플레이어 상태
    private bool m_isDashing = false; // 대쉬 중인지 확인하는 플래그
    private bool m_isInvulnerable = false; // 무적 상태인지 확인하는 플래그
    private bool m_isMovementAllowedWhileSkill = false; // 스킬 사용 중 이동이 허용되는지 확인하는 플래그
    
    // 카메라 경계
    private float m_cameraMinX;
    private float m_cameraMaxX;

    // 상수
    private const string k_EnemyTag = "Enemy";
    private const string k_EnemyArrowTag = "EnemyArrow";

    #endregion

    #region Unity 메시지

    private void Awake()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        m_rigidbody2D = GetComponent<Rigidbody2D>();
        m_collider = GetComponent<Collider2D>();
        m_spumPrefabs = GetComponentInChildren<SPUM_Prefabs>();
        m_afterimageEffect = GetComponent<AfterimageEffect>();
        if (m_afterimageEffect == null)
        {
            Debug.LogWarning("Player에 AfterimageEffect 컴포넌트가 없습니다. 대쉬 잔상 효과가 동작하지 않습니다.");
        }

        m_currentHp = m_maxHp;
        m_tempHp = m_maxHp;

        if (m_spumPrefabs != null)
        {
            if(!m_spumPrefabs.allListsHaveItemsExist()){
                m_spumPrefabs.PopulateAnimationLists();
            }
            m_spumPrefabs.OverrideControllerInit();
            if (m_spumPrefabs._anim == null)
            {
                Debug.LogError("SPUM_Prefabs의 Animator 참조가 null입니다!");
            }
        }
        else
        {
            Debug.LogError("자식 오브젝트에서 SPUM_Prefabs 컴포넌트를 찾을 수 없습니다. 애니메이션이 작동하지 않습니다.");
        }

        m_rigidbody2D.gravityScale = 1;
        m_rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void OnEnable()
    {
        Enemy.OnEnemyDestroyed += HandleEnemyDestroyed;
    }

    private void OnDisable()
    {
        Enemy.OnEnemyDestroyed -= HandleEnemyDestroyed;
    }

    private void Start()
    {
        CalculateCameraBoundaries();
        ForceUpdateHpUI();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (m_currentState != PlayerState.DEATH && other.CompareTag(k_EnemyArrowTag))
        {
            TakeDamage(10);
        }
    }

    private void OnDestroy()
    {
        m_moveCts?.Cancel();
        m_moveCts?.Dispose();
        m_fireCts?.Cancel();
        m_fireCts?.Dispose();
        m_dashCts?.Cancel();
        m_dashCts?.Dispose();
        m_movementTween?.Kill();
    }

    #endregion

    #region 공개 메서드

    public void ForceUpdateHpUI()
    {
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
    }

    public void TakeDamage(int damage)
    {
        // [우선권 1순위: 피격] - 다른 모든 행동을 중단시킴
        // 무적 상태일 경우 데미지를 받지 않습니다.
        if (m_isInvulnerable) return;

        // 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDamagedSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDamagedSfx);
        }

        if (m_currentState == PlayerState.DEATH) return; 
        
        CancelAllActions();

        // 데미지를 받기 시작한 시점의 체력을 예비 체력으로 기록합니다.
        // 예비 체력 감소 딜레이 시간 내에 추가 타격을 받으면, 예비 체력은 갱신되지 않고 현재 체력만 감소합니다.
        if (Time.time > m_lastDamageTime + m_tempHpDecreaseDelay)
        {
            m_tempHp = m_currentHp;
        }

        m_currentHp -= damage;
        m_currentHp = Mathf.Max(0, m_currentHp);
        m_lastDamageTime = Time.time;
        Debug.Log($"플레이어가 {damage}의 데미지를 입었습니다. 현재 체력: {m_currentHp}");
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);

        SetState(PlayerState.DAMAGED);

        // 데미지 텍스트 표시 (캐릭터 머리 위)
        EffectManager.Instance.ShowDamageText(gameObject, damage);

        PlayDamagedAnimationAsync().Forget();

        if (m_currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 예비 체력만큼 현재 체력을 회복합니다. (회복 스킬용)
    /// </summary>
    public void HealWithTempHp()
    {
        if (m_currentState == PlayerState.DEATH) return;
        int recoveryAmount = m_tempHp - m_currentHp;
        if (recoveryAmount > 0)
        {
            m_currentHp += recoveryAmount;
            m_tempHp = m_currentHp; // 회복 후에는 예비 체력과 현재 체력을 일치시킴
            OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);

            // 사운드 재생
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerHealSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerHealSfx);
            }
        }
    }

    /// <summary>
    /// 플레이어의 무적 상태를 설정합니다. (배리어 스킬용)
    /// </summary>
    /// <param name="state">무적 상태 여부</param>
    public void SetInvulnerable(bool state)
    {
        m_isInvulnerable = state;
    }

    /// <summary>
    /// 플레이어의 최대 체력을 반환합니다.
    /// </summary>
    public int GetMaxHp() => m_maxHp;

    /// <summary>
    /// 지정된 시간 동안 지정된 양만큼 체력을 서서히 회복합니다.
    /// </summary>
    public async UniTask GradualHeal(float totalHealAmount, float duration, CancellationToken token)
    {
        float healPerSecond = totalHealAmount / duration;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            if (token.IsCancellationRequested) return;

            float healThisFrame = healPerSecond * Time.deltaTime;
            m_currentHp = Mathf.Min(m_currentHp + (int)Mathf.Ceil(healThisFrame), m_maxHp);
            m_tempHp = m_currentHp; // 점진적 회복 중에는 예비 체력도 함께 회복됩니다.
            OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
            
            elapsedTime += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// 스킬 사용 상태를 설정하고, 공격 로직을 제어합니다.
    /// SkillManager에서 호출합니다.
    /// </summary>
    /// <param name="isUsing">스킬을 사용 중이면 true, 아니면 false.</param>
    /// <param name="stopMovement">스킬 사용 시 이동을 강제로 멈출지 여부.</param>
    public void SetSkillUsageState(bool isUsing, bool stopMovement = true)
    {
        m_isMovementAllowedWhileSkill = isUsing && !stopMovement;
        if (isUsing)
        {
            // 이동 정지가 필요한 스킬일 경우에만 모든 행동을 중단합니다.
            if (stopMovement)
            {
                CancelAllActions();
            }
            else // 이동을 허용하는 스킬의 경우, 공격만 중단합니다.
            {
                m_fireCts?.Cancel();
            }
            SetState(PlayerState.OTHER);
        }
        else
        {
            // 스킬 사용이 끝나면 IDLE 상태로 돌아가 자동 공격을 다시 시작합니다.
            if (m_currentState == PlayerState.OTHER)
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire();
            }
        }
    }

    public void StartMoving(float direction)
    {
        // 이동, 공격, 대쉬 등 새로운 행동을 시작할 수 있는 상태인지 확인합니다.
        if (!IsActionableState()) return;

        m_fireCts?.Cancel(); // 공격 루프 중단

        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        m_moveCts?.Cancel();
        // 이전 CancellationTokenSource를 Dispose하여 리소스 누수를 방지합니다.
        m_moveCts?.Dispose();
        m_moveCts = new CancellationTokenSource();
        MoveLoopAsync(direction, m_moveCts.Token).Forget();
    }

    public void StopMoving()
    {
        // 이동 중에만 정지 로직을 실행합니다.
        if (m_currentState != PlayerState.MOVE) return;
        m_moveCts?.Cancel();
        m_movementTween?.Kill();

        m_movementTween = m_rigidbody2D.DOVector(new Vector2(0, m_rigidbody2D.linearVelocity.y), m_decelerationTime)
            .SetEase(m_decelerationEase)
            .SetUpdate(UpdateType.Fixed)
            .OnComplete(() =>
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire();
            });
    }

    public void FireStraightArrow()
    {
        if (!IsActionableState() || ObjectPoolManager.Instance == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            PlayAttackAnimationAsync().Forget();

            Vector3 startPos = m_firePoint != null ? m_firePoint.position : transform.position;
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;

            GameObject arrowObject = ObjectPoolManager.Instance.Get(m_arrowPrefab);
            if (arrowObject == null) return;

            var arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Owner = ArrowController.ArrowOwner.Player;
                arrowController.LaunchStraight(startPos, direction, 100f, 1.5f);
            }
            else
            {
                ObjectPoolManager.Instance.Return(arrowObject);
            }
        }
    }

    /// <summary>
    /// 씬에 있는 모든 적 중에서 가장 가까운 살아있는 적을 찾습니다.
    /// [성능 경고] 이 메서드는 매 프레임 호출될 경우 성능 저하를 유발할 수 있습니다.
    /// EnemyManager를 구현하여 적 목록을 관리하는 것을 강력히 권장합니다.
    /// </summary>
    /// <returns>가장 가까운 적의 GameObject. 없으면 null을 반환합니다.</returns>
    public GameObject FindNearestEnemy()
    {
        // TODO: 성능 최적화를 위해 EnemyManager를 구현하여 사용하세요.
        // 예: return EnemyManager.Instance.GetNearestEnemy(transform.position);
        return GameObject.FindGameObjectsWithTag(k_EnemyTag)
            .Where(e => e.GetComponent<Enemy>()?.IsDead == false)
            .OrderBy(e => Vector2.Distance(transform.position, e.transform.position))
            .FirstOrDefault();
    }

    /// <summary>
    /// 지정된 방향으로 대쉬합니다.
    /// </summary>
    /// <param name="direction">대쉬 방향 (-1 for left, 1 for right)</param>
    public void Dash(float direction)
    {
        if (!IsActionableState() || Time.time < m_lastDashTime + m_dashCooldown) return;
        
        Vector3 currentPosition = transform.position;
        Bounds playerBounds = m_collider.bounds; // m_collider.bounds는 이미 캐싱되어 있으므로 그대로 사용
        // 1. 대쉬는 지상에 있을 때만 가능하도록, 먼저 발밑의 땅을 확인합니다.
        RaycastHit2D groundCheck = Physics2D.Raycast(
            (Vector2)currentPosition + m_collider.offset,
            Vector2.down,
            playerBounds.extents.y + 0.5f,
            groundLayer
        );
        if (groundCheck.collider == null)
        {
            return; // 공중에서는 대쉬를 실행하지 않습니다.
        }

        m_lastDashTime = Time.time;
        float finalDashDistance = CalculateSafeDashDistance(direction, currentPosition);

        // 최종 목표 지점을 카메라 경계 내로 제한합니다.
        float finalTargetX = Mathf.Clamp(m_rigidbody2D.position.x + direction * finalDashDistance, m_cameraMinX, m_cameraMaxX);

        // 4. 실제 이동 거리에 따른 대쉬 시간을 계산하고 실행합니다.
        float actualDashDistance = Mathf.Abs(finalTargetX - m_rigidbody2D.position.x);
        float actualDuration = actualDashDistance / m_dashSpeed;
        if (actualDuration < Time.fixedDeltaTime) return; // 이동 거리가 매우 짧으면 실행하지 않음

        DashAsync(finalTargetX, actualDuration).Forget();
    }
    #endregion

    #region 내부 로직

    /// <summary>
    /// 카메라의 월드 좌표 경계를 계산하고, 캐릭터의 너비를 고려하여 보정합니다.
    /// </summary>
    private void CalculateCameraBoundaries()
    {
        if (GameManager.Instance != null && GameManager.Instance.MainCamera != null)
        {
            Camera cam = GameManager.Instance.MainCamera;
            m_cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
            m_cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;

            // 캐릭터가 화면 밖으로 완전히 나가지 않도록 너비만큼 보정합니다.
            Bounds playerBounds = m_collider.bounds;
            float playerWidth = playerBounds.extents.x;
            m_cameraMinX += playerWidth;
            m_cameraMaxX -= playerWidth;
        }
        else
        {
            Debug.LogError("카메라 경계를 계산할 수 없습니다. GameManager 또는 MainCamera를 찾을 수 없습니다.");
            m_cameraMinX = -Mathf.Infinity;
            m_cameraMaxX = Mathf.Infinity;
        }
    }

    private float CalculateSafeDashDistance(float direction, Vector3 currentTransformPosition)
    {
        Bounds playerBounds = m_collider.bounds;
        float currentX = m_rigidbody2D.position.x;
        float maxDashDistance = m_dashSpeed * m_dashDuration;

        float wallLimitedDistance = maxDashDistance;
        RaycastHit2D wallHit = Physics2D.BoxCast(
            (Vector2)currentTransformPosition + m_collider.offset,
            new Vector2(playerBounds.size.x, playerBounds.size.y * 0.9f),
            0f,
            new Vector2(direction, 0),
            maxDashDistance,
            groundLayer
        );
        if (wallHit.collider != null)
        {
            wallLimitedDistance = wallHit.distance;
        }

        // 2. 절벽을 감지하여 실제 이동 가능한 거리를 찾습니다.
        float finalDashDistance = wallLimitedDistance; // 절벽이 없다면 벽까지의 거리가 최종 거리
        int steps = 15;
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, m_rigidbody2D.position.y);

            // 예상 위치 아래에 땅이 있는지 확인합니다.
            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(playerBounds.size.x * 0.9f, 0.1f),
                0f, Vector2.down, playerBounds.extents.y + 0.5f, groundLayer
            );

            if (groundUnderneath.collider == null)
            {
                // 땅이 없으면, 이전 단계까지가 안전한 거리입니다.
                finalDashDistance = (i - 1) * stepDistance;
                break;
            }
        }

        // 3. 벽이나 절벽에 너무 가깝게 붙지 않도록 캐릭터 너비 기반의 여유를 줍니다.
        finalDashDistance = Mathf.Max(0, finalDashDistance - playerBounds.extents.x);

        return finalDashDistance;
    }
    
    private void HandleEnemyDestroyed(Enemy enemy)
    {
        // 적이 파괴된 후, 다른 적이 있는지 확인
        if (FindNearestEnemy() == null)
        {
            // BGM을 정지할 필요가 있다면 여기에 로직 추가
            // 예: if (SoundManager.Instance != null)
            //     {
            //         SoundManager.Instance.StopBGM();
            //     }

            // 남은 적이 없으면 공격 루프 중단
            m_fireCts?.Cancel();
        }
    }

    private void Die()
    {
        SetState(PlayerState.DEATH); // 상태를 사망으로 변경

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDeathSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDeathSfx);
        }

        CancelAllActions();

        // 체력을 0으로 설정하고 UI를 업데이트합니다.
        m_currentHp = 0;
        m_tempHp = 0;
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        OnPlayerDied?.Invoke();

        Debug.Log("플레이어가 사망했습니다.");
    }

    private void CancelAllActions()
    {
        m_moveCts?.Cancel();
        m_fireCts?.Cancel();
        m_dashCts?.Cancel();
        m_movementTween?.Kill();
        m_rigidbody2D.linearVelocity = Vector2.zero;
    }

    private void StartRepeatingFire()
    {
        // [우선권 3순위: 공격] - 다른 액션 중이 아닐 때만 시작
        if (!IsActionableState()) return;

        m_fireCts?.Cancel();
        m_fireCts?.Dispose();
        m_fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(m_fireCts.Token).Forget();
    }

    public void FireAtNearestEnemy()
    {
        if (!IsActionableState() || ObjectPoolManager.Instance == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            // 사운드 재생
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerFireSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerFireSfx);
            }

            if (m_spumPrefabs != null)
            {
                float directionToEnemyX = nearestEnemy.transform.position.x - transform.position.x;
                m_spumPrefabs.transform.rotation = Quaternion.Euler(0f, directionToEnemyX > 0 ? 180f : 0f, 0f);
            }

            PlayAttackAnimationAsync().Forget();

            Vector3 currentTransformPosition = transform.position;
            Vector3 startPos = m_firePoint != null ? m_firePoint.position : currentTransformPosition;
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;
            Vector3 endPos = startPos + (Vector3)direction * m_fireDistance;

            Vector3 apex = (startPos + endPos) / 2f + Vector3.up * m_fireArcHeight;
            Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

            GameObject arrowObject = ObjectPoolManager.Instance.Get(m_arrowPrefab);
            if (arrowObject == null) return;

            var arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Owner = ArrowController.ArrowOwner.Player;
                arrowController.Launch(startPos, controlPoint, endPos, m_fireDuration);
            }
            else
            {
                ObjectPoolManager.Instance.Return(arrowObject);
            }
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = m_rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, m_cameraMinX, m_cameraMaxX);
        m_rigidbody2D.position = clampedPosition;
    }

    /// <summary>
    /// 이동, 공격, 대쉬 등 새로운 행동을 시작할 수 있는 상태인지 확인합니다.
    /// </summary>
    /// <returns>행동 가능 여부</returns>
    private bool IsActionableState()
    {
        // 스킬 사용 중 이동이 허용된 상태라면, 다른 제약(사망, 피격 등)이 없는 한 행동 가능으로 판단합니다.
        if (m_isMovementAllowedWhileSkill) return m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED && !m_isDashing;

        // 사망, 피격, 스킬 사용, 대쉬 중에는 새로운 행동을 시작할 수 없습니다.
        return m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED &&
               m_currentState != PlayerState.OTHER &&
               !m_isDashing;
    }

    /// <summary>
    /// 플레이어의 상태를 변경하고, 상태에 맞는 애니메이션을 재생합니다.
    /// </summary>
    /// <param name="newState">변경할 새로운 상태</param>
    private void SetState(PlayerState newState)
    {
        if (m_currentState == newState) return;
        m_currentState = newState;
        PlayAnimationForState(newState);
    }

    /// <summary>
    /// 지정된 상태에 해당하는 애니메이션을 재생합니다.
    /// </summary>
    /// <param name="stateToPlay">재생할 애니메이션의 상태</param>
    private void PlayAnimationForState(PlayerState stateToPlay)
    {
        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.PlayAnimation(stateToPlay, 0);
        }
    }
    #endregion

    #region 코루틴 및 비동기 메서드
    private async UniTaskVoid PlayAttackAnimationAsync()
    {
        // 공격 애니메이션은 IDLE 또는 MOVE 상태에서만 시작할 수 있습니다.
        if (m_currentState != PlayerState.IDLE && m_currentState != PlayerState.MOVE) return;

        SetState(PlayerState.ATTACK);

        var attackClips = m_spumPrefabs.StateAnimationPairs[PlayerState.ATTACK.ToString()];
        if (attackClips != null && attackClips.Count > 0 && attackClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(attackClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException) { return; } // 애니메이션 중 취소되면(피격, 이동 등) 즉시 복귀
        }

        // 다른 상태로 변경되지 않았다면 IDLE로 복귀
        if (m_currentState == PlayerState.ATTACK)
        {
            SetState(PlayerState.IDLE);
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        // SetState는 이미 TakeDamage에서 호출되었으므로, 여기서는 애니메이션 재생을 기다리기만 합니다.
        if (m_currentState != PlayerState.DAMAGED) return;

        var damagedClips = m_spumPrefabs.StateAnimationPairs[PlayerState.DAMAGED.ToString()];
        if (damagedClips != null && damagedClips.Count > 0 && damagedClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException) { return; } // 애니메이션 중 다른 피격 등으로 취소되면 즉시 복귀
        }

        // 피격 애니메이션이 끝난 후, 사망 상태가 아니라면 다시 자동 공격을 시작합니다.
        if (m_currentState != PlayerState.DEATH)
        {
            // TakeDamage에서 다른 행동이 취소되었으므로, IDLE 상태로 전환하고 공격을 다시 시작합니다.
            SetState(PlayerState.IDLE);
            StartRepeatingFire();
        }
    }

    private async UniTaskVoid RepeatingFireLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                float timeUntilReady = (m_lastFireTime + m_fireInterval) - Time.time;
                if (timeUntilReady > 0)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(timeUntilReady), cancellationToken: token);
                }
                if (token.IsCancellationRequested) break;

                // 공격을 실행하기 직전에 적이 여전히 유효한지 다시 확인합니다.
                // 이렇게 하면 대기 시간 동안 적이 사망하는 경우를 처리할 수 있습니다.
                if (FindNearestEnemy() == null)
                {
                    // 공격할 대상이 없으므로 공격 루프를 중단합니다.
                    break;
                }

                FireAtNearestEnemy();
                m_lastFireTime = Time.time;
            }
        }
        catch (OperationCanceledException){}
    }

    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        SetState(PlayerState.MOVE);

        float targetVelocityX = direction * m_maxSpeed;
        var tcs = new UniTaskCompletionSource();
        // 트윈이 외부에서 취소될 경우 Task도 함께 취소되도록 등록합니다.
        token.Register(() => tcs.TrySetCanceled());

        m_movementTween?.Kill();
        m_movementTween = m_rigidbody2D.DOVector(new Vector2(targetVelocityX, m_rigidbody2D.linearVelocity.y), m_accelerationTime)
            .SetEase(m_accelerationEase)
            .SetUpdate(UpdateType.Fixed)
            // 트윈이 정상적으로 완료되면 Task를 성공 상태로 만듭니다.
            .OnComplete(() => tcs.TrySetResult());

        try
        {
            // UniTaskCompletionSource의 Task를 기다립니다.
            // 이렇게 하면 DOTween 확장 기능 없이도 트윈의 완료를 비동기적으로 기다릴 수 있습니다.
            await tcs.Task;
            
            while (!token.IsCancellationRequested)
            {
                // --- 예측 기반 이동 제한 로직 ---
                float finalVelocityX = targetVelocityX;

                // 1. 카메라 경계 예측: 이동하려는 방향으로 카메라 경계를 넘어서는지 확인합니다.
                if ((m_rigidbody2D.position.x <= m_cameraMinX && finalVelocityX < 0) ||
                    (m_rigidbody2D.position.x >= m_cameraMaxX && finalVelocityX > 0))
                {
                    finalVelocityX = 0; // 카메라 경계에 도달하면 멈춥니다.
                }

                // 2. 절벽 예측: 이동하려는 방향의 발밑에 땅이 있는지 확인합니다.
                if (Mathf.Abs(finalVelocityX) > 0.01f) // 실제로 움직일 때만 확인
                {
                    Bounds playerBounds = m_collider.bounds;
                    float moveSign = Mathf.Sign(finalVelocityX);
                    Vector2 groundCheckOrigin = (Vector2)playerBounds.center + new Vector2(moveSign * playerBounds.extents.x, -playerBounds.extents.y - 0.05f); // 발밑 확인 지점
                    RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, groundLayer);

                    if (groundHit.collider == null)
                    {
                        finalVelocityX = 0; // 앞에 땅이 없으면 멈춥니다.
                    }
                }

                // 3. 최종 속도 적용 및 시각적 상태(애니메이션) 업데이트
                m_rigidbody2D.linearVelocity = new Vector2(finalVelocityX, m_rigidbody2D.linearVelocity.y);

                // 이동 가능 여부에 따라 애니메이션을 즉시 변경합니다.
                // _currentState는 'MOVE'로 유지하여, 장애물이 사라졌을 때 다시 움직일 수 있도록 합니다.
                PlayAnimationForState(Mathf.Abs(finalVelocityX) > 0.01f ? PlayerState.MOVE : PlayerState.IDLE);

                ClampPosition();
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }
        }
        catch (OperationCanceledException){}
    }

    private async UniTaskVoid DashAsync(float targetX, float duration)
    {
        m_isDashing = true;
        SetState(PlayerState.MOVE); // 대쉬 중에는 이동 애니메이션 재생

        // 다른 행동 중단
        m_moveCts?.Cancel();
        m_fireCts?.Cancel();
        m_movementTween?.Kill();

        float startY = m_rigidbody2D.position.y;
        // 대쉬 방향으로 캐릭터 회전
        float direction = Mathf.Sign(targetX - m_rigidbody2D.position.x);
        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        // 잔상 효과 시작
        if (m_afterimageEffect != null)
            m_afterimageEffect.StartEffect(duration);

        // 사운드 재생
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDashSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDashSfx);
        }

        // 대쉬 움직임
        float originalGravity = m_rigidbody2D.gravityScale;
        m_rigidbody2D.gravityScale = 0; // 대쉬 중에는 중력 무시
        m_rigidbody2D.linearVelocity = Vector2.zero; // MoveTowards를 사용하므로 초기 속도는 0으로 설정

        // 대쉬 취소를 위한 토큰 설정
        m_dashCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(m_dashCts.Token, this.GetCancellationTokenOnDestroy());

        try
        {
            float elapsedTime = 0f;
            while (elapsedTime < duration)
            {
                // 다음 FixedUpdate까지 대기
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, linkedCts.Token);
                elapsedTime += Time.fixedDeltaTime;

                // [개선] 속도 대신 MoveTowards를 사용하여 위치를 직접 제어합니다.
                // 이렇게 하면 물리 엔진의 오차로 인한 오버슈팅을 방지하여 경계를 벗어나는 문제를 근본적으로 해결합니다.
                float newX = Mathf.MoveTowards(m_rigidbody2D.position.x, targetX, m_dashSpeed * Time.fixedDeltaTime);
                m_rigidbody2D.position = new Vector2(newX, startY);

                // 목표 지점에 도달했는지 확인
                if (Mathf.Approximately(m_rigidbody2D.position.x, targetX))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TakeDamage 등에 의해 대쉬가 취소됨
        }
        finally
        {
            m_rigidbody2D.gravityScale = originalGravity;
            m_rigidbody2D.linearVelocity = Vector2.zero;
            // 대쉬가 끝난 후 정확한 위치에 있도록 보정
            if (!this.GetCancellationTokenOnDestroy().IsCancellationRequested)
            {
                m_rigidbody2D.position = new Vector2(targetX, startY);
            }
            m_isDashing = false;
            
            if (m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED)
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire(); // 대쉬 후 다시 자동 공격 상태로 전환
            }
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