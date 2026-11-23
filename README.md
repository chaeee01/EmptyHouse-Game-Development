# EmptyHouse Game Development — 개인 담당 작업 기록

> 저장소 상태: **비공개(Private)**. 팀 공개 승인과 라이선스 확인 전에는 Public 전환·외부 공유를 하지 않는다.
> 기준일: 2026-11-23

## 프로젝트
- 게임: Empty House (빈 집) — 1~4인 협동 생존 호러, PC(Steam), Unity 6 URP, Netcode for GameObjects
- 팀: SW마에스트로 17기 · Studio 301
- 이 저장소의 범위: **본인이 직접 담당한 신규 좀비 2종(선생님 Teacher · 학생 Student)의 기획·구현·검증 기록**. 신규 맵은 후순위(미착수).

## 현재 상태 요약

### 완료 — 종별 행동 기능 구현과 검증
| 항목 | 결과 | 근거 |
|---|---|---|
| 기획 · 핵심 행동 구현 | **Teacher·Student 기획 확정 · 핵심 행동 구현 완료** — 종별 컨트롤러·프로파일·전용 상태·순수 규칙, 독립 프리팹·NetworkPrefab 등록 | [01](docs/01_ZOMBIE_DESIGN.md) · [03](docs/03_IMPLEMENTATION.md) |
| Unity 컴파일 | **오류 0** (Teacher 커밋 단독 상태 · 최종 상태 각각 확인) | [04](docs/04_TEST_RESULTS.md) 0절 |
| 신규 자동 테스트 | **38/38 통과** (Teacher 22 · Student 16) | 04 0절 |
| 전체 EditMode | **294개 중 291 통과 · 기존 실패 2 · 건너뜀 1** — 실패·건너뜀은 변경 전 기준선과 동일한 팀 기존 테스트 | 04 0절 |
| Teacher 실제 플레이 | **핵심 행동 확인** — 걷기 무반응 · 달리기 시 경고 진입(7회) · 경고 중 달리기 중단 시 취소(5회) · 경고 무시 2초 후 추격(2회, 2.02s·2.03s) · 추격 중 걷기 전환 시 추격 유지 · 관성 추격 후 귀가 | 04 3절 |
| Student 실제 플레이 | **비명·도주·쿨다운 로그 확인** — 예고 72 = 완료 61 + 시야 상실 취소 11 · 비명 61회(중복 0) · 비명 후 도주 61 · 쿨다운 중 비명 없이 도주 101 · 위협 상실 후 진정 159(귀가 완료 34) | 04 5절 |

### 부분 확인 — 인과 미확정
- **주변 Walker 의 소음 반응**: 학생 2마리 동시 비명 직후 0~3.19초 안에 Walker 12마리가 조사 상태로 전환. 소음 연동의 **강한 정황 증거**이나, 감지(수신) 로그가 없어 **직접 인과관계는 확정하지 않는다**.

### 미완료 — 남은 작업
| 구분 | 내용 |
|---|---|
| 외형·연출 | 두 종 모두 **전용 외형·모션 미제작** (Walker 모델·포효 클립 임시 사용), 학생 비명 전용 음성 없음 |
| 미검증 | 2인(클라이언트) 연출 · Teacher 경고 연출 시각 식별 · 0.25초 유예 정량 측정 · 경고 중 시야 상실 취소 · 포효 중단 화면 확인 · Student 예고 중 경직·기절 취소 |
| 정식 게임 통합 | 팀 PR 미생성·미병합 · 일반 맵 스폰 목록(레지스트리) 미반영 · 도감·설명 텍스트 미작성 |
| 증거 자료 | 플레이 영상·스크린샷 미확보 |
| 코드 공개 | 코드 원문 미포함 — 팀 승인 대기(아래 "개인 코드 아카이브") |

## 기여 범위
- **팀 기존 시스템(사용)**: 좀비 상태 머신·의심도, 시각·청각 감지 수집, 추격·관성 추격·조사·귀가, 소음 전파·가청 판정, 무리 동조, 서버 권위 상태 복제, 맵 생성 스폰, Walker 모델·애니메이션.
- **본인 구현**: 두 종의 기획·수치, 기존 확장 지점(감지 기여·상태 교체) 위의 종별 컨트롤러·상태·순수 규칙, 비명 소음 발행·쿨다운, 독립 도주 구현, 경고 취소 시 포효 중단(종별 옵트인), 프리팹·프로파일·등록, 신규 테스트 38개, 플레이 로그 분석.
- 기존 시스템을 새로 개발한 것으로 표현하지 않는다. 구현하지 않은 기능은 완료로 적지 않는다.

