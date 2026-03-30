using UnityEngine;

public class CinemachineClickToControl : MonoBehaviour
{
    [Header("Drag the Cinemachine Input Axis Controller here")]
    public Behaviour inputAxisController; // CinemachineInputAxisController is a Behaviour

    [Header("Toggle")]
    public int mouseButtonToggle = 1;     // 1 = right mouse button
    public bool controlEnabled = true;

    void Awake()
    {
        // Auto-find keeps the script usable without a hard compile-time Cinemachine dependency.
        if (inputAxisController == null)
        {
            // This will grab the first Behaviour on this object that matches the type name
            // without requiring a compile-time reference.
            foreach (var b in GetComponents<Behaviour>())
            {
                if (b != null && b.GetType().Name.Contains("CinemachineInputAxisController"))
                {
                    inputAxisController = b;
                    break;
                }
            }
        }

        Apply();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(mouseButtonToggle))
        {
            controlEnabled = !controlEnabled;
            Apply();
        }

        // ESC always re-enables mouse (optional)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            controlEnabled = true;
            Apply();
        }
    }

    void Apply()
    {
        if (inputAxisController != null)
            inputAxisController.enabled = controlEnabled;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 450, 20),
            $"Cinemachine control: {(controlEnabled ? "ON" : "OFF")} (RMB toggles, ESC enables)");
    }
}
