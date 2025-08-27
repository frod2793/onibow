using UnityEngine;
using Cysharp.Threading.Tasks;
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
    [SerializeField] private GameObject homingArrowPrefab; // 추적탄 프리팹
    [SerializeField] private GameObject explosiveArrowPrefab; // 폭발탄 프리팹

    [Header("플레이어 참조 컴포넌트")]
    [SerializeField] private PlayerControl playerControl; // 플레이어 컨트롤러
    [SerializeField] private Transform playerFirePoint;   // 플레이어 발사 위치

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
        if (Skill3_RemainingCooldown > 0) return; // 쿨타임 체크
        if (playerControl == null || homingArrowPrefab == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null)
        {
            Debug.Log("추적할 대상이 없습니다.");
            return; // 추적할 대상이 없음
        }

        _lastSkill3_Time = Time.time;
        Debug.Log("스킬 3: 추적탄 사용!");

        GameObject arrow = Instantiate(homingArrowPrefab, playerFirePoint.position, playerFirePoint.rotation);
        arrow.GetComponent<HomingArrow>()?.Launch(target.transform);
    }

    /// <summary>
    /// 스킬 4: 폭발탄 사용
    /// </summary>
    public void UseSkill4()
    {
        if (Skill4_RemainingCooldown > 0) return; // 쿨타임 체크
        if (playerControl == null || explosiveArrowPrefab == null) return;

        GameObject target = playerControl.FindNearestEnemy();
        if (target == null)
        {
            Debug.Log("조준할 대상이 없어 스킬을 사용하지 않습니다.");
            return;
        }

        _lastSkill4_Time = Time.time;
        Debug.Log("스킬 4: 폭발탄 사용!");

        Vector2 direction = (target.transform.position - playerFirePoint.position).normalized;
        GameObject arrow = Instantiate(explosiveArrowPrefab, playerFirePoint.position, Quaternion.identity);
        arrow.GetComponent<ExplosiveArrow>()?.Launch(direction);
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
        for (int i = 0; i < 5; i++)
        {
            if (token.IsCancellationRequested || playerControl == null) break;

            playerControl.FireAtNearestEnemy();
            await UniTask.Delay((int)(playerSkill1_FireInterval * 1000), cancellationToken: token);
        }
    }

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

    #endregion
}
