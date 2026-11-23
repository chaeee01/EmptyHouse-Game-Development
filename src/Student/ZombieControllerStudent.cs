using Border.Core;
using EmptyHouse.NoiseSystem;
using EmptyHouse.Zombie.Rules;
using UnityEngine;

/// <summary>
/// Student 컨트롤러 — 공통 AI(감지 수집·의심도·상태 머신·배회·진정·귀가)와 기존 소음 전파를 그대로 쓰고 훅 두 개를 얹는다.
/// 훅② 발견: 시야에 플레이어가 들어오면 즉시 발각(바닥 100 + 타겟 — Oiler 선례). 비명 쿨다운 중이면 발각 전환 없이 곧바로 도주한다.
/// 훅③ 상태 교체: 발각 전환(RoarTransition) → 비명 예고, 도주(Flee) → Student 도주. 비명은 서버가 소음 이벤트로 1회 발행하고,
///   주변 좀비는 기존 청각 파이프라인으로 그 위치를 조사한다(소음원이 플레이어가 아니라 타겟은 잡지 않는다).
/// Oiler 를 상속하지 않는다 — 도주 알고리즘만 참고해 StudentFleeState 에 독립 구현했다.
/// </summary>
public class ZombieControllerStudent : ZombieController
{
    [SerializeField] private NoiseEmittedEventChannelSO noiseEmittedChannel; // 서버 소음 파이프라인(SO_Event_NoiseEmitted) — 비명 발행

    private double lastScreamAt = double.NegativeInfinity; // 마지막 비명 시각(서버 Time.timeAsDouble). 복제하지 않는다

    public StudentProfileSO StudentProfile => (StudentProfileSO)Profile; // 비명·도주 수치 — 프리팹 계약상 항상 StudentProfileSO

    /// <summary>서버가 이 개체로 발행한 비명 수(검증·로그용).</summary>
    public int ServerScreamCount { get; private set; }

    /// <summary>비명 쿨다운 중인가(서버).</summary>
    public bool ServerIsScreamOnCooldown => StudentScreamRules.IsOnCooldown(Time.timeAsDouble, lastScreamAt, StudentProfile.ScreamCooldownSeconds);

    /// <summary>예고가 취소돼 배회로 돌아가면 예고(포효) 연출을 즉시 끊는다. 예고 → 도주는 종전대로 이어 재생된다.</summary>
    public override bool InterruptsRoarOnCancel => true;

    /// <summary>시야 발견을 즉시 발각으로 올린다. 쿨다운 중이면 비명 없이 곧바로 도주시킨다.</summary>
    /// <param name="builder">이번 프레임의 지각 조립체.</param>
    protected override void ServerContributePerception(ZombiePerceptionBuilder builder)
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다(ZombieAnimator 선례).
        // 시각 수집이 위장·관전·차폐·거리·원뿔을 이미 걸렀다.
        if (!builder.HasVisualStimulus) return;

        builder.DirectSuspicionFloor = 100f;
        builder.DirectTarget = builder.VisualTarget;
        builder.DirectPosition = builder.VisualPosition;

        // 쿨다운 중 발견 — 발각 전환(예고 = 포효 연출)을 거치지 않고 바로 도주한다. 도주는 지각 전이 예외라 다음 틱에 예고로 끌려가지 않는다.
        if (IsCalmState(CurrentState) && ServerIsScreamOnCooldown)
        {
            Log.D($"[ZombieControllerStudent] 쿨다운 중 발견 — 비명 없이 도주 t={Time.time:0.00} · 남은 쿨다운 {StudentProfile.ScreamCooldownSeconds - (Time.timeAsDouble - lastScreamAt):0.0}s. id {NetworkObjectId}");
            StateMachine.SwitchState(ZombieStateKind.Flee);
        }
    }

    /// <summary>발각 전환을 비명 예고로, 도주를 Student 도주로 교체한다.</summary>
    /// <param name="kind">머신이 만들려는 상태.</param>
    /// <returns>교체 상태 — 해당 없으면 null.</returns>
    public override IZombieState TryCreateSpeciesState(ZombieStateKind kind)
    {
        switch (kind)
        {
            case ZombieStateKind.RoarTransition:
                return new StudentScreamWindupState();
            case ZombieStateKind.Flee:
                return new StudentFleeState();
            default:
                return null;
        }
    }

    /// <summary>
    /// 비명 소음을 서버 소음 파이프라인에 1회 발행한다(단발 채널). 쿨다운을 시작한다.
    /// 전파·감쇠·차폐·각 좀비의 가청 판정은 기존 NoisePropagationSystem 이 한다.
    /// </summary>
    /// <returns>발행했으면 true.</returns>
    public bool ServerEmitScream()
    {
        if (!IsServer) return false;
        if (noiseEmittedChannel == null)
        {
            Debug.LogError($"[ZombieControllerStudent] noiseEmittedChannel 미배선 — 비명 소음을 낼 수 없다. id {NetworkObjectId}", this);
            return false;
        }

        Vector3 origin = VisionOrigin.position;
        noiseEmittedChannel.RaiseEvent(new NoiseEmittedEvent(NetworkObjectId, origin, StudentProfile.ScreamNoiseDb, NoiseSourceChannel.OneShot));
        lastScreamAt = Time.timeAsDouble;
        ServerScreamCount++;
        Log.D($"[ZombieControllerStudent] 비명 발행 #{ServerScreamCount} — {StudentProfile.ScreamNoiseDb:0}dB · 위치 {origin} · t={Time.time:0.00} · 쿨다운 {StudentProfile.ScreamCooldownSeconds:0}s. id {NetworkObjectId}");
        return true;
    }

    /// <summary>비명 예고를 취소한다 — 타겟·래치·의심도를 비운다. 상태 전이는 호출자가 한다.</summary>
    /// <param name="reason">취소 사유(로그용).</param>
    public void ServerCancelScream(StudentWindupOutcome reason)
    {
        if (!IsServer) return;
        Log.D($"[ZombieControllerStudent] 비명 취소 — id {NetworkObjectId} · 사유 {reason}");
        ServerCalmDown();
    }

    /// <summary>도주를 끝내며 위협을 잊는다 — 타겟·래치·의심도를 비운다.</summary>
    public void ServerForgetThreat()
    {
        if (!IsServer) return;
        Log.D($"[ZombieControllerStudent] 위협 망각(도주 종료) — id {NetworkObjectId}");
        ServerCalmDown();
    }

    /// <summary>발각 전 평상 상태인가 — 쿨다운 중 즉시 도주를 걸 수 있는 상태.</summary>
    private static bool IsCalmState(ZombieStateKind kind)
    {
        return kind == ZombieStateKind.Wander || kind == ZombieStateKind.Alert
            || kind == ZombieStateKind.Investigate || kind == ZombieStateKind.Subside;
    }
}
