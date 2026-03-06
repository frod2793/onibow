/// <설명>Player HUD의 상태를 보유하고 View로 변경 알림을 브로드캐스트하는 ViewModel.</설명>
/// <설명>MVVM 아키텍처의 핵심 연결자 역할로, MonoBehaviour 의존 없이 순수 C# 로 작성됩니다.</설명>
using System;
using OniBow.UI.Interfaces;

namespace OniBow.UI.ViewModels
{
    /// <설명>Player HUD 상태를 관리하는 ViewModel. Model의 건강 상태를 View에 전달합니다.</설명>
    public class PlayerHUDViewModel
    {
        private IHealthProvider m_HealthProvider;
        private PlayerControl m_Player; // 이동 기능을 위해 직접 참조 (또는 IMovable 인터페이스 활용 가능)

        // Player HP 변화에 따른 UI 갱신 신호
        public event Action<float, float> OnHpRatioChanged;
        // HP 텍스트 변화에 따른 UI 갱신 신호
        public event Action<string> OnHpTextChanged;

        /// <summary>
        /// 주입 대상 초기화 (인터페이스 기반 주입 지원)
        /// </summary>
        /// <param name="provider">HealthProvider</param>
        public void Initialize(IHealthProvider provider)
        {
            BindHealthProvider(provider);
            if (provider is PlayerControl pc)
            {
                m_Player = pc;
            }
        }

        /// <summary>
        /// 예전 객체 형태 주입도 호환합니다. UIManager/Resolver에서 사용 가능하도록 오버로드 제공
        /// </summary>
        /// <param name="providerObj">Health provider 객체</param>
        public void Initialize(object providerObj)
        {
            if (providerObj is IHealthProvider hp)
            {
                BindHealthProvider(hp);
            }
        }

        private void BindHealthProvider(IHealthProvider hp)
        {
            m_HealthProvider = hp;
            m_HealthProvider.OnHealthUpdated += HealthUpdated;
        }

        private void HealthUpdated(float currentHp, float maxHp, float tempHp, float maxTempHp)
        {
            float ratioMain = maxHp > 0f ? currentHp / maxHp : 0f;
            float ratioTemp = maxTempHp > 0f ? tempHp / maxTempHp : 0f;
            OnHpRatioChanged?.Invoke(ratioMain, ratioTemp);
            OnHpTextChanged?.Invoke($"{(int)currentHp}/{(int)maxHp}");
        }

        #region 이동 명령
        /// <summary>
        /// [설명]: 플레이어 이동을 시작합니다. (UI 버튼 Down 이벤트에서 호출)
        /// </summary>
        /// <param name="direction">방향 (-1: 좌, 1: 우)</param>
        public void MoveBegin(float direction)
        {
            if (m_Player != null)
            {
                m_Player.OnMoveButtonDown(direction);
            }
        }

        /// <summary>
        /// [설명]: 플레이어 이동을 중지합니다. (UI 버튼 Up 이벤트에서 호출)
        /// </summary>
        public void MoveEnd()
        {
            if (m_Player != null)
            {
                m_Player.OnMoveButtonUp();
            }
        }
        #endregion
    }
}
