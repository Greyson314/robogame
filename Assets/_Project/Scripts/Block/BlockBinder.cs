using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Base class for components that auto-attach behaviour scripts to
    /// blocks placed into a <see cref="BlockGrid"/>. Subscribes to
    /// <see cref="BlockGrid.BlockPlaced"/> and re-binds existing children
    /// on enable so template rebuilds work transparently.
    /// </summary>
    /// <remarks>
    /// Subclasses implement <see cref="ShouldBind"/> + <see cref="Bind"/>;
    /// the dispatch / lifecycle plumbing is shared. This replaces three
    /// near-identical binder MonoBehaviours.
    /// </remarks>
    [RequireComponent(typeof(BlockGrid))]
    public abstract class BlockBinder : MonoBehaviour
    {
        private BlockGrid _grid;

        protected virtual void OnEnable()
        {
            _grid = GetComponent<BlockGrid>();
            if (_grid == null) return;

            _grid.BlockPlaced += HandleBlockPlaced;
            foreach (BlockBehaviour b in GetComponentsInChildren<BlockBehaviour>(includeInactive: true))
            {
                HandleBlockPlaced(b);
            }
        }

        protected virtual void OnDisable()
        {
            if (_grid != null) _grid.BlockPlaced -= HandleBlockPlaced;
        }

        private void HandleBlockPlaced(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return;
            if (!ShouldBind(block)) return;
            Bind(block);
        }

        /// <summary>Return true if <paramref name="block"/> is one we care about.</summary>
        protected abstract bool ShouldBind(BlockBehaviour block);

        /// <summary>Attach (or update) the per-block behaviour for <paramref name="block"/>.</summary>
        protected abstract void Bind(BlockBehaviour block);
    }

    /// <summary>
    /// One-block-id, one-component binder — the shape most binders take
    /// (ADR-0008; RobotPogoBinder and RobotGyroBinder were line-identical
    /// files before this base). Subclass with the concrete component type
    /// and override <see cref="BlockId"/>; the closed generic subclass is
    /// what AddComponent needs, so each binder is three lines.
    /// </summary>
    public abstract class SingleBlockBinder<TBehaviour> : BlockBinder
        where TBehaviour : Component
    {
        /// <summary>The block id this binder reacts to.</summary>
        protected abstract string BlockId { get; }

        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Id == BlockId;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<TBehaviour>() == null)
                block.gameObject.AddComponent<TBehaviour>();
        }
    }
}
