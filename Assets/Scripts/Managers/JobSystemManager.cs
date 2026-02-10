using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine; // Graphics.DrawMeshInstanced 사용을 위해 필요
using Unity.Burst; // BurstCompile 필수
using OptimizationLab.JobSystem; // PositionUpdateJob 사용을 위해 필요

namespace OptimizationLab.Managers
{
    public class JobSystemManager : MonoBehaviour
    {
        [Header("Rendering Settings")]
        [SerializeField] private Mesh instanceMesh;      // 큐브 메쉬 연결
        [SerializeField] private Material instanceMaterial; // GPU Instancing 체크된 매터리얼 연결

        [Header("Spawn Settings")]
        [SerializeField] private int objectCount = 10000; // 5만 개도 가능해짐
        [SerializeField] private float spawnRadius = 50f;

        [Header("Bounds")]
        [SerializeField] private Vector3 boundsMin = new Vector3(-50, -50, -50);
        [SerializeField] private Vector3 boundsMax = new Vector3(50, 50, 50);

        // 데이터 배열
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private NativeArray<Matrix4x4> matrices; // Job에서 계산된 행렬 배열

        // 렌더링용 매트릭스 배열 (C# 쪽에서 배치로 그리기 위함)
        private Matrix4x4[] batchMatrices;
        private List<Matrix4x4[]> batchedMatricesList; // 1023개씩 묶은 리스트

        private JobHandle positionJobHandle;
        private JobHandle matrixJobHandle;
        private bool isInitialized = false;

        private void Initialize()
        {
            // 메모리 할당
            positions = new NativeArray<float3>(objectCount, Allocator.Persistent);
            velocities = new NativeArray<float3>(objectCount, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(objectCount, Allocator.Persistent);

            // 렌더링 배칭 준비 (DrawMeshInstanced는 한 번에 1023개까지만 그려짐)
            int batchCount = Mathf.CeilToInt(objectCount / 1023f);
            batchedMatricesList = new List<Matrix4x4[]>();

            for (int i = 0; i < batchCount; i++)
            {
                // 마지막 배치는 1023개보다 적을 수 있음
                int count = (i == batchCount - 1) ? objectCount - (i * 1023) : 1023;
                batchedMatricesList.Add(new Matrix4x4[count]);
            }

            // 초기값 설정 (Random)
            var random = new Unity.Mathematics.Random(1);
            for (int i = 0; i < objectCount; i++)
            {
                positions[i] = random.NextFloat3(-spawnRadius, spawnRadius);
                velocities[i] = random.NextFloat3Direction() * 5f;
            }
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
            // Job 완료 대기
            positionJobHandle.Complete();
            matrixJobHandle.Complete();

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
            if (!isInitialized) return;

            // 이전 프레임의 Job들이 완료될 때까지 대기
            positionJobHandle.Complete();
            matrixJobHandle.Complete();

            // 1. 위치 계산 Job 스케줄링
            var positionJob = new PositionUpdateJob
            {
                positions = positions,
                velocities = velocities,
                deltaTime = Time.deltaTime,
                boundsMin = boundsMin,
                boundsMax = boundsMax
            };

            positionJobHandle = positionJob.Schedule(objectCount, 64);
            

            // 2. 행렬 변환 Job 스케줄링 (위치 계산 Job이 완료된 후 실행)
            var matrixJob = new MatrixTransformJob
            {
                positions = positions,
                matrices = matrices
            };

            matrixJobHandle = matrixJob.Schedule(objectCount, 64, positionJobHandle);
        }

        private void LateUpdate()
        {
            // 초기화되지 않았으면 실행하지 않음
            if (!isInitialized) return;

            // 행렬 변환 Job 완료 대기
            matrixJobHandle.Complete();

            // 3. 화면에 그리기 (GPU Instancing)
            // Job에서 계산된 행렬 데이터를 배치로 나눠서 렌더링합니다.
            // 메인 스레드에서는 SetTRS를 호출하지 않고, Job에서 계산된 결과만 사용합니다.

            int globalIndex = 0;
            for (int batchIndex = 0; batchIndex < batchedMatricesList.Count; batchIndex++)
            {
                Matrix4x4[] batch = batchedMatricesList[batchIndex];

                // NativeArray에서 배치 배열로 복사 (Job에서 계산된 결과 사용)
                for (int i = 0; i < batch.Length; i++)
                {
                    batch[i] = matrices[globalIndex];
                    globalIndex++;
                }

                // ★ 핵심: GameObject 없이 GPU에 바로 그리기
                Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, batch);
            }
        }

        /// <summary>
        /// 오브젝트 개수 설정
        /// </summary>
        public void SetObjectCount(int count)
        {
            if (count <= 0) return;

            // 기존 데이터 정리
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
        /// 정리 작업: Native Array 해제
        /// </summary>
        public void Cleanup()
        {
            // Job 완료 대기
            positionJobHandle.Complete();
            matrixJobHandle.Complete();

            // Native Array 해제
            if (positions.IsCreated)
                positions.Dispose();
            if (velocities.IsCreated)
                velocities.Dispose();
            if (matrices.IsCreated)
                matrices.Dispose();

            // 배치 리스트 정리
            if (batchedMatricesList != null)
            {
                batchedMatricesList.Clear();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}