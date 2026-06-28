using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VTuberMyAvatar.EditorTools
{
    /// <summary>
    /// One-click builder for the VTuber My Avatar scene in the STANDALONE (no-SDK) project.
    /// Instantiates the already-baked avatar prefab, sets up a transparent output
    /// camera + Spout sender, an operator preview, a key light, and the full driver
    /// rig with everything wired together.
    ///
    /// This project has NO VRChat SDK / VRCFury / NDMF: the avatar is the baked prefab
    /// exported from the SDK authoring project ("VTuber My Avatar/Export Baked Avatar" there).
    /// The bake / strip / PhysBone-conversion / DLL-restore tooling lives ONLY in the SDK
    /// project and is intentionally absent here - there is nothing to bake or strip.
    ///
    /// Menu: VTuber My Avatar/Build Scene
    /// </summary>
    public static class VTuberMyAvatarSetup
    {
        private const string ScenePath = "Assets/vTuber/Scenes/VTuberMyAvatar.unity";
        private const string RtPath = "Assets/vTuber/VTuberOutput.renderTexture";

        // Where the chosen baked-avatar prefab is remembered (per Unity install). Keyed so
        // the standalone and authoring projects don't clobber each other's choice. Set it
        // with "VTuber My Avatar/Select Avatar Prefab...".
        private const string AvatarPrefKey = "VTuberMyAvatar.Standalone.AvatarPrefabPath";

        // The baked avatar imported from the SDK project's "Export Baked Avatar" package
        // (VRCFury applied, PhysBones converted to SpringBones, components stripped) lands
        // here by default, so this project is zero-config. Override per install with the
        // "Select Avatar Prefab..." menu if your baked prefab lives elsewhere.
        private const string DefaultPrefabPath = "Assets/Export/Avatar_Baked.prefab";

        [MenuItem("VTuber My Avatar/Select Avatar Prefab...", priority = 1)]
        public static void SelectAvatarPrefab()
        {
            var obj = Selection.activeObject as GameObject;
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("VTuber My Avatar",
                    "Select your baked avatar PREFAB in the Project window first, then run this again.\n\n" +
                    "It's the prefab from the Avatar_Baked.unitypackage you imported from the SDK project.", "OK");
                return;
            }
            EditorPrefs.SetString(AvatarPrefKey, path);
            EditorUtility.DisplayDialog("VTuber My Avatar",
                "Avatar prefab set to:\n" + path + "\n\nNow run 'VTuber My Avatar > Build Scene'.", "OK");
        }

        /// <summary>
        /// Resolves which baked prefab to build from: an explicit "Select Avatar Prefab..."
        /// choice, then the default Avatar_Baked.prefab path, then a single humanoid prefab.
        /// Returns null (after explaining why in a dialog) if it can't decide.
        /// </summary>
        private static GameObject ResolveAvatarPrefab()
        {
            string chosen = EditorPrefs.GetString(AvatarPrefKey, "");
            if (!string.IsNullOrEmpty(chosen))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(chosen);
                if (p != null) return p;
            }
            if (!string.IsNullOrEmpty(DefaultPrefabPath))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
                if (p != null) return p;
            }
            var humanoids = FindHumanoidAvatarPrefabs();
            if (humanoids.Count == 1)
                return AssetDatabase.LoadAssetAtPath<GameObject>(humanoids[0]);

            string why = humanoids.Count > 1
                ? "Found several humanoid prefabs, so I can't guess which one is your avatar."
                : "Couldn't find the baked avatar prefab.";
            EditorUtility.DisplayDialog("VTuber My Avatar",
                why + "\n\nImport the Avatar_Baked.unitypackage exported from the SDK project, " +
                "or pick the prefab in the Project window and run " +
                "'VTuber My Avatar > Select Avatar Prefab...'.", "OK");
            return null;
        }

        private static System.Collections.Generic.List<string> FindHumanoidAvatarPrefabs()
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim != null && anim.avatar != null && anim.avatar.isHuman)
                    result.Add(path);
            }
            return result;
        }

        [MenuItem("VTuber My Avatar/Build Scene", priority = 0)]
        public static void BuildScene()
        {
            var prefab = ResolveAvatarPrefab();
            // ResolveAvatarPrefab already told the user why
            if (prefab == null) return;

            if (!EditorUtility.DisplayDialog("VTuber My Avatar",
                "This creates a fresh scene at\n" + ScenePath +
                "\nand sets it as the build scene. Continue?", "Build", "Cancel"))
                return;

            // defaults for editor-time framing/res
            var settings = new AppSettings();

            Directory.CreateDirectory("Assets/vTuber/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Avatar ---
            // The prefab is already fully baked (clothing/ears/tail merged onto the rig,
            // PhysBones converted to SpringBones, VRChat/VRCFury components stripped), so we
            // just instantiate it as-is. Nothing here needs the VRChat SDK at runtime.
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Avatars can have several Animators (e.g. a non-humanoid one on a wrapper
            // plus the real humanoid rig). Pick the humanoid one for the drivers, and
            // neutralize ALL of them so none fight our bone writes.
            var allAnims = avatar.GetComponentsInChildren<Animator>(true);
            Animator anim = null;
            foreach (var a in allAnims) if (a.isHuman) { anim = a; break; }
            if (anim == null && allAnims.Length > 0) anim = allAnims[0];
            foreach (var a in allAnims)
            {
                // rest pose; our drivers own the bones
                a.runtimeAnimatorController = null;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (anim == null || !anim.isHuman)
                Debug.LogWarning("[vTuber] No humanoid Animator found on the avatar - head/eye/idle " +
                                 "tracking needs the rig set to Humanoid with an Avatar assigned.");
            else
                Debug.Log($"[vTuber] Using humanoid Animator on '{anim.gameObject.name}'.");
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                // keep blendshapes updating regardless of bounds
                smr.updateWhenOffscreen = true;

            // --- Output render texture (ARGB32 = alpha for transparency) ---
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RtPath);
            if (rt != null && (rt.width != settings.render.width || rt.height != settings.render.height))
            {
                // Resolution changed: RenderTextures can't be resized in place, so recreate.
                AssetDatabase.DeleteAsset(RtPath);
                rt = null;
            }
            if (rt == null)
            {
                rt = new RenderTexture(settings.render.width, settings.render.height, 24,
                                       RenderTextureFormat.ARGB32) { name = "VTuberOutput" };
                rt.antiAliasing = 2;
                AssetDatabase.CreateAsset(rt, RtPath);
            }

            // --- Output camera (transparent background) ---
            var camGo = new GameObject("OutputCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Fully transparent clear colour (alpha 0) so the Spout feed carries true alpha.
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.targetTexture = rt;
            FrameCameraEditor(cam, anim, settings);

            AddSpoutSender(camGo, rt, settings.render.spoutName);

            // Screen-clear camera: the output camera renders to a texture, so nothing
            // would clear the on-screen (Game view / standalone window) framebuffer and
            // the operator preview would leave motion trails. This camera renders no
            // objects, just clears the screen each frame. It is not part of the Spout
            // feed and has no effect on OBS.
            var clearGo = new GameObject("PreviewClearCamera");
            SceneManager.MoveGameObjectToScene(clearGo, scene);
            var clearCam = clearGo.AddComponent<Camera>();
            clearCam.clearFlags = CameraClearFlags.SolidColor;
            // Opaque dark background for the operator preview (not the transparent Spout feed).
            clearCam.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 1f);
            // Cull everything: this camera only clears the framebuffer, it renders no objects.
            clearCam.cullingMask = 0;
            // Lowest depth so it draws before every other camera each frame.
            clearCam.depth = -100;
            clearCam.allowHDR = false;
            clearCam.allowMSAA = false;

            // --- Key light ---
            var lightGo = new GameObject("KeyLight");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = settings.render.lightIntensity;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(settings.render.lightPitch, settings.render.lightYaw, 0f);

            // --- Operator preview canvas (NOT part of the Spout feed) ---
            var canvasGo = new GameObject("OperatorPreview");
            SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            var rawGo = new GameObject("Preview");
            rawGo.transform.SetParent(canvasGo.transform, false);
            var raw = rawGo.AddComponent<RawImage>();
            raw.texture = rt;
            var rectt = raw.rectTransform;
            rectt.anchorMin = Vector2.zero; rectt.anchorMax = Vector2.one;
            rectt.offsetMin = Vector2.zero; rectt.offsetMax = Vector2.zero;

            // --- Driver rig ---
            var rig = new GameObject("VTuberMyAvatarRig");
            SceneManager.MoveGameObjectToScene(rig, scene);
            var receiver = rig.AddComponent<UdpTrackingReceiver>();
            receiver.port = settings.udpPort;

            var face = rig.AddComponent<AvatarFaceDriver>();
            face.receiver = receiver;
            face.avatarRoot = avatar;

            var head = rig.AddComponent<HeadPoseDriver>();
            head.receiver = receiver; head.animator = anim;

            var gaze = rig.AddComponent<EyeGazeDriver>();
            gaze.receiver = receiver; gaze.animator = anim; gaze.avatarRoot = avatar;

            var mic = rig.AddComponent<MicLipSync>();
            mic.faceDriver = face;

            var idle = rig.AddComponent<IdleMotion>();
            idle.faceDriver = face; idle.animator = anim;

            var app = rig.AddComponent<AppController>();
            app.receiver = receiver; app.face = face; app.head = head; app.gaze = gaze;
            app.mic = mic; app.idle = idle; app.keyLight = light; app.outputCamera = cam;

            // --- Save & register ---
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();

            Debug.Log("[vTuber] Scene built at " + ScenePath +
                      ". Press Play, start the Python tracker, and add a Spout2 source named '" +
                      settings.render.spoutName + "' in OBS.");
            EditorUtility.DisplayDialog("VTuber My Avatar",
                "Scene built and set as the build scene.\n\n" +
                "Next:\n" +
                "1) Press Play (or build the app).\n" +
                "2) Run Tracker/start_tracker.bat.\n" +
                "3) In OBS add a 'Spout2 Capture' source -> '" + settings.render.spoutName + "'.\n\n" +
                "Hotkeys: R recenter, M mirror, L mic mode, F1 overlay.", "Great");
        }

        private static void FrameCameraEditor(Camera cam, Animator anim, AppSettings s)
        {
            cam.fieldOfView = s.render.cameraFov;
            Transform root = anim != null ? anim.transform : null;
            Vector3 basePos = root != null ? root.position : Vector3.zero;
            Vector3 fwd = root != null ? root.forward : Vector3.forward;
            Vector3 pivot = basePos + Vector3.up * s.render.pivotY;
            Vector3 camPos = basePos + fwd * s.render.cameraDistance + Vector3.up * s.render.cameraHeight;
            cam.transform.position = camPos;
            cam.transform.LookAt(pivot);
            Vector3 pan = cam.transform.right * s.render.panX + cam.transform.up * s.render.panY;
            cam.transform.position = camPos + pan;
            cam.transform.LookAt(pivot + pan);
        }

        private const string SpoutResPath = "Assets/vTuber/SpoutResources.asset";

        /// <summary>
        /// Adds and fully configures KlakSpout's SpoutSender via reflection (so this
        /// script compiles even without the package). Uses Texture capture (the only
        /// mode that works in the Built-in pipeline - Camera mode is SRP-only) and
        /// assigns a build-safe SpoutResources asset, which the package does not do
        /// automatically.
        /// </summary>
        private static void AddSpoutSender(GameObject camGo, RenderTexture rt, string spoutName)
        {
            // The runtime assembly is "Klak.Spout.Runtime"; scan all loaded assemblies
            // by full type name so we don't depend on the exact assembly name.
            var senderType = FindType("Klak.Spout.SpoutSender");
            var resourcesType = FindType("Klak.Spout.SpoutResources");
            if (senderType == null || resourcesType == null)
            {
                Debug.LogWarning("[vTuber] KlakSpout types not found. Make sure the package is imported " +
                                 "(Window > Package Manager), then re-run 'VTuber My Avatar/Build Scene'.");
                return;
            }

            var sender = camGo.GetComponent(senderType) ?? camGo.AddComponent(senderType);

            var resources = GetOrCreateSpoutResources(resourcesType);
            if (resources != null)
            {
                var setRes = senderType.GetMethod("SetResources");
                setRes?.Invoke(sender, new object[] { resources });
            }

            SetProperty(sender, "spoutName", spoutName);
            SetProperty(sender, "keepAlpha", true);

            var capProp = senderType.GetProperty("captureMethod");
            if (capProp != null)
            {
                try { capProp.SetValue(sender, Enum.Parse(capProp.PropertyType, "Texture")); }
                catch { /* enum name drift; leave default */ }
            }
            SetProperty(sender, "sourceTexture", rt);

            Debug.Log(resources != null
                ? "[vTuber] Added & configured SpoutSender '" + spoutName + "' (Texture capture, alpha on)."
                : "[vTuber] Added SpoutSender but could not assign SpoutResources; output may be blank.");
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// Returns a SpoutResources asset under Assets/ (so it survives a build). The
        /// package ships its copy in an Editor/ folder, which would be stripped from
        /// builds, so we clone its blit-shader reference into our own asset.
        /// </summary>
        private static UnityEngine.Object GetOrCreateSpoutResources(Type resourcesType)
        {
            // Reuse ours if it already exists.
            var existing = AssetDatabase.LoadAssetAtPath(SpoutResPath, resourcesType);
            if (existing != null) return existing;

            // Find any SpoutResources asset (incl. the package's) to copy the shader from.
            Shader blit = null;
            foreach (var guid in AssetDatabase.FindAssets("t:" + resourcesType.Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var src = AssetDatabase.LoadAssetAtPath(path, resourcesType);
                var f = resourcesType.GetField("blitShader");
                if (src != null && f != null && f.GetValue(src) is Shader s) { blit = s; break; }
            }
            if (blit == null)
            {
                Debug.LogWarning("[vTuber] Could not locate KlakSpout's blit shader; Spout output may be blank.");
                return null;
            }

            var res = ScriptableObject.CreateInstance(resourcesType);
            resourcesType.GetField("blitShader")?.SetValue(res, blit);
            AssetDatabase.CreateAsset(res, SpoutResPath);
            AssetDatabase.SaveAssets();
            return res;
        }

        private static void SetProperty(object target, string prop, object value)
        {
            var p = target.GetType().GetProperty(prop);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(target, value); }
                catch (Exception e) { Debug.LogWarning($"[vTuber] could not set {prop}: {e.Message}"); }
            }
        }
    }
}
