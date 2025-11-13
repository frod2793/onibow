using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

// --- 커스텀 셰이더 방식 중요 설정 안내 ---
// 1. 씬 구성:
//    a. 배경 레이어 수만큼 '3D Object > Quad'를 생성하고, BackgroundManager의 'Scene Layers' 리스트에 할당합니다.
//    b. 각 Quad는 항상 활성화된 상태로 유지됩니다.
// 2. 머티리얼 설정:
//    a. 각 Quad에 할당할 새 Material을 생성합니다.
//    b. [중요] Material의 Shader를 'Custom/Crossfade'로 설정해야 합니다.
// 3. 텍스처 설정:
//    a. 배경으로 사용할 각 이미지(텍스처)의 Wrap Mode를 'Repeat'으로 설정해야 합니다.
//    b. BackgroundManager의 'Background Themes' 리스트에 배경 세트를 만들고, 각 세트의 'Layer Textures'에 씬 레이어 순서에 맞는 텍스처를 할당합니다.

public class BackgroundManager  : MonoBehaviour
{
    #region 직렬화 가능 클래스
    [System.Serializable]
    public class SceneLayer
    {
        public MeshRenderer meshRenderer; // 씬에 배치된 Quad의 MeshRenderer
        public float speedFactor;

        [HideInInspector] public Material materialInstance;
        [HideInInspector] public Vector2 currentOffset;
    }

    [System.Serializable]
    public class BackgroundTheme
    {
        public string name;
        public List<Texture> layerTextures = new List<Texture>(); // 각 SceneLayer에 대응하는 텍스처 리스트
    }
    #endregion

    #region 공개 필드
    [Header("씬 설정")]
    public List<SceneLayer> sceneLayers = new List<SceneLayer>();

    [Header("배경 테마")]
    public List<BackgroundTheme> backgroundThemes = new List<BackgroundTheme>();
    public int startBackgroundIndex = 0;

    [Header("스크롤 속도")]
    public float mainScrollSpeed = 0.1f;

    [Header("카메라 설정")]
    [Tooltip("배경의 기준이 될 메인 카메라. 할당하지 않으면 Camera.main을 사용합니다.")]
    [SerializeField] private Camera m_camera;

    [Header("전환 효과")]
    public float fadeDuration = 1.5f;
    #endregion

    #region 비공개 필드
    private int _currentBackgroundIndex;
    private static Texture2D _placeholderTexture;
    private bool _isSwitching = false;
    #endregion

    #region Unity 생명주기 메서드
    void Start()
    {
        if (sceneLayers.Count == 0)
        {
            Debug.LogError("Scene Layers가 설정되지 않았습니다.");
            return;
        }
        if (backgroundThemes.Count == 0)
        {
            Debug.LogError("Background Themes가 설정되지 않았습니다.");
            return;
        }

        if (m_camera == null)
        {
            m_camera = Camera.main;
        }

        ResizeBackgroundsToFitScreen();
        InitializeLayers();
        SetInitialBackground();
    }

    void Update()
    {
        // 전환 중에는 스크롤 로직을 건너뜁니다. (단, 스크롤 값 계산은 계속될 수 있음)
        foreach (var layer in sceneLayers)
        {
            ScrollLayer(layer);
        }
    }
    #endregion

