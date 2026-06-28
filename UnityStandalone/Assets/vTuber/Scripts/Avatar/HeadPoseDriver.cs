using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Rotates the head (and, by a configurable share, the neck and chest) from
    /// the tracker's head Euler angles. Rotations are applied in the avatar root's
    /// frame and written as absolute world rotations on top of each bone's rest
    /// pose, so the result is independent of per-rig local bone axes.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class HeadPoseDriver : MonoBehaviour
    {
        [Header("Wiring")]
        public UdpTrackingReceiver receiver;
        public Animator animator;

        [Header("Enable")]
        public bool enableHead = true;

        [Header("Axis tuning")]
        [Range(0f, 2f)] public float pitchScale = 1f;
        [Range(0f, 2f)] public float yawScale = 1f;
        [Range(0f, 2f)] public float rollScale = 1f;
        public bool invertPitch = false;
        public bool invertYaw = false;
        public bool invertRoll = false;
        [Tooltip("Mirror yaw/roll to match the face driver's mirror setting.")]
        public bool mirror = true;
        [Tooltip("Clamp each axis to +/- this many degrees.")]
        public float maxAngle = 35f;

        [Header("Distribution")]
        [Range(0f, 1f)] public float neckShare = 0.35f;
        [Range(0f, 1f)] public float chestLean = 0.15f;

        [Header("Smoothing")]
        [Range(0f, 1f)] public float smoothing = 0.5f;

        [Header("Blink handling")]
        [Tooltip("Briefly hold the head when a blink is detected (webcam head-pose pitches back as the eyelids close).")]
        public bool freezeOnBlink = true;
        [Tooltip("Blink detected when eye-closed rises faster than this (per second). A real blink " +
                 "snaps shut fast; looking down narrows the eyes slowly, so this won't freeze normal movement.")]
        public float blinkOnsetRate = 6f;

        /// <summary>True if a humanoid Head bone was found and head tracking can run.</summary>
        public bool HasHead => _head != null;

        /// <summary>Smoothed head angles currently applied (degrees, after scale/mirror/clamp).
        /// Other drivers read these to lag the torso behind the head - e.g. IdleMotion's
        /// body-angle follow. Zero when not tracking (the head eases back to neutral).</summary>
        public float SmoothedPitch => _pitch;
        public float SmoothedYaw => _yaw;
        public float SmoothedRoll => _roll;

        private Transform _root, _head, _neck, _chest;
        private Quaternion _headBase, _neckBase, _chestBase;
        // Smoothed, post-scale.
        private float _pitch, _yaw, _roll;
        // Held target (frozen during blinks).
        private float _tPitch, _tYaw, _tRoll;
        // Neutral offset (recenter).
        private float _nPitch, _nYaw, _nRoll;
        // Seconds remaining to keep frozen.
        private float _blinkHold;
        // Lockout so one blink triggers once.
        private float _blinkRefractory;
        // Last frame's eye-closed value.
        private float _prevBlink;
        // Last frame's raw incoming yaw (deg).
        private float _prevRawYaw;
        private bool _calibrated;

        // How long to hold the head after a blink is detected - long enough to cover
        // a full blink (close + reopen), short enough to never feel sticky.
        private const float BlinkHoldSeconds = 0.2f;
        // Above this incoming yaw speed (deg/s) the head is turning, so we never
        // treat the eye wobble as a blink. Blinks keep yaw nearly still (~<10 deg/s).
        private const float HeadStillYawMax = 25f;
        private TrackingFrame _frame;

        private void Start()
        {
            animator = AvatarUtil.FindHumanoid(animator, null);
            if (animator != null && animator.isHuman)
            {
                _root = animator.transform;
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                _chest = animator.GetBoneTransform(HumanBodyBones.Chest)
                         ?? animator.GetBoneTransform(HumanBodyBones.UpperChest);
            }
            if (_head != null) _headBase = _head.rotation;
            if (_neck != null) _neckBase = _neck.rotation;
            if (_chest != null) _chestBase = _chest.rotation;

            if (_head == null)
                Debug.LogWarning("[vTuber] HeadPoseDriver: no humanoid Head bone found; head tracking disabled. " +
                                 "Is the avatar's Animator set to Humanoid with an Avatar assigned?");
            else
                Debug.Log($"[vTuber] HeadPoseDriver: driving '{_head.name}'" +
                          (_neck != null ? $" + neck '{_neck.name}'" : "") + ".");
        }

        /// <summary>Capture the current head pose as the new neutral (look-straight) reference.</summary>
        public void Recenter()
        {
            _nPitch = _frame.HeadPitch;
            _nYaw = _frame.HeadYaw;
            _nRoll = _frame.HeadRoll;
            _calibrated = true;
            Debug.Log("[vTuber] Head pose recentred.");
        }

        private void LateUpdate()
        {
            if (!enableHead || _head == null) return;
            if (receiver != null) _frame = receiver.Current;
            bool active = receiver != null && receiver.TrackerConnected && _frame.FaceValid;

            if (active && !_calibrated) Recenter();

            // Blink detection: the eyes close FAST (high rate) while the head stays
            // still (low yaw speed). The yaw gate is what stops a fast head move - which
            // also jolts the webcam's eye estimate - from being mistaken for a blink and
            // freezing the head. We trigger once per blink (refractory) so it can't stack.
            float dtSafe = Mathf.Max(Time.deltaTime, 1e-4f);
            float blink = 0f;
            if (_frame.Shapes != null && _frame.Shapes.Length == TrackingProtocol.NumShapes)
                blink = Mathf.Max(_frame.Shape(ArkShape.EyeBlinkLeft), _frame.Shape(ArkShape.EyeBlinkRight));
            float blinkRate = (blink - _prevBlink) / dtSafe;
            float yawSpeed = Mathf.Abs(_frame.HeadYaw - _prevRawYaw) / dtSafe;
            _prevBlink = blink;
            _prevRawYaw = _frame.HeadYaw;

            if (_blinkRefractory > 0f) _blinkRefractory -= Time.deltaTime;
            if (freezeOnBlink && active && blinkRate > blinkOnsetRate
                && yawSpeed < HeadStillYawMax && _blinkRefractory <= 0f)
            {
                _blinkHold = BlinkHoldSeconds;
                _blinkRefractory = 0.35f;
            }
            if (_blinkHold > 0f) _blinkHold -= Time.deltaTime;
            bool frozen = _blinkHold > 0f;

            if (active && !frozen)
            {
                _tPitch = Mathf.Clamp((_frame.HeadPitch - _nPitch) * pitchScale * (invertPitch ? -1f : 1f), -maxAngle, maxAngle);
                _tYaw = Mathf.Clamp((_frame.HeadYaw - _nYaw) * yawScale * (invertYaw ? -1f : 1f), -maxAngle, maxAngle);
                _tRoll = Mathf.Clamp((_frame.HeadRoll - _nRoll) * rollScale * (invertRoll ? -1f : 1f), -maxAngle, maxAngle);
                if (mirror) { _tYaw = -_tYaw; _tRoll = -_tRoll; }
            }
            else if (!active)
            {
                // Tracking lost: ease gently back to neutral rather than freezing.
                float e = 1f - Mathf.Exp(-Time.deltaTime * 3f);
                _tPitch = Mathf.Lerp(_tPitch, 0f, e);
                _tYaw = Mathf.Lerp(_tYaw, 0f, e);
                _tRoll = Mathf.Lerp(_tRoll, 0f, e);
            }
            // else (active && frozen): hold the pose for the brief blink window.

            // Frame-rate-independent smoothing: 1-exp(-dt*rate) is this frame's lerp factor
            // toward the target (higher rate = snappier, same feel at any fps).
            float k = 1f - Mathf.Exp(-Time.deltaTime * Mathf.Lerp(30f, 4f, smoothing));
            _pitch = Mathf.Lerp(_pitch, _tPitch, k);
            _yaw = Mathf.Lerp(_yaw, _tYaw, k);
            _roll = Mathf.Lerp(_roll, _tRoll, k);

            Quaternion rootRot = _root != null ? _root.rotation : Quaternion.identity;
            Quaternion full = RootSpace(rootRot, _pitch, _yaw, _roll);

            // Apply parents before children: a parent's rotation moves the child's
            // world transform, so the head must be written LAST to win.
            if (_chest != null && chestLean > 0f)
            {
                Quaternion lean = RootSpace(rootRot, _pitch * 0.5f, _yaw, _roll * 0.5f);
                _chest.rotation = Quaternion.Slerp(_chestBase, lean * _chestBase, chestLean);
            }
            if (_neck != null && neckShare > 0f)
                _neck.rotation = Quaternion.Slerp(_neckBase, full * _neckBase, neckShare);
            _head.rotation = full * _headBase;
        }

        // A rotation about the avatar root's axes, expressed in world space.
        private static Quaternion RootSpace(Quaternion rootRot, float pitch, float yaw, float roll)
        {
            return rootRot * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(rootRot);
        }
    }
}
