using System;
using System.IO;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// User-editable runtime configuration, saved as vtuber_config.json next to the
    /// built executable (or the project root in the editor). Lets the streamer tune
    /// the rig without rebuilding.
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        [Serializable]
        public class HeadCfg
        {
            public bool enable = true;
            public float pitchScale = 1f, yawScale = 1f, rollScale = 1f;
            public bool invertPitch = false, invertYaw = false, invertRoll = false;
            public float maxAngle = 35f;
            public float neckShare = 0.35f, chestLean = 0.15f;
            public float smoothing = 0.5f;
            public bool freezeOnBlink = true;
            public float blinkOnsetRate = 6f;
        }

        [Serializable]
        public class GazeCfg
        {
            public bool enable = true;
            public float maxYaw = 12f, maxPitch = 9f, smoothing = 0.4f;
            public float deadzone = 0.12f;
        }

        [Serializable]
        public class MicCfg
        {
            // Off | FillWhenNoWebcam | Blend
            public string mode = "Blend";
            public string device = "";
            public float noiseFloor = 0.012f, loudAt = 0.16f;
            public float attack = 0.6f, release = 0.25f;
        }

        [Serializable]
        public class IdleCfg
        {
            public bool breathing = true, sway = true, autoBlink = true;
            public float breathsPerMinute = 14f, breathAmplitude = 1.2f;
            // Off: keep the prefab's natural pose.
            public bool restArms = false;
            public float restArmsAngle = 72f;

            // Body-angle follow: lean the torso the same way the head turns, lagged and
            // reduced (2D vTuber style). Derived from head tracking; no extra input.
            public bool bodyFollow = true;
            public float bodyFollowStrength = 0.35f;
            public float bodyFollowMaxAngle = 12f;
            public float bodyFollowSmoothing = 0.6f;
            public float bodyHipShare = 0.5f, bodySpineShare = 0.5f;
        }

        [Serializable]
        public class SpringCfg
        {
            // Hair / clothing / ear secondary motion (replaces VRChat PhysBones, which
            // only run in the editor). By default each chain uses the tuning imported
            // from its PhysBone (matches VRChat); the *Mul values let you nudge all
            // chains at once without losing their per-chain ratios. Tune live with F5.
            public bool enable = true;
            public bool useImported = true;
            public float gravityMul = 1f;
            public float stiffnessMul = 1f;
            public float dampingMul = 1f;
            public float elasticityMul = 1f;

            // Flat values used only if useImported = false (one feel for every chain).
            public float stiffness = 0.3f;
            public float damping = 0.25f;
            public float gravity = 0f;
            public float elasticity = 0.25f;
        }

        [Serializable]
        public class CameraPreset
        {
            public string name = "Preset";
            public float fov = 30f;
            public float distance = 2.2f;
            public float height = 1.1f;
            public float pivotY = 1.1f;
            public float panX = 0f;
            public float panY = 0f;
            public float roll = 0f;
        }

        [Serializable]
        public class RenderCfg
        {
            // 16:9 transparent canvas at 1080p. Resolution trades directly against
            // sharpness (the GPU cost is the pixels the avatar *covers*, so lowering it just
            // renders her smaller/blurrier) - keep this high and cut GPU via the frame
            // rate / shader instead. (Applied when you run "VTuber My Avatar > Build Scene".)
            public int width = 1920, height = 1080;
            public int targetFps = 60;
            public string spoutName = "VTuberMyAvatar";
            public float lightIntensity = 1.1f;
            public float lightPitch = 35f, lightYaw = -25f;
            public float ambient = 0.55f;

            // Camera framing, relative to the avatar (metres / degrees). Live-tunable
            // with F5. Defaults show head-to-thigh with room around her.
            public float cameraFov = 30f;
            public float cameraDistance = 2.2f;
            public float cameraHeight = 1.1f;
            public float pivotY = 1.1f;
            // Pan the framing within the canvas (metres): +X right, +Y up.
            public float panX = 0f;
            public float panY = 0f;
            // Roll/tilt the rendered image around its centre (degrees, clockwise).
            public float cameraRoll = 0f;

            // Quick zoom/framing presets - recall with 1-5, save the current framing
            // into a slot with Shift+1-5. All default to today's framing at a ladder of
            // camera distances (look-at point unchanged); re-save each slot once you've
            // dialed in a shot you like on stream.
            public float presetBlendSeconds = 0.25f;
            public CameraPreset[] presets = new[]
            {
                new CameraPreset { name = "Wide",   distance = 2.8f },
                new CameraPreset { name = "Medium", distance = 2.2f },
                new CameraPreset { name = "Close",  distance = 1.5f },
                new CameraPreset { name = "Closer", distance = 1.0f },
                new CameraPreset { name = "Tight",  distance = 0.7f },
            };
        }

        // Network / general
        public int udpPort = 39539;
        public bool mirror = true;
        public float expressionSmoothing = 0.4f;
        public float expressionMultiplier = 1.0f;
        // Eye-close remap: webcam blink rarely hits 1.0, so [low..high] maps to fully
        // open..fully shut. Lower blinkInputHigh if the lids don't fully close.
        public float blinkInputLow = 0.1f;
        public float blinkInputHigh = 0.8f;

        public HeadCfg head = new HeadCfg();
        public GazeCfg gaze = new GazeCfg();
        public MicCfg mic = new MicCfg();
        public IdleCfg idle = new IdleCfg();
        public SpringCfg spring = new SpringCfg();
        public RenderCfg render = new RenderCfg();

        public static string ConfigPath
        {
            get
            {
                // dataPath: <project>/Assets in editor, <app>_Data in a build.
                string dir = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(dir, "vtuber_config.json");
            }
        }

        public static AppSettings LoadOrCreate()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var s = JsonUtility.FromJson<AppSettings>(json);
                    if (s != null)
                    {
                        Debug.Log($"[vTuber] Loaded config: {ConfigPath}");
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[vTuber] Failed to read config, using defaults: {e.Message}");
            }

            var def = new AppSettings();
            def.Save();
            Debug.Log($"[vTuber] Wrote default config: {ConfigPath}");
            return def;
        }

        public void Save()
        {
            try { File.WriteAllText(ConfigPath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning($"[vTuber] Failed to save config: {e.Message}"); }
        }

        public MicMode MicModeEnum =>
            Enum.TryParse<MicMode>(mic.mode, true, out var m) ? m : MicMode.Blend;
    }
}
