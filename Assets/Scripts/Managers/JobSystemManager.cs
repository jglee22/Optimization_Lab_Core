using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;
using OptimizationLab.JobSystem;
using OptimizationLab.Helpers;
using OptimizationLab.Tool;

namespace OptimizationLab.Managers
{
    /// <summary>
    /// Job System + GPU Instancing으로 대량 오브젝트를 그리드 배치·렌더링하는 매니저.
    /// idle/walk/run 매터리얼을 랜덤 적용할 수 있음.
    /// </summary>
    public class JobSystemManager : MonoBehaviour
    {
        [Header("Rendering Settings")]
        [Tooltip("인스턴스로 그릴 메쉬 (큐브, 구, 커스텀 메쉬 등)")]
        [SerializeField] private Mesh instanceMesh;
        [Tooltip("idle/walk/run 모두 비어 있으면 아래 단일 매터리얼 사용")]
        [SerializeField] private Material instanceMaterial;
        [Header("Idle / Walk / Run 매터리얼 (랜덤 적용)")]
        [SerializeField] private Material materialIdle;
        [SerializeField] private Material materialWalk;
        [SerializeField] private Material materialRun;

        [Header("Spawn Settings")]
        [SerializeField] private int objectCount = 10000;
        [Tooltip("그리드 간격 (오브젝트 사이 거리)")]
        [SerializeField] private float gridSpacing = 2f;
        [Tooltip("바닥 높이 (Y)")]
        [SerializeField] private float groundHeight = 0f;

        // 데이터 배열
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private NativeArray<Matrix4x4> matrices;
        private NativeArray<int> materialIndices; // 0=idle, 1=walk, 2=run

        // 렌더링용: 3가지 매터리얼별 배치 리스트 (1023개씩)
        private List<Matrix4x4[]> batchedMatricesList;
        private List<Matrix4x4[]> batchedMatricesIdle;
        private List<Matrix4x4[]> batchedMatricesWalk;
        private List<Matrix4x4[]> batchedMatricesRun;
        private bool useThreeMaterials;

        private JobHandle positionJobHandle;
        private JobHandle matrixJobHandle;
        private bool isInitialized = false;

