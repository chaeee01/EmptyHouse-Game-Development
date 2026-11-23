using UnityEngine;

/// <summary>
/// 한 발성 카테고리의 설정 — 후보 목록에서 랜덤 1개를, 랜덤 텀(min~max)마다 재생한다.
/// 후보가 비어 있으면 그 카테고리는 울지 않는다.
/// </summary>
[System.Serializable]
public sealed class ZombieVoiceProfile
{
    [Min(0f)] public float PlaybackSeconds; // 후보 중 최장 재생 시간(초), 보컬 중첩 방지
    [Tooltip("재생 후보 — 매번 이 중 하나를 랜덤으로 고른다.")]
    public AudioId[] AudioIds = System.Array.Empty<AudioId>();
    [Tooltip("재생 텀 하한(초).")]
    [Min(0.5f)] public float MinIntervalSeconds = 6f;
    [Tooltip("재생 텀 상한(초). 매 사이클 min~max 에서 새로 뽑는다.")]
    [Min(0.5f)] public float MaxIntervalSeconds = 10f;
}

/// <summary>
/// 좀비 종 프로파일 — 한 종의 모든 파라미터를 담는 단일 SO(D-9). 종당 에셋 정확히 1개.
/// Common SO 를 따로 두지 않는다 — 전-종 공통값도 여기 들어가며, 그 대가로 공통값 변경 시
/// 에셋 4개를 함께 만진다(기획이 택한 트레이드오프).
/// 참조는 ZombieController 만 들고, 형제 컴포넌트는 controller 경유로 매 사용 시점에 읽는다
/// — 초기화 복사 금지(플레이 중 인스펙터 튜닝 즉시 반영, ⚪ 라벨 원칙).
/// 수치 원천 = Docs/Spec/Data/zombie.csv. 코드에 숫자를 박지 말 것.
/// 전파 상수(감쇠·차폐·합산 창)는 넣지 않는다 — 소음 도메인(NoisePropagationSettingsSO) 소유.
/// </summary>
public abstract class ZombieProfileSO : ScriptableObject
{
    public abstract ZombieSpecies Species { get; } // 종 식별. 데이터가 아니라 파생 타입의 정체성이라 인스펙터로 바꿀 수 없다

    [Header("채널 — false 면 해당 지각 루프 자체를 건너뛴다 (0 곱하기 금지 — 좀비공통 12-17)")]
    [Tooltip("시각 채널 사용 여부. false 면 시야 판정 루프를 통째로 건너뛴다(Listener·Dog). 배율을 0 으로 두는 방식은 금지 — 좀비공통 12-17.")]
    public bool HasVision = true;
    [Tooltip("청각 채널 사용 여부. false 면 소음 판정을 통째로 건너뛴다.")]
    public bool HasHearing = true;
    [Tooltip("위장이 타겟을 끊는가. true 면 위장 중인 플레이어는 어느 채널로도 타겟이 되지 않고, 물고 있던 타겟도 즉시 풀려 래치 유예 뒤 관성 추격으로 내려간다(Walker 와 동일). false 는 Dog 전용(후각은 위장 무관). ⚠️ Listener 를 true 로 두는 것은 좀비공통 E15(눈 없는 종에 위장 무효) 이탈 — 사용자 확정 2026-10-06. 시트 행 없음 — 임포터 ApplyChannelFlags 가 못박는다.")]
    public bool DisguiseBreaksTargeting = true;

    [Header("의심도 임계 — 종별 조절 가능(D-9)")]
    [Tooltip("배회 → 경계 진입 임계.")]
    public float ThAlert = 30f;
    [Tooltip("경계 → 조사 진입 임계. ⚠️ 청각 pull-up 바닥을 겸한다(D30 동치 규칙 — 들린 소리는 반드시 조사를 유발). 별도 hear_floor 필드는 폐지됐다.")]
    public float ThInvestigate = 60f;
    [Tooltip("단서 0 + 유예 경과 후 초당 감소량.")]
    public float CoolRatePerSecond = 8f;
    [Tooltip("단서 소실 후 냉각 시작까지의 유예(초). 0이면 코너 들락날락으로 게이지가 무한 리셋된다.")]
    public float SuspicionGraceSeconds = 1.5f;

