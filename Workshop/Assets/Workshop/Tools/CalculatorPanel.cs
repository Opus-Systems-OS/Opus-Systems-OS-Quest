using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// A calculator on a panel: a display, a short tape of past results and
    /// a keypad you point at and pinch. Evaluation is <see cref="Calc"/>,
    /// local and instant; Jarvis's `calculate` tool lands here too, so a
    /// spoken sum shows up on the tape. Built onto a SessionPanel's canvas.
    /// </summary>
    public sealed class CalculatorPanel : MonoBehaviour
    {
        private SessionPanel _panel;
        private Text _display, _tape;
        private string _expr = "";
        private readonly List<string> _history = new List<string>();
        private bool _justEvaluated;

        private static readonly string[][] Keys =
        {
            new[] { "C", "⌫", "(", ")", "√" },
            new[] { "7", "8", "9", "÷", "%" },
            new[] { "4", "5", "6", "×", "^" },
            new[] { "1", "2", "3", "−", "=" },
            new[] { "0", ".", "00", "+", "=" },
        };

        public static CalculatorPanel Attach(SessionPanel panel)
        {
            var c = panel.gameObject.AddComponent<CalculatorPanel>();
            c._panel = panel;
            c.Build();
            return c;
        }

        private void Build()
        {
            var canvas = _panel.GetComponentInChildren<Canvas>().transform;
            if (_panel.body) _panel.body.gameObject.SetActive(false);
            _panel.Set("calculator", "", "");
            var font = _panel.title ? _panel.title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var displayBg = new GameObject("DisplayBg", typeof(Image));
            displayBg.transform.SetParent(canvas, false);
            Place(displayBg.GetComponent<RectTransform>(), 20, -62, 560, 62);
            displayBg.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
            _display = Label(canvas, font, 34, FontStyle.Bold, 30, -66, 540, 54, new Color(0.95f, 0.97f, 1f));
            _display.alignment = TextAnchor.MiddleRight;
            _display.horizontalOverflow = HorizontalWrapMode.Overflow;

            _tape = Label(canvas, font, 17, FontStyle.Normal, 20, -134, 200, 250, new Color(0.7f, 0.8f, 0.9f));
            _tape.alignment = TextAnchor.LowerLeft;

            // Keypad: 5 × 5, "=" tall on the right.
            const float x0 = 236, y0 = -134, w = 62, h = 46, gap = 8;
            var placedEquals = false;
            for (var r = 0; r < Keys.Length; r++)
            for (var c = 0; c < Keys[r].Length; c++)
            {
                var key = Keys[r][c];
                if (key == "=")
                {
                    if (placedEquals) continue;
                    placedEquals = true;
                    Key(canvas, font, key, x0 + c * (w + gap), y0 - r * (h + gap), w, h * 2 + gap, new Color(0.2f, 0.5f, 0.7f, 0.95f));
                    continue;
                }
                var isOp = "÷×−+√%^".Contains(key) || key == "(" || key == ")";
                var isClear = key == "C" || key == "⌫";
                Key(canvas, font, key, x0 + c * (w + gap), y0 - r * (h + gap), w, h,
                    isClear ? new Color(0.45f, 0.25f, 0.25f, 0.95f) : isOp ? new Color(0.22f, 0.36f, 0.5f, 0.95f) : new Color(0.16f, 0.26f, 0.36f, 0.95f));
            }
            Render();
        }

        private void Key(Transform canvas, Font font, string label, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("Key " + label, typeof(Image), typeof(Button));
            go.transform.SetParent(canvas, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Image>().color = color;
            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(0.35f, 0.6f, 0.8f);
            colors.pressedColor = new Color(0.6f, 0.9f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => Press(label));
            var tgo = new GameObject("Text", typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>();
            t.font = font; t.fontSize = 26; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white; t.text = label == "⌫" ? "<" : label;
        }

        public void Press(string key)
        {
            switch (key)
            {
                case "C": _expr = ""; _justEvaluated = false; break;
                case "⌫": if (_expr.Length > 0) _expr = _expr.Substring(0, _expr.Length - 1); break;
                case "=": Evaluate(_expr); return;
                default:
                    // A digit after a result starts fresh; an operator continues from it.
                    if (_justEvaluated && (char.IsDigit(key[0]) || key == "." || key == "("))
                        _expr = "";
                    _justEvaluated = false;
                    _expr += key;
                    break;
            }
            Render();
        }

        /// <summary>Evaluate and record; the string the tool returns to Jarvis.</summary>
        public string Evaluate(string expression)
        {
            var shown = expression.Trim();
            if (shown.Length == 0) return "nothing to calculate";
            string line;
            try
            {
                var v = Calc.Evaluate(shown);
                var result = Calc.Format(v);
                line = $"{shown} = {result}";
                _expr = result;
                _justEvaluated = true;
            }
            catch (Exception e)
            {
                line = $"{shown} → {e.Message}";
                _justEvaluated = false;
            }
            _history.Add(line);
            if (_history.Count > 8) _history.RemoveAt(0);
            Render();
            return line;
        }

        private void Render()
        {
            if (_display) _display.text = _expr.Length == 0 ? "0" : _expr;
            if (_tape) _tape.text = string.Join("\n", _history);
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Text Label(Transform parent, Font font, int size, FontStyle style, float x, float y, float w, float h, Color color)
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
    }
}
