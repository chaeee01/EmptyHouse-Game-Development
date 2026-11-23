# 06. 신규 캐릭터 제작 파이프라인 — 영상 → 3D 리깅 모델 → 동작 FBX → Unity

> 기준일 2026-11-23. **FBX 생성 완료**와 **Unity 적용 완료**를 구분한다. Unity 적용은 팀 Unity 프로젝트의 작업 브랜치(로컬)에 반영·자동 검증까지 마쳤고, **팀 저장소에는 아직 커밋하지 않았다**.

## 1. 결과 요약
| 구분 | 결과 | 근거 |
|---|---|---|
| 신규 캐릭터 | **2종**(Teacher·Student) — 외형 FBX 2편 | 납품 메모 · 파일 |
| 동작 FBX 생성 | **20편 완료**(Teacher 11 · Student 9) — 전부 SMPL 24본 · 24fps | 납품 메모 · Blender 골격 검사 |
| 영상 생성 | **22편 사용 / 23회 시도**(23회 모두 생성 성공, Student `rise` 1편 재생성) | 생성 로그 |
| 제작 기간 | **약 1일** — 산출 파일 시각 기준 2026-11-22 19:22 ~ 11-23 20:04 | 파일 시각 |
| 영상 생성 비용 | **약 $35**(23회 $35.19 · 사용분 22편 $33.66) — 요약본 단가 기반 **추정치**, 청구서 금액 아님 | 생성 기록(record_v2) |
| Unity 적용 | 외형 2종 + 대표 동작 연결 완료(아래 3절) — 로컬 작업 브랜치, 팀 미반영 | 반입 로그 · 테스트 |
| AI 행동 로직 | 신규 자동 테스트 **38/38 통과**, 새 외형 연결 뒤에도 전체 EditMode 294개 중 291 통과(기존 실패 2 · 건너뜀 1, 기준선 동일) | 04 |

## 2. 파이프라인 단계
영상 생성 → 객체 분리(SAM2) → 외형 복원(TRELLIS.2) → 동작 복원(WHAM) → SMPL 리깅 → Unity 반입용 FBX.

| 단계 | 산출 | Teacher | Student |
|---|---|---|---|
| 외형 | 리깅된 외형 FBX(메시 + SMPL 24본 + 가중치) · 2048 텍스처 2장(baseColor · metallicRoughness) | 약 96만 정점 | 약 73만 정점 |
| 동작 | 클립별 FBX(골격 + 애니메이션, 메시 없음) | idle · walk · run · warn_start · warn_loop · warn_end · attack · hit · stun_fall · stunned_loop · rise | idle · walk · run · notice · scream · hit · stun_fall · stunned_loop · rise |

반입 전 확인한 사항:
- 외형 2편과 동작 20편이 **같은 24본 골격**(`SMPL_rig`, 루트 `Pelvis`) — 리타게팅 불필요.
- 리그 rest 가 키프레임 자세라 **Generic 반입**(Humanoid 로 억지 설정하면 근육 변환으로 동작이 왜곡됨).
- 서는 클립은 첫 구간이 A포즈 도입부, 단발 동작은 10초 영상 안에서 2~3초 — 사용 구간을 잘라 써야 한다.
- 합성 클립: `student@stunned_loop`(넘어짐 마지막 자세를 유지해 합성). 육안 확인 대상: `student@rise`(팔·어깨 늘어남 지표 최대), 두 외형의 손 가중치.

## 3. Unity 적용 (작업 브랜치 로컬 · 팀 미반영)
팀이 기존 좀비(Walker·Stalker·Listener·Lurker·Oiler)를 같은 파이프라인 산출물로 반입할 때 쓰는 에디터 도구(`ZombieSmplBuilder`)를 재사용했다. 도구에는 종 항목 2개와 **종별 반입 배율 옵션**(기존 종 기본 1.9 유지, Teacher 2.0 · Student 1.7 — 기획 키)만 추가했다.

| 단계 | Teacher | Student |
|---|---|---|
| 메시 감량(LOD 3단) | 25만 · 12.5만 · 10.6만 삼각형 | 25만 · 12.5만 · 7.6만 삼각형 |
| 연결한 동작 | 대기 idle · 걷기 walk · 달리기/추격 run · **경고 warn_start** · 공격 attack · 피격 hit · 기절 stun_fall · 기상 rise | 대기 idle · 걷기 walk · 달리기/도주 run · **비명 예고 scream** · 피격 hit · 기절 stun_fall · 기상 rise |
| 추출만(미연결) | warn_loop · warn_end · stunned_loop | notice · stunned_loop |
| 게임 프리팹 | `Zombie_Teacher` 에 새 모델·전용 컨트롤러 연결(종별 행동 로직은 그대로) | `Zombie_Student` 같음 |

