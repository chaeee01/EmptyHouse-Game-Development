# EmptyHouse Game Development — 개인 담당 작업 기록

> 상태: **비공개(Private) · 작성 중**. 팀 공개 승인과 라이선스 확인 전에는 Public 전환·외부 공유를 하지 않는다.

## 프로젝트
- 게임: Empty House (빈 집) — 1~4인 협동 생존 호러, PC(Steam), Unity 6 URP, Netcode for GameObjects
- 팀: SW마에스트로 17기 · Studio 301
- 이 저장소의 범위: **본인이 직접 담당한 신규 콘텐츠(신규 좀비 2종 — 선생님·학생, 후순위: 신규 방)의 기획·구현·검증 과정만** 기록한다.

## 기여 범위 원칙
- 좀비 AI(상태 머신·시각/청각 감지·추격/복귀), 절차적 맵 생성기, 네트워크 동기화는 **팀이 개발한 기존 시스템**이다. 본인 작업은 그 위에서의 데이터 설정·프리팹 구성·에셋 통합·검증이며, 이를 새로 개발한 것으로 표현하지 않는다.
- 팀 저장소의 C# 코드, 프리팹, 에셋, 팀원이 작성한 기획서는 이 저장소에 복사하지 않는다. 기존 시스템은 직접 작성한 설명·다이어그램으로만 다룬다.
- 구현하지 않은 기능은 완료로 적지 않는다. 각 항목에 상태(미착수 / 진행 중 / 완료 / 보류)를 표시한다.

## 문서
| 문서 | 내용 | 상태 |
|---|---|---|
| [01_ZOMBIE_DESIGN](docs/01_ZOMBIE_DESIGN.md) | 선생님·학생 좀비 기획: 행동 규칙, 수치, 설계 판단 | 작성 |
| [02_LEVEL_DESIGN](docs/02_LEVEL_DESIGN.md) | (후순위) 신규 방 기획 | 미착수 |
| [03_IMPLEMENTATION](docs/03_IMPLEMENTATION.md) | 작업별 구현 기록 (1 선생님 · 1-2 포효 중단 · 1-3 플레이 분석 · 2 학생) | 진행 중 (플레이 테스트·커밋·PR 남음) |
| [04_TEST_RESULTS](docs/04_TEST_RESULTS.md) | 테스트 시나리오·결과·영상 | 신규 38개 통과 · 전체 294개 중 291 통과(기존 실패 2 · 건너뜀 1) · 두 종 플레이 1차(일부 미검증) |
| [05_PORTFOLIO_PAGE_PLAN](docs/05_PORTFOLIO_PAGE_PLAN.md) | 지원용 포트폴리오 2장 구성안(1장 선생님 · 2장 학생+공통 검증) | 초안 |

## 팀 저장소 참조
- 팀 저장소는 **비공개**다. 아래 커밋·PR 링크는 팀원 외에는 열람할 수 없다.
- 브랜치: `feat/teacher-student-zombies` (팀 저장소 `SOMA-Studio-301/EmptyHouse`, 기준 `release/v1.2.0`) — https://github.com/SOMA-Studio-301/EmptyHouse/tree/feat/teacher-student-zombies
- 커밋: `20d1c679` 선생님(Teacher) 좀비 종 추가 · `a33b1b92` 학생(Student) 좀비 종 추가
- PR: 미생성(팀 절차에 따라 별도 진행)
