using UnityEngine;
using DG.Tweening;

/// <summary>
/// 화살의 포물선 이동과 생명 주기를 관리합니다.
/// Enemy 또는 Player가 이 스크립트의 Launch 메서드를 호출하여 발사합니다.
/// </summary>
public class ArrowController : MonoBehaviour
{
    private Tween _moveTween;

    /// <summary>
    /// 지정된 궤적을 따라 화살을 발사합니다.
    /// </summary>
    /// <param name="startPos">시작 위치</param>
    /// <param name="controlPoint">베지어 곡선의 제어점</param>
    /// <param name="endPos">종료 위치</param>
    /// <param name="duration">도달 시간</param>
    /// <param name="easeCurve">움직임의 Easing 커브</param>
    public void Launch(Vector3 startPos, Vector3 controlPoint, Vector3 endPos, float duration, AnimationCurve easeCurve)
    {
        // 이전 트윈이 있다면 안전하게 종료
        _moveTween?.Kill();

        float t = 0f;
        Vector3 previousPos = startPos;
        transform.position = startPos;

        _moveTween = DOTween.To(() => t, x =>
        {
            t = x;
            // 화살이 비활성화되었다면 트윈을 중단
            if (this == null || !gameObject.activeInHierarchy) return;

            // 2차 베지어 곡선 공식으로 위치 계산
            float oneMinusT = 1f - t;
            Vector3 newPos = oneMinusT * oneMinusT * startPos +
                             2f * oneMinusT * t * controlPoint +
                             t * t * endPos;
            transform.position = newPos;

            // 이동 방향으로 화살 회전
            if (newPos != previousPos)
            {
                Vector2 dir = (newPos - previousPos).normalized;
                if (dir != Vector2.zero) // 0으로 나누는 것을 방지
                {
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0, 0, angle);
                }
            }
            previousPos = newPos;
        }, 1f, duration)
        .SetEase(easeCurve)
        .OnComplete(() =>
        {
            // [최적화] 트윈 완료 시 오브젝트를 파괴하는 대신 풀에 반환합니다.
            if (gameObject != null && ArrowPool.Instance != null)
            {
                ArrowPool.Instance.Return(gameObject);
            }
        });
    }

    private void OnDisable()
    {
        // 오브젝트가 비활성화될 때(풀에 반환될 때) 트윈도 확실히 정리
        _moveTween?.Kill();
    }
}