    [Header("시각 — HasVision=false 종은 전부 무시된다")]
    [Tooltip("시야 원뿔의 전체 각도(도). 정면을 가운데 두고 좌우로 절반씩 벌어진다. 수평(XZ)각만 잰다 — 상하는 VisionVerticalAngle.")]
    public float VisionAngle = 110f;
    [Tooltip("시야 원뿔의 상하 전체 각도(도). 정면을 가운데 두고 위아래로 절반씩. 좌우각(VisionAngle)과 따로 재며, 가까운 높은 단·아래층의 플레이어를 놓치지 않게 좌우보다 넓게 둔다. 시트 행 없음 — 코드 기본값(2026-10-17 사용자 확정).")]
    [Range(0f, 180f)] public float VisionVerticalAngle = 160f;
    [Tooltip("시야 최대 거리(m). 이보다 멀면 원뿔 안이어도 보이지 않는다.")]
    public float VisionDistance = 15f;
    [Tooltip("원뿔 + LoS + 이 거리 안 = 게이지 무시 즉시 발각. 「5m 룰」의 상수 그 자체(좀비공통 3-2).")]
    public float VisInstantRange = 5f;
    [Tooltip("원뿔 밖 밀착 감지 반경(m). 시야각 밖이어도 LoS + 이 수평 거리 안이면 의심도를 조사 임계(ThInvestigate)까지 올려 그 자리를 조사한다. 위장 중엔 무반응. 0 = 끔.")]
    [Min(0f)] public float VisProximityRange = 1f;
    [Tooltip("시각 의심도 증가율의 기준값(초당). 여기에 거리·정면·조명·자세 배율을 모두 곱한 값이 실제 증가율이다.")]
    public float VisGainBase = 25f;
    [Tooltip("거리 배율 — 근거리값. 시야 거리의 25% 안쪽은 거리와 무관하게 전부 이 값이다.")]
    public float VisDistNear = 2f;
    [Tooltip("거리 배율 — 최대 거리에서의 값. 근거리 경계부터 시야 끝까지 선형 보간한다.")]
    public float VisDistFar = 0.5f;
    [Tooltip("정면 배율. 시선에서 20도 안에 들어온 대상에만 곱한다. 밖이면 1.")]
    public float VisFront = 1.5f;
    [Tooltip("조명 배율 — 밝은 환경(EnvironmentIsBright 가 켜졌을 때).")]
    public float VisLightBright = 1.3f;
    [Tooltip("조명 배율 — 어두운 환경. 빈집의 기본값이다.")]
    public float VisLightDark = 0.7f;
    [Tooltip("조명 배율 — 플레이어 손전등이 이 좀비를 비추는 중. 다른 조명 배율보다 우선한다.")]
    public float VisLightFlashlight = 1.8f;
    [Tooltip("자세 배율 — 서서 이동 중. 가장 잘 보이는 자세다.")]
    public float VisPoseWalk = 1f;
    [Tooltip("자세 배율 — 웅크려 이동 중.")]
    public float VisPoseCrouch = 0.6f;
    [Tooltip("자세 배율 — 정지. 움직이지 않으면 가장 덜 보인다.")]
    public float VisPoseIdle = 0.4f;
    [Tooltip("주변이 밝은 환경인가(조명 배율 선택). 구 ZombieSensorySystem 개별 필드에서 이관(B-3) — 월드 밝기 판정 재설계는 P3에서.")]
    public bool EnvironmentIsBright;
    [Tooltip("시야 LoS 차단 판정 레이어. 구 ZombieSensorySystem 개별 필드에서 이관(B-3).")]
    public LayerMask ObstructionMask = ~0;

    [Header("청각 — dB 임계 2종. 거리 아님(거리는 감쇠로 유도 — 반경 변수 금지)")]
    [Tooltip("가청 임계. 도달 dB가 이 미만이면 아예 들리지 않는다. ⚠️ 12 미만으로 내리면 소음 전파 컷오프(NoisePropagationSettingsSO.LowestHearingThresholdDb)도 함께 내려야 한다.")]
    public float HearMinDb = 30f;
    [Tooltip("발각 dB. 도달 dB가 이 이상이면 즉시 발각. 가청 임계~이 값 사이는 60→100 선형 pull-up.")]
    public float HearDetectDb = 70f;

