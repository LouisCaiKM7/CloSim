using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace InputController
{
    public class InputDeviceTracker : MonoBehaviour
    {
        public static InputDeviceTracker Instance { get; private set; }

        public enum DeviceMode
        {
            KeyboardMouse,
            Gamepad
        }

        public DeviceMode CurrentMode { get; private set; } = DeviceMode.KeyboardMouse;

        public event System.Action<DeviceMode> OnDeviceModeChanged;

        [Tooltip("Minimum pixel movement before mouse motion counts as 'using the mouse'.")]
        [SerializeField] private float mouseMoveThreshold = 4f;

        [Tooltip("Ignore mouse events briefly after gamepad input. Prevents mouse jitter/virtual mouse from stealing focus.")]
        [SerializeField] private float mouseIgnoreAfterGamepadSeconds = 0.25f;

        private Vector2 _lastMousePos;
        private double _ignoreMouseUntilTime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (Mouse.current != null)
                _lastMousePos = Mouse.current.position.ReadValue();

            // Fixed: keyboard/mouse mode should show the cursor.
            Cursor.visible = CurrentMode == DeviceMode.KeyboardMouse;
        }

        private void OnEnable()
        {
            InputSystem.onEvent += OnInputEvent;
        }

        private void OnDisable()
        {
            InputSystem.onEvent -= OnInputEvent;
        }

        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!eventPtr.valid)
                return;

            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
                return;

            switch (device)
            {
                case Gamepad _:
                    // A controller press often causes UI submit, then a mouse/virtual-mouse event can arrive.
                    // Do not let that immediate mouse event steal the mode.
                    _ignoreMouseUntilTime = eventPtr.time + mouseIgnoreAfterGamepadSeconds;

                    if (Mouse.current != null)
                        _lastMousePos = Mouse.current.position.ReadValue();

                    SetMode(DeviceMode.Gamepad);
                    break;

                case Keyboard _:
                    SetMode(DeviceMode.KeyboardMouse);
                    break;

                case Mouse mouse:
                    if (eventPtr.time < _ignoreMouseUntilTime)
                    {
                        _lastMousePos = mouse.position.ReadValue();
                        return;
                    }

                    Vector2 pos = mouse.position.ReadValue();

                    bool clicked =
                        mouse.leftButton.isPressed ||
                        mouse.rightButton.isPressed ||
                        mouse.middleButton.isPressed;

                    bool scrolled = mouse.scroll.ReadValue().sqrMagnitude > 0.01f;

                    bool moved =
                        (pos - _lastMousePos).sqrMagnitude >
                        mouseMoveThreshold * mouseMoveThreshold;

                    if (clicked || scrolled || moved)
                    {
                        _lastMousePos = pos;
                        SetMode(DeviceMode.KeyboardMouse);
                    }

                    break;
            }
        }

        private void SetMode(DeviceMode mode)
        {
            if (CurrentMode == mode)
                return;

            CurrentMode = mode;
            Cursor.visible = mode == DeviceMode.KeyboardMouse;
            OnDeviceModeChanged?.Invoke(mode);
        }
    }
}