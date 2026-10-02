using UnityEngine;

namespace Diamond.Stadium
{
    /// <summary>
    /// Broadcast-style camera. During the pitch it holds a fixed view behind home plate (the view the mouse aim is mapped
    /// on). When the ball is hit it glides back and up, tracks the ball and zooms in with distance, then returns to the pitch view
    /// when the next pitch begins.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GameCamera : MonoBehaviour
    {
        [SerializeField] PitchReplayDemo demo;
        [Header("Pitch view (behind home plate)")]
        [SerializeField] Vector3 pitchPosition = new Vector3(0f, 1.9f, -6.6f);
        [SerializeField] Vector3 pitchLookAt = new Vector3(0f, 1.3f, 12f);
        [SerializeField] float pitchFov = 45f;
        [Header("Pitcher view (behind the mound, when the user pitches)")]
        [SerializeField] Vector3 pitcherViewPosition = new Vector3(0f, 3.4f, 24.5f);
        [SerializeField] Vector3 pitcherViewLookAt = new Vector3(0f, 1.0f, 0f);
        [SerializeField] float pitcherViewFov = 30f;
        [Header("Ball tracking view")]
        [SerializeField] Vector3 followPosition = new Vector3(0f, 3.8f, -10.5f);
        [SerializeField] float frameWidthMetres = 14f;    // height of the field window kept around the ball
        [SerializeField] float minFov = 20f;
        [SerializeField] float maxFov = 50f;
        [SerializeField] float positionTime = 0.45f;
        [SerializeField] float lookTime = 0.12f;
        [SerializeField] float fovTime = 0.25f;

        Camera _cam;
        Vector3 _posVelocity, _lookVelocity;
        float _fovVelocity;
        Vector3 _lookPoint, _prevBall, _ballVelocity;
        bool _following;
        const float BallBaseScale = 0.074f;
        bool _init, _pitcherView;

        /// <summary>Selects the view used before contact: behind home plate (batting) or behind the mound (pitching).</summary>
        public void SetPitcherView(bool pitcher) => _pitcherView = pitcher;

        void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Advances the camera by <paramref name="dt"/> seconds (also called by capture tools in edit mode).</summary>
        public void Tick(float dt)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (demo == null || demo.Ball == null) return;
            if (!_init)
            {
                transform.position = pitchPosition;
                _lookPoint = pitchLookAt;
                _cam.fieldOfView = pitchFov;
                _init = true;
            }

            Vector3 wantPos, wantLook;
            float wantFov;
            if (demo.BattedSince != null)
            {
                var ball = demo.Ball.position;
                wantPos = followPosition;
                // Lead the ball by the smoothing lag so a fast, distant ball stays inside a narrow field of view.
                var ballVelocity = dt > 1e-5f ? (ball - _prevBall) / dt : Vector3.zero;
                _ballVelocity = Vector3.Lerp(_ballVelocity, ballVelocity, 0.35f);
                wantLook = ball + _ballVelocity * lookTime;
                _following = true;
                var dist = Mathf.Max(3f, Vector3.Distance(followPosition, ball));
                wantFov = Mathf.Clamp(2f * Mathf.Rad2Deg * Mathf.Atan(frameWidthMetres * 0.5f / dist), minFov, maxFov);
                // Keep the ball in the upper part of the frame so the field stays visible, and enlarge it with distance.
                var windowHeight = 2f * dist * Mathf.Tan(wantFov * 0.5f * Mathf.Deg2Rad);
                wantLook -= Vector3.up * (0.22f * windowHeight);
                demo.Ball.localScale = Vector3.one * (BallBaseScale * Mathf.Clamp(dist / 12f, 1f, 5f));
            }
            else
            {
                wantPos = _pitcherView ? pitcherViewPosition : pitchPosition;
                wantLook = _pitcherView ? pitcherViewLookAt : pitchLookAt;
                wantFov = _pitcherView ? pitcherViewFov : pitchFov;
                if (_following) { demo.Ball.localScale = Vector3.one * BallBaseScale; _following = false; }
            }

            if (demo.Ball != null) _prevBall = demo.Ball.position;
            transform.position = Vector3.SmoothDamp(transform.position, wantPos, ref _posVelocity, positionTime, Mathf.Infinity, dt);
            _lookPoint = Vector3.SmoothDamp(_lookPoint, wantLook, ref _lookVelocity, lookTime, Mathf.Infinity, dt);
            var forward = _lookPoint - transform.position;
            if (forward.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, wantFov, ref _fovVelocity, fovTime, Mathf.Infinity, dt);
        }
    }
}
