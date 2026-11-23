using Border.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[SelectionBase] // 씬 뷰에서 모델(자식)을 클릭해도 루트가 선택된다 — 디버그 기즈모가 선택 전용이라 루트가 잡혀야 보인다
[RequireComponent(typeof(NetworkObject), typeof(NavMeshAgent))]
public class ZombieController : NetworkBehaviour
{
    [SerializeField] protected AchievementFactEventChannelSO achievementFacts; // 서버 확정 사실
    private ulong achievementLatchedTarget = ulong.MaxValue; // 마지막으로 집계한 래치 대상

    [Header("Data")]
    [SerializeField] private ZombieProfileSO profile; // 종 프로파일. SO 참조를 드는 유일한 지점이다 — 형제 컴포넌트는 Profile 경유로 사용 시점마다 읽는다(값 복사 금지)
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private ZombieAudioEventChannelSO zombieAudioEventChannel; // 클라이언트 오디오 순간 이벤트 채널

    [Header("Weapon Prototype")]
    [SerializeField] private ZombieWeaponHitEventChannelSO weaponHit; // 서버 무기 명중 채널.
    [SerializeField, Min(1f)] private float maxWeaponHealth = 100f; // 기절까지 필요한 누적 피해량.
    [SerializeField, Min(0.01f)] private float staggerSeconds = 0.3f; // 명중 시 경직 시간.
    [SerializeField, Min(0.1f)] private float stunSeconds = 12f; // 기절 및 위장 충전 가능 시간.
    [SerializeField, Min(0f)] private float riseSeconds = 0f; // 기절이 끝난 뒤 일어나는 시간(초) — 기절 시간에 더해지며 그동안 전투 중단·명중 무시. 0 이면 즉시 복귀. 종 전용 컨트롤러의 GetUp 클립 길이에 맞춘다(Dog·Caged 는 0)
    private readonly NetworkVariable<float> weaponHealth = new NetworkVariable<float>(100f); // 서버가 공유하는 좀비 체력.
    private readonly NetworkVariable<uint> weaponHitCount = new NetworkVariable<uint>(); // 서버가 확정한 무기 명중 누적 수 — 클라이언트 피격 연출(ZombieAnimator)의 트리거.
    private double combatDisabledUntil; // 경직 또는 기절·기상 종료 시각.
    private ZombieStateKind riseRecoveryState = ZombieStateKind.Wander; // 기상이 끝나면 들어갈 상태 — 기절 해제 시점에 훅④가 정한 값을 보관
    public float WeaponHealth => weaponHealth.Value; // 모든 플레이어에게 동일한 남은 체력.
    public float MaxWeaponHealth => maxWeaponHealth; // 기절 게이지 총량 — 비율 피해(투척 명중)를 피해량으로 환산하는 기준.
    public bool IsStunned => CurrentState == ZombieStateKind.Stunned; // 기절 중 충전 허용.
    public bool IsRising => CurrentState == ZombieStateKind.Rising; // 기상 중 — 기절처럼 명중을 무시하지만 충전·운반은 닫힌다.
    public bool IsCombatDisabled => IsStunned || IsRising || CurrentState == ZombieStateKind.Staggered; // 전투 중단 여부.

