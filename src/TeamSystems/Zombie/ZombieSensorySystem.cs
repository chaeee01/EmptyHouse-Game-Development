using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 좀비의 시각·청각을 한 서버 프레임 분량으로 수집해 <see cref="ZombiePerceptionBuilder"/> 에 채운다.
/// 종을 분기하지 않는다 — 채널 유무는 <see cref="ZombieProfileSO.HasVision"/>·
/// <see cref="ZombieProfileSO.HasHearing"/> 플래그가 정하고, 종 전용 자극은 컨트롤러 파생의 훅②가 얹는다.
/// 눈·귀가 없는 종은 배율 0 이 아니라 루프 자체를 돌지 않는다.
/// </summary>
public class ZombieSensorySystem : NetworkBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private EmptyHouse.NoiseSystem.NoiseDetectedEventChannelSO noiseDetectedChannel;
    [SerializeField] private EmptyHouse.NoiseSystem.NoisePropagationSettingsSO noiseSettings; // 전파 상수(감쇠·차폐·합산 창)의 유일한 원천. 좀비 프로파일은 dB 임계만 들고 전파는 소음 도메인이 소유한다

    private readonly List<TimedDetectedNoise> pendingDetectedNoises = new List<TimedDetectedNoise>();
    private readonly ZombiePerceptionBuilder builder = new ZombiePerceptionBuilder(); // 좀비당 1개 재사용 — 매 프레임 새로 만들면 GC 가 돈다

    private float lastReachedDb;                          // 디버그 — 마지막으로 접수된 도달 dB(가청 임계 미달 소음은 전파 단계에서 걸러져 오지 않는다)
    private float lastReachedAt = float.NegativeInfinity; // 디버그 — 그 접수 시각(Time.time)

    public float LastReachedDb => lastReachedDb;
    public float LastReachedAt => lastReachedAt;

    private readonly struct TimedDetectedNoise
    {
        public readonly EmptyHouse.NoiseSystem.NoiseDetectedEvent Noise;
        public readonly float ReceivedAt;

        public TimedDetectedNoise(EmptyHouse.NoiseSystem.NoiseDetectedEvent noise, float receivedAt)
        {
            Noise = noise;
            ReceivedAt = receivedAt;
        }
    }

    /// <summary>서버에서만, 그리고 청각 채널이 있는 종에서만 소음 구독을 건다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // 귀가 없는 종(Dog)은 구독 자체를 하지 않는다 — 들어와도 버릴 이벤트를 큐에 쌓을 이유가 없다.
        if (!controller.Profile.HasHearing) return;

        // 청각의 유일한 입구는 전파가 끝난 대상별 채널이다 — 거리 감쇠·차폐는 NoisePropagationSystem 이 이미 반영했다.
        // 자기 NetworkObjectId 로 키 구독한다 — 채널이 대상 좀비 핸들러 하나만 부르므로 수신 측 필터가 없다.
        noiseDetectedChannel.Subscribe(NetworkObjectId, OnNoiseDetected);
    }

    /// <summary>소음 구독을 해제한다. 구독하지 않은 종·클라에서는 무동작.</summary>
    public override void OnNetworkDespawn()
    {
        noiseDetectedChannel.Unsubscribe(NetworkObjectId);
    }

    /// <summary>전파가 끝나 자신에게 도달한 소음을 큐에 담는다. 채널이 대상별로 전달하므로 자기 것만 들어온다.</summary>
    /// <param name="noiseEvent">도달 소음.</param>
    private void OnNoiseDetected(EmptyHouse.NoiseSystem.NoiseDetectedEvent noiseEvent)
    {
        if (!IsServer) return;
        pendingDetectedNoises.Add(new TimedDetectedNoise(noiseEvent, Time.time));
        lastReachedDb = noiseEvent.ReachedDb;
        lastReachedAt = Time.time;
    }

    /// <summary>
    /// 이번 프레임의 시각·청각을 수집해 조립체에 채운다. 종 전용 자극(훅②)은 컨트롤러가
    /// 이 반환값 위에 얹으므로 여기서 얼리지(Build) 않는다.
    /// </summary>
    /// <returns>좀비당 재사용되는 조립체. 호출자가 훅② 이후 Build() 로 얼린다.</returns>
    public ZombiePerceptionBuilder ServerCollectPerception()
    {
        builder.Reset();
        if (!IsServer) return builder;

        ZombieProfileSO profile = controller.Profile;

        if (profile.HasVision)
        {
            EvaluateVision(profile);
        }

        if (profile.HasHearing)
        {
            PruneExpiredNoises();
            EvaluateHearing(profile);
        }

        return builder;
    }

    /// <summary>시야 원뿔·LoS 판정으로 시각 자극을 조립체에 채운다.</summary>
    /// <param name="profile">이 좀비의 프로파일.</param>
    private void EvaluateVision(ZombieProfileSO profile)
    {
        Vector3 origin = controller.VisionOrigin.position;
        Vector3 forward = controller.VisionOrigin.forward;
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        flatForward = flatForward.sqrMagnitude > 0.000001f ? flatForward.normalized : forward; // 시야 원점이 수직으로 누워 있으면(비정상) 3D 정면으로 폴백
        float cosThreshold = Mathf.Cos(profile.VisionAngle * 0.5f * Mathf.Deg2Rad);
        float bestGain = 0f;
        float nearestDistance = float.MaxValue;
        IZombiePerceptionSource nearestTarget = null;
        Vector3 nearestPosition = default;
        bool instantDetection = false;
        float nearestProximitySqr = float.MaxValue;
        Vector3 proximityPosition = default;

        IReadOnlyList<IZombiePerceptionSource> perceptionSources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < perceptionSources.Count; i++)
        {
            IZombiePerceptionSource source = perceptionSources[i];
            if (source == null || source.Root == null) continue;
            PlayerSignals signals = ReadSignals(source);

            // 위장은 시각 채널 그 자체를 끊는다(좀비공통 3-7). 눈이 있는 종의 시각 루프에서만
            // 성립하는 판정이라 여기 남는다 — 청각·직접 자극은 위장과 무관하다.
            // latch 로 문 현재 타겟도 예외가 아니다(B-084 번복 — 사용자 확정 2026-09-30) — 발각된 채로 위장해도 시각 자극이 즉시 끊긴다.
            if (signals.IsSpectator) continue;
            if (signals.IsDisguised) continue;

            Vector3 targetPosition = source.EyePosition;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon || distance > profile.VisionDistance) continue;

            Vector3 direction = toTarget / distance;
            // 원뿔은 수평각(좌우 · VisionAngle)과 수직각(상하 · VisionVerticalAngle)을 따로 잰다 — 3D 각도 하나로 재면
            // 가까운 높은 단 위의 플레이어가 상하각만으로 원뿔을 벗어나 추격 자극이 끊긴다(2026-10-17 · 사용자 확정).
            // 수평 성분이 거의 없으면(바로 위·아래) 수평각은 통과로 보고 층 구분은 LoS 에 맡긴다.
            Vector3 flatDirection = direction;
            flatDirection.y = 0f;
            float flatMagnitude = flatDirection.magnitude;
            float facingDot = flatMagnitude > 0.001f ? Vector3.Dot(flatForward, flatDirection / flatMagnitude) : 1f;
            float verticalDegrees = Mathf.Atan2(Mathf.Abs(direction.y), flatMagnitude) * Mathf.Rad2Deg;
            if (facingDot < cosThreshold || verticalDegrees > profile.VisionVerticalAngle * 0.5f)
            {
                // 원뿔 밖이어도 밀착하면 기척을 느낀다 — 발각이 아니라 조사다(사용자 확정 2026-09-30, 좀비공통 3-2 번복 · B-133).
                // 거리는 수평(XZ)으로만 잰다 — 좀비 루트는 발, 플레이어 루트는 캡슐 꼭대기라 높이가 달라
                // 3D 거리로는 밀착해도 반경에 들지 못한다. 웅크림에도 흔들리지 않고, 층 구분은 LoS 가 맡는다.
                Vector3 proximityOffset = source.Root.position - controller.transform.position;
                proximityOffset.y = 0f;
                float proximitySqr = proximityOffset.sqrMagnitude;
                if (proximitySqr <= profile.VisProximityRange * profile.VisProximityRange
                    && proximitySqr < nearestProximitySqr
                    && HasLineOfSight(profile, origin, direction, distance, source.Root))
                {
                    nearestProximitySqr = proximitySqr;
                    proximityPosition = source.Root.position;
                }

                continue;
            }

            if (!HasLineOfSight(profile, origin, direction, distance, source.Root)) continue;

            float gain = ComputeVisualGain(profile, distance, facingDot, signals);
            if (gain > bestGain) bestGain = gain;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = source;
                nearestPosition = targetPosition;
            }

            if (distance <= profile.VisInstantRange)
            {
                instantDetection = true;
            }
        }

        // 밀착 기척은 직접 자극 바닥으로 싣는다 — 타겟은 물지 않고(조사), 위치만 단서로 남긴다.
        // 코어가 max 합류로 받으므로 원뿔 안 시각 자극과 순서를 다투지 않는다.
        if (nearestProximitySqr < float.MaxValue)
        {
            builder.DirectSuspicionFloor = Mathf.Max(builder.DirectSuspicionFloor, profile.ThInvestigate);
            builder.DirectPosition = proximityPosition;
        }

        if (nearestTarget == null) return;

        builder.HasVisualStimulus = true;
        builder.HasInstantVisualDetection = instantDetection;
        builder.VisualGainPerSecond = bestGain;
        builder.VisualPosition = nearestPosition;
        builder.VisualTarget = nearestTarget;
    }

    /// <summary>큐에 남은 소음 중 도달 dB 가 가장 큰 하나를 골라 청각 자극을 조립체에 채운다.</summary>
    /// <param name="profile">이 좀비의 프로파일.</param>
    private void EvaluateHearing(ZombieProfileSO profile)
    {
        if (pendingDetectedNoises.Count == 0) return;

        float bestEffectiveDb = float.MinValue;
        EmptyHouse.NoiseSystem.NoiseDetectedEvent bestDetectedNoise = default;

        for (int i = 0; i < pendingDetectedNoises.Count; i++)
        {
            EmptyHouse.NoiseSystem.NoiseDetectedEvent detected = pendingDetectedNoises[i].Noise;
            if (detected.ReachedDb <= bestEffectiveDb) continue;
            bestEffectiveDb = detected.ReachedDb;
            bestDetectedNoise = detected;
        }

        if (bestEffectiveDb < profile.HearMinDb) return;

        IZombiePerceptionSource target = ResolveNetworkSource(bestDetectedNoise.SourceId);

        // 사망(관전) 대상은 어느 종에게도 추적 대상이 아니다(E2).
        // 위장이 타겟을 끊는 종(DisguiseBreaksTargeting — Walker·Stalker·Listener)에서는 청각 타겟까지 무효화한다 —
        // Listener 도 Walker 와 같이 위장자를 소리로 물지 않는다(E15 이탈 — 사용자 확정 2026-10-06). Dog 만 꺼져 있다.
        // ChaseDisguisedOnAuditoryDetect(기획 이탈 플래그 — 기본 OFF)가 켜지면 무효화하지 않는다(D-4 ③).
        // latch 로 문 현재 타겟도 예외가 아니다(B-084 번복 — 사용자 확정 2026-09-30).
        bool disguiseBreaksTarget = profile.DisguiseBreaksTargeting && !profile.ChaseDisguisedOnAuditoryDetect;
        if (target != null
            && (target.Root == null || target.IsSpectator
                || (disguiseBreaksTarget && target.IsDisguised)))
        {
            target = null;
        }

        // 플래그 ON 전용 — 발각 dB 에 닿았는데 소음원이 타겟이 못 되면(문·투척물·위장 소음원 등)
        // 최근접 위장자를 대신 확보한다("남의 소음으로 청각 발각 시 위장 중인 최근접 플레이어도 확보").
        // OFF(스펙 정본)면 E8 그대로 — 조사만 유발하고 위장자는 물지 않는다.
        if (profile.ChaseDisguisedOnAuditoryDetect
            && target == null
            && bestEffectiveDb >= profile.HearDetectDb)
        {
            target = FindNearestDisguisedSource();
        }

        builder.HasAuditoryStimulus = true;
        builder.AuditoryEffectiveDb = bestEffectiveDb;
        builder.AuditoryPosition = bestDetectedNoise.Origin;
        builder.AuditoryTarget = target;
    }

    /// <summary>최근접 위장자를 찾는다. ChaseDisguisedOnAuditoryDetect ON 전용(D-4 ③).</summary>
    /// <returns>가장 가까운 위장 중 지각 소스 — 없으면 null.</returns>
    private IZombiePerceptionSource FindNearestDisguisedSource()
    {
        Vector3 origin = controller.transform.position;
        IZombiePerceptionSource nearest = null;
        float nearestSqr = float.MaxValue;

        IReadOnlyList<IZombiePerceptionSource> sources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            IZombiePerceptionSource source = sources[i];
            if (source == null || source.Root == null || source.IsSpectator || !source.IsDisguised) continue;

            float sqr = (source.Root.position - origin).sqrMagnitude;
            if (sqr >= nearestSqr) continue;

            nearestSqr = sqr;
            nearest = source;
        }

        return nearest;
    }

    /// <summary>소음원 NetworkObjectId 를 지각 소스로 해석한다. 플레이어가 아닌 소음원(문·투척물 등)은 null 이다.</summary>
    /// <param name="sourceId">소음원 NetworkObjectId.</param>
    /// <returns>해당 지각 소스 — 없으면 null.</returns>
    private IZombiePerceptionSource ResolveNetworkSource(ulong sourceId)
    {
        IReadOnlyList<IZombiePerceptionSource> sources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] != null && sources[i].NetworkObjectId == sourceId) return sources[i];
        }

        return null;
    }

    /// <summary>시야가 대상까지 뚫려 있는지 검사한다.</summary>
    /// <param name="profile">이 좀비의 프로파일.</param>
    /// <param name="origin">시야 원점.</param>
    /// <param name="direction">대상 방향(정규화).</param>
    /// <param name="distance">대상까지 거리.</param>
    /// <param name="playerRoot">대상 루트.</param>
    /// <returns>가로막힌 것이 없으면 true.</returns>
    private bool HasLineOfSight(ZombieProfileSO profile, Vector3 origin, Vector3 direction, float distance, Transform playerRoot)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, profile.ObstructionMask, QueryTriggerInteraction.Ignore);
        float nearestPlayerHit = float.MaxValue;
        float nearestObstacleHit = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null || hitTransform.IsChildOf(controller.transform)) continue;

            if (hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot))
            {
                nearestPlayerHit = Mathf.Min(nearestPlayerHit, hits[i].distance);
            }
            else
            {
                nearestObstacleHit = Mathf.Min(nearestObstacleHit, hits[i].distance);
            }
        }

        return nearestObstacleHit >= nearestPlayerHit || nearestObstacleHit == float.MaxValue;
    }

    /// <summary>거리·정면·조명·자세 배율을 곱해 초당 의심도 증가율을 낸다.</summary>
    /// <param name="profile">이 좀비의 프로파일.</param>
    /// <param name="distance">대상까지 거리.</param>
    /// <param name="facingDot">시야 정면과의 내적.</param>
    /// <param name="signals">대상의 지각 신호.</param>
    /// <returns>초당 증가율.</returns>
    private float ComputeVisualGain(ZombieProfileSO profile, float distance, float facingDot, PlayerSignals signals)
    {
        float nearThreshold = profile.VisionDistance * 0.25f;
        float distanceFactor = distance <= nearThreshold
            ? profile.VisDistNear
            : Mathf.Lerp(profile.VisDistNear, profile.VisDistFar,
                Mathf.InverseLerp(nearThreshold, profile.VisionDistance, distance));

        float frontFactor = facingDot >= Mathf.Cos(20f * Mathf.Deg2Rad) ? profile.VisFront : 1f;
        float lightFactor = signals.IsFlashlightAimingAtZombie
            ? profile.VisLightFlashlight
            : profile.EnvironmentIsBright ? profile.VisLightBright : profile.VisLightDark;
        float poseFactor = !signals.IsMoving
            ? profile.VisPoseIdle
            : signals.IsCrouching ? profile.VisPoseCrouch : profile.VisPoseWalk;

        return profile.VisGainBase * distanceFactor * frontFactor * lightFactor * poseFactor;
    }

    /// <summary>합산 창을 벗어난 소음을 큐에서 버린다.</summary>
    private void PruneExpiredNoises()
    {
        float cutoff = Time.time - noiseSettings.SumWindowSeconds;
        for (int i = pendingDetectedNoises.Count - 1; i >= 0; i--)
        {
            if (pendingDetectedNoises[i].ReceivedAt < cutoff) pendingDetectedNoises.RemoveAt(i);
        }
    }

    /// <summary>지각 소스의 신호를 한 번에 읽어 구조체로 묶는다.</summary>
    /// <param name="source">지각 소스.</param>
    /// <returns>이번 프레임의 신호 묶음.</returns>
    private PlayerSignals ReadSignals(IZombiePerceptionSource source)
    {
        return new PlayerSignals(
            source.IsDisguised,
            source.IsSpectator,
            source.IsCrouching,
            source.IsMoving,
            source.IsFlashlightAimingAt(controller.transform));
    }

    private readonly struct PlayerSignals
    {
        public readonly bool IsDisguised;
        public readonly bool IsSpectator;
        public readonly bool IsCrouching;
        public readonly bool IsMoving;
        public readonly bool IsFlashlightAimingAtZombie;

        public PlayerSignals(bool isDisguised, bool isSpectator, bool isCrouching, bool isMoving, bool isFlashlightAimingAtZombie)
        {
            IsDisguised = isDisguised;
            IsSpectator = isSpectator;
            IsCrouching = isCrouching;
            IsMoving = isMoving;
            IsFlashlightAimingAtZombie = isFlashlightAimingAtZombie;
        }
    }
}