    [Header("이동")]
    [Tooltip("배회 이동 속도(m/s). 홈과 순찰점 사이를 이 속도로 오간다.")]
    public float WanderSpeed = 1.2f;
    [Tooltip("홈 기준 순찰점 추첨 반경(m). 좀비마다 인스턴스에서 덮어쓸 수 있다(ZombieController 의 순찰 반경 오버라이드).")]
    [Min(0f)] public float PatrolRadius = 5f;
    [Tooltip("순찰·수색 지점 추첨 시도 횟수. NavMesh 위 지점이 안 잡히면 이 횟수까지 다시 뽑는다.")]
    [Min(1)] public int PatrolSampleAttempts = 12;
    [Tooltip("배회 도착 후 대기 시간 하한(초). 이 시간에 맞춰 다음 목적지 쪽으로 천천히 몸을 돌린다. 기획 미규정 튜닝값.")]
    [Min(0f)] public float PatrolPauseMinSeconds = 8f;  // 배회 도착 대기 하한(초). 기획 미규정 튜닝값
    [Tooltip("배회 도착 후 대기 시간 상한(초). 매번 하한~상한에서 새로 뽑아 무리가 한 박자로 서고 걷지 않게 한다.")]
    [Min(0f)] public float PatrolPauseMaxSeconds = 10f; // 상한(초). 무리가 한 박자로 움직이지 않게 매번 뽑는다
    [Tooltip("경계 이동 속도(m/s). ⚠️ 현재 이 값을 읽는 코드가 없다 — 경계는 제자리 정지 상태다. 시트 zombie_alert 열을 받아 두기만 한 자리.")]
    public float AlertSpeed = 0f;
    [Tooltip("조사 기본 이동 속도(m/s). 들린 소리가 클수록 여기서 더 올라간다.")]
    public float InvestigateBaseSpeed = 1.6f;
    [Tooltip("조사 이동 속도 상한(m/s). dB 로 올린 속도를 여기서 자른다.")]
    public float InvestigateCapSpeed = 3.5f;
    [Tooltip("가청 임계를 넘은 dB 1 당 조사 속도 증가량(m/s). 기본 속도에 더한 뒤 상한에서 자른다.")]
    public float InvestigateDbToSpeed = 0.05f;
    [Tooltip("추격 속도. ⚠️ Stalker는 차폐 간격 6m와, Dog는 선회 각속도와 한 쌍 — 단독으로 올리면 그 종의 회피가 사라진다(좀비공통 7-1).")]
    public float ChaseSpeed = 3f;
    [Tooltip("진정(홈 복귀) 이동 속도(m/s).")]
    public float SubsideSpeed = 1.2f;
    [Tooltip("속도 램프 가속(m/s²) — D-3. agent.speed 를 목표까지 MoveTowards. 이동이 루트모션이라 체감 가속은 ZombieAnimator 속력 평활(speedSmoothingSeconds)과 합쳐 정해진다. ⚠️ 하한 3.0: 이보다 낮으면 램프 손실이 1.5m를 넘어 「3m 조우 = 사망」 생사 공식이 깨진다. 시트 acceleration_mps2.")]
    [Min(0f)] public float AccelerationMps2 = 6f;
    [Tooltip("제자리 선회 각속도(도/초). 턴 클립 재생 배속이 이 값에 맞춰지므로 올리면 발도 같이 빨라진다(발 미끄러짐 없음). 배회 대기 중 회전만 대기 시간에 맞춰 따로 역산한다.")]
    [Min(0f)] public float TurnSpeedDegreesPerSecond = 120f;
    [Tooltip("이 각도(도) 안이면 다 돌았다고 본다. 0 에 가까우면 미세 각도로 영원히 떤다.")]
    [Range(0f, 45f)] public float TurnDeadZoneDegrees = 8f;
    [Tooltip("목적지 재설정 최소 거리(m). 새 목적지가 기존 목적지에서 이 안이면 경로를 다시 계산하지 않는다 — 매 프레임 재계산으로 인한 버벅임 방지.")]
    [Min(0.01f)] public float DestinationRefreshDistance = 0.3f;
    [Tooltip("추격 대치 재경로 거리(m) — 메시 밖 타겟 앞에 선 뒤, 최근접 지점이 이만큼 옮겨야 다시 따라간다(가다 서다 깜빡임 히스테리시스 · P-036). 기획 미규정 튜닝값, 시트 바인딩 없음.")]
    [Min(0f)] public float ChaseHoldRepathMeters = 1f;

