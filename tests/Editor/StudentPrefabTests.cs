using EmptyHouse.NoiseSystem;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 실제 Student 프리팹·프로파일·등록 검증 — 컨트롤러·프로파일 계약, 비명 소음 채널 배선, 공통 컴포넌트 참조,
/// 네트워크 해시 고유성·NetworkPrefab 등록. 씬·네트워크는 실행하지 않는다.
/// </summary>
public sealed class StudentPrefabTests
{
    private const string studentPrefabPath = "Assets/02. Prefab/Zombie/Zombie_Student.prefab";
    private const string noiseChannelPath = "Assets/03. ScriptableObjects/Events/Noise/SO_Event_NoiseEmitted.asset";
    private const string networkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    private GameObject root; // 임시 프리팹 편집 씬의 Student 루트

    [SetUp]
    public void SetUp() => root = PrefabUtility.LoadPrefabContents(studentPrefabPath);

    [TearDown]
    public void TearDown()
    {
        if (root != null) PrefabUtility.UnloadPrefabContents(root);
    }

    [Test]
    public void 루트_컨트롤러는_Student_하나이고_프로파일_계약을_지킨다()
    {
        ZombieController[] controllers = root.GetComponents<ZombieController>();
        Assert.That(controllers.Length, Is.EqualTo(1));
        var student = controllers[0] as ZombieControllerStudent;
        Assert.That(student, Is.Not.Null, "루트 컨트롤러가 ZombieControllerStudent 가 아님");

        StudentProfileSO profile = student.StudentProfile;
        Assert.That(profile.Species, Is.EqualTo(ZombieSpecies.Student));
        Assert.That(profile.HasVision, Is.True, "시야로 발견");
        Assert.That(profile.HasHearing, Is.False, "자기 비명에 반응하지 않게 청각 끔");
        Assert.That(profile.SyncEnabled, Is.False, "무리 동조 없음");
        Assert.That(profile.ScreamWindupSeconds, Is.GreaterThan(0f));
        Assert.That(profile.ScreamNoiseDb, Is.GreaterThan(profile.HearMinDb), "비명이 Walker 류 가청 하한보다 커야 주변이 반응한다");
        Assert.That(student.InterruptsRoarOnCancel, Is.True, "예고 취소 시 포효 중단 옵트인");
    }

    [Test]
    public void 비명_소음_채널이_서버_소음_파이프라인에_배선돼_있다()
    {
        var student = root.GetComponent<ZombieControllerStudent>();
        var channel = new SerializedObject(student).FindProperty("noiseEmittedChannel").objectReferenceValue;
        Assert.That(channel, Is.SameAs(AssetDatabase.LoadAssetAtPath<NoiseEmittedEventChannelSO>(noiseChannelPath)));
    }

    [Test]
    public void 공통_컴포넌트의_컨트롤러_참조가_모두_Student를_가리킨다()
    {
        var student = root.GetComponent<ZombieController>();
        int references = 0;
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            Assert.That(component, Is.Not.Null, "누락 스크립트");
            SerializedProperty p = new SerializedObject(component).GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                Assert.That(p.objectReferenceValue != null || p.objectReferenceInstanceIDValue == 0, Is.True, $"끊긴 참조 {component.GetType().Name}.{p.propertyPath}");
                if (p.objectReferenceValue is ZombieController referenced)
                {
                    references++;
                    Assert.That(referenced, Is.SameAs(student), $"{component.GetType().Name}.{p.propertyPath}");
                }
            }
        }
        Assert.That(references, Is.EqualTo(5), "StateMachine·Sensory·SquadSync·Animator·Sfx");
    }

    [Test]
    public void 네트워크_해시가_고유하고_NetworkPrefab에_등록돼_있다()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(studentPrefabPath);
        uint hash = asset.GetComponent<NetworkObject>().PrefabIdHash;
        Assert.That(hash, Is.Not.Zero);
        foreach (string other in new[] { "Zombie_Walker", "Zombie_Teacher", "Zombie_Oiler" })
        {
            uint otherHash = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/02. Prefab/Zombie/{other}.prefab").GetComponent<NetworkObject>().PrefabIdHash;
            Assert.That(hash, Is.Not.EqualTo(otherHash), other);
        }
        Assert.That(AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(networkPrefabsPath).Contains(asset), Is.True);
    }
}
