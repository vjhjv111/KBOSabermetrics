using System;
using System.Collections.Generic;
using UnityEngine;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Builds the playing field procedurally from the same measurements as the web game
    /// (turf, infield clay, mound, bases, foul lines/poles, warning track, padded wall).
    /// Seating, crowd, dugouts and skyline are not ported yet.
    /// Materials are loaded from Resources/Diamond (created by Diamond > Create field materials).
    /// </summary>
    public sealed class StadiumBuilder : MonoBehaviour
    {
        [SerializeField] bool buildOnStart = true;
        Transform _root;

        public Transform Root => _root;

        void Start()
        {
            if (buildOnStart) Build();
        }

        static Material Mat(string name)
        {
            var m = Resources.Load<Material>("Diamond/" + name);
            if (m == null) Debug.LogWarning($"Missing material Resources/Diamond/{name}. Run Diamond > Create field materials.");
            return m;
        }

        [ContextMenu("Rebuild")]
        public void Build()
        {
            if (_root != null) { if (Application.isPlaying) Destroy(_root.gameObject); else DestroyImmediate(_root.gameObject); }
            _root = new GameObject("Regulation baseball field").transform;
            _root.SetParent(transform, false);

            var turf = Mat("Turf"); var clay = Mat("Clay"); var chalk = Mat("Chalk");
            var track = Mat("WarningTrack"); var padding = Mat("WallPadding"); var gold = Mat("Gold");

            // Ground: one large turf plane under everything, centred behind the mound like the web scene.
            AddMesh(Quad(Field.ToUnity(0, -0.025, -45), 330, 6f, "Mown turf"), turf, "Mown natural turf");

            // Infield clay: the web outline (two arms from home + a 29 m arc around the mound).
            var outline = new List<Vector3> { Field.ToUnity(-4, 0, 4), Field.ToUnity(-29, 0, -18.44) };
            for (var i = 0; i <= 64; i++)
            {
                var a = -Math.PI / 2 + i / 64.0 * Math.PI;
                outline.Add(Field.ToUnity(Math.Sin(a) * 29, 0, -18.44 - Math.Cos(a) * 29));
            }
            outline.Add(Field.ToUnity(4, 0, 4));
            AddMesh(FieldGeometry.Polygon(outline, 0.006f, 1.3f, "Infield clay"), clay, "Infield clay");
            // The grass diamond sits just above the clay (the web mesh cuts a hole; here a thin overlay avoids needing a hole triangulation).
            var diamond = new List<Vector3>
            {
                Field.ToUnity(0, 0, -4.3), Field.ToUnity(-15.9, 0, -19.4), Field.ToUnity(0, 0, -34.5), Field.ToUnity(15.9, 0, -19.4),
            };
            AddMesh(FieldGeometry.Polygon(diamond, 0.035f, 6f, "Infield grass"), turf, "Infield grass");
            AddMesh(FieldGeometry.Disc(Field.ToUnity(0, 0, 0), 5.6f, 0.016f, 64, 1.3f, "Home plate area"), clay, "Home plate clay");

            // Pitching mound with rubber; a clay apron keeps the base dirt above the infield grass overlay.
            AddMesh(FieldGeometry.Disc(Field.ToUnity(0, 0, -Field.MoundDistance), (float)Field.MoundRadius, 0.037f, 48, 1.3f, "Mound apron"), clay, "Mound apron");
            var moundCentre = Field.ToUnity(0, 0, -Field.MoundDistance);
            AddMesh(FieldGeometry.Mound(moundCentre, (float)Field.MoundRadius, (float)Field.MoundHeight, 0.012f, 64, 1.3f), clay, "Pitching mound");
            Box("Pitcher's rubber", chalk, Field.ToUnity(0, 0.26, -Field.MoundDistance), new Vector3(0.6096f, 0.025f, 0.1524f));

            // Bases (18-inch squares rotated 45 degrees) and home plate.
            var names = new[] { "First", "Second", "Third" };
            for (var i = 0; i < 3; i++)
            {
                var b = Field.Bases[i + 1];
                var bag = Box(names[i] + " base", chalk, Field.ToUnity(b.x, 0.06, b.z), new Vector3(0.4572f, 0.075f, 0.4572f));
                bag.transform.rotation = Quaternion.Euler(0, 45, 0);
            }
            var plate = new List<Vector3>
            {
                Field.ToUnity(-.216, 0, -.216), Field.ToUnity(.216, 0, -.216), Field.ToUnity(.216, 0, 0), Field.ToUnity(0, 0, .216), Field.ToUnity(-.216, 0, 0),
            };
            AddMesh(FieldGeometry.Polygon(plate, 0.036f, 1f, "Home plate"), chalk, "Home plate");

            // Foul lines and poles.
            foreach (var side in new[] { -1, 1 })
            {
                var end = FieldGeometry.RadialPoint(side * Math.PI / 4);
                var dir = new Vector3(end.x, 0, end.z).normalized;
                var perp = new Vector3(-dir.z, 0, dir.x) * 0.0375f;
                var home = Field.ToUnity(0, 0, 0);
                AddMesh(FieldGeometry.Ribbon(new[] { home - perp, end - perp }, new[] { home + perp, end + perp }, 0.025f, 1f, "Foul line"), chalk, "Foul line");
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "Foul pole";
                Setup(pole, gold, new Vector3(end.x, 10f, end.z), new Vector3(0.22f, 10f, 0.22f));
            }

            const double Pi = Math.PI;
            var outer = FieldGeometry.Perimeter(112, 0, -Pi, Pi);
            var inner = FieldGeometry.Perimeter(112, -5, -Pi, Pi);
            AddMesh(FieldGeometry.Ribbon(inner, outer, 0.005f, 4f, "Warning track"), track, "Warning track");
            AddMesh(FieldGeometry.Wall(FieldGeometry.Perimeter(56, 0, -Pi / 4, Pi / 4), 3.25f, 0.26f, "Outfield wall"), padding, "Outfield wall (fair)");
            AddMesh(FieldGeometry.Wall(FieldGeometry.Perimeter(28, 0, -Pi, -Pi / 4), 2.55f, 0.26f, "Foul wall left"), padding, "Perimeter wall (foul left)");
            AddMesh(FieldGeometry.Wall(FieldGeometry.Perimeter(28, 0, Pi / 4, Pi), 2.55f, 0.26f, "Foul wall right"), padding, "Perimeter wall (foul right)");
        }

        // --- helpers ---------------------------------------------------------------------------------------------

        static Mesh Quad(Vector3 centre, float size, float metresPerUv, string name)
        {
            var h = size / 2;
            var m = new Mesh { name = name };
            m.SetVertices(new[]
            {
                centre + new Vector3(-h, 0, -h), centre + new Vector3(-h, 0, h), centre + new Vector3(h, 0, h), centre + new Vector3(h, 0, -h),
            });
            var uv = new List<Vector2>();
            foreach (var v in m.vertices) uv.Add(new Vector2(v.x / metresPerUv, v.z / metresPerUv));
            m.SetUVs(0, uv);
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        GameObject AddMesh(Mesh mesh, Material material, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            return go;
        }

        GameObject Box(string name, Material material, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Setup(go, material, position, size);
            return go;
        }

        void Setup(GameObject go, Material material, Vector3 position, Vector3 scale)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            go.transform.SetParent(_root, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
