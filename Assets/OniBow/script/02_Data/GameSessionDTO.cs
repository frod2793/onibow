using System;

namespace OniBow.Data
{
    #region 데이터 모델 (DTO)
    /// <summary>
    /// [설명]: 게임 세션의 초기 설정 및 상태 데이터를 담는 전송 객체입니다.
    /// </summary>
    [Serializable]
    public class GameSessionDTO
    {
        public bool DeveloperMode = false;
        
        // 전환 효과 설정
        public float InitialDelay = 1f;
        public float DoorOpenDuration = 1.0f;
        public float BackgroundFadeDuration = 1.0f;
        public int CountdownStart = 5;
    }
    #endregion
}
