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
    [SerializeField] private float shakeDuration = 0.1f;  // 카메라 쉐이크 지속 시간
    [SerializeField] private float shakeStrength = 0.15f; // 카메라 쉐이크 강도

    private CancellationTokenSource _lifeTimeCts;

    private void OnEnable()
    {
        // 오브젝트가 활성화될 때, 3초 후에 자동으로 풀에 반환되도록 타이머를 시작합니다.
        _lifeTimeCts = new CancellationTokenSource();
        ReturnAfterDelay(3f, _lifeTimeCts.Token).Forget();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어와 충돌 시 즉시 풀로 반환합니다.
        if (other.CompareTag("Player"))
        {
            // 착탄 이펙트 재생
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayBulletHitEffect(transform.position);
            }

            // 명중 사운드 재생
            if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.AKHitSfx))
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.AKHitSfx);
            }

            // 카메라 쉐이크
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ShakeCamera(shakeDuration, shakeStrength);
            }

            // 플레이어에게 데미지 처리
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
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token);
            // 딜레이가 끝날 때까지 취소되지 않았다면 풀로 반환합니다.
            ReturnToPool();
        }
        catch (OperationCanceledException)
        {
            // OnDisable 또는 다른 경로에서 작업이 취소된 경우
        }
    }

    /// <summary>
    /// 이 오브젝트를 오브젝트 풀로 반환합니다.
    /// </summary>
    private void ReturnToPool()
    {
        // 중복 호출을 방지하고, 비활성화 전에 타이머를 취소합니다.
        _lifeTimeCts?.Cancel();

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
        // 오브젝트가 비활성화될 때, 실행 중인 모든 비동기 작업을 확실히 취소합니다.
        _lifeTimeCts?.Cancel();
    }
}