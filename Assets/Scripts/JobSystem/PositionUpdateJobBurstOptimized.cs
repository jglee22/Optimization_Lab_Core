using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptimizationLab.JobSystem
{
    /// <summary>
    /// Burst Compiler 최적화 버전 (Safety Check 우회)
    /// 프로덕션 빌드에서 최대 성능을 위해 Safety Check를 비활성화한 버전
    /// 
    /// 주의: Safety Check를 비활성화하면 배열 범위 체크가 없어지므로,
    /// 잘못된 인덱스 접근 시 크래시가 발생할 수 있습니다.
    /// 디버깅 시에는 SafetyChecks = true를 사용하세요.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct PositionUpdateJobBurstOptimized : IJobParallelFor
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
        /// Burst Compiler가 SIMD 명령어로 자동 최적화합니다.
        /// </summary>
        /// <param name="index">처리할 오브젝트의 인덱스</param>
        public void Execute(int index)
        {
            // 위치 업데이트: P = P + V * dt
            // Unity.Mathematics의 float3 연산은 Burst가 SIMD로 최적화합니다
            float3 newPosition = positions[index] + velocities[index] * deltaTime;

            // 경계 체크 및 래핑 (경계를 벗어나면 반대편으로 이동)
            // math.select를 사용하면 Burst가 더 효율적으로 최적화할 수 있습니다
            newPosition.x = math.select(
                math.select(newPosition.x, boundsMax.x, newPosition.x < boundsMin.x),
                boundsMin.x,
                newPosition.x > boundsMax.x
            );

            newPosition.y = math.select(
                math.select(newPosition.y, boundsMax.y, newPosition.y < boundsMin.y),
                boundsMin.y,
                newPosition.y > boundsMax.y
            );

            newPosition.z = math.select(
                math.select(newPosition.z, boundsMax.z, newPosition.z < boundsMin.z),
                boundsMin.z,
                newPosition.z > boundsMax.z
            );

            positions[index] = newPosition;
        }
    }
}
