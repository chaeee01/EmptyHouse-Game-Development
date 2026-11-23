namespace EmptyHouse.Zombie.Rules
{
    /// <summary>경고 한 틱의 판정 결과.</summary>
    public enum TeacherWarningOutcome
    {
        Continue, // 경고 유지
        BeginChase, // 경고를 끝까지 무시하고 달렸다 — 추격 시작
        CancelSprintStopped, // 달리기를 멈췄다(유예 경과) — 경고 취소 후 순찰 복귀
        CancelLostSight, // 시야를 잃었다(유예 경과) — 경고 취소 후 순찰 복귀
        CancelNoTarget // 타겟이 사라졌다(사망·귀환·위장 해제 등) — 즉시 취소
    }

    /// <summary>경고 판정 수치 — TeacherProfileSO 값을 그대로 옮겨 담는다.</summary>
    public readonly struct TeacherWarningSettings
    {
        public readonly float WarningSeconds; // 경고 지속 시간(초). 이 시간 동안 계속 달리면 추격
        public readonly float SprintStopGraceSeconds; // 달리기 중단이 이 시간 이상 이어지면 취소(달리기 키 순간 떼기·복제 지연 흡수)
        public readonly float LostSightGraceSeconds; // 시야 상실이 이 시간 이상 이어지면 취소

        public TeacherWarningSettings(float warningSeconds, float sprintStopGraceSeconds, float lostSightGraceSeconds)
        {
            WarningSeconds = warningSeconds < 0f ? 0f : warningSeconds;
            SprintStopGraceSeconds = sprintStopGraceSeconds < 0f ? 0f : sprintStopGraceSeconds;
            LostSightGraceSeconds = lostSightGraceSeconds < 0f ? 0f : lostSightGraceSeconds;
        }
    }

    /// <summary>경고 진행 시계 — 경고 상태가 한 개씩 소유한다(서버 메모리 전용, 복제하지 않는다).</summary>
    public struct TeacherWarningClock
    {
        public float Elapsed; // 경고 경과 시간(초)
        public float NotSprintingSeconds; // 연속으로 달리지 않은 시간(초)
        public float LostSightSeconds; // 연속으로 시야를 잃은 시간(초)
    }

    /// <summary>
    /// 선생님 좀비 규칙 — Unity·네트워크와 무관한 순수 판정이라 EditMode 테스트로 고정한다.
    /// 상태 객체(TeacherWarningState)는 이 판정 결과를 상태 전이로 옮기기만 한다.
    /// </summary>
    public static class TeacherWarningRules
    {
        /// <summary>
        /// 추격 전 단계의 감지 게이트 — 시야 안 대상이 달리고 있을 때만 감지를 통과시킨다.
        /// 걷기·웅크림·정지·위장은 모두 「달리지 않음」이다(달리기 복제값이 이미 이 조건을 합성한다).
        /// </summary>
        /// <param name="hasVisualTarget">시각 수집이 대상을 찾았는가.</param>
        /// <param name="targetSprinting">그 대상이 달리는 중인가.</param>
        /// <returns>감지를 통과시키면 true.</returns>
        public static bool PassesDetectionGate(bool hasVisualTarget, bool targetSprinting)
        {
            return hasVisualTarget && targetSprinting;
        }

        /// <summary>
        /// 경고 한 틱을 진행한다. 우선순위: 타겟 소실 → 달리기 중단 → 시야 상실 → 경고 만료(추격).
        /// 같은 틱에 취소와 만료가 겹치면 취소가 이긴다 — 마지막 순간에 걸음을 멈춘 플레이어를 쫓지 않는다.
        /// </summary>
        /// <param name="clock">경고 시계(갱신된다).</param>
        /// <param name="settings">판정 수치.</param>
        /// <param name="deltaTime">이번 틱 시간(초). 음수는 0 으로 본다.</param>
        /// <param name="hasTarget">타겟을 쥐고 있는가.</param>
        /// <param name="targetSprinting">타겟이 달리는 중인가.</param>
        /// <param name="hasSight">이번 틱에 타겟 본인을 보았는가.</param>
        /// <returns>이번 틱 판정.</returns>
        public static TeacherWarningOutcome Step(ref TeacherWarningClock clock, in TeacherWarningSettings settings,
            float deltaTime, bool hasTarget, bool targetSprinting, bool hasSight)
        {
            if (!hasTarget) return TeacherWarningOutcome.CancelNoTarget;

            float dt = deltaTime > 0f ? deltaTime : 0f;
            clock.Elapsed += dt;
            clock.NotSprintingSeconds = targetSprinting ? 0f : clock.NotSprintingSeconds + dt;
            clock.LostSightSeconds = hasSight ? 0f : clock.LostSightSeconds + dt;

            if (!targetSprinting && clock.NotSprintingSeconds >= settings.SprintStopGraceSeconds)
                return TeacherWarningOutcome.CancelSprintStopped;
            if (!hasSight && clock.LostSightSeconds >= settings.LostSightGraceSeconds)
                return TeacherWarningOutcome.CancelLostSight;
            if (clock.Elapsed >= settings.WarningSeconds)
                return TeacherWarningOutcome.BeginChase;
            return TeacherWarningOutcome.Continue;
        }
    }
}
