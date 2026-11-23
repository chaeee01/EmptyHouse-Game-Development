using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Runtime;
using EmptyHouse.Zombie.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(ZombieController), typeof(NavMeshAgent))]
public class ZombieStateMachine : NetworkBehaviour
{
    // 추격 회피 우선순위(낮을수록 우선). 전원이 프리팹 기본값 50 동순위면 RVO 가 누가 비킬지
    // 정하지 못해 회피 부담을 반반 나누다 오실레이션이 남는다(EH-43). 타겟에 가까운 좀비에게
    // 우선권을 주면 뒤의 좀비가 앞을 비켜 옆으로 돌아 나간다. 범위는 기본값 50 을 가운데 둔 30~70.
    private const int chasePriorityClosest = 30;
    private const int chasePriorityFarthest = 70;
    private const float chasePriorityMaxDistance = 20f; // 이 거리(m) 이상은 전부 최하 우선권
    private const int defaultAvoidancePriority = 50;    // Zombie.prefab 의 NavMeshAgent 기본값

    // 배회·조사도 같은 이유로 동순위를 피한다. 전원이 50 이면 서로가 서로를 피해 좌우로 흔들리는 「회피 춤」이 남고,
    // 그 흔들림이 몸을 비비는 버벅임으로 보인다. 좀비마다 조금씩 다른 값을 주면 한쪽이 양보하고 다른 쪽은 직진한다.
    private const int avoidancePrioritySpread = 5;       // 기본값 ± 이 폭 안에서 좀비마다 다른 값을 뽑는다
    private const ulong idHashMultiplier = 2654435761UL; // Knuth 승산 해시 — 연속된 오브젝트 id 를 고르게 흩뜨린다

    // 옆걸음 양보(P3) — 서 있는 좀비는 RVO 로 비키지 않으므로(정지 에이전트는 회피 계산에서 밀리지 않는다)
    // 접근하는 위장 플레이어에게 직접 한 걸음 길을 내준다. 걷는 좀비의 양보는 P2(RVO 프록시)가 담당.
    private const float yieldDetectRadiusMeters = 1.5f; // 이 거리 안의 위장 플레이어만 본다
    private const float yieldStepMeters = 0.7f;         // 비켜서는 걸음 폭(m)
    private const float yieldApproachMinMps = 0.3f;     // 이 속력(m/s) 미만은 「나에게 오는 중」이 아니다
    private const float yieldApproachMinDot = 0.5f;     // 진행 방향과 나 사이 각도 코사인 하한(±60도)
    private const float yieldTimeoutSeconds = 1.5f;     // 양보 걸음 상한 — 막혀서 못 가도 이 시간이면 접는다
    private const float yieldCooldownSeconds = 0.75f;   // 연속 양보 사이 간격 — 매 프레임 재발동 오실레이션 방지
    private const float yieldMinStepMeters = 0.35f;     // 접촉 양보 후보 하한 — NavMesh 스냅이 현재 위치 이 안으로 끌어온 지점은 걸음이 안 된다(SetDestination 의 발밑 0.3m 무시 규칙에도 걸린다)

    // 도달 불가 판정(좀비공통 E11) — 요청 지점이 NavMesh 에서 수평으로 이만큼 벗어나면 「메시 밖」이다.
    public const float DestinationReachToleranceMeters = 0.5f; // 추격 대치 진입 판정(ZombieChaseState)도 같은 값을 쓴다 — 리터럴 중복 금지(P-036)
    private const float destinationFallbackSampleMeters = 6f; // 2m 샘플 실패 시 최근접 지점을 넓게 찾는 반경
    private const float blockedDestinationMatchMeters = 0.5f; // 막혔던 목적지와 이 거리 안이면 같은 목적지로 본다 — 도착 취급해 포기시킨다

    [SerializeField] private ZombieController controller;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaughtChannel;
    [SerializeField] private ZombieStateChangedEventChannelSO stateChangedChannel; // 상태 전이 방송 채널(서버 로컬). 튜토리얼 판정 등 외부 관찰자용

    [Header("Chase Blind")]
    [SerializeField] private MapRuntimeGraphSO runtimeGraph; // 관성 추격 간선 조회 — 프로젝트 에셋 SO_MapRuntimeGraph(필수 · 좀비 프리팹 전부 배선). null 이 되는 것은 runtimeGraph.Current 뿐이다(클라이언트 · 튜토리얼 · 서버 미바인드)
    [SerializeField] private float chaseBlindStuckSeconds = 1f; // 간선으로 가는 중 이 시간(초) 동안 chaseBlindStuckMeters 미만으로 움직이면 그 간선을 도달 불가로 친다 ⚪
    [SerializeField] private float chaseBlindStuckMeters = 0.1f; // 무진행 판정 거리(m) ⚪
    [SerializeField] private float chaseBlindArriveMeters = 1.5f; // 간선 선행 도착 반경(m) — 선회 반경(속도 ÷ 각속도 ≈ 0.5 m)보다 넉넉히 커야 간선 지점 주위를 돌지 않는다 ⚪
    [SerializeField] private float chaseBlindForwardConeDegrees = 90f; // 관성 추격 후보 간선 반각(도) — 진행 방향 기준 이 각 밖의 간선은 고르지 않는다. 안에 열린 간선이 없으면 그 자리에 선다 ⚪

    private const int chaseBlindNoEdge = -1;                   // 향하는 간선 없음 — 미시작이거나 막다른 곳에 선 상태
    private const float chaseBlindHeadingMinSqrMps = 0.0001f;  // 마지막 속도의 수평 제곱 크기가 이 미만이면 0 으로 보고 좀비 전방을 쓴다

    private readonly ChaseBlindNavigator chaseBlindNav = new ChaseBlindNavigator(); // 관성 추격 간선 선택기 — 어느 간선으로 갈지만 정한다. 이동·도착 판정은 아래 ChaseBlindMove 3종이 한다
    private IChaseBlindGraph chaseBlindGraph;                  // 이번 관성 추격이 읽는 그래프(어댑터). 맵 그래프 미바인드면 null
    private int chaseBlindEdge = chaseBlindNoEdge;             // 지금 향하는 간선. chaseBlindNoEdge 면 서 있다
    private float chaseBlindStuckTimer;                        // 무진행 경과 시간(초)
    private Vector3 chaseBlindStuckAnchor;                     // 무진행 판정 기준 위치 — chaseBlindStuckMeters 이상 벗어나면 다시 잡는다
    private Vector3 chaseBlindEdgePosition;                    // 지금 향하는 간선의 월드 위치 — 선행 도착 판정 기준(MoveToEdge 에서 캐시)
    private Vector3 chaseBlindMoveStart;                       // 지금 향하는 간선으로 출발한 위치 — 도착 시 실제 진행 방향(간선 위치 − 출발 위치)의 기준

    private IZombieState currentState;
    private Vector3 lastRequestedDestination;
    private bool hasRequestedDestination;
    private Vector3 patrolPoint;
    private float targetSpeed;                 // SetTargetSpeed 가 건 목표 속도(m/s). agent.speed 는 이 값을 향해 램프한다
    private bool hasPatrolPoint;
    private NavMeshPath reachabilityPath; // 도달 가능 판정 재사용 버퍼(TrySnapReachablePoint) — 후보마다 new 하지 않는다. NavMeshPath 는 필드 초기화자에서 만들 수 없어(Unity 예외) 첫 사용 시 생성

