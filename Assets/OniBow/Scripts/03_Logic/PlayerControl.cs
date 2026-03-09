using System;
using UnityEngine;
using System.Threading;
using Cysharp.Threading.Tasks;
using OniBow.FX;
using OniBow.Managers;
using OniBow.Logic;
using OniBow.Data;
using VContainer;
using OniBow.UI.Interfaces;
using OniBow.Presentation;

namespace OniBow
{
    /// <summary>
    /// [설명]: 플레이어의 이동, 공격, 체력을 조율하는 Facade 클래스입니다.
    /// 실제 로직은 PlayerMovement, PlayerCombat, PlayerHealth 컴포넌트에서 처리합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerControl : MonoBehaviour, IHealthProvider, IDamageable
    {
        #region 내부 필드 (컴포넌트 참조)
        private PlayerMovement m_movement;
        private PlayerMovement Movement
        {
            get
            {
                if (m_movement == null)
                {
                    m_movement = GetComponent<PlayerMovement>();
                    if (m_movement == null) m_movement = gameObject.AddComponent<PlayerMovement>();
                }
                return m_movement;
            }
        }

        private PlayerCombat m_combat;
        private PlayerCombat Combat
        {
            get
            {
                if (m_combat == null)
                {
                    m_combat = GetComponent<PlayerCombat>();
                    if (m_combat == null) m_combat = gameObject.AddComponent<PlayerCombat>();
                }
                return m_combat;
            }
        }

        private PlayerHealth m_health;
        private PlayerHealth Health
        {
            get
            {
                if (m_health == null)
                {
                    m_health = GetComponent<PlayerHealth>();
                    if (m_health == null) m_health = gameObject.AddComponent<PlayerHealth>();
                }
                return m_health;
            }
        }
        private SPUM_Prefabs m_spum;
        #endregion

        #region 의존성 주입
        private GameFlowController m_gameFlow;
        private CameraEffectView m_cameraEffect;
        private GameSessionDTO m_session;

        [Inject]
        public void Construct(GameFlowController gameFlow, CameraEffectView cameraEffect, GameSessionDTO session)
        {
            m_gameFlow = gameFlow;
            m_cameraEffect = cameraEffect;
            m_session = session;
        }
        #endregion

        #region 내부 상태
        private PlayerState m_currentState = PlayerState.IDLE;
        private CancellationTokenSource m_actionCts;
        
        // 더블 클릭(대쉬)용 입력 타이머
        private float m_lastClickTime = -1f;
        private float m_lastClickDirection = 0f;
        private const float k_DoubleClickTime = 0.3f;
        private const string k_EnemyArrowTag = "EnemyArrow";
        #endregion

        #region IHealthProvider 구현 (위임)
        public event Action<float, float, float, float> OnHealthUpdated
        {
            add => Health.OnHealthUpdated += value;
            remove => Health.OnHealthUpdated -= value;
        }
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            // 하위 시스템 지연 초기화 프로퍼티를 통해 사전 접근
            _ = Movement;
            _ = Combat;

            m_spum = GetComponentInChildren<SPUM_Prefabs>();

            Health.OnPlayerDied += Die;
        }

        private void Start()
        {
            InitializeSystems();
            
            if (m_gameFlow != null)
            {
                m_gameFlow.OnStateChanged += HandleGameStateChanged;
                if (m_gameFlow.CurrentState == GameState.Playing)
                {
                    StartAutoAttack();
                }
            }
            else
            {
                StartAutoAttack();
            }
        }

        private void HandleGameStateChanged(GameState newState)
        {
            if (newState == GameState.Playing)
            {
                SetState(PlayerState.IDLE);
                StartAutoAttack();
                
                // [추가]: 게임 시작 시 UI가 최신 체력 상태를 반영하도록 강제 업데이트합니다.
                ForceUpdateHpUI();
            }
            else
            {
                CancelAllActions();
            }
        }

        private void Update()
        {
            if (m_gameFlow != null && m_gameFlow.CurrentState == GameState.Playing)
            {
                HandleInput();
            }
        }



