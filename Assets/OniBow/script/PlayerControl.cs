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

    [Header("Movement Boundaries")]
    [Tooltip("이동 가능한 최소 X 좌표")]
    [SerializeField] private float minXPosition = -4.0f;
    [Tooltip("이동 가능한 최대 X 좌표")]
    [SerializeField] private float maxXPosition = 4.0f;

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
    private PlayerState _currentState; // 현재 플레이어 상태
    private bool _isAction; // 액션(공격, 피격) 애니메이션 재생 중인지 확인하는 플래그
    private bool _isDead = false; // 사망 상태 플래그
    private bool _isDashing = false; // 대쉬 중인지 확인하는 플래그

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
                Debug.LogError("SPUM_Prefabs has a null Animator reference!");
            }
        }
        else
        {
            Debug.LogError("SPUM_Prefabs component not found in children. Animations will not work.");
        }

        _rigidbody2D.gravityScale = 1;
        _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
        _currentState = PlayerState.IDLE;
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
        ForceUpdateHpUI();
    }

    private void Update()
    {
        // [우선권 4순위: 대기/이동] 다른 액션 중이 아닐 때만 상태 갱신
        if (_isAction || _isDead) return;

        if (Mathf.Abs(_rigidbody2D.linearVelocity.x) > 0.1f)
        {
            _currentState = PlayerState.MOVE;
        }
        else
        {
            _currentState = PlayerState.IDLE;
        }

        if (_spumPrefabs != null)
        {
            _spumPrefabs.PlayAnimation(_currentState, 0);
        }

        // 예비 체력(Temp HP)이 현재 체력보다 높고, 마지막 피격 후 일정 시간이 지났다면
        // 예비 체력을 서서히 감소시켜 현재 체력과 맞춥니다.
        if (!_isDead && _tempHp > _currentHp && Time.time >= _lastDamageTime + tempHpDecreaseDelay)
        {
            int decreaseAmount = (int)Mathf.Ceil(tempHpCatchUpSpeed * Time.deltaTime);
            _tempHp = Mathf.Max(_currentHp, _tempHp - decreaseAmount);
            OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isDead && other.CompareTag("EnemyArrow"))
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
        if (_isDead) return; 

        _moveCts?.Cancel();
        _dashCts?.Cancel();
        _fireCts?.Cancel();
        _movementTween?.Kill();
        _rigidbody2D.linearVelocity = Vector2.zero;

        int oldHp = _currentHp;
        _currentHp -= damage;
        _currentHp = Mathf.Max(0, _currentHp);

        // 예비 체력이 현재 체력과 같았다면 (즉, 예비 체력 효과가 없었다면),
        // 예비 체력을 이전 체력 값으로 설정하여 효과를 시작합니다.
        if (_tempHp == oldHp)
        {
            _tempHp = oldHp;
        }
        // 예비 체력이 이미 존재했다면 (이전 피격 후 아직 회복되지 않았다면),
        // 예비 체력도 함께 데미지를 입습니다.
        else
        {
            _tempHp -= damage;
        }
        // 예비 체력이 현재 체력보다 낮아지는 것을 방지합니다.
        _tempHp = Mathf.Max(_tempHp, _currentHp);
        _lastDamageTime = Time.time; // 마지막 피격 시간 갱신

        Debug.Log($"플레이어가 {damage}의 데미지를 입었습니다. 현재 체력: {_currentHp}");
        OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
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
        if (_isDead) return;
        int recoveryAmount = _tempHp - _currentHp;
        if (recoveryAmount > 0)
        {
            _currentHp += recoveryAmount;
            _tempHp = _currentHp; // 회복 후에는 예비 체력과 현재 체력을 일치시킴
            OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);
        }
    }

    public void StartMoving(float direction)
    {
        // [우선권 2순위: 이동] - 공격 행동을 중단시킴
        if (_isDead || _isDashing) return;

        _isAction = false; // 공격 애니메이션 중이었다면, 이를 해제
        _fireCts?.Cancel(); // 공격 루프 중단

        if (_spumPrefabs != null)
        {
            _spumPrefabs.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);
        }

        _moveCts?.Cancel();
        _moveCts?.Dispose();
        _moveCts = new CancellationTokenSource();
        MoveLoopAsync(direction, _moveCts.Token).Forget();
    }

    public void StopMoving()
    {
        if (_isDead) return;
        _moveCts?.Cancel();
        _movementTween?.Kill();

        _movementTween = _rigidbody2D.DOVector(new Vector2(0, _rigidbody2D.linearVelocity.y), decelerationTime)
            .SetEase(decelerationEase)
            .SetUpdate(UpdateType.Fixed)
            .OnComplete(StartRepeatingFire);
    }

    public void FireStraightArrow()
    {
        if (_isDead || _isAction || _isDashing || ArrowPool.Instance == null) return;

        GameObject nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            PlayAttackAnimationAsync().Forget();

            Vector3 startPos = firePoint != null ? firePoint.position : transform.position;
            Vector2 direction = (nearestEnemy.transform.position - startPos).normalized;

            GameObject arrowObject = ArrowPool.Instance.Get();
            if (arrowObject == null) return;

            ArrowController arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.LaunchStraight(startPos, direction, 100f, 1.5f);
            }
            else
            {
                ArrowPool.Instance.Return(arrowObject);
            }
        }
    }

    public GameObject FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length == 0) return null;

        GameObject nearestEnemy = null;
        float minDistanceSqr = float.MaxValue;

        foreach (GameObject enemyObject in enemies)
        {
            // [수정] 적 컴포넌트를 가져와서 살아있는지 확인합니다.
            Enemy enemyComponent = enemyObject.GetComponent<Enemy>();

            // 살아있는 적만 대상으로 거리를 계산합니다.
            if (enemyComponent != null && !enemyComponent.IsDead)
            {
                float distanceSqr = (enemyObject.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < minDistanceSqr)
                {
                    minDistanceSqr = distanceSqr;
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
        if (_isDead || _isAction || _isDashing || Time.time < _lastDashTime + dashCooldown) return;

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
        int steps = 15; // 정밀도를 높이기 위해 확인 횟수를 늘립니다.
        float stepDistance = wallLimitedDistance / steps;

        for (int i = 1; i <= steps; i++)
        {
            float checkDistance = i * stepDistance;
            Vector2 checkPos = new Vector2(currentX + direction * checkDistance, _rigidbody2D.position.y);

            // 예상 위치 아래에 땅이 있는지 확인합니다.
            RaycastHit2D groundUnderneath = Physics2D.BoxCast(
                checkPos,
                new Vector2(_collider.bounds.size.x * 0.5f, 0.1f), // 폭을 약간 좁게 하여 더 안정적으로 감지
                0f, Vector2.down, _collider.bounds.extents.y + 0.5f, groundLayer
            );

            if (groundUnderneath.collider == null)
            {
                // 땅이 없으면, 이전 단계까지가 안전한 거리입니다.
                finalDashDistance = (i - 1) * stepDistance;
                goto FoundSafeDistance; // 루프 탈출
            }
        }
        // 루프가 중단 없이 끝났다면, 벽까지의 거리가 최종 거리입니다.
        finalDashDistance = wallLimitedDistance;

    FoundSafeDistance:
        // 벽이나 절벽에 너무 가깝게 붙지 않도록 약간의 여유를 줍니다.
        finalDashDistance = Mathf.Max(0, finalDashDistance - 0.1f);

        float finalTargetX = currentX + direction * finalDashDistance;

        // 4. 실제 이동 거리에 따른 대쉬 시간을 계산하고 실행합니다.
        float actualDuration = finalDashDistance / dashSpeed;
        if (actualDuration < Time.fixedDeltaTime) return; // 이동 거리가 매우 짧으면 실행하지 않음

        DashAsync(finalTargetX, actualDuration).Forget();
    }
    #endregion

    #region 내부 로직

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
        _isDead = true;
        _currentState = PlayerState.DEATH;
        Debug.Log("플레이어가 사망했습니다.");

        _moveCts?.Cancel();
        _fireCts?.Cancel();
        _dashCts?.Cancel();

        _rigidbody2D.linearVelocity = Vector2.zero;

        // 체력을 0으로 설정하고 UI를 업데이트합니다.
        _currentHp = 0;
        _tempHp = 0;
        OnHealthUpdated?.Invoke(_currentHp, _tempHp, maxHp);

        if (_spumPrefabs != null)
        {
            _spumPrefabs.PlayAnimation(PlayerState.DEATH, 0);
        }
    }

    private void StartRepeatingFire()
    {
        // [우선권 3순위: 공격] - 다른 액션 중이 아닐 때만 시작
        if (_isDead || _isAction) return;

        _fireCts?.Cancel();
        _fireCts?.Dispose();
        _fireCts = new CancellationTokenSource();
        RepeatingFireLoopAsync(_fireCts.Token).Forget();
    }

    public void FireAtNearestEnemy()
    {
        if (_isDead || _isAction || _isDashing || ArrowPool.Instance == null) return;

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

            GameObject arrowObject = ArrowPool.Instance.Get();
            if (arrowObject == null) return;
            arrowObject.transform.position = startPos;
            arrowObject.transform.rotation = Quaternion.identity;

            ArrowController arrowController = arrowObject.GetComponent<ArrowController>();
            if (arrowController != null)
            {
                arrowController.Launch(startPos, controlPoint, endPos, fireDuration, fireEaseCurve);
            }
            else
            {
                ArrowPool.Instance.Return(arrowObject);
            }
        }
    }

    private void ClampPosition()
    {
        Vector2 clampedPosition = _rigidbody2D.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minXPosition, maxXPosition);
        _rigidbody2D.position = clampedPosition;
    }

    #endregion

    #region 코루틴 및 비동기 메서드

    private async UniTaskVoid PlayAttackAnimationAsync()
    {
        if (_isDead || _isDashing || _spumPrefabs == null) return;

        _isAction = true;
        _spumPrefabs.PlayAnimation(PlayerState.ATTACK, 0);

        var attackClips = _spumPrefabs.StateAnimationPairs[PlayerState.ATTACK.ToString()];
        if (attackClips != null && attackClips.Count > 0)
        {
            var clip = attackClips[0];
            if (clip != null)
            {
                await UniTask.Delay((int)(clip.length * 1000), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
        }

        _isAction = false;
    }

    private async UniTaskVoid PlayDamagedAnimationAsync()
    {
        if (_isDead || _isDashing || _spumPrefabs == null) return;

        _isAction = true;
        _spumPrefabs.PlayAnimation(PlayerState.DAMAGED, 0);

        var damagedClips = _spumPrefabs.StateAnimationPairs[PlayerState.DAMAGED.ToString()];
        if (damagedClips != null && damagedClips.Count > 0)
        {
            var clip = damagedClips[0];
            if (clip != null)
            {
                await UniTask.Delay((int)(clip.length * 1000), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
        }

        _isAction = false;

        // 피격 애니메이션이 끝난 후, 사망 상태가 아니라면 다시 자동 공격을 시작합니다.
        if (!_isDead)
        {
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
                    await UniTask.Delay((int)(timeUntilReady * 1000), cancellationToken: token);
                }
                if (token.IsCancellationRequested) break;

                FireAtNearestEnemy();
                _lastFireTime = Time.time;
            }
        }
        catch (OperationCanceledException){}
    }

    private async UniTaskVoid MoveLoopAsync(float direction, CancellationToken token)
    {
        float targetVelocityX = direction * maxSpeed;

        _movementTween?.Kill();
        _movementTween = _rigidbody2D.DOVector(new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y), accelerationTime)
            .SetEase(accelerationEase)
            .SetUpdate(UpdateType.Fixed);

        try
        {
            while (_movementTween.IsActive() && !token.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }
            if (token.IsCancellationRequested) return;

            while (!token.IsCancellationRequested)
            {
                _rigidbody2D.linearVelocity = new Vector2(targetVelocityX, _rigidbody2D.linearVelocity.y);
                ClampPosition();
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token);
            }
        }
        catch (OperationCanceledException){}
    }

    private async UniTaskVoid DashAsync(float targetX, float duration)
    {
        _isAction = true;
        _isDashing = true;

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
        _rigidbody2D.linearVelocity = new Vector2(direction * dashSpeed, 0);

        // 대쉬 취소를 위한 토큰 설정
        _dashCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_dashCts.Token, this.GetCancellationTokenOnDestroy());

        try
        {
            float elapsedTime = 0f;
            while (elapsedTime < duration)
            {
                // 다음 FixedUpdate까지 대기하여 물리 엔진이 캐릭터를 움직이게 합니다.
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, linkedCts.Token);
                elapsedTime += Time.fixedDeltaTime;

                // 대쉬 중 Y축 위치를 고정하여 수평 이동을 보장합니다.
                _rigidbody2D.position = new Vector2(_rigidbody2D.position.x, startY);

                // 목표 지점을 지나쳤는지 확인 (물리 오차 감안)
                if ((direction > 0 && _rigidbody2D.position.x >= targetX) ||
                    (direction < 0 && _rigidbody2D.position.x <= targetX))
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
            _rigidbody2D.position = new Vector2(targetX, startY);
            _isDashing = false;
            _isAction = false;

            if (!_isDead) StartRepeatingFire(); // 대쉬 후 다시 자동 공격 상태로 전환
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
