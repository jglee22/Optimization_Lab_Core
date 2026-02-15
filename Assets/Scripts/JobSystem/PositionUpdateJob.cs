using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptimizationLab.JobSystem
{
    /// <summary>
    /// 대규모 오브젝트의 위치 연산을 병렬 처리하기 위한 Job
    /// IJobParallelFor를 사용하여 멀티스레드로 위치 업데이트 수행
    /// Burst Compiler를 통해 SIMD 최적화 및 연산 속도 극대화
    /// 
    /// BurstCompile 옵션:
    /// - FloatPrecision.Standard: 표준 정밀도 (성능과 정확도의 균형)
    /// - FloatMode.Fast: 빠른 부동소수점 연산 모드 (약간의 정밀도 손실 허용)
    /// - SafetyChecks: 기본값 true (배열 범위 체크 활성화, 릴리즈 빌드에서는 false 권장)
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct PositionUpdateJob : IJobParallelFor
    {
        /// <summary>
        /// 현재 위치 배열 (읽기/쓰기)
        /// </summary>
        public NativeArray<float3> positions;

        /// <summary>
        /// 속도 배열 (읽기 전용)
        /// </summary>
        [ReadOnly]
        public NativeArray<float3> velocities;

        /// <summary>
        /// 델타 타임 (읽기 전용)
        /// </summary>
        [ReadOnly]
        public float deltaTime;

        /// <summary>
        /// 경계 영역 (오브젝트가 이 영역을 벗어나면 반대편으로 이동)
        /// </summary>
        [ReadOnly]
        public float3 boundsMin;

        /// <summary>
        /// 경계 영역 최대값
        /// </summary>
        [ReadOnly]
        public float3 boundsMax;

        /// <summary>
        /// 각 인덱스에 대한 위치 업데이트 수행
        /// </summary>
        /// <param name="index">처리할 오브젝트의 인덱스</param>
        public void Execute(int index)
        {
            // 위치 업데이트: P = P + V * dt
            float3 newPosition = positions[index] + velocities[index] * deltaTime;

            // 1. 최소값 경계 체크 (x, y, z 동시에 비교)
            bool3 isOutMin = newPosition < boundsMin;

            // 2. x, y, z 중 true인 곳만 boundsMax로 바뀜
            newPosition = math.select(newPosition, boundsMax, isOutMin);

            // 3. 최대값 경계 체크
            bool3 isOutMax = newPosition > boundsMax;

            // 4.적용
            newPosition = math.select(newPosition, boundsMin, isOutMax);
            
            // newPosition은 스택(Stack)에 있는 임시 변수이므로,
            // 변경 사항을 힙(Native Heap)에 있는 positions 배열에 다시 저장해야 함
            positions[index] = newPosition;
        }
    }
}
