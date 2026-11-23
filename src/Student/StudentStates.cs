using System.Collections.Generic;
using Border.Core;
using EmptyHouse.Zombie.Rules;
using UnityEngine;

/// <summary>
/// Student 비명 예고 — 발각 전환(RoarTransition) 자리의 종 전용 상태. 제자리에 서서 예고하고(포효 레이어·음성은 복제된
/// RoarTransition 을 보고 클라이언트가 재생한다), 매 틱 StudentScreamRules 로 판정한다.
/// 예고 완료 → 비명 소음 1회 → 도주. 시야 상실(유예 뒤)·타겟 소실 → 취소 후 배회.
/// 경직·기절은 코어가 전투 중단으로 이 상태의 Tick 을 멈추고 복귀 시 다른 상태로 전이하므로 비명이 나지 않는다(규칙에도 명시).
/// </summary>
public class StudentScreamWindupState : IZombieState
{
    private StudentWindupClock clock; // 예고 시계 — 진입마다 새 상태 객체라 0 에서 출발

    public ZombieStateKind Kind => ZombieStateKind.RoarTransition;

    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAlertTimer();
        clock = default;
        Log.D($"[StudentScreamWindupState] 비명 예고 시작 t={Time.time:0.00} — id {machine.Controller.NetworkObjectId}");
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieControllerStudent controller = (ZombieControllerStudent)machine.Controller;
        controller.AdvanceAlertTimer(deltaTime);

        StudentWindupOutcome outcome = StudentScreamRules.StepWindup(
            ref clock,
            controller.StudentProfile.ToScreamSettings(),
            deltaTime,
            controller.CurrentTargetSource != null,
            controller.HasTargetStimulus,
            controller.IsCombatDisabled);

        switch (outcome)
        {
            case StudentWindupOutcome.Continue:
                return;
            case StudentWindupOutcome.Scream:
                Log.D($"[StudentScreamWindupState] 예고 완료 {clock.Elapsed:0.00}s — 비명 t={Time.time:0.00}. id {controller.NetworkObjectId}");
                controller.ServerEmitScream();
                machine.SwitchState(ZombieStateKind.Flee);
                return;
            default:
                Log.D($"[StudentScreamWindupState] 비명 취소 {outcome} — 경과 {clock.Elapsed:0.00}s · 시야 상실 {clock.LostSightSeconds:0.00}s · t={Time.time:0.00}. id {controller.NetworkObjectId}");
                controller.ServerCancelScream(outcome);
                machine.SwitchState(ZombieStateKind.Wander);
                return;
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}

/// <summary>
/// Student 도주 — 위협(타겟, 없으면 시야 거리 안 최근접 플레이어) 반대편으로 NavMesh 지점을 골라 달아난다.
/// 위협을 FleeGiveUpSeconds 동안 보지 못하면 위협을 잊고 진정(Subside) → 귀가한다.
/// 알고리즘은 Oiler 도주를 참고했으나 상속·공유 없이 독립 구현했다(후보 생성·점수·포기 판정은 StudentScreamRules).
/// </summary>
public class StudentFleeState : IZombieState
{
    private const float arriveTolerance = 0.5f; // 도주 지점 도착 판정(m)
    private const float minAwayAlignment = -0.2f; // 후보 방향과 「위협 반대 방향」의 코사인 하한 — 위협 쪽 후보는 버린다(옆까지 허용)
    private const float sampleSnapMeters = 2f; // 후보 지점 NavMesh 스냅 거리(m)

    private readonly FleeCandidate[] candidates = new FleeCandidate[16]; // 후보 버퍼(최대 방향 수)
    private float lostSeconds; // 위협을 못 본 누적 시간(초)
    private float repathTimer; // 마지막 지점 선정 후 경과(초)
    private bool hasDestination; // 도주 지점으로 이동 중인가

    public ZombieStateKind Kind => ZombieStateKind.Flee;

