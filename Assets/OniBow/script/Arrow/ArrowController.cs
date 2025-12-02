using UnityEngine;
using DG.Tweening;

/// <summary>
/// 화살의 포물선 이동과 생명 주기를 관리합니다.
/// </summary>
public class ArrowController : MonoBehaviour
{
    public enum ArrowOwner { Player, Enemy }
    public ArrowOwner Owner { get; set; }

    private Tween _moveTween;

    /// <summary>
    /// 2차 베지에 곡선을 따라 포물선 궤적으로 화살을 발사합니다.
    /// </summary>
    /// <param name="startPos">시작 위치</param>
    /// <param name="controlPoint">곡선의 제어점</param>
    /// <param name="endPos">목표 위치</param>
    /// <param name="duration">이동에 걸리는 시간</param>
    public void Launch(Vector3 startPos, Vector3 controlPoint, Vector3 endPos, float duration)
    {
        _moveTween?.Kill();

        float t = 0f;
        Vector3 previousPos = startPos;
        transform.position = startPos;

        _moveTween = DOTween.To(() => t, x =>
        {
            t = x;
            if (this == null || !gameObject.activeInHierarchy) return;

            Vector3 newPos = (1 - t) * (1 - t) * startPos + 2 * (1 - t) * t * controlPoint + t * t * endPos;
            transform.position = newPos;

            if (newPos != previousPos)
            {
                Vector2 dir = (newPos - previousPos).normalized;
                if (dir != Vector2.zero)
                {
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0, 0, angle);
                }
            }
            previousPos = newPos;
        }, 1f, duration)
        .SetEase(Ease.Linear)
        .OnComplete(ReturnToPool);
    }

    /// <summary>
    /// 지정된 방향으로 화살을 직선 발사합니다.
    /// </summary>
    /// <param name="startPos">시작 위치</param>
    /// <param name="direction">발사 방향</param>
    /// <param name="distance">최대 사거리</param>
    /// <param name="duration">이동에 걸리는 시간</param>
    public void LaunchStraight(Vector3 startPos, Vector2 direction, float distance, float duration)
    {
        _moveTween?.Kill();

        transform.position = startPos;
        Vector3 endPos = startPos + (Vector3)direction * distance;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        _moveTween = transform.DOMove(endPos, duration)
            .SetEase(Ease.Linear)
            .OnComplete(ReturnToPool);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((Owner == ArrowOwner.Player && other.CompareTag("Enemy")) || (Owner == ArrowOwner.Enemy && other.CompareTag("Player")))
        {
            ReturnToPool();
        }
    }

    /// <summary>
    /// 이 오브젝트를 오브젝트 풀로 반환합니다.
    /// </summary>
    private void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null && gameObject.activeInHierarchy)
        {
            ObjectPoolManager.Instance.Return(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        _moveTween?.Kill();
    }
}
