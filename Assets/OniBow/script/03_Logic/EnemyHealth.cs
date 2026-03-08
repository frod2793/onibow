using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using OniBow.UI.Interfaces;

namespace OniBow
{
    /// <summary>
    /// [설명]: 적의 체력 관리, 피격, 사망 로직을 담당하는 컴포넌트입니다.
    /// </summary>
    public class EnemyHealth : MonoBehaviour, IHealthProvider
    {
        #region 에디터 설정
        [Header("체력 설정")]
        [SerializeField] private int m_maxHp = 150;
        [Tooltip("예비 체력이 현재 체력을 따라잡기 시작하는 시간 (초)")]
        [SerializeField] private float m_tempHpDecreaseDelay = 1.5f;
        #endregion

        #region 내부 필드
        private int m_currentHp;
        private int m_tempHp;
        private float m_lastDamageTime;
        private bool m_isDead;
        private bool m_isDecayRunning = false;
        #endregion

        #region 프로퍼티 및 이벤트
        public int CurrentHp => m_currentHp;
        public int MaxHp => m_maxHp;
        public bool IsDead => m_isDead;

        public event Action<float, float, float, float> OnHealthUpdated;
        public event Action OnEnemyDied;
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            m_currentHp = m_maxHp;
            m_tempHp = m_maxHp;
        }
        #endregion

        #region 공개 메서드
        public void Initialize(int maxHp, float tempHpDelay)
        {
            m_maxHp = maxHp;
            m_tempHpDecreaseDelay = tempHpDelay;
            m_currentHp = m_maxHp;
            m_tempHp = m_maxHp;
            m_isDead = false;
            ForceUpdateHpUI();
        }

        public void TakeDamage(int damage, Action<int> onDamagedCallback)
        {
            if (m_isDead) return;

            m_currentHp = Mathf.Max(0, m_currentHp - damage);
            m_lastDamageTime = Time.time;

            // 임시 체력 감소 루틴 시작 (최적화 버전)
            StartTempHpDecayAsync().Forget();

            onDamagedCallback?.Invoke(damage);
            ForceUpdateHpUI();

            if (m_currentHp <= 0)
            {
                Die();
            }
        }

        public void HealWithTempHp()
        {
            if (m_isDead) return;
            int recoveryAmount = m_tempHp - m_currentHp;
            if (recoveryAmount > 0)
            {
                m_currentHp += recoveryAmount;
                m_tempHp = m_currentHp;
                ForceUpdateHpUI();
            }
        }

        public void ForceUpdateHpUI()
        {
            OnHealthUpdated?.Invoke(m_currentHp, m_maxHp, m_tempHp, m_maxHp);
        }
        #endregion

        #region 내부 로직
        private void Die()
        {
            if (m_isDead) return;
            m_isDead = true;
            m_currentHp = 0;
            m_tempHp = 0;
            ForceUpdateHpUI();
            OnEnemyDied?.Invoke();
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

                if (!m_isDead && m_tempHp > m_currentHp)
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