    #region 공개 메서드
    public void SwitchBackground(int newIndex)
    {
        if (newIndex < 0 || newIndex >= backgroundThemes.Count || newIndex == _currentBackgroundIndex || _isSwitching)
        {
            return;
        }

        _isSwitching = true;
        var newTheme = backgroundThemes[newIndex];

        if (newTheme.layerTextures.Count != sceneLayers.Count)
        {
            Debug.LogError($"배경 테마 '{newTheme.name}'의 텍스처 개수({newTheme.layerTextures.Count})가 씬 레이어 개수({sceneLayers.Count})와 일치하지 않습니다.");
            _isSwitching = false;
            return;
        }

        // 새 텍스처를 _SecondTex에 할당
        for (int i = 0; i < sceneLayers.Count; i++)
        {
            var layer = sceneLayers[i];
            var newTex = newTheme.layerTextures[i] ?? GetPlaceholderTexture();
            layer.materialInstance.SetTexture("_SecondTex", newTex);
        }

        // _Blend 값을 0에서 1로 트위닝하여 크로스페이드 효과 적용
        DOTween.To(() => 0f, value => SetBlend(value), 1f, fadeDuration)
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                // 전환 완료 후 _MainTex를 새 텍스처로 교체하고 _Blend 리셋
                for (int i = 0; i < sceneLayers.Count; i++)
                {
                    var layer = sceneLayers[i];
                    // OnComplete 시점의 newTheme을 다시 참조합니다.
                    var completedTheme = backgroundThemes[newIndex];
                    var newTex = completedTheme.layerTextures[i] ?? GetPlaceholderTexture();
                    layer.materialInstance.SetTexture("_MainTex", newTex);
                }
                SetBlend(0f);
                _currentBackgroundIndex = newIndex;
                _isSwitching = false;
            });
    }
    #endregion

    #region 비공개 메서드
    /// <summary>
    /// 화면 해상도와 카메라 뷰포트에 맞춰 모든 배경 레이어의 크기를 조절합니다.
    /// ScreenResolutionManager에 의해 레터박스가 적용된 실제 게임 화면을 채우도록 계산합니다.
    /// </summary>
    private void ResizeBackgroundsToFitScreen()
    {
        if (m_camera == null)
        {
            Debug.LogError("카메라가 할당되지 않아 배경 크기를 조절할 수 없습니다.", this);
            return;
        }

        // ScreenResolutionManager가 적용한 뷰포트(rect)를 고려하여 월드 크기를 계산합니다.
        float screenHeightInWorld = m_camera.orthographicSize * 2.0f;
        float screenWidthInWorld = screenHeightInWorld * m_camera.aspect;

        // 실제 게임이 렌더링되는 영역의 크기를 계산합니다.
        float viewportWidth = screenWidthInWorld * m_camera.rect.width;
        float viewportHeight = screenHeightInWorld * m_camera.rect.height;

        foreach (var layer in sceneLayers)
        {
            layer.meshRenderer.transform.localScale = new Vector3(viewportWidth, viewportHeight, 1f);
        }
    }
    private void InitializeLayers()
    {
        foreach (var layer in sceneLayers)
        {
            if (layer.meshRenderer == null)
            {
                Debug.LogError("Scene Layers에 할당되지 않은 Mesh Renderer가 있습니다. 이 레이어는 무시됩니다.");
                continue;
            }

            // 각 렌더러에 대한 머티리얼 인스턴스 생성
            layer.materialInstance = layer.meshRenderer.material;
            layer.currentOffset = Vector2.zero;

            if (!layer.materialInstance.HasProperty("_Blend"))
            {
                Debug.LogError($"'{layer.meshRenderer.gameObject.name}'의 머티리얼이 'Custom/Crossfade' 셰이더를 사용하지 않습니다. 배경 전환이 작동하지 않습니다.", layer.meshRenderer.gameObject);
            }
        }
    }

    private void SetInitialBackground()
    {
        _currentBackgroundIndex = startBackgroundIndex;
        if (_currentBackgroundIndex >= backgroundThemes.Count) {
            Debug.LogError("시작 인덱스가 배경 테마 개수보다 큽니다.");
            return;
        }
        var initialTheme = backgroundThemes[_currentBackgroundIndex];

        if (initialTheme.layerTextures.Count != sceneLayers.Count)
        {
            Debug.LogError($"시작 배경 테마 '{initialTheme.name}'의 텍스처 개수({initialTheme.layerTextures.Count})가 씬 레이어 개수({sceneLayers.Count})와 일치하지 않습니다.");
            return;
        }

        for (int i = 0; i < sceneLayers.Count; i++)
        {
            var layer = sceneLayers[i];
            var tex = initialTheme.layerTextures[i] ?? GetPlaceholderTexture();

            if (tex.wrapMode != TextureWrapMode.Repeat)
            {
                 Debug.LogWarning($"텍스처 '{tex.name}'의 Wrap Mode가 'Repeat'이 아닙니다. 스크롤이 제대로 동작하지 않을 수 있습니다.", tex);
            }

            layer.materialInstance.SetTexture("_MainTex", tex);
            layer.materialInstance.SetFloat("_Blend", 0f);
            SetLayerAlpha(layer, 1f); // 전체 알파값 1로 초기화
        }
    }

    private void ScrollLayer(SceneLayer layer)
    {
        if (layer.materialInstance == null) return;

        float scrollAmount = mainScrollSpeed * layer.speedFactor * Time.deltaTime;
        layer.currentOffset.x += scrollAmount;

        if (layer.currentOffset.x > 1.0f) layer.currentOffset.x -= 1.0f;

        // 전환 중에도 두 텍스처가 같은 오프셋으로 움직여야 자연스럽습니다.
        layer.materialInstance.SetTextureOffset("_MainTex", layer.currentOffset);
        layer.materialInstance.SetTextureOffset("_SecondTex", layer.currentOffset);
    }

    private void SetBlend(float value)
    {
        foreach (var layer in sceneLayers)
        {
            if (layer.materialInstance != null)
            {
                layer.materialInstance.SetFloat("_Blend", value);
            }
        }
    }
    
    private void SetLayerAlpha(SceneLayer layer, float alpha)
    {
        if (layer.materialInstance != null && layer.materialInstance.HasProperty("_Color"))
        {
            Color color = layer.materialInstance.GetColor("_Color");
            layer.materialInstance.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
        }
    }

    private static Texture2D GetPlaceholderTexture()
    {
        if (_placeholderTexture != null) return _placeholderTexture;

        _placeholderTexture = new Texture2D(1, 1);
        _placeholderTexture.SetPixel(0, 0, Color.white);
        _placeholderTexture.Apply();
        _placeholderTexture.wrapMode = TextureWrapMode.Repeat;
        _placeholderTexture.name = "Placeholder Texture";
        return _placeholderTexture;
    }
    #endregion
}
