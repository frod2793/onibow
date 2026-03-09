using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;
using UnityEngine.Serialization;
using OniBow.FX;
using OniBow.Managers;
using OniBow.Projectiles;
using OniBow.Logic;
using OniBow.Presentation;
using VContainer;

using OniBow.UI.Interfaces;
using OniBow.AI.BT;

namespace OniBow
{
    /// <summary>
    /// 플레이어를 공격하는 적 AI 클래스입니다.
    /// 자신의 화살 궤적을 기준으로 최적의 공격 위치로 이동한 후 공격하는 패턴을 반복합니다.
    /// UniTask를 사용하여 비동기적으로 동작합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Enemy : MonoBehaviour, IHealthProvider, IDamageable
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

        [Header("컴포넌트 참조")]
        private EnemyHealth m_health;
        private EnemyHealth Health
        {
            get
            {
                if (m_health == null) m_health = GetComponent<EnemyHealth>();
                return m_health;
            }
        }

        private EnemyMovement m_movement;
        private EnemyMovement Movement
        {
            get
            {
                if (m_movement == null) m_movement = GetComponent<EnemyMovement>();
                return m_movement;
            }
        }

        private EnemyCombat m_combat;
        private EnemyCombat Combat
        {
            get
            {
                if (m_combat == null) m_combat = GetComponent<EnemyCombat>();
                return m_combat;
            }
        }

        private SPUM_Prefabs m_enemyAnimation;
        private Rigidbody2D m_rigidbody2D;
        private Collider2D m_collider;
        private AfterimageEffect m_afterimageEffect;
        private CancellationTokenSource m_aiTaskCts;
        private OniBow.AI.BT.Node m_behaviorTree;
        private GameFlowController m_gameFlowController;
        private CameraEffectView m_cameraEffectView;

        [Header("이동 대기 설정")]
        private float m_moveDelayStartTime = -1f;
        private float m_currentMoveDelay = 0f;

        [Header("AI 설정")]
        [SerializeField] private float m_moveSpeed = 3f;
        public EnemyState CurrentState { get; private set; } = EnemyState.Idle;
        [SerializeField] private Transform m_player;
        [Inject]
        public void Construct(GameFlowController gameFlowController, CameraEffectView cameraEffectView)
        {
            m_gameFlowController = gameFlowController;
            m_cameraEffectView = cameraEffectView;
        }

        public bool IsDead => Health != null && Health.IsDead;
        
        private const string k_PlayerTag = "Player";
        private const string k_ArrowTag = "Arrow";
        #endregion

        #region MonoBehaviour 콜백
        private void Awake()
        {
            m_rigidbody2D = GetComponent<Rigidbody2D>();
            m_collider = GetComponent<Collider2D>();
            m_afterimageEffect = GetComponent<AfterimageEffect>();
            m_enemyAnimation = GetComponentInChildren<SPUM_Prefabs>();
            
            if (Movement != null)
            {
                // [Safety]: EnemyMovement 내부에서 컴포넌트를 직접 찾아 초기화하도록 보장합니다.
                Movement.Initialize(m_moveSpeed, Movement.DistanceTolerance, LayerMask.GetMask("Ground"));
                Movement.DetectBoundaries();
            }
            else
            {
                Debug.LogError($"[Enemy] {gameObject.name}에 EnemyMovement 컴포넌트가 없습니다!");
            }

            if (Health != null) Health.OnEnemyDied += Die;

            if (m_enemyAnimation != null)
            {
                if (!m_enemyAnimation.allListsHaveItemsExist())
                {
                    m_enemyAnimation.PopulateAnimationLists();
                }
                m_enemyAnimation.OverrideControllerInit();
            }

            SetupHealth();
            SetupBehaviorTree();
        }

        private void SetupHealth()
        {
            if (Health != null)
            {
                // [수정]: 인스펙터 설정을 유지하기 위해 Initialize(하드코딩)를 제거하고 
                // 초기 UI 동기화를 위한 ForceUpdateHpUI만 호출합니다.
                Health.ForceUpdateHpUI();
            }
        }

