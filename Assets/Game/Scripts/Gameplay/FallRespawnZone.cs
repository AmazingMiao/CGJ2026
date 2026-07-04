using CGJ2026.Boulder;
using CGJ2026.Player;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    public sealed class FallRespawnZone : MonoBehaviour
    {
        [SerializeField] private RespawnService respawnService;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.attachedRigidbody == null)
            {
                return;
            }

            if (other.attachedRigidbody.GetComponent<PlayerController>() != null)
            {
                respawnService?.KillPlayer();
            }
            else if (other.attachedRigidbody.GetComponent<BoulderGravityController>() != null)
            {
                respawnService?.Respawn();
            }
        }
    }
}
