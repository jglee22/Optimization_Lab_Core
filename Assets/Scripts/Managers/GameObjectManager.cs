using System.Collections.Generic;
using UnityEngine;
using OptimizationLab.Helpers;

namespace OptimizationLab.Managers
{
    /// <summary>
    /// 전통적인 GameObject 방식을 사용하는 매니저 (비교용)
    /// 오브젝트를 일정 간격 그리드로만 생성하며, 움직임 없음
    /// </summary>
    public class GameObjectManager : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private int objectCount = 10000;
        [Tooltip("바닥 위에 생성할 오브젝트 프리팹 (Animator 있으면 idle/walk/run 랜덤 재생)")]
        [SerializeField] private GameObject prefab;
        [SerializeField] private float gridSpacing = 2f;
        [SerializeField] private float groundHeight = 0f;

        [Header("Animation - Blend Tree (Speed 파라미터)")]
        [Tooltip("Blend Tree가 있는 스테이트 이름 (재생 타이밍 랜덤용, 비우면 Speed만 설정)")]
        [SerializeField] private string blendTreeStateName = "Blend Tree";
        [SerializeField] private int animatorLayer = 0;
        [Tooltip("Blend Tree 제어용 파라미터 이름 (예: Speed)")]
        [SerializeField] private string speedParameterName = "Speed";
        [Tooltip("idle / walk / run에 넣을 Speed 값")]
        [SerializeField] private float speedIdle = 0f;
        [SerializeField] private float speedWalk = 1f;
        [SerializeField] private float speedRun = 2f;

        private List<GameObject> spawnedObjects;
        private bool isInitialized = false;

        /// <summary>
        /// 초기화: 그리드 배치로 오브젝트 생성 및 Blend Tree 애니메이션 랜덤 적용
        /// </summary>
        private void Initialize()
        {
            if (prefab == null)
            {
                Debug.LogError("Prefab이 할당되지 않았습니다!");
                return;
            }

            spawnedObjects = new List<GameObject>(objectCount);

            for (int i = 0; i < objectCount; i++)
            {
                Vector3 position = GridLayoutHelper.GetGridPositionVector3(i, objectCount, gridSpacing, groundHeight);
                GameObject obj = Instantiate(prefab, position, Quaternion.identity);
                spawnedObjects.Add(obj);

                // Blend Tree: Speed 파라미터로 idle(0)/walk/run 랜덤 + 재생 시점 랜덤
                var anim = obj.GetComponent<Animator>();
                if (anim != null && anim.runtimeAnimatorController != null)
                {
                    int stateIndex = Random.Range(0, 3);
                    float speed = stateIndex == 0 ? speedIdle : (stateIndex == 1 ? speedWalk : speedRun);
                    anim.SetFloat(speedParameterName, speed);

                    if (!string.IsNullOrEmpty(blendTreeStateName))
                    {
                        float normalizedTime = Random.Range(0f, 1f);
                        anim.Play(blendTreeStateName, animatorLayer, normalizedTime);
                    }
                }
            }

            Debug.Log($"GameObjectManager: {objectCount}개의 오브젝트 생성 완료");
        }

        /// <summary> 활성화 시 초기화 (모드 전환 시) </summary>
        private void OnEnable()
        {
            if (!isInitialized)
            {
                Initialize();
                isInitialized = true;
            }
        }

        /// <summary> 비활성화 시 정리 (모드 전환 시) </summary>
        private void OnDisable()
        {
            if (isInitialized)
            {
                Cleanup();
                isInitialized = false;
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

        }

        /// <summary> 파괴 시 생성된 오브젝트 정리 </summary>
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
