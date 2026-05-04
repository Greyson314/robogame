using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Attaches <see cref="WeaponBlock"/> behaviour to weapon blocks placed
    /// in the chassis grid, and binds them to the robot's <see cref="WeaponMount"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotWeaponBinder : BlockBinder
    {
        [SerializeField] private WeaponMount _mount;

        protected override void OnEnable()
        {
            if (_mount == null) _mount = GetComponentInChildren<WeaponMount>();
            base.OnEnable();
        }

        protected override bool ShouldBind(BlockBehaviour block)
        {
            if (block.Definition.Category != BlockCategory.Weapon) return false;
            // Tip blocks (Hook / Mace) are weapon-category for the hotbar
            // (they ARE weapons gameplay-wise) but they're handled by
            // RobotTipBlockBinder, not RobotWeaponBinder. WeaponBlock's
            // mounted-yoke aim model rotates the host to track the
            // reticle — wrong behaviour for a rope-tip that should hang
            // freely from the rope. Skip them here.
            string id = block.Definition.Id;
            if (id == BlockIds.Hook || id == BlockIds.Mace) return false;
            return true;
        }

        protected override void Bind(BlockBehaviour block)
        {
            // Dispatch by stable id so future weapon variants (rocket pod,
            // mortar, …) can each land on their own behaviour while
            // sharing the same WeaponMount aim system.
            string id = block.Definition.Id;
            if (id == BlockIds.BombBay)
            {
                BombBayBlock bay = block.GetComponent<BombBayBlock>();
                if (bay == null) bay = block.gameObject.AddComponent<BombBayBlock>();
                bay.Bind(_mount);
                return;
            }

            WeaponBlock weapon = block.GetComponent<WeaponBlock>();
            if (weapon == null) weapon = block.gameObject.AddComponent<WeaponBlock>();
            weapon.Bind(_mount);
        }
    }
}
