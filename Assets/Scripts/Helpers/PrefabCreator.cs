using UnityEngine;

namespace OptimizationLab.Helpers
{
    /// <summary>
    /// 벤치마크용 프리팹을 생성하는 헬퍼 스크립트
    /// </summary>
    public static class PrefabCreator
    {
        /// <summary>
        /// 간단한 큐브 프리팹 생성
        /// </summary>
        public static GameObject CreateSimpleCubePrefab(string name = "BenchmarkCube")
        {
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = name;
            
            // 머티리얼 설정 (옵션)
            Renderer renderer = prefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(Random.value, Random.value, Random.value, 1f);
                renderer.material = mat;
            }

            // 콜라이더 제거 (성능 최적화를 위해, 필요시 주석 해제)
            // Collider collider = prefab.GetComponent<Collider>();
            // if (collider != null)
            //     Object.DestroyImmediate(collider);

            return prefab;
        }

        /// <summary>
        /// 간단한 스피어 프리팹 생성
        /// </summary>
        public static GameObject CreateSimpleSpherePrefab(string name = "BenchmarkSphere")
        {
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prefab.name = name;
            
            Renderer renderer = prefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(Random.value, Random.value, Random.value, 1f);
                renderer.material = mat;
            }

            return prefab;
        }
    }
}
