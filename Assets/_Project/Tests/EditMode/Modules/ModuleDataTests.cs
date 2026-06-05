using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Modules
{
    /// <summary>
    /// Pure data-layer tests for the module spine: the id ↔ kind map, the
    /// power→cooldown trade, and the per-chassis module cap. These encode the
    /// invariants the runtime + garage both depend on — a block self-describes
    /// its ability, more power costs proportionally more cooldown, and a build
    /// can never field more than the cap.
    /// </summary>
    public sealed class ModuleDataTests
    {
        [Test]
        public void ModuleKinds_RoundTripsEveryKind()
        {
            foreach (ModuleKind kind in System.Enum.GetValues(typeof(ModuleKind)))
            {
                string id = ModuleKinds.BlockIdFor(kind);
                Assert.IsTrue(ModuleKinds.IsModuleId(id), $"{id} should be recognised as a module id.");
                Assert.AreEqual(kind, ModuleKinds.ForBlockId(id),
                    $"id↔kind map must round-trip for {kind} — a placed block resolves its own ability.");
            }
        }

        [Test]
        public void ModuleKinds_NonModuleId_IsNotAModule()
        {
            Assert.IsFalse(ModuleKinds.IsModuleId(BlockIds.Cube));
            Assert.IsNull(ModuleKinds.ForBlockId(BlockIds.Cube));
        }

        [Test]
        public void Tuning_DefaultPower_GivesBaseCooldown()
        {
            // power = 0 means "use the default", which must land on the base cd.
            ModuleTuning.Resolved at0 = ModuleTuning.Resolve(ModuleKind.Spring, 0f);
            ModuleTuning.Resolved atDefault = ModuleTuning.Resolve(ModuleKind.Spring, ModuleTuning.DefaultPower(ModuleKind.Spring));
            Assert.AreEqual(atDefault.Cooldown, at0.Cooldown, 1e-4f,
                "power 0 must resolve identically to default power (the untuned fallback).");
        }

        [Test]
        public void Tuning_CooldownScalesLinearlyWithPower_ClampedAtTwice()
        {
            float def = ModuleTuning.DefaultPower(ModuleKind.Spring);
            float baseCd = ModuleTuning.Resolve(ModuleKind.Spring, def).Cooldown;

            // 2× power → 2× cooldown (the design brief's "commensurate" trade).
            float doubleCd = ModuleTuning.Resolve(ModuleKind.Spring, def * 2f).Cooldown;
            Assert.AreEqual(baseCd * 2f, doubleCd, 1e-3f, "2× power must cost 2× cooldown.");

            // Beyond 2× the ratio is clamped, so cooldown can't run away.
            float wayOverCd = ModuleTuning.Resolve(ModuleKind.Spring, def * 10f).Cooldown;
            Assert.AreEqual(baseCd * 2f, wayOverCd, 1e-3f, "Cooldown ratio is clamped at 2×.");

            // Half power → half cooldown, clamped at 0.5×.
            float halfCd = ModuleTuning.Resolve(ModuleKind.Spring, def * 0.5f).Cooldown;
            Assert.AreEqual(baseCd * 0.5f, halfCd, 1e-3f, "0.5× power must cost 0.5× cooldown.");
        }

        [Test]
        public void Tuning_Invisibility_DurationEqualsPower()
        {
            ModuleTuning.Resolved r = ModuleTuning.Resolve(ModuleKind.Invisibility, 7f);
            Assert.AreEqual(7f, r.Duration, 1e-4f,
                "Invisibility's power IS its duration — the slider sets how long you stay cloaked.");
        }

        [Test]
        public void Tuning_Repair_IsInstantaneousHealAtDefaultPower()
        {
            // Repair pulses once (no lifetime) and its power IS the per-block
            // heal amount. Encodes the design: a sustain tool on a real cooldown,
            // not a channel and not a duration buff.
            ModuleTuning.Resolved r = ModuleTuning.Resolve(ModuleKind.Repair, 0f);
            Assert.AreEqual(0f, r.Duration, 1e-4f, "Repair is instantaneous — no effect lifetime.");
            Assert.AreEqual(ModuleTuning.DefaultPower(ModuleKind.Repair), r.Magnitude, 1e-4f,
                "power 0 resolves to the default per-block heal amount.");
            Assert.Greater(r.Cooldown, 0f, "Repair must carry a real cooldown — sustain, not invulnerability.");
        }

        [Test]
        public void Budget_TrimToFit_DropsModulesBeyondCap_KeepsOthers()
        {
            // 5 modules + a CPU: the 5th module drops, the CPU is untouched.
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu,         new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleEmp,   new Vector3Int(1, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleBlink, new Vector3Int(2, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleShield,new Vector3Int(3, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleSmoke, new Vector3Int(4, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleInvis, new Vector3Int(5, 0, 0)),
            };
            Assert.AreEqual(5, ModuleBudget.Count(entries), "Sanity: 5 modules placed.");

            ChassisBlueprint.Entry[] trimmed = ModuleBudget.TrimToFit(entries, out int removed);
            Assert.AreEqual(1, removed, "Exactly one module over the cap should be dropped.");
            Assert.AreEqual(ModuleBudget.MaxModules, ModuleBudget.Count(trimmed), "Trimmed set is at the cap.");
            Assert.IsTrue(System.Array.Exists(trimmed, e => e.BlockId == BlockIds.Cpu),
                "Trimming modules must never remove the CPU.");
        }

        [Test]
        public void Budget_UnderCap_ReturnsInputUnchanged()
        {
            var entries = new[]
            {
                new ChassisBlueprint.Entry(BlockIds.Cpu,       new Vector3Int(0, 0, 0)),
                new ChassisBlueprint.Entry(BlockIds.ModuleEmp, new Vector3Int(1, 0, 0)),
            };
            ChassisBlueprint.Entry[] trimmed = ModuleBudget.TrimToFit(entries, out int removed);
            Assert.AreEqual(0, removed);
            Assert.AreSame(entries, trimmed, "Under the cap, the input array is returned unchanged.");
        }
    }
}
