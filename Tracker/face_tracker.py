"""
VTuber My Avatar - webcam face tracker.

Captures the webcam, runs Google MediaPipe Face Landmarker (with blendshapes +
head transform), and streams ARKit blendshape weights + head pose to the Unity
app over localhost UDP. Designed to be lightweight enough to run alongside a
game and OBS on a single PC.

For privacy this tool never opens a camera window - it only ever emits tracking
numbers over localhost. Your webcam image never leaves this process.

Usage:
    python face_tracker.py                 # use config.json / defaults
    python face_tracker.py --camera 1      # override camera index
    python face_tracker.py --list-cameras  # probe which camera indices work

Stop with Ctrl+C.
"""

import argparse
import json
import os
import socket
import sys
import time

# Quiet MediaPipe / absl C++ logging (INFO/WARNING startup spam and the harmless
# "clearcut" telemetry-upload errors). Must be set before mediapipe is imported.
os.environ.setdefault("GLOG_minloglevel", "2")
os.environ.setdefault("GLOG_logtostderr", "0")

import numpy as np

import protocol


def silence_native_stderr():
    """
    Route C/C++ stderr (fd 2, where MediaPipe logs the 'clearcut' noise) to the
    void, while keeping Python's sys.stderr pointed at the real terminal so our
    own messages and any Python tracebacks stay visible.
    """
    try:
        sys.stderr.flush()
        saved = os.dup(2)
        devnull = os.open(os.devnull, os.O_WRONLY)
        os.dup2(devnull, 2)
        os.close(devnull)
        sys.stderr = os.fdopen(saved, "w")
    except Exception:
        pass

# MediaPipe is imported lazily inside main() so that --list-cameras and
# --help stay fast and don't fail if mediapipe isn't installed yet.

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG_PATH = os.path.join(HERE, "config.json")
DEFAULT_MODEL_PATH = os.path.join(HERE, "models", "face_landmarker.task")

DEFAULTS = {
    "camera_index": 0,
    "capture_width": 640,
    "capture_height": 480,
    "target_fps": 30,
    "udp_host": "127.0.0.1",
    "udp_port": 39539,
    "model_path": DEFAULT_MODEL_PATH,
    "num_faces": 1,
    "min_face_detection_confidence": 0.5,
    "min_face_presence_confidence": 0.5,
    "min_tracking_confidence": 0.5,
}


def load_config(path):
    cfg = dict(DEFAULTS)
    if path and os.path.isfile(path):
        try:
            with open(path, "r", encoding="utf-8") as f:
                cfg.update(json.load(f))
            print(f"[tracker] loaded config: {path}")
        except Exception as e:
            print(f"[tracker] WARNING failed to read {path}: {e}")
    return cfg


def parse_args():
    p = argparse.ArgumentParser(description="VTuber My Avatar webcam face tracker")
    p.add_argument("--config", default=DEFAULT_CONFIG_PATH)
    p.add_argument("--camera", type=int, help="camera index override")
    p.add_argument("--port", type=int, help="UDP port override")
    p.add_argument("--fps", type=int, help="target FPS override")
    p.add_argument("--model", help="path to face_landmarker.task override")
    p.add_argument("--list-cameras", action="store_true",
                   help="probe camera indices 0..5 and exit")
    p.add_argument("--verbose", action="store_true",
                   help="keep MediaPipe native logs (don't silence the clearcut noise)")
    p.add_argument("--debug-head", action="store_true",
                   help="print head pose angles + rotation matrix for diagnosis")
    return p.parse_args()


def list_cameras():
    import cv2
    print("[tracker] probing camera indices 0..5 ...")
    found = []
    for i in range(6):
        cap = cv2.VideoCapture(i, cv2.CAP_DSHOW if os.name == "nt" else 0)
        ok = cap.isOpened()
        if ok:
            ret, _ = cap.read()
            ok = ret
        cap.release()
        print(f"  index {i}: {'OK' if ok else 'unavailable'}")
        if ok:
            found.append(i)
    if found:
        print(f"[tracker] usable camera indices: {found}")
    else:
        print("[tracker] no cameras detected. Check it isn't in use by another app.")


def matrix_to_euler_deg(m):
    """
    Extract intuitive head Euler angles (pitch, yaw, roll) in degrees from the
    MediaPipe facial transformation matrix (4x4, row-major numpy array).

      pitch (X) -> nodding up/down
      yaw   (Y) -> turning left/right
      roll  (Z) -> head tilt

    Axis mapping was verified empirically with --debug-head: a pure "look up" is a
    clean X-axis rotation (lands in r1/r2), turning flips r[2,0]/r[0,2] (Y axis),
    and tilt shows in r[1,0]/r[0,0] (Z axis). The Unity side re-centres and lets you
    flip any axis (invertPitch/invertYaw/invertRoll), so only the axis assignment
    needs to be right here.
    """
    r = m[:3, :3]
    sy = max(-1.0, min(1.0, -r[2, 0]))
    pitch = np.degrees(np.arctan2(r[2, 1], r[2, 2]))
    yaw = np.degrees(np.arcsin(sy))
    roll = np.degrees(np.arctan2(r[1, 0], r[0, 0]))
    return float(pitch), float(yaw), float(roll)


