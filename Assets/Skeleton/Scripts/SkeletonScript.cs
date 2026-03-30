using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sample
{
    /// <summary>
    /// Original sample controller, but made safe for "visualization only" use:
    /// - Never calls CharacterController.Move on a disabled / missing controller.
    /// - Allows you to turn off gravity / movement / input from the Inspector.
    /// </summary>
    public class SkeletonScript : MonoBehaviour
    {
        private Animator _Animator;
        private RuntimeAnimatorController[] _CacheAnimator = new RuntimeAnimatorController[6];
        private int _Animator_num = 0;

        private CharacterController _Ctrl;
        private Vector3 _MoveDirection = Vector3.zero;
        private GameObject _View_Camera;

        private const int num = 15;
        [SerializeField] private GameObject[] _Mesh = new GameObject[num];

        private GameObject _Skull;
        [SerializeField] private GameObject HandRPos;

        // ---------- Behaviour toggles (important for your project) ----------
        [Header("Visualization / Gameplay Toggles")]
        [Tooltip("If false, no gravity is applied (character will not fall).")]
        public bool enableGravity = false;

        [Tooltip("If false, Move/Jump etc will NOT move the character via CharacterController.")]
        public bool enableCharacterMotion = false;

        [Tooltip("If false, key-driven actions (Q/W/E/A/X/Space, arrows, Z/S) are ignored.\n" +
                 "Animator still plays its default state machine.")]
        public bool enableInputActions = false;

        [Tooltip("If true, the main camera is kept at a fixed offset from this skeleton.")]
        public bool cameraFollowsSkeleton = false;

        // internal guards / warnings
        private bool _warnedMissingCamera = false;
        private bool _warnedMissingCtrl = false;
        private bool _warnedMissingSkullOrHand = false;

        // Animator hashes
        private static readonly int IdleState = Animator.StringToHash("Base Layer.idle");
        private static readonly int MoveState = Animator.StringToHash("Base Layer.move");
        private static readonly int JumpState = Animator.StringToHash("Base Layer.jump");
        private static readonly int LandingState = Animator.StringToHash("Base Layer.landing");
        private static readonly int DamageState = Animator.StringToHash("Base Layer.damage");
        private static readonly int DownState = Animator.StringToHash("Base Layer.down");
        private static readonly int FaintState = Animator.StringToHash("Base Layer.faint");
        private static readonly int StandUpFaintState = Animator.StringToHash("Base Layer.standup_faint");
        private static readonly int AttackState = Animator.StringToHash("Base Layer.attack");
        private static readonly int AttackChargeState = Animator.StringToHash("Base Layer.attack_charge");
        private static readonly int ThrowState = Animator.StringToHash("Base Layer.throw");
        private static readonly int ThrowChargeState = Animator.StringToHash("Base Layer.throw_charge");

        private static readonly int JumpTag = Animator.StringToHash("Jump");
        private static readonly int DamageTag = Animator.StringToHash("Damage");
        private static readonly int FaintTag = Animator.StringToHash("Faint");
        private static readonly int AttackTag = Animator.StringToHash("Attack");
        private static readonly int ThrowTag = Animator.StringToHash("Throw");

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int JumpPoseParameter = Animator.StringToHash("JumpPose");

        // ---------------- STATUS flags ----------------
        private const int Jump = 1;
        private const int Damage = 2;
        private const int Faint = 3;
        private const int Attack = 4;
        private const int Throw = 5;

        private Dictionary<int, bool> _Status = new Dictionary<int, bool>
        {
            {Jump, false },
            {Damage, false },
            {Faint, false },
            {Attack, false },
            {Throw, false },
        };

        void Start()
        {
            // Animator (required)
            _Animator = GetComponent<Animator>();
            if (_Animator == null)
            {
                Debug.LogError("SkeletonScript: Animator component not found on the GameObject. Disabling script.");
                enabled = false;
                return;
            }

            // Load animator controllers (original behaviour)
            _CacheAnimator[0] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonHumanoid0");
            _CacheAnimator[1] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonHumanoid1");
            _CacheAnimator[2] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonUpper");
            _CacheAnimator[3] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonLower");
            _CacheAnimator[4] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonOneleg");
            _CacheAnimator[5] = Resources.Load<RuntimeAnimatorController>("Animators/AnimatorSkeletonCrawl");

            // CharacterController (optional)
            _Ctrl = GetComponent<CharacterController>();
            if (_Ctrl == null)
            {
                if (!_warnedMissingCtrl)
                {
                    Debug.LogWarning("SkeletonScript: CharacterController not found. Movement / GRAVITY will be skipped.");
                    _warnedMissingCtrl = true;
                }
            }

            // Camera (robust acquisition)
            _View_Camera = Camera.main != null ? Camera.main.gameObject : null;
            if (_View_Camera == null)
            {
                _View_Camera = GameObject.Find("Main Camera") ?? GameObject.FindWithTag("MainCamera");
            }
            if (_View_Camera == null)
            {
                if (!_warnedMissingCamera)
                {
                    Debug.LogWarning("SkeletonScript: No Main Camera found in scene. CAMERA() will be skipped until a camera is present.");
                    _warnedMissingCamera = true;
                }
            }

            // Skull lookup (handles: 'skeleton_skull', 'Skeleton.Humanoid.Head', any child containing 'head')
            GameObject foundSkull = null;

            // 1) exact global name
            foundSkull = GameObject.Find("skeleton_skull");

            // 2) explicit child path (your rig)
            if (foundSkull == null)
            {
                var candidate = transform.Find("Skeleton.Humanoid.Head");
                if (candidate != null) foundSkull = candidate.gameObject;
            }

            // 3) deep search for any child with 'head'
            if (foundSkull == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    if (t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foundSkull = t.gameObject;
                        break;
                    }
                }
            }

            // 4) final global search
            if (foundSkull == null)
            {
#if UNITY_2023_2_OR_NEWER
                var allTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
#elif UNITY_2023_1_OR_NEWER
                var allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
#else
                var allTransforms = GameObject.FindObjectsOfType<Transform>();
#endif
                foreach (var t in allTransforms)
                {
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    if (t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foundSkull = t.gameObject;
                        break;
                    }
                }
            }

            if (foundSkull != null)
            {
                _Skull = foundSkull;
            }
            else
            {
                _Skull = null;
                if (!_warnedMissingSkullOrHand)
                {
                    Debug.LogWarning("SkeletonScript: skull object not found (tried 'skeleton_skull', 'Skeleton.Humanoid.Head', any child containing 'head').");
                    _warnedMissingSkullOrHand = true;
                }
            }

            // HandRPos auto-detect (if not assigned)
            if (HandRPos == null)
            {
                string[] boneCandidates =
                {
                    "Hand_R","hand_r","RightHand","HandR","hand.R",
                    "wrist_r","Wrist_R","Right_Wrist","RightHandTarget","Hand.R",
                    "Hand.R","Hand.R","Hand.R"
                };

                foreach (var name in boneCandidates)
                {
                    var direct = transform.Find(name);
                    if (direct != null)
                    {
                        HandRPos = direct.gameObject;
                        break;
                    }
                }

                if (HandRPos == null)
                {
                    foreach (var t in GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null || string.IsNullOrEmpty(t.name)) continue;
                        foreach (var name in boneCandidates)
                        {
                            if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                            {
                                HandRPos = t.gameObject;
                                break;
                            }
                        }
                        if (HandRPos != null) break;
                    }
                }
            }

            if (HandRPos == null)
            {
                if (!_warnedMissingSkullOrHand)
                {
                    Debug.LogWarning("SkeletonScript: HandRPos not assigned and auto-find failed.");
                    _warnedMissingSkullOrHand = true;
                }
            }

            // Parent skull to hand (if both exist)
            if (_Skull != null && HandRPos != null)
            {
                try
                {
                    _Skull.transform.SetParent(HandRPos.transform);
                    _Skull.transform.position = HandRPos.transform.position;
                    _Skull.transform.localPosition = new Vector3(-0.00017f, 0.00052f, -0.00247f);

                    // Only deactivate if it's the legacy separate skull prefab
                    if (string.Equals(_Skull.name, "skeleton_skull", StringComparison.OrdinalIgnoreCase))
                    {
                        _Skull.SetActive(false);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("SkeletonScript: failed to parent skull to HandRPos: " + e.Message);
                }
            }
        }

        void Update()
        {
            if (cameraFollowsSkeleton)
                CAMERA();

            if (enableGravity)
                GRAVITY();   // internally checks controller

            STATUS();

            // If input actions are disabled, we stop here.
            if (!enableInputActions)
                return;

            CHANGE_ANIMATOR();

            if (!_Status.ContainsValue(true))
            {
                MOVE();
                JUMP();
                DAMAGE();
                FAINT();
                ATTACK();
                THROW();
            }
            else
            {
                int status_name = 0;
                foreach (var i in _Status)
                {
                    if (i.Value)
                    {
                        status_name = i.Key;
                        break;
                    }
                }

                if (status_name == Jump)
                {
                    MOVE();
                    JUMP();
                    FAINT();
                }
                else if (status_name == Damage)
                {
                    DAMAGE();
                }
                else if (status_name == Faint)
                {
                    FAINT();
                }
                else if (status_name == Attack)
                {
                    ATTACK();
                }
                else if (status_name == Throw)
                {
                    THROW();
                }
            }
        }

        // ---------------- STATUS ----------------
        private void STATUS()
        {
            if (_Animator == null) return;

            var info = _Animator.GetCurrentAnimatorStateInfo(0);
            _Status[Jump] = (info.tagHash == JumpTag);
            _Status[Damage] = (info.tagHash == DamageTag);
            _Status[Faint] = (info.tagHash == FaintTag);
            _Status[Attack] = (info.tagHash == AttackTag);
            _Status[Throw] = (info.tagHash == ThrowTag);
        }

        // ---------------- CAMERA ----------------
        private void CAMERA()
        {
            if (_View_Camera == null)
            {
                _View_Camera = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
                if (_View_Camera == null && !_warnedMissingCamera)
                {
                    Debug.LogWarning("SkeletonScript.CAMERA: No camera found. CAMERA() skipped.");
                    _warnedMissingCamera = true;
                    return;
                }
            }

            _View_Camera.transform.position = transform.position + new Vector3(0, 1.0f, 3.0f);
        }

        // ---------------- GRAVITY ----------------
        private void GRAVITY()
        {
            if (!enableGravity) return;
            if (_Ctrl == null || !_Ctrl.enabled || !gameObject.activeInHierarchy)
                return;

            if (CheckGrounded())
            {
                if (_MoveDirection.y < -0.1f)
                    _MoveDirection.y = -0.1f;
            }

            _MoveDirection.y -= 0.1f;
            _Ctrl.Move(_MoveDirection * Time.deltaTime);
        }

        private bool CheckGrounded()
        {
            if (_Ctrl == null || !_Ctrl.enabled) return false;

            if (_Ctrl.isGrounded)
                return true;

            Ray ray = new Ray(transform.position + Vector3.up * 0.1f, Vector3.down);
            float range = 0.2f;
            return Physics.Raycast(ray, range);
        }

        // ---------------- MOVE ----------------
        private void MOVE()
        {
            if (!enableCharacterMotion) return;
            if (_Animator == null) return;

            float speed = _Animator.GetFloat(SpeedParameter);

            // Speed param
            if (SafeGetKey(KeyCode.Z))
            {
                if (speed <= 2) speed += 0.01f;
                else speed = 2;
            }
            else
            {
                if (speed >= 1) speed -= 0.01f;
                else speed = 1;
            }
            _Animator.SetFloat(SpeedParameter, speed);

            // Forward
            if (SafeGetKey(KeyCode.UpArrow))
            {
                if (_Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == MoveState)
                {
                    Vector3 velocity = transform.rotation * new Vector3(0, 0, speed);
                    MOVE_XZ(velocity);
                    MOVE_RESET();
                }
            }
            if (SafeGetKeyDown(KeyCode.UpArrow))
            {
                if (_Animator.GetCurrentAnimatorStateInfo(0).tagHash != JumpTag)
                {
                    _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                }
            }

            // Rotation
            if (SafeGetKey(KeyCode.RightArrow) && !SafeGetKey(KeyCode.LeftArrow))
            {
                transform.Rotate(Vector3.up, 1.0f);
            }
            else if (SafeGetKey(KeyCode.LeftArrow) && !SafeGetKey(KeyCode.RightArrow))
            {
                transform.Rotate(Vector3.up, -1.0f);
            }

            if (!SafeGetKey(KeyCode.UpArrow) && !SafeGetKey(KeyCode.DownArrow))
            {
                if (_Animator.GetCurrentAnimatorStateInfo(0).tagHash != JumpTag)
                {
                    if (SafeGetKeyDown(KeyCode.RightArrow) && !SafeGetKey(KeyCode.LeftArrow))
                    {
                        _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                    }
                    else if (SafeGetKeyDown(KeyCode.LeftArrow) && !SafeGetKey(KeyCode.RightArrow))
                    {
                        _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                    }
                }
                else if (SafeGetKey(KeyCode.RightArrow) && SafeGetKey(KeyCode.LeftArrow))
                {
                    if (_Animator.GetCurrentAnimatorStateInfo(0).tagHash != JumpTag)
                    {
                        _Animator.CrossFade(IdleState, 0.1f, 0, 0);
                    }
                }
            }

            KEY_UP();
        }

        private void KEY_UP()
        {
            if (!enableCharacterMotion) return;
            if (_Animator == null) return;

            if (_Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != JumpState
                && !_Animator.IsInTransition(0))
            {
                if (SafeGetKeyUp(KeyCode.UpArrow))
                {
                    if (!SafeGetKey(KeyCode.LeftArrow) && !SafeGetKey(KeyCode.RightArrow))
                    {
                        _Animator.CrossFade(IdleState, 0.1f, 0, 0);
                    }
                }
                else if (!SafeGetKey(KeyCode.UpArrow) && !SafeGetKey(KeyCode.DownArrow))
                {
                    if (SafeGetKeyUp(KeyCode.RightArrow) || SafeGetKeyUp(KeyCode.LeftArrow))
                    {
                        if (SafeGetKey(KeyCode.LeftArrow))
                            _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                        else if (SafeGetKey(KeyCode.RightArrow))
                            _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                        else
                            _Animator.CrossFade(IdleState, 0.1f, 0, 0);
                    }
                }
            }
        }

        private void MOVE_XZ(Vector3 velocity)
        {
            if (!enableCharacterMotion) return;
            if (_Ctrl == null || !_Ctrl.enabled || !gameObject.activeInHierarchy)
                return;

            _MoveDirection = new Vector3(velocity.x, _MoveDirection.y, velocity.z);
            _Ctrl.Move(_MoveDirection * Time.deltaTime);
        }

        private void MOVE_RESET()
        {
            _MoveDirection.x = 0;
            _MoveDirection.z = 0;
        }

        // ---------------- JUMP ----------------
        private void JUMP()
        {
            if (!enableCharacterMotion) return;
            if (_Animator == null) return;

            if (CheckGrounded())
            {
                if (SafeGetKeyDown(KeyCode.S)
                    && _Animator.GetCurrentAnimatorStateInfo(0).tagHash != JumpTag
                    && !_Animator.IsInTransition(0))
                {
                    _Animator.CrossFade(JumpState, 0.1f, 0, 0);
                    _MoveDirection.y = 8.0f;
                    _Animator.SetFloat(JumpPoseParameter, _MoveDirection.y);
                }
                if (_Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == JumpState
                    && !_Animator.IsInTransition(0)
                    && JumpPoseParameter < 0)
                {
                    _Animator.CrossFade(LandingState, 0.3f, 0, 0);
                }
                if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                    && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == LandingState
                    && !_Animator.IsInTransition(0))
                {
                    if (SafeGetKey(KeyCode.UpArrow) || SafeGetKey(KeyCode.DownArrow)
                        || SafeGetKey(KeyCode.LeftArrow) || SafeGetKey(KeyCode.RightArrow))
                    {
                        _Animator.CrossFade(MoveState, 0.1f, 0, 0);
                    }
                    else
                    {
                        _Animator.CrossFade(IdleState, 0.1f, 0, 0);
                    }
                }
            }
            else if (!CheckGrounded())
            {
                if (_Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == JumpState
                    && !_Animator.IsInTransition(0))
                {
                    _Animator.SetFloat(JumpPoseParameter, _MoveDirection.y);
                }
                if (_Animator.GetCurrentAnimatorStateInfo(0).fullPathHash != JumpState
                    && !_Animator.IsInTransition(0))
                {
                    _Animator.CrossFade(JumpState, 0.1f, 0, 0);
                }
            }
        }

        // ---------------- DAMAGE ----------------
        private void DAMAGE()
        {
            if (!enableInputActions) return;
            if (_Animator == null) return;

            if (SafeGetKeyDown(KeyCode.Q))
            {
                _Animator.CrossFade(DamageState, 0.1f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).tagHash == DamageTag
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(IdleState, 0.3f, 0, 0);
            }
        }

        // ---------------- FAINT ----------------
        private void FAINT()
        {
            if (!enableInputActions) return;
            if (_Animator == null) return;

            if (SafeGetKeyDown(KeyCode.W) && !_Status[Faint])
            {
                _Animator.CrossFade(DownState, 0.1f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == DownState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(FaintState, 0.3f, 0, 0);
            }

            if (SafeGetKeyDown(KeyCode.E)
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == FaintState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(StandUpFaintState, 0.1f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == StandUpFaintState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(IdleState, 0.1f, 0, 0);
            }
        }

        // ---------------- ATTACK ----------------
        private void ATTACK()
        {
            if (!enableInputActions) return;
            if (_Animator == null) return;

            if (SafeGetKeyDown(KeyCode.A))
            {
                _Animator.CrossFade(AttackChargeState, 0.3f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == AttackChargeState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(AttackState, 0.1f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == AttackState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(IdleState, 0.3f, 0, 0);
            }
        }

        // ---------------- THROW ----------------
        private void THROW()
        {
            if (!enableInputActions) return;
            if (_Animator == null) return;

            if (SafeGetKeyDown(KeyCode.X)
                && _Animator.GetCurrentAnimatorStateInfo(0).tagHash != ThrowTag)
            {
                if (_Mesh != null && _Mesh.Length > 0 && _Mesh[0] != null)
                    _Mesh[0].gameObject.SetActive(false);

                if (_Skull != null)
                {
                    _Skull.gameObject.SetActive(true);
                }
                else
                {
                    if (!_warnedMissingSkullOrHand)
                    {
                        Debug.LogWarning("SkeletonScript: Skull missing; cannot perform throw visuals.");
                        _warnedMissingSkullOrHand = true;
                    }
                }

                _Animator.CrossFade(ThrowChargeState, 0.0f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == ThrowChargeState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(ThrowState, 0.3f, 0, 0);
            }
            if (_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1
                && _Animator.GetCurrentAnimatorStateInfo(0).fullPathHash == ThrowState
                && !_Animator.IsInTransition(0))
            {
                _Animator.CrossFade(IdleState, 0.3f, 0, 0);
            }
        }

        // Called from animation event
        private void ThrowRelease()
        {
            if (_Skull == null || HandRPos == null)
            {
                Debug.LogWarning("SkeletonScript.ThrowRelease: Skull or HandRPos missing; cannot release skull.");
                return;
            }

            _Skull.transform.SetParent(null);
            Vector3 pos1 = HandRPos.transform.position + new Vector3(-0.00017f, 0.00052f, -0.00247f);
            Vector3 pos2 = transform.rotation * new Vector3(0, 0.5f, 3);
            _Skull.transform.rotation = transform.rotation;
            StartCoroutine(ThrowHead(pos1, pos2, 1));
        }

        private IEnumerator ThrowHead(Vector3 start, Vector3 end, float arrow_speed)
        {
            Vector3 middle = Vector3.Lerp(start, end, 0.5f);
            middle.y += 3;
            float t = 0.0f;

            while (true)
            {
                if (t > 1)
                {
                    var script1 = GetComponent<SkeletonScript>();
                    if (script1 != null) script1.enabled = false;

                    if (_Skull != null)
                    {
                        var skullScript = _Skull.GetComponent<SkeletonSkullScript>();
                        if (skullScript != null) skullScript.enabled = true;
                    }

                    if (_Animator != null) _Animator.CrossFade(IdleState, 0.3f, 0, 0);
                    yield break;
                }

                t += arrow_speed * Time.deltaTime;
                Vector3 a = Vector3.Lerp(start, middle, t);
                Vector3 b = Vector3.Lerp(middle, end, t);

                if (_Skull != null)
                    _Skull.transform.position = Vector3.Lerp(a, b, t);

                yield return null;
            }
        }

        // ---------------- Animator controller cycling ----------------
        private void CHANGE_ANIMATOR()
        {
            if (!enableInputActions) return;

            if (SafeGetKeyDown(KeyCode.Space))
            {
                _Animator_num++;
                if (_Animator_num >= 6)
                    _Animator_num = 0;

                _Animator.runtimeAnimatorController = _CacheAnimator[_Animator_num];

                if (_Animator_num == 0)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.82f, 0);
                        _Ctrl.height = 1.5f;
                    }
                    for (int i = 0; i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(true);
                    }
                }
                else if (_Animator_num == 1)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.97f, 0);
                        _Ctrl.height = 1.8f;
                    }
                }
                else if (_Animator_num == 2)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.42f, 0);
                        _Ctrl.height = 0.7f;
                    }
                    for (int i = 8; i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(false);
                    }
                }
                else if (_Animator_num == 3)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.6f, 0);
                        _Ctrl.height = 1.05f;
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(false);
                    }
                    for (int i = 8; i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(true);
                    }
                }
                else if (_Animator_num == 4)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.97f, 0);
                        _Ctrl.height = 1.8f;
                    }
                    for (int i = 0; i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(true);
                    }
                    for (int i = 9; i < 12 && i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(false);
                    }
                }
                else if (_Animator_num == 5)
                {
                    if (_Ctrl != null)
                    {
                        _Ctrl.center = new Vector3(0, 0.3f, 0.5f);
                        _Ctrl.height = 0.25f;
                    }
                    for (int i = 8; i < _Mesh.Length; i++)
                    {
                        if (_Mesh[i] != null) _Mesh[i].gameObject.SetActive(false);
                    }
                }
            }
        }

        // ---------- Safe Input wrappers ----------
        private bool SafeGetKey(KeyCode key)
        {
            try { return Input.GetKey(key); }
            catch (InvalidOperationException) { return false; }
            catch { return false; }
        }

        private bool SafeGetKeyDown(KeyCode key)
        {
            try { return Input.GetKeyDown(key); }
            catch (InvalidOperationException) { return false; }
            catch { return false; }
        }

        private bool SafeGetKeyUp(KeyCode key)
        {
            try { return Input.GetKeyUp(key); }
            catch (InvalidOperationException) { return false; }
            catch { return false; }
        }
    }
}
