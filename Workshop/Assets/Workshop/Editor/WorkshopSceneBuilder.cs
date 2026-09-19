using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks.Editor;
using OpusSystems.Workshop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpusSystems.Workshop.Editor
{
    /// <summary>
    /// Builds Assets/Workshop/Scenes/Workshop.unity from Meta's Building
    /// Blocks and our own panel, and applies the Project Setup Tool's fixes
    /// for Android, so the committed scene and settings never depend on
    /// anyone's editor session. From the CLI:
    ///   Unity -batchmode -projectPath . -executeMethod OpusSystems.Workshop.Editor.WorkshopSceneBuilder.Build -quit
    /// The Building Blocks install API is internal; this calls it by
    /// reflection, the same way Meta's own MCP bridge does.
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        private const string ScenePath = "Assets/Workshop/Scenes/Workshop.unity";

        // Meta's BlockDataIds is internal; these are its values (core 205).
        private const string CameraRig = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        private const string Passthrough = "f0540b20-dfd6-420e-b20d-c270f88dc77e";
        private const string HandTracking = "8b26b298-7bf4-490e-b245-a039c0184303";
        private const string RealHands = "f547fe18-d477-46ec-bdf5-7208df19cb98";
        private const string GrabbableItem = "5c5184f2-c2f5-4063-b14b-3b1264fb3c1a";

        /// <summary>
        /// Entry point for -executeMethod. Returns at once and lets the
        /// editor's own loop drive the async chain (the Project Setup Tool
        /// and Building Blocks complete on the main-thread sync context,
        /// which a blocking loop here would starve). Run WITHOUT -quit; the
        /// chain exits the editor itself.
        /// </summary>
        public static void Build()
        {
            _ = Run();
        }

        private static async Task Run()
        {
            try
            {
                await BuildAsync();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"WorkshopSceneBuilder failed: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static async Task BuildAsync()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Project configuration first (XR plugin, Android target, passthrough
            // capability, permissions) — the blocks validate against it.
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            await OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);

            await Install(CameraRig);
            await Install(Passthrough);
            await Install(HandTracking);
            await Install(RealHands);

            var panel = MakePanel();
            await Install(GrabbableItem, panel);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"WorkshopSceneBuilder: wrote {ScenePath}");
        }

        /// <summary>Install a block into the scene, or onto <paramref name="onto"/>.</summary>
        private static async Task Install(string blockId, GameObject onto = null)
        {
            // The registry is an internal static field; `Id` on each block is
            // public, so scan the field's values by reflection.
            var field = typeof(BlockBaseData).GetField("Registry", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new System.Exception("BlockBaseData.Registry not found — Meta SDK API changed");
            var registry = field.GetValue(null);
            var values = registry.GetType().GetProperty("Values")?.GetValue(registry) as System.Collections.IEnumerable;
            BlockData data = null;
            if (values != null)
                foreach (var v in values)
                    if (v is BlockData b && b.Id == blockId) { data = b; break; }
            if (data == null) throw new System.Exception($"block {blockId} not found in the registry");
            var m = typeof(BlockData).GetMethod("AddToProject", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) throw new System.Exception("BlockData.AddToProject not found — Meta SDK API changed");
            await (Task)m.Invoke(data, new object[] { onto, null });
            Debug.Log($"WorkshopSceneBuilder: installed {data.BlockName}");
        }

        /// <summary>
        /// A 60×40 cm translucent card 1.2 m in front of the user at eye
        /// height, with title/status/body text. Physics body + collider so
        /// the Grabbable block can take it.
        /// </summary>
        private static GameObject MakePanel()
        {
            var root = new GameObject("SessionPanel");
            root.transform.position = new Vector3(0, 1.4f, 1.2f);
            root.transform.rotation = Quaternion.identity;

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.6f, 0.4f, 0.02f);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(600, 400);
            canvasGo.transform.localScale = Vector3.one * 0.001f; // 1 unit = 1 mm
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2;
            canvasGo.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("Background", typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            Stretch(bg.GetComponent<RectTransform>());
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.05f, 0.12f, 0.18f, 0.72f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var title = MakeText(canvasGo.transform, "Title", font, 30, FontStyle.Bold, new Vector2(20, -20), new Vector2(-160, 44), TextAnchor.UpperLeft);
            title.color = new Color(0.7f, 0.9f, 1f);
            var status = MakeText(canvasGo.transform, "Status", font, 22, FontStyle.Normal, new Vector2(440, -24), new Vector2(-20, 36), TextAnchor.UpperRight);
            status.color = new Color(0.55f, 0.85f, 0.6f);
            var body = MakeText(canvasGo.transform, "Body", font, 22, FontStyle.Normal, new Vector2(20, -70), new Vector2(-20, -20), TextAnchor.UpperLeft);
            body.color = new Color(0.92f, 0.95f, 1f);

            var panel = root.AddComponent<SessionPanel>();
            panel.title = title;
            panel.status = status;
            panel.body = body;
            panel.Set("jarvis", "idle", "Hello, sir. Pinch to grab this panel and put it where you like.\n\nStage 2 binds it to a live session.");
            return root;
        }

        private static Text MakeText(Transform parent, string name, Font font, int size, FontStyle style, Vector2 offsetMin, Vector2 offsetMax, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(offsetMin.x, offsetMax.y < 0 ? offsetMax.y : 0);
            rt.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
            // Header rows: pin to the top with a fixed height.
            if (name != "Body")
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(offsetMin.x, offsetMin.y - offsetMax.y);
                rt.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
            }
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