    [Header("Composition")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private ZombieSensorySystem sensorySystem;
    [SerializeField] private ZombieStateMachine stateMachine;
    [SerializeField] private ZombieSquadSync squadSync;

    [Header("Scene Anchors")]
    [SerializeField] private Transform homeAnchor;
    [SerializeField] private Transform visionOrigin;
    [SerializeField] private Transform attackOrigin;

    [Header("Optional")]
    [SerializeField] private Animator animator;
    [Tooltip("거리 컬링할 모델과 모든 LOD 렌더러의 공통 루트. 미지정이면 시각 컬링에서 제외한다.")]
    [SerializeField] private Transform visualRoot;

    [Header("Instance Overrides")]
    [Tooltip("Negative values use ZombieProfileSO.PatrolRadius. Set this to customize only this zombie.")]
    [SerializeField] private float patrolRadiusOverride = -1f;
    [Tooltip("끄면 홈 앵커에 제자리 대기한다. 지각·조사·추격·무리 동조는 정상 동작.")]
    [SerializeField] private bool wanderEnabled = true; // 배회 여부 (군중 좀비는 OFF)
    [Tooltip("끄면 사거리에 들어도 Attack 으로 넘어가지 않고 추격을 유지한다. 지각·포효·추격은 정상 — 튜토리얼 관상용·시연 좀비 전용.")]
    [SerializeField] private bool attackEnabled = true; // 공격 가능 여부. 튜토리얼 Caged 3체·소음 시연 좀비 A 는 OFF(플레이어를 때려 사망시키면 튜토리얼이 끊긴다)

    [Header("Editor Debug")]
    [SerializeField] private bool drawDebugGizmos = true; // 선택 시 디버그 기즈모·상태 라벨 표시 여부(선택된 좀비만 그린다)

    private readonly NetworkVariable<float> suspicion = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ZombieStateKind> stateKind = new NetworkVariable<ZombieStateKind>(ZombieStateKind.Wander, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> suspicionLatched = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> chaseHold = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 추격 대치 중 — 타겟이 NavMesh 밖이라 최근접 지점에서 노려보는 구간. 연출 전용(ZombieAnimator Reach), 전이에 쓰지 않는다(P-036)

    private float silenceTimer;
    private float investigateTimer;
    private float alertTimer;
    private float attackTimer;
    private float chaseLostTimer;
    private float chaseBlindTimer;      // 관성 추격 경과 시간(초). ChaseBlindSeconds 에 닿으면 조사로 내려간다(T8c)
    private bool isChaseBlind;          // 관성 추격 중인가 — 타겟 없이 의심도 100 을 유지하는 유일한 구간(좀비공통 4-4)
    private float investigationStimulusDb;
    private Vector3 lastKnownPosition;
    private Vector3 lastKnownVelocity;  // 타겟을 마지막으로 지각한 프레임의 타겟 속도. 관성 추격 첫 간선의 기준(U2) — 상실 뒤에는 갱신하지 않는다(정보 누출 방지)
    private Vector3 homePosition;
    private IZombiePerceptionSource currentTargetSource;
    private Transform currentTarget;

    // 타겟의 NetworkObjectId 는 복제한다 — ZombieThreatAudioDirector 가 "발각된 게 로컬 플레이어인가"를
    // 클라이언트에서 판정해야 하기 때문이다(EH-34). 0 은 "타겟 없음"이다.
    private readonly NetworkVariable<ulong> currentTargetNetworkObjectId = new NetworkVariable<ulong>(0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool hasTrackingStimulus;
    private bool hasTargetStimulus;
    private bool hadStimulusThisFrame;
    private bool isDormant;             // 휴면 여부 — 튜토리얼 연출 전용. Update 최상단에서 전부 멈춘다
    private Vector3 spawnPosition; // 배치 시점 위치 — ServerResetToSpawn 복귀 지점. homeAnchor(배회 중심)가 아니라 배치된 Transform 그대로다
    private Quaternion spawnRotation; // 배치 시점 회전 — ServerResetToSpawn 이 복원한다. 앵커 회전을 쓰면 배치 방향과 어긋나 리셋 직후 플레이어를 정면으로 보게 된다
    private NetworkTransform networkTransform; // 서버 권위 NetworkTransform — 리셋 워프를 원격에 즉시 전파(Teleport). 프리팹 구성에 따라 없을 수 있어 널 허용(있을 때만 태운다)
    private Rigidbody body; // 루트 리지드바디(kinematic·Interpolate) — 리셋 워프는 트랜스폼이 아니라 이 포즈를 옮겨야 남는다
    private bool isFrameSuppressed; // 훅① 동결 유지 여부 — 진입 프레임에만 StopAgent 를 돌리기 위한 에지 검출용
    private float playerContactUntil; // 플레이어와의 몸 접촉이 유효한 시각(Time.time). OnCollisionStay 가 갱신, 이 시각까지 「접촉 중」(P4 접촉 정지 · 접촉 양보)
    private bool playerContactDisguised; // 마지막 접촉 플레이어가 위장 중이었는가 — 양보 비율(ZombieProfileSO.ContactYield*)과 P4 정지 판정을 가른다
    private Vector3 playerContactNormal; // 마지막 접촉의 수평 단위 법선(플레이어 → 좀비). ZombieRootMotion 이 이 방향으로 파고드는 성분을 깎는다
    private static readonly int turnStateTagHash = Animator.StringToHash("Turn"); // 제자리 회전 상태 태그(AnimationSetupTool 메뉴 4 가 붙인다)
    private const float playerContactHoldSeconds = 0.2f; // 접촉 판정 유지 시간(초) — Exit 콜백에 기대지 않고 Stay 가 끊기면 자연 만료된다

    public static bool DebugIgnorePlayers; // [촬영 도구] 서버 전용 — true 면 지각을 수집만 하고 버린다(배회는 유지, 인지·추격 없음). 좀비 디렉터만 쓴다

    public ZombieProfileSO Profile => profile;
    public ZombieRuntimeRegistrySO RuntimeRegistry => runtimeRegistry; // 지각 소스 목록 통로 — 종 파생의 훅②(Dog 후각)와 옆걸음 양보(P3, ZombieStateMachine)가 읽는다
    public NavMeshAgent Agent => agent;
    public Animator Animator => animator;
    /// <summary>시각 컬링이 수집할 명시적 모델 루트.</summary>
    public Transform VisualRoot => visualRoot;
    public Transform VisionOrigin => visionOrigin != null ? visionOrigin : transform;
    public Transform AttackOrigin => attackOrigin != null ? attackOrigin : transform;
    public Vector3 HomePosition => homePosition;
    public Vector3 LastKnownPosition => lastKnownPosition;
    public Vector3 LastKnownVelocity => lastKnownVelocity; // 타겟의 마지막 속도 벡터 — 관성 추격 첫 간선 선택의 기준
    public bool IsChaseBlind => isChaseBlind; // 관성 추격 중인가
    public bool IsChaseHolding => chaseHold.Value; // 추격 대치 중인가(복제값) — 클라 연출이 읽는다
    public bool HoldsSuspicion => suspicionLatched.Value || isChaseBlind; // 의심도 냉각을 막는 구간 — 래치 중이거나 관성 추격 중(좀비공통 3-4)
    public Transform CurrentTarget => currentTarget;
    public IZombiePerceptionSource CurrentTargetSource => currentTargetSource;
    public ulong CurrentTargetNetworkObjectId => currentTargetNetworkObjectId.Value;
    public float SuspicionValue => suspicion.Value;
    public bool IsLatched => suspicionLatched.Value;

    /// <summary>
    /// 이 소스가 latch 로 물고 있는 현재 타겟인지 판정한다.
    /// 추격이 확정된 상대에겐 위장이 통하지 않는다(좀비공통 3-7·8-1 이탈 — 사용자 확정 2026-09-11 · B-084) — 비발각 좀비에 대한 위장 원칙은 그대로다.
    /// </summary>
    /// <param name="source">검사할 지각 소스.</param>
    /// <returns>latch 중이고 그 소스가 현재 타겟이면 true.</returns>
    public bool IsLatchedTarget(IZombiePerceptionSource source)
    {
        return IsLatched && source != null && ReferenceEquals(source, currentTargetSource);
    }
    public bool HasPlayerContact => Time.time < playerContactUntil; // 플레이어와 몸이 닿아 있는가(서버 · 위장 무관). 상태 머신이 배회·경계·조사 이동을 멈추거나 비켜서는 근거(P4 확장)
    public bool HasDisguisedContact => HasPlayerContact && playerContactDisguised; // 닿아 있는 플레이어가 위장 중인가(서버)
    public Vector3 PlayerContactNormal => playerContactNormal; // 마지막 접촉의 수평 단위 법선(플레이어 → 좀비). HasPlayerContact 가 false 면 의미 없음
    public float ContactYieldFraction => playerContactDisguised ? profile.ContactYieldDisguised : profile.ContactYieldUndisguised; // 이번 접촉에 적용할 파고드는 성분 제거 비율(0~1) — 위장 여부로 프로필 값을 고른다
    public ZombieStateKind CurrentState => stateKind.Value;
    public bool IsEngaging => CurrentState == ZombieStateKind.RoarTransition || CurrentState == ZombieStateKind.Chase || CurrentState == ZombieStateKind.Attack; // 타겟을 쥔 교전 상태인가 — 접촉 규칙을 가른다: 교전 중 접촉 = 도달(정지·응시), 그 외 = 장애물(비껴가기·양보)
    public bool IsFollower => squadSync != null && squadSync.IsFollower;
    public bool HasTrackingStimulus => hasTrackingStimulus;

    /// <summary>
    /// 이번 프레임에 <see cref="CurrentTarget"/> 본인을 지각했는가. 추격 유지 판정은 이 값으로 한다 —
    /// "아무 자극"(<see cref="HasTrackingStimulus"/>)으로 판정하면 다른 플레이어의 소음이
    /// 추격 상실 타이머를 영구히 리셋해 좀비가 Chase 에서 영영 못 내려온다.
    /// </summary>
    public bool HasTargetStimulus => hasTargetStimulus;
    public bool HadStimulusThisFrame => hadStimulusThisFrame;
    public float InvestigationStimulusDb => investigationStimulusDb;
    public ZombieController SquadLeader => squadSync != null ? squadSync.Leader : null;
    public float PatrolRadius => patrolRadiusOverride >= 0f
        ? patrolRadiusOverride
        : profile != null ? profile.PatrolRadius : 0f;
    public bool WanderEnabled => wanderEnabled; // 배회 여부. OFF면 배회 상태에서 홈 대기
    public bool AttackEnabled => attackEnabled; // 공격 가능 여부. OFF면 사거리 안에서도 Chase 유지, 잡힘 이벤트 미발행

    /// <summary>
    /// 서버 회전 상태의 정본. 루트 Rigidbody 가 Interpolate 라 transform.rotation 직접 대입은 매 렌더
    /// 프레임 보간에 덮인다 — 회전 상태는 여기 누적하고 적용은 ZombieRootMotion 의 MoveRotation 이 한다.
    /// </summary>
    public Quaternion ServerFacingRotation { get; private set; }

    /// <summary>서버 회전 상태를 갱신한다(ZombieStateMachine 전용).</summary>
    /// <param name="rotation">새 회전.</param>
    public void ServerSetFacingRotation(Quaternion rotation) => ServerFacingRotation = rotation;

    /// <summary>지금 유효한 선회 속도(도/초). 턴 클립 배속의 기준이다(A3).</summary>
    public float CurrentTurnDegreesPerSecond => stateMachine.CurrentTurnDegreesPerSecond;

    /// <summary>목표 방향까지 남은 각도(도, 부호 있음). ZombieAnimator 가 TurnBlend 로 넘긴다(A3).</summary>
    public float FacingErrorDegrees => stateMachine.FacingErrorDegrees;

    /// <summary>
    /// 제자리 회전 애니(Base Layer 「Turn」 상태)가 재생 중인가. 참이면 몸을 돌리는 주체가 코드가 아니라
    /// 턴 클립의 루트 회전이다(A3). 전이 중에도 참이라야 블렌드 구간에서 회전 주체가 두 번 바뀌지 않는다.
    /// </summary>
    public bool IsAnimatedTurning =>
        animator.GetCurrentAnimatorStateInfo(0).tagHash == turnStateTagHash
        || animator.GetNextAnimatorStateInfo(0).tagHash == turnStateTagHash;

    /// <summary>턴 클립이 만든 루트 Y 회전을 상태 머신에 넘긴다(ZombieRootMotion 전용).</summary>
    /// <param name="degrees">회전량(도, 부호 있음).</param>
    public void ServerApplyAnimatedYaw(float degrees) => stateMachine.ServerApplyAnimatedYaw(degrees);

    /// <summary>복제된 상태가 바뀔 때 발행된다. 서버·클라이언트 모두에서 발행되므로 애니메이션 구동에 쓴다.</summary>
    public event System.Action<ZombieStateKind> StateChanged;

    /// <summary>복제된 명중 수가 늘 때마다 발행된다. 경직 중 재명중처럼 상태가 바뀌지 않는 명중도 알린다.</summary>
    public event System.Action WeaponHitLanded;

    /// <summary>서버에서 확정된 오디오 순간 이벤트를 모든 클라이언트에 전달한다.</summary>
    /// <param name="kind">확정된 이벤트 종류.</param>
    public void PublishAudioEvent(ZombieAudioEventKind kind)
    {
        Log.D($"[ZombieController] 오디오 이벤트: {kind}");
        if (!IsServer || !IsSpawned) return;
        PublishAudioEventClientRpc(kind);
    }

    /// <summary>호스트를 포함한 각 클라이언트에서 확정 이벤트를 한 번 발행한다.</summary>
    /// <param name="kind">서버가 확정한 이벤트.</param>
    [ClientRpc]
    private void PublishAudioEventClientRpc(ZombieAudioEventKind kind)
    {
        zombieAudioEventChannel.RaiseEvent(NetworkObjectId, kind);
    }

    // ── 종 확장 훅 4개 (Docs/Handoff/좀비_종축_도입.md §3-1 + 훅④ 기절 복귀) ─────────────────
    // Update() 는 이 base 에 봉인된 템플릿 메서드다. 파생은 Update 를 선언하지 말 것 —
    // 유니티 매직 메서드 섀도잉으로 base Update 가 통째로 죽는다(컴파일 에러 없음).
    // 3-5 고정 갱신 순서(위장→즉발→시각→pull-up→냉각→클램프→래치)는 base 소유이며 파생이 바꿀 수 없다.

    /// <summary>
    /// 훅 ① — 이번 서버 프레임을 통째로 동결할지(E7). true 면 지각·의심도·냉각·전이·이동·타이머가
    /// 전부 건너뛰어지고, latch·타겟·의심도·현재 상태가 그대로 보존된다(해제 시 그 자리에서 재개).
    /// Update 최상단(isDormant 옆)에서 호출된다. 기본 = 동결 없음. 사용 종: Stalker.
    /// </summary>
    /// <returns>이번 프레임 동결 여부.</returns>
    protected virtual bool ServerShouldSuppressFrame() => false;

    /// <summary>
    /// 훅 ② — 종 전용 자극을 지각 조립체에 얹는다. 시각·청각 수집 직후, 의심도 반영 직전에 호출된다.
    /// SO 값으로 표현되는 차이는 여기가 아니라 프로파일로 — 이 훅은 규칙 주입 전용이다(Listener 반경 2단 · Dog 후각).
    /// 기본 = 기여 없음.
    /// </summary>
    /// <param name="builder">이번 프레임의 지각 조립체.</param>
    protected virtual void ServerContributePerception(ZombiePerceptionBuilder builder) { }

    /// <summary>
    /// 훅 ③ — FSM 상태를 종 전용 구현으로 교체한다. ZombieStateMachine.CreateState 가 우선 조회하며,
    /// null 이면 공통 상태를 쓴다. 전이표(T1~T13)는 교체와 무관하게 유지된다. 사용 종: Dog(울부짖음·돌진).
    /// </summary>
    /// <param name="kind">머신이 만들려는 상태.</param>
    /// <returns>교체 상태 — 해당 없으면 null.</returns>
    public virtual IZombieState TryCreateSpeciesState(ZombieStateKind kind) => null;

    /// <summary>
    /// 연출 동결 여부 — 전 클라이언트에서 같은 답이어야 한다(복제값 기반). true 인 동안
    /// ZombieAnimator 가 파라미터 갱신까지 멈춰 「그 순간 자세 그대로」 굳는다. 사용 종: Stalker.
    /// </summary>
    public virtual bool IsVisualFrozen => false;

    /// <summary>
    /// 소멸(투명·비접촉) 여부 — 전 클라이언트에서 같은 답이어야 한다(복제값 기반). true 인 동안
    /// 지도 프로브·시선 통계가 이 좀비를 건너뛴다(보이지 않는 좀비가 지도·칭호에 새지 않게). 사용 종: Lurker.
    /// </summary>
    public virtual bool IsVanished => false;

    /// <summary>
    /// 발각 전환(RoarTransition)이 추격으로 이어지지 않고 취소돼 배회로 돌아갈 때 포효 레이어를 즉시 끊을지 — 종 고정값이라
    /// 전 클라이언트에서 같은 답이다. 기본 = 끊지 않음(포효 클립 끝까지 재생 · 기존 종 동작 유지). 사용 종: Teacher(경고 취소).
    /// </summary>
    public virtual bool InterruptsRoarOnCancel => false;

    /// <summary>같은 개체의 상태 머신 — 종 파생이 발각 전환을 거치지 않는 전이(Student 쿨다운 중 즉시 도주)를 걸 때만 쓴다.</summary>
    protected ZombieStateMachine StateMachine => stateMachine;

    /// <summary>
    /// 훅 ④ — 기절이 끝날 때 복귀할 상태. Update 의 기절 해제 분기에서 호출되며 반환 직후 그 상태로 강제 전이한다.
    /// 복귀에 딸린 부수 효과(운반 해제 등)는 여기서 처리한다. 기본 = 배회. 사용 종: Oiler(업혀 있으면 내려놓고 도주).
    /// </summary>
    /// <returns>기절 해제 직후 진입할 상태.</returns>
    protected virtual ZombieStateKind ServerResolveStunRecoveryState() => ZombieStateKind.Wander;

    private void Awake()
    {
        CacheHomePosition();
        spawnPosition = transform.position; // 배치된 Transform 그대로 — 앵커는 배회 중심일 뿐 스폰 포즈가 아니다
        spawnRotation = transform.rotation;
        ServerFacingRotation = transform.rotation; // 회전 정본 초기화 — 배치 포즈에서 출발
        networkTransform = GetComponent<NetworkTransform>();
        body = GetComponent<Rigidbody>();

        if (agent == null || sensorySystem == null || stateMachine == null || squadSync == null)
        {
            Debug.LogError($"[{nameof(ZombieController)}] {name} prefab composition is incomplete.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// 좀비 런타임 집합에 자신을 올린다. AI 와 달리 서버로 가르지 않는다 —
    /// 이 집합을 읽는 쪽이 클라이언트 연출(<see cref="ZombieThreatAudioDirector"/>)이라
    /// 비서버 인스턴스도 목록에 있어야 복제된 의심도를 훑을 수 있다.
    /// 형제 부품(ZombieSquadSync)과 같은 등록 시점이다.
    /// </summary>
    private void OnEnable() => runtimeRegistry?.RegisterZombie(this);

    private void OnDisable() => runtimeRegistry?.UnregisterZombie(this);

    /// <summary>복제 상태를 구독하고 서버 AI와 전투 체력을 초기화한다.</summary>
    public override void OnNetworkSpawn()
    {
        CacheHomePosition();

        // 상태는 서버가 쓰고 전 클라이언트에 복제된다. 애니메이션은 이 복제 상태로 구동하므로 서버 게이트 앞에서 구독한다.
        stateKind.OnValueChanged += HandleStateKindChanged;
        weaponHitCount.OnValueChanged += HandleWeaponHitCountChanged;

        if (!IsServer)
        {
            // 좀비 AI 도 NavMesh 베이크도 서버 전용이다(AC-20) — 비서버 인스턴스의 에이전트는 딛을 NavMesh 가
            // 아예 없어, 켜지는 순간 좀비 수만큼 "Failed to create agent because there is no valid NavMesh" 가
            // 쏟아져 진짜 에러를 덮는다. 위치는 NetworkTransform 이 밀어 주고 ZombieAnimator 도 복제 좌표로
            // 구동하므로(agent.velocity 를 쓰지 않는다) 꺼도 잃는 것이 없다.
            agent.enabled = false;

            // 클라이언트 회전/위치는 NetworkTransform 자체 보간이 담당한다 — 리지드바디 보간까지 겹치면
            // 서로 트랜스폼을 덮어써 회전이 씹힌다(서버의 문워크와 같은 충돌). 클라는 물리 보간을 끈다.
            GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
            return;
        }

        // 씬 배치 좀비는 Agent 를 꺼 둔 채 로드할 수 있다. NavMeshSurface 가 전부 등록되기 전에
        // Agent 가 켜지면 먼저 등록된 다른 철창의 메시로 붙으며 배치 위치까지 바뀔 수 있다.
        // 씬 로드가 끝난 서버 스폰 시점에 켠다. 클라이언트는 위 분기에서 계속 꺼 둔다.
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.enabled = true;

        // 이동을 애니메이션이 만들게 한다. 자식 Animator 의 델타는 ZombieRootMotion 이 이 루트로 옮긴다.
        // 클라이언트는 꺼진 채로 두어 NetworkTransform 이 밀어 준 복제 좌표와 싸우지 않게 한다.
        animator.applyRootMotion = true;

        weaponHealth.Value = maxWeaponHealth;
        weaponHit.Hit += HandleWeaponHit;
        stateMachine.ServerInitialize();
    }

    /// <summary>복제 상태 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        stateKind.OnValueChanged -= HandleStateKindChanged;
        weaponHitCount.OnValueChanged -= HandleWeaponHitCountChanged;
        if (IsServer) weaponHit.Hit -= HandleWeaponHit;
    }

    /// <summary>복제된 명중 수 증가를 받아 WeaponHitLanded 를 발행한다.</summary>
    /// <param name="previous">이전 누적 수.</param>
    /// <param name="current">새 누적 수.</param>
    private void HandleWeaponHitCountChanged(uint previous, uint current)
    {
        WeaponHitLanded?.Invoke();
    }

    /// <summary>복제된 상태 변화를 받아 StateChanged 를 발행한다.</summary>
    /// <param name="previous">이전 상태.</param>
    /// <param name="current">새 상태.</param>
    private void HandleStateKindChanged(ZombieStateKind previous, ZombieStateKind current)
    {
        StateChanged?.Invoke(current);
    }

    /// <summary>서버에서 경직·기절 시간을 처리하고 활성 좀비의 지각과 행동을 갱신한다.</summary>
    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (IsCombatDisabled)
        {
            stateMachine.StopAgent();
            if (Time.timeAsDouble < combatDisabledUntil) return;

            // 복귀 상태 — 경직은 배회 고정, 기절은 훅④가 정한다(기본 배회 · Oiler 는 운반 해제 후 도주).
            ZombieStateKind recovery = ZombieStateKind.Wander;
            if (IsStunned)
            {
                weaponHealth.Value = maxWeaponHealth;
                recovery = ServerResolveStunRecoveryState(); // 부수 효과(운반 해제)는 기절이 끝나는 지금 일어난다 — 업힌 채 일어나지 않게
                if (riseSeconds > 0f)
                {
                    // 기상 — 일어나는 동안도 전투 중단. 복귀 상태는 보관했다가 기상이 끝나면 들어간다(ZombieAnimator 가 Rising 진입에 GetUp 을 건다)
                    riseRecoveryState = recovery;
                    combatDisabledUntil = Time.timeAsDouble + riseSeconds;
                    ServerSetState(ZombieStateKind.Rising);
                    return;
                }
            }
            else if (IsRising) recovery = riseRecoveryState;
            stateMachine.SwitchState(recovery, true);
        }
        if (isDormant) return; // 휴면 — 지각·상태머신·의심도 전부 정지. 스폰 포즈(등 돌림 등)가 그대로 유지된다

        // 훅① — 종이 이번 프레임을 통째로 동결할지 정한다(E7 · Stalker 정지 볼륨).
        // 동결은 「보존」이다: 아래를 전부 건너뛰므로 latch·타겟·의심도·현재 상태·잔여 타이머가
        // 손대지 않은 채 남고, 해제되면 그 자리에서 이어진다. 진입 프레임에만 이동을 끊는다 —
        // 매 프레임 StopAgent 를 돌리면 해제 직후 경로 재요청이 한 프레임 밀린다.
        if (ServerShouldSuppressFrame())
        {
            if (!isFrameSuppressed)
            {
                isFrameSuppressed = true;
                stateMachine.StopAgent();
            }
            return;
        }

        isFrameSuppressed = false;

        float deltaTime = Time.deltaTime;

        // 지각·상태 갱신보다 타겟 유효성이 먼저다. 사망(관전)·위장·파괴된 대상을 계속 물고 있으면
        // Attack 의 탈출 조건(CurrentTarget == null)이 성립하지 않고 추격 상실 타이머도 리셋돼
        // 좀비가 시신에 영구 고착된다.
        ServerValidateTarget();

        // 공격(선딜·타격 락) 중에는 대상을 문 채 지각을 멈춘다(좀비공통 7-4 「락 동안 전 채널 OFF」) — 대상 교체·의심도 냉각·
        // 무리원의 대표 복사가 모두 돌지 않아 공격 도중 타겟을 잃을 경로가 없다. 타격 후에도 타겟을 놓지 않으므로 락이 끝나면
        // 같은 대상으로 추격에 복귀한다. 대상이 사망·탈출하면 위의 ServerValidateTarget 이 타겟을 비워 이 조건이 풀리고,
        // 남은 락 동안 다시 보고 듣는다 — 한 번 잡을 때마다 석상이 되지 않는다.
        if (stateMachine.CurrentStateKind == ZombieStateKind.Attack && currentTarget != null)
        {
            // 자극 플래그도 함께 내려 잔상이 남지 않게 한다.
            hadStimulusThisFrame = false;
            hasTrackingStimulus = false;
            hasTargetStimulus = false;
            stateMachine.ServerTick(deltaTime);
            squadSync?.ServerReleaseFollowersIfSettled(deltaTime);
            return;
        }

        // 동조 지연 — 예약된 무리원은 지연이 끝나는 프레임에 합류한다. 그 전까지는 아래 자기 지각 분기를 탄다.
        squadSync?.ServerAdvanceJoinDelay(deltaTime);

        if (squadSync != null && squadSync.IsFollower)
        {
            squadSync.ServerSynchronizeFollower();
        }
        else
        {
            // 지각 수집 → 훅②(종 전용 자극 얹기) → 얼리기. 순서가 계약이다 —
            // 훅②는 의심도 반영 직전에만 끼어들고, 3-5 고정 갱신 순서 자체는 base 가 소유한다.
            ZombiePerceptionBuilder builder = sensorySystem.ServerCollectPerception();
            if (DebugIgnorePlayers)
            {
                // [촬영 도구] 어그로 끔 — 수집은 돌려 소음 큐만 비우고 결과는 버린다(훅② 생략). 빈 프레임이라 냉각만 돈다
                builder.Reset();
            }
            else
            {
                ServerContributePerception(builder);
            }
            ServerApplyPerception(builder.Build(), deltaTime);
        }

        stateMachine.ServerTick(deltaTime);
        squadSync?.ServerReleaseFollowersIfSettled(deltaTime);
    }

    /// <summary>
    /// 여러 플레이어의 피해를 서버에서 합산한다. 피해량 0은 체력 소모 없이 경직만 적용한다.
    /// 경직은 남은 시간보다 짧아지지 않는다 — 긴 경직(양손 투척) 도중의 짧은 명중이 좀비를 일찍 풀어 주면 안 된다.
    /// </summary>
    /// <param name="targetId">명중한 좀비 ID.</param>
    /// <param name="damage">서버 피해량. 0이면 경직 전용 명중이다.</param>
    /// <param name="hitStaggerSeconds">이 명중의 경직 시간(초). 0 이하면 기본 경직(<see cref="staggerSeconds"/>)을 쓴다.</param>
    private void HandleWeaponHit(ulong targetId, float damage, float hitStaggerSeconds, ulong attackerId, WeaponHitKind kind)
    {
        if (!IsServer || !IsSpawned || targetId != NetworkObjectId || IsStunned || IsRising || isDormant
            || !float.IsFinite(damage) || damage < 0f || !float.IsFinite(hitStaggerSeconds)) return;
        squadSync.ServerDisbandSquad();
        ServerReleaseTarget();
        stateMachine.EndTimedFacing();
        stateMachine.StopAgent();
        weaponHealth.Value = Mathf.Max(0f, weaponHealth.Value - damage);
        bool stunned = weaponHealth.Value <= 0f;
        double until = Time.timeAsDouble + (stunned ? stunSeconds : hitStaggerSeconds > 0f ? hitStaggerSeconds : staggerSeconds);
        // 이미 경직 중일 때만 남은 시간과 비교한다 — 경직이 아닌 상태의 combatDisabledUntil 은 지난 명중의 잔재다.
        if (!stunned && CurrentState == ZombieStateKind.Staggered) until = System.Math.Max(until, combatDisabledUntil);
        combatDisabledUntil = until;
        ServerSetState(stunned ? ZombieStateKind.Stunned : ZombieStateKind.Staggered);
        weaponHitCount.Value++;
        if (stunned && kind == WeaponHitKind.Bat && attackerId != ulong.MaxValue)
            achievementFacts.RaiseEvent(AchievementFactKind.BatStun, attackerId);
    }

    private void ServerApplyPerception(ZombiePerceptionFrame frame, float deltaTime)
    {
        if (!IsServer) return;
        // 종 전용 자극(훅②)도 자극이다 — 여기 넣지 않으면 Listener 반경 안에서 걷고 있는데도
        // 「단서 0」으로 판정돼 유예 뒤 냉각이 돈다.
        bool hasDirectStimulus = frame.DirectSuspicionFloor > 0f;
        hadStimulusThisFrame = frame.HasVisualStimulus || frame.HasAuditoryStimulus || hasDirectStimulus;
        hasTrackingStimulus = frame.HasTrackingStimulus;
        float nextSuspicion = suspicion.Value;

        // 추격 중에는 소리에 타겟을 뺏기지 않는다. 단 물고 있던 타겟이 이미 해제된 뒤라면
        // (사망·위장으로 ServerReleaseTarget 이 돈 경우) 청각으로 새 대상을 잡을 수 있어야 한다 —
        // 이 예외가 없으면 Chase 상태에 갇힌 채 아무도 새로 타겟팅하지 못한다.
        bool chaseHoldsValidTarget = CurrentState == ZombieStateKind.Chase && currentTargetSource != null;

        // 명세 3-5 고정 순서: 즉시 발각 → 시각 가산 → 청각 pull-up → 냉각 → clamp → latch.
        if (frame.HasInstantVisualDetection)
        {
            nextSuspicion = 100f;
            AcceptTarget(frame.PreferredTarget, frame.VisualPosition);
        }
        else
        {
            if (frame.VisualGainPerSecond > 0f)
            {
                nextSuspicion += frame.VisualGainPerSecond * deltaTime;
                AcceptTarget(frame.PreferredTarget, frame.VisualPosition);
                investigationStimulusDb = 0f;
            }

            if (frame.HasAuditoryStimulus && !chaseHoldsValidTarget)
            {
                float previous = nextSuspicion;
                float pullUp = frame.AuditoryEffectiveDb >= profile.HearDetectDb
                    ? 100f
                    : Mathf.Lerp(profile.ThInvestigate, 100f,
                        Mathf.InverseLerp(profile.HearMinDb, profile.HearDetectDb, frame.AuditoryEffectiveDb));

                nextSuspicion = Mathf.Max(nextSuspicion, pullUp);
                if (nextSuspicion > previous)
                {
                    lastKnownPosition = frame.AuditoryPosition;
                    investigationStimulusDb = frame.AuditoryEffectiveDb;
                }

                if (frame.AuditoryEffectiveDb >= profile.HearDetectDb
                    && frame.PreferredTarget != null
                    && frame.PreferredTarget.Root != null)
                {
                    AcceptTarget(frame.PreferredTarget, frame.PreferredTarget.Root.position);
                }
                else if (isChaseBlind
                    && frame.AuditoryTarget != null
                    && IsTargetable(frame.AuditoryTarget, frame.AuditoryTarget.Root))
                {
                    // T8b — 관성 추격 중에는 발각 dB 미달이어도 가청 임계 이상의 플레이어 소음이면 그 플레이어를 문다.
                    // 주인이 타겟 후보가 아닌 소음(문·투척물)은 AuditoryTarget 이 null 이라 무시된다(좀비공통 13 Q9).
                    AcceptTarget(frame.AuditoryTarget, frame.AuditoryTarget.Root.position);
                }
            }

            // 종 전용 자극 바닥(훅②) — 청각 pull-up 과 같은 max 합류다. 큰 쪽이 이기므로 순서 경합이 없다.
            // 위장·차폐와 무관한 채널이라 추격 중에도 막지 않는다: Dog 는 소지자가 바뀌면 즉시 재타겟해야 하고,
            // Listener 는 5m 안의 이동을 놓치면 안 된다.
            if (hasDirectStimulus)
            {
                if (frame.DirectSuspicionFloor > nextSuspicion)
                {
                    nextSuspicion = frame.DirectSuspicionFloor;
                    lastKnownPosition = frame.DirectPosition;
                    investigationStimulusDb = 0f; // 거리·후각 판정이라 dB 가 없다 — 조사 속도는 기본값을 쓴다
                }

                // 바닥 100 = 발각. latch 는 타겟 확보를 전제하므로(불변식) 여기서 함께 문다.
                if (frame.DirectSuspicionFloor >= 100f)
                {
                    AcceptTarget(frame.PreferredTarget, frame.DirectPosition);
                }
            }

            if (!hadStimulusThisFrame)
            {
                silenceTimer += deltaTime;
                if (!HoldsSuspicion && silenceTimer >= profile.SuspicionGraceSeconds)
                {
                    nextSuspicion = Mathf.MoveTowards(nextSuspicion, 0f, profile.CoolRatePerSecond * deltaTime);
                }
            }
            else
            {
                silenceTimer = 0f;
            }
        }

        nextSuspicion = Mathf.Clamp(nextSuspicion, 0f, 100f);

        // 래치는 여기서 켜지 않는다 — 값(의심도 100)이 아니라 전이가 켠다(좀비공통 3-5 원칙 4).
        // 「의심도 100 + 타겟 확보」가 되면 상태 머신이 발각 전환으로 넘기고, 그 전이 훅이 ServerSetLatched 를 부른다.
        // 값으로 켜면 관성 추격에서 끈 래치가 다음 프레임에 도로 켜진다.
        // NavMesh 밖 플레이어도 그대로 발각한다 — 튜토리얼 철창 좀비처럼 플레이어 자리에 메시가 없어도 포효는
        // 성립해야 한다. 도달 불가는 Chase 안에서 최근접점 정지로 처리한다(좀비공통 E11).
        bool holdsTarget = currentTargetSource != null;
        if (isChaseBlind)
        {
            // 관성 추격은 타겟 없이 100 을 유지하는 유일한 구간이다 — 단서가 없어도 내려가지 않는다(좀비공통 3-4).
            nextSuspicion = 100f;
        }
        else if (nextSuspicion >= 100f && !holdsTarget)
        {
            // 타겟 없는 100 은 유지하지 않는다. 조사 단계로 눌러 두고 재확보를 기다린다(좀비AI E8).
            ServerSetLatched(false, false);
            nextSuspicion = profile.ThInvestigate;
        }

        suspicion.Value = nextSuspicion;

        // 추격 유지 판정. 이 프레임에 지각한 대상이 지금 물고 있는 타겟 본인일 때만 참이다.
        // 청각 대상도 따로 본다 — 눈에는 다른 플레이어가 들어왔어도 타겟의 소리가 닿는 동안은 접촉이 이어진다(T8a 「시야·소리 모두」).
        hasTargetStimulus = currentTargetSource != null
            && (ReferenceEquals(frame.PreferredTarget, currentTargetSource)
                || ReferenceEquals(frame.AuditoryTarget, currentTargetSource));

        // 마지막 접촉 기록 — 접촉한 프레임에만 갱신한다. 놓친 뒤에 읽으면 보이지 않는 플레이어의 현재 위치·속도가 새어 나온다.
        if (hasTargetStimulus)
        {
            lastKnownPosition = currentTarget.position;
            lastKnownVelocity = currentTargetSource.Velocity;
        }
    }

    /// <summary>
    /// 래치를 켜고 끈다. 값 대입은 여기 한 곳뿐이다 — 켜는 쪽은 상태 전이 훅(ZombieStateMachine.SwitchState)과
    /// 대표 스냅샷 복사만 부른다. 타겟 없이 켜 달라는 요청은 끈 것으로 처리한다(불변식 IsLatched ⇒ CurrentTarget != null).
    /// </summary>
    /// <param name="on">켤지 여부.</param>
    /// <param name="formSquad">켜는 김에 주변 같은 종을 무리로 묶을지 — 발각 전환 진입에서만 true.</param>
    public void ServerSetLatched(bool on, bool formSquad)
    {
        // 대표 복사가 매 프레임 부르므로 진입 트레이스를 두지 않는다.
        if (!IsServer) return;

        bool next = on && currentTargetSource != null;
        if (suspicionLatched.Value != next) suspicionLatched.Value = next;
        if (next) RecordAchievementLatch();
        else achievementLatchedTarget = ulong.MaxValue;

        if (next && formSquad && squadSync != null && !squadSync.IsFollower)
        {
            squadSync.ServerFormSquadSnapshot();
        }
    }

    /// <summary>루트모션이 가장자리·벽에 막혀 전진하지 못한다고 알린다 — 상태 머신이 목적지를 포기하고 멈춘다.</summary>
    public void ServerNotifyMovementBlocked()
    {
        if (!IsServer) return;
        stateMachine.MarkBlocked();
    }

    private void AcceptTarget(IZombiePerceptionSource target, Vector3 position)
    {
        lastKnownPosition = position;
        if (target == null || target.Root == null) return;

        // 매 프레임 다시 받으므로 대상이 바뀔 때만 남긴다.
        if (!ReferenceEquals(target, currentTargetSource))
        {
            Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} 타겟 확보 {target.NetworkObjectId} (이전 {(currentTargetSource != null ? currentTargetSource.NetworkObjectId.ToString() : "None")}) | 상태 {stateKind.Value} · 의심도 {suspicion.Value:0}");
        }

        currentTargetSource = target;
        currentTarget = target.Root;
        currentTargetNetworkObjectId.Value = target.NetworkObjectId;
        if (IsLatched) RecordAchievementLatch();
    }
    /// <summary>
    /// 물고 있는 타겟이 아직 유효한지 검사한다. Transform 은 Unity 의 == 오버로드로 파괴까지 걸러지므로
    /// 인터페이스 참조보다 먼저 확인한다 — 파괴된 컴포넌트의 프로퍼티를 읽으면 예외가 난다.
    /// 관전(사망) 배제는 전 종 무조건이고(E2), 위장 배제는 프로파일 <see cref="ZombieProfileSO.DisguiseBreaksTargeting"/> 가 켜진 종에서만 성립한다 —
    /// Walker·Stalker·Listener 는 켜고(Listener 는 E15 이탈 — 사용자 확정 2026-10-06), 후각 단일 채널인 Dog 만 끈다.
    /// </summary>
    /// <param name="source">검사할 지각 소스.</param>
    /// <param name="root">그 소스의 루트 Transform.</param>
    /// <returns>계속 물고 있어도 되면 true.</returns>
    private bool IsTargetable(IZombiePerceptionSource source, Transform root)
    {
        if (source == null || root == null) return false;
        if (source.IsSpectator) return false;
        // 위장은 latch 로 문 현재 타겟에게도 즉시 통한다(좀비공통 8-1 「래치 타겟 위장 무효」·B-084 번복 — 사용자 확정 2026-09-30).
        // 발각 상태에서 위장하면 이 프레임에 타겟·latch 가 함께 풀린다.
        // ChaseDisguisedOnAuditoryDetect(기획 이탈 — 기본 OFF)가 켜지면 위장으로도 타겟이 풀리지 않는다(D-4 ③).
        // 청각으로 확보한 위장자를 다음 프레임 검증이 도로 놓아 버리면 플래그가 무의미해진다.
        return !profile.DisguiseBreaksTargeting || !source.IsDisguised || profile.ChaseDisguisedOnAuditoryDetect;
    }

    /// <summary>매 서버 프레임 타겟 유효성을 검증하고, 사망(관전)·위장·파괴된 대상이면 즉시 놓는다.</summary>
    private void ServerValidateTarget()
    {
        if (currentTargetSource == null && currentTarget == null) return;
        if (IsTargetable(currentTargetSource, currentTarget)) return;

        string reason = currentTargetSource == null || currentTarget == null ? "파괴"
            : currentTargetSource.IsSpectator ? "관전(사망·귀환)"
            : "위장";
        Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} 타겟 무효 — {reason}");
        ServerReleaseTarget();
    }

    /// <summary>
    /// 타겟을 놓는다. latch 도 함께 푼다 — latch 가 선 채로 남으면 의심도 냉각이 막혀
    /// (<see cref="ServerApplyPerception"/> 의 냉각 분기) 좀비가 영영 평상시로 돌아오지 못한다.
    /// </summary>
    /// <summary>
    /// 휴면을 켜고 끈다 — 켜면 지각·이동·의심도가 전부 멈추고 몸은 스폰 포즈 그대로 보인다(비활성화와 달리 모델이 남는다).
    /// 튜토리얼 연출 전용: Barrier 가 열릴 때까지 「등 돌린 채 서 있는」 상태를 보장한다. 끄면 다음 프레임부터 정상 지각한다.
    /// </summary>
    /// <param name="dormant">true 면 휴면 진입, false 면 해제.</param>
    public void ServerSetDormant(bool dormant)
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] ServerSetDormant {dormant} {NetworkObjectId}");

        isDormant = dormant;
        if (!dormant) return; // 해제는 플래그만 — 상태머신이 멈춘 자리(Wander)에서 그대로 재개한다

        // 진입 시 잔재를 전부 비운다 — 남으면 해제 직후 첫 ServerTick 이 옛 자극으로 즉시 반응한다(ServerResetToSpawn 과 같은 목록).
        squadSync.ServerDisbandSquad();
        ServerReleaseTarget();
        suspicion.Value = 0f;
        hasTrackingStimulus = false;
        hasTargetStimulus = false;
        hadStimulusThisFrame = false;
        investigationStimulusDb = 0f;
        silenceTimer = 0f;
        stateMachine.StopAgent();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// [촬영 도구] 지정 플레이어를 문 진짜 발각을 강제한다 — 타겟 확보 → 의심도 100 → 발각 전환(RoarTransition).
    /// 자연 발각과 같은 전이 훅을 타므로 포효 애니·SE·래치·무리 동조·위협 음악이 그대로 따라온다. 휴면 중에도 상태는 바뀐다.
    /// </summary>
    /// <param name="target">물 지각 소스(호스트 플레이어).</param>
    public void ServerDebugForceRoar(IZombiePerceptionSource target)
    {
        Log.D($"[ZombieController] ServerDebugForceRoar {NetworkObjectId}");
        if (!IsServer || target == null || target.Root == null) return;

        AcceptTarget(target, target.Root.position);
        suspicion.Value = 100f;
        silenceTimer = 0f;
        // force — 경직·조사 등 어느 상태에서든 발각 전환으로 넘긴다. 래치·무리 묶기는 SwitchState 의 RoarTransition 진입 훅이 건다.
        // 이미 RoarTransition 이면 같은 값 재기록이라 복제되지 않는다 — 재포효는 호출자가 다른 상태를 거쳐 건다.
        stateMachine.SwitchState(ZombieStateKind.RoarTransition, true);
    }
#endif

    public void ServerReleaseTarget()
    {
        if (!IsServer) return;

        // 타겟 없이도 자주 불리므로(리셋·휴면·강등) 실제로 쥐고 있던 때만 남긴다.
        if (currentTargetSource != null)
        {
            Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} 타겟 해제 {currentTargetSource.NetworkObjectId} | 상태 {stateKind.Value} · 의심도 {suspicion.Value:0} · 상실 {chaseLostTimer:0.0}s");
        }

        currentTargetSource = null;
        currentTarget = null;
        currentTargetNetworkObjectId.Value = 0UL;
        hasTargetStimulus = false;
        ServerSetLatched(false, false);
    }