def main():
    args = parse_args()

    if args.list_cameras:
        list_cameras()
        return 0

    cfg = load_config(args.config)
    if args.camera is not None:
        cfg["camera_index"] = args.camera
    if args.port is not None:
        cfg["udp_port"] = args.port
    if args.fps is not None:
        cfg["target_fps"] = args.fps
    if args.model is not None:
        cfg["model_path"] = args.model
    if args.verbose:
        cfg["verbose"] = True
    if args.debug_head:
        cfg["debug_head"] = True

    model_path = cfg["model_path"]
    if not os.path.isfile(model_path):
        print(f"[tracker] ERROR model not found: {model_path}")
        print("[tracker] Run setup_tracker.bat once to download it, or set "
              "model_path in config.json.")
        return 2

    # Heavy imports after the cheap exits above.
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision as mp_vision

    # Build the index map: ARKit canonical index -> name, for fast packing.
    shape_names = protocol.ARKIT_SHAPES

    base_options = mp_python.BaseOptions(model_asset_path=model_path)
    options = mp_vision.FaceLandmarkerOptions(
        base_options=base_options,
        running_mode=mp_vision.RunningMode.VIDEO,
        num_faces=cfg["num_faces"],
        min_face_detection_confidence=cfg["min_face_detection_confidence"],
        min_face_presence_confidence=cfg["min_face_presence_confidence"],
        min_tracking_confidence=cfg["min_tracking_confidence"],
        output_face_blendshapes=True,
        output_facial_transformation_matrixes=True,
    )

    cam_index = cfg["camera_index"]
    backend = cv2.CAP_DSHOW if os.name == "nt" else 0
    cap = cv2.VideoCapture(cam_index, backend)
    if not cap.isOpened():
        print(f"[tracker] ERROR could not open camera index {cam_index}.")
        print("[tracker] Try: python face_tracker.py --list-cameras")
        return 3
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, cfg["capture_width"])
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, cfg["capture_height"])
    cap.set(cv2.CAP_PROP_FPS, cfg["target_fps"])

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (cfg["udp_host"], int(cfg["udp_port"]))

    target_fps = max(5, int(cfg["target_fps"]))
    frame_interval = 1.0 / target_fps

    print(f"[tracker] camera={cam_index} {cfg['capture_width']}x{cfg['capture_height']} "
          f"@~{target_fps}fps -> udp {dest[0]}:{dest[1]}")
    print("[tracker] streaming. Press Ctrl+C to stop.")

    # Setup succeeded; mute the native C++ telemetry/log noise unless --verbose.
    if not cfg.get("verbose", False):
        silence_native_stderr()

    frame_idx = 0
    zero_shapes = [0.0] * protocol.NUM_SHAPES
    last_log = time.time()
    sent_since_log = 0
    last_head_log = 0.0
    debug_head = cfg.get("debug_head", False)

    with mp_vision.FaceLandmarker.create_from_options(options) as landmarker:
        try:
            while True:
                # monotonic(): MediaPipe requires non-decreasing frame timestamps, and a
                # wall-clock step (NTP/DST) must never make the pacing math go negative.
                loop_start = time.monotonic()
                ok, frame = cap.read()
                if not ok:
                    # Camera hiccup: send an "invalid" frame so Unity can idle.
                    sock.sendto(
                        protocol.pack(frame_idx, False, zero_shapes,
                                      (0, 0, 0), (0, 0, 0)),
                        dest,
                    )
                    frame_idx += 1
                    # Pace the retry to the target FPS instead of busy-looping, so a
                    # disconnected camera doesn't flood ~100 idle packets/sec.
                    spent = time.monotonic() - loop_start
                    if spent < frame_interval:
                        time.sleep(frame_interval - spent)
                    continue

                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                ts_ms = int(loop_start * 1000)
                result = landmarker.detect_for_video(mp_image, ts_ms)

                valid = bool(result.face_blendshapes)
                if valid:
                    # Map MediaPipe's named scores into canonical ARKit order.
                    scores = {c.category_name: c.score
                              for c in result.face_blendshapes[0]}
                    shapes = [float(scores.get(name, 0.0)) for name in shape_names]

                    if result.facial_transformation_matrixes:
                        m = np.array(result.facial_transformation_matrixes[0])
                        head_euler = matrix_to_euler_deg(m)
                        head_pos = (float(m[0, 3]), float(m[1, 3]), float(m[2, 3]))
                    else:
                        head_euler = (0.0, 0.0, 0.0)
                        head_pos = (0.0, 0.0, 0.0)
                else:
                    shapes = zero_shapes
                    head_euler = (0.0, 0.0, 0.0)
                    head_pos = (0.0, 0.0, 0.0)

                sock.sendto(protocol.pack(frame_idx, valid, shapes,
                                          head_euler, head_pos), dest)
                frame_idx += 1
                sent_since_log += 1

                if debug_head and valid and result.facial_transformation_matrixes:
                    now_h = time.time()
                    if now_h - last_head_log > 0.35:
                        rr = np.array(result.facial_transformation_matrixes[0])[:3, :3]
                        print(f"[head] P={head_euler[0]:+6.1f} Y={head_euler[1]:+6.1f} "
                              f"R={head_euler[2]:+6.1f}  | "
                              f"r0=[{rr[0,0]:+.2f} {rr[0,1]:+.2f} {rr[0,2]:+.2f}] "
                              f"r1=[{rr[1,0]:+.2f} {rr[1,1]:+.2f} {rr[1,2]:+.2f}] "
                              f"r2=[{rr[2,0]:+.2f} {rr[2,1]:+.2f} {rr[2,2]:+.2f}]")
                        last_head_log = now_h

                now = time.time()
                if now - last_log >= 5.0:
                    fps = sent_since_log / (now - last_log)
                    print(f"[tracker] {fps:5.1f} fps  "
                          f"{'face ok' if valid else 'searching for face'}")
                    last_log = now
                    sent_since_log = 0

                # Pace the loop to the target FPS (don't burn a whole core).
                spent = time.monotonic() - loop_start
                if spent < frame_interval:
                    time.sleep(frame_interval - spent)
        except KeyboardInterrupt:
            print("\n[tracker] stopping (Ctrl+C).")
        finally:
            cap.release()
            sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
