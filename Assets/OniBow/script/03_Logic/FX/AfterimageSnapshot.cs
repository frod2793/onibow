using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using OniBow.Managers;
using System.Threading;

namespace OniBow.FX
{
    /// <summary>
    /// [설명]: 단일 잔상 스냅샷의 외형 변환 및 페이드 아웃 생명 주기를 담당합니다.
    /// 풀링 시스템과 연계되어 메모리 누수 없이 작동합니다.
    /// </summary>
    public class AfterimageSnapshot : MonoBehaviour
    {
        #region 내부 필드
        private readonly List<SpriteRenderer> m_partRenderers = new List<SpriteRenderer>(20);
        #endregion

        #region 유니티 생명주기
        private void Awake()
        {
            GetComponentsInChildren(true, m_partRenderers);
        }

        private void OnDisable()
        {
            // 오브젝트 비활성화(풀 반환) 시 모든 트윈(해당 오브젝트 소속)을 정리하여 예기치 않은 동작 방지
            DOTween.Kill(transform);
        }
        #endregion

        #region 공개 메서드
        /// <summary>
        /// [설명]: 원본 캐릭터의 렌더링 상태를 읽어와 자신을 복제하고, 페이드 아웃을 시작합니다.
        /// </summary>
        public async UniTaskVoid ActivateAsync(List<SpriteRenderer> sourceRenderers, Color color, float fadeDuration, bool overrideSorting, int sortingOrderOverride, CancellationToken token)
        {
            DOTween.Kill(transform); // 기존 트윈 초기화

            int activeRenderers = 0;

            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                SpriteRenderer partRenderer;

                // [최적화]: 초기화(Activate) 중 파츠가 부족하면 늘리되, 풀에 한 번 캐시된 이후로는 할당(Allocation) 최소화
                if (i >= m_partRenderers.Count)
                {
                    var newPartObj = new GameObject($"Part_{i}");
                    newPartObj.transform.SetParent(transform, false);
                    partRenderer = newPartObj.AddComponent<SpriteRenderer>();
                    m_partRenderers.Add(partRenderer);
                }
                else
                {
                    partRenderer = m_partRenderers[i];
                }

                var sourceRenderer = sourceRenderers[i];

                if (sourceRenderer.gameObject.activeInHierarchy && sourceRenderer.sprite != null)
                {
                    partRenderer.gameObject.SetActive(true);

                    partRenderer.sprite = sourceRenderer.sprite;
                    partRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                    partRenderer.sortingOrder = overrideSorting ? sortingOrderOverride : sourceRenderer.sortingOrder - 1;

                    // Matrix 렌더 위치 지정
                    Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                    partRenderer.transform.localPosition = targetMatrix.GetColumn(3);
                    partRenderer.transform.localRotation = targetMatrix.rotation;
                    
                    partRenderer.transform.localScale = new Vector3(
                        targetMatrix.GetColumn(0).magnitude,
                        targetMatrix.GetColumn(1).magnitude,
                        targetMatrix.GetColumn(2).magnitude
                    );

                    partRenderer.color = new Color(color.r, color.g, color.b, 1f);
                    
                    // transform.DOFade 확장 메서드 대신 스프라이트 색상을 조절, 이 객체를 타겟ID로 식별
                    partRenderer.DOFade(0, fadeDuration)
                                .SetEase(Ease.InQuad)
                                .SetId(transform); 

                    activeRenderers++;
                }
                else
                {
                    partRenderer.gameObject.SetActive(false);
                }
            }

            // 안 쓰는 남은 파츠 제거
            for (int i = sourceRenderers.Count; i < m_partRenderers.Count; i++)
            {
                m_partRenderers[i].gameObject.SetActive(false);
            }

            // [최적화]: 콜백 지옥이나 불필요한 Delegate 할당 대신, 비동기 딜레이 대기로 안전한 풀 반환 제어
            try
            {
                // 페이드 애니메이션 동작 시간 동안 비동기로 대기
                if (activeRenderers > 0)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(fadeDuration), cancellationToken: token);
                }
                else
                {
                    await UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, token);
                }
            }
            catch (OperationCanceledException) { /* 씬 종료 등 무시 */ }
            finally
            {
                ReturnToPool();
            }
        }
        #endregion

        #region 내부 로직
        private void ReturnToPool()
        {
            if (ObjectPoolManager.Instance != null && gameObject.activeInHierarchy)
            {
                ObjectPoolManager.Instance.Return(gameObject);
            }
            else if (ObjectPoolManager.Instance == null)
            {
                Destroy(gameObject);
            }
        }
        #endregion
    }
}
