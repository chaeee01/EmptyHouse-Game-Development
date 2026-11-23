using Border.Core;
using UnityEngine;

/// <summary>
/// 홈과 순찰점 사이를 오가며, 도착할 때마다 잠시 서 있는다.
/// 홈 ─이동→ 순찰점 ─대기→ 홈 ─대기→ 순찰점 … 순으로 돈다.
///
/// 대기 시간은 도착마다 <see cref="ZombieProfileSO.PatrolPauseMinSeconds"/>~Max 에서 새로 뽑는다.
/// 고정값을 쓰면 같은 순간에 스폰된 무리가 한 박자로 서고 걸어 기계처럼 보인다.
///
/// 다음 목적지는 대기가 끝날 때가 아니라 <b>시작할 때</b> 정한다. 그래야 서 있는 동안 그쪽으로
/// 몸을 돌릴 수 있고, 회전 속도를 대기 시간에 맞춰 역산해 출발 직전에 정확히 정면을 잡는다
/// (<see cref="ZombieStateMachine.BeginTimedFacing"/>).
/// </summary>
public class ZombieWanderState : IZombieState
{
    private enum PatrolPhase
    {
        MovingHome,     // 홈으로 이동 중
        MovingToPatrol, // 순찰점으로 이동 중
        Pausing         // 도착해서 서 있는 중
    }

    private const float patrolRetrySeconds = 0.5f; // 순찰점 생성 실패 시 재시도 간격(초)

    private PatrolPhase phase;
    private PatrolPhase phaseAfterPause; // 대기가 끝나면 넘어갈 단계
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;
    private float pauseTimer;

    public ZombieStateKind Kind => ZombieStateKind.Wander;

