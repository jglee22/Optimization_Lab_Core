# 🚀 Unity Job System / Burst Benchmark: 대규모 객체 최적화

> **프로젝트 개요**: Unity의 **Job System, Burst Compiler, GPU Instancing**을 활용한 대규모 객체 최적화 데모입니다. 모바일에서 10,000개 기준 **약 8~10 FPS → 약 50 FPS**를 확인했습니다.

**Unity 버전**: 6000.0.56f1 (Unity 6)

---

## 📌 빠른 시작 (Quick Start)

1. **Unity**에서 프로젝트 열기
2. 벤치마크 씬 실행 후 **시작 모드**: 오브젝트 방식 → **버튼/스페이스**로 인스턴스 렌더링 모드 전환
3. **카메라**: PC는 우클릭 회전·휠 줌·휠클릭 팬, 모바일은 한 손가락 회전·두 손가락 팬/줌
4. **오브젝트 개수**: UI 버튼 또는 ↑/↓ 키로 변경 (1000 ~ 50000)

---

https://github.com/user-attachments/assets/bf9bb883-ddeb-4c8d-90cc-c3ef8a1afb44



## 📊 성능 테스트 결과 (모바일)
테스트 기기: **Galaxy Z Fold 3** (Snapdragon 888)

### 객체 수 10,000개

**약 8~10 FPS → 약 50 FPS**

인스턴스 경로는 그리드에 고정(속도 0)이며, 이동 시뮬레이션이 아닌 **데이터 처리와 GPU Instancing 렌더링** 부하를 측정했습니다.

| 모드 (Mode) | FPS | 비고 |
|:---:|:---:|:---|
| **GameObject** | 약 8~10 FPS | 개별 GameObject/Transform + Blend Tree Animator |
| **Job System + Burst + GPU Instancing** | **약 50 FPS** | NativeArray 기반 데이터 처리 + GPU Instancing |

※ 두 방식은 렌더링·애니메이션 구조가 다르며, 동일한 시각적 구성에서 GameObject 기반 구조와 인스턴싱 기반 구조의 성능 차이를 비교했습니다.

---

## 🛠️ 핵심 기술 구현 (Key Technologies)

대규모 반복 연산과 렌더링 비용을 줄이기 위해 **Job System / Burst / NativeArray / GPU Instancing**을 적용했습니다.

### 1. Branchless Boundary Check (Burst)
경계 체크의 `if-else` 분기를 제거하고, Burst 최적화에 적합한 연산 구조로 구성했습니다.
- **구현 파일**: `PositionUpdateJob.cs`
- **적용 기술**: `math.select`와 `bool3` 마스크로 경계 체크를 분기 없는 형태로 처리했습니다.

### 2. 스레드 의존성 관리 (Dependency Chaining)
데이터 레이스(Data Race)를 방지하고 Job 간 실행 순서를 보장했습니다.
- **구현 파일**: `JobSystemManager.cs`
- **적용 기술**: `PositionUpdateJob`(위치 갱신)이 완료된 후 `MatrixTransformJob`(렌더링 데이터 변환)이 수행되도록 `JobHandle`을 체이닝했습니다.

### 3. GPU Instancing 및 배칭 (Batching)
인스턴스별 GameObject/Transform 갱신 비용을 제거했습니다.
- **구현 파일**: `JobSystemManager.cs`
- **적용 기술**: `NativeArray`로 계산된 행렬 데이터를 1,023개 단위로 배칭(Batching) 처리하여, `Graphics.DrawMeshInstanced`를 통해 GPU Instancing Draw Call로 렌더링합니다.

### 4. 런타임 GC 할당 최소화
NativeArray와 사전 생성 배치 데이터를 활용해 런타임 GC 할당을 최소화했습니다.
- **메모리 관리**: 연산 데이터는 `NativeArray<T>`에서 관리하며, 모드 전환 시 `Initialize`/`Cleanup` 과정에서 Native 메모리의 생성·해제를 관리합니다.

---

## 💻 프로젝트 구조

```text
Assets/Scripts/
├── 📂 Managers
│   ├── GameObjectManager.cs     // 대조군 (GameObject + Blend Tree 애니메이션)
│   └── JobSystemManager.cs     // ★ 인스턴스 렌더링 (NativeArray, 3매터리얼 배칭)
├── 📂 JobSystem
│   ├── PositionUpdateJob.cs    // [Burst] 분기 없는 경계 체크 (math.select)
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
