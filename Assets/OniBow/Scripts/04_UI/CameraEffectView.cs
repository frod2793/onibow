using UnityEngine;
using DG.Tweening;
using VContainer;

namespace OniBow.Presentation
{
    /// <summary>
    /// [설명]: 카메라 연출(흔들림 등)을 담당하는 뷰 클래스입니다.
    /// </summary>
    public class CameraEffectView : MonoBehaviour
    {
        #region 내부 필드
        [SerializeField] private Camera m_targetCamera;
        private Vector3 m_initialPosition;
        #endregion

        private void Awake()
        {
            if (m_targetCamera == null) m_targetCamera = Camera.main;
            if (m_targetCamera != null) m_initialPosition = m_targetCamera.transform.position;
        }

        #region 공개 메서드
        /// <summary>
        /// [설명]: 카메라를 흔드는 효과를 실행합니다.
        /// </summary>
        public void ShakeCamera(float duration, float strength, int vibrato = 10, float randomness = 90)
        {
            if (m_targetCamera == null) return;

            m_targetCamera.transform.DOKill(true);
            m_targetCamera.transform.DOShakePosition(duration, strength, vibrato, randomness)
                .SetLoops(1, LoopType.Restart)
                .OnComplete(() =>
                {
                    if (m_targetCamera.transform.position != m_initialPosition)
                        m_targetCamera.transform.DOMove(m_initialPosition, 0.2f).SetEase(Ease.OutQuad);
                });
        }
        #endregion
    }
}
