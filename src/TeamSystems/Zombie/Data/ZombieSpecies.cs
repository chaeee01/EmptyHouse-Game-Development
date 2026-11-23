/// <summary>
/// 좀비 종. 코어는 이 값으로 분기하지 않는다(좀비공통 5-2) — 종별 차이는
/// <see cref="ZombieProfileSO"/> 파생과 ZombieController 파생의 훅 오버라이드로만 표현된다.
/// 구 ZombieType(Watcher 명칭)을 대체한다.
/// </summary>
public enum ZombieSpecies
{
    Walker,
    Listener,
    Stalker, // 구 Watcher — 빛에 멈춘다(좀비타입 4장)
    Dog,
    Lurker, // 매복 — 숨어서 첫 인지 플레이어를 사냥감으로 삼고, 발견되면 도망·방 이탈 시 돌진 즉사(사용자 확정 2026-10-06)
    Oiler, // 기름 좀비 — 겁쟁이. 보면 느리게 도망치고, 기절(30s) 중 들쳐 업어 분쇄기에 넣으면 300L(사용자 확정 2026-10-06)
    Teacher, // 선생님 — 시야 안에서 달리는 플레이어만 감지해 경고한다. 경고 중 걸으면 취소, 무시하고 달리면 추격(2026-11-23)
    Student // 학생 — 플레이어를 보면 비명 예고 → 서버 소음 1회(주변 좀비 조사) → 도주. 쿨다운 중 발견하면 비명 없이 도주(2026-11-23)
}
