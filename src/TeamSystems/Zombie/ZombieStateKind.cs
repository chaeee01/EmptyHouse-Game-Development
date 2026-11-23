public enum ZombieStateKind : byte
{
    Wander,
    Alert,
    Investigate,
    RoarTransition,
    Chase,
    Attack,
    Subside,
    ChaseBlind, // 관성 추격 — 래치 OFF · 의심도 100 유지 · 타겟 없음(좀비공통 4-4). 복제되는 값이라 끝에 추가
    Staggered,
    Stunned,
    Flee, // Lurker 도망·귀환 — 사냥감에서 멀어지거나 홈으로 달린다. 복제되는 값이라 끝에 추가
    Vanished, // Lurker 소멸 — 렌더러·콜라이더 OFF 로 숨어 재배치 대기. 복제되는 값이라 끝에 추가
    Rising // 기상 — 기절이 끝나 일어나는 중(riseSeconds). 전투 중단·명중 무시, 충전·운반은 불가(IsStunned 아님). 복제되는 값이라 끝에 추가
}
