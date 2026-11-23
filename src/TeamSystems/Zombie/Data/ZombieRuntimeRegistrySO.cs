using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEngine;

/// <summary>
/// Border-style runtime set for zombie-domain scene members.
/// Scene objects register themselves, so hot paths never scan hierarchies or physics colliders
/// to discover another logic component.
/// </summary>
[CreateAssetMenu(fileName = "SO_Zombie_RuntimeRegistry", menuName = "Game/Zombie Runtime Registry")]
public sealed class ZombieRuntimeRegistrySO : ScriptableObject
{
    private readonly List<ZombieController> zombies = new List<ZombieController>();
    private readonly List<ZombieSquadSync> squads = new List<ZombieSquadSync>();
    private readonly List<IZombiePerceptionSource> perceptionSources = new List<IZombiePerceptionSource>();
    private readonly List<IStalkerFreezeVolume> freezeVolumes = new List<IStalkerFreezeVolume>(); // 정지 볼륨 — 레벨 콜라이더·플레이어 스포트라이트 원뿔이 종류 구분 없이 한 집합(좀비타입 4-2). 해제는 각 구현의 OnDisable 이 책임진다
    private readonly List<MapLurkerSlot> lurkerSlots = new List<MapLurkerSlot>(); // Lurker 은신 자리(방 프리팹 저작) — 스폰·재배치 풀. 자리가 OnEnable 에서 자기등록한다

    /// <summary>
    /// 씬에 살아 있는 모든 좀비. 다른 집합과 달리 서버 전용이 아니다 —
    /// 읽는 쪽(<see cref="ZombieThreatAudioDirector"/>)이 각 클라이언트의 연출이라,
    /// 복제된 의심도·상태를 훑으려면 비서버 인스턴스도 목록에 있어야 한다.
    /// </summary>
    public IReadOnlyList<ZombieController> Zombies => zombies;
    public IReadOnlyList<ZombieSquadSync> Squads => squads;
    public IReadOnlyList<IZombiePerceptionSource> PerceptionSources => perceptionSources;
    public IReadOnlyList<IStalkerFreezeVolume> FreezeVolumes => freezeVolumes;
    public IReadOnlyList<MapLurkerSlot> LurkerSlots => lurkerSlots; // 서버 전용 소비(스포너·Lurker 재배치). 클라이언트 인스턴스도 등록되지만 읽는 쪽이 없다

    /// <summary>등록 직후 로컬 시각 관리자가 캐시를 구성할 때 사용한다.</summary>
    public event System.Action<ZombieController> ZombieRegistered;
    /// <summary>풀 반환 시 다음 정기 검사 전에 렌더링 차단을 복원한다.</summary>
    public event System.Action<ZombieController> ZombieUnregistered;

    public void RegisterZombie(ZombieController zombie)
    {
        if (zombie == null || zombies.Contains(zombie)) return;
        zombies.Add(zombie);
        ZombieRegistered?.Invoke(zombie);
    }

    public void UnregisterZombie(ZombieController zombie)
    {
        if (zombies.Remove(zombie)) ZombieUnregistered?.Invoke(zombie);
    }

    public void RegisterSquad(ZombieSquadSync squad)
    {
        if (squad != null && !squads.Contains(squad)) squads.Add(squad);
    }

    public void UnregisterSquad(ZombieSquadSync squad) => squads.Remove(squad);

    public void RegisterPerceptionSource(IZombiePerceptionSource source)
    {
        if (source != null && !perceptionSources.Contains(source)) perceptionSources.Add(source);
    }

    public void UnregisterPerceptionSource(IZombiePerceptionSource source) => perceptionSources.Remove(source);

    /// <summary>Stalker 정지 볼륨을 집합에 올린다(레벨 볼륨·스포트라이트 원뿔 컴포넌트가 OnEnable 에서 호출).</summary>
    /// <param name="volume">등록할 볼륨.</param>
    public void RegisterFreezeVolume(IStalkerFreezeVolume volume)
    {
        if (volume != null && !freezeVolumes.Contains(volume)) freezeVolumes.Add(volume);
    }

    /// <summary>Stalker 정지 볼륨을 집합에서 내린다.</summary>
    /// <param name="volume">해제할 볼륨.</param>
    public void UnregisterFreezeVolume(IStalkerFreezeVolume volume)
    {
        freezeVolumes.Remove(volume);
    }

    /// <summary>로그용 볼륨 이름 — 컴포넌트면 GO 이름, 아니면 타입 이름.</summary>
    /// <param name="volume">이름을 뽑을 볼륨.</param>
    /// <returns>로그에 쓸 이름.</returns>
    private static string FreezeVolumeName(IStalkerFreezeVolume volume)
        => (volume as Component)?.name ?? volume?.GetType().Name ?? "null";

    /// <summary>Lurker 은신 자리를 집합에 올린다(방 프리팹의 자리 컴포넌트가 OnEnable 에서 호출).</summary>
    /// <param name="slot">등록할 자리.</param>
    public void RegisterLurkerSlot(MapLurkerSlot slot)
    {
        if (slot != null && !lurkerSlots.Contains(slot)) lurkerSlots.Add(slot);
    }

    /// <summary>Lurker 은신 자리를 집합에서 내린다.</summary>
    /// <param name="slot">해제할 자리.</param>
    public void UnregisterLurkerSlot(MapLurkerSlot slot) => lurkerSlots.Remove(slot);

    private void OnDisable()
    {
        while (zombies.Count > 0) UnregisterZombie(zombies[zombies.Count - 1]);
        squads.Clear();
        perceptionSources.Clear();
        freezeVolumes.Clear();
        lurkerSlots.Clear();
    }
}