    [Header("전이 타이밍")]
    [Tooltip("발각 전환 이동 정지 시간(초) — 갭 생성기(D-2). 포효 연출 1.5초는 애니 상체 레이어 소관이라 이 값과 무관. ⚠️ 올리면 「3m 조우 = 사망」 AC가 깨진다 — 갭 재계산 없이 만지지 말 것. Dog는 울부짖음 3.0.")]
    public float RoarHoldSeconds = 0.5f;
    [Tooltip("T8a 래치 유예(초) — 타겟을 시야·소리(가청 임계 기준) 모두 놓친 시간. 지나면 관성 추격으로 내려가며 래치가 꺼진다 = 코너 위장의 성립 조건. 0 금지(기둥 스침 위장 방지) · 길면 코너 위장이 죽는다(좀비공통 3-5 원칙 4). 시트 chase_latch_grace_seconds.")]
    [Min(0.1f)] public float ChaseLatchGraceSeconds = 1.5f;
    [Tooltip("T8c 관성 추격 지속 시간(초) — 종별. 만료하면 그 자리에서 조사(의심도 60). 초안 Walker 8 / Listener 3 / Stalker 3 (B-102 확정 대기). 시트 chase_blind_seconds_<종>.")]
    [Min(0f)] public float ChaseBlindSeconds = 8f;
    [Tooltip("타격 상태 유지 시간(초). 붙잡은 뒤 이 시간이 지나야 다음 상태로 넘어간다. 이 동안 타겟을 문 채 지각을 멈추고(좀비공통 7-4) 끝나면 같은 대상으로 추격에 복귀한다. 대상이 사망·탈출하면 남은 시간 동안 지각을 재개한다.")]
    public float AttackLockSeconds = 5f;
    [Tooltip("조사 지점 도착 후 주변 수색 시간(초). 이 동안 새 단서가 없으면 진정(귀가). 시트 investigate_to_wander 가 원천 — 스펙 T7 은 5.")]
    public float InvestigateToWanderSeconds = 5f;
    [Tooltip("조사 수색 반경(m) — 도착 지점 주변에서 수색 지점을 뽑는 범위. 기획 미규정 튜닝값.")]
    [Min(0f)] public float InvestigateSearchRadius = 2.5f;
    [Tooltip("수색 지점 도착 후 머무는 시간(초). 기획 미규정 튜닝값.")]
    [Min(0f)] public float InvestigateSearchPauseSeconds = 1f;

    [Header("공격")]
    [Tooltip("타격 사거리(m). 타격 원점에서 대상까지의 거리로 잰다. 이 안에 들어오면 추격에서 타격으로 넘어간다.")]
    public float AttackRange = 1.5f;
    [Tooltip("타격 준비 시간(초). 타격 진입 후 이 시점에 붙잡힘이 확정된다.")]
    public float AttackWindupSeconds = 0.3f;
    [Tooltip("타격 선딜 동안의 이동 속도 배율 — 추격 속도 × 이 값. 1 이면 추격과 같은 속도로 물러 들어가고, 0 이면 제자리에서 문다. 시트 attack_speed_multiplier.")]
    [Range(0f, 1.5f)] public float AttackSpeedMultiplier = 0.5f;
    [Tooltip("붙잡음 1회가 즉사인가. 켜면 PlayerHealth 가 남은 체력과 무관하게 사망 처리한다(AttackDamage 무시). 현재 쓰는 종 없음 — Lurker 도 AttackDamage 2 로 전환(2026-10-07).")]
    public bool LethalAttack;
    [Tooltip("붙잡음 1회가 깎는 체력 칸 수(PlayerSettingsSO.MaxHits 기준). 기본 종 1 · Lurker 2. LethalAttack 이 켜져 있으면 무시된다.")]
    [Min(1)] public int AttackDamage = 1;

