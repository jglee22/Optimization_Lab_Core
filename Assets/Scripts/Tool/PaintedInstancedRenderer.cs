using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptimizationLab.Tool
{
    /// <summary>
    /// 에디터/런타임에서 공통으로 사용할 수 있는 간단한 인스턴스 렌더러.
    /// - 위치/회전/스케일 목록을 보관하고
    /// - Job System(+Burst 가능)로 행렬을 병렬 계산한 뒤
    /// - Graphics.DrawMeshInstanced로 1023개 단위로 드로우한다.
    ///
    /// 브러시 툴에서 이 컴포넌트에 대량 배치를 누적하는 방식으로 사용한다.
    /// </summary>
    [ExecuteAlways]
    public sealed class PaintedInstancedRenderer : MonoBehaviour
    {
        private const int MaxBatchSize = 1023;

        [Header("Rendering")]
        // 브러시/툴에서 그릴 실제 메쉬와 머티리얼
        [SerializeField] private Mesh instanceMesh;
        [SerializeField] private Material instanceMaterial;
        [SerializeField] private ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        [SerializeField] private bool receiveShadows = true;

        [Header("Instances (Serialized)")]
        [SerializeField] private List<Vector3> positions = new();
        [SerializeField] private List<Quaternion> rotations = new();
        [SerializeField] private List<float> scales = new();

        [Header("Debug")]
        [SerializeField] private bool rebuildMatricesEveryFrame = false;

        private NativeArray<float3> positionsNative;
        private NativeArray<quaternion> rotationsNative;
        private NativeArray<float> scalesNative;
        private NativeArray<Matrix4x4> matricesNative;

        private Matrix4x4[] matricesManaged;
        private List<Matrix4x4[]> batchedMatrices;

        private JobHandle matrixJobHandle;
        private bool isInitialized;
        private bool isDirty;

        /// <summary>
        /// 현재 씬에 존재하는 인스턴스 개수.
        /// 직렬화된 <see cref="positions"/> 리스트의 길이와 동일하다.
        /// </summary>
        public int Count => positions?.Count ?? 0;

        /// <summary>
        /// 인스턴스의 월드 위치 리스트(ReadOnly).
        /// MeshBrush 툴에서 겹침 검사 등에 사용한다.
        /// </summary>
        public IReadOnlyList<Vector3> Positions => positions;

        /// <summary>
        /// GPU Instancing에 사용할 메쉬.
        /// 변경 시 행렬 재빌드가 필요하므로 더티 플래그를 건다.
        /// </summary>
        public Mesh InstanceMesh
        {
            get => instanceMesh;
            set { instanceMesh = value; isDirty = true; }
        }

        /// <summary>
        /// GPU Instancing에 사용할 머티리얼.
        /// </summary>
        public Material InstanceMaterial
        {
            get => instanceMaterial;
            set { instanceMaterial = value; }
        }

        private void OnEnable()
        {
            // 도메인 리로드/활성화 시 항상 한 번은 리빌드 되도록 표시
            isDirty = true;
            EnsureInitialized();
            SubscribeCameraEvents();
        }

        private void OnDisable()
        {
            UnsubscribeCameraEvents();
            DisposeNative();
        }

        private void OnDestroy()
        {
            DisposeNative();
        }

        /// <summary>
        /// 브러시 툴 등에서 인스턴스를 한 번에 추가한다.
        /// (Undo 처리는 호출자(에디터 툴)에서 수행)
        /// </summary>
        public void AddInstances(IReadOnlyList<Vector3> addPositions, IReadOnlyList<Quaternion> addRotations, IReadOnlyList<float> addScales)
        {
            if (addPositions == null || addRotations == null || addScales == null) return;
            if (addPositions.Count != addRotations.Count || addPositions.Count != addScales.Count) return;
            if (addPositions.Count == 0) return;

            positions ??= new List<Vector3>();
            rotations ??= new List<Quaternion>();
            scales ??= new List<float>();

            positions.AddRange(addPositions);
            rotations.AddRange(addRotations);
            scales.AddRange(addScales);
            isDirty = true;
        }

        public void ClearAll()
        {
            positions?.Clear();
            rotations?.Clear();
            scales?.Clear();
            // 데이터가 비었음을 알리기 위해 더티
            isDirty = true;
        }

        /// <summary>
        /// 주어진 중심/반경 안에 있는 인스턴스를 삭제한다. (에디터 툴: Shift 지우개 용도)
        /// Undo/Dirty 처리는 호출자(에디터)에서 수행.
        /// </summary>
        public int RemoveInstancesInRadius(Vector3 center, float radius)
        {
            int count = Count;
            if (count <= 0) return 0;
            if (radius <= 0f) return 0;

            float radiusSq = radius * radius;

            // positions만 Job으로 판정하고, 결과를 바탕으로 리스트를 재구성한다.
            var posTemp = new NativeArray<float3>(count, Allocator.TempJob);
            var flags = new NativeArray<byte>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++) posTemp[i] = positions[i];

            var job = new EraseWithinRadiusJob
            {
                positions = posTemp,
                center = center,
                radiusSq = radiusSq,
                eraseFlags = flags
            };

            JobHandle h = job.Schedule(count, 128);
            h.Complete();

            int removed = 0;
            // 새 리스트로 keep만 복사 (RemoveAt 반복 O(n^2) 방지)
            var newPositions = new List<Vector3>(count);
            var newRotations = new List<Quaternion>(count);
            var newScales = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                if (flags[i] == 1)
                {
                    removed++;
                    continue;
                }
                newPositions.Add(positions[i]);
                newRotations.Add(rotations[i]);
                newScales.Add(scales[i]);
            }

            posTemp.Dispose();
            flags.Dispose();

            if (removed == 0) return 0;

            // 실제 데이터 리스트를 필터링된 새 리스트로 교체
            positions = newPositions;
            rotations = newRotations;
            scales = newScales;
            isDirty = true;
            return removed;
        }

        /// <summary>
        /// 에디터/런타임 공통 Update 루프.
        /// - 필요 시 더티 플래그를 갱신하고
        /// - 행렬 캐시를 리빌드한다.
        /// </summary>
        private void Update()
        {
            if (!isActiveAndEnabled) return;
            if (rebuildMatricesEveryFrame) isDirty = true;
            EnsureInitialized();
            RebuildIfNeeded();
        }

        /// <summary>
        /// 한 번만 실행되는 초기화 가드.
        /// 실제 Allocation은 <see cref="RebuildIfNeeded"/>에서 수행한다.
        /// </summary>
        private void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;
            isDirty = true;
        }

        /// <summary>
        /// 더티 플래그가 켜져 있을 때 NativeArray/배치 행렬을 다시 구성한다.
        /// - positions/rotations/scales → MatrixTRSJob으로 행렬 계산
        /// - Graphics.DrawMeshInstanced가 요구하는 1023 단위로 배치 리스트 생성
        /// </summary>
        private void RebuildIfNeeded()
        {
            if (!isDirty) return;
            isDirty = false;

            matrixJobHandle.Complete();

            int count = Count;
            DisposeNative();

            if (count <= 0) return;

            positionsNative = new NativeArray<float3>(count, Allocator.Persistent);
            rotationsNative = new NativeArray<quaternion>(count, Allocator.Persistent);
            scalesNative = new NativeArray<float>(count, Allocator.Persistent);
            matricesNative = new NativeArray<Matrix4x4>(count, Allocator.Persistent);

            for (int i = 0; i < count; i++)
            {
                positionsNative[i] = positions[i];
                rotationsNative[i] = rotations[i];
                scalesNative[i] = scales[i];
            }

            var job = new MatrixTRSJob
            {
                positions = positionsNative,
                rotations = rotationsNative,
                scales = scalesNative,
                matrices = matricesNative
            };

            matrixJobHandle = job.Schedule(count, 64);
            matrixJobHandle.Complete();

            matricesManaged = new Matrix4x4[count];
            matricesNative.CopyTo(matricesManaged);

            // 1023개 단위로 분할 (DrawMeshInstanced 제한)
            int batchCount = Mathf.CeilToInt(count / (float)MaxBatchSize);
            batchedMatrices = new List<Matrix4x4[]>(batchCount);
            for (int b = 0; b < batchCount; b++)
            {
                int start = b * MaxBatchSize;
                int len = Mathf.Min(MaxBatchSize, count - start);
                var batch = new Matrix4x4[len];
                Array.Copy(matricesManaged, start, batch, 0, len);
                batchedMatrices.Add(batch);
            }
        }

        /// <summary>
        /// 카메라 단위로 GPU Instancing 드로우를 수행한다.
        /// Built-in / SRP 모두에서 호출될 수 있다.
        /// </summary>
        private void Render(UnityEngine.Camera cam)
        {
            if (cam == null) return;
            if (instanceMesh == null || instanceMaterial == null) return;
            if (batchedMatrices == null || batchedMatrices.Count == 0) return;

            // 씬뷰/게임뷰 모두 지원 (카메라별 호출)
            for (int i = 0; i < batchedMatrices.Count; i++)
            {
                var batch = batchedMatrices[i];
                if (batch == null || batch.Length == 0) continue;
                Graphics.DrawMeshInstanced(
                    instanceMesh,
                    0,
                    instanceMaterial,
                    batch,
                    batch.Length,
                    null,
                    shadowCasting,
                    receiveShadows,
                    gameObject.layer,
                    cam
                );
            }
        }

        private void OnRenderObject()
        {
            // Built-in 렌더 파이프라인에서는 OnRenderObject 기반으로 렌더링
            if (!isActiveAndEnabled) return;
            if (GraphicsSettings.currentRenderPipeline != null) return; // SRP에서는 이벤트로만 렌더
            EnsureInitialized();
            RebuildIfNeeded();
            Render(UnityEngine.Camera.current);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera cam)
        {
            if (!isActiveAndEnabled) return;
            if (GraphicsSettings.currentRenderPipeline == null) return; // Built-in에서는 OnRenderObject 사용
            EnsureInitialized();
            RebuildIfNeeded();
            Render(cam);
        }

        private void SubscribeCameraEvents()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void UnsubscribeCameraEvents()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void DisposeNative()
        {
            // 혹시 남아있을지 모르는 Job을 우선 완료시킨다.
            matrixJobHandle.Complete();

            if (positionsNative.IsCreated) positionsNative.Dispose();
            if (rotationsNative.IsCreated) rotationsNative.Dispose();
            if (scalesNative.IsCreated) scalesNative.Dispose();
            if (matricesNative.IsCreated) matricesNative.Dispose();

            matricesManaged = null;
            batchedMatrices?.Clear();
            batchedMatrices = null;
        }
    }
}