        private void Initialize()
        {
            positions = new NativeArray<float3>(objectCount, Allocator.Persistent);
            velocities = new NativeArray<float3>(objectCount, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(objectCount, Allocator.Persistent);
            materialIndices = new NativeArray<int>(objectCount, Allocator.Persistent);

            useThreeMaterials = materialIdle != null && materialWalk != null && materialRun != null;

            if (useThreeMaterials)
            {
                // 인스턴스마다 랜덤하게 idle(0) / walk(1) / run(2) 매터리얼 부여
                var random = new Unity.Mathematics.Random((uint)(System.Environment.TickCount + GetInstanceID()));
                int countIdle = 0, countWalk = 0, countRun = 0;
                for (int i = 0; i < objectCount; i++)
                {
                    int m = random.NextInt(0, 3);
                    materialIndices[i] = m;
                    if (m == 0) countIdle++; else if (m == 1) countWalk++; else countRun++;
                }
                batchedMatricesList = null;
                batchedMatricesIdle = CreateBatchedList(countIdle);
                batchedMatricesWalk = CreateBatchedList(countWalk);
                batchedMatricesRun = CreateBatchedList(countRun);
            }
            else
            {
                int batchCount = Mathf.CeilToInt(objectCount / 1023f);
                batchedMatricesList = new List<Matrix4x4[]>();
                for (int i = 0; i < batchCount; i++)
                {
                    int count = (i == batchCount - 1) ? objectCount - (i * 1023) : 1023;
                    batchedMatricesList.Add(new Matrix4x4[count]);
                }
                batchedMatricesIdle = batchedMatricesWalk = batchedMatricesRun = null;
            }

            // 일정 간격 그리드로 배치 (움직임 없음, 속도 0) — GridLayoutHelper 공통 사용
            for (int i = 0; i < objectCount; i++)
            {
                GridLayoutHelper.GetGridPosition(i, objectCount, gridSpacing, groundHeight, out float x, out float z);
                positions[i] = new float3(x, groundHeight, z);
                velocities[i] = float3.zero;
            }
        }

        /// <summary>
        /// DrawMeshInstanced용 1023개 단위 배치 리스트 생성
        /// </summary>
        private List<Matrix4x4[]> CreateBatchedList(int totalCount)
        {
            var list = new List<Matrix4x4[]>();
            int batchCount = Mathf.CeilToInt(totalCount / 1023f);
            for (int i = 0; i < batchCount; i++)
            {
                int count = (i == batchCount - 1) ? totalCount - (i * 1023) : 1023;
                list.Add(new Matrix4x4[count]);
            }
            return list;
        }

        /// <summary>
        /// 씬에 브러시로 배치한 인스턴스가 하나라도 있으면 true.
        /// (비활성 오브젝트까지 포함, 프리팹 에셋은 제외)
        /// </summary>
        private static bool HasPaintedInstancesInScene()
        {
            var all = Resources.FindObjectsOfTypeAll<PaintedInstancedRenderer>();
            foreach (var p in all)
            {
                if (p == null) continue;
                if (!p.gameObject.scene.IsValid()) continue; // 프리팹 에셋 제외
                if (p.Count > 0) return true;
            }
            return false;
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

        /// <summary> 비활성화 시 Job 완료 대기 후 정리 (모드 전환 시) </summary>
        private void OnDisable()
        {
            positionJobHandle.Complete();
            matrixJobHandle.Complete();
            if (isInitialized)
            {
                Cleanup();
                isInitialized = false;
            }
        }

        /// <summary> 매 프레임 위치 Job → 행렬 Job 스케줄링 </summary>
        private void Update()
        {
            if (!isInitialized) return;
            positionJobHandle.Complete();
            matrixJobHandle.Complete();

            // 1. 위치 계산 Job (속도 0이라 위치 변화 없음, 행렬만 갱신용)
            var positionJob = new PositionUpdateJob
            {
                positions = positions,
                velocities = velocities,
                deltaTime = Time.deltaTime,
                boundsMin = new float3(-1e10f, groundHeight, -1e10f),
                boundsMax = new float3(1e10f, groundHeight, 1e10f)
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

        /// <summary> 행렬 Job 완료 후 매터리얼별로 GPU Instancing 드로우 </summary>
        private void LateUpdate()
        {
            if (!isInitialized) return;
            matrixJobHandle.Complete();
            if (useThreeMaterials)
            {
                int[] writeIdx = { 0, 0, 0 };
                for (int i = 0; i < objectCount; i++)
                {
                    int m = materialIndices[i];
                    var list = m == 0 ? batchedMatricesIdle : (m == 1 ? batchedMatricesWalk : batchedMatricesRun);
                    int bi = writeIdx[m] / 1023;
                    int si = writeIdx[m] % 1023;
                    list[bi][si] = matrices[i];
                    writeIdx[m]++;
                }
                Material matIdle = materialIdle ?? instanceMaterial;
                Material matWalk = materialWalk ?? instanceMaterial;
                Material matRun = materialRun ?? instanceMaterial;
                for (int b = 0; b < batchedMatricesIdle.Count; b++)
                { if (batchedMatricesIdle[b].Length > 0) Graphics.DrawMeshInstanced(instanceMesh, 0, matIdle, batchedMatricesIdle[b]); }
                for (int b = 0; b < batchedMatricesWalk.Count; b++)
                { if (batchedMatricesWalk[b].Length > 0) Graphics.DrawMeshInstanced(instanceMesh, 0, matWalk, batchedMatricesWalk[b]); }
                for (int b = 0; b < batchedMatricesRun.Count; b++)
                { if (batchedMatricesRun[b].Length > 0) Graphics.DrawMeshInstanced(instanceMesh, 0, matRun, batchedMatricesRun[b]); }
            }
            else
            {
                int globalIndex = 0;
                for (int batchIndex = 0; batchIndex < batchedMatricesList.Count; batchIndex++)
                {
                    Matrix4x4[] batch = batchedMatricesList[batchIndex];
                    for (int i = 0; i < batch.Length; i++)
                        batch[i] = matrices[globalIndex++];
                    if (instanceMaterial != null)
                        Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, batch);
                }
            }
        }

        /// <summary>
        /// 오브젝트 개수 설정.
        /// 씬에 브러시로 배치한 인스턴스가 있으면 0으로 덮어써서 그리드 생성을 건너뛰고, 배치된 것만 렌더링한다.
        /// </summary>
        public void SetObjectCount(int count)
        {
            if (count < 0) return;
            if (HasPaintedInstancesInScene())
                count = 0;

            // 기존 데이터 정리
            Cleanup();
            isInitialized = false;

            objectCount = count;

            // 활성화되어 있을 때만 즉시 초기화 (0이어도 빈 배열로 초기화해 두어야 Update/LateUpdate가 안전함)
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
            if (materialIndices.IsCreated)
                materialIndices.Dispose();

            batchedMatricesList?.Clear();
            batchedMatricesIdle?.Clear();
            batchedMatricesWalk?.Clear();
            batchedMatricesRun?.Clear();
        }

        /// <summary> 파괴 시 Native 배열 및 배치 리스트 해제 </summary>
        private void OnDestroy()
        {
            Cleanup();
        }
    }
}