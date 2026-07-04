using CGJ2026.Boulder;
using CGJ2026.Player;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    public sealed class CheckpointZone : MonoBehaviour
    {
        [SerializeField] private RespawnService respawnService;

        private bool hasPlayer;
        private bool hasBoulder;
        private PlayerController player;
        private BoulderGravityController boulder;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.attachedRigidbody == null)
            {
                return;
            }

            if (other.attachedRigidbody.TryGetComponent(out PlayerController enteredPlayer))
            {
                hasPlayer = true;
                player = enteredPlayer;
            }

            if (other.attachedRigidbody.TryGetComponent(out BoulderGravityController enteredBoulder))
            {
                hasBoulder = true;
                boulder = enteredBoulder;
            }

            TryActivate();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.attachedRigidbody == null)
            {
                return;
            }

            if (other.attachedRigidbody.GetComponent<PlayerController>() != null)
            {
                hasPlayer = false;
            }

            if (other.attachedRigidbody.GetComponent<BoulderGravityController>() != null)
            {
                hasBoulder = false;
            }
        }

        private void TryActivate()
        {
            if (!hasPlayer || !hasBoulder || respawnService == null || player == null || boulder == null)
            {
                return;
            }

            respawnService.SetCheckpoint(player.transform.position, boulder.transform.position);
        }
    }
}
