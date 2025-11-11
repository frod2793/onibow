using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

/// <summary>
/// AK 총알의 생명주기와 충돌을 관리합니다.
/// 일정 시간이 지나거나 플레이어와 충돌하면 자동으로 오브젝트 풀로 돌아갑니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AKBullet : MonoBehaviour
{
    [SerializeField] private int damage = 5; // 총알 데미지
    [Header("카메라 쉐이크")]
    [SerializeField] private float shakeDuration = 0.1f;
    [SerializeField] private float shakeStrength = 0.15f;
    private CancellationTokenSource _lifeTimeCts;
    private void OnEnable()
    {
        _lifeTimeCts = new CancellationTokenSource();
        ReturnAfterDelay(3f, _lifeTimeCts.Token).Forget();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayBulletHitEffect(transform.position);
            }
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.AKHitSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.AKHitSfx);
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ShakeCamera(shakeDuration, shakeStrength);
            }
            if (other.TryGetComponent<PlayerControl>(out var player))
            {
                player.TakeDamage(damage);
            }
            ReturnToPool();
        }
    }
    /// <summary>
    /// 지정된 시간 후에 오브젝트를 풀로 반환하는 비동기 메서드입니다.
    /// </summary>
    private async UniTaskVoid ReturnAfterDelay(float delay, CancellationToken token)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token); // 딜레이가 끝날 때까지 취소되지 않았다면 풀로 반환합니다.
            ReturnToPool();
        }
        catch (OperationCanceledException)
        {
        }
    }
    /// <summary>
    /// 이 오브젝트를 오브젝트 풀로 반환합니다.
    /// </summary>
    private void ReturnToPool()
    {
        _lifeTimeCts?.Cancel();
        if (ObjectPoolManager.Instance != null)
        {
            if(gameObject.activeInHierarchy)
                ObjectPoolManager.Instance.Return(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void OnDisable()
    {
        _lifeTimeCts?.Cancel();
    }
}