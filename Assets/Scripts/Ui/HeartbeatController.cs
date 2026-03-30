using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class HeartbeatController : MonoBehaviour
{
    const float kAutoUiVerticalOffset = 80f;
    const float kMinControlsPanelHeight = 380f;

    [Header("UI")]
    public Slider bpmSlider;
    public TextMeshProUGUI bpmLabel;
    public Slider diameterSlider;
    public TextMeshProUGUI diameterLabel;
    public TMP_Dropdown movementDropdown;
    public TMP_Dropdown colorMetricDropdown;
    public Toggle skeletonVisibilityToggle;
    public Slider audioVolumeSlider;
    public Toggle audioMuteToggle;

    [Header("Heartbeat")]
    [Range(40f, 180f)] public float bpm = 60f;
    public float bpmSmoothing = 8f;

    [Header("Diameter")]
    [Range(0.1f, 4f)] public float diameterScale = 1f;

    [Header("Optional references")]
    public SimulationPlaybackSystem playbackSystem;
    public HeartbeatAudioController heartbeatAudioController;
    public Animator skeletonAnimator;
    public RuntimeAnimatorController[] movementControllers;

    [Header("Keyboard Control")]
    public bool enableKeyboardControls = true;
    public KeyCode nextMovementKey = KeyCode.RightArrow;
    public KeyCode prevMovementKey = KeyCode.LeftArrow;
    public KeyCode bpmUpKey = KeyCode.UpArrow;
    public KeyCode bpmDownKey = KeyCode.DownArrow;
    public float bpmStep = 5f;

    [Header("Debug")]
    public bool verboseLogs = true;

    public float Phase01 { get; private set; }

    private int currentMovementIndex = 0;
    private float currentBpm;
    private Renderer[] skeletonRenderers;

    void Start()
    {
        currentBpm = bpm;

        // Auto-create the diameter controls from the BPM controls when the extra UI is absent.
        EnsureDiameterUi();

        if (playbackSystem == null)
            playbackSystem = FindAnyObjectByType<SimulationPlaybackSystem>();
        if (heartbeatAudioController == null)
            heartbeatAudioController = FindAnyObjectByType<HeartbeatAudioController>();

        if (skeletonAnimator == null && verboseLogs)
            Debug.LogWarning("HeartbeatController: skeletonAnimator is not assigned.");

        if ((movementControllers == null || movementControllers.Length == 0) && verboseLogs)
            Debug.LogWarning("HeartbeatController: movementControllers is empty.");

        DisablePanelNonInteractiveRaycasts();
        EnsureSliderInteraction(bpmSlider);
        EnsureSliderInteraction(diameterSlider);

        if (bpmSlider != null)
        {
            bpmSlider.minValue = 40f;
            bpmSlider.maxValue = 180f;
            bpm = Mathf.Clamp(bpm, bpmSlider.minValue, bpmSlider.maxValue);
            bpmSlider.SetValueWithoutNotify(bpm);
            bpmSlider.onValueChanged.RemoveListener(OnBpmChanged);
            bpmSlider.onValueChanged.AddListener(OnBpmChanged);
        }

        if (diameterSlider != null)
        {
            diameterSlider.minValue = 0.1f;
            diameterSlider.maxValue = 4f;
            diameterScale = Mathf.Clamp(diameterScale, diameterSlider.minValue, diameterSlider.maxValue);
            diameterSlider.SetValueWithoutNotify(diameterScale);
            diameterSlider.onValueChanged.RemoveListener(OnDiameterChanged);
            diameterSlider.onValueChanged.AddListener(OnDiameterChanged);
        }

        if (movementDropdown != null)
        {
            movementDropdown.onValueChanged.RemoveListener(OnMovementChanged);
            movementDropdown.onValueChanged.AddListener(OnMovementChanged);
            PopulateMovementDropdown();
        }

        if (colorMetricDropdown != null)
        {
            colorMetricDropdown.onValueChanged.RemoveListener(OnColorMetricChanged);
            colorMetricDropdown.onValueChanged.AddListener(OnColorMetricChanged);
            PopulateColorMetricDropdown();
        }

        if (audioVolumeSlider != null)
        {
            audioVolumeSlider.minValue = 0f;
            audioVolumeSlider.maxValue = 1f;
            float v = heartbeatAudioController != null ? heartbeatAudioController.masterVolume : 1f;
            audioVolumeSlider.SetValueWithoutNotify(v);
            audioVolumeSlider.onValueChanged.RemoveListener(OnAudioVolumeChanged);
            audioVolumeSlider.onValueChanged.AddListener(OnAudioVolumeChanged);
        }

        if (audioMuteToggle != null)
        {
            audioMuteToggle.onValueChanged.RemoveListener(OnAudioMuteChanged);
            audioMuteToggle.onValueChanged.AddListener(OnAudioMuteChanged);
            bool m = heartbeatAudioController != null && heartbeatAudioController.mute;
            audioMuteToggle.onValueChanged.RemoveListener(OnAudioMuteChanged);
            audioMuteToggle.isOn = m;
            audioMuteToggle.onValueChanged.AddListener(OnAudioMuteChanged);
        }

        if (movementControllers != null && movementControllers.Length > 0)
            SetMovementByIndex(currentMovementIndex);

        CacheSkeletonRenderers();
        if (skeletonVisibilityToggle != null)
        {
            skeletonVisibilityToggle.onValueChanged.RemoveListener(OnSkeletonVisibilityChanged);
            skeletonVisibilityToggle.onValueChanged.AddListener(OnSkeletonVisibilityChanged);
            // On = visible
            skeletonVisibilityToggle.onValueChanged.RemoveListener(OnSkeletonVisibilityChanged);
            skeletonVisibilityToggle.isOn = true;
            skeletonVisibilityToggle.onValueChanged.AddListener(OnSkeletonVisibilityChanged);
            OnSkeletonVisibilityChanged(true);
        }

        ApplyDiameter();
        UpdateLabels();
    }

    void Update()
    {
        currentBpm = Mathf.Lerp(currentBpm, bpm, 1f - Mathf.Exp(-bpmSmoothing * Time.deltaTime));
        // Phase01 is the normalized master timeline used by playback and audio systems.
        float cyclesPerSecond = currentBpm / 60f;
        Phase01 = Mathf.Repeat(Phase01 + Time.deltaTime * cyclesPerSecond, 1f);

        if (diameterSlider != null)
        {
            float sliderDiameter = Mathf.Clamp(diameterSlider.value, 0.1f, 4f);
            if (!Mathf.Approximately(sliderDiameter, diameterScale))
            {
                diameterScale = sliderDiameter;
                UpdateLabels();
            }
        }

        // Keep the playback diameter scale synchronized even if a UI event is missed.
        if (playbackSystem == null)
            playbackSystem = FindAnyObjectByType<SimulationPlaybackSystem>();
        ApplyDiameter();

        if (!enableKeyboardControls) return;

        if (WasPressed(nextMovementKey))
            SetMovementByIndex(currentMovementIndex + 1);

        if (WasPressed(prevMovementKey))
            SetMovementByIndex(currentMovementIndex - 1);

        if (WasPressed(bpmUpKey))
            OnBpmChanged(bpm + bpmStep);

        if (WasPressed(bpmDownKey))
            OnBpmChanged(bpm - bpmStep);
    }

    void OnBpmChanged(float value)
    {
        bpm = Mathf.Clamp(value, 40f, 180f);
        if (bpmSlider != null) bpmSlider.SetValueWithoutNotify(bpm);
        UpdateLabels();
    }

    void OnDiameterChanged(float value)
    {
        diameterScale = Mathf.Clamp(value, 0.1f, 4f);
        if (diameterSlider != null) diameterSlider.SetValueWithoutNotify(diameterScale);
        ApplyDiameter();
        UpdateLabels();
    }

    void OnMovementChanged(int index)
    {
        SetMovementByIndex(index);
    }

    void OnColorMetricChanged(int index)
    {
        if (playbackSystem != null)
            playbackSystem.SetColorMetric(index);
    }

    void OnSkeletonVisibilityChanged(bool visible)
    {
        if (skeletonRenderers == null) return;
        for (int i = 0; i < skeletonRenderers.Length; i++)
        {
            var r = skeletonRenderers[i];
            if (r != null) r.enabled = visible;
        }
    }

    void OnAudioVolumeChanged(float value)
    {
        if (heartbeatAudioController != null)
            heartbeatAudioController.SetMasterVolume(value);
    }

    void OnAudioMuteChanged(bool muted)
    {
        if (heartbeatAudioController != null)
            heartbeatAudioController.SetMuted(muted);
    }

    void SetMovementByIndex(int index)
    {
        if (movementControllers == null || movementControllers.Length == 0) return;
        if (skeletonAnimator == null) return;

        int n = movementControllers.Length;
        currentMovementIndex = ((index % n) + n) % n;

        var controller = movementControllers[currentMovementIndex];
        if (controller == null) return;

        skeletonAnimator.runtimeAnimatorController = controller;

        if (movementDropdown != null)
            movementDropdown.SetValueWithoutNotify(currentMovementIndex);

        if (verboseLogs)
            Debug.Log($"HeartbeatController: switched movement to [{currentMovementIndex}] {controller.name}");
    }

    void ApplyDiameter()
    {
        if (playbackSystem != null)
            playbackSystem.globalDiameterScale = diameterScale;
    }

    void PopulateMovementDropdown()
    {
        if (movementDropdown == null) return;

        movementDropdown.ClearOptions();

        var options = new List<string>();
        if (movementControllers != null)
        {
            for (int i = 0; i < movementControllers.Length; i++)
            {
                var c = movementControllers[i];
                options.Add(c != null ? c.name : $"Style {i}");
            }
        }

        movementDropdown.AddOptions(options);
    }

    void PopulateColorMetricDropdown()
    {
        if (colorMetricDropdown == null) return;

        colorMetricDropdown.ClearOptions();
        colorMetricDropdown.AddOptions(new List<string> { "Pressure", "Flow" });

        int selected = (playbackSystem != null && playbackSystem.colorMetric == SimulationPlaybackSystem.ColorMetric.Flow)
            ? 1
            : 0;
        colorMetricDropdown.SetValueWithoutNotify(selected);
    }

    void UpdateLabels()
    {
        if (bpmLabel != null)
            bpmLabel.text = $"BPM: {bpm:0}";

        if (diameterLabel != null)
            diameterLabel.text = $"Diameter: {diameterScale:0.00}x";
    }

    bool WasPressed(KeyCode key)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(key)) return true;
