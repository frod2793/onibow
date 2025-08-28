using UnityEngine;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public Camera mainCamera;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    /// <summary>
    /// 메인 카메라를 흔드는 효과를 재생합니다.
    /// </summary>
    /// <param name="duration">흔들림 지속 시간</param>
    /// <param name="strength">흔들림 강도</param>
    /// <param name="vibrato">진동 횟수</param>
    /// <param name="randomness">무작위성</param>
    public void ShakeCamera(float duration, float strength, int vibrato = 10, float randomness = 90)
    {
        if (mainCamera != null)
        {
            mainCamera.transform.DOShakePosition(duration, strength, vibrato, randomness);
        }
        else
        {
            Debug.LogWarning("메인 카메라가 할당되지 않아 카메라 쉐이크를 실행할 수 없습니다.");
        }
    }
}
