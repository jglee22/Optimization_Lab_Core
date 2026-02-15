using UnityEngine;

namespace OptimizationLab.Helpers
{
    /// <summary>
    /// 그리드 배치 계산 공통 로직 (GameObjectManager / JobSystemManager에서 재사용)
    /// </summary>
    public static class GridLayoutHelper
    {
        /// <summary>
        /// 인덱스에 해당하는 그리드 위치 (XZ 평면, Y는 groundHeight)를 반환합니다.
        /// </summary>
        /// <param name="index">오브젝트 인덱스 (0 ~ objectCount-1)</param>
        /// <param name="objectCount">총 오브젝트 개수</param>
        /// <param name="gridSpacing">그리드 간격</param>
        /// <param name="groundHeight">바닥 높이 (Y)</param>
        /// <param name="outX">출력 X</param>
        /// <param name="outZ">출력 Z</param>
        public static void GetGridPosition(int index, int objectCount, float gridSpacing, float groundHeight,
            out float outX, out float outZ)
        {
            int cols = Mathf.CeilToInt(Mathf.Sqrt(objectCount));
            int rows = Mathf.CeilToInt((float)objectCount / cols);
            float offsetX = (cols - 1) * gridSpacing * 0.5f;
            float offsetZ = (rows - 1) * gridSpacing * 0.5f;

            int row = index / cols;
            int col = index % cols;
            outX = col * gridSpacing - offsetX;
            outZ = row * gridSpacing - offsetZ;
        }

        /// <summary>
        /// 인덱스에 해당하는 그리드 위치를 Vector3로 반환합니다.
        /// </summary>
        public static Vector3 GetGridPositionVector3(int index, int objectCount, float gridSpacing, float groundHeight)
        {
            GetGridPosition(index, objectCount, gridSpacing, groundHeight, out float x, out float z);
            return new Vector3(x, groundHeight, z);
        }
    }
}
