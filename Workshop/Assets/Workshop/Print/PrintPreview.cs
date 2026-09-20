using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// A print on a stand. The stand is a small SessionPanel (grab it to
    /// move or turn the whole thing); the model floats above it, scaled to
    /// fit a hand-sized box, turning slowly until you pick the stand up.
    /// STL through <see cref="StlLoader"/>; GLB/glTF through glTFast. The
    /// source is a file in <see cref="ModelLibrary"/> or an http(s) URL.
    /// </summary>
    public sealed class PrintPreview : MonoBehaviour
    {
        public SessionPanel stand;
        public float fitSize = 0.22f;
        public float spinDegreesPerSecond = 20f;

        private Transform _mount;
        private Transform _model;
        private Vector3 _sizeMm;
        private int _triangles;

        /// <summary>What the stand shows and the tool reports.</summary>
        public string Summary { get; private set; } = "";

        private void Update()
        {
            if (_model) _model.Rotate(0, spinDegreesPerSecond * Time.deltaTime, 0, Space.Self);
        }

        private int _loadSerial;

        public async Task LoadAsync(string source)
        {
            var name = Path.GetFileName(source);
            var serial = ++_loadSerial;
            stand?.Set("print", "loading…", name);
            _model = null;
            foreach (Transform old in Mount()) Destroy(old.gameObject);
            try
            {
                var bytes = await ReadAsync(source);
                var holder = new GameObject("Model");
                holder.transform.SetParent(Mount(), false);
                var ext = Path.GetExtension(source).ToLowerInvariant();
                if (ext == ".stl")
                {
                    var mesh = StlLoader.Load(bytes, name);
                    var go = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(holder.transform, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("PrintMaterial");
                    _triangles = mesh.triangles.Length / 3;
                }
                else
                {
                    var gltf = new GltfImport();
                    if (!await gltf.Load(bytes, new Uri(source.StartsWith("http") ? source : "file://" + source)))
                        throw new Exception("glTF failed to load");
                    // Meshes only: sample files carry cameras and lights, and
                    // a second Camera in a VR scene draws the room twice.
                    var instantiator = new GameObjectInstantiator(gltf, holder.transform,
                        settings: new InstantiationSettings { Mask = ComponentType.Mesh });
                    if (!await gltf.InstantiateMainSceneAsync(instantiator))
                        throw new Exception("glTF failed to instantiate");
                    foreach (var cam in holder.GetComponentsInChildren<Camera>()) Destroy(cam);
                    foreach (var light in holder.GetComponentsInChildren<Light>()) Destroy(light);
                    // A print is one material; glTF's PBR shaders aren't in the build anyway.
                    var print = Resources.Load<Material>("PrintMaterial");
                    foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.sharedMaterial = print;
                    _triangles = 0;
                    foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
                        if (mf.sharedMesh) _triangles += mf.sharedMesh.triangles.Length / 3;
                }
                if (serial != _loadSerial) { Destroy(holder); return; } // a newer load superseded this one
                Fit(holder.transform);
                _model = holder.transform;
                Summary = $"{name}: {_sizeMm.x:0}×{_sizeMm.z:0}×{_sizeMm.y:0} mm (w×d×h), {_triangles:n0} triangles";
                stand?.Set("print", "ready", Summary.Replace(": ", "\n"));
            }
            catch (Exception e)
            {
                Summary = $"{name}: {e.Message}";
                stand?.Set("print", "failed", e.Message);
                throw;
            }
        }

        private static async Task<byte[]> ReadAsync(string source)
        {
            if (source.StartsWith("http://") || source.StartsWith("https://"))
            {
                using var req = UnityWebRequest.Get(source);
                req.timeout = 60;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success) throw new Exception($"{req.responseCode} {req.error}");
                return req.downloadHandler.data;
            }
            if (!File.Exists(source)) throw new FileNotFoundException("no such model", source);
            return await Task.Run(() => File.ReadAllBytes(source));
        }

        /// <summary>Centre the model over the stand and scale it to fit.</summary>
        private void Fit(Transform holder)
        {
            var renderers = holder.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new Exception("model has no geometry");
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            _sizeMm = b.size * 1000f;
            var longest = Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-4f);
            var scale = fitSize / longest;
            holder.localScale = Vector3.one * scale;
            // Bounds were world-space at scale 1 under this (unscaled) holder:
            // shift so the fitted box sits centred just above the stand.
            var centre = holder.InverseTransformPoint(b.center) * scale;
            var half = b.size.y * scale * 0.5f;
            holder.localPosition = new Vector3(-centre.x, 0.03f + half - centre.y, -centre.z);
        }

        /// <summary>
        /// A child at the stand's top edge with world scale 1, so the model
        /// is placed in metres regardless of the canvas's millimetre scale.
        /// </summary>
        private Transform Mount()
        {
            if (_mount) return _mount;
            var go = new GameObject("Mount");
            go.transform.SetParent(transform, false);
            var ls = transform.lossyScale;
            go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            var rt = GetComponent<RectTransform>();
            go.transform.localPosition = new Vector3(0, rt ? rt.rect.height * 0.5f : 0, 0);
            _mount = go.transform;
            return _mount;
        }
    }
}