    public void Enter(ZombieStateMachine machine)
    {
        ZombieControllerStudent controller = (ZombieControllerStudent)machine.Controller;
        machine.SetTargetSpeed(controller.StudentProfile.FleeSpeed);
        lostSeconds = 0f;
        repathTimer = controller.StudentProfile.FleeRepathSeconds; // 첫 Tick 에 바로 지점을 고른다
        hasDestination = false;
        Log.D($"[StudentFleeState] 도주 시작 t={Time.time:0.00} — 타겟 {(controller.CurrentTarget != null ? controller.CurrentTarget.name : "None")}. id {controller.NetworkObjectId}");
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieControllerStudent controller = (ZombieControllerStudent)machine.Controller;
        StudentProfileSO profile = controller.StudentProfile;
        machine.SetTargetSpeed(profile.FleeSpeed); // 플레이 중 튜닝 즉시 반영

        if (StudentScreamRules.StepFleeGiveUp(ref lostSeconds, deltaTime, controller.HasTargetStimulus, profile.FleeGiveUpSeconds))
        {
            Log.D($"[StudentFleeState] 위협 상실 {lostSeconds:0.0}s — 진정 → 귀가 t={Time.time:0.00}. id {controller.NetworkObjectId}");
            controller.ServerForgetThreat();
            machine.SwitchState(ZombieStateKind.Subside);
            return;
        }

        if (!TryResolveThreatPosition(machine, controller, profile, out Vector3 threat))
        {
            machine.StopAgent(); // 위협을 특정할 수 없다(전원 관전 등) — 서서 포기 타이머만 돌린다
            hasDestination = false;
            return;
        }

        repathTimer += deltaTime;
        bool arrived = hasDestination && machine.IsAtDestination(arriveTolerance);
        if (hasDestination && !arrived && repathTimer < profile.FleeRepathSeconds) return;

        repathTimer = 0f;
        if (TryFindFleePoint(machine, profile, threat, out Vector3 point))
        {
            machine.SetDestination(point);
            hasDestination = true;
            return;
        }

        machine.StopAgent(); // 막다른 곳 — 선 채로 포기 타이머만 돈다
        hasDestination = false;
    }

    public void Exit(ZombieStateMachine machine) { }

    private static bool TryResolveThreatPosition(ZombieStateMachine machine, ZombieController controller, StudentProfileSO profile, out Vector3 threat)
    {
        if (controller.CurrentTarget != null)
        {
            threat = controller.CurrentTarget.position;
            return true;
        }

        Vector3 origin = machine.transform.position;
        float nearestSqr = profile.VisionDistance * profile.VisionDistance;
        threat = default;
        bool found = false;
        IReadOnlyList<IZombiePerceptionSource> sources = controller.RuntimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            IZombiePerceptionSource source = sources[i];
            if (source == null || source.Root == null || source.IsSpectator) continue;
            float sqr = (source.Root.position - origin).sqrMagnitude;
            if (sqr >= nearestSqr) continue;
            nearestSqr = sqr;
            threat = source.Root.position;
            found = true;
        }
        return found;
    }

    private bool TryFindFleePoint(ZombieStateMachine machine, StudentProfileSO profile, Vector3 threat, out Vector3 point)
    {
        Vector3 origin = machine.transform.position;
        Vector3 away = origin - threat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = machine.transform.forward; // 위협이 발밑 — 정면을 반대 방향으로 삼는다

        int count = StudentScreamRules.BuildFleeCandidates(away.x, away.z, profile.FleeSampleDirections, minAwayAlignment, candidates);
        point = origin;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector3 direction = new Vector3(candidates[i].X, 0f, candidates[i].Z);
            if (!machine.TrySnapReachablePoint(origin + direction * profile.FleeStepMeters, sampleSnapMeters, out Vector3 reachable)) continue;

            Vector3 fromThreat = reachable - threat;
            fromThreat.y = 0f;
            float score = StudentScreamRules.ScoreFleePoint(fromThreat.magnitude, candidates[i].Alignment, profile.FleeStepMeters);
            if (score <= bestScore) continue;
            bestScore = score;
            point = reachable;
        }
        return bestScore > float.NegativeInfinity;
    }
}
