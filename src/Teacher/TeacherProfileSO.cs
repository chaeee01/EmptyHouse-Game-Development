using EmptyHouse.Zombie.Rules;
using UnityEngine;

/// <summary>
/// Teacher 프로파일 — 감지·이동 수치는 base 필드(Walker 값에서 출발), 경고 규칙 수치만 전용 필드로 둔다.
/// 에셋 설정 계약: HasHearing=false(시야 안 달리기만 감지) · SyncEnabled=false(무리 동조 없음 — 무리원은 대표 상태를 복사해 경고 규칙을 건너뛴다).
/// </summary>
[CreateAssetMenu(fileName = "SO_Zombie_Teacher", menuName = "Game/Zombie Profile/Teacher")]
public class TeacherProfileSO : ZombieProfileSO
{
    public override ZombieSpecies Species => ZombieSpecies.Teacher; // 종 식별 — 파생 타입이 정한다

    [Header("Teacher 전용 — 경고")]
    [Tooltip("경고 지속 시간(초). 이 시간 동안 계속 달리면 추격으로 넘어간다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float WarningSeconds = 2f;
    [Tooltip("경고 중 달리기 중단이 이 시간(초) 이상 이어지면 경고를 취소하고 순찰로 돌아간다. 달리기 키 순간 떼기·복제 지연을 흡수한다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float WarningSprintStopGraceSeconds = 0.25f;
    [Tooltip("경고 중 타겟을 보지 못한 시간이 이 시간(초) 이상 이어지면 경고를 취소하고 순찰로 돌아간다. ⚪ 기획 미확정 임시값")]
    [Min(0f)] public float WarningLostSightGraceSeconds = 0.75f;

    /// <summary>경고 판정 수치를 순수 규칙 입력으로 옮긴다.</summary>
    /// <returns>경고 판정 수치.</returns>
    public TeacherWarningSettings ToWarningSettings()
        => new TeacherWarningSettings(WarningSeconds, WarningSprintStopGraceSeconds, WarningLostSightGraceSeconds);
}
