using UnityEngine;
using Diamond.Model;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Replays one fixed pitch and its batted ball with the ported trajectory math, so the field and
    /// coordinate conversion can be checked by eye in Play mode. Press Space to replay.
    /// </summary>
    public sealed class PitchReplayDemo : MonoBehaviour
    {
        [SerializeField] float slowMotion = 1f;
        Transform _ball;
        TrailRenderer _trail;
        Pitch _pitch;
        PitchResult _result;
        float _start;

        void Start()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            Destroy(ball.GetComponent<Collider>());
            ball.transform.localScale = Vector3.one * 0.074f;
            var material = Resources.Load<Material>("Diamond/Ball");
            if (material != null) ball.GetComponent<MeshRenderer>().sharedMaterial = material;
            _ball = ball.transform;
            _trail = ball.AddComponent<TrailRenderer>();
            _trail.time = 2.5f;
            _trail.widthMultiplier = 0.05f;
            _trail.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.white };
            Restart();
        }

        void Restart()
        {
            _pitch = new Pitch
            {
                id = 1, type = "slider", velocity = 140, releaseAt = 1000, flightMs = 1700,
                releaseX = -0.33, releaseY = 1.84, releaseZ = -18.44 + 0.12,
                target = new Vec2 { x = 0.4, y = -0.3 }, breakX = 0.42, breakY = 0.18,
            };
            _result = new PitchResult
            {
                trajectory = "fly", exitSpeed = 150, launchAngle = 28, direction = 0.3,
                contact = new Contact { at = 2700, position = new Position3 { x = 0.2, y = 1.2, z = 0 } },
            };
            _start = Time.time;
            _trail.Clear();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Restart();
            var ms = (Time.time - _start) / Mathf.Max(0.05f, slowMotion) * 1000.0;
            var position = ms >= _result.contact.at ? BallFlight.Batted(_result, ms) : BallFlight.Pitched(_pitch, ms);
            _ball.position = Field.ToUnity(position);
        }
    }
}
