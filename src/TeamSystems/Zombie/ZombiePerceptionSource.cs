using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Explicit player-to-zombie perception adapter. It registers once in the SO runtime set,
/// replacing per-zombie hierarchy scans for interfaces, rigidbodies, cameras, and NetworkObjects.
/// </summary>
public sealed class ZombiePerceptionSource : NetworkBehaviour, IZombiePerceptionSource
{
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private Transform eyeAnchor;
    [SerializeField] private Transform flashlightAimAnchor;
    [SerializeField] private PlayerDeathHandler deathHandler; // 같은 프리팹의 사망 권위 — IsSpectator 를 여기서 직접 읽는다
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;
    [SerializeField, Min(0f)] private float movingSpeedThreshold = 0.05f;
    [SerializeField, Min(0.01f)] private float velocitySmoothingSeconds = 0.2f; // 속도 벡터 지수 평활 시간 상수(초). 짧으면 지터가 남고 길면 방향 전환이 늦게 잡힌다
    [SerializeField, Range(0f, 90f)] private float flashlightAimTolerance = 10f;

    [Header("Server-owned perception flags")]
    private bool isDisguised;
    [SerializeField] private bool isCrouching;
    [SerializeField] private bool isFlashlightActive;

    private Vector3 previousPosition;
    private bool isMoving;
    private Vector3 velocity; // 서버 위치 델타 속도(m/s, 평활값) — 관성 추격 heading 의 원천
    private PlayerReturn playerReturn; // 같은 오브젝트의 귀환 권위 — Awake 캐시(자기완결, 인스펙터 배선 불필요)
    private PlayerInventory inventory; // 같은 오브젝트의 인벤 — 스크랩 소지 요약의 원천. 오너에서만 살아 있다(비오너는 비활성)
    private PlayerFlashlight flashlight; // 같은 오브젝트의 손전등 — 켜짐(IsOn) 복제값의 원천. Stalker 정지 원뿔은 StalkerFreezeLightCone 이 따로 등록한다
    private bool carriesScrap; // 서버 소유 플래그 — 인벤 슬롯은 복제되지 않는 오너 로컬이라 오너가 요약값만 밀어 올린다
    private bool ownerSentCarriesScrap; // 오너가 마지막으로 서버에 보낸 값 — 변할 때만 RPC 를 태운다
    private PlayerHeadCameraFollow cameraFollow; // 같은 오브젝트의 카메라 공전 중심 — 눈 위치(뷰 원점)의 원천. 원격 인스턴스도 같은 값을 재구성한다(P-050 적용결과)
    private PlayerController playerController; // 같은 오브젝트의 이동 권위 — 달리기 복제값(Sprinting)의 원천. Awake 캐시
    private Animator aimAnimator; // 조준 파라미터(AimPitch·AimYawOffset) 원천 — 오너는 PlayerAnimator 가 쓰고 서버·원격은 OwnerNetworkAnimator 복제로 같은 값을 받는다
    private static readonly int aimPitchHash = Animator.StringToHash("AimPitch"); // 시선 pitch(도, 아래가 양수 — PlayerController.AimPitchDeg)
    private static readonly int aimYawOffsetHash = Animator.StringToHash("AimYawOffset"); // 시선-하체 yaw 차(도 — PlayerController.AimYawOffsetDeg)

    public Transform Root => transform;
    // 눈 위치 = 실제 카메라 뷰 원점(자세 중심 + 공전 오프셋 · 손 IK 와 같은 재구성). 종전 폴백 root+1.5m 는 루트가 몸통 높이라
    // 발 위 2.5m 허공이었다 — 좀비 시야 LoS·목 시선이 머리 위를 겨눴다(2026-10-17). eyeAnchor 는 명시 오버라이드용으로만 남긴다.
    public Vector3 EyePosition => eyeAnchor != null ? eyeAnchor.position : cameraFollow.GetViewOrigin(ViewRotation);
    public bool IsDisguised => isDisguised;
    // 사망·귀환 권위를 서버 메모리에서 직접 읽는다(push 아님) — 확정이 선 프레임부터 즉시 인지에서 제외된다(좀비AI E2).
    // 귀환도 관전이다(EH-94) — 귀환한 폰은 버스 자리에 투명하게 남으므로, 사망만 거르면 좀비가
    // 이 유령 폰을 보고 타겟으로 물어 귀환한 관전자에게 추격 BGM 이 깔리고 좀비도 빈자리를 공격한다.
    public bool IsSpectator => deathHandler.IsDead.Value || playerReturn.HasExtracted.Value;

    /// <summary>형제 참조(귀환·인벤·손전등·조준 애니메이터)를 캐시한다 — PlayerBodyVisibility 와 같은 자기완결 방식.</summary>
    private void Awake()
    {
        playerReturn = GetComponent<PlayerReturn>();
        inventory = GetComponent<PlayerInventory>();
        flashlight = GetComponent<PlayerFlashlight>();
        cameraFollow = GetComponent<PlayerHeadCameraFollow>();
        aimAnimator = GetComponentInChildren<PlayerAnimator>(true).Animator;
        playerController = GetComponent<PlayerController>();
    }
    public bool IsCrouching => isCrouching;
    public bool IsMoving => isMoving;
    public bool IsSprinting => playerController != null && playerController.Sprinting; // 달리기 복제값(달리기 키·이동 입력·비위장·비웅크림·스태미나를 오너가 이미 합성) — 서버에서도 유효
    public Vector3 Velocity => velocity; // IZombiePerceptionSource — FixedUpdate 서버 분기에서 갱신
    public bool CarriesScrap => carriesScrap;

