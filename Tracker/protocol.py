"""
Shared wire-protocol definition for the VTuber My Avatar face tracker.

The tracker (this Python process) captures the webcam, runs MediaPipe Face
Landmarker, and streams one packet per processed frame over localhost UDP to the
Unity app. Keep this file in sync with the C# parser:
    Unity/Assets/vTuber/Scripts/FaceTracking/TrackingProtocol.cs

Packet layout (little-endian, fixed size):
    char[4]   magic     = "VTRK"
    uint32    version   = PROTOCOL_VERSION
    uint32    frame     monotonically increasing frame counter
    uint32    flags     bit0 = face is currently tracked (valid)
    float32[52]         ARKit blendshape weights, order == ARKIT_SHAPES, 0..1
    float32[3]          head rotation as Euler degrees: pitch (X), yaw (Y), roll (Z)
    float32[3]          head position offset: x, y, z (roughly metres, camera space)

Total size = 16 (header) + 58 * 4 = 248 bytes.

Head pose is sent as intuitive Euler degrees rather than a raw matrix so the
Unity side can tune per-axis sign/scale and mirror without coordinate-system
guesswork. The Unity side also re-centres (captures a neutral offset) at runtime.
"""

import struct

MAGIC = b"VTRK"
PROTOCOL_VERSION = 1

FLAG_FACE_VALID = 1 << 0

# Canonical Apple ARKit 52 blendshape order. MediaPipe Face Landmarker emits
# these same names (its index 0 "_neutral" is dropped). We always re-map by
# NAME on the tracker side so we are independent of MediaPipe's internal order
# and version drift, then serialise in exactly this order.
ARKIT_SHAPES = [
    "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
    "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
    "eyeBlinkLeft", "eyeBlinkRight",
    "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight",
    "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight",
    "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight",
    "jawForward", "jawLeft", "jawOpen", "jawRight",
    "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft", "mouthFrownRight",
    "mouthFunnel", "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight",
    "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight",
    "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
    "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
    "mouthUpperUpLeft", "mouthUpperUpRight",
    "noseSneerLeft", "noseSneerRight",
    "tongueOut",
]
# Number of ARKit blendshapes carried per packet (52).
NUM_SHAPES = len(ARKIT_SHAPES)

# "<" little-endian; 4s magic; 3 uint32 header; 52 + 3 + 3 floats.
PACKET_FORMAT = "<4sIII" + f"{NUM_SHAPES}f" + "3f" + "3f"
# Total size of one serialised tracking packet, in bytes (248).
PACKET_SIZE = struct.calcsize(PACKET_FORMAT)


def pack(frame: int, valid: bool, shapes, head_euler, head_pos) -> bytes:
    """Serialise one tracking frame. `shapes` is a length-52 sequence."""
    flags = FLAG_FACE_VALID if valid else 0
    return struct.pack(
        PACKET_FORMAT,
        MAGIC, PROTOCOL_VERSION, frame & 0xFFFFFFFF, flags,
        *shapes,
        head_euler[0], head_euler[1], head_euler[2],
        head_pos[0], head_pos[1], head_pos[2],
    )


if __name__ == "__main__":
    # Sanity check when run directly.
    assert NUM_SHAPES == 52, NUM_SHAPES
    assert PACKET_SIZE == 248, PACKET_SIZE
    print(f"OK  shapes={NUM_SHAPES}  packet={PACKET_SIZE} bytes  fmt={PACKET_FORMAT}")
