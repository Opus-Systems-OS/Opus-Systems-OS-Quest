using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// Print files on the headset. The folder is the app's external files
    /// directory, so from the Mac:
    ///   adb push benchy.stl /sdcard/Android/data/dev.opustower.workshop/files/models/
    /// Names are matched loosely ("benchy" finds "3DBenchy_v2.stl").
    /// </summary>
    public static class ModelLibrary
    {
        private static readonly string[] Extensions = { ".stl", ".glb", ".gltf" };

        public static string Folder => Path.Combine(Application.persistentDataPath, "models");

        public static string[] List()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                return Directory.GetFiles(Folder)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n)
                    .ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"models: {e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>Full path of the best match for a loose name, or null.</summary>
        public static string Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var wanted = Simplify(name);
            var files = List();
            var hit = files.FirstOrDefault(f => Simplify(f) == wanted)
                      ?? files.FirstOrDefault(f => Simplify(Path.GetFileNameWithoutExtension(f)) == wanted)
                      ?? files.FirstOrDefault(f => Simplify(f).Contains(wanted));
            return hit == null ? null : Path.Combine(Folder, hit);
        }

        private static string Simplify(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
