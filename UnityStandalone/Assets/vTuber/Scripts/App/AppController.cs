using System;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Orchestrates the app: loads config, applies it to all drivers, enforces the
    /// performance budget (FPS cap, no vsync), handles hotkeys, and draws an
    /// operator-only status overlay + settings panel (NOT part of the Spout output,
    /// since that comes from the camera's render texture).
    ///
    /// Hotkeys:
    ///   R   recenter head pose      M  toggle mirror
    ///   L   cycle mic mode          T  toggle face tracking
    ///   B   calibrate blink         F1 toggle status overlay
    ///   F2  toggle settings panel   F5 reload config from disk
    ///   1-5 recall camera preset    Shift+1-5 save current framing to that preset
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AppController : MonoBehaviour
    {
        [Header("Wiring (auto-found if left empty)")]
        public UdpTrackingReceiver receiver;
        public AvatarFaceDriver face;
        public HeadPoseDriver head;
        public EyeGazeDriver gaze;
        public MicLipSync mic;
        public IdleMotion idle;
        public Light keyLight;
        public Camera outputCamera;

        public AppSettings Settings { get; private set; }
        private bool _showOverlay = true;
        private bool _faceEnabled = true;
        private GUIStyle _style;
        private string _calibMsg = "";
        private float _calibMsgTime = -10f;
        private int _springCount;

        // --- Settings panel (F2) ---
        private bool _showSettingsPanel;
        private int _settingsTab;
        private Vector2 _settingsScroll;
        private Rect _settingsWindowRect = new Rect(420, 20, 460, 480);
        private bool _settingsDirty;
        private float _settingsDirtyAt;
        private bool _showMicDeviceList;
        private string[] _micDevices = Array.Empty<string>();
        private GUIStyle _smallStyle;
        private const int SettingsWindowId = 194100;
        private static readonly string[] SettingsTabNames =
            { "General", "Head", "Gaze", "Mic", "Idle", "Spring", "Render", "Presets" };
        private static readonly string[] MicModeNames = { "Off", "FillWhenNoWebcam", "Blend" };

        // --- Camera presets (1-5 recall, Shift+1-5 save) ---
        private int _activePreset = -1;
        private bool _camLerping;
        private float _camLerpT;
        private AppSettings.CameraPreset _camFrom = new AppSettings.CameraPreset();

        private void Awake()
        {
            Settings = AppSettings.LoadOrCreate();
            AutoWire();
            ApplyPerformance();
            ApplySettings();
            _micDevices = Microphone.devices;
        }

        private void OnApplicationQuit()
        {
            if (_settingsDirty) Settings.Save();
        }

        private void AutoWire()
        {
            if (receiver == null) receiver = FindObjectOfType<UdpTrackingReceiver>();
            if (face == null) face = FindObjectOfType<AvatarFaceDriver>();
            if (head == null) head = FindObjectOfType<HeadPoseDriver>();
            if (gaze == null) gaze = FindObjectOfType<EyeGazeDriver>();
            if (mic == null) mic = FindObjectOfType<MicLipSync>();
            if (idle == null) idle = FindObjectOfType<IdleMotion>();
        }

        private void ApplyPerformance()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Max(15, Settings.render.targetFps);
            // Keep rendering while the operator window is in the background; OBS still needs frames.
            Application.runInBackground = true;
        }

        private void ApplySettings()
        {
            var s = Settings;

            if (receiver != null) receiver.port = s.udpPort;

            if (face != null)
            {
                face.mirror = s.mirror;
                face.smoothing = s.expressionSmoothing;
                face.expressionMultiplier = s.expressionMultiplier;
                face.blinkInputLow = s.blinkInputLow;
                face.blinkInputHigh = s.blinkInputHigh;
            }
            if (head != null)
            {
                head.enableHead = s.head.enable;
                head.pitchScale = s.head.pitchScale; head.yawScale = s.head.yawScale; head.rollScale = s.head.rollScale;
                head.invertPitch = s.head.invertPitch; head.invertYaw = s.head.invertYaw; head.invertRoll = s.head.invertRoll;
                head.maxAngle = s.head.maxAngle;
                head.neckShare = s.head.neckShare; head.chestLean = s.head.chestLean;
                head.smoothing = s.head.smoothing;
                head.freezeOnBlink = s.head.freezeOnBlink;
                head.blinkOnsetRate = s.head.blinkOnsetRate;
                head.mirror = s.mirror;
            }
            if (gaze != null)
            {
                gaze.enableGaze = s.gaze.enable;
                gaze.maxYaw = s.gaze.maxYaw; gaze.maxPitch = s.gaze.maxPitch;
                gaze.deadzone = s.gaze.deadzone;
                gaze.smoothing = s.gaze.smoothing; gaze.mirror = s.mirror;
            }
            if (mic != null)
            {
                mic.mode = s.MicModeEnum;
                mic.deviceName = s.mic.device;
                mic.noiseFloor = s.mic.noiseFloor; mic.loudAt = s.mic.loudAt;
                mic.attack = s.mic.attack; mic.release = s.mic.release;
            }
            if (idle != null)
            {
                idle.enableBreathing = s.idle.breathing;
                idle.enableSway = s.idle.sway;
                idle.enableAutoBlink = s.idle.autoBlink;
                idle.breathsPerMinute = s.idle.breathsPerMinute;
                idle.breathAmplitude = s.idle.breathAmplitude;
                idle.applyRestArms = s.idle.restArms;
                idle.restArmsAngle = s.idle.restArmsAngle;
                idle.enableBodyFollow = s.idle.bodyFollow;
                idle.bodyFollowStrength = s.idle.bodyFollowStrength;
                idle.bodyMaxAngle = s.idle.bodyFollowMaxAngle;
                idle.bodySmoothing = s.idle.bodyFollowSmoothing;
                idle.bodyHipShare = s.idle.bodyHipShare;
                idle.bodySpineShare = s.idle.bodySpineShare;
                idle.RefreshRestArms();
            }
            if (keyLight != null)
            {
                keyLight.intensity = s.render.lightIntensity;
                keyLight.transform.rotation = Quaternion.Euler(s.render.lightPitch, s.render.lightYaw, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(s.render.ambient, s.render.ambient, s.render.ambient, 1f);

            // Apply hair/cloth spring settings to every SpringBone in the scene.
            var springs = FindObjectsOfType<SpringBone>();
            _springCount = springs.Length;
            foreach (var sb in springs)
            {
                sb.simulate = s.spring.enable;
                if (s.spring.useImported)
                {
                    // Keep each chain's imported (VRChat) feel, scaled by global multipliers.
                    sb.elasticity = Mathf.Clamp01(sb.baseElasticity * s.spring.elasticityMul);
                    sb.stiffness = Mathf.Clamp01(sb.baseStiffness * s.spring.stiffnessMul);
                    sb.damping = Mathf.Clamp01(sb.baseDamping * s.spring.dampingMul);
                    sb.gravity = sb.baseGravity * s.spring.gravityMul;
                }
                else
                {
                    sb.elasticity = s.spring.elasticity;
                    sb.stiffness = s.spring.stiffness;
                    sb.damping = s.spring.damping;
                    sb.gravity = s.spring.gravity;
                }
            }

            FrameCamera();
        }

        /// <summary>Position the output camera in front of the avatar from config.</summary>
        private void FrameCamera()
        {
            if (outputCamera == null) return;
            var r = Settings.render;
            outputCamera.fieldOfView = r.cameraFov;

            Transform root = head != null && head.animator != null ? head.animator.transform : null;
            Vector3 basePos = root != null ? root.position : Vector3.zero;
            Vector3 fwd = root != null ? root.forward : Vector3.forward;

            Vector3 pivot = basePos + Vector3.up * r.pivotY;
            Vector3 camPos = basePos + fwd * r.cameraDistance + Vector3.up * r.cameraHeight;
            outputCamera.transform.position = camPos;
            outputCamera.transform.LookAt(pivot);

            // Pan the framing left/right/up without rotating: shift camera and look
            // target by the same amount along the camera's own right/up axes.
            Vector3 pan = outputCamera.transform.right * r.panX + outputCamera.transform.up * r.panY;
            outputCamera.transform.position = camPos + pan;
            outputCamera.transform.LookAt(pivot + pan);

            // Roll the image around the view centre - applied last so it tilts the
            // already-framed shot instead of changing what's centred in it.
            outputCamera.transform.Rotate(0f, 0f, r.cameraRoll, Space.Self);
        }

        private void Update()
        {
            // Suppress single-key hotkeys while a settings-panel text field has focus
            // (e.g. renaming a preset or editing the UDP port), so typing "1" or "b"
            // doesn't also fire a hotkey.
            if (GUIUtility.keyboardControl == 0)
            {
                if (Input.GetKeyDown(KeyCode.R) && head != null) head.Recenter();
                if (Input.GetKeyDown(KeyCode.M)) ToggleMirror();
                if (Input.GetKeyDown(KeyCode.L)) CycleMic();
                if (Input.GetKeyDown(KeyCode.T)) ToggleFace();
                if (Input.GetKeyDown(KeyCode.B)) CalibrateBlink();
                if (Input.GetKeyDown(KeyCode.F1)) _showOverlay = !_showOverlay;
                if (Input.GetKeyDown(KeyCode.F2)) ToggleSettingsPanel();
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    // Flush any pending panel edit before reloading, so F5 right after a
                    // tweak reloads what you just set instead of discarding it.
                    if (_settingsDirty) { Settings.Save(); _settingsDirty = false; }
                    Settings = AppSettings.LoadOrCreate();
                    ApplyPerformance();
                    ApplySettings();
                }

                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                var presets = Settings.render.presets;
                for (int i = 0; i < presets.Length && i < 5; i++)
                {
                    if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                    if (shift) SavePresetSlot(i);
                    else RecallPreset(i);
                }
            }

            // Advance any in-flight camera preset blend.
            if (_camLerping)
            {
                _camLerpT += Time.unscaledDeltaTime / Mathf.Max(0.001f, Settings.render.presetBlendSeconds);
                if (_camLerpT >= 1f) { _camLerpT = 1f; _camLerping = false; }
                ApplyCameraPreset(Settings.render.presets[_activePreset], EaseOutCubic(_camLerpT));
            }

            // Debounced autosave: flush to disk shortly after the last panel edit, rather
            // than once per slider tick while dragging.
            if (_settingsDirty && Time.unscaledTime - _settingsDirtyAt > 0.4f)
            {
                Settings.Save();
                _settingsDirty = false;
            }
        }

        /// <summary>
        /// One-key blink calibration: press with eyes OPEN to learn your open baseline,
        /// press again with eyes CLOSED to learn your closed peak. Auto-detects which is
        /// which from the value, so it works with glasses (which lower the closed reading).
        /// </summary>
        private void CalibrateBlink()
        {
            if (receiver == null || face == null) { return; }
            var f = receiver.Current;
            if (f.Shapes == null || f.Shapes.Length != TrackingProtocol.NumShapes || !receiver.TrackerConnected)
            {
                ShowCalib("Blink calibrate: tracker not running.");
                return;
            }
            float v = Mathf.Max(f.Shapes[(int)ArkShape.EyeBlinkLeft], f.Shapes[(int)ArkShape.EyeBlinkRight]);

            if (v < 0.35f)
            {
                Settings.blinkInputLow = Mathf.Clamp(v + 0.05f, 0f, 0.4f);
                ShowCalib($"Eyes-OPEN learned ({v:F2}). Now close your eyes & press B.");
            }
            else
            {
                Settings.blinkInputHigh = Mathf.Clamp(v - 0.05f, Settings.blinkInputLow + 0.1f, 1f);
                ShowCalib($"Eyes-CLOSED learned ({v:F2}). Blink range saved.");
            }
            face.blinkInputLow = Settings.blinkInputLow;
            face.blinkInputHigh = Settings.blinkInputHigh;
            Settings.Save();
        }

        private void ShowCalib(string msg)
        {
            _calibMsg = msg;
            _calibMsgTime = Time.unscaledTime;
            Debug.Log("[vTuber] " + msg);
        }

        private void ToggleMirror()
        {
            Settings.mirror = !Settings.mirror;
            if (face != null) face.mirror = Settings.mirror;
            if (head != null) head.mirror = Settings.mirror;
            if (gaze != null) gaze.mirror = Settings.mirror;
        }

        private void CycleMic()
        {
            if (mic == null) return;
            mic.mode = (MicMode)(((int)mic.mode + 1) % 3);
            // Persist like the other hotkeys (e.g. M/mirror) so the choice survives an
            // F5 reload / restart instead of silently reverting to the config value.
            Settings.mic.mode = mic.mode.ToString();
            _settingsDirty = true;
            _settingsDirtyAt = Time.unscaledTime;
        }

        private void ToggleFace()
        {
            _faceEnabled = !_faceEnabled;
            if (face != null) face.enabled = _faceEnabled;
        }

        private void ToggleSettingsPanel()
        {
            _showSettingsPanel = !_showSettingsPanel;
            if (!_showSettingsPanel && _settingsDirty)
            {
                Settings.Save();
                _settingsDirty = false;
            }
        }

        /// <summary>Call after any settings-panel edit: applies it live and marks the
        /// config dirty for the debounced autosave in Update().</summary>
        private void SettingsChanged()
        {
            // A manual edit always wins over an in-flight preset blend - otherwise the
            // blend's very next frame silently overwrites whatever you just dragged.
            _camLerping = false;
            ApplyPerformance();
            ApplySettings();
            _settingsDirty = true;
            _settingsDirtyAt = Time.unscaledTime;
        }

        /// <summary>Smoothly blends the live camera framing to preset <paramref name="i"/>
        /// over <see cref="AppSettings.RenderCfg.presetBlendSeconds"/> seconds.</summary>
        private void RecallPreset(int i)
        {
            var r = Settings.render;
            _camFrom = new AppSettings.CameraPreset
            {
                fov = r.cameraFov, distance = r.cameraDistance, height = r.cameraHeight,
                pivotY = r.pivotY, panX = r.panX, panY = r.panY, roll = r.cameraRoll
            };
            _activePreset = i;
            _camLerpT = 0f;
            _camLerping = Settings.render.presetBlendSeconds > 0.001f;
            if (!_camLerping) ApplyCameraPreset(Settings.render.presets[i], 1f);
            ShowCalib($"Camera preset {i + 1}: {Settings.render.presets[i].name}");
            _settingsDirty = true;
            _settingsDirtyAt = Time.unscaledTime;
        }

        /// <summary>Overwrites preset <paramref name="i"/> with the current live framing.</summary>
        private void SavePresetSlot(int i)
        {
            var r = Settings.render;
            var p = Settings.render.presets[i];
            p.fov = r.cameraFov; p.distance = r.cameraDistance; p.height = r.cameraHeight;
            p.pivotY = r.pivotY; p.panX = r.panX; p.panY = r.panY; p.roll = r.cameraRoll;
            _activePreset = i;
            ShowCalib($"Saved current framing to preset {i + 1}: {p.name}");
            _settingsDirty = true;
            _settingsDirtyAt = Time.unscaledTime;
        }

        private void ApplyCameraPreset(AppSettings.CameraPreset p, float t)
        {
            var r = Settings.render;
            r.cameraFov = Mathf.Lerp(_camFrom.fov, p.fov, t);
            r.cameraDistance = Mathf.Lerp(_camFrom.distance, p.distance, t);
            r.cameraHeight = Mathf.Lerp(_camFrom.height, p.height, t);
            r.pivotY = Mathf.Lerp(_camFrom.pivotY, p.pivotY, t);
            r.panX = Mathf.Lerp(_camFrom.panX, p.panX, t);
            r.panY = Mathf.Lerp(_camFrom.panY, p.panY, t);
            r.cameraRoll = Mathf.LerpAngle(_camFrom.roll, p.roll, t);
            FrameCamera();
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3);

        private void OnGUI()
        {
            if (_showOverlay) DrawStatusOverlay();
            if (_showSettingsPanel)
                _settingsWindowRect = GUILayout.Window(SettingsWindowId, _settingsWindowRect, DrawSettingsWindow,
                    "VTuber My Avatar - Settings  (F2 close)");
        }

        private void DrawStatusOverlay()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    richText = true,
                    normal = { textColor = Color.white }
                };
            }

            bool connected = receiver != null && receiver.TrackerConnected;
            bool faceValid = receiver != null && receiver.Current.FaceValid;
            string tracker = !connected ? "<color=#ff6666>tracker OFFLINE</color>"
                : faceValid ? "<color=#66ff88>tracking face</color>"
                : "<color=#ffcc44>searching...</color>";

            GUI.Box(new Rect(8, 8, 380, 211), GUIContent.none);
            float y = 12;
            void Line(string t) { GUI.Label(new Rect(16, y, 360, 20), t, _style); y += 19; }

            var cur = receiver != null ? receiver.Current : default;
            string headBone = head == null ? "n/a"
                : head.HasHead ? "<color=#66ff88>found</color>"
                : "<color=#ff6666>MISSING</color>";
            float liveBlink = (cur.Shapes != null && cur.Shapes.Length == TrackingProtocol.NumShapes)
                ? Mathf.Max(cur.Shapes[(int)ArkShape.EyeBlinkLeft], cur.Shapes[(int)ArkShape.EyeBlinkRight])
                : 0f;

            Line($"<b>VTuber My Avatar</b>   {Application.targetFrameRate} fps cap");
            Line($"Tracker: {tracker}");
            Line($"Packets/s: {(receiver != null ? receiver.PacketsPerSecond.ToString("0") : "-")}   render: {(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)):0} fps");
            Line($"Head bone: {headBone}   incoming P/Y/R: {cur.HeadPitch:0}/{cur.HeadYaw:0}/{cur.HeadRoll:0}");
            Line($"Eye-close: now {liveBlink:F2}  range {(Settings != null ? Settings.blinkInputLow : 0):F2}-{(Settings != null ? Settings.blinkInputHigh : 0):F2}  [B] calibrate");
            Line($"Mirror [M]: {(Settings != null && Settings.mirror ? "on" : "off")}   Face [T]: {(_faceEnabled ? "on" : "off")}   Mic [L]: {(mic != null ? mic.mode.ToString() : "n/a")}");
            Line($"Spring bones: {_springCount} {(Settings != null && Settings.spring.enable ? "<color=#66ff88>on</color>" : "<color=#ff6666>off</color>")}");
            string camLabel = (Settings != null && _activePreset >= 0 && _activePreset < Settings.render.presets.Length)
                ? Settings.render.presets[_activePreset].name
                : "custom";
            Line($"Cam preset: {camLabel}   [1-5] recall  [Shift+1-5] save");
            if (Time.unscaledTime - _calibMsgTime < 4f)
                Line($"<color=#88ccff>{_calibMsg}</color>");
            else
                Line("R recenter | B blink-cal | F1 hide | F2 settings | F5 reload");
        }

        // --- Settings panel (F2) -------------------------------------------------

        private void DrawSettingsWindow(int id)
        {
            if (_smallStyle == null)
                _smallStyle = new GUIStyle(GUI.skin.label) { richText = true, normal = { textColor = Color.white } };

            _settingsTab = GUILayout.Toolbar(_settingsTab, SettingsTabNames);
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.Height(420));
            switch (_settingsTab)
            {
                case 0: DrawGeneralTab(); break;
                case 1: DrawHeadTab(); break;
                case 2: DrawGazeTab(); break;
                case 3: DrawMicTab(); break;
                case 4: DrawIdleTab(); break;
                case 5: DrawSpringTab(); break;
                case 6: DrawRenderTab(); break;
                case 7: DrawPresetsTab(); break;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save now", GUILayout.Width(100))) { Settings.Save(); _settingsDirty = false; }
            GUILayout.FlexibleSpace();
            GUILayout.Label(_settingsDirty ? "<color=#ffcc44>unsaved...</color>" : "saved", _smallStyle);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawGeneralTab()
        {
            GUILayout.Label("<b>General</b>", _smallStyle);

            bool b = Toggle("Mirror", Settings.mirror);
            if (b != Settings.mirror) { Settings.mirror = b; SettingsChanged(); }

            float v = Slider("Expr. smoothing", Settings.expressionSmoothing, 0f, 1f);
            if (v != Settings.expressionSmoothing) { Settings.expressionSmoothing = v; SettingsChanged(); }

            v = Slider("Expr. multiplier", Settings.expressionMultiplier, 0f, 2f);
            if (v != Settings.expressionMultiplier) { Settings.expressionMultiplier = v; SettingsChanged(); }

            v = Slider("Blink input low", Settings.blinkInputLow, 0f, 0.5f);
            if (v != Settings.blinkInputLow) { Settings.blinkInputLow = v; SettingsChanged(); }

            // Floor at 0.2 (not 0.5): blink calibration can legitimately store values this
            // low (e.g. with glasses), and a 0.5 floor would snap them up when touched.
            v = Slider("Blink input high", Settings.blinkInputHigh, 0.2f, 1f);
            if (v != Settings.blinkInputHigh) { Settings.blinkInputHigh = v; SettingsChanged(); }

            int port = IntField("UDP port", Settings.udpPort);
            if (port != Settings.udpPort)
            {
                Settings.udpPort = port;
                if (receiver != null) receiver.Rebind(port);
                SettingsChanged();
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset General to defaults"))
            {
                var d = new AppSettings();
                Settings.mirror = d.mirror;
                Settings.expressionSmoothing = d.expressionSmoothing;
                Settings.expressionMultiplier = d.expressionMultiplier;
                Settings.blinkInputLow = d.blinkInputLow;
                Settings.blinkInputHigh = d.blinkInputHigh;
                Settings.udpPort = d.udpPort;
                if (receiver != null) receiver.Rebind(Settings.udpPort);
                SettingsChanged();
            }
        }

        private void DrawHeadTab()
        {
            // Class field: mutating h.* mutates Settings.head directly.
            var h = Settings.head;
            GUILayout.Label("<b>Head</b>", _smallStyle);

            bool b = Toggle("Enable", h.enable);
            if (b != h.enable) { h.enable = b; SettingsChanged(); }

            float v = Slider("Pitch scale", h.pitchScale, 0f, 2f);
            if (v != h.pitchScale) { h.pitchScale = v; SettingsChanged(); }
            v = Slider("Yaw scale", h.yawScale, 0f, 2f);
            if (v != h.yawScale) { h.yawScale = v; SettingsChanged(); }
            v = Slider("Roll scale", h.rollScale, 0f, 2f);
            if (v != h.rollScale) { h.rollScale = v; SettingsChanged(); }

            b = Toggle("Invert pitch", h.invertPitch);
            if (b != h.invertPitch) { h.invertPitch = b; SettingsChanged(); }
            b = Toggle("Invert yaw", h.invertYaw);
            if (b != h.invertYaw) { h.invertYaw = b; SettingsChanged(); }
            b = Toggle("Invert roll", h.invertRoll);
            if (b != h.invertRoll) { h.invertRoll = b; SettingsChanged(); }

            v = Slider("Max angle", h.maxAngle, 0f, 90f);
            if (v != h.maxAngle) { h.maxAngle = v; SettingsChanged(); }
            v = Slider("Neck share", h.neckShare, 0f, 1f);
            if (v != h.neckShare) { h.neckShare = v; SettingsChanged(); }
            v = Slider("Chest lean", h.chestLean, 0f, 1f);
            if (v != h.chestLean) { h.chestLean = v; SettingsChanged(); }
            v = Slider("Smoothing", h.smoothing, 0f, 1f);
            if (v != h.smoothing) { h.smoothing = v; SettingsChanged(); }

            b = Toggle("Freeze on blink", h.freezeOnBlink);
            if (b != h.freezeOnBlink) { h.freezeOnBlink = b; SettingsChanged(); }
            v = Slider("Blink onset rate", h.blinkOnsetRate, 1f, 20f);
            if (v != h.blinkOnsetRate) { h.blinkOnsetRate = v; SettingsChanged(); }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Head to defaults")) { Settings.head = new AppSettings.HeadCfg(); SettingsChanged(); }
        }

        private void DrawGazeTab()
        {
            var g = Settings.gaze;
            GUILayout.Label("<b>Gaze</b>", _smallStyle);

            bool b = Toggle("Enable", g.enable);
            if (b != g.enable) { g.enable = b; SettingsChanged(); }

            float v = Slider("Max yaw", g.maxYaw, 0f, 45f);
            if (v != g.maxYaw) { g.maxYaw = v; SettingsChanged(); }
            v = Slider("Max pitch", g.maxPitch, 0f, 45f);
            if (v != g.maxPitch) { g.maxPitch = v; SettingsChanged(); }
            v = Slider("Smoothing", g.smoothing, 0f, 1f);
            if (v != g.smoothing) { g.smoothing = v; SettingsChanged(); }
            v = Slider("Deadzone", g.deadzone, 0f, 0.5f);
            if (v != g.deadzone) { g.deadzone = v; SettingsChanged(); }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Gaze to defaults")) { Settings.gaze = new AppSettings.GazeCfg(); SettingsChanged(); }
        }

        private void DrawMicTab()
        {
            var m = Settings.mic;
            GUILayout.Label("<b>Mic</b>", _smallStyle);

            int idx = Array.IndexOf(MicModeNames, m.mode);
            if (idx < 0) idx = 2;
            int nidx = GUILayout.Toolbar(idx, MicModeNames);
            if (nidx != idx) { m.mode = MicModeNames[nidx]; SettingsChanged(); }

            GUILayout.Space(4);
            GUILayout.Label("Device", GUILayout.Width(140));
            string curDevice = string.IsNullOrEmpty(m.device) ? "(Default)" : m.device;
            if (GUILayout.Button(curDevice)) _showMicDeviceList = !_showMicDeviceList;
            if (_showMicDeviceList)
            {
                if (GUILayout.Button("(Default)")) { m.device = ""; SettingsChanged(); _showMicDeviceList = false; }
                foreach (var d in _micDevices)
                {
                    if (GUILayout.Button(d)) { m.device = d; SettingsChanged(); _showMicDeviceList = false; }
                }
                if (GUILayout.Button("Refresh device list")) _micDevices = Microphone.devices;
            }

            float v = Slider("Noise floor", m.noiseFloor, 0f, 0.2f);
            if (v != m.noiseFloor) { m.noiseFloor = v; SettingsChanged(); }
            v = Slider("Loud at", m.loudAt, 0f, 1f);
            if (v != m.loudAt) { m.loudAt = v; SettingsChanged(); }
            v = Slider("Attack", m.attack, 0f, 1f);
            if (v != m.attack) { m.attack = v; SettingsChanged(); }
            v = Slider("Release", m.release, 0f, 1f);
            if (v != m.release) { m.release = v; SettingsChanged(); }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Mic to defaults")) { Settings.mic = new AppSettings.MicCfg(); SettingsChanged(); }
        }

        private void DrawIdleTab()
        {
            var idleS = Settings.idle;
            GUILayout.Label("<b>Idle</b>", _smallStyle);

            bool b = Toggle("Breathing", idleS.breathing);
            if (b != idleS.breathing) { idleS.breathing = b; SettingsChanged(); }
            b = Toggle("Sway", idleS.sway);
            if (b != idleS.sway) { idleS.sway = b; SettingsChanged(); }
            b = Toggle("Auto-blink", idleS.autoBlink);
            if (b != idleS.autoBlink) { idleS.autoBlink = b; SettingsChanged(); }

            float v = Slider("Breaths/min", idleS.breathsPerMinute, 4f, 30f);
            if (v != idleS.breathsPerMinute) { idleS.breathsPerMinute = v; SettingsChanged(); }
            v = Slider("Breath amplitude", idleS.breathAmplitude, 0f, 3f);
            if (v != idleS.breathAmplitude) { idleS.breathAmplitude = v; SettingsChanged(); }

            b = Toggle("Rest arms", idleS.restArms);
            if (b != idleS.restArms) { idleS.restArms = b; SettingsChanged(); }
            v = Slider("Rest arms angle", idleS.restArmsAngle, 0f, 90f);
            if (v != idleS.restArmsAngle) { idleS.restArmsAngle = v; SettingsChanged(); }

            GUILayout.Space(4);
            GUILayout.Label("<i>Rest arms takes effect on next launch.</i>", _smallStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>Body-angle follow (2D-style)</b>", _smallStyle);
            b = Toggle("Body follow", idleS.bodyFollow);
            if (b != idleS.bodyFollow) { idleS.bodyFollow = b; SettingsChanged(); }
            v = Slider("Follow strength", idleS.bodyFollowStrength, 0f, 1f);
            if (v != idleS.bodyFollowStrength) { idleS.bodyFollowStrength = v; SettingsChanged(); }
            v = Slider("Follow max angle", idleS.bodyFollowMaxAngle, 0f, 30f);
            if (v != idleS.bodyFollowMaxAngle) { idleS.bodyFollowMaxAngle = v; SettingsChanged(); }
            v = Slider("Follow smoothing", idleS.bodyFollowSmoothing, 0f, 1f);
            if (v != idleS.bodyFollowSmoothing) { idleS.bodyFollowSmoothing = v; SettingsChanged(); }
            v = Slider("Hip share", idleS.bodyHipShare, 0f, 1f);
            if (v != idleS.bodyHipShare) { idleS.bodyHipShare = v; SettingsChanged(); }
            v = Slider("Spine share", idleS.bodySpineShare, 0f, 1f);
            if (v != idleS.bodySpineShare) { idleS.bodySpineShare = v; SettingsChanged(); }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Idle to defaults")) { Settings.idle = new AppSettings.IdleCfg(); SettingsChanged(); }
        }

        private void DrawSpringTab()
        {
            var sp = Settings.spring;
            GUILayout.Label("<b>Spring (hair / cloth)</b>", _smallStyle);

            bool b = Toggle("Enable", sp.enable);
            if (b != sp.enable) { sp.enable = b; SettingsChanged(); }
            b = Toggle("Use imported (per-chain) feel", sp.useImported);
            if (b != sp.useImported) { sp.useImported = b; SettingsChanged(); }

            float v;
            if (sp.useImported)
            {
                v = Slider("Gravity x", sp.gravityMul, 0f, 2f);
                if (v != sp.gravityMul) { sp.gravityMul = v; SettingsChanged(); }
                v = Slider("Stiffness x", sp.stiffnessMul, 0f, 2f);
                if (v != sp.stiffnessMul) { sp.stiffnessMul = v; SettingsChanged(); }
                v = Slider("Damping x", sp.dampingMul, 0f, 2f);
                if (v != sp.dampingMul) { sp.dampingMul = v; SettingsChanged(); }
                v = Slider("Elasticity x", sp.elasticityMul, 0f, 2f);
                if (v != sp.elasticityMul) { sp.elasticityMul = v; SettingsChanged(); }
            }
            else
            {
                v = Slider("Stiffness", sp.stiffness, 0f, 1f);
                if (v != sp.stiffness) { sp.stiffness = v; SettingsChanged(); }
                v = Slider("Damping", sp.damping, 0f, 1f);
                if (v != sp.damping) { sp.damping = v; SettingsChanged(); }
                v = Slider("Gravity", sp.gravity, 0f, 9.81f);
                if (v != sp.gravity) { sp.gravity = v; SettingsChanged(); }
                v = Slider("Elasticity", sp.elasticity, 0f, 1f);
                if (v != sp.elasticity) { sp.elasticity = v; SettingsChanged(); }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Spring to defaults")) { Settings.spring = new AppSettings.SpringCfg(); SettingsChanged(); }
        }

        private void DrawRenderTab()
        {
            var r = Settings.render;
            GUILayout.Label("<b>Render</b>", _smallStyle);

            float v = Slider("Light intensity", r.lightIntensity, 0f, 3f);
            if (v != r.lightIntensity) { r.lightIntensity = v; SettingsChanged(); }
            v = Slider("Light pitch", r.lightPitch, -90f, 90f);
            if (v != r.lightPitch) { r.lightPitch = v; SettingsChanged(); }
            v = Slider("Light yaw", r.lightYaw, -180f, 180f);
            if (v != r.lightYaw) { r.lightYaw = v; SettingsChanged(); }
            v = Slider("Ambient", r.ambient, 0f, 1f);
            if (v != r.ambient) { r.ambient = v; SettingsChanged(); }

            GUILayout.Space(4);
            v = Slider("Camera FOV", r.cameraFov, 10f, 90f);
            if (v != r.cameraFov) { r.cameraFov = v; SettingsChanged(); }
            v = Slider("Camera distance", r.cameraDistance, 0.5f, 6f);
            if (v != r.cameraDistance) { r.cameraDistance = v; SettingsChanged(); }
            v = Slider("Camera height", r.cameraHeight, -1f, 3f);
            if (v != r.cameraHeight) { r.cameraHeight = v; SettingsChanged(); }
            v = Slider("Pivot Y", r.pivotY, 0f, 2.5f);
            if (v != r.pivotY) { r.pivotY = v; SettingsChanged(); }
            v = Slider("Pan X", r.panX, -1.5f, 1.5f);
            if (v != r.panX) { r.panX = v; SettingsChanged(); }
            v = Slider("Pan Y", r.panY, -1.5f, 1.5f);
            if (v != r.panY) { r.panY = v; SettingsChanged(); }
            v = Slider("Camera roll", r.cameraRoll, -180f, 180f);
            if (v != r.cameraRoll) { r.cameraRoll = v; SettingsChanged(); }

            GUILayout.Space(4);
            v = Slider("Target FPS", r.targetFps, 15f, 144f);
            int newFps = Mathf.RoundToInt(v);
            if (newFps != r.targetFps) { r.targetFps = newFps; SettingsChanged(); }

            GUILayout.Space(8);
            GUILayout.Label($"Output size: {r.width}x{r.height}  - needs 'Build Scene' + rebuild " +
                             "(not changeable from the running app)", _smallStyle);
            GUILayout.Label($"Spout name: {r.spoutName}  - set at Build Scene time", _smallStyle);

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Render to defaults (keeps size/Spout name)"))
            {
                var d = new AppSettings.RenderCfg();
                r.lightIntensity = d.lightIntensity; r.lightPitch = d.lightPitch; r.lightYaw = d.lightYaw;
                r.ambient = d.ambient;
                r.cameraFov = d.cameraFov; r.cameraDistance = d.cameraDistance; r.cameraHeight = d.cameraHeight;
                r.pivotY = d.pivotY; r.panX = d.panX; r.panY = d.panY; r.cameraRoll = d.cameraRoll;
                r.targetFps = d.targetFps;
                SettingsChanged();
            }
        }

        private void DrawPresetsTab()
        {
            GUILayout.Label("<b>Camera Presets</b>  -  1-5 recall, Shift+1-5 save current", _smallStyle);

            float bs = Slider("Blend time (s)", Settings.render.presetBlendSeconds, 0f, 1.5f);
            if (bs != Settings.render.presetBlendSeconds) { Settings.render.presetBlendSeconds = bs; SettingsChanged(); }

            GUILayout.Space(6);
            var presets = Settings.render.presets;
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i + 1}", GUILayout.Width(16));
                string nn = GUILayout.TextField(p.name, GUILayout.Width(70));
                if (nn != p.name) { p.name = nn; _settingsDirty = true; _settingsDirtyAt = Time.unscaledTime; }
                GUILayout.Label($"d {p.distance:0.00}  h {p.height:0.00}  r {p.roll:0}°  f {p.fov:0}°", GUILayout.Width(180));
                if (GUILayout.Button("Recall", GUILayout.Width(55))) RecallPreset(i);
                if (GUILayout.Button("Save current", GUILayout.Width(90))) SavePresetSlot(i);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<i>Dial in a shot on the Render tab above, then Shift+1..5 (or " +
                             "'Save current' here) to bank it for quick recall on stream.</i>", _smallStyle);
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140));
            float nv = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(170));
            GUILayout.Label(nv.ToString("0.00"), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return nv;
        }

        private static bool Toggle(string label, bool value) => GUILayout.Toggle(value, label);

        private static int IntField(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(80));
            GUILayout.EndHorizontal();
            return int.TryParse(text, out var parsed) ? parsed : value;
        }
    }
}
