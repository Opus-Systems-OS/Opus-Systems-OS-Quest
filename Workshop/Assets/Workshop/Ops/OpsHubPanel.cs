using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpusSystems.Api;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The Opus launcher: every layer of the stack as a row — a state dot,
    /// the name, one headline line — from `GET /v1/ops`, refreshed every
    /// 30 s. Each row is a button that opens (or focuses) that service's
    /// panel; Jarvis's `open_panel` does the same by voice.
    /// </summary>
    public sealed class OpsHubPanel : MonoBehaviour
    {
        public Action<string> OnOpen;

        private SessionPanel _panel;
        private OpusClient _api;
        private Transform _rows;
        private Font _font;
        private readonly Dictionary<string, (Image dot, Text name, Text line)> _rowUi = new Dictionary<string, (Image, Text, Text)>();
        private float _nextRefresh;

        public static OpsHubPanel Attach(SessionPanel panel, OpusClient api)
        {
            var h = panel.gameObject.AddComponent<OpsHubPanel>();
            h._panel = panel;
            h._api = api;
            h.Build();
            return h;
        }

        private void Build()
        {
            var canvas = _panel.GetComponentInChildren<Canvas>().transform;
            if (_panel.body) _panel.body.gameObject.SetActive(false);
            _panel.Set("Opus Systems", "…", "");
            _font = _panel.title ? _panel.title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rows = new GameObject("Rows", typeof(RectTransform));
            rows.transform.SetParent(canvas, false);
            Ui.Place(rows.GetComponent<RectTransform>(), 20, -66, 560, 320);
            _rows = rows.transform;
        }

        private void Update()
        {
            if (_api == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 30f;
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            try
            {
                var hub = await _api.OpsAsync();
                MainThread.Run(() => Apply(hub));
            }
            catch (Exception e)
            {
                MainThread.Run(() => _panel.Set("Opus Systems", "error", e.Message));
            }
        }

        private void Apply(JObject hub)
        {
            var services = hub["services"] as JArray ?? new JArray();
            var i = 0;
            var worst = "ok";
            foreach (var s in services)
            {
                var id = s.Value<string>("id") ?? "";
                var state = s.Value<string>("state") ?? "unknown";
                if (!_rowUi.ContainsKey(id)) _rowUi[id] = Row(id, i);
                var (dot, name, line) = _rowUi[id];
                dot.color = Ui.StateColor(state);
                name.text = s.Value<string>("name") ?? id;
                line.text = s.Value<string>("headline") ?? "";
                if (state == "down" || (state == "warn" && worst == "ok")) worst = state;
                i++;
            }
            _panel.Set("Opus Systems", $"{services.Count} layers · {worst}", "");
            if (_panel.status) _panel.status.color = Ui.StateColor(worst);
        }

        private (Image, Text, Text) Row(string id, int index)
        {
            const float h = 50, gap = 4;
            var y = -index * (h + gap);
            var go = new GameObject("Row " + id, typeof(Image), typeof(Button));
            go.transform.SetParent(_rows, false);
            Ui.Place(go.GetComponent<RectTransform>(), 0, y, 560, h);
            go.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.35f, 0.6f, 0.8f, 0.5f);
            colors.pressedColor = new Color(0.6f, 0.9f, 1f, 0.7f);
            b.colors = colors;
            b.onClick.AddListener(() => OnOpen?.Invoke(id));

            var dotGo = new GameObject("Dot", typeof(Image));
            dotGo.transform.SetParent(go.transform, false);
            Ui.Place(dotGo.GetComponent<RectTransform>(), 12, -17, 16, 16);
            var name = Ui.Label(go.transform, _font, 22, FontStyle.Bold, 40, -4, 150, 42, new Color(0.95f, 0.97f, 1f));
            name.alignment = TextAnchor.MiddleLeft;
            var line = Ui.Label(go.transform, _font, 17, FontStyle.Normal, 190, -4, 360, 42, new Color(0.75f, 0.82f, 0.9f));
            line.alignment = TextAnchor.MiddleLeft;
            return (dotGo.GetComponent<Image>(), name, line);
        }
    }

    /// <summary>Shared bits for the runtime-built panel UIs.</summary>
    public static class Ui
    {
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static Text Label(Transform parent, Font font, int size, FontStyle style, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.color = color;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button Button(Transform parent, Font font, string label, float x, float y, float w, float h, Color color, Action onClick)
        {
            var go = new GameObject("Button " + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Image>().color = color;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.35f, 0.6f, 0.8f);
            colors.pressedColor = new Color(0.6f, 0.9f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var tgo = new GameObject("Text", typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>();
            t.font = font; t.fontSize = 24; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white; t.text = label;
            return b;
        }

        public static Color StateColor(string state) => state switch
        {
            "ok" => new Color(0.45f, 0.9f, 0.55f),
            "warn" => new Color(1f, 0.8f, 0.35f),
            "down" => new Color(1f, 0.4f, 0.4f),
            _ => new Color(0.6f, 0.65f, 0.7f),
        };
    }
}
