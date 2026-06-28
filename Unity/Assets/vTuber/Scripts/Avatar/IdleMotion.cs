using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Adds life when the streamer is still or tracking is lost: gentle breathing
    /// (spine), slow idle sway (hips), and automatic blinking that takes over only
    /// while face tracking is unavailable. Breathing/sway run on different bones
    /// than the head/eye drivers, so nothing fights.
    /// </summary>
    // Runs before the head/eye drivers so its root-bone (hips/spine) motion is
    // applied first and the upper-body absolute rotations win.
    [DefaultExecutionOrder(90)]
    public class IdleMotion : MonoBehaviour
    {
        [Header("Wiring")]
        public AvatarFaceDriver faceDriver;
        public Animator animator;

        [Header("Breathing")]
        public bool enableBreathing = true;
        [Tooltip("Breaths per minute.")]
        public float breathsPerMinute = 14f;
        [Tooltip("Spine rotation amplitude in degrees.")]
        public float breathAmplitude = 1.2f;

        [Header("Idle sway")]
        public bool enableSway = true;
        // Peak sway angle, in degrees.
        public float swayAmplitude = 0.8f;
        // Time for one full sway cycle, in seconds.
        public float swayPeriod = 9f;

        [Header("Auto-blink (only when not tracking)")]
        public bool enableAutoBlink = true;
        public float minBlinkInterval = 2.5f;
        public float maxBlinkInterval = 6.0f;
        public float blinkDuration = 0.12f;

        [Header("Body-angle follow (2D-style)")]
        [Tooltip("Lean the torso (hips/spine) the same way your head turns - lagged and " +
                 "reduced like a 2D vTuber's body angle. Derived from head tracking; no extra " +
                 "input. Composes with breathing/sway; turn off to get the old behaviour exactly.")]
        public bool enableBodyFollow = true;
        [Tooltip("Source of the head angles. Auto-found if left empty.")]
        public HeadPoseDriver headDriver;
        [Tooltip("Fraction of the head angle the body leans by (2D body angle is ~1/3).")]
        [Range(0f, 1f)] public float bodyFollowStrength = 0.35f;
        [Tooltip("Clamp each body axis to +/- this many degrees.")]
        public float bodyMaxAngle = 12f;
        [Tooltip("Higher = the body trails further behind the head.")]
        [Range(0f, 1f)] public float bodySmoothing = 0.6f;
        [Range(0f, 1f)] public float bodyHipShare = 0.5f;
        [Range(0f, 1f)] public float bodySpineShare = 0.5f;

        [Header("Rest pose")]
        [Tooltip("Off by default. Enable if the avatar's bind pose leaves arms raised " +
                 "(T-pose/A-pose) and tune the angle to bring them down naturally; both " +
                 "this and the angle apply live, no relaunch needed.")]
        public bool applyRestArms = false;
        [Tooltip("Degrees to lower each upper arm from its bind pose (90 = straight down " +
                 "from a horizontal T-pose; an A-pose bind needs less).")]
        public float restArmsAngle = 72f;

        private Transform _root, _spine, _hips, _lUpperArm, _rUpperArm;
        private Quaternion _spineBase, _hipsBase, _lUpperArmBase, _rUpperArmBase;
        private float _phase;
        private float _nextBlink, _blinkTimer = -1f;
        // Smoothed body-follow angles (deg).
        private float _bPitch, _bYaw, _bRoll;

        private void Start()
        {
            animator = AvatarUtil.FindHumanoid(animator, null);
            if (headDriver == null) headDriver = FindObjectOfType<HeadPoseDriver>();
            if (animator != null && animator.isHuman)
            {
                _root = animator.transform;
                _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                _lUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _rUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
            if (_spine != null) _spineBase = _spine.rotation;
            if (_hips != null) _hipsBase = _hips.rotation;
            if (_lUpperArm != null) _lUpperArmBase = _lUpperArm.rotation;
            if (_rUpperArm != null) _rUpperArmBase = _rUpperArm.rotation;

            RefreshRestArms();
            ScheduleNextBlink();
        }

        /// <summary>
        /// Rotates the upper arms down around the avatar's forward axis from their bind
        /// pose toward a natural arms-at-side rest. Always computed from the bind
        /// rotation captured at Start, so it's safe to call repeatedly - toggling
        /// applyRestArms or dragging restArmsAngle live never compounds.
        /// </summary>
        public void RefreshRestArms()
        {
            if (animator == null || !animator.isHuman) return;
            Quaternion rootRot = _root != null ? _root.rotation : Quaternion.identity;
            float angle = applyRestArms ? restArmsAngle : 0f;
            // Roll about the root's forward (Z) axis swings the arm down.
            if (_lUpperArm != null)
                _lUpperArm.rotation = rootRot * Quaternion.Euler(0f, 0f, angle) * Quaternion.Inverse(rootRot) * _lUpperArmBase;
            if (_rUpperArm != null)
                _rUpperArm.rotation = rootRot * Quaternion.Euler(0f, 0f, -angle) * Quaternion.Inverse(rootRot) * _rUpperArmBase;
        }

        private void ScheduleNextBlink() => _nextBlink = Random.Range(minBlinkInterval, maxBlinkInterval);

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Quaternion rootRot = _root != null ? _root.rotation : Quaternion.identity;

            // --- Body-angle follow: a reduced, lagged copy of the head angles ---
            // 2D vTubers derive "body angle" from the head (~1/3 magnitude, trailing). We read
            // HeadPoseDriver's already smoothed/recentred/mirrored angles a frame late (this
            // driver runs before it), scale them down, and smooth again so the body trails.
            // When not tracking those angles ease to 0, so the body returns to neutral.
            bool bodyOn = enableBodyFollow && headDriver != null;
            float tbp = 0f, tby = 0f, tbr = 0f;
            if (bodyOn)
            {
                tbp = Mathf.Clamp(headDriver.SmoothedPitch * bodyFollowStrength, -bodyMaxAngle, bodyMaxAngle);
                tby = Mathf.Clamp(headDriver.SmoothedYaw * bodyFollowStrength, -bodyMaxAngle, bodyMaxAngle);
                tbr = Mathf.Clamp(headDriver.SmoothedRoll * bodyFollowStrength, -bodyMaxAngle, bodyMaxAngle);
            }
            // Frame-rate-independent smoothing: 1-exp(-dt*rate) is this frame's lerp factor
            // toward the target (higher rate = snappier, same feel at any fps).
            float bk = 1f - Mathf.Exp(-dt * Mathf.Lerp(20f, 3f, bodySmoothing));
            _bPitch = Mathf.Lerp(_bPitch, tbp, bk);
            _bYaw = Mathf.Lerp(_bYaw, tby, bk);
            _bRoll = Mathf.Lerp(_bRoll, tbr, bk);

            // --- Spine: breathing (X) + body-follow share, as one absolute write ---
            if (_spine != null && (enableBreathing || bodyOn))
            {
                float b = 0f;
                if (enableBreathing)
                {
                    _phase += dt * (breathsPerMinute / 60f) * Mathf.PI * 2f;
                    b = Mathf.Sin(_phase) * breathAmplitude;
                }
                Quaternion breath = rootRot * Quaternion.Euler(b, 0f, 0f) * Quaternion.Inverse(rootRot);
                Quaternion body = bodyOn
                    ? rootRot * Quaternion.Euler(_bPitch * bodySpineShare, _bYaw * bodySpineShare, _bRoll * bodySpineShare) * Quaternion.Inverse(rootRot)
                    : Quaternion.identity;
                _spine.rotation = body * breath * _spineBase;
            }

            // --- Hips: idle sway + body-follow share, as one absolute write ---
            if (_hips != null && (enableSway || bodyOn))
            {
                float yaw = 0f, roll = 0f;
                if (enableSway && swayPeriod > 0.01f)
                {
                    float t = Time.time / swayPeriod * Mathf.PI * 2f;
                    yaw = Mathf.Sin(t) * swayAmplitude;
                    roll = Mathf.Sin(t * 0.5f) * swayAmplitude * 0.5f;
                }
                Quaternion sway = rootRot * Quaternion.Euler(0f, yaw, roll) * Quaternion.Inverse(rootRot);
                Quaternion body = bodyOn
                    ? rootRot * Quaternion.Euler(_bPitch * bodyHipShare, _bYaw * bodyHipShare, _bRoll * bodyHipShare) * Quaternion.Inverse(rootRot)
                    : Quaternion.identity;
                _hips.rotation = body * sway * _hipsBase;
            }

            // --- Auto-blink (only when face tracking isn't driving the eyes) ---
            if (!enableAutoBlink || faceDriver == null) return;
            bool tracking = faceDriver.TrackingActive;
            if (tracking)
            {
                faceDriver.externalBlink = 0f;
                _blinkTimer = -1f;
                return;
            }

            if (_blinkTimer < 0f)
            {
                _nextBlink -= dt;
                faceDriver.externalBlink = 0f;
                // Countdown elapsed: begin a blink by starting the blink timer.
                if (_nextBlink <= 0f) _blinkTimer = 0f;
            }
            else
            {
                _blinkTimer += dt;
                float h = Mathf.Clamp01(_blinkTimer / blinkDuration);
                // Sine over the blink window: 0 -> open, peak (0.5) -> fully closed, 1 -> open again.
                faceDriver.externalBlink = Mathf.Sin(h * Mathf.PI);
                if (_blinkTimer >= blinkDuration)
                {
                    _blinkTimer = -1f;
                    ScheduleNextBlink();
                }
            }
        }
    }
}