## 팀 저장소 참조 (비공개)
> 팀 저장소 `SOMA-Studio-301/EmptyHouse` 는 **비공개(Private)** 다. 아래 링크는 팀 저장소 접근 권한이 있는 사람만 열 수 있고, **외부(지원서 검토자 등)에서는 열람할 수 없다**.

- 기능 브랜치: `feat/teacher-student-zombies` (기준 `release/v1.2.0`) — https://github.com/SOMA-Studio-301/EmptyHouse/tree/feat/teacher-student-zombies
- Teacher 커밋 `20d1c679` — https://github.com/SOMA-Studio-301/EmptyHouse/commit/20d1c67915edccb29e54f7d5ac301ba20aefd32e
- Student 커밋 `a33b1b92` — https://github.com/SOMA-Studio-301/EmptyHouse/commit/a33b1b92d53a318a8cdbbc3a9ed669afad5f9444
- PR: 미생성

## 개인 코드 아카이브 — 원문 미포함 · 팀 승인 대기 (2026-11-23)
이 저장소에는 아직 **코드 원문을 넣지 않았다**(`src/`·`tests/` 없음). 아래는 팀 저장소 기능 브랜치에 본인 명의로 커밋한 신규 파일 목록과 역할이며, 팀의 명시적 승인이 나면 원래 파일명·구조를 유지해 `src/`·`tests/` 에 옮길 예정이다.

> **독립 실행 불가**: 이 코드는 팀 Unity 프로젝트(Empty House)의 공통 좀비 AI·소음 시스템·Netcode 위에서만 동작한다. 개인 저장소로 옮겨도 단독으로 빌드·실행되는 프로젝트가 아니다. 예외적으로 순수 규칙 3개와 그 테스트는 Unity·팀 타입에 의존하지 않는다.

