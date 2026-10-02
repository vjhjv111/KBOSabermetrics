namespace Diamond.Sim
{
    /// <summary>
    /// Key instants of the Mixamo clips, measured with Diamond.EditorTools.AnalyzeMotions / AnalyzeFacing
    /// (30 fps, Humanoid, root rotation baked as imported). Values are estimates from hand motion and must be
    /// confirmed visually in the editor before final tuning.
    ///
    /// Root rotation and position are baked into the pose at import (ConfigureMotions), so the body turn survives applyRootMotion=false.
    /// Verified world facing with the pitcher at yaw 180 on the mound and the batter at yaw 0 at x&lt;0: the pitcher starts facing the
    /// third-base side (-x), squares to home (-z) at release; the batter starts with the chest towards the plate (+x).
    ///
    /// Earlier notes on model-space orientation (before baking):
    ///  - Pitching clips throw along +z; place the pitcher on the mound with yaw 180 so the ball travels towards home (-z in Unity).
    ///  - The hitting clips are a right-handed stance whose chest faces +x and whose lead (left) foot strides to +z,
    ///    so a right-handed batter at Unity x&lt;0 uses yaw 0. A left-handed batter is the mirror image (flip x scale or use mirrored clips).
    /// </summary>
    public static class MotionTiming
    {
        public const string Pitch1 = "Baseball Pitching_1";
        /// <summary>Clip time at which the throwing hand is closest (about 0.22 m) to the game's release point (measured by CaptureDemo with baked root motion).</summary>
        public const double Pitch1ReleaseSeconds = 1.41;
        public const double Pitch1LengthSeconds = 3.933;

        public const string Hit = "Baseball Hit";
        /// <summary>Hands reach the hitting zone at their peak speed around 1.47 s.</summary>
        public const double HitContactSeconds = 1.47;
        public const double HitLengthSeconds = 2.900;
        /// <summary>
        /// The Mixamo "Baseball Hit" clip continues into a sprint to first base: hip ground speed jumps from 0.4 to 1.5 m/s
        /// after about 2.5 s (measured by AnalyzeRun). Hold the pose at the end of the follow-through instead.
        /// </summary>
        public const double HitSwingEndSeconds = 2.40;

        public const string Catcher = "Baseball Catcher";
        /// <summary>The first second of the clip is a still crouch (hips 0.38 m); after 1.0 s the catcher stands up and throws (AnalyzeCatcher).</summary>
        public const double CatcherStanceSeconds = 0.5;
        /// <summary>Clip time with the catcher partly risen (hips 0.78 m), used to reach high pitches (AnalyzeCatcher: 1.4 s).</summary>
        public const double CatcherStandSeconds = 1.45;

        public const string Miss = "Baseball Hit_almostmiss";
        /// <summary>Hand speed peaks at 1.00 s in the swing-and-miss clip (AnalyzeMotions).</summary>
        public const double MissContactSeconds = 1.00;
        /// <summary>The clip's sprint begins at about 1.7 s (AnalyzeRun); hold the pose just before it.</summary>
        public const double MissSwingEndSeconds = 1.65;
    }
}
