namespace Diamond.Sim
{
    /// <summary>
    /// Key instants of the Mixamo clips, measured with Diamond.EditorTools.AnalyzeMotions / AnalyzeFacing
    /// (30 fps, Humanoid, root rotation baked as imported). Values are estimates from hand motion and must be
    /// confirmed visually in the editor before final tuning.
    ///
    /// Orientation (model space, +z = model forward, identity root):
    ///  - Pitching clips throw along +z; place the pitcher on the mound with yaw 180 so the ball travels towards home (-z in Unity).
    ///  - The hitting clips are a right-handed stance whose chest faces +x and whose lead (left) foot strides to +z,
    ///    so a right-handed batter at Unity x&lt;0 uses yaw 0. A left-handed batter is the mirror image (flip x scale or use mirrored clips).
    /// </summary>
    public static class MotionTiming
    {
        public const string Pitch1 = "Baseball Pitching_1";
        /// <summary>Right hand is overhead and moving forward at about 1.50 s; the 1.63 s speed peak is the follow-through.</summary>
        public const double Pitch1ReleaseSeconds = 1.50;
        public const double Pitch1LengthSeconds = 3.933;

        public const string Hit = "Baseball Hit";
        /// <summary>Hands reach the hitting zone at their peak speed around 1.47 s.</summary>
        public const double HitContactSeconds = 1.47;
        public const double HitLengthSeconds = 2.900;
    }
}