    /// <summary>
    /// 관성 추격에 들어간다(T8a) — 타겟과 래치를 놓고 의심도 100 을 유지한 채 타이머를 시작한다.
    /// 마지막 속도 기록은 건드리지 않는다 — 첫 간선 선택의 기준이다.
    /// </summary>
    public void ServerEnterChaseBlind()
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} 관성 추격 진입 — 마지막 위치 {lastKnownPosition} · 속도 {lastKnownVelocity}");

        RecordAchievementTarget(AchievementFactKind.ChaseBlind);
        ServerReleaseTarget();
        isChaseBlind = true;
        suspicion.Value = 100f;
        chaseBlindTimer = 0f;
    }

    /// <summary>관성 추격을 벗어난다 — 의심도 유지 구간을 닫고 타이머를 비운다.</summary>
    public void ServerExitChaseBlind()
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} 관성 추격 탈출 — 경과 {chaseBlindTimer:0.0}s · 타겟 {(currentTargetSource != null ? currentTargetSource.NetworkObjectId.ToString() : "None")}");

        isChaseBlind = false;
        chaseBlindTimer = 0f;
    }

    /// <summary>
    /// 추격 대치 플래그를 쓴다(P-036) — 메시 밖 타겟 앞에서 최근접 지점에 서는 구간의 시작·끝.
    /// 서버 전용. 값이 같으면 쓰지 않고, 로그는 에지에서만 남긴다. 전이·래치·의심도에는 관여하지 않는다.
    /// </summary>
    /// <param name="hold">대치 진입이면 true, 해제면 false.</param>
    public void ServerSetChaseHold(bool hold)
    {
        if (!IsServer || chaseHold.Value == hold) return;
        Log.D($"[ZombieController] {NetworkObjectId} {profile.Species} ServerSetChaseHold {hold}");
        chaseHold.Value = hold;
    }

    /// <summary>
    /// 발각 잔재를 즉시 걷는다 — 타겟·latch 해제 + 의심도 0 + 자극 잔재 리셋(ServerSetDormant 와 같은 목록).
    /// Dog 해제(D-11 — 스크랩 0 = 후각 소멸 = 즉시 배회 복귀) 전용의 훅 지원 API.
    /// 진행 중인 돌진의 완주는 상태(DogChargeState)가 보장한다 — 여기는 알람만 끈다.
    /// </summary>
    protected void ServerCalmDown()
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] ServerCalmDown {NetworkObjectId}");

        ServerReleaseTarget();
        suspicion.Value = 0f;
        hasTrackingStimulus = false;
        hadStimulusThisFrame = false;
        investigationStimulusDb = 0f;
        silenceTimer = 0f;
    }

    /// <summary>
    /// 개체를 초기 상태로 되돌린다 — 타겟 해제 · 의심도 0 · 스폰 포인트 복귀 · 상태 Wander.
    /// 튜토리얼 구간 초기화 전용(튜토리얼.md 4-3-1) — 실패 후 되돌리기이지 판정 개입이 아니다.
    /// 본편 루프에서는 호출하지 않는다. 자연 복귀는 조사 만료(investigate_sec)가 담당한다.
    /// </summary>
    public void ServerResetToSpawn()
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] ServerResetToSpawn {NetworkObjectId}");

        // 스쿼드부터 해산한다 — 팔로워로 남으면 자기 지각이 정지된 채 리더 스냅샷만 복사해 리셋이 무효가 된다.
        squadSync.ServerDisbandSquad();

        // 타겟·latch 해제 후 의심도와 자극 잔재를 전부 0 으로 — 하나라도 남으면 다음 ServerTick 이 즉시 재상승시킨다.
        ServerReleaseTarget();
        suspicion.Value = 0f;
        hasTrackingStimulus = false;
        hasTargetStimulus = false;
        hadStimulusThisFrame = false;
        investigationStimulusDb = 0f;
        silenceTimer = 0f;

        // 배치된 Transform(spawnPosition·spawnRotation) 그대로 — 마지막 인지 위치도 여기로 되돌려야 정지 중 시선이 옛 플레이어 위치로 돌아가지 않는다.
        ServerWarpTo(spawnPosition, spawnRotation);

        // 기절 게이지도 되채운다 — 남기면 실패 재시도마다 투척 명중 피해가 누적돼 멀쩡한 좀비가 한두 번에 기절한다.
        weaponHealth.Value = maxWeaponHealth;

        // ServerSetState 단독은 상태머신 객체를 갈아끼우지 않는다 — force 로 같은 상태여도 Wander 를 재진입시킨다.
        stateMachine.SwitchState(ZombieStateKind.Wander, force: true);
    }

    /// <summary>
    /// 개체를 즉시 다른 포즈로 옮긴다(서버) — 에이전트 Warp · 회전 정본 · NetworkTransform Teleport · 리지드바디 포즈를 한 번에 맞춘다.
    /// 마지막 인지 위치도 목적지로 덮어 정지 중 시선이 옛 플레이어 위치로 돌아가지 않게 한다.
    /// ServerResetToSpawn 의 워프 절차를 종 파생(Lurker 재배치)이 재사용하도록 떼어 낸 것이다.
    /// </summary>
    /// <param name="position">옮길 위치.</param>
    /// <param name="rotation">옮길 회전.</param>
    protected void ServerWarpTo(Vector3 position, Quaternion rotation)
    {
        Log.D($"[ZombieController] ServerWarpTo {NetworkObjectId} → {position}");

        // 에이전트가 updatePosition=false 라도 Warp 는 트랜스폼까지 함께 옮긴다. 회전은 Warp 가 건드리지 않아 직접 복원한다.
        agent.Warp(position);
        transform.rotation = rotation;
        ServerFacingRotation = rotation; // 회전 정본도 함께 복원 — 남기면 다음 FixedUpdate 의 MoveRotation 이 옛 방향으로 되돌린다
        lastKnownPosition = position;
        stateMachine.EndTimedFacing(); // 배회 대기 등이 남긴 시선 오버라이드 해제 — 남으면 워프 직후 옛 관심 지점으로 다시 돈다
        if (networkTransform != null)
        {
            networkTransform.Teleport(position, rotation, transform.localScale); // 원격 보간 스미어 없이 점프시키는 정본 경로(PlayerController.ForcePose 와 동일)
        }

        // 리지드바디도 함께 옮긴다 — Warp·Teleport 는 트랜스폼만 쓰는데, 루트가 Interpolate kinematic 이라
        // 바디 포즈가 옛 자리에 남으면 다음 프레임에 트랜스폼이 그 자리로 되돌아가 워프가 무효가 된다(실측: 12m 밖 좀비가 제자리에 남았다).
        body.position = position;
        body.rotation = rotation;
    }

    /// <summary>
    /// 개체를 연출 종료 상태로 끈다 — 감각·이동·판정이 전부 멈춘다. 튜토리얼 S2-1 시연 종료 전용(튜토리얼.md 5-1).
    /// 닫힌 Barrier 도 벽 1장(−20dB)일 뿐이라, 켜 둔 채로는 큰 소음(고함 등)에 계속 반응하고 발각 상태로 벽에 붙어 있게 된다.
    /// 본편 루프에서는 호출하지 않는다.
    /// </summary>
    public void ServerDeactivate()
    {
        if (!IsServer) return;

        Log.D($"[ZombieController] ServerDeactivate {NetworkObjectId}");

        // 스쿼드 해산이 선행돼야 한다 — 리더가 비활성화되면 해제 루프(ServerReleaseFollowersIfSettled)가 영영 돌지 않아
        // 팔로워가 동결 스냅샷(Chase 등)을 매 프레임 복사하는 고착이 생긴다(시연 좀비 A 가 B 를 스쿼드로 물었던 사고).
        squadSync.ServerDisbandSquad();

        ServerReleaseTarget();
        stateMachine.StopAgent();

        // 인씬 배치 오브젝트라 destroy 금지 — Despawn(true)는 NGO 가 경고하는 경로다. 재활성 계획이 없어 despawn 만 한다.
        NetworkObject.Despawn(false);

        // Despawn 뒤에도 IsServer 는 참이라 Update 가 계속 돌며 NetworkVariable 쓰기에서 터진다 — 반드시 끈다.
        // OnDisable 이 런타임 레지스트리·무리 후보 해제까지 처리한다.
        gameObject.SetActive(false);
    }

    public void ServerSetState(ZombieStateKind nextState)
    {
        if (!IsServer) return;
        stateKind.Value = nextState;
    }

    /// <summary>배회 여부를 설정한다. 서버 AI만 읽는 설정 값이라 씬 로드 시(군중 구성)에도 쓸 수 있다.</summary>
    /// <param name="enabled">배회 허용 여부.</param>
    public void SetWanderEnabled(bool enabled)
    {
        wanderEnabled = enabled;
    }

    /// <summary>
    /// 배회 반경만 설정한다(군중 구성 전용). 홈 원점은 건드리지 않는다 —
    /// <see cref="ServerConfigureSpawn"/> 는 원점까지 덮어써서 각자의 homeAnchor 로 서 있는
    /// 무리 좀비에는 쓸 수 없고, Awake 에서 주입해도 OnNetworkSpawn 의 CacheHomePosition 이 되돌린다.
    /// patrolRadiusOverride 는 그 경로가 건드리지 않으므로 씬 로드 시점 주입이 유효하다.
    /// </summary>
    /// <param name="radiusMeters">배회 반경(m). 음수면 ZombieProfileSO.PatrolRadius 를 쓴다.</param>
    public void SetPatrolRadius(float radiusMeters)
    {
        patrolRadiusOverride = radiusMeters;
    }

    /// <summary>
    /// 절차 스폰 직후 서버가 배회 원점·반경을 주입한다(MapStateObjectSpawner 전용 경로 — Spawn() 이후 호출).
    /// 수동 배치 좀비는 homeAnchor/인스펙터 값 그대로 동작한다. AI 가 서버 전용이라 복제는 불필요.
    /// </summary>
    /// <param name="home">배회 기준 원점(스폰 마커 위치).</param>
    /// <param name="patrolRadiusMeters">배회 반경(m). 음수면 기존 값(ZombieProfileSO.PatrolRadius) 유지.</param>
    public void ServerConfigureSpawn(Vector3 home, float patrolRadiusMeters)
    {
        homePosition = home;
        lastKnownPosition = home;
        if (patrolRadiusMeters >= 0f)
        {
            patrolRadiusOverride = patrolRadiusMeters;
        }
    }

    public void ServerDowngradeToInvestigation()
    {
        if (!IsServer) return;
        ServerReleaseTarget();
        lastKnownPosition = transform.position; // 조사 목표 = 만료 시점의 현재 위치 — 마지막 목격 위치로 돌아가지 않는다(T8c)
        suspicion.Value = Mathf.Clamp(profile.ThInvestigate, 0f, 100f);
        hasTrackingStimulus = false;
        investigationStimulusDb = profile.HearMinDb;
        silenceTimer = 0f;
    }

    /// <summary>
    /// 대표의 의심도·상태·타겟을 무리원에게 복사한다(좀비공통 6-1). 공격은 개체 행동이라 예외다 —
    /// 무리원 자신의 공격은 덮어쓰지 않고, 대표가 공격 중에 타겟을 잃어도 무리원은 쥐고 있던 공유 타겟을 유지한다.
    /// </summary>
    /// <param name="leader">복사할 대표.</param>
    public void ServerCopyLeaderSnapshot(ZombieController leader)
    {
        if (!IsServer || leader == null) return;

        suspicion.Value = leader.SuspicionValue;

        // 공격은 사거리로 갈리는 개체 행동이다 — 대표가 공격 중이면 무리원은 공유 타겟을 계속 쫓고,
        // 스스로 사거리에 닿았을 때만 공격에 들어간다.
        bool leaderIsAttacking = leader.CurrentState == ZombieStateKind.Attack;

        // 무리원 자신의 공격(선딜·타격 락)은 끝날 때까지 대표 상태로 덮지 않는다. 공격 중 타겟이 비면(대상 사망 등) 이 복사가
        // 다시 도는데, 덮으면 락이 잘려 곧바로 추격·재타격으로 넘어간다 — PlayerHealth 는 좀비별 연타 방지를 락에 맡긴다.
        // 해산(ServerReleaseFromLeader)의 마지막 복사도 같다. 락이 끝나면 공격 상태가 스스로 나가고 다음 복사부터 대표를 따른다.
        // 무리원 자신의 포효(발각 전환)도 같다 — 자기 타이머가 끝나 스스로 추격으로 나갈 때까지 덮지 않는다.
        // 덮으면 늦게 합류한 무리원의 포효가 대표의 추격 전환에 잘려 전원이 같은 프레임에 출발한다.
        if (CurrentState != ZombieStateKind.Attack && CurrentState != ZombieStateKind.RoarTransition)
        {
            stateKind.Value = leaderIsAttacking ? ZombieStateKind.Chase : leader.CurrentState;
        }

        // 대표가 공격 중에 타겟을 잃었으면 그 개체의 사정이다 — 무리원은 쥐고 있던 공유 타겟을 계속 문다.
        // 받아 적으면 대표 한 마리의 공격이 무리 전체의 추격·공격을 끊는다. 무효가 된 타겟(사망·위장)은 ServerValidateTarget 이 걸러 낸다.
        bool keepSharedTarget = leaderIsAttacking && leader.CurrentTargetSource == null && currentTargetSource != null;
        if (!keepSharedTarget)
        {
            currentTargetSource = leader.CurrentTargetSource;
            currentTarget = leader.CurrentTarget;
            currentTargetNetworkObjectId.Value = leader.CurrentTargetNetworkObjectId;
        }

        // 래치는 타겟 복사 뒤에 맞춘다 — 타겟 없는 래치는 성립하지 않는다(ServerSetLatched 의 불변식).
        // 관성 추격 여부와 그 타이머는 복사하지 않는다: 무리원은 대표를 따라 들어가고 나온다.
        ServerSetLatched(leader.IsLatched, false);

        lastKnownPosition = leader.LastKnownPosition;
        lastKnownVelocity = leader.LastKnownVelocity;
        investigationStimulusDb = leader.InvestigationStimulusDb;

        // 공유 타겟이 이미 죽었으면 자극이 있다고 속이지 않는다 — 그래야 팔로워도
        // 추격 상실 타이머를 굴려 조사 단계로 내려올 수 있다.
        bool sharedTargetAlive = IsTargetable(currentTargetSource, currentTarget);
        hasTrackingStimulus = leaderIsAttacking ? sharedTargetAlive : leader.HasTrackingStimulus;
        hadStimulusThisFrame = leaderIsAttacking ? sharedTargetAlive : leader.HadStimulusThisFrame;
        hasTargetStimulus = leaderIsAttacking ? sharedTargetAlive : leader.HasTargetStimulus;
    }

    /// <summary>
    /// 동조 지연이 끝난 무리원을 대표에 합류시킨다. 대표가 이미 추격 중이어도 포효를 건너뛰지 않고
    /// 자기 발각 전환부터 시작한다(좀비공통 6-1 「각자 제자리 포효」).
    /// </summary>
    /// <param name="leader">합류할 대표.</param>
    public void ServerJoinSquad(ZombieController leader)
    {
        if (!IsServer || leader == null) return;

        // 지연 중 자력으로 발각된 개체는 이미 포효를 했다 — 다시 시키지 않는다.
        ZombieStateKind ownState = CurrentState;
        bool wasDetected = ownState == ZombieStateKind.RoarTransition
            || ownState == ZombieStateKind.Chase
            || ownState == ZombieStateKind.Attack;

        ServerCopyLeaderSnapshot(leader);

        if (!wasDetected && suspicionLatched.Value && currentTarget != null)
        {
            stateKind.Value = ZombieStateKind.RoarTransition;
        }
    }

    public void BeginInvestigateTimer() => investigateTimer = 0f;
    public void AdvanceInvestigateTimer(float deltaTime) => investigateTimer += deltaTime;
    public float InvestigateTimer => investigateTimer;
    public void BeginAlertTimer() => alertTimer = 0f;
    public void AdvanceAlertTimer(float deltaTime) => alertTimer += deltaTime;
    public float AlertTimer => alertTimer;
    public void BeginAttackTimer() => attackTimer = 0f;
    public void AdvanceAttackTimer(float deltaTime) => attackTimer += deltaTime;
    public float AttackTimer => attackTimer;
    public void BeginChaseLostTimer() => chaseLostTimer = 0f;
    public void AdvanceChaseLostTimer(float deltaTime) => chaseLostTimer += deltaTime;
    public float ChaseLostTimer => chaseLostTimer;
    public void ResetChaseLostTimer() => chaseLostTimer = 0f;
    public void AdvanceChaseBlindTimer(float deltaTime) => chaseBlindTimer += deltaTime;
    public float ChaseBlindTimer => chaseBlindTimer;

    /// <summary>
    /// 플레이어와의 몸 접촉을 기록한다(P4 접촉 정지 · 접촉 양보). 좀비는 kinematic 몸이라 그대로 두면 플레이어를
    /// 종이처럼 밀어붙인다 — 닿아 있는 동안 ZombieRootMotion 이 파고드는 성분을 깎고(위장 여부별 비율), 배회·경계·조사면
    /// 상태 머신이 멈추거나 비켜선다. 위장 여부는 비율 선택에만 쓰고 접촉 자체는 전 플레이어를 기록한다(길막 해소 · 2026-10-18).
    /// 법선은 PhysX 접점 대신 두 루트의 수평 변위로 잰다 — 캡슐끼리라 같고, 접점 법선의 방향 규약에 기대지 않는다.
    /// </summary>
    /// <param name="collision">접촉 정보. 플레이어는 루트 Rigidbody 에 PlayerDisguise 가 있다.</param>
    private void OnCollisionStay(Collision collision)
    {
        // 접촉 프레임마다 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsServer || collision.rigidbody == null) return;
        PlayerDisguise disguise = collision.rigidbody.GetComponent<PlayerDisguise>();
        if (disguise == null) return;

        Vector3 normal = transform.position - collision.rigidbody.position;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.0001f) return; // 겹친 중심 — 방향이 없어 이 박자는 기록하지 않는다

        playerContactUntil = Time.time + playerContactHoldSeconds;
        playerContactDisguised = disguise.IsDisguised;
        playerContactNormal = normal.normalized;
    }

    private void CacheHomePosition()
    {
        homePosition = homeAnchor != null ? homeAnchor.position : transform.position;
        lastKnownPosition = homePosition;
    }

    /// <summary>디버그 기즈모 — 홈·배회 반경·타격 사거리·시야 원뿔, 플레이 중엔 인지 위치·타겟·목적지·순찰점까지 그린다. OnDrawGizmosSelected 전용.</summary>
    private void DrawDebugGizmos()
    {
        Vector3 home = Application.isPlaying
            ? homePosition
            : homeAnchor != null ? homeAnchor.position : transform.position;
        Vector3 visionPos = visionOrigin != null ? visionOrigin.position : transform.position;
        Quaternion visionRot = visionOrigin != null ? visionOrigin.rotation : transform.rotation;
        Vector3 attackPos = attackOrigin != null ? attackOrigin.position : transform.position;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(home, 0.2f);
        Gizmos.DrawLine(transform.position, home);

        if (profile == null) return;

        Gizmos.color = new Color(0.2f, 0.55f, 1f, 0.65f);
        Gizmos.DrawWireSphere(home, PatrolRadius);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(attackPos, Mathf.Max(0.1f, profile.AttackRange));
        DrawVisionGizmo(visionPos, visionRot, profile.VisionAngle, profile.VisionDistance);

        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, lastKnownPosition);
            Gizmos.DrawWireSphere(lastKnownPosition, 0.25f);

            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(visionPos, currentTarget.position);
            }

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, agent.destination);
                Gizmos.DrawWireSphere(agent.destination, 0.2f);
            }

            if (stateMachine != null && stateMachine.HasPatrolPoint)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(home, stateMachine.PatrolPoint);
                Gizmos.DrawWireSphere(stateMachine.PatrolPoint, 0.3f);
            }
        }
    }

    /// <summary>
    /// 선택된 좀비에 한해 디버그 기즈모와 상태 라벨을 그린다. 전 좀비가 매 리페인트마다 그리면(OnDrawGizmos) 좀비 수만큼
    /// 드로우 호출이 불어나 에디터가 버벅이므로 선택 전용이다 — 모델을 클릭해도 [SelectionBase] 가 루트를 선택한다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos) return;

        DrawDebugGizmos();

