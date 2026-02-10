using System.Collections.Generic;
using UnityEngine;

namespace OptimizationLab.Managers
{
    /// <summary>
    /// 전통적인 GameObject 방식을 사용하는 매니저 (비교용)
    /// 메인 스레드에서 순차적으로 위치 업데이트 수행
    /// </summary>
    public class GameObjectManager : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private int objectCount = 10000;
        [SerializeField] private GameObject prefab;
        [SerializeField] private float spawnRadius = 50f;
        [SerializeField] private float speedRange = 5f;

        [Header("Bounds")]
        [SerializeField] private Vector3 boundsMin = new Vector3(-50, -50, -50);
        [SerializeField] private Vector3 boundsMax = new Vector3(50, 50, 50);

        private List<GameObject> spawnedObjects;
        private List<Vector3> velocities;
        private bool isInitialized = false;

        /// <summary>
        /// 초기화: 오브젝트 생성 및 속도 설정
        /// </summary>
        private void Initialize()
        {
            if (prefab == null)
            {
                Debug.LogError("Prefab이 할당되지 않았습니다!");
                return;
            }

            spawnedObjects = new List<GameObject>(objectCount);
            velocities = new List<Vector3>(objectCount);

            // 오브젝트 생성 및 초기 위치/속도 설정
            for (int i = 0; i < objectCount; i++)
            {
                Vector3 randomPosition = UnityEngine.Random.insideUnitSphere * spawnRadius;
                Vector3 randomVelocity = UnityEngine.Random.insideUnitSphere * speedRange;

                GameObject obj = Instantiate(prefab, randomPosition, Quaternion.identity);
                spawnedObjects.Add(obj);
                velocities.Add(randomVelocity);
            }

            Debug.Log($"GameObjectManager: {objectCount}개의 오브젝트 생성 완료");
        }

        private void OnEnable()
        {
            // 활성화될 때만 초기화 (모드 전환 시)
            if (!isInitialized)
            {
                Initialize();
                isInitialized = true;
            }
        }

        private void OnDisable()
        {
            // 비활성화될 때 정리 (모드 전환 시)
            if (isInitialized)
            {
                Cleanup();
                isInitialized = false;
            }
        }

        private void Update()
        {
            // 초기화되지 않았으면 실행하지 않음
            if (!isInitialized || spawnedObjects == null) return;

            // 메인 스레드에서 순차적으로 위치 업데이트
            for (int i = 0; i < spawnedObjects.Count; i++)
            {
                if (spawnedObjects[i] == null) continue;

                // 위치 업데이트: P = P + V * dt
                Vector3 newPosition = spawnedObjects[i].transform.position + velocities[i] * Time.deltaTime;

                // 경계 체크 및 래핑
                if (newPosition.x < boundsMin.x)
                    newPosition.x = boundsMax.x;
                else if (newPosition.x > boundsMax.x)
                    newPosition.x = boundsMin.x;

                if (newPosition.y < boundsMin.y)
                    newPosition.y = boundsMax.y;
                else if (newPosition.y > boundsMax.y)
                    newPosition.y = boundsMin.y;

                if (newPosition.z < boundsMin.z)
                    newPosition.z = boundsMax.z;
                else if (newPosition.z > boundsMax.z)
                    newPosition.z = boundsMin.z;

                spawnedObjects[i].transform.position = newPosition;
            }
        }

        /// <summary>
        /// 오브젝트 개수 설정
        /// </summary>
        public void SetObjectCount(int count)
        {
            if (count <= 0) return;

            // 기존 오브젝트 정리
            Cleanup();
            isInitialized = false;

            objectCount = count;
            
            // 활성화되어 있을 때만 즉시 초기화
            if (enabled)
            {
                Initialize();
                isInitialized = true;
            }
        }

        /// <summary>
        /// 정리 작업: 오브젝트 삭제
        /// </summary>
        public void Cleanup()
        {
            if (spawnedObjects != null)
            {
                foreach (var obj in spawnedObjects)
                {
                    if (obj != null)
                        Destroy(obj);
                }
                spawnedObjects.Clear();
            }

            if (velocities != null)
            {
                velocities.Clear();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
