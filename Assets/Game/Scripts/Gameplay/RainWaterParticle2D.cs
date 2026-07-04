using CGJ2026.Player;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
    public sealed class RainWaterParticle2D : MonoBehaviour
    {
        private static Sprite runtimeSprite;

        private RainEmitter2D owner;
        private Rigidbody2D body;
        private float expiresAt;
        private float slipperyDuration;

        public bool IsActive { get; private set; }

        internal void Configure(RainEmitter2D emitter, Rigidbody2D dropletBody)
        {
            owner = emitter;
            body = dropletBody;
        }

        internal void Activate(Vector2 position, Vector2 velocity, float lifetime, float wetDuration)
        {
            transform.position = position;
            gameObject.SetActive(true);
            body.simulated = true;
            body.position = position;
            body.rotation = 0f;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;
            expiresAt = Time.time + Mathf.Max(0.1f, lifetime);
            slipperyDuration = Mathf.Max(0f, wetDuration);
            IsActive = true;
        }

        internal void Deactivate()
        {
            IsActive = false;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (IsActive && Time.time >= expiresAt)
            {
                owner.Release(this);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!IsActive)
            {
                return;
            }

            CharacterMotor2D motor = collision.rigidbody != null
                ? collision.rigidbody.GetComponent<CharacterMotor2D>()
                : collision.collider.GetComponentInParent<CharacterMotor2D>();
            if (motor != null)
            {
                motor.ApplySlippery(slipperyDuration);
            }

            // Any first impact (player or ground) soaks the droplet in immediately, so a droplet
            // that already landed can never linger as a "puddle" that grants slip on a later touch.
            owner.Release(this);
        }

        internal static Sprite GetOrCreateRuntimeSprite()
        {
            if (runtimeSprite != null)
            {
                return runtimeSprite;
            }

            const int size = 16;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RainWaterDroplet_Runtime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Vector2 center = Vector2.one * ((size - 1) * 0.5f);
            float radius = size * 0.45f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            runtimeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                Vector2.one * 0.5f,
                size,
                0,
                SpriteMeshType.FullRect);
            runtimeSprite.name = "RainWaterDroplet_Runtime";
            runtimeSprite.hideFlags = HideFlags.HideAndDontSave;
            return runtimeSprite;
        }
    }
}