    /// <summary>배회 속도로 맞추고 홈 복귀부터 시작한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        machine.SetTargetSpeed(machine.Controller.Profile.WanderSpeed);
        phase = PatrolPhase.MovingHome;
        hasPatrolPoint = false;
        pauseTimer = 0f;
        machine.ClearPatrolPoint();
        machine.EndTimedFacing();
        machine.MoveToHome();
    }

    /// <summary>이동·대기 단계를 진행한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        // 속도는 매 Tick 다시 건다 — 진입 때 한 번만 걸면 플레이 중 WanderSpeed 를 바꿔도 다음 배회 진입까지 먹지 않는다
        // (추격·진정과 같은 꼴). 대기 중에는 램프가 0 으로 눌러 주므로 목표를 들고 있어도 무해하다.
        machine.SetTargetSpeed(machine.Controller.Profile.WanderSpeed);

        // 배회 OFF: 순찰을 버리고 홈에 제자리 대기한다. 플레이 중 토글 변경도 즉시 반영된다.
        if (!machine.Controller.WanderEnabled)
        {
            if (hasPatrolPoint)
            {
                hasPatrolPoint = false;
                machine.ClearPatrolPoint();
            }
            phase = PatrolPhase.MovingHome;
            machine.EndTimedFacing();

            if (!machine.IsAtDestination(0.25f))
            {
                machine.MoveToHome();
                return;
            }

            machine.StopAgent();
            return;
        }

        switch (phase)
        {
            case PatrolPhase.Pausing:
                TickPause(machine, deltaTime);
                return;

            case PatrolPhase.MovingToPatrol:
                if (!machine.IsAtDestination(0.25f))
                {
                    machine.SetDestination(patrolPoint);
                    return;
                }

                machine.StopAgent();
                BeginPause(machine, PatrolPhase.MovingHome, machine.Controller.HomePosition);
                return;

            default:
                if (!machine.IsAtDestination(0.25f))
                {
                    machine.MoveToHome();
                    return;
                }

                machine.StopAgent();

                // 순찰점은 귀가 중에도 들고 있다가 홈에 실제로 닿았을 때만 버린다.
                if (hasPatrolPoint)
                {
                    hasPatrolPoint = false;
                    machine.ClearPatrolPoint();
                }

                // 다음 순찰점을 지금 잡는다. 대기가 끝난 뒤에 잡으면 서 있는 동안 어디를 볼지 알 수 없다.
                if (!machine.TryCreateRandomPatrolPoint(out patrolPoint))
                {
                    // 잡히지 않으면 방향도 정할 수 없다. 짧게 쉬고 이 분기로 돌아와 다시 시도한다.
                    machine.EndTimedFacing();
                    phase = PatrolPhase.Pausing;
                    phaseAfterPause = PatrolPhase.MovingHome;
                    pauseTimer = patrolRetrySeconds;
                    return;
                }

                hasPatrolPoint = true;
                machine.SetPatrolPoint(patrolPoint);
                BeginPause(machine, PatrolPhase.MovingToPatrol, patrolPoint);
                return;
        }
    }

    /// <summary>배회를 벗어날 때 순찰점 표시와 회전 오버라이드를 함께 거둔다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Exit(ZombieStateMachine machine)
    {
        machine.ClearPatrolPoint();

        // 이걸 빠뜨리면 추격·조사로 넘어간 좀비가 배회 때 잡아둔 지점을 계속 바라본다.
        machine.EndTimedFacing();
    }

    /// <summary>대기 타이머를 깎고, 만료되면 미리 정해 둔 다음 목적지로 출발한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    private void TickPause(ZombieStateMachine machine, float deltaTime)
    {
        // 대기 중 밀려나면 에이전트가 다시 움직이려 든다. 매 프레임 붙잡아 둔다.
        machine.StopAgent();

        pauseTimer -= deltaTime;
        if (pauseTimer > 0f) return;

        machine.EndTimedFacing();

        if (phaseAfterPause == PatrolPhase.MovingToPatrol)
        {
            phase = PatrolPhase.MovingToPatrol;
            machine.SetDestination(patrolPoint);
            return;
        }

        phase = PatrolPhase.MovingHome;
        machine.MoveToHome();
    }

    /// <summary>
    /// 도착 지점에서 대기를 시작한다. 대기 시간은 매번 새로 뽑고, 그 시간에 딱 맞춰
    /// 다음 목적지 쪽으로 도는 회전을 건다 — 출발할 때는 이미 정면을 보고 있게 된다.
    /// </summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="next">대기가 끝나면 넘어갈 단계.</param>
    /// <param name="facingTarget">대기 중 바라볼 다음 목적지.</param>
    private void BeginPause(ZombieStateMachine machine, PatrolPhase next, Vector3 facingTarget)
    {
        ZombieProfileSO data = machine.Controller.Profile;

        phase = PatrolPhase.Pausing;
        phaseAfterPause = next;
        pauseTimer = Random.Range(data.PatrolPauseMinSeconds, data.PatrolPauseMaxSeconds);
        machine.BeginTimedFacing(facingTarget, pauseTimer);
    }
}