    private Vector3 facingTarget;              // 회전 오버라이드가 바라볼 월드 지점
    private float facingDegreesPerSecond;      // 그 지점까지 도는 속도(도/초). 주어진 시간에 딱 맞게 역산된 값이다
    private bool hasFacingOverride;            // 오버라이드 사용 여부. 꺼져 있으면 기존 규칙(이동 방향/관심 지점)을 따른다
    private float facingErrorDegrees;          // 목표 방향까지 남은 각도(도, 부호 있음 — 양수면 시계 방향). 제자리 회전 애니 선택·소진의 재료(A3)

    private int baseAvoidancePriority = defaultAvoidancePriority; // 이 좀비 고유의 회피 우선순위. 추격이 끝나면 여기로 되돌린다

    private bool isYielding;                   // 옆걸음 양보 걸음 진행 중 — 이 동안 상태 Tick 을 쉰다(상태가 매 프레임 StopAgent 로 붙잡는 것과 충돌)
    private bool yieldWasMoving;               // 이번 양보가 걷는 중(접촉 회피)에 시작됐는가 — 끝날 때 StopAgent 없이 상태 Tick 에 목적지를 되돌려 준다
    private float yieldTimer;                  // 이번 양보 걸음의 경과 시간(초)
    private float yieldCooldown;               // 다음 양보까지 남은 시간(초)
    private bool heldForContact;               // 위장 접촉 정지 폴백 중 — 옆걸음을 못 시작해 선 상태. 접촉이 풀리면 마지막 목적지를 1회 재발행한다(P-043 U4)

    /// <summary>
    /// 지금 유효한 선회 속도(도/초). 배회 대기처럼 시간이 정해진 회전은 역산값이고, 그 밖에는 프로파일 값이다.
    /// ZombieAnimator 가 턴 클립 배속을 여기에 맞춘다 — 안 맞추면 역산한 시간을 무시하고 휙 돌고 남은 시간을 서 있는다.
    /// </summary>
    public float CurrentTurnDegreesPerSecond =>
        hasFacingOverride ? facingDegreesPerSecond : controller.Profile.TurnSpeedDegreesPerSecond;

    /// <summary>목표 방향까지 남은 각도(도, 부호 있음). 데드존 안이면 0. ZombieAnimator 의 TurnBlend 원천이다(A3).</summary>
    public float FacingErrorDegrees => facingErrorDegrees;

    public ZombieController Controller => controller;
    public ZombieStateKind CurrentStateKind => currentState != null ? currentState.Kind : controller.CurrentState;
    public Vector3 PatrolPoint => patrolPoint;
    public bool HasPatrolPoint => hasPatrolPoint;

    public void ServerInitialize()
    {
        if (!IsServer) return;
        agent.updateRotation = false;
        // 위치는 루트모션이 만든다(ZombieRootMotion). 에이전트가 직접 밀면 애니와 이중으로 이동한다.
        agent.updatePosition = false;

        // 회피 동순위 해소 — 오브젝트 id 로 뽑으므로 재접속·재스폰과 무관하게 같은 좀비는 같은 값을 갖는다.
        ulong bucket = NetworkObjectId * idHashMultiplier % (ulong)(avoidancePrioritySpread * 2 + 1);
        baseAvoidancePriority = defaultAvoidancePriority - avoidancePrioritySpread + (int)bucket;
        agent.avoidancePriority = baseAvoidancePriority;
        SwitchState(ZombieStateKind.Wander, true);
    }

    public void ServerTick(float deltaTime)
    {
        if (!IsServer || controller == null || controller.Profile == null || agent == null) return;

        if (currentState == null || currentState.Kind != controller.CurrentState)
        {
            SwitchState(controller.CurrentState, true);
        }

        EvaluatePerceptionTransitions();
        UpdateYield(deltaTime);
        if (isYielding)
        {
            // 옆걸음 진행 중 — 상태 Tick 도 접촉 회피도 끼어들지 않는다(걸음이 접촉을 푸는 수단이다).
        }
        else if (ShouldHoldForDisguisedContact())
        {
            // 플레이어 접촉(위장 무관) — 옆걸음 우선 · 정지는 폴백(P-043 U4). 걷는 중이면 지금 속도 그대로 옆으로 한 걸음 비켜 지나가고,
            // 걸음이 끝나면 상태 Tick 이 제 목적지를 다시 건다. 비켜설 자리가 없거나 쿨다운이면 그 자리에 선다 —
            // 상태 Tick 으로 흘려 보내면 전진이 이어져 플레이어를 밀친다(disableCollisionWhileDisguised 0 유지).
            if (!(!agent.isStopped && TryBeginMovingYield()))
            {
                if (!heldForContact)
                {
                    heldForContact = true;
                    Log.D($"[ZombieStateMachine] {name} 플레이어 접촉 정지 진입");
                }
                StopAgent();
            }
        }
        else
        {
            if (heldForContact)
            {
                // 접촉이 풀렸다 — 수색 단계처럼 목적지를 매 틱 재발행하지 않는 상태가 고착되지 않도록 마지막 목적지를 1회 다시 건다.
                heldForContact = false;
                Log.D($"[ZombieStateMachine] {name} 플레이어 접촉 정지 해제");
                if (hasRequestedDestination) SetDestination(lastRequestedDestination);
            }
            currentState?.Tick(this, deltaTime);
        }
        UpdateSpeedRamp(deltaTime);
        UpdateFacing(deltaTime);
    }

    private void EvaluatePerceptionTransitions()
    {
        ZombieStateKind kind = currentState.Kind;
        // 관성 추격의 상향 전이(재포착 → 추격)는 그 상태가 직접 한다 — 여기서 받으면 포효를 다시 거친다(T8b 는 포효 없음).
        // 도주(Flee)도 종 상태가 수명을 스스로 정한다 — 위협이 보이는 동안 의심도 100 이 유지되므로 여기서 받으면 매 프레임 재발각(포효)된다.
        if (kind == ZombieStateKind.RoarTransition || kind == ZombieStateKind.Chase || kind == ZombieStateKind.Attack
            || kind == ZombieStateKind.ChaseBlind || kind == ZombieStateKind.Flee)
        {
            return;
        }

        // 발각 조건은 값이다(의심도 100 + 타겟 확보). 래치는 이 전이의 결과로 SwitchState 훅이 켠다 — 조건이 아니다.
        if (controller.SuspicionValue >= 100f && controller.CurrentTarget != null)
        {
            SwitchState(ZombieStateKind.RoarTransition);
            return;
        }

        float value = controller.SuspicionValue;
        if (value >= controller.Profile.ThInvestigate)
        {
            SwitchState(ZombieStateKind.Investigate);
            return;
        }

        // 하향 전이는 의심도 밴드로 하지 않는다. 각 상태가 스스로 내려간다(Investigate → Subside → Wander).
        // 밴드로 끌어내리면 조사 상태가 냉각 시작(SuspicionGraceSeconds) 직후 곧바로 Alert 에 뺏겨
        // 조사 타이머(InvestigateToWanderSeconds)도 Subside 도 영영 성립하지 않는다. 그 결과
        // 교전 직후 좀비는 아무 동작도 없는 Alert 에 굳고(정지·무회전), 스쿼드 해제 조건(Subside/Wander)도
        // 성립하지 않아 주변 무리가 leash 만료까지 자기 지각을 멈춘 채 리더만 복사한다.
        if (kind != ZombieStateKind.Wander && kind != ZombieStateKind.Alert) return;

        SwitchState(value >= controller.Profile.ThAlert
            ? ZombieStateKind.Alert
            : ZombieStateKind.Wander);
    }

