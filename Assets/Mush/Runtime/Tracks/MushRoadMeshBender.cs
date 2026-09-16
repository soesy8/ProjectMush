using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

/// <summary>Editor-time mesh bending. Work and output are bounded before replacing scene content.</summary>
public static class MushRoadMeshBender
{
    public const int VertexBudget = 240000;
    public delegate void RouteFrame(float distance, out Vector3 center, out Vector3 right);

    private struct Vertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new()
        {
            position = Vector3.Lerp(a.position, b.position, t),
            normal = Vector3.Lerp(a.normal, b.normal, t).normalized,
            uv = Vector2.Lerp(a.uv, b.uv, t),
        };
    }

    public static Mesh Bend(MeshFilter source, Transform model, float minZ, float maxZ,
        float centerX, float baseY, float widthScale, float start, float length,
        float sampleSpacing, RouteFrame frame, ref int remainingVertices)
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Road meshes must be baked in edit mode.");
        Mesh input = source.sharedMesh;
        if (input.vertexCount > remainingVertices)
            throw new InvalidOperationException("도로 원본 메시가 생성 정점 예산보다 큽니다.");
#if UNITY_EDITOR
        using var snapshot = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(input);
#else
        using var snapshot = Mesh.AcquireReadOnlyMeshData(input);
#endif
        Mesh.MeshData data = snapshot[0];
        using var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
        using var normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
        using var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
        data.GetVertices(positions);
        bool hasNormals = data.HasVertexAttribute(VertexAttribute.Normal);
        if (hasNormals) data.GetNormals(normals);
        if (data.HasVertexAttribute(VertexAttribute.TexCoord0)) data.GetUVs(0, uv);
        Matrix4x4 matrix = model.worldToLocalMatrix * source.transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = matrix.inverse.transpose;
        var vertices = new Vertex[positions.Length];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new Vertex
            {
                position = matrix.MultiplyPoint3x4(positions[i]),
                normal = normalMatrix.MultiplyVector(hasNormals ? normals[i] : Vector3.up).normalized,
                uv = uv[i],
            };

        float sourceLength = maxZ - minZ;
        int slices = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Min(2f, sampleSpacing)), 1, 64);
        var output = new List<Vector3>();
        var outputNormals = new List<Vector3>();
        var outputUv = new List<Vector2>();
        var submeshes = new List<int>[input.subMeshCount];
        var polygon = new List<Vertex>(8);
        var clipped = new List<Vertex>(8);
        for (int submesh = 0; submesh < input.subMeshCount; submesh++)
        {
            if (input.GetTopology(submesh) != MeshTopology.Triangles)
                throw new InvalidOperationException("도로 모델에는 삼각형 메시가 필요합니다.");
            using var indices = new NativeArray<int>(data.GetSubMesh(submesh).indexCount, Allocator.Temp);
            data.GetIndices(indices, submesh);
            var triangles = new List<int>();
            submeshes[submesh] = triangles;
            for (int t = 0; t < indices.Length; t += 3)
            {
                Vertex a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                float lo = Mathf.Min(a.position.z, Mathf.Min(b.position.z, c.position.z));
                float hi = Mathf.Max(a.position.z, Mathf.Max(b.position.z, c.position.z));
                int first = Mathf.Clamp(Mathf.FloorToInt((lo - minZ) / sourceLength * slices), 0, slices - 1);
                int last = Mathf.Clamp(Mathf.FloorToInt((hi - minZ) / sourceLength * slices), 0, slices - 1);
                for (int slice = first; slice <= last; slice++)
                {
                    polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                    Clip(polygon, clipped, Mathf.Lerp(minZ, maxZ, slice / (float)slices), true);
                    Clip(clipped, polygon, Mathf.Lerp(minZ, maxZ, (slice + 1f) / slices), false);
                    if (polygon.Count < 3) continue;
                    if (output.Count + polygon.Count > remainingVertices)
                        throw new InvalidOperationException("도로 모델의 생성 정점 예산(240,000개)을 초과했습니다. 더 단순한 모델 또는 기본 도로를 사용해 주세요.");
                    int offset = output.Count;
                    foreach (Vertex vertex in polygon)
                    {
                        float distance = start + (maxZ - vertex.position.z) / sourceLength * length;
                        frame(distance, out Vector3 center, out Vector3 right);
                        frame(Mathf.Max(0f, distance - 0.05f), out Vector3 before, out _);
                        frame(distance + 0.05f, out Vector3 after, out _);
                        Vector3 forward = (after - before).normalized;
                        Vector3 up = Vector3.Cross(forward, right).normalized;
                        if (up.sqrMagnitude < 0.001f) up = Vector3.up;
                        // Source +Z faces the start of the module; invert X as well to preserve winding.
                        output.Add(center - right * ((vertex.position.x - centerX) * widthScale)
                            + up * ((vertex.position.y - baseY) * model.localScale.y + 0.10f));
                        Vector3 n = vertex.normal;
                        outputNormals.Add((-right * (n.x / widthScale) + up * (n.y / Mathf.Max(0.001f, model.localScale.y))
                            - forward * (n.z * sourceLength / length)).normalized);
                        outputUv.Add(vertex.uv);
                    }
                    for (int i = 1; i < polygon.Count - 1; i++)
                    {
                        triangles.Add(offset); triangles.Add(offset + i); triangles.Add(offset + i + 1);
                    }
                }
            }
        }
        remainingVertices -= output.Count;
        Mesh mesh = new() { name = "Baked Road " + input.name,
            indexFormat = output.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(output);
        mesh.SetNormals(outputNormals);
        mesh.SetUVs(0, outputUv);
        mesh.subMeshCount = submeshes.Length;
        for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i, false);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static void Clip(List<Vertex> input, List<Vertex> output, float z, bool keepAbove)
    {
        output.Clear();
        if (input.Count == 0) return;
        Vertex previous = input[^1];
        bool previousInside = keepAbove ? previous.position.z >= z : previous.position.z <= z;
        foreach (Vertex current in input)
        {
            bool inside = keepAbove ? current.position.z >= z : current.position.z <= z;
            if (inside != previousInside)
                output.Add(Vertex.Lerp(previous, current, (z - previous.position.z) / (current.position.z - previous.position.z)));
            if (inside) output.Add(current);
            previous = current;
            previousInside = inside;
        }
    }
}
