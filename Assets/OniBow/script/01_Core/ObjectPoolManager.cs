using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace OniBow.Managers
{
    /// <summary>
    /// 다양한 종류의 게임 오브젝트를 관리하는 범용 오브젝트 풀 매니저입니다.
    /// Unity의 ObjectPool을 사용하여 구현되었습니다.
    /// </summary>
    public class ObjectPoolManager : MonoBehaviour
    {
        public static ObjectPoolManager Instance { get; private set; }

        private Dictionary<GameObject, IObjectPool<GameObject>> _prefabPools;
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
        /// 지정된 프리팹에 해당하는 오브젝트를 풀에서 가져옵니다. 해당 프리팹의 풀이 없으면 새로 생성합니다.
        /// </summary>
        /// <param name="prefab">가져올 오브젝트의 프리팹</param>
        /// <returns>생성된 게임 오브젝트</returns>
        public GameObject Get(GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogError("풀에서 오브젝트를 가져올 수 없습니다: 프리팹이 null입니다.");
                return null;
            }

            if (!_prefabPools.TryGetValue(prefab, out var pool))
            {
                pool = CreateNewPoolForPrefab(prefab);
                _prefabPools.Add(prefab, pool);
            }

            GameObject objectToSpawn = pool.Get();
            _spawnedObjects.Add(objectToSpawn.GetInstanceID(), pool);

            return objectToSpawn;
        }

        /// <summary>
        /// 사용이 끝난 게임 오브젝트를 원래의 풀로 반환합니다.
        /// </summary>
        /// <param name="objectToReturn">반환할 게임 오브젝트</param>
        public void Return(GameObject objectToReturn)
        {
            if (objectToReturn == null) return;
            
            int instanceID = objectToReturn.GetInstanceID();

            if (_spawnedObjects.TryGetValue(instanceID, out var pool))
            {
                pool.Release(objectToReturn);
                _spawnedObjects.Remove(instanceID);
            }
            else
            {
                // 이미 반환되었거나 풀에서 관리되지 않는 오브젝트일 수 있습니다.
                // 이 경우, 경고를 남기고 오브젝트를 파괴하여 메모리 누수를 방지합니다.
                if (objectToReturn.activeSelf)
                {
                    Debug.LogWarning($"'{objectToReturn.name}' 오브젝트는 풀에서 관리되지 않거나 이미 반환되었습니다. 오브젝트를 파괴합니다.");
                    Destroy(objectToReturn);
                }
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
            return new ObjectPool<GameObject>(
                createFunc: () => Instantiate(prefab),
                actionOnGet: (obj) => {
                    obj.transform.SetParent(null);
                    obj.SetActive(true);
                },
                actionOnRelease: (obj) => {
                    obj.transform.SetParent(transform);
                    obj.SetActive(false);
                },
                actionOnDestroy: (obj) => Destroy(obj),
                collectionCheck: true, defaultCapacity: defaultCapacity, maxSize: 10000);
        }
    }
}