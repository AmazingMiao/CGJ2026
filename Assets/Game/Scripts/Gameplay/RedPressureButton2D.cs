using System.Collections.Generic;
using CGJ2026.Boulder;
using CGJ2026.Player;
using UnityEngine;
using UnityEngine.Events;

namespace CGJ2026.Gameplay
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class RedPressureButton2D : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField, InspectorName("按钮面板")] private Transform pressPlate;
        [SerializeField, InspectorName("触发碰撞体")] private Collider2D triggerCollider;
        [SerializeField, InspectorName("控制的墙块组")] private TileWallGate2D[] wallGates;

        [Header("按压规则")]
        [SerializeField, InspectorName("玩家可触发")] private bool allowPlayer = true;
        [SerializeField, InspectorName("球可触发")] private bool allowBoulder = true;
        [SerializeField, InspectorName("松开后复原")] private bool releaseWhenEmpty;

        [Header("下压表现")]
        [SerializeField, InspectorName("下压距离")] private float pressedOffset = 0.22f;
        [SerializeField, InspectorName("下压速度")] private float pressSpeed = 18f;

        [Header("声音接口")]
        [SerializeField, InspectorName("音源")] private AudioSource audioSource;
        [SerializeField, InspectorName("按下音效")] private AudioClip pressClip;
        [SerializeField, InspectorName("松开音效")] private AudioClip releaseClip;

        [Header("事件接口")]
        [SerializeField, InspectorName("按下时")] private UnityEvent onPressed;
        [SerializeField, InspectorName("松开时")] private UnityEvent onReleased;

        private readonly HashSet<Rigidbody2D> pressingBodies = new HashSet<Rigidbody2D>();
        private Vector3 releasedLocalPosition;
        private bool isPressed;

        public bool IsPressed => isPressed;

        private void Reset()
        {
            triggerCollider = GetComponent<Collider2D>();
            triggerCollider.isTrigger = true;
            pressPlate = transform.Find("PressPlate");
            audioSource = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (triggerCollider == null)
            {
                triggerCollider = GetComponent<Collider2D>();
            }

            triggerCollider.isTrigger = true;

            if (pressPlate == null)
            {
                pressPlate = transform;
            }

            releasedLocalPosition = pressPlate.localPosition;
        }

        private void Update()
        {
            Vector3 target = releasedLocalPosition + Vector3.down * (isPressed ? pressedOffset : 0f);
            pressPlate.localPosition = Vector3.MoveTowards(
                pressPlate.localPosition,
                target,
                pressSpeed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryAddPressingBody(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryAddPressingBody(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            Rigidbody2D body = other.attachedRigidbody;
            if (body != null)
            {
                pressingBodies.Remove(body);
            }

            if (releaseWhenEmpty && pressingBodies.Count == 0)
            {
                SetPressed(false);
            }
        }

        private void TryAddPressingBody(Collider2D other)
        {
            Rigidbody2D body = other.attachedRigidbody;
            if (body == null || !CanPress(body))
            {
                return;
            }

            pressingBodies.Add(body);
            SetPressed(true);
        }

        private bool CanPress(Rigidbody2D body)
        {
            if (allowPlayer && body.GetComponent<PlayerController>() != null)
            {
                return true;
            }

            return allowBoulder && body.GetComponent<BoulderGravityController>() != null;
        }

        private void SetPressed(bool pressed)
        {
            if (isPressed == pressed)
            {
                return;
            }

            isPressed = pressed;
            if (wallGates != null)
            {
                for (int i = 0; i < wallGates.Length; i++)
                {
                    if (wallGates[i] != null)
                    {
                        wallGates[i].SetOpen(isPressed);
                    }
                }
            }

            if (isPressed)
            {
                PlayClip(pressClip);
                onPressed?.Invoke();
            }
            else
            {
                PlayClip(releaseClip);
                onReleased?.Invoke();
            }
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
