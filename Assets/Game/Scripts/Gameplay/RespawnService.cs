using System;
using System.Collections;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    public sealed class RespawnService : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D playerBody;
        [SerializeField] private Rigidbody2D boulderBody;
        [SerializeField] private Transform playerSpawn;
        [SerializeField] private Transform boulderSpawn;
        [SerializeField] private float deathRespawnDelay = 0.48f;

        private Vector3 currentPlayerSpawn;
        private Vector3 currentBoulderSpawn;
        private Coroutine deathRoutine;

        public event Action<Vector3> Died;
        public event Action Respawned;

        public bool IsPlayerDead { get; private set; }
        public Rigidbody2D PlayerBody => playerBody;

        private void Awake()
        {
            if (playerBody != null)
            {
                currentPlayerSpawn = playerSpawn != null ? playerSpawn.position : playerBody.position;
            }

            if (boulderBody != null)
            {
                currentBoulderSpawn = boulderSpawn != null ? boulderSpawn.position : boulderBody.position;
            }
        }

        public void SetCheckpoint(Vector3 playerPosition, Vector3 boulderPosition)
        {
            currentPlayerSpawn = playerPosition;
            currentBoulderSpawn = boulderPosition;
        }

        public void KillPlayer()
        {
            if (IsPlayerDead || playerBody == null)
            {
                return;
            }

            IsPlayerDead = true;
            Vector3 deathPosition = playerBody.position;
            FreezeBody(playerBody);
            FreezeBody(boulderBody);
            Died?.Invoke(deathPosition);
            deathRoutine = StartCoroutine(RespawnAfterDeath());
        }

        public void Respawn()
        {
            if (deathRoutine != null)
            {
                StopCoroutine(deathRoutine);
                deathRoutine = null;
            }

            CompleteRespawn();
        }

        private IEnumerator RespawnAfterDeath()
        {
            yield return new WaitForSeconds(deathRespawnDelay);
            deathRoutine = null;
            CompleteRespawn();
        }

        private void CompleteRespawn()
        {
            ResetBody(playerBody, currentPlayerSpawn);
            ResetBody(boulderBody, currentBoulderSpawn);
            IsPlayerDead = false;
            Respawned?.Invoke();
        }

        private static void FreezeBody(Rigidbody2D body)
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }

        private static void ResetBody(Rigidbody2D body, Vector3 position)
        {
            if (body == null)
            {
                return;
            }

            body.simulated = false;
            body.position = position;
            body.rotation = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = true;
        }
    }
}
