using System.Collections;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Input;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Combat
{
    /// <summary>
    /// Wiring + lifecycle tests for the active-module system: a registered
    /// module reports ready, firing engages the server-authoritative
    /// cooldown, and destroying the carrier block disables the ability
    /// (functional disable). Uses the EMP default so the effect path needs
    /// no Rigidbody target or scene neighbours.
    /// </summary>
    public sealed class ActiveModuleSystemTests
    {
        private sealed class StubInput : MonoBehaviour, IInputSource
        {
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => 0f;
            public bool FireHeld => false;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
            public bool ModulePressed { get; set; }
        }

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        // Build the root with everything wired BEFORE activation so OnEnable
        // fires once with the system already present — the same deactivated-
        // build dance ChassisAssembler uses.
        private (ActiveModuleSystem sys, ActiveModuleBlock block, StubInput input) BuildChassisWithModule()
        {
            _root = new GameObject("ModuleChassis");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody>();
            StubInput input = _root.AddComponent<StubInput>();
            ActiveModuleSystem sys = _root.AddComponent<ActiveModuleSystem>();

            var child = new GameObject("ModuleBlock");
            child.transform.SetParent(_root.transform);
            child.AddComponent<BlockBehaviour>();
            ActiveModuleBlock block = child.AddComponent<ActiveModuleBlock>();

            _root.SetActive(true);
            return (sys, block, input);
        }

        [UnityTest]
        public IEnumerator ModulePresent_SystemReportsReady()
        {
            var (sys, _, _) = BuildChassisWithModule();
            yield return null;

            Assert.IsTrue(sys.HasModule, "System should see the registered module block.");
            Assert.IsTrue(sys.IsReady, "A fresh module should be ready to fire.");
            Assert.AreEqual(ModuleKind.EmpBurst, sys.ModuleKindOrNull,
                "With no blueprint override, the module defaults to EmpBurst.");
        }

        [UnityTest]
        public IEnumerator PressingModule_EngagesCooldown()
        {
            var (sys, _, input) = BuildChassisWithModule();
            yield return null;
            Assume.That(sys.IsReady, Is.True);

            input.ModulePressed = true;
            yield return null; // ActiveModuleSystem.Update polls input and fires
            input.ModulePressed = false;

            Assert.IsFalse(sys.IsReady,
                "Firing the ability must engage the server-authoritative cooldown.");
            Assert.Less(sys.ReadyFraction, 1f,
                "ReadyFraction should drop below 1 immediately after firing.");
        }

        [UnityTest]
        public IEnumerator DestroyingModuleBlock_DisablesAbility()
        {
            var (sys, block, _) = BuildChassisWithModule();
            yield return null;
            Assume.That(sys.HasModule, Is.True);

            Object.Destroy(block.gameObject);
            yield return null; // OnDisable unregisters the block

            Assert.IsFalse(sys.HasModule,
                "Destroying the carrier block must disable the ability (functional disable).");
        }
    }
}