public class ZombieAlertState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Alert;
    public void Enter(ZombieStateMachine machine) => machine.StopAgent();
    public void Tick(ZombieStateMachine machine, float deltaTime) { }
    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieInvestigateState : IZombieState
{
    private enum Phase
    {
        Approach, // 조사 지점으로 이동 중 — 타이머는 돌지 않는다(T7 「이동 중에는 돌지 않는다」)
        Search    // 도착 후 주변 수색 — 수색 지점을 옮겨 다니며 타이머가 돈다
    }

    private Phase phase;
    private Vector3 searchCenter;  // 수색 중심 — 도착 시점의 조사 지점
    private float pauseTimer;      // 수색 지점 도착 후 남은 대기(초). 0 이하가 되면 다음 지점을 뽑는다
    private bool hasSearchPoint;   // 수색 지점으로 이동 중인가

    public ZombieStateKind Kind => ZombieStateKind.Investigate;

    /// <summary>조사 속도로 맞추고 조사 지점으로 향한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        machine.SetTargetSpeed(machine.GetInvestigationSpeed());
        BeginApproach(machine);
    }

    /// <summary>접근 → 도착 후 수색 → 시간 만료 시 진정. 새 단서가 오면 접근부터 다시 한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;

        if (controller.HadStimulusThisFrame)
        {
            machine.SetTargetSpeed(machine.GetInvestigationSpeed());
            // 단서 지점이 이미 발밑(도착 범위)이면 접근으로 되돌리지 않는다 — 메시 밖 플레이어가 계속 보일 때
            // 매 프레임 재경로·정지를 반복하며 가장자리에서 가다 서다 하던 루프. 타이머만 리셋하고 수색을 잇는다.
            controller.BeginInvestigateTimer();
            machine.MoveToInvestigationPoint();
            if (phase == Phase.Search && machine.IsAtDestination(1.5f))
            {
                searchCenter = controller.LastKnownPosition;
                return;
            }
            phase = Phase.Approach;
            hasSearchPoint = false;
            return;
        }

        if (phase == Phase.Approach)
        {
            machine.SetTargetSpeed(machine.GetInvestigationSpeed());
            if (!machine.IsAtDestination(1.5f))
            {
                controller.BeginInvestigateTimer();
                machine.MoveToInvestigationPoint();
                return;
            }

            machine.StopAgent();
            phase = Phase.Search;
            searchCenter = controller.LastKnownPosition;
            hasSearchPoint = false;
            pauseTimer = controller.Profile.InvestigateSearchPauseSeconds;
        }

        // 수색 — 도착 이후의 시간이 T7 타이머다. 이동·대기 모두 포함해 센다.
        controller.AdvanceInvestigateTimer(deltaTime);
        if (controller.InvestigateTimer >= controller.Profile.InvestigateToWanderSeconds)
        {
            machine.SwitchState(ZombieStateKind.Subside);
            return;
        }

        if (hasSearchPoint)
        {
            if (!machine.IsAtDestination(0.3f)) return;
            machine.StopAgent();
            hasSearchPoint = false;
            pauseTimer = controller.Profile.InvestigateSearchPauseSeconds;
            return;
        }

        pauseTimer -= deltaTime;
        if (pauseTimer > 0f) return;

        if (!machine.TryCreateSearchPoint(searchCenter, controller.Profile.InvestigateSearchRadius, out Vector3 point))
        {
            pauseTimer = controller.Profile.InvestigateSearchPauseSeconds; // 지점이 안 잡히면 제자리에서 잠시 더 기다렸다 재시도
            return;
        }

        hasSearchPoint = true;
        machine.SetTargetSpeed(controller.Profile.InvestigateBaseSpeed);
        machine.SetDestination(point);
    }

    public void Exit(ZombieStateMachine machine) { }

    /// <summary>조사 지점 접근을 (다시) 시작한다 — 타이머를 리셋하고 수색 상태를 버린다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    private void BeginApproach(ZombieStateMachine machine)
    {
        phase = Phase.Approach;
        hasSearchPoint = false;
        machine.Controller.BeginInvestigateTimer();
        machine.MoveToInvestigationPoint();
    }
}

