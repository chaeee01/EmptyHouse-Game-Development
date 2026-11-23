namespace EmptyHouse.Zombie.Rules
{
    /// <summary>
    /// 포효 취소 판정 — 클라이언트 연출(ZombieAnimator)이 복제 상태 에지마다 묻는다. Unity 비의존이라 EditMode 로 고정한다.
    /// 끊는 경우는 단 하나: 발각 전환(RoarTransition)에서 배회(Wander)로 나갔고(= 경고 취소) 종이 옵트인했을 때.
    /// 추격(Chase)·도주(Flee — Oiler 놀람)·경직/기절 등 다른 출구와 옵트인하지 않은 종은 종전대로 포효 클립을 끝까지 재생한다.
    /// </summary>
    public static class RoarInterruptRules
    {
        /// <param name="leavingRoarTransition">직전 상태가 발각 전환인가.</param>
        /// <param name="enteringWander">새 상태가 배회인가.</param>
        /// <param name="speciesInterruptsOnCancel">종이 취소 시 포효 끊기를 원하는가(ZombieController.InterruptsRoarOnCancel).</param>
        /// <returns>포효 레이어를 지금 끊어야 하면 true.</returns>
        public static bool ShouldInterrupt(bool leavingRoarTransition, bool enteringWander, bool speciesInterruptsOnCancel)
        {
            return leavingRoarTransition && enteringWander && speciesInterruptsOnCancel;
        }
    }
}