        private void OnDestroy()
        {
            CancelAllActions();
            if (Health != null) Health.OnPlayerDied -= Die;
            if (m_gameFlow != null) m_gameFlow.OnStateChanged -= HandleGameStateChanged;
        }
        #endregion

        #region 초기화 로직
        private void InitializeSystems()
        {
            Health.Initialize();
            Combat.Initialize();
            
            if (m_spum != null)
            {
                if (!m_spum.allListsHaveItemsExist()) m_spum.PopulateAnimationLists();
                m_spum.OverrideControllerInit();
            }
        }
        #endregion

        #region 입력 처리 및 조율
        private void HandleInput()
        {
            if (m_currentState == PlayerState.DEATH || m_currentState == PlayerState.DAMAGED) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            // 이동 및 대쉬(더블 클릭)
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                OnMoveButtonDown(1);
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                OnMoveButtonDown(-1);

            bool anyKeyPressed = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ||
                                 keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            
            if (!anyKeyPressed && (keyboard.dKey.wasReleasedThisFrame || keyboard.rightArrowKey.wasReleasedThisFrame ||
                                   keyboard.aKey.wasReleasedThisFrame || keyboard.leftArrowKey.wasReleasedThisFrame))
            {
                OnMoveButtonUp();
            }
        }

        public void OnMoveButtonDown(float direction)
        {
            // 이전 클릭과 같은 방향이고, 제 시간 내에 다시 눌렀는지 확인
            // 단, 같은 프레임에서 동시에 여러 키가 눌린 경우는 더블 클릭으로 간주하지 않음 (0.05s 이상의 간격 필요)
            bool isSameDirection = Mathf.Approximately(direction, m_lastClickDirection);
            bool isProperTimeGap = Time.time - m_lastClickTime < k_DoubleClickTime && Time.time - m_lastClickTime > 0.05f;

            if (isSameDirection && isProperTimeGap)
            {
                if (Movement.CanDash())
                {
                    Dash(direction);
                }
                m_lastClickTime = -1f; // 대쉬 후 타이머 초기화 (연속 대쉬 방지)
            }
            else
            {
                StartMoving(direction);
                m_lastClickTime = Time.time;
                m_lastClickDirection = direction;
            }
        }

        public void OnMoveButtonUp()
        {
            if (m_currentState != PlayerState.MOVE) return;
            
            CancelCurrentAction();
            Movement.StopMoving(() => {
                SetState(PlayerState.IDLE);
                StartAutoAttack();
            });
        }

        public void StartMoving(float direction)
        {
            if (Movement.IsDashing || m_currentState == PlayerState.DAMAGED || m_currentState == PlayerState.DEATH) return;

            Combat.StopRepeatingFire();
            CancelCurrentAction();
            m_actionCts = new CancellationTokenSource();

            // 방향 전환
            if (m_spum != null)
                m_spum.transform.rotation = Quaternion.Euler(0f, direction > 0 ? 180f : 0f, 0f);

            Movement.MoveLoopAsync(direction, m_actionCts.Token, (isMoving) => {
                SetState(isMoving ? PlayerState.MOVE : PlayerState.IDLE);
            }).Forget();
        }

        public void Dash(float direction)
        {
            CancelCurrentAction();
            m_actionCts = new CancellationTokenSource();

            var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(m_actionCts.Token, this.GetCancellationTokenOnDestroy()).Token;

            Movement.DashAsync(direction, linkedToken, 
                onStart: () => {
                    Combat.StopRepeatingFire();
                    SetInvulnerable(true); // 대쉬 시작 시 무적 적용
                    SetState(PlayerState.MOVE); // 대쉬 시에도 이동 애니메이션 사용
                },
                onComplete: () => {
                    SetInvulnerable(false); // 대쉬 완료 시 무적 해제
                    if (m_currentState != PlayerState.DEATH && m_currentState != PlayerState.DAMAGED)
                    {
                        SetState(PlayerState.IDLE);
                        StartAutoAttack();
                    }
                }
            ).Forget();
        }

