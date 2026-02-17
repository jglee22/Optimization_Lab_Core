using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OptimizationLab.Tool.Editor
{
    /// <summary>
    /// 씬뷰에서 드래그(브러시)로 대량 인스턴스를 배치하는 에디터 툴.
    /// - 씬뷰 마우스 입력을 받아 레이캐스트로 히트 포인트를 계산하고
    /// - 스탬프(센터)를 기준으로 Job System에서 디스크 분포/회전/스케일을 병렬 계산한 뒤
    /// - PaintedInstancedRenderer에 누적하여 GPU Instancing으로 렌더링한다.
    /// - Shift 드래그 지우개 / 겹침 방지 / 프리셋 저장까지 포함한다.
    /// </summary>
    public sealed class MeshBrushToolWindow : EditorWindow
    {
        private const float DefaultRayDistance = 5000f;
        private const string PrefPrefix = "OptimizationLab_MeshBrush_";

        [SerializeField] private PaintedInstancedRenderer target;

        // --- Brush 기본 설정 ---
        [Header("Brush")]
        [SerializeField] private float brushRadius = 1.5f;
        [SerializeField] private float stampSpacing = 0.75f;
        [SerializeField] private int instancesPerStamp = 25;
        [SerializeField] private bool scatterWithinRadius = true;
        [SerializeField] private bool includeCenterInstance = true;
        [SerializeField] private bool alignToSurfaceNormal = true;
        [SerializeField] private float randomYawDegrees = 180f;
        [SerializeField] private float minScale = 0.9f;
        [SerializeField] private float maxScale = 1.1f;

        // --- 겹침 방지 설정 ---
        [Header("Overlap")]
        [SerializeField] private bool preventOverlap = false;
        [SerializeField] private float minSeparation = 1.0f;

        // --- Shift 지우개 설정 ---
        [Header("Eraser (Shift)")]
        [SerializeField] private bool enableShiftEraser = true;

        // --- 레이캐스트 / 히트 설정 ---
        [Header("Raycast")]
        [SerializeField] private LayerMask paintMask = ~0;
        [SerializeField] private bool fallbackToGroundPlaneY0 = true;

        [Header("Target Defaults (optional)")]
        [SerializeField] private Mesh defaultMesh;
        [SerializeField] private Material defaultMaterial;

        private bool isPainting;
        private Vector3 lastStampWorldPos;
        private uint strokeSeed;

        [MenuItem("Tools/Optimization Lab/Mesh Brush (Job System)")]
        public static void Open()
        {
            var w = GetWindow<MeshBrushToolWindow>();
            w.titleContent = new GUIContent("Mesh Brush");
            w.Show();
        }

        [InitializeOnLoadMethod]
        private static void InitOnLoad()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// 플레이 모드 전환 시에도 SceneGUI 훅/타겟이 유지되도록
        /// 열려 있는 모든 MeshBrushToolWindow에 대해 재설정한다.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 플레이 모드 전환 후에도 SceneGUI 훅이 끊기지 않게 재연결
            var windows = Resources.FindObjectsOfTypeAll<MeshBrushToolWindow>();
            foreach (var w in windows)
            {
                if (w == null) continue;
                w.EnsureSceneHooked();
                w.TryAutoAssignTarget();
            }
        }

        private void OnEnable()
        {
            EnsureSceneHooked();
            LoadPrefs();
        }

        private void OnDisable()
        {
            SavePrefs();
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnFocus()
        {
            EnsureSceneHooked();
            TryAutoAssignTarget();
            LoadPrefs();
        }

        private void OnGUI()
        {
            EnsureSceneHooked();
            TryAutoAssignTarget();
            LoadPrefs();

            EditorGUILayout.Space();
            target = (PaintedInstancedRenderer)EditorGUILayout.ObjectField("Target", target, typeof(PaintedInstancedRenderer), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Target in Scene"))
                {
                    CreateTargetInScene();
                }

                using (new EditorGUI.DisabledScope(target == null))
                {
                    if (GUILayout.Button("Clear Target"))
                    {
                        Undo.RecordObject(target, "Clear Painted Instances");
                        target.ClearAll();
                        EditorUtility.SetDirty(target);
                        SceneView.RepaintAll();
                    }
                }
            }

            EditorGUILayout.Space();
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.05f, 50f);
            stampSpacing = EditorGUILayout.Slider("Stamp Spacing", stampSpacing, 0.01f, 10f);
            instancesPerStamp = EditorGUILayout.IntSlider("Instances / Stamp", instancesPerStamp, 1, 500);
            scatterWithinRadius = EditorGUILayout.Toggle("Scatter Within Radius", scatterWithinRadius);
            includeCenterInstance = EditorGUILayout.Toggle("Include Center Instance", includeCenterInstance);
            alignToSurfaceNormal = EditorGUILayout.Toggle("Align To Normal", alignToSurfaceNormal);
            randomYawDegrees = EditorGUILayout.Slider("Random Yaw (deg)", randomYawDegrees, 0f, 180f);
            minScale = EditorGUILayout.FloatField("Min Scale", minScale);
            maxScale = EditorGUILayout.FloatField("Max Scale", maxScale);
            if (maxScale < minScale) maxScale = minScale;

            EditorGUILayout.Space();
            paintMask = LayerMaskField("Paint Mask", paintMask);
            fallbackToGroundPlaneY0 = EditorGUILayout.Toggle("Fallback Plane (Y=0)", fallbackToGroundPlaneY0);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Overlap", EditorStyles.boldLabel);
            preventOverlap = EditorGUILayout.Toggle("Prevent Overlap", preventOverlap);
            using (new EditorGUI.DisabledScope(!preventOverlap))
            {
                minSeparation = EditorGUILayout.FloatField("Min Separation", minSeparation);
                if (minSeparation < 0f) minSeparation = 0f;
                using (new EditorGUI.DisabledScope(target == null || target.InstanceMesh == null))
                {
                    if (GUILayout.Button("Set From Mesh Bounds (XZ)"))
                    {
                        var b = target.InstanceMesh.bounds;
                        float sizeXZ = Mathf.Max(b.size.x, b.size.z);
                        // 중심 간 최소 거리(대략): 메쉬 XZ 직경 기준
                        minSeparation = Mathf.Max(0.001f, sizeXZ);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Eraser", EditorStyles.boldLabel);
            enableShiftEraser = EditorGUILayout.Toggle("Enable Shift Eraser", enableShiftEraser);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target Defaults (create 시 적용)", EditorStyles.boldLabel);
            defaultMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", defaultMesh, typeof(Mesh), false);
            defaultMaterial = (Material)EditorGUILayout.ObjectField("Material", defaultMaterial, typeof(Material), false);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "씬뷰에서 좌클릭 드래그로 페인트합니다.\n" +
                "- Shift + 좌클릭 드래그로 지웁니다.\n" +
                "- Alt(카메라 조작) 중에는 페인트하지 않습니다.\n" +
                "- Target이 없으면 Create Target을 먼저 누르세요.",
                MessageType.Info
            );

            SavePrefs();
        }

        /// <summary>
        /// SceneView의 GUI 이벤트 루프.
        /// - 마우스 위치를 레이캐스트하여 브러시 프리뷰를 그려주고
        /// - 좌클릭/드래그/업 이벤트에 따라 페인트/지우개 동작을 처리한다.
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            EnsureSceneHooked();
            if (target == null) TryAutoAssignTarget();
            if (target == null) return;

            Event e = Event.current;
            if (e == null) return;

            // Alt 누른 상태(씬뷰 카메라 조작)는 제외
            if (e.alt) return;

            // 브러시 프리뷰 + 입력 처리
            if (TryGetBrushHit(e.mousePosition, sceneView.camera, out var hitPos, out var hitNormal))
            {
                bool isEraser = enableShiftEraser && e.shift;
                DrawBrushGizmo(hitPos, hitNormal, isEraser);
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        if (enableShiftEraser && e.shift) BeginErase(sceneView, e.mousePosition);
                        else BeginStroke(sceneView, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (e.button == 0 && isPainting)
                    {
                        if (enableShiftEraser && e.shift) ContinueErase(sceneView, e.mousePosition);
                        else ContinueStroke(sceneView, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (e.button == 0 && isPainting)
                    {
                        EndStroke();
                        e.Use();
                    }
                    break;
            }
        }

        private void BeginErase(SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryGetBrushHit(mousePosition, sceneView.camera, out var hitPos, out var hitNormal)) return;
            isPainting = true;
            lastStampWorldPos = hitPos;
            EraseAtHit(hitPos);
        }

        private void ContinueErase(SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryGetBrushHit(mousePosition, sceneView.camera, out var hitPos, out _)) return;
            float dist = Vector3.Distance(lastStampWorldPos, hitPos);
            if (dist < stampSpacing) return;

            Vector3 from = lastStampWorldPos;
            Vector3 to = hitPos;
            Vector3 dir = (to - from).normalized;
            int steps = Mathf.FloorToInt(dist / stampSpacing);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = from + dir * (stampSpacing * i);
                EraseAtHit(p);
            }

            lastStampWorldPos = from + dir * (stampSpacing * steps);
        }

        private void EraseAtHit(Vector3 center)
        {
            Undo.RecordObject(target, "Erase Instances (Job System)");
            int removed = target.RemoveInstancesInRadius(center, brushRadius);
            if (removed > 0)
            {
                EditorUtility.SetDirty(target);
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 브러시 스트로크 시작 (페인트 모드).
        /// 현재 마우스 히트 지점을 첫 스탬프로 사용한다.
        /// </summary>
        private void BeginStroke(SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryGetBrushHit(mousePosition, sceneView.camera, out var hitPos, out var hitNormal)) return;

            isPainting = true;
            lastStampWorldPos = hitPos;
            strokeSeed = (uint)(System.Environment.TickCount ^ target.GetInstanceID() ^ (int)(hitPos.x * 1000f) ^ (int)(hitPos.z * 1000f));

            // 첫 스탬프 즉시 생성
            StampAtHit(hitPos, hitNormal);
        }

        private void ContinueStroke(SceneView sceneView, Vector2 mousePosition)
        {
            if (!TryGetBrushHit(mousePosition, sceneView.camera, out var hitPos, out var hitNormal)) return;

            float dist = Vector3.Distance(lastStampWorldPos, hitPos);
            if (dist < stampSpacing) return;

            // 간격만큼 보간하면서 여러 스탬프 생성 (표면 재레이캐스트 없이 월드 선분 기준)
            Vector3 from = lastStampWorldPos;
            Vector3 to = hitPos;
            Vector3 dir = (to - from).normalized;
            int steps = Mathf.FloorToInt(dist / stampSpacing);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = from + dir * (stampSpacing * i);
                StampAtHit(p, hitNormal);
            }

            // 마지막으로 스탬프 찍힌 지점까지 갱신 (spacing 유지)
            lastStampWorldPos = from + dir * (stampSpacing * steps);
        }

        private void EndStroke()
        {
            isPainting = false;
        }

        /// <summary>
        /// 하나의 스탬프(중심/노말) 기준으로 Job을 돌려 인스턴스를 생성하고,
        /// Undo/Overlap 필터링까지 적용한 뒤 PaintedInstancedRenderer에 누적한다.
        /// </summary>
        private void StampAtHit(Vector3 center, Vector3 normal)
        {
            var centers = new NativeArray<float3>(1, Allocator.TempJob);
            var normals = new NativeArray<float3>(1, Allocator.TempJob);
            centers[0] = center;
            normals[0] = normal;

            int total = instancesPerStamp;
            var outPos = new NativeArray<float3>(total, Allocator.TempJob);
            var outRot = new NativeArray<quaternion>(total, Allocator.TempJob);
            var outScale = new NativeArray<float>(total, Allocator.TempJob);

            var job = new ScatterOnDiscJob
            {
                centers = centers,
                normals = normals,
                instancesPerStamp = instancesPerStamp,
                brushRadius = brushRadius,
                minScale = Mathf.Max(0.0001f, minScale),
                maxScale = Mathf.Max(0.0001f, maxScale),
                randomYawRadians = math.radians(randomYawDegrees),
                alignToNormal = alignToSurfaceNormal,
                scatterWithinRadius = scatterWithinRadius,
                includeCenterInstance = includeCenterInstance,
                seed = strokeSeed++,
                outPositions = outPos,
                outRotations = outRot,
                outScales = outScale
            };

            JobHandle handle = job.Schedule(total, 64);
            handle.Complete();

            // managed로 옮겨서 target에 누적 (Undo 가능)
            var mPos = new List<Vector3>(total);
            var mRot = new List<Quaternion>(total);
            var mScale = new List<float>(total);
            for (int i = 0; i < total; i++)
            {
                mPos.Add(outPos[i]);
                mRot.Add(outRot[i]);
                mScale.Add(outScale[i]);
            }

            if (preventOverlap && minSeparation > 0f)
            {
                FilterOverlap(center, mPos, mRot, mScale, minSeparation);
            }

            Undo.RecordObject(target, "Paint Instances (Job System)");
            target.AddInstances(mPos, mRot, mScale);
            EditorUtility.SetDirty(target);

            centers.Dispose();
            normals.Dispose();
            outPos.Dispose();
            outRot.Dispose();
            outScale.Dispose();

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 기존 인스턴스 + 현재 스탬프에서 생성된 후보들 사이의 거리를 검사해
        /// 최소 간격(separation)보다 가까운 후보는 제거한다.
        /// 공간 해시(그리드)를 사용해 O(n) 수준으로 필터링한다.
        /// </summary>
        private void FilterOverlap(Vector3 stampCenter, List<Vector3> pos, List<Quaternion> rot, List<float> scale, float separation)
        {
            if (target == null) return;
            if (pos == null || rot == null || scale == null) return;
            if (pos.Count == 0) return;

            float cellSize = Mathf.Max(0.0001f, separation);
            float sepSq = separation * separation;

            // 스탬프 근방의 기존 인스턴스 + 이번에 통과한 신규 인스턴스를 공간 해시로 관리
            var grid = new Dictionary<Vector3Int, List<Vector3>>(256);

            float range = brushRadius + separation;
            var existing = target.Positions;
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    Vector3 p = existing[i];
                    // 스탬프 중심 근방만 넣어도 충분 (성능)
                    if (Mathf.Abs(p.x - stampCenter.x) > range) continue;
                    if (Mathf.Abs(p.y - stampCenter.y) > range) continue;
                    if (Mathf.Abs(p.z - stampCenter.z) > range) continue;

                    Vector3Int c = ToCell(p, cellSize);
                    if (!grid.TryGetValue(c, out var list))
                    {
                        list = new List<Vector3>(4);
                        grid.Add(c, list);
                    }
                    list.Add(p);
                }
            }

            var keepPos = new List<Vector3>(pos.Count);
            var keepRot = new List<Quaternion>(pos.Count);
            var keepScale = new List<float>(pos.Count);

            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 p = pos[i];
                Vector3Int c = ToCell(p, cellSize);
                bool overlapped = false;

                for (int dx = -1; dx <= 1 && !overlapped; dx++)
                for (int dy = -1; dy <= 1 && !overlapped; dy++)
                for (int dz = -1; dz <= 1 && !overlapped; dz++)
                {
                    var nc = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                    if (!grid.TryGetValue(nc, out var list)) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        Vector3 q = list[k];
                        if ((p - q).sqrMagnitude <= sepSq)
                        {
                            overlapped = true;
                            break;
                        }
                    }
                }

                if (overlapped) continue;

                keepPos.Add(p);
                keepRot.Add(rot[i]);
                keepScale.Add(scale[i]);

                if (!grid.TryGetValue(c, out var cellList))
                {
                    cellList = new List<Vector3>(4);
                    grid.Add(c, cellList);
                }
                cellList.Add(p);
            }

            pos.Clear();
            rot.Clear();
            scale.Clear();
            pos.AddRange(keepPos);
            rot.AddRange(keepRot);
            scale.AddRange(keepScale);
        }

        private static Vector3Int ToCell(Vector3 p, float cellSize)
        {
            return new Vector3Int(
                Mathf.FloorToInt(p.x / cellSize),
                Mathf.FloorToInt(p.y / cellSize),
                Mathf.FloorToInt(p.z / cellSize)
            );
        }

        /// <summary>
        /// 씬뷰 마우스 위치를 월드 레이로 변환한 뒤,
        /// - 우선 Physics.Raycast로 충돌체를 찾고
        /// - 없으면 선택적으로 Y=0 평면을 히트 포인트로 사용한다.
        /// </summary>
        private bool TryGetBrushHit(Vector2 mousePosition, UnityEngine.Camera cam, out Vector3 hitPos, out Vector3 hitNormal)
        {
            hitPos = default;
            hitNormal = Vector3.up;
            if (cam == null) return false;

            UnityEngine.Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, DefaultRayDistance, paintMask))
            {
                hitPos = hit.point;
                hitNormal = hit.normal.sqrMagnitude < 1e-6f ? Vector3.up : hit.normal.normalized;
                return true;
            }

            if (!fallbackToGroundPlaneY0) return false;

            UnityEngine.Plane p = new UnityEngine.Plane(Vector3.up, Vector3.zero);
            if (!p.Raycast(ray, out float enter)) return false;
            hitPos = ray.GetPoint(enter);
            hitNormal = Vector3.up;
            return true;
        }

        /// <summary>
        /// 씬뷰에 브러시/지우개 원을 그린다.
        /// isEraser=true일 때는 붉은색으로 표현한다.
        /// </summary>
        private void DrawBrushGizmo(Vector3 center, Vector3 normal, bool isEraser)
        {
            Handles.color = isEraser ? new Color(1f, 0.25f, 0.25f, 0.95f) : new Color(0.2f, 0.8f, 1f, 0.9f);
            Handles.DrawWireDisc(center, normal, brushRadius);
        }

        private void CreateTargetInScene()
        {
            var go = new GameObject("PaintedInstancedRenderer");
            Undo.RegisterCreatedObjectUndo(go, "Create PaintedInstancedRenderer");
            target = go.AddComponent<PaintedInstancedRenderer>();

            if (defaultMesh != null) target.InstanceMesh = defaultMesh;
            if (defaultMaterial != null) target.InstanceMaterial = defaultMaterial;

            Selection.activeObject = go;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// SceneView.duringSceneGui에 OnSceneGUI를 안전하게 재구독한다.
        /// (중복 등록을 막기 위해 항상 한 번 제거 후 추가)
        /// </summary>
        private void EnsureSceneHooked()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        /// <summary>
        /// 씬에서 사용 가능한 PaintedInstancedRenderer를 자동으로 찾아
        /// 브러시 타겟으로 설정한다. (도메인 리로드/플레이 전환 대비)
        /// </summary>
        private void TryAutoAssignTarget()
        {
            if (target != null) return;

            // 씬에 존재하는 PaintedInstancedRenderer를 자동으로 잡는다 (플레이/도메인 리로드 후 끊김 대비)
            var found = UnityEngine.Object.FindFirstObjectByType<PaintedInstancedRenderer>();
            if (found != null)
            {
                target = found;
                return;
            }

            // 비활성 오브젝트까지 포함해 탐색
            var all = Resources.FindObjectsOfTypeAll<PaintedInstancedRenderer>();
            foreach (var r in all)
            {
                if (r == null) continue;
                if (!r.gameObject.scene.IsValid()) continue; // 프리팹 에셋 제외
                target = r;
                return;
            }
        }

        /// <summary>
        /// EditorPrefs에서 브러시 세팅을 로드한다.
        /// Unity 재시작/창 닫힘 이후에도 이전 설정을 복원하기 위함.
        /// </summary>
        private void LoadPrefs()
        {
            brushRadius = EditorPrefs.GetFloat(PrefPrefix + "BrushRadius", brushRadius);
            stampSpacing = EditorPrefs.GetFloat(PrefPrefix + "StampSpacing", stampSpacing);
            instancesPerStamp = EditorPrefs.GetInt(PrefPrefix + "InstancesPerStamp", instancesPerStamp);
            scatterWithinRadius = EditorPrefs.GetBool(PrefPrefix + "ScatterWithinRadius", scatterWithinRadius);
            includeCenterInstance = EditorPrefs.GetBool(PrefPrefix + "IncludeCenterInstance", includeCenterInstance);
            alignToSurfaceNormal = EditorPrefs.GetBool(PrefPrefix + "AlignToSurfaceNormal", alignToSurfaceNormal);
            randomYawDegrees = EditorPrefs.GetFloat(PrefPrefix + "RandomYawDegrees", randomYawDegrees);
            minScale = EditorPrefs.GetFloat(PrefPrefix + "MinScale", minScale);
            maxScale = EditorPrefs.GetFloat(PrefPrefix + "MaxScale", maxScale);
            enableShiftEraser = EditorPrefs.GetBool(PrefPrefix + "EnableShiftEraser", enableShiftEraser);
            preventOverlap = EditorPrefs.GetBool(PrefPrefix + "PreventOverlap", preventOverlap);
            minSeparation = EditorPrefs.GetFloat(PrefPrefix + "MinSeparation", minSeparation);
            paintMask = (LayerMask)EditorPrefs.GetInt(PrefPrefix + "PaintMask", paintMask.value);
            fallbackToGroundPlaneY0 = EditorPrefs.GetBool(PrefPrefix + "FallbackToGroundPlaneY0", fallbackToGroundPlaneY0);
        }

        /// <summary>
        /// 현재 브러시 세팅을 EditorPrefs에 저장한다.
        /// </summary>
        private void SavePrefs()
        {
            EditorPrefs.SetFloat(PrefPrefix + "BrushRadius", brushRadius);
            EditorPrefs.SetFloat(PrefPrefix + "StampSpacing", stampSpacing);
            EditorPrefs.SetInt(PrefPrefix + "InstancesPerStamp", instancesPerStamp);
            EditorPrefs.SetBool(PrefPrefix + "ScatterWithinRadius", scatterWithinRadius);
            EditorPrefs.SetBool(PrefPrefix + "IncludeCenterInstance", includeCenterInstance);
            EditorPrefs.SetBool(PrefPrefix + "AlignToSurfaceNormal", alignToSurfaceNormal);
            EditorPrefs.SetFloat(PrefPrefix + "RandomYawDegrees", randomYawDegrees);
            EditorPrefs.SetFloat(PrefPrefix + "MinScale", minScale);
            EditorPrefs.SetFloat(PrefPrefix + "MaxScale", maxScale);
            EditorPrefs.SetBool(PrefPrefix + "EnableShiftEraser", enableShiftEraser);
            EditorPrefs.SetBool(PrefPrefix + "PreventOverlap", preventOverlap);
            EditorPrefs.SetFloat(PrefPrefix + "MinSeparation", minSeparation);
            EditorPrefs.SetInt(PrefPrefix + "PaintMask", paintMask.value);
            EditorPrefs.SetBool(PrefPrefix + "FallbackToGroundPlaneY0", fallbackToGroundPlaneY0);
        }

        private static LayerMask LayerMaskField(string label, LayerMask selected)
        {
            string[] layers = InternalEditorUtility.layers;
            int maskWithoutEmpty = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                int layer = LayerMask.NameToLayer(layers[i]);
                if (layer >= 0) maskWithoutEmpty |= (1 << layer);
            }

            int field = EditorGUILayout.MaskField(label, selected.value, layers);
            selected.value = field & maskWithoutEmpty;
            return selected;
        }
    }
}

