using System;

namespace OniBow.UI.Interfaces
{
    /// <summary>
    /// [설명]: 건강 상태(HP)를 제공하는 인터페이스입니다.
    /// 플레이어와 적군 등 건강 상태를 UI에 표시해야 하는 모든 도메인 모델이 상속합니다.
    /// </summary>
    public interface IHealthProvider
    {
        /// <summary>
        /// [설명]: 건강 상태가 변경될 때 호출되는 이벤트입니다.
        /// (현재 체력, 최대 체력, 현재 예비 체력, 최대 예비 체력)
        /// </summary>
        event Action<float, float, float, float> OnHealthUpdated;
    }
}
