using UnityEngine;
using TMPro;
using OptimizationLab.Managers;
using UnityEngine.UI;
namespace OptimizationLab.Benchmark
{
    /// <summary>
    /// GameObject 방식과 Job System 방식의 성능을 비교하는 벤치마크 컨트롤러
    /// </summary>
    public class BenchmarkController : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private GameObjectManager gameObjectManager;
        [SerializeField] private JobSystemManager jobSystemManager;

        [Header("UI")]
        [SerializeField] private TextMeshProUGUI fpsText;
        [SerializeField] private TextMeshProUGUI objectCountText;
        [SerializeField] private TextMeshProUGUI modeText;
        [SerializeField] private Button mobileModeChangeButton;
        [SerializeField] private Button increaseButton;
        [SerializeField] private Button decreaseButton;

        [Header("Settings")]
        [SerializeField] private int[] testObjectCounts = { 1000, 5000, 10000, 20000, 50000 };
        [SerializeField] private KeyCode toggleKey = KeyCode.Space;
        [SerializeField] private KeyCode increaseKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode decreaseKey = KeyCode.DownArrow;




        private enum BenchmarkMode
        {
            GameObject,
            JobSystem,
            None
        }

        private BenchmarkMode currentMode = BenchmarkMode.None;
        private int currentObjectCountIndex = 2; // 기본값: 10000
        private float deltaTime = 0.0f;
        private float fps = 0.0f;

        private void Awake()
        {
            // 매니저 참조 확인 및 초기화
            if (gameObjectManager == null)
            {
                gameObjectManager = GetComponent<GameObjectManager>();
            }
            if (jobSystemManager == null)
            {
                jobSystemManager = GetComponent<JobSystemManager>();
            }

            // 초기 상태: 둘 다 비활성화
            if (gameObjectManager != null)
                gameObjectManager.enabled = false;
            if (jobSystemManager != null)
                jobSystemManager.enabled = false;

            Application.targetFrameRate = 60; // 혹은 -1 (무제한)
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private void Start()
        {
            // 초기 모드: 오브젝트 → 버튼 누르면 인스턴스 렌더링으로 전환
            SwitchMode(BenchmarkMode.GameObject);
            UpdateObjectCount();
            ObjectCountChangeButtons();
            ModeChangeButton();
        }

        private void Update()
        {
            // FPS 계산
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            fps = 1.0f / deltaTime;

            // 입력 처리
            if (Input.GetKeyDown(toggleKey))
            {
                Debug.Log("스페이스바 입력 감지 - 모드 전환");
                ToggleMode();
            }

            if (Input.GetKeyDown(increaseKey))
            {
                IncreaseObjectCount();
            }

            if (Input.GetKeyDown(decreaseKey))
            {
                DecreaseObjectCount();
            }

            // UI 업데이트
            UpdateUI();
        }

        /// <summary>
        /// 모드 전환 (GameObject <-> JobSystem)
        /// </summary>
        private void ToggleMode()
        {
            if (currentMode == BenchmarkMode.GameObject)
            {
                SwitchMode(BenchmarkMode.JobSystem);
            }
            else
            {
                SwitchMode(BenchmarkMode.GameObject);
            }
        }

        /// <summary>
        /// 모드 전환
        /// </summary>
        private void SwitchMode(BenchmarkMode mode)
        {
            // 기존 모드 비활성화 (컴포넌트의 enabled 속성 사용)
            if (gameObjectManager != null)
                gameObjectManager.enabled = false;
            if (jobSystemManager != null)
                jobSystemManager.enabled = false;

            currentMode = mode;

            // 새 모드 활성화
            switch (mode)
            {
                case BenchmarkMode.GameObject:
                    if (gameObjectManager != null)
                    {
                        gameObjectManager.enabled = true;
                        gameObjectManager.SetObjectCount(testObjectCounts[currentObjectCountIndex]);
                    }
                    break;

                case BenchmarkMode.JobSystem:
                    if (jobSystemManager != null)
                    {
                        jobSystemManager.enabled = true;
                        jobSystemManager.SetObjectCount(testObjectCounts[currentObjectCountIndex]);
                    }
                    break;
            }

            Debug.Log($"모드 전환: {mode}");
        }

        /// <summary>
        /// 오브젝트 개수 증가
        /// </summary>
        private void IncreaseObjectCount()
        {
            if (currentObjectCountIndex < testObjectCounts.Length - 1)
            {
                currentObjectCountIndex++;
                UpdateObjectCount();
            }
        }

        /// <summary>
        /// 오브젝트 개수 감소
        /// </summary>
        private void DecreaseObjectCount()
        {
            if (currentObjectCountIndex > 0)
            {
                currentObjectCountIndex--;
                UpdateObjectCount();
            }
        }

        /// <summary>
        /// 오브젝트 개수 업데이트
        /// </summary>
        private void UpdateObjectCount()
        {
            int count = testObjectCounts[currentObjectCountIndex];

            switch (currentMode)
            {
                case BenchmarkMode.GameObject:
                    if (gameObjectManager != null)
                        gameObjectManager.SetObjectCount(count);
                    break;

                case BenchmarkMode.JobSystem:
                    if (jobSystemManager != null)
                        jobSystemManager.SetObjectCount(count);
                    break;
            }
        }

        /// <summary>
        /// UI 업데이트
        /// </summary>
        private void UpdateUI()
        {
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {fps:F1}";
            }

            if (objectCountText != null)
            {
                int count = testObjectCounts[currentObjectCountIndex];
                objectCountText.text = $"Objects: {count:N0}\n(↑/↓: Change)";
            }

            if (modeText != null)
            {
                string modeName = currentMode == BenchmarkMode.GameObject ? "GameObject" : "Job System";
                modeText.text = $"Mode: {modeName}\n(Space: Toggle)";
            }
        }

        /// <summary>
        /// 오브젝트 개수 증감 버튼에 리스너 연결
        /// </summary>
        private void ObjectCountChangeButtons()
        {
            if (increaseButton != null)
                increaseButton.onClick.AddListener(IncreaseObjectCount);
            if (decreaseButton != null)
                decreaseButton.onClick.AddListener(DecreaseObjectCount);
        }

        /// <summary>
        /// 모드 전환 버튼에 리스너 연결 (오브젝트 ↔ 인스턴스 렌더링)
        /// </summary>
        private void ModeChangeButton()
        {
            if (mobileModeChangeButton != null)
                mobileModeChangeButton.onClick.AddListener(ToggleMode);
        }

        private void OnGUI()
        {
            // 추가 정보 표시
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label($"Current Mode: {currentMode}");
            GUILayout.Label($"Object Count: {testObjectCounts[currentObjectCountIndex]:N0}");
            GUILayout.Label($"FPS: {fps:F1}");
            GUILayout.Label($"Delta Time: {deltaTime * 1000:F2} ms");
            GUILayout.EndArea();
        }
    }
}
