using UnityEngine;

/// <summary>
/// 게임 화면의 해상도와 비율을 관리하는 싱글톤 클래스입니다.
/// WebGL 빌드 등 다양한 화면 크기에서도 16:9 비율을 유지하고,
/// 남는 공간은 검은색 레터박스로 처리합니다.
/// </summary>
public class ScreenResolutionManager : MonoBehaviour
{
    public static ScreenResolutionManager Instance { get; private set; }

    [Tooltip("비율을 고정할 메인 카메라. 할당하지 않으면 Camera.main을 사용합니다.")]
    [SerializeField] private Camera m_mainCamera;

    // 목표 비율 (16:9)
    private const float k_TargetAspectRatio = 16.0f / 9.0f;

    private Camera m_letterboxCamera;
    private int m_lastScreenWidth = 0;
    private int m_lastScreenHeight = 0;

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
            return;
        }

        if (m_mainCamera == null)
        {
            m_mainCamera = Camera.main;
        }

        if (m_mainCamera == null)
        {
            Debug.LogError("메인 카메라를 찾을 수 없습니다. ScreenResolutionManager가 정상적으로 동작하지 않을 수 있습니다.");
            enabled = false;
            return;
        }

        CreateLetterboxCamera();
        UpdateAspectRatio();
    }

    private void Update()
    {
        // 에디터 또는 런타임에서 화면 크기가 변경될 경우를 대비하여 매 프레임 체크
        if (Screen.width != m_lastScreenWidth || Screen.height != m_lastScreenHeight)
        {
            UpdateAspectRatio();
        }
    }

    /// <summary>
    /// 현재 화면 비율을 계산하고, 목표 비율(16:9)에 맞게 카메라의 Viewport Rect를 조절합니다.
    /// </summary>
    private void UpdateAspectRatio()
    {
        m_lastScreenWidth = Screen.width;
        m_lastScreenHeight = Screen.height;

        float windowAspectRatio = (float)m_lastScreenWidth / m_lastScreenHeight;
        float scaleHeight = windowAspectRatio / k_TargetAspectRatio;

        Rect rect = m_mainCamera.rect;

        if (scaleHeight < 1.0f) // 현재 화면이 세로로 더 긴 경우 (Letterbox)
        {
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;
        }
        else // 현재 화면이 가로로 더 긴 경우 (Pillarbox)
        {
            float scaleWidth = 1.0f / scaleHeight;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;
        }

        m_mainCamera.rect = rect;
    }

    /// <summary>
    /// 레터박스 영역을 검은색으로 채우기 위한 별도의 카메라를 생성하고 설정합니다.
    /// </summary>
    private void CreateLetterboxCamera()
    {
        GameObject letterboxCamGo = new GameObject("LetterboxCamera");
        letterboxCamGo.transform.SetParent(transform);
        m_letterboxCamera = letterboxCamGo.AddComponent<Camera>();
        m_letterboxCamera.backgroundColor = Color.black;
        m_letterboxCamera.cullingMask = 0; // 아무것도 렌더링하지 않음
        m_letterboxCamera.depth = -100; // 메인 카메라보다 먼저 렌더링
        m_letterboxCamera.clearFlags = CameraClearFlags.SolidColor;
    }
}