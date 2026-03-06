/// <설명>적군 HUD의 상태를 관리하는 ViewModel. 대상 Enemy 의 HP 변화를 UI에 반영하기 위한 브로드캐스트를 제공합니다.</설명>
using System;
using OniBow.UI.Interfaces;

namespace OniBow.UI.ViewModels
{
    /// <설명>Enemy HUD 상태를 관리하는 ViewModel. Target Enemy 의 HP 상태를 뷰에 전달합니다.</설명>
    public class EnemyHUDViewModel
    {
        private IHealthProvider m_HealthProvider;

        public event Action<float, float> OnHpRatioChanged;
        public event Action<string> OnHpTextChanged;

        public void Initialize(IHealthProvider provider)
        {
            BindHealthProvider(provider);
        }

        // 호환성: 기존 객체 주입 경로도 지원
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
    }
}
