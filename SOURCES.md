# SOURCES — 파일 출처와 원래 Unity 경로

> 모든 파일은 팀 저장소 `SOMA-Studio-301/EmptyHouse` 기능 브랜치 `feat/teacher-student-zombies` 의 커밋 `a33b1b92` 에서 **바이트 그대로** 가져왔다(수정 없음). 원래 경로는 Unity 프로젝트 루트(`EmptyHouse/`) 기준이다.
> 구분: **본인 신규** = 이 작업에서 새로 작성한 파일 · **팀 원본(무수정)** = 팀원이 작성한 공통 시스템, 이해를 위한 참고용 · **팀 원본 + 본인 수정** = 팀 공통 파일에 본인이 몇 줄을 추가한 것(변경분은 [`src/TeamSystems/OWN_CHANGES.diff`](src/TeamSystems/OWN_CHANGES.diff)).

| 아카이브 경로 | 원래 Unity 경로 | 구분 | 역할 | 크기 | sha256(앞 12자) |
|---|---|---|---|---|---|
| [`src/Teacher/ZombieControllerTeacher.cs`](src/Teacher/ZombieControllerTeacher.cs) | `Assets/01. Scripts/Game/Zombie/Species/ZombieControllerTeacher.cs` | 본인 신규 | Teacher 컨트롤러 — 감지 게이트·경고 상태 교체·경고 취소·포효 중단 옵트인 | 4,396B | `a0e040d2bcfb` |
| [`src/Teacher/TeacherStates.cs`](src/Teacher/TeacherStates.cs) | `Assets/01. Scripts/Game/Zombie/States/TeacherStates.cs` | 본인 신규 | Teacher 경고 상태(TeacherWarningState) | 2,812B | `cdc53b103665` |
| [`src/Teacher/TeacherProfileSO.cs`](src/Teacher/TeacherProfileSO.cs) | `Assets/01. Scripts/Game/Zombie/Data/TeacherProfileSO.cs` | 본인 신규 | Teacher 프로파일 — 경고 수치 | 1,737B | `2151ce123513` |
| [`src/Teacher/TeacherWarningRules.cs`](src/Teacher/TeacherWarningRules.cs) | `Assets/01. Scripts/Game/Zombie/Rules/TeacherWarningRules.cs` | 본인 신규 | Teacher 감지 게이트·경고 판정 순수 규칙 | 4,891B | `a7769fc63ba3` |
| [`src/Student/ZombieControllerStudent.cs`](src/Student/ZombieControllerStudent.cs) | `Assets/01. Scripts/Game/Zombie/Species/ZombieControllerStudent.cs` | 본인 신규 | Student 컨트롤러 — 발견·쿨다운 즉시 도주·상태 교체·비명 소음 1회 발행 | 6,170B | `5c2c41014f57` |
| [`src/Student/StudentStates.cs`](src/Student/StudentStates.cs) | `Assets/01. Scripts/Game/Zombie/States/StudentStates.cs` | 본인 신규 | Student 비명 예고 상태·도주 상태 | 8,376B | `7577fa3d2345` |
| [`src/Student/StudentProfileSO.cs`](src/Student/StudentProfileSO.cs) | `Assets/01. Scripts/Game/Zombie/Data/StudentProfileSO.cs` | 본인 신규 | Student 프로파일 — 비명·도주 수치 | 2,757B | `cc31e8f2ccdb` |
| [`src/Student/StudentScreamRules.cs`](src/Student/StudentScreamRules.cs) | `Assets/01. Scripts/Game/Zombie/Rules/StudentScreamRules.cs` | 본인 신규 | Student 예고·쿨다운·도주 판정 순수 규칙 | 6,779B | `fb1a7e7c0215` |
| [`src/Shared/RoarInterruptRules.cs`](src/Shared/RoarInterruptRules.cs) | `Assets/01. Scripts/Game/Zombie/Rules/RoarInterruptRules.cs` | 본인 신규 | 두 종 공용 — 취소 시 포효 중단 판정 순수 규칙 | 1,246B | `5a9e3f5e86ff` |
| [`src/Shared/EmptyHouse.Zombie.Rules.asmdef`](src/Shared/EmptyHouse.Zombie.Rules.asmdef) | `Assets/01. Scripts/Game/Zombie/Rules/EmptyHouse.Zombie.Rules.asmdef` | 본인 신규 | 순수 규칙 어셈블리 정의(Unity 엔진 비참조) | 379B | `6328754094a8` |
| [`tests/Rules/TeacherWarningRulesTests.cs`](tests/Rules/TeacherWarningRulesTests.cs) | `Assets/01. Scripts/Game/Zombie/Rules/Tests/TeacherWarningRulesTests.cs` | 본인 신규 | Teacher 경고 규칙 테스트 9개 | 5,910B | `2ee6daf9e7fb` |
| [`tests/Rules/RoarInterruptRulesTests.cs`](tests/Rules/RoarInterruptRulesTests.cs) | `Assets/01. Scripts/Game/Zombie/Rules/Tests/RoarInterruptRulesTests.cs` | 본인 신규 | 포효 중단 규칙 테스트 5개 | 1,681B | `d9f3d800b2da` |
| [`tests/Rules/StudentScreamRulesTests.cs`](tests/Rules/StudentScreamRulesTests.cs) | `Assets/01. Scripts/Game/Zombie/Rules/Tests/StudentScreamRulesTests.cs` | 본인 신규 | Student 비명·도주 규칙 테스트 12개 | 6,701B | `071f8f1cf562` |
| [`tests/Rules/EmptyHouse.Zombie.Rules.Tests.asmdef`](tests/Rules/EmptyHouse.Zombie.Rules.Tests.asmdef) | `Assets/01. Scripts/Game/Zombie/Rules/Tests/EmptyHouse.Zombie.Rules.Tests.asmdef` | 본인 신규 | 규칙 테스트 어셈블리 정의(EditMode) | 591B | `78a79801bfd1` |
| [`tests/Editor/TeacherRoarInterruptTests.cs`](tests/Editor/TeacherRoarInterruptTests.cs) | `Assets/01. Scripts/Editor/Tests/TeacherRoarInterruptTests.cs` | 본인 신규 | 실제 프리팹·애니메이터 검증 8개(Teacher 옵트인·포효 레이어) | 3,298B | `2c6ddc11abc6` |
| [`tests/Editor/StudentPrefabTests.cs`](tests/Editor/StudentPrefabTests.cs) | `Assets/01. Scripts/Editor/Tests/StudentPrefabTests.cs` | 본인 신규 | 실제 프리팹·등록 검증 4개(Student) | 4,499B | `466e7842952e` |
| [`assets/Prefabs/Zombie_Teacher.prefab`](assets/Prefabs/Zombie_Teacher.prefab) | `Assets/02. Prefab/Zombie/Zombie_Teacher.prefab` | 본인 생성 — 팀 Walker 프리팹 복제 기반 | Teacher 프리팹(팀 Walker 프리팹 복제 → 컨트롤러 교체) | 22,953B | `32cd2a5a3d4a` |
| [`assets/Prefabs/Zombie_Student.prefab`](assets/Prefabs/Zombie_Student.prefab) | `Assets/02. Prefab/Zombie/Zombie_Student.prefab` | 본인 생성 — 팀 Walker 프리팹 복제 기반 | Student 프리팹(팀 Walker 프리팹 복제 → 컨트롤러 교체·소음 채널 배선) | 23,045B | `6a8e08b3241f` |
| [`assets/Profiles/SO_Zombie_Teacher.asset`](assets/Profiles/SO_Zombie_Teacher.asset) | `Assets/03. ScriptableObjects/Zombies/SO_Zombie_Teacher.asset` | 본인 생성 — Walker 수치 복사 + 종별 값 | Teacher 프로파일 에셋(Walker 값 복사 + 청각·동조 끔) | 2,404B | `7281b99356a2` |
| [`assets/Profiles/SO_Zombie_Student.asset`](assets/Profiles/SO_Zombie_Student.asset) | `Assets/03. ScriptableObjects/Zombies/SO_Zombie_Student.asset` | 본인 생성 — Walker 수치 복사 + 종별 값 | Student 프로파일 에셋(Walker 값 복사 + 청각·동조 끔) | 2,527B | `9db3426cb0e2` |
| [`src/TeamSystems/Zombie/ZombieController.cs`](src/TeamSystems/Zombie/ZombieController.cs) | `Assets/01. Scripts/Game/Zombie/ZombieController.cs` | 팀 원본 + 본인 수정(+9줄) | 공통 좀비 컨트롤러 — 서버 틱·감지·의심도·확장 훅(종별 클래스의 부모) | 74,411B | `460347e330ba` |
| [`src/TeamSystems/Zombie/ZombieStateMachine.cs`](src/TeamSystems/Zombie/ZombieStateMachine.cs) | `Assets/01. Scripts/Game/Zombie/ZombieStateMachine.cs` | 팀 원본(무수정) | 공통 상태 머신 — 상태 생성(종별 교체 우선)·지각 전이·이동 API | 57,242B | `05f9839f32eb` |
| [`src/TeamSystems/Zombie/IZombieState.cs`](src/TeamSystems/Zombie/IZombieState.cs) | `Assets/01. Scripts/Game/Zombie/IZombieState.cs` | 팀 원본(무수정) | 상태 인터페이스 | 214B | `81b55fa9569e` |
| [`src/TeamSystems/Zombie/ZombieStateKind.cs`](src/TeamSystems/Zombie/ZombieStateKind.cs) | `Assets/01. Scripts/Game/Zombie/ZombieStateKind.cs` | 팀 원본(무수정) | 복제되는 상태 종류 열거형 | 750B | `2f358cc38a0c` |
| [`src/TeamSystems/Zombie/ZombiePerceptionBuilder.cs`](src/TeamSystems/Zombie/ZombiePerceptionBuilder.cs) | `Assets/01. Scripts/Game/Zombie/ZombiePerceptionBuilder.cs` | 팀 원본(무수정) | 지각 조립체 — 종별 감지 기여 훅이 수정하는 대상 | 3,633B | `fab4734b7bf8` |
| [`src/TeamSystems/Zombie/ZombiePerceptionFrame.cs`](src/TeamSystems/Zombie/ZombiePerceptionFrame.cs) | `Assets/01. Scripts/Game/Zombie/ZombiePerceptionFrame.cs` | 팀 원본(무수정) | 지각 프레임(조립 결과) | 2,565B | `1c9c3c1934c5` |
| [`src/TeamSystems/Zombie/IZombiePerceptionSource.cs`](src/TeamSystems/Zombie/IZombiePerceptionSource.cs) | `Assets/01. Scripts/Game/Zombie/IZombiePerceptionSource.cs` | 팀 원본 + 본인 수정(+1줄) | 플레이어 지각 신호 인터페이스(IsSprinting 추가) | 1,447B | `683213ccd418` |
| [`src/TeamSystems/Zombie/ZombiePerceptionSource.cs`](src/TeamSystems/Zombie/ZombiePerceptionSource.cs) | `Assets/01. Scripts/Game/Zombie/ZombiePerceptionSource.cs` | 팀 원본 + 본인 수정(+3줄) | 플레이어 지각 신호 구현(IsSprinting 구현) | 9,711B | `5f7ca3670581` |
| [`src/TeamSystems/Zombie/ZombieSensorySystem.cs`](src/TeamSystems/Zombie/ZombieSensorySystem.cs) | `Assets/01. Scripts/Game/Zombie/ZombieSensorySystem.cs` | 팀 원본(무수정) | 시각·청각 수집 — 감지 기여 훅 직전 단계 | 18,606B | `fc12fde0e1f0` |
| [`src/TeamSystems/Zombie/ZombieAnimator.cs`](src/TeamSystems/Zombie/ZombieAnimator.cs) | `Assets/01. Scripts/Game/Zombie/ZombieAnimator.cs` | 팀 원본 + 본인 수정(+19줄) | 좀비 애니메이터(포효 중단 처리 추가) | 34,899B | `20c14cf53f0f` |
| [`src/TeamSystems/Zombie/Data/ZombieProfileSO.cs`](src/TeamSystems/Zombie/Data/ZombieProfileSO.cs) | `Assets/01. Scripts/Game/Zombie/Data/ZombieProfileSO.cs` | 팀 원본(무수정) | 공통 프로파일(종별 프로파일의 부모) | 16,790B | `32def0ae69e9` |
| [`src/TeamSystems/Zombie/Data/ZombieSpecies.cs`](src/TeamSystems/Zombie/Data/ZombieSpecies.cs) | `Assets/01. Scripts/Game/Zombie/Data/ZombieSpecies.cs` | 팀 원본 + 본인 수정(+3줄) | 종 열거형(Teacher·Student 추가) | 1,102B | `d8cc6cb90fa2` |
| [`src/TeamSystems/Zombie/Data/ZombieRuntimeRegistrySO.cs`](src/TeamSystems/Zombie/Data/ZombieRuntimeRegistrySO.cs) | `Assets/01. Scripts/Game/Zombie/Data/ZombieRuntimeRegistrySO.cs` | 팀 원본(무수정) | 런타임 좀비·지각 소스 목록(Student 도주가 위협 탐색에 사용) | 5,084B | `bfbbe73edb65` |
| [`src/TeamSystems/Zombie/States/ZombieStates.cs`](src/TeamSystems/Zombie/States/ZombieStates.cs) | `Assets/01. Scripts/Game/Zombie/States/ZombieStates.cs` | 팀 원본(무수정) | 공통 상태(배회·조사·추격·진정 등) — Teacher 가 추격 이후 인계 | 26,195B | `04719e365f1a` |
| [`src/TeamSystems/Zombie/States/OilerStates.cs`](src/TeamSystems/Zombie/States/OilerStates.cs) | `Assets/01. Scripts/Game/Zombie/States/OilerStates.cs` | 팀 원본(무수정) | Oiler 놀람·도주 — Student 도주의 참고 원본(상속·공유 없음) | 9,455B | `bc330efcdbba` |
| [`src/TeamSystems/NoiseSystem/NoiseEvents.cs`](src/TeamSystems/NoiseSystem/NoiseEvents.cs) | `Assets/01. Scripts/Game/NoiseSystem/NoiseEvents.cs` | 팀 원본(무수정) | 소음 이벤트 구조체·채널 종류 — Student 비명이 사용 | 4,065B | `9c8951639ceb` |
| [`src/TeamSystems/NoiseSystem/NoiseEmittedEventChannelSO.cs`](src/TeamSystems/NoiseSystem/NoiseEmittedEventChannelSO.cs) | `Assets/01. Scripts/Game/NoiseSystem/NoiseEmittedEventChannelSO.cs` | 팀 원본(무수정) | 서버 소음 발행 채널 | 428B | `dfe26fb84c01` |
| [`src/TeamSystems/NoiseSystem/NoisePropagationSystem.cs`](src/TeamSystems/NoiseSystem/NoisePropagationSystem.cs) | `Assets/01. Scripts/Game/NoiseSystem/NoisePropagationSystem.cs` | 팀 원본(무수정) | 서버 소음 전파 — 거리 감쇠·벽 차폐·좀비별 가청 판정 | 12,531B | `3dc1b6c7628d` |

`.meta` 파일은 각 본인 신규 파일 옆에 함께 두었다(Unity GUID — 프리팹·프로파일이 스크립트를 GUID 로 참조하므로 대응 관계 확인용).

집계: 본인 신규 코드·테스트·설정 16개 · 본인 생성 에셋 4개(팀 Walker 기반) · 팀 원본 + 본인 수정 5개 · 팀 원본(무수정) 13개