사용 구간은 실측으로 정했다 — 시작은 납품 메모의 A포즈 이탈 프레임, 끝은 프레임별 자세 변화량(본 회전 변화 합)이 가라앉는 지점. 경고는 원본 영상에서 **f72~96 에 오른손을 드는 동작이 2초 경고 안에 보이도록 f60 부터**, 비명 예고는 **가리키며 비명 f24~96 이 1.5초 예고 안에 들어가도록 f11 부터, 선 자세 복귀(f120)에서 끝**으로 잡았다(첫 반입에서 경고가 거의 보이지 않던 원인을 렌더로 확인해 수정).

이동 속도: 납품 보행은 제자리 동작이라 기존 종 관례대로 걷기 0.5 · 달리기 3 m/s 를 루트 모션으로 합성했다. 납품 메모의 무미끄럼 권장 속도(Teacher 0.40/1.03 · Student 0.25/0.62)는 게임 이동 속도의 1/4~1/7 이라 **발 미끄러짐을 감수**했다(기존 종과 같은 결론 — 게임 속도 튜닝 사안).

### 검증
| 항목 | 결과 |
|---|---|
| Unity 반입 도구 1→7단계(임포트·감량·추출·컨트롤러·프리팹·렌더) | 두 종 모두 오류 없이 완료, 좌우 반전 의심 없음 |
| 다른 캐릭터 영향 | Walker 등 기존 종 아트·프리팹·컨트롤러 변경 0(git 확인) |
| 컴파일·전체 EditMode | 컴파일 오류 0 · 294개 중 291 통과(기존 실패 2 · 건너뜀 1), 새 외형 연결 전후 결과 동일 |
| 검증 렌더 | 대기·걷기·달리기·경고/비명·공격·피격·기절·기상 정면·측면·후면 렌더 생성 |
| **실제 플레이(새 외형)** | **미실행** — 게임 안 경고·추격·비명·도주 장면, 2인 연출 |
| **육안 확인 필요** | `student@rise` 팔·어깨, 두 외형 손목–손 가중치, Student `rise` 는 반입 시 루트 드리프트(최대 2.72m · 51°)를 고정으로 제거함 |

## 4. 자료 (실제 산출물 캡처 — 생성형 이미지 아님)
| 파일 | 내용 |
|---|---|
| [`media/teacher_turnaround.png`](../media/teacher_turnaround.png) · [`media/student_turnaround.png`](../media/student_turnaround.png) | 게임 프리팹 Unity 렌더 — 정면·측면·후면(대기 자세) |
| [`media/teacher_warn_chase_pose.png`](../media/teacher_warn_chase_pose.png) | Teacher 경고(warn_start)·추격(run) 동작 포즈 — Unity 렌더 |
| [`media/student_scream_flee_pose.png`](../media/student_scream_flee_pose.png) | Student 비명 예고(scream)·도주(run) 동작 포즈 — Unity 렌더 |
| [`media/pipeline_teacher_warn.png`](../media/pipeline_teacher_warn.png) · [`media/pipeline_student_scream.png`](../media/pipeline_student_scream.png) | 생성 영상 프레임 → 리깅 외형(Blender 렌더) → Unity 적용 결과 |

- 동작 포즈 이미지는 **게임플레이 화면이 아니라** 클립의 한 지점(35%)을 게임 프리팹에 적용해 렌더한 것이다. 실제 게임 화면과 Inspector·Animator 창 캡처는 아직 없다.
- 비교 이미지에서 "동작 FBX 를 외형에 바로 입힌 Blender 렌더" 단계는 뺐다 — 외형과 동작 FBX 의 rest 자세가 달라 단순 적용 시 팔이 T자로 남는 부정확한 그림이 나왔다(Unity 반입 도구는 이 차이를 보정한다).

## 5. 라이선스 메모
- TRELLIS.2: 코드·모델 카드 MIT(파이프라인 문서 확인).
- **SMPL**: 무료판은 비상업 연구용 — 상업 배포(Steam 출시) 시 Meshcapade 라이선스 필요. 기존 파이프라인 캐릭터와 같은 미해결 사안.
- 영상은 생성형 영상 모델로 만든 산출물이다.
