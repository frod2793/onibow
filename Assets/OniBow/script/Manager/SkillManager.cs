using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System.Threading;

/// <summary>
/// 플레이어와 적이 사용할 스킬을 관리하는 클래스입니다.
/// </summary>
public class SkillManager : MonoBehaviour
{
    #region 싱글턴 패턴
    public static SkillManager Instance { get; private set; }
    #endregion

    #region 플레이어 스킬 변수

    [Header("플레이어 스킬 쿨타임")]
    [SerializeField] private float playerSkill1_Cooldown = 10f; //배리어
    [SerializeField] private float playerSkill2_Cooldown = 15f;  // 힐
    [SerializeField] private float playerSkill3_Cooldown = 15f; // 호밍 미사일 
    [SerializeField] private float playerSkill4_Cooldown = 20f;// 바주카
  
    [Header("플레이어 스킬 설정")]
    [SerializeField] private GameObject barrierEffectPrefab; // 배리어 이펙트 프리팹
    [SerializeField] private float barrierDuration = 5f; // 배리어 지속 시간
    [SerializeField] private GameObject homingMissilePrefab; // 추적 미사일 프리팹
    [SerializeField] private GameObject explosiveArrowPrefab; // 폭발탄 프리팹
    [SerializeField] private int homingMissileCount = 5; // 추적 미사일 발사 개수
    [SerializeField] private float homingMissileSpawnInterval = 0.1f; // 추적 미사일 발사 간격

    [Header("플레이어 참조 컴포넌트")]
    [SerializeField] private PlayerControl playerControl; // 플레이어 컨트롤러
    [SerializeField] private Transform playerFirePoint;   // 플레이어 발사 위치

    [Header("스킬무기 가 장착될 위치")] [SerializeField]
    private GameObject playerHand;
    
    [Header("스킬 무기")] [SerializeField] private GameObject BazookaPrefab;
    [SerializeField] private GameObject AK47;// 적이 6연발 할떄 사용할무기 
    
    
    
    // 각 스킬의 마지막 사용 시간
    private float _lastSkill1_Time = -999f;
    private float _lastSkill2_Time = -999f;
    private float _lastSkill3_Time = -999f;
    private float _lastSkill4_Time = -999f;

    // 외부에서 남은 쿨타임을 확인할 수 있는 프로퍼티
    public float Skill1_RemainingCooldown => Mathf.Max(0f, _lastSkill1_Time + playerSkill1_Cooldown - Time.time);
    public float Skill2_RemainingCooldown => Mathf.Max(0f, _lastSkill2_Time + playerSkill2_Cooldown - Time.time);
    public float Skill3_RemainingCooldown => Mathf.Max(0f, _lastSkill3_Time + playerSkill3_Cooldown - Time.time);
    public float Skill4_RemainingCooldown => Mathf.Max(0f, _lastSkill4_Time + playerSkill4_Cooldown - Time.time);

    #endregion

    #region 적 스킬 변수

    [Header("적 스킬 설정")]
    [Tooltip("적이 다발 사격 시 사용할 총알 프리팹")]
    [SerializeField]
    private GameObject akBulletPrefab;
    [SerializeField] private int enemyMultiShot_Count = 5; // 5연발
    [SerializeField] private float enemyMultiShot_Interval = 0.15f; // 발사 간격
    [SerializeField] private float akBulletSpeed = 30f; // AK 총알 속도

    #endregion

    #region 적 스킬 실행 (외부 호출용)

    /// <summary>
    /// 적 스킬: 지정된 대상을 향해 다발 사격을 가합니다.
    /// 이 메서드는 쿨타임을 관리하지 않으므로, 호출하는 쪽(Enemy.cs)에서 관리해야 합니다.
    /// </summary>
    public async UniTask ExecuteEnemyMultiShot(Transform handPoint, Transform target)
    {
        if (AK47 == null || handPoint == null || ObjectPoolManager.Instance == null || target == null)
        {
            Debug.LogWarning("적 다발 사격 스킬의 설정이 올바르지 않습니다. (AK47, handPoint, ObjectPoolManager, target)");
            return;
        }

        await EnemyAKSkillAsync(handPoint, target, this.GetCancellationTokenOnDestroy());
    }

