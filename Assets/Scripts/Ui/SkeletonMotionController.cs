using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SkeletonMotionController : MonoBehaviour
{
    public enum MotionCategory
    {
        Crawl,
        Head,
        Normal,
        OneLeg,
        Upper
    }

    [Header("References")]
    public Animator skeletonAnimator;
    public HeartbeatController heartbeatController; // optional legacy reference; no longer drives motion speed
    public Slider motionSpeedSlider;
    public TextMeshProUGUI motionSpeedLabel;

    [Header("Category Buttons (single-select)")]
    public Button crawlButton;
    public Button headButton;
    public Button normalButton;
    public Button oneLegButton;
    public Button upperButton;

    [Header("Category Dropdowns")]
    public TMP_Dropdown crawlDropdown;
    public TMP_Dropdown headDropdown;
    public TMP_Dropdown normalDropdown;
    public TMP_Dropdown oneLegDropdown;
    public TMP_Dropdown upperDropdown;

    [Header("Animation Clips")]
    public List<AnimationClip> crawlClips = new();
    public List<AnimationClip> headClips = new();
    public List<AnimationClip> normalClips = new();
    public List<AnimationClip> oneLegClips = new();
    public List<AnimationClip> upperClips = new();

    [Header("Keyboard")]
    public bool enableKeyboard = true;
    public KeyCode nextMoveKey = KeyCode.RightArrow;
    public KeyCode prevMoveKey = KeyCode.LeftArrow;
    public KeyCode playSelectedActionKey = KeyCode.Return;

    [Header("Behavior")]
    public MotionCategory startCategory = MotionCategory.Normal;
    public bool autoplayIdleOnStart = true;
    [Tooltip("If enabled, selecting a category button immediately plays that category's current dropdown selection.")]
    public bool playSelectionOnCategoryChange = true;
    [Tooltip("If enabled, startup begins from index 0 of the start category instead of forcing the first idle clip.")]
    public bool startWithFirstClipSelection = true;
    [Tooltip("If enabled, selections from dropdown/left-right arrows always loop (including attack/jump/etc.). Enter key still plays once.")]
    public bool loopAllDropdownSelections = true;
    [Range(0.1f, 3f)] public float motionSpeed = 1f;
    public bool verboseLogs = true;

    private MotionCategory _activeCategory;
    private readonly Dictionary<MotionCategory, int> _selectedIndexByCategory = new();
    private readonly Dictionary<MotionCategory, int> _selectedMoveIndexByCategory = new();
    private readonly Dictionary<MotionCategory, List<AnimationClip>> _clipsByCategory = new();
    private readonly Dictionary<MotionCategory, TMP_Dropdown> _dropdownByCategory = new();

    private PlayableGraph _graph;
    private AnimationPlayableOutput _output;
    private AnimationMixerPlayable _mixer;
    private Playable _currentClipPlayable;
    private Coroutine _actionRoutine;
    private bool _playOnceMode;

    void Start()
    {
        if (skeletonAnimator == null)
        {
            Debug.LogError("SkeletonMotionController: skeletonAnimator is not assigned.");
            enabled = false;
            return;
        }

        _clipsByCategory[MotionCategory.Crawl] = crawlClips;
        _clipsByCategory[MotionCategory.Head] = headClips;
        _clipsByCategory[MotionCategory.Normal] = normalClips;
        _clipsByCategory[MotionCategory.OneLeg] = oneLegClips;
        _clipsByCategory[MotionCategory.Upper] = upperClips;

        _dropdownByCategory[MotionCategory.Crawl] = crawlDropdown;
        _dropdownByCategory[MotionCategory.Head] = headDropdown;
        _dropdownByCategory[MotionCategory.Normal] = normalDropdown;
        _dropdownByCategory[MotionCategory.OneLeg] = oneLegDropdown;
        _dropdownByCategory[MotionCategory.Upper] = upperDropdown;

        BuildPlayableGraph();
        WireUi();
        PopulateDropdowns();
        InitializeMotionSpeedUi();

        SetActiveCategory(startCategory);

        if (startWithFirstClipSelection)
        {
            _selectedIndexByCategory[startCategory] = 0;

            if (_dropdownByCategory.TryGetValue(startCategory, out TMP_Dropdown startDropdown) && startDropdown != null)
                startDropdown.SetValueWithoutNotify(0);

            PlaySelection(startCategory, 0, forcePlayOnce: false);
        }
        else if (autoplayIdleOnStart)
        {
            var idle = FindIdleClip(startCategory);
            if (idle != null) PlayLoop(idle);
        }
    }

    void Update()
    {
        float speedScale = Mathf.Max(0.01f, motionSpeed);

        if (_currentClipPlayable.IsValid())
            _currentClipPlayable.SetSpeed(speedScale);

        if (!enableKeyboard) return;

        if (WasPressed(nextMoveKey))
            StepSelectedOption(+1);

        if (WasPressed(prevMoveKey))
            StepSelectedOption(-1);

        if (WasPressed(playSelectedActionKey))
            PlaySelectedActionOnce();
    }

    void OnDestroy()
    {
        if (_graph.IsValid())
            _graph.Destroy();
    }

    void BuildPlayableGraph()
    {
        // Bypass Animator Controller transitions and play selected clips directly through Playables.
        _graph = PlayableGraph.Create("SkeletonMotionGraph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        _mixer = AnimationMixerPlayable.Create(_graph, 1);

        _output = AnimationPlayableOutput.Create(_graph, "SkeletonOutput", skeletonAnimator);
        _output.SetSourcePlayable(_mixer);

        _graph.Play();
    }

    void WireUi()
    {
        if (crawlButton != null) crawlButton.onClick.AddListener(() => SetActiveCategory(MotionCategory.Crawl));
        if (headButton != null) headButton.onClick.AddListener(() => SetActiveCategory(MotionCategory.Head));
        if (normalButton != null) normalButton.onClick.AddListener(() => SetActiveCategory(MotionCategory.Normal));
        if (oneLegButton != null) oneLegButton.onClick.AddListener(() => SetActiveCategory(MotionCategory.OneLeg));
        if (upperButton != null) upperButton.onClick.AddListener(() => SetActiveCategory(MotionCategory.Upper));

        foreach (var kv in _dropdownByCategory)
        {
            MotionCategory category = kv.Key;
            TMP_Dropdown dd = kv.Value;
            if (dd == null) continue;

            dd.onValueChanged.RemoveAllListeners();
            dd.onValueChanged.AddListener(index =>
            {
                _selectedIndexByCategory[category] = index;
                if (category != _activeCategory) return;
                PlaySelection(category, index, forcePlayOnce: false);
            });
        }
    }

    void InitializeMotionSpeedUi()
    {
        if (motionSpeedSlider != null)
        {
            motionSpeedSlider.minValue = 0.1f;
            motionSpeedSlider.maxValue = 3f;
            motionSpeed = Mathf.Clamp(motionSpeed, motionSpeedSlider.minValue, motionSpeedSlider.maxValue);
            motionSpeedSlider.SetValueWithoutNotify(motionSpeed);
            motionSpeedSlider.onValueChanged.RemoveListener(OnMotionSpeedChanged);
            motionSpeedSlider.onValueChanged.AddListener(OnMotionSpeedChanged);
        }

        UpdateMotionSpeedLabel();
    }

    void OnMotionSpeedChanged(float value)
    {
        motionSpeed = Mathf.Clamp(value, 0.1f, 3f);
        if (motionSpeedSlider != null)
            motionSpeedSlider.SetValueWithoutNotify(motionSpeed);
        UpdateMotionSpeedLabel();
    }

    void UpdateMotionSpeedLabel()
    {
        if (motionSpeedLabel != null)
            motionSpeedLabel.text = $"Motion Speed: {motionSpeed:0.00}x";
    }

    void PopulateDropdowns()
    {
        foreach (var kv in _dropdownByCategory)
        {
            MotionCategory category = kv.Key;
            TMP_Dropdown dd = kv.Value;
            if (dd == null) continue;

            dd.ClearOptions();

            var clips = _clipsByCategory[category];
            var options = clips.Select(c => c != null ? c.name : "<null>").ToList();
            dd.AddOptions(options);

            _selectedIndexByCategory[category] = 0;
            _selectedMoveIndexByCategory[category] = 0;
            dd.SetValueWithoutNotify(0);
        }
    }

    void SetActiveCategory(MotionCategory category)
    {
        _activeCategory = category;

        // Emulate single-select tabs using plain buttons.
        SetButtonState(crawlButton, category != MotionCategory.Crawl);
        SetButtonState(headButton, category != MotionCategory.Head);
        SetButtonState(normalButton, category != MotionCategory.Normal);
        SetButtonState(oneLegButton, category != MotionCategory.OneLeg);
        SetButtonState(upperButton, category != MotionCategory.Upper);

        // Show only the dropdown relevant to the active movement family.
        SetDropdownVisible(crawlDropdown, category == MotionCategory.Crawl);
        SetDropdownVisible(headDropdown, category == MotionCategory.Head);
        SetDropdownVisible(normalDropdown, category == MotionCategory.Normal);
        SetDropdownVisible(oneLegDropdown, category == MotionCategory.OneLeg);
        SetDropdownVisible(upperDropdown, category == MotionCategory.Upper);

        if (playSelectionOnCategoryChange && _selectedIndexByCategory.TryGetValue(category, out int selectedIndex))
            PlaySelection(category, selectedIndex, forcePlayOnce: false);

        if (verboseLogs) Debug.Log($"SkeletonMotionController: active category = {_activeCategory}");
    }

    void SetButtonState(Button btn, bool interactable)
    {
        if (btn != null) btn.interactable = interactable;
    }

    void SetDropdownVisible(TMP_Dropdown dd, bool visible)
    {
        if (dd != null) dd.gameObject.SetActive(visible);
    }

    void StepSelectedOption(int direction)
    {
        var clips = _clipsByCategory[_activeCategory];
        if (clips == null || clips.Count == 0) return;

        int idx = _selectedIndexByCategory[_activeCategory];
        idx = ((idx + direction) % clips.Count + clips.Count) % clips.Count;
        _selectedIndexByCategory[_activeCategory] = idx;

        var dd = _dropdownByCategory[_activeCategory];
        if (dd != null) dd.SetValueWithoutNotify(idx);

        PlaySelection(_activeCategory, idx, forcePlayOnce: false);
    }

    void SyncContinuousMoveIndex(MotionCategory category, AnimationClip selectedMove)
    {
        var moves = GetContinuousMoves(category);
        int moveIdx = moves.IndexOf(selectedMove);
        if (moveIdx >= 0)
            _selectedMoveIndexByCategory[category] = moveIdx;
    }

    void PlaySelectedActionOnce()
    {
        int idx = _selectedIndexByCategory[_activeCategory];
        PlaySelection(_activeCategory, idx, forcePlayOnce: true);
    }

    void PlaySelection(MotionCategory category, int index, bool forcePlayOnce)
    {
        var clips = _clipsByCategory[category];
        if (clips == null || index < 0 || index >= clips.Count) return;

        var clip = clips[index];
        if (clip == null) return;

        if (_actionRoutine != null)
        {
            StopCoroutine(_actionRoutine);
            _actionRoutine = null;
        }

        string name = clip.name.ToLowerInvariant();
        bool isMove = name.Contains("_move");
        bool isIdle = name.Contains("_idle");
        bool oneShot = forcePlayOnce || (!loopAllDropdownSelections && !isMove && !isIdle);

        if (oneShot)
            _actionRoutine = StartCoroutine(PlayActionThenReturnToIdle(clip));
        else
            PlayLoop(clip);

        if (isMove)
            SyncContinuousMoveIndex(category, clip);

        if (verboseLogs)
            Debug.Log($"SkeletonMotionController: play -> {clip.name} ({(oneShot ? "once" : "loop")})");
    }

    IEnumerator PlayActionThenReturnToIdle(AnimationClip actionClip)
    {
        PlayOnce(actionClip);

        float speedScale = Mathf.Max(0.01f, motionSpeed);

        // Wait for the one-shot action at the current motion speed, then fall back to idle.
        float duration = actionClip.length / speedScale;
        yield return new WaitForSeconds(duration);

        var idle = FindIdleClip(_activeCategory);
        if (idle != null) PlayLoop(idle);

        _actionRoutine = null;
    }

    AnimationClip FindIdleClip(MotionCategory category)
    {
        var clips = _clipsByCategory[category];
        if (clips == null) return null;

        return clips.FirstOrDefault(c =>
            c != null && c.name.ToLowerInvariant().Contains("_idle"));
    }

    List<AnimationClip> GetContinuousMoves(MotionCategory category)
    {
        var clips = _clipsByCategory[category];
        if (clips == null) return new List<AnimationClip>();

        return clips.Where(c =>
            c != null && c.name.ToLowerInvariant().Contains("_move")).ToList();
    }

    void PlayLoop(AnimationClip clip)
    {
        _playOnceMode = false;
        PlayClipInternal(clip);
    }

    void PlayOnce(AnimationClip clip)
    {
        _playOnceMode = true;
        PlayClipInternal(clip);
    }

    void PlayClipInternal(AnimationClip clip)
    {
        if (!_graph.IsValid() || clip == null) return;

        if (_currentClipPlayable.IsValid())
            _graph.DestroyPlayable(_currentClipPlayable);

        var clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        clipPlayable.SetApplyFootIK(false);
        clipPlayable.SetApplyPlayableIK(false);
        clipPlayable.SetTime(0);

        _graph.Connect(clipPlayable, 0, _mixer, 0);
        _mixer.SetInputWeight(0, 1f);

        // If playing once, cap duration; otherwise let it run continuously.
        if (_playOnceMode)
            clipPlayable.SetDuration(clip.length);
        else
            clipPlayable.SetDuration(double.PositiveInfinity);

        _currentClipPlayable = clipPlayable;
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
            case KeyCode.Return: return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            default: return false;
        }
#else
        return false;
#endif
    }
}
