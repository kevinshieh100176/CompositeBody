using System.Collections.Generic;
using UnityEngine;

namespace CompositeBody.Avatar.Skin
{
    /// <summary>
    /// Builds the second-skin garment layer: a copy of the body mesh pushed out along its
    /// normals, carrying its own bind-pose position in <b>TEXCOORD2</b>.
    ///
    /// That channel is the entire basis of the tension shader, and it has to be the *shell's*
    /// undeformed position rather than the body's. Storing the body position instead -- which
    /// is what <see cref="Goo.GooShellBuilder"/> does, correctly, for its own purposes -- makes
    /// the offset itself read as permanent stretch: inflating a surface by a fixed distance
    /// grows its area in proportion to curvature, so a 6 mm offset measures as roughly 12%
    /// stretch on a shin and 75% on a finger before the avatar has moved at all.
    ///
    /// Bone weights and bind poses are copied straight across, so the garment skins with the
    /// body and needs no simulation to follow it.
    /// </summary>
    public static class TensionSkinBuilder
    {
        /// <summary>
        /// Creates the garment mesh. <paramref name="sourceMesh"/> must be readable; when
        /// driving this from the editor an imported FBX mesh works even with Read/Write off.
        /// </summary>
        public static Mesh Build(Mesh sourceMesh, float offset)
        {
            var srcVerts = sourceMesh.vertices;
            var srcNormals = sourceMesh.normals;

            if (srcNormals == null || srcNormals.Length != srcVerts.Length)
            {
                sourceMesh.RecalculateNormals();
                srcNormals = sourceMesh.normals;
            }

            int count = srcVerts.Length;
            var verts = new Vector3[count];
            var restPositions = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 p = srcVerts[i] + srcNormals[i].normalized * offset;
                verts[i] = p;

                // The garment's own rest surface. Measured against this, an unposed avatar
                // reports exactly 1.0 stretch everywhere, which is the baseline the shader's
                // whole response curve is hung off.
                restPositions.Add(p);
            }

            var skin = new Mesh
            {
                name = sourceMesh.name + "_TensionSkin",
                indexFormat = count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            skin.vertices = verts;
            skin.normals = srcNormals;
            skin.uv = sourceMesh.uv;
            skin.SetUVs(2, restPositions);

            skin.subMeshCount = sourceMesh.subMeshCount;
            for (int s = 0; s < sourceMesh.subMeshCount; s++)
                skin.SetTriangles(sourceMesh.GetTriangles(s), s);

            // The shader bends the normal along the tangent frame for knit relief and for the
            // creases in compressed cloth, so tangents have to survive the copy.
            var srcTangents = sourceMesh.tangents;
            if (srcTangents != null && srcTangents.Length == count)
                skin.tangents = srcTangents;
            else
                skin.RecalculateTangents();

            skin.boneWeights = sourceMesh.boneWeights;
            skin.bindposes = sourceMesh.bindposes;

            skin.RecalculateBounds();
            return skin;
        }
    }
}
