# VTuber My Avatar

Drive **your own VRChat avatar** from a webcam and send a **transparent feed straight into OBS**.
No VR headset, no running VRChat, no VRM conversion, no third-party vTuber app, just your real
avatar with its real blendshapes, rendered as-is. It's a straightforward setup, and it gets you
streaming as a VTuber.

It has two pieces:

| Piece | What it is | Where |
|-------|------------|-------|
| **Tracker** | A lightweight Python program. Reads your webcam, runs Google MediaPipe Face Landmarker, and streams ARKit blendshapes + head pose over localhost UDP. | [`Tracker/`](Tracker/) |
| **Unity app** | A small Unity project that loads your avatar prefab, applies the tracking to its blendshapes/bones, and sends a transparent picture to OBS via Spout2. | [`Unity/Assets/vTuber/`](Unity/Assets/vTuber/) |

```
 webcam --> Python tracker --> (UDP 39539) --> Unity app --> Spout2 --> OBS
                                                    ^
                                                your mic   (jaw / lip-sync)
```

Why does this work on almost any avatar? The drivers are avatar-agnostic. The app
discovers your avatar's blendshapes at runtime, and MediaPipe's ARKit-52 shapes map onto a
**Unified-Expressions** blendshape set almost 1:1. Most modern VRChat avatars already carry
that full UE set (EyeClosed/Wide/Squint, BrowInnerUp, LipFunnel/Pucker/Suck, Jaw*, visemes,
eye bones, and so on), so they're already rigged for this. You just point the kit at
your prefab.

---

## Bring your own avatar

**This kit ships no avatar, outfit, or shader.** It's only the tracker and the driver code. You
supply your **own** VRChat avatar (the one you already own and use). Avatar, outfit and shader
licences almost always forbid redistribution, so keep your model in *your* copy of the
project. Don't share the built `.exe` either, since it contains your avatar.

**What your avatar needs for the full experience:**
- A **humanoid** rig (so head/neck/eye bones can be driven).
- **Unified-Expressions** (or ARKit) blendshapes for full face tracking. Fewer shapes still
  work, you just get less of the face. The app logs `mapped N/52 ARKit shapes` on start and
  lists anything it couldn't map, so you can see exactly what your avatar supports.

---

## What you get

- Full face tracking from a normal webcam: brows, cheeks, eyes (blink, squint and
  wide), jaw, mouth, lips, nose, tongue.
- Head pose (turn, tilt, nod) on the head and neck, with a subtle chest lean.
- Eye gaze on the eye bones.
- Mic lip-sync blended with the webcam mouth, so talking looks snappier.
- Idle life: breathing, gentle sway, and auto-blink when tracking is lost.
- Transparent Spout2 output with true alpha, so you can drop it straight onto your scene.
- Tuned to run alongside a game and OBS on one PC (FPS cap, no vsync, single light,
  no shadows or post).

---

## What you need (install once)

The kit is just the Python tracker plus the C# driver scripts. You bring your own avatar
(see above) and the dependencies below, since those are third-party. You install them
yourself, once:

