using Border.Core;
using EmptyHouse.Zombie.Rules;

/// <summary>
/// Teacher 컨트롤러 — 공통 AI(감지 수집·의심도·상태 머신·추격·관성 추격·복귀)를 그대로 쓰고, 훅 두 개만 얹는다.
/// 훅② 감지 게이트: 추격 전 단계(배회·경계·조사·진정)에서는 시야 안 대상이 달릴 때만 감지한다(바닥 100 + 타겟 — Oiler 선례).
///   달리지 않으면 시각 자극과 근접 반응을 지운다. 경고·추격·관성 추격·공격 중에는 손대지 않는다 —
///   추격이 시작된 뒤 걷기로 바뀌어도 기존 Chase·ChaseBlind 규칙으로만 놓친다.
/// 훅③ 경고: 발각 전환(RoarTransition)을 TeacherWarningState 로 교체한다. 판정은 TeacherWarningRules(순수 규칙)에 있다.
/// </summary>
public class ZombieControllerTeacher : ZombieController
{
    public TeacherProfileSO TeacherProfile => (TeacherProfileSO)Profile; // 경고 수치 — 프리팹 계약상 항상 TeacherProfileSO

    /// <summary>경고가 취소돼 배회로 돌아가면 포효(경고) 연출을 즉시 끊는다. 경고 → 추격은 종전대로 이어 재생된다.</summary>
    public override bool InterruptsRoarOnCancel => true;

    /// <summary>쥐고 있는 타겟이 지금 달리는 중인가(서버).</summary>
    public bool ServerIsTargetSprinting => CurrentTargetSource != null && CurrentTargetSource.IsSprinting;

    /// <summary>추격 전 단계에서 「시야 안 달리기」만 감지로 통과시킨다.</summary>
    /// <param name="builder">이번 프레임의 지각 조립체.</param>
    protected override void ServerContributePerception(ZombiePerceptionBuilder builder)
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다(ZombieAnimator 선례).
        if (!IsDetectionGated(CurrentState)) return;

        // 시각 수집이 위장·관전·차폐·거리·원뿔을 이미 걸렀다. 대상은 원뿔 안 최근접 1명이다(다인 판정 한계 — 문서화).
        IZombiePerceptionSource target = builder.VisualTarget;
        if (TeacherWarningRules.PassesDetectionGate(target != null, target != null && target.IsSprinting))
        {
            builder.DirectSuspicionFloor = 100f;
            builder.DirectTarget = target;
            builder.DirectPosition = builder.VisualPosition;
            return;
        }

        // 달리지 않는 대상은 보지 못한 것으로 친다 — 시각 가산·즉시 발각·원뿔 밖 근접 반응(바닥 ThInvestigate)까지 지운다.
        builder.HasVisualStimulus = false;
        builder.HasInstantVisualDetection = false;
        builder.VisualGainPerSecond = 0f;
        builder.VisualTarget = null;
        builder.VisualPosition = default;
        builder.DirectSuspicionFloor = 0f;
        builder.DirectTarget = null;
        builder.DirectPosition = default;
    }

    /// <summary>발각 전환을 경고로 교체한다. 나머지 상태는 공통 구현을 쓴다.</summary>
    /// <param name="kind">머신이 만들려는 상태.</param>
    /// <returns>교체 상태 — 해당 없으면 null.</returns>
    public override IZombieState TryCreateSpeciesState(ZombieStateKind kind)
    {
        if (kind != ZombieStateKind.RoarTransition) return null;
        Log.D($"[ZombieControllerTeacher] TeacherWarningState 진입 — id {NetworkObjectId}");
        return new TeacherWarningState();
    }

    /// <summary>
    /// 경고를 취소한다 — 타겟·래치·의심도를 비운다(ServerCalmDown). 래치가 풀리고 타겟 id 가 0 이 되면
    /// 클라이언트의 위협 연출(ZombieThreatAudioDirector)도 내려간다. 상태 전이는 호출자(경고 상태)가 한다.
    /// </summary>
    /// <param name="reason">취소 사유(로그용).</param>
    public void ServerCancelWarning(TeacherWarningOutcome reason)
    {
        if (!IsServer) return;
        Log.D($"[ZombieControllerTeacher] 경고 취소 — id {NetworkObjectId} · 사유 {reason}");
        ServerCalmDown();
    }

    /// <summary>감지 게이트를 적용할 상태인가 — 추격 전 단계만.</summary>
    private static bool IsDetectionGated(ZombieStateKind kind)
    {
        return kind == ZombieStateKind.Wander || kind == ZombieStateKind.Alert
            || kind == ZombieStateKind.Investigate || kind == ZombieStateKind.Subside;
    }
}
