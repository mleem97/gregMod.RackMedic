using System;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace RackMedic.Rendering
{
    /// <summary>
    /// Parses binary STL files into a UnityEngine.Mesh at runtime.
    ///
    /// Binary STL format:
    ///   80-byte header (ignored)
    ///   uint32 triangle_count
    ///   Per triangle (50 bytes):
    ///     float[3] normal
    ///     float[3] vertex1
    ///     float[3] vertex2
    ///     float[3] vertex3
    ///     uint16   attribute_byte_count (ignored)
    /// </summary>
    public static class StlLoader
    {
        public static Mesh Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MelonLogger.Warning($"[RackMedic] StlLoader: file not found: {filePath}");
                return FallbackCube();
            }

            try
            {
                return ParseBinaryStl(File.ReadAllBytes(filePath));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] StlLoader: parse failed for {filePath}: {ex.Message}");
                return FallbackCube();
            }
        }

        private static Mesh ParseBinaryStl(byte[] data)
        {
            using var reader = new BinaryReader(new MemoryStream(data));

            // Skip 80-byte header
            reader.ReadBytes(80);

            uint triCount = reader.ReadUInt32();
            if (triCount == 0 || triCount > 2_000_000)
                throw new InvalidDataException($"Unreasonable triangle count: {triCount}");

            var verts   = new Vector3[triCount * 3];
            var normals = new Vector3[triCount * 3];
            var tris    = new int[triCount * 3];

            for (int i = 0; i < (int)triCount; i++)
            {
                float nx = reader.ReadSingle();
                float ny = reader.ReadSingle();
                float nz = reader.ReadSingle();
                var n = new Vector3(nx, ny, nz);

                for (int v = 0; v < 3; v++)
                {
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    float z = reader.ReadSingle();
                    int idx = i * 3 + v;
                    verts[idx]   = new Vector3(x, y, z);
                    normals[idx] = n;
                    tris[idx]    = idx;
                }

                reader.ReadUInt16(); // attribute byte count
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh FallbackCube()
        {
            var go   = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.Destroy(go);
            return mesh;
        }
    }
}
