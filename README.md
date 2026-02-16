# 🚀 Unity DOTS Benchmark: 대규모 오브젝트 시뮬레이션

> **프로젝트 개요**: Unity의 **Job System, Burst Compiler, GPU Instancing** 기술을 활용하여 50,000개 이상의 객체를 모바일 환경에서 60 FPS로 시뮬레이션하는 고성능 최적화 데모입니다.

**Unity 버전**: 6000.0.56f1 (Unity 6)

---

## 📌 빠른 시작 (Quick Start)

1. **Unity**에서 프로젝트 열기
2. 벤치마크 씬 실행 후 **시작 모드**: 오브젝트 방식 → **버튼/스페이스**로 인스턴스 렌더링 모드 전환
3. **카메라**: PC는 우클릭 회전·휠 줌·휠클릭 팬, 모바일은 한 손가락 회전·두 손가락 팬/줌
4. **오브젝트 개수**: UI 버튼 또는 ↑/↓ 키로 변경 (1000 ~ 50000)

---

## 📊 성능 테스트 결과 (모바일)
테스트 기기: **Galaxy Z Fold 3** (Snapdragon 888)

### 객체 수 10,000개

| 모드 (Mode) | FPS | 비고 |
|:---:|:---:|:---|
| **GameObject** | ~10 FPS | 메인 스레드 병목, 드로우콜 과다 |
| **인스턴스(VAT)** | **~40 FPS** | Job System + GPU Instancing |

### 객체 수 50,000개

| 모드 (Mode) | FPS | 상태 | 분석 결과 |
|:---:|:---:|:---:|:---|
| **GameObject** | ~3 FPS | 플레이 불가 | 메인 스레드 병목, 대량의 GC 발생, 드로우콜 과다 |
| **Job System** | **59.9 FPS** | **매우 쾌적** | **약 400% 성능 향상**, 병렬 처리, Zero GC 달성 |

---

## 🛠️ 핵심 기술 구현 (Key Technologies)

단순한 API 사용을 넘어, 하드웨어 아키텍처를 고려한 **Low-Level 최적화 기법**을 적용했습니다.

### 1. Branchless Programming & SIMD (Burst)
CPU의 분기 예측 실패(Branch Misprediction) 비용을 제거하기 위해 `if-else` 제어문을 제거했습니다.
- **구현 파일**: `PositionUpdateJob.cs`
- **적용 기술**: `math.select`와 `bool3` 마스크 연산을 활용하여, 경계(Boundary) 체크 로직을 분기 없는 단일 파이프라인으로 처리했습니다.

### 2. 스레드 의존성 관리 (Dependency Chaining)
데이터 레이스(Data Race)를 방지하고 메인 스레드의 대기 시간(Stall)을 최소화했습니다.
- **구현 파일**: `JobSystemManager.cs`
- **적용 기술**: `PositionUpdateJob`(물리 연산)이 완료된 후 `MatrixTransformJob`(렌더링 데이터 변환)이 수행되도록 `JobHandle`을 체이닝하여 워커 스레드 간의 실행 순서를 보장했습니다.

### 3. GPU Instancing 및 배칭 (Batching)
`GameObject`의 Transform 연산 오버헤드를 완전히 제거했습니다.
- **구현 파일**: `JobSystemManager.cs`
- **적용 기술**: `NativeArray`로 계산된 행렬 데이터를 1023개 단위로 배칭(Batching) 처리하여 `Graphics.DrawMeshInstanced` API를 통해 GPU에 직접 그리기 명령을 전달합니다.

### 4. Zero Garbage Collection (GC)
런타임 중 힙(Heap) 메모리 할당을 0으로 억제했습니다.
- **메모리 관리**: 모든 연산 데이터는 `NativeArray<T>` (Unmanaged Memory)에서 관리되며, 벤치마크 모드 전환 시 `Setup/Cleanup` 프로세스를 통해 메모리 누수를 원천 차단했습니다.

---

## 💻 프로젝트 구조

```text
Assets/Scripts/
├── 📂 Managers
│   ├── GameObjectManager.cs     // 대조군 (GameObject + Blend Tree 애니메이션)
│   └── JobSystemManager.cs     // ★ 인스턴스 렌더링 (NativeArray, 3매터리얼 배칭)
├── 📂 JobSystem
│   ├── PositionUpdateJob.cs    // [Burst] SIMD 위치 연산 (math.select)
│   ├── MatrixTransformJob.cs   // [Burst] 행렬 변환
│   └── PositionUpdateJobBurstOptimized.cs  // 선택) 추가 최적화 버전
├── 📂 Benchmark
│   └── BenchmarkController.cs // 모드 전환, 오브젝트 수 변경, UI/버튼
├── 📂 Camera
│   └── CameraOrbitController.cs // PC(마우스/키보드) · 모바일(터치) 카메라
└── 📂 Helpers
    ├── GridLayoutHelper.cs     // 그리드 배치 공통 로직
    └── PrefabCreator.cs        // 벤치마크용 프리팹 생성
```

---

## ⚙️ 설정 요약

| 항목 | 설명 |
|------|------|
| **시작 모드** | 오브젝트 모드 → 버튼/스페이스로 인스턴스 모드 전환 |
| **오브젝트 방식** | 프리팹 + Blend Tree(Speed 파라미터), idle/walk/run 랜덤·타이밍 랜덤 |
| **인스턴스 방식** | 메쉬 + Idle/Walk/Run 매터리얼 3종 랜덤 적용, 그리드 배치 |
| **카메라** | 타겟 지정 가능, 타겟 아래로 내려가지 않음(최소 pitch 설정 가능) |