public class ZombieRoarTransitionState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.RoarTransition;

    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAlertTimer();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        controller.AdvanceAlertTimer(deltaTime);
        if (controller.AlertTimer >= controller.Profile.RoarHoldSeconds)
        {
            machine.SwitchState(ZombieStateKind.Chase);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieChaseState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Chase;
    private bool contactLost; // 타겟 접촉을 놓친 구간인가 — 상실·회복 로그를 에지에서만 남기기 위한 기억
    private bool holding;     // 대치 중인가 — 타겟이 NavMesh 밖이라 최근접 지점에 서서 노려보는 구간(P-036)
    private Vector3 holdAnchor; // 대치 진입 시 최근접 지점 — 여기서 ChaseHoldRepathMeters 이상 옮겨야 다시 따라간다

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetTargetSpeed(machine.Controller.Profile.ChaseSpeed);
        machine.Controller.ResetChaseLostTimer();
        contactLost = false;
        holding = false;
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;

        // 무리 추격에서 누가 비킬지는 타겟까지의 거리가 정한다 — 가까운 좀비가 우선권을 갖고,
        // 뒤의 좀비가 옆으로 돌아 나간다. 거리가 매 프레임 변하므로 여기서 갱신한다(EH-43).
        machine.UpdateChaseAvoidancePriority();

        // 사거리 판정이 지각 판정보다 먼저다. 상실 분기 안에 두면 대상이 시야 원뿔 밖으로 돌아
        // 들어왔을 때(뒤·옆에 붙었을 때) 몸이 닿아 있는데도 래치 유예 동안 때리지 않는다.
        // 유효성은 ServerValidateTarget 이 매 프레임 보장하므로, 여기서 잡히는 대상은 살아 있고 위장도 아니다.
        if (controller.AttackEnabled // 공격 OFF(튜토리얼 관상용·시연 좀비)는 사거리 안에서도 추격을 유지한다 — 철창 앞 벽치기 연출이 이 상태를 읽는다
            && controller.CurrentTarget != null
            && Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position) <= controller.Profile.AttackRange)
        {
            machine.SwitchState(ZombieStateKind.Attack);
            return;
        }

        // 유예 중 타겟이 풀린 뒤(사망·위장) 새 대상을 잡았으면 래치를 다시 건다 — 같은 상태 안이라 전이 훅이 돌지 않는 유일한 경로다.
        if (controller.CurrentTarget != null && !controller.IsLatched)
        {
            controller.ServerSetLatched(true, false);
        }

        // 추격 상실 판정은 "타겟 본인을 지각했는가"(HasTargetStimulus)로 한다.
        // "아무 자극"(HasTrackingStimulus)으로 판정하면 다른 플레이어의 소음이 타이머를
        // 매 프레임 리셋해 좀비가 Chase 에서 영영 내려오지 못한다.
        if (!controller.HasTargetStimulus)
        {
            if (!contactLost)
            {
                contactLost = true;
                Log.D($"[ZombieChaseState] {controller.NetworkObjectId} {controller.Profile.Species} 타겟 자극 상실 — 마지막 위치 {controller.LastKnownPosition} · 거리 {Vector3.Distance(controller.transform.position, controller.LastKnownPosition):0.0}m · 타겟 {(controller.CurrentTarget != null ? controller.CurrentTarget.name : "None")}");
            }

            // 유예 동안은 아직 추격이다 — 마지막으로 접촉한 위치로 계속 간다.
            machine.MoveToInvestigationPoint();
            controller.AdvanceChaseLostTimer(deltaTime);

            // T8a — 시야·소리 모두 유예만큼 놓치면 관성 추격으로 내려간다. 무리원은 스스로 내려가지 않는다:
            // 대표를 매 프레임 복사하므로 혼자 내려가면 다음 복사가 추격으로 되돌려 상태가 핑퐁한다.
            if (controller.ChaseLostTimer >= controller.Profile.ChaseLatchGraceSeconds && !controller.IsFollower)
            {
                Log.D($"[ZombieChaseState] {controller.NetworkObjectId} {controller.Profile.Species} 추격 상실 {controller.ChaseLostTimer:0.0}s — 유예 만료 → 관성 추격");
                machine.SwitchState(ZombieStateKind.ChaseBlind);
            }
            return;
        }

        if (contactLost)
        {
            contactLost = false;
            Log.D($"[ZombieChaseState] {controller.NetworkObjectId} {controller.Profile.Species} 타겟 자극 회복 — 상실 {controller.ChaseLostTimer:0.0}s");
        }

        controller.ResetChaseLostTimer();

        // 타겟이 NavMesh 밖(낭떠러지 아래·철창 밖·높은 단)이면 최근접 지점까지만 올 수 있다. 추격은 유지한다 — 자극이 이어지는
        // 동안 노려보고, 끊기면 위의 래치 유예가 관성 추격으로, 그 만료가 조사로 내린다(좀비공통 E11 개정 · B-161).
        // 강등으로 처리하면 다음 프레임 재발각·포효를 반복하고, latch 를 막으면 메시 없는 자리의 플레이어를
        // 영영 발각하지 못한다(튜토리얼 철창 좀비). 그 구간은 「대치」 서브페이즈다(P-036) — 가장자리에 서서
        // 재경로를 걸지 않고(가다 서다 깜빡임 방지) 복제 플래그로 모든 클라이언트에 Reach 몸짓을 보인다.
        Transform targetTransform = controller.CurrentTarget;
        if (targetTransform == null)
        {
            // 무리원은 대표의 자극 플래그를 복사하므로 타겟 없이 여기 올 수 있다 — 갈 곳이 없으니 대치를 풀고 끝점이면 선다(종전 동작).
            ReleaseHold(controller, "타겟 없음");
            if (machine.IsAtDestination(0.25f)) machine.StopAgent();
            return;
        }

        Vector3 target = targetTransform.position;
        bool sampled = machine.TrySampleApproachPoint(target, out Vector3 point, out bool reachable);

        // 대치 해제 — 타겟이 메시 위로 돌아왔거나, 최근접 지점이 히스테리시스 거리 이상 옮겨 한 번 따라갈 가치가 생겼다.
        if (holding)
        {
            Vector3 anchorShift = point - holdAnchor;
            anchorShift.y = 0f;
            float repath = controller.Profile.ChaseHoldRepathMeters;
            if (reachable || anchorShift.sqrMagnitude >= repath * repath)
            {
                ReleaseHold(controller, reachable ? "타겟 도달 가능" : $"최근접 지점 이동 {anchorShift.magnitude:0.00}m");
            }
            else
            {
                // 대치 유지 — 재경로 없음. 얼굴은 UpdateFacing 정지 분기가 타겟을 향한다.
                return;
            }
        }

        // 접근 — 도달 가능 여부와 무관하게 최근접 지점까지는 간다.
        machine.SetTargetSpeed(controller.Profile.ChaseSpeed);
        machine.MoveToTarget();

        if (!reachable)
        {
            // 대치 진입 — 최근접 지점 도착(샘플 성공) 또는 6m 밖이라 샘플 실패로 걸 경로가 없음(남은 옛 경로가 있으면 그 끝까지는 간다).
            bool atDestination = machine.IsAtDestination(ZombieStateMachine.DestinationReachToleranceMeters);
            bool arrived = sampled ? atDestination : !machine.HasPath || atDestination;
            if (arrived)
            {
                machine.StopAgent();
                holding = true;
                holdAnchor = point;
                controller.ServerSetChaseHold(true);
                Log.D($"[ZombieChaseState] {controller.NetworkObjectId} {controller.Profile.Species} 대치 진입 · 샘플 {(sampled ? "성공" : "실패")} — 지점 {point} · 타겟 {target}");
            }
            return;
        }

        // 도달 가능 끝점 정지 — 문턱처럼 닿을 수 있는데 사거리 밖인 케이스. 정지 없이 두면 매 프레임 밀고 나가려 해 몸이 떤다.
        if (machine.IsAtDestination(0.25f))
        {
            machine.StopAgent();
        }
    }

    /// <summary>추격을 떠난다 — 회피 우선순위와 대치 플래그를 되돌린다. 리셋하지 않으면 배회·조사 중에도 추격 값을 물고 다닌다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Exit(ZombieStateMachine machine)
    {
        machine.ResetAvoidancePriority();
        machine.Controller.ServerSetChaseHold(false);
    }

    /// <summary>대치를 푼다 — 플래그를 내리고 에지 로그를 남긴다. 대치 중이 아니면 아무것도 하지 않는다.</summary>
    /// <param name="controller">소속 컨트롤러.</param>
    /// <param name="reason">해제 사유(로그용).</param>
    private void ReleaseHold(ZombieController controller, string reason)
    {
        if (!holding) return;
        holding = false;
        controller.ServerSetChaseHold(false);
        Log.D($"[ZombieChaseState] {controller.NetworkObjectId} {controller.Profile.Species} 대치 해제 — {reason}");
    }
}

