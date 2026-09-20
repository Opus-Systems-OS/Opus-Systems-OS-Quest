using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// STL (binary or ASCII) → Mesh. Print files are in millimetres; the
    /// mesh comes back in metres, flat-shaded (one vertex per corner, as the
    /// format stores it), Y up (STL's Z is up). Big prints exceed 65k
    /// vertices, so the index format is 32-bit.
    /// </summary>
    public static class StlLoader
    {
        public static Mesh Load(byte[] bytes, string name = "stl")
        {
            var mesh = IsAscii(bytes) ? ParseAscii(bytes) : ParseBinary(bytes);
            mesh.name = name;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsAscii(byte[] b)
        {
            if (b.Length < 84) return true;
            // A binary file's header may start with "solid" too; trust the
            // triangle count instead: 84 + 50 * n must be the file size.
            var n = BitConverter.ToUInt32(b, 80);
            return 84 + 50L * n != b.Length;
        }

        private static Mesh ParseBinary(byte[] b)
        {
            var n = (int)BitConverter.ToUInt32(b, 80);
            var verts = new Vector3[n * 3];
            var tris = new int[n * 3];
            var o = 84;
            for (var i = 0; i < n; i++)
            {
                o += 12; // facet normal; recomputed from the winding
                for (var c = 0; c < 3; c++)
                {
                    var v = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 8), BitConverter.ToSingle(b, o + 4));
                    verts[i * 3 + c] = v * 0.001f;
                    o += 12;
                }
                // Z-up right-handed → Y-up left-handed flips the winding.
                tris[i * 3] = i * 3; tris[i * 3 + 1] = i * 3 + 2; tris[i * 3 + 2] = i * 3 + 1;
                o += 2; // attribute byte count
            }
            return Build(verts, tris);
        }

        private static Mesh ParseAscii(byte[] b)
        {
            var verts = new List<Vector3>();
            using var reader = new StringReader(Encoding.ASCII.GetString(b));
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (!t.StartsWith("vertex")) continue;
                var p = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) continue;
                verts.Add(new Vector3(F(p[1]), F(p[3]), F(p[2])) * 0.001f);
            }
            var n = verts.Count / 3;
            var tris = new int[n * 3];
            for (var i = 0; i < n; i++) { tris[i * 3] = i * 3; tris[i * 3 + 1] = i * 3 + 2; tris[i * 3 + 2] = i * 3 + 1; }
            return Build(verts.ToArray(), tris);
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static Mesh Build(Vector3[] verts, int[] tris)
        {
            var mesh = new Mesh { indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
