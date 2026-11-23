using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 실제 좀비 프리팹으로 포효 취소 연결을 검증한다 — Teacher 만 옵트인하고, Teacher 애니메이터의 포효 레이어에
/// 취소 크로스페이드 목적지(Empty)와 포효 상태(태그 Roar)가 있어야 한다. 씬·네트워크는 실행하지 않는다.
/// </summary>
public sealed class TeacherRoarInterruptTests
{
    private const string zombieFolder = "Assets/02. Prefab/Zombie/";
    private const string roarLayerName = "Roar (Upper)"; // ZombieAnimator 와 같은 레이어 이름
    private const string roarEmptyStateName = "Empty";   // ZombieAnimator 의 포효 취소 목적지

    /// <summary>포효 취소 옵트인은 Teacher 하나뿐이다 — 기존 종(Oiler 놀람 → 도주 포함)의 포효는 종전대로 끝까지 재생된다.</summary>
    [TestCase("Zombie_Teacher", true)]
    [TestCase("Zombie_Walker", false)]
    [TestCase("Zombie_Listener", false)]
    [TestCase("Zombie_Stalker", false)]
    [TestCase("Zombie_Dog", false)]
    [TestCase("Zombie_Lurker", false)]
    [TestCase("Zombie_Oiler", false)]
    public void 포효_취소_옵트인은_Teacher만_켠다(string prefabName, bool expected)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(zombieFolder + prefabName + ".prefab");
        try
        {
            ZombieController controller = root.GetComponent<ZombieController>();
            Assert.That(controller, Is.Not.Null, $"{prefabName}: 루트 컨트롤러 없음");
            Assert.That(controller.InterruptsRoarOnCancel, Is.EqualTo(expected), prefabName);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Teacher 애니메이터의 포효 레이어에 포효 상태(태그 Roar)와 취소 목적지(Empty)가 있다.</summary>
    [Test]
    public void Teacher_포효_레이어에_포효_상태와_Empty_상태가_있다()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(zombieFolder + "Zombie_Teacher.prefab");
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, "Teacher 모델 Animator 없음");

            RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
            if (runtime is AnimatorOverrideController overrideController) runtime = overrideController.runtimeAnimatorController;
            var controller = runtime as AnimatorController;
            Assert.That(controller, Is.Not.Null, "AnimatorController 를 찾지 못함");

            AnimatorControllerLayer layer = controller.layers.FirstOrDefault(l => l.name == roarLayerName);
            Assert.That(layer, Is.Not.Null, $"{roarLayerName} 레이어 없음");

            ChildAnimatorState[] states = layer.stateMachine.states;
            Assert.That(states.Any(s => s.state.name == roarEmptyStateName), Is.True, "포효 취소 목적지 Empty 상태 없음");
            Assert.That(states.Any(s => s.state.tag == "Roar"), Is.True, "Roar 태그 상태 없음");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