### 승인 필요 목록 (본인 작성 신규 파일, 팀 커밋 `20d1c679` · `a33b1b92`)
| 종 | 파일 (팀 저장소 경로 `Assets/01. Scripts/` 기준) | 줄 수 | 역할 | 의존 |
|---|---|---|---|---|
| Teacher | `Game/Zombie/Species/ZombieControllerTeacher.cs` | 77 | 감지 게이트(시야 안 달리기만 통과) · 경고 상태 교체 · 경고 취소 · 포효 중단 옵트인 | 팀 `ZombieController` 상속 |
| Teacher | `Game/Zombie/States/TeacherStates.cs` | 56 | 경고 상태 — 판정 결과를 추격/취소 전이로 옮김 | 팀 `IZombieState`·상태 머신 |
| Teacher | `Game/Zombie/Data/TeacherProfileSO.cs` | 25 | 경고 수치(2s · 0.25s · 0.75s) | 팀 `ZombieProfileSO` 상속 |
| Teacher | `Game/Zombie/Rules/TeacherWarningRules.cs` | 84 | 감지 게이트·경고 판정 순수 규칙 | **없음(순수 C#)** |
| 공통 | `Game/Zombie/Rules/RoarInterruptRules.cs` | 19 | 취소 시 포효 중단 판정 순수 규칙 | **없음(순수 C#)** |
| Student | `Game/Zombie/Species/ZombieControllerStudent.cs` | 111 | 발견 즉시 예고 · 쿨다운 중 즉시 도주 · 예고/도주 상태 교체 · 비명 소음 1회 발행 | 팀 `ZombieController`·소음 이벤트 채널 |
| Student | `Game/Zombie/States/StudentStates.cs` | 176 | 비명 예고 상태 · 독립 도주 상태 | 팀 `IZombieState`·상태 머신·NavMesh |
| Student | `Game/Zombie/Data/StudentProfileSO.cs` | 39 | 비명·도주 수치 | 팀 `ZombieProfileSO` 상속 |
| Student | `Game/Zombie/Rules/StudentScreamRules.cs` | 134 | 예고 판정(비명 1회 보장)·쿨다운·도주 후보·점수·포기 순수 규칙 | **없음(순수 C#)** |
| 테스트 | `Game/Zombie/Rules/Tests/TeacherWarningRulesTests.cs` | 111 | 경고 규칙 9개 | NUnit + 순수 규칙 |
| 테스트 | `Game/Zombie/Rules/Tests/RoarInterruptRulesTests.cs` | 42 | 포효 중단 규칙 5개 | NUnit + 순수 규칙 |
| 테스트 | `Game/Zombie/Rules/Tests/StudentScreamRulesTests.cs` | 140 | 비명·도주 규칙 12개 | NUnit + 순수 규칙 |
| 테스트 | `Editor/Tests/TeacherRoarInterruptTests.cs` | 67 | 실제 프리팹·애니메이터 검증 8개 | Unity 에디터 + 팀 프리팹 |
| 테스트 | `Editor/Tests/StudentPrefabTests.cs` | 89 | 실제 프리팹·등록 검증 4개 | Unity 에디터·Netcode + 팀 프리팹 |
| 설정 | `Game/Zombie/Rules/EmptyHouse.Zombie.Rules.asmdef`, `Rules/Tests/EmptyHouse.Zombie.Rules.Tests.asmdef` | 14·24 | 순수 규칙·테스트 어셈블리 정의 | — |

### 승인과 무관하게 제외하는 파일
| 파일 | 제외 이유 |
|---|---|
| `ZombieController.cs`, `ZombieAnimator.cs`, `IZombiePerceptionSource.cs`, `ZombiePerceptionSource.cs`, `ZombieSpecies.cs` | **팀 공용 코드** — 본인 변경은 수 줄의 추가뿐이고 파일 대부분이 팀원 작성 |
| `Zombie_Teacher.prefab`, `Zombie_Student.prefab` | Unity 프리팹 — 팀 Walker 프리팹 복제본(팀 에셋) |
| `SO_Zombie_Teacher.asset`, `SO_Zombie_Student.asset`, `DefaultNetworkPrefabs.asset`, `SO_MapPrefabRegistry.asset` | ScriptableObject·프로젝트 에셋(팀 에셋) |
| 각 `.meta` | Unity 프로젝트 메타데이터 — 개인 저장소에서 의미 없음 |

### 공개 규칙 검토 결과 — 왜 아직 복사하지 않았나
- 팀 저장소(조직 `SOMA-Studio-301` 소유, 비공개)에는 **라이선스·기여자 정책(CONTRIBUTING·CLA)·코드 소유자 규정이 모든 브랜치에 없다**. 조직 수준 정책 저장소도 없다. 라이선스가 없으면 저작권자의 허락 없이 복제·배포할 권리가 주어지지 않는다.
- 조직 설정에서 **비공개 저장소의 개인 포크가 금지**돼 있다(`members_can_fork_private_repositories = false`) — 비공개 코드를 조직 밖으로 옮기는 것을 제한하는 방향이다.
- 팀 README·문서에 외부 공개·포트폴리오용 재사용을 허용하는 규정이 없다.
- 위 파일은 본인 명의 커밋이지만 팀 제품(Steam 출시 게임)의 일부로 팀 저장소에 들어갔고, 대부분 팀 코드의 타입을 상속·호출한다.
- 따라서 **명시적으로 허용된 코드가 없어 원문을 옮기지 않았다.** 승인 요청 범위 제안: ① 순수 규칙 3개 + 규칙 테스트 3개(팀 타입 비의존, 위험 가장 낮음) ② 종별 Controller·State·Profile ③ Unity 에디터 테스트. 승인 형태와 범위를 기록으로 남긴 뒤 옮긴다.
- SW마에스트로 과정의 결과물 귀속·공개 규정은 별도 확인 필요.

## 문서
| 문서 | 내용 | 상태 |
|---|---|---|
| [01_ZOMBIE_DESIGN](docs/01_ZOMBIE_DESIGN.md) | 선생님·학생 좀비 기획: 행동 규칙, 수치, 설계 판단 | 기획 확정 |
| [02_LEVEL_DESIGN](docs/02_LEVEL_DESIGN.md) | 신규 방 기획 | 미착수(후순위) |
| [03_IMPLEMENTATION](docs/03_IMPLEMENTATION.md) | 작업별 구현 기록 (1 선생님 · 1-2 포효 중단 · 1-3 플레이 분석 · 2 학생) | 행동 구현 완료 · 팀 브랜치 커밋·푸시 완료 · PR 미생성 |
| [04_TEST_RESULTS](docs/04_TEST_RESULTS.md) | 자동 테스트·플레이 로그 검증 결과 | 자동 테스트 완료 · 두 종 플레이 1차 완료(일부 미검증) |
| [05_PORTFOLIO_PAGE_PLAN](docs/05_PORTFOLIO_PAGE_PLAN.md) | 지원용 포트폴리오 2장 구성안 | 구성 확정 · 캡처·영상 미확보 |
