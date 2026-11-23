using UnityEngine;

public readonly struct ZombiePerceptionFrame
{
    public readonly bool HasVisualStimulus;
    public readonly bool HasAuditoryStimulus;
    public readonly bool HasInstantVisualDetection;
    public readonly bool HasTrackingStimulus;
    public readonly float VisualGainPerSecond;
    public readonly float AuditoryEffectiveDb;
    public readonly Vector3 VisualPosition;
    public readonly Vector3 AuditoryPosition;

    // 종 전용 자극(훅②)이 얹은 의심도 바닥. 0 이면 기여 없음. 청각 pull-up 과 같은 max 합류라
    // 시각·청각과 순서를 다투지 않는다 — 큰 쪽이 이긴다.
    public readonly float DirectSuspicionFloor;
    public readonly Vector3 DirectPosition;

    // 추적 대상은 Transform 이 아니라 지각 소스로 넘긴다. 좀비가 한 번 문 타겟의
    // 사망(관전)·위장을 매 프레임 재검증하려면 상태를 읽을 수 있는 원본이 필요하다 —
    // Transform 만 들고 있으면 시신을 영원히 타겟으로 붙들게 된다.
    public readonly IZombiePerceptionSource PreferredTarget;

    // 청각으로 해석된 대상(가청 임계 이상 · 해석 성공 시만). 관성 추격의 T8b 는 발각 dB 미달 소음으로도
    // 타겟을 잡아야 해서(좀비공통 4-2 T8b) PreferredTarget 과 따로 노출한다. 지각 단계가 관전·비래치 위장을 이미 걸러 null 로 만든다.
    public readonly IZombiePerceptionSource AuditoryTarget;

    public ZombiePerceptionFrame(
        bool hasVisualStimulus,
        bool hasAuditoryStimulus,
        bool hasInstantVisualDetection,
        bool hasTrackingStimulus,
        float visualGainPerSecond,
        float auditoryEffectiveDb,
        Vector3 visualPosition,
        Vector3 auditoryPosition,
        float directSuspicionFloor,
        Vector3 directPosition,
        IZombiePerceptionSource preferredTarget,
        IZombiePerceptionSource auditoryTarget)
    {
        HasVisualStimulus = hasVisualStimulus;
        HasAuditoryStimulus = hasAuditoryStimulus;
        HasInstantVisualDetection = hasInstantVisualDetection;
        HasTrackingStimulus = hasTrackingStimulus;
        VisualGainPerSecond = visualGainPerSecond;
        AuditoryEffectiveDb = auditoryEffectiveDb;
        VisualPosition = visualPosition;
        AuditoryPosition = auditoryPosition;
        DirectSuspicionFloor = directSuspicionFloor;
        DirectPosition = directPosition;
        PreferredTarget = preferredTarget;
        AuditoryTarget = auditoryTarget;
    }
}