| Dependency | Where to get it |
|------------|-----------------|
| **Unity 2022.3.22f1** (Built-in RP) | Unity Hub |
| **VRChat SDK3 (Avatars)** | VRChat Creator Companion (VCC) |
| **VRCFury** | VCC *(optional, only if your avatar uses it)* |
| **Poiyomi** / your avatar's shaders | *optional, only if your avatar uses it* |
| **KlakSpout** | added in Package Manager via git URL during setup (needs [Git for Windows](https://git-scm.com/download/win)) |
| **Spout2 Plugin for OBS** | https://github.com/Off-World-Live/obs-spout2-plugin |

Before you do anything, **make a copy of your VRChat avatar's Unity project and work in the
copy**, never your live one. Setup wires scenes and components into the project, so if a step
goes wrong you've still got your original safe. Everything below assumes you're in that copy.
The [One-time setup](#one-time-setup) walks you through the rest.

---

## The two Unity projects

You work in two separate Unity projects, for two different jobs. Neither one *is* this repo,
the repo just holds the kit files that go into each (a copy under `Unity/` for the first,
a copy under `UnityStandalone/` for the second).

- **Authoring project** (author and test). This is a **copy of your own VRChat avatar project**,
  opened with the VRChat SDK (plus VRCFury and NDMF if your avatar uses them). Your avatar prefab
  lives here intact and you test it in editor Play mode. You drop the repo's
  **`Unity/Assets/vTuber/`** folder into it. Unity 2022.3.22f1, Built-in RP.
- **Standalone build project** (build the `.exe`). This is a **fresh, empty Unity project with
  no VRChat SDK**, which you create yourself. The SDK's build hooks block a normal player build,
  so the avatar is baked out of the authoring project and imported here, alongside the repo's
  **`UnityStandalone/Assets/vTuber/`** folder. See [Build the .exe](#build-the-standalone-exe-recommended).

> **Why the `.exe`, not editor Play?** The build is what you actually stream with: it honours
> the FPS cap (the editor ignores it), runs leaner beside your game + OBS, and launches on its
> own without opening Unity. Editor Play mode is for quick testing while you tune the avatar.

---

## One-time setup

### 1. Tracker (Python)
1. Install **Python 3.10-3.12** (tick *Add python.exe to PATH* during install).
2. Double-click **`Tracker/setup_tracker.bat`**. It makes a local virtual env, installs
   MediaPipe/OpenCV/NumPy, and downloads the face model. (A few minutes the first time.)

### 2. Authoring project (a copy of your avatar project)
1. **Make a copy of your VRChat avatar's Unity project**, then open the copy in **Unity
   2022.3.22f1** with the **VRChat SDK** (via VCC), plus **VRCFury** if your avatar uses it.
   Work in this copy, not your live project.
2. Make sure your avatar exists as a **prefab** in `Assets/` (drag it from the Hierarchy into a
   Project folder if it isn't already one).
3. Copy the repo's **`Unity/Assets/vTuber/`** folder into the project's `Assets/` (keep the
   `.meta` files alongside it).
4. Add **KlakSpout**: *Window > Package Manager > + > Add package from git URL*, then paste
   `https://github.com/keijiro/KlakSpout.git?path=Packages/jp.keijiro.klak.spout`. *(Git-URL
   packages need [Git for Windows](https://git-scm.com/download/win). If it won't fetch, see
   Troubleshooting.)*
5. **Pick your avatar:** select its prefab in the Project window, then run
   **`VTuber My Avatar > Select Avatar Prefab...`** (top menu bar). *(If your project has
   exactly one humanoid prefab, the kit auto-detects it and you can skip this.)*
6. Run **`VTuber My Avatar > Build Scene`**. This creates and wires everything: your avatar, a
   transparent output camera, the Spout sender, a key light, an operator preview, and the
   full driver rig. It saves the scene to `Assets/vTuber/Scenes/VTuberMyAvatar.unity` and sets it
   as the build scene.
7. Press **Play** for a quick test inside the editor (handy while tuning the avatar).
8. **Build the `.exe` you'll stream with**: follow
   [Build the standalone .exe](#build-the-standalone-exe-recommended). This is the recommended
   way to run: it honours the FPS cap, runs leaner, and launches without Unity.

### 3. OBS
1. Install the **Spout2 Plugin for OBS** (https://github.com/Off-World-Live/obs-spout2-plugin).
2. In OBS: **+ > Spout2 Capture >** select the sender **`VTuberMyAvatar`** (the default; rename
   it via `render.spoutName` in the config if you like). It arrives with transparency
   already, no chroma key needed.

---

## Running it (every stream)

1. Start the tracker: **`Tracker/start_tracker.bat`**
   (first time, check your camera: `start_tracker.bat --list-cameras`, then set
   `camera_index` in `Tracker/config.json`).
2. Launch your **VTuber My Avatar `.exe`** (in `Build/`). (Just testing? Press **Play** in
   `Unity/` instead.)
3. OBS already shows your avatar via the Spout2 source.
4. Sit facing the camera and press **R** once to re-centre your neutral head pose.

That's it. Close the tracker window (or Ctrl+C) when you're done.

### Hotkeys (in the app window)
| Key | Action |
|-----|--------|
| **R** | Recentre head pose (look straight, then press) |
| **M** | Toggle mirror (avatar reflects you like a mirror) |
| **L** | Cycle mic lip-sync mode (Blend -> Off -> FillWhenNoWebcam) |
| **T** | Toggle face tracking on/off |
| **B** | Calibrate blink range: press with eyes **open**, then again with eyes **closed** (do it with glasses on if you wear them) |
| **F1** | Show/hide the status overlay (operator-only; never in the OBS feed) |
| **F2** | Show/hide the in-app settings panel (live sliders for everything below, no F5 needed) |
| **F5** | Reload `vtuber_config.json` from disk |
| **1-5** | Recall camera preset (instant zoom/framing change, see **Camera presets** below) |
| **Shift+1-5** | Save the current camera framing into that preset slot |

---

## Tuning

The easy way is **in the app**: press **F2** to open the settings panel and drag the sliders
while watching the avatar update live. You don't have to touch any files, and it covers almost
everything below.

Your settings are saved to a **`vtuber_config.json`** file (created on first run, next to the
built `.exe`, or in the project folder when running in the editor: `Unity/` or `UnityStandalone/`,
whichever you pressed Play in). Editing that file by hand is just an optional alternative to the
F2 panel: change a value and press **F5** in the app to reload it. The list below names each
config key so you can find its matching F2 slider (or edit the file directly if you prefer).

> **Note: editor and exe don't share a config.** Tuning sliders, mic calibration, or camera
> presets while testing in an editor's Play mode writes to *that project's*
> `vtuber_config.json`, not the built `.exe`'s. Carry settings over by hand (redo them in
> the exe) or copy the file across, otherwise the exe quietly falls back to defaults for
> anything you only tuned in the editor.

> Note: if you do hand-edit, the file is plain JSON, no comments allowed. Pasting in `//` or
> `/* */` comments makes it fail to parse, and it resets to defaults.

- Head turns the wrong way? Flip `head.invertYaw` (and/or `invertPitch`/`invertRoll`).
- Mic source: pick your input live in **F2 > Mic > Device** (it lists every microphone, with
  a Refresh button), or set `mic.device` in the config to the exact device name. Leave it `""`
  to use the system default. Lip-sync silent? It's usually the wrong input here.
- Framing (live, press F5): `render.cameraDistance` = zoom (bigger = further/smaller),
  `cameraFov` = lens, `pivotY` = vertical centre, `panX`/`panY` = slide the model around
  the transparent canvas (+X right, +Y up), `cameraRoll` = rotate/tilt the rendered image
  around its centre (degrees, e.g. for a Dutch-angle effect). Output is a 16:9
  transparent canvas by default. The model sits in it with room to spare, so you can
  also just scale and move it in OBS.
- Camera presets: `render.presets` is a fixed list of 5 named framings (`fov`,
  `distance`, `height`, `pivotY`, `panX`, `panY`, `roll`), recalled live with the **1-5**
  hotkeys and blended over `presetBlendSeconds` (0 = instant cut). They ship as a
  distance-only zoom ladder around the default framing. Open **F2 > Render**, dial in
  a shot you like with the camera sliders there (including **Camera roll** if you want a
  tilt), then **Shift+1-5** (or the **F2 > Presets** tab's "Save current" button) to bank
  it. Handy for a quick on-stream punch-in without leaving the game. One catch: dragging the
  camera GameObject directly in the Scene view does *not* get captured by Save, because the
  preset system reads the Render-tab values, not the camera's live transform, so always go
  through those sliders.
- Output size: change `render.width`/`height`, then **re-run *Build Scene*** (resolution
  is applied there, not via F5).
- Spout source name: `render.spoutName` (default `VTuberMyAvatar`) is the name OBS's Spout2
  Capture source picks. Change it, re-run *Build Scene*, and re-select it in OBS.
- Arms stuck up in a T/A-pose? Turn on `idle.restArms` (**F2 > Idle > Rest arms**) and
  drag **Rest arms angle** while watching the avatar; both apply live, with no relaunch needed.
  It rotates each upper arm down from whatever bind pose the avatar imported with, so the
  right angle depends on how raised the arms start (a full T-pose needs close to 90 degrees; an
  A-pose needs less). Back the angle off if the arms start folding into the body.
- Performance: lower `render.targetFps` (e.g. 30) and the tracker's `target_fps` in
  `Tracker/config.json` to free up CPU/GPU for your game.

---

## Performance notes (one-PC streaming)

- App caps FPS, disables vsync, uses one directional light, and runs no realtime shadows or post.
- The tracker is paced to its `target_fps` (default 30) and won't peg a core.
- Want it lighter? Drop both FPS caps to 30, lower the render texture (e.g. 720x1280),
  and reduce the webcam capture resolution in `Tracker/config.json`.
- Resolution isn't the main lever. GPU cost tracks the pixels your avatar *covers*, so
  the frame-rate cap (which only takes effect in a build) and lighter shading matter more.

---

## Build the standalone .exe (recommended)

This is the recommended way to run for streaming: the build honours the FPS cap (the editor
ignores it), runs leaner beside your game + OBS, and launches on its own without opening Unity.
Editor Play mode is fine for a quick test, but the `.exe` is what you'll actually go live with.

Because the VRChat SDK blocks normal player builds, the avatar is **baked** out of the authoring
project and rebuilt in a clean one that you set up once.

**First time only, create the build project:**
1. In **Unity Hub**, create a **new, empty 3D (Built-in RP) project** on **Unity 2022.3.22f1**,
   with **no VRChat SDK** installed. This is your standalone build project.
2. Copy the repo's **`UnityStandalone/Assets/vTuber/`** folder into its `Assets/` (keep the
   `.meta` files), and add **KlakSpout** the same way as in setup (*Package Manager > + > git URL*:
   `https://github.com/keijiro/KlakSpout.git?path=Packages/jp.keijiro.klak.spout`).
3. Import **your avatar's shaders** (e.g. Poiyomi) so the avatar renders correctly.

**Each time you bake and build:**
1. In the **authoring project**, run **`VTuber My Avatar > Export Baked Avatar (for standalone
   project)`**. It runs the real VRChat/VRCFury bake on a *disposable copy* (merging VRCFury
   ArmatureLink outfits and applying Toggle state), converts PhysBones to build-safe SpringBones,
   strips the SDK/VRCFury components, persists the generated meshes, and writes
   `Avatar_Baked.unitypackage`.
   - VRCFury Toggles bake at their default state, so set any outfit pieces "Default On"
     before exporting, or they bake hidden.
2. In the **build project**, import that package into `Assets/Export/`.
3. Run **`VTuber My Avatar > Build Scene`** there (it auto-finds `Assets/Export/Avatar_Baked.prefab`),
   then **File > Build Settings > Build** a standalone Windows player.

---

## Limitations & notes

- Hair and clothing physics: VRChat **PhysBones** only simulate in the Unity editor (and
  the VRChat client), not in a standalone build. In the editor (pressing Play in
  `Unity/`) PhysBones simulate natively, so there's nothing to do. For the standalone `.exe`,
  **Export Baked Avatar** converts every PhysBone chain to a build-safe
  [SpringBone](Unity/Assets/vTuber/Scripts/Avatar/SpringBone.cs), importing each chain's
  PhysBone tuning (pull/spring/stiffness/gravity) so the feel matches VRChat. You can also
  preview that build-safe feel inside the editor by running
  **VTuber My Avatar > Convert PhysBones to Spring Bones (current scene)** by hand (re-run it
  after changing the VRChat tuning to re-import). Under `"spring"` in the config,
  `useImported` keeps that per-chain feel and the `*Mul` values scale all chains at once
  (F5 to apply); set `useImported:false` to force one flat feel instead. Collisions
  aren't simulated, so hair can clip slightly, which is fine for a front-facing cam.
- Webcam vs iPhone: a webcam is good; an iPhone (ARKit) would be studio-grade and
  maps 1:1 to UE shapes. The tracker is isolated behind the UDP protocol, so an ARKit
  source can be swapped in later without touching the Unity side.
- No hand or finger tracking. Front-facing webcams can't see hands reliably, so it's face,
  head, gaze and lean only, by design.
- VRCFury and Modular Avatar features apply at Play time. If your avatar's outfit/ears/tail
  are attached by VRCFury **ArmatureLink** and its clothes are VRCFury **Toggles**,
  *Build Scene* instantiates the prefab intact (no NDMF bake, no stripping, no
  PhysBone conversion), because VRCFury's own play-mode processor merges ArmatureLink
  outfits and applies Toggle state the moment you press Play, before any of our drivers
  start running. So whatever state a piece is left in (its VRCFury Toggle default, or
  however you've set it in the Hierarchy) is what shows up; turn a piece on before pressing
  Play if it's hidden. The standalone `.exe` is different: baking only happens in
  **Export Baked Avatar**, which runs the real VRCFury/VRChat bake on a disposable copy so
  the result can leave the SDK behind.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `tracker OFFLINE` in overlay | Tracker not running, or wrong port. Start `start_tracker.bat`; check `udpPort` matches `udp_port`. |
| "no face" forever | Wrong camera. `start_tracker.bat --list-cameras`, set `camera_index`. Improve lighting on your face. |
| Camera won't open | Another app is using the webcam (close Discord/Zoom/VRChat camera). |
| Build Scene can't find an avatar | Select your avatar prefab in the Project window and run **VTuber My Avatar > Select Avatar Prefab...** (it remembers the choice per project). |
| Head/eyes mirrored wrong | Press **M**, or set `mirror` / `head.invert*` in config. |
| Mouth too twitchy / too stiff | Adjust `expressionSmoothing`; for mic, tune `mic.noiseFloor`/`loudAt`. |
| Mic lip-sync not reacting | Wrong input device. Pick your mic in **F2 > Mic > Device** (or set `mic.device`); confirm the Console shows `MicLipSync using device: ...`. |
| OBS source is black/opaque | Use the **Spout2** source (not a window/virtual-cam). Confirm the sender name matches `render.spoutName` (default `VTuberMyAvatar`). |
| KlakSpout won't install | Install Git for Windows, or in Package Manager *+ > Add package from git URL*: `https://github.com/keijiro/KlakSpout.git?path=Packages/jp.keijiro.klak.spout`. Then re-run *Build Scene*. |
| Some shapes don't move | The app logs `mapped N/52 ARKit shapes` and any unmapped names on start (Console / `Logs`). If your avatar names a shape differently, extend the mapping table in [`ArkitAvatarMap.cs`](Unity/Assets/vTuber/Scripts/Avatar/ArkitAvatarMap.cs). |
| Tracker spams `Failed to send to clearcut` | Harmless MediaPipe telemetry (it's *failing* to upload usage stats, nothing is sent). Silenced by default; if you ever need the native logs back, run `start_tracker.bat --verbose`. |
| No Spout source in OBS | Re-run **VTuber My Avatar > Build Scene** (the SpoutSender + its resources are set up there), then **press Play / launch the build**: Spout senders only broadcast while the app is running. Confirm the console shows `Added & configured SpoutSender`. |
| Trails/ghosting in the Unity Game view | Cosmetic to the operator preview only (the Spout/OBS feed is clean). The `PreviewClearCamera` added by *Build Scene* clears the preview framebuffer each frame. Re-run *Build Scene* if you don't have it. |
| Eyes show white at big gaze | Eye bones over-rotating. Lower `gaze.maxYaw`/`maxPitch`, raise `gaze.deadzone`, or set `gaze.enable=false`, then F5. |
| Arms stuck out in a T/A-pose | Turn on `idle.restArms` and dial in `restArmsAngle` (**F2 > Idle**), live, no relaunch. |
| Arms folded/clipping into the body | `restArmsAngle` is too high for this avatar's bind pose, lower it (or turn `idle.restArms` off) live in **F2 > Idle**. |
| Eyes don't fully close (esp. with glasses) | Press **B** with eyes open, then **B** with eyes closed (glasses on) to calibrate. Watch the overlay's "Eye-close: now" value while closing your eyes, if it can't get above ~0.4 through the glasses, webcam blink isn't usable and auto-blink is the better option. |
