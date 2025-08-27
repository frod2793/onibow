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
    /// 지정된 궤적을 따라 화살을 발사합니다. (포물선)
    /// </summary>
    public void Launch(Vector3 startPos, Vector3 controlPoint, Vector3 endPos, float duration, AnimationCurve easeCurve)
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
        .SetEase(easeCurve)
        .OnComplete(ReturnToPool);
    }

    /// <summary>
    /// 지정된 방향으로 화살을 직선 발사합니다. (스킬용)
    /// </summary>
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
        // 플레이어의 화살이 적과 충돌했거나, 적의 화살이 플레이어와 충돌했을 때 풀로 반환
        if ((CompareTag("Untagged") && other.CompareTag("Enemy")) || (CompareTag("EnemyArrow") && other.CompareTag("Player")))
        {
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        if (gameObject == null) return;

        // 자신의 태그에 따라 올바른 풀로 돌아감
        if (CompareTag("EnemyArrow"))
        {
            if (EnemyArrowPool.Instance != null) EnemyArrowPool.Instance.Return(gameObject);
        }
        else
        {
            if (ArrowPool.Instance != null) ArrowPool.Instance.Return(gameObject);
        }
    }

    private void OnDisable()
    {
        _moveTween?.Kill();
    }
}
