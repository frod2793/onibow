using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using OniBow.Managers;

namespace OniBow.FX
{
    /// <summary>
    /// 잔상 '스냅샷'의 생명 주기를 관리합니다.
    /// 자신과 모든 자식 SpriteRenderer들의 투명도를 점차 0으로 만들어 사라지는 효과를 연출합니다.
    /// </summary>
    public class AfterimageSnapshot : MonoBehaviour
    {
        // 이 리스트는 프리팹에 미리 포함된 자식 렌더러들로 채워집니다.
        private readonly List<SpriteRenderer> _partRenderers = new List<SpriteRenderer>();
        private readonly List<Tween> _fadeTweens = new List<Tween>();

        private void Awake()
        {
            // 비활성화된 자식까지 포함하여 모든 파츠 렌더러를 미리 찾아 캐시합니다.
            // 이 방식은 런타임에 GetComponentsInChildren를 호출하는 것을 방지합니다.
            GetComponentsInChildren(true, _partRenderers);
        }

        /// <summary>
        /// 원본 렌더러들의 상태를 복제하여 스냅샷을 활성화하고, 모든 파츠의 사라짐 효과를 시작합니다.
        /// </summary>
        /// <param name="sourceRenderers">복제할 원본 렌더러 리스트</param>
        /// <param name="color">잔상에 적용할 색상</param>
        /// <param name="fadeDuration">사라지는 데 걸리는 시간</param>
        /// <param name="overrideSorting">Sorting Order를 덮어쓸지 여부</param>
        /// <param name="sortingOrderOverride">덮어쓸 Sorting Order 값</param>
        public void Activate(List<SpriteRenderer> sourceRenderers, Color color, float fadeDuration, bool overrideSorting, int sortingOrderOverride)
        {
            // 기존 트윈 정리
            foreach (var tween in _fadeTweens)
            {
                tween?.Kill();
            }
            _fadeTweens.Clear();

            int activeRenderers = 0;
            // 원본 캐릭터의 모든 파츠를 복제하려고 시도합니다.
            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                SpriteRenderer partRenderer;
                // 잔상 프리팹에 준비된 파츠가 부족한 경우, 동적으로 생성합니다.
                if (i >= _partRenderers.Count)
                {
                      var newPartObj = new GameObject($"Part_{i}");
                    newPartObj.transform.SetParent(transform, false);
                    partRenderer = newPartObj.AddComponent<SpriteRenderer>();
                    _partRenderers.Add(partRenderer);
                }
                else
                {
                    partRenderer = _partRenderers[i];
                }

                var sourceRenderer = sourceRenderers[i];

                // 원본 파츠가 활성화 상태일 때만 잔상을 복제합니다.
                if (sourceRenderer.gameObject.activeInHierarchy && sourceRenderer.sprite != null)
                {
                    partRenderer.gameObject.SetActive(true);

                    // 속성 복사
                    partRenderer.sprite = sourceRenderer.sprite;
                    partRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                    partRenderer.sortingOrder = overrideSorting ? sortingOrderOverride : sourceRenderer.sortingOrder - 1;

                    // [개선된 방식]
                    // Matrix 연산을 통해 원본 렌더러의 모든 Transform 속성(위치, 회전, 크기)을
                    // 스냅샷 컨테이너(부모)에 상대적인 로컬 Transform으로 정확하게 변환합니다.
                    Matrix4x4 targetMatrix = transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                    partRenderer.transform.localPosition = targetMatrix.GetColumn(3);
                    partRenderer.transform.localRotation = targetMatrix.rotation;
                    
                    // [수정] lossyScale 대신, 행렬의 각 축(column) 벡터의 크기(magnitude)를 직접 계산하여 정확한 스케일 값을 추출합니다.
                    // 이 방식은 복잡한 계층 구조에서도 스케일 왜곡 없이 1:1 비율을 보장합니다.
                    partRenderer.transform.localScale = new Vector3(
                        targetMatrix.GetColumn(0).magnitude,
                        targetMatrix.GetColumn(1).magnitude,
                        targetMatrix.GetColumn(2).magnitude
                    );

                    // 시작 색상 및 투명도 설정 후 페이드 아웃 트윈 시작
                    partRenderer.color = new Color(color.r, color.g, color.b, 1f);
                    Tween fade = partRenderer.DOFade(0, fadeDuration).SetEase(Ease.InQuad);
                    _fadeTweens.Add(fade);
                    activeRenderers++;
                }
                else
                {
                    // 원본 파츠가 비활성이면, 잔상 파츠도 비활성화합니다.
                    partRenderer.gameObject.SetActive(false);
                }
            }

            // 복제하고 남은 잔상 프리팹의 파츠들을 비활성화합니다.
            for (int i = sourceRenderers.Count; i < _partRenderers.Count; i++)
            {
                _partRenderers[i].gameObject.SetActive(false);
            }

            // 활성화된 렌더러가 하나라도 있으면, 마지막 트윈 완료 시 풀로 반환하도록 설정합니다.
            if (activeRenderers > 0 && _fadeTweens.Count > 0)
            {
                // OnComplete은 마지막 트윈에만 연결하여 중복 호출을 방지합니다.
                _fadeTweens[_fadeTweens.Count - 1].OnComplete(ReturnToPool);
            }
            else
            {
                // 활성화된 렌더러가 없으면 즉시 풀로 반환합니다.
                // UniTask.NextFrame()을 사용하여 현재 프레임의 로직이 모두 끝난 후 반환하도록 합니다.
                // 이는 Get -> Activate -> Return이 한 프레임에 일어날 때 발생할 수 있는 문제를 방지합니다.
                UniTask.NextFrame().ContinueWith(ReturnToPool).Forget();
            }
        }

        private void ReturnToPool()
        {
            if (ObjectPoolManager.Instance != null)
            {
                // 중복 반환을 막기 위해 오브젝트가 아직 활성 상태일 때만 반환합니다.
                if(gameObject.activeInHierarchy)
                    ObjectPoolManager.Instance.Return(gameObject);
            }
            else // 풀 매니저가 없다면(씬 종료 등) 오브젝트를 파괴하여 메모리 누수를 방지합니다.
            {
                Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            // 비활성화될 때(풀에 반환될 때) 모든 트윈을 확실히 정리합니다.
            foreach (var tween in _fadeTweens)
            {
                tween?.Kill();
            }
            _fadeTweens.Clear();
        }
    }
}