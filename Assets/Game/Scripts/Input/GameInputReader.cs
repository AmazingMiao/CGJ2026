using UnityEngine;
using UnityEngine.InputSystem;

namespace CGJ2026.Input
{
    public sealed class GameInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string playerMapName = "Player";
        [SerializeField] private string playerMoveActionName = "Move";
        [SerializeField] private string playerJumpActionName = "Jump";
        [SerializeField] private string boulderMapName = "Boulder";
        [SerializeField] private string boulderGravityActionName = "AimGravity";

        private InputActionMap playerMap;
        private InputActionMap boulderMap;
        private InputAction playerMoveAction;
        private InputAction playerJumpAction;
        private InputAction boulderGravityAction;
        private bool jumpQueued;

        public Vector2 PlayerMove { get; private set; }
        public Vector2 BoulderGravityDirection { get; private set; }

        public void AssignInputActions(InputActionAsset actions)
        {
            inputActions = actions;
            BindActions();
        }

        public bool ConsumeJumpPressed()
        {
            if (!jumpQueued)
            {
                return false;
            }

            jumpQueued = false;
            return true;
        }

        public void DiscardJumpPressed()
        {
            jumpQueued = false;
        }

        private void Awake()
        {
            BindActions();
        }

        private void OnEnable()
        {
            BindActions();
            playerMap?.Enable();
            boulderMap?.Enable();
            if (playerJumpAction != null)
            {
                playerJumpAction.performed += OnJumpPerformed;
            }
        }

        private void OnDisable()
        {
            if (playerJumpAction != null)
            {
                playerJumpAction.performed -= OnJumpPerformed;
            }

            playerMap?.Disable();
            boulderMap?.Disable();
        }

        private void Update()
        {
            PlayerMove = playerMoveAction != null ? playerMoveAction.ReadValue<Vector2>() : Vector2.zero;
            BoulderGravityDirection = boulderGravityAction != null ? boulderGravityAction.ReadValue<Vector2>() : Vector2.zero;
        }

        private void BindActions()
        {
            if (inputActions == null)
            {
                return;
            }

            playerMap = inputActions.FindActionMap(playerMapName, false);
            boulderMap = inputActions.FindActionMap(boulderMapName, false);
            playerMoveAction = playerMap?.FindAction(playerMoveActionName, false);
            playerJumpAction = playerMap?.FindAction(playerJumpActionName, false);
            boulderGravityAction = boulderMap?.FindAction(boulderGravityActionName, false);
        }

        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            jumpQueued = true;
        }
    }
}
