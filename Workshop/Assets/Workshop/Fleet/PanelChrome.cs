using System;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The frame every panel shares: a title bar you point at to move the
    /// panel (see <see cref="PanelMover"/>), a ⌖ button that turns it to
    /// face you, and a × that closes it. Built at runtime onto the panel's
    /// canvas so every prefab instance gets it.
    /// </summary>
    public sealed class PanelChrome : MonoBehaviour
    {
        public const float BarHeight = 44f;

        public Action OnClose;
        public PanelMover Mover { get; private set; }

        public static PanelChrome Attach(SessionPanel panel)
        {
            var c = panel.gameObject.AddComponent<PanelChrome>();
            c.Build(panel);
            return c;
        }

        private void Build(SessionPanel panel)
        {
            var canvas = panel.GetComponentInChildren<Canvas>();
            var ct = canvas.transform;
            var rt = canvas.GetComponent<RectTransform>();
            var w = rt.rect.width;
            var h = rt.rect.height;
            var font = panel.title ? panel.title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // The bar's look: a strip behind the title.
            var bar = new GameObject("TitleBar", typeof(Image));
            bar.transform.SetParent(ct, false);
            var brt = bar.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.offsetMin = new Vector2(0, -BarHeight); brt.offsetMax = new Vector2(0, 0);
            bar.GetComponent<Image>().color = new Color(1, 1, 1, 0.09f);
            bar.transform.SetSiblingIndex(1); // just above the background

            // The bar's grab surface: its own plane, 5 mm proud of the canvas
            // so the ray picks it over the content underneath.
            var handle = new GameObject("Handle");
            handle.transform.SetParent(ct, false);
            handle.transform.localPosition = new Vector3(0, h * 0.5f - BarHeight * 0.5f, -5f);
            var plane = handle.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            var clipper = handle.AddComponent<BoundsClipper>();
            clipper.Size = new Vector3(w - 110f, BarHeight, 0.01f); // leaves the buttons out
            clipper.Position = new Vector3(-55f, 0, 0);
            var clipped = handle.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });
            var pointable = handle.AddComponent<PointableElement>();
            var ray = handle.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalPointableElement(pointable);

            Mover = panel.gameObject.AddComponent<PanelMover>();
            Mover.Attach(pointable, handle.transform);

            // Title and status live in the bar; status makes room for the buttons.
            if (panel.title) { var t = panel.title.rectTransform; t.offsetMin = new Vector2(16, -BarHeight); t.offsetMax = new Vector2(-260, 0); panel.title.alignment = TextAnchor.MiddleLeft; panel.title.fontSize = 26; }
            if (panel.status) { var s = panel.status.rectTransform; s.offsetMin = new Vector2(220, -BarHeight); s.offsetMax = new Vector2(-104, 0); panel.status.alignment = TextAnchor.MiddleRight; panel.status.fontSize = 19; }

            Ui.Button(ct, font, "⌖", w - 100, -4, 44, BarHeight - 8, new Color(0.2f, 0.36f, 0.5f, 0.9f), () => Mover.FaceMe());
            Ui.Button(ct, font, "×", w - 50, -4, 44, BarHeight - 8, new Color(0.5f, 0.24f, 0.24f, 0.9f), () => OnClose?.Invoke());
        }
    }
}
