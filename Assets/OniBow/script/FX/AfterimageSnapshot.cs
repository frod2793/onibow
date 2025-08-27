using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 잔상 '스냅샷'의 생명 주기를 관리합니다.
/// 자신과 모든 자식 SpriteRenderer들의 투명도를 점차 0으로 만들어 사라지는 효과를 연출합니다.
/// </summary>
public class AfterimageSnapshot : MonoBehaviour
{
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
    private readonly List<Tween> _fadeTweens = new List<Tween>();

    /// <summary>
    /// 스냅샷을 활성화하고 모든 파츠의 사라짐 효과를 시작합니다.
    /// </summary>
    /// <param name="fadeDuration">사라지는 데 걸리는 시간</param>
    public void Activate(float fadeDuration)
    {
        // 기존 트윈 정리
        foreach (var tween in _fadeTweens)
        {
            tween?.Kill();
        }
        _fadeTweens.Clear();
        
        // 자신과 모든 자식의 SpriteRenderer를 찾습니다.
        GetComponentsInChildren(true, _renderers);

        if (_renderers.Count == 0)
        {
            // 렌더러가 없으면 바로 풀로 반환
            ReturnToPool();
            return;
        }

        // 모든 렌더러의 fade-out 트윈을 시작합니다.
        foreach (var sr in _renderers)
        {
            // 시작 투명도를 1로 강제하여 항상 보이게 함
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f); 
            Tween fade = sr.DOFade(0, fadeDuration).SetEase(Ease.InQuad);
            _fadeTweens.Add(fade);
        }

        // 마지막 트윈이 완료되면 풀로 돌아가도록 예약합니다.
        if (_fadeTweens.Count > 0)
        {
            _fadeTweens[_fadeTweens.Count - 1].OnComplete(ReturnToPool);
        }
    }

    private void ReturnToPool()
    {
        if (AfterimagePool.Instance != null) AfterimagePool.Instance.Return(gameObject);
        else Destroy(gameObject);
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