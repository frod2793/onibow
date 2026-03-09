/// <설명>Player HUD의 상태를 보유하고 View로 변경 알림을 브로드캐스트하는 ViewModel.</설명>
/// <설명>MVVM 아키텍처의 핵심 연결자 역할로, MonoBehaviour 의존 없이 순수 C# 로 작성됩니다.</설명>
using System;
using OniBow.UI.Interfaces;

namespace OniBow.UI.ViewModels
{
    /// <설명>Player HUD 상태를 관리하는 ViewModel. Model의 건강 상태를 View에 전달합니다.</설명>
    public class PlayerHUDViewModel
    {
        private IHealthProvider m_healthProvider;
        private PlayerControl m_player;

        public event Action<float, float> OnHpRatioChanged;
        public event Action<string> OnHpTextChanged;

        public PlayerHUDViewModel(PlayerControl player)
        {
            m_player = player;
            BindHealthProvider(player);
        }

        public void Initialize(IHealthProvider provider)
        {
            if (m_healthProvider != null) m_healthProvider.OnHealthUpdated -= HealthUpdated;
            BindHealthProvider(provider);
            if (provider is PlayerControl pc) m_player = pc;
        }

        public void Initialize(object providerObj)
        {
            if (providerObj is IHealthProvider hp)
            {
                Initialize(hp);
            }
        }

        private void BindHealthProvider(IHealthProvider hp)
        {
            m_healthProvider = hp;
            m_healthProvider.OnHealthUpdated += HealthUpdated;
        }

        private void HealthUpdated(float currentHp, float maxHp, float tempHp, float maxTempHp)
        {
            float ratioMain = maxHp > 0f ? currentHp / maxHp : 0f;
            float ratioTemp = maxTempHp > 0f ? tempHp / maxTempHp : 0f;
            OnHpRatioChanged?.Invoke(ratioMain, ratioTemp);
            OnHpTextChanged?.Invoke($"{(int)currentHp}/{(int)maxHp}");
        }

        #region 이동 명령
        public void MoveBegin(float direction)
        {
            if (m_player != null) m_player.OnMoveButtonDown(direction);
        }

        public void MoveEnd()
        {
            if (m_player != null) m_player.OnMoveButtonUp();
        }
        #endregion
    }
}
