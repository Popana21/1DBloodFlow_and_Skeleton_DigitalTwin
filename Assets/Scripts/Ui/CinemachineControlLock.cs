using UnityEngine;

public class CinemachineControlLock : MonoBehaviour
{
    [Header("Drag the Cinemachine Input Axis Controller component here")]
    public Behaviour cinemachineInputAxisController;

    [Header("Toggle")]
    public int toggleMouseButton = 1; // 1 = Right Mouse Button
    public bool controlEnabled = true;

    void Awake()
    {
        // If not assigned, try to auto-find it on this object.
        if (cinemachineInputAxisController == null)
        {
            foreach (var b in GetComponents<Behaviour>())
            {
                if (b != null && b.GetType().Name == "CinemachineInputAxisController")
                {
                    cinemachineInputAxisController = b;
                    break;
                }
            }
        }

        Apply();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(toggleMouseButton))
        {
            controlEnabled = !controlEnabled;
            Apply();
        }

        // ESC always re-enables camera control
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            controlEnabled = true;
            Apply();
        }
    }

    void Apply()
    {
        if (cinemachineInputAxisController != null)
            cinemachineInputAxisController.enabled = controlEnabled;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 520, 20),
            $"Cinemachine control: {(controlEnabled ? "ON" : "OFF")}  (RMB toggles, ESC enables)");
    }
}
