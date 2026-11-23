using NUnit.Framework;

namespace EmptyHouse.Zombie.Rules.Tests
{
    /// <summary>
    /// 학생 좀비 비명 예고·취소·1회 발행·쿨다운·도주 판정 기대값 테스트. 수치는 StudentProfileSO 기본값과 같다.
    /// 틱은 0.125초(2진수로 정확) — 경계값이 부동소수 오차에 흔들리지 않게 한다.
    /// </summary>
    public sealed class StudentScreamRulesTests
    {
        private const float tick = 0.125f;
        private static readonly StudentScreamSettings settings = new StudentScreamSettings(1.5f, 0.25f, 20f); // 예고 12틱 · 시야 상실 유예 2틱

        private static StudentWindupOutcome Once(ref StudentWindupClock clock, bool sight = true, bool target = true, bool incapacitated = false)
            => StudentScreamRules.StepWindup(ref clock, settings, tick, target, sight, incapacitated);

        private static StudentWindupOutcome Run(ref StudentWindupClock clock, int ticks, bool sight = true)
        {
            StudentWindupOutcome outcome = StudentWindupOutcome.Continue;
            for (int i = 0; i < ticks; i++)
            {
                outcome = Once(ref clock, sight);
                if (outcome != StudentWindupOutcome.Continue) break;
            }
            return outcome;
        }

        [Test]
        public void 예고_시간이_끝나면_비명을_낸다()
        {
            var clock = new StudentWindupClock();
            Assert.That(Run(ref clock, 11), Is.EqualTo(StudentWindupOutcome.Continue), "1.375초 — 예고 중");
            Assert.That(Once(ref clock), Is.EqualTo(StudentWindupOutcome.Scream), "1.5초 — 비명");
        }

        [Test]
        public void 비명은_한_예고에서_정확히_1회만_발행된다()
        {
            var clock = new StudentWindupClock();
            int screams = 0;
            for (int i = 0; i < 100; i++)
            {
                if (Once(ref clock) == StudentWindupOutcome.Scream) screams++;
            }
            Assert.That(screams, Is.EqualTo(1));
            Assert.That(clock.Screamed, Is.True);
        }

        [Test]
        public void 예고_중_시야를_잃으면_유예_뒤_취소하고_비명은_없다()
        {
            var clock = new StudentWindupClock();
            Run(ref clock, 4);
            Assert.That(Once(ref clock, sight: false), Is.EqualTo(StudentWindupOutcome.Continue), "0.125초 — 유예 안");
            Assert.That(Once(ref clock, sight: false), Is.EqualTo(StudentWindupOutcome.CancelLostSight), "0.25초 — 취소");
            Assert.That(clock.Screamed, Is.False);
        }

        [Test]
        public void 시야를_잠깐_잃었다가_되찾으면_예고가_이어진다()
        {
            var clock = new StudentWindupClock();
            Run(ref clock, 3);
            Assert.That(Once(ref clock, sight: false), Is.EqualTo(StudentWindupOutcome.Continue));
            Assert.That(Run(ref clock, 20), Is.EqualTo(StudentWindupOutcome.Scream));
        }

        [Test]
        public void 경직이나_기절이면_즉시_취소하고_비명은_없다()
        {
            var clock = new StudentWindupClock();
            Run(ref clock, 10);
            Assert.That(Once(ref clock, incapacitated: true), Is.EqualTo(StudentWindupOutcome.CancelIncapacitated));
            Assert.That(clock.Screamed, Is.False);
        }

        [Test]
        public void 타겟이_사라지면_즉시_취소한다()
        {
            var clock = new StudentWindupClock();
            Assert.That(Once(ref clock, target: false), Is.EqualTo(StudentWindupOutcome.CancelNoTarget));
        }

        [Test]
        public void 비명을_낸_뒤에는_기절해도_취소로_바뀌지_않는다()
        {
            var clock = new StudentWindupClock { Screamed = true };
            Assert.That(Once(ref clock, incapacitated: true), Is.EqualTo(StudentWindupOutcome.Continue));
        }

        [Test]
        public void 쿨다운은_비명_후_지정_시간_동안만_유지된다()
        {
            Assert.That(StudentScreamRules.IsOnCooldown(5.0, double.NegativeInfinity, 20f), Is.False, "비명 전에는 쿨다운 없음");
            Assert.That(StudentScreamRules.IsOnCooldown(10.0, 10.0, 20f), Is.True, "비명 직후");
            Assert.That(StudentScreamRules.IsOnCooldown(29.875, 10.0, 20f), Is.True, "19.875초 경과");
            Assert.That(StudentScreamRules.IsOnCooldown(30.0, 10.0, 20f), Is.False, "20초 경과 — 다시 비명 가능");
        }

        [Test]
        public void 도주_후보는_위협_쪽을_향하지_않는다()
        {
            var buffer = new FleeCandidate[16];
            int n = StudentScreamRules.BuildFleeCandidates(1f, 0f, 8, -0.2f, buffer); // 위협 반대 = +X
            Assert.That(n, Is.EqualTo(5), "8방향 중 정면·좌우 45°·좌우 90° 만 남는다(뒤쪽 3방향 제외)");
            for (int i = 0; i < n; i++)
            {
                Assert.That(buffer[i].Alignment, Is.GreaterThanOrEqualTo(-0.2f));
                Assert.That(buffer[i].X, Is.GreaterThan(-0.2f), "위협(-X) 쪽 후보 없음");
            }
            Assert.That(buffer[0].X, Is.EqualTo(1f).Within(1e-5f), "첫 후보는 정반대 방향");
        }

        [Test]
        public void 위협이_발밑이면_후보를_만들지_않는다()
        {
            Assert.That(StudentScreamRules.BuildFleeCandidates(0f, 0f, 8, -0.2f, new FleeCandidate[8]), Is.EqualTo(0));
        }

        [Test]
        public void 같은_거리면_정반대_방향_후보가_이긴다()
        {
            float straight = StudentScreamRules.ScoreFleePoint(6f, 1f, 6f);
            float side = StudentScreamRules.ScoreFleePoint(6f, 0f, 6f);
            Assert.That(straight, Is.GreaterThan(side));
            Assert.That(StudentScreamRules.ScoreFleePoint(8f, 0f, 6f), Is.GreaterThan(StudentScreamRules.ScoreFleePoint(3f, 1f, 6f)), "벽에 막혀 짧은 후보보다 멀리 트인 후보");
        }

        [Test]
        public void 도주는_위협을_지정_시간_못_보면_포기하고_보면_초기화된다()
        {
            float lost = 0f;
            for (int i = 0; i < 23; i++) Assert.That(StudentScreamRules.StepFleeGiveUp(ref lost, tick, false, 3f), Is.False);
            Assert.That(StudentScreamRules.StepFleeGiveUp(ref lost, tick, true, 3f), Is.False, "다시 보면 0 으로");
            Assert.That(lost, Is.EqualTo(0f));
            for (int i = 0; i < 23; i++) StudentScreamRules.StepFleeGiveUp(ref lost, tick, false, 3f);
            Assert.That(StudentScreamRules.StepFleeGiveUp(ref lost, tick, false, 3f), Is.True, "3초 — 포기");
        }
    }
}
