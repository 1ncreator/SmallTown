using SmallTown.Simulation.Events;
using UnityEngine;

namespace SmallTown.View
{
    /// <summary>Marks a collider as a clickable building or place.</summary>
    public sealed class PickTarget : MonoBehaviour
    {
        [SerializeField] private EntityKind kind;
        [SerializeField] private int id = -1;

        public EntityRef Entity => new EntityRef(kind, id);

        public void Set(EntityRef e)
        {
            kind = e.Kind;
            id = e.Id;
        }
    }
}
