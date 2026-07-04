using System.Collections.Generic;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class RainEmitter2D : MonoBehaviour
    {
        [Header("Emission")]
        [SerializeField] private bool emitOnStart = true;
        [SerializeField, Min(0f)] private float emissionRate = 24f;
        [SerializeField, Min(1)] private int maxActiveDroplets = 120;
        [SerializeField, Min(0f)] private float emissionWidth = 1.8f;
        [SerializeField] private Vector2 localDirection = Vector2.down;
        [SerializeField, Min(0f)] private float initialSpeed = 7f;
        [SerializeField, Min(0f)] private float sidewaysSpeedJitter = 1.2f;

        [Header("Droplet Physics")]
        [SerializeField, Min(0.02f)] private float dropletRadius = 0.11f;
        [SerializeField, Min(0.001f)] private float dropletMass = 0.015f;
        [SerializeField, Min(0f)] private float dropletGravityScale = 1.5f;
        [SerializeField, Min(0.1f)] private float dropletLifetime = 8f;
        [SerializeField, Min(0f)] private float slipperyDuration = 1.8f;
        [SerializeField] private PhysicsMaterial2D waterMaterial;

        [Header("Droplet Visual")]
        [SerializeField] private Sprite dropletSprite;
        [SerializeField] private Color dropletColor = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private int sortingOrder = 20;

        private readonly Queue<RainWaterParticle2D> pooledDroplets = new Queue<RainWaterParticle2D>();
        private readonly List<RainWaterParticle2D> allDroplets = new List<RainWaterParticle2D>();
        private PhysicsMaterial2D runtimeMaterial;
        private float emissionAccumulator;
        private int activeDropletCount;
        private bool isEmitting;

        private void Awake()
        {
            isEmitting = emitOnStart;
            if (waterMaterial == null)
            {
                runtimeMaterial = new PhysicsMaterial2D("RainWater_Runtime")
                {
                    friction = 0f,
                    bounciness = 0f,
                    hideFlags = HideFlags.HideAndDontSave
                };
                waterMaterial = runtimeMaterial;
            }

            if (dropletSprite == null)
            {
                dropletSprite = RainWaterParticle2D.GetOrCreateRuntimeSprite();
            }
        }

        private void Update()
        {
            if (!isEmitting || emissionRate <= 0f)
            {
                return;
            }

            emissionAccumulator += emissionRate * Time.deltaTime;
            int spawnCount = Mathf.Min(Mathf.FloorToInt(emissionAccumulator), 8);
            emissionAccumulator -= spawnCount;
            for (int i = 0; i < spawnCount; i++)
            {
                SpawnDroplet();
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < allDroplets.Count; i++)
            {
                if (allDroplets[i].IsActive)
                {
                    Release(allDroplets[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        public void SetEmitting(bool value)
        {
            isEmitting = value;
            if (!value)
            {
                emissionAccumulator = 0f;
            }
        }

        private void SpawnDroplet()
        {
            if (activeDropletCount >= maxActiveDroplets)
            {
                return;
            }

            RainWaterParticle2D droplet = pooledDroplets.Count > 0 ? pooledDroplets.Dequeue() : CreateDroplet();
            Vector2 direction = localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : Vector2.down;
            direction = transform.TransformDirection(direction).normalized;
            Vector2 tangent = new Vector2(-direction.y, direction.x);
            Vector3 spawnPosition = transform.TransformPoint(Vector3.right * Random.Range(-emissionWidth * 0.5f, emissionWidth * 0.5f));
            Vector2 velocity = direction * initialSpeed + tangent * Random.Range(-sidewaysSpeedJitter, sidewaysSpeedJitter);

            activeDropletCount++;
            droplet.Activate(spawnPosition, velocity, dropletLifetime, slipperyDuration);
        }

        private RainWaterParticle2D CreateDroplet()
        {
            GameObject dropletObject = new GameObject("RainWaterDroplet");
            int waterLayer = LayerMask.NameToLayer("Water");
            dropletObject.layer = waterLayer >= 0 ? waterLayer : 0;
            dropletObject.transform.SetParent(transform, false);

            Rigidbody2D dropletBody = dropletObject.AddComponent<Rigidbody2D>();
            dropletBody.mass = dropletMass;
            dropletBody.gravityScale = dropletGravityScale;
            dropletBody.linearDamping = 0.05f;
            dropletBody.freezeRotation = true;
            dropletBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            dropletBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D dropletCollider = dropletObject.AddComponent<CircleCollider2D>();
            dropletCollider.radius = dropletRadius;
            dropletCollider.sharedMaterial = waterMaterial;

            SpriteRenderer renderer = dropletObject.AddComponent<SpriteRenderer>();
            renderer.sprite = dropletSprite;
            renderer.color = dropletColor;
            renderer.sortingOrder = sortingOrder;
            dropletObject.transform.localScale = Vector3.one * (dropletRadius * 2f);

            RainWaterParticle2D droplet = dropletObject.AddComponent<RainWaterParticle2D>();
            droplet.Configure(this, dropletBody);
            allDroplets.Add(droplet);
            dropletObject.SetActive(false);
            return droplet;
        }

        internal void Release(RainWaterParticle2D droplet)
        {
            if (droplet == null || !droplet.IsActive)
            {
                return;
            }

            droplet.Deactivate();
            activeDropletCount = Mathf.Max(0, activeDropletCount - 1);
            pooledDroplets.Enqueue(droplet);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 direction = localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : Vector2.down;
            direction = transform.TransformDirection(direction).normalized;
            Vector3 tangent = new Vector3(-direction.y, direction.x, 0f);
            Gizmos.color = new Color(0.1f, 0.7f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position - tangent * emissionWidth * 0.5f, transform.position + tangent * emissionWidth * 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)direction * 1.2f);
        }

        private void OnValidate()
        {
            maxActiveDroplets = Mathf.Max(1, maxActiveDroplets);
            emissionRate = Mathf.Max(0f, emissionRate);
            dropletRadius = Mathf.Max(0.02f, dropletRadius);
            dropletLifetime = Mathf.Max(0.1f, dropletLifetime);
        }
    }
}
