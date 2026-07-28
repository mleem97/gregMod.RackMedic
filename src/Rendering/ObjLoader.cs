using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace RackMedic.Rendering
{
    /// <summary>
    /// Parses Wavefront OBJ text files into a UnityEngine.Mesh at runtime.
    /// No Unity Editor or asset pipeline needed.
    ///
    /// Usage:
    ///   var mesh = ObjLoader.Load("/path/to/model.obj");
    ///   var go   = new GameObject("MyModel");
    ///   go.AddComponent<MeshFilter>().mesh = mesh;
    ///   go.AddComponent<MeshRenderer>().material = mat;
    /// </summary>
    public static class ObjLoader
    {
        /// <summary>
        /// Load and parse an OBJ file. Returns a UnityEngine.Mesh on success,
        /// or a fallback cube mesh if the file is missing or unparseable.
        /// </summary>
        public static Mesh Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MelonLogger.Warning($"[RackMedic] ObjLoader: file not found: {filePath}");
                return FallbackCube();
            }

            try
            {
                return ParseObj(File.ReadAllText(filePath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] ObjLoader: parse failed for {filePath}: {ex.Message}");
                return FallbackCube();
            }
        }

        /// <summary>
        /// Load a PNG or JPG texture from disk.
        /// Returns a 1×1 white texture if the file is missing.
        /// </summary>
        public static Texture2D LoadTexture(string filePath)
        {
            if (!File.Exists(filePath))
                return Texture2D.whiteTexture;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                    return tex;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] LoadTexture failed for {filePath}: {ex.Message}");
            }

            return Texture2D.whiteTexture;
        }

        // ── Parser ────────────────────────────────────────────────────────────

        private static Mesh ParseObj(string src)
        {
            var verts  = new List<Vector3>();
            var uvs    = new List<Vector2>();
            var norms  = new List<Vector3>();

            // Face lists — each face entry = (vi, uvi, ni) where each is 0-based index into verts/uvs/norms
            var faceVerts  = new List<int>();
            var faceUvs    = new List<int>();
            var faceNormals= new List<int>();

            foreach (string raw in src.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("v "))
                {
                    var parts = line.Substring(2).Split(new[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                        verts.Add(new Vector3(
                            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));
                }
                else if (line.StartsWith("vt "))
                {
                    var parts = line.Substring(3).Split(new[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        uvs.Add(new Vector2(
                            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
                }
                else if (line.StartsWith("vn "))
                {
                    var parts = line.Substring(3).Split(new[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                        norms.Add(new Vector3(
                            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));
                }
                else if (line.StartsWith("f "))
                {
                    // Triangulate polygon: fan from first vertex
                    var tokens = line.Substring(2).Split(new[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 3) continue;

                    int[] vi = new int[tokens.Length];
                    int[] ti = new int[tokens.Length];
                    int[] ni = new int[tokens.Length];
                    for (int i = 0; i < tokens.Length; i++)
                        ParseFaceToken(tokens[i], out vi[i], out ti[i], out ni[i]);

                    // Fan triangulation
                    for (int i = 1; i < tokens.Length - 1; i++)
                    {
                        faceVerts.Add(vi[0]);  faceUvs.Add(ti[0]);  faceNormals.Add(ni[0]);
                        faceVerts.Add(vi[i]);  faceUvs.Add(ti[i]);  faceNormals.Add(ni[i]);
                        faceVerts.Add(vi[i+1]);faceUvs.Add(ti[i+1]);faceNormals.Add(ni[i+1]);
                    }
                }
            }

            // Build Unity vertex/triangle arrays
            int triCount = faceVerts.Count;
            var meshVerts   = new Vector3[triCount];
            var meshUvs     = new Vector2[triCount];
            var meshNormals = new Vector3[triCount];
            var tris        = new int[triCount];

            for (int i = 0; i < triCount; i++)
            {
                int vi = faceVerts[i];
                meshVerts[i] = (vi >= 0 && vi < verts.Count) ? verts[vi] : Vector3.zero;

                int ti = faceUvs[i];
                meshUvs[i] = (ti >= 0 && ti < uvs.Count) ? uvs[ti] : Vector2.zero;

                int ni = faceNormals[i];
                meshNormals[i] = (ni >= 0 && ni < norms.Count) ? norms[ni] : Vector3.up;

                tris[i] = i;
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices    = meshVerts;
            mesh.uv          = meshUvs;
            mesh.normals     = meshNormals;
            mesh.triangles   = tris;

            if (norms.Count == 0) mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <param name="token">OBJ face token: "1", "1/2", "1/2/3", or "1//3"</param>
        private static void ParseFaceToken(string token, out int v, out int t, out int n)
        {
            v = t = n = -1;
            var parts = token.Split('/');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int vi)) v = vi - 1;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int ti)) t = ti - 1;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int ni)) n = ni - 1;
        }

        // ── Fallback ──────────────────────────────────────────────────────────

        private static Mesh FallbackCube()
        {
            // A simple 1m³ cube
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.Destroy(go);
            return mesh;
        }
    }
}
