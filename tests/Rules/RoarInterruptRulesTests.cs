using NUnit.Framework;

namespace EmptyHouse.Zombie.Rules.Tests
{
    /// <summary>포효 취소 판정 — 끊는 경우는 「발각 전환 → 배회 + 옵트인 종」 하나뿐이다.</summary>
    public sealed class RoarInterruptRulesTests
    {
        [Test]
        public void Teacher_경고_취소는_포효를_끊는다()
        {
            Assert.That(RoarInterruptRules.ShouldInterrupt(leavingRoarTransition: true, enteringWander: true, speciesInterruptsOnCancel: true), Is.True);
        }

        [Test]
        public void 경고에서_추격으로_가면_포효를_끊지_않는다()
        {
            // RoarTransition → Chase: 새 상태가 배회가 아니다
            Assert.That(RoarInterruptRules.ShouldInterrupt(true, false, true), Is.False);
        }

        [Test]
        public void 옵트인하지_않은_종은_취소돼도_끊지_않는다()
        {
            // 기존 종(Walker 등) — 종전대로 클립 끝까지
            Assert.That(RoarInterruptRules.ShouldInterrupt(true, true, false), Is.False);
        }

        [Test]
        public void Oiler_놀람에서_도주로_가는_전이는_끊지_않는다()
        {
            // Oiler: RoarTransition(놀람) → Flee — 새 상태가 배회가 아니고 옵트인도 없다
            Assert.That(RoarInterruptRules.ShouldInterrupt(true, false, false), Is.False);
        }

        [Test]
        public void 발각_전환이_아닌_곳에서_배회로_가면_끊지_않는다()
        {
            // 예: Subside → Wander, Staggered → Wander
            Assert.That(RoarInterruptRules.ShouldInterrupt(false, true, true), Is.False);
        }
    }
}
