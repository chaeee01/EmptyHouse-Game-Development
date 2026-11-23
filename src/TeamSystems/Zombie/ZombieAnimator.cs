using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 좀비 애니메이터를 구동한다. 포효·공격은 복제된 상태로 고르고, 대기·보행·질주는 실제 이동 속력을
/// 그대로 Speed 파라미터로 넘겨 Locomotion 블렌드 트리가 섞는다.
///
/// 블렌드 트리의 문턱값은 각 클립의 실측 고유 이동 속도(Idle 0 / Walk 0.957 / Run 3.406 m/s)다.
/// 그래서 Speed 에 실속력을 그대로 넣으면 보폭이 이동 속도에 맞아 발이 미끄러지지 않는다.
///
/// 클립을 상태만으로 고르면 안 된다 — Wander·Investigate·Chase·Subside 는 모두 목적지 도착 후
/// StopAgent() 로 제자리에 서는 구간을 갖는다(조사는 InvestigateToWanderSeconds 동안, 배회 OFF 인
/// 군중 좀비는 영구). 그 구간에서도 상태는 그대로라, 가만히 선 좀비가 걷기 클립을 제자리에서 돌린다.
/// 반대로 포효·공격은 이동이 없는 연출이라 이동량으로 고를 수 없다 — 그래서 둘을 나눠서 판정한다.
///
/// 속력의 출처는 서버와 클라가 다르다. 서버는 위치를 루트모션이 만들어(ZombieRootMotion) 위치 델타로
/// 재면 되먹임이 돌므로, 에이전트가 내려는 속도(desiredVelocity)를 쓴다. 클라는 NavMeshAgent 가 꺼져 있고
/// NetworkTransform 이 보간해 준 복제 좌표뿐이라 위치 델타로 잰다(PlayerFootstepSfx 와 같은 방식).
///
/// 상태는 서버가 쓰고 전 클라이언트에 복제되므로, 이 컴포넌트는 모든 인스턴스에서 동작한다(NetworkAnimator 불필요).
/// </summary>
public partial class ZombieAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieController controller;
    [SerializeField] private Animator animator;

    [Header("Locomotion")]
    [Tooltip("속력 평활 시간 상수(초). 네트워크 보간 지터와 제자리 회전 중의 미세 이동을 흡수한다. 클수록 속도 변화에 애니가 늦게 반응한다.")]
    [SerializeField] private float speedSmoothingSeconds = 0.1f;
    [Tooltip("이 속력(m/s)을 넘는 표본은 순간이동으로 보고 버린다(스폰·워프·NavMesh 재배치).")]
    [SerializeField] private float teleportSpeed = 20f;
    [Tooltip("Speed 값별로 블렌드 트리가 실제로 만들어내는 이동 속력(m/s) 실측값. 이 곡선으로 재생 배속을 역보정해 발 미끄러짐을 없앤다. 클립이나 문턱값을 바꾸면 다시 재서 갱신한다.")]
    [SerializeField] private AnimationCurve blendedSpeedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 0.352f),
        new Keyframe(1.2f, 1.011f),
        new Keyframe(1.6f, 1.217f),
        new Keyframe(2f, 1.506f),
        new Keyframe(2.5f, 1.995f),
        new Keyframe(3f, 2.672f),
        new Keyframe(3.406f, 3.429f));

    [Header("Turn")]
    [Tooltip("Turn 클립이 본래 도는 속도(도/초). 프로파일 선회 속도를 이 값으로 나눠 재생 배속을 만든다 — 그래야 클립이 만드는 회전이 정확히 선회 속도가 된다. 클립을 바꾸거나 트리밍하면 메뉴 10c 로그의 실측값으로 갱신한다.")]
    [SerializeField] private float turnClipDegreesPerSecond = 58f;
    [Tooltip("이 각도(도) 이상 남으면 회전 세기가 최대(1)가 된다. 남은 각도를 0~1 로 정규화하는 분모다.")]
    [SerializeField] private float turnBlendSaturationDegrees = 20f;
    [Tooltip("회전 각속도 평활 시간 상수(초). 클라이언트의 복제 회전 보간 지터를 흡수한다.")]
    [SerializeField] private float turnSmoothingSeconds = 0.12f;
    [Tooltip("클라이언트에서 이 각속도(도/초) 미만은 회전으로 보지 않는다. 복제 보간 지터가 Turn 상태를 들락거리게 하는 것을 막는다.")]
    [SerializeField] private float turnClientMinDegreesPerSecond = 5f;

    [Header("Hit Reaction")]
    [Tooltip("경직(Staggered) 동안 전신 피격 클립을 재생한다. 끄면 종전처럼 애니메이터를 멈추고 뒤로 젖힌다 — 기어다니는 Dog 리그에는 서 있는 반응 클립이 맞지 않는다.")]
    [SerializeField] private bool hitReactionEnabled = true;

    private const string roarLayerName = "Roar (Upper)"; // AnimationSetupTool 이 만드는 상체 전용 포효 레이어 이름
    private const string roarStateTag = "Roar";          // 포효 재생 판정 태그 — 클립 길이가 연출 시간의 원천이라 코드 타이머를 두지 않는다(D-2)
    private const string roarEmptyStateName = "Empty";   // 포효 레이어의 기본(빈) 상태 — 포효 취소 시 크로스페이드 목적지
    private const float roarInterruptFadeSeconds = 0.15f; // 포효 취소 크로스페이드(초). 이후 레이어 가중치는 oneShotLayerFadeSeconds 로 내려간다
    private const string attackLayerName = "Attack (Upper)"; // AnimationSetupTool 이 만드는 상체 전용 공격 레이어 이름. Caged 컨트롤러엔 없다
    private const string attackStateTag = "Attack";          // 공격 재생 판정 태그
    private const float oneShotLayerFadeSeconds = 0.1f;      // 상체 원샷 레이어 가중치 페이드(초). 0이면 켜지고 꺼질 때 상체가 팝 한다
    private const string hitLayerName = "Hit (Full)";        // AnimationSetupTool(11) 이 만드는 전신 피격 레이어 이름. Caged 컨트롤러엔 없다
    private const string hitStateName = "Hit";               // 피격 클립 상태 — 복제된 명중 수로 Play 해 들고, 경직이 끝나면 가중치 페이드로 나간다
    private const string fallStateName = "Fall";             // 기절 눕기 상태(ZombieSmplBuilder 4단계) — Stunned 진입에 Play, 끝 프레임(누움) 유지. 없는 컨트롤러(Dog·Caged)는 코드 눕힘
    private const string getUpStateName = "GetUp";           // 일어나기 상태 — Rising 진입에 크로스페이드, 기상이 끝나면 가중치 페이드로 나간다
    private const float getUpBlendSeconds = 0.35f;           // Fall → GetUp 크로스페이드(초). 두 클립의 누운 위치 차이를 구르는 동작에 숨긴다

    private const string locomotionStateName = "Locomotion"; // Base Layer 기본 상태 — 스폰 위상 분산의 대상
    private const int idleVariantCount = 4;                  // AnimationSetupTool 의 Idle 변주 트리 자식 수와 같아야 한다
    private const int walkVariantCount = 3;                  // ZombieSmplBuilder.walkSlots(Walk Variants 트리 자식 수)와 같아야 한다 — 없는 컨트롤러(Dog·Caged)는 건너뛴다
    private const ulong idHashMultiplier = 2654435761UL;     // Knuth 승산 해시 — 연속된 오브젝트 id 를 0~1 에 고르게 흩뜨린다
    private const ulong idHashBuckets = 1024UL;              // 위상 해상도. 1/1024 면 눈으로 겹침을 못 본다

    private const int animLocomotion = 0; // 대기·보행·질주 — Speed 로 블렌드
    private const int animAttack = 3;     // 공격 — base 컨트롤러는 이 값을 더 듣지 않는다(공격은 상체 레이어). Caged 의 base Attack 이 아직 쓴다

    private const int animNone = -1;  // 아직 한 번도 쓰지 않았다 — 첫 판정은 무조건 밀어 넣는다

    private const float minNaturalSpeed = 0.05f;   // 이 아래에서는 배속 보정을 하지 않는다(나눗셈 폭주)

    // 경로 속력 바닥값 — 이동을 루트모션이 만드는 구조에서는 이 파라미터가 곧 보폭이다. RVO 협상이 desiredVelocity 를
    // 눌러 대기 자세로 미끄러지면 보폭이 0 이 되고, 그러면 정체를 풀 이동 자체가 사라져 되먹임에 갇힌다
    // (실측: 목적지 0.8m 앞에서 Speed 0.05 로 25초 이상 제자리). 갈 길이 남아 있는 동안은 걸음을 멈추지 않는다.
    private const float pathSpeedFloor = 0.6f;        // 그때 유지할 최저 속력(m/s). 걷기 문턱(1.16)의 절반쯤
    private const float pathSpeedFloorMeters = 0.25f; // 이 거리(m) 넘게 남았을 때만 적용한다 — 도착 직전에는 정상적으로 멈춘다
    private const float speedMultiplierMin = 0.5f; // 로코모션 배속 하한
    private const float speedMultiplierMax = 2.5f; // 로코모션 배속 상한 — Stalker 일반인 걸음(0.57m/s)으로 배회 1.2 를 내려면 2.1 배가 필요하다

    // 회전 배속 = 프로파일 선회 속도 ÷ 클립 고유 속도. 상·하한은 클립이 알아볼 수 없을 만큼 빨라지거나 느려지는 것을 막는다.
    private const float turnMultiplierMin = 0.3f;
    private const float turnMultiplierMax = 2.5f;

    private static readonly int stateHash = Animator.StringToHash("State");
    private static readonly int speedHash = Animator.StringToHash("Speed");
    private static readonly int speedMulHash = Animator.StringToHash("SpeedMul");
    private static readonly int turnBlendHash = Animator.StringToHash("TurnBlend");
    private static readonly int turnMulHash = Animator.StringToHash("TurnMul");
    private static readonly int roarTriggerHash = Animator.StringToHash("RoarTrigger");
    private static readonly int roarTagHash = Animator.StringToHash(roarStateTag);
    private static readonly int roarEmptyStateHash = Animator.StringToHash(roarEmptyStateName);
    private static readonly int attackTriggerHash = Animator.StringToHash("AttackTrigger");
    private static readonly int attackTagHash = Animator.StringToHash(attackStateTag);
    private static readonly int locomotionStateHash = Animator.StringToHash(locomotionStateName);
    private static readonly int idleVariantHash = Animator.StringToHash("IdleVariant");
    private static readonly int walkVariantHash = Animator.StringToHash("WalkVariant"); // 걷기 변주 슬롯(ZombieSmplBuilder 4단계) — SMPL 종 컨트롤러에만 있다
    private static readonly int hitStateHash = Animator.StringToHash(hitStateName);
    private static readonly int fallStateHash = Animator.StringToHash(fallStateName);
    private static readonly int getUpStateHash = Animator.StringToHash(getUpStateName);
    private static readonly int reachHash = Animator.StringToHash("IsReaching"); // 추격 대치 몸짓(Reach) Bool 파라미터(P-036)

    private bool hasReachParam;  // 컨트롤러에 IsReaching 이 있는가 — 첫 Update 에서 한 번 확인(스폰 시점 Animator 는 파라미터 준비 전 · ApplySpawnLook 주석)
    private bool lastReachValue; // 직전에 쓴 IsReaching 값 — 같으면 쓰지 않는다

    private Vector3 previousPosition; // 직전 프레임의 위치. 이동량은 이 차이로 구한다
    private float smoothedSpeed;      // 평활된 평면 속력(m/s)
    private int appliedAnimState = animNone;

    private float previousYaw;     // 직전 프레임의 좀비 루트 Y 회전(도). 회전 각속도는 이 차이로 구한다
    private float smoothedYawRate; // 평활된 회전 각속도(도/초, 부호 있음)
    private int roarLayerIndex;    // 상체 전용 포효 레이어 인덱스. 스폰 시 이름으로 찾는다
    private bool hasRoarEmptyState; // 포효 레이어에 Empty 상태가 있는가 — 없으면 포효 취소를 건너뛴다
    private ZombieStateKind lastStateKind; // 직전에 반영한 복제 상태 — 포효 취소 판정(RoarTransition 에서 나가는 에지)용
    private int attackLayerIndex;  // 상체 전용 공격 레이어 인덱스. 없는 컨트롤러(Caged)면 -1
    private int hitLayerIndex;     // 전신 피격 레이어 인덱스. 꺼져 있거나 없는 컨트롤러(Caged)면 -1
    private bool hasFallStates;    // 피격 레이어에 Fall·GetUp 상태가 있는가 — 있으면 기절을 클립으로, 없으면 코드 눕힘으로 그린다
    private bool spawnLookApplied; // 대기 변주·재생 위상을 이미 적용했는가 — 스폰당 한 번만 건다
    private Quaternion combatRestRotation; // 피격 연출 이전 모델의 회전.
    private Vector3 combatRestPosition; // 피격 연출 이전 모델의 localPosition — 모델 원점은 골반 높이(루트 위 ~0.9m)라 젖힘은 루트(발)를 축으로 돌려야 한다
    private bool combatPoseActive; // 피격 모델 회전의 복원 필요 여부.

    /// <summary>상태 변화를 구독하고 현재 상태를 즉시 반영한다(늦게 접속한 클라이언트 포함).</summary>
    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        smoothedSpeed = 0f;
        appliedAnimState = animNone;

        previousYaw = controller.transform.eulerAngles.y;
        smoothedYawRate = 0f;
        roarLayerIndex = animator.GetLayerIndex(roarLayerName);
        hasRoarEmptyState = roarLayerIndex >= 0 && animator.HasState(roarLayerIndex, roarEmptyStateHash);
        lastStateKind = controller.CurrentState;
        attackLayerIndex = animator.GetLayerIndex(attackLayerName);
        hitLayerIndex = hitReactionEnabled ? animator.GetLayerIndex(hitLayerName) : -1;
        hasFallStates = hitLayerIndex >= 0 && animator.HasState(hitLayerIndex, fallStateHash) && animator.HasState(hitLayerIndex, getUpStateHash);

        controller.StateChanged += HandleStateChanged;
        controller.WeaponHitLanded += HandleWeaponHitLanded;
        animator.SetFloat(speedHash, 0f);
        animator.SetFloat(speedMulHash, 1f);
        animator.SetFloat(turnBlendHash, 0f);
        animator.SetFloat(turnMulHash, 1f);
        animator.SetLayerWeight(roarLayerIndex, 0f);
        if (attackLayerIndex >= 0) animator.SetLayerWeight(attackLayerIndex, 0f);
        if (hitLayerIndex >= 0) animator.SetLayerWeight(hitLayerIndex, 0f);

        spawnLookApplied = false; // 실제 적용은 첫 Update — 이유는 ApplySpawnLook 주석 참조
        ResetHeadLook();

        ApplyAnimState();
    }

    /// <summary>상태 변화 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        controller.StateChanged -= HandleStateChanged;
        controller.WeaponHitLanded -= HandleWeaponHitLanded;
        if (combatPoseActive)
        {
            animator.transform.SetLocalPositionAndRotation(combatRestPosition, combatRestRotation);
            animator.speed = 1f;
            combatPoseActive = false;
        }
    }

    /// <summary>이동 속력을 갱신하고 보행·대기 판정을 반영한다.</summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsSpawned) return;

        if (!spawnLookApplied)
        {
            spawnLookApplied = true;
            ApplySpawnLook();
            hasReachParam = HasParameter(reachHash);
            lastReachValue = false; // 컨트롤러 기본값과 맞춘다 — 첫 true 는 반드시 쓰인다
            // 늦은 합류 — 이미 기절·기상 중이면 그 포즈로 바로 든다(스폰 시점의 Play 는 버려지므로 여기서). 누움은 끝 프레임
            if (hasFallStates && controller.IsStunned) animator.Play(fallStateHash, hitLayerIndex, 1f);
            else if (hasFallStates && controller.IsRising) animator.Play(getUpStateHash, hitLayerIndex, 0f);
        }

        // 피격 레이어는 경직 중에도 굴려야 한다 — 아래의 전투 중단 조기 반환보다 앞선다.
        UpdateHitLayer(Time.deltaTime);

        // 동결 연출 중엔 파라미터 갱신까지 얼린다 — Animator.speed 0 은 클립 「시간」만 멈출 뿐이라,
        // 여기서 Speed 파라미터가 0 으로 흘러들면 블렌드 트리가 달리기→대기 자세로 미끄러진다.
        // 위치·회전 추적 기준점도 같이 멈추지만 동결 중엔 몸이 움직이지 않으므로 해제 프레임 델타는 0이다.
        if (controller.IsVisualFrozen || controller.IsCombatDisabled) return;

        Vector3 current = transform.position;
        Vector3 delta = current - previousPosition;
        previousPosition = current;
        delta.y = 0f;

        float deltaTime = Time.deltaTime;
        if (deltaTime > 0f)
        {
            // 서버는 에이전트가 내려는 속도를 그대로 쓴다. 루트모션이 위치를 만드는 쪽이라, 서버에서
            // 위치 델타로 속력을 재면 애니 → 위치 → 속력 → 애니 로 되먹임이 돌아 속도가 발산한다.
            // 클라는 NetworkTransform 이 밀어 준 복제 좌표뿐이므로 지금처럼 위치 델타로 잰다.
            float sampleSpeed = IsServer
                ? ResolveServerSpeedSample()
                : delta.magnitude / deltaTime;

            // 순간이동 표본은 평활에 먹이지 않는다. 기준점은 위에서 이미 옮겼으므로 다음 프레임부터 정상 측정된다.
            if (sampleSpeed <= teleportSpeed)
            {
                // 지수 평활이라 프레임률이 흔들려도 반응 속도가 일정하다.
                float weight = speedSmoothingSeconds > 0f
                    ? 1f - Mathf.Exp(-deltaTime / speedSmoothingSeconds)
                    : 1f;
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, sampleSpeed, weight);
            }
        }

        // 문턱 판정 없이 실속력을 그대로 넘긴다 — 대기·보행·질주 사이는 블렌드 트리가 이어 준다.
        animator.SetFloat(speedHash, smoothedSpeed);
        animator.SetFloat(speedMulHash, ResolveSpeedMultiplier());

        UpdateReach();
        ApplyAnimState();
        UpdateTurn(deltaTime);
        UpdateOneShotLayer(roarLayerIndex, roarTagHash, deltaTime);
        if (attackLayerIndex >= 0) UpdateOneShotLayer(attackLayerIndex, attackTagHash, deltaTime);
    }

    /// <summary>복제 상태에 맞춰 기절은 애니메이터를 멈추고 모델을 눕힌다 — Fall·GetUp 상태가 없는 좀비(Dog·Caged)만. 피격 레이어가 없는 좀비는 경직도 뒤로 젖힌다.</summary>
    private void LateUpdate()
    {
        if (!IsSpawned) return;
        // 경직·기절·기상은 피격 레이어가 클립으로 연출한다(UpdateHitLayer). 레이어나 Fall 상태가 없는 좀비만 종전대로 멈춰 젖힌다.
        if ((controller.IsStunned && !hasFallStates) || (controller.IsCombatDisabled && hitLayerIndex < 0))
        {
            if (!combatPoseActive)
            {
                combatRestRotation = animator.transform.localRotation;
                combatRestPosition = animator.transform.localPosition;
            }
            combatPoseActive = true;
            animator.speed = 0f;
            Quaternion target = combatRestRotation * Quaternion.Euler(controller.IsStunned ? -80f : -12f, 0f, 0f);
            Quaternion rotation = Quaternion.Slerp(animator.transform.localRotation, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
            // 회전축을 모델 원점이 아니라 루트 원점(발밑·NavMesh 접지점)에 둔다 — 모델 원점은 골반 높이라 그대로 돌리면 몸 전체가 공중에 뜬다.
            Vector3 position = rotation * Quaternion.Inverse(combatRestRotation) * combatRestPosition;
            animator.transform.SetLocalPositionAndRotation(position, rotation);
        }
        else if (combatPoseActive)
        {
            animator.transform.SetLocalPositionAndRotation(combatRestPosition, combatRestRotation);
            animator.speed = controller.IsVisualFrozen ? 0f : 1f;
            combatPoseActive = false;
        }

        // 목 시선은 Animator 가 포즈를 쓴 뒤·모델 젖힘 뒤에 얹는다(P-050 · ZombieAnimator.HeadLook.cs).
        UpdateHeadLook(Time.deltaTime);
    }

    /// <summary>
    /// 대기·걷기 변주와 재생 위상을 오브젝트 id 로 정해 한 번만 적용한다. 같은 프레임에 스폰된 좀비는 같은 상태에 같이
    /// 진입해 재생 위상까지 똑같아지고(실측: normalizedTime 이 소수 셋째 자리까지 일치), 대기 클립이 하나뿐인
    /// 구간에서는 그것이 복제 인형으로 보인다. id 는 전 클라이언트 공통이라 어느 화면에서나 같은 좀비가 같은 자세를 쓴다.
    ///
    /// OnNetworkSpawn 이 아니라 첫 Update 에서 거는 이유는, 스폰 시점의 Animator 가 아직 파라미터를 받을 준비가
    /// 되어 있지 않아 쓰기가 조용히 버려지기 때문이다(실측: 22마리 전원 IdleVariant 0).
    /// </summary>
    private void ApplySpawnLook()
    {
        animator.SetFloat(idleVariantHash, NetworkObjectId % idleVariantCount);
        // 걷기 변주는 승산 해시로 고른다 — id 나머지(대기 슬롯)와 상관이 없어 같은 대기 자세끼리 같은 걸음이 되지 않는다
        if (HasParameter(walkVariantHash)) animator.SetFloat(walkVariantHash, NetworkObjectId * idHashMultiplier % idHashBuckets % walkVariantCount);
        animator.Play(locomotionStateHash, 0, NetworkObjectId * idHashMultiplier % idHashBuckets / (float)idHashBuckets);
    }

    /// <summary>
    /// 서버 이동 애니의 속력 표본을 구한다. 기본은 에이전트가 내려는 속도지만, 갈 길이 남아 있는 동안에는
    /// 바닥값 아래로 내려가지 않게 잡아 준다 — 그 값이 곧 보폭이라 눌리는 순간 정체를 풀 이동이 사라지기 때문이다.
    /// 방향은 여전히 회피(desiredVelocity)가 정하므로 서로 비껴가는 성질은 그대로다.
    /// </summary>
    /// <returns>Speed 파라미터에 먹일 속력(m/s).</returns>
    private float ResolveServerSpeedSample()
    {
        UnityEngine.AI.NavMeshAgent agent = controller.Agent;
        float desired = agent.desiredVelocity.magnitude;
        if (!agent.hasPath || agent.remainingDistance <= pathSpeedFloorMeters) return desired;

        return Mathf.Max(desired, Mathf.Min(agent.speed, pathSpeedFloor));
    }

    /// <summary>
    /// 상체 원샷 레이어(포효·공격) 가중치를 굴린다. 연출 시간의 원천은 코드 타이머가 아니라 레이어의 클립 상태
    /// 재생 그 자체다(D-2) — 상태가 해당 태그인 동안만 1 로 올리고, 끝나면 페이드로 내린다.
    /// </summary>
    /// <param name="layerIndex">레이어 인덱스.</param>
    /// <param name="tagHash">그 레이어의 클립 상태 태그 해시.</param>
    /// <param name="deltaTime">이번 프레임 경과 시간(초).</param>
    private void UpdateOneShotLayer(int layerIndex, int tagHash, float deltaTime)
    {
        float target = IsPlayingTag(layerIndex, tagHash) ? 1f : 0f;
        float current = animator.GetLayerWeight(layerIndex);
        float step = oneShotLayerFadeSeconds > 0f ? deltaTime / oneShotLayerFadeSeconds : 1f;
        animator.SetLayerWeight(layerIndex, Mathf.MoveTowards(current, target, step));
    }

    /// <summary>복제된 명중마다 피격 클립을 처음부터 다시 든다 — 경직 중 재명중도 다시 반동한다. 기절시킨 명중은 Fall 이 대신한다(호스트는 상태가 먼저 바뀐다).</summary>
    private void HandleWeaponHitLanded()
    {
        if (hitLayerIndex < 0 || (hasFallStates && controller.IsStunned)) return;
        animator.Play(hitStateHash, hitLayerIndex, 0f);
    }

    /// <summary>
    /// 전신 피격 레이어 가중치를 굴린다. 경직(Staggered)·피격 상태, 기절(Stunned)·Fall 상태, 기상(Rising)·GetUp 상태가 짝으로 맞을 때만 1 —
    /// 상태 조건은 전투 중단이 끝나는 즉시 보행으로 돌리고, 재생 조건은 늦게 접속해 명중을 못 본 클라이언트의 빈 상태가 T포즈를 밀어넣지 않게 한다.
    /// </summary>
    /// <param name="deltaTime">이번 프레임 경과 시간(초).</param>
    private void UpdateHitLayer(float deltaTime)
    {
        if (hitLayerIndex < 0) return;
        int playing = animator.GetCurrentAnimatorStateInfo(hitLayerIndex).shortNameHash;
        ZombieStateKind state = controller.CurrentState;
        bool reacting = (state == ZombieStateKind.Staggered && playing == hitStateHash)
            || (hasFallStates && ((state == ZombieStateKind.Stunned && playing == fallStateHash)
                || (state == ZombieStateKind.Rising && (playing == getUpStateHash || playing == fallStateHash)))); // 크로스페이드 중엔 현재 상태가 아직 Fall
        float current = animator.GetLayerWeight(hitLayerIndex);
        float step = oneShotLayerFadeSeconds > 0f ? deltaTime / oneShotLayerFadeSeconds : 1f;
        animator.SetLayerWeight(hitLayerIndex, Mathf.MoveTowards(current, reacting ? 1f : 0f, step));
    }

    /// <summary>레이어의 현재 또는 다음 상태가 해당 태그인지 판정한다(전이 중에도 참).</summary>
    /// <param name="layerIndex">레이어 인덱스.</param>
    /// <param name="tagHash">태그 해시.</param>
    /// <returns>재생 중이면 true.</returns>
    private bool IsPlayingTag(int layerIndex, int tagHash)
    {
        return animator.GetCurrentAnimatorStateInfo(layerIndex).tagHash == tagHash
            || animator.GetNextAnimatorStateInfo(layerIndex).tagHash == tagHash;
    }

    /// <summary>
    /// 제자리 회전 파라미터를 굴린다(A3). 회전을 만드는 주체는 코드가 아니라 Base Layer 「Turn」 상태의
    /// 턴 클립 루트 회전이고(ZombieRootMotion), 이 메서드는 어느 쪽으로 얼마나 세게 돌지만 정해 준다.
    ///
    /// TurnBlend 의 출처는 서버와 클라가 다르다. 서버는 「목표까지 남은 각도」(의도)를 쓴다 — 측정된 회전량을
    /// 쓰면 돌기 전에는 0 이라 Turn 상태에 영영 못 들어가는 교착이 된다. 클라는 복제된 회전의 실제 변화율로
    /// 잰다(Speed 파라미터의 서버·클라 분기와 같은 이유).
    ///
    /// 발이 미끄러지지 않을 조건은 <c>몸의 회전 = 클립 회전 × 재생 배속</c> 인데, A3 이후로는 몸의 회전이
    /// 곧 클립 회전이라 항등식이 된다. 배속은 「얼마나 빨리 돌 것인가」만 정한다.
    /// </summary>
    /// <param name="deltaTime">이번 프레임 경과 시간(초).</param>
    private void UpdateTurn(float deltaTime)
    {
        float currentYaw = controller.transform.eulerAngles.y;
        if (deltaTime > 0f)
        {
            float sample = Mathf.DeltaAngle(previousYaw, currentYaw) / deltaTime;
            float weight = turnSmoothingSeconds > 0f
                ? 1f - Mathf.Exp(-deltaTime / turnSmoothingSeconds)
                : 1f;
            smoothedYawRate = Mathf.Lerp(smoothedYawRate, sample, weight);
        }
        previousYaw = currentYaw;

        animator.SetFloat(turnBlendHash, ResolveTurnBlend());
        animator.SetFloat(turnMulHash, ResolveTurnMultiplier());
    }

    /// <summary>회전 세기·방향(-1~1)을 구한다. 서버는 남은 각도, 클라는 복제 회전의 변화율이 원천이다.</summary>
    /// <returns>Turn 트리에 넘길 부호 있는 세기.</returns>
    private float ResolveTurnBlend()
    {
        if (IsServer)
        {
            // 데드존 안이면 상태 머신이 이미 0 으로 준다 — 여기서 따로 자르지 않는다.
            return Mathf.Clamp(controller.FacingErrorDegrees / Mathf.Max(1f, turnBlendSaturationDegrees), -1f, 1f);
        }

        if (Mathf.Abs(smoothedYawRate) < turnClientMinDegreesPerSecond) return 0f;

        // 선회 속도의 절반만 넘어도 최대 세기로 본다 — 복제 보간이 실제 각속도를 늘 조금 낮춰 오기 때문이다.
        float saturation = Mathf.Max(1f, controller.Profile.TurnSpeedDegreesPerSecond * 0.5f);
        return Mathf.Clamp(smoothedYawRate / saturation, -1f, 1f);
    }

    /// <summary>
    /// 요구 선회 속도와 클립 고유 회전 속도의 비로 턴 재생 배속을 구한다. 회전을 클립이 만들므로 이 배속이 곧 실제 각속도다.
    /// 서버는 요구값(배회 대기의 역산 속도 포함)을, 클라는 복제된 회전의 실제 변화율을 쓴다 — Speed·TurnBlend 와 같은 분기다.
    /// </summary>
    /// <returns>Turn 상태에 넘길 재생 배속.</returns>
    private float ResolveTurnMultiplier()
    {
        if (turnClipDegreesPerSecond <= 0f) return 1f;

        float desired = IsServer ? controller.CurrentTurnDegreesPerSecond : Mathf.Abs(smoothedYawRate);
        return Mathf.Clamp(desired / turnClipDegreesPerSecond, turnMultiplierMin, turnMultiplierMax);
    }

    /// <summary>복제된 상태가 바뀐 즉시 반영한다. 포효·공격은 한 프레임도 늦으면 안 되는 연출이다.</summary>
    /// <param name="kind">복제된 현재 좀비 상태.</param>
    private void HandleStateChanged(ZombieStateKind kind)
    {
        ZombieStateKind previousKind = lastStateKind;
        lastStateKind = kind;

        // 포효 취소 — 발각 전환이 추격 없이 배회로 끝났고 종이 원할 때만(Teacher 경고 취소) 포효 레이어를 비운다.
        // 추격·도주(Oiler)·경직 등 다른 출구와 옵트인하지 않은 종은 종전대로 클립 끝까지 재생된다.
        if (hasRoarEmptyState && EmptyHouse.Zombie.Rules.RoarInterruptRules.ShouldInterrupt(
                previousKind == ZombieStateKind.RoarTransition, kind == ZombieStateKind.Wander, controller.InterruptsRoarOnCancel))
        {
            animator.ResetTrigger(roarTriggerHash);
            animator.CrossFadeInFixedTime(roarEmptyStateHash, roarInterruptFadeSeconds, roarLayerIndex);
        }

        // 포효는 상태 유지가 아니라 발각 「순간」의 원샷 연출이다 — 전이 에지에서만 트리거를 세운다.
        // 늦게 접속한 클라이언트는 이 에지를 놓치지만, 지나간 포효를 되감을 이유가 없어 그대로 둔다.
        if (kind == ZombieStateKind.RoarTransition)
        {
            animator.SetTrigger(roarTriggerHash);
        }

        // 공격도 상체 원샷이다. 헛손질 뒤 Chase ↔ Attack 을 0.3초 주기로 오가며 재진입하므로, 이미 무는 중이면
        // 다시 걸지 않는다 — 걸면 클립 앞 10% 만 되풀이돼 팔만 덜컥거린다. 명중 판정은 애니와 무관하게 상태 머신이 한다.
        if (kind == ZombieStateKind.Attack && attackLayerIndex >= 0 && !IsPlayingTag(attackLayerIndex, attackTagHash))
        {
            animator.SetTrigger(attackTriggerHash);
        }

        // 기절·기상은 전신 레이어의 Fall·GetUp 을 Play 로 든다(전이 없음, 끝 프레임 유지). 스폰 전 에지는 첫 Update 가 현재 상태로 다시 건다.
        if (hasFallStates && spawnLookApplied)
        {
            if (kind == ZombieStateKind.Stunned)
            {
                animator.Play(fallStateHash, hitLayerIndex, 0f);
                // 누워 있는 동안 Base Layer 를 대기로 내린다 — Update 가 전투 중단에서 조기 반환해 Speed 가 추격 값에 멈춰 있으면 일어난 직후 제자리 달리기가 비친다
                smoothedSpeed = 0f;
                animator.SetFloat(speedHash, 0f);
            }
            // 일어나기는 크로스페이드 — 눕기 끝 자세와 일어나기 시작 자세(둘 다 서 있는 끝을 루트에 맞춘 터라 누운 위치가 0.3~0.5m 다르다)를 구르는 동안 섞는다
            else if (kind == ZombieStateKind.Rising) animator.CrossFadeInFixedTime(getUpStateHash, getUpBlendSeconds, hitLayerIndex, 0f);
        }

        ApplyAnimState();
    }

    /// <summary>
    /// 추격 대치 몸짓을 복제 플래그로 켠다(P-036) — <see cref="ZombieController.IsChaseHolding"/> 를 IsReaching 에 쓴다.
    /// 파라미터가 없는 컨트롤러면 쓰지 않고, 값이 직전과 같으면 쓰지 않는다. 호출은 Update 의 조기 반환 뒤·ApplyAnimState 앞.
    /// </summary>
    private void UpdateReach()
    {
        // 매 프레임 호출이라 진입 트레이스를 두지 않는다.
        if (!hasReachParam) return;
        bool reaching = controller.IsChaseHolding;
        if (reaching == lastReachValue) return;
        lastReachValue = reaching;
        animator.SetBool(reachHash, reaching);
    }

    /// <summary>컨트롤러에 해당 파라미터가 있는지 — 없는 파라미터에 SetBool 하면 경고 스팸이라 한 번만 확인해 캐시한다.</summary>
    /// <param name="nameHash">파라미터 이름 해시.</param>
    /// <returns>있으면 true.</returns>
    private bool HasParameter(int nameHash)
    {
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == nameHash) return true;
        }
        return false;
    }

    /// <summary>결정된 애니메이터 상태를 반영한다. 값이 그대로면 쓰지 않는다.</summary>
    private void ApplyAnimState()
    {
        int next = ResolveAnimState();
        if (next == appliedAnimState) return;

        appliedAnimState = next;
        animator.SetInteger(stateHash, next);
    }

    /// <summary>
    /// 블렌드 트리가 실제로 만들어내는 속력과 실이동 속력의 비로 재생 배속을 구한다.
    /// 두 클립을 섞으면 보폭이 짧아져 실이동보다 느리게 걷는 탓에(중간 구간 최대 25%),
    /// 그만큼 빠르게 돌려야 발이 지면에 붙는다.
    /// </summary>
    /// <returns>Locomotion 상태에 넘길 재생 배속.</returns>
    private float ResolveSpeedMultiplier()
    {
        float natural = blendedSpeedCurve.Evaluate(smoothedSpeed);
        if (natural <= minNaturalSpeed) return 1f;

        return Mathf.Clamp(smoothedSpeed / natural, speedMultiplierMin, speedMultiplierMax);
    }

    /// <summary>
    /// 애니메이터 상태 인덱스를 결정한다. 포효·공격은 제자리 연출이라 상태로 고르고,
    /// 나머지는 전부 Locomotion 으로 보낸다 — 그 안의 대기·보행·질주는 Speed 가 가른다.
    /// </summary>
    /// <returns>ZombieLocomotion 컨트롤러의 State 파라미터 값.</returns>
    private int ResolveAnimState()
    {
        switch (controller.CurrentState)
        {
            // 포효·공격은 base 상태가 아니라 상체 레이어 원샷이다(D-2) — base 는 Locomotion 을 유지해 하체가 계속 뛴다.
            // State 3 은 base 컨트롤러에 듣는 상태가 더 없지만, Caged 컨트롤러의 base Attack 이 아직 쓰므로 그대로 낸다.
            case ZombieStateKind.Attack:
                return animAttack;
            default:
                return animLocomotion;
        }
    }
}