#if UNITY_EDITOR
        // 상태 라벨은 런타임 값이라 플레이 중에만 그린다. 문자열 조립 + IMGUI 비용도 선택된 한 마리만 치른다.
        if (!Application.isPlaying) return;

        Vector3 visionPos = visionOrigin != null ? visionOrigin.position : transform.position;
        string leaderName = SquadLeader != null ? SquadLeader.name : "None";
        // 도달 소음 표시 — 접수 후 2초까지만 값으로 보여 주고 이후엔 무음 표기. 가청 임계 미달 소음은
        // 전파 단계(NoisePropagationSystem)에서 걸러져 아예 오지 않으므로 "—"가 곧 「임계 미달/무음」이다.
        float heardAge = Time.time - sensorySystem.LastReachedAt;
        string heardText = heardAge <= 2f
            ? $"{sensorySystem.LastReachedDb:0.0}dB ({heardAge:0.0}s)"
            : "—";
        Handles.Label(visionPos + Vector3.up * 0.5f,
            $"{stateKind.Value} | Suspicion {suspicion.Value:0.0}\n" +
            $"Latched {suspicionLatched.Value} | Signal {hasTrackingStimulus} | OnTarget {hasTargetStimulus}\n" +
            $"Target {(currentTarget != null ? currentTarget.name : "None")} | Lost {chaseLostTimer:0.0}s | Blind {chaseBlindTimer:0.0}s | Leader {leaderName}\n" +
            $"Heard {heardText} | InvDb {investigationStimulusDb:0.0}");
