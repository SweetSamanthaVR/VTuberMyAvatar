using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VTuberMyAvatar.EditorTools
{
    /// <summary>
    /// One-click builder for the VTuber My Avatar scene. Instantiates the avatar prefab,
    /// sets up a transparent output camera + Spout sender, an operator preview, a
    /// key light, and the full driver rig with everything wired together.
    ///
    /// Menu: VTuber My Avatar/Build Scene
    /// </summary>
    public static class VTuberMyAvatarSetup
    {
        private const string ScenePath = "Assets/vTuber/Scenes/VTuberMyAvatar.unity";
        private const string RtPath = "Assets/vTuber/VTuberOutput.renderTexture";

        // Where the chosen avatar prefab is remembered (per Unity install). Keyed so the
        // authoring and standalone projects don't clobber each other's choice. Set it with
        // "VTuber My Avatar/Select Avatar Prefab...".
        private const string AvatarPrefKey = "VTuberMyAvatar.Authoring.AvatarPrefabPath";

        // Optional zero-config convenience: if you keep ONE avatar prefab at a fixed path,
        // put it here and Build Scene finds it with no setup. Leave "" to always rely on
        // "Select Avatar Prefab..." / auto-detection (the normal bring-your-own-avatar path).
        private const string DefaultPrefabPath = "";

        // --- Avatar selection -------------------------------------------------------------
        // The runtime drivers are avatar-agnostic (blendshapes are discovered at runtime and
        // ARKit maps onto any Unified-Expressions shape set), so the only per-user choice is
        // WHICH prefab to build the scene from. Pick it once here.

        [MenuItem("VTuber My Avatar/Select Avatar Prefab...", priority = 1)]
        public static void SelectAvatarPrefab()
        {
            var obj = Selection.activeObject as GameObject;
            string path = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("VTuber My Avatar",
                    "Select your avatar's PREFAB in the Project window first, then run this again.\n\n" +
                    "No prefab yet? Drag your avatar (the object with its VRChat Avatar Descriptor) " +
                    "from the Hierarchy into a Project folder to create one.", "OK");
                return;
            }
            EditorPrefs.SetString(AvatarPrefKey, path);
            EditorUtility.DisplayDialog("VTuber My Avatar",
                "Avatar prefab set to:\n" + path + "\n\nNow run 'VTuber My Avatar > Build Scene'.", "OK");
        }

        /// <summary>
        /// Resolves which prefab to build from: an explicit "Select Avatar Prefab..." choice,
        /// then the optional DefaultPrefabPath, then auto-detection of a single humanoid prefab.
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
                : "No avatar prefab has been chosen yet.";
            EditorUtility.DisplayDialog("VTuber My Avatar",
                why + "\n\nSelect your avatar prefab in the Project window, then run " +
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
            // Instantiate the prefab with its VRChat/VRCFury components INTACT. The avatar's
            // clothing, ears and tail are attached by VRCFury (ArmatureLink + Toggles),
            // and VRCFury applies that when you press Play - its play-mode processor bakes
            // the outfit onto the rig before any of our drivers' Start() runs. So we must
            // NOT bake the avatar down or strip VRCFury here: doing so leaves the body
            // working but the outfit dead. Keep the avatar exactly as authored and let
            // play-mode VRCFury do its job, the same way pressing Play worked before this
            // scene builder touched the avatar.
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

            // NOTE: We deliberately do NOT convert PhysBones or strip VRChat/VRCFury here.
            // In the editor (Play mode) PhysBones simulate natively and VRCFury applies the
            // clothing/ears/tail at Play time, so the avatar must keep all its components.
            // The ConvertPhysBonesToSpringBones / StripNonRuntimeComponents helpers (and the
            // BakeAvatarWithNdmf bake) are only needed for a standalone .exe build where
            // VRCFury can't run - wire them into a dedicated build step if we add that.

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

        /// <summary>
        /// Bakes the avatar (VRCFury clothing/ears/tail applied), captures its PhysBone
        /// tuning as build-safe SpringBones, strips every VRChat/VRCFury/SDK component,
        /// then saves the result as a prefab and exports it (plus all meshes / materials /
        /// textures / locked shaders it references) as a .unitypackage. Import that into a
        /// SEPARATE Unity project that has NO VRChat SDK to build a standalone .exe - the
        /// SDK blocks normal player builds, so the avatar has to leave the SDK behind.
        /// </summary>
        [MenuItem("VTuber My Avatar/Export Baked Avatar (for standalone project)", priority = 5)]
        public static void ExportBakedAvatar()
        {
            var prefab = ResolveAvatarPrefab();
            // ResolveAvatarPrefab already told the user why
            if (prefab == null) return;

            if (!EditorUtility.DisplayDialog("VTuber My Avatar",
                "This opens a fresh scene to bake & export the avatar - your current scene " +
                "will be closed, so save it first if you need it. Continue?", "Export", "Cancel"))
                return;

            // Bake in a fresh SINGLE scene (not additive): this matches Build Scene, which
            // is the setup NDMF/VRCFury's ManualProcessAvatar reliably bakes in. With extra
            // scenes loaded the bake can fail.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1) Bake exactly like VRCFury's "Build an Editor Test Copy" / a real upload:
            //    run the full VRChat preprocess chain. THIS is what merges VRCFury Armature
            //    Link clothing (which has its own armature) onto the body rig - NDMF's
            //    ManualProcessAvatar alone does not do that merge.
            var avatar = BakeAvatarViaPreprocess(prefab, scene);
            if (avatar == null)
            {
                // Don't export a raw, broken-clothing avatar - that defeats the purpose.
                EditorUtility.DisplayDialog("VTuber My Avatar",
                    "The VRChat/VRCFury bake failed, so the export was aborted (a raw export " +
                    "would have missing/un-merged clothing).\n\n" +
                    "Check the Console for a line starting '[vTuber] VRChat/VRCFury preprocess' - " +
                    "send it over and we'll fix it.", "OK");
                return;
            }
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 2) Capture PhysBone tuning as SpringBones now, while the SDK is present
            //    (that data won't exist in the clean project).
            ConvertPhysBonesToSpringBones(avatar);

            // 3) Strip all VRChat/VRCFury/SDK components so a non-SDK project can open it.
            StripNonRuntimeComponents(avatar);

            // 3b) Drop the baked FX/toggle controller (the standalone drives the avatar
            //     itself; the controller also lives in VRCFury's temp dir) and PERSIST the
            //     baked meshes. VRCFury writes merged meshes into a temp package folder it
            //     DELETES on entering Play - that's why they showed as "Missing (Mesh)".
            //     Copy them into a permanent folder and re-point the renderers.
            foreach (var anim in avatar.GetComponentsInChildren<Animator>(true))
                anim.runtimeAnimatorController = null;
            PersistGeneratedMeshes(avatar, "Assets/Export/Avatar_Baked_Meshes");

            // 4) Save as a prefab and export it with every dependency.
            Directory.CreateDirectory("Assets/Export");
            const string prefabOut = "Assets/Export/Avatar_Baked.prefab";
            PrefabUtility.SaveAsPrefabAsset(avatar, prefabOut);

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string pkgOut = Path.Combine(projectRoot, "Avatar_Baked.unitypackage");
            AssetDatabase.ExportPackage(prefabOut, pkgOut,
                ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Recurse);

            Debug.Log("[vTuber] Exported baked avatar package: " + pkgOut);
            EditorUtility.DisplayDialog("VTuber My Avatar",
                "Baked avatar exported to:\n" + pkgOut + "\n\n" +
                "In your clean (no-SDK) project, import this package, plus the vTuber " +
                "Scripts folder (KEEP the .meta files) and the Poiyomi shaders.", "OK");
        }

        /// <summary>
        /// Bakes the avatar by running the full VRChat preprocess pipeline on a disconnected
        /// clone - identical to VRCFury's "Build an Editor Test Copy" and to a real upload
        /// (both call VRCBuildPipelineCallbacks.OnPreprocessAvatar). This is what actually
        /// merges VRCFury Armature Link clothing (clothes that ship with their own armature)
        /// onto the body rig; NDMF's ManualProcessAvatar alone does NOT perform that merge.
        /// Returns the baked clone, or null if the VRChat SDK is absent or preprocess fails.
        /// </summary>
        private static GameObject BakeAvatarViaPreprocess(GameObject prefab, Scene scene)
        {
            // Call VRCFury's OWN "Build an Editor Test Copy" (RunBuildTestCopy), which runs
            // VRCFPrefabFixer + clones the connected instance + the full VRChat preprocess.
            // Reimplementing it (unpack + OnPreprocessAvatar) drops Armature Link clothing,
            // so we drive VRCFury's exact, proven path instead.
            var menuType = FindType("VF.Menu.VRCFuryTestCopyMenuItem");
            var runMethod = menuType?.GetMethod("RunBuildTestCopy", BindingFlags.Public | BindingFlags.Static);
            if (runMethod == null)
            {
                Debug.LogWarning("[vTuber] VRCFury test-copy builder not found - run this in the SDK " +
                                 "project with VRCFury installed.");
                return null;
            }

            // Put a connected avatar instance in the scene and select it - VRCFury's builder
            // operates on the selected avatar (do NOT unpack; it relies on the prefab links).
            var src = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            src.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var prevSelection = Selection.activeGameObject;
            Selection.activeGameObject = src;

            GameObject baked = null;
            try
            {
                // builds "VRCF Test Copy for <name>" and selects it
                runMethod.Invoke(null, null);
                var sel = Selection.activeGameObject;
                if (sel != null && sel != src && sel.name.StartsWith("VRCF Test Copy"))
                    baked = sel;
                else
                {
                    string cloneName = "VRCF Test Copy for " + src.name;
                    foreach (var root in scene.GetRootGameObjects())
                        if (root.name == cloneName) { baked = root; break; }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[vTuber] VRCFury test-copy bake threw: " +
                                 (e.InnerException?.Message ?? e.Message));
            }
            finally
            {
                Selection.activeGameObject = prevSelection;
                if (src != null) UnityEngine.Object.DestroyImmediate(src);
            }

            if (baked == null)
            {
                Debug.LogWarning("[vTuber] VRCFury test-copy produced no baked avatar.");
                return null;
            }

            baked.name = "Avatar (Baked)";
            Debug.Log("[vTuber] Baked avatar via VRCFury 'Build an Editor Test Copy' - clothing merged onto the rig.");
            return baked;
        }

        /// <summary>
        /// Copies every mesh that lives outside Assets/ (VRCFury's merged meshes are written
        /// to a temp package dir it deletes on Play) into a permanent folder and re-points the
        /// renderers, so the baked avatar keeps its meshes through Play and through export.
        /// Meshes already under Assets/ (e.g. the body FBX) are left alone.
        /// </summary>
        private static void PersistGeneratedMeshes(GameObject avatar, string folder)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Export"))
                AssetDatabase.CreateFolder("Assets", "Export");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Export", Path.GetFileName(folder));

            int n = 0;
            var seen = new System.Collections.Generic.Dictionary<Mesh, Mesh>();

            Mesh Persist(Mesh mesh, string ownerName)
            {
                if (mesh == null) return null;
                if (seen.TryGetValue(mesh, out var cached)) return cached;
                var path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/")) { seen[mesh] = mesh; return mesh; }
                var copy = UnityEngine.Object.Instantiate(mesh);
                copy.name = mesh.name;
                string outPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SanitizeFileName(ownerName) + ".asset");
                AssetDatabase.CreateAsset(copy, outPath);
                seen[mesh] = copy;
                n++;
                return copy;
            }

            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.sharedMesh = Persist(smr.sharedMesh, smr.gameObject.name);
            foreach (var mf in avatar.GetComponentsInChildren<MeshFilter>(true))
                mf.sharedMesh = Persist(mf.sharedMesh, mf.gameObject.name);

            AssetDatabase.SaveAssets();
            Debug.Log($"[vTuber] Persisted {n} baked mesh(es) into {folder} (they survive Play/export now).");
        }

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "mesh" : s;
        }

        /// <summary>
        /// Runs the full NDMF build pipeline (which is how VRCFury and Modular Avatar
        /// apply themselves) on a clone of the prefab, via reflection so this compiles with
        /// or without those packages. The returned object is a plain, fully-baked avatar:
        /// ArmatureLinks resolved, outfit toggles baked, VRCFury components consumed and
        /// gone. Returns null if NDMF isn't installed or the bake throws.
        /// </summary>
        private static GameObject BakeAvatarWithNdmf(GameObject prefab, Scene scene)
        {
            var procType = FindType("nadena.dev.ndmf.AvatarProcessor");
            var method = procType?.GetMethod("ManualProcessAvatar",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null) return null;

            // Instantiate a source instance in the scene (mirrors the "Manual bake avatar"
            // menu, which operates on a scene object). NDMF clones THIS and bakes the clone.
            var src = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            GameObject baked = null;
            try
            {
                // Signature: ManualProcessAvatar(GameObject obj, INDMFPlatformProvider platform = null)
                var args = method.GetParameters().Length >= 2
                    ? new object[] { src, null }
                    : new object[] { src };
                baked = method.Invoke(null, args) as GameObject;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[vTuber] NDMF/VRCFury bake threw: " +
                                 (e.InnerException?.Message ?? e.Message));
            }

            // The un-baked source was only NDMF's input; drop it and keep the baked clone.
            if (src != null) UnityEngine.Object.DestroyImmediate(src);
            if (baked == null) return null;

            SceneManager.MoveGameObjectToScene(baked, scene);
            baked.name = "Avatar (Baked)";
            Debug.Log("[vTuber] Baked avatar through NDMF/VRCFury - outfit, ears/tail and all " +
                      "ArmatureLink pieces are now merged onto the body rig.");
            return baked;
        }

        [MenuItem("VTuber My Avatar/Restore VRChat DLL Settings (fix editor compile)", priority = 10)]
        public static void RestoreVRChatDllSettings()
        {
            // Undo the earlier (mistaken) editor-only change: restore VRChat's intended
            // split so the duplicate types (ApiFile etc.) don't both load in the editor.
            //   *-Editor.dll      -> editor only
            //   *-Standalone.dll  -> standalone only (NOT editor)
            //   everything else   -> Any Platform (editor + standalone), the SDK default
            var standalones = new[]
            {
                BuildTarget.StandaloneWindows64, BuildTarget.StandaloneWindows,
                BuildTarget.StandaloneLinux64, BuildTarget.StandaloneOSX,
            };
            int n = 0;
            foreach (var imp in PluginImporter.GetAllImporters())
            {
                string path = imp?.assetPath?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".dll")) continue;
                string lower = path.ToLowerInvariant();
                if (!(lower.Contains("com.vrchat") || lower.Contains("vrcsdk") || lower.Contains("vrccore")))
                    continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(lower);
                if (file.EndsWith("-editor"))
                {
                    imp.SetCompatibleWithAnyPlatform(false);
                    imp.SetCompatibleWithEditor(true);
                    foreach (var bt in standalones) imp.SetCompatibleWithPlatform(bt, false);
                }
                else if (file.EndsWith("-standalone"))
                {
                    imp.SetCompatibleWithAnyPlatform(false);
                    imp.SetCompatibleWithEditor(false);
                    foreach (var bt in standalones) imp.SetCompatibleWithPlatform(bt, true);
                }
                else
                {
                    imp.SetCompatibleWithAnyPlatform(true);
                    imp.SetCompatibleWithEditor(true);
                }
                imp.SaveAndReimport();
                n++;
            }
            Debug.Log($"[vTuber] Restored platform settings on {n} VRChat DLL(s).");
            EditorUtility.DisplayDialog("VTuber My Avatar",
                $"Restored {n} VRChat DLL(s) to their editor/standalone split. Your editor should " +
                "compile again. If it doesn't, reinstall the VRChat packages via the Creator " +
                "Companion (VCC) to fully reset them.", "OK");
        }

        [MenuItem("VTuber My Avatar/Convert PhysBones to Spring Bones (current scene)", priority = 20)]
        public static void ConvertPhysBonesMenu()
        {
            int total = 0;
            foreach (var rootGo in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                total += ConvertPhysBonesToSpringBones(rootGo);
            EditorUtility.DisplayDialog("VTuber My Avatar",
                total > 0
                    ? $"Converted {total} PhysBone chain(s) to build-safe SpringBones and disabled the originals."
                    : "No active PhysBones found to convert.", "OK");
        }

        /// <summary>
        /// Finds every (enabled) VRChat PhysBone under <paramref name="avatar"/>, adds a
        /// build-safe <see cref="SpringBone"/> driving the same chain, and disables the
        /// PhysBone so the two don't fight. Done via reflection so this compiles whether
        /// or not the VRChat SDK is present. Returns how many were converted.
        /// </summary>
        private static int ConvertPhysBonesToSpringBones(GameObject avatar)
        {
            var pbType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            // SDK not present; nothing to convert
            if (pbType == null) return 0;

            var rootField = pbType.GetField("rootTransform");
            int count = 0;
            foreach (var comp in avatar.GetComponentsInChildren(pbType, true))
            {
                Transform chainRoot = (rootField?.GetValue(comp) as Transform) ?? comp.transform;
                if (chainRoot == null) continue;

                // One SpringBone per chain root (get-or-create so re-running re-imports).
                var sb = chainRoot.GetComponent<SpringBone>();
                if (sb == null) sb = chainRoot.gameObject.AddComponent<SpringBone>();
                sb.root = chainRoot;

                // Import the PhysBone's tuning so each chain feels like it did in VRChat.
                float pull = ReadFloat(comp, "pull", 0.2f);
                float spring = ReadFloat(comp, "spring", 0f);
                float stiff = ReadFloat(comp, "stiffness", 0.2f);
                float pbGrav = ReadFloat(comp, "gravity", 0f);

                sb.baseElasticity = sb.elasticity = Mathf.Clamp01(pull);
                sb.baseStiffness = sb.stiffness = Mathf.Clamp01(stiff);
                // more spring = bouncier
                sb.baseDamping = sb.damping = Mathf.Lerp(0.3f, 0.02f, Mathf.Clamp01(spring));
                sb.baseGravity = sb.gravity = Mathf.Clamp01(pbGrav) * 9.81f;

                var behaviour = comp as Behaviour;
                // PhysBone is inert in a build anyway
                if (behaviour != null) behaviour.enabled = false;
                count++;
            }

            Debug.Log(count > 0
                ? $"[vTuber] Converted {count} PhysBone chain(s) to SpringBones with imported tuning (originals disabled)."
                : "[vTuber] No PhysBones found to convert.");
            return count;
        }

        /// <summary>
        /// Removes every component on the avatar that isn't a Unity built-in or one of our
        /// own (Assembly-CSharp) scripts - i.e. all VRChat SDK / VRCFury / Modular Avatar /
        /// NDMF components. Those reference editor-only assemblies (e.g. VRCCore-Editor.dll)
        /// that make a standalone player build fail, and none are needed at runtime since we
        /// drive the rig ourselves. Multiple passes handle RequireComponent dependencies.
        /// The prefab asset is untouched (we unpacked the scene instance first).
        /// </summary>
        private static void StripNonRuntimeComponents(GameObject avatar)
        {
            int removed = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                var kill = new System.Collections.Generic.List<Component>();
                foreach (var c in avatar.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform) continue;
                    string asm = c.GetType().Assembly.GetName().Name;
                    bool keep = asm == "Assembly-CSharp" || asm.StartsWith("Unity") ||
                                asm.StartsWith("System") || asm == "mscorlib" || asm == "netstandard";
                    if (!keep) kill.Add(c);
                }
                if (kill.Count == 0) break;
                foreach (var c in kill)
                {
                    try { UnityEngine.Object.DestroyImmediate(c, true); removed++; }
                    catch { /* blocked by a RequireComponent dependency; a later pass gets it */ }
                }
            }

            // Remove leftover missing-script slots too (they can also break builds).
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            if (removed > 0)
                Debug.Log($"[vTuber] Stripped {removed} VRChat/VRCFury/etc. component(s) so the build is clean.");
        }

        private static float ReadFloat(object comp, string field, float fallback)
        {
            var f = comp.GetType().GetField(field);
            if (f != null && f.FieldType == typeof(float))
            {
                try { return (float)f.GetValue(comp); } catch { /* fall through */ }
            }
            return fallback;
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