    [Header("접촉 양보 — 플레이어 몸에 닿은 박자의 루트모션 중 「파고드는 성분」을 깎는 비율(상태 무관 · 서버)")]
    [Tooltip("위장 플레이어와 닿았을 때 파고드는 성분 제거 비율. 1 = 완전 양보(밀지 않고 선다 · 접선 성분만 남아 비껴간다), 0 = 기존처럼 밀고 들어간다. 기획 미규정 튜닝값(2026-10-18 사용자 지시).")]
    [Range(0f, 1f)] public float ContactYieldDisguised = 1f;
    [Tooltip("비위장 플레이어와 닿았을 때 파고드는 성분 제거 비율. 추격·타격 중에도 적용된다 — 1 이면 닿는 순간 서서 문다(타격 사거리 AttackRange 가 캡슐 합 0.65m 보다 커야 손해가 없다). 기획 미규정 튜닝값.")]
    [Range(0f, 1f)] public float ContactYieldUndisguised = 1f;

    [Header("무리 동조")]
    [Tooltip("false 면 대표도 무리원도 되지 않는다(참여도 유발도 없음) — Stalker·Dog. 동조는 같은 종끼리만(좀비공통 6-1).")]
    public bool SyncEnabled = true;
    [Tooltip("무리 동조 반경(m). 이 안의 같은 종에게 리더의 타겟이 복사된다. 층이 다르면 반경 안이어도 동조하지 않는다.")]
    public float SyncRadius = 15f;
    [Tooltip("동조 지연 최대(초). 무리원마다 0~이 값 사이 랜덤 지연 뒤 발각 전환에 들어간다 — 전원이 같은 프레임에 포효하지 않게 한다. 0 이면 즉시 동조. 시트 sync_delay_max_seconds.")]
    [Min(0f)] public float SyncDelayMaxSeconds = 1f;
    [Tooltip("스쿼드 스냅샷 최대 유지 시간. 리더 고착 시 팔로워 강제 해제.")]
    [Min(0f)] public float SquadLeashSeconds = 15f;

    public ZombieAudioProfileSO AudioProfile; // 같은 종이 공유하는 이벤트별 오디오

    [Header("이전 발성 설정 — 직렬화 호환용, 현재 재생은 AudioProfile 사용")]
    [Tooltip("비추격(배회·경계·조사·진정) 신음.")]
    public ZombieVoiceProfile IdleVoice = new ZombieVoiceProfile
    {
        AudioIds = new[] { AudioId.Sfx_Zombie_Idle },
        MinIntervalSeconds = 7f,
        MaxIntervalSeconds = 11f,
    };
    [Tooltip("추격 중 으르렁.")]
    public ZombieVoiceProfile ChaseVoice = new ZombieVoiceProfile
    {
        AudioIds = new[] { AudioId.Sfx_Zombie_Chase },
        MinIntervalSeconds = 3f,
        MaxIntervalSeconds = 5f,
    };
    [Tooltip("비명 — 발각 전환·타격 진입 시 1회 + 추격 중에도 텀을 두고 반복.")]
    public ZombieVoiceProfile ScreamVoice = new ZombieVoiceProfile
    {
        AudioIds = new[] { AudioId.Sfx_Zombie_Scream },
        MinIntervalSeconds = 6f,
        MaxIntervalSeconds = 10f,
    };

    [Header("기획 이탈 옵션")]
    [Tooltip("⚠️ 기획 이탈(D-4 ③ · 기본 OFF): 남의 소음으로 청각 발각 시 위장 중인 최근접 플레이어도 타겟으로 확보한다. 스펙 정본은 E8(조사) — 켜는 결정은 기획 소관.")]
    public bool ChaseDisguisedOnAuditoryDetect;
}
