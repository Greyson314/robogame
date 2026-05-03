using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Procedurally builds a unit-radius icosphere mesh with smooth normals.
    /// Subdivision <c>n</c> yields <c>20 * 4^n</c> triangles (sub-5 = 20 480 tri,
    /// sub-6 = 81 920 tri).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why we ship our own instead of using Unity's primitive Sphere:
    /// the primitive is a low-poly UV sphere (~80 tris). At a multi-km
    /// radius that's faceted enough to read as "20-sided die" instead
    /// of "planet", and \u2014 worse \u2014 the primitive's
    /// <see cref="SphereCollider"/> sits on the mathematical sphere
    /// while the visual faces dip inward between vertices, which
    /// produces the "land 10 ft in the air over a flat patch" bug
    /// the planet arena was reporting.
    /// </para>
    /// <para>
    /// Using this mesh as <i>both</i> the <see cref="MeshFilter"/>
    /// source and the <see cref="MeshCollider"/> mesh fixes the gap
    /// exactly \u2014 visual triangles and collision triangles are the
    /// same triangles. Smooth vertex normals (every vertex normal =
    /// its own normalised position) hide the polygon edges under
    /// shading, which gets us back the perceived smoothness without
    /// needing higher subdivision.
    /// </para>
    /// <para>
    /// Recommendation for the planet arena (2400 m radius): subdivision
    /// 6. That's a max chord-deviation of ~9 cm vs. a true sphere
    /// (invisible at any distance) and ~80 K tris, which is still trivial
    /// for a single static MeshCollider. Sub-5 (~36 cm at this radius)
    /// is fine for collision but the silhouette is visibly polygonal
    /// against the sky — the per-pixel edge length on the limb is what
    /// the eye actually picks up, and sub-6 halves it.
    /// </para>
    /// </remarks>
    internal static class IcosphereBuilder
    {
        /// <summary>
        /// Build a unit-radius icosphere mesh. Caller is responsible for
        /// scaling via the renderer's transform (or by multiplying
        /// vertices before assigning, if a true-radius mesh is wanted
        /// for collider authoring).
        /// </summary>
        /// <param name="displace">
        /// Optional radial displacement callback. If non-null, called per
        /// vertex with the unit-direction vector; the returned value is
        /// added to the radius (1) before the vertex is written. Mesh
        /// normals switch from "smooth, vertex == position" to a
        /// face-averaged <see cref="Mesh.RecalculateNormals"/> in this
        /// case so cliffs / mountains shade correctly.
        /// </param>
        public static Mesh Build(
            int subdivisions,
            string name = "Icosphere",
            System.Func<Vector3, float> displace = null)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 7);

            // Golden-ratio icosahedron seed (12 verts, 20 tris).
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            var verts = new List<Vector3>
            {
                new Vector3(-1f,  t, 0f).normalized,
                new Vector3( 1f,  t, 0f).normalized,
                new Vector3(-1f, -t, 0f).normalized,
                new Vector3( 1f, -t, 0f).normalized,

                new Vector3(0f, -1f,  t).normalized,
                new Vector3(0f,  1f,  t).normalized,
                new Vector3(0f, -1f, -t).normalized,
                new Vector3(0f,  1f, -t).normalized,

                new Vector3( t, 0f, -1f).normalized,
                new Vector3( t, 0f,  1f).normalized,
                new Vector3(-t, 0f, -1f).normalized,
                new Vector3(-t, 0f,  1f).normalized,
            };

            var tris = new List<int>
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };

            // Loop subdivide. Each iteration replaces every triangle with
            // four, with new midpoint verts pushed back onto the unit sphere.
            var midCache = new Dictionary<long, int>();
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(a, b, verts, midCache);
                    int bc = Midpoint(b, c, verts, midCache);
                    int ca = Midpoint(c, a, verts, midCache);
                    next.Add(a);  next.Add(ab); next.Add(ca);
                    next.Add(b);  next.Add(bc); next.Add(ab);
                    next.Add(c);  next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                tris = next;
                midCache.Clear();
            }

            // Smooth normals = unit position. Free — but only valid for an
            // un-displaced sphere. If the caller passed a displacement
            // callback we must apply it first, then let Unity recompute
            // normals from face geometry so cliffs and mountain ridges
            // shade as cliffs and ridges instead of as a smooth ball.
            Vector3[] finalVerts = new Vector3[verts.Count];
            Vector3[] normals = new Vector3[verts.Count];
            bool displaced = displace != null;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i]; // already unit-length
                if (displaced)
                {
                    float h = displace(dir);
                    finalVerts[i] = dir * (1f + h); // unit-radius mesh; transform scales to world
                }
                else
                {
                    finalVerts[i] = dir;
                }
                normals[i] = dir; // overwritten by RecalculateNormals below when displaced
            }

            var mesh = new Mesh { name = name };
            // Index format: sub-5 stays under 65 K verts (10 242). Sub-6 needs
            // 32-bit indices (40 962 verts is fine; 81 920 tris > UInt16.Max).
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(finalVerts);
            mesh.SetTriangles(tris, submesh: 0, calculateBounds: true);
            mesh.SetNormals(normals);
            // Spherical UVs \u2014 cheap mapping that lets shaders sample world-
            // direction noise without seam handling. Pole singularities are
            // fine for our use cases (we don't author textures with seams).
            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                uvs[i] = new Vector2(
                    0.5f + Mathf.Atan2(v.z, v.x) / (2f * Mathf.PI),
                    0.5f - Mathf.Asin(v.y) / Mathf.PI);
            }
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            if (displaced)
            {
                // Face-averaged smooth normals — cliffs read as cliffs,
                // rolling hills as rolling hills. RecalculateTangents
                // is unnecessary for our shaders (no normal maps).
                mesh.RecalculateNormals();
            }
            return mesh;
        }

        private static int Midpoint(int i0, int i1, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
            if (cache.TryGetValue(key, out int hit)) return hit;
            Vector3 m = ((verts[i0] + verts[i1]) * 0.5f).normalized;
            int idx = verts.Count;
            verts.Add(m);
            cache[key] = idx;
            return idx;
        }
    }
}