public class ZombieAttackState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Attack;
    private bool caught;

    /// <summary>
    /// 선딜을 시작한다. 세우지 않는다 — 공격 클립은 상체 레이어 원샷이라 다리는 Locomotion 을 타고 계속 쫓는다.
    /// 진입에서 세우면 걷기만 하는 플레이어도 선딜 0.3초 안에 사거리를 벗어나 영원히 헛손질이 된다.
    /// </summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        machine.SetTargetSpeed(ResolveAttackSpeed(machine.Controller.Profile));
        machine.Controller.BeginAttackTimer();
        caught = false;
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        controller.AdvanceAttackTimer(deltaTime);

        if (!caught)
        {
            // 타격 전에 대상을 잃으면(사망·위장·디스폰) 조사로 내려간다.
            if (controller.CurrentTarget == null)
            {
                machine.SwitchState(ZombieStateKind.Investigate);
                return;
            }

            // 선딜 동안에도 따라붙는다 — 추격보다 느린 배율로(물러 드는 동작). 명중 판정만 선딜 끝에 한다.
            machine.SetTargetSpeed(ResolveAttackSpeed(controller.Profile));
            machine.MoveToTarget();

            if (controller.AttackTimer >= controller.Profile.AttackWindupSeconds)
            {
                // 명중 판정은 선딜을 다 채운 순간에 한다 — 그때 사거리 밖이면 「헛손질」로 추격 복귀.
                // 이탈 즉시 전이하면 사거리 경계에서 Attack↔Chase 가 프레임 단위로 튀어 정지/이동이
                // 매 프레임 뒤집히는 버벅임이 된다. 선딜(0.3s)이 최소 사이클을 보장하고,
                // 선딜 중 걸어 나가면 빗나가는 회피 여지(attack_windup의 존재 이유)도 그대로다.
                if (Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position) > controller.Profile.AttackRange)
                {
                    machine.SwitchState(ZombieStateKind.Chase);
                    return;
                }
                // 붙잡음 — 여기서야 선다. 타격 락은 물고 있는 연출이라 발이 움직이면 안 된다.
                caught = true;
                machine.StopAgent();
                machine.NotifyPlayerCaught();
                controller.PublishAudioEvent(ZombieAudioEventKind.AttackConfirmed);

                // 타격 후에도 타겟·latch 를 락 끝까지 문다. 여기서 놓으면 락 동안 의심도가 식고, 서서 옛 위치만 보다가
                // 대상이 시야 밖으로 빠지면 재획득에 실패해 락이 끝나자마자 조사로 떨어진다(공격 한 번에 타겟 상실).
                // 도망칠 틈은 락의 정지가 준다. 대상이 사망·탈출하면 ZombieController.ServerValidateTarget 이 다음 프레임에 놓는다.
            }
            return;
        }

        // 타격 락은 연출 고정 구간이라 타겟이 사라져도 끝까지 채운다.
        if (controller.AttackTimer < controller.Profile.AttackLockSeconds) return;

        machine.SwitchState(controller.CurrentTarget != null
            ? ZombieStateKind.Chase
            : ZombieStateKind.Investigate);
    }

    public void Exit(ZombieStateMachine machine) { }

    /// <summary>선딜 중 이동 속도 — 추격 속도에 프로파일 배율을 곱한다. 매 Tick 다시 읽어 플레이 중 튜닝이 즉시 반영된다.</summary>
    /// <param name="profile">종 프로파일.</param>
    /// <returns>선딜 이동 속도(m/s).</returns>
    private static float ResolveAttackSpeed(ZombieProfileSO profile)
    {
        return profile.ChaseSpeed * profile.AttackSpeedMultiplier;
    }
}

public class ZombieSubsideState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Subside;

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetTargetSpeed(machine.Controller.Profile.SubsideSpeed);
        machine.MoveToHome();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        machine.SetTargetSpeed(controller.Profile.SubsideSpeed);
        machine.MoveToHome();

        if (machine.IsAtDestination(1f) && controller.SuspicionValue < controller.Profile.ThAlert)
        {
            machine.SwitchState(ZombieStateKind.Wander);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}
