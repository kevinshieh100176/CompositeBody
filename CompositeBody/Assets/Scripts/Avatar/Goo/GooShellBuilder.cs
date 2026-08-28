using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Avatar.Goo
{
    /// <summary>
    /// Builds the goo layer that wraps a character: a copy of the body mesh pushed out along its
    /// normals, carrying two extra pieces of per-vertex data the shader needs.
    ///
    /// - <b>UV1.x</b> is how much room that vertex has between the body and the outer shell.
    ///   The shader wobbles the surface only inside that gap, so the goo can never pass through
    ///   the body and visibly flattens wherever it bottoms out against it.
    /// - <b>UV2.xyz</b> is the bind-pose body position, which anchors the noise to the body.
    ///   Sampling noise from the live (skinned) position instead would make the body appear to
    ///   swim through a stationary blob field whenever it moves.
    ///
    /// Bone weights and bind poses are copied straight across, so the shell skins with the body.
    /// </summary>
    public static class GooShellBuilder
    {
        /// <summary>
        /// Creates the shell mesh. <paramref name="sourceMesh"/> must be readable; when driving
        /// this from the editor an imported FBX mesh works even with Read/Write disabled.
        /// </summary>
        public static Mesh Build(Mesh sourceMesh, float baseThickness, float thicknessVariation, float variationScale)
        {
            var srcVerts = sourceMesh.vertices;
            var srcNormals = sourceMesh.normals;

            if (srcNormals == null || srcNormals.Length != srcVerts.Length)
            {
                sourceMesh.RecalculateNormals();
                srcNormals = sourceMesh.normals;
            }

            int count = srcVerts.Length;
            var shellVerts = new Vector3[count];
            var thicknessData = new Vector2[count];
            var restPositions = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 n = srcNormals[i].normalized;

                // Vary thickness so the coating reads as an uneven gooey layer rather than a
                // uniformly inflated copy of the body.
                float variation = Mathf.PerlinNoise(srcVerts[i].x * variationScale + 13.7f,
                                                    srcVerts[i].y * variationScale + 4.2f);
                float thickness = baseThickness * Mathf.Lerp(1f - thicknessVariation, 1f + thicknessVariation, variation);
                thickness = Mathf.Max(thickness, 1e-4f);

                shellVerts[i] = srcVerts[i] + n * thickness;
                thicknessData[i] = new Vector2(thickness, 0f);
                restPositions.Add(srcVerts[i]);
            }

            var shell = new Mesh
            {
                name = sourceMesh.name + "_GooShell",
                indexFormat = count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            shell.vertices = shellVerts;
            shell.normals = srcNormals;
            shell.uv = sourceMesh.uv;
            shell.uv2 = thicknessData;
            shell.SetUVs(2, restPositions); // 3-component channel: bind-pose body position

            shell.subMeshCount = sourceMesh.subMeshCount;
            for (int s = 0; s < sourceMesh.subMeshCount; s++)
                shell.SetTriangles(sourceMesh.GetTriangles(s), s);

            // Copy skinning so the shell follows the same skeleton as the body.
            shell.boneWeights = sourceMesh.boneWeights;
            shell.bindposes = sourceMesh.bindposes;

            shell.RecalculateBounds();
            return shell;
        }
    }
}
