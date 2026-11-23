using UnityEngine;

/// <summary>
/// 한 서버 프레임의 지각 결과를 조립하는 가변 중간체. ZombiePerceptionFrame(readonly)이 얼리기 전 단계다.
/// 흐름: ZombieSensorySystem 이 시각·청각을 채운다 → 훅②(ServerContributePerception)가 종 전용 자극을
/// 얹는다 → Build() 로 얼려 코어의 고정 갱신 순서(좀비공통 3-5)에 넘긴다.
/// Direct* 3필드가 종 확장의 전부다 — Listener 반경 2단(60/100)과 Dog 후각(100)이 이걸로 표현된다.
/// 값은 청각 pull-up 과 같은 max 합류라 순서 경합이 없다(큰 쪽이 이긴다).
/// </summary>
public sealed class ZombiePerceptionBuilder
{
    public bool HasVisualStimulus;
    public bool HasAuditoryStimulus;
    public bool HasInstantVisualDetection;
    public float VisualGainPerSecond;
    public float AuditoryEffectiveDb;
    public Vector3 VisualPosition;
    public Vector3 AuditoryPosition;
    public IZombiePerceptionSource VisualTarget;
    public IZombiePerceptionSource AuditoryTarget;

    public float DirectSuspicionFloor;            // 종 전용 의심도 바닥(max 합류 대상). 0 = 무효
    public IZombiePerceptionSource DirectTarget;  // 바닥값과 함께 확보할 타겟. 위장 무관(E15) — null 허용
    public Vector3 DirectPosition;                // 종 전용 단서 위치

    /// <summary>프레임 시작 시 모든 필드를 초기값으로 되돌린다. 인스턴스는 좀비당 1개를 재사용한다(GC 0).</summary>
    public void Reset()
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다(ZombieAnimator 선례).
        HasVisualStimulus = false;
        HasAuditoryStimulus = false;
        HasInstantVisualDetection = false;
        VisualGainPerSecond = 0f;
        AuditoryEffectiveDb = 0f;
        VisualPosition = default;
        AuditoryPosition = default;
        VisualTarget = null;
        AuditoryTarget = null;
        DirectSuspicionFloor = 0f;
        DirectTarget = null;
        DirectPosition = default;
    }

    /// <summary>조립 결과를 불변 프레임으로 얼린다. 타겟 우선순위(시각 &gt; 직접 &gt; 청각)도 여기서 정한다.</summary>
    /// <returns>코어 갱신 순서에 넘길 지각 프레임.</returns>
    public ZombiePerceptionFrame Build()
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다.

        // 시각이 최우선이다 — 눈으로 본 대상이 소리보다 확실하다. 직접 자극(반경·후각)이 그다음,
        // 청각이 마지막. 위장 무관 채널인 직접 자극을 청각보다 앞에 두어야 Listener·Dog 가
        // 위장한 플레이어를 놓치지 않는다(E15).
        IZombiePerceptionSource preferredTarget = VisualTarget ?? DirectTarget ?? AuditoryTarget;

        // 추적 자극 = "따라갈 대상이 이번 프레임에 잡혔는가". 시각은 대상 없이도 자극이 서지만
        // (원뿔 안 존재 자체가 자극) 청각·직접은 대상을 해석해 낸 경우에만 추적이 성립한다.
        bool hasTrackingStimulus = HasVisualStimulus || AuditoryTarget != null || DirectTarget != null;

        return new ZombiePerceptionFrame(
            HasVisualStimulus,
            HasAuditoryStimulus,
            HasInstantVisualDetection,
            hasTrackingStimulus,
            VisualGainPerSecond,
            AuditoryEffectiveDb,
            VisualPosition,
            AuditoryPosition,
            DirectSuspicionFloor,
            DirectPosition,
            preferredTarget,
            AuditoryTarget);
    }
}
