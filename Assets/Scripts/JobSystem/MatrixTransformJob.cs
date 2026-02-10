using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptimizationLab.JobSystem
{
    /// <summary>
    /// 위치 데이터를 Matrix4x4로 변환하는 Job
    /// SetTRS 연산을 병렬 처리하여 메인 스레드 부하를 제거합니다.
    /// 
    /// 이 Job을 사용하면 LateUpdate에서 SetTRS를 호출하는 대신,
    /// Job에서 병렬로 행렬 변환을 수행하여 성능을 크게 향상시킬 수 있습니다.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct MatrixTransformJob : IJobParallelFor
    {
        /// <summary>
        /// 입력: 위치 배열 (읽기 전용)
        /// </summary>
        [ReadOnly]
        public NativeArray<float3> positions;

        /// <summary>
        /// 출력: 변환된 행렬 배열 (쓰기 전용)
        /// </summary>
        [WriteOnly]
        public NativeArray<Matrix4x4> matrices;

        /// <summary>
        /// 각 인덱스에 대한 행렬 변환 수행
        /// Unity.Mathematics를 사용하여 Burst가 최적화할 수 있도록 합니다.
        /// </summary>
        /// <param name="index">처리할 오브젝트의 인덱스</param>
        public void Execute(int index)
        {
            // float3 위치 가져오기
            float3 pos = positions[index];

            // Matrix4x4를 직접 구성 (SetTRS보다 빠름)
            // TRS: Translation, Rotation(identity), Scale(one)
            // Burst가 최적화할 수 있도록 Unity.Mathematics 사용
            matrices[index] = float4x4.Translate(pos);
        }
    }
}
