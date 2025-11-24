using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 잔상 '스냅샷'의 생명 주기를 관리합니다.
/// 단일 Tween을 사용하여 모든 자식 렌더러의 투명도를 효율적으로 제어합니다.
/// </summary>
public class AfterimageSnapshot : MonoBehaviour
{
    #region Private Fields
    // 풀링된 스프라이트 렌더러 파츠 목록
    private readonly List<SpriteRenderer> m_partRenderers = new List<SpriteRenderer>();
    
    // 현재 활성화된 파츠의 개수 (전체 리스트 순회 방지용)
    private int m_activePartCount = 0;
    
    // 알파값 제어를 위한 단일 Tween
    private Tween m_fadeTween;
    
    // 초기 색상 (알파값 계산을 위해 캐싱)
    private Color m_baseColor;
    #endregion

    #region MonoBehaviour Flow
    private void Awake()
    {
        GetComponentsInChildren(true, m_partRenderers);
    }

    private void OnDisable()
    {
        // 오브젝트가 비활성화되면(풀 반환 등) 트윈을 즉시 정리합니다.
        m_fadeTween?.Kill();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// 원본 렌더러들의 상태를 복제하여 스냅샷을 활성화합니다.
    /// </summary>
    public void Activate(List<SpriteRenderer> sourceRenderers, Color color, float fadeDuration, bool overrideSorting, int sortingOrderOverride)
    {
        // 이전 트윈이 혹시 남아있다면 제거
        m_fadeTween?.Kill();
        
        m_baseColor = color;
        m_activePartCount = 0;

        // 원본과 잔상 파츠 매칭
        int sourceCount = sourceRenderers.Count;
        
        // 1. 필요한 파츠 확보 (부족하면 생성 - 런타임 부하가 있지만 필수적인 방어 코드)
        EnsurePartCapacity(sourceCount);

        // 2. 속성 복사 (Copy Properties)
        // 불필요한 transform 접근을 줄이기 위해 캐싱된 transform 사용
        Transform myTransform = transform;
        Matrix4x4 myWorldToLocal = myTransform.worldToLocalMatrix;

        for (int i = 0; i < sourceCount; i++)
        {
            SpriteRenderer source = sourceRenderers[i];

            // 유효하지 않거나 비활성화된 소스는 건너뜀
            if (source == null || !source.gameObject.activeInHierarchy || source.sprite == null)
            {
                m_partRenderers[i].gameObject.SetActive(false);
                continue;
            }

            SpriteRenderer target = m_partRenderers[i];
            target.gameObject.SetActive(true);
            m_activePartCount++;

            // 렌더링 속성 복사
            target.sprite = source.sprite;
            target.color = m_baseColor; // 알파값은 1로 시작
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = overrideSorting ? sortingOrderOverride : source.sortingOrder - 1;
            
            Matrix4x4 targetMatrix = myWorldToLocal * source.transform.localToWorldMatrix;
            
            Transform targetTransform = target.transform;
            targetTransform.localPosition = targetMatrix.GetColumn(3);
            targetTransform.localRotation = targetMatrix.rotation;
            targetTransform.localScale = targetMatrix.lossyScale;
        }

        // 3. 사용하지 않는 나머지 파츠 비활성화
        for (int i = sourceCount; i < m_partRenderers.Count; i++)
        {
            if (m_partRenderers[i].gameObject.activeSelf)
                m_partRenderers[i].gameObject.SetActive(false);
        }

        // 4. 단일 트윈 시작 (Batch Tweening)
        // 활성화된 파츠가 하나라도 있을 때만 트윈 실행
        if (m_activePartCount > 0)
        {
            m_fadeTween = DOVirtual.Float(1f, 0f, fadeDuration, OnUpdateAlpha)
                .SetEase(Ease.InQuad)
                .OnComplete(ReturnToPool);
        }
        else
        {
            ReturnToPool();
        }
    }
    #endregion

    #region Internal Logic
    /// <summary>
    /// Tween의 Update 콜백입니다. 모든 활성 파츠의 투명도를 조절합니다.
    /// </summary>
    /// <param name="alpha">현재 알파 값 (0.0 ~ 1.0)</param>
    private void OnUpdateAlpha(float alpha)
    {
        // 현재 사용 중인 파츠까지만 순회하여 성능 최적화
        // Color 구조체 생성을 최소화하기 위해 r,g,b는 미리 캐싱된 값을 사용
        Color newColor = new Color(m_baseColor.r, m_baseColor.g, m_baseColor.b, alpha);
        
        for (int i = 0; i < m_activePartCount; i++)
        {
            // Activate 단계에서 활성화된 파츠만 앞쪽에 배치되므로 
            // activeSelf 체크 없이 순회 가능하나, 안전을 위해 추가할 수도 있음.
            // 여기서는 Activate 로직을 신뢰하고 바로 접근.
            if (m_partRenderers[i].gameObject.activeSelf)
            {
                m_partRenderers[i].color = newColor;
            }
        }
    }

    /// <summary>
    /// 잔상 파츠 리스트의 용량을 확보합니다. 부족할 경우 새로 생성합니다.
    /// </summary>
    private void EnsurePartCapacity(int requiredCount)
    {
        int currentCount = m_partRenderers.Count;
        if (requiredCount <= currentCount) return;

        for (int i = currentCount; i < requiredCount; i++)
        {
            GameObject newPartObj = new GameObject($"Part_{i}");
            newPartObj.transform.SetParent(transform, false);
            
            SpriteRenderer renderer = newPartObj.AddComponent<SpriteRenderer>();
            m_partRenderers.Add(renderer);
        }
    }

    private void ReturnToPool()
    {
        m_fadeTween?.Kill(); // 안전 장치

        if (ObjectPoolManager.Instance != null)
        {
            if (gameObject.activeInHierarchy)
            {
                ObjectPoolManager.Instance.Return(gameObject);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion
}