    public void SwitchState(ZombieStateKind nextState, bool force = false)
    {
        if (!IsServer || controller == null || controller.Profile == null) return;
        if (!force && currentState != null && currentState.Kind == nextState) return;

        ZombieStateKind previousState = currentState != null ? currentState.Kind : nextState;

        // 양보 걸음 중 전이가 나면 걸음을 접는다 — StopAgent 는 하지 않는다(다음 상태의 Enter 가 에이전트를 정한다).
        if (isYielding)
        {
            isYielding = false;
            yieldCooldown = yieldCooldownSeconds;
        }
        heldForContact = false; // 접촉 정지 중 전이(발각·자극)가 나면 폴백도 접는다 — 다음 상태의 Enter 가 에이전트를 정한다

        currentState?.Exit(this);

        // 추격 대치 플래그는 추격을 떠나면 반드시 내린다 — ZombieChaseState.Exit 누락에 대비한 이중 안전(P-036).
        if (previousState == ZombieStateKind.Chase && nextState != ZombieStateKind.Chase) controller.ServerSetChaseHold(false);

        // 래치 전이 훅 — 상태 객체가 아니라 kind 로 건다. 종 훅③이 상태 구현을 갈아 끼워도(Dog 울부짖음·돌진) 불변식
        // 「래치 ⇔ 발각 전환·추격·공격」이 코어에서 지켜진다. 무리 묶기는 발각 전환 진입에서만 한다 —
        // 관성 추격에서 돌아온 추격(T8b)은 포효도 동조도 없다.
        if (nextState == ZombieStateKind.RoarTransition) controller.ServerSetLatched(true, true);
        else if (nextState == ZombieStateKind.Chase) controller.ServerSetLatched(true, false);
        else if (nextState == ZombieStateKind.ChaseBlind) controller.ServerSetLatched(false, false);
        else if (nextState == ZombieStateKind.Flee) controller.ServerSetLatched(false, false); // 도주는 추격이 아니다 — 래치를 내려 냉각을 열고 추격 음악도 켜지 않는다

        if (previousState != nextState)
        {
            Transform target = controller.CurrentTarget;
            Log.D($"[ZombieState] {NetworkObjectId} {controller.Profile.Species} {previousState} 탈출 → {nextState} 입장 | 타겟 {(target != null ? target.name : "None")} · 래치 {controller.IsLatched} · 의심도 {controller.SuspicionValue:0} · 타겟자극 {controller.HasTargetStimulus}");
        }

        currentState = CreateState(nextState);
        controller.ServerSetState(nextState);
        // 발행은 Enter 이전 — Enter 안에서 연쇄 전이가 나면 바깥 전이보다 안쪽 전이가 먼저 방송돼 순서가 뒤집힌다.
        // 동일 상태 강제 재진입(force)은 전이가 아니라 재동기화라 방송하지 않는다.
        if (previousState != nextState)
        {
            stateChangedChannel.RaiseEvent(new ZombieStateChangedEvent(NetworkObjectId, previousState, nextState));
        }
        currentState.Enter(this);
    }

    /// <summary>
    /// 상태 객체를 만든다. 종 전용 구현(훅③)을 먼저 물어보고, 없을 때만 공통 상태를 쓴다 —
    /// 전이표는 교체와 무관하게 그대로다(무엇으로 갈지는 코어가, 그 상태가 무엇을 하는지는 종이 정한다).
    /// </summary>
    /// <param name="kind">만들려는 상태.</param>
    /// <returns>상태 객체.</returns>
    private IZombieState CreateState(ZombieStateKind kind)
    {
        IZombieState speciesState = controller.TryCreateSpeciesState(kind);
        if (speciesState != null) return speciesState;
        switch (kind)
        {
            case ZombieStateKind.Alert: return new ZombieAlertState();
            case ZombieStateKind.Investigate: return new ZombieInvestigateState();
            case ZombieStateKind.RoarTransition: return new ZombieRoarTransitionState();
            case ZombieStateKind.Chase: return new ZombieChaseState();
            case ZombieStateKind.ChaseBlind: return new ZombieChaseBlindState();
            case ZombieStateKind.Attack: return new ZombieAttackState();
            case ZombieStateKind.Subside: return new ZombieSubsideState();
            default: return new ZombieWanderState();
        }
    }

    public bool IsDestinationReachable { get; private set; } = true; // 마지막 요청 지점이 NavMesh 위였는가. false 면 최근접 지점으로 가는 중(E11)
    private bool hasBlockedDestination;   // 루트모션이 막혔다고 알린 목적지를 들고 있는가
    private Vector3 blockedDestination;   // 그 목적지 — 같은 곳을 다시 걸면 도착 취급해 상태가 다른 지점을 뽑게 한다
    public Vector3 LastRequestedDestination => lastRequestedDestination;      // 마지막으로 에이전트에 건 지점(메시 위). 도달 불가 시 「최근접 지점」

    public void SetDestination(Vector3 destination)
    {
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // 메시 밖 요청은 최근접 지점으로 대신 가되 도달 불가로 기록한다 — 추격이 이를 보고 대치·응시한다(E11 개정 · B-161).
        bool sampled = TrySampleApproachPoint(destination, out Vector3 point, out bool reachable);
        IsDestinationReachable = reachable;
        if (!sampled) return;

        float refreshDistance = controller.Profile.DestinationRefreshDistance;
        if (hasRequestedDestination
            && !agent.isStopped
            && agent.hasPath
            && (lastRequestedDestination - point).sqrMagnitude <= refreshDistance * refreshDistance)
        {
            return;
        }

        // 막혔던 목적지와 다른 곳이면 포기 기록을 푼다 — 같은 곳이면 IsAtDestination 이 도착으로 답한다.
        if (hasBlockedDestination
            && (blockedDestination - point).sqrMagnitude > blockedDestinationMatchMeters * blockedDestinationMatchMeters)
        {
            hasBlockedDestination = false;
        }

        // 정지 중인데 목적지가 발밑이면 경로를 걸지 않는다 — 걸면 pathPending 한 박자 뒤 도착·정지가 매 프레임
        // 반복돼(조사 중 자극 수신 루프) 가장자리에서 가다 서다를 되풀이한다. 요청만 기록하고 도착 상태로 둔다.
        Vector3 planarToHit = point - transform.position;
        planarToHit.y = 0f;
        if (agent.isStopped && planarToHit.sqrMagnitude <= refreshDistance * refreshDistance)
        {
            lastRequestedDestination = point;
            hasRequestedDestination = true;
            return;
        }

        lastRequestedDestination = point;
        hasRequestedDestination = true;
        agent.isStopped = false;
        agent.SetDestination(point);
    }