        void Start()
        {
            if (m_player == null)
            {
                var playerObject = GameObject.FindGameObjectWithTag(k_PlayerTag);
                if (playerObject != null) m_player = playerObject.transform;
            }

            // 초기 배치 위치 기준 경계 감지 (스폰 지점 준수)
            if (Movement != null) Movement.DetectBoundaries();

            // [추가]: Start 시점에 UI가 최종적으로 동기화되도록 한 번 더 호출합니다.
            if (Health != null) Health.ForceUpdateHpUI();

            m_aiTaskCts = new CancellationTokenSource();
            AI_LoopAsync(m_aiTaskCts.Token).Forget();
        }

        private void Update()
        {
            if (IsDead) return;

            if (m_gameFlowController != null && m_gameFlowController.CurrentState == GameState.Playing)
            {
                CheckIfOffScreen();
            }

            // [개선]: 이동 상태일 때 실제 이동 속도에 비례하여 애니메이션 재생 속도를 조절합니다.
            if (CurrentState == EnemyState.Moving && m_enemyAnimation != null && m_enemyAnimation._anim != null && Movement != null)
            {
                float speedRatio = Movement.ActualSpeed / Mathf.Max(0.1f, Movement.MoveSpeed);
                m_enemyAnimation._anim.speed = Mathf.Clamp(speedRatio, 0.1f, 3f);
            }

            // [추가]: 모든 상태에서 적이 경계 밖으로 벗어나지 않도록 강제로 Clamp를 수행합니다.
            // 이는 피격이나 물리적 충돌로 밀려나는 상황에서도 안전성을 확보합니다.
            if (!IsDead && Movement != null)
            {
                Movement.ClampPosition();
            }
        }



