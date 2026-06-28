using System.Collections.Generic;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Drives eye gaze from the ARKit eyeLook* shapes. Prefers rotating the
    /// humanoid eye bones; if the rig has none, falls back to the avatar's
    /// EyeLook* blendshapes. Gaze shapes are owned here so the face driver
    /// doesn't also touch them.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public class EyeGazeDriver : MonoBehaviour
    {
        [Header("Wiring")]
        public UdpTrackingReceiver receiver;
        public Animator animator;
        [Tooltip("Avatar root for blendshape fallback discovery. Defaults to animator root.")]
        public GameObject avatarRoot;

        [Header("Settings")]
        public bool enableGaze = true;
        public bool mirror = true;
        [Tooltip("Max horizontal eye rotation (degrees) at full gaze. Keep modest - " +
                 "anime eyes show sclera ('white') if rotated too far.")]
        public float maxYaw = 12f;
        [Tooltip("Max vertical eye rotation (degrees) at full gaze.")]
        public float maxPitch = 9f;
        [Tooltip("Ignore gaze below this magnitude (0..1) to kill webcam jitter.")]
        [Range(0f, 0.5f)] public float deadzone = 0.12f;
        [Range(0f, 1f)] public float smoothing = 0.4f;

        private Transform _root, _leftEye, _rightEye;
        private Quaternion _leftBase, _rightBase;
        private bool _useBones;

        // Blendshape fallback refs.
        private List<ShapeRef> _bsLookUpL, _bsLookUpR, _bsLookDownL, _bsLookDownR;
        private List<ShapeRef> _bsLookInL, _bsLookInR, _bsLookOutL, _bsLookOutR;
        private bool _useShapes;

        // Smoothed gaze, -1..1.
        private float _gx, _gy;
        private TrackingFrame _frame;

        private void Start()
        {
            animator = AvatarUtil.FindHumanoid(animator, avatarRoot);
            if (avatarRoot == null && animator != null) avatarRoot = animator.gameObject;

            if (animator != null && animator.isHuman)
            {
                _root = animator.transform;
                _leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                _rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

            if (_leftEye != null || _rightEye != null)
            {
                _useBones = true;
                if (_leftEye != null) _leftBase = _leftEye.rotation;
                if (_rightEye != null) _rightBase = _rightEye.rotation;
            }
            else if (avatarRoot != null)
            {
                var lib = new BlendshapeLibrary(avatarRoot);
                _bsLookUpL = lib.Resolve(new[] { "EyeLookUpLeft" });
                _bsLookUpR = lib.Resolve(new[] { "EyeLookUpRight" });
                _bsLookDownL = lib.Resolve(new[] { "EyeLookDownLeft" });
                _bsLookDownR = lib.Resolve(new[] { "EyeLookDownRight" });
                _bsLookInL = lib.Resolve(new[] { "EyeLookInLeft" });
                _bsLookInR = lib.Resolve(new[] { "EyeLookInRight" });
                _bsLookOutL = lib.Resolve(new[] { "EyeLookOutLeft" });
                _bsLookOutR = lib.Resolve(new[] { "EyeLookOutRight" });
                _useShapes = _bsLookUpL != null || _bsLookInL != null;
            }

            if (!_useBones && !_useShapes)
                Debug.LogWarning("[vTuber] EyeGazeDriver: no eye bones or EyeLook* shapes found; gaze disabled.");
        }

        private void LateUpdate()
        {
            if (!enableGaze) return;
            if (receiver != null) _frame = receiver.Current;
            bool active = receiver != null && receiver.TrackerConnected && _frame.FaceValid;

            float tx = 0f, ty = 0f;
            if (active)
            {
                // Positive X = looking to the subject's right.
                float rightward = _frame.Shape(ArkShape.EyeLookInLeft) + _frame.Shape(ArkShape.EyeLookOutRight);
                float leftward = _frame.Shape(ArkShape.EyeLookOutLeft) + _frame.Shape(ArkShape.EyeLookInRight);
                tx = (rightward - leftward) * 0.5f;
                float up = _frame.Shape(ArkShape.EyeLookUpLeft) + _frame.Shape(ArkShape.EyeLookUpRight);
                float down = _frame.Shape(ArkShape.EyeLookDownLeft) + _frame.Shape(ArkShape.EyeLookDownRight);
                ty = (up - down) * 0.5f;
                if (mirror) tx = -tx;
            }

            // Deadzone + magnitude clamp: removes webcam jitter and prevents the eyes
            // from over-rotating past the socket (which shows white).
            Vector2 g = new Vector2(tx, ty);
            float mag = g.magnitude;
            if (mag <= deadzone) g = Vector2.zero;
            else g = (g / mag) * Mathf.Clamp01(Mathf.InverseLerp(deadzone, 1f, mag));
            g = Vector2.ClampMagnitude(g, 1f);

            // Frame-rate-independent smoothing: 1-exp(-dt*rate) is this frame's lerp factor
            // toward the target (higher rate = snappier, same feel at any fps).
            float k = 1f - Mathf.Exp(-Time.deltaTime * Mathf.Lerp(35f, 6f, smoothing));
            _gx = Mathf.Lerp(_gx, g.x, k);
            _gy = Mathf.Lerp(_gy, g.y, k);

            if (_useBones)
            {
                Quaternion rootRot = _root != null ? _root.rotation : Quaternion.identity;
                // Pitch up should be negative X rotation in Unity convention.
                Quaternion d = rootRot * Quaternion.Euler(-_gy * maxPitch, _gx * maxYaw, 0f)
                                       * Quaternion.Inverse(rootRot);
                if (_leftEye != null) _leftEye.rotation = d * _leftBase;
                if (_rightEye != null) _rightEye.rotation = d * _rightBase;
            }
            else if (_useShapes)
            {
                float right = Mathf.Max(0f, _gx), left = Mathf.Max(0f, -_gx);
                float up = Mathf.Max(0f, _gy), down = Mathf.Max(0f, -_gy);
                // Left eye: "in" = toward nose = subject's right; "out" = subject's left.
                SetShapes(_bsLookInL, right); SetShapes(_bsLookOutL, left);
                SetShapes(_bsLookInR, left); SetShapes(_bsLookOutR, right);
                SetShapes(_bsLookUpL, up); SetShapes(_bsLookUpR, up);
                SetShapes(_bsLookDownL, down); SetShapes(_bsLookDownR, down);
            }
        }

        private static void SetShapes(List<ShapeRef> refs, float value01)
        {
            if (refs == null) return;
            float w = Mathf.Clamp01(value01) * 100f;
            foreach (var r in refs) r.Renderer.SetBlendShapeWeight(r.Index, w);
        }
    }
}