        private void StartAutoAttack()
        {
            if (m_currentState == PlayerState.DEATH || m_currentState == PlayerState.DAMAGED) return;
            
            Combat.StartRepeatingFire(
                canAction: () => m_currentState == PlayerState.IDLE || m_currentState == PlayerState.MOVE,
                onFire: () => PlayAttackAnimation().Forget()
            );
        }
        #endregion

        #region 상태 및 애니메이션 조율
        private void SetState(PlayerState newState)
        {
            if (m_currentState == newState) return;
            m_currentState = newState;
            
            if (m_spum != null)
                m_spum.PlayAnimation(newState, 0);
        }

        private async UniTaskVoid PlayAttackAnimation()
        {
            if (m_currentState == PlayerState.DEATH || m_currentState == PlayerState.DAMAGED) return;

            // 적 방향으로 회전
            GameObject target = Combat.FindNearestEnemyOptimized();
            if (target != null && m_spum != null)
            {
                float dir = target.transform.position.x - transform.position.x;
                m_spum.transform.rotation = Quaternion.Euler(0f, dir > 0 ? 180f : 0f, 0f);
            }

            SetState(PlayerState.ATTACK);

            // 애니메이션 길이만큼 대기
            var clips = m_spum.StateAnimationPairs[PlayerState.ATTACK.ToString()];
            if (clips != null && clips.Count > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(clips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
                if (this == null) return; // [Hotfix]: 파괴된 경우 중단
            }

            if (m_currentState == PlayerState.ATTACK)
                SetState(PlayerState.IDLE);
        }
        #endregion

        #region 공개 인터페이스 (Facade)
        public event Action OnPlayerDied;

        public void ForceUpdateHpUI() => Health.ForceUpdateHpUI();

        public void TakeDamage(int damage)
        {
            if (Health.IsInvulnerable || Health.IsDead) return;

            CancelAllActions();
            Health.TakeDamage(damage);
            
            EffectManager.Instance.ShowDamageText(gameObject, damage);
            
            if (!Health.IsDead)
            {
                SetState(PlayerState.DAMAGED);
                PlayDamagedAnimation().Forget();
            }
        }

        private async UniTaskVoid PlayDamagedAnimation()
        {
            var clips = m_spum.StateAnimationPairs[PlayerState.DAMAGED.ToString()];
            if (clips != null && clips.Count > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(clips[0].length), cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
                if (this == null) return; // [Hotfix]: 파괴된 경우 중단
            }

            if (m_currentState != PlayerState.DEATH)
            {
                SetState(PlayerState.IDLE);
                StartAutoAttack();
            }
        }

        private void Die()
        {
            SetState(PlayerState.DEATH);
            CancelAllActions();
            
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDeathSfx))
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDeathSfx);

            m_gameFlow?.HandlePlayerDeath();
            OnPlayerDied?.Invoke();
        }

        private void CancelAllActions()
        {
            CancelCurrentAction();
            Movement.CancelMovement();
            Combat.StopRepeatingFire();
        }

        private void CancelCurrentAction()
        {
            if (m_actionCts != null)
            {
                m_actionCts.Cancel();
                m_actionCts.Dispose();
                m_actionCts = null;
            }
        }

        public void HealWithTempHp() => Health.HealWithTempHp();
        public void SetInvulnerable(bool state) => Health.SetInvulnerable(state);
        public int GetMaxHp() => Health.MaxHp;
        public async UniTask GradualHeal(float amount, float duration, CancellationToken token) => await Health.GradualHeal(amount, duration, token);
        
        public void FireStraightArrow() => Combat.FireStraightArrow(() => PlayAttackAnimation().Forget());
        public GameObject FindNearestEnemy() => Combat.FindNearestEnemyOptimized();

        public void SetSkillUsageState(bool isUsing, bool stopMovement = true)
        {
            if (isUsing)
            {
                if (stopMovement) CancelAllActions();
                else Combat.StopRepeatingFire();
                SetState(PlayerState.OTHER);
            }
            else
            {
                if (m_currentState == PlayerState.OTHER)
                {
                    SetState(PlayerState.IDLE);
                    StartAutoAttack();
                }
            }
        }
        #endregion
    }
}