        private void OnDestroy()
        {
            if (m_health != null) Health.OnEnemyDied -= Die;
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
            if (IsDead || CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged) return;

            bool canTryEvade = CurrentState == EnemyState.Moving || CurrentState == EnemyState.Attacking || CurrentState == EnemyState.Idle;
            if (canTryEvade && Combat != null && UnityEngine.Random.value < Combat.EvadeChance)
            {
                bool evaded = await EvadeAsync();
                if (evaded) return;
            }

            if (Health != null)
            {
                Health.TakeDamage(damage, (actualDamage) => {
                    if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDamagedSfx))
                    {
                        SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDamagedSfx);
                    }
                    EffectManager.Instance.ShowDamageText(gameObject, actualDamage);
                });
            }

            if (!IsDead)
            {
                PlayDamagedAnimationAsync().Forget();
            }
        }

        public void ForceUpdateHpUI()
        {
            if (Health != null) Health.ForceUpdateHpUI();
        }

        public void HealWithTempHp()
        {
            if (Health != null) Health.HealWithTempHp();
        }

        public event Action<float, float, float, float> OnHealthUpdated
        {
            add { if (Health != null) Health.OnHealthUpdated += value; }
            remove { if (Health != null) Health.OnHealthUpdated -= value; }
        }
        #endregion

        #region AI 핵심 로직

        /// <summary>
        /// 적의 사망 처리를 담당합니다.
        /// </summary>
        private void Die()
        {
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyDeathSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyDeathSfx);
            }
            
            m_aiTaskCts?.Cancel();
            if (m_rigidbody2D != null) m_rigidbody2D.linearVelocity = Vector2.zero;
            if (m_collider != null) m_collider.enabled = false;
            
            SetState(EnemyState.Dead);
     
            m_gameFlowController?.HandleEnemyDeath();
            OnEnemyDestroyed?.Invoke(this);
            
            Destroy(gameObject, 3f);
        }

        /// <summary>
        /// 적의 주 AI 루프를 비동기적으로 실행합니다. BT의 Evaluate를 주기적으로 호출합니다.
        /// </summary>
        private async UniTaskVoid AI_LoopAsync(CancellationToken token)
        {
            if (m_gameFlowController != null && m_gameFlowController.CurrentState != GameState.Playing)
            {
                await UniTask.WaitUntil(() => m_gameFlowController.CurrentState == GameState.Playing, cancellationToken: token);
            }

            while (!token.IsCancellationRequested && !IsDead)
            {
                if (m_gameFlowController != null && m_gameFlowController.CurrentState == GameState.Playing)
                {
                    if (m_behaviorTree != null)
                    {
                        m_behaviorTree.Evaluate();
                    }
                }
                
                await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow();
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
                if (useSkill && Combat != null)
                {
                    await Combat.ExecuteSkillAsync(m_player, token);
                }
                else if (Combat != null)
                {
                    if (attackClip != null)
                    {
                        float fireDelay = attackClip.length * 0.5f;
                        await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);
                        if (this == null) return; // [Hotfix]: 파괴된 경우 중단

                        if (IsDead) return;
                        Combat.PerformArrowLaunch(m_player);

                        await UniTask.Delay(TimeSpan.FromSeconds(attackClip.length - fireDelay), cancellationToken: token);
                        if (this == null) return;
                    }
                    else
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                        if (this == null) return;
                        
                        if (IsDead) return;
                        Combat.PerformArrowLaunch(m_player);
                        await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
                        if (this == null) return;
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
            if (IsDead || m_enemyAnimation == null) return;

            m_aiTaskCts?.Cancel();
            SetState(EnemyState.Damaged);
            if (Movement != null) Movement.Stop();

            var damagedClip = m_enemyAnimation.DAMAGED_List.Count > 0 ? m_enemyAnimation.DAMAGED_List[0] : null;
            if (damagedClip != null)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(damagedClip.length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
                    if (this == null) return; // [Hotfix]: 파괴된 경우 중단
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            
            if (!IsDead)
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
            if (m_cameraEffectView != null)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                float cameraBottom = cam.ViewportToWorldPoint(new Vector3(0, 0, 0)).y;
                float destroyThreshold = cameraBottom - 2f;

                if (transform.position.y < destroyThreshold)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void FlipCharacter(float horizontalDirection)
        {
            if (Mathf.Abs(horizontalDirection) < 0.01f) return;
            if (m_enemyAnimation == null) return;
            
            m_enemyAnimation.transform.rotation = Quaternion.Euler(0f, horizontalDirection > 0 ? 180f : 0f, 0f);
        }

        private void SetState(EnemyState newState)
        {
            // [추가]: 이동 상태에서 다른 상태로 벗어날 때만 다음 이동 지연 타이머를 초기화합니다.
            if (CurrentState == EnemyState.Moving && newState != EnemyState.Moving)
            {
                m_moveDelayStartTime = -1f;
            }

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

            // [개선]: 이동 상태가 아닐 때는 애니메이션 속도를 기본값(1.0)으로 복구합니다.
            if (newState != EnemyState.Moving && m_enemyAnimation._anim != null)
            {
                m_enemyAnimation._anim.speed = 1f;
            }
        }

        #if UNITY_EDITOR
        public async void TestMultiShotSkill()
        {
            if (!Application.isPlaying) return;
            if (m_combat != null) await m_combat.ExecuteSkillAsync(m_player, this.GetCancellationTokenOnDestroy());
        }
        
        public async void TestEvade()
        {
            if (!Application.isPlaying) return;
            if (CurrentState == EnemyState.Evading || CurrentState == EnemyState.Damaged || IsDead) return;
            await EvadeAsync();
        }
        #endif
        #endregion

        #region BT 구성
        private void SetupBehaviorTree()
        {
            var root = new Selector();

            // 1. 피격/사망/회피/스킬 중에는 대기 (Priority 0)
            var busyCheck = new ConditionNode(() => 
                CurrentState == EnemyState.Damaged || 
                CurrentState == EnemyState.Dead || 
                CurrentState == EnemyState.Evading ||
                CurrentState == EnemyState.SkillAttacking);
            
            root.AddChild(new Sequence()
                .AddChild(busyCheck)
                .AddChild(new ActionNode(() => NodeState.Success)));

            // 2. 힐링 조건 체크 및 실행
            root.AddChild(new Sequence()
                .AddChild(new CheckHealthPercentNode(this, Health, Combat != null ? Combat.HealHealthThreshold : 0.4f))
                .AddChild(new CheckCooldownNode(() => Combat != null && Time.time >= Combat.LastHealTime + Combat.HealSkillCooldown))
                .AddChild(new EnemyActionNode(this, () => OnHealingStateAsync(m_aiTaskCts.Token))));

            // 3. 공격 사거리 밖이거나 너무 가까우면 이동
            float arrowRange = Combat != null ? Combat.ArrowRange : 7f;
            float retreatDistance = arrowRange * 0.8f; // [조정]: 90%는 너무 좁아 80%로 완화하여 이동 안정성 확보
            
            root.AddChild(new Sequence()
                .AddChild(new ConditionNode(() => {
                    if (m_player == null) return false;
                    float dist = Mathf.Abs(m_player.position.x - transform.position.x);
                    // 화살 사거리보다 멀거나, 최소 퇴각 거리보다 가까우면 이동이 필요함
                    return dist > arrowRange || dist < retreatDistance;
                }))
                .AddChild(new EnemyActionNode(this, () => OnMovingStateAsync(m_aiTaskCts.Token))));

            // 4. 공격 (스킬 우선)
            var attackSelector = new Selector();
            
            // 4.1 스킬 공격 (실전 로직: 스킬 중이면 쿨다운/확률 무시하고 진행 중인 노드 유지)
            attackSelector.AddChild(new Sequence()
                .AddChild(new ConditionNode(() => 
                    CurrentState == EnemyState.SkillAttacking || 
                    (Combat != null && Time.time >= Combat.LastSkillUseTime + Combat.SkillCooldown && 
                     UnityEngine.Random.value < Combat.SkillChance)))
                .AddChild(new EnemyActionNode(this, () => OnSkillAttackingStateAsync(m_aiTaskCts.Token))));

            // 4.2 일반 공격 (스킬 중이거나 이미 공격 중이면 차단)
            attackSelector.AddChild(new Sequence()
                .AddChild(new ConditionNode(() => CurrentState != EnemyState.Attacking && CurrentState != EnemyState.SkillAttacking))
                .AddChild(new EnemyActionNode(this, () => OnAttackingStateAsync(m_aiTaskCts.Token))));

            root.AddChild(attackSelector);

            // 5. 아무것도 해당 안되면 Idle (공격 중에는 전환 금지)
            root.AddChild(new ActionNode(() => {
                if (CurrentState == EnemyState.Attacking || CurrentState == EnemyState.SkillAttacking)
                    return NodeState.Success;

                if (CurrentState != EnemyState.Idle) SetState(EnemyState.Idle);
                if (Movement != null) Movement.Stop();
                return NodeState.Success;
            }));

            m_behaviorTree = root;
        }

        #endregion

        #region AI 상태별 로직 (BT 노드에서 호출됨)
        private async UniTask OnMovingStateAsync(CancellationToken token)
        {
            if (IsDead || m_player == null) return;

            // [추가]: 스킬 사용 중에는 이동하지 않도록 차단합니다.
            if (CurrentState == EnemyState.SkillAttacking)
            {
                StopAndIdle();
                return;
            }

            float horizontalDistanceToPlayer = Mathf.Abs(m_player.position.x - transform.position.x);
            float xDirection = Mathf.Sign(m_player.position.x - transform.position.x);
            
            float arrowRange = Combat != null ? Combat.ArrowRange : 7f;
            float retreatDistance = arrowRange * 0.8f; // [조정]: 이동 안정성을 위해 80%로 환원
            
            float moveDirection = 0f;
            if (horizontalDistanceToPlayer > arrowRange)
            {
                // 화살 사거리보다 멀면 전진
                moveDirection = 1f;
            }
            else if (horizontalDistanceToPlayer < retreatDistance)
            {
                // 너무 가까우면 후진
                moveDirection = -1f;
            }
            else
            {
                // 적정 거리(80%~100%) 내에 있으면 정지
                StopAndIdle();
                return;
            }

            float targetXVelocity = xDirection * (Movement != null ? Movement.MoveSpeed : 3f) * moveDirection;

            if (Movement != null)
            {
                // [개선]: 정지 상태에서 이동 상태로 전환될 때만 0~3초의 랜덤 딜레이를 부여합니다.
                if (CurrentState != EnemyState.Moving)
                {
                    if (m_moveDelayStartTime < 0f)
                    {
                        m_moveDelayStartTime = Time.time;
                        m_currentMoveDelay = UnityEngine.Random.Range(0f, 1f);
                    }

                    if (Time.time < m_moveDelayStartTime + m_currentMoveDelay)
                    {
                        // 아직 딜레이 시간이 지나지 않았으면 이동 명령을 내리지 않고 유지
                        StopAndIdle();
                        return;
                    }
                }

                // 딜레이가 끝나고 실제 이동을 시작할 때 초기화
                m_moveDelayStartTime = -1f;

                // Movement 컴포넌트 내부에서 이미 IsGroundAhead 및 경계 체크를 수행함
                float actualVelocity = Movement.Move(targetXVelocity);
                
                // [수정]: ActualSpeed 대신 실제 명령 속도(actualVelocity)를 기준으로 상태를 결정하여 
                // 첫 프레임에 이동이 멈추는 피드백 루프 오류를 방지합니다.
                if (Mathf.Abs(actualVelocity) > 0.01f)
                {
                    if (CurrentState != EnemyState.Moving) SetState(EnemyState.Moving);
                    FlipCharacter(actualVelocity);
                }
                else
                {
                    // 벽이나 경계에 막혀 속도가 0이 된 경우
                    StopAndIdle();
                }
            }
            else
            {
                StopAndIdle();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        /// <summary>
        /// [설명]: 적을 정지시키고 즉시 Idle 상태로 전환합니다.
        /// </summary>
        private void StopAndIdle()
        {
            Movement?.Stop();
            if (CurrentState != EnemyState.Idle) SetState(EnemyState.Idle);
        }

        private async UniTask OnAttackingStateAsync(CancellationToken token)
        {
            if (IsDead) return;

            if (m_player != null)
            {
                float directionToPlayer = m_player.position.x - transform.position.x;
                FlipCharacter(directionToPlayer);
            }

            try
            {
                await PlayAttackAndFireAsync(false, token);
                await UniTask.Delay(TimeSpan.FromSeconds(m_combat != null ? m_combat.AttackCooldown : 2f), cancellationToken: token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (!IsDead && CurrentState == EnemyState.Attacking)
                    SetState(EnemyState.Idle);
            }
        }

        private async UniTask OnSkillAttackingStateAsync(CancellationToken token)
        {
            if (IsDead) return;

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
                if (!IsDead && CurrentState == EnemyState.SkillAttacking)
                    SetState(EnemyState.Idle);
            }
        }

        private async UniTask OnHealingStateAsync(CancellationToken token)
        {
            if (IsDead) return;

            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.EnemyHealSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.EnemyHealSfx);
            }

            if (Combat != null) Combat.ResetHealCooldown();
            
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), cancellationToken: token);

            if (token.IsCancellationRequested || IsDead) return;

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
            if (IsDead || Combat == null || Movement == null) return false;

            return await Combat.EvadeAsync(
                Movement,
                FlipCharacter,
                (s) => SetState(s),
                () => m_aiTaskCts,
                (cts) => m_aiTaskCts = cts,
                () => { if (m_afterimageEffect != null) m_afterimageEffect.StartEffect(0.25f); }, // Using a default duration for now
                this.GetCancellationTokenOnDestroy()
            );
        }
        #endregion
    }
}