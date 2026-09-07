using UnityEngine;

namespace RoomForge
{
    // Deliberately empty — no spawn logic lives here. It's a marker a room-prefab author
    // places at authoring time; RoomDefinition.CollectSpawnPoints picks up every child with
    // this component at runtime, and the game's own code (subscribed to
    // DungeonGenerator.OnRoomPlaced) decides what, if anything, to instantiate at each one.
    [AddComponentMenu("RoomForge/Spawn Point")]
    public class SpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.2f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.15f, transform.position + Vector3.up * 0.45f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0f, 0.25f);
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
    }
}
