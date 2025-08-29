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

    [Header("Health Settings")]
    [Tooltip("최대 체력")]
    [SerializeField] private int maxHp = 100;
    [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
    [SerializeField] private float tempHpDecreaseDelay = 3f;
    [Tooltip("예비 체력이 현재 체력을 따라잡는 속도 (초당 체력)")]
    [SerializeField] private float tempHpCatchUpSpeed = 50f;
    private int _currentHp;
    private int _tempHp; // 예비 체력 (UI의 상단 바에 해당)
    private float _lastDamageTime; // 마지막 피격 시간
    public event Action OnPlayerDied; // 플레이어 사망 시 발생하는 이벤트
    public event Action<int, int, int> OnHealthUpdated; // 체력 변경 시 발생하는 이벤트 (현재, 예비, 최대)

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
    [SerializeField] private GameObject arrowPrefab;
    [Tooltip("화살이 발사될 위치")]
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

    [Header("Collision Settings")]
    [Tooltip("대쉬 시 충돌을 감지할 지면 및 벽 레이어")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Dash Settings")]
    [Tooltip("대쉬 속도")]
    [SerializeField] private float dashSpeed = 20f;
    [Tooltip("대쉬 지속 시간 (초)")]
    [SerializeField] private float dashDuration = 0.2f;
    [Tooltip("대쉬 쿨다운 (초)")]
    [SerializeField] private float dashCooldown = 1f;

    private Rigidbody2D _rigidbody2D;
    private Collider2D _collider;
    private Tween _movementTween;
    private CancellationTokenSource _moveCts;
    private CancellationTokenSource _fireCts;
    private CancellationTokenSource _dashCts;
    private float _lastFireTime = -999f; // 마지막 발사 시간을 기록
    private float _lastDashTime = -999f; // 마지막 대쉬 시간을 기록
    private SPUM_Prefabs _spumPrefabs; // 애니메이션 제어를 위한 참조
    private AfterimageEffect _afterimageEffect; // 잔상 효과 참조
    private PlayerState _currentState = PlayerState.IDLE; // 현재 플레이어 상태
    private bool _isDashing = false; // 대쉬 중인지 확인하는 플래그
    private bool isInvulnerable = false; // 무적 상태인지 확인하는 플래그
    
    // 카메라 경계
    private float _cameraMinX;
    private float _cameraMaxX;

    #endregion

    #region Unity 메시지

    private void Awake()
    {
        _rigidbody2D = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        _spumPrefabs = GetComponentInChildren<SPUM_Prefabs>();
        _afterimageEffect = GetComponent<AfterimageEffect>();
        if (_afterimageEffect == null)
        {
            Debug.LogWarning("Player에 AfterimageEffect 컴포넌트가 없습니다. 대쉬 잔상 효과가 동작하지 않습니다.");
        }

        _currentHp = maxHp;
        _tempHp = maxHp;

        if (_spumPrefabs != null)
        {
            if(!_spumPrefabs.allListsHaveItemsExist()){
                _spumPrefabs.PopulateAnimationLists();
            }
            _spumPrefabs.OverrideControllerInit();
            if (_spumPrefabs._anim == null)
            {
                Debug.LogError("SPUM_Prefabs의 Animator 참조가 null입니다!");
            }
        }
        else
        {
            Debug.LogError("자식 오브젝트에서 SPUM_Prefabs 컴포넌트를 찾을 수 없습니다. 애니메이션이 작동하지 않습니다.");
        }

        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
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
        if (_currentState != PlayerState.DEATH && other.CompareTag("EnemyArrow"))
        {
            TakeDamage(10);
        }
    }

    private void OnDestroy()
    {
        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _dashCts?.Cancel();
        _dashCts?.Dispose();
        _movementTween?.Kill();
    }

    #endregion

    #region 공개 메서드

    public void ForceUpdateHpUI()
    {
        OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
    }

    public void TakeDamage(int damage)
    {
        // [우선권 1순위: 피격] - 다른 모든 행동을 중단시킴
        // 무적 상태일 경우 데미지를 받지 않습니다.
        if (isInvulnerable) return;

        if (_currentState == PlayerState.DEATH) return; 

        CancelAllActions();
        SetState(PlayerState.DAMAGED);

        // 데미지를 받기 시작한 시점의 체력을 예비 체력으로 기록합니다.
        // 예비 체력 감소 딜레이 시간 내에 추가 타격을 받으면, 예비 체력은 갱신되지 않고 현재 체력만 감소합니다.
        if (Time.time > _lastDamageTime + tempHpDecreaseDelay)
        {
            _tempHp = _currentHp;
        }

        _currentHp -= damage;
        _currentHp = Mathf.Max(0, _currentHp);
        _lastDamageTime = Time.time; // 마지막 피격 시간 갱신

        Debug.Log($"플레이어가 {damage}의 데미지를 입었습니다. 현재 체력: {_currentHp}");
        // UI 매니저에게 현재 체력과, 데미지를 받기 시작한 시점의 체력(예비 체력)을 전달합니다.
        OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);

        // 데미지 텍스트 표시 (캐릭터 머리 위)
        EffectManager.Instance.ShowDamageText(gameObject, damage);

        PlayDamagedAnimationAsync().Forget();

        if (_currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 예비 체력만큼 현재 체력을 회복합니다. (회복 스킬용)
    /// </summary>
    public void HealWithTempHp()
    {
        if (_currentState == PlayerState.DEATH) return;
        int recoveryAmount = _tempHp - _currentHp;
        if (recoveryAmount > 0)
        {
            _currentHp += recoveryAmount;
            _tempHp = _currentHp; // 회복 후에는 예비 체력과 현재 체력을 일치시킴
            OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
        }
    }

    /// <summary>
    /// 플레이어의 무적 상태를 설정합니다. (배리어 스킬용)
    /// </summary>
    /// <param name="state">무적 상태 여부</param>
    public void SetInvulnerable(bool state)
    {
        isInvulnerable = state;
    }

    /// <summary>
    /// 플레이어의 최대 체력을 반환합니다.
    /// </summary>
    public int GetMaxHp() => maxHp;

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
            _currentHp = Mathf.Min(_currentHp + (int)Mathf.Ceil(healThisFrame), maxHp);
            _tempHp = _currentHp; // 점진적 회복 중에는 예비 체력도 함께 회복됩니다.
            OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
            
            elapsedTime += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// 스킬 사용 상태를 설정하고, 공격 로직을 제어합니다.
    /// SkillManager에서 호출합니다.
    /// </summary>
    /// <param name="isUsing">스킬을 사용 중이면 true, 아니면 false.</param>
    public void SetSkillUsageState(bool isUsing)
    {
        if (isUsing)
        {
            // 스킬 사용 시작 시, 다른 모든 행동을 중단하고 상태를 변경합니다.
            CancelAllActions();
            SetState(PlayerState.OTHER);
        }
        else
        {
            // 스킬 사용이 끝나면 IDLE 상태로 돌아가 자동 공격을 다시 시작합니다.
            if (_currentState == PlayerState.OTHER)
            {
                SetState(PlayerState.IDLE);
                StartRepeatingFire();
            }
        }
    }

    public void StartMoving(float direction)
    {
        // [우선권 2순위: 이동] - 공격 행동을 중단시킴
        // 사망, 피격, 대쉬, 스킬 사용 중에는 이동할 수 없습니다. 공격 중에는 이동으로 전환할 수 있습니다.
        if (_currentState == PlayerState.DEATH || _isDashing || _currentState == PlayerState.DAMAGED || _currentState == PlayerState.OTHER) return;

        _fireCts?.Cancel(); // 공격 루프 중단

        if (_spumPrefabs != null)
        {
            _spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }
        SetState(PlayerState.MOVE);

        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _moveCts = new CancellationTokenSource();
        MoveLoopAsync(direction, _moveCts.Token).Forget();
    }

    public void StopMoving()
    {
        if (_currentState == PlayerState.DEATH) return;
        _moveCts?.Cancel();
        _movementTween?.Kill();

        _movementTween = _rigidbody2D.DOVector(new Vector2(0, _rigidbody2D.linearVelocity.y), decelerationTime)
            .SetEase(decelerationEase)
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

            Vector3 startPos = firePoint != null ? firePoint.position : transform.position;
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;

            GameObject arrowObject = ObjectPoolManager.Instance.Get(arrowPrefab);
            if (arrowObject == null) return;

            ArrowController arrowController = arrowObject.GetComponent<ArrowController>();
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
    /// </summary>
    /// <returns>가장 가까운 적의 GameObject. 없으면 null을 반환합니다.</returns>
    public GameObject FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        GameObject nearestEnemy = null;
        float minDistance = Mathf.Infinity;

        foreach (var enemyObject in enemies)
        {
            Enemy enemyComponent = enemyObject.GetComponent<Enemy>();
            // 살아있는 적만 대상으로 합니다.
            if (enemyComponent != null && enemyComponent.currentState != Enemy.EnemyState.Dead)
            {
                float distance = Vector2.Distance(transform.position, enemyObject.transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestEnemy = enemyObject;
                }
            }
        }
        return nearestEnemy;
    }

    /// <summary>
    /// 지정된 방향으로 대쉬합니다.
    /// </summary>
    /// <param name="direction">대쉬 방향 (-1 for left, 1 for right)</param>
    public void Dash(float direction)
    {
        if (!IsActionableState() || Time.time < _lastDashTime + dashCooldown) return;

        // 1. 대쉬는 지상에 있을 때만 가능하도록, 먼저 발밑의 땅을 확인합니다.
        RaycastHit2D groundCheck = Physics2D.Raycast(
            (Vector2)transform.position + _collider.offset,
            Vector2.down,
            _collider.bounds.extents.y + 0.5f,
            groundLayer
        );
        if (groundCheck.collider == null)
        {
            return; // 공중에서는 대쉬를 실행하지 않습니다.
        }

        _lastDashTime = Time.time;
        float currentX = _rigidbody2D.position.x;
        float maxDashDistance = dashSpeed * dashDuration;

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
        // 대쉬 경로를 따라가며 발밑에 땅이 있는지 반복적으로 확인합니다.
        float finalDashDistance = 0f;
        bool cliffFound = false;
        int steps = 15; // 정밀도를 높이기 위해 확인 횟수를 늘립니다.
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, _rigidbody2D.position.y);

            // 예상 위치 아래에 땅이 있는지 확인합니다.
            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(_collider.bounds.size.x * 0.9f, 0.1f), // 캐릭터 너비의 90%로 확인하여 안정성 향상
                0f, Vector2.down, _collider.bounds.extents.y + 0.5f, groundLayer
            );

            if (groundUnderneath.collider == null)
            {
                // 땅이 없으면, 이전 단계까지가 안전한 거리입니다.
                finalDashDistance = (i - 1) * stepDistance;
                cliffFound = true;
                break; // 루프 탈출
            }
        }
        if (!cliffFound)
        {
            // 루프가 중단 없이 끝났다면, 벽까지의 거리가 최종 거리입니다.
            finalDashDistance = wallLimitedDistance;
        }

        // 벽이나 절벽에 너무 가깝게 붙지 않도록 캐릭터 너비 기반의 여유를 줍니다.
        finalDashDistance = Mathf.Max(0, finalDashDistance - _collider.bounds.extents.x);

        // 최종 목표 지점을 카메라 경계 내로 제한합니다.
        float finalTargetX = Mathf.Clamp(currentX + direction * finalDashDistance, _cameraMinX, _cameraMaxX);

        // 4. 실제 이동 거리에 따른 대쉬 시간을 계산하고 실행합니다.
        float actualDashDistance = Mathf.Abs(finalTargetX - currentX);
        float actualDuration = actualDashDistance / dashSpeed;
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
        if (GameManager.Instance != null && GameManager.Instance.mainCamera != null)
        {
            Camera cam = GameManager.Instance.mainCamera;
            _cameraMinX = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).x;
            _cameraMaxX = cam.ViewportToWorldPoint(new Vector3(1, 0, 0)).x;

            // 캐릭터가 화면 밖으로 완전히 나가지 않도록 너비만큼 보정합니다.
            float playerWidth = _collider.bounds.extents.x;
            _cameraMinX += playerWidth;
            _cameraMaxX -= playerWidth;
        }
        else
        {
            Debug.LogError("카메라 경계를 계산할 수 없습니다. GameManager 또는 MainCamera를 찾을 수 없습니다.");
            _cameraMinX = -Mathf.Infinity;
            _cameraMaxX = Mathf.Infinity;
        }
    }
    
    private void HandleEnemyDestroyed(Enemy enemy)
    {
        // 적이 파괴된 후, 다른 적이 있는지 확인
        if (FindNearestEnemy() == null)
        {
            // 남은 적이 없으면 공격 루프 중단
            _fireCts?.Cancel();
        }
    }

    private void Die()
    {
        SetState(PlayerState.DEATH); // 상태를 사망으로 변경
        CancelAllActions();

        // 체력을 0으로 설정하고 UI를 업데이트합니다.
        _currentHp = 0;
        _tempHp = 0;
        OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
        OnPlayerDied?.Invoke();

        Debug.Log("플레이어가 사망했습니다.");
    }

    private void CancelAllActions()
    {
        _moveCts?.Cancel();
        _fireCts?.Cancel();
        _dashCts?.Cancel();
        _movementTween?.Kill();
        _rigidbody2D.linearVelocity = Vector2.zero;
    }

    private void StartRepeatingFire()
    {
        // [우선권 3순위: 공격] - 다른 액션 중이 아닐 때만 시작
        if (!IsActionableState()) return;

        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(_fireCts.Token).Forget();
    }

    public void FireAtNearestEnemy()
    {
        if (!IsActionableState() || ObjectPoolManager.Instance == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            if (_spumPrefabs != null)
            {
                float directionToEnemyX = nearestEnemy.transform.position.x - transform.position.x;
                _spumPrefabs.transform.rotation = Quaternion.Euler(0f, directionToEnemyX > 0 ? 180f : 0f, 0f);
            }

            PlayAttackAnimationAsync().Forget();

            Vector3 startPos = firePoint != null ? firePoint.position : transform.position;
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;
            Vector3 endPos = startPos + (Vector3)direction * fireDistance;

            Vector3 apex = (startPos + endPos) / 2f + Vector3.up * fireArcHeight;
            Vector3 controlPoint = 2 * apex - (startPos + endPos) / 2f;

            GameObject arrowObject = ObjectPoolManager.Instance.Get(arrowPrefab);
            if (arrowObject == null) return;

            ArrowController arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Owner = ArrowController.ArrowOwner.Player;
                arrowController.Launch(startPos, controlPoint, endPos, fireDuration, fireEaseCurve);
            }
            else
            {
                ObjectPoolManager.Instance.Return(arrowObject);
            }
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, _cameraMinX, _cameraMaxX);
        _rigidbody2D.position = clampedPosition;
    }

    /// <summary>
    /// 이동, 공격, 대쉬 등 새로운 행동을 시작할 수 있는 상태인지 확인합니다.
    /// </summary>
    /// <returns>행동 가능 여부</returns>
    private bool IsActionableState()
    {
        // 대쉬 중에는 다른 행동 불가
        if (_isDashing) return false;

        // IDLE 또는 MOVE 상태일 때만 새로운 행동을 시작할 수 있습니다.
        return _currentState == PlayerState.IDLE || _currentState == PlayerState.MOVE;
    }

    /// <summary>
    /// 플레이어의 상태를 변경하고, 상태에 맞는 애니메이션을 재생합니다.
    /// </summary>
    /// <param name="newState">변경할 새로운 상태</param>
    private void SetState(PlayerState newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;
        PlayAnimationForState(newState);
    }

    /// <summary>
    /// 지정된 상태에 해당하는 애니메이션을 재생합니다.
    /// </summary>
    /// <param name="stateToPlay">재생할 애니메이션의 상태</param>
    private void PlayAnimationForState(PlayerState stateToPlay)
    {
        if (_spumPrefabs != null)
        {
            _spumPrefabs.PlayAnimation(stateToPlay, 0);
        }
    }
    #endregion

    #region 코루틴 및 비동기 메서드
    private async UniTaskVoid PlayAttackAnimationAsync()
    {
        if (!IsActionableState()) return;

        SetState(PlayerState.ATTACK);

        var attackClips = _spumPrefabs.StateAnimationPairs[PlayerState.ATTACK.ToString()];
        if (attackClips != null && attackClips.Count > 0 && attackClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(attackClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException) { return; } // 애니메이션 중 취소되면(피격, 이동 등) 즉시 복귀
        }

        // 다른 상태로 변경되지 않았다면 IDLE로 복귀
        if (_currentState == PlayerState.ATTACK)
        {
            SetState(PlayerState.IDLE);
        }
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        // SetState는 이미 TakeDamage에서 호출되었으므로, 여기서는 애니메이션 재생을 기다리기만 합니다.
        if (_currentState != PlayerState.DAMAGED) return;

        var damagedClips = _spumPrefabs.StateAnimationPairs[PlayerState.DAMAGED.ToString()];
        if (damagedClips != null && damagedClips.Count > 0 && damagedClips[0] != null)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(damagedClips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException) { return; } // 애니메이션 중 다른 피격 등으로 취소되면 즉시 복귀
        }

        // 피격 애니메이션이 끝난 후, 사망 상태가 아니라면 다시 자동 공격을 시작합니다.
        if (_currentState != PlayerState.DEATH)
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
                float timeUntilReady = (_lastFireTime + fireInterval) - Time.time;
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
                _lastFireTime = Time.time;
            }
        }
        catch (OperationCanceledException){}
    }

    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        float targetVelocityX = direction * maxSpeed;
        var tcs = new UniTaskCompletionSource();
        // 트윈이 외부에서 취소될 경우 Task도 함께 취소되도록 등록합니다.
        token.Register(() => tcs.TrySetCanceled());

        _movementTween?.Kill();
        _movementTween = _rigidbody2D.DOVector(new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y), accelerationTime)
            .SetEase(accelerationEase)
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
                if ((_rigidbody2D.position.x <= _cameraMinX && finalVelocityX < 0) ||
                    (_rigidbody2D.position.x >= _cameraMaxX && finalVelocityX > 0))
                {
                    finalVelocityX = 0;
                }

                // 2. 절벽 예측: 이동하려는 방향의 발밑에 땅이 있는지 확인합니다.
                if (Mathf.Abs(finalVelocityX) > 0.01f) // 실제로 움직일 때만 확인
                {
                    float moveSign = Mathf.Sign(finalVelocityX);
                    Vector2 groundCheckOrigin = (Vector2)_collider.bounds.center + new Vector2(moveSign * _collider.bounds.extents.x, -_collider.bounds.extents.y - 0.05f);
                    RaycastHit2D groundHit = Physics2D.Raycast(groundCheckOrigin, Vector2.down, 0.2f, groundLayer);

                    if (groundHit.collider == null)
                    {
                        finalVelocityX = 0; // 앞에 땅이 없으면 멈춥니다.
                    }
                }

                // 3. 최종 속도 적용 및 시각적 상태(애니메이션) 업데이트
                _rigidbody2D.linearVelocity = new Vector2(finalVelocityX, _rigidbody2D.linearVelocity.y);

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
        _isDashing = true;
        SetState(PlayerState.MOVE); // 대쉬 중에는 이동 애니메이션을 재생합니다.

        // 다른 행동 중단
        _moveCts?.Cancel();
        _fireCts?.Cancel();
        _movementTween?.Kill();

        float startY = _rigidbody2D.position.y;
        // 대쉬 방향으로 캐릭터 회전
        float direction = Mathf.Sign(targetX - _rigidbody2D.position.x);
        if (_spumPrefabs != null)
        {
            _spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        // 잔상 효과 시작
        _afterimageEffect?.StartEffect(duration);

        // 대쉬 움직임
        float originalGravity = _rigidbody2D.gravityScale;
        _rigidbody2D.gravityScale = 0; // 대쉬 중에는 중력 무시
        _rigidbody2D.linearVelocity = Vector2.zero; // MoveTowards를 사용하므로 초기 속도는 0으로 설정

        // 대쉬 취소를 위한 토큰 설정
        _dashCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_dashCts.Token, this.GetCancellationTokenOnDestroy());

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
                float newX = Mathf.MoveTowards(_rigidbody2D.position.x, targetX, dashSpeed * Time.fixedDeltaTime);
                _rigidbody2D.position = new Vector2(newX, startY);

                // 목표 지점에 도달했는지 확인
                if (Mathf.Approximately(_rigidbody2D.position.x, targetX))
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
            _rigidbody2D.gravityScale = originalGravity;
            _rigidbody2D.linearVelocity = Vector2.zero;
            // 대쉬가 끝난 후 정확한 위치에 있도록 보정
            if (!this.GetCancellationTokenOnDestroy().IsCancellationRequested)
            {
                _rigidbody2D.position = new Vector2(targetX, startY);
            }
            _isDashing = false;
            
            if (_currentState != PlayerState.DEATH)
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