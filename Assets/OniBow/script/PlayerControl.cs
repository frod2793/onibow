using System;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Linq;
using System.Threading;
using UnityEngine.InputSystem;
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
    private int m_tempHp;
    private float m_lastDamageTime;
    public event Action OnPlayerDied;
    public event Action<int, int, int> OnHealthUpdated;

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
    private float m_lastFireTime = -999f;
    private float m_lastDashTime = -999f;
    private SPUM_Prefabs m_spumPrefabs;
    private AfterimageEffect m_afterimageEffect;
    private PlayerState m_currentState = PlayerState.IDLE;
    private bool m_isDashing = false;
    private bool m_isInvulnerable = false;
    private bool m_isMovementAllowedWhileSkill = false;
    
    private float m_leftInputTime = -1f;
    private float m_rightInputTime = -1f;
    private const float k_DoubleClickTime = 0.3f;

    private float m_cameraMinX;
    private float m_cameraMaxX;

    private const string k_EnemyTag = "Enemy";
    private const string k_EnemyArrowTag = "EnemyArrow";

    #endregion

    #region Unity 메시지

    private void Awake()
    {
        InitializeComponents();
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
    
    private void Update()
    {
        HandleKeyboardInput();
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

    /// <summary>
    /// 현재 체력 정보를 기반으로 UI를 강제로 업데이트합니다.
    /// </summary>
    public void ForceUpdateHpUI()
    {
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
    }

    /// <summary>
    /// 플레이어에게 데미지를 적용하고 관련 효과를 처리합니다.
    /// </summary>
    /// <param name="damage">적용할 데미지 양</param>
    public void TakeDamage(int damage)
    {
        if (m_isInvulnerable || m_currentState == PlayerState.DEATH) return;

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDamagedSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDamagedSfx);
        }
        
        CancelAllActions();

        if (Time.time > m_lastDamageTime + m_tempHpDecreaseDelay)
        {
            m_tempHp = m_currentHp;
        }

        m_currentHp = Mathf.Max(0, m_currentHp - damage);
        m_lastDamageTime = Time.time;
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);

        SetState(PlayerState.DAMAGED);
        EffectManager.Instance.ShowDamageText(gameObject, damage);
        PlayDamagedAnimationAsync().Forget();

        if (m_currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 예비 체력만큼 현재 체력을 즉시 회복합니다.
    /// </summary>
    public void HealWithTempHp()
    {
        if (m_currentState == PlayerState.DEATH) return;
        int recoveryAmount = m_tempHp - m_currentHp;
        if (recoveryAmount > 0)
        {
            m_currentHp += recoveryAmount;
            m_tempHp = m_currentHp;
            OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);

            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerHealSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerHealSfx);
            }
        }
    }

    /// <summary>
    /// 플레이어의 무적 상태를 설정합니다.
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
            m_tempHp = m_currentHp;
            OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
            
            elapsedTime += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// 스킬 사용 상태를 설정하고, 필요에 따라 플레이어의 행동을 제어합니다.
    /// </summary>
    /// <param name="isUsing">스킬 사용 중 여부</param>
    /// <param name="stopMovement">스킬 사용 시 이동을 멈출지 여부</param>
    public void SetSkillUsageState(bool isUsing, bool stopMovement = true)
    {
        m_isMovementAllowedWhileSkill = isUsing && !stopMovement;
        if (isUsing)
        {
            if (stopMovement)
            {
                CancelAllActions();
            }
            else
            {
                m_fireCts?.Cancel();
            }
            SetState(PlayerState.OTHER);
        }
        else
        {
            if (m_currentState == PlayerState.OTHER)
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire();
            }
        }
    }

    /// <summary>
    /// UI 이동 버튼을 눌렀을 때 호출됩니다. 더블 클릭 시 대쉬를 실행합니다.
    /// </summary>
    /// <param name="direction">이동 방향 (-1 또는 1)</param>
    public void OnMoveButtonDown(float direction)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.GenericButtonClickSfx);
        }

        if (direction > 0)
        {
            if (Time.time - m_rightInputTime < k_DoubleClickTime)
            {
                Dash(direction);
                m_rightInputTime = -1f;
            }
            else
            {
                StartMoving(direction);
                m_rightInputTime = Time.time;
            }
        }
        else
        {
            if (Time.time - m_leftInputTime < k_DoubleClickTime)
            {
                Dash(direction);
                m_leftInputTime = -1f;
            }
            else
            {
                StartMoving(direction);
                m_leftInputTime = Time.time;
            }
        }
    }

    /// <summary>
    /// UI 이동 버튼에서 손을 뗐을 때 호출됩니다.
    /// </summary>
    public void OnMoveButtonUp()
    {
        StopMoving();
    }

    /// <summary>
    /// 지정된 방향으로 이동을 시작합니다.
    /// </summary>
    /// <param name="direction">이동 방향 (-1 또는 1)</param>
    public void StartMoving(float direction)
    {
        if (!IsActionableState()) return;

        m_fireCts?.Cancel();

        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        m_moveCts?.Cancel();
        m_moveCts?.Dispose();
        m_moveCts = new CancellationTokenSource();
        MoveLoopAsync(direction, m_moveCts.Token).Forget();
    }

    /// <summary>
    /// 현재 이동을 멈추고 정지 상태로 전환합니다.
    /// </summary>
    public void StopMoving()
    {
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

    /// <summary>
    /// 가장 가까운 적을 향해 직선으로 날아가는 화살을 발사합니다.
    /// </summary>
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
    /// </summary>
    public GameObject FindNearestEnemy()
    {
        // TODO: 성능 최적화를 위해 EnemyManager를 구현하여 사용하세요.
        return GameObject.FindGameObjectsWithTag(k_EnemyTag)
            .Where(e => e.GetComponent<Enemy>()?.IsDead == false)
            .OrderBy(e => Vector2.Distance(transform.position, e.transform.position))
            .FirstOrDefault();
    }

    /// <summary>
    /// 지정된 방향으로 대쉬를 실행합니다.
    /// </summary>
    /// <param name="direction">대쉬 방향 (-1 또는 1)</param>
    public void Dash(float direction)
    {
        if (!IsActionableState() || Time.time < m_lastDashTime + m_dashCooldown) return;
        
        RaycastHit2D groundCheck = Physics2D.Raycast(
            (Vector2)transform.position + m_collider.offset,
            Vector2.down,
            m_collider.bounds.extents.y + 0.5f,
            groundLayer
        );
        if (groundCheck.collider == null) return;

        m_lastDashTime = Time.time;
        float finalDashDistance = CalculateSafeDashDistance(direction, transform.position);

        float finalTargetX = Mathf.Clamp(m_rigidbody2D.position.x + direction * finalDashDistance, m_cameraMinX, m_cameraMaxX);

        float actualDashDistance = Mathf.Abs(finalTargetX - m_rigidbody2D.position.x);
        float actualDuration = actualDashDistance / m_dashSpeed;
        if (actualDuration < Time.fixedDeltaTime) return;

        DashAsync(finalTargetX, actualDuration).Forget();
    }
    #endregion

    #region 내부 로직

    /// <summary>
    /// 플레이어의 행동에 필요한 컴포넌트들을 초기화합니다.
    /// </summary>
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

    /// <summary>
    /// 키보드 입력을 처리하여 이동 및 대쉬를 제어합니다.
    /// </summary>
    private void HandleKeyboardInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
        {
            OnMoveButtonDown(1);
        }

        if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
        {
            OnMoveButtonDown(-1);
        }

        bool anyKeyReleased = keyboard.dKey.wasReleasedThisFrame || keyboard.rightArrowKey.wasReleasedThisFrame ||
                              keyboard.aKey.wasReleasedThisFrame || keyboard.leftArrowKey.wasReleasedThisFrame;
        bool anyKeyPressed = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ||
                             keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;

        if (anyKeyReleased && !anyKeyPressed)
        {
            OnMoveButtonUp();
        }
    }
    
    /// <summary>
    /// 카메라의 월드 좌표 경계를 계산하고, 캐릭터 너비를 고려하여 보정합니다.
    /// </summary>
    private void CalculateCameraBoundaries()
    {
        if (GameManager.Instance != null && GameManager.Instance.MainCamera != null)
        {
            Camera cam = GameManager.Instance.MainCamera;
            m_cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
            m_cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;

            float playerWidth = m_collider.bounds.extents.x;
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

    /// <summary>
    /// 벽과 절벽을 감지하여 안전하게 대쉬할 수 있는 최대 거리를 계산합니다.
    /// </summary>
    /// <returns>안전한 대쉬 거리</returns>
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

        float finalDashDistance = wallLimitedDistance;
        int steps = 15;
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, m_rigidbody2D.position.y);

            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(playerBounds.size.x * 0.9f, 0.1f),
                0f, Vector2.down, playerBounds.extents.y + 0.5f, groundLayer
            );

            if (groundUnderneath.collider == null)
            {
                finalDashDistance = (i - 1) * stepDistance;
                break;
            }
        }

        finalDashDistance = Mathf.Max(0, finalDashDistance - playerBounds.extents.x);

        return finalDashDistance;
    }
    
    /// <summary>
    /// 적 사망 이벤트를 처리하여, 남은 적이 없으면 공격 루프를 중단합니다.
    /// </summary>
    private void HandleEnemyDestroyed(Enemy enemy)
    {
        if (FindNearestEnemy() == null)
        {
            m_fireCts?.Cancel();
        }
    }

    /// <summary>
    /// 플레이어의 사망 처리를 담당합니다.
    /// </summary>
    private void Die()
    {
        SetState(PlayerState.DEATH);

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDeathSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDeathSfx);
        }

        CancelAllActions();

        m_currentHp = 0;
        m_tempHp = 0;
        OnHealthUpdated?.Invoke(m_currentHp, m_tempHp, m_maxHp);
        OnPlayerDied?.Invoke();
    }

    /// <summary>
    /// 플레이어의 모든 현재 행동(이동, 공격, 대쉬)을 즉시 취소합니다.
    /// </summary>
    private void CancelAllActions()
    {
        m_moveCts?.Cancel();
        m_fireCts?.Cancel();
        m_dashCts?.Cancel();
        m_movementTween?.Kill();
        m_rigidbody2D.linearVelocity = Vector2.zero;
    }

    /// <summary>
    /// 가장 가까운 적을 향한 자동 공격 루프를 시작합니다.
    /// </summary>
    private void StartRepeatingFire()
    {
        if (!IsActionableState()) return;

        m_fireCts?.Cancel();
        m_fireCts?.Dispose();
        m_fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(m_fireCts.Token).Forget();
    }

    /// <summary>
    /// 가장 가까운 적을 향해 포물선 궤적의 화살을 발사합니다.
    /// </summary>
    public void FireAtNearestEnemy()
    {
        if (!IsActionableState() || ObjectPoolManager.Instance == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
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

            Vector3 startPos = m_firePoint != null ? m_firePoint.position : transform.position;
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

    /// <summary>
    /// 플레이어의 위치를 카메라 경계 내로 제한합니다.
    /// </summary>
    private void ClampPosition()
    {
        Vector2 clampedPosition = m_rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, m_cameraMinX, m_cameraMaxX);
        m_rigidbody2D.position = clampedPosition;
    }

    /// <summary>
    /// 플레이어가 새로운 행동(이동, 공격 등)을 시작할 수 있는 상태인지 확인합니다.
    /// </summary>
    /// <returns>행동 가능 여부</returns>
    private bool IsActionableState()
    {
        if (m_isMovementAllowedWhileSkill) return m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED && !m_isDashing;

        return m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED &&
               m_currentState != PlayerState.OTHER &&
               !m_isDashing;
    }

    /// <summary>
    /// 플레이어의 상태를 변경하고, 상태에 맞는 애니메이션을 재생합니다.
    /// </summary>
    private void SetState(PlayerState newState)
    {
        if (m_currentState == newState) return;
        m_currentState = newState;
        PlayAnimationForState(newState);
    }

    /// <summary>
    /// 지정된 상태에 해당하는 애니메이션을 재생합니다.
    /// </summary>
    private void PlayAnimationForState(PlayerState stateToPlay)
    {
        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.PlayAnimation(stateToPlay, 0);
        }
    }
    #endregion

    #region 코루틴 및 비동기 메서드
    /// <summary>
    /// 공격 애니메이션을 비동기적으로 재생합니다.
    /// </summary>
    private async UniTaskVoid PlayAttackAnimationAsync()
    {
        if (m_currentState != PlayerState.IDLE && m_currentState != PlayerState.MOVE) return;

        SetState(PlayerState.ATTACK);

        var attackClips = m_spumPrefabs.StateAnimationPairs[PlayerState.ATTACK.ToString()];
        if (attackClips != null && attackClips.Count > 0 && attackClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(attackClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException) { return; }
        }

        if (m_currentState == PlayerState.ATTACK)
        {
            SetState(PlayerState.IDLE);
        }
    }

    /// <summary>
    /// 피격 애니메이션을 비동기적으로 재생하고, 완료 후 상태를 복귀시킵니다.
    /// </summary>
    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (m_currentState != PlayerState.DAMAGED) return;

        var damagedClips = m_spumPrefabs.StateAnimationPairs[PlayerState.DAMAGED.ToString()];
        if (damagedClips != null && damagedClips.Count > 0 && damagedClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
            }
            catch (OperationCanceledException) { return; }
        }

        if (m_currentState != PlayerState.DEATH)
        {
            SetState(PlayerState.IDLE);
            StartRepeatingFire();
        }
    }

    /// <summary>
    /// 가장 가까운 적을 향해 자동으로 반복 발사하는 루프를 실행합니다.
    /// </summary>
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

                if (FindNearestEnemy() == null)
                {
                    break;
                }

                FireAtNearestEnemy();
                m_lastFireTime = Time.time;
            }
        }
        catch (OperationCanceledException){}
    }

    /// <summary>
    /// 지정된 방향으로 계속 이동하는 루프를 실행합니다.
    /// </summary>
    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        SetState(PlayerState.MOVE);

        float targetVelocityX = direction * m_maxSpeed;
        var tcs = new UniTaskCompletionSource();
        token.Register(() => tcs.TrySetCanceled());

        m_movementTween?.Kill();
        m_movementTween = m_rigidbody2D.DOVector(new Vector2(targetVelocityX, m_rigidbody2D.linearVelocity.y), m_accelerationTime)
            .SetEase(m_accelerationEase)
            .SetUpdate(UpdateType.Fixed)
            .OnComplete(() => tcs.TrySetResult());

        try
        {
            await tcs.Task;
            
            while (!token.IsCancellationRequested)
            {
                float finalVelocityX = targetVelocityX;

                if ((m_rigidbody2D.position.x <= m_cameraMinX && finalVelocityX < 0) ||
                    (m_rigidbody2D.position.x >= m_cameraMaxX && finalVelocityX > 0))
                {
                    finalVelocityX = 0;
                }

                if (Mathf.Abs(finalVelocityX) > 0.01f)
                {
                    Bounds playerBounds = m_collider.bounds;
                    float moveSign = Mathf.Sign(finalVelocityX);
                    Vector2 groundCheckOrigin = (Vector2)playerBounds.center + new Vector2(moveSign * playerBounds.extents.x, -playerBounds.extents.y - 0.05f);
                    RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, groundLayer);

                    if (groundHit.collider == null)
                    {
                        finalVelocityX = 0;
                    }
                }

                m_rigidbody2D.linearVelocity = new Vector2(finalVelocityX, m_rigidbody2D.linearVelocity.y);
                PlayAnimationForState(Mathf.Abs(finalVelocityX) > 0.01f ? PlayerState.MOVE : PlayerState.IDLE);

                ClampPosition();
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }
        }
        catch (OperationCanceledException){}
    }

    /// <summary>
    /// 지정된 위치까지 대쉬하는 동작을 비동기적으로 실행합니다.
    /// </summary>
    private async UniTaskVoid DashAsync(float targetX, float duration)
    {
        m_isDashing = true;
        SetState(PlayerState.MOVE);

        m_moveCts?.Cancel();
        m_fireCts?.Cancel();
        m_movementTween?.Kill();

        float startY = m_rigidbody2D.position.y;
        float direction = Mathf.Sign(targetX - m_rigidbody2D.position.x);
        if (m_spumPrefabs != null)
        {
            m_spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        if (m_afterimageEffect != null)
            m_afterimageEffect.StartEffect(duration);

        if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDashSfx))
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDashSfx);
        }

        float originalGravity = m_rigidbody2D.gravityScale;
        m_rigidbody2D.gravityScale = 0;
        m_rigidbody2D.linearVelocity = Vector2.zero;

        m_dashCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(m_dashCts.Token, this.GetCancellationTokenOnDestroy());

        try
        {
            float elapsedTime = 0f;
            while (elapsedTime < duration)
            {
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, linkedCts.Token);
                elapsedTime += Time.fixedDeltaTime;

                float newX = Mathf.MoveTowards(m_rigidbody2D.position.x, targetX, m_dashSpeed * Time.fixedDeltaTime);
                m_rigidbody2D.position = new Vector2(newX, startY);

                if (Mathf.Approximately(m_rigidbody2D.position.x, targetX))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            m_rigidbody2D.gravityScale = originalGravity;
            m_rigidbody2D.linearVelocity = Vector2.zero;
            if (!this.GetCancellationTokenOnDestroy().IsCancellationRequested)
            {
                m_rigidbody2D.position = new Vector2(targetX, startY);
            }
            m_isDashing = false;
            
            if (m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED)
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire();
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