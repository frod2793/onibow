using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 적의 화살 오브젝트를 재사용하여 성능을 최적화하는 오브젝트 풀입니다.
/// 씬에 하나의 인스턴스만 존재하도록 싱글턴으로 구현되었습니다.
/// </summary>
public class EnemyArrowPool : MonoBehaviour
{
    public static EnemyArrowPool Instance { get; private set; }

    [Tooltip("풀에서 관리할 적의 화살 프리팹입니다.")]
    [SerializeField] private GameObject arrowPrefab;
    [Tooltip("최초에 생성해 둘 화살의 개수입니다.")]
    [SerializeField] private int initialPoolSize = 20;

    private Queue<GameObject> _pool = new Queue<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializePool();
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject arrow = Instantiate(arrowPrefab, transform); // 풀 오브젝트의 자식으로 생성
            arrow.SetActive(false);
            _pool.Enqueue(arrow);
        }
    }

    public GameObject Get()
    {
        if (_pool.Count > 0)
        {
            GameObject arrow = _pool.Dequeue();
            arrow.SetActive(true);
            return arrow;
        }
        // 풀이 비어있으면 새로 생성 (비상시 대비)
        return Instantiate(arrowPrefab);
    }

    public void Return(GameObject arrow)
    {
        arrow.SetActive(false);
        _pool.Enqueue(arrow);
    }
}