using System.Collections.Generic;

namespace VTuberMyAvatar
{
    /// <summary>
    /// One mapping rule: drive the first existing of <see cref="Candidates"/> from
    /// ARKit shape <see cref="ArkIndex"/>, scaled by <see cref="Mul"/>.
    /// A single ARKit shape may produce several entries (e.g. a centre ARKit shape
    /// driving split Left/Right Unified-Expressions shapes).
    /// </summary>
    public struct ShapeMapEntry
    {
        public int ArkIndex;
        public string[] Candidates;
        public float Mul;

        public ShapeMapEntry(ArkShape s, float mul, params string[] candidates)
        {
            ArkIndex = (int)s;
            Mul = mul;
            Candidates = candidates;
        }
    }

    /// <summary>
    /// ARKit-52 -> avatar blendshape mapping for Unified-Expressions style models
    /// (which the avatar uses: EyeClosed*, EyeWide*, BrowInnerUp*, LipFunnel*, etc.).
    /// Candidate lists fall back to plain ARKit names so this also works on
    /// straight ARKit/perfect-sync meshes.
    ///
    /// Eye-gaze shapes (eyeLook*) are intentionally NOT included here; gaze is
    /// owned by <see cref="EyeGazeDriver"/> (eye bones, or these shapes as fallback)
    /// to avoid double-driving.
    /// </summary>
    public static class ArkitAvatarMap
    {
        public static List<ShapeMapEntry> Build()
        {
            var m = new List<ShapeMapEntry>(64);

            // --- Brows ---
            m.Add(new ShapeMapEntry(ArkShape.BrowDownLeft, 1f, "BrowDownLeft", "browDown_L"));
            m.Add(new ShapeMapEntry(ArkShape.BrowDownRight, 1f, "BrowDownRight", "browDown_R"));
            // ARKit browInnerUp is a single centre shape; UE splits it L/R.
            m.Add(new ShapeMapEntry(ArkShape.BrowInnerUp, 1f, "BrowInnerUpLeft", "browInnerUp_L"));
            m.Add(new ShapeMapEntry(ArkShape.BrowInnerUp, 1f, "BrowInnerUpRight", "browInnerUp_R"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.BrowInnerUp, 1f, "BrowInnerUp"));
            m.Add(new ShapeMapEntry(ArkShape.BrowOuterUpLeft, 1f, "BrowOuterUpLeft"));
            m.Add(new ShapeMapEntry(ArkShape.BrowOuterUpRight, 1f, "BrowOuterUpRight"));

            // --- Cheeks ---
            m.Add(new ShapeMapEntry(ArkShape.CheekPuff, 1f, "CheekPuffLeft"));
            m.Add(new ShapeMapEntry(ArkShape.CheekPuff, 1f, "CheekPuffRight"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.CheekPuff, 1f, "CheekPuff"));
            m.Add(new ShapeMapEntry(ArkShape.CheekSquintLeft, 1f, "CheekSquintLeft"));
            m.Add(new ShapeMapEntry(ArkShape.CheekSquintRight, 1f, "CheekSquintRight"));

            // --- Eyes (blink / squint / wide; gaze handled elsewhere) ---
            m.Add(new ShapeMapEntry(ArkShape.EyeBlinkLeft, 1f, "EyeClosedLeft", "eyeBlinkLeft", "Blink_L"));
            m.Add(new ShapeMapEntry(ArkShape.EyeBlinkRight, 1f, "EyeClosedRight", "eyeBlinkRight", "Blink_R"));
            m.Add(new ShapeMapEntry(ArkShape.EyeSquintLeft, 1f, "EyeSquintLeft"));
            m.Add(new ShapeMapEntry(ArkShape.EyeSquintRight, 1f, "EyeSquintRight"));
            m.Add(new ShapeMapEntry(ArkShape.EyeWideLeft, 1f, "EyeWideLeft"));
            m.Add(new ShapeMapEntry(ArkShape.EyeWideRight, 1f, "EyeWideRight"));

            // --- Jaw ---
            m.Add(new ShapeMapEntry(ArkShape.JawForward, 1f, "JawForward"));
            m.Add(new ShapeMapEntry(ArkShape.JawLeft, 1f, "JawLeft"));
            m.Add(new ShapeMapEntry(ArkShape.JawRight, 1f, "JawRight"));
            m.Add(new ShapeMapEntry(ArkShape.JawOpen, 1f, "JawOpen", "jawOpen"));

            // --- Mouth ---
            m.Add(new ShapeMapEntry(ArkShape.MouthClose, 1f, "MouthClosed", "mouthClose"));
            m.Add(new ShapeMapEntry(ArkShape.MouthDimpleLeft, 1f, "MouthDimpleLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthDimpleRight, 1f, "MouthDimpleRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthFrownLeft, 1f, "MouthFrownLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthFrownRight, 1f, "MouthFrownRight"));
            // ARKit mouthFunnel is single; UE splits into 4 lip-funnel shapes.
            m.Add(new ShapeMapEntry(ArkShape.MouthFunnel, 1f, "LipFunnelUpperLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthFunnel, 1f, "LipFunnelUpperRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthFunnel, 1f, "LipFunnelLowerLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthFunnel, 1f, "LipFunnelLowerRight"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.MouthFunnel, 1f, "MouthFunnel"));
            m.Add(new ShapeMapEntry(ArkShape.MouthLeft, 1f, "MouthLeft", "MouthUpperLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthRight, 1f, "MouthRight", "MouthUpperRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthLowerDownLeft, 1f, "MouthLowerDownLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthLowerDownRight, 1f, "MouthLowerDownRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthPressLeft, 1f, "MouthPressLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthPressRight, 1f, "MouthPressRight"));
            // ARKit mouthPucker single; UE LipPucker split L/R.
            m.Add(new ShapeMapEntry(ArkShape.MouthPucker, 1f, "LipPuckerLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthPucker, 1f, "LipPuckerRight"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.MouthPucker, 1f, "MouthPucker"));
            m.Add(new ShapeMapEntry(ArkShape.MouthRollLower, 1f, "LipSuckLowerLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthRollLower, 1f, "LipSuckLowerRight"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.MouthRollLower, 1f, "MouthRollLower"));
            m.Add(new ShapeMapEntry(ArkShape.MouthRollUpper, 1f, "LipSuckUpperLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthRollUpper, 1f, "LipSuckUpperRight"));
            // Fallback for avatars that have one combined shape instead of the split L/R pair above.
            m.Add(new ShapeMapEntry(ArkShape.MouthRollUpper, 1f, "MouthRollUpper"));
            m.Add(new ShapeMapEntry(ArkShape.MouthShrugLower, 1f, "MouthShrugLower", "MouthRaiserLower"));
            m.Add(new ShapeMapEntry(ArkShape.MouthShrugUpper, 1f, "MouthShrugUpper", "MouthRaiserUpper"));
            m.Add(new ShapeMapEntry(ArkShape.MouthSmileLeft, 1f, "MouthSmileLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthSmileRight, 1f, "MouthSmileRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthStretchLeft, 1f, "MouthStretchLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthStretchRight, 1f, "MouthStretchRight"));
            m.Add(new ShapeMapEntry(ArkShape.MouthUpperUpLeft, 1f, "MouthUpperUpLeft"));
            m.Add(new ShapeMapEntry(ArkShape.MouthUpperUpRight, 1f, "MouthUpperUpRight"));

            // --- Nose ---
            m.Add(new ShapeMapEntry(ArkShape.NoseSneerLeft, 1f, "NoseSneerLeft"));
            m.Add(new ShapeMapEntry(ArkShape.NoseSneerRight, 1f, "NoseSneerRight"));

            // --- Tongue ---
            m.Add(new ShapeMapEntry(ArkShape.TongueOut, 1f, "TongueOut", "tongueOut"));

            return m;
        }
    }

    /// <summary>
    /// Maps each ARKit shape index to its left/right mirror counterpart, so the
    /// rendered avatar can act like a mirror of the streamer when enabled.
    /// </summary>
    public static class ArkitMirror
    {
        private static int[] _table;

        public static int Counterpart(int index)
        {
            if (_table == null) Build();
            return _table[index];
        }

        private static void Build()
        {
            var names = TrackingProtocol.ShapeNames;
            _table = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                string swapped = null;
                if (n.EndsWith("Left")) swapped = n.Substring(0, n.Length - 4) + "Right";
                else if (n.EndsWith("Right")) swapped = n.Substring(0, n.Length - 5) + "Left";

                _table[i] = i;
                if (swapped != null)
                {
                    int j = TrackingProtocol.IndexOf(swapped);
                    if (j >= 0) _table[i] = j;
                }
            }
        }
    }
}
