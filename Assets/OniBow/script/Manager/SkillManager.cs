using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System.Threading;

/// <summary>
/// 플레이어와 적이 사용할 스킬을 관리하는 클래스입니다.
/// </summary>
public class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    #region 플레이어 스킬 변수

    [Header("플레이어 스킬 쿨타임")]
    [SerializeField] private float playerSkill1_Cooldown = 10f;
    [SerializeField] private float playerSkill2_Cooldown = 15f;
    [SerializeField] private float playerSkill3_Cooldown = 15f;
    [SerializeField] private float playerSkill4_Cooldown = 20f;
  
    [Header("플레이어 스킬 설정")]
    [SerializeField] private GameObject barrierEffectPrefab;
    [SerializeField] private float barrierDuration = 5f;
    [SerializeField] private GameObject homingMissilePrefab;
    [SerializeField] private GameObject explosiveArrowPrefab;
    [SerializeField] private int homingMissileCount = 5;
    [SerializeField] private float homingMissileSpawnInterval = 0.1f;

    [Header("플레이어 참조 컴포넌트")]
    [SerializeField] private PlayerControl playerControl;
    [SerializeField] private Transform playerFirePoint;

    [Header("스킬무기 가 장착될 위치")]
    [SerializeField] private GameObject playerHand;
    
    [Header("스킬 무기")]
    [SerializeField] private GameObject BazookaPrefab;
    [SerializeField] private GameObject AK47;
    
    private float _lastSkill1_Time = -999f;
    private float _lastSkill2_Time = -999f;
    private float _lastSkill3_Time = -999f;
    private float _lastSkill4_Time = -999f;

    public float Skill1_RemainingCooldown => Mathf.Max(0f, _lastSkill1_Time + playerSkill1_Cooldown - Time.time);
    public float Skill2_RemainingCooldown => Mathf.Max(0f, _lastSkill2_Time + playerSkill2_Cooldown - Time.time);
    public float Skill3_RemainingCooldown => Mathf.Max(0f, _lastSkill3_Time + playerSkill3_Cooldown - Time.time);
    public float Skill4_RemainingCooldown => Mathf.Max(0f, _lastSkill4_Time + playerSkill4_Cooldown - Time.time);

    public float PlayerSkill1_Cooldown => playerSkill1_Cooldown;
    public float PlayerSkill2_Cooldown => playerSkill2_Cooldown;
    public float PlayerSkill3_Cooldown => playerSkill3_Cooldown;
    public float PlayerSkill4_Cooldown => playerSkill4_Cooldown;

    #endregion

    #region 적 스킬 변수

    [Header("적 스킬 설정")]
    [Tooltip("적이 다발 사격 시 사용할 총알 프리팹")]
    [SerializeField] private GameObject akBulletPrefab;
    [SerializeField] private int enemyMultiShot_Count = 5;
    [SerializeField] private float enemyMultiShot_Interval = 0.15f;
    [SerializeField] private float akBulletSpeed = 30f;

    #endregion

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

    #region 적 스킬 실행
    
    /// <summary>
    /// 적의 다발 사격 스킬을 실행합니다. Enemy.cs에서 호출됩니다.
    /// </summary>
    /// <param name="handPoint">무기가 생성될 위치</param>
    /// <param name="target">공격 대상</param>
    public async UniTask ExecuteEnemyMultiShot(Transform handPoint, Transform target)
    {
        if (AK47 == null || handPoint == null || ObjectPoolManager.Instance == null || target == null)
        {
            Debug.LogWarning("적 다발 사격 스킬의 설정이 올바르지 않습니다.");
            return;
        }
        await EnemyAKSkillAsync(handPoint, target, this.GetCancellationTokenOnDestroy());
    }

    #endregion

    #region 플레이어 스킬 (UI에서 호출)

    /// <summary>
    /// 스킬 1 (배리어)을 사용합니다.
    /// </summary>
    public void UseSkill1()
    {
        if (Skill1_RemainingCooldown > 0) return;
        _lastSkill1_Time = Time.time;
        PlayerSkill1_BarrierAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 2 (힐)를 사용합니다.
    /// </summary>
    public void UseSkill2()
    {
        if (Skill2_RemainingCooldown > 0 || playerControl == null) return;
        _lastSkill2_Time = Time.time;
        PlayerSkill2_HealAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 3 (추적 미사일)을 사용합니다.
    /// </summary>
    public void UseSkill3()
    {
        if (Skill3_RemainingCooldown > 0 || playerControl == null || homingMissilePrefab == null || playerHand == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null) return;

        _lastSkill3_Time = Time.time;
        PlayerSkill3_HomingMissilesAsync(target.transform, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 4 (바주카)를 사용합니다.
    /// </summary>
    public void UseSkill4()
    {
        if (Skill4_RemainingCooldown > 0 || playerControl == null || explosiveArrowPrefab == null || BazookaPrefab == null || playerHand == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null) return;

        _lastSkill4_Time = Time.time;
        ExecuteBazookaSkillAsync(playerFirePoint, playerHand, target.transform, this.GetCancellationTokenOnDestroy()).Forget();
    }

    #endregion

    #region 스킬 실행 로직

    /// <summary>
    /// 지정된 시간 동안 플레이어에게 무적 배리어를 생성합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill1_BarrierAsync(CancellationToken token)
    {
        playerControl.SetSkillUsageState(true, false);
        playerControl.SetInvulnerable(true);

        GameObject barrierInstance = null;
        Animator barrierAnimator = null;

        try
        {
            if (playerControl == null || barrierEffectPrefab == null) return;
            barrierInstance = Instantiate(barrierEffectPrefab, playerControl.transform.position, Quaternion.identity, playerControl.transform);
            if (barrierInstance == null) return;
            
            barrierInstance.SetActive(true);
            barrierAnimator = barrierInstance.GetComponentInChildren<Animator>();

            if (barrierAnimator != null)
            {
                barrierAnimator.SetTrigger("Spawn");
                barrierAnimator.SetTrigger("Stay");
            }
            
            await UniTask.Delay(TimeSpan.FromSeconds(barrierDuration), cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (barrierInstance != null)
            {
                PopAndDestroyBarrierAsync(barrierInstance, barrierAnimator).Forget();
            }
            
            playerControl.SetInvulnerable(false);
            playerControl.SetSkillUsageState(false, false);
        }
    }

    /// <summary>
    /// 배리어의 소멸 애니메이션을 재생하고 오브젝트를 파괴합니다.
    /// </summary>
    private async UniTaskVoid PopAndDestroyBarrierAsync(GameObject barrierInstance, Animator animator)
    {
        if (animator == null || barrierInstance == null)
        {
            if(barrierInstance != null) Destroy(barrierInstance);
            return;
        }

        var token = barrierInstance.GetCancellationTokenOnDestroy();
        animator.SetTrigger("Pop");

        try
        {
            await UniTask.WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).IsName("Pop"), cancellationToken: token);
            await UniTask.WaitUntil(() => animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f, cancellationToken: token);
        }
        catch (OperationCanceledException) { return; }

        if (barrierInstance != null)
        {
            Destroy(barrierInstance);
        }
    }

    /// <summary>
    /// 플레이어의 체력을 회복하고 관련 이펙트를 재생합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill2_HealAsync(CancellationToken token)
    {
        if (playerControl == null) return;

        playerControl.SetSkillUsageState(true, false);
        GameObject healEffectInstance = null;
        
        try
        {
            playerControl.HealWithTempHp();

            if (EffectManager.Instance != null && EffectManager.Instance.HealEffectPrefab != null)
            {
                healEffectInstance = Instantiate(EffectManager.Instance.HealEffectPrefab, playerControl.transform.position, Quaternion.identity, playerControl.transform);
                if (healEffectInstance != null) healEffectInstance.SetActive(true); 

                var spum = playerControl.GetComponentInChildren<SPUM_Prefabs>();
                if (spum != null && spum._anim != null)
                {
                    int maxPlayerSortingOrder = spum._anim.GetComponentsInChildren<SpriteRenderer>().Max(r => r.sortingOrder);
                    foreach (var effectRenderer in healEffectInstance.GetComponentsInChildren<Renderer>())
                    {
                        effectRenderer.sortingOrder = maxPlayerSortingOrder + 1;
                    }
                }
            }
            ApplyPlayerTintAsync(new Color(0.7f, 1f, 0.7f, 1f), 3f, token).Forget();
            
            float healAmount = playerControl.GetMaxHp() * 0.3f;
            await playerControl.GradualHeal(healAmount, 3f, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (healEffectInstance != null) Destroy(healEffectInstance);
            playerControl.SetSkillUsageState(false, false);
        }
    }

    /// <summary>
    /// 지정된 시간 동안 플레이어 캐릭터에 틴트 색상을 적용합니다.
    /// </summary>
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
    /// 대상을 향해 여러 개의 추적 미사일을 순차적으로 발사합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill3_HomingMissilesAsync(Transform target, CancellationToken token)
    {
        if (playerControl == null) return;
        playerControl.SetSkillUsageState(true, false);
        try
        {
            for (int i = 0; i < homingMissileCount; i++)
            {
                if (token.IsCancellationRequested || target == null) break;

                Vector3 spawnPosition = playerHand.transform.position + (Vector3)UnityEngine.Random.insideUnitCircle * 0.05f;

                GameObject missileObject = ObjectPoolManager.Instance.Get(homingMissilePrefab);
                if (missileObject == null) continue;

                missileObject.transform.SetPositionAndRotation(spawnPosition, playerHand.transform.rotation);
                missileObject.GetComponent<HomingMissile>()?.Launch(target, playerHand.transform);

                await UniTask.Delay(TimeSpan.FromSeconds(homingMissileSpawnInterval), cancellationToken: token);
            }
        }
        finally
        {
            playerControl.SetSkillUsageState(false, false);
        }
    }

    /// <summary>
    /// 적의 AK47 다발 사격 스킬을 비동기적으로 실행합니다.
    /// </summary>
    private async UniTask EnemyAKSkillAsync(Transform handPoint, Transform target, CancellationToken token)
    {
        GameObject akInstance = Instantiate(AK47, handPoint);
        
        Transform akFirePoint = akInstance.transform.Find("FirePoint");
        if (akFirePoint == null)
        {
            Destroy(akInstance);
            return;
        }

        try
        {
            float aimDuration = 0.3f;
            Vector2 directionToTarget = (target.position - handPoint.position).normalized;
            Vector3 localDirection = handPoint.InverseTransformDirection(directionToTarget);
            float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

            float currentAngle = 90f;

            if (handPoint.lossyScale.x < 0)
            {
                Vector3 akScale = akInstance.transform.localScale;
                akScale.y *= -1;
                akInstance.transform.localScale = akScale;
                currentAngle *= -1;
                finalLocalAngle *= -1;
            }

            akInstance.transform.localRotation = Quaternion.Euler(0, 0, currentAngle+10f);

            DOTween.To(() => currentAngle, z => { currentAngle = z; akInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, aimDuration)
                .SetEase(Ease.OutQuad);

            await UniTask.Delay(TimeSpan.FromSeconds(aimDuration), cancellationToken: token);

            for (int i = 0; i < enemyMultiShot_Count; i++)
            {
                if (token.IsCancellationRequested || target == null) break;

                Vector2 direction = (target.position - akFirePoint.position).normalized;

                GameObject bullet = ObjectPoolManager.Instance.Get(akBulletPrefab);
                if (bullet == null) continue;
                
                if (SoundManager.Instance != null && !string.IsNullOrEmpty(SoundManager.Instance.AKFireSfx))
                {
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.AKFireSfx);
                }

                bullet.transform.SetPositionAndRotation(akFirePoint.position, Quaternion.identity);
                
                Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = direction * akBulletSpeed;

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
    /// 플레이어의 바주카 스킬을 비동기적으로 실행합니다.
    /// </summary>
    private async UniTaskVoid ExecuteBazookaSkillAsync(Transform firePoint, GameObject hand, Transform target, CancellationToken token)
    {
        if (hand == null || BazookaPrefab == null || playerControl == null) return;

        playerControl.SetSkillUsageState(true);

        GameObject bazookaInstance = Instantiate(BazookaPrefab, hand.transform);
        bazookaInstance.transform.localRotation = Quaternion.Euler(0, 0, -90f);
        
        Animator bazookaAnimator = bazookaInstance.GetComponent<Animator>();
        if (bazookaAnimator == null)
        {
            Destroy(bazookaInstance);
            return;
        }

        Transform bazookaFirePoint = bazookaInstance.transform.Find("FirePoint");
        if (bazookaFirePoint == null)
        {
            Destroy(bazookaInstance);
            return;
        }

        float shoulderAnimDuration = 0.3f;
        float fireDelay = 0.2f;
        float totalFireAnimDuration = 1.2f;

        try
        {
            Vector2 directionToTarget = (target.position - hand.transform.position).normalized;
            Vector3 localDirection = hand.transform.InverseTransformDirection(directionToTarget);
            float finalLocalAngle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;
            
            float currentZ = -90f;
            DOTween.To(() => currentZ, z => { currentZ = z; bazookaInstance.transform.localEulerAngles = new Vector3(0, 0, z); }, finalLocalAngle, shoulderAnimDuration)
                .SetEase(Ease.OutQuad);

            await UniTask.Delay(TimeSpan.FromSeconds(shoulderAnimDuration), cancellationToken: token);

            bazookaAnimator.enabled = true;
            bazookaAnimator.SetTrigger("Fire");

            await UniTask.Delay(TimeSpan.FromSeconds(fireDelay), cancellationToken: token);

            if (explosiveArrowPrefab != null)
            {
                Vector2 direction = (target.position - bazookaFirePoint.position).normalized;
                GameObject arrow = Instantiate(explosiveArrowPrefab, bazookaFirePoint.position, Quaternion.identity);
                arrow.GetComponent<Roket>()?.Launch(direction);
            }
            
            await UniTask.Delay(TimeSpan.FromSeconds(totalFireAnimDuration - fireDelay), cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (bazookaInstance != null)
            {
                Destroy(bazookaInstance);
            }
            playerControl.SetSkillUsageState(false);
        }
    }
    #endregion
}
