using Border.Core;
using EmptyHouse.Zombie.Rules;
using UnityEngine;

/// <summary>
/// Teacher 경고 — 발각 전환(RoarTransition) 자리의 종 전용 상태. 제자리에 서서 경고하고(포효 레이어·포효 음성은
/// 복제된 RoarTransition 을 보고 클라이언트가 재생한다), 매 틱 TeacherWarningRules 로 판정한다.
/// 계속 달리면 → Chase. 달리기를 멈추거나 시야를 잃으면(각 유예 뒤) → 타겟·래치 해제 후 Wander.
/// 진입 시 래치는 코어 SwitchState 훅이 켠다(Teacher 는 SyncEnabled=false 라 무리는 묶이지 않는다).
/// </summary>
public class TeacherWarningState : IZombieState
{
    private TeacherWarningClock clock; // 경고 시계 — 상태 객체가 전이마다 새로 만들어지므로 진입마다 0 에서 출발

    public ZombieStateKind Kind => ZombieStateKind.RoarTransition;

    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAlertTimer();
        clock = default;
        // 플레이 테스트 분석용 — Editor.log 에는 줄별 시각이 없어 게임 시각(t)을 함께 남긴다
        Log.D($"[TeacherWarningState] 경고 시작 t={Time.time:0.00} — id {machine.Controller.NetworkObjectId}");
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieControllerTeacher controller = (ZombieControllerTeacher)machine.Controller;
        controller.AdvanceAlertTimer(deltaTime);

        TeacherWarningOutcome outcome = TeacherWarningRules.Step(
            ref clock,
            controller.TeacherProfile.ToWarningSettings(),
            deltaTime,
            controller.CurrentTargetSource != null,
            controller.ServerIsTargetSprinting,
            controller.HasTargetStimulus);

        switch (outcome)
        {
            case TeacherWarningOutcome.Continue:
                return;
            case TeacherWarningOutcome.BeginChase:
                Log.D($"[TeacherWarningState] 경고 무시 {clock.Elapsed:0.00}s — 추격 t={Time.time:0.00} · 달리기 중단 {clock.NotSprintingSeconds:0.00}s · 시야 상실 {clock.LostSightSeconds:0.00}s. id {controller.NetworkObjectId}");
                machine.SwitchState(ZombieStateKind.Chase);
                return;
            default:
                Log.D($"[TeacherWarningState] 경고 취소 {outcome} — 경과 {clock.Elapsed:0.00}s · t={Time.time:0.00} · 달리기 중단 {clock.NotSprintingSeconds:0.00}s · 시야 상실 {clock.LostSightSeconds:0.00}s. id {controller.NetworkObjectId}");
                controller.ServerCancelWarning(outcome);
                machine.SwitchState(ZombieStateKind.Wander);
                return;
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}
