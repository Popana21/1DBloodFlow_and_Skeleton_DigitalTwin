using UnityEngine;
using UnityEngine.InputSystem;

public class UiCameraModeToggle : MonoBehaviour
{
    [Header("Disable these while in UI mode (e.g., CinemachineInputAxisController)")]
    public Behaviour[] cameraInputBehaviours;

    [Header("Start State")]
    public bool startInUiMode = false;

    [Header("Debug")]
    public bool showDebug = true;

    public bool UiMode { get; private set; }

    void Start()
    {
        SetUiMode(startInUiMode);
    }

    void Update()
    {
        // NEW Input System RMB toggle
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            SetUiMode(!UiMode);
        }
    }

    public void SetUiMode(bool enabled)
    {
        UiMode = enabled;

        // UI mode means the cursor is released and camera-look input is disabled.
        if (cameraInputBehaviours != null)
        {
            for (int i = 0; i < cameraInputBehaviours.Length; i++)
            {
                var b = cameraInputBehaviours[i];
                if (b != null) b.enabled = !UiMode;
            }
        }

        // Cursor handling (critical for build)
        if (UiMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void OnGUI()
    {
        if (!showDebug) return;

        GUI.Label(new Rect(10, 10, 600, 22),
            $"Mode: {(UiMode ? "UI MODE (camera OFF)" : "CAMERA MODE (camera ON)")}  | RMB toggles");
    }
}
