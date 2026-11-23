using EmptyHouse.Zombie.Rules;
using UnityEngine;

/// <summary>
/// Student 프로파일 — 감지·이동 수치는 base 필드(Walker 값에서 출발), 비명·도주 수치만 전용 필드로 둔다.
/// 에셋 설정 계약: HasHearing=false(자기 비명에 반응하지 않게 — 소음 전파는 자기 자신도 후보로 훑는다) · SyncEnabled=false(무리 동조 없음).
/// 도주 수치는 Oiler 의 도주 필드 구성을 참고했지만 Oiler 와 공유하지 않는 독립 필드다.
/// </summary>
[CreateAssetMenu(fileName = "SO_Zombie_Student", menuName = "Game/Zombie Profile/Student")]
public class StudentProfileSO : ZombieProfileSO
{
    public override ZombieSpecies Species => ZombieSpecies.Student; // 종 식별 — 파생 타입이 정한다

    [Header("Student 전용 — 비명")]
    [Tooltip("비명 예고 시간(초). 이 시간 동안 플레이어를 계속 보면 비명을 낸다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float ScreamWindupSeconds = 1.5f;
    [Tooltip("예고 중 시야 상실 허용 시간(초). 이보다 길게 놓치면 비명을 취소한다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float ScreamLostSightGraceSeconds = 0.25f;
    [Tooltip("비명 소음 크기(dB). 기존 소음 전파(거리 감쇠·벽 차폐)를 그대로 탄다 — 개활지 기준 Walker(가청 30dB)는 약 33m 안에서 조사로 반응한다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float ScreamNoiseDb = 80f;
    [Tooltip("비명 재사용 대기(초). 이 동안 다시 발견하면 비명 없이 도주만 한다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float ScreamCooldownSeconds = 20f;

    [Header("Student 전용 — 도주")]
    [Tooltip("도주 이동 속도(m/s). ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float FleeSpeed = 3f;
    [Tooltip("도주 지점 샘플 거리(m) — 위협 반대편 이 거리에서 NavMesh 지점을 뽑는다.")]
    [Min(0.5f)] public float FleeStepMeters = 6f;
    [Tooltip("도주 지점 재선정 주기(초).")]
    [Min(0.1f)] public float FleeRepathSeconds = 1f;
    [Tooltip("위협(타겟)을 이 시간(초) 동안 보지 못하면 도주를 끝내고 진정 → 귀가한다.")]
    [Min(0f)] public float FleeGiveUpSeconds = 3f;
    [Tooltip("도주 후보 방향 수 — 위협 반대 방향 중심으로 360° 를 균등 분할한다. 위협 쪽 후보는 버린다.")]
    [Range(3, 16)] public int FleeSampleDirections = 8;

    /// <summary>비명 판정 수치를 순수 규칙 입력으로 옮긴다.</summary>
    public StudentScreamSettings ToScreamSettings()
        => new StudentScreamSettings(ScreamWindupSeconds, ScreamLostSightGraceSeconds, ScreamCooldownSeconds);
}
