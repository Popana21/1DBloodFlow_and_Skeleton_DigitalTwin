using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VesselNamePicker : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Transform vesselsRoot;
    public TextMeshProUGUI vesselNameLabel;

    [Header("Camera-Off Gating (optional)")]
    public UiCameraModeToggle uiCameraModeToggle;
    public CinemachineControlLock cinemachineControlLock;
    public CinemachineClickToControl cinemachineClickToControl;
    public Behaviour cinemachineInputAxisController;
    public bool requireCameraOff = true;

    [Header("Picking")]
    public LayerMask raycastMask = ~0;
    public float maxRayDistance = 1000f;
    public float sphereCastRadius = 0.03f;
    public bool ignoreClicksOverUI = true;

    [Header("Label")]
    public string labelPrefix = "Selected Vessel: ";
    public string noSelectionText = "-";

    [Header("Editor")]
    public bool selectInHierarchyInEditor = true;

    [Header("Debug")]
    public bool debugLogs = false;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (vesselsRoot == null)
        {
            var go = GameObject.Find("Vessels");
            if (go != null) vesselsRoot = go.transform;
        }
        if (vesselNameLabel != null)
            vesselNameLabel.text = labelPrefix + noSelectionText;
    }

    void Update()
    {
        if (!WasLeftMousePressed()) return;
        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (debugLogs) Debug.Log("VesselNamePicker: click ignored because pointer is over UI.");
            return;
        }
        if (requireCameraOff && !IsCameraOffMode())
        {
            if (debugLogs) Debug.Log("VesselNamePicker: click ignored because camera is not in UI/camera-off mode.");
            return;
        }
        if (targetCamera == null)
        {
            if (debugLogs) Debug.LogWarning("VesselNamePicker: targetCamera is null.");
            return;
        }

        // Resolve the clicked mesh child back to its logical vessel root object.
        var ray = targetCamera.ScreenPointToRay(GetPointerPosition());
        Transform vesselRootTransform = ResolveVesselRootFromRay(ray);
        if (vesselRootTransform == null)
        {
            if (debugLogs) Debug.Log("VesselNamePicker: no vessel hit.");
            return;
        }

        if (vesselNameLabel != null)
            vesselNameLabel.text = labelPrefix + vesselRootTransform.name;

        if (debugLogs) Debug.Log($"VesselNamePicker: selected '{vesselRootTransform.name}'.");

#if UNITY_EDITOR
        if (selectInHierarchyInEditor)
            Selection.activeGameObject = vesselRootTransform.gameObject;
#endif
    }

    bool IsCameraOffMode()
    {
        bool hasEvidence = false;

        if (uiCameraModeToggle != null)
        {
            hasEvidence = true;
            if (uiCameraModeToggle.UiMode)
                return true;

            if (uiCameraModeToggle.cameraInputBehaviours != null)
            {
                for (int i = 0; i < uiCameraModeToggle.cameraInputBehaviours.Length; i++)
                {
                    var behaviour = uiCameraModeToggle.cameraInputBehaviours[i];
                    if (behaviour != null && !behaviour.enabled)
                        return true;
                }
            }
        }

        if (cinemachineControlLock != null)
        {
            hasEvidence = true;
            if (!cinemachineControlLock.controlEnabled)
                return true;
        }

        if (cinemachineClickToControl != null)
        {
            hasEvidence = true;
            if (!cinemachineClickToControl.controlEnabled)
                return true;
        }

        if (cinemachineInputAxisController != null)
        {
            hasEvidence = true;
            if (!cinemachineInputAxisController.enabled)
                return true;
        }

        // In this project, camera-off mode also means the cursor is free for UI interaction.
        if (Cursor.lockState == CursorLockMode.None || Cursor.visible)
            return true;

        // If nothing is assigned, allow picking rather than block it.
        return !hasEvidence;
    }

    Transform ResolveVesselRootFromRay(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var vesselRootTransform = ResolveVesselRoot(hits[i].transform);
                if (vesselRootTransform != null)
                    return vesselRootTransform;
            }
        }

        if (sphereCastRadius > 0f)
        {
            RaycastHit[] sphereHits = Physics.SphereCastAll(ray, sphereCastRadius, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore);
            if (sphereHits != null && sphereHits.Length > 0)
            {
                System.Array.Sort(sphereHits, (a, b) => a.distance.CompareTo(b.distance));

                for (int i = 0; i < sphereHits.Length; i++)
                {
                    var vesselRootTransform = ResolveVesselRoot(sphereHits[i].transform);
                    if (vesselRootTransform != null)
                        return vesselRootTransform;
                }
            }
        }

        return null;
    }

    Transform ResolveVesselRoot(Transform hit)
    {
        if (hit == null) return null;

        if (vesselsRoot != null)
        {
            var t = hit;
            while (t != null && t.parent != vesselsRoot)
                t = t.parent;

            if (t != null && t.parent == vesselsRoot)
                return t;
        }

        // Fallback: infer from a "_segN" child name.
        string n = hit.name;
        int segIdx = n.LastIndexOf("_seg", System.StringComparison.Ordinal);
        if (segIdx > 0)
        {
            string rootName = n.Substring(0, segIdx);
            if (vesselsRoot != null)
            {
                var found = vesselsRoot.Find(rootName);
                if (found != null) return found;
            }
        }

        return null;
    }

    bool WasLeftMousePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Mouse.current != null
            && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
#else
        return false;
#endif
    }

    Vector2 GetPointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null)
            return UnityEngine.InputSystem.Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }
}
