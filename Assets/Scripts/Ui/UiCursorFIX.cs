using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class UiCursorFix : MonoBehaviour
{
    [Header("Disable these while UI mode is active")]
    public Behaviour[] cameraInputBehaviours;

    [Header("State")]
    [Tooltip("If true, UI is usable and camera input is disabled.")]
    public bool startInUiMode = false;

    public bool UiMode { get; private set; }

    void Start()
    {
        EnsureCameraInputBehaviours();
        SetUiMode(startInUiMode);
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            SetUiMode(!UiMode);
    }

    void EnsureCameraInputBehaviours()
    {
        if (cameraInputBehaviours != null && cameraInputBehaviours.Length > 0)
            return;

        // Search by type name so the project keeps working with different Cinemachine package variants.
        var found = new List<Behaviour>();
        var all = FindObjectsByType<Behaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var behaviour = all[i];
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("CinemachineInputAxisController") ||
                typeName.Contains("InputAxisController") ||
                typeName.Contains("CinemachineInputProvider"))
            {
                found.Add(behaviour);
            }
        }

        cameraInputBehaviours = found.ToArray();

        if (cameraInputBehaviours.Length == 0)
            Debug.LogWarning("UiCursorFix: No camera input behaviours found. Assign the Cinemachine input controller manually if camera still moves in UI mode.");
    }

    public void SetUiMode(bool enabled)
    {
        UiMode = enabled;

        // Mirror the same mode switch used by the picker/UI scripts: free cursor, disable camera input.
        if (cameraInputBehaviours != null)
        {
            for (int i = 0; i < cameraInputBehaviours.Length; i++)
            {
                var behaviour = cameraInputBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = !UiMode;
            }
        }

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

        Debug.Log($"UiCursorFix: {(UiMode ? "UI mode" : "Camera mode")} | cameraInputs={(cameraInputBehaviours != null ? cameraInputBehaviours.Length : 0)}");
    }
}
