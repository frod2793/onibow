using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Pool;

/// <summary>
/// 다양한 종류의 게임 오브젝트를 관리하는 범용 오브젝트 풀 매니저입니다.
/// Unity의 ObjectPool을 사용하여 구현되었습니다.
/// </summary>
public class ObjectPoolManager : MonoBehaviour
{
    public static ObjectPoolManager Instance { get; private set; }

    // 프리팹을 키로 사용하는 딕셔너리
    private Dictionary<GameObject, IObjectPool<GameObject>> _prefabPools;
    // 활성화된 오브젝트가 어느 풀에 속하는지 추적하는 딕셔너리 (Key: 인스턴스 ID, Value: 풀)
    private Dictionary<int, IObjectPool<GameObject>> _spawnedObjects;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _prefabPools = new Dictionary<GameObject, IObjectPool<GameObject>>();
        _spawnedObjects = new Dictionary<int, IObjectPool<GameObject>>();
    }

    /// <summary>
    /// 지정된 프리팹에 해당하는 오브젝트를 풀에서 가져옵니다.
    /// </summary>
    public GameObject Get(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("풀에서 오브젝트를 가져올 수 없습니다: 프리팹이 null입니다.");
            return null;
        }

        if (!_prefabPools.TryGetValue(prefab, out var pool))
        {
            // 요청된 프리팹에 대한 풀이 없으면 동적으로 생성합니다.
            // 이를 통해 인스펙터에서 모든 풀을 미리 정의할 필요가 없어집니다.
            pool = CreateNewPoolForPrefab(prefab);
            _prefabPools.Add(prefab, pool);
        }

        GameObject objectToSpawn = pool.Get();
        _spawnedObjects.Add(objectToSpawn.GetInstanceID(), pool);

        return objectToSpawn;
    }

    /// <summary>
    /// 사용이 끝난 오브젝트를 풀로 반환합니다.
    /// </summary>
    public void Return(GameObject objectToReturn)
    {
        int instanceID = objectToReturn.GetInstanceID();

        if (_spawnedObjects.TryGetValue(instanceID, out var pool))
        {
            pool.Release(objectToReturn);
            _spawnedObjects.Remove(instanceID);
        }
        else
        {
            Debug.LogWarning($"'{objectToReturn.name}' 오브젝트는 풀에서 관리되지 않거나 이미 반환되었습니다. 오브젝트를 파괴합니다.");
            Destroy(objectToReturn); // 풀에서 관리하지 않는 오브젝트는 파괴
        }
    }

    /// <summary>
    /// 특정 프리팹에 대한 새로운 오브젝트 풀을 생성합니다.
    /// </summary>
    /// <param name="prefab">풀링할 프리팹</param>
    /// <param name="defaultCapacity">풀의 초기 용량</param>
    /// <returns>생성된 IObjectPool 인스턴스</returns>
    private IObjectPool<GameObject> CreateNewPoolForPrefab(GameObject prefab, int defaultCapacity = 10)
    {
        // 요청 시점에 풀이 없으면 동적으로 생성합니다.
        Debug.Log($"ObjectPoolManager: '{prefab.name}' 프리팹에 대한 풀을 동적으로 생성합니다.");
        return new ObjectPool<GameObject>(
            createFunc: () => Instantiate(prefab),
            actionOnGet: (obj) => {
                obj.transform.SetParent(null); // 풀에서 나올 때 부모를 해제하여 월드 공간에 배치
                obj.SetActive(true);
            },
            actionOnRelease: (obj) => {
                obj.transform.SetParent(transform); // 반환 시 매니저의 자식으로 다시 설정하여 씬을 깔끔하게 유지합니다.
                obj.SetActive(false);
            },
            actionOnDestroy: (obj) => Destroy(obj),
            collectionCheck: true, defaultCapacity: defaultCapacity, maxSize: 10000);
    }
}