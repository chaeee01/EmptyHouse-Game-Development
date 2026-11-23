using UnityEngine;

// Player-side camouflage/pose/flashlight systems implement this interface.
// Zombie AI only consumes the read-only signals and never owns those systems.
public interface IZombiePerceptionSource
{
    Transform Root { get; }
    Vector3 EyePosition { get; }
    ulong NetworkObjectId { get; }
    bool IsDisguised { get; }
    bool IsSpectator { get; }
    bool IsCrouching { get; }
    bool IsMoving { get; }
    bool IsSprinting { get; } // 달리는 중인가 — PlayerController.Sprinting(오너 권위 복제값) 그대로. Teacher 감지·경고 판정 전용이며 기존 종은 읽지 않는다
    Vector3 Velocity { get; } // 서버 위치 델타로 구한 이동 속도(m/s · 3D). 관성 추격 첫 간선 = 타겟의 마지막 속도 벡터(좀비공통 4-4). 소유자 권위 NetworkTransform 이라 Rigidbody 속도는 비신뢰
    bool CarriesScrap { get; } // 인벤에 스크랩 1개 이상(등급 무관 — 좀비타입 5-1). Dog 후각의 유일한 트리거
    Vector3 LookDirection { get; } // 시선 방향(월드 단위 벡터) 서버 근사 — 몸 yaw + 복제 조준 파라미터(AimPitch·AimYawOffset). 카메라 원뿔은 복제되지 않아 Lurker 「발견」 판정이 이 근사를 쓴다
    bool IsFlashlightAimingAt(Transform zombie); // Walker 손전등 피폭 배율(좀비타입 2-2 ×1.8) 전용. Stalker 정지는 여기가 아니라 IStalkerFreezeVolume 집합이 판정한다
}
