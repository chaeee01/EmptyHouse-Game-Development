using NUnit.Framework;

namespace EmptyHouse.Zombie.Rules.Tests
{
    /// <summary>
    /// 선생님 좀비 감지 게이트·경고 판정 기대값 테스트. 수치는 TeacherProfileSO 기본값과 같다.
    /// 틱은 0.125초(2 진수로 정확) — 경계값 비교가 부동소수 누적 오차에 흔들리지 않게 한다.
    /// </summary>
    public sealed class TeacherWarningRulesTests
    {
        private const float tick = 0.125f; // 판정 틱(초)
        private static readonly TeacherWarningSettings settings = new TeacherWarningSettings(2f, 0.25f, 0.75f); // 경고 16틱 · 달리기 중단 유예 2틱 · 시야 상실 유예 6틱

        /// <summary>같은 입력으로 최대 n 틱 진행하고, Continue 가 아닌 판정이 나오면 그 자리에서 멈춘다.</summary>
        private static TeacherWarningOutcome Run(ref TeacherWarningClock clock, int ticks, bool sprinting, bool sight)
        {
            TeacherWarningOutcome outcome = TeacherWarningOutcome.Continue;
            for (int i = 0; i < ticks; i++)
            {
                outcome = TeacherWarningRules.Step(ref clock, settings, tick, true, sprinting, sight);
                if (outcome != TeacherWarningOutcome.Continue) break;
            }
            return outcome;
        }

        private static TeacherWarningOutcome Once(ref TeacherWarningClock clock, bool sprinting, bool sight)
            => TeacherWarningRules.Step(ref clock, settings, tick, true, sprinting, sight);

        [Test]
        public void 감지_게이트는_보이는_대상이_달릴_때만_통과한다()
        {
            Assert.That(TeacherWarningRules.PassesDetectionGate(true, true), Is.True);
            Assert.That(TeacherWarningRules.PassesDetectionGate(true, false), Is.False, "걷기·정지·웅크림은 무반응");
            Assert.That(TeacherWarningRules.PassesDetectionGate(false, true), Is.False, "시야 밖 달리기는 무반응");
            Assert.That(TeacherWarningRules.PassesDetectionGate(false, false), Is.False);
        }

        [Test]
        public void 경고를_무시하고_계속_달리면_경고_시간이_지나_추격한다()
        {
            var clock = new TeacherWarningClock();
            Assert.That(Run(ref clock, 15, true, true), Is.EqualTo(TeacherWarningOutcome.Continue), "1.875초 — 아직 경고 중");
            Assert.That(Once(ref clock, true, true), Is.EqualTo(TeacherWarningOutcome.BeginChase), "2.0초 — 추격");
        }

        [Test]
        public void 경고_중_달리기를_멈추면_유예_뒤_취소한다()
        {
            var clock = new TeacherWarningClock();
            Run(ref clock, 5, true, true);
            Assert.That(Once(ref clock, false, true), Is.EqualTo(TeacherWarningOutcome.Continue), "0.125초 — 유예 안");
            Assert.That(Once(ref clock, false, true), Is.EqualTo(TeacherWarningOutcome.CancelSprintStopped), "0.25초 — 취소");
            Assert.That(clock.Elapsed, Is.LessThan(settings.WarningSeconds));
        }

        [Test]
        public void 달리기를_잠깐_떼었다가_다시_달리면_취소하지_않는다()
        {
            var clock = new TeacherWarningClock();
            Run(ref clock, 3, true, true);
            Assert.That(Once(ref clock, false, true), Is.EqualTo(TeacherWarningOutcome.Continue), "0.125초 중단 — 유예 미만");
            Assert.That(Run(ref clock, 30, true, true), Is.EqualTo(TeacherWarningOutcome.BeginChase), "다시 달리면 중단 시간이 0 으로 돌아가 경고가 끝까지 이어진다");
        }

        [Test]
        public void 경고_중_시야를_잃으면_유예_뒤_취소한다()
        {
            var clock = new TeacherWarningClock();
            Run(ref clock, 3, true, true);
            Assert.That(Run(ref clock, 5, true, false), Is.EqualTo(TeacherWarningOutcome.Continue), "0.625초 — 유예 안");
            Assert.That(Once(ref clock, true, false), Is.EqualTo(TeacherWarningOutcome.CancelLostSight), "0.75초 — 취소");
        }

        [Test]
        public void 시야를_잠깐_잃었다가_되찾으면_경고가_이어진다()
        {
            var clock = new TeacherWarningClock();
            Run(ref clock, 3, true, true);
            Assert.That(Run(ref clock, 4, true, false), Is.EqualTo(TeacherWarningOutcome.Continue), "0.5초 가림");
            Assert.That(Run(ref clock, 30, true, true), Is.EqualTo(TeacherWarningOutcome.BeginChase));
        }

        [Test]
        public void 타겟이_사라지면_즉시_취소한다()
        {
            var clock = new TeacherWarningClock();
            Assert.That(TeacherWarningRules.Step(ref clock, settings, tick, false, true, true), Is.EqualTo(TeacherWarningOutcome.CancelNoTarget));
        }

        [Test]
        public void 같은_틱에_취소와_만료가_겹치면_취소가_이긴다()
        {
            var clock = new TeacherWarningClock { Elapsed = 1.875f, NotSprintingSeconds = 0.125f };
            Assert.That(Once(ref clock, false, true), Is.EqualTo(TeacherWarningOutcome.CancelSprintStopped));
            Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(settings.WarningSeconds), "만료 조건도 함께 성립한 틱이다");
        }

        [Test]
        public void 음수_틱과_음수_설정은_0으로_본다()
        {
            var negative = new TeacherWarningSettings(-1f, -1f, -1f);
            Assert.That(negative.WarningSeconds, Is.EqualTo(0f));
            Assert.That(negative.SprintStopGraceSeconds, Is.EqualTo(0f));
            Assert.That(negative.LostSightGraceSeconds, Is.EqualTo(0f));

            var clock = new TeacherWarningClock();
            Assert.That(TeacherWarningRules.Step(ref clock, settings, -5f, true, true, true), Is.EqualTo(TeacherWarningOutcome.Continue));
            Assert.That(clock.Elapsed, Is.EqualTo(0f));
        }
    }
}
