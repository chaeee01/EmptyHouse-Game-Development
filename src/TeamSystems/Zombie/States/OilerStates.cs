using System.Collections.Generic;
using Border.Core;
using UnityEngine;

/// <summary>
/// Oiler 놀람 — 발각 전환의 Oiler 구현. RoarHoldSeconds 동안 제자리에 굳었다가 추격이 아니라 도주로 넘어간다.
/// 포효 연출(상체 원샷·비명 SE)은 공통 그대로 — 겁에 질린 비명으로 읽힌다. 전용 놀람 클립은 아트 후속.
/// </summary>
public class OilerStartleState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.RoarTransition;

    /// <summary>제자리에 서서 놀람 타이머를 시작한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAlertTimer();
    }

    /// <summary>타이머가 차면 도주로 넘어간다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        controller.AdvanceAlertTimer(deltaTime);
        if (controller.AlertTimer >= controller.Profile.RoarHoldSeconds)
        {
            machine.SwitchState(ZombieStateKind.Flee);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}

/// <summary>
/// Oiler 도주 — 위협(물고 있는 타겟 · 없으면 시야 거리 안 최근접 플레이어)의 반대편으로 FleeSpeed 로 달린다.
/// 목적지는 위협 반대 방향을 중심으로 FleeStepMeters 거리의 NavMesh 지점을 방향별로 샘플해 위협에서 가장 멀어지는 곳을 고르고,
/// 도착·FleeRepathSeconds 마다 다시 고른다. 갈 곳이 없으면(구석) 선다.
/// 타겟 자극(시야)을 FleeGiveUpSeconds 동안 잃으면 위협을 잊고(의심도 0) 진정 → 귀가 → 배회.
/// 기절 해제 직후에는 타겟이 없어 최근접 플레이어(운반자)가 위협이고, 상실 타이머가 곧바로 돌아 최소 FleeGiveUpSeconds 는 도망친다.
/// </summary>
public class OilerFleeState : IZombieState
{
    private const float arriveTolerance = 0.5f; // 도주 지점 도착 판정(m) — 정확히 서지 않고 근처에서 다음 지점으로 꺾는다
    private const float minAwayAlignment = -0.2f; // 후보 방향과 「위협 반대 방향」의 코사인 하한 — 위협 쪽으로 가는 후보는 버린다(옆까지 허용)
    private const float sampleSnapMeters = 2f; // 후보 지점 NavMesh 스냅 거리(m)

    private float lostTimer; // 타겟 자극을 잃은 누적 시간(초)
    private float repathTimer; // 마지막 지점 선정 후 경과 시간(초)
    private bool hasDestination; // 도주 지점으로 이동 중인가

    public ZombieStateKind Kind => ZombieStateKind.Flee;

    /// <summary>도주 속도로 맞추고 첫 지점 선정을 예약한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        ZombieController controller = machine.Controller;
        OilerProfileSO profile = (OilerProfileSO)controller.Profile;
        Log.D($"[OilerFleeState] 도주 시작 — id {controller.NetworkObjectId} · 타겟 {(controller.CurrentTarget != null ? controller.CurrentTarget.name : "None")}");

        machine.SetTargetSpeed(profile.FleeSpeed);
        lostTimer = 0f;
        repathTimer = profile.FleeRepathSeconds; // 첫 Tick 에 바로 지점을 고른다
        hasDestination = false;
    }

    /// <summary>상실 판정 → 위협 위치 해석 → 도주 지점 갱신 순으로 진행한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieControllerOiler controller = (ZombieControllerOiler)machine.Controller;
        OilerProfileSO profile = (OilerProfileSO)controller.Profile;

        // 속도는 매 Tick 다시 건다 — 플레이 중 FleeSpeed 튜닝 즉시 반영(배회·추격 선례).
        machine.SetTargetSpeed(profile.FleeSpeed);

        // 상실 판정은 추격과 같은 기준 — 타겟 본인을 이번 프레임 봤는가. 타겟이 없으면(기절 해제 직후) 계속 누적된다.
        if (controller.HasTargetStimulus) lostTimer = 0f;
        else lostTimer += deltaTime;

        if (lostTimer >= profile.FleeGiveUpSeconds)
        {
            Log.D($"[OilerFleeState] 위협 상실 {lostTimer:0.0}s — 진정 → 귀가. id {controller.NetworkObjectId}");
            controller.ServerForgetThreat();
            machine.SwitchState(ZombieStateKind.Subside);
            return;
        }

        if (!TryResolveThreatPosition(machine, controller, profile, out Vector3 threat))
        {
            // 위협을 특정할 수 없다(플레이어 전원 관전 등) — 서서 상실 타이머만 돌린다.
            machine.StopAgent();
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

        // 막다른 곳 — 위협 반대편 어디에도 갈 수 있는 지점이 없다. 선 채로 상실 타이머만 돌린다(구석 몰림은 플레이어의 이득).
        machine.StopAgent();
        hasDestination = false;
    }

    public void Exit(ZombieStateMachine machine) { }

    /// <summary>위협 위치를 정한다 — 물고 있는 타겟이 우선, 없으면 시야 거리 안 최근접 플레이어(관전 제외).</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="controller">좀비 컨트롤러.</param>
    /// <param name="profile">종 프로파일.</param>
    /// <param name="threat">위협의 월드 위치.</param>
    /// <returns>위협을 특정했으면 true.</returns>
    private static bool TryResolveThreatPosition(ZombieStateMachine machine, ZombieController controller, OilerProfileSO profile, out Vector3 threat)
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

    /// <summary>
    /// 위협 반대 방향을 중심으로 FleeSampleDirections 방향을 돌며 FleeStepMeters 거리의 도달 가능 지점을 샘플하고,
    /// 위협에서 가장 멀어지는(거리 + 정렬 가산) 후보를 고른다. 위협 쪽으로 가는 후보는 버린다.
    /// </summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="profile">종 프로파일.</param>
    /// <param name="threat">위협 위치.</param>
    /// <param name="point">고른 지점.</param>
    /// <returns>지점을 찾았으면 true.</returns>
    private static bool TryFindFleePoint(ZombieStateMachine machine, OilerProfileSO profile, Vector3 threat, out Vector3 point)
    {
        Vector3 origin = machine.transform.position;
        Vector3 away = origin - threat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = machine.transform.forward; // 위협이 발밑 — 정면을 반대 방향으로 삼는다
        away.Normalize();

        point = origin;
        float bestScore = float.NegativeInfinity;
        int directions = Mathf.Max(1, profile.FleeSampleDirections);
        for (int i = 0; i < directions; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, 360f * i / directions, 0f) * away;
            float alignment = Vector3.Dot(direction, away);
            if (alignment < minAwayAlignment) continue;

            Vector3 candidate = origin + direction * profile.FleeStepMeters;
            if (!machine.TrySnapReachablePoint(candidate, sampleSnapMeters, out Vector3 reachable)) continue;

            // 위협에서 멀어지는 거리 + 반대 방향 정렬 가산 — 벽에 막혀 짧게 스냅된 후보보다 멀리 트인 후보가 이긴다.
            Vector3 fromThreat = reachable - threat;
            fromThreat.y = 0f;
            float score = fromThreat.magnitude + alignment * profile.FleeStepMeters * 0.5f;
            if (score <= bestScore) continue;

            bestScore = score;
            point = reachable;
        }

        return bestScore > float.NegativeInfinity;
    }
}
