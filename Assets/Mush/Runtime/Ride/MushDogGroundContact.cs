using System.Collections.Generic;
using UnityEngine;

/// <summary>Tracks the skinned paw surface without baking the whole dog every frame.</summary>
internal sealed class MushDogGroundContact
{
    private struct PawVertex
    {
        public Vector3 bindPosition;
        public int firstWeight;
        public int weightCount;
    }

    private readonly Transform holder;
    private readonly SkinnedMeshRenderer renderer;
    private readonly Transform[] bones;
    private readonly Matrix4x4[] bindPoses;
    private readonly Matrix4x4[] skinMatrices;
    private readonly PawVertex[] vertices;
    private readonly BoneWeight1[] weights;

    private MushDogGroundContact(Transform holder, SkinnedMeshRenderer renderer,
        Transform[] bones, Matrix4x4[] bindPoses, PawVertex[] vertices, BoneWeight1[] weights)
    {
        this.holder = holder;
        this.renderer = renderer;
        this.bones = bones;
        this.bindPoses = bindPoses;
        this.vertices = vertices;
        this.weights = weights;
        skinMatrices = new Matrix4x4[bones.Length];
    }

    public static MushDogGroundContact Create(Transform holder, Transform visual)
    {
        SkinnedMeshRenderer renderer = visual.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh source = renderer != null ? renderer.sharedMesh : null;
        if (source == null || source.blendShapeCount != 0)
            return null;

        Transform[] bones = renderer.bones;
        Matrix4x4[] bindPoses = source.bindposes;
        if (bones.Length == 0 || bones.Length != bindPoses.Length)
            return null;
        var pawBones = new bool[bones.Length];
        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) return null;
            string name = bones[i].name.ToLowerInvariant();
            pawBones[i] = name.Contains("foot") || name.Contains("toe") || name.Contains("paw");
            matrices[i] = renderer.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * bindPoses[i];
        }

        // Match the renderer's influence limit, including Quest's two-bone quality.
        int influenceLimit = renderer.quality == SkinQuality.Auto
            ? (int)QualitySettings.skinWeights
            : (int)renderer.quality;
        influenceLimit = Mathf.Max(1, influenceLimit);
        var boneCounts = source.GetBonesPerVertex();
        var sourceWeights = source.GetAllBoneWeights();
        var pawVertices = new List<PawVertex>();
        var pawWeights = new List<BoneWeight1>();
        var unique = new HashSet<(Vector3Int position, int weights)>();
        Mesh snapshot = new() { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            // A single setup bake also works with non-readable imported meshes.
            renderer.BakeMesh(snapshot);
            Vector3[] positions = snapshot.vertices;
            int cursor = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                int count = Mathf.Min(boneCounts[i], influenceLimit);
                float totalWeight = 0f;
                float pawWeight = 0f;
                for (int j = 0; j < count; j++)
                {
                    BoneWeight1 weight = sourceWeights[cursor + j];
                    totalWeight += weight.weight;
                    if (pawBones[weight.boneIndex]) pawWeight += weight.weight;
                }
                if (totalWeight > 0f && pawWeight >= totalWeight * 0.5f)
                {
                    Matrix4x4 skin = Matrix4x4.zero;
                    int weightHash = count;
                    for (int j = 0; j < count; j++)
                    {
                        BoneWeight1 weight = sourceWeights[cursor + j];
                        float normalized = weight.weight / totalWeight;
                        for (int element = 0; element < 16; element++)
                            skin[element] += matrices[weight.boneIndex][element] * normalized;
                        weightHash = unchecked(weightHash * 31 + weight.boneIndex);
                        weightHash = unchecked(weightHash * 31 + Mathf.RoundToInt(normalized * 10000f));
                    }
                    // Recover bind-space paw vertices from the setup pose, avoiding
                    // a Read/Write import change just to access source vertices.
                    Vector3 bindPosition = skin.inverse.MultiplyPoint3x4(positions[i]);
                    Vector3Int positionKey = Vector3Int.RoundToInt(bindPosition * 1000f);
                    if (unique.Add((positionKey, weightHash)))
                    {
                        pawVertices.Add(new PawVertex
                        {
                            bindPosition = bindPosition,
                            firstWeight = pawWeights.Count,
                            weightCount = count,
                        });
                        for (int j = 0; j < count; j++)
                        {
                            BoneWeight1 weight = sourceWeights[cursor + j];
                            weight.weight /= totalWeight;
                            pawWeights.Add(weight);
                        }
                    }
                }
                cursor += boneCounts[i];
            }
        }
        finally
        {
            if (Application.isPlaying) Object.Destroy(snapshot);
            else Object.DestroyImmediate(snapshot);
        }

        return pawVertices.Count > 0
            ? new MushDogGroundContact(holder, renderer, bones, bindPoses, pawVertices.ToArray(), pawWeights.ToArray())
            : null;
    }

    public bool TryGetMinimumHeight(out float height)
    {
        height = float.PositiveInfinity;
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            return false;

        Matrix4x4 worldToHolder = holder.worldToLocalMatrix;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) return false;
            skinMatrices[i] = worldToHolder * bones[i].localToWorldMatrix * bindPoses[i];
        }
        foreach (PawVertex vertex in vertices)
        {
            float vertexHeight = 0f;
            for (int i = 0; i < vertex.weightCount; i++)
            {
                BoneWeight1 weight = weights[vertex.firstWeight + i];
                Matrix4x4 matrix = skinMatrices[weight.boneIndex];
                Vector3 p = vertex.bindPosition;
                vertexHeight += (matrix.m10 * p.x + matrix.m11 * p.y + matrix.m12 * p.z + matrix.m13) * weight.weight;
            }
            height = Mathf.Min(height, vertexHeight);
        }
        return !float.IsInfinity(height);
    }
}
