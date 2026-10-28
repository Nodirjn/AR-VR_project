#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarInternals
{
    /// <summary>
    /// Procedural CAD-style geometry for the car internals. Lengths are metres in the part's own space. Primitives are
    /// placed through a matrix stack (Push / Pop), go into the current submesh (Sub), and get analytic normals: hard
    /// edges where a profile turns by more than the hard angle, smooth shading elsewhere. UVs are in metres (arc length
    /// around, profile or path length along), so one tiling value gives the same texel density everywhere.
    /// Triangle winding is chosen per triangle from the normals (Unity: clockwise seen from the front), and degenerate
    /// triangles (on an axis) are dropped.
    /// </summary>
    public sealed class MeshKit
    {
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<List<int>> submeshes = new List<List<int>>();
        private readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        private Matrix4x4 matrix = Matrix4x4.identity;
        private Matrix4x4 normalMatrix = Matrix4x4.identity;
        private int submesh;

        public MeshKit(int submeshCount = 1)
        {
            for (int i = 0; i < Mathf.Max(1, submeshCount); i++) submeshes.Add(new List<int>());
        }

        public int VertexCount => positions.Count;
        public int SubmeshCount => submeshes.Count;

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (List<int> s in submeshes) n += s.Count / 3;
                return n;
            }
        }

        /// <summary>Selects the submesh that the next primitives go into (created on demand).</summary>
        public MeshKit Sub(int index)
        {
            while (submeshes.Count <= index) submeshes.Add(new List<int>());
            submesh = index;
            return this;
        }

        public MeshKit Push(Vector3 position, Quaternion rotation) => Push(Matrix4x4.TRS(position, rotation, Vector3.one));

        public MeshKit Push(Vector3 position, Quaternion rotation, Vector3 scale) => Push(Matrix4x4.TRS(position, rotation, scale));

        public MeshKit Push(Matrix4x4 local)
        {
            stack.Push(matrix);
            matrix = matrix * local;
            normalMatrix = matrix.inverse.transpose;
            return this;
        }

        public MeshKit Pop()
        {
            matrix = stack.Count > 0 ? stack.Pop() : Matrix4x4.identity;
            normalMatrix = matrix.inverse.transpose;
            return this;
        }

        public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            positions.Add(matrix.MultiplyPoint3x4(position));
            Vector3 n = normalMatrix.MultiplyVector(normal);
            normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
            uvs.Add(uv);
            return positions.Count - 1;
        }

        /// <summary>Triangle wound to face along its vertex normals; degenerate triangles are skipped.</summary>
        public void Tri(int a, int b, int c)
        {
            Vector3 pa = positions[a], pb = positions[b], pc = positions[c];
            Vector3 face = Vector3.Cross(pb - pa, pc - pa);
            if (face.sqrMagnitude < 1e-16f) return;
            List<int> list = submeshes[submesh];
            if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) >= 0f) { list.Add(a); list.Add(b); list.Add(c); }
            else { list.Add(a); list.Add(c); list.Add(b); }
        }

        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>Appends another kit's geometry (already in its own final space) through the current matrix.</summary>
        public void Append(MeshKit other, int targetSubmeshOffset = 0)
        {
            int start = positions.Count;
            for (int i = 0; i < other.positions.Count; i++) Vertex(other.positions[i], other.normals[i], other.uvs[i]);
            for (int s = 0; s < other.submeshes.Count; s++)
            {
                Sub(s + targetSubmeshOffset);
                foreach (int index in other.submeshes[s]) submeshes[submesh].Add(start + index);
            }
        }

        // ------------------------------------------------------------------------------------------------ lathe

        /// <summary>
        /// Revolves a profile of (radius, height) points about local +Y. Order the points so that the solid lies on the
        /// left when walking along the profile with radius to the right and height up (counter-clockwise): axis bottom,
        /// outward, up the outside, back in at the top. Joints that turn by more than <paramref name="hardAngle"/> degrees
        /// get a hard edge. <paramref name="faceted"/> gives flat sides between the segments (hex nuts and heads).
        /// </summary>
        public void Lathe(IList<Vector2> profile, int segments, float hardAngle = 40f, float startDeg = 0f,
            float sweepDeg = 360f, bool faceted = false)
        {
            int count = profile.Count;
            if (count < 2) return;
            segments = Mathf.Max(3, segments);
            var segNormal = new Vector2[count - 1];
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 d = profile[i + 1] - profile[i];
                segNormal[i] = d.sqrMagnitude > 1e-14f ? new Vector2(d.y, -d.x).normalized : Vector2.up;
            }
            float cosHard = Mathf.Cos(hardAngle * Mathf.Deg2Rad);
            float rRef = 0f;
            foreach (Vector2 p in profile) rRef = Mathf.Max(rRef, p.x);
            rRef = Mathf.Max(0.005f, rRef);
            float length = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                float segLength = (p1 - p0).magnitude;
                if (segLength < 1e-7f) continue;
                Vector2 n0 = EndNormal(segNormal, i, true, cosHard);
                Vector2 n1 = EndNormal(segNormal, i, false, cosHard);
                float v0 = length, v1 = length + segLength;
                length = v1;
                if (p0.x < 1e-6f && p1.x < 1e-6f) continue;
                if (faceted)
                {
                    for (int k = 0; k < segments; k++)
                    {
                        float a0 = (startDeg + sweepDeg * k / segments) * Mathf.Deg2Rad;
                        float a1 = (startDeg + sweepDeg * (k + 1) / segments) * Mathf.Deg2Rad;
                        float am = (a0 + a1) * 0.5f;
                        Vector3 fn0 = new Vector3(n0.x * Mathf.Cos(am), n0.y, n0.x * Mathf.Sin(am));
                        Vector3 fn1 = new Vector3(n1.x * Mathf.Cos(am), n1.y, n1.x * Mathf.Sin(am));
                        int i00 = Vertex(Ring(p0, a0), fn0, new Vector2(a0 * rRef, v0));
                        int i01 = Vertex(Ring(p0, a1), fn0, new Vector2(a1 * rRef, v0));
                        int i10 = Vertex(Ring(p1, a0), fn1, new Vector2(a0 * rRef, v1));
                        int i11 = Vertex(Ring(p1, a1), fn1, new Vector2(a1 * rRef, v1));
                        Quad(i00, i01, i11, i10);
                    }
                    continue;
                }
                int baseIndex = positions.Count;
                for (int k = 0; k <= segments; k++)
                {
                    float a = (startDeg + sweepDeg * k / segments) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vertex(Ring(p0, a), new Vector3(n0.x * c, n0.y, n0.x * s), new Vector2(a * rRef, v0));
                    Vertex(Ring(p1, a), new Vector3(n1.x * c, n1.y, n1.x * s), new Vector2(a * rRef, v1));
                }
                for (int k = 0; k < segments; k++)
                {
                    int a0 = baseIndex + k * 2, a1 = a0 + 2;
                    Quad(a0, a1, a1 + 1, a0 + 1);
                }
            }
        }

        private static Vector3 Ring(Vector2 p, float angle) => new Vector3(p.x * Mathf.Cos(angle), p.y, p.x * Mathf.Sin(angle));

        private static Vector2 EndNormal(Vector2[] segNormal, int segment, bool start, float cosHard)
        {
            Vector2 own = segNormal[segment];
            int other = start ? segment - 1 : segment + 1;
            if (other < 0 || other >= segNormal.Length) return own;
            Vector2 neighbour = segNormal[other];
            if (Vector2.Dot(own, neighbour) < cosHard) return own;
            Vector2 avg = own + neighbour;
            return avg.sqrMagnitude > 1e-10f ? avg.normalized : own;
        }

        /// <summary>Solid cylinder along local +Y from y0 to y1 with flat caps.</summary>
        public void Cylinder(float radius, float y0, float y1, int segments)
        {
            Lathe(new[] { new Vector2(0f, y0), new Vector2(radius, y0), new Vector2(radius, y1), new Vector2(0f, y1) }, segments);
        }

        /// <summary>Tube (sleeve) along local +Y with an inner bore.</summary>
        public void Sleeve(float outer, float inner, float y0, float y1, int segments)
        {
            Lathe(new[]
            {
                new Vector2(inner, y0), new Vector2(outer, y0), new Vector2(outer, y1), new Vector2(inner, y1),
                new Vector2(inner, y0)
            }, segments);
        }

        /// <summary>Bellows (dust boot) along local +Y: collars of radius r0 / r1 at the ends, folds in between.</summary>
        public void Bellows(float r0, float r1, float rMin, float rMax, float length, int folds, int segments, float collar = 0.012f)
        {
            var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(r0, 0f), new Vector2(r0, collar) };
            int samples = folds * 6;
            float span = length - 2f * collar;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float taper = Mathf.Lerp(0f, 1f, t);
                float amp = (rMax - rMin) * 0.5f;
                float mid = (rMax + rMin) * 0.5f + Mathf.Lerp(0f, (r1 - r0) * 0.5f, taper);
                float r = mid + amp * Mathf.Cos(t * folds * Mathf.PI * 2f + Mathf.PI);
                p.Add(new Vector2(Mathf.Max(r, Mathf.Min(r0, r1) * 0.8f), collar + span * t));
            }
            p.Add(new Vector2(r1, length - collar));
            p.Add(new Vector2(r1, length));
            p.Add(new Vector2(0f, length));
            Lathe(p, segments, 55f);
        }

        // ------------------------------------------------------------------------------------------------ sweeps

        /// <summary>Catmull-Rom (centripetal) through the points; the result starts and ends on the end points.</summary>
        public static List<Vector3> CatmullRom(IList<Vector3> points, int samplesPerSpan)
        {
            var result = new List<Vector3>();
            int n = points.Count;
            if (n < 3) { result.AddRange(points); return result; }
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = i > 0 ? points[i - 1] : points[0] * 2f - points[1];
                Vector3 p1 = points[i], p2 = points[i + 1];
                Vector3 p3 = i + 2 < n ? points[i + 2] : points[n - 1] * 2f - points[n - 2];
                float t0 = 0f;
                float t1 = t0 + Mathf.Pow(Mathf.Max(1e-6f, (p1 - p0).magnitude), 0.5f);
                float t2 = t1 + Mathf.Pow(Mathf.Max(1e-6f, (p2 - p1).magnitude), 0.5f);
                float t3 = t2 + Mathf.Pow(Mathf.Max(1e-6f, (p3 - p2).magnitude), 0.5f);
                for (int s = 0; s < samplesPerSpan; s++)
                {
                    float t = Mathf.Lerp(t1, t2, (float)s / samplesPerSpan);
                    Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
                    Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
                    Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
                    Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
                    Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
                    result.Add((t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2);
                }
            }
            result.Add(points[n - 1]);
            return result;
        }

        /// <summary>Polyline with rounded corners (bend radius), as a bent tube or rod follows its centre line.</summary>
        public static List<Vector3> Bends(IList<Vector3> corners, float bendRadius, int samplesPerBend = 6)
        {
            var result = new List<Vector3>();
            int n = corners.Count;
            if (n < 3) { result.AddRange(corners); return result; }
            result.Add(corners[0]);
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i], c = corners[i + 1];
                Vector3 da = (a - b).normalized, dc = (c - b).normalized;
                float angle = Vector3.Angle(da, dc) * Mathf.Deg2Rad;
                if (angle > Mathf.PI - 0.01f) { result.Add(b); continue; }
                float cut = Mathf.Min(bendRadius / Mathf.Tan(angle * 0.5f), Mathf.Min((a - b).magnitude, (c - b).magnitude) * 0.45f);
                Vector3 p0 = b + da * cut, p2 = b + dc * cut;
                for (int s = 0; s <= samplesPerBend; s++)
                {
                    float t = (float)s / samplesPerBend;
                    // quadratic Bezier through the corner: close to a circular arc for these angles
                    Vector3 q = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p2;
                    result.Add(q);
                }
            }
            result.Add(corners[n - 1]);
            return result;
        }

        /// <summary>
        /// Rotation-minimising frames along a polyline (double reflection). Returns tangents and normals; the first
        /// normal is <paramref name="normalHint"/> made perpendicular to the first tangent.
        /// </summary>
        public static void Frames(IList<Vector3> path, Vector3 normalHint, out Vector3[] tangents, out Vector3[] frameNormals)
        {
            int n = path.Count;
            tangents = new Vector3[n];
            frameNormals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = i == 0 ? path[1] - path[0] : i == n - 1 ? path[n - 1] - path[n - 2] : path[i + 1] - path[i - 1];
                tangents[i] = d.sqrMagnitude > 1e-14f ? d.normalized : (i > 0 ? tangents[i - 1] : Vector3.forward);
            }
            Vector3 r0 = Vector3.ProjectOnPlane(normalHint, tangents[0]);
            if (r0.sqrMagnitude < 1e-8f) r0 = Vector3.ProjectOnPlane(Mathf.Abs(tangents[0].y) < 0.9f ? Vector3.up : Vector3.right, tangents[0]);
            frameNormals[0] = r0.normalized;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 v1 = path[i + 1] - path[i];
                float c1 = Vector3.Dot(v1, v1);
                if (c1 < 1e-14f) { frameNormals[i + 1] = frameNormals[i]; continue; }
                Vector3 rL = frameNormals[i] - (2f / c1) * Vector3.Dot(v1, frameNormals[i]) * v1;
                Vector3 tL = tangents[i] - (2f / c1) * Vector3.Dot(v1, tangents[i]) * v1;
                Vector3 v2 = tangents[i + 1] - tL;
                float c2 = Vector3.Dot(v2, v2);
                Vector3 r1 = c2 < 1e-14f ? rL : rL - (2f / c2) * Vector3.Dot(v2, rL) * v2;
                frameNormals[i + 1] = Vector3.ProjectOnPlane(r1, tangents[i + 1]).normalized;
            }
        }

        /// <summary>
        /// Round (or elliptical) tube along a path: pipes, hoses, wires, rods, arms. <paramref name="radiusScale"/>
        /// scales the radius along the path (0..1 of the length); <paramref name="ellipse"/> stretches the section along
        /// the frame normal (x) and binormal (y).
        /// </summary>
        public void Tube(IList<Vector3> path, float radius, int sides, bool capStart = true, bool capEnd = true,
            Vector3? normalHint = null, Func<float, float> radiusScale = null, Vector2? ellipse = null)
        {
            int n = path.Count;
            if (n < 2) return;
            sides = Mathf.Max(3, sides);
            Frames(path, normalHint ?? Vector3.up, out Vector3[] t, out Vector3[] r);
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + (path[i] - path[i - 1]).magnitude;
            float total = Mathf.Max(1e-6f, along[n - 1]);
            Vector2 e = ellipse ?? Vector2.one;
            int start = positions.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 b = Vector3.Cross(t[i], r[i]);
                float rad = radius * (radiusScale != null ? radiusScale(along[i] / total) : 1f);
                for (int k = 0; k <= sides; k++)
                {
                    float a = Mathf.PI * 2f * k / sides;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vector3 offset = r[i] * (c * rad * e.x) + b * (s * rad * e.y);
                    Vector3 normal = r[i] * (c / Mathf.Max(1e-4f, e.x)) + b * (s / Mathf.Max(1e-4f, e.y));
                    Vertex(path[i] + offset, normal, new Vector2(a * radius, along[i]));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a0 = start + i * (sides + 1) + k, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    Quad(a0, a1, b1, b0);
                }
            }
            if (capStart) TubeCap(path[0], -t[0], r[0], Vector3.Cross(t[0], r[0]), radius * (radiusScale != null ? radiusScale(0f) : 1f), e, sides);
            if (capEnd) TubeCap(path[n - 1], t[n - 1], r[n - 1], Vector3.Cross(t[n - 1], r[n - 1]), radius * (radiusScale != null ? radiusScale(1f) : 1f), e, sides);
        }

        private void TubeCap(Vector3 centre, Vector3 normal, Vector3 r, Vector3 b, float radius, Vector2 e, int sides)
        {
            int c = Vertex(centre, normal, Vector2.zero);
            int first = positions.Count;
            for (int k = 0; k < sides; k++)
            {
                float a = Mathf.PI * 2f * k / sides;
                Vector3 offset = r * (Mathf.Cos(a) * radius * e.x) + b * (Mathf.Sin(a) * radius * e.y);
                Vertex(centre + offset, normal, new Vector2(offset.x, offset.z));
            }
            for (int k = 0; k < sides; k++) Tri(c, first + k, first + (k + 1) % sides);
        }

        /// <summary>
        /// Centre line of a helical coil spring along local +Y from 0 to <paramref name="length"/>: closed end coils
        /// (pitch = wire diameter) and evenly pitched active coils between them.
        /// </summary>
        public static List<Vector3> HelixPath(float coilRadius, float length, float wireDiameter, float activeTurns,
            float endTurns = 0.85f, int samplesPerTurn = 28, float startDeg = 0f)
        {
            var path = new List<Vector3>();
            float total = activeTurns + 2f * endTurns;
            float pitch = Mathf.Max(wireDiameter * 1.05f, (length - wireDiameter - 2f * endTurns * wireDiameter) / Mathf.Max(0.1f, activeTurns));
            int samples = Mathf.CeilToInt(total * samplesPerTurn);
            for (int i = 0; i <= samples; i++)
            {
                float turn = total * i / samples;
                float h;
                if (turn < endTurns) h = turn * wireDiameter;
                else if (turn > total - endTurns) h = endTurns * wireDiameter + activeTurns * pitch + (turn - (total - endTurns)) * wireDiameter;
                else h = endTurns * wireDiameter + (turn - endTurns) * pitch;
                float a = startDeg * Mathf.Deg2Rad + turn * Mathf.PI * 2f;
                path.Add(new Vector3(coilRadius * Mathf.Cos(a), wireDiameter * 0.5f + h, coilRadius * Mathf.Sin(a)));
            }
            return path;
        }

        /// <summary>
        /// Sweeps a closed polygon (counter-clockwise in x = side, y = up of the path frame) along a path: frame rails,
        /// box, C and I sections. Edges that turn by more than <paramref name="hardAngle"/> stay hard.
        /// </summary>
        public void Sweep(IList<Vector2> polygon, IList<Vector3> path, Vector3 upHint, bool capStart = true, bool capEnd = true,
            float hardAngle = 30f, bool inward = false)
        {
            int m = polygon.Count, n = path.Count;
            if (m < 3 || n < 2) return;
            if (SignedArea(polygon) < 0f)
            {
                var reversed = new List<Vector2>(polygon);
                reversed.Reverse();
                polygon = reversed;
            }
            Frames(path, upHint, out Vector3[] t, out Vector3[] up);
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + (path[i] - path[i - 1]).magnitude;
            var edgeNormal = new Vector2[m];
            for (int j = 0; j < m; j++)
            {
                Vector2 d = polygon[(j + 1) % m] - polygon[j];
                edgeNormal[j] = d.sqrMagnitude > 1e-14f ? new Vector2(d.y, -d.x).normalized : Vector2.up;
            }
            float cosHard = Mathf.Cos(hardAngle * Mathf.Deg2Rad);
            float perimeter = 0f;
            for (int j = 0; j < m; j++)
            {
                Vector2 p0 = polygon[j], p1 = polygon[(j + 1) % m];
                Vector2 n0 = edgeNormal[j], n1 = edgeNormal[j];
                Vector2 prev = edgeNormal[(j + m - 1) % m], next = edgeNormal[(j + 1) % m];
                if (Vector2.Dot(n0, prev) >= cosHard) n0 = (n0 + prev).normalized;
                if (Vector2.Dot(n1, next) >= cosHard) n1 = (n1 + next).normalized;
                float u0 = perimeter, u1 = perimeter + (p1 - p0).magnitude;
                perimeter = u1;
                int start = positions.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 side = Vector3.Cross(up[i], t[i]);
                    float flip = inward ? -1f : 1f;
                    Vertex(path[i] + side * p0.x + up[i] * p0.y, (side * n0.x + up[i] * n0.y) * flip, new Vector2(u0, along[i]));
                    Vertex(path[i] + side * p1.x + up[i] * p1.y, (side * n1.x + up[i] * n1.y) * flip, new Vector2(u1, along[i]));
                }
                for (int i = 0; i < n - 1; i++)
                {
                    int a = start + i * 2;
                    Quad(a, a + 1, a + 3, a + 2);
                }
            }
            if (capStart) PolygonCap(polygon, path[0], Vector3.Cross(up[0], t[0]), up[0], -t[0]);
            if (capEnd) PolygonCap(polygon, path[n - 1], Vector3.Cross(up[n - 1], t[n - 1]), up[n - 1], t[n - 1]);
        }

        public static float SignedArea(IList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        /// <summary>
        /// Prism of a planar polygon given by its corners in the current space (plates, gussets, brackets), thickness
        /// centred on the polygon's plane.
        /// </summary>
        public void PlanarPrism(IList<Vector3> corners, float thickness, float hardAngle = 30f)
        {
            int n = corners.Count;
            if (n < 3) return;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % n];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
            }
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            Vector3 o = corners[0];
            Vector3 u = (corners[1] - corners[0]).normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;
            var outline = new Vector2[n];
            for (int i = 0; i < n; i++) outline[i] = new Vector2(Vector3.Dot(corners[i] - o, u), Vector3.Dot(corners[i] - o, v));
            var frame = new Matrix4x4();
            frame.SetColumn(0, u);
            frame.SetColumn(1, v);
            frame.SetColumn(2, normal);
            frame.SetColumn(3, new Vector4(o.x, o.y, o.z, 1f));
            Push(frame);
            Extrude(outline, thickness, hardAngle);
            Pop();
        }

        /// <summary>Rounded slot outline (a tab with round ends) from a to b with the given half width.</summary>
        public static List<Vector2> Slot(Vector2 a, Vector2 b, float halfWidth, int segments = 8)
        {
            var list = new List<Vector2>();
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x);
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.PI * i / segments;
                list.Add(b + (n * Mathf.Cos(t) + d * Mathf.Sin(t)) * halfWidth);
            }
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.PI * i / segments;
                list.Add(a - (n * Mathf.Cos(t) + d * Mathf.Sin(t)) * halfWidth);
            }
            return list;
        }

        /// <summary>Flat polygon (counter-clockwise in x, y) at a frame, facing <paramref name="normal"/>.</summary>
        public void PolygonCap(IList<Vector2> polygon, Vector3 origin, Vector3 xAxis, Vector3 yAxis, Vector3 normal)
        {
            List<int> tris = Triangulate(polygon);
            int start = positions.Count;
            foreach (Vector2 p in polygon) Vertex(origin + xAxis * p.x + yAxis * p.y, normal, p);
            for (int i = 0; i < tris.Count; i += 3) Tri(start + tris[i], start + tris[i + 1], start + tris[i + 2]);
        }

        /// <summary>Prism of a closed outline (x, y) between z = -depth/2 and +depth/2: brackets, plates, arms.</summary>
        public void Extrude(IList<Vector2> outline, float depth, float hardAngle = 30f)
        {
            Sweep(outline, new[] { new Vector3(0f, 0f, -depth * 0.5f), new Vector3(0f, 0f, depth * 0.5f) }, Vector3.up, true, true, hardAngle);
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (either winding); indices into the polygon.</summary>
        public static List<int> Triangulate(IList<Vector2> polygon)
        {
            var result = new List<int>();
            int n = polygon.Count;
            if (n < 3) return result;
            var idx = new List<int>(n);
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            for (int i = 0; i < n; i++) idx.Add(area > 0f ? i : n - 1 - i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = polygon[i0], b = polygon[i1], c = polygon[i2];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 1e-12f) continue;
                    bool inside = false;
                    foreach (int k in idx)
                    {
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTriangle(polygon[k], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------------------------------------ boxes

        /// <summary>Box with flat faces.</summary>
        public void Box(Vector3 center, Vector3 size)
        {
            RoundedBox(center, size, 0f, 0);
        }

        /// <summary>Box with rounded edges and corners (fillet radius, segments per quarter round).</summary>
        public void RoundedBox(Vector3 center, Vector3 size, float radius, int segments)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.999f);
            if (radius < 1e-5f) segments = 0;
            Vector3 inner = h - Vector3.one * radius;
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2;
                float sign = face % 2 == 0 ? 1f : -1f;
                int ua = axis == 0 ? 2 : 0, va = axis == 1 ? 2 : 1;
                float[] su = Samples(h[ua], radius, segments), sv = Samples(h[va], radius, segments);
                Vector3 faceN = Vector3.zero;
                faceN[axis] = sign;
                int nu = su.Length, nv = sv.Length, start = positions.Count;
                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[ua] = su[i];
                        p[va] = sv[j];
                        Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - q;
                        Vector3 nrm = radius > 0f && d.sqrMagnitude > 1e-12f ? d.normalized : faceN;
                        Vector3 pos = radius > 0f ? q + nrm * radius : p;
                        Vertex(center + pos, nrm, new Vector2(pos[ua], pos[va]));
                    }
                }
                for (int j = 0; j < nv - 1; j++)
                    for (int i = 0; i < nu - 1; i++)
                    {
                        int a = start + j * nu + i;
                        Quad(a, a + 1, a + nu + 1, a + nu);
                    }
            }
        }

        private static float[] Samples(float half, float radius, int segments)
        {
            if (segments <= 0) return new[] { -half, half };
            var list = new List<float>();
            float inner = half - radius;
            for (int s = 0; s <= segments; s++) list.Add(-inner - radius * Mathf.Cos(Mathf.PI * 0.5f * s / segments));
            for (int s = 0; s <= segments; s++) list.Add(inner + radius * Mathf.Sin(Mathf.PI * 0.5f * s / segments));
            return list.ToArray();
        }

        // ------------------------------------------------------------------------------------------------ discs

        /// <summary>
        /// Flat annulus in the local XZ plane at height y, facing +Y (or -Y), with optional round through-holes on a
        /// pitch circle (drilled discs, flanges). Holes are cut cell by cell from a polar grid, so the face stays one
        /// clean surface.
        /// </summary>
        public void HoledAnnulus(float rInner, float rOuter, float y, bool up, int segments, int holes = 0,
            float holeRadius = 0f, float holePitchRadius = 0f, float holeDepth = 0f, float startDeg = 0f)
        {
            Vector3 n = up ? Vector3.up : Vector3.down;
            if (holes <= 0 || holeRadius <= 0f)
            {
                Lathe(up ? new[] { new Vector2(rOuter, y), new Vector2(rInner, y) } : new[] { new Vector2(rInner, y), new Vector2(rOuter, y) }, segments);
                return;
            }
            // Angular cells; each hole sits in one cell spanning the full ring width between rInner and rOuter.
            int perHole = Mathf.Max(2, segments / holes);
            int cells = holes * perHole;
            const int ringPts = 12;
            for (int c = 0; c < cells; c++)
            {
                float a0 = (startDeg * Mathf.Deg2Rad) + Mathf.PI * 2f * c / cells;
                float a1 = (startDeg * Mathf.Deg2Rad) + Mathf.PI * 2f * (c + 1) / cells;
                bool hole = c % perHole == perHole / 2;
                if (!hole)
                {
                    int i0 = Vertex(Ring(new Vector2(rInner, y), a0), n, Vector2.zero);
                    int i1 = Vertex(Ring(new Vector2(rOuter, y), a0), n, Vector2.zero);
                    int i2 = Vertex(Ring(new Vector2(rOuter, y), a1), n, Vector2.zero);
                    int i3 = Vertex(Ring(new Vector2(rInner, y), a1), n, Vector2.zero);
                    Quad(i0, i1, i2, i3);
                    continue;
                }
                // Cell boundary sampled with ringPts points (counter-clockwise), hole circle with as many points.
                float am = (a0 + a1) * 0.5f;
                Vector3 centre = Ring(new Vector2(holePitchRadius, y), am);
                var boundary = new Vector3[ringPts];
                for (int k = 0; k < ringPts; k++)
                {
                    float t = (float)k / ringPts;
                    // walk: inner edge a0->a1, outer edge a1->a0 (via sides)
                    float u = t * 4f;
                    Vector3 p;
                    if (u < 1f) p = Ring(new Vector2(rInner, y), Mathf.Lerp(a0, a1, u));
                    else if (u < 2f) p = Ring(new Vector2(Mathf.Lerp(rInner, rOuter, u - 1f), y), a1);
                    else if (u < 3f) p = Ring(new Vector2(rOuter, y), Mathf.Lerp(a1, a0, u - 2f));
                    else p = Ring(new Vector2(Mathf.Lerp(rOuter, rInner, u - 3f), y), a0);
                    boundary[k] = p;
                }
                var circle = new Vector3[ringPts];
                for (int k = 0; k < ringPts; k++)
                {
                    Vector3 dir = boundary[k] - centre;
                    dir.y = 0f;
                    circle[k] = centre + dir.normalized * holeRadius;
                }
                int sb = positions.Count;
                for (int k = 0; k < ringPts; k++) Vertex(boundary[k], n, Vector2.zero);
                int sc = positions.Count;
                for (int k = 0; k < ringPts; k++) Vertex(circle[k], n, Vector2.zero);
                for (int k = 0; k < ringPts; k++)
                {
                    int k1 = (k + 1) % ringPts;
                    Quad(sb + k, sb + k1, sc + k1, sc + k);
                }
                if (holeDepth > 0f)
                {
                    // hole wall, facing the hole axis
                    int sw = positions.Count;
                    for (int k = 0; k <= ringPts; k++)
                    {
                        Vector3 p = circle[k % ringPts];
                        Vector3 inward = (centre - p).normalized;
                        Vertex(p, inward, new Vector2(k, 0f));
                        Vertex(p - n * holeDepth, inward, new Vector2(k, 1f));
                    }
                    for (int k = 0; k < ringPts; k++) Quad(sw + k * 2, sw + k * 2 + 2, sw + k * 2 + 3, sw + k * 2 + 1);
                }
            }
        }

        /// <summary>
        /// Rectangular hollow section along a path (frame rails, crossmembers): outer and inner walls and flat end
        /// rings. Width along the frame side axis, height along its up axis, wall thickness t.
        /// </summary>
        public void BoxTube(IList<Vector3> path, float width, float height, float t, Vector3 upHint)
        {
            var outer = Profiles.Rect(width, height);
            var inner = Profiles.Rect(width - 2f * t, height - 2f * t);
            Sweep(outer, path, upHint, false, false, 30f);
            Sweep(inner, path, upHint, false, false, 30f, true);
            Frames(path, upHint, out Vector3[] tangents, out Vector3[] ups);
            foreach (int end in new[] { 0, path.Count - 1 })
            {
                Vector3 n = end == 0 ? -tangents[0] : tangents[path.Count - 1];
                Vector3 up = ups[end], side = Vector3.Cross(up, tangents[end]);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 a = outer[i], b = outer[(i + 1) % 4], c = inner[(i + 1) % 4], d = inner[i];
                    int i0 = Vertex(path[end] + side * a.x + up * a.y, n, a), i1 = Vertex(path[end] + side * b.x + up * b.y, n, b);
                    int i2 = Vertex(path[end] + side * c.x + up * c.y, n, c), i3 = Vertex(path[end] + side * d.x + up * d.y, n, d);
                    Quad(i0, i1, i2, i3);
                }
            }
        }

        /// <summary>
        /// Ventilated brake disc in the local XZ plane, axis +Y: two friction rings joined by radial vanes, the hat
        /// (to the hub) rising on +Y, optional drilled holes through both rings.
        /// </summary>
        public void VentedDisc(float rOuter, float rInner, float ringThickness, float ventGap, int vanes, float hatRadius,
            float hatHeight, int segments = 48, int holes = 0, float holeRadius = 0.004f)
        {
            float y0 = 0f, y1 = ringThickness, y2 = ringThickness + ventGap, y3 = 2f * ringThickness + ventGap;
            float pitch = (rOuter + rInner) * 0.5f;
            HoledAnnulus(rInner, rOuter, y0, false, segments, holes, holeRadius, pitch, ringThickness);
            HoledAnnulus(rInner, rOuter, y3, true, segments, holes, holeRadius, pitch, ringThickness);
            Lathe(new[] { new Vector2(rOuter, y0), new Vector2(rOuter, y1) }, segments);
            Lathe(new[] { new Vector2(rOuter, y2), new Vector2(rOuter, y3) }, segments);
            Lathe(new[] { new Vector2(rInner, y1), new Vector2(rInner, y0) }, segments);
            Lathe(new[] { new Vector2(rInner, y3), new Vector2(rInner, y2) }, segments);
            HoledAnnulus(rInner, rOuter, y1, true, segments);
            HoledAnnulus(rInner, rOuter, y2, false, segments);
            for (int i = 0; i < vanes; i++)
            {
                float a = Mathf.PI * 2f * (i + 0.5f) / vanes;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float mid = (rOuter + rInner) * 0.5f, len = rOuter - rInner - 0.004f;
                Push(dir * mid + Vector3.up * (y1 + ventGap * 0.5f), Quaternion.LookRotation(dir, Vector3.up));
                Box(Vector3.zero, new Vector3(0.004f, ventGap, len));
                Pop();
            }
            // Hat: from the inner edge of the outer ring up to the hub face.
            Lathe(new[] { new Vector2(rInner, y3), new Vector2(hatRadius + 0.004f, y3 + hatHeight * 0.2f), new Vector2(hatRadius, y3 + hatHeight),
                new Vector2(hatRadius * 0.45f, y3 + hatHeight), new Vector2(hatRadius * 0.45f, y3 + hatHeight - 0.006f), new Vector2(hatRadius - 0.006f, y3 + hatHeight - 0.006f),
                new Vector2(rInner - 0.006f, y3) }, segments, 35f);
        }

        // ------------------------------------------------------------------------------------------------ output

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            Fill(mesh);
            return mesh;
        }

        /// <summary>Writes the geometry into an existing mesh (kept asset references stay valid).</summary>
        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            int used = submeshes.Count;
            while (used > 1 && submeshes[used - 1].Count == 0) used--;
            mesh.subMeshCount = used;
            for (int s = 0; s < used; s++) mesh.SetTriangles(submeshes[s], s, false);
            mesh.RecalculateBounds();
        }
    }

    /// <summary>Closed section outlines (counter-clockwise, centred) for MeshKit.Sweep and Extrude.</summary>
    public static class Profiles
    {
        public static Vector2[] Rect(float w, float h) =>
            new[] { new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f) };

        public static List<Vector2> RoundRect(float w, float h, float r, int segments = 3)
        {
            var list = new List<Vector2>();
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f - 1e-4f);
            Vector2[] centres = { new Vector2(w * 0.5f - r, -h * 0.5f + r), new Vector2(w * 0.5f - r, h * 0.5f - r), new Vector2(-w * 0.5f + r, h * 0.5f - r), new Vector2(-w * 0.5f + r, -h * 0.5f + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= segments; i++)
                {
                    float a = (c * 90f - 90f + 90f * i / segments) * Mathf.Deg2Rad;
                    list.Add(centres[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            return list;
        }

        /// <summary>C (channel) section opening toward -x: web on +x.</summary>
        public static Vector2[] Channel(float w, float h, float t) => new[]
        {
            new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f),
            new Vector2(-w * 0.5f, h * 0.5f - t), new Vector2(w * 0.5f - t, h * 0.5f - t), new Vector2(w * 0.5f - t, -h * 0.5f + t), new Vector2(-w * 0.5f, -h * 0.5f + t)
        };

        /// <summary>I section: flanges w wide and tf thick, web tw thick.</summary>
        public static Vector2[] IBeam(float w, float h, float tf, float tw) => new[]
        {
            new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, -h * 0.5f + tf), new Vector2(tw * 0.5f, -h * 0.5f + tf),
            new Vector2(tw * 0.5f, h * 0.5f - tf), new Vector2(w * 0.5f, h * 0.5f - tf), new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f),
            new Vector2(-w * 0.5f, h * 0.5f - tf), new Vector2(-tw * 0.5f, h * 0.5f - tf), new Vector2(-tw * 0.5f, -h * 0.5f + tf), new Vector2(-w * 0.5f, -h * 0.5f + tf)
        };

        /// <summary>Top-hat section (pressed rails, crossmembers): flanges at the bottom.</summary>
        public static Vector2[] Hat(float w, float h, float flange, float t) => new[]
        {
            new Vector2(-w * 0.5f - flange, -h * 0.5f), new Vector2(-w * 0.5f + t, -h * 0.5f), new Vector2(-w * 0.5f + t, h * 0.5f - t), new Vector2(w * 0.5f - t, h * 0.5f - t),
            new Vector2(w * 0.5f - t, -h * 0.5f), new Vector2(w * 0.5f + flange, -h * 0.5f), new Vector2(w * 0.5f + flange, -h * 0.5f + t), new Vector2(w * 0.5f, -h * 0.5f + t),
            new Vector2(w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, h * 0.5f), new Vector2(-w * 0.5f, -h * 0.5f + t), new Vector2(-w * 0.5f - flange, -h * 0.5f + t)
        };

        public static List<Vector2> Circle(float r, int segments)
        {
            var list = new List<Vector2>();
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                list.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return list;
        }
    }

    /// <summary>Standard parts: bolts, nuts, washers, ball joints, built along local -Y (into the joint).</summary>
    public static class Fasteners
    {
        /// <summary>Hex-head bolt: head on top of y = 0 (up to the head height), shank down to y = -length.</summary>
        public static void HexBolt(MeshKit k, float d, float length, bool flange = true)
        {
            float s = d * 1.55f, R = s / (2f * Mathf.Cos(30f * Mathf.Deg2Rad)), head = d * 0.65f;
            float y0 = flange ? d * 0.14f : 0f;
            if (flange) k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(R * 1.08f, 0f), new Vector2(R * 1.08f, y0 * 0.6f), new Vector2(R * 0.9f, y0), new Vector2(0f, y0) }, 16);
            k.Lathe(new[] { new Vector2(0f, y0), new Vector2(R, y0), new Vector2(R, y0 + head * 0.82f), new Vector2(R * 0.86f, y0 + head), new Vector2(0f, y0 + head) }, 6, 40f, 30f, 360f, true);
            float r = d * 0.5f;
            k.Lathe(new[] { new Vector2(0f, -length), new Vector2(r * 0.82f, -length), new Vector2(r, -length + d * 0.12f), new Vector2(r, 0.001f), new Vector2(0f, 0.001f) }, 10);
        }

        /// <summary>Hex nut seated on y = 0, rising to its height (0.8 d).</summary>
        public static void HexNut(MeshKit k, float d, bool flange = false)
        {
            float s = d * 1.55f, R = s / (2f * Mathf.Cos(30f * Mathf.Deg2Rad)), h = d * 0.8f, ri = d * 0.5f;
            float y0 = 0f;
            if (flange)
            {
                y0 = d * 0.14f;
                k.Lathe(new[] { new Vector2(ri, 0f), new Vector2(R * 1.1f, 0f), new Vector2(R * 1.1f, y0 * 0.6f), new Vector2(R * 0.9f, y0), new Vector2(ri, y0) }, 16);
            }
            k.Lathe(new[] { new Vector2(ri, y0), new Vector2(R, y0), new Vector2(R, y0 + h * 0.85f), new Vector2(R * 0.86f, y0 + h), new Vector2(ri, y0 + h) }, 6, 40f, 30f, 360f, true);
            k.Lathe(new[] { new Vector2(ri, y0 + h), new Vector2(ri, y0) }, 10);
        }

        /// <summary>Flat washer seated on y = 0.</summary>
        public static void Washer(MeshKit k, float d)
        {
            k.Sleeve(d * 1.0f, d * 0.55f, 0f, d * 0.12f, 18);
        }

        /// <summary>
        /// Ball joint along local +Y: housing (radius r) at the origin, ball stud rising to <paramref name="studLength"/>,
        /// rubber boot over the neck. Submeshes: 0 housing, 1 stud, 2 boot.
        /// </summary>
        public static void BallJoint(MeshKit k, float r, float studLength)
        {
            k.Sub(0);
            k.Lathe(new[] { new Vector2(0f, -r * 0.9f), new Vector2(r * 0.85f, -r * 0.9f), new Vector2(r, -r * 0.6f), new Vector2(r, r * 0.25f), new Vector2(r * 0.7f, r * 0.45f), new Vector2(0f, r * 0.45f) }, 18, 35f);
            k.Sub(2);
            k.Lathe(new[] { new Vector2(0f, r * 0.45f), new Vector2(r * 0.95f, r * 0.45f), new Vector2(r * 1.05f, r * 0.8f), new Vector2(r * 0.85f, r * 1.2f), new Vector2(r * 0.42f, r * 1.4f), new Vector2(r * 0.4f, r * 1.55f), new Vector2(0f, r * 1.55f) }, 18, 50f);
            k.Sub(1);
            float sr = r * 0.36f;
            k.Lathe(new[] { new Vector2(0f, r * 1.5f), new Vector2(sr, r * 1.5f), new Vector2(sr * 0.75f, studLength - r * 0.05f), new Vector2(sr * 0.6f, studLength), new Vector2(0f, studLength) }, 12);
        }
    }
}
#endif