#endif

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;

        switch (key)
        {
            case KeyCode.LeftArrow: return kb.leftArrowKey.wasPressedThisFrame;
            case KeyCode.RightArrow: return kb.rightArrowKey.wasPressedThisFrame;
            case KeyCode.UpArrow: return kb.upArrowKey.wasPressedThisFrame;
            case KeyCode.DownArrow: return kb.downArrowKey.wasPressedThisFrame;
            default: return false;
        }
#else
        return false;
#endif
    }

    void EnsureDiameterUi()
    {
        if (diameterLabel == null && bpmLabel != null)
        {
            var labelObject = Instantiate(bpmLabel.gameObject, bpmLabel.transform.parent);
            labelObject.name = "DiameterLabel";

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchoredPosition = bpmLabel.rectTransform.anchoredPosition + new Vector2(0f, -kAutoUiVerticalOffset);

            diameterLabel = labelObject.GetComponent<TextMeshProUGUI>();
            if (diameterLabel != null)
                diameterLabel.raycastTarget = false;
        }

        if (diameterSlider == null && bpmSlider != null)
        {
            var sliderObject = Instantiate(bpmSlider.gameObject, bpmSlider.transform.parent);
            sliderObject.name = "DiameterSlider";

            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchoredPosition = ((RectTransform)bpmSlider.transform).anchoredPosition + new Vector2(0f, -kAutoUiVerticalOffset);

            diameterSlider = sliderObject.GetComponent<Slider>();
            if (diameterSlider != null)
                diameterSlider.onValueChanged.RemoveAllListeners();
        }

        ExpandControlsPanelIfNeeded();
    }

    void ExpandControlsPanelIfNeeded()
    {
        Transform reference = null;
        if (diameterSlider != null) reference = diameterSlider.transform.parent;
        else if (bpmSlider != null) reference = bpmSlider.transform.parent;

        if (reference == null) return;

        var panelRect = reference as RectTransform;
        if (panelRect == null) return;

        var size = panelRect.sizeDelta;
        if (size.y < kMinControlsPanelHeight)
        {
            size.y = kMinControlsPanelHeight;
            panelRect.sizeDelta = size;
        }
    }

    void DisablePanelNonInteractiveRaycasts()
    {
        Transform reference = null;
        if (bpmLabel != null) reference = bpmLabel.transform.parent;
        else if (bpmSlider != null) reference = bpmSlider.transform.parent;
        else if (diameterLabel != null) reference = diameterLabel.transform.parent;
        else if (diameterSlider != null) reference = diameterSlider.transform.parent;

        if (reference == null) return;

        // Disable decorative UI raycasts so vessel picking still works through the control panel canvas.
        var keepRaycasts = new HashSet<Graphic>();
        var selectables = reference.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            var selectable = selectables[i];
            if (selectable == null) continue;

            if (selectable.targetGraphic != null)
                keepRaycasts.Add(selectable.targetGraphic);

            if (selectable is Slider slider && slider.handleRect != null)
            {
                var handleGraphic = slider.handleRect.GetComponent<Graphic>();
                if (handleGraphic != null)
                    keepRaycasts.Add(handleGraphic);
            }
        }

        var graphics = reference.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null) continue;
            graphic.raycastTarget = keepRaycasts.Contains(graphic);
        }
    }

    void EnsureSliderInteraction(Slider slider)
    {
        if (slider == null) return;

        if (slider.targetGraphic != null)
            slider.targetGraphic.raycastTarget = true;

        if (slider.handleRect != null)
        {
            var handleGraphic = slider.handleRect.GetComponent<Graphic>();
            if (handleGraphic != null)
                handleGraphic.raycastTarget = true;
        }
    }

    void CacheSkeletonRenderers()
    {
        if (skeletonAnimator == null)
        {
            skeletonRenderers = System.Array.Empty<Renderer>();
            return;
        }
        skeletonRenderers = skeletonAnimator.GetComponentsInChildren<Renderer>(true);
    }
}
