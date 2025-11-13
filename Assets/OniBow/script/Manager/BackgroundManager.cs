using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

public class BackgroundManager  : MonoBehaviour
{
    #region 직렬화 가능 클래스
    [System.Serializable]
    public class SceneLayer
    {
        public MeshRenderer meshRenderer;
        public float speedFactor;

        [HideInInspector] public Material materialInstance;
        [HideInInspector] public Vector2 currentOffset;
    }

    [System.Serializable]
    public class BackgroundTheme
    {
        public string name;
        public List<Texture> layerTextures = new List<Texture>();
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
    private bool m_isSwitching = false;

    // 셰이더 프로퍼티 ID를 캐싱하여 성능을 향상시킵니다.
    private int m_mainTexId;
    private int m_secondTexId;
    private int m_blendId;
    private int m_colorId;
    #endregion

    #region Unity 생명주기 메서드
    private void Awake()
    {
        InitializeShaderPropertyIDs();
    }

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
        foreach (var layer in sceneLayers)
        {
            ScrollLayer(layer);
        }
    }
    #endregion

    #region 공개 메서드
    public void SwitchBackground(int newIndex)
    {
        if (newIndex < 0 || newIndex >= backgroundThemes.Count || newIndex == _currentBackgroundIndex || m_isSwitching)
        {
            return;
        }

        m_isSwitching = true;
        var newTheme = backgroundThemes[newIndex];

        if (newTheme.layerTextures.Count != sceneLayers.Count)
        {
            Debug.LogError($"배경 테마 '{newTheme.name}'의 텍스처 개수({newTheme.layerTextures.Count})가 " +
                           $"씬 레이어 개수({sceneLayers.Count})와 일치하지 않습니다.");
            m_isSwitching = false;
            return;
        }

        for (int i = 0; i < sceneLayers.Count; i++)
        {
            var layer = sceneLayers[i];
            var newTex = newTheme.layerTextures[i] ?? GetPlaceholderTexture();
            layer.materialInstance.SetTexture(m_secondTexId, newTex);
        }

        DOTween.To(() => 0f, value => SetBlend(value), 1f, fadeDuration)
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                for (int i = 0; i < sceneLayers.Count; i++)
                {
                    var layer = sceneLayers[i];
                    var completedTheme = backgroundThemes[newIndex];
                    var newTex = completedTheme.layerTextures[i] ?? GetPlaceholderTexture(); // null 체크 강화
                    layer.materialInstance.SetTexture(m_mainTexId, newTex);
                }
                SetBlend(0f);
                _currentBackgroundIndex = newIndex;
                m_isSwitching = false;
            });
    }
    #endregion

    #region 비공개 메서드
    /// <summary>
    /// 화면 해상도와 카메라 뷰포트에 맞춰 모든 배경 레이어의 크기를 조절합니다.
    /// Pixel Perfect Camera 및 ScreenResolutionManager에 의해 조정된 최종 뷰포트를 기준으로 크기를 계산하여 안정성을 높입니다.
    /// </summary>
    private void ResizeBackgroundsToFitScreen()
    {
        if (m_camera == null)
        {
            Debug.LogError("카메라가 할당되지 않아 배경 크기를 조절할 수 없습니다.", this);
            return;
        }
    
        Vector3 bottomLeft = m_camera.ViewportToWorldPoint(new Vector3(0, 0, m_camera.nearClipPlane));
        Vector3 topRight = m_camera.ViewportToWorldPoint(new Vector3(1, 1, m_camera.nearClipPlane));
    
        float worldSpaceWidth = topRight.x - bottomLeft.x;
        float worldSpaceHeight = topRight.y - bottomLeft.y;
    
        foreach (var layer in sceneLayers)
        {
            if (layer.meshRenderer != null)
            {
                layer.meshRenderer.transform.localScale = new Vector3(worldSpaceWidth, worldSpaceHeight, 1f);
            }
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

            layer.materialInstance = layer.meshRenderer.material;
            layer.currentOffset = Vector2.zero;

            if (!layer.materialInstance.HasProperty(m_blendId))
            {
                Debug.LogError($"'{layer.meshRenderer.gameObject.name}'의 머티리얼이 'Custom/Crossfade' 셰이더를 사용하지 않습니다. 배경 전환이 작동하지 않습니다.", layer.meshRenderer.gameObject);
            }
        }
    }
    
    /// <summary>
    /// 성능 최적화를 위해 셰이더 프로퍼티 ID를 미리 캐싱합니다.
    /// </summary>
    private void InitializeShaderPropertyIDs()
    {
        m_mainTexId = Shader.PropertyToID("_MainTex");
        m_secondTexId = Shader.PropertyToID("_SecondTex");
        m_blendId = Shader.PropertyToID("_Blend");
        m_colorId = Shader.PropertyToID("_Color");
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
            Debug.LogError($"시작 배경 테마 '{initialTheme.name}'의 텍스처 개수({initialTheme.layerTextures.Count})가 " +
                           $"씬 레이어 개수({sceneLayers.Count})와 일치하지 않습니다.");
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

            layer.materialInstance.SetTexture(m_mainTexId, tex);
            layer.materialInstance.SetFloat(m_blendId, 0f);
            SetLayerAlpha(layer, 1f);
        }
    }

    private void ScrollLayer(SceneLayer layer)
    {
        if (layer.materialInstance == null) return;

        float scrollAmount = mainScrollSpeed * layer.speedFactor * Time.deltaTime;
        layer.currentOffset.x += scrollAmount;

        if (layer.currentOffset.x > 1.0f) layer.currentOffset.x -= 1.0f;

        layer.materialInstance.SetTextureOffset(m_mainTexId, layer.currentOffset);
        layer.materialInstance.SetTextureOffset(m_secondTexId, layer.currentOffset);
    }

    private void SetBlend(float value)
    {
        foreach (var layer in sceneLayers)
        {
            if (layer.materialInstance != null)
            {
                layer.materialInstance.SetFloat(m_blendId, value);
            }
        }
    }
    
    private void SetLayerAlpha(SceneLayer layer, float alpha)
    {
        if (layer.materialInstance != null && layer.materialInstance.HasProperty(m_colorId))
        {
            Color color = layer.materialInstance.GetColor(m_colorId);
            layer.materialInstance.SetColor(m_colorId, new Color(color.r, color.g, color.b, alpha));
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
