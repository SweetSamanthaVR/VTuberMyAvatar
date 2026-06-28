using System.Collections.Generic;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Mirror of Tracker/protocol.py. Keep these two files in sync.
    /// Defines the canonical ARKit-52 blendshape order and the UDP packet layout
    /// the Python tracker sends.
    /// </summary>
    public static class TrackingProtocol
    {
        // "VTRK" little-endian as a uint32 (V=0x56, T=0x54, R=0x52, K=0x4B).
        public const uint Magic = 0x4B525456;
        public const uint Version = 1;
        public const uint FlagFaceValid = 1u << 0;

        public const int NumShapes = 52;

        // header = 4 (magic) + 4 (version) + 4 (frame) + 4 (flags) = 16
        // body   = 52 floats + 3 (head euler) + 3 (head pos) = 58 floats
        public const int HeaderBytes = 16;
        // Total size of one tracking packet, in bytes (248).
        public const int PacketBytes = HeaderBytes + (NumShapes + 6) * 4;

        /// <summary>
        /// Canonical ARKit blendshape names, in the exact order they are packed.
        /// Index into <see cref="ArkShape"/> matches this array.
        /// </summary>
        public static readonly string[] ShapeNames =
        {
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
        };

        private static Dictionary<string, int> _index;

        /// <summary>Index of an ARKit shape name, or -1 if unknown.</summary>
        public static int IndexOf(string arkitName)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, int>(NumShapes);
                for (int i = 0; i < ShapeNames.Length; i++)
                    _index[ShapeNames[i]] = i;
            }
            return _index.TryGetValue(arkitName, out var idx) ? idx : -1;
        }
    }

    /// <summary>Strongly-typed indices for shapes referenced directly in code.</summary>
    public enum ArkShape
    {
        BrowDownLeft = 0, BrowDownRight = 1, BrowInnerUp = 2, BrowOuterUpLeft = 3, BrowOuterUpRight = 4,
        CheekPuff = 5, CheekSquintLeft = 6, CheekSquintRight = 7,
        EyeBlinkLeft = 8, EyeBlinkRight = 9,
        EyeLookDownLeft = 10, EyeLookDownRight = 11, EyeLookInLeft = 12, EyeLookInRight = 13,
        EyeLookOutLeft = 14, EyeLookOutRight = 15, EyeLookUpLeft = 16, EyeLookUpRight = 17,
        EyeSquintLeft = 18, EyeSquintRight = 19, EyeWideLeft = 20, EyeWideRight = 21,
        JawForward = 22, JawLeft = 23, JawOpen = 24, JawRight = 25,
        MouthClose = 26, MouthDimpleLeft = 27, MouthDimpleRight = 28, MouthFrownLeft = 29, MouthFrownRight = 30,
        MouthFunnel = 31, MouthLeft = 32, MouthLowerDownLeft = 33, MouthLowerDownRight = 34,
        MouthPressLeft = 35, MouthPressRight = 36, MouthPucker = 37, MouthRight = 38,
        MouthRollLower = 39, MouthRollUpper = 40, MouthShrugLower = 41, MouthShrugUpper = 42,
        MouthSmileLeft = 43, MouthSmileRight = 44, MouthStretchLeft = 45, MouthStretchRight = 46,
        MouthUpperUpLeft = 47, MouthUpperUpRight = 48,
        NoseSneerLeft = 49, NoseSneerRight = 50,
        TongueOut = 51,
    }
}
