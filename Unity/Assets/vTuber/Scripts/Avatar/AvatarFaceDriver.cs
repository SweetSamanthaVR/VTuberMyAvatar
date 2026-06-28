using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VTuberMyAvatar
{
    public enum JawBlendMode { WebcamOnly, Max, MicOnly }

    /// <summary>
    /// Core facial animation: reads the latest tracking frame, maps ARKit shapes
    /// onto the avatar's Unified-Expressions blendshapes, smooths them, and writes
    /// the weights every LateUpdate. Mic lip-sync and idle auto-blink feed in via
    /// the External* hooks below rather than fighting over the same shapes.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class AvatarFaceDriver : MonoBehaviour
    {
        [Header("Wiring")]
        public UdpTrackingReceiver receiver;
        [Tooltip("Root of the instantiated avatar. Defaults to this GameObject.")]
        public GameObject avatarRoot;

        [Header("Tuning")]
        [Range(0f, 1f)] public float smoothing = 0.4f;
        [Tooltip("Mirror left/right so the avatar reflects you like a mirror.")]
        public bool mirror = true;
        [Range(0f, 2f)] public float expressionMultiplier = 1.0f;

        [Header("Eye close (blink) range")]
        [Tooltip("Webcam eye-closed value treated as fully open. Below this = eyes open.")]
        [Range(0f, 0.5f)] public float blinkInputLow = 0.1f;
        [Tooltip("Webcam eye-closed value treated as fully shut. Lower this if lids don't fully close.")]
        [Range(0.4f, 1f)] public float blinkInputHigh = 0.8f;

        [Tooltip("Log the resolved ARKit->mesh mapping and any unmapped shapes once at startup.")]
        public bool logMappingOnStart = true;

        // --- Hooks driven by other components ---
        // Value 0..1, supplied by MicLipSync.
        [HideInInspector] public float externalJawOpen;
        [HideInInspector] public JawBlendMode jawMode = JawBlendMode.Max;
        // Value 0..1 from IdleMotion (used only when not tracking).
        [HideInInspector] public float externalBlink;

        /// <summary>True when fresh, face-valid tracking is arriving.</summary>
        public bool TrackingActive { get; private set; }

        private struct Contribution { public int ArkIndex; public float Mul; }

        private class BlendTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int ShapeIndex;
            public List<Contribution> Contribs = new List<Contribution>(2);
            // Smoothed weight 0..100.
            public float Current;
        }

        private BlendshapeLibrary _lib;
        private readonly List<BlendTarget> _targets = new List<BlendTarget>();
        private TrackingFrame _frame;
        // Mirror-resolved + mic-blended jaw, exposed for other drivers.
        private float _jawWorking;

        private void Start()
        {
            if (avatarRoot == null) avatarRoot = gameObject;
            BuildTargets();
        }

        private void BuildTargets()
        {
            _lib = new BlendshapeLibrary(avatarRoot);
            _targets.Clear();

            var entries = ArkitAvatarMap.Build();
            // Group contributions per concrete (renderer, shapeIndex).
            var byShape = new Dictionary<(SkinnedMeshRenderer, int), BlendTarget>();
            var mappedArk = new HashSet<int>();
            var unresolved = new List<string>();

            foreach (var e in entries)
            {
                var refs = _lib.Resolve(e.Candidates);
                if (refs == null)
                {
                    unresolved.Add($"{TrackingProtocol.ShapeNames[e.ArkIndex]} -> [{string.Join(", ", e.Candidates)}]");
                    continue;
                }
                mappedArk.Add(e.ArkIndex);
                foreach (var r in refs)
                {
                    var key = (r.Renderer, r.Index);
                    if (!byShape.TryGetValue(key, out var t))
                    {
                        t = new BlendTarget { Renderer = r.Renderer, ShapeIndex = r.Index };
                        byShape[key] = t;
                        _targets.Add(t);
                    }
                    t.Contribs.Add(new Contribution { ArkIndex = e.ArkIndex, Mul = e.Mul });
                }
            }

            if (logMappingOnStart)
            {
                var sb = new StringBuilder();
                sb.Append(_lib.Describe());
                sb.Append($"[vTuber] mapped {mappedArk.Count}/{TrackingProtocol.NumShapes} ARKit shapes onto {_targets.Count} avatar blendshapes.\n");
                if (unresolved.Count > 0)
                    sb.Append($"[vTuber] unmapped (no matching shape, will be ignored):\n   {string.Join("\n   ", unresolved)}");
                Debug.Log(sb.ToString());
            }
        }

        private void LateUpdate()
        {
            if (receiver != null) _frame = receiver.Current;
            TrackingActive = receiver != null && receiver.TrackerConnected && _frame.FaceValid;

            // Pre-resolve the jaw source (mirror + mic blend) so it's consistent
            // and available to other drivers.
            _jawWorking = ComputeJaw();

            float dt = Time.deltaTime;
            // Higher smoothing -> lower responsiveness (Hz). Critically damped feel.
            float responsiveness = Mathf.Lerp(45f, 5f, Mathf.Clamp01(smoothing));
            // k = 1-exp(-dt*responsiveness): the per-frame lerp factor, independent of fps.
            float k = 1f - Mathf.Exp(-dt * responsiveness);

            for (int i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                float raw = 0f;
                for (int c = 0; c < t.Contribs.Count; c++)
                {
                    var con = t.Contribs[c];
                    raw += SourceValue(con.ArkIndex) * con.Mul;
                }
                raw = Mathf.Clamp01(raw * expressionMultiplier) * 100f;
                t.Current = Mathf.Lerp(t.Current, raw, k);
                t.Renderer.SetBlendShapeWeight(t.ShapeIndex, t.Current);
            }
        }

        /// <summary>Value (0..1) for an ARKit index, applying mirror and the hooks.</summary>
        private float SourceValue(int arkIndex)
        {
            if (arkIndex == (int)ArkShape.JawOpen) return _jawWorking;

            if (arkIndex == (int)ArkShape.EyeBlinkLeft || arkIndex == (int)ArkShape.EyeBlinkRight)
            {
                // Idle auto-blink.
                if (!TrackingActive) return externalBlink;
                int s = mirror ? ArkitMirror.Counterpart(arkIndex) : arkIndex;
                // Webcam blink rarely reads a full 1.0 at full closure, so remap
                // [low..high] -> [0..1] so closed eyes fully shut the lids.
                return Mathf.Clamp01((_frame.Shapes[s] - blinkInputLow)
                                     / Mathf.Max(0.01f, blinkInputHigh - blinkInputLow));
            }

            if (!TrackingActive) return 0f;
            int src = mirror ? ArkitMirror.Counterpart(arkIndex) : arkIndex;
            return _frame.Shapes[src];
        }

        private float ComputeJaw()
        {
            float webcam = TrackingActive ? _frame.Shape(ArkShape.JawOpen) : 0f;
            switch (jawMode)
            {
                case JawBlendMode.MicOnly: return Mathf.Clamp01(externalJawOpen);
                case JawBlendMode.WebcamOnly: return webcam;
                default: return Mathf.Max(webcam, Mathf.Clamp01(externalJawOpen));
            }
        }

        /// <summary>Latest mirror+mic-resolved jaw-open value, for other drivers.</summary>
        public float CurrentJawOpen => _jawWorking;
    }
}
