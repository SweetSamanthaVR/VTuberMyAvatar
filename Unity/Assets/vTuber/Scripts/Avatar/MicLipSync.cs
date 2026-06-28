using UnityEngine;

namespace VTuberMyAvatar
{
    public enum MicMode { Off, FillWhenNoWebcam, Blend }

    /// <summary>
    /// Microphone-driven jaw movement. Measures input loudness and feeds a jaw-open
    /// value into the <see cref="AvatarFaceDriver"/>, which blends it with webcam
    /// mouth tracking. Audio analysis only (no phonemes), which is simple and cheap.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public class MicLipSync : MonoBehaviour
    {
        [Header("Wiring")]
        public AvatarFaceDriver faceDriver;

        [Header("Settings")]
        public MicMode mode = MicMode.Blend;
        [Tooltip("Empty = system default microphone.")]
        public string deviceName = "";
        [Tooltip("Loudness below this (RMS) is treated as silence.")]
        public float noiseFloor = 0.012f;
        [Tooltip("RMS that maps to a fully open jaw.")]
        public float loudAt = 0.16f;
        // How fast the mouth opens.
        [Range(0f, 1f)] public float attack = 0.6f;
        // How fast it closes.
        [Range(0f, 1f)] public float release = 0.25f;

        private const int SampleRate = 16000;
        private const int ClipSeconds = 1;
        private AudioClip _clip;
        private string _device;
        private readonly float[] _window = new float[512];
        private float _level;
        // The mode the capture device is currently set up for.
        private MicMode _activeMode;

        private void OnEnable()
        {
            _activeMode = mode;
            if (mode != MicMode.Off) StartMic();
        }

        private void OnDisable()
        {
            StopMic();
        }

        private void StartMic()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[vTuber] MicLipSync: no microphone devices found.");
                return;
            }
            _device = string.IsNullOrEmpty(deviceName) ? Microphone.devices[0] : deviceName;
            _clip = Microphone.Start(_device, true, ClipSeconds, SampleRate);
            Debug.Log($"[vTuber] MicLipSync using device: {_device}");
        }

        private void StopMic()
        {
            if (_clip != null)
            {
                if (Microphone.IsRecording(_device)) Microphone.End(_device);
                _clip = null;
            }
        }

        private void Update()
        {
            if (faceDriver == null) return;

            // Reconcile the capture device with the current mode so live mode changes
            // (L hotkey / settings panel) take effect immediately: start the mic when it
            // becomes enabled, and release it (mic indicator off) when switched to Off.
            // Only acts on an actual change, so a failed start (no devices) isn't retried
            // every frame.
            if (mode != _activeMode)
            {
                if (mode == MicMode.Off) StopMic();
                else if (_clip == null) StartMic();
                _activeMode = mode;
            }

            if (mode == MicMode.Off)
            {
                faceDriver.externalJawOpen = 0f;
                faceDriver.jawMode = JawBlendMode.WebcamOnly;
                return;
            }

            float target = 0f;
            if (_clip != null && Microphone.IsRecording(_device))
            {
                int pos = Microphone.GetPosition(_device);
                int start = pos - _window.Length;
                if (start < 0) start = 0;
                if (start + _window.Length <= _clip.samples)
                {
                    _clip.GetData(_window, start);
                    // RMS (root-mean-square) loudness of the window, then map
                    // [noiseFloor..loudAt] onto a [0..1] jaw-open target.
                    double sum = 0;
                    for (int i = 0; i < _window.Length; i++) sum += _window[i] * _window[i];
                    float rms = Mathf.Sqrt((float)(sum / _window.Length));
                    target = Mathf.InverseLerp(noiseFloor, loudAt, rms);
                }
            }

            // Asymmetric smoothing: snappy open, gentle close.
            float k = target > _level ? attack : release;
            float rate = 1f - Mathf.Exp(-Time.deltaTime * Mathf.Lerp(4f, 30f, k));
            _level = Mathf.Lerp(_level, target, rate);

            faceDriver.jawMode = JawBlendMode.Max;
            if (mode == MicMode.FillWhenNoWebcam)
                faceDriver.externalJawOpen = faceDriver.TrackingActive ? 0f : _level;
            // Blend mode: always feed the mic level so it mixes with the webcam mouth.
            else
                faceDriver.externalJawOpen = _level;
        }
    }
}
