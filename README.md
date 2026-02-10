# 🚀 Unity DOTS Benchmark: 대규모 오브젝트 시뮬레이션

> **프로젝트 개요**: Unity의 **Job System, Burst Compiler, GPU Instancing** 기술을 활용하여 50,000개 이상의 객체를 모바일 환경에서 60 FPS로 시뮬레이션하는 고성능 최적화 데모입니다.

## 📊 성능 테스트 결과 (모바일)
테스트 기기: **Galaxy Z Fold 3** (Snapdragon 888) / 객체 수: **50,000개**

| 모드 (Mode) | FPS | 상태 | 분석 결과 |
|:---:|:---:|:---:|:---|
| **GameObject** | ~15 FPS | 플레이 불가 | 메인 스레드 병목, 대량의 GC 발생, 드로우콜 과다 |
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
OptimizationLab
├── 📂 Managers
│   ├── GameObjectManager.cs    // 대조군 (기존 방식)
│   └── JobSystemManager.cs     // ★ 핵심 구현 (NativeArray & Batching)
├── 📂 JobSystem
│   ├── PositionUpdateJob.cs    // [Burst] SIMD 물리 연산 (math.select 적용)
│   └── MatrixTransformJob.cs   // [Burst] 행렬 변환 최적화
└── 📂 Benchmark
    └── BenchmarkController.cs  // 모드 전환 및 UI/Input 관리