#endif
    }

    /// <summary>
    /// 시야 원뿔 기즈모 — 판정(ZombieSensorySystem.EvaluateVision)이 3D 방향 내적이라 상하도 같은 각으로 잘리므로
    /// 수평 부채꼴이 아닌 원뿔로 그린다. 끝 테두리 링 + 모선 + 수평·수직 호. 링은 평면이라 구면 판정 경계와 미세 차이가 있다(디버그 전용).
    /// </summary>
    /// <param name="origin">시야 원점.</param>
    /// <param name="rotation">시야 원점 회전.</param>
    /// <param name="angle">원뿔 전체 각(도).</param>
    /// <param name="distance">시야 거리(m).</param>
    private static void DrawVisionGizmo(Vector3 origin, Quaternion rotation, float angle, float distance)
    {
        Gizmos.color = new Color(0f, 1f, 0.35f, 0.85f);
        const int ringSegments = 24;
        const int edgeLines = 8;
        float halfAngle = angle * 0.5f;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;

        // 끝 테두리 링 — 정면에서 반각만큼 기울인 방향을 정면 축 둘레로 한 바퀴 돌린다.
        Vector3 previousRim = RimPoint(origin, forward, right, up, halfAngle, distance, 0f);
        for (int i = 1; i <= ringSegments; i++)
        {
            float azimuth = 360f * i / ringSegments;
            Vector3 rim = RimPoint(origin, forward, right, up, halfAngle, distance, azimuth);
            Gizmos.DrawLine(previousRim, rim);
            if (i % (ringSegments / edgeLines) == 0) Gizmos.DrawLine(origin, rim);
            previousRim = rim;
        }

        // 수평·수직 호 — 원뿔 안쪽 단면. 기존 부채꼴 가독성 유지.
        DrawVisionArc(origin, forward, right, halfAngle, distance);
        DrawVisionArc(origin, forward, up, halfAngle, distance);
    }

    /// <summary>원뿔 끝 테두리 위 한 점. 정면에서 halfAngle 기울인 뒤 정면 축 둘레로 azimuth 회전.</summary>
    /// <param name="origin">시야 원점.</param>
    /// <param name="forward">정면 축.</param>
    /// <param name="right">우측 축.</param>
    /// <param name="up">상측 축.</param>
    /// <param name="halfAngle">원뿔 반각(도).</param>
    /// <param name="distance">시야 거리(m).</param>
    /// <param name="azimuth">정면 축 둘레 회전각(도).</param>
    /// <returns>월드 좌표.</returns>
    private static Vector3 RimPoint(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up, float halfAngle, float distance, float azimuth)
    {
        float tilt = halfAngle * Mathf.Deg2Rad;
        float spin = azimuth * Mathf.Deg2Rad;
        Vector3 lateral = right * Mathf.Cos(spin) + up * Mathf.Sin(spin);
        Vector3 direction = forward * Mathf.Cos(tilt) + lateral * Mathf.Sin(tilt);
        return origin + direction * distance;
    }

    /// <summary>정면 축과 주어진 측축이 이루는 평면 위의 원뿔 단면 호(-halfAngle~+halfAngle)를 그린다.</summary>
    /// <param name="origin">시야 원점.</param>
    /// <param name="forward">정면 축.</param>
    /// <param name="side">단면 평면을 정하는 측축(right 또는 up).</param>
    /// <param name="halfAngle">원뿔 반각(도).</param>
    /// <param name="distance">시야 거리(m).</param>
    private static void DrawVisionArc(Vector3 origin, Vector3 forward, Vector3 side, float halfAngle, float distance)
    {
        const int segments = 16;
        Vector3 previousPoint = origin + ArcDirection(forward, side, -halfAngle) * distance;
        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 currentPoint = origin + ArcDirection(forward, side, currentAngle) * distance;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    /// <summary>정면 축에서 측축 쪽으로 angle(도)만큼 기울인 단위 방향.</summary>
    /// <param name="forward">정면 축.</param>
    /// <param name="side">측축.</param>
    /// <param name="angle">기울임 각(도, 음수면 반대쪽).</param>
    /// <returns>단위 방향.</returns>
    private static Vector3 ArcDirection(Vector3 forward, Vector3 side, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return forward * Mathf.Cos(radians) + side * Mathf.Sin(radians);
    }
    /// <summary>래치 대상이 바뀔 때 해당 플레이어에게 탐지 이력을 남긴다.</summary>
    private void RecordAchievementLatch()
    {
        if (currentTargetSource == null || achievementLatchedTarget == currentTargetSource.NetworkObjectId) return;
        achievementLatchedTarget = currentTargetSource.NetworkObjectId;
        RecordAchievementTarget(AchievementFactKind.Latch);
    }

    /// <summary>플레이어 네트워크 오브젝트를 소유자 ID로 변환해 사실을 전달한다.</summary>
    /// <param name="kind">확정 사실 종류.</param>
    private void RecordAchievementTarget(AchievementFactKind kind)
    {
        if (currentTargetSource == null) return;
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(currentTargetSource.NetworkObjectId, out NetworkObject target)
            && target.IsPlayerObject) achievementFacts.RaiseEvent(kind, target.OwnerClientId);
    }
}
