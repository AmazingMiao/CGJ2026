using System.Collections;
using CGJ2026.Gameplay;
using UnityEngine;

namespace CGJ2026.View
{
    // A short squash-and-vanish plus heavy, gravity-driven red chunks. It deliberately works
    // without an assigned prefab so death feedback cannot silently disappear in hand-built scenes.
    public sealed class PlayerDeathVfx : MonoBehaviour
    {
        [SerializeField] private RespawnService respawnService;
        [SerializeField] private SpriteRenderer playerRenderer;
        [SerializeField] private Transform playerVisualRoot;
        [SerializeField] private ParticleSystem explosionPrefab;
        [SerializeField] private LayerMask terrainMask = 1 << 8;
        [SerializeField] private float animationDuration = 0.38f;

        private SpriteRenderer[] playerRenderers;
        private Color[] restColors;
        private bool[] restRendererEnabled;
        private Animator playerAnimator;
        private Vector3 restScale;
        private Coroutine animationRoutine;

        private void Awake()
        {
            if (respawnService == null)
            {
                respawnService = GetComponent<RespawnService>();
            }

            if (playerRenderer == null && respawnService != null && respawnService.PlayerBody != null)
            {
                playerRenderer = respawnService.PlayerBody.GetComponentInChildren<SpriteRenderer>();
            }

            if (playerRenderer != null)
            {
                if (playerVisualRoot == null)
                {
                    Transform playerRoot = respawnService != null && respawnService.PlayerBody != null
                        ? respawnService.PlayerBody.transform
                        : playerRenderer.transform.root;
                    playerVisualRoot = playerRoot.Find("Sprite");
                    if (playerVisualRoot == null)
                    {
                        playerVisualRoot = playerRenderer.transform;
                    }
                }

                playerAnimator = playerVisualRoot.GetComponentInChildren<Animator>();
                restScale = playerVisualRoot.localScale;
                playerRenderers = playerVisualRoot.GetComponentsInChildren<SpriteRenderer>(true);
                restColors = new Color[playerRenderers.Length];
                restRendererEnabled = new bool[playerRenderers.Length];
                for (int i = 0; i < playerRenderers.Length; i++)
                {
                    restColors[i] = playerRenderers[i].color;
                    restRendererEnabled[i] = playerRenderers[i].enabled;
                }
            }
        }

        private void OnEnable()
        {
            if (respawnService != null)
            {
                respawnService.Died += PlayDeath;
                respawnService.Respawned += RestorePlayerVisual;
            }
        }

        private void OnDisable()
        {
            if (respawnService != null)
            {
                respawnService.Died -= PlayDeath;
                respawnService.Respawned -= RestorePlayerVisual;
            }
        }

        private void PlayDeath(Vector3 position)
        {
            SpawnExplosion(position);

            bool animatorOwnsDeathAnimation = playerAnimator != null
                && playerAnimator.runtimeAnimatorController != null
                && playerRenderers != null
                && playerRenderers.Length == 1;
            if (playerRenderer != null && !animatorOwnsDeathAnimation)
            {
                if (animationRoutine != null)
                {
                    StopCoroutine(animationRoutine);
                }

                animationRoutine = StartCoroutine(AnimatePlayerDeath());
            }
        }

        private IEnumerator AnimatePlayerDeath()
        {
            float duration = Mathf.Max(0.01f, animationDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float squashT = Mathf.Clamp01(t / 0.32f);
                float vanishT = Mathf.Clamp01((t - 0.18f) / 0.82f);

                float scaleX = Mathf.Lerp(restScale.x, restScale.x * 1.45f, squashT);
                scaleX = Mathf.Lerp(scaleX, 0f, vanishT);
                float scaleY = Mathf.Lerp(restScale.y, restScale.y * 0.45f, squashT);
                scaleY = Mathf.Lerp(scaleY, 0f, vanishT);
                playerVisualRoot.localScale = new Vector3(scaleX, scaleY, restScale.z);

                float alpha = 1f - Mathf.SmoothStep(0f, 1f, vanishT);
                for (int i = 0; i < playerRenderers.Length; i++)
                {
                    Color color = Color.Lerp(restColors[i], new Color(0.9f, 0.05f, 0.04f, 1f), squashT);
                    color.a = restColors[i].a * alpha;
                    playerRenderers[i].color = color;
                }
                yield return null;
            }

            for (int i = 0; i < playerRenderers.Length; i++)
            {
                playerRenderers[i].enabled = false;
            }
            animationRoutine = null;
        }

        private void RestorePlayerVisual()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }

            if (playerRenderer == null)
            {
                return;
            }

            playerVisualRoot.localScale = restScale;
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                playerRenderers[i].color = restColors[i];
                playerRenderers[i].enabled = restRendererEnabled[i];
            }
        }

        private void SpawnExplosion(Vector3 position)
        {
            if (explosionPrefab != null)
            {
                Instantiate(explosionPrefab, position, Quaternion.identity);
                return;
            }

            ParticleSystem particles = CreateRuntimeExplosion(position);
            particles.Play();
            particles.Emit(34);
        }

        private ParticleSystem CreateRuntimeExplosion(Vector3 position)
        {
            GameObject vfxObject = new GameObject("PlayerDeathExplosion_Runtime");
            vfxObject.transform.position = position;
            ParticleSystem particles = vfxObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.9f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0f, 0f, 1f),
                new Color(1f, 0.12f, 0.04f, 1f));
            main.gravityModifier = 3.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.18f;
            shape.arc = 360f;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 3f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.12f, 0.04f), 0f),
                    new GradientColorKey(new Color(0.35f, 0f, 0f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 0.9f), new Keyframe(1f, 0f)));

            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision2D;
            collision.collidesWith = terrainMask;
            collision.dampen = 0.35f;
            collision.bounce = 0.28f;
            collision.lifetimeLoss = 0.12f;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 30;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                Material material = new Material(shader)
                {
                    mainTexture = Texture2D.whiteTexture
                };
                renderer.material = material;
                Destroy(material, 1.5f);
            }

            return particles;
        }
    }
}
