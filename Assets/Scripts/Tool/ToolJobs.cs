using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptimizationLab.Tool
{
    /// <summary>
    /// 브러시 배치/렌더링에 사용할 Job 묶음.
    /// - 위치/회전/스케일 → 행렬 변환
    /// - 브러시 디스크 상 산포(Scatter)
    /// - 반경 내 지우기(Eraser)
    /// </summary>
    public static class ToolJobs
    {
        // Marker class only (namespacing).
    }

    /// <summary>
    /// (position, rotation, scale) → Matrix4x4.TRS를 병렬 계산한다.
    /// CPU 쪽 리스트를 한 번에 행렬 배열로 변환할 때 사용된다.
    /// </summary>
    [BurstCompile]
    public struct MatrixTRSJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<quaternion> rotations;
        [ReadOnly] public NativeArray<float> scales;
        [WriteOnly] public NativeArray<Matrix4x4> matrices;

        public void Execute(int index)
        {
            // 인스턴스별 TRS 구성 (float3/quat/float → Matrix4x4)
            float3 p = positions[index];
            quaternion r = rotations[index];
            float s = scales[index];
            matrices[index] = Matrix4x4.TRS((Vector3)p, (Quaternion)r, Vector3.one * s);
        }
    }

    /// <summary>
    /// 브러시 스탬프(센터+노말) 목록을 입력으로 받아,
    /// 각 스탬프당 지정된 개수만큼 원형 디스크 분포로 위치/회전/스케일을 생성한다.
    /// - alignToNormal: 표면 노말을 기준으로 회전 정렬
    /// - scatterWithinRadius: 반경 안에 산포할지 여부
    /// - includeCenterInstance: 첫 인스턴스를 정중앙에 둘지 여부
    /// </summary>
    [BurstCompile]
    public struct ScatterOnDiscJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> centers;
        [ReadOnly] public NativeArray<float3> normals;

        public int instancesPerStamp;
        public float brushRadius;
        public float minScale;
        public float maxScale;
        public float randomYawRadians;
        public bool alignToNormal;
        public bool scatterWithinRadius;
        public bool includeCenterInstance;
        public uint seed;

        [WriteOnly] public NativeArray<float3> outPositions;
        [WriteOnly] public NativeArray<quaternion> outRotations;
        [WriteOnly] public NativeArray<float> outScales;

        public void Execute(int index)
        {
            int stampIndex = index / instancesPerStamp;
            int localIndex = index - (stampIndex * instancesPerStamp);

            // index 기반 RNG (결과 재현 가능) – 스탬프/인스턴스 인덱스만으로 결정
            var rnd = Unity.Mathematics.Random.CreateFromIndex(seed ^ (uint)(index * 747796405u + localIndex * 2891336453u));

            float3 center = centers[stampIndex];
            float3 n = math.normalizesafe(normals[stampIndex], new float3(0, 1, 0));

            float2 offset2 = float2.zero;
            bool shouldScatter = scatterWithinRadius && brushRadius > 0f;
            if (includeCenterInstance && localIndex == 0)
            {
                // 첫 번째 인스턴스는 스탬프 중심에 그대로 배치
                offset2 = float2.zero;
            }
            else if (shouldScatter)
            {
                // 균일 분포 디스크 (sqrt 샘플링)
                float u = rnd.NextFloat(0f, 1f);
                float v = rnd.NextFloat(0f, 1f);
                float r = math.sqrt(u) * brushRadius;
                float theta = v * (math.PI * 2f);
                offset2 = new float2(math.cos(theta), math.sin(theta)) * r;
            }

            float3 up = new float3(0, 1, 0);
            float yaw = rnd.NextFloat(-randomYawRadians, randomYawRadians);
            float s = rnd.NextFloat(minScale, maxScale);
            outScales[index] = s;

            // 위치는 항상 표면(tangent/bitangent) 평면 위에 배치 → 산맥·큐브 등 높낮이에 맞게 찍힘
            float3 tangent = math.cross(up, n);
            float tangentLenSq = math.lengthsq(tangent);
            tangent = tangentLenSq < 1e-6f ? math.normalize(math.cross(new float3(1, 0, 0), n)) : (tangent / math.sqrt(tangentLenSq));
            float3 bitangent = math.normalize(math.cross(n, tangent));
            float3 pos = center + tangent * offset2.x + bitangent * offset2.y;
            outPositions[index] = pos;

            if (!alignToNormal)
            {
                // 표면에는 붙이되, 메쉬는 항상 세워서(Y-up) Yaw만 랜덤
                outRotations[index] = quaternion.AxisAngle(up, yaw);
                return;
            }

            // up → normal 정렬 회전 (AxisAngle)
            float dot = math.clamp(math.dot(up, n), -1f, 1f);
            float angle = math.acos(dot);
            float3 axis = math.cross(up, n);
            float axisLenSq = math.lengthsq(axis);
            quaternion align;
            if (axisLenSq < 1e-8f)
            {
                // up과 normal이 (거의) 평행: 같은 방향이면 identity, 반대면 180도 회전
                align = dot < 0f ? quaternion.AxisAngle(new float3(1, 0, 0), math.PI) : quaternion.identity;
            }
            else
            {
                align = quaternion.AxisAngle(axis / math.sqrt(axisLenSq), angle);
            }

            // normal 축 기준 yaw
            quaternion yawQ = quaternion.AxisAngle(n, yaw);
            outRotations[index] = math.mul(yawQ, align);
        }
    }

    /// <summary>
    /// 주어진 중심/반경 안에 있는 인스턴스를 표시(eraseFlags=1)한다.
    /// </summary>
    [BurstCompile]
    public struct EraseWithinRadiusJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        public float3 center;
        public float radiusSq;

        /// <summary>1이면 제거 대상, 0이면 유지</summary>
        [WriteOnly] public NativeArray<byte> eraseFlags;

        public void Execute(int index)
        {
            float3 p = positions[index];
            float3 d = p - center;
            float distSq = math.dot(d, d);
            eraseFlags[index] = (byte)(distSq <= radiusSq ? 1 : 0);
        }
    }
}

