using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 잔상 효과에 사용될 오브젝트를 관리하는 오브젝트 풀입니다.
/// </summary>
public class AfterimagePool : MonoBehaviour
{
    public static AfterimagePool Instance { get; private set; }

    [SerializeField] private GameObject afterimagePrefab;
    [SerializeField] private int initialPoolSize = 15;

    private readonly Queue<GameObject> _pool = new Queue<GameObject>();

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
            GameObject obj = Instantiate(afterimagePrefab, transform);
            obj.SetActive(false);
            _pool.Enqueue(obj);
        }
    }

    public GameObject Get()
    {
        if (_pool.Count > 0)
        {
            GameObject obj = _pool.Dequeue();
            obj.SetActive(true);
            return obj;
        }
        // 풀이 비어있으면 비상용으로 새로 생성
        return Instantiate(afterimagePrefab);
    }

    public void Return(GameObject obj)
    {
        obj.SetActive(false);
        _pool.Enqueue(obj);
    }
}