    #endregion

    #region Unity 생명주기
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region 플레이어 스킬 (UI에서 호출)

    /// <summary>
    /// 스킬 1: 배리어 사용
    /// </summary>
    public void UseSkill1()
    {
        if (Skill1_RemainingCooldown > 0) return; // 쿨타임 체크
        _lastSkill1_Time = Time.time;
        Debug.Log("스킬 1: 배리어 사용!");
        PlayerSkill1_BarrierAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 2: 힐 사용
    /// </summary>
    public void UseSkill2()
    {
        if (Skill2_RemainingCooldown > 0) return; // 쿨타임 체크
        if (playerControl == null) return;

        _lastSkill2_Time = Time.time;
        Debug.Log("스킬 2: 힐 사용!");
        // 새로운 비동기 힐 로직 호출
        PlayerSkill2_HealAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 3: 추적 미사일 사용
    /// </summary>
    public void UseSkill3()
    {
        if (Skill3_RemainingCooldown > 0) return;
        if (playerControl == null || homingMissilePrefab == null || playerHand == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null)
        {
            Debug.Log("추적할 대상이 없습니다.");
            return;
        }

        _lastSkill3_Time = Time.time;
        Debug.Log("스킬 3: 추적 미사일 사용!");
        PlayerSkill3_HomingMissilesAsync(target.transform, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 4: 바주카 사용
    /// </summary>
    public void UseSkill4()
    {
        if (Skill4_RemainingCooldown > 0) return;
        if (playerControl == null || explosiveArrowPrefab == null || BazookaPrefab == null || playerHand == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null)
        {
            Debug.Log("조준할 대상이 없어 스킬을 사용하지 않습니다.");
            return;
        }

        _lastSkill4_Time = Time.time;
        Debug.Log("스킬 4: 폭발탄(바주카) 사용!");

        ExecuteBazookaSkillAsync(playerFirePoint, playerHand, target.transform, this.GetCancellationTokenOnDestroy()).Forget();
    }

    #endregion

    #region 스킬 실행 로직 (공용/내부)

    /// <summary>
    /// 플레이어 스킬 1: 배리어를 생성합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill1_BarrierAsync(CancellationToken token)
    {
        if (playerControl == null || barrierEffectPrefab == null) return;

        playerControl.SetSkillUsageState(true);
        GameObject barrierInstance = null;
        try
        {
            // 배리어 이펙트 생성
            barrierInstance = Instantiate(barrierEffectPrefab, playerControl.transform.position, Quaternion.identity, playerControl.transform);
            if (barrierInstance != null) barrierInstance.SetActive(true); // 프리팹이 비활성화 상태일 경우를 대비해 명시적으로 활성화합니다.
            
            // 플레이어를 무적 상태로 만듭니다.
            playerControl.SetInvulnerable(true);

            // 지정된 시간 동안 대기
            await UniTask.Delay(TimeSpan.FromSeconds(barrierDuration), cancellationToken: token);
        }
        catch (OperationCanceledException) { /* 스킬이 중간에 취소된 경우 */ }
        finally
        {
            // 배리어 이펙트 파괴 및 상태 복구
            if (barrierInstance != null) Destroy(barrierInstance);
            playerControl.SetInvulnerable(false); // 무적 상태 해제
            playerControl.SetSkillUsageState(false);
        }
    }

    /// <summary>
    /// 플레이어 스킬 2: 힐 스킬의 비동기 실행 로직입니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill2_HealAsync(CancellationToken token)
    {
        if (playerControl == null) return;

        playerControl.SetSkillUsageState(true);
        GameObject healEffectInstance = null;
        
        try
        {
            // 1. 예비 체력이 본체력보다 높을 경우, 즉시 예비 체력까지 회복합니다.
            playerControl.HealWithTempHp();

            // 2. 힐 이펙트를 생성하고 컬러 틴트를 적용합니다.
            if (EffectManager.Instance != null && EffectManager.Instance.HealEffectPrefab != null)
            {
                // 이펙트를 플레이어의 자식으로 생성하여 따라다니게 합니다.
                healEffectInstance = Instantiate(EffectManager.Instance.HealEffectPrefab, playerControl.transform.position, Quaternion.identity, playerControl.transform);
                if (healEffectInstance != null) healEffectInstance.SetActive(true); // 프리팹이 비활성화 상태일 경우를 대비해 명시적으로 활성화합니다.

                // 힐 이펙트가 플레이어 위에 렌더링되도록 Sorting Order를 조정합니다.
                var spum = playerControl.GetComponentInChildren<SPUM_Prefabs>();
                if (spum != null && spum._anim != null)
                {
                    int maxPlayerSortingOrder = 0;
                    // 플레이어의 모든 렌더러를 찾아 가장 높은 Sorting Order를 구합니다.
                    foreach (var playerRenderer in spum._anim.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (playerRenderer.sortingOrder > maxPlayerSortingOrder)
                        {
                            maxPlayerSortingOrder = playerRenderer.sortingOrder;
                        }
                    }

                    // 힐 이펙트의 모든 렌더러를 찾아 플레이어보다 높은 Sorting Order를 부여합니다.
                    foreach (var effectRenderer in healEffectInstance.GetComponentsInChildren<Renderer>())
                    {
                        effectRenderer.sortingOrder = maxPlayerSortingOrder + 1;
                    }
                }
            }
            // 3초간 초록색 틴트 적용 (이 작업은 힐과 동시에 진행됩니다)
            ApplyPlayerTintAsync(new Color(0.7f, 1f, 0.7f, 1f), 3f, token).Forget();

            // 3. 3초에 걸쳐 체력의 30%를 서서히 회복합니다.
            float healAmount = playerControl.GetMaxHp() * 0.3f;
            await playerControl.GradualHeal(healAmount, 3f, token);
        }
        catch (OperationCanceledException) { /* 스킬이 중간에 취소된 경우 */ }
        finally
        {
            // 4. 이펙트 파괴 및 상태 복구
            if (healEffectInstance != null) Destroy(healEffectInstance);
            // 컬러 틴트는 ApplyPlayerTintAsync 내부에서 자동으로 복구됩니다.
            playerControl.SetSkillUsageState(false);
        }
    }

    private async UniTaskVoid ApplyPlayerTintAsync(Color tintColor, float duration, CancellationToken token)
    {
        if (playerControl == null) return;
        var spum = playerControl.GetComponentInChildren<SPUM_Prefabs>();
        if (spum == null || spum._anim == null) return;

        var renderers = spum._anim.GetComponentsInChildren<SpriteRenderer>();
        var originalColors = new Dictionary<SpriteRenderer, Color>();

        try
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    originalColors[renderer] = renderer.color;
                    renderer.color = tintColor;
                }
            }
            await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
        }
        finally
        {
            foreach (var kvp in originalColors)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.color = kvp.Value;
                }
            }
        }
    }

    /// <summary>
    /// 플레이어 스킬 3: 추적 미사일을 비동기적으로 발사합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill3_HomingMissilesAsync(Transform target, CancellationToken token)
    {
        if (playerControl == null) return;
        playerControl.SetSkillUsageState(true); // 스킬 사용 시작
        try
        {
            for (int i = 0; i < homingMissileCount; i++)
            {
                if (token.IsCancellationRequested || target == null) break;

                // 약간의 랜덤한 위치에서 발사하여 겹치지 않게 함
                Vector3 spawnPosition = playerHand.transform.position + (Vector3)UnityEngine.Random.insideUnitCircle * 0.05f;

                GameObject missile = Instantiate(homingMissilePrefab, spawnPosition, playerHand.transform.rotation);
                missile.GetComponent<HomingMissile>()?.Launch(target);

                await UniTask.Delay(TimeSpan.FromSeconds(homingMissileSpawnInterval), cancellationToken: token);
            }
        }
        finally
        {
            playerControl.SetSkillUsageState(false); // 스킬 사용 종료
        }
    }

    /// <summary>
    /// 적의 AK47 스킬을 실행하는 비동기 메서드.
    /// AK47을 손에 들고, 5연발을 발사합니다.
    /// </summary>
    private async UniTask EnemyAKSkillAsync(Transform handPoint, Transform target, CancellationToken token)
    {
        GameObject akInstance = Instantiate(AK47, handPoint);
        
        // AK47 프리팹에서 발사 위치를 찾습니다.
        Transform akFirePoint = akInstance.transform.Find("FirePoint");
        if (akFirePoint == null)
        {
            Debug.LogError("AK47 프리팹에 'FirePoint' 자식 오브젝트가 없습니다.");
            Destroy(akInstance);
            return;
        }

        try
        {
            // 1. AK47이 대상을 향하도록 회전 (들어올리는 애니메이션 추가)
            float aimDuration = 0.3f; // 바주카와 유사하게 들어올리는 시간
            Vector2 directionToTarget = (target.position - handPoint.position).normalized;
            Vector3 localDirection = handPoint.InverseTransformDirection(directionToTarget);
            float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

            // 총을 아래에서 위로 들어올리는 애니메이션을 위해 시작 각도를 설정합니다.
            float currentAngle = 90f;

            // 캐릭터가 뒤집혔을 때(오른쪽을 볼 때) 총이 상하로 뒤집히는 문제를 보정합니다.
            if (handPoint.lossyScale.x < 0)
            {
                // 총의 Y축 스케일을 반전시켜 뒤집힌 것을 바로잡습니다.
                Vector3 akScale = akInstance.transform.localScale;
                akScale.y *= -1;
                akInstance.transform.localScale = akScale;

                // Y축이 반전되었으므로, 회전 각도도 반대로 적용해야 올바른 방향을 가리킵니다.
                currentAngle *= -1;
                finalLocalAngle *= -1;
            }

            akInstance.transform.localRotation = Quaternion.Euler(0, 0, currentAngle+10f);

            DOTween.To(() => currentAngle, z => { currentAngle = z; akInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, aimDuration)
                .SetEase(Ease.OutQuad);

            await UniTask.Delay(TimeSpan.FromSeconds(aimDuration), cancellationToken: token);

            // 2. 연발 발사
            for (int i = 0; i < enemyMultiShot_Count; i++)
            {
                if (token.IsCancellationRequested || target == null) break;

                // 발사 시점의 타겟 방향을 다시 계산하여 정확도 향상
                Vector2 direction = (target.position - akFirePoint.position).normalized;

                GameObject bullet = ObjectPoolManager.Instance.Get(akBulletPrefab);
                if (bullet == null) continue;

                bullet.transform.SetPositionAndRotation(akFirePoint.position, Quaternion.identity);
                
                Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = direction * akBulletSpeed; // 총알 속도

                await UniTask.Delay(TimeSpan.FromSeconds(enemyMultiShot_Interval), cancellationToken: token);
            }
        }
        finally
        {
            if (akInstance != null)
            {
                Destroy(akInstance);
            }
        }
    }
    /// <summary>
    /// 바주카 스킬을 실행하는 공용 비동기 메서드.
    /// 바주카를 손에 들고, 발사 애니메이션을 재생한 후 폭발탄을 발사합니다.
    /// </summary>
    /// <param name="firePoint">발사 위치</param>
    /// <param name="hand">무기가 장착될 손</param>
    /// <param name="target">조준 대상</param>
    /// <param name="token">취소 토큰</param>
    private async UniTaskVoid ExecuteBazookaSkillAsync(Transform firePoint, GameObject hand, Transform target, CancellationToken token)
    {
        if (hand == null || BazookaPrefab == null || playerControl == null) return;

        playerControl.SetSkillUsageState(true); // 스킬 사용 시작

        GameObject bazookaInstance = Instantiate(BazookaPrefab, hand.transform);
        // 캐릭터의 방향에 따라 초기 각도가 달라질 수 있으므로 localRotation을 사용합니다.
        bazookaInstance.transform.localRotation = Quaternion.Euler(0, 0, -90f);
        
        Animator bazookaAnimator = bazookaInstance.GetComponent<Animator>();
        if (bazookaAnimator == null)
        {
            Debug.LogError("바주카 프리팹에 Animator 컴포넌트가 없습니다.");
            Destroy(bazookaInstance);
            return;
        }

        // 바주카 프리팹에서 발사 위치를 찾습니다.
        Transform bazookaFirePoint = bazookaInstance.transform.Find("FirePoint");
        if (bazookaFirePoint == null)
        {
            Debug.LogError("바주카 프리팹에 'FirePoint' 자식 오브젝트가 없습니다.");
            Destroy(bazookaInstance);
            return;
        }

        // 애니메이션 및 회전 시간 설정
        float shoulderAnimDuration = 0.3f; // 어깨에 얹는 회전 시간
        float fireDelay = 0.2f; // 발사 애니메이션 시작 후 실제 발사까지의 딜레이
        float totalFireAnimDuration = 1.2f; // Fire 애니메이션의 총 길이 (발사 딜레이 포함)

        try
        {
            // 2. 바주카가 대상을 향하도록 회전 (어깨에 얹는 동작)
            // 월드 공간에서의 목표 방향을 계산합니다.
            Vector2 directionToTarget = (target.position - hand.transform.position).normalized;
            
            // 월드 방향을 부모(손)의 로컬 공간으로 변환합니다.
            Vector3 localDirection = hand.transform.InverseTransformDirection(directionToTarget);

            // 로컬 방향으로부터 최종 로컬 Z축 각도를 계산합니다.
            float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

            // X, Y축은 고정한 채 Z축만 애니메이션하기 위해 DOTween.To를 사용합니다.
            // 이렇게 하면 회전 중 Y축이 변하는 현상을 방지할 수 있습니다.
            float currentZ = -90f;
            DOTween.To(() => currentZ, z => { currentZ = z; bazookaInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, shoulderAnimDuration)
                .SetEase(Ease.OutQuad);

            // 회전 애니메이션이 끝날 때까지 대기합니다.
            await UniTask.Delay(TimeSpan.FromSeconds(shoulderAnimDuration), cancellationToken: token);

            // 3. 애니메이터 활성화 및 발사 애니메이션 재생
            bazookaAnimator.enabled = true;
            bazookaAnimator.SetTrigger("Fire");

            // 4. 발사 타이밍까지 대기
            await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

            // 5. 폭발탄 발사
            if (explosiveArrowPrefab != null)
            {
                // 발사 시점의 타겟 방향을 다시 계산하여 정확도 향상
                Vector2 direction = (target.position - bazookaFirePoint.position).normalized;
                GameObject arrow = Instantiate(explosiveArrowPrefab, bazookaFirePoint.position, Quaternion.identity);
                arrow.GetComponent<Roket>()?.Launch(direction);
            }

            // 6. 남은 애니메이션 시간만큼 대기
            await UniTask.Delay(TimeSpan.FromSeconds(totalFireAnimDuration - fireDelay), cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled
        }
        finally
        {
            // 7. 바주카 파괴
            if (bazookaInstance != null)
            {
                Destroy(bazookaInstance);
            }
            playerControl.SetSkillUsageState(false); // 스킬 사용 종료
        }
    }
    #endregion
}
