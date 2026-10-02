using System;
using System.Collections.Generic;
using UnityEngine;
using Diamond.Sim;

namespace Diamond.Stadium
{
    /// <summary>
    /// Mesh helpers and the outfield perimeter profile ported from lib/stadium-world.ts (radialPoint, floor, ribbon).
    /// All meshes are built in Unity axes; web coordinates are converted with Field.ToUnity.
    /// </summary>
    public static class FieldGeometry
    {
        /// <summary>Outfield wall distance profile around home plate. angle 0 = centre field, +/-pi/4 = foul poles.</summary>
        public static Vector3 RadialPoint(double angle, double offset = 0)
        {
            var a = Math.Abs(Math.Atan2(Math.Sin(angle), Math.Cos(angle)));
            var radius = a <= Math.PI / 4
                ? 122 - 23 * Math.Pow(a / (Math.PI / 4), 1.6)
                : 99 * Math.Exp(-0.65 * (a - Math.PI / 4));
            return Field.ToUnity(Math.Sin(angle) * (radius + offset), 0, -Math.Cos(angle) * (radius + offset));
        }

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public int Vertex(Vector3 p, float metresPerUv)
            {
                V.Add(p);
                UV.Add(new Vector2(p.x / metresPerUv, p.z / metresPerUv));
                return V.Count - 1;
            }

            /// <summary>Adds a triangle whose normal faces up (or along <paramref name="facing"/> when given).</summary>
            public void Tri(int a, int b, int c, Vector3? facing = null)
            {
                var n = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                var want = facing ?? Vector3.up;
                if (Vector3.Dot(n, want) < 0) { var t = b; b = c; c = t; }
                T.Add(a); T.Add(b); T.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(V);
                mesh.SetUVs(0, UV);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>Flat polygon facing up, triangulated as a fan from its centroid (valid for star-shaped outlines).</summary>
        public static Mesh Polygon(IList<Vector3> points, float y, float metresPerUv, string name)
        {
            var b = new Builder();
            var centre = Vector3.zero;
            foreach (var p in points) centre += p;
            centre /= points.Count;
            var c = b.Vertex(new Vector3(centre.x, y, centre.z), metresPerUv);
            var ring = new int[points.Count];
            for (var i = 0; i < points.Count; i++) ring[i] = b.Vertex(new Vector3(points[i].x, y, points[i].z), metresPerUv);
            for (var i = 0; i < points.Count; i++) b.Tri(c, ring[i], ring[(i + 1) % points.Count]);
            return b.ToMesh(name);
        }

        public static Mesh Disc(Vector3 centre, float radius, float y, int segments, float metresPerUv, string name)
        {
            var pts = new List<Vector3>();
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2;
                pts.Add(new Vector3(centre.x + Mathf.Sin(a) * radius, y, centre.z + Mathf.Cos(a) * radius));
            }
            return Polygon(pts, y, metresPerUv, name);
        }

        /// <summary>Pitching mound: a 10-inch cone-like rise, flattened at the rubber (mirrors the web mesh).</summary>
        public static Mesh Mound(Vector3 centre, float radius, float peak, float edge, int segments, float metresPerUv)
        {
            var b = new Builder();
            var c = b.Vertex(new Vector3(centre.x, centre.y + peak, centre.z), metresPerUv);
            var ring = new int[segments];
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2;
                ring[i] = b.Vertex(new Vector3(centre.x + Mathf.Sin(a) * radius, centre.y + edge, centre.z + Mathf.Cos(a) * radius), metresPerUv);
            }
            for (var i = 0; i < segments; i++) b.Tri(c, ring[i], ring[(i + 1) % segments]);
            return b.ToMesh("Pitching mound");
        }

        /// <summary>Strip between two polylines of equal length at a fixed height (warning track, foul lines).</summary>
        public static Mesh Ribbon(IList<Vector3> inner, IList<Vector3> outer, float y, float metresPerUv, string name)
        {
            var b = new Builder();
            var i0 = new int[inner.Count];
            var o0 = new int[outer.Count];
            for (var i = 0; i < inner.Count; i++)
            {
                i0[i] = b.Vertex(new Vector3(inner[i].x, y, inner[i].z), metresPerUv);
                o0[i] = b.Vertex(new Vector3(outer[i].x, y, outer[i].z), metresPerUv);
            }
            for (var i = 0; i < inner.Count - 1; i++)
            {
                b.Tri(i0[i], o0[i], i0[i + 1]);
                b.Tri(o0[i], o0[i + 1], i0[i + 1]);
            }
            return b.ToMesh(name);
        }

        /// <summary>Vertical wall along a polyline: both faces plus a top cap, so it renders with back-face culling on.</summary>
        public static Mesh Wall(IList<Vector3> line, float height, float thickness, string name)
        {
            var b = new Builder();
            var half = thickness / 2;
            for (var i = 0; i < line.Count - 1; i++)
            {
                var p = line[i]; var q = line[i + 1];
                var dir = (q - p).normalized;
                var side = new Vector3(-dir.z, 0, dir.x) * half;
                // Four vertical corners of the segment: bottom/top of near and far faces.
                var pn = b.Vertex(p + side, 4); var pf = b.Vertex(p - side, 4);
                var qn = b.Vertex(q + side, 4); var qf = b.Vertex(q - side, 4);
                var pnt = b.Vertex(p + side + Vector3.up * height, 4); var pft = b.Vertex(p - side + Vector3.up * height, 4);
                var qnt = b.Vertex(q + side + Vector3.up * height, 4); var qft = b.Vertex(q - side + Vector3.up * height, 4);
                b.Tri(pn, qn, pnt, side); b.Tri(qn, qnt, pnt, side);
                b.Tri(pf, qf, pft, -side); b.Tri(qf, qft, pft, -side);
                b.Tri(pnt, qnt, pft, Vector3.up); b.Tri(qnt, qft, pft, Vector3.up);
            }
            return b.ToMesh(name);
        }

        /// <summary>Full outfield perimeter (including behind home) sampled at <paramref name="segments"/> points, optionally offset inward.</summary>
        public static List<Vector3> Perimeter(int segments, double offset, double startAngle, double endAngle)
        {
            var list = new List<Vector3>();
            for (var i = 0; i <= segments; i++)
            {
                var a = startAngle + i / (double)segments * (endAngle - startAngle);
                list.Add(RadialPoint(a, offset));
            }
            return list;
        }
    }
}
