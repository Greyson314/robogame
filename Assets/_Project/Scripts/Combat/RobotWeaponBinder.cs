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

        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Category == BlockCategory.Weapon;

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
