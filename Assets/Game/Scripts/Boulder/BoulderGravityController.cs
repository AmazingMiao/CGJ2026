using CGJ2026.Input;
using CGJ2026.Gameplay;
using UnityEngine;

namespace CGJ2026.Boulder
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class BoulderGravityController : MonoBehaviour
    {
        [SerializeField] private GameInputReader inputReader;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private GroundRaySensor2D groundSensor;
        [SerializeField] private float gravityAcceleration = 34f;
        [SerializeField] private float maxSpeed = 16f;
        [SerializeField] private Vector2 defaultGravityDirection = Vector2.down;

        private Vector2 activeGravityDirection;

        public Vector2 ActiveGravityDirection => activeGravityDirection;
        public bool IsGrounded => groundSensor != null && groundSensor.IsGrounded;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            groundSensor = GetComponent<GroundRaySensor2D>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (groundSensor == null)
            {
                groundSensor = GetComponent<GroundRaySensor2D>();
            }

            if (inputReader == null)
            {
                inputReader = FindFirstObjectByType<GameInputReader>();
            }

            body.gravityScale = 0f;
            activeGravityDirection = defaultGravityDirection.normalized;
        }

        private void FixedUpdate()
        {
            groundSensor?.Refresh();

            Vector2 requestedDirection = inputReader != null ? inputReader.BoulderGravityDirection : Vector2.zero;
            if (requestedDirection.sqrMagnitude > 0.01f)
            {
                activeGravityDirection = requestedDirection.normalized;
            }

            body.AddForce(activeGravityDirection * gravityAcceleration * body.mass, ForceMode2D.Force);
            ClampSpeed();
        }

        private void ClampSpeed()
        {
            Vector2 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude <= maxSpeed * maxSpeed)
            {
                return;
            }

            body.linearVelocity = velocity.normalized * maxSpeed;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = transform.position;
            Vector3 end = start + (Vector3)(Application.isPlaying ? activeGravityDirection : defaultGravityDirection.normalized);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(end, 0.12f);
        }
    }
}
