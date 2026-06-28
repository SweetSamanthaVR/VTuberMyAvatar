namespace VTuberMyAvatar
{
    /// <summary>
    /// One decoded tracking frame. Plain data, no Unity types other than what's
    /// needed, so it can be copied between the network thread and the main thread.
    /// </summary>
    public struct TrackingFrame
    {
        public uint Frame;
        public bool FaceValid;

        /// <summary>ARKit-52 blendshape weights (0..1), order == TrackingProtocol.ShapeNames.</summary>
        public float[] Shapes;

        // Head pose, Euler degrees (pitch X, yaw Y, roll Z) as sent by the tracker.
        public float HeadPitch;
        public float HeadYaw;
        public float HeadRoll;

        // Head translation in camera space (rough metres), usable for body lean.
        public float HeadPosX;
        public float HeadPosY;
        public float HeadPosZ;

        public static TrackingFrame CreateEmpty()
        {
            return new TrackingFrame
            {
                Frame = 0,
                FaceValid = false,
                Shapes = new float[TrackingProtocol.NumShapes],
            };
        }

        public float Shape(ArkShape s) => Shapes[(int)s];
    }
}
