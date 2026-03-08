using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using OniBow.Managers;
using OniBow.UI.Interfaces;

namespace OniBow
{
    /// <summary>
    /// [설명]: 플레이어의 체력 관리, 피격, 회복 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IHealthProvider, IDamageable
    {
        #region 에디터 설정
        [Header("체력 설정")]
        [Tooltip("최대 체력")]
        [SerializeField] private int m_maxHp = 100;
        
        [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
        [SerializeField] private float m_tempHpDecreaseDelay = 1f;
        #endregion

        #region 내부 필드
        private int m_currentHp;
        private int m_tempHp;
        private float m_lastDamageTime;
        private bool m_isInvulnerable = false;
        private bool m_isDecayRunning = false;
        private CancellationTokenSource m_decayCts;
        #endregion

        #region 프로퍼티 및 이벤트
        public int CurrentHp => m_currentHp;
        public int MaxHp => m_maxHp;
        public bool IsDead => m_currentHp <= 0;
        public bool IsInvulnerable => m_isInvulnerable;

        public event Action OnPlayerDied;
        /// <summary>
        /// [설명]: 건강 상태 변경 이벤트 (현재, 최대, 예비, 최대예비)
        /// </summary>
        public event Action<float, float, float, float> OnHealthUpdated;
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            m_currentHp = m_maxHp;
            m_tempHp = m_maxHp;
        }
        #endregion

        #region 공개 메서드
        /// <summary>
        /// [설명]: 초기화 및 초기 UI 업데이트를 수행합니다.
        /// </summary>
        public void Initialize()
        {
            m_currentHp = m_maxHp;
            m_tempHp = m_maxHp;
            ForceUpdateHpUI();
        }

        public void ForceUpdateHpUI()
        {
            OnHealthUpdated?.Invoke(m_currentHp, m_maxHp, m_tempHp, m_maxHp);
        }

        /// <summary>
        /// [설명]: 데미지를 적용합니다.
        /// </summary>
        public void TakeDamage(int damage)
        {
            if (m_isInvulnerable || IsDead) return;

            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerDamagedSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerDamagedSfx);
            }

            m_currentHp = Mathf.Max(0, m_currentHp - damage);
            m_lastDamageTime = Time.time;
            
            // 임시 체력 감소 루틴 시작 (최적화 버전)
            StartTempHpDecayAsync().Forget();
            
            ForceUpdateHpUI();

            if (m_currentHp <= 0)
            {
                OnPlayerDied?.Invoke();
            }
        }

        /// <summary>
        /// [설명]: 예비 체력만큼 현재 체력을 회복합니다.
        /// </summary>
        public void HealWithTempHp()
        {
            if (IsDead) return;
            int recoveryAmount = m_tempHp - m_currentHp;
            if (recoveryAmount > 0)
            {
                m_currentHp += recoveryAmount;
                m_tempHp = m_currentHp;
                ForceUpdateHpUI();

                if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.PlayerHealSfx))
                {
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.PlayerHealSfx);
                }
            }
        }

        public void SetInvulnerable(bool state)
        {
            m_isInvulnerable = state;
        }

        /// <summary>
        /// [설명]: 지정된 시간 동안 서서히 체력을 회복합니다.
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
                ForceUpdateHpUI();
                
                elapsedTime += Time.deltaTime;
                await UniTask.Yield(token);
            }
        }

        /// <summary>
        /// [설명]: 마지막 피격 후 일정 시간이 지나면 임시 체력을 현재 체력으로 동기화합니다.
        /// Update 대신 UniTask를 사용하여 필요한 시점에만 동작하도록 최적화되었습니다.
        /// </summary>
        private async UniTaskVoid StartTempHpDecayAsync()
        {
            if (m_isDecayRunning) return;
            m_isDecayRunning = true;

            try
            {
                while (Time.time < m_lastDamageTime + m_tempHpDecreaseDelay)
                {
                    float remainingTime = (m_lastDamageTime + m_tempHpDecreaseDelay) - Time.time;
                    if (remainingTime > 0)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(remainingTime), cancellationToken: this.GetCancellationTokenOnDestroy());
                    }
                }

                // 대기 후 최종 확인: 현재 체력이 예비 체력보다 낮을 때만 업데이트
                if (m_tempHp > m_currentHp)
                {
                    m_tempHp = m_currentHp;
                    ForceUpdateHpUI();
                }
            }
            finally
            {
                m_isDecayRunning = false;
            }
        }
        #endregion
    }
}