    /// <summary>
    /// 시선 방향 서버 근사 — 카메라 원뿔은 복제되지 않으므로 몸 회전(NetworkTransform) 위에 복제 조준 파라미터를 얹는다.
    /// PlayerController 가 cameraPivot 에 적용하는 Euler(pitch, yaw 차, 0) 과 같은 합성이라 오너 카메라 forward 와 일치한다(댐핑 지연 제외).
    /// </summary>
    public Vector3 LookDirection => ViewRotation * Vector3.forward;

    /// <summary>
    /// 뷰(시선) 회전 서버 근사 — 몸 회전 × Euler(복제 AimPitch, 복제 AimYawOffset, 0). PlayerHandArmIK 가 뷰 원점을 만들 때 쓰는 합성과 같다.
    /// </summary>
    private Quaternion ViewRotation
    {
        get
        {
            float pitch = aimAnimator.GetFloat(aimPitchHash);
            float yawOffset = aimAnimator.GetFloat(aimYawOffsetHash);
            return transform.rotation * Quaternion.Euler(pitch, yawOffset, 0f);
        }
    }

    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        if (!IsServer) return;

        if (disguiseStateChanged == null)
        {
            Debug.LogError($"[{nameof(ZombiePerceptionSource)}] Disguise state channel is not assigned on {name}.", this);
            enabled = false;
            return;
        }

        disguiseStateChanged.OnEventRaised += HandleDisguiseStateChanged;

        if (runtimeRegistry == null)
        {
            Debug.LogError($"[{nameof(ZombiePerceptionSource)}] Runtime registry is not assigned on {name}.", this);
            enabled = false;
            return;
        }

        runtimeRegistry.RegisterPerceptionSource(this);
    }

    public override void OnNetworkDespawn()
    {
        if (disguiseStateChanged != null)
            disguiseStateChanged.OnEventRaised -= HandleDisguiseStateChanged;

        runtimeRegistry?.UnregisterPerceptionSource(this);
    }

    private void HandleDisguiseStateChanged(DisguiseStateChangedEvent evt)
    {
        if (!IsServer || evt.PlayerNetworkObjectId != NetworkObjectId) return;
        isDisguised = evt.IsDisguised;
    }

    private void FixedUpdate()
    {
        // 스크랩 소지 요약은 오너만 안다(인벤 슬롯 비복제) — 변화 에지에서만 서버로 밀어 올린다.
        // 매 픽업 경로에 훅을 심는 대신 폴링으로 모든 변이 경로(획득·버리기·소비·정산·사망 드롭)를 한 번에 덮는다.
        if (IsOwner)
        {
            bool has = inventory.HasItemOfKind(ItemKind.Scrap);
            if (has != ownerSentCarriesScrap)
            {
                ownerSentCarriesScrap = has;
                SubmitCarriesScrapServerRpc(has);
            }
        }

        if (!IsServer) return;

        Vector3 rawVelocity = (transform.position - previousPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        isMoving = rawVelocity.magnitude >= movingSpeedThreshold;

        // 소유자 권위 NetworkTransform 의 서버 위치는 보간 지터가 있다 — 지수 평활로 방향이 박자마다 튀지 않게 한다.
        velocity = Vector3.Lerp(velocity, rawVelocity, 1f - Mathf.Exp(-Time.fixedDeltaTime / velocitySmoothingSeconds));
        previousPosition = transform.position;

        // 손전등 켜짐은 복제값(isOn ∧ 장착)에서 직접 읽는다 — 예전 push 방식(ServerSetFlashlightActive)은
        // 호출자가 없어 플래그가 영원히 false 였다(Walker 손전등 배율·Stalker 정지 둘 다 침묵하던 부채).
        isFlashlightActive = flashlight.IsOn;
    }

    /// <summary>오너가 계산한 스크랩 소지 요약을 서버 플래그에 확정한다.</summary>
    /// <param name="value">스크랩 1개 이상 소지 여부.</param>
    [ServerRpc]
    private void SubmitCarriesScrapServerRpc(bool value)
    {
        Log.D($"[ZombiePerceptionSource] CarriesScrap {value} — id {NetworkObjectId}");
        carriesScrap = value;
    }

    public bool IsFlashlightAimingAt(Transform zombie)
    {
        if (!isFlashlightActive || flashlightAimAnchor == null || zombie == null) return false;

        Vector3 direction = zombie.position - flashlightAimAnchor.position;
        if (direction.sqrMagnitude <= Mathf.Epsilon) return true;
        return Vector3.Angle(flashlightAimAnchor.forward, direction) <= flashlightAimTolerance;
    }

    public void ServerSetCrouching(bool value) { if (IsServer) isCrouching = value; }
}
