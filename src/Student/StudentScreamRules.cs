using System;

namespace EmptyHouse.Zombie.Rules
{
    /// <summary>비명 예고 한 틱의 판정 결과.</summary>
    public enum StudentWindupOutcome
    {
        Continue, // 예고 유지(또는 이미 비명을 낸 뒤)
        Scream, // 예고 완료 — 비명 소음을 낸다. 한 예고에서 정확히 1회만 나온다
        CancelLostSight, // 시야 상실(유예 경과) — 비명 취소
        CancelNoTarget, // 타겟 소실 — 비명 취소
        CancelIncapacitated // 경직·기절 — 비명 취소
    }

    /// <summary>비명 수치 — StudentProfileSO 값을 옮겨 담는다.</summary>
    public readonly struct StudentScreamSettings
    {
        public readonly float WindupSeconds; // 비명 예고 시간(초)
        public readonly float LostSightGraceSeconds; // 예고 중 시야 상실 허용 시간(초). 넘으면 취소
        public readonly float CooldownSeconds; // 비명 재사용 대기(초)

        public StudentScreamSettings(float windupSeconds, float lostSightGraceSeconds, float cooldownSeconds)
        {
            WindupSeconds = windupSeconds < 0f ? 0f : windupSeconds;
            LostSightGraceSeconds = lostSightGraceSeconds < 0f ? 0f : lostSightGraceSeconds;
            CooldownSeconds = cooldownSeconds < 0f ? 0f : cooldownSeconds;
        }
    }

    /// <summary>예고 진행 시계 — 예고 상태가 하나씩 소유한다(서버 메모리 전용).</summary>
    public struct StudentWindupClock
    {
        public float Elapsed; // 예고 경과(초)
        public float LostSightSeconds; // 연속 시야 상실(초)
        public bool Screamed; // 이 예고에서 이미 비명을 냈는가 — 1회 보장
    }

    /// <summary>도주 후보 방향(수평 단위 벡터)과 「위협 반대 방향」과의 정렬도(코사인).</summary>
    public readonly struct FleeCandidate
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Alignment;

        public FleeCandidate(float x, float z, float alignment)
        {
            X = x;
            Z = z;
            Alignment = alignment;
        }
    }

    /// <summary>
    /// 학생 좀비 규칙 — 비명 예고·취소·1회 발행·쿨다운·도주 방향 판정. Unity 비의존 순수 규칙이라 EditMode 로 고정한다.
    /// 상태 객체(StudentScreamWindupState·StudentFleeState)는 판정 결과를 전이·이동으로 옮기기만 한다.
    /// </summary>
    public static class StudentScreamRules
    {
        /// <summary>
        /// 예고 한 틱을 진행한다. 우선순위: 이미 비명 → 경직·기절 → 타겟 소실 → 시야 상실(유예) → 예고 완료(비명).
        /// 비명은 한 시계에서 단 한 번만 반환된다 — 이후 호출은 Continue 다.
        /// </summary>
        public static StudentWindupOutcome StepWindup(ref StudentWindupClock clock, in StudentScreamSettings settings,
            float deltaTime, bool hasTarget, bool hasSight, bool incapacitated)
        {
            if (clock.Screamed) return StudentWindupOutcome.Continue;
            if (incapacitated) return StudentWindupOutcome.CancelIncapacitated;
            if (!hasTarget) return StudentWindupOutcome.CancelNoTarget;

            float dt = deltaTime > 0f ? deltaTime : 0f;
            clock.Elapsed += dt;
            clock.LostSightSeconds = hasSight ? 0f : clock.LostSightSeconds + dt;

            if (!hasSight && clock.LostSightSeconds >= settings.LostSightGraceSeconds) return StudentWindupOutcome.CancelLostSight;
            if (clock.Elapsed < settings.WindupSeconds) return StudentWindupOutcome.Continue;

            clock.Screamed = true;
            return StudentWindupOutcome.Scream;
        }

        /// <summary>비명 쿨다운 중인가. 아직 비명을 낸 적이 없으면(lastScreamAt = 음의 무한대) false.</summary>
        /// <param name="now">현재 시각(초).</param>
        /// <param name="lastScreamAt">마지막 비명 시각(초).</param>
        /// <param name="cooldownSeconds">쿨다운(초).</param>
        public static bool IsOnCooldown(double now, double lastScreamAt, float cooldownSeconds)
        {
            if (double.IsNegativeInfinity(lastScreamAt)) return false;
            return now - lastScreamAt < cooldownSeconds;
        }

        /// <summary>
        /// 도주 후보 방향을 만든다 — 위협 반대 방향을 중심으로 360° 를 count 등분하고, 정렬도가 하한 미만(위협 쪽)인 방향은 버린다.
        /// 반대 방향이 영벡터면(위협이 발밑) 후보를 만들지 않는다 — 호출자가 정면을 대신 쓴다.
        /// </summary>
        /// <returns>buffer 에 채운 후보 수.</returns>
        public static int BuildFleeCandidates(float awayX, float awayZ, int count, float minAlignment, FleeCandidate[] buffer)
        {
            float length = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
            if (length < 0.0001f || buffer == null) return 0;
            awayX /= length;
            awayZ /= length;

            int directions = Math.Max(1, count);
            int written = 0;
            for (int i = 0; i < directions && written < buffer.Length; i++)
            {
                // Unity 의 Quaternion.Euler(0, θ, 0) * v 와 같은 Y축 회전
                double theta = 2.0 * Math.PI * i / directions;
                float cos = (float)Math.Cos(theta);
                float sin = (float)Math.Sin(theta);
                float x = awayX * cos + awayZ * sin;
                float z = -awayX * sin + awayZ * cos;
                float alignment = x * awayX + z * awayZ;
                if (alignment < minAlignment) continue;
                buffer[written++] = new FleeCandidate(x, z, alignment);
            }
            return written;
        }

        /// <summary>도주 지점 점수 — 위협에서 멀어진 거리 + 반대 방향 정렬 가산. 벽에 막혀 짧게 스냅된 후보보다 멀리 트인 후보가 이긴다.</summary>
        public static float ScoreFleePoint(float distanceFromThreat, float alignment, float stepMeters)
        {
            return distanceFromThreat + alignment * stepMeters * 0.5f;
        }

        /// <summary>도주 포기 판정 — 위협(타겟)을 이 시간 동안 보지 못하면 포기하고 진정한다. 보면 누적이 0 으로 돌아간다.</summary>
        /// <returns>포기해야 하면 true.</returns>
        public static bool StepFleeGiveUp(ref float lostSeconds, float deltaTime, bool hasSight, float giveUpSeconds)
        {
            lostSeconds = hasSight ? 0f : lostSeconds + (deltaTime > 0f ? deltaTime : 0f);
            return lostSeconds >= giveUpSeconds;
        }
    }
}
