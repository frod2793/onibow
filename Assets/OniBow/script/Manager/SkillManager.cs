using System;
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
    [SerializeField] private float playerSkill1_Cooldown = 10f; // 5연발
    [SerializeField] private float playerSkill2_Cooldown = 5f;  // 직선 발사
    [SerializeField] private float playerSkill3_Cooldown = 15f; // 추적탄
    [SerializeField] private float playerSkill4_Cooldown = 20f; // 폭발탄
    [SerializeField] private float playerHealSkill_Cooldown = 30f; // 회복

    [Header("플레이어 스킬 설정")]
    [SerializeField] private float playerSkill1_FireInterval = 0.2f; // 5연발 발사 간격
    [SerializeField] private GameObject homingMissilePrefab; // 추적 미사일 프리팹
    [SerializeField] private GameObject explosiveArrowPrefab; // 폭발탄 프리팹
    [SerializeField] private int homingMissileCount = 5; // 추적 미사일 발사 개수
    [SerializeField] private float homingMissileSpawnInterval = 0.1f; // 추적 미사일 발사 간격

    [Header("플레이어 참조 컴포넌트")]
    [SerializeField] private PlayerControl playerControl; // 플레이어 컨트롤러
    [SerializeField] private Transform playerFirePoint;   // 플레이어 발사 위치

    [Header("스킬무기 가 장착될 위치")] [SerializeField]
    private GameObject playerHand;
    [SerializeField] private GameObject EnemyHand;
    
    [Header("스킬 무기")] [SerializeField] private GameObject BazookaPrefab;
    
    
    
    
    // 각 스킬의 마지막 사용 시간
    private float _lastSkill1_Time = -999f;
    private float _lastSkill2_Time = -999f;
    private float _lastSkill3_Time = -999f;
    private float _lastSkill4_Time = -999f;
    private float _lastHealSkill_Time = -999f;

    // 외부에서 남은 쿨타임을 확인할 수 있는 프로퍼티
    public float Skill1_RemainingCooldown => Mathf.Max(0f, _lastSkill1_Time + playerSkill1_Cooldown - Time.time);
    public float Skill2_RemainingCooldown => Mathf.Max(0f, _lastSkill2_Time + playerSkill2_Cooldown - Time.time);
    public float Skill3_RemainingCooldown => Mathf.Max(0f, _lastSkill3_Time + playerSkill3_Cooldown - Time.time);
    public float Skill4_RemainingCooldown => Mathf.Max(0f, _lastSkill4_Time + playerSkill4_Cooldown - Time.time);
    public float HealSkill_RemainingCooldown => Mathf.Max(0f, _lastHealSkill_Time + playerHealSkill_Cooldown - Time.time);

    #endregion

    #region 적 스킬 변수

    [Header("적 스킬 설정")]
    [Tooltip("적이 사용할 다발 사격용 화살 프리팹")]
    [SerializeField] private GameObject enemyMultiShotArrowPrefab;
    [SerializeField] private int enemyMultiShot_Count = 3; // 3연발
    [SerializeField] private float enemyMultiShot_Interval = 0.3f; // 발사 간격

    #endregion

    #region 적 스킬 실행 (외부 호출용)

    /// <summary>
    /// 적 스킬: 지정된 대상을 향해 다발 사격을 가합니다.
    /// 이 메서드는 쿨타임을 관리하지 않으므로, 호출하는 쪽(Enemy.cs)에서 관리해야 합니다.
    /// </summary>
    public void ExecuteEnemyMultiShot(Transform firePoint, Transform target)
    {
        if (enemyMultiShotArrowPrefab == null || firePoint == null || target == null)
        {
            Debug.LogWarning("적 다발 사격 스킬의 설정이 올바르지 않습니다.");
            return;
        }

        EnemyMultiShotAsync(firePoint, target, this.GetCancellationTokenOnDestroy()).Forget();
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
    /// 스킬 1: 5연발 사용
    /// </summary>
    public void UseSkill1()
    {
        if (Skill1_RemainingCooldown > 0) return; // 쿨타임 체크
        _lastSkill1_Time = Time.time;
        Debug.Log("스킬 1: 5연발 사용!");
        PlayerSkill1_FiveShotBarrageAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>
    /// 스킬 2: 직선 발사 사용
    /// </summary>
    public void UseSkill2()
    {
        if (Skill2_RemainingCooldown > 0) return; // 쿨타임 체크
        if (playerControl == null) return;

        _lastSkill2_Time = Time.time;
        Debug.Log("스킬 2: 직선 발사 사용!");
        playerControl.FireStraightArrow();
    }

    /// <summary>
    /// 스킬 3: 추적탄 사용
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
    /// 스킬 4: 폭발탄 사용
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

    /// <summary>
    /// 플레이어 회복 스킬 사용
    /// </summary>
    public void UseHealSkill()
    {
        if (HealSkill_RemainingCooldown > 0 || playerControl == null) return;

        _lastHealSkill_Time = Time.time;
        Debug.Log("플레이어 회복 스킬 사용!");
        // 예비 체력만큼 현재 체력을 회복합니다.
        playerControl.HealWithTempHp();
    }

    #endregion

    #region 스킬 실행 로직 (공용/내부)

    /// <summary>
    /// 플레이어 스킬 1: 5연발을 비동기적으로 발사합니다.
    /// </summary>
    private async UniTaskVoid PlayerSkill1_FiveShotBarrageAsync(CancellationToken token)
    {
        if (playerControl == null) return;
        playerControl.SetSkillUsageState(true); // 스킬 사용 시작
        try
        {
            for (int i = 0; i < 5; i++)
            {
                if (token.IsCancellationRequested) break;

                playerControl.FireAtNearestEnemy();
                await UniTask.Delay(TimeSpan.FromSeconds(playerSkill1_FireInterval), cancellationToken: token);
            }
        }
        finally
        {
            playerControl.SetSkillUsageState(false); // 스킬 사용 종료
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

    private async UniTaskVoid EnemyMultiShotAsync(Transform firePoint, Transform target, CancellationToken token)
    {
        for (int i = 0; i < enemyMultiShot_Count; i++)
        {
            if (token.IsCancellationRequested || firePoint == null || target == null) break;

            Vector2 direction = (target.position - firePoint.position).normalized;
            GameObject arrow = Instantiate(enemyMultiShotArrowPrefab, firePoint.position, Quaternion.identity);
            
            // 화살의 속도나 발사 로직은 화살 프리팹의 스크립트에서 처리한다고 가정합니다.
            // 예를 들어, Rigidbody2D를 사용해 힘을 가할 수 있습니다.
            Rigidbody2D rb = arrow.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = direction * 20f; // 예시 속도
            }

            await UniTask.Delay((int)(enemyMultiShot_Interval * 1000), cancellationToken: token);
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
