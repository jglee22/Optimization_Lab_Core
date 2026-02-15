using UnityEngine;

namespace OptimizationLab.Camera
{
    /// <summary>
    /// PC(마우스/키보드)와 모바일(Android 터치) 공통 카메라 컨트롤러
    /// 회전(오빗), 줌, 이동(팬) 지원
    /// </summary>
    public class CameraOrbitController : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("바라볼 중심점 (비어 있으면 월드 원점)")]
        [SerializeField] private Transform target;

        [Header("PC - Mouse / Keyboard")]
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private bool invertY = false;
        [Tooltip("마우스 버튼: 0=좌클릭, 1=우클릭, 2=휠클릭")]
        [SerializeField] private int orbitMouseButton = 1;
        [Tooltip("휠클릭(2) 드래그 = 카메라 이동(팬)")]
        [SerializeField] private int panMouseButton = 2;
        [SerializeField] private float panSpeed = 0.5f;
        [SerializeField] private float zoomScrollSpeed = 2f;
        [SerializeField] private float keyboardOrbitSpeed = 50f;
        [SerializeField] private float keyboardPanSpeed = 20f;
        [SerializeField] private KeyCode zoomInKey = KeyCode.E;
        [SerializeField] private KeyCode zoomOutKey = KeyCode.Q;

        [Header("Mobile - Touch")]
        [SerializeField] private float touchSensitivity = 0.2f;
        [SerializeField] private float pinchZoomSpeed = 0.5f;
        [SerializeField] private float twoFingerPanSpeed = 0.003f;
        [Tooltip("한 손가락 = 회전, 두 손가락 드래그 = 이동, 벌리기/모으기 = 줌")]
        [SerializeField] private bool useTwoFingerZoom = true;

        [Header("Limits")]
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 200f;
        [SerializeField] private float minPitch = -89f;
        [SerializeField] private float maxPitch = 89f;
        [Tooltip("체크 시 타겟 밑으로 카메라가 내려가지 않음")]
        [SerializeField] private bool preventBelowTarget = true;
        [Tooltip("preventBelowTarget 켜졌을 때 최소 피치(도). 수평=0, 10이면 수평에서 10° 위까지만 내려감")]
        [SerializeField] private float minPitchAboveHorizontal = 10f;

        [Header("Initial")]
        [SerializeField] private float initialDistance = 50f;
        [SerializeField] private float initialYaw = 0f;
        [SerializeField] private float initialPitch = 20f;

        private float _yaw;
        private float _pitch;
        private float _distance;
        private Vector3 _targetPosition;

        private void Start()
        {
            _targetPosition = target != null ? target.position : Vector3.zero;
            _distance = initialDistance;
            _yaw = initialYaw;
            _pitch = Mathf.Clamp(initialPitch, GetEffectiveMinPitch(), maxPitch);
            ApplyPosition();
        }

        private void LateUpdate()
        {
            if (target != null)
                _targetPosition = target.position;

            // 터치가 있으면 모바일 입력, 없으면 PC 입력 (에디터에서도 터치 시뮬 가능)
            if (Input.touchCount > 0)
                HandleMobileInput();
            else
                HandlePCInput();

            ApplyPosition();
        }

        private void HandlePCInput()
        {
            // 휠클릭(또는 pan 버튼) 드래그 = 카메라 이동(팬)
            if (Input.GetMouseButton(panMouseButton))
            {
                float dx = -Input.GetAxis("Mouse X") * panSpeed * _distance * 0.1f;
                float dz = -Input.GetAxis("Mouse Y") * panSpeed * _distance * 0.1f;
                Vector3 right = transform.right;
                right.y = 0f;
                right.Normalize();
                Vector3 forward = transform.forward;
                forward.y = 0f;
                forward.Normalize();
                _targetPosition += right * dx + forward * dz;
                if (target != null) target.position = _targetPosition;
            }
            // 마우스 드래그로 회전
            else if (Input.GetMouseButton(orbitMouseButton))
            {
                float dx = Input.GetAxis("Mouse X") * mouseSensitivity;
                float dy = Input.GetAxis("Mouse Y") * mouseSensitivity;
                if (invertY) dy = -dy;
                _yaw += dx;
                _pitch -= dy;
                _pitch = Mathf.Clamp(_pitch, GetEffectiveMinPitch(), maxPitch);
            }

            // 스크롤 줌
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _distance -= scroll * zoomScrollSpeed * _distance * 0.5f;
                _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            }

            // 키보드 회전 (방향키 또는 A/D) — 꾹 누르면 회전
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            if (Input.GetMouseButton(orbitMouseButton) == false && Input.GetMouseButton(panMouseButton) == false)
            {
                if (Mathf.Abs(h) > 0.01f)
                    _yaw += h * keyboardOrbitSpeed * Time.deltaTime;
                if (Mathf.Abs(v) > 0.01f)
                {
                    _pitch -= v * keyboardOrbitSpeed * Time.deltaTime;
                    _pitch = Mathf.Clamp(_pitch, GetEffectiveMinPitch(), maxPitch);
                }
            }

            // Shift + 방향키 = 팬 (카메라 이동)
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                Vector3 right = transform.right;
                right.y = 0f;
                right.Normalize();
                Vector3 forward = transform.forward;
                forward.y = 0f;
                forward.Normalize();
                _targetPosition += (right * h + forward * v) * keyboardPanSpeed * Time.deltaTime;
                if (target != null) target.position = _targetPosition;
            }

            // E/Q 줌
            if (Input.GetKey(zoomInKey))
            {
                _distance -= zoomScrollSpeed * _distance * 0.5f * Time.deltaTime * 10f;
                _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            }
            if (Input.GetKey(zoomOutKey))
            {
                _distance += zoomScrollSpeed * _distance * 0.5f * Time.deltaTime * 10f;
                _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            }
        }

        private void HandleMobileInput()
        {
            if (Input.touchCount == 0) return;

            if (Input.touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);
                Vector2 mid = (t0.position + t1.position) * 0.5f;
                Vector2 prevMid = (t0.position - t0.deltaPosition + t1.position - t1.deltaPosition) * 0.5f;
                Vector2 midDelta = mid - prevMid;

                // 두 손가락 드래그 = 팬(이동)
                if (midDelta.sqrMagnitude > 0.001f)
                {
                    float dx = -midDelta.x * twoFingerPanSpeed * _distance;
                    float dz = -midDelta.y * twoFingerPanSpeed * _distance;
                    Vector3 right = transform.right;
                    right.y = 0f;
                    right.Normalize();
                    Vector3 forward = transform.forward;
                    forward.y = 0f;
                    forward.Normalize();
                    _targetPosition += right * dx + forward * dz;
                    if (target != null) target.position = _targetPosition;
                }

                // 두 손가락 핀치 = 줌
                if (useTwoFingerZoom)
                {
                    Vector2 prev0 = t0.position - t0.deltaPosition;
                    Vector2 prev1 = t1.position - t1.deltaPosition;
                    float prevDist = Vector2.Distance(prev0, prev1);
                    float currDist = Vector2.Distance(t0.position, t1.position);
                    float delta = (prevDist - currDist) * pinchZoomSpeed * 0.01f;
                    _distance += delta * _distance;
                    _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
                }
            }
            else if (Input.touchCount == 1)
            {
                // 한 손가락: 드래그 회전
                Touch t = Input.GetTouch(0);
                float dx = t.deltaPosition.x * touchSensitivity;
                float dy = t.deltaPosition.y * touchSensitivity;
                if (invertY) dy = -dy;
                _yaw += dx;
                _pitch -= dy;
                _pitch = Mathf.Clamp(_pitch, GetEffectiveMinPitch(), maxPitch);
            }
        }

        /// <summary> 타겟 밑으로 안 내려가게 할 때 최소 pitch (minPitchAboveHorizontal° 이상) </summary>
        private float GetEffectiveMinPitch()
        {
            return preventBelowTarget ? Mathf.Max(minPitch, minPitchAboveHorizontal) : minPitch;
        }

        /// <summary> 현재 yaw/pitch/distance로 카메라 위치·방향 적용 </summary>
        private void ApplyPosition()
        {
            float radYaw = _yaw * Mathf.Deg2Rad;
            float radPitch = _pitch * Mathf.Deg2Rad;

            float x = Mathf.Cos(radPitch) * Mathf.Sin(radYaw);
            float y = Mathf.Sin(radPitch);
            float z = Mathf.Cos(radPitch) * Mathf.Cos(radYaw);

            Vector3 offset = new Vector3(x, y, z) * _distance;
            transform.position = _targetPosition + offset;
            transform.LookAt(_targetPosition);
        }

        /// <summary> 타겟 변경 </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (newTarget != null)
                _targetPosition = newTarget.position;
        }

        /// <summary> 현재 거리 설정 (줌) </summary>
        public void SetDistance(float distance)
        {
            _distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            _pitch = Mathf.Clamp(_pitch, GetEffectiveMinPitch(), maxPitch);
        }
#endif
    }
}