    /// <summary>
    /// 타겟의 접근 지점을 경로를 걸지 않고 구한다(P-036) — <see cref="SetDestination"/> 의 샘플 2단(2m → destinationFallbackSampleMeters)과
    /// <see cref="DestinationReachToleranceMeters"/> 도달 가능 판정을 그대로 돌려준다. 추격 대치의 진입·해제 판정이 쓴다.
    /// </summary>
    /// <param name="target">타겟 월드 위치.</param>
    /// <param name="point">NavMesh 최근접 지점. 샘플 실패면 현재 위치.</param>
    /// <param name="reachable">최근접 지점이 타겟과 수평 0.5m 안이면 true. 샘플 실패면 false.</param>
    /// <returns>샘플 성공 여부.</returns>
    public bool TrySampleApproachPoint(Vector3 target, out Vector3 point, out bool reachable)
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다.
        point = transform.position;
        reachable = false;
        if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, agent.areaMask)
            && !NavMesh.SamplePosition(target, out hit, destinationFallbackSampleMeters, agent.areaMask))
        {
            return false;
        }

        Vector3 offset = hit.position - target;
        offset.y = 0f;
        point = hit.position;
        reachable = offset.sqrMagnitude <= DestinationReachToleranceMeters * DestinationReachToleranceMeters;
        return true;
    }

    public bool HasPath => agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath; // 에이전트가 경로를 쥐고 있는가 — 상태가 머신 API 만 쓰게 하는 공개 접근자(P-036)

    public void StopAgent()
    {
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    /// <summary>
    /// 루트모션이 가장자리·벽에 막혀 전진하지 못할 때 호출된다. 그 자리에 멈추고 현재 목적지를 「막힌 곳」으로 기억한다 —
    /// 이후 같은 목적지는 도착 취급되어 각 상태가 자기 규칙대로 다른 지점(새 순찰점·수색점)을 잡는다.
    /// 발각·추격 계열(RoarTransition/Chase/Attack)은 무시한다 — 타겟은 계속 움직이고 벽 슬라이드가 몸을 풀어 주는데,
    /// 여기서 세우면 타겟이 0.5m 움직일 때까지 벽 앞에 서 있게 된다(추격 중 벽에 걸리던 원인).
    /// </summary>
    public void MarkBlocked()
    {
        if (!IsServer || !hasRequestedDestination) return;
        ZombieStateKind kind = currentState != null ? currentState.Kind : ZombieStateKind.Wander;
        if (kind == ZombieStateKind.RoarTransition || kind == ZombieStateKind.Chase || kind == ZombieStateKind.Attack
            || kind == ZombieStateKind.ChaseBlind) return;
        //Log.D($"[ZombieStateMachine] {name} 이동 막힘 — 목적지 포기 {lastRequestedDestination}");
        blockedDestination = lastRequestedDestination;
        hasBlockedDestination = true;
        StopAgent();
    }

    public bool IsAtDestination(float tolerance = 0.25f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending) return false;
        if (!hasRequestedDestination) return false;
        // 막혀서 포기한 목적지는 도착으로 답한다 — 상태가 같은 곳을 고집하지 않고 다음 지점으로 넘어가게.
        if (hasBlockedDestination
            && (blockedDestination - lastRequestedDestination).sqrMagnitude <= blockedDestinationMatchMeters * blockedDestinationMatchMeters)
        {
            return true;
        }
        if (agent.hasPath) return agent.remainingDistance <= tolerance;

        Vector3 current = transform.position;
        Vector3 destination = lastRequestedDestination;
        current.y = 0f;
        destination.y = 0f;
        return Vector3.Distance(current, destination) <= tolerance;
    }

    public void MoveToHome() => SetDestination(controller.HomePosition);
    public void MoveToInvestigationPoint() => SetDestination(controller.LastKnownPosition);

    public ChaseBlindNavigator ChaseBlindNav => chaseBlindNav; // 관성 추격 간선 선택기(디버그·테스트 관찰용) — 상태는 아래 ChaseBlindMove 3종만 부른다

    /// <summary>
    /// 관성 추격 이동을 시작한다 — 맵 그래프를 선택기에 걸고 첫 간선을 향한다.
    /// 첫 방향은 타겟의 마지막 속도 벡터, 0 이면 좀비 전방이다. 그래프가 없거나 열린 간선이 0 이면 제자리에 선다.
    /// </summary>
    public void BeginChaseBlindMove()
    {
        Log.D($"[ZombieStateMachine] {NetworkObjectId} BeginChaseBlindMove");

        // 그래프는 들어올 때마다 다시 읽는다 — 클라이언트·튜토리얼·서버 미바인드면 Current 가 비어 있고, 선택기는 「열린 간선 0」 으로 답한다.
        MapRuntimeGraph mapGraph = runtimeGraph.Current;
        chaseBlindGraph = mapGraph != null ? new MapRuntimeGraphChaseBlindAdapter(mapGraph) : null;
        chaseBlindNav.Bind(chaseBlindGraph);

        // 이전 상태가 남긴 「막힌 목적지」 기록을 지운다 — 관성 추격 중에는 새로 기록되지 않으므로(MarkBlocked 무시 목록),
        // 여기서 지우면 IsAtDestination 의 막힘 도착 분기가 간선 도착으로 읽힐 일이 없다.
        hasBlockedDestination = false;

        Vector3 heading = controller.LastKnownVelocity;
        heading.y = 0f;
        if (heading.sqrMagnitude < chaseBlindHeadingMinSqrMps) heading = transform.forward;

        chaseBlindNav.Begin(transform.position, heading);
        PickChaseBlindEdge();
    }

    /// <summary>
    /// 다음 간선을 골라 향한다. 고른 간선이 NavMesh 밖이면 그 간선을 빼고 다시 고른다 — 후보가 하나씩 줄어 반드시 끝난다.
    /// 열린 간선이 0 이면 그 자리에 서고 향하는 간선을 비운다(이후 다시 고르지 않는다).
    /// </summary>
    private void PickChaseBlindEdge()
    {
        while (chaseBlindNav.TryPickNextEdge(transform.position, chaseBlindForwardConeDegrees, out int edgeIndex))
        {
            MoveToEdge(edgeIndex);
            if (IsDestinationReachable) return;
            chaseBlindNav.OnEdgeUnreachable(edgeIndex);
        }

        chaseBlindEdge = chaseBlindNoEdge;
        StopAgent();
    }

    /// <summary>향하는 간선까지 경로가 끊겼는지 돌려준다 — 부분 경로의 끝에 선 것을 간선 도착으로 읽지 않기 위한 검사.</summary>
    /// <returns>경로 계산이 끝났고 그 경로가 간선까지 닿지 않으면 true.</returns>
    private bool IsChaseBlindPathBroken()
    {
        if (!agent.enabled || !agent.isOnNavMesh || agent.pathPending || !agent.hasPath) return false;
        return agent.pathStatus != NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// 관성 추격 이동을 한 박자 진행한다 — 간선 도착이면 다음 간선, 도달 불가·무진행이면 그 간선을 빼고 다시 고른다.
    /// 도착은 선행 반경이다: 간선 지점에 정확히 서지 않고 반경 안에 들거나 지점을 지나치면 곧바로 다음 간선으로 꺾어 멈추지 않는다
    /// (정확 도착을 요구하면 선회 반경 때문에 지점 주위를 맴돈다).
    /// 열린 간선이 0 이면 그 자리에 멈춰 서고 다시 고르지 않는다(좀비공통 4-4 「막다른 곳」 — 타이머와 인지는 상태가 계속 돌린다).
    /// </summary>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void TickChaseBlindMove(float deltaTime)
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다.
        // 향하는 간선이 없으면 막다른 곳에 선 것이다 — 서 있는 동안 문이 열려도 다시 고르지 않는다.
        if (chaseBlindEdge == chaseBlindNoEdge) return;

        // 도달 불가 — 간선 지점이 NavMesh 밖이거나 경로가 거기까지 닿지 않는다. 도착 검사보다 먼저다(부분 경로의 끝도 「도착」 으로 읽힌다).
        if (!IsDestinationReachable || IsChaseBlindPathBroken())
        {
            chaseBlindNav.OnEdgeUnreachable(chaseBlindEdge);
            PickChaseBlindEdge();
            return;
        }

        if (HasReachedChaseBlindEdge())
        {
            // 진행 방향은 실제 이동 벡터(출발 위치 → 간선 위치) — 후보 방향과 같은 「좀비 위치」 기준이라야 원뿔 판정이 어긋나지 않는다.
            chaseBlindNav.OnEdgeReached(chaseBlindEdge, chaseBlindEdgePosition - chaseBlindMoveStart);
            PickChaseBlindEdge();
            return;
        }

        // 무진행 — 관성 추격은 MarkBlocked 를 무시하므로 지나갈 수 없는 간선은 이 타임아웃으로만 걸러진다.
        if (agent.enabled && agent.isOnNavMesh && agent.pathPending) return; // 경로 계산 중에 서 있는 것은 막힌 것이 아니다

        Vector3 moved = transform.position - chaseBlindStuckAnchor;
        moved.y = 0f;
        if (moved.sqrMagnitude >= chaseBlindStuckMeters * chaseBlindStuckMeters)
        {
            chaseBlindStuckAnchor = transform.position;
            chaseBlindStuckTimer = 0f;
            return;
        }

        chaseBlindStuckTimer += deltaTime;
        if (chaseBlindStuckTimer < chaseBlindStuckSeconds) return;

        chaseBlindNav.OnEdgeUnreachable(chaseBlindEdge);
        PickChaseBlindEdge();
    }

    /// <summary>
    /// 향하는 간선 지점에 선행 도착했는지 — 수평 거리가 반경 안이거나, 지점이 진행 방향 뒤로 넘어갔고 반경의 2배 안이면 도착이다.
    /// IsAtDestination 은 쓰지 않는다(막힘 도착 분기와 remainingDistance 가 끼어든다).
    /// 진행 방향은 agent.desiredVelocity — 루트모션·회전이 따라가는 조향 정본이라 몸의 forward 보다 앞서 꺾인다. 0 이면 transform.forward.
    /// </summary>
    /// <returns>선행 도착이면 true.</returns>
    private bool HasReachedChaseBlindEdge()
    {
        Vector3 toEdge = chaseBlindEdgePosition - transform.position;
        toEdge.y = 0f;
        float sqrDistance = toEdge.sqrMagnitude;
        if (sqrDistance <= chaseBlindArriveMeters * chaseBlindArriveMeters) return true;

        float lookAhead = chaseBlindArriveMeters * 2f;
        if (sqrDistance > lookAhead * lookAhead) return false;

        Vector3 heading = agent.enabled && agent.isOnNavMesh ? agent.desiredVelocity : Vector3.zero;
        heading.y = 0f;
        if (heading.sqrMagnitude < chaseBlindHeadingMinSqrMps) heading = transform.forward;
        return Vector3.Dot(toEdge, heading) < 0f;
    }

    /// <summary>관성 추격 이동을 끝낸다 — 선택기와 무진행 추적을 비운다.</summary>
    public void EndChaseBlindMove()
    {
        Log.D($"[ZombieStateMachine] {NetworkObjectId} EndChaseBlindMove");
        chaseBlindNav.End();
        chaseBlindGraph = null;
        chaseBlindEdge = chaseBlindNoEdge;
        chaseBlindStuckTimer = 0f;
    }

    /// <summary>간선 위치를 목적지로 건다 — 허공의 가짜 목적지가 아니라 그래프가 준 간선 지점만 쓴다.</summary>
    /// <param name="edgeIndex">향할 간선 인덱스.</param>
    public void MoveToEdge(int edgeIndex)
    {
        Log.D($"[ZombieStateMachine] {NetworkObjectId} MoveToEdge e{edgeIndex}");
        chaseBlindEdge = edgeIndex;
        chaseBlindStuckTimer = 0f;
        chaseBlindStuckAnchor = transform.position;
        chaseBlindMoveStart = transform.position;
        chaseBlindEdgePosition = chaseBlindGraph.EdgeWorldPosition(edgeIndex);
        SetDestination(chaseBlindEdgePosition);
    }

    /// <summary>
    /// 주어진 시간에 딱 맞게 목표 지점을 바라보도록 회전을 넘겨받는다.
    /// 선회 속도를 <c>남은 각도 ÷ 시간</c> 으로 역산하므로, 90도를 돌든 180도를 돌든 같은 순간에 끝난다.
    /// 기본 선회 속도(TurnSpeedDegreesPerSecond)는 추격·조사용이라 배회 대기에 쓰면 한참 남기고 멈춰 선다.
    /// </summary>
    /// <param name="worldPoint">바라볼 월드 지점.</param>
    /// <param name="seconds">이 시간 안에 회전을 끝낸다.</param>
    public void BeginTimedFacing(Vector3 worldPoint, float seconds)
    {
        facingTarget = worldPoint;
        hasFacingOverride = true;

        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;

        // 목표가 발밑이거나 시간이 없으면 역산이 성립하지 않는다. 기본 선회 속도로 되돌린다.
        if (direction.sqrMagnitude < 0.01f || seconds <= 0f)
        {
            facingDegreesPerSecond = controller.Profile.TurnSpeedDegreesPerSecond;
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        facingDegreesPerSecond = Quaternion.Angle(controller.ServerFacingRotation, targetRotation) / seconds; // 회전 정본 기준 — transform 은 보간 포즈라 잔여 각도가 어긋난다
    }

    /// <summary>회전 오버라이드를 해제하고 기본 규칙(이동 방향 / 관심 지점)으로 되돌린다.</summary>
    public void EndTimedFacing() => hasFacingOverride = false;

    /// <summary>
    /// 「현재 위치」 주변에서 배회 지점을 뽑는다 — 홈 기준(<see cref="TryCreateRandomPatrolPoint"/>)과 달리
    /// 다리마다 기준점이 이동해 맵 전체를 떠돌게 된다. Dog 자유 배회 전용(D-10).
    /// </summary>
    /// <param name="point">뽑힌 지점.</param>
    /// <returns>유효한 지점을 찾았으면 true.</returns>
    public bool TryCreateRoamPoint(out Vector3 point)
    {
        point = transform.position;
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

        float radius = controller.PatrolRadius;
        if (radius <= 0f) return false;

        float snapDistance = Mathf.Min(2f, radius);
        int attempts = Mathf.Max(1, controller.Profile.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, snapDistance, agent.areaMask)) continue;

            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            point = hit.position;
            return true;
        }

        return false;
    }

    /// <summary>중심점 주변 반경 안에서 도달 가능한 지점을 뽑는다 — 조사 도착 후 주변 수색용.</summary>
    /// <param name="center">수색 중심.</param>
    /// <param name="radius">수색 반경(m).</param>
    /// <param name="point">뽑힌 지점.</param>
    /// <returns>유효한 지점을 찾았으면 true.</returns>
    public bool TryCreateSearchPoint(Vector3 center, float radius, out Vector3 point)
    {
        point = center;
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        if (radius <= 0f) return false;

        float snapDistance = Mathf.Min(2f, radius);
        int attempts = Mathf.Max(1, controller.Profile.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, snapDistance, agent.areaMask)) continue;
            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            point = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>후보 지점을 NavMesh 에 스냅하고 완전 경로가 있는지 확인한다 — 종 전용 지점 선정(Oiler 도주)의 공용 부품.</summary>
    /// <param name="candidate">후보 월드 지점.</param>
    /// <param name="snapDistance">NavMesh 스냅 허용 거리(m).</param>
    /// <param name="point">스냅된 도달 가능 지점.</param>
    /// <returns>메시 위이고 경로가 완전하면 true.</returns>
    public bool TrySnapReachablePoint(Vector3 candidate, float snapDistance, out Vector3 point)
    {
        point = candidate;
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, snapDistance, agent.areaMask)) return false;
        reachabilityPath ??= new NavMeshPath();
        if (!agent.CalculatePath(hit.position, reachabilityPath) || reachabilityPath.status != NavMeshPathStatus.PathComplete) return false;

        point = hit.position;
        return true;
    }

    public bool TryCreateRandomPatrolPoint(out Vector3 point)
    {
        point = controller.HomePosition;
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

        float radius = controller.PatrolRadius;
        if (radius <= 0f) return false;

        // 스냅 거리는 배회 반경을 넘지 않는다. 2m 로 고정하면 반경이 그보다 좁은 무리 좀비(ZombieCrowd)의
        // 후보가 반경 밖으로 스냅돼 아래 반경 검사에서 계속 버려지고, 순찰점이 영영 안 잡혀 재시도만 돈다.
        float snapDistance = Mathf.Min(2f, radius);

        int attempts = Mathf.Max(1, controller.Profile.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = controller.HomePosition + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, snapDistance, agent.areaMask)) continue;

            Vector3 homeToPoint = hit.position - controller.HomePosition;
            homeToPoint.y = 0f;
            if (homeToPoint.sqrMagnitude > radius * radius) continue;

            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            point = hit.position;
            return true;
        }

        return false;
    }

    public void SetPatrolPoint(Vector3 point)
    {
        patrolPoint = point;
        hasPatrolPoint = true;
    }

    public void ClearPatrolPoint()
    {
        patrolPoint = default;
        hasPatrolPoint = false;
    }

    public void MoveToTarget()
    {
        if (controller.CurrentTarget != null) SetDestination(controller.CurrentTarget.position);
    }

    /// <summary>
    /// 목표 이동 속도를 건다. 즉시 대입이 아니라 램프의 목표값이다 —
    /// 실제 <c>agent.speed</c> 는 <see cref="ServerTick"/> 이 매 프레임
    /// <see cref="ZombieProfileSO.AccelerationMps2"/> 로 좁혀 간다(D-3).
    /// <c>agent.acceleration</c> 은 쓸 수 없다: 이동을 루트모션이 만들어 desiredVelocity 가 그 값을 타지 않는다.
    /// </summary>
    /// <param name="speed">목표 속도(m/s).</param>
    public void SetTargetSpeed(float speed)
    {
        if (!IsServer) return;
        targetSpeed = speed;
    }

    /// <summary>
    /// 목표 속도까지 가속도만큼만 좁힌다. 발각 순간 속도가 계단처럼 뛰는 것을 막는다.
    /// 서 있는 동안의 목표는 0 이다 — 그래야 설 때 감속하고 출발할 때 가속한다. 정지 중에도 목표를 들고 있으면
    /// isStopped 가 풀리는 프레임에 전속력이 튀어나와, 걷기 시작이 뚝 끊긴 것처럼 보인다.
    /// </summary>
    /// <param name="deltaTime">경과 시간(초).</param>
    private void UpdateSpeedRamp(float deltaTime)
    {
        float rampTarget = agent.isStopped ? 0f : targetSpeed;
        agent.speed = Mathf.MoveTowards(agent.speed, rampTarget, controller.Profile.AccelerationMps2 * deltaTime);
    }

    /// <summary>
    /// 타겟까지 남은 거리로 회피 우선순위를 매 프레임 갱신한다(가까울수록 우선).
    /// 타겟이 없으면(추격 상실 구간) 마지막 값을 유지한다 — 어차피 상태를 벗어날 때 리셋된다.
    /// </summary>
    public void UpdateChaseAvoidancePriority()
    {
        if (!IsServer || agent == null || controller.CurrentTarget == null) return;

        float distance = Vector3.Distance(transform.position, controller.CurrentTarget.position);
        float t = Mathf.Clamp01(distance / chasePriorityMaxDistance);
        agent.avoidancePriority = Mathf.RoundToInt(Mathf.Lerp(chasePriorityClosest, chasePriorityFarthest, t));
    }

    /// <summary>회피 우선순위를 이 좀비 고유값으로 되돌린다. 추격을 벗어날 때 반드시 호출한다.</summary>
    public void ResetAvoidancePriority()
    {
        if (!IsServer || agent == null) return;
        agent.avoidancePriority = baseAvoidancePriority;
    }

    /// <summary>
    /// 옆걸음 양보(P3)를 진행한다. 새로 시작하는 쪽은 서 있는(isStopped) 좀비 한정 —
    /// 정지 에이전트만 회피 계산에서 움직이지 않아 이 걸음이 필요하다(걷는 중의 접촉 회피는 <see cref="TryBeginMovingYield"/>).
    /// 걸음 중에는 상태 Tick 이 쉬므로(대기 상태가 매 프레임 StopAgent 로 붙잡는다), 도착·시간 상한으로 반드시 끝낸다.
    /// 끝날 때 서서 시작한 걸음은 StopAgent 로 돌아가고, 걷다가 시작한 걸음은 세우지 않는다 — 다음 박자 상태 Tick 이 제 목적지를 다시 건다.
    /// </summary>
    /// <param name="deltaTime">경과 시간(초).</param>
    private void UpdateYield(float deltaTime)
    {
        if (isYielding)
        {
            yieldTimer += deltaTime;
            if (yieldTimer >= yieldTimeoutSeconds || IsAtDestination(0.15f))
            {
                isYielding = false;
                yieldCooldown = yieldCooldownSeconds;
                if (!yieldWasMoving) StopAgent();
            }
            return;
        }

        yieldCooldown -= deltaTime;
        if (yieldCooldown > 0f) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.isStopped) return;
        if (controller.IsLatched) return;
        if (!IsYieldableStateKind()) return;

        if (!TryFindYieldTarget(out Vector3 target, controller.HasPlayerContact)) return;

        isYielding = true;
        yieldWasMoving = false;
        yieldTimer = 0f;
        Log.D($"[ZombieStateMachine] {name} 양보 시작(정지 중) — 접촉 {controller.HasPlayerContact} · 지점 {target} · 거리 {Vector3.Distance(transform.position, target):F2}m");
        SetTargetSpeed(controller.Profile.WanderSpeed);
        SetDestination(target);
    }

    /// <summary>
    /// 걷는 중 위장 접촉의 회피 걸음을 시작한다 — 세우지 않고 지금 목표 속도 그대로 옆 지점으로 목적지만 바꾼다.
    /// 쿨다운 중이거나 비켜설 자리가 없으면 시작하지 않는다(호출자는 상태 Tick 으로 흘러 계속 간다).
    /// </summary>
    /// <returns>회피 걸음을 시작했으면 true.</returns>
    private bool TryBeginMovingYield()
    {
        if (yieldCooldown > 0f) return false;
        if (!TryFindYieldTarget(out Vector3 target, true)) return false;

        isYielding = true;
        yieldWasMoving = true;
        yieldTimer = 0f;
        Log.D($"[ZombieStateMachine] {name} 양보 시작(걷는 중) — 지점 {target} · 거리 {Vector3.Distance(transform.position, target):F2}m");
        SetDestination(target);
        return true;
    }

    /// <summary>양보·접촉 회피가 허용되는 상태인가 — 배회·경계·조사만. Attack/Roar 는 교전 연출이라 제외.</summary>
    /// <returns>배회·경계·조사면 true.</returns>
    private bool IsYieldableStateKind()
    {
        ZombieStateKind kind = currentState != null ? currentState.Kind : controller.CurrentState;
        return kind == ZombieStateKind.Wander || kind == ZombieStateKind.Alert || kind == ZombieStateKind.Investigate;
    }

    /// <summary>
    /// 플레이어와 몸이 닿아 회피가 필요한지(위장 무관 · 2026-10-18 길막 해소). 배회·경계·조사 한정 —
    /// 추격·공격·포효는 교전이라 상태 머신은 끼어들지 않고(파고듦 자체는 ZombieRootMotion 의 접촉 양보가 깎는다), latch 상태는 이미 대상을 물고 있다.
    /// 비위장 플레이어는 보이면 지각이 먼저 추격으로 올리므로, 여기 닿는 경우는 「못 본 채 부딪힌」 접촉이다.
    /// 옆걸음 우선 · 정지는 폴백(P-043 U4): 걷는 중이면 <see cref="TryBeginMovingYield"/> 로 비켜 가고, 못 비키면 <see cref="ServerTick"/> 이 세우며,
    /// 서 있으면 <see cref="UpdateYield"/> 의 옆걸음이 맡는다.
    /// </summary>
    /// <returns>접촉 중이고 배회·경계·조사 상태면 true.</returns>
    private bool ShouldHoldForDisguisedContact()
    {
        if (!controller.HasPlayerContact || controller.IsLatched) return false;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        return IsYieldableStateKind();
    }

    /// <summary>
    /// 비켜설 지점을 뽑는다. 두 경로:
    /// ① 몸이 닿아 있으면(<see cref="ZombieController.HasPlayerContact"/>) 플레이어 속도와 무관하게 접촉 법선의 수직 방향으로 한 걸음 —
    ///    서 있는 플레이어에 부딪혔거나 플레이어가 서서 미는 길막도 이 경로가 푼다(2026-10-18 사용자 확정).
    /// ② 접촉 전 선제 옆걸음: 접근 중인 플레이어를 찾아 진행선에서 먼 쪽(이미 치우친 쪽)으로 — 걸음 하나로 이동 경로에서 벗어난다.
    /// </summary>
    /// <param name="target">비켜설 NavMesh 지점.</param>
    /// <param name="includeUndisguised">②에서 비위장 플레이어도 대상에 넣는가. 선제 옆걸음은 위장 한정(비위장은 보이면 추격이 먼저다). ①은 이 값과 무관하게 위장 무관.</param>
    /// <returns>비켜야 할 대상이 있고 지점이 잡혔으면 true.</returns>
    private bool TryFindYieldTarget(out Vector3 target, bool includeUndisguised)
    {
        target = default;
        Vector3 myPosition = transform.position;

        // ① 접촉 양보 — 법선(플레이어 → 좀비)에 수직인 좌우, 둘 다 막히면 법선 방향 뒷걸음(좁은 복도 길막은 물러나야 풀린다).
        // 플레이어가 멈춰 있어도 비켜선다. 스냅이 발밑으로 끌어온 후보는 걸음이 안 되니 버린다.
        if (controller.HasPlayerContact)
        {
            Vector3 normal = controller.PlayerContactNormal;
            Vector3 contactSide = Vector3.Cross(Vector3.up, normal);
            if (TrySampleContactYieldPoint(myPosition, contactSide, out target)) return true;
            if (TrySampleContactYieldPoint(myPosition, -contactSide, out target)) return true;
            if (TrySampleContactYieldPoint(myPosition, normal, out target)) return true;
            Log.D($"[ZombieStateMachine] {name} 접촉 양보 후보 전부 실패 — 위치 {myPosition} · 법선 {normal}");
        }

        IReadOnlyList<IZombiePerceptionSource> sources = controller.RuntimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            IZombiePerceptionSource source = sources[i];
            if (source == null || source.Root == null || source.IsSpectator) continue;
            if (!includeUndisguised && !source.IsDisguised) continue;

            Vector3 toMe = myPosition - source.Root.position;
            toMe.y = 0f;
            if (toMe.sqrMagnitude > yieldDetectRadiusMeters * yieldDetectRadiusMeters) continue;
            if (toMe.sqrMagnitude < 0.0001f) continue;

            // 「나에게 오는 중」 판정 — P2 프록시가 재 둔 수평 속도를 그대로 쓴다.
            PlayerNavAvoidanceProxy proxy = source.Root.GetComponent<PlayerNavAvoidanceProxy>();
            if (proxy == null) continue;
            Vector3 velocity = proxy.PlanarVelocity;
            if (velocity.sqrMagnitude < yieldApproachMinMps * yieldApproachMinMps) continue;

            Vector3 travel = velocity.normalized;
            if (Vector3.Dot(travel, toMe.normalized) < yieldApproachMinDot) continue;

            Vector3 lateral = toMe - travel * Vector3.Dot(toMe, travel);
            lateral.y = 0f;
            Vector3 side = lateral.sqrMagnitude > 0.0001f
                ? lateral.normalized
                : Vector3.Cross(Vector3.up, travel); // 정통으로 마주 오면 오른쪽

            if (TrySampleYieldPoint(myPosition + side * yieldStepMeters, out target)) return true;
            if (TrySampleYieldPoint(myPosition - side * yieldStepMeters, out target)) return true; // 그쪽이 벽이면 반대쪽
        }

        return false;
    }

    /// <summary>접촉 양보 후보 1개 — 방향으로 한 걸음 간 지점을 스냅하고, 현재 위치에서 최소 걸음 폭 미만이면 실패로 본다.</summary>
    /// <param name="origin">현재 위치.</param>
    /// <param name="direction">걸음 방향(수평 단위 벡터).</param>
    /// <param name="point">스냅된 지점.</param>
    /// <returns>최소 걸음 폭 이상 떨어진 메시 위 지점을 얻었으면 true.</returns>
    private bool TrySampleContactYieldPoint(Vector3 origin, Vector3 direction, out Vector3 point)
    {
        if (!TrySampleYieldPoint(origin + direction * yieldStepMeters, out point)) return false;

        Vector3 planar = point - origin;
        planar.y = 0f;
        return planar.sqrMagnitude >= yieldMinStepMeters * yieldMinStepMeters;
    }

    /// <summary>후보 지점을 NavMesh 에 스냅한다.</summary>
    /// <param name="candidate">후보 월드 지점.</param>
    /// <param name="point">스냅된 지점.</param>
    /// <returns>메시 위 지점을 얻었으면 true.</returns>
    private bool TrySampleYieldPoint(Vector3 candidate, out Vector3 point)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.5f, agent.areaMask))
        {
            point = hit.position;
            return true;
        }

        point = default;
        return false;
    }

    private void UpdateFacing(float deltaTime)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 direction;
        float degreesPerSecond = controller.Profile.TurnSpeedDegreesPerSecond;

        if (hasFacingOverride)
        {
            // 배회 대기처럼 "정해진 시간에 걸쳐 이쪽을 보게 하라"는 요구는 기본 선회 속도로는 표현할 수 없다.
            direction = facingTarget - transform.position;
            degreesPerSecond = facingDegreesPerSecond;
        }
        else if (controller.IsEngaging && controller.HasPlayerContact && controller.CurrentTarget != null)
        {
            // 교전 중 몸이 닿아 있으면 타겟을 본다 — desiredVelocity 는 RVO 가 플레이어 프록시를 피하려는 옆 방향이라
            // 그대로 따르면 시야 원뿔이 타겟에서 벗어나 코앞의 타겟을 놓친다(접촉 공전 · 2026-10-18).
            direction = controller.CurrentTarget.position - transform.position;
        }
        else if (!agent.isStopped)
        {
            direction = agent.desiredVelocity;
        }
        else
        {
            // 정지 중에도 관심 지점을 향해 돈다. 멈췄다고 회전을 막으면 시야 원뿔이 마지막 이동 방향에
            // 그대로 굳어, 경계·조사·타격 락처럼 제자리에 서는 구간에서는 바로 옆을 지나가도 영영 보지 못한다.
            Vector3 focus = controller.CurrentTarget != null
                ? controller.CurrentTarget.position
                : controller.LastKnownPosition;
            direction = focus - transform.position;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
        {
            facingErrorDegrees = 0f;
            return;
        }

        // 회전은 transform 직접 대입이 아니라 회전 정본(ServerFacingRotation)에 누적한다 —
        // 루트 Rigidbody 의 Interpolate 가 매 렌더 프레임 transform 을 보간 포즈로 덮어써,
        // 직접 대입은 씹히고 몸이 이동 방향을 안 따라가는 문워크가 된다. 적용은 ZombieRootMotion 의 MoveRotation.
        Quaternion currentRotation = controller.ServerFacingRotation;
        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float error = Mathf.DeltaAngle(currentRotation.eulerAngles.y, targetRotation.eulerAngles.y);
        if (Mathf.Abs(error) <= controller.Profile.TurnDeadZoneDegrees)
        {
            facingErrorDegrees = 0f;
            return;
        }

        facingErrorDegrees = error;

        // 제자리 회전은 턴 클립의 루트 회전이 만든다(A3 — ServerApplyAnimatedYaw). 여기서 또 돌리면 이중 회전이라
        // 몸이 클립보다 빨리 돌고 그 차이가 그대로 발 미끄러짐이 된다. 걷는 중에는 Turn 상태에 들어가지 않으므로
        // 이 게이트가 열려 있고, 이동 방향 추종은 지금까지처럼 코드가 한다.
        if (controller.IsAnimatedTurning) return;

        controller.ServerSetFacingRotation(Quaternion.RotateTowards(
            currentRotation,
            targetRotation,
            degreesPerSecond * deltaTime));
    }

    /// <summary>
    /// 턴 클립이 이번 물리 박자에 만든 루트 Y 회전을 회전 정본에 적용한다(제자리 회전 정본 경로, A3).
    /// 남은 각도로 잘라 목표를 지나치지 않게 하고, 목표와 반대 부호인 델타는 버린다 — 전이 블렌드 한두 박자에
    /// 반대쪽 클립이 섞여 들어오는데, 그대로 먹이면 목표에서 멀어졌다 돌아오는 떨림이 된다.
    /// </summary>
    /// <param name="degrees">클립이 만든 Y 회전량(도, 부호 있음).</param>
    public void ServerApplyAnimatedYaw(float degrees)
    {
        if (!IsServer || facingErrorDegrees == 0f || degrees == 0f) return;
        if (Mathf.Sign(degrees) != Mathf.Sign(facingErrorDegrees)) return;

        float applied = Mathf.Sign(facingErrorDegrees) * Mathf.Min(Mathf.Abs(degrees), Mathf.Abs(facingErrorDegrees));
        controller.ServerSetFacingRotation(controller.ServerFacingRotation * Quaternion.Euler(0f, applied, 0f));
        facingErrorDegrees -= applied;
    }

    public float GetInvestigationSpeed()
    {
        float effectiveDb = controller.InvestigationStimulusDb;
        if (effectiveDb <= 0f) return controller.Profile.InvestigateBaseSpeed;

        float speed = controller.Profile.InvestigateBaseSpeed
            + Mathf.Max(0f, effectiveDb - controller.Profile.HearMinDb) * controller.Profile.InvestigateDbToSpeed;
        return Mathf.Min(speed, controller.Profile.InvestigateCapSpeed);
    }

    public void NotifyPlayerCaught()
    {
        if (!IsServer || playerCaughtChannel == null || controller.CurrentTarget == null) return;
        if (!controller.AttackEnabled) return; // 공격 OFF 보험 — Chase 전이 가드를 거치지 않는 종(Dog 돌진·Lurker)도 잡힘을 내지 않는다

        playerCaughtChannel.RaiseEvent(new ZombiePlayerCaughtEvent(
            controller.NetworkObjectId,
            controller.CurrentTargetNetworkObjectId,
            controller.Profile.LethalAttack,
            controller.Profile.AttackDamage